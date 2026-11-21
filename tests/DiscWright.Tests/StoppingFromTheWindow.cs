using System.Xml.Linq;

using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The Stop button: when it is there, what it asks, and what the screen does
/// with a build that was stopped.
/// </summary>
/// <remarks>
/// The owner, 21 November 2026, forty minutes into a build: "should there be
/// a way to stop the build? That would of course delete everything that was
/// created, with the previous warning, and if the user clicks yes the build
/// stops and everything that was built is deleted." There was none. The
/// builder had always known how to stop when told, and nothing on the window
/// ever told it.
/// </remarks>
public class StoppingFromTheWindow
{
    private const string Out = @"C:\out";

    // --------------------------------------------------------- when it is there

    [Fact]
    public async Task There_is_nothing_to_stop_until_a_build_is_running_and_nothing_once_it_ends()
    {
        ABuilderThatWaitsToBeStopped builder = new();
        MakeADisc screen = TheScreen(builder);

        Assert.False(screen.CanStopTheBuild);
        Assert.Null(screen.WhatStoppingWouldLose());

        Task building = screen.MakeIt();
        await builder.Started.Task;

        Assert.True(screen.CanStopTheBuild);
        Assert.False(screen.CanMakeIt);

        builder.LetItFinish();
        await Ended(building);

        Assert.False(screen.CanStopTheBuild);
        Assert.Null(screen.WhatStoppingWouldLose());
        Assert.Null(screen.HowTheStopWent);
        Assert.NotNull(screen.Made);
    }

    [Fact]
    public async Task It_asks_first_and_says_what_goes_and_what_does_not()
    {
        ABuilderThatWaitsToBeStopped builder = new();
        MakeADisc screen = TheScreen(builder);
        Task building = screen.MakeIt();
        await builder.Started.Task;

        AQuestion asked = Assert.IsType<AQuestion>(screen.WhatStoppingWouldLose());

        Assert.Equal("Stop the build, and delete what it has made so far?", asked.Headline);
        Assert.Contains("The disc folders and images this build has written are deleted", asked.Because, StringComparison.Ordinal);
        Assert.Contains("It cannot be picked up where it left off.", asked.Because, StringComparison.Ordinal);
        Assert.Contains("Your games are not touched", asked.Because, StringComparison.Ordinal);

        // Stopping is the likely answer, because it was just pressed, and
        // shutting the question is the other one, which costs nothing.
        Assert.Equal("Stop the build", asked.TheLikelyOne);
        Assert.Equal("Keep building", asked.TheOtherWay);

        // Asking stops nothing.
        Assert.True(screen.CanStopTheBuild);

        builder.LetItFinish();
        await Ended(building);
    }

    [Fact]
    public void Stopping_with_nothing_running_does_nothing()
    {
        MakeADisc screen = TheScreen(new ABuilderThatWaitsToBeStopped());

        screen.StopTheBuild();

        Assert.Null(screen.HowTheStopWent);
        Assert.False(screen.IsWorking);
    }

    // ------------------------------------------------- stopped in the building

    [Fact]
    public async Task Stopped_it_says_so_at_once_and_then_what_became_of_what_was_built()
    {
        ABuilderThatWaitsToBeStopped builder = new();
        MakeADisc screen = TheScreen(builder);
        List<string> doing = [];
        List<string> told = [];
        screen.PropertyChanged += (_, moved) =>
        {
            told.Add(moved.PropertyName!);

            if (moved.PropertyName == nameof(MakeADisc.Doing))
            {
                doing.Add(screen.Doing);
            }
        };

        Task building = screen.MakeIt();
        await builder.Started.Task;

        screen.StopTheBuild();

        // At once, before the build has answered: the press was heard.
        Assert.Equal("Stopping, and deleting what was built", screen.Doing);
        Assert.False(screen.CanStopTheBuild);
        Assert.Null(screen.WhatStoppingWouldLose());

        await Ended(building);

        Assert.Equal("The build was stopped, and what it had written was deleted.", screen.HowTheStopWent);
        Assert.Null(screen.Made);
        Assert.Null(screen.ItWentWrong);
        Assert.False(screen.IsWorking);
        Assert.True(screen.CanMakeIt);
        Assert.Contains("Stopping, and deleting what was built", doing);

        foreach (string named in new[] { nameof(MakeADisc.CanStopTheBuild), nameof(MakeADisc.HowTheStopWent), nameof(MakeADisc.Doing) })
        {
            Assert.Contains(named, told, StringComparer.Ordinal);
        }

        // The builder took its own away, so the screen had nothing to.
        Assert.True(builder.WasStopped);
    }

    [Fact]
    public async Task What_the_builder_could_not_delete_is_named()
    {
        ABuilderThatWaitsToBeStopped builder = new() { LeavesBehind = [@"C:\out\disc"] };
        MakeADisc screen = TheScreen(builder);
        Task building = screen.MakeIt();
        await builder.Started.Task;

        screen.StopTheBuild();
        await Ended(building);

        Assert.Equal(
            @"The build was stopped. Something has these open, so they could not be deleted: C:\out\disc.",
            screen.HowTheStopWent);
    }

    [Fact]
    public async Task Whoever_started_it_stopping_it_is_a_stop_too()
    {
        ABuilderThatWaitsToBeStopped builder = new();
        MakeADisc screen = TheScreen(builder);
        using CancellationTokenSource theirs = new();

        Task building = screen.MakeIt(theirs.Token);
        await builder.Started.Task;
        await theirs.CancelAsync();
        await Ended(building);

        Assert.Equal("The build was stopped, and what it had written was deleted.", screen.HowTheStopWent);
    }

    [Fact]
    public async Task The_build_after_a_stopped_one_runs_and_the_line_about_the_stop_is_gone()
    {
        ABuilderThatWaitsToBeStopped builder = new();
        MakeADisc screen = TheScreen(builder);
        Task building = screen.MakeIt();
        await builder.Started.Task;
        screen.StopTheBuild();
        await Ended(building);
        Assert.NotNull(screen.HowTheStopWent);

        builder.StartAgain();
        Task again = screen.MakeIt();
        await builder.Started.Task;

        Assert.Null(screen.HowTheStopWent);
        Assert.True(screen.CanStopTheBuild);

        builder.LetItFinish();
        await Ended(again);

        Assert.NotNull(screen.Made);
        Assert.Null(screen.HowTheStopWent);
    }

    // ----------------------------------------- stopped while the images are made

    [Fact]
    public async Task Stopped_in_the_second_image_of_three_it_takes_the_folders_and_every_image_begun()
    {
        ARemover remover = new();
        ImagesThatWaitAt images = new(waitsAt: 2);
        MakeADisc screen = TheScreen(new ABuilderOfSoMany(3), images, remover);

        Task building = screen.MakeIt();
        await images.Waiting.Task;
        screen.StopTheBuild();
        await Ended(building);

        // The first image was finished and the second begun. The third was
        // never started, so there is nothing of it to delete.
        Assert.Equal([@"C:\out\HOLLOW KNIGHT D1.iso", @"C:\out\HOLLOW KNIGHT D2.iso"], remover.Images);
        Assert.Equal([@"C:\out\disc D1", @"C:\out\disc D2", @"C:\out\disc D3"], remover.Folders);

        // A set sits in the folder somebody chose, which is theirs and stays.
        Assert.Empty(remover.IfEmpty);

        Assert.Equal("The build was stopped, and what it had written was deleted.", screen.HowTheStopWent);
        Assert.Null(screen.Made);
        Assert.False(screen.IsWorking);
    }

    [Fact]
    public async Task With_a_disc_each_the_folder_made_for_a_game_goes_if_nothing_is_left_in_it()
    {
        ARemover remover = new();
        ImagesThatWaitAt images = new(waitsAt: 1);
        MakeADisc screen = TheScreen(new ABuilderOfADiscEach("Alpha", "Beta"), images, remover);

        Task building = screen.MakeIt();
        await images.Waiting.Task;
        screen.StopTheBuild();
        await Ended(building);

        Assert.Equal([@"C:\out\Alpha\disc", @"C:\out\Beta\disc"], remover.Folders);
        Assert.Equal([@"C:\out\Alpha", @"C:\out\Beta"], remover.IfEmpty);
        Assert.DoesNotContain(Out, remover.IfEmpty);
    }

    [Fact]
    public async Task What_the_screen_could_not_delete_is_named_with_what_the_builder_could_not()
    {
        ARemover remover = new();
        remover.HeldOpen.Add(@"C:\out\disc D2");
        remover.HeldOpen.Add(@"C:\out\HOLLOW KNIGHT D1.iso");
        ImagesThatWaitAt images = new(waitsAt: 2);
        MakeADisc screen = TheScreen(new ABuilderOfSoMany(2), images, remover);

        Task building = screen.MakeIt();
        await images.Waiting.Task;
        screen.StopTheBuild();
        await Ended(building);

        Assert.Equal(
            @"The build was stopped. Something has these open, so they could not be deleted: "
                + @"C:\out\HOLLOW KNIGHT D1.iso, C:\out\disc D2.",
            screen.HowTheStopWent);

        // The ones that would go, went: one refusal does not stop the rest.
        Assert.Contains(@"C:\out\disc D1", remover.Folders);
        Assert.Contains(@"C:\out\HOLLOW KNIGHT D2.iso", remover.Images);
    }

    [Fact]
    public async Task An_image_that_fails_is_not_a_stop_and_nothing_built_is_deleted()
    {
        // The disk filling up is the image writer's to tidy, and it takes
        // away its own half. The disc folders are whole and stay.
        ARemover remover = new();
        MakeADisc screen = TheScreen(new ABuilderOfSoMany(2), new ImagesThatFail(), remover);

        await Ended(screen.MakeIt());

        Assert.NotNull(screen.ItWentWrong);
        Assert.Null(screen.HowTheStopWent);
        Assert.Empty(remover.Folders);
        Assert.Empty(remover.Images);
        Assert.Empty(remover.IfEmpty);
    }

    [Fact]
    public async Task With_nothing_that_deletes_a_stop_still_stops_and_says_so()
    {
        ImagesThatWaitAt images = new(waitsAt: 1);
        MakeADisc screen = TheScreen(new ABuilderOfSoMany(1), images, remover: null);

        Task building = screen.MakeIt();
        await images.Waiting.Task;
        screen.StopTheBuild();
        await Ended(building);

        Assert.NotNull(screen.HowTheStopWent);
        Assert.False(screen.IsWorking);
    }

    // ---------------------------------------------------------------- the window

    [Fact]
    public void Stop_is_beside_Build_it_while_a_build_runs_and_Build_it_stays_where_it_is()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XElement window = XDocument.Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml")).Root!;

        XElement Named(string id) => Assert.Single(
            window.Descendants(), each => (string?)each.Attribute("AutomationProperties.AutomationId") == id);

        XElement stop = Named("StopTheBuild");
        XElement build = Named("MakeTheDisc");

        Assert.Equal("Stop", (string?)stop.Attribute("Content"));
        Assert.Equal("StopTheBuild", (string?)stop.Attribute("Click"));
        Assert.Equal("{Binding CanStopTheBuild, Converter={StaticResource WhenSo}}", (string?)stop.Attribute("Visibility"));
        Assert.False(string.IsNullOrWhiteSpace((string?)stop.Attribute("ToolTip")));

        // Not the accent: the accent is the thing to do next.
        Assert.Equal("{StaticResource Ordinary}", (string?)stop.Attribute("Style"));
        Assert.Equal("{StaticResource TheButton}", (string?)build.Attribute("Style"));

        // Beside it, just before it, and Build it is never hidden.
        Assert.Same(stop.Parent, build.Parent);
        Assert.Same(build, stop.ElementsAfterSelf().First());
        Assert.Null(build.Attribute("Visibility"));

        XElement line = Named("HowTheStopWent");

        Assert.Equal("{Binding HowTheStopWent}", (string?)line.Attribute("Text"));
        Assert.Equal("{Binding HowTheStopWent, Converter={StaticResource WhenAnything}}", (string?)line.Attribute("Visibility"));
        Assert.Equal("Wrap", (string?)line.Attribute("TextWrapping"));
    }

    [Fact]
    public void The_window_asks_before_it_stops_anything()
    {
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        string handler = code[code.IndexOf("private void StopTheBuild(", StringComparison.Ordinal)..];
        handler = handler[..handler.IndexOf("private async void MakeTheDisc(", StringComparison.Ordinal)];

        Assert.Contains("if (_screen.WhatStoppingWouldLose() is AQuestion losing && Ask(losing))", handler, StringComparison.Ordinal);
        Assert.Contains("_screen.StopTheBuild();", handler, StringComparison.Ordinal);
        Assert.Equal(1, handler.Split("StopTheBuild();").Length - 1);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Waits for a build to end, and not for ever. A Stop that stopped
    /// nothing left these tests waiting on a build that would never be let
    /// finish: the fault was planted, and the run sat for fifty minutes
    /// until somebody ended it by hand. A fault that hangs a test is worse
    /// than one that passes.
    /// </summary>
    private static Task Ended(Task build) => build.WaitAsync(TimeSpan.FromSeconds(30));

    private static MakeADisc TheScreen(IBuildDiscs builder, IMakeImages? images = null, ARemover? remover = null)
    {
        MakeADisc screen = TheMainWindowWithoutAWindow.New(builder: builder, images: images, remover: remover);

        screen.Add(TheMainWindowWithoutAWindow.AGame("hollow_knight", 1000));
        screen.Label = "HOLLOW KNIGHT";
        screen.OutputFolder = Out;

        return screen;
    }

    /// <summary>A build that waits until it is let finish or stopped, as a real one copying a big file does.</summary>
    private sealed class ABuilderThatWaitsToBeStopped : IBuildDiscs
    {
        private TaskCompletionSource _go = new();

        internal TaskCompletionSource Started { get; private set; } = new();

        internal bool WasStopped { get; private set; }

        internal IReadOnlyList<string> LeavesBehind { get; init; } = [];

        internal void LetItFinish() => _go.TrySetResult();

        internal void StartAgain()
        {
            _go = new TaskCompletionSource();
            Started = new TaskCompletionSource();
        }

        public async Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            Started.TrySetResult();

            using (ct.Register(() => _go.TrySetResult()))
            {
                await _go.Task;
            }

            if (ct.IsCancellationRequested)
            {
                WasStopped = true;

                throw new ABuildThatWasStopped(LeavesBehind);
            }

            return new BuiltDiscs([new BuiltDisc(project.Label, 1, 1, Path.Combine(outputFolder, "disc"), 1000)]);
        }
    }

    private sealed class ABuilderOfSoMany(int discs) : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs(
            [
                .. Enumerable.Range(1, discs).Select(number => new BuiltDisc(
                    project.Label, number, discs, Path.Combine(outputFolder, discs > 1 ? $"disc D{number}" : "disc"), 1000))
            ]));
    }

    private sealed class ABuilderOfADiscEach(params string[] games) : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs(
            [
                .. games.Select(game => new BuiltDisc(game, 1, 1, Path.Combine(outputFolder, game, "disc"), 1000))
            ]));
    }

    /// <summary>Makes each image at once, until the one it waits at, where it waits to be stopped.</summary>
    private sealed class ImagesThatWaitAt(int waitsAt) : IMakeImages
    {
        private int _asked;

        internal TaskCompletionSource Waiting { get; } = new();

        public async Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct)
        {
            if (++_asked == waitsAt)
            {
                TaskCompletionSource stopped = new();

                using (ct.Register(() => stopped.TrySetResult()))
                {
                    Waiting.TrySetResult();
                    await stopped.Task;
                }

                ct.ThrowIfCancellationRequested();
            }

            return new MadeImage(imagePath, 1024, null);
        }
    }

    private sealed class ImagesThatFail : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(Refusals.TheImageCouldNotBeWritten(imagePath, "There is not enough space on the disk."));
    }

    private sealed class ARemover : IRemoveADiscFolder
    {
        internal List<string> Folders { get; } = [];

        internal List<string> Images { get; } = [];

        internal List<string> IfEmpty { get; } = [];

        internal HashSet<string> HeldOpen { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Remove(string discFolder)
        {
            if (HeldOpen.Contains(discFolder))
            {
                throw new TheFolderIsInUse(discFolder, "The process cannot access the file.", new IOException());
            }

            Folders.Add(discFolder);
        }

        public void RemoveIfEmpty(string folder) => IfEmpty.Add(folder);

        public void RemoveTheImage(string imagePath)
        {
            if (HeldOpen.Contains(imagePath))
            {
                throw new IOException("The process cannot access the file.");
            }

            Images.Add(imagePath);
        }
    }
}
