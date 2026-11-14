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
        game with { Files = [.. game.Files, .. chosen.SelectMany(each => each.Inside ?? [each.File])] };

    /// <summary>
    /// The same files, with everything inside a folder gathered into one row
    /// for the folder.
    /// </summary>
    /// <remarks>
    /// An artbook is a folder of forty pictures, and asking about it picture
    /// by picture is forty questions with one answer. A file at the top of
    /// the folder stays a row of its own, in the order the folder listed it;
    /// a folder takes the place of its first file.
    /// </remarks>
    public static IReadOnlyList<LeftBehind> Gathered(IReadOnlyList<LeftBehind> left)
    {
        List<LeftBehind> rows = [];
        Dictionary<string, List<ListedFile>> folders = new(StringComparer.OrdinalIgnoreCase);
        List<string> order = [];

        foreach (LeftBehind each in left)
        {
            string path = each.File.RelativePath;
            int slash = path.IndexOfAny(['/', (char)92]);

            if (slash < 0)
            {
                rows.Add(each);
                order.Add(path);
                continue;
            }

            string folder = path[..slash];

            if (!folders.TryGetValue(folder, out List<ListedFile>? inside))
            {
                folders[folder] = inside = [];
                rows.Add(null!);
                order.Add(folder);
            }

            inside.Add(each.File);
        }

        for (int at = 0; at < rows.Count; at++)
        {
            if (rows[at] is null)
            {
                List<ListedFile> inside = folders[order[at]];

                rows[at] = new LeftBehind(
                    new ListedFile(order[at], inside.Sum(file => file.Bytes)),
                    LeftBehindKind.LooksLikeSomethingToKeep,
                    inside);
            }
        }

        return rows;
    }

    /// <summary>
    /// What the tick offering the same answers for the games after this one
    /// says, or nothing when no game comes after it, and then there is no
    /// tick at all.
    /// </summary>
    public static string? TheSameForTheRest(int after) => after switch
    {
        <= 0 => null,
        1 => "Answer the same way for the other game being added",
        _ => $"Answer the same way for the other {after} games being added"
    };

    /// <summary>The dialog's headline, counting rows as somebody sees them.</summary>
    public static string Headline(IReadOnlyList<LeftBehind> rows)
    {
        string what = rows.All(row => !row.IsAFolder) ? "file" : "thing";

        return rows.Count == 1
            ? $"There is one more {what} in that folder"
            : $"There are {rows.Count} more {what}s in that folder";
    }

    /// <summary>
    /// The game as the answers make it, and what is to be attached to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Here rather than in the dialog, which no test can open, so what each
    /// of the four answers does can be asked.
    /// </para>
    /// <para>
    /// The game's manual and its extras become its own, pointing at where
    /// they are in the folder, and get its menu's Manual and Extras buttons.
    /// Beside the installer goes in with the game's files, with no button,
    /// which is what "Copy it onto the disc" always did. An add-on is
    /// something that installs, so a folder answered as one goes on as an
    /// extra: there is nothing in a folder to run.
    /// </para>
    /// </remarks>
    public static (GameFolder Game, IReadOnlyList<LeftBehind> AddOns) Answered(
        GameFolder game, IEnumerable<(LeftBehind What, WhatBecomesOfIt As)> answers)
    {
        List<LeftBehind> beside = [];
        List<LeftBehind> addOns = [];
        List<GameExtra> own = [.. game.OwnOrNone];

        foreach ((LeftBehind what, WhatBecomesOfIt becomes) in answers)
        {
            WhatBecomesOfIt really = becomes == WhatBecomesOfIt.AddOn && what.IsAFolder
                ? WhatBecomesOfIt.Extra
                : becomes;

            switch (really)
            {
                case WhatBecomesOfIt.BesideTheInstaller:
                    beside.Add(what);
                    break;

                case WhatBecomesOfIt.AddOn:
                    addOns.Add(what);
                    break;

                default:
                    own.Add(new GameExtra(
                        System.IO.Path.Combine(game.Path, what.File.RelativePath),
                        really == WhatBecomesOfIt.Manual ? GameExtraKind.Manual : GameExtraKind.Extra,
                        what.Bytes,
                        what.Inside?.Count ?? 1));
                    break;
            }
        }

        GameFolder taken = Taking(game, beside);

        return (own.Count > 0 ? taken with { Own = own } : taken, addOns);
    }

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

/// <summary>One file the game did not take, or one folder of them.</summary>
/// <param name="File">
/// The file, or for a folder, its name and everything in it summed.
/// </param>
/// <param name="Inside">
/// The files in it, when it is a folder, each by its path in the game's
/// folder. None for a file.
/// </param>
public sealed record LeftBehind(ListedFile File, LeftBehindKind Kind, IReadOnlyList<ListedFile>? Inside = null)
{
    /// <summary>Whether this row is a whole folder.</summary>
    public bool IsAFolder => Inside is not null;

    /// <summary>
    /// What the dialog suggests for it, which is only ever a suggestion.
    /// </summary>
    /// <remarks>
    /// The table in the design the owner approved on 2026-11-13: something
    /// with manual or guide in its name is the game's manual, an executable
    /// is an add-on, and everything else, folders, archives, music and
    /// pictures, is one of the game's extras.
    /// </remarks>
    public WhatBecomesOfIt Suggested =>
        Kind == LeftBehindKind.LooksLikeAnAddOn && !IsAFolder ? WhatBecomesOfIt.AddOn
        : LooksLikeAManual ? WhatBecomesOfIt.Manual
        : WhatBecomesOfIt.Extra;

    private static readonly string[] ManualsAre = [".pdf", ".chm", ".htm", ".html", ".txt", ".rtf"];

    private bool LooksLikeAManual
    {
        get
        {
            string bare = IsAFolder ? Name : System.IO.Path.GetFileNameWithoutExtension(Name);

            bool named = bare.Contains("manual", StringComparison.OrdinalIgnoreCase)
                || bare.Contains("guide", StringComparison.OrdinalIgnoreCase);

            return named && (IsAFolder || ManualsAre.Contains(
                System.IO.Path.GetExtension(Name), StringComparer.OrdinalIgnoreCase));
        }
    }

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
        if (Inside is { } inside)
        {
            return inside.Count == 1 ? "A folder of 1 file" : $"A folder of {inside.Count} files";
        }

        if (Kind != LeftBehindKind.LooksLikeAnAddOn)
        {
            return Suggested == WhatBecomesOfIt.Manual
                ? "Looks like the game's manual"
                : "Looks like something to read or look at";
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

/// <summary>
/// The four answers the dialog offers for each row, in the order it lists
/// them.
/// </summary>
public enum WhatBecomesOfIt
{
    /// <summary>The game's manual, behind its menu's Manual button.</summary>
    Manual,

    /// <summary>One of the game's extras, behind its menu's Extras button.</summary>
    Extra,

    /// <summary>Something that installs onto the game, dead until it is installed.</summary>
    AddOn,

    /// <summary>
    /// With the game's files, beside its installer, with no button. Called
    /// "Copy it onto the disc" before 2.0.4, which xniwo found said nothing
    /// about where it went or that nothing on the menu would show it.
    /// </summary>
    BesideTheInstaller
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
