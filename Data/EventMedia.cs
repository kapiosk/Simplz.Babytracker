using System.ComponentModel.DataAnnotations;

namespace Simplz.Babytracker.Data;

/// <summary>
/// A photo or a clip attached to an entry — a stool worth showing a doctor, mostly.
///
/// The file itself lives on the data volume next to the database rather than inside it: a video
/// has no business in a SQLite row, and keeping them as files means a backup is a copy of one
/// folder. This is the row that says which entry it belongs to and what it is.
/// </summary>
public class EventMedia
{
    public int Id { get; set; }

    public int BabyEventId { get; set; }

    /// <summary>The name on disk — a generated one, so nothing a phone sends can pick the path.</summary>
    [MaxLength(80)]
    public string FileName { get; set; } = "";

    /// <summary>What the browser called it, kept only to show the person who added it.</summary>
    [MaxLength(200)]
    public string? OriginalName { get; set; }

    [MaxLength(100)]
    public string ContentType { get; set; } = "";

    public long Bytes { get; set; }

    public bool IsVideo { get; set; }

    public DateTime AddedUtc { get; set; }
}
