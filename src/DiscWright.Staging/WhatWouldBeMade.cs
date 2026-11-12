using DiscWright.Core;

namespace DiscWright.Staging;

/// <summary>
/// Works out what a project would produce, without producing any of it.
/// </summary>
/// <remarks>
/// <para>
/// Pulled out of the builder because two things need the same answer and have
/// to agree exactly: the builder, which makes the discs, and the window, which
/// has to say <em>"this will make 3 discs"</em> before anybody presses
/// anything. Answered twice they would agree until somebody changed one of
/// them, and then the window would promise a disc count the build does not
/// produce.
/// </para>
/// <para>
/// It touches nothing. Sizes come from a function the caller supplies, so the
/// window can ask this question on every keystroke without reading a disk.
/// </para>
/// </remarks>
public static class WhatWouldBeMade
{
    public static Outcome<PlannedBuild> For(DiscProject project, Medium medium, Func<string, long> sizeOf)
    {
        if (project.Games.Count == 0)
        {
            return Refusals.ThereIsNothingToPutOnADisc();
        }

        // A loose extra whose name belongs to the disc would replace part of
        // what makes the disc work, so it is refused before anything is
        // planned rather than noticed afterwards.
        foreach (string item in project.Extras?.Loose ?? [])
        {
            string name = System.IO.Path.GetFileName(item.TrimEnd(
                System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

            if (ReservedDiscNames.Claims(name, project.Label))
            {
                return Refusals.ThatNameBelongsToTheDisc(name);
            }
        }

        long overhead = DiscOverhead.For(project, sizeOf);

        // Everything together first, because several games on one disc is the
        // ordinary answer and the only one that needs no explaining.
        Outcome<DiscLayout> together = DiscPlanner.Plan(RequestFor(project, project.Games, medium, overhead, sizeOf));

        if (together is Outcome<DiscLayout>.Ok(DiscLayout shared) && shared.Discs.Count == 1)
        {
            return new PlannedBuild([new PlannedOutput(project.Label, project.Games, shared, false)]);
        }

        // One game that needs more than one disc: a real set, where the discs
        // genuinely do continue one another.
        if (project.Games.Count == 1)
        {
            return together switch
            {
                Outcome<DiscLayout>.No refused => refused.Refusal,
                Outcome<DiscLayout>.Ok(DiscLayout set) =>
                    new PlannedBuild([new PlannedOutput(project.Label, project.Games, set, false)]),
                _ => Refusals.ThereIsNothingToPutOnADisc()
            };
        }

        // Several games that will not share a disc. A disc each, each named
        // after its own game, and none of them numbered, because none of them
        // continues another.
        List<PlannedOutput> outputs = [];

        foreach (GameFolder game in project.Games)
        {
            Outcome<DiscLayout> alone = DiscPlanner.Plan(RequestFor(project, [game], medium, overhead, sizeOf));

            if (alone is Outcome<DiscLayout>.No refused)
            {
                return refused.Refusal;
            }

            outputs.Add(new PlannedOutput(game.Name, [game], ((Outcome<DiscLayout>.Ok)alone).Value, true));
        }

        return new PlannedBuild(outputs);
    }

    internal static PlanRequest RequestFor(
        DiscProject project,
        IReadOnlyList<GameFolder> games,
        Medium medium,
        long overhead,
        Func<string, long> sizeOf) =>
        new(
            [
                .. games.SelectMany(game => game.Files.Select(file =>
                    new PlanFile(
                        WhereItGoes(games, game, file),
                        System.IO.Path.Combine(game.Path, file.RelativePath),
                        file.Bytes))),

                // Add-ons travel with their game and are planned beside it, so
                // they are never cut away onto a disc the game is not on. A
                // soundtrack on disc 3 of a game that finishes on disc 2 is a
                // button somebody can press and nothing else.
                .. AddOnsOf(project, games).Select(each =>
                    new PlanFile(each.RelativePath, each.Source, each.Bytes))
            ],
            medium,
            overhead,
            ExtrasBytes(project, sizeOf),
            project.Options.CutFiles);

    private static string WhereItGoes(IReadOnlyList<GameFolder> games, GameFolder game, ListedFile file)
    {
        string folder = OnTheDisc.FolderFor(games.ToList().IndexOf(game) + 1, game.Name, games.Count, game.Files.Select(file => file.RelativePath));

        return folder.Length == 0 ? file.RelativePath : System.IO.Path.Combine(folder, file.RelativePath);
    }

    private static IEnumerable<(string RelativePath, string Source, long Bytes)> AddOnsOf(
        DiscProject project, IReadOnlyList<GameFolder> games)
    {
        for (int at = 0; at < games.Count; at++)
        {
            string folder = OnTheDisc.FolderFor(at + 1, games[at].Name, games.Count, games[at].Files.Select(file => file.RelativePath));
            int which = project.Games.ToList().IndexOf(games[at]);

            foreach (AddOn addOn in project.AddOnsFor(which))
            {
                yield return (
                    System.IO.Path.Combine(folder, AddOn.Folder, System.IO.Path.GetFileName(addOn.Path)),
                    addOn.Path,
                    addOn.Bytes);
            }
        }
    }

    private static long ExtrasBytes(DiscProject project, Func<string, long> sizeOf)
    {
        DiscExtras? extras = project.Extras;

        if (extras is null)
        {
            return 0;
        }

        long bytes = 0;

        if (extras.ManualPath is not null)
        {
            bytes += sizeOf(extras.ManualPath);
        }

        if (extras.ExtrasFolderPath is not null)
        {
            bytes += sizeOf(extras.ExtrasFolderPath);
        }

        foreach (string item in extras.Loose)
        {
            bytes += sizeOf(item);
        }

        return bytes;
    }
}

/// <summary>Everything a build would produce.</summary>
/// <remarks>
/// Usually one output. Several when the games will not share a disc and each
/// gets one of its own, which is the only time a single project makes more than
/// one independent thing.
/// </remarks>
public sealed record PlannedBuild(IReadOnlyList<PlannedOutput> Outputs)
{
    /// <summary>How many discs in all, across everything this would make.</summary>
    public int Discs => Outputs.Sum(output => output.Layout.Discs.Count);

    /// <summary>
    /// What it would make, in words, for the line somebody reads before
    /// pressing anything.
    /// </summary>
    public string InWords
    {
        get
        {
            if (Outputs.Count > 1)
            {
                // "A disc each" only when it is true. One of them can need a
                // set of its own, and then four games are five discs.
                return Outputs.All(output => output.Layout.Discs.Count == 1)
                    ? $"{Outputs.Count} games, a disc each"
                    : $"{Outputs.Count} games on {Discs} discs";
            }

            int discs = Outputs[0].Layout.Discs.Count;

            // "one disc" rather than "1 disc", because this is a sentence
            // somebody reads rather than a figure they compare.
            return discs == 1 ? "one disc" : $"a set of {discs} discs";
        }
    }

    /// <summary>
    /// What cutting files is actually going to do here, or nothing when it
    /// would do nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The option used to be called "Split a file across two discs", which is
    /// a promise about two and was never what the planner does: it cuts a file
    /// into as many pieces as the file and the chosen disc between them come
    /// to, and ten is as ordinary as two. The wording said otherwise and that
    /// is all that was wrong, so this says the real number for the real
    /// project instead of a number baked into a label.
    /// </para>
    /// <para>
    /// A piece is found by being the first of its file. Every piece of one
    /// file carries the same part count and the same source, so the first of
    /// each is one per cut file and nothing has to be counted twice.
    /// </para>
    /// </remarks>
    public string WhatGetsCut
    {
        get
        {
            PlannedFile[] cut =
            [
                .. Outputs
                    .SelectMany(output => output.Layout.Discs)
                    .SelectMany(disc => disc.Files)
                    .Where(file => file.Part is { Index: 1 })
            ];

            if (cut.Length == 0)
            {
                return string.Empty;
            }

            if (cut.Length == 1)
            {
                FilePart part = cut[0].Part!;

                // Named, because which file is about to be cut in half is the
                // thing somebody wants to know, and the name is taken from
                // where it came from rather than from the piece, which is
                // called something with dwpart01 on the end.
                return $"{System.IO.Path.GetFileName(cut[0].Source)} is "
                    + $"{ByteSize.Describe(part.WholeBytes)} and will be cut into "
                    + $"{part.Count} pieces, a disc each.";
            }

            int pieces = cut.Sum(file => file.Part!.Count);

            return $"{cut.Length} files will be cut, into {pieces} pieces between them.";
        }
    }

    /// <summary>
    /// The line under Split a file too big for one disc: how many discs this
    /// comes to and whether anything is cut to get there, or nothing when
    /// there is nothing worth saying.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>More than one disc is always said, ticked or not.</strong> On
    /// 2026-11-12 the owner put Dead Space on a DVD5: 8.13 GB with no file over
    /// 3.91 GB, so a set of two with whole files on each. Ticked, this said
    /// "Nothing here is bigger than one disc, so nothing will be cut", which is
    /// true and read, right under the size, as "it fits". Unticked it said
    /// nothing at all, and the only sign of a second disc was the strip by the
    /// buttons. Needing more discs is not a mistake, so it is not a refusal,
    /// but it is said where the size was chosen.
    /// </para>
    /// <para>
    /// One disc says something only when the box is ticked, because then the
    /// question was asked. Unticked, a file too big for any disc is a refusal
    /// with words of its own and never reaches here.
    /// </para>
    /// </remarks>
    public string WhatCuttingComesTo(bool cuttingIsOn)
    {
        if (Discs > 1)
        {
            string cut = WhatGetsCut;

            return cut.Length > 0
                ? $"Needs {Discs} discs. {cut}"
                : $"Needs {Discs} discs. Every file fits on one, so each disc gets whole files and nothing is cut.";
        }

        return cuttingIsOn ? "Fits on one disc, so nothing will be cut." : string.Empty;
    }
}

/// <summary>One independent thing a build would produce: a disc, or a set.</summary>
/// <param name="Label">What it is called.</param>
/// <param name="Games">What goes on it.</param>
/// <param name="Layout">How it falls across discs.</param>
/// <param name="InItsOwnFolder">
/// Whether it needs a folder of its own under the output folder, which is true
/// only when several games each got a disc and would otherwise land on top of
/// one another.
/// </param>
public sealed record PlannedOutput(
    string Label,
    IReadOnlyList<GameFolder> Games,
    DiscLayout Layout,
    bool InItsOwnFolder);
