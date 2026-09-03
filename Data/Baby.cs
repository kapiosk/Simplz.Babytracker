using System.ComponentModel.DataAnnotations;

namespace Simplz.Babytracker.Data;

/// <summary>
/// One baby being tracked. There is always at least one: the migration that introduced this
/// table created it and assigned every entry logged before then to it.
/// </summary>
public class Baby
{
    public int Id { get; set; }

    [Required, MaxLength(60)]
    public string Name { get; set; } = "";
}
