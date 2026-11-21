using DiscWright.Core;
using DiscWright.Platform;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Whether the drive has room for a build, asked before the build starts.
/// </summary>
/// <remarks>
/// The owner's own build, 21 November 2026: four games, 153 GB written over
/// forty minutes onto a drive that could not take the last 37. A build
/// writes every disc twice, the folder and an image as big again, and
/// nothing had looked.
/// </remarks>
public class ThereIsRoomForABuild
{
    private const string Out = @"C:\out";

    // ------------------------------------------------------------- the sum

    [Fact]
    public void A_build_needs_every_disc_twice_over_the_folder_and_the_image()
    {
        PlannedBuild build = new(
        [
            ADiscOf("ALPHA", own: false, 100, 200),
            ADiscOf("BETA", own: false, 50),
        ]);

        Assert.Equal(700, RoomForABuild.NeededBy(build));
        Assert.Equal(0, RoomForABuild.NeededBy(new PlannedBuild([])));
    }

    [Fact]
    public void A_drive_that_will_not_say_how_much_room_it_has_gives_no_figures_at_all()
    {
        PlannedBuild build = new([ADiscOf("ALPHA", own: false, 100)]);

        Assert.Null(RoomForABuild.For(build, new ADrive(free: null), Out));
        Assert.Null(RoomForABuild.For(build, new ADrive(free: 5), "  "));
        Assert.NotNull(RoomForABuild.For(build, new ADrive(free: 5), Out));
    }

    [Theory]
    [InlineData(200, true, 0)]
    [InlineData(201, true, 0)]
    [InlineData(199, false, 1)]
    [InlineData(0, false, 200)]
    public void There_is_enough_when_what_is_free_covers_it_and_not_a_byte_before(long free, bool enough, long shortBy)
    {
        RoomForABuild room = RoomForABuild.For(new PlannedBuild([ADiscOf("ALPHA", own: false, 100)]), new ADrive(free), Out)!;

        Assert.Equal(200, room.Needed);
        Assert.Equal(free, room.Free);
        Assert.Equal(0, room.ComesBack);
        Assert.Equal(enough, room.IsEnough);
        Assert.Equal(shortBy, room.TooLittleBy);
    }

    [Fact]
    public void What_an_earlier_build_left_where_this_one_goes_comes_back()
    {
        // A set of two, built here before: two disc folders and two images.
        // Beside them, things that are not this build's and do not go: a
        // file of somebody's, another disc's image, and a folder.
        ADrive drive = new(free: 100);
        drive.Has(Out,
            new ListedFile("HOLLOW KNIGHT D1.iso", 300), new ListedFile("HOLLOW KNIGHT D2.iso", 200),
            new ListedFile("notes.txt", 9000), new ListedFile("ANOTHER DISC.iso", 7000));
        drive.Has(Out + @"\disc D1", new ListedFile("setup.exe", 250), new ListedFile(@"AUTORUN\menu.hta", 50));
        drive.Has(Out + @"\disc D2", new ListedFile("setup-1.bin", 200));
        drive.Has(Out + @"\holiday photos", new ListedFile("beach.jpg", 5000));

        RoomForABuild room = RoomForABuild.For(
            new PlannedBuild([ADiscOf("HOLLOW KNIGHT", own: false, 300, 200)]), drive, Out)!;

        Assert.Equal(300 + 200 + 300 + 200, room.ComesBack);
        Assert.Equal(100 + 1000, room.ThereIs);
        Assert.Equal(1000, room.Needed);
        Assert.True(room.IsEnough);
    }

    [Fact]
    public void With_a_disc_each_it_is_looked_for_in_each_games_own_folder_and_nowhere_else()
    {
        ADrive drive = new(free: 0);

        // Alpha was built before, in its own folder. Beta was not.
        drive.Has(Out + @"\Alpha", new ListedFile("Alpha.iso", 400));
        drive.Has(Out + @"\Alpha\disc", new ListedFile("setup_alpha.exe", 400));

        // And a disc straight in the folder, which is where a build of one
        // disc goes and not where this one will write.
        drive.Has(Out, new ListedFile("Alpha.iso", 60000));
        drive.Has(Out + @"\disc", new ListedFile("setup_other.exe", 60000));

        RoomForABuild room = RoomForABuild.For(
            new PlannedBuild([ADiscOf("Alpha", own: true, 400), ADiscOf("Beta", own: true, 300)]), drive, Out)!;

        Assert.Equal(800, room.ComesBack);
        Assert.Equal(1400, room.Needed);
        Assert.Equal(600, room.TooLittleBy);
    }

    // ----------------------------------------------------------- the question

    [Fact]
    public void With_room_nothing_is_asked()
    {
        Assert.Null(TheScreen(new ADrive(free: long.MaxValue / 4)).WhetherThereIsRoom());
    }

    [Fact]
    public void A_drive_that_will_not_say_stops_nobody()
    {
        Assert.Null(TheScreen(new ADrive(free: null)).WhetherThereIsRoom());
    }

    [Fact]
    public void Without_room_it_asks_with_the_figures_before_anything_is_built()
    {
        ADrive drive = new(free: 1_000_000);
        MakeADisc screen = TheScreen(drive, games: 1, each: 50_000_000);

        AQuestion asked = Assert.IsType<AQuestion>(screen.WhetherThereIsRoom());
        RoomForABuild room = RoomForABuild.For(ThePlanOf(screen), drive, Out)!;

        Assert.Equal("There is not room on C: for this build.", asked.Headline);
        Assert.Contains($"It needs about {ByteSize.Describe(room.Needed)}: the disc folder and an image of it, which is as big again.", asked.Because, StringComparison.Ordinal);
        Assert.Contains($"C: has {ByteSize.Describe(1_000_000)} free.", asked.Because, StringComparison.Ordinal);
        Assert.Contains($"That is {ByteSize.Describe(room.TooLittleBy)} short", asked.Because, StringComparison.Ordinal);
        Assert.Contains("choose another folder under WHERE TO SAVE THE ISO", asked.Because, StringComparison.Ordinal);
        Assert.DoesNotContain("comes back", asked.Because, StringComparison.Ordinal);

        // More than the game, by the image: at least twice its size.
        Assert.True(room.Needed >= 100_000_000);

        // Building is the likely answer, as it is when building over an
        // earlier build, and shutting the question is Cancel.
        Assert.Equal("Build anyway", asked.TheLikelyOne);
        Assert.Equal("Cancel", asked.TheOtherWay);
    }

    [Fact]
    public void Several_discs_are_folders_and_an_image_of_each()
    {
        MakeADisc screen = TheScreen(new ADrive(free: 0), games: 3, each: 1000);
        screen.Medium = new Medium("TEST", "a small test disc", (2L * 1024 * 1024) + 1500);
        Assert.Equal("3 games, a disc each", screen.WhatItWillMake);

        Assert.Contains(
            "the disc folders and an image of each, which is as big again.",
            Assert.IsType<AQuestion>(screen.WhetherThereIsRoom()).Because,
            StringComparison.Ordinal);
    }

    [Fact]
    public void What_comes_back_from_an_earlier_build_is_said_and_counted()
    {
        ADrive drive = new(free: 1_000_000);
        MakeADisc screen = TheScreen(drive, games: 1, each: 50_000_000);

        // Not enough comes back to make the difference: it still asks, and
        // says what it counted.
        drive.Has(Out, new ListedFile("HOLLOW KNIGHT.iso", 3_000_000));
        drive.Has(Out + @"\disc", new ListedFile("setup.exe", 2_000_000));

        AQuestion asked = Assert.IsType<AQuestion>(screen.WhetherThereIsRoom());

        Assert.Contains(
            $"C: has {ByteSize.Describe(1_000_000)} free, and {ByteSize.Describe(5_000_000)} more comes back from the earlier build this replaces.",
            asked.Because, StringComparison.Ordinal);

        // Enough comes back: the same disc built again over itself, on a
        // drive with next to nothing free, is not asked about.
        RoomForABuild room = RoomForABuild.For(ThePlanOf(screen), drive, Out)!;
        drive.Has(Out, new ListedFile("HOLLOW KNIGHT.iso", room.Needed));

        Assert.Null(screen.WhetherThereIsRoom());
    }

    [Fact]
    public void With_nothing_to_build_nothing_is_asked()
    {
        MakeADisc empty = TheMainWindowWithoutAWindow.New(disk: new ADrive(free: 0));
        empty.OutputFolder = Out;

        Assert.Null(empty.WhetherThereIsRoom());

        // And with no folder chosen there is no drive to ask about.
        MakeADisc nowhere = TheScreen(new ADrive(free: 0));
        nowhere.OutputFolder = string.Empty;

        Assert.Null(nowhere.WhetherThereIsRoom());
    }

    [Fact]
    public void The_window_asks_it_first_and_builds_only_on_a_yes()
    {
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        string handler = code[code.IndexOf("private async void MakeTheDisc(", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("await _screen.MakeIt();", StringComparison.Ordinal)];

        int room = handler.IndexOf("if (_screen.WhetherThereIsRoom() is AQuestion tight && !Ask(tight))", StringComparison.Ordinal);
        int over = handler.IndexOf("if (_screen.WhatBuildingHereReplaces() is AQuestion replacing && !Ask(replacing))", StringComparison.Ordinal);

        // Room first: no point agreeing to delete an earlier build for one
        // that is then not started.
        Assert.True(room >= 0, "The window does not ask whether there is room.");
        Assert.True(over > room, "The window asks about room after asking to build over.");
        Assert.StartsWith("return;", handler[room..].Split('{')[1].Trim(), StringComparison.Ordinal);
    }

    // ------------------------------------------------------- the real drive

    [Fact]
    public void A_real_drive_says_how_much_room_it_has_for_a_folder_that_is_not_there_yet()
    {
        WindowsFileSystem disk = new();
        string here = Path.GetTempPath();
        long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(here))!).AvailableFreeSpace;

        long said = Assert.IsType<long>(disk.RoomAt(Path.Combine(here, "not made yet", "nor this")));

        // The same drive asked twice, a moment apart, with a machine at work
        // on it: near enough, and never nothing.
        Assert.True(said > 0);
        Assert.InRange(said, free - (2L << 30), free + (2L << 30));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"\\no-such-machine-anywhere\share\folder")]
    public void And_nothing_for_a_path_it_cannot_ask_about(string folder)
    {
        Assert.Null(new WindowsFileSystem().RoomAt(folder));
    }

    [Fact]
    public void And_nothing_for_a_drive_that_is_not_there()
    {
        // The first letter nothing is using.
        char? unused = "QRSTUVWXYZ".Cast<char?>().FirstOrDefault(letter => !DriveInfo.GetDrives().Any(drive => drive.Name[0] == letter));

        if (unused is char letter)
        {
            Assert.Null(new WindowsFileSystem().RoomAt(letter + @":\somewhere"));
        }
    }

    // ---------------------------------------------------------------- helpers

    private static PlannedOutput ADiscOf(string label, bool own, params long[] discs) =>
        new(
            label,
            [],
            new DiscLayout([.. discs.Select((bytes, at) => new PlannedDisc(at + 1, [], bytes))], 1_000_000, false),
            own);

    /// <summary>The plan the screen is working to, made the way the screen makes it.</summary>
    private static PlannedBuild ThePlanOf(MakeADisc screen) =>
        Assert.IsType<Outcome<PlannedBuild>.Ok>(WhatWouldBeMade.For(screen.AsAProject(), screen.Medium, _ => 1000)).Value;

    private static MakeADisc TheScreen(ADrive drive, int games = 1, int each = 1000)
    {
        MakeADisc screen = TheMainWindowWithoutAWindow.New(disk: drive);
        screen.OutputFolder = Out;

        for (int at = 0; at < games; at++)
        {
            screen.Add(TheMainWindowWithoutAWindow.AGame($"game{at + 1}", each));
        }

        screen.Label = "HOLLOW KNIGHT";

        return screen;
    }

    /// <summary>A drive with so much free, and whatever a test says is on it.</summary>
    private sealed class ADrive(long? free) : IFileSystem
    {
        private readonly Dictionary<string, List<ListedFile>> _in = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Puts files in a folder, each named from that folder. Said again for a folder, it adds.</summary>
        internal void Has(string folder, params ListedFile[] files)
        {
            if (!_in.TryGetValue(folder, out List<ListedFile>? there))
            {
                _in[folder] = there = [];
            }

            there.AddRange(files);
        }

        public bool FolderExists(string path) => _in.ContainsKey(path) || string.Equals(path, Out, StringComparison.OrdinalIgnoreCase);

        public FolderListing List(string path) => new(path, _in.TryGetValue(path, out List<ListedFile>? there) ? there : []);

        public long? RoomAt(string folder) => free;
    }
}
