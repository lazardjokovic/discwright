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
/// <param name="Bytes">How big it is.</param>
public sealed partial record AddOn(int BelongsTo, string Path, string Name, long Bytes)
{
    /// <summary>Where add-ons sit, beside the game they belong to.</summary>
    public const string Folder = "Add-ons";

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

    [GeneratedRegex(@"^patch_.+_to_(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PatchToVersion();

    [GeneratedRegex(@"\s+")]
    private static partial Regex RunsOfSpace();
}
