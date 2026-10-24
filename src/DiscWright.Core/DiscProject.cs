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
    DiscArtwork? Artwork = null,
    DiscExtras? Extras = null,
    IReadOnlyList<AddOn>? AddOns = null)
{
    /// <summary>The add-ons, or none.</summary>
    public IReadOnlyList<AddOn> AddOnsOrNone => AddOns ?? [];

    /// <summary>The add-ons belonging to one game, in the order they were added.</summary>
    public IEnumerable<AddOn> AddOnsFor(int game) => AddOnsOrNone.Where(addOn => addOn.BelongsTo == game);

    /// <summary>
    /// The same project narrowed to some of its games, under another label,
    /// with the add-ons of those games pointed at where the games sit now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a game that gets a disc of its own when several will not share
    /// one. <strong>An add-on remembers its game by position</strong>, so a
    /// project cut down to one game with its add-on list left whole hands
    /// every game the add-ons of whichever game sat first: the second game's
    /// menu offered the first game's patches, and its own, whose files were
    /// on its disc, were on no button at all. The builder and the preview
    /// both cut the project down this way and both had this wrong.
    /// </para>
    /// <para>
    /// Games are matched by identity rather than by name, because two folders
    /// can report the same name and a patch must stay with the folder it was
    /// taken from.
    /// </para>
    /// </remarks>
    public DiscProject KeepingOnly(IReadOnlyList<GameFolder> kept, string label)
    {
        List<AddOn> addOns = [];

        for (int now = 0; now < kept.Count; now++)
        {
            int was = IndexOf(kept[now]);

            if (was < 0)
            {
                continue;
            }

            addOns.AddRange(AddOnsFor(was).Select(addOn => addOn with { BelongsTo = now }));
        }

        return this with { Label = label, Games = kept, AddOns = addOns };
    }

    private int IndexOf(GameFolder game)
    {
        for (int at = 0; at < Games.Count; at++)
        {
            if (ReferenceEquals(Games[at], game))
            {
                return at;
            }
        }

        return -1;
    }
}

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
    bool LegacyFileSystems = false,
    bool CutFiles = false);
