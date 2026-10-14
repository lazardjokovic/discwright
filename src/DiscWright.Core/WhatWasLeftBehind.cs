namespace DiscWright.Core;

/// <summary>
/// The files in a folder that the game did not take.
/// </summary>
/// <remarks>
/// <para>
/// A GOG download holds the installer and its parts, and often a patch, a
/// README, a manual or a soundtrack beside them. The recogniser takes the
/// installer and its parts, because that is what the game is, and <strong>said
/// nothing at all about the rest</strong>: somebody pointed at a folder with
/// four files in it and got a disc with two, with no sign that two had gone.
/// </para>
/// <para>
/// Losing a file quietly is the worst way to lose it. So this is what the
/// window asks about.
/// </para>
/// <para>
/// A folder of game files leaves nothing behind, because that recogniser takes
/// everything. This is a GOG download's problem.
/// </para>
/// </remarks>
public static class WhatWasLeftBehind
{
    /// <summary>
    /// Everything in the folder that is not part of the game, in the order the
    /// folder listed it.
    /// </summary>
    public static IReadOnlyList<LeftBehind> In(FolderListing folder, GameFolder game)
    {
        HashSet<string> taken = new(
            game.Files.Select(file => file.RelativePath),
            StringComparer.OrdinalIgnoreCase);

        return
        [
            .. folder.Files
                .Where(file => !taken.Contains(file.RelativePath))
                .Select(file => new LeftBehind(file, KindOf(file)))
        ];
    }

    /// <summary>
    /// The game with some of what was left behind put back into it.
    /// </summary>
    /// <remarks>
    /// They become part of the game's own files rather than anything new, so
    /// they land in its folder on the disc, get planned with it, get cut with
    /// it and appear in the checksum list like everything else. A README that
    /// came from beside the installer belongs beside the installer.
    /// </remarks>
    public static GameFolder Taking(GameFolder game, IEnumerable<LeftBehind> chosen) =>
        game with { Files = [.. game.Files, .. chosen.Select(each => each.File)] };

    /// <summary>
    /// What a left behind file looks like it is.
    /// </summary>
    /// <remarks>
    /// A guess, and only a guess: it decides what the window suggests, never
    /// what happens. An executable beside a game is nearly always something
    /// that installs onto the game, which is what an add-on is, and everything
    /// else is something to read or look at.
    /// </remarks>
    private static LeftBehindKind KindOf(ListedFile file) =>
        file.RelativePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? LeftBehindKind.LooksLikeAnAddOn
            : LeftBehindKind.LooksLikeSomethingToKeep;
}

/// <summary>One file the game did not take.</summary>
public sealed record LeftBehind(ListedFile File, LeftBehindKind Kind)
{
    /// <summary>What to call it on the window.</summary>
    public string Name => File.Name;

    /// <summary>How big it is.</summary>
    public long Bytes => File.Bytes;

    /// <summary>
    /// What it would be called as an add-on, which is not its file name.
    /// </summary>
    public string AsAnAddOn => AddOn.NameFor(File.Name);
}

/// <summary>What a left behind file looks like it is.</summary>
public enum LeftBehindKind
{
    /// <summary>
    /// Something that installs onto the game: a patch, a DLC, a mod. It belongs
    /// in the game's Add-ons folder, dead until the game is installed.
    /// </summary>
    LooksLikeAnAddOn,

    /// <summary>
    /// Something to read or look at: a README, a manual, cover art. It belongs
    /// beside the game, as part of it.
    /// </summary>
    LooksLikeSomethingToKeep
}
