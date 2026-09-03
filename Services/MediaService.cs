using Microsoft.EntityFrameworkCore;
using Simplz.Babytracker.Data;

namespace Simplz.Babytracker.Services;

/// <summary>
/// The photos and clips attached to entries. Files go in a folder next to the database, named by
/// a generated id rather than anything the phone sent, so nothing arriving from outside gets to
/// choose a path. The rows say which entry each belongs to.
/// </summary>
public sealed class MediaService(
    IDbContextFactory<AppDbContext> factory,
    string root,
    ILogger<MediaService> log)
{
    /// <summary>Generous for a phone photo, and nowhere near enough to be a way of filling the disk.</summary>
    public const long MaxImageBytes = 25L * 1024 * 1024;

    /// <summary>A couple of minutes of phone video. Stored as it arrives — a Pi is no place to transcode.</summary>
    public const long MaxVideoBytes = 200L * 1024 * 1024;

    public event Action<int>? Changed;

    public async Task<List<EventMedia>> ForEventAsync(int eventId, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Media
            .Where(m => m.BabyEventId == eventId)
            .OrderBy(m => m.Id)
            .ToListAsync(ct);
    }

    /// <summary>Everything attached to a page of entries, in one query rather than one each.</summary>
    public async Task<Dictionary<int, List<EventMedia>>> ForEventsAsync(
        IReadOnlyCollection<int> eventIds, CancellationToken ct = default)
    {
        if (eventIds.Count == 0)
        {
            return [];
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        var rows = await db.Media
            .Where(m => eventIds.Contains(m.BabyEventId))
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        return rows.GroupBy(m => m.BabyEventId).ToDictionary(g => g.Key, g => g.ToList());
    }

    public async Task<Dictionary<int, int>> CountsForAsync(IEnumerable<int> eventIds, CancellationToken ct = default)
    {
        var ids = eventIds.ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Media
            .Where(m => ids.Contains(m.BabyEventId))
            .GroupBy(m => m.BabyEventId)
            .Select(g => new { EventId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EventId, x => x.Count, ct);
    }

    public async Task<EventMedia?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Media.FirstOrDefaultAsync(m => m.Id == id, ct);
    }

    public string PathFor(EventMedia media) => Path.Combine(root, media.FileName);

    /// <summary>
    /// Streams an upload straight to disk — it is never held in memory, because a two minute
    /// video would be a very expensive thing for a Pi to hold on behalf of every phone at once.
    /// </summary>
    public async Task<EventMedia?> AddAsync(
        int eventId, Stream content, string contentType, string? originalName, CancellationToken ct = default)
    {
        var isVideo = contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
        var isImage = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

        if (!isVideo && !isImage)
        {
            return null;
        }

        Directory.CreateDirectory(root);

        var fileName = $"{Guid.NewGuid():N}{Extension(contentType, originalName)}";
        var path = Path.Combine(root, fileName);
        var limit = isVideo ? MaxVideoBytes : MaxImageBytes;

        long written;
        try
        {
            await using var file = File.Create(path);
            written = await CopyWithLimitAsync(content, file, limit, ct);
        }
        catch
        {
            Delete(path);
            throw;
        }

        if (written < 0)
        {
            // Over the limit. The partial file is of no use to anybody.
            Delete(path);
            return null;
        }

        var media = new EventMedia
        {
            BabyEventId = eventId,
            FileName = fileName,
            OriginalName = Trim(originalName),
            ContentType = contentType,
            Bytes = written,
            IsVideo = isVideo,
            AddedUtc = DateTime.UtcNow
        };

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Media.Add(media);
        await db.SaveChangesAsync(ct);

        Notify(eventId);
        return media;
    }

    /// <summary>Copies until the limit is passed, and reports that by returning -1.</summary>
    private static async Task<long> CopyWithLimitAsync(Stream from, Stream to, long limit, CancellationToken ct)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;

        while ((read = await from.ReadAsync(buffer, ct)) > 0)
        {
            total += read;
            if (total > limit)
            {
                return -1;
            }

            await to.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return total;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var media = await db.Media.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (media is null)
        {
            return;
        }

        Delete(PathFor(media));
        db.Media.Remove(media);
        await db.SaveChangesAsync(ct);
        Notify(media.BabyEventId);
    }

    /// <summary>
    /// The files belonging to an entry about to be deleted. The rows go on their own through the
    /// foreign key; without this the files would stay on the disk forever with nothing pointing
    /// at them.
    /// </summary>
    public async Task DeleteForEventAsync(int eventId, CancellationToken ct = default)
    {
        foreach (var media in await ForEventAsync(eventId, ct))
        {
            Delete(PathFor(media));
        }
    }

    private void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            // Losing the file is not worth failing the delete the person actually asked for.
            log.LogError(ex, "Could not delete the media file at {Path}.", path);
        }
    }

    private void Notify(int eventId)
    {
        if (Changed is not { } subscribers)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((Action<int>)subscriber)(eventId);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "A page failed to handle a change to an entry's attachments.");
            }
        }
    }

    private static string? Trim(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null
            : name.Length <= 200 ? name.Trim()
            : name.Trim()[..200];

    /// <summary>
    /// Taken from the content type where it is recognised, and otherwise from the name the phone
    /// gave — but only ever as a short run of letters and digits, since this ends up in a path.
    /// </summary>
    private static string Extension(string contentType, string? originalName) =>
        contentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/heic" => ".heic",
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "video/webm" => ".webm",
            _ => Safe(Path.GetExtension(originalName ?? ""))
        };

    private static string Safe(string extension) =>
        extension.Length is > 1 and <= 6 && extension[1..].All(char.IsLetterOrDigit)
            ? extension.ToLowerInvariant()
            : ".bin";
}
