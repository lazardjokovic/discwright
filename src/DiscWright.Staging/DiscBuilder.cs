using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;

namespace DiscWright.Staging;

/// <summary>
/// Turns a project into disc folders you could burn, or into a refusal.
/// </summary>
/// <remarks>
/// <para>
/// The piece that was missing while every part of it existed. It decides how
/// many discs there are, makes each staging folder, and runs the steps over it
/// in the order they have to happen.
/// </para>
/// <para>
/// It makes <strong>folders</strong>, not images. Turning a folder into an ISO
/// is a separate job with separate failures, and a folder is the thing somebody
/// can open and click before any of that exists.
/// </para>
/// </remarks>
public sealed class DiscBuilder(IFileSystem files, IStageWriter writer, IImages images, IHasher hasher)
{
    /// <summary>
    /// The steps, in the order they have to run.
    /// </summary>
    /// <remarks>
    /// The order is not arrangement, it is correctness. The checksum step
    /// hashes what every other step wrote, so it goes last; run earlier it
    /// would hash a half-made disc and still look like it worked. The rest
    /// could be in any order and are in the order somebody would describe them.
    /// </remarks>
    private IReadOnlyList<IBuildStep> Steps =>
    [
        new CopyTheFilesStep(),
        new CopyTheExtrasStep(files),
        new CopyTheMusicStep(),
        new WriteTheAutorunStep(),
        new CopyTheArtworkStep(images),
        new WriteTheMenuStep(),
        new WriteTheChecksumsStep(files, hasher)
    ];

    /// <summary>
    /// Builds everything the project describes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Games that do not all fit on one disc get a disc each.</strong>
    /// A numbered set is a promise that disc 2 continues disc 1, which is true
    /// for one game split across discs and a lie for four unrelated games, so
    /// unrelated games are not called a set at all. Several games that DO fit
    /// share one disc, which is the ordinary compilation.
    /// </para>
    /// </remarks>
    public async Task<Outcome<BuiltDiscs>> Build(
        DiscProject project,
        Medium medium,
        string outputFolder,
        IProgress<BuildProgress>? progress,
        CancellationToken ct)
    {
        if (project.Games.Count == 0)
        {
            return Refusals.ThereIsNothingToPutOnADisc();
        }

        long overhead = DiscOverhead.For(project, SizeOf);

        // A loose extra whose name belongs to the disc would replace part of
        // what makes the disc work, so it is refused before anything is
        // planned rather than noticed afterwards.
        foreach (string item in project.Extras?.Loose ?? [])
        {
            string name = System.IO.Path.GetFileName(item.TrimEnd(
                System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

            if (ReservedDiscNames.Claims(name))
            {
                return Refusals.ThatNameBelongsToTheDisc(name);
            }
        }

        // Everything together first, because several games on one disc is the
        // ordinary answer and the only one that needs no explaining.
        Outcome<DiscLayout> together = DiscPlanner.Plan(RequestFor(project, project.Games, medium, overhead));

        if (together is Outcome<DiscLayout>.Ok(DiscLayout layout) && layout.Discs.Count == 1)
        {
            return await Make(project, project.Games, layout, project.Label, outputFolder, progress, ct);
        }

        // One game, and it needs more than one disc: that is a real set, and
        // the discs really do continue one another.
        if (project.Games.Count == 1)
        {
            return together switch
            {
                Outcome<DiscLayout>.No refused => refused.Refusal,
                Outcome<DiscLayout>.Ok(DiscLayout set) =>
                    await Make(project, project.Games, set, project.Label, outputFolder, progress, ct),
                _ => Refusals.ThereIsNothingToPutOnADisc()
            };
        }

        // Several games that will not share a disc. A disc each, each named
        // after its own game, and none of them numbered, because none of them
        // continues another.
        List<BuiltDisc> made = [];

        foreach (GameFolder game in project.Games)
        {
            ct.ThrowIfCancellationRequested();

            Outcome<DiscLayout> alone = DiscPlanner.Plan(RequestFor(project, [game], medium, overhead));

            if (alone is Outcome<DiscLayout>.No refused)
            {
                return refused.Refusal;
            }

            DiscLayout its = ((Outcome<DiscLayout>.Ok)alone).Value;

            Outcome<BuiltDiscs> built = await Make(
                project with { Games = [game] },
                [game],
                its,
                game.Name,
                System.IO.Path.Combine(outputFolder, Tidied(game.Name)),
                progress,
                ct);

            if (built is Outcome<BuiltDiscs>.No stopped)
            {
                return stopped.Refusal;
            }

            made.AddRange(((Outcome<BuiltDiscs>.Ok)built).Value.Discs);
        }

        return new BuiltDiscs(made);
    }

    private async Task<Outcome<BuiltDiscs>> Make(
        DiscProject project,
        IReadOnlyList<GameFolder> games,
        DiscLayout layout,
        string label,
        string outputFolder,
        IProgress<BuildProgress>? progress,
        CancellationToken ct)
    {
        DiscProject forThisDisc = project with { Label = label, Games = games };
        BuildPipeline pipeline = new(Steps);
        List<BuiltDisc> made = [];

        foreach (PlannedDisc disc in layout.Discs)
        {
            ct.ThrowIfCancellationRequested();

            // "disc" on its own, "disc D1" in a set, which is what v1 writes and
            // what anybody who has one of these folders already expects.
            string folder = System.IO.Path.Combine(
                outputFolder,
                layout.Discs.Count > 1 ? $"disc D{disc.Number}" : "disc");

            // Emptied, not just made. Built twice, a file left from the first
            // run is hashed into the second run's checksum list and burned,
            // where it is now correct and permanent.
            writer.EmptyTheFolder(folder);

            await pipeline.Run(new BuildContext(forThisDisc, layout, disc, folder, writer), progress, ct);

            made.Add(new BuiltDisc(label, disc.Number, layout.Discs.Count, folder, disc.Bytes));
        }

        return new BuiltDiscs(made);
    }

    private PlanRequest RequestFor(
        DiscProject project,
        IReadOnlyList<GameFolder> games,
        Medium medium,
        long overhead) =>
        new(
            [.. games.SelectMany(game => game.Files.Select(file =>
                new PlanFile(
                    WhereItGoes(games, game, file),
                    System.IO.Path.Combine(game.Path, file.RelativePath),
                    file.Bytes)))],
            medium,
            overhead,
            ExtrasBytes(project),
            project.Options.CutFiles);

    /// <summary>
    /// Where one game's file sits on a disc.
    /// </summary>
    /// <remarks>
    /// One game has the disc to itself and keeps its own shape. Several games
    /// go into numbered folders, because two games are perfectly entitled to
    /// both contain a <c>Data</c> folder and the second one must not land on
    /// top of the first.
    /// </remarks>
    private static string WhereItGoes(IReadOnlyList<GameFolder> games, GameFolder game, ListedFile file)
    {
        if (games.Count == 1)
        {
            return file.RelativePath;
        }

        int number = games.ToList().IndexOf(game) + 1;

        return System.IO.Path.Combine($"{number:00} - {Tidied(game.Name)}", file.RelativePath);
    }

    /// <summary>A name that is safe as a folder on a disc.</summary>
    private static string Tidied(string name)
    {
        string tidied = new([.. name.Select(character =>
            System.IO.Path.GetInvalidFileNameChars().Contains(character) ? ' ' : character)]);

        return tidied.Trim().Length == 0 ? "game" : tidied.Trim();
    }

    /// <summary>
    /// What the manual, the extras folder and the loose files come to.
    /// </summary>
    /// <remarks>
    /// Weighed apart from the game because they land on ONE disc of a set
    /// rather than on all of them, which is what lets the planner put them
    /// where the slack already is instead of pushing a file onto a disc that
    /// did not need to exist. Music is not here: it rides every disc, so it is
    /// in the overhead.
    /// </remarks>
    private long ExtrasBytes(DiscProject project)
    {
        DiscExtras? extras = project.Extras;

        if (extras is null)
        {
            return 0;
        }

        long bytes = 0;

        if (extras.ManualPath is not null)
        {
            bytes += SizeOf(extras.ManualPath);
        }

        if (extras.ExtrasFolderPath is not null)
        {
            bytes += SizeOf(extras.ExtrasFolderPath);
        }

        foreach (string item in extras.Loose)
        {
            bytes += SizeOf(item);
        }

        return bytes;
    }

    private long SizeOf(string path)
    {
        try
        {
            return files.FolderExists(path) ? files.List(path).Bytes : new System.IO.FileInfo(path).Length;
        }
        catch (System.IO.IOException)
        {
            return 0;
        }
    }
}

/// <summary>Everything a build produced.</summary>
public sealed record BuiltDiscs(IReadOnlyList<BuiltDisc> Discs);

/// <summary>One disc folder, ready to be turned into an image or opened by hand.</summary>
/// <param name="Label">What the disc is called.</param>
/// <param name="Number">From one.</param>
/// <param name="OfDiscs">How many discs this one belongs with. One when it stands alone.</param>
/// <param name="Folder">Where it is.</param>
/// <param name="Bytes">What the game's files on it come to, not counting the menu.</param>
public sealed record BuiltDisc(string Label, int Number, int OfDiscs, string Folder, long Bytes);
