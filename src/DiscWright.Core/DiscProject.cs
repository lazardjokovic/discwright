namespace DiscWright.Core;

/// <summary>
/// What the disc is meant to be. The whole intent, and the only thing saved.
/// </summary>
/// <remarks>
/// Deliberately small for now and it will grow: the artwork, the manual, the
/// extras and the music all belong here. What is here is what the staging
/// pipeline needs in order to put files on a disc, and nothing has been added
/// in anticipation of a step that does not exist.
/// </remarks>
/// <param name="Label">
/// What This PC shows, and what AutoRun reads. Short, because AutoRun reads it
/// in the system codepage and a version in there costs the room the game's name
/// needs.
/// </param>
/// <param name="Games">In the order they were added, which is the order they appear.</param>
/// <param name="Options">Everything that has an answer already.</param>
/// <param name="Artwork">
/// The pictures, or null for a disc that carries none. Last and optional so
/// that adding it did not have to touch every place a project is made, which is
/// the first time that has mattered and will not be the last.
/// </param>
public sealed record DiscProject(
    string Label,
    IReadOnlyList<GameFolder> Games,
    DiscOptions Options,
    DiscArtwork? Artwork = null);

/// <summary>
/// The things somebody can change but mostly does not.
/// </summary>
/// <remarks>
/// One record with defaults, so adding an option touches three named places:
/// here, its binding in the window, and its test. Not eleven.
/// </remarks>
/// <param name="Menu">A menu that runs itself when the disc goes in.</param>
/// <param name="Checksums">
/// A list the disc carries so somebody can prove it is still good in ten years
/// without this program. On by default, because that is the promise.
/// </param>
/// <param name="ArtworkOnDisc">
/// The cover and disc face, carried on the disc itself, so whoever holds it can
/// print them without this app and without a project file.
/// </param>
/// <param name="LegacyFileSystems">
/// ISO9660 and Joliet beside UDF, for machines older than Windows Vista. Dead
/// when any one file is over <see cref="FileSystems.Iso9660MaxFile"/>.
/// </param>
/// <param name="CutFiles">
/// Whether a single file bigger than a whole disc may be cut across several.
/// </param>
public sealed record DiscOptions(
    bool Menu = true,
    bool Checksums = true,
    bool ArtworkOnDisc = true,
    bool LegacyFileSystems = false,
    bool CutFiles = false);
