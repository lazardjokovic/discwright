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

    /// <summary>
    /// The sentence the dialog says once, above the rows, when patches are
    /// among what was left behind, or nothing when none are and there is
    /// nothing to explain.
    /// </summary>
    /// <remarks>
    /// The owner's ask on 2026-11-09: one place where a person who has
    /// never heard of patches gets the explanation. Worded here rather
    /// than in the dialog so a test can read it.
    /// </remarks>
    public static string? AboutThePatches(string? installerFileName, IEnumerable<LeftBehind> left)
    {
        if (!left.Any(each => APatchAgainstTheInstaller.IsAPatch(each.File.Name)))
        {
            return null;
        }

        string? installs = APatchAgainstTheInstaller.WhatTheInstallerInstalls(installerFileName);

        return installs is null
            ? "A patch takes an installed game from one version to the next. DiscWright cannot "
                + "tell which version this game's installer carries, so it suggests keeping every patch."
            : "A patch is GOG's small installer that takes the game from one version to the next. "
                + $"The installer going on this disc already installs {installs}.";
    }
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

    /// <summary>
    /// The line under the file's name in the dialog, judged against the
    /// disc's installer when the file is a patch the names can judge.
    /// </summary>
    public string ExplainedAgainst(string? installerFileName)
    {
        if (Kind != LeftBehindKind.LooksLikeAnAddOn)
        {
            return "Looks like something to read or look at";
        }

        return APatchAgainstTheInstaller.For(installerFileName, File.Name) switch
        {
            WhatAPatchAdds.NothingTheInstallerLacks =>
                $"Reaches {APatchAgainstTheInstaller.WhatItReaches(File.Name)}, which the installer "
                    + "already includes, so it adds nothing to this disc.",
            WhatAPatchAdds.SomethingPastTheInstaller =>
                $"Updates the installed game past this disc's installer, to {APatchAgainstTheInstaller.WhatItReaches(File.Name)}.",
            _ => "Looks like something that installs onto the game, so: " + AsAnAddOn,
        };
    }

    /// <summary>
    /// Whether the file's tick starts on. Only a patch the installer
    /// provably already covers starts off, because keeping a file is the
    /// recoverable mistake and quietly losing one is not.
    /// </summary>
    public bool WantedAgainst(string? installerFileName) =>
        APatchAgainstTheInstaller.For(installerFileName, File.Name) != WhatAPatchAdds.NothingTheInstallerLacks;
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
