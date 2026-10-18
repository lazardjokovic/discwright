namespace DiscWright.Core.Staging;

/// <summary>
/// Puts the manual, the extras folder and the loose files on the one disc that
/// carries them.
/// </summary>
/// <remarks>
/// <strong>One disc of a set, not all of them.</strong> Copying a three
/// gigabyte texture pack onto five discs costs five times the room, and the
/// planner has already worked out which disc has the slack for it. For a three
/// megabyte manual that is arguably the wrong answer, and is still the right
/// default: the person who wants it on every disc can say so, and nobody wants
/// to discover they burned the same extras five times.
/// </remarks>
public sealed class CopyTheExtrasStep(IFileSystem files) : IBuildStep
{
    /// <summary>Where the manual and the extras folder land.</summary>
    public const string ExtrasFolder = "Extras";

    public string Says => "adding the extras";

    public bool AppliesTo(BuildContext context) =>
        context.Project.Extras?.IsAnythingOnOneDisc == true
        && context.Disc.Number == context.Layout.DiscCarryingTheExtras;

    public Task Run(BuildContext context, CancellationToken ct)
    {
        DiscExtras extras = context.Project.Extras!;

        if (extras.ManualPath is not null)
        {
            ct.ThrowIfCancellationRequested();

            context.Writer.MakeFolder(context.On(ExtrasFolder));

            // Keeps its own name. Somebody looking for the manual in ten years
            // is looking for whatever it was called.
            context.Writer.Copy(
                extras.ManualPath,
                context.On(System.IO.Path.Combine(ExtrasFolder, NameOf(extras.ManualPath))));
        }

        if (extras.ExtrasFolderPath is not null)
        {
            ct.ThrowIfCancellationRequested();

            // Its CONTENTS, not the folder itself. Somebody picking a folder
            // called "stuff for the disc" did not mean to put a folder called
            // "stuff for the disc" on it.
            CopyInto(context, extras.ExtrasFolderPath, ExtrasFolder, ct);
        }

        foreach (string item in extras.Loose)
        {
            ct.ThrowIfCancellationRequested();

            if (files.FolderExists(item))
            {
                // A folder keeps its name here, unlike the extras folder,
                // because this one was added as itself rather than as a
                // container of things.
                CopyInto(context, item, NameOf(item), ct);
            }
            else
            {
                context.Writer.Copy(item, context.On(NameOf(item)));
            }
        }

        return Task.CompletedTask;
    }

    private void CopyInto(BuildContext context, string from, string into, CancellationToken ct)
    {
        context.Writer.MakeFolder(context.On(into));

        foreach (ListedFile file in files.List(from).Files)
        {
            ct.ThrowIfCancellationRequested();

            string landing = context.On(System.IO.Path.Combine(into, file.RelativePath));
            string? folder = System.IO.Path.GetDirectoryName(landing);

            if (!string.IsNullOrEmpty(folder))
            {
                context.Writer.MakeFolder(folder);
            }

            context.Writer.Copy(System.IO.Path.Combine(from, file.RelativePath), landing);
        }
    }

    private static string NameOf(string path) =>
        System.IO.Path.GetFileName(path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
}

/// <summary>
/// Puts the music the menu plays on every disc.
/// </summary>
/// <remarks>
/// Its own step, and one that applies to every disc, because music is the one
/// piece of extra content that is not content: it belongs to the menu, and
/// every disc has a menu of its own to play it. That is also why it is counted
/// as overhead rather than as payload.
/// </remarks>
public sealed class CopyTheMusicStep : IBuildStep
{
    /// <summary>Where the music lands, beside the menu that plays it.</summary>
    public const string MenuFolder = "AUTORUN";

    public string Says => "adding the music";

    public bool AppliesTo(BuildContext context) =>
        context.Project.Extras?.MusicPath is not null && context.Project.Options.Menu;

    /// <summary>
    /// What the music is called once it is on the disc, or null when the disc
    /// has none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Renamed to "music", keeping its extension. The menu then names one file
    /// whatever somebody's download was called, and a name with a quote or a
    /// bracket in it never reaches the menu's script at all.
    /// </para>
    /// <para>
    /// Asked here by both the step that copies it and the menu that plays it,
    /// rather than spelled out twice. Spelled twice they agree until one of
    /// them changes, and then the menu asks the player for a file that is not
    /// there and the disc is silent with nothing to say why.
    /// </para>
    /// </remarks>
    public static string? NameOnTheDisc(DiscProject project) =>
        project.Extras?.MusicPath is string music && project.Options.Menu
            ? "music" + System.IO.Path.GetExtension(music).ToLowerInvariant()
            : null;

    public Task Run(BuildContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        string music = context.Project.Extras!.MusicPath!;

        context.Writer.MakeFolder(context.On(MenuFolder));

        context.Writer.Copy(
            music,
            context.On(MenuFolder + (char)92 + NameOnTheDisc(context.Project)!));

        return Task.CompletedTask;
    }
}
