using System.Text.RegularExpressions;

namespace DiscWright.Core;

/// <summary>
/// Something installed after a game, and only after it: a patch, a DLC, a
/// soundtrack, somebody's overhaul mod.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Picked as a file, not a folder.</strong> A patch or a DLC usually
/// sits in the same folder as the game it belongs to, so pointing at the folder
/// would just find the game again.
/// </para>
/// <para>
/// Any executable is accepted. The <c>setup_</c> rule exists to identify a GOG
/// <em>game folder</em>, and an add-on's identity comes from the game it was
/// attached to rather than from its file name. That is what lets a mod or an
/// overhaul, which is never called <c>setup_</c> anything, go on the disc at
/// all.
/// </para>
/// </remarks>
/// <param name="BelongsTo">
/// Which game in the project it sits under, from zero. An add-on with no game
/// is not an add-on, it is a game of its own.
/// </param>
/// <param name="Path">Where the installer is now.</param>
/// <param name="Name">What its button says.</param>
/// <param name="Bytes">How big the installer is, on its own.</param>
/// <param name="Parts">
/// The numbered parts that belong to the installer, each where it is now.
/// None for most add-ons, which are one file.
/// </param>
public sealed partial record AddOn(
    int BelongsTo, string Path, string Name, long Bytes, IReadOnlyList<AddOnPart>? Parts = null)
{
    /// <summary>Where add-ons sit, beside the game they belong to.</summary>
    public const string Folder = "Add-ons";

    /// <summary>Its parts, or none.</summary>
    public IReadOnlyList<AddOnPart> PartsOrNone => Parts ?? [];

    /// <summary>How big it is with everything it needs beside it.</summary>
    public long BytesWithItsParts => Bytes + PartsOrNone.Sum(part => part.Bytes);

    /// <summary>
    /// The numbered parts that belong to one installer, in name order, out of
    /// the files it was listed with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Parts belong to ONE installer, sit in the same folder as it and are
    /// named "&lt;installer&gt;-1.bin". Taking every .bin in the folder swept
    /// in the parts of a DLC or of a second game stored alongside. Matched as
    /// plain strings and never as wildcards, because GOG names are full of
    /// brackets and dots that a pattern would read as syntax.
    /// </para>
    /// <para>
    /// <strong>One rule, asked by the game and by its add-ons.</strong> A big
    /// DLC arrives the way a big game does, an .exe and its .bin files, and
    /// an add-on used to be the .exe alone: it went into Add-ons without its
    /// parts, where it could not install, and its parts were offered
    /// separately as extras. Reported against 2.0.4.
    /// </para>
    /// </remarks>
    public static List<ListedFile> PartsOf(ListedFile installer, IEnumerable<ListedFile> listedWith)
    {
        string within = installer.RelativePath[..^installer.Name.Length];
        string stem = installer.NameWithoutExtension + "-";

        return
        [
            .. listedWith
                .Where(file => file.Extension == ".bin"
                               && file.Name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(
                                   file.RelativePath[..^file.Name.Length],
                                   within,
                                   StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>
    /// The same add-ons after the game at that position has been taken off
    /// the list: the ones belonging to it gone, and the ones after it moved
    /// down by one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>An add-on remembers its game by where that game sits.</strong>
    /// So taking a game out moves every game after it, and without this a
    /// patch for the game that was removed quietly becomes a patch for the
    /// next one along, which on the disc means a patch in the wrong game's
    /// Add-ons folder and a button in the wrong game's menu.
    /// </para>
    /// <para>
    /// Here rather than written out twice in the window, where it was the same
    /// loop over two lists and a mutation pass found the shuffle survived a
    /// thousand tests. The position is the only thing holding the two
    /// together, so the sum that moves it is worth being able to ask about.
    /// </para>
    /// <para>
    /// <strong>One fault planted here cannot be caught, and that is the right
    /// answer rather than a missing test.</strong> Turning the
    /// <c>&gt; which</c> below into <c>&gt;= which</c> changes nothing,
    /// because the line above it has already taken out every add-on belonging
    /// to the game that went, so nothing reaching the sum can be equal to it.
    /// </para>
    /// </remarks>
    /// <param name="addOns">Every add-on, whichever game it belongs to.</param>
    /// <param name="which">Where the game that has gone was sitting, from nought.</param>
    public static IReadOnlyList<AddOn> AfterTheGameAt(IReadOnlyList<AddOn> addOns, int which) =>
    [
        .. addOns
            .Where(addOn => addOn.BelongsTo != which)
            .Select(addOn => addOn.BelongsTo > which
                ? addOn with { BelongsTo = addOn.BelongsTo - 1 }
                : addOn)
    ];

    /// <summary>
    /// The add-ons after the games have changed places, each still belonging
    /// to the game it belonged to.
    /// </summary>
    /// <remarks>
    /// An add-on knows its game by where the game sits, so a list put in
    /// another order without this hands every patch to whichever game is now
    /// in the old place: move Hollow Knight above The Witcher and the
    /// Witcher's DLC installs onto Hollow Knight. Kept in the order of the
    /// games they now follow, and in their own order under one game, so the
    /// menu lists them as the list does.
    /// </remarks>
    /// <param name="addOns">Every add-on, whichever game it belongs to.</param>
    /// <param name="whereEachWent">
    /// For each old place, from nought, the place that game is in now.
    /// </param>
    public static IReadOnlyList<AddOn> AfterTheGamesMoved(IReadOnlyList<AddOn> addOns, IReadOnlyList<int> whereEachWent) =>
    [
        .. addOns
            .Select(addOn => addOn.BelongsTo >= 0 && addOn.BelongsTo < whereEachWent.Count
                ? addOn with { BelongsTo = whereEachWent[addOn.BelongsTo] }
                : addOn)
            .OrderBy(addOn => addOn.BelongsTo)
    ];

    /// <summary>
    /// What to call an add-on, worked out from its file name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Never from the product name inside the file.</strong> Every GOG
    /// patch reports the product name of the game it patches, so all four
    /// Hollow Knight patches call themselves "Hollow Knight", and a disc built
    /// from them would offer four identical buttons.
    /// </para>
    /// <para>
    /// A patch is named after the version it moves <em>to</em>, because that is
    /// what tells two patches apart, and it goes at the front where a button
    /// will not clip it.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The words GOG puts in the name of a thing that is not the game: a
    /// soundtrack, an artbook, wallpapers.
    /// </summary>
    /// <remarks>
    /// A vocabulary rather than a rule, because there is no marker in the file
    /// itself. It is used only to ask a question, never to refuse one, so a
    /// game genuinely called "Artbook Simulator" costs somebody one dialog and
    /// nothing else.
    /// </remarks>
    private static readonly string[] NotTheGame =
    [
        "soundtrack", "artbook", "art_book", "wallpaper", "avatars", "avatar",
        "manual", "bonus", "goodies", "artworks", "ringtones", "soundtracks"
    ];

    /// <summary>
    /// Whether a file name looks like something that installs ONTO a game
    /// rather than a game.
    /// </summary>
    /// <remarks>
    /// A patch is certain, because <c>patch_x_to_y</c> says so outright. The
    /// rest is a guess from what GOG calls its extras.
    /// </remarks>
    public static bool LooksLikeOne(string fileName)
    {
        string bare = System.IO.Path.GetFileNameWithoutExtension(fileName);

        return PatchToVersion().IsMatch(bare)
            || NotTheGame.Any(word => bare.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The numbered parts sitting beside an installer on the disk now, or
    /// nothing when it has none.
    /// </summary>
    /// <remarks>
    /// Read off the disk rather than out of a project file, the way a game's
    /// own parts are. So the file says nothing new, a project saved by an
    /// older version comes back with the parts it never knew about, and one
    /// saved now opens in an older version exactly as its own did. Asked the
    /// same way when an installer is attached to a game by hand.
    /// </remarks>
    public static IReadOnlyList<AddOnPart>? PartsBeside(string installer, IFileSystem files)
    {
        string? folder = System.IO.Path.GetDirectoryName(installer);

        if (string.IsNullOrEmpty(folder) || !files.FolderExists(folder))
        {
            return null;
        }

        List<ListedFile> parts = PartsOf(
            new ListedFile(System.IO.Path.GetFileName(installer), 0),
            files.ListHere(folder).Files);

        return parts.Count > 0
            ? [.. parts.Select(part => new AddOnPart(System.IO.Path.Combine(folder, part.RelativePath), part.Bytes))]
            : null;
    }

    /// <summary>
    /// Whether a file is named the way GOG names something that installs:
    /// <c>setup_</c> or <c>patch_</c>, and an executable.
    /// </summary>
    public static bool IsNamedLikeAnInstaller(string fileName) =>
        fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && (fileName.StartsWith("setup_", StringComparison.OrdinalIgnoreCase)
            || fileName.StartsWith("patch_", StringComparison.OrdinalIgnoreCase));

    public static string NameFor(string fileName)
    {
        string raw = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string name = raw;

        Match patch = PatchToVersion().Match(name);

        if (patch.Success)
        {
            name = "Update " + patch.Groups[1].Value;
        }
        else if (name.StartsWith("setup_", StringComparison.OrdinalIgnoreCase))
        {
            name = name["setup_".Length..];
        }

        name = RunsOfSpace().Replace(name.Replace('_', ' '), " ").Trim();

        // A file name that is nothing but underscores leaves an empty label,
        // and a button with no text is a button nobody can identify.
        if (name.Length == 0)
        {
            name = raw.Trim();
        }

        return name.Length == 0 ? "Add-on" : name;
    }

    /// <summary>
    /// The build an update leaves a game at: the number GOG writes in
    /// brackets at the end of a patch's name. Nothing for anything that is
    /// not an update, or whose name does not end in one.
    /// </summary>
    public static long? TheBuildItReaches(string fileName)
    {
        Match build = PatchToABuild().Match(System.IO.Path.GetFileNameWithoutExtension(fileName));

        return build.Success && long.TryParse(
            build.Groups[1].Value, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out long reached)
            ? reached
            : null;
    }

    /// <summary>
    /// A game's add-ons as they are found, with its updates in the order
    /// they are installed in: lowest build first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They came in the order of their file names, and a patch's file name
    /// starts with the version it patches <em>from</em> written as text. The
    /// owner's own Hollow Knight, 20 November 2026, listed 1.5.12618,
    /// 1.5.12620, 1.5.12459 and 1.5.78.11833a, which by GOG's build numbers
    /// is the third, the fourth, the second and the first.
    /// </para>
    /// <para>
    /// <strong>Only the updates move, and only among the places updates
    /// already had.</strong> A DLC or a soundtrack has no build to go by, so
    /// it stays exactly where it was, and so does an update whose name says
    /// no build. Only for add-ons as they are first found: an order somebody
    /// made, or one a project file carries, is never put through this.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<AddOn> WithUpdatesInTheirOrder(IReadOnlyList<AddOn> addOns)
    {
        Queue<AddOn> inOrder = new(addOns
            .Where(addOn => TheBuildItReaches(addOn.Path) is not null)
            .OrderBy(addOn => addOn.BelongsTo)
            .ThenBy(addOn => TheBuildItReaches(addOn.Path)));

        // Each game's updates go back into that game's own places, which a
        // sort that is stable by game does without mixing two games.
        Dictionary<int, Queue<AddOn>> perGame = inOrder
            .GroupBy(addOn => addOn.BelongsTo)
            .ToDictionary(group => group.Key, group => new Queue<AddOn>(group));

        return
        [
            .. addOns.Select(addOn => TheBuildItReaches(addOn.Path) is null
                ? addOn
                : perGame[addOn.BelongsTo].Dequeue())
        ];
    }

    /// <summary>
    /// One game's add-ons with one of them moved to where another is, before
    /// it or after, and every other game's left as they were.
    /// </summary>
    /// <returns>The list as it is now, or nothing when nothing would move.</returns>
    public static IReadOnlyList<AddOn>? WithOneMoved(
        IReadOnlyList<AddOn> addOns, int game, string moved, string onto, bool after)
    {
        List<AddOn> its = [.. addOns.Where(addOn => addOn.BelongsTo == game)];
        int from = its.FindIndex(addOn => string.Equals(addOn.Path, moved, StringComparison.OrdinalIgnoreCase));
        int target = its.FindIndex(addOn => string.Equals(addOn.Path, onto, StringComparison.OrdinalIgnoreCase));

        if (from < 0 || target < 0 || from == target)
        {
            return null;
        }

        AddOn carried = its[from];
        AddOn over = its[target];
        List<AddOn> now = [.. its];

        now.RemoveAt(from);
        now.Insert(now.IndexOf(over) + (after ? 1 : 0), carried);

        if (now.SequenceEqual(its))
        {
            return null;
        }

        Queue<AddOn> next = new(now);

        return [.. addOns.Select(addOn => addOn.BelongsTo == game ? next.Dequeue() : addOn)];
    }

    [GeneratedRegex(@"^patch_.+_to_(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PatchToVersion();

    [GeneratedRegex(@"^patch_.+_to_.+\((\d+)\)$", RegexOptions.IgnoreCase)]
    private static partial Regex PatchToABuild();

    [GeneratedRegex(@"\s+")]
    private static partial Regex RunsOfSpace();
}

/// <summary>One numbered part of an add-on's installer.</summary>
/// <param name="Path">Where it is now.</param>
/// <param name="Bytes">How big it is.</param>
public sealed record AddOnPart(string Path, long Bytes);
