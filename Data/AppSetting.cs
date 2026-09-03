using System.ComponentModel.DataAnnotations;

namespace Simplz.Babytracker.Data;

/// <summary>
/// A handful of things that have to outlive the container but are not events: the passwords
/// once they have been changed from the ones in compose.yaml, and the stamp that decides
/// whether a sign-in cookie is still any good.
/// </summary>
public class AppSetting
{
    [MaxLength(64)]
    public string Key { get; set; } = "";

    [MaxLength(400)]
    public string Value { get; set; } = "";
}
