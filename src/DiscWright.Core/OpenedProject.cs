namespace DiscWright.Core;

/// <summary>
/// What a project file says somebody chose.
/// </summary>
/// <remarks>
/// <para>
/// Faithful to the file and nothing more. It holds paths, not files: nothing
/// here has been looked at on disk, and a folder named in it may well have
/// moved or gone since the disc was made.
/// </para>
/// <para>
/// Turning this into a disc means reading those folders again, which is also
/// how a project repairs itself: a game renamed under older code gets its name
/// back from the folder it is still in.
/// </para>
/// </remarks>
/// <param name="Version">The schema it was written at.</param>
/// <param name="MadeBy">Which DiscWright wrote it, when it says.</param>
/// <param name="MusicPath">The tune the menu plays, when one was chosen.</param>
public sealed record OpenedProject(
    int Version,
    string? MadeBy,
    string Label,
    IReadOnlyList<ProjectEntry> Entries,
    DiscOptions Options,
    string MediaKey,
    string? OutputFolder,
    string? ManualPath,
    string? ExtrasFolderPath,
    IReadOnlyList<string> LooseExtras,
    string? MusicPath,
    ProjectArtwork Artwork)
{
    /// <summary>The games, without the things that install onto them.</summary>
    public IEnumerable<ProjectEntry> Games => Entries.Where(entry => !entry.IsAnAddOn);

    /// <summary>What was attached to one game, by where that game sits.</summary>
    public IEnumerable<ProjectEntry> AddOnsFor(int game) =>
        Entries.Where(entry => entry.IsAnAddOn && entry.BelongsTo == game);

    /// <summary>
    /// The size of disc it was built for, or none when it was left automatic.
    /// </summary>
    public Medium? Medium => MediaKey.Length == 0 ? null : Media.ByKey(MediaKey);
}

/// <summary>One thing that was on the disc, as the file describes it.</summary>
/// <param name="Folder">Where it was read from.</param>
/// <param name="Name">What it was called.</param>
/// <param name="MatchName">
/// The name it is registered under, for finding it on a machine later. Older
/// files have none, and then the name found by reading the folder stands.
/// </param>
/// <param name="Installer">The executable that installs it, when there is one.</param>
/// <param name="IsAnAddOn">Whether it installs onto a game rather than being one.</param>
/// <param name="BelongsTo">Which game it belongs to, by position, or -1.</param>
/// <param name="WasAGogDownload">
/// Whether it was recognised as a GOG download rather than a folder of files.
/// </param>
/// <param name="Own">
/// The game's own manual and extras, as the file names them. None in a file
/// older than 2.0.4.
/// </param>
public sealed record ProjectEntry(
    string Folder,
    string? Name,
    string? MatchName,
    string? Installer,
    bool IsAnAddOn,
    int BelongsTo,
    bool WasAGogDownload,
    IReadOnlyList<GameExtra>? Own = null)
{
    /// <summary>Its own manual and extras, or none.</summary>
    public IReadOnlyList<GameExtra> OwnOrNone => Own ?? [];
}

/// <summary>The pictures a project pointed at.</summary>
public sealed record ProjectArtwork(
    string? IconPath,
    bool IconIsAlreadyAnIcon,
    string? BackgroundPath,
    bool BackgroundAsItIs,
    string? CoverPath,
    string? DiscFacePath,
    string? BackgroundColour = null);
