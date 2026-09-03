using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;

namespace Simplz.Babytracker.Services;

/// <summary>The two things a visitor can be. There are no user accounts, only these two passwords.</summary>
public static class Roles
{
    public const string Parent = "Parent";
    public const string Doctor = "Doctor";
}

/// <summary>
/// The passwords the container was started with, from the <c>Auth</c> configuration section.
/// These are the passwords until somebody changes one in the app, after which the stored one
/// wins — see <see cref="Credentials"/>, which is what decides who a password lets in.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string ParentPassword { get; set; } = "parent";
    public string DoctorPassword { get; set; } = "doctor";

    /// <summary>Compared in fixed time so the answer does not leak the password one character at a time.</summary>
    public static bool MatchesPlain(string given, string expected) =>
        !string.IsNullOrEmpty(expected)
        && CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(given),
            Encoding.UTF8.GetBytes(expected));
}

/// <summary>Shared by the pages to decide whether to render anything that writes.</summary>
public static class Access
{
    public static async Task<bool> CanEditAsync(Task<AuthenticationState>? state) =>
        state is not null && (await state).User.IsInRole(Roles.Parent);
}
