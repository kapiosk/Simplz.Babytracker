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

    /// <summary>
    /// The day they were born, if anybody has said. Everything about age is optional and
    /// nothing in the app needs it — without one, the age beside the date simply does not
    /// appear and the milestones are still there to record against.
    /// </summary>
    public DateOnly? BornOn { get; set; }
}
