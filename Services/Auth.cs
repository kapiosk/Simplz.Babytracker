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
/// The whole authentication model: one shared password per role, read from the <c>Auth</c>
/// configuration section. The parent password logs and edits entries, the doctor password
/// gives the same views without any way to change anything.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string ParentPassword { get; set; } = "parent";
    public string DoctorPassword { get; set; } = "doctor";

    /// <summary>The role a password unlocks, or <c>null</c> when it matches neither.</summary>
    public string? RoleFor(string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return null;
        }

        // Parent is checked first, so setting both passwords to the same value keeps full access.
        if (Matches(password, ParentPassword))
        {
            return Roles.Parent;
        }

        return Matches(password, DoctorPassword) ? Roles.Doctor : null;
    }

    /// <summary>Compared in fixed time so the answer does not leak the password one character at a time.</summary>
    private static bool Matches(string given, string expected) =>
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
