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
public sealed class DiscBuilder(
    IFileSystem files,
    IStageWriter writer,
    IImages images,
    IHasher hasher,
    string madeBy = "",
    Func<DateTimeOffset>? now = null)
    : IBuildDiscs
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
    private IReadOnlyList<IBuildStep> StepsFor() =>
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
        // What to make is not decided here. The window has to answer the same
        // question before anybody presses anything, so it is answered in one
        // place and this builds whatever that says.
        Outcome<PlannedBuild> planned = WhatWouldBeMade.For(project, medium, SizeOf);

        if (planned is Outcome<PlannedBuild>.No refused)
        {
            return refused.Refusal;
        }

        PlannedBuild build = ((Outcome<PlannedBuild>.Ok)planned).Value;
        List<BuiltDisc> made = [];

        foreach (PlannedOutput output in build.Outputs)
        {
            ct.ThrowIfCancellationRequested();

            string where = output.InItsOwnFolder
                ? System.IO.Path.Combine(outputFolder, OnTheDisc.Tidied(output.Label))
                : outputFolder;

            // Narrowed with its add-ons, not only its games. Cut down to the
            // games alone, every disc of a disc-each build offered the first
            // game's add-ons on its menu and none of its own.
            Outcome<BuiltDiscs> one = await Make(
                project.KeepingOnly(output.Games, output.Label), output.Layout, medium, where, progress, ct);

            if (one is Outcome<BuiltDiscs>.No stopped)
            {
                return stopped.Refusal;
            }

            made.AddRange(((Outcome<BuiltDiscs>.Ok)one).Value.Discs);
        }

        WriteTheProjectFile(project, medium, outputFolder);

        return new BuiltDiscs(made);
    }

    /// <summary>
    /// Writes <c>discproject.json</c> beside the discs, so what was built can
    /// be opened again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Once per build and for the whole project, at the top of the output
    /// folder, which is where v1 put it and therefore where "Open one you
    /// built" looks. It was a step in each disc's pipeline, so a build of
    /// several games that each got a disc wrote one into every game's own
    /// folder, each describing that one game, and nothing at the top: the
    /// compilation could not be opened again, and each of those files carried
    /// every add-on pointed at the wrong game.
    /// </para>
    /// <para>
    /// Beside the discs and not on one. A disc is a copy of a game; the
    /// project is the recipe, and a recipe on the disc is a file on every copy
    /// nobody asked for. After the discs, because nothing written before this
    /// point can be opened again without it, and a build that stops part way
    /// leaves no recipe for discs that were never made.
    /// </para>
    /// </remarks>
    private void WriteTheProjectFile(DiscProject project, Medium medium, string outputFolder)
    {
        if (outputFolder.Length == 0)
        {
            return;
        }

        writer.WriteText(
            System.IO.Path.Combine(outputFolder, TheProjectAsJson.FileName),
            TheProjectAsJson.Of(project, madeBy, medium.Key, outputFolder, (now ?? (() => DateTimeOffset.UtcNow))()));
    }

    private async Task<Outcome<BuiltDiscs>> Make(
        DiscProject forThisDisc,
        DiscLayout layout,
        Medium medium,
        string outputFolder,
        IProgress<BuildProgress>? progress,
        CancellationToken ct)
    {
        string label = forThisDisc.Label;
        BuildPipeline pipeline = new(StepsFor());
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

        WriteTheSetFile(forThisDisc, made, medium, ct);

        return new BuiltDiscs(made);
    }

    /// <summary>
    /// Writes the set file to every disc of a set, after all of them are made.
    /// </summary>
    /// <remarks>
    /// <para>
    /// After, and not as a step, because it describes every file on EVERY disc
    /// and so it cannot be written until every disc has been hashed. It costs
    /// nothing to do it this way: the hashes are read back out of the checksum
    /// lists that were just written, so nothing is hashed twice.
    /// </para>
    /// <para>
    /// The same file on every disc, so any one disc can answer every question
    /// about the set. Somebody who finds disc 2 in a drawer gets a disc that
    /// knows what it belongs to.
    /// </para>
    /// </remarks>
    private void WriteTheSetFile(
        DiscProject project, List<BuiltDisc> made, Medium medium, CancellationToken ct)
    {
        if (made.Count <= 1)
        {
            // A disc on its own is not a set, and telling somebody that one
            // disc of one is all there is would be noise on every disc that
            // never needed it.
            return;
        }

        List<SetEntry> entries = [];

        foreach (BuiltDisc disc in made)
        {
            ct.ThrowIfCancellationRequested();

            string listed = System.IO.Path.Combine(disc.Folder, ChecksumList.FileName);

            if (!System.IO.File.Exists(listed))
            {
                // No checksum list means nothing to describe the set with, and
                // a set file with no files in it is worse than none.
                return;
            }

            foreach (ReadEntry entry in ChecksumList.Read(System.IO.File.ReadAllText(listed)))
            {
                entries.Add(new SetEntry(
                    disc.Number,
                    entry.RelativePath,
                    entry.Sha256,
                    SizeOf(System.IO.Path.Combine(disc.Folder, WithBackslashes(entry.RelativePath)))));
            }
        }

        foreach (BuiltDisc disc in made)
        {
            writer.WriteText(
                System.IO.Path.Combine(disc.Folder, SetManifest.FileName),
                SetManifest.For(project.Label, entries, disc.Number, made.Count, medium.Name, DateTimeOffset.Now));
        }
    }

    /// <summary>
    /// A path from the checksum list, which writes forward slashes, back as
    /// Windows wants it in order to open the file again.
    /// </summary>
    private static string WithBackslashes(string path) =>
        path.Replace('/', '\\');

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
