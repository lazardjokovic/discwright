using System.Collections.Specialized;
using System.Text;

using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// What the main window says back, tested with no window.
/// </summary>
/// <remarks>
/// Every one of these would otherwise be a test that needs a desktop, a VM and
/// a workaround for the foreground lock, which is what v1's window suite needs
/// and why it costs minutes rather than milliseconds.
/// </remarks>
public class TheMainWindowWithoutAWindow
{
    [Fact]
    public void Says_nothing_at_all_about_an_empty_window()
    {
        MakeADisc window = New();

        // Somebody who has not added a game yet has not done anything wrong,
        // and telling them so is the program complaining about its own
        // starting state.
        Assert.Null(window.WhyNot);
        Assert.Null(window.Worry);
        Assert.Equal(string.Empty, window.WhatItWillMake);
    }

    [Fact]
    public void Names_the_disc_after_the_first_game_and_then_leaves_it_alone()
    {
        MakeADisc window = New();

        window.Add(AGame("Hollow Knight", 1000));
        Assert.Equal("HOLLOW KNIGHT", window.Label);

        window.Add(AGame("Gothic", 1000));

        // Renaming it is their decision, and a second game must not undo it.
        Assert.Equal("HOLLOW KNIGHT", window.Label);
    }

    [Fact]
    public void Keeps_a_name_somebody_typed_themselves()
    {
        MakeADisc window = New();
        window.Label = "MY COMPILATION";

        window.Add(AGame("Hollow Knight", 1000));

        Assert.Equal("MY COMPILATION", window.Label);
    }

    [Fact]
    public void Says_what_it_would_make_before_anything_is_pressed()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));

        Assert.Equal("one disc", window.WhatItWillMake);
        Assert.True(window.CanMakeIt);
    }

    [Fact]
    public void Counts_the_discs_of_a_set_before_anything_is_pressed()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(3000);
        window.Add(AGame("Big", 1200, parts: 4));

        // A sentence somebody reads, not a figure they compare, so "a set of"
        // rather than a bare number.
        Assert.StartsWith("a set of ", window.WhatItWillMake, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_when_several_games_will_each_get_a_disc()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(1500);
        window.Add(AGame("One", 1000));
        window.Add(AGame("Two", 1000));

        Assert.Equal("2 games, a disc each", window.WhatItWillMake);
    }

    [Fact]
    public void Changes_its_answer_when_the_disc_changes()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(1500);
        window.Add(AGame("One", 1000));
        window.Add(AGame("Two", 1000));

        Assert.Equal("2 games, a disc each", window.WhatItWillMake);

        // A bigger disc turns two discs into one, which is exactly the thing
        // somebody is trying out when they change that dropdown.
        window.Medium = ADiscHolding(5000);

        Assert.Equal("one disc", window.WhatItWillMake);
    }

    [Fact]
    public void Will_not_build_without_a_name()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));
        window.Label = string.Empty;

        Assert.NotNull(window.WhyNot);
        Assert.False(window.CanMakeIt);
    }

    [Fact]
    public void Will_not_build_without_somewhere_to_put_it()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        Assert.Null(window.WhyNot);
        Assert.False(window.CanMakeIt);
    }

    [Fact]
    public void Shows_a_refusal_as_things_are_chosen_rather_than_when_the_button_is_pressed()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(500);
        window.Add(AGame("Too Big", 4000));

        // A refusal that arrives after a click is one somebody has already
        // committed to. The same refusal beforehand is just a sentence.
        Assert.NotNull(window.WhyNot);
        Assert.Contains("bigger than a whole disc", window.WhyNot.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.False(window.CanMakeIt);
    }

    [Fact]
    public void Warns_about_a_name_Windows_cannot_show_without_stopping_anything()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));
        window.Label = "\u0414\u0430\u043d\u043d\u044b\u0435";

        // The disc still works, still installs, and somebody may genuinely not
        // mind. What they must not do is find out afterwards.
        Assert.NotNull(window.Worry);
        Assert.Null(window.WhyNot);
        Assert.True(window.CanMakeIt);
    }

    [Fact]
    public void Tells_the_window_when_anything_it_shows_has_changed()
    {
        MakeADisc window = New();
        List<string> told = [];
        window.PropertyChanged += (_, changed) => told.Add(changed.PropertyName ?? string.Empty);

        window.Add(AGame("Hollow Knight", 1000));

        // Without this the window shows the answer to a question somebody has
        // already changed, which is worse than showing nothing.
        Assert.Contains(nameof(MakeADisc.WhatItWillMake), told);
        Assert.Contains(nameof(MakeADisc.CanMakeIt), told);
        Assert.Contains(nameof(MakeADisc.Label), told);
    }

    [Fact]
    public void Forgets_a_game_that_was_taken_off_the_list()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(1500);

        GameFolder one = AGame("One", 1000);
        window.Add(one);
        window.Add(AGame("Two", 1000));
        Assert.Equal("2 games, a disc each", window.WhatItWillMake);

        window.Remove(one);

        Assert.Equal("one disc", window.WhatItWillMake);
    }

    [Fact]
    public void Attaches_an_add_on_to_the_game_it_came_with()
    {
        MakeADisc window = New();
        GameFolder game = AGame("The Witcher", 1000);

        window.Add(game, [APatch("patch_the_witcher_2_3.3_to_3.4_(21034).exe")]);

        AddOn attached = Assert.Single(window.AsAProject().AddOnsOrNone);
        Assert.Equal(0, attached.BelongsTo);
        Assert.Equal("Update 3.4 (21034)", attached.Name);
    }

    [Fact]
    public void Keeps_an_add_on_pointed_at_its_own_game_when_an_earlier_one_goes()
    {
        MakeADisc window = New();
        GameFolder first = AGame("First", 1000);
        GameFolder second = AGame("Second", 1000);

        window.Add(first);
        window.Add(second, [APatch("patch_second_1_to_2.exe")]);

        window.Remove(first);

        // An add-on remembers its game by where that game sits in the list, so
        // taking one out moves every game after it. Without this, a patch for
        // Second would quietly become a patch for whatever is now first.
        AddOn attached = Assert.Single(window.AsAProject().AddOnsOrNone);
        Assert.Equal(0, attached.BelongsTo);
        Assert.Equal("Second", window.Games[attached.BelongsTo].Name);
    }

    [Fact]
    public void Takes_a_games_add_ons_off_with_it()
    {
        MakeADisc window = New();
        GameFolder game = AGame("The Witcher", 1000);

        window.Add(game, [APatch("patch_the_witcher_2_3.3_to_3.4_(21034).exe")]);
        window.Remove(game);

        Assert.Empty(window.AsAProject().AddOnsOrNone);
    }

    [Fact]
    public void Carries_things_that_belong_to_the_disc_rather_than_to_a_game()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 1000));

        window.AlsoOnTheDisc([@"C:\downloads\READ ME.txt"]);

        // A README sitting loose in a folder of games belongs to none of them,
        // so it belongs to the disc, and taking the folder apart must not cost
        // somebody a file.
        DiscExtras extras = Assert.IsType<DiscExtras>(window.AsAProject().Extras);
        Assert.Equal([@"C:\downloads\READ ME.txt"], extras.Loose);
    }

    [Fact]
    public void Has_no_extras_at_all_when_nothing_was_put_there()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 1000));

        Assert.Null(window.AsAProject().Extras);
    }

    [Fact]
    public void Will_not_build_with_somewhere_to_put_it_and_nothing_to_put_there()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";

        // An empty window says nothing, because nothing has been done wrong.
        // Saying nothing is not the same as being ready: this said it was
        // ready until a real window was driven with an empty list.
        Assert.Null(window.WhyNot);
        Assert.False(window.CanMakeIt);
    }

    [Fact]
    public void Says_when_the_list_itself_changes_and_not_only_that_it_exists()
    {
        MakeADisc window = New();
        List<NotifyCollectionChangedAction> changes = [];
        ((INotifyCollectionChanged)window.Games).CollectionChanged += (_, e) => changes.Add(e.Action);

        GameFolder game = AGame("Hollow Knight", 1000);
        window.Add(game);
        window.Remove(game);

        // The property returns the same object every time, so a list bound to
        // it cannot tell that what is inside has changed. The window built the
        // right disc while showing an empty list until this was found.
        Assert.Equal([NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Remove], changes);
    }

    [Fact]
    public async Task Says_what_the_build_is_doing_in_the_builds_own_words()
    {
        // The builder speaks and then waits, so the line it reported is heard
        // while the build is still running. Without the waiting this passed
        // here and failed on the test VM: Progress<T> hands reports over
        // through the synchronisation context, so with a builder that finishes
        // at once the report can arrive after the end and be ignored.
        BuilderThatTalks builder = new();
        MakeADisc window = Ready(builder);

        TaskCompletionSource<string> heard = new();
        window.PropertyChanged += (_, changed) =>
        {
            // Not the window's own "Starting", which is what it says before
            // the builder has said anything.
            if (changed.PropertyName == nameof(MakeADisc.Doing)
                && window.Doing.Length > 0
                && window.Doing != "Starting")
            {
                heard.TrySetResult(window.Doing);
            }
        };

        Task building = window.MakeIt();
        string line = await heard.Task.WaitAsync(TimeSpan.FromSeconds(10));
        builder.LetItFinish();
        await building;

        // Passed straight through. The step knows what it is doing and the
        // window does not, and a window that invents its own account of
        // somebody else's work will one day be wrong about it.
        Assert.Contains("copying the files", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Will_not_start_a_second_build_on_top_of_the_first()
    {
        BuilderThatWaits builder = new();
        MakeADisc window = Ready(builder);

        Task first = window.MakeIt();
        await builder.Started.Task;

        Assert.True(window.IsWorking);
        Assert.False(window.CanMakeIt);

        await window.MakeIt();
        builder.LetItFinish();
        await first;

        Assert.Equal(1, builder.Builds);
    }

    [Fact]
    public async Task Keeps_what_the_build_made()
    {
        MakeADisc window = Ready(new BuilderThatWorks());

        await window.MakeIt();

        Assert.NotNull(window.Made);
        Assert.False(window.IsWorking);
        Assert.Null(window.ItWentWrong);

        // And the button works again, because somebody may well want another.
        Assert.True(window.CanMakeIt);
    }

    [Fact]
    public async Task Says_why_a_build_stopped()
    {
        MakeADisc window = Ready(new BuilderThatRefuses());

        await window.MakeIt();

        Assert.NotNull(window.ItWentWrong);
        Assert.Null(window.Made);
        Assert.False(window.IsWorking);
    }

    [Fact]
    public async Task Says_nothing_at_all_about_a_build_somebody_stopped()
    {
        BuilderThatWaits builder = new();
        MakeADisc window = Ready(builder);

        using CancellationTokenSource stop = new();
        Task building = window.MakeIt(stop.Token);
        await builder.Started.Task;
        stop.Cancel();
        builder.LetItFinish();
        await building;

        // Somebody who pressed stop knows why it stopped. Telling them is the
        // program explaining their own decision back to them.
        Assert.Null(window.ItWentWrong);
        Assert.Null(window.Made);
        Assert.False(window.IsWorking);
    }

    [Fact]
    public async Task Is_quiet_again_once_the_build_is_over()
    {
        MakeADisc window = Ready(new BuilderThatWorks());

        await window.MakeIt();

        Assert.Equal(string.Empty, window.Doing);
        Assert.Equal(0, window.HowFar);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A window with everything filled in, ready to press.</summary>
    private static MakeADisc Ready(IBuildDiscs builder)
    {
        MakeADisc window = New(builder);
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));

        return window;
    }

    private static MakeADisc New(IBuildDiscs? builder = null) =>
        new(new Codepage1252(), _ => 0, builder ?? new BuilderThatWorks());

    private static LeftBehind APatch(string name) =>
        new(new ListedFile(name, 200), LeftBehindKind.LooksLikeAnAddOn);

    private static Medium ADiscHolding(long bytes) => new("TEST", "a test disc", (2L * 1024 * 1024) + bytes);

    private static GameFolder AGame(string name, int bytes, int parts = 0)
    {
        List<ListedFile> files = [new($"setup_{name}.exe", bytes)];

        for (int part = 1; part <= parts; part++)
        {
            files.Add(new ListedFile($"setup_{name}-{part}.bin", bytes));
        }

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, files[0], files, [], null);
    }

    private sealed class Codepage1252 : IAutorunEncoding
    {
        private static readonly Encoding Ansi = MakeIt();

        public string AsAutorunWillSeeIt(string text) => Ansi.GetString(Ansi.GetBytes(text));

        private static Encoding MakeIt()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
    }

    private sealed class BuilderThatWorks : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            progress?.Report(new BuildProgress(1, 2, "copying the files"));
            progress?.Report(new BuildProgress(2, 2, "writing the menu"));

            return Task.FromResult<Outcome<BuiltDiscs>>(
                new BuiltDiscs([new BuiltDisc(project.Label, 1, 1, outputFolder, 1000)]));
        }
    }

    private sealed class BuilderThatRefuses : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(Refusals.ThereIsNothingToPutOnADisc());
    }

    /// <summary>
    /// A build that starts and then waits to be let go, so a test can look at
    /// the window while something is running.
    /// </summary>
    private sealed class BuilderThatWaits : IBuildDiscs
    {
        private readonly TaskCompletionSource _go = new();

        internal TaskCompletionSource Started { get; } = new();

        internal int Builds { get; private set; }

        internal void LetItFinish() => _go.TrySetResult();

        public async Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            Builds++;
            Started.TrySetResult();
            await _go.Task;
            ct.ThrowIfCancellationRequested();

            return new BuiltDiscs([new BuiltDisc(project.Label, 1, 1, outputFolder, 1000)]);
        }
    }

    /// <summary>
    /// A build that says one thing and then waits, so a test can be certain
    /// the report was heard while the build was still running.
    /// </summary>
    private sealed class BuilderThatTalks : IBuildDiscs
    {
        private readonly TaskCompletionSource _go = new();

        internal void LetItFinish() => _go.TrySetResult();

        public async Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            progress?.Report(new BuildProgress(1, 2, "copying the files"));
            await _go.Task;

            return new BuiltDiscs([new BuiltDisc(project.Label, 1, 1, outputFolder, 1000)]);
        }
    }
}
