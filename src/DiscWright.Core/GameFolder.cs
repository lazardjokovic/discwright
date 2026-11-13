namespace DiscWright.Core;

/// <summary>
/// What a folder turned out to be.
/// </summary>
/// <param name="Path">The folder it was read from.</param>
/// <param name="Kind">Which of the three shapes it is.</param>
/// <param name="Name">
/// What to call it on the menu and the cover. Taken from the installer's own
/// version information where there is one, because "The Witcher: Enhanced
/// Edition" is what somebody expects to see and
/// "setup_the_witcher_enhanced_edition_directors_cut_1.5" is not.
/// </param>
/// <param name="MatchName">
/// The name the disc's menu hands to its "is this game installed" lookup.
/// </param>
/// <param name="Installer">
/// The GOG installer, for a <see cref="GameKind.GogInstaller"/>. Null otherwise.
/// </param>
/// <param name="Files">
/// Exactly what goes on the disc. For a GOG download that is the installer and
/// its own numbered parts and nothing else in the folder. For a folder of game
/// files it is everything, subfolders and all, because that shape is the game.
/// </param>
/// <param name="LauncherCandidates">
/// For a folder of game files, the executables that might start it, largest
/// first. <strong>The recogniser never picks one.</strong> Which executable
/// starts a game is a question for the person, and guessing it wrong produces a
/// disc whose only button does nothing.
/// </param>
/// <param name="Warning">
/// Something true and worth saying that is not a refusal. An unfinished
/// download is the one that matters: it builds a clean looking ISO that only
/// fails when somebody runs the installer off the burned disc, which is the
/// worst possible place to find out.
/// </param>
/// <param name="Own">
/// Its own manual and extras, chosen rather than found, or none. Last and
/// optional, the way the disc's artwork came, so every place a game is made
/// did not have to change for it.
/// </param>
public sealed record GameFolder(
    string Path,
    GameKind Kind,
    string Name,
    string MatchName,
    ListedFile? Installer,
    IReadOnlyList<ListedFile> Files,
    IReadOnlyList<ListedFile> LauncherCandidates,
    string? Warning,
    IReadOnlyList<GameExtra>? Own = null)
{
    /// <summary>Its own manual and extras, or none.</summary>
    public IReadOnlyList<GameExtra> OwnOrNone => Own ?? [];

    /// <summary>Everything that will go on the disc, summed.</summary>
    public long Bytes => Files.Sum(file => file.Bytes);

    /// <summary>
    /// The same game with one of its files taken off, by the path the file
    /// has inside the folder.
    /// </summary>
    /// <remarks>
    /// The installer is deliberately not treated as special. Leaving it off is
    /// allowed, because this program refuses by explaining rather than by
    /// greying things out, and the window says out loud that a disc without it
    /// will not install the game.
    /// </remarks>
    /// <remarks>
    /// One of the game's own manual or extras comes off the same way, by its
    /// full path, which no file inside the folder can carry.
    /// </remarks>
    public GameFolder Without(string relativePath)
    {
        GameExtra[] own =
        [
            .. OwnOrNone.Where(each => !string.Equals(each.Path, relativePath, StringComparison.OrdinalIgnoreCase))
        ];

        return this with
        {
            Files =
            [
                .. Files.Where(file =>
                    !string.Equals(file.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
            ],
            Own = own.Length > 0 ? own : null
        };
    }

    /// <summary>
    /// Whether a file is among the game's files, by the path it has inside
    /// the folder, the same way <see cref="Without"/> finds one to take off.
    /// </summary>
    public bool Holds(string relativePath) =>
        Files.Any(file => string.Equals(file.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The largest single file, which is what the ISO9660 ceiling is measured
    /// against and what decides whether one file is too big for a whole disc.
    /// </summary>
    public long LargestFile => Files.Count == 0 ? 0 : Files.Max(file => file.Bytes);
}

/// <summary>The three shapes a folder of a game can take.</summary>
public enum GameKind
{
    /// <summary>A GOG download: one <c>setup_*.exe</c> and its numbered parts.</summary>
    GogInstaller,

    /// <summary>
    /// Game files with at least one executable among them. Somebody still has to
    /// say which executable starts it.
    /// </summary>
    FilesWithLauncher,

    /// <summary>
    /// Game files with nothing to run. Not a failure: the disc's menu offers the
    /// folder instead, and the disc is still a disc.
    /// </summary>
    FilesOnly
}
