namespace DiscWright.Core.Recognisers;

/// <summary>
/// A folder that is not a GOG download: an unpacked zip, an itch.io download, a
/// portable game, anything at all.
/// </summary>
/// <remarks>
/// <para>
/// Everything in the folder goes on the disc, subfolders and all, which is the
/// difference from a GOG download. There the disc carries one installer and its
/// numbered parts; here it carries whatever shape the game already has.
/// </para>
/// <para>
/// This one claims any folder with files in it, so it goes last in the chain.
/// It answers <see cref="GameKind.FilesWithLauncher"/> when something in it
/// could be started and <see cref="GameKind.FilesOnly"/> when nothing can, and
/// neither is a failure.
/// </para>
/// </remarks>
public sealed class GameFilesRecogniser : IGameFolderRecogniser
{
    /// <summary>
    /// Executables that are never the game, so they are kept out of the list
    /// somebody has to choose from.
    /// </summary>
    /// <remarks>
    /// v1 showed every executable ordered by size and let the person sort it
    /// out. These five are worth removing because they are never the answer and
    /// they crowd out the one that is: a redistributable is often the largest
    /// executable in a game folder, which puts it at the top of a list ordered
    /// by size.
    /// </remarks>
    private static readonly string[] NeverTheGame =
    [
        "unins",        // unins000.exe and its numbered friends
        "vcredist",
        "dxsetup",
        "dotnetfx",
        "oalinst"       // OpenAL, shipped with a great many older games
    ];

    /// <summary>
    /// How many candidates to offer. A game folder can hold hundreds of tools
    /// and nobody picks from a list that long.
    /// </summary>
    private const int MostToOffer = 25;

    public string Describes => "a folder of game files";

    public Outcome<GameFolder>? Recognise(FolderListing folder)
    {
        if (folder.Files.Count == 0)
        {
            return Refusals.ThatFolderIsEmpty(folder);
        }

        List<ListedFile> candidates =
        [
            .. folder.Files
                .Where(file => file.Extension == ".exe")
                .Where(IsPlausiblyTheGame)
                // Largest first: a game's own executable is nearly always bigger
                // than the crash handler and the config tool beside it.
                .OrderByDescending(file => file.Bytes)
                .Take(MostToOffer)
        ];

        string name = NameOf(folder);

        return new GameFolder(
            folder.Path,
            candidates.Count > 0 ? GameKind.FilesWithLauncher : GameKind.FilesOnly,
            name,
            name,
            null,
            folder.Files,
            candidates,
            null);
    }

    private static bool IsPlausiblyTheGame(ListedFile file) =>
        !NeverTheGame.Any(never => file.Name.StartsWith(never, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The folder's own name, because there is nothing else to go on. No
    /// installer to ask, and the executables are named after the engine as
    /// often as after the game.
    /// </summary>
    private static string NameOf(FolderListing folder) =>
        new System.IO.DirectoryInfo(folder.Path.TrimEnd('\\', '/')).Name;
}
