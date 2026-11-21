using DiscWright.Core;
using DiscWright.Imaging;
using DiscWright.Platform;
using DiscWright.Staging;

namespace DiscWright.Tests;

/// <summary>
/// A build that is stopped takes away what it had started, and nothing
/// else, on real folders.
/// </summary>
/// <remarks>
/// <para>
/// The owner, 21 November 2026, forty minutes into a build with a game of
/// 150 GB in it: should there be a way to stop it, "and everything that was
/// built is deleted". Deleting is the dangerous half, so it is tested
/// against folders that really exist, with things in them that are not this
/// build's.
/// </para>
/// <para>
/// The same night that build ran out of room and left 39 GB of half an
/// image on a full drive, called by an image's name. An image that is not
/// finished is deleted now, whatever stopped it.
/// </para>
/// </remarks>
public sealed class StoppingABuild : IDisposable
{
    private const long Overhead = 2L * 1024 * 1024;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dwstop_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _out;

    public StoppingABuild()
    {
        _out = Path.Combine(_root, "out");
        Directory.CreateDirectory(_out);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // --------------------------------------------------- the build, stopped

    [Fact]
    public async Task Stopped_part_way_it_takes_its_disc_folder_away_and_writes_no_project_file()
    {
        File.WriteAllText(Path.Combine(_out, "my notes.txt"), "not the build's");
        AWriterThatStopsTheBuild writer = new(stopAtCopy: 2);

        ABuildThatWasStopped stopped = await Assert.ThrowsAsync<ABuildThatWasStopped>(() => Builder(writer).Build(
            Project(AGame("Hollow Knight", parts: 3)), ADisc(), _out, null, writer.Stop.Token));

        Assert.Empty(stopped.LeftBehind);

        // It had begun: the second file was being copied when it stopped.
        Assert.Equal(2, writer.Copies);
        Assert.False(Directory.Exists(Path.Combine(_out, "disc")));
        Assert.False(File.Exists(Path.Combine(_out, TheProjectAsJson.FileName)));

        // And what was in the folder before it is as it was.
        Assert.Equal(["my notes.txt"], Directory.GetFileSystemEntries(_out).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Still_a_cancellation_to_anything_that_only_knows_those()
    {
        AWriterThatStopsTheBuild writer = new(stopAtCopy: 1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Builder(writer).Build(
            Project(AGame("Hollow Knight")), ADisc(), _out, null, writer.Stop.Token));
    }

    [Fact]
    public async Task Stopped_before_it_began_it_touches_nothing_not_even_an_earlier_build()
    {
        // The earlier build is in the very folder this one would empty.
        // Nothing has been emptied yet, so nothing is this build's to take.
        string earlier = Path.Combine(_out, "disc");
        Directory.CreateDirectory(earlier);
        File.WriteAllText(Path.Combine(earlier, "setup_earlier.exe"), "an earlier build");

        using CancellationTokenSource already = new();
        await already.CancelAsync();

        ABuildThatWasStopped stopped = await Assert.ThrowsAsync<ABuildThatWasStopped>(() => Builder(new WindowsStageWriter()).Build(
            Project(AGame("Hollow Knight")), ADisc(), _out, null, already.Token));

        Assert.Empty(stopped.LeftBehind);
        Assert.Equal("an earlier build", File.ReadAllText(Path.Combine(earlier, "setup_earlier.exe")));
    }

    [Fact]
    public async Task With_a_disc_each_it_takes_what_it_began_and_leaves_an_earlier_build_it_never_reached()
    {
        // Three games that will not share a disc: a folder each. Stopped in
        // the second. The third's folder holds a whole earlier build, which
        // this one had not come to and must not touch.
        GameFolder[] games = [AGame("Alpha"), AGame("Beta"), AGame("Gamma"), AGame("Delta")];

        // And a fourth, never reached either, whose folder is there and
        // empty. Empty or not, this build did not make it and has not come
        // to it, so it is not this build's to take away.
        Directory.CreateDirectory(Path.Combine(_out, "Delta"));

        string gamma = Path.Combine(_out, "Gamma");
        Directory.CreateDirectory(Path.Combine(gamma, "disc"));
        File.WriteAllText(Path.Combine(gamma, "disc", "setup_gamma.exe"), "an earlier build");
        File.WriteAllText(Path.Combine(gamma, "Gamma.iso"), "its image");

        // Something of somebody's own in the first game's folder.
        Directory.CreateDirectory(Path.Combine(_out, "Alpha"));
        File.WriteAllText(Path.Combine(_out, "Alpha", "cover idea.txt"), "mine");

        // One copy for each game's installer: stopped on the second game's.
        AWriterThatStopsTheBuild writer = new(stopAtCopy: 2);

        ABuildThatWasStopped stopped = await Assert.ThrowsAsync<ABuildThatWasStopped>(() => Builder(writer).Build(
            new DiscProject("THREE", games, new DiscOptions()), ADisc(holding: 1500), _out, null, writer.Stop.Token));

        Assert.Empty(stopped.LeftBehind);

        // The first was finished and the second begun: both are gone.
        Assert.False(Directory.Exists(Path.Combine(_out, "Alpha", "disc")));
        Assert.False(Directory.Exists(Path.Combine(_out, "Beta", "disc")));

        // The second's own folder was made by this build and is empty, so
        // it goes. The first's holds something else, so it stays, with it.
        Assert.False(Directory.Exists(Path.Combine(_out, "Beta")));
        Assert.Equal(["cover idea.txt"], Directory.GetFileSystemEntries(Path.Combine(_out, "Alpha")).Select(Path.GetFileName));

        // And the earlier build of the third is exactly as it was.
        Assert.Equal("an earlier build", File.ReadAllText(Path.Combine(gamma, "disc", "setup_gamma.exe")));
        Assert.Equal("its image", File.ReadAllText(Path.Combine(gamma, "Gamma.iso")));

        // So is the empty folder of the fourth.
        Assert.True(Directory.Exists(Path.Combine(_out, "Delta")));
    }

    [Fact]
    public async Task What_it_cannot_delete_it_names()
    {
        AWriterThatStopsTheBuild writer = new(stopAtCopy: 1) { CannotRemove = "disc" };

        ABuildThatWasStopped stopped = await Assert.ThrowsAsync<ABuildThatWasStopped>(() => Builder(writer).Build(
            Project(AGame("Hollow Knight")), ADisc(), _out, null, writer.Stop.Token));

        Assert.Equal([Path.Combine(_out, "disc")], stopped.LeftBehind);
    }

    [Fact]
    public async Task The_build_after_a_stopped_one_is_not_stopped_by_the_same_press()
    {
        // One writer for the life of the program, as the program has.
        AWriterThatStopsTheBuild writer = new(stopAtCopy: 1);
        DiscBuilder builder = Builder(writer);
        DiscProject project = Project(AGame("Hollow Knight", parts: 2));

        await Assert.ThrowsAsync<ABuildThatWasStopped>(() => builder.Build(project, ADisc(), _out, null, writer.Stop.Token));

        Outcome<BuiltDiscs> again = await builder.Build(project, ADisc(), _out, null, CancellationToken.None);

        BuiltDisc disc = Assert.Single(Assert.IsType<Outcome<BuiltDiscs>.Ok>(again).Value.Discs);

        Assert.True(File.Exists(Path.Combine(disc.Folder, "setup_hollow_knight.exe")));
        Assert.True(File.Exists(Path.Combine(disc.Folder, "setup_hollow_knight-2.bin")));
        Assert.True(File.Exists(Path.Combine(_out, TheProjectAsJson.FileName)));
    }

    [Fact]
    public async Task A_build_nobody_stops_is_told_nothing_to_watch_when_it_ends()
    {
        AWriterThatStopsTheBuild writer = new(stopAtCopy: 0);

        await Builder(writer).Build(Project(AGame("Hollow Knight")), ADisc(), _out, null, CancellationToken.None);

        // Told at the start what to watch, and at the end to stop watching.
        Assert.Equal(2, writer.ToldToWatch.Count);
        Assert.False(writer.ToldToWatch[^1].CanBeCanceled);
    }

    // ------------------------------------------------ one file, given up

    [Fact]
    public void A_copy_is_the_copy_it_always_was()
    {
        string from = AFile("from.bin", 300_000);
        string to = Path.Combine(_root, "to.bin");
        DateTime written = new(2019, 3, 4, 5, 6, 8, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(from, written);
        File.WriteAllText(to, "something already there, and longer than nothing");

        new WindowsStageWriter().Copy(from, to);

        Assert.Equal(File.ReadAllBytes(from), File.ReadAllBytes(to));
        Assert.Equal(written, File.GetLastWriteTimeUtc(to));
    }

    [Fact]
    public void A_file_that_is_not_there_is_the_exception_it_always_was()
    {
        WindowsStageWriter writer = new();

        Assert.Throws<FileNotFoundException>(() => writer.Copy(Path.Combine(_root, "nowhere.bin"), Path.Combine(_root, "to.bin")));
        Assert.Throws<DirectoryNotFoundException>(() => writer.Copy(AFile("from.bin", 10), Path.Combine(_root, "no such folder", "to.bin")));
    }

    [Fact]
    public void A_copy_goes_to_a_path_longer_than_Windows_likes()
    {
        // 260 characters is where a call made straight to Windows stops,
        // unless it is asked the long way. The framework does that for
        // every path it is handed, and this is not handed to it any more.
        string deep = _root;

        while (deep.Length < 300)
        {
            deep = Path.Combine(deep, "a folder with a long name in it");
        }

        Directory.CreateDirectory(deep);
        string from = AFile("from.bin", 5000);
        string to = Path.Combine(deep, "to.bin");

        Assert.True(to.Length > 300);

        new WindowsStageWriter().Copy(from, to);
        Assert.Equal(File.ReadAllBytes(from), File.ReadAllBytes(to));

        // And from one.
        string back = Path.Combine(_root, "back.bin");
        new WindowsStageWriter().Copy(to, back);
        Assert.Equal(File.ReadAllBytes(from), File.ReadAllBytes(back));
    }

    [Fact]
    public void A_copy_that_is_already_stopped_writes_nothing()
    {
        string from = AFile("from.bin", 5000);
        string to = Path.Combine(_root, "to.bin");
        using CancellationTokenSource stop = new();
        stop.Cancel();

        WindowsStageWriter writer = new();
        writer.StopWhen(stop.Token);

        Assert.ThrowsAny<OperationCanceledException>(() => writer.Copy(from, to));
        Assert.False(File.Exists(to));

        // Told to watch nothing, it copies again.
        writer.StopWhen(CancellationToken.None);
        writer.Copy(from, to);

        Assert.True(File.Exists(to));
    }

    [Fact]
    public void A_copy_is_given_up_part_way_and_leaves_no_part_of_the_file()
    {
        // Big enough that it cannot be over before the stop: a quarter of a
        // gigabyte, stopped a moment after it starts. Windows takes away
        // what it had written of the file itself.
        string from = Path.Combine(_root, "big.bin");

        using (FileStream big = new(from, FileMode.Create))
        {
            byte[] bite = new byte[1024 * 1024];
            Random.Shared.NextBytes(bite);

            for (int at = 0; at < 256; at++)
            {
                big.Write(bite);
            }
        }

        string to = Path.Combine(_root, "big copy.bin");
        using CancellationTokenSource stop = new();
        WindowsStageWriter writer = new();
        writer.StopWhen(stop.Token);

        System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
        stop.CancelAfter(TimeSpan.FromMilliseconds(15));

        // On a disk fast enough to finish in 15 milliseconds there is
        // nothing to give up, and the test has nothing to say.
        try
        {
            writer.Copy(from, to);
        }
        catch (OperationCanceledException)
        {
            Assert.False(File.Exists(to));
            Assert.True(took.Elapsed < TimeSpan.FromSeconds(20), $"It took {took.Elapsed} to hear the stop.");

            return;
        }

        Assert.Equal(new FileInfo(from).Length, new FileInfo(to).Length);
    }

    [Fact]
    public void A_piece_of_a_file_is_given_up_between_one_bite_and_the_next()
    {
        string from = AFile("from.bin", 3 * 1024 * 1024);
        using CancellationTokenSource stop = new();
        stop.Cancel();

        WindowsStageWriter writer = new();
        writer.StopWhen(stop.Token);

        Assert.ThrowsAny<OperationCanceledException>(() =>
            writer.CopyPart(from, 0, 2 * 1024 * 1024, Path.Combine(_root, "piece.bin")));
    }

    // ------------------------------------------------- what may be deleted

    [Fact]
    public void A_folder_goes_only_when_nothing_is_left_in_it()
    {
        WindowsStageWriter writer = new();
        string empty = Path.Combine(_root, "empty");
        string kept = Path.Combine(_root, "kept");
        Directory.CreateDirectory(empty);
        Directory.CreateDirectory(Path.Combine(kept, "a folder with nothing in it"));

        writer.RemoveIfEmpty(empty);
        writer.RemoveIfEmpty(kept);
        writer.RemoveIfEmpty(Path.Combine(_root, "never was"));

        Assert.False(Directory.Exists(empty));

        // A folder inside it is something in it, empty or not.
        Assert.True(Directory.Exists(Path.Combine(kept, "a folder with nothing in it")));
    }

    [Fact]
    public void Only_a_file_called_an_image_is_deleted_as_one()
    {
        WindowsStageWriter writer = new();
        string image = AFile("HOLLOW KNIGHT.ISO", 100);
        string other = AFile("setup_hollow_knight.exe", 100);

        writer.RemoveTheImage(image);
        writer.RemoveTheImage(Path.Combine(_root, "never was.iso"));

        Assert.False(File.Exists(image));
        Assert.Throws<ArgumentException>(() => writer.RemoveTheImage(other));
        Assert.Throws<ArgumentException>(() => writer.RemoveTheImage(Path.Combine(_root, "an image.iso.txt")));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void Half_an_image_is_taken_away_and_it_never_throws()
    {
        string half = AFile("Metro Exodus.iso", 4000);

        NotHalfAnImage.Leave(half);
        Assert.False(File.Exists(half));

        // Not there: nothing. Held open by something: it stays, quietly,
        // because this is called on the way out of what already went wrong.
        NotHalfAnImage.Leave(half);

        string held = AFile("held.iso", 4000);

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            NotHalfAnImage.Leave(held);
        }

        Assert.True(File.Exists(held));
    }

    [Fact]
    public void An_image_that_could_not_be_written_says_what_became_of_the_part_that_was()
    {
        Refusal refusal = Refusals.TheImageCouldNotBeWritten(@"C:\out\Metro Exodus.iso", "There is not enough space on the disk.");

        Assert.Equal("The image could not be written", refusal.Headline);
        Assert.Contains(@"C:\out\Metro Exodus.iso", refusal.Because, StringComparison.Ordinal);
        Assert.Contains("ran out of room", refusal.Because, StringComparison.Ordinal);
        Assert.Contains("twice the size of the disc free", refusal.Because, StringComparison.Ordinal);
        Assert.Contains("The part that was written has been deleted.", refusal.Because, StringComparison.Ordinal);
        Assert.Contains("The disc folder is whole.", refusal.Because, StringComparison.Ordinal);
        Assert.EndsWith("Windows said: There is not enough space on the disk.", refusal.Because, StringComparison.Ordinal);
    }

    [Fact]
    public void The_image_writer_takes_half_an_image_away_whatever_stopped_it()
    {
        // The writer is Windows' own and cannot be made to fail to order,
        // so this reads that every way out of it that is not success goes
        // by the one thing that deletes what was written.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.Imaging", "WindowsImageMaker.cs"));
        string making = code[code.IndexOf("Copy.StreamToFile(", StringComparison.Ordinal)..code.IndexOf("finally", StringComparison.Ordinal)];

        foreach (string caught in new[] { "catch (COMException wrong)", "catch (OperationCanceledException)", "catch (System.IO.IOException wrong)" })
        {
            int at = making.IndexOf(caught, StringComparison.Ordinal);

            Assert.True(at >= 0, caught + " is not there.");
            Assert.StartsWith(
                "NotHalfAnImage.Leave(imagePath);",
                making[(at + caught.Length)..].TrimStart().TrimStart('{').TrimStart().Split((char)10)
                    .First(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)).Trim(),
                StringComparison.Ordinal);
        }

        Assert.Contains("return Refusals.TheImageCouldNotBeWritten(imagePath, wrong.Message);", making, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- helpers

    private static DiscBuilder Builder(IStageWriter writer) =>
        new(new WindowsFileSystem(), writer, new WindowsImages(), new Sha256Hasher());

    private static Medium ADisc(long holding = 1_000_000) => new("TEST", "a test disc", Overhead + holding);

    private static DiscProject Project(params GameFolder[] games) => new("HOLLOW KNIGHT", games, new DiscOptions());

    private string AFile(string name, int bytes)
    {
        string path = Path.Combine(_root, name);
        byte[] made = new byte[bytes];
        Random.Shared.NextBytes(made);
        File.WriteAllBytes(path, made);

        return path;
    }

    /// <summary>A GOG download on the real disk: an installer of 1000 bytes and its parts.</summary>
    private GameFolder AGame(string name, int parts = 0)
    {
        string tidied = name.ToLowerInvariant().Replace(' ', '_');
        string folder = Path.Combine(_root, "src", tidied);
        Directory.CreateDirectory(folder);

        List<ListedFile> listed = [];

        foreach (string file in new[] { $"setup_{tidied}.exe" }.Concat(Enumerable.Range(1, parts).Select(part => $"setup_{tidied}-{part}.bin")))
        {
            File.WriteAllBytes(Path.Combine(folder, file), new byte[1000]);
            listed.Add(new ListedFile(file, 1000));
        }

        return new GameFolder(folder, GameKind.GogInstaller, name, name, listed[0], listed, [], null);
    }

    /// <summary>
    /// The real writer, which stops the build itself when it is asked for a
    /// chosen copy, so a build is stopped at a known place and not at
    /// whatever a timer happened to catch.
    /// </summary>
    private sealed class AWriterThatStopsTheBuild(int stopAtCopy) : IStageWriter, IRemoveADiscFolder, IListenForAStop
    {
        private readonly WindowsStageWriter _real = new();

        internal CancellationTokenSource Stop { get; } = new();

        internal int Copies { get; private set; }

        internal List<CancellationToken> ToldToWatch { get; } = [];

        /// <summary>The name of a folder that will not be deleted, as if it were open somewhere.</summary>
        internal string? CannotRemove { get; init; }

        public void StopWhen(CancellationToken stopped)
        {
            ToldToWatch.Add(stopped);
            _real.StopWhen(stopped);
        }

        public void Copy(string source, string destination)
        {
            // Only a game's own files are counted: the menu's are written,
            // not copied, and the count is of what somebody would see.
            if (Path.GetFileName(source).StartsWith("setup_", StringComparison.Ordinal) && ++Copies == stopAtCopy)
            {
                Stop.Cancel();
            }

            _real.Copy(source, destination);
        }

        public void CopyPart(string source, long offset, long bytes, string destination) =>
            _real.CopyPart(source, offset, bytes, destination);

        public void MakeFolder(string path) => _real.MakeFolder(path);

        public void EmptyTheFolder(string path) => _real.EmptyTheFolder(path);

        public void WriteText(string path, string text) => _real.WriteText(path, text);

        public void WriteBytes(string path, byte[] bytes) => _real.WriteBytes(path, bytes);

        public void WriteAutorunText(string path, string text) => _real.WriteAutorunText(path, text);

        public void Remove(string discFolder)
        {
            if (CannotRemove is not null && Path.GetFileName(discFolder) == CannotRemove)
            {
                throw new TheFolderIsInUse(discFolder, "The process cannot access the file.", new IOException());
            }

            _real.Remove(discFolder);
        }

        public void RemoveIfEmpty(string folder) => _real.RemoveIfEmpty(folder);

        public void RemoveTheImage(string imagePath) => _real.RemoveTheImage(imagePath);
    }
}
