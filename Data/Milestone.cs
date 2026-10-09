using System.ComponentModel.DataAnnotations;

namespace Simplz.Babytracker.Data;

/// <summary>
/// A milestone this baby has reached, and when.
///
/// Only the reached ones are rows. The guide itself is a list in code — see
/// <see cref="Services.Milestones"/> — because it is content that ships with the build rather
/// than anything a user owns, and a table of it would want seeding, versioning and migrating
/// every time a word changed.
/// </summary>
public class Milestone
{
    public int Id { get; set; }

    public int BabyId { get; set; }

    /// <summary>
    /// Which one from the guide, or null when somebody wrote their own. A key rather than a
    /// copy of the text, so improving the wording later does not leave old rows saying the old
    /// thing — and so an entry survives the guide being reworded at all.
    /// </summary>
    [MaxLength(60)]
    public string? Key { get; set; }

    /// <summary>What it was, for one somebody wrote themselves. Null for one from the guide.</summary>
    [MaxLength(120)]
    public string? Title { get; set; }

    /// <summary>
    /// The day it happened, not the day it was typed in. A first smile noticed on Tuesday and
    /// written down on Friday belongs to Tuesday, and the age shown against it follows this.
    /// </summary>
    public DateOnly ReachedOn { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
