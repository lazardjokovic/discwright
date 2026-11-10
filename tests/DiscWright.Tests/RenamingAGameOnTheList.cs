using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Renaming a game in place on the list, the companion discwright#98's
/// duplicate name notice was waiting for.
/// </summary>
/// <remarks>
/// A notice that two games answer to one name is only half an answer while
/// the way to act on it is to take a game off and add it again. Only a
/// game's own row renames: a file keeps the name it has on disk whatever
/// the list says, and the registered match name is never touched, because
/// what GOG wrote in the registry does not change when somebody retitles a
/// menu button.
/// </remarks>
public class RenamingAGameOnTheList
{
    [Fact]
    public void Renames_the_game_everywhere_at_once()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        AThingOnTheDisc row = screen.WhatGoesOnIt[0];

        screen.BeginRenaming(row);
        Assert.True(row.IsBeingRenamed);

        screen.FinishRenaming(row, "  Sword of Doom  ");

        Assert.False(row.IsBeingRenamed);
        Assert.Equal("Sword of Doom", row.Name);
        Assert.Equal("Sword of Doom", screen.AsAProject().Games[0].Name);
    }

    [Fact]
    public void A_rename_survives_the_next_tick()
    {
        // The ticks rebuild the games from the list as found, so a rename
        // that only touched the built list would be quietly undone by the
        // next checkbox anybody pressed.
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        AThingOnTheDisc row = screen.WhatGoesOnIt[0];

        screen.BeginRenaming(row);
        screen.FinishRenaming(row, "Sword of Doom");

        row.Inside[^1].IsOn = false;

        Assert.Equal("Sword of Doom", screen.AsAProject().Games[0].Name);
    }

    [Fact]
    public void Nothing_typed_keeps_the_old_name()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        AThingOnTheDisc row = screen.WhatGoesOnIt[0];

        screen.BeginRenaming(row);
        screen.FinishRenaming(row, "   ");

        Assert.False(row.IsBeingRenamed);
        Assert.Equal("Hollow Knight", row.Name);
        Assert.Equal("Hollow Knight", screen.AsAProject().Games[0].Name);
    }

    [Fact]
    public void Escape_keeps_the_old_name()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        AThingOnTheDisc row = screen.WhatGoesOnIt[0];

        screen.BeginRenaming(row);
        screen.CancelRenaming(row);

        Assert.False(row.IsBeingRenamed);
        Assert.Equal("Hollow Knight", row.Name);
    }

    [Fact]
    public void Only_a_games_own_row_ever_opens()
    {
        // A file keeps the name it has on disk whatever the list says, so
        // its row never opens, however it is asked.
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        AThingOnTheDisc file = screen.WhatGoesOnIt[0].Inside[0];

        screen.BeginRenaming(file);

        Assert.False(file.IsBeingRenamed);
    }

    [Fact]
    public void The_duplicate_name_notice_follows_a_rename()
    {
        // The whole reason this exists: the notice says two games answer
        // alike, and renaming is how somebody acts on it without taking a
        // game off.
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight", @"C:\games\one"));
        screen.Add(AGame("Celeste", @"C:\games\two"));
        Assert.Null(screen.WhoWearsTheSameName);

        AThingOnTheDisc second = screen.WhatGoesOnIt[1];
        screen.BeginRenaming(second);
        screen.FinishRenaming(second, "Hollow Knight");

        Assert.NotNull(screen.WhoWearsTheSameName);

        screen.BeginRenaming(second);
        screen.FinishRenaming(second, "Celeste Again");

        Assert.Null(screen.WhoWearsTheSameName);
    }

    [Fact]
    public void The_registered_name_is_never_touched()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        AThingOnTheDisc row = screen.WhatGoesOnIt[0];

        screen.BeginRenaming(row);
        screen.FinishRenaming(row, "Sword of Doom");

        Assert.Equal("Hollow Knight", screen.AsAProject().Games[0].MatchName);
    }

    [Fact]
    public void The_window_hears_the_rows_new_name_and_the_notice_that_follows()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight", @"C:\games\one"));
        screen.Add(AGame("Celeste", @"C:\games\two"));
        AThingOnTheDisc row = screen.WhatGoesOnIt[1];

        List<string> rowSaid = [];
        row.PropertyChanged += (_, moved) => rowSaid.Add(moved.PropertyName!);
        List<string> screenSaid = [];
        screen.PropertyChanged += (_, moved) => screenSaid.Add(moved.PropertyName!);

        screen.BeginRenaming(row);
        screen.FinishRenaming(row, "Hollow Knight");

        Assert.Contains(nameof(AThingOnTheDisc.IsBeingRenamed), rowSaid, StringComparer.Ordinal);
        Assert.Contains(nameof(AThingOnTheDisc.Name), rowSaid, StringComparer.Ordinal);
        Assert.Contains(nameof(MakeADisc.WhoWearsTheSameName), screenSaid, StringComparer.Ordinal);
    }

    [Fact]
    public void A_saved_project_carries_the_new_name()
    {
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        AThingOnTheDisc row = screen.WhatGoesOnIt[0];

        screen.BeginRenaming(row);
        screen.FinishRenaming(row, "Sword of Doom");

        string written = TheProjectAsJson.Of(
            screen.AsAProject(), "2.0.0", "DVD5", @"C:\out", DateTimeOffset.UtcNow);

        Assert.Contains("Sword of Doom", written, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- helpers

    private static GameFolder AGame(string name, string path = @"C:\games\somewhere")
    {
        ListedFile setup = new($"setup_{name}.exe", 1000);
        ListedFile part = new($"setup_{name}-1.bin", 2000);

        return new GameFolder(path, GameKind.GogInstaller, name, name, setup, [setup, part], [], null);
    }

    private static MakeADisc TheScreen() =>
        new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatDoesNothing(),
            new ImagesThatDoNothing(),
            new NoDrives(),
            new ABurnerThatDoesNothing(),
            new AnEraserThatDoesNothing(),
            new ADiskWithNothingOnIt(),
            new NoHashing(),
            new NothingIsRemembered(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            new NoPreview(),
            new PicturesThatRead(),
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience));

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
    }

    private sealed class ABuilderThatDoesNothing : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs([]));
    }

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class AnEraserThatDoesNothing : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
    }

    private sealed class ADiskWithNothingOnIt : IFileSystem
    {
        public bool FolderExists(string path) => false;

        public FolderListing List(string path) => new(path, []);
    }

    private sealed class NoHashing : IHasher
    {
        public string Sha256(string path, CancellationToken ct) => string.Empty;
    }

    private sealed class NothingIsRemembered : IKeepTheSetsPlace
    {
        public string? WhatItSays(string folder) => null;

        public void Leave(string folder, string text)
        {
        }
    }

    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "no asking in a test"));
    }

    private sealed class NoPreview : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            Refusals.ThereIsNothingToPutOnADisc();
    }

    private sealed class PicturesThatRead : IImages
    {
        public ImageFacts Read(string path) => new(true, 512, 512, null);

        public void WriteIcon(string source, string destination)
        {
        }

        public void WriteBackground(string source, string destination, int width, int height, bool asIs)
        {
        }
    }
}
