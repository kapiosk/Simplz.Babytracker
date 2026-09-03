using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Simplz.Babytracker.Components;
using Simplz.Babytracker.Data;
using Simplz.Babytracker.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        // A phone that has been locked for a few minutes should get its own circuit back
        // rather than be told to reload, so keep disconnected circuits well past the
        // three minute default. They are cheap: this app has a handful of users at most.
        options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(15);
    })
    .AddHubOptions(options =>
    {
        // Ping more often than the 15 second default, so a socket that died with the
        // screen is noticed sooner. wwwroot/circuit-watchdog.js is what actually catches
        // it, but a livelier connection means fewer of those to catch.
        options.KeepAliveInterval = TimeSpan.FromSeconds(10);
    });

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=App_Data/babytracker.db";

// Make sure the folder the SQLite file lives in exists (it is a mounted volume in Docker).
var dataSource = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString).DataSource;
var dataDirectory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
if (!string.IsNullOrEmpty(dataDirectory))
{
    Directory.CreateDirectory(dataDirectory);
}

builder.Services.AddDbContextFactory<AppDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddSingleton<EventService>();
builder.Services.AddSingleton<BabyService>();
builder.Services.AddSingleton<Credentials>();

// Attachments live beside the database, so one folder is the whole backup.
builder.Services.AddSingleton(sp => new MediaService(
    sp.GetRequiredService<IDbContextFactory<AppDbContext>>(),
    Path.Combine(dataDirectory ?? ".", "media"),
    sp.GetRequiredService<ILogger<MediaService>>()));

// A video is far past the 128MB a multipart form will take by default.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MediaService.MaxVideoBytes + (8 * 1024 * 1024);
});

// Keep the cookie-signing keys next to the database, so a redeploy does not sign everyone out.
if (!string.IsNullOrEmpty(dataDirectory))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(Directory.CreateDirectory(Path.Combine(dataDirectory, "keys")));
}

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = ".Babytracker.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.ReturnUrlParameter = "returnUrl";

        // A phone that has the app on its home screen should stay signed in for months.
        options.ExpireTimeSpan = TimeSpan.FromDays(180);
        options.SlidingExpiration = true;

        // Changing a password leaves the phones already signed in alone, which is what you
        // want at 3am. "Sign out every device" instead moves the stamp, and every cookie
        // issued under the old one — including this one — stops being worth anything.
        options.Events.OnValidatePrincipal = async context =>
        {
            var credentials = context.HttpContext.RequestServices.GetRequiredService<Credentials>();
            var current = await credentials.StampAsync();

            if (current is null)
            {
                // Nobody has ever asked to be signed out everywhere; nothing to check against.
                return;
            }

            if (context.Principal?.FindFirst(Credentials.StampClaim)?.Value != current)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();

// Running behind a reverse proxy (nginx/Traefik/Caddy) that terminates TLS.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.Migrate();
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/healthz", () => Results.Ok("ok"));

// Where wwwroot/circuit-watchdog.js sends a failure that happened in the browser. An unhandled
// error on the server is already logged; one in the page never reaches the Pi at all, which is
// why "the app crashed" can leave no trace behind. Signed-in callers only, and small: this puts
// text into the log, so it should not be something anyone passing by can fill.
app.MapPost("/clientlog", async (HttpContext http, ILogger<Program> log) =>
{
    if (http.User.Identity?.IsAuthenticated != true
        || !http.Request.HasJsonContentType())
    {
        return Results.Unauthorized();
    }

    var report = await http.Request.ReadFromJsonAsync<ClientLogEntry>();
    if (report is null)
    {
        return Results.BadRequest();
    }

    log.LogError(
        "Client-side failure: {Kind} on {Page} — {Detail} (page {AgeMs}ms old, off screen for {AwayMs}ms beforehand, circuit seen: {HadCircuit}, Blazor reported down: {BlazorSaysDown})",
        report.Kind, report.Url, report.Detail, report.AgeMs, report.AwayMs, report.HadCircuit, report.BlazorSaysDown);

    return Results.NoContent();
});

// A minimal endpoint is only checked automatically when it binds a form, so validate by hand —
// otherwise any other site could sign the phone out with a hidden form post.
app.MapPost("/logout", async (HttpContext http, IAntiforgery antiforgery) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(http);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest();
    }

    await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/login");
});

// Switching baby re-issues the sign-in cookie with a different baby claim. A plain form post
// rather than anything interactive, so it still works when the circuit does not — and the
// redirect lands back on whichever page asked, already showing the other baby.
app.MapPost("/baby", async (HttpContext http, IAntiforgery antiforgery, BabyService babies, Credentials credentials) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(http);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest();
    }

    if (http.User.Identity?.IsAuthenticated != true)
    {
        return Results.LocalRedirect("/login");
    }

    var form = await http.Request.ReadFormAsync();
    var role = http.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

    if (role is null
        || !int.TryParse(form["babyId"], out var babyId)
        || await babies.GetAsync(babyId) is null)
    {
        return Results.BadRequest();
    }

    await http.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        await credentials.PrincipalAsync(role, babyId, CookieAuthenticationDefaults.AuthenticationScheme),
        new AuthenticationProperties { IsPersistent = true });

    var back = form["returnUrl"].ToString();
    return Results.LocalRedirect(back is ['/'] or ['/', not ('/' or '\\'), ..] ? back : "/");
});

// Uploading an attachment. A real multipart post from the browser rather than anything going
// over the circuit: Blazor would carry the bytes through SignalR in small pieces, which is slow
// for a photo and hopeless for a video, and it is the circuit that is least reliable here.
app.MapPost("/media", async (HttpContext http, IAntiforgery antiforgery, EventService events, MediaService media) =>
{
    // Raised before anything reads the body, validating the token included — the default cap
    // is 30MB and would refuse a video before the request was ever looked at.
    if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } size)
    {
        size.MaxRequestBodySize = MediaService.MaxVideoBytes + (8 * 1024 * 1024);
    }

    // Checked by hand: an endpoint is only validated automatically when it binds a form as a
    // parameter, and this one reads it itself.
    try
    {
        await antiforgery.ValidateRequestAsync(http);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest();
    }

    if (!http.User.IsInRole(Roles.Parent))
    {
        return Results.Forbid();
    }

    var form = await http.Request.ReadFormAsync();

    if (!int.TryParse(form["eventId"], out var eventId) || await events.GetAsync(eventId) is null)
    {
        return Results.BadRequest();
    }

    var added = 0;
    var refused = 0;

    foreach (var file in form.Files)
    {
        await using var stream = file.OpenReadStream();
        var saved = await media.AddAsync(eventId, stream, file.ContentType ?? "", file.FileName);

        if (saved is null)
        {
            refused++;
        }
        else
        {
            added++;
        }
    }

    return Results.Ok(new { added, refused });
});

// Serving one back. Behind the password like everything else, and readable by the doctor role,
// which is rather the point of photographing a stool in the first place.
app.MapGet("/media/{id:int}", async (int id, MediaService media) =>
{
    if (await media.GetAsync(id) is not { } item)
    {
        return Results.NotFound();
    }

    var path = media.PathFor(item);
    if (!File.Exists(path))
    {
        return Results.NotFound();
    }

    // Ranges matter: without them a video will not seek, and some browsers will not play at all.
    return Results.File(File.OpenRead(path), item.ContentType, enableRangeProcessing: true);
}).RequireAuthorization();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>What wwwroot/circuit-watchdog.js posts to /clientlog when the page breaks.</summary>
internal sealed record ClientLogEntry(
    string? Kind,
    string? Detail,
    string? Url,
    long AwayMs,
    long AgeMs,
    bool HadCircuit,
    bool BlazorSaysDown);
