using DiscWright.Core;

namespace DiscWright.ViewModels;

/// <summary>
/// Turns a game and its add-ons into a tree of rows, and reads back what is
/// still ticked.
/// </summary>
/// <remarks>
/// A game's files arrive as a flat list of paths relative to its folder, so
/// <c>Add-ons/setup_patch.exe</c> and <c>data/textures/skin.pak</c> are both
/// one string. Shown flat, an installed game is three hundred rows of nothing;
/// shown as the folders they already describe, it is what somebody put there.
/// </remarks>
public static class TheTreeOfWhatGoesOn
{
    /// <summary>What add-ons are gathered under.</summary>
    public const string AddOnsAreUnder = "Add-ons";

    /// <summary>What the installer row says it is.</summary>
    public const string TheInstaller = "the installer";

    /// <summary>What an add-on row says it is.</summary>
    public const string AnAddOn = "an add-on";

    /// <summary>
    /// One game, with everything found in it and every add-on of its own.
    /// </summary>
    /// <param name="game">The game as it was found, with every file.</param>
    /// <param name="addOns">Its add-ons, already narrowed to this game.</param>
    /// <param name="tell">Called whenever any tick in this game changes.</param>
    public static AThingOnTheDisc For(GameFolder game, IEnumerable<AddOn> addOns, Action? tell = null)
    {
        AThingOnTheDisc root = new(game.Name, 0, null, game.Path, tell);

        foreach (ListedFile file in game.Files)
        {
            string[] steps = file.RelativePath.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);

            AThingOnTheDisc under = root;
            string sofar = string.Empty;

            for (int at = 0; at < steps.Length - 1; at++)
            {
                // The folder's OWN path, built up as the walk goes down.
                // Every folder used to be handed the game's folder instead, so
                // hovering "data" and hovering "textures" inside it said the
                // same thing, and neither said where it was.
                sofar = sofar.Length == 0 ? steps[at] : sofar + (char)92 + steps[at];
                under = FolderIn(under, steps[at], sofar);
            }

            bool isTheInstaller = game.Installer is not null
                && string.Equals(game.Installer.RelativePath, file.RelativePath, StringComparison.OrdinalIgnoreCase);

            under.Holding(new AThingOnTheDisc(
                steps.Length == 0 ? file.RelativePath : steps[^1],
                file.Bytes,
                isTheInstaller ? TheInstaller : null,
                file.RelativePath));
        }

        // Add-ons are not in the game's own listing. They were left behind in
        // the folder and taken on deliberately, and they are written into a
        // folder of their own, so that is where they are shown.
        AddOn[] its = [.. addOns];
        if (its.Length > 0)
        {
            // Named after where they land rather than where they came from.
            // Add-ons are not in the game's listing at all: they were left
            // beside it and taken on, and each one carries its own full path.
            AThingOnTheDisc under = FolderIn(root, AddOnsAreUnder, AddOnsAreUnder);
            foreach (AddOn addOn in its)
            {
                under.Holding(new AThingOnTheDisc(addOn.Name, addOn.Bytes, AnAddOn, addOn.Path));
            }
        }

        return root;
    }

    /// <summary>
    /// The game again, carrying only the files still ticked.
    /// </summary>
    /// <remarks>
    /// Everything downstream reads <see cref="GameFolder.Files"/>: the size,
    /// the refusals, how many discs it takes, what the builder writes and what
    /// goes in the ISO. So handing back a game with fewer files is the whole
    /// of leaving a file off, and nothing else had to learn about it.
    ///
    /// The installer is cleared when it is not ticked. The menu already copes
    /// with a game that has none, writing no Install button rather than one
    /// that points at a file nobody put on the disc.
    /// </remarks>
    public static GameFolder AsChosen(this AThingOnTheDisc root, GameFolder game)
    {
        // Every path still ticked, add-ons included, which costs nothing:
        // an add-on's path is built onto the game folder and is therefore
        // absolute, while a file's is relative to it, so no add-on can be
        // mistaken for a file here. Filtering them out first was a guard
        // against something that cannot happen, and no test could tell
        // whether it was there.
        HashSet<string> on = new(
            root.EverythingOn().Select(each => each.ItsPath),
            StringComparer.OrdinalIgnoreCase);

        ListedFile[] kept = [.. game.Files.Where(file => on.Contains(file.RelativePath))];

        bool theInstallerStayed = game.Installer is not null && on.Contains(game.Installer.RelativePath);

        return game with
        {
            Files = kept,
            Installer = theInstallerStayed ? game.Installer : null
        };
    }

    /// <summary>The add-ons of this game that are still ticked.</summary>
    public static IEnumerable<AddOn> AddOnsChosen(this AThingOnTheDisc root, IEnumerable<AddOn> addOns)
    {
        HashSet<string> on = new(
            root.EverythingOn().Where(each => each.What == AnAddOn).Select(each => each.ItsPath),
            StringComparer.OrdinalIgnoreCase);

        return addOns.Where(addOn => on.Contains(addOn.Path));
    }

    /// <summary>
    /// Whether this game's installer has been turned off, which is allowed and
    /// worth saying out loud.
    /// </summary>
    public static bool ItsInstallerIsOff(this AThingOnTheDisc root) =>
        root.EveryThing().Any(each => each.What == TheInstaller && each.IsOn == false);

    private static AThingOnTheDisc FolderIn(AThingOnTheDisc under, string named, string path)
    {
        AThingOnTheDisc? already = under.Inside
            .FirstOrDefault(each => each.HasThingsInside && each.Name == named);

        if (already is not null)
        {
            return already;
        }

        AThingOnTheDisc made = new(named, 0, null, path);

        // Put under its parent before anything goes in it, so the sizes add up
        // the way they are meant to.
        under.Holding(made);
        return made;
    }

    /// <summary>
    /// What the whole list comes to, or nothing when nothing is ticked.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every row of the tree shows its own size and a game shows the sum of
    /// what is under it, and there was nowhere at all that said what the lot
    /// came to. With four games on the window somebody had to add them up by
    /// eye to work out whether it would fit.
    /// </para>
    /// <para>
    /// <strong>It follows the ticks.</strong> What goes on the disc is what is
    /// ticked, and a total that counted rows somebody had just turned off
    /// would be worse than no total at all.
    /// </para>
    /// <para>
    /// The files alone, which is what the tree is a list of. The menu, the
    /// artwork and the extras take their own room on top of this, and what
    /// that comes to is already said where it matters: in the disc count, and
    /// in the refusal when it will not fit.
    /// </para>
    /// <para>
    /// Out of the window because the sentence is about the tree and nothing
    /// else, and because nothing asserted either half of it: a mutation pass
    /// could turn "in one file" into "in 1 files" and the suite went green.
    /// </para>
    /// </remarks>
    public static string WhatItAllComesTo(this IEnumerable<AThingOnTheDisc> tree)
    {
        int things = 0;
        long bytes = 0;

        foreach (AThingOnTheDisc game in tree)
        {
            foreach (AThingOnTheDisc thing in game.EverythingOn())
            {
                things++;
                bytes += thing.Bytes;
            }
        }

        if (things == 0)
        {
            // Everything turned off is a thing somebody has just done on
            // purpose, and the line under this one already says so.
            return string.Empty;
        }

        return things == 1
            ? $"{ByteSize.Describe(bytes)} in one file"
            : $"{ByteSize.Describe(bytes)} in {things} files";
    }

    /// <summary>
    /// What has been taken off the disc, said plainly, or nothing when the
    /// answer is everything.
    /// </summary>
    /// <remarks>
    /// Leaving a file off is allowed, because this window refuses by
    /// explaining rather than by greying things out, and somebody making a
    /// disc of just the extras has a reason nobody here can guess. It is said
    /// out loud because a disc that cannot install the game is not what
    /// anybody sets out to make by accident.
    /// </remarks>
    public static string? WhatIsTurnedOff(this IEnumerable<AThingOnTheDisc> tree)
    {
        int left = 0;
        long bytes = 0;

        foreach (AThingOnTheDisc thing in tree.SelectMany(game => game.EveryThing()))
        {
            if (thing.IsOn == false)
            {
                left++;
                bytes += thing.Bytes;
            }
        }

        if (left == 0)
        {
            return null;
        }

        return $"Leaving off {(left == 1 ? "1 thing" : $"{left} things")}, {ByteSize.Describe(bytes)}.";
    }

    /// <summary>
    /// What the button that clears out the turned off rows says, or nothing
    /// when there are none and there is no such button.
    /// </summary>
    /// <remarks>
    /// Its own count in its own words, because the button does something
    /// nobody can see the extent of: the rows it takes off are scattered down
    /// a tree and some of them are inside folders that are shut.
    /// </remarks>
    public static string? WhatTakingThoseOffWouldSay(this IEnumerable<AThingOnTheDisc> tree) =>
        tree.SelectMany(game => game.EveryThing()).Count(each => each.IsOn == false) switch
        {
            0 => null,
            1 => "Remove the 1 thing left off",
            int left => $"Remove the {left} things left off"
        };

}
