using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

/// <summary>
/// The two passwords, once they can be changed from inside the app.
///
/// Until somebody changes one, the password is whatever <c>compose.yaml</c> says, compared as
/// it always was. A changed password is stored here instead — hashed, because a shared family
/// password is likely to be a password used elsewhere too, and the database sits on an SD card
/// that is easier to walk off with than a server. A stored password always wins over the
/// configured one, so changing it in the app is what takes effect.
/// </summary>
public sealed class Credentials(
    IDbContextFactory<AppDbContext> factory,
    IOptions<AuthOptions> configured)
{
    public const string ParentKey = "password.parent";
    public const string DoctorKey = "password.doctor";

    /// <summary>
    /// Bumped to turn every sign-in cookie ever issued into rubbish. Only set when somebody
    /// asks for it: changing a password does not otherwise disturb the phones already signed in.
    /// </summary>
    public const string StampKey = "security.stamp";

    /// <summary>The claim carrying the stamp the cookie was issued under.</summary>
    public const string StampClaim = "stamp";

    private const int Iterations = 210_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    // Read on every request that carries a cookie, so it does not go to the database each time.
    private Dictionary<string, string>? cache;
    private readonly SemaphoreSlim gate = new(1, 1);

    private async Task<Dictionary<string, string>> SettingsAsync(CancellationToken ct = default)
    {
        if (cache is { } ready)
        {
            return ready;
        }

        await gate.WaitAsync(ct);
        try
        {
            if (cache is { } justLoaded)
            {
                return justLoaded;
            }

            await using var db = await factory.CreateDbContextAsync(ct);
            cache = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value, ct);
            return cache;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);

        if (row is null)
        {
            db.Settings.Add(new AppSetting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await db.SaveChangesAsync(ct);
        cache = null;
    }

    /// <summary>The role a password unlocks, or <c>null</c> when it matches neither.</summary>
    public async Task<string?> RoleForAsync(string? password, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(password))
        {
            return null;
        }

        var settings = await SettingsAsync(ct);

        // Parent is checked first, so setting both to the same value keeps full access.
        return Matches(password, ParentKey, configured.Value.ParentPassword, settings)
            ? Roles.Parent
            : Matches(password, DoctorKey, configured.Value.DoctorPassword, settings)
                ? Roles.Doctor
                : null;
    }

    /// <summary>A stored password if there is one, otherwise whatever the container was given.</summary>
    private static bool Matches(
        string given, string key, string fromConfig, Dictionary<string, string> settings) =>
        settings.TryGetValue(key, out var stored)
            ? Verify(given, stored)
            : AuthOptions.MatchesPlain(given, fromConfig);

    public async Task<bool> IsChangedAsync(string key, CancellationToken ct = default) =>
        (await SettingsAsync(ct)).ContainsKey(key);

    public Task SetPasswordAsync(string key, string password, CancellationToken ct = default) =>
        SetAsync(key, Hash(password), ct);

    /// <summary>The stamp cookies are currently valid under, or null while none has been set.</summary>
    public async Task<string?> StampAsync(CancellationToken ct = default) =>
        (await SettingsAsync(ct)).GetValueOrDefault(StampKey);

    /// <summary>Invalidates every cookie already out there, this device's included.</summary>
    public Task SignOutEverywhereAsync(CancellationToken ct = default) =>
        SetAsync(StampKey, Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)), ct);

    /// <summary>
    /// Everything a signed-in device carries: its role, the baby it is looking at, and the
    /// stamp it was let in under. Built in one place so signing in and switching baby cannot
    /// drift apart and quietly drop one of them.
    /// </summary>
    public async Task<ClaimsPrincipal> PrincipalAsync(
        string role, int? babyId, string scheme, CancellationToken ct = default)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, role),
            new(ClaimTypes.Role, role)
        };

        if (babyId is { } id)
        {
            claims.Add(new Claim(BabyService.ClaimType, id.ToString()));
        }

        if (await StampAsync(ct) is { } stamp)
        {
            claims.Add(new Claim(StampClaim, stamp));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, scheme));
    }

    // ---------- hashing ----------

    private static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[0], out var iterations)
            || iterations <= 0)
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[1]);
            var expected = Convert.FromBase64String(parts[2]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
