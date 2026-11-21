using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Platform;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// What an earlier build left in the output folder that a new build does not
/// write over: found, counted in the question the window asks, and gone once
/// the new build is whole.
/// </summary>
/// <remarks>
/// <para>
/// Real folders and the real builder throughout, with the window's own
/// screen pressing Build. The only thing stood in is the image, which is a
/// file of a few bytes under the real name: writing a real one is the
/// image writer's business and takes a drive's worth of time.
/// </para>
/// <para>
/// It mattered the day several games came to share a set. Every project of
/// several games that was built a disc each the day before now builds as a
/// set, into the same folder, and without this the games' own folders would
/// sit beside it looking like part of what was made.
/// </para>
/// </remarks>
public sealed class WhatAnEarlierBuildLeftBehind : IDisposable
{
    private const long Overhead = 2L * 1024 * 1024;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "dwleft_" + Guid.NewGuid().ToString("N")[..8]);

    private readonly string _out;

    public WhatAnEarlierBuildLeftBehind()
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
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ================================================================ finding it

    [Fact]
    public void Finds_what_is_at_the_top_and_what_is_in_a_folder_for_each_game()
    {
        Stand("disc D1", "disc D2", "disc D3");
        Stand(@"One\disc", @"Two\disc D1", @"Two\disc D2", @"Gone\disc");
        Write("TWO D1.iso", "TWO D2.iso", "TWO D3.iso", "Something else.iso", "discproject.json");
        Write(@"One\One.iso", @"Two\Two D1.iso", @"Two\Two D2.iso", @"Two\notes.txt", @"Gone\Gone.iso");

        WhatAnEarlierBuildLeft there = WhatAnEarlierBuildLeft.In(new WindowsFileSystem(), _out, "TWO", ["One", "Two"]);

        Assert.True(there.IsThere);
        Assert.Equal(
            [At("disc D1"), At("disc D2"), At("disc D3"), At(@"One\disc"), At(@"Two\disc D1"), At(@"Two\disc D2")],
            there.Folders);
        Assert.Equal(
            [At("TWO D1.iso"), At("TWO D2.iso"), At("TWO D3.iso"), At(@"One\One.iso"), At(@"Two\Two D1.iso"), At(@"Two\Two D2.iso")],
            there.Images);

        // The folder of a game that is not on the window is not looked in.
        Assert.Equal([At("One"), At("Two")], there.OfOneGame);
        Assert.Equal("6 disc folders and 6 images", there.InWords);
    }

    [Fact]
    public void A_folder_with_nothing_of_a_build_in_it_holds_nothing()
    {
        Directory.CreateDirectory(At("One"));
        Write(@"One\readme.txt", "holiday.iso");

        WhatAnEarlierBuildLeft there = WhatAnEarlierBuildLeft.In(new WindowsFileSystem(), _out, "TWO", ["One", "Two"]);

        Assert.False(there.IsThere);
        Assert.Empty(there.OfOneGame);

        Assert.False(WhatAnEarlierBuildLeft.In(new WindowsFileSystem(), string.Empty, "TWO", ["One"]).IsThere);
        Assert.False(WhatAnEarlierBuildLeft.In(new WindowsFileSystem(), "   ", "TWO", ["One"]).IsThere);
        Assert.False(WhatAnEarlierBuildLeft.In(new WindowsFileSystem(), At("never made"), "TWO", ["One"]).IsThere);
    }

    [Fact]
    public void A_game_named_twice_or_with_no_name_is_looked_for_once_and_not_at_all()
    {
        // A game with no name is looked for where the build would put it,
        // which is never the folder itself: nothing at the top is twice.
        Stand("disc", @"One\disc");

        WhatAnEarlierBuildLeft there = WhatAnEarlierBuildLeft.In(new WindowsFileSystem(), _out, "TWO", ["One", "ONE", "", "One"]);

        Assert.Equal([At("disc"), At(@"One\disc")], there.Folders);
        Assert.Equal([At("One")], there.OfOneGame);
    }

    [Fact]
    public void What_a_build_wrote_over_is_not_what_it_left()
    {
        WhatAnEarlierBuildLeft before = new(
            [At("disc D1"), At("disc D2"), At("disc D3"), At(@"One\disc")],
            [At("TWO D1.iso"), At("TWO D2.iso"), At("TWO D3.iso"), At(@"One\One.iso")],
            [At("One")]);

        WhatAnEarlierBuildLeft left = before.NotAmong(
            [At("disc D1").ToUpperInvariant(), At("disc D2") + Path.DirectorySeparatorChar],
            [At("TWO D1.iso"), At("two d2.ISO")]);

        Assert.Equal([At("disc D3"), At(@"One\disc")], left.Folders);
        Assert.Equal([At("TWO D3.iso"), At(@"One\One.iso")], left.Images);
        Assert.Equal([At("One")], left.OfOneGame);
        Assert.True(left.IsThere);

        Assert.False(before.NotAmong(before.Folders, before.Images).IsThere);
    }

    // ================================================ a build made from the window

    [Fact]
    public async Task Games_built_a_disc_each_and_then_as_one_set_leave_no_folder_for_each_game_behind()
    {
        MakeADisc screen = TheScreen(1500, ("One", 1000), ("Two", 1000));
        screen.ADiscForEachGame = true;

        await screen.MakeIt();

        Assert.Equal(["One", "Two", "discproject.json"], Under(_out));
        Assert.Equal(["One.iso", "disc"], Under(At("One")));

        // Something of somebody's own, in a game's folder.
        File.WriteAllText(At(@"Two\my notes.txt"), "kept");

        screen.ADiscForEachGame = false;

        // Asked first, and what it counts is what will go.
        AQuestion asked = Assert.IsType<AQuestion>(screen.WhatBuildingHereReplaces());

        Assert.Equal("Build over the disc that is already in this folder?", asked.Headline);
        Assert.Contains("holds 2 disc folders and 2 images from an earlier build. Building here deletes them", asked.Because, StringComparison.Ordinal);

        await screen.MakeIt();

        Assert.Null(screen.ItWentWrong);
        Assert.Null(screen.AboutTheImage);

        // The set, and Two's folder only because of what was put in it.
        Assert.Equal(["TWO D1.iso", "TWO D2.iso", "Two", "disc D1", "disc D2", "discproject.json"], Under(_out));
        Assert.Equal(["my notes.txt"], Under(At("Two")));
        Assert.True(File.Exists(At(@"disc D1\01 - One\setup_one.exe")));
        Assert.True(File.Exists(At(@"disc D2\02 - Two\setup_two.exe")));

        // Nothing is left to ask about but what was just made.
        Assert.Contains("holds 2 disc folders and 2 images", screen.WhatBuildingHereReplaces()!.Because, StringComparison.Ordinal);
    }

    [Fact]
    public async Task And_the_other_way_round_the_set_goes_when_each_game_gets_a_disc()
    {
        MakeADisc screen = TheScreen(1500, ("One", 1000), ("Two", 1000));

        await screen.MakeIt();

        Assert.Equal(["TWO D1.iso", "TWO D2.iso", "disc D1", "disc D2", "discproject.json"], Under(_out));

        screen.ADiscForEachGame = true;

        // It said building here deletes them. It used not to: the new
        // discs went into folders of their own and the set stayed.
        Assert.Contains("Building here deletes them", screen.WhatBuildingHereReplaces()!.Because, StringComparison.Ordinal);

        await screen.MakeIt();

        Assert.Equal(["One", "Two", "discproject.json"], Under(_out));
        Assert.Equal(["Two.iso", "disc"], Under(At("Two")));
    }

    [Fact]
    public async Task A_set_that_comes_to_fewer_discs_does_not_keep_the_last_of_the_longer_one()
    {
        MakeADisc screen = TheScreen(1500, ("One", 1000), ("Two", 1000), ("Three", 1000));

        await screen.MakeIt();

        Assert.Equal(
            ["THREE D1.iso", "THREE D2.iso", "THREE D3.iso", "disc D1", "disc D2", "disc D3", "discproject.json"],
            Under(_out));

        // A disc that holds two of them.
        screen.Medium = ADisc(2500);
        await screen.MakeIt();

        Assert.Equal(["THREE D1.iso", "THREE D2.iso", "disc D1", "disc D2", "discproject.json"], Under(_out));

        // And one that holds them all: one disc, under the one-disc names.
        screen.Medium = ADisc(5000);
        await screen.MakeIt();

        Assert.Equal(["THREE.iso", "disc", "discproject.json"], Under(_out));

        // Back to a set, and the one disc goes.
        screen.Medium = ADisc(1500);
        await screen.MakeIt();

        Assert.Equal(
            ["THREE D1.iso", "THREE D2.iso", "THREE D3.iso", "disc D1", "disc D2", "disc D3", "discproject.json"],
            Under(_out));
    }

    [Fact]
    public async Task The_same_build_again_deletes_nothing_but_what_it_writes_over()
    {
        MakeADisc screen = TheScreen(1500, ("One", 1000), ("Two", 1000));
        ARecordingRemover remover = _remover!;

        await screen.MakeIt();
        File.WriteAllText(At("holiday.iso"), "not ours");
        Directory.CreateDirectory(At("disc backup"));

        remover.Removed.Clear();
        await screen.MakeIt();

        Assert.Empty(remover.Removed);
        Assert.Equal(
            ["TWO D1.iso", "TWO D2.iso", "disc D1", "disc D2", "disc backup", "discproject.json", "holiday.iso"],
            Under(_out));
    }

    [Fact]
    public async Task A_build_whose_image_fails_leaves_the_earlier_build_where_it_was()
    {
        MakeADisc screen = TheScreen(1500, ("One", 1000), ("Two", 1000));
        screen.ADiscForEachGame = true;
        await screen.MakeIt();

        _images!.Refuse = true;
        screen.ADiscForEachGame = false;
        await screen.MakeIt();

        Assert.NotNull(screen.ItWentWrong);

        // The earlier one is the only finished disc there is.
        Assert.True(Directory.Exists(At(@"One\disc")));
        Assert.True(File.Exists(At(@"One\One.iso")));
        Assert.True(File.Exists(At(@"Two\Two.iso")));
    }

    [Fact]
    public async Task A_build_that_is_stopped_leaves_the_earlier_build_where_it_was()
    {
        MakeADisc screen = TheScreen(1500, ("One", 1000), ("Two", 1000));
        screen.ADiscForEachGame = true;
        await screen.MakeIt();

        screen.ADiscForEachGame = false;
        _images!.StopWith = screen;
        await screen.MakeIt().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.NotNull(screen.HowTheStopWent);
        Assert.Null(screen.Made);
        Assert.True(Directory.Exists(At(@"One\disc")));
        Assert.True(File.Exists(At(@"One\One.iso")));
        Assert.True(Directory.Exists(At(@"Two\disc")));
    }

    [Fact]
    public async Task What_will_not_go_is_named_and_the_build_is_not_called_failed()
    {
        MakeADisc screen = TheScreen(1500, ("One", 1000), ("Two", 1000));
        screen.ADiscForEachGame = true;
        await screen.MakeIt();

        _remover!.HeldOpen.Add(At(@"One\disc"));
        screen.ADiscForEachGame = false;
        await screen.MakeIt();

        Assert.Null(screen.ItWentWrong);
        Assert.NotNull(screen.Made);
        Assert.Equal(
            $"An earlier build left {At(@"One\disc")}, which could not be deleted because something has it open. "
                + "Close whatever is using it and delete it by hand.",
            screen.AboutTheImage);

        // Everything else of it went.
        Assert.True(Directory.Exists(At(@"One\disc")));
        Assert.False(File.Exists(At(@"One\One.iso")));
        Assert.False(Directory.Exists(At("Two")));

        // Two of them held, and it says how many and names the first.
        _remover.HeldOpen.Clear();
        screen.ADiscForEachGame = true;
        await screen.MakeIt();

        _remover.HeldOpen.Add(At("disc D1"));
        _remover.HeldOpen.Add(At("TWO D2.iso"));
        screen.ADiscForEachGame = false;
        await screen.MakeIt();
        screen.ADiscForEachGame = true;
        await screen.MakeIt();

        Assert.Equal(
            $"An earlier build left 2 things that could not be deleted because something has them open, {At("TWO D2.iso")} "
                + "among them. Close whatever is using them and delete them by hand.",
            screen.AboutTheImage);
    }

    [Fact]
    public async Task With_only_the_image_kept_the_earlier_builds_folders_and_images_still_go()
    {
        MakeADisc screen = TheScreen(1500, ("One", 1000), ("Two", 1000));
        screen.ADiscForEachGame = true;
        await screen.MakeIt();

        screen.ADiscForEachGame = false;
        screen.OnlyTheImage = true;
        await screen.MakeIt();

        Assert.Null(screen.ItWentWrong);
        Assert.Equal(["TWO D1.iso", "TWO D2.iso", "discproject.json"], Under(_out));
    }

    [Fact]
    public async Task With_nothing_that_can_delete_nothing_is_deleted_and_nothing_is_wrong()
    {
        MakeADisc screen = TheScreen(1500, withARemover: false, ("One", 1000), ("Two", 1000));
        screen.ADiscForEachGame = true;
        await screen.MakeIt();

        screen.ADiscForEachGame = false;
        await screen.MakeIt();

        Assert.Null(screen.ItWentWrong);
        Assert.Null(screen.AboutTheImage);
        Assert.True(Directory.Exists(At(@"One\disc")));
        Assert.True(Directory.Exists(At("disc D1")));
    }

    [Fact]
    public async Task While_it_deletes_the_window_says_so()
    {
        MakeADisc screen = TheScreen(1500, ("One", 1000), ("Two", 1000));
        screen.ADiscForEachGame = true;
        await screen.MakeIt();

        List<string> said = [];
        screen.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(MakeADisc.Doing))
            {
                lock (said)
                {
                    said.Add(screen.Doing);
                }
            }
        };

        // Each image waits until the window has said the line about it.
        _images!.Heard = disc =>
        {
            lock (said)
            {
                return said.Any(line => line.StartsWith($"TWO, disc {disc} of 2: writing the image, ", StringComparison.Ordinal));
            }
        };

        screen.ADiscForEachGame = false;
        await screen.MakeIt();
        _images.Heard = null;

        Assert.Contains("Deleting what the earlier build left", said);

        // And while each image is written, whose it is and which disc of
        // the set: the window passes on what is said and makes up nothing.
        Assert.Contains(said, line => line.StartsWith("TWO, disc 1 of 2: writing the image, ", StringComparison.Ordinal));
        Assert.Contains(said, line => line.StartsWith("TWO, disc 2 of 2: writing the image, ", StringComparison.Ordinal));

        // Not when there is nothing of an earlier build to delete.
        said.Clear();
        await screen.MakeIt();

        Assert.DoesNotContain("Deleting what the earlier build left", said);
    }

    // ==================================================================== helpers

    private ARecordingRemover? _remover;
    private ImagesThatAreFiles? _images;

    private static Medium ADisc(long room) => new("TEST", "a test disc", Overhead + room);

    private string At(string under) => Path.Combine(_out, under);

    private void Stand(params string[] folders)
    {
        foreach (string folder in folders)
        {
            Directory.CreateDirectory(At(folder));
        }
    }

    private void Write(params string[] files)
    {
        foreach (string file in files)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(At(file))!);
            File.WriteAllText(At(file), "x");
        }
    }

    private static string[] Under(string folder) =>
        [.. Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    private MakeADisc TheScreen(long room, params (string Name, int Bytes)[] games) => TheScreen(room, true, games);

    /// <summary>
    /// The window's screen over the real disk, the real builder and the real
    /// deleting, with those games as real files, named for how many there are.
    /// </summary>
    private MakeADisc TheScreen(long room, bool withARemover, params (string Name, int Bytes)[] games)
    {
        _remover = new ARecordingRemover(new WindowsStageWriter());
        _images = new ImagesThatAreFiles();

        MakeADisc screen = TheMainWindowWithoutAWindow.New(
            builder: new DiscBuilder(new WindowsFileSystem(), new WindowsStageWriter(), new WindowsImages(), new Sha256Hasher()),
            images: _images,
            disk: new WindowsFileSystem(),
            remover: withARemover ? _remover : null);

        screen.OutputFolder = _out;
        screen.Medium = ADisc(room);

        foreach ((string name, int bytes) in games)
        {
            string tidied = name.ToLowerInvariant();
            string folder = Path.Combine(_root, "src", name);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, $"setup_{tidied}.exe"), new byte[bytes]);

            ListedFile installer = new($"setup_{tidied}.exe", bytes);

            screen.Add(new GameFolder(folder, GameKind.GogInstaller, name, name, installer, [installer], [], null));
        }

        screen.Label = games.Length == 2 ? "TWO" : "THREE";

        return screen;
    }

    /// <summary>An image that is a small file under its real name, or a refusal, or a stop.</summary>
    private sealed class ImagesThatAreFiles : IMakeImages
    {
        internal bool Refuse { get; set; }

        internal MakeADisc? StopWith { get; set; }

        /// <summary>
        /// Whether the window has said what this reported. A report crosses
        /// to the window by way of the thread pool, and an image that is
        /// made at once can be over before it arrives, when it is rightly
        /// ignored. A test that listens for the line waits for it here.
        /// </summary>
        internal Func<int, bool>? Heard { get; set; }

        public async Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct)
        {
            if (StopWith is MakeADisc screen)
            {
                StopWith = null;
                screen.StopTheBuild();
                ct.ThrowIfCancellationRequested();
            }

            if (Refuse)
            {
                return Refusals.TheOldImageWillNotMoveAside(imagePath, "it is mounted");
            }

            progress?.Report(new ImageProgress(1, 2));

            for (int tries = 0; Heard is not null && !Heard(discNumber) && tries < 500; tries++)
            {
                await Task.Delay(10, CancellationToken.None);
            }

            File.WriteAllText(imagePath, "an image");

            return new MadeImage(imagePath, 8, null);
        }
    }

    /// <summary>The real deleting, written down, and refusing what a test says is held open.</summary>
    private sealed class ARecordingRemover(IRemoveADiscFolder real) : IRemoveADiscFolder
    {
        internal List<string> Removed { get; } = [];

        internal HashSet<string> HeldOpen { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Remove(string discFolder)
        {
            if (HeldOpen.Contains(discFolder))
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            Removed.Add(discFolder);
            real.Remove(discFolder);
        }

        public void RemoveIfEmpty(string folder) => real.RemoveIfEmpty(folder);

        public void RemoveTheImage(string imagePath)
        {
            if (HeldOpen.Contains(imagePath))
            {
                throw new IOException("The process cannot access the file because it is being used by another process.");
            }

            Removed.Add(imagePath);
            real.RemoveTheImage(imagePath);
        }
    }
}
