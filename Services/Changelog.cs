namespace Simplz.Babytracker.Services;

/// <summary>
/// What has changed, newest first, written for whoever is holding the phone rather than for
/// whoever wrote it. Kept in code on purpose: it ships with the build, so the running container
/// can only ever describe itself, and there is no file to forget to copy onto the volume.
///
/// Add a release at the top when deploying something worth mentioning. The version is what
/// decides whether a phone has seen the notice, so it has to change for the notice to appear.
/// </summary>
public static class Changelog
{
    /// <summary>When this container started — which is to say, when the update actually landed.</summary>
    public static readonly DateTime RunningSinceUtc = DateTime.UtcNow;

    public static Release Current => Releases[0];

    public static readonly IReadOnlyList<Release> Releases =
    [
        new("1.6", new DateOnly(2026, 9, 4),
        [
            "This notice. The app now says when it has been updated, and this page says what changed.",
            "Fewer interruptions coming back to the app after leaving it a while: the page rejoins or reloads itself instead of sitting there with an error."
        ]),

        new("1.5", new DateOnly(2026, 9, 4),
        [
            "Photos and video can be attached to an entry — a stool worth showing a doctor, mostly.",
            "They appear as thumbnails under the entry on Track and Report; tap one to open it full size.",
            "The doctor password can see them and cannot add or remove any."
        ]),

        new("1.4", new DateOnly(2026, 9, 4),
        [
            "Both passwords can be changed from the app, on Passwords, reached from the Babies screen.",
            "Changing a password does not sign anyone out — there is a checkbox for when that is the point of changing it."
        ]),

        new("1.3", new DateOnly(2026, 9, 3),
        [
            "More than one baby can be tracked, each with its own log, chart and report.",
            "Add one on the Babies screen, and a row of names appears under the header to switch between them.",
            "Everything logged before this belongs to the first baby."
        ]),

        new("1.2", new DateOnly(2026, 9, 2),
        [
            "A Reload button in the header, for when the page stops responding.",
            "The app notices when it has lost the server and rejoins, or reloads itself, without being asked."
        ]),

        new("1.1", new DateOnly(2026, 9, 2),
        [
            "The tracker is behind a password, with a second read-only one for a doctor."
        ]),

        new("1.0", new DateOnly(2026, 9, 1),
        [
            "Track breast feeds, bottles, nappies and spit-ups.",
            "The paper-chart view and the report.",
            "Entry times can be corrected after the fact."
        ])
    ];

    public sealed record Release(string Version, DateOnly Released, IReadOnlyList<string> Changes);
}
