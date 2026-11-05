using System.Collections.Specialized;
using System.Text;

using System.Runtime.InteropServices;
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
    public void Will_not_offer_to_start_again_from_a_window_with_nothing_on_it()
    {
        MakeADisc window = New();

        // It asked nothing and cleared nothing, which is right and is also
        // exactly what a button that does not work looks like.
        Assert.False(window.HasAnythingOnIt);
        Assert.False(window.CanStartAgain);
    }

    [Theory]
    [InlineData("a game")]
    [InlineData("a name")]
    [InlineData("a manual")]
    [InlineData("a folder of extras")]
    [InlineData("a tune")]
    public void Offers_to_start_again_as_soon_as_there_is_anything_to_start_again_from(string put)
    {
        MakeADisc window = New();

        switch (put)
        {
            case "a game": window.Add(AGame("A Game", 1000)); break;
            case "a name": window.Label = "MY COMPILATION"; break;
            case "a manual": window.Manual = @"C:\somewhere\how to play.txt"; break;
            case "a folder of extras": window.ExtrasFolder = @"C:\somewhere\extras"; break;
            case "a tune": window.Music = @"C:\somewhere\tune.mp3"; break;
        }

        // Every one of these is something somebody would lose, so every one
        // of them has to light the button that would lose it. A property the
        // window is never told about keeps whatever it read the first time,
        // which for this one would be a button dead for the whole session.
        Assert.True(window.HasAnythingOnIt, put + " is not counted as anything on it");
        Assert.True(window.CanStartAgain, put + " does not light Start a new disc");
    }

    [Fact]
    public void Stops_offering_to_start_again_once_it_has()
    {
        MakeADisc window = New();

        // A tune and nothing else, so the name never changes. Adding a game
        // names the disc, and clearing that name is its own announcement,
        // which told this one for free and hid whether anything here does.
        window.Music = @"C:\somewhere\tune.mp3";
        Assert.True(window.CanStartAgain);
        Assert.Equal(string.Empty, window.Label);

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.StartAgain();

        Assert.False(window.CanStartAgain);

        // And the window has to hear about it. A binding never told stays on
        // whatever it read last, which here is a lit button over a window
        // with nothing left on it.
        Assert.Contains(nameof(MakeADisc.CanStartAgain), told);
    }

    [Fact]
    public void Tells_the_window_the_moment_there_is_something_to_start_again_from()
    {
        MakeADisc window = New();

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.Add(AGame("A Game", 1000));

        // Asking the property is not enough: nothing asks it twice. The
        // button is drawn once and redrawn only when it is told, so a fault
        // that stops the telling leaves it dead for the whole session and
        // every assertion about the property still passes.
        Assert.Contains(nameof(MakeADisc.CanStartAgain), told);
    }

    [Fact]
    public async Task Will_not_offer_to_start_again_in_the_middle_of_being_made()
    {
        bool? couldStartAgainMidWay = null;
        MakeADisc? window = null;
        window = New(
            drives: new ADriveHolding(3, false, 0),
            eraser: new AnEraserThatAsks(() => couldStartAgainMidWay = window!.CanStartAgain));

        window.Add(AGame("A Game", 1000));
        Assert.True(window.CanStartAgain);

        await window.LookAtTheDrives();
        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        // StartAgain refuses while something is running, so a button left lit
        // here is the exact complaint this came from: pressed, and nothing
        // happens.
        Assert.False(couldStartAgainMidWay);
        Assert.True(window.CanStartAgain);
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
    public void Says_how_many_discs_when_one_of_the_games_needs_a_set_of_its_own()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(1500);
        window.Add(AGame("Big", 1000, parts: 2));
        window.Add(AGame("Two", 1000));

        // "A disc each" only when it is true. Big is three files of 1000
        // bytes on a disc holding 1500, so it is a set of three beside Two's
        // one.
        Assert.Equal("2 games on 4 discs", window.WhatItWillMake);
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

        // Saying nothing used to be the expected answer here, written down
        // as Assert.Null. A sound plan, a named disc, and the one button on
        // the window dead with no sentence anywhere explaining why: every
        // other missing thing says so, and this one read as a broken button.
        Assert.NotNull(window.WhyNot);
        Assert.Contains("nowhere to put it", window.WhyNot.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.False(window.CanMakeIt);

        // What it would make is still said. Only the destination is missing,
        // and hiding the plan too would answer a question nobody asked.
        Assert.Equal("one disc", window.WhatItWillMake);
    }

    [Fact]
    public void Stops_saying_there_is_nowhere_to_put_it_once_there_is()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        Assert.NotNull(window.WhyNot);

        window.OutputFolder = @"C:\out";

        Assert.Null(window.WhyNot);
        Assert.True(window.CanMakeIt);
    }

    [Fact]
    public void Complains_about_the_disc_before_it_complains_about_the_folder()
    {
        // Both are wrong and only one is worth saying. Choosing a folder
        // would not make a file fit on a disc, so the refusal on the window
        // has to be the one somebody can actually act on.
        MakeADisc window = New();
        window.Medium = ADiscHolding(500);
        window.Add(AGame("Too Big", 4000));

        Assert.NotNull(window.WhyNot);
        Assert.Contains("bigger than a whole disc", window.WhyNot.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Says_nothing_about_a_folder_before_there_is_anything_to_put_in_one()
    {
        // An empty window has not done anything wrong. Telling somebody off
        // about a folder they have had no reason to choose yet is the program
        // complaining about its own starting state.
        MakeADisc window = New();

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
    public void Lets_the_splitting_box_answer_the_refusal_it_is_the_answer_to()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = Media.Cd;
        window.Add(AGame("Too Big", 800_000_000));

        Assert.NotNull(window.WhyNot);

        window.CutFiles = true;

        // The red line stands until the box is ticked and then it goes,
        // because ticking it is what makes the thing possible. A refusal that
        // stayed after being answered would read as a bug.
        Assert.Null(window.WhyNot);
        Assert.True(window.CanMakeIt);

        // And the size that was chosen is still chosen. Ticking the box used
        // to reword every row of the dropdown, which both hid the plain sizes
        // and handed WPF a list the chosen row was no longer in.
        Assert.Equal(Media.Cd.Key, window.TheSizeChosen!.Medium.Key);
        Assert.Equal(Media.Cd.Name, window.TheSizeChosen.InWords);
    }

    [Fact]
    public void Moves_the_dropdown_when_the_size_is_set_from_anywhere_else()
    {
        MakeADisc window = New();
        List<string> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        window.Medium = Media.Cd;

        // Opening a project, and a disc appearing in a drive, both set the
        // size without anybody touching the dropdown. A dropdown only redraws
        // for a property it was told about, so without this the window would
        // go on saying DVD5 while holding a CD.
        Assert.Contains(nameof(MakeADisc.TheSizeChosen), told);
        Assert.Equal(Media.Cd.Key, window.TheSizeChosen!.Medium.Key);
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
        await builder.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(window.IsWorking);
        Assert.False(window.CanMakeIt);

        await window.MakeIt();
        builder.LetItFinish();
        await first;

        Assert.Equal(1, builder.Builds);
    }

    [Fact]
    public async Task Makes_an_image_of_every_disc_it_builds()
    {
        ImagesThatWork images = new();
        MakeADisc window = Ready(new BuilderThatWorks(), images);

        await window.MakeIt();

        // The folder is the disc and the image is the same disc as one file.
        // Both, every time: the folder is what somebody checks and the image is
        // what they burn.
        Assert.Equal(1, images.Made);
        Assert.Null(window.ItWentWrong);
    }

    [Fact]
    public async Task Gives_every_disc_of_a_set_an_image_of_its_own()
    {
        ImagesThatWork images = new();
        MakeADisc window = New(builder: new ABuilderThatMakesASet(), images: images);
        window.OutputFolder = @"C:\out";
        window.Add(AGame("A Big Game", 1000));

        await window.MakeIt();

        // Every disc of a set carries the same label, so a name made from the
        // label alone lands the second disc's image on top of the first and
        // the set becomes one file that says it is three.
        Assert.Equal(3, images.Made);
        Assert.Equal(3, images.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(images.Paths, path => Assert.EndsWith(".iso", path, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Offers_the_discs_of_a_set_to_burn_in_order()
    {
        MakeADisc window = New(builder: new ABuilderThatMakesASet(), images: new ImagesThatWork());
        window.OutputFolder = @"C:\out";
        window.Add(AGame("A Big Game", 1000));

        Assert.Empty(window.TheSetToBurn);

        await window.MakeIt();

        Assert.Equal(
            [@"C:\out\A BIG GAME D1.iso", @"C:\out\A BIG GAME D2.iso", @"C:\out\A BIG GAME D3.iso"],
            window.TheSetToBurn);
    }

    [Fact]
    public async Task Offers_no_set_when_several_games_each_got_a_disc_of_their_own()
    {
        MakeADisc window = New(builder: new ABuilderThatMakesADiscEach(), images: new ImagesThatWork());
        window.OutputFolder = @"C:\out";
        window.Add(AGame("One", 1000));
        window.Add(AGame("Two", 1000));

        await window.MakeIt();

        // Three discs that do not continue one another are not a set. The
        // window took any build with more than one disc for one, walked them
        // as "disc 1 of 3, disc 2 of 3", and read the second one back against
        // the first game's folder, which called a perfect disc wrong.
        Assert.Equal(3, window.Made!.Discs.Count);
        Assert.Empty(window.TheSetToBurn);
    }

    [Fact]
    public async Task Offers_only_the_set_when_one_game_needed_one_beside_discs_that_stand_alone()
    {
        MakeADisc window = New(builder: new ABuilderThatMakesADiscEachAndOneSet(), images: new ImagesThatWork());
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Small", 1000));
        window.Add(AGame("Big", 1000));

        await window.MakeIt();

        Assert.Equal([@"C:\out\Big\Big D1.iso", @"C:\out\Big\Big D2.iso"], window.TheSetToBurn);
    }

    [Fact]
    public async Task Tells_the_window_about_the_set_to_burn_when_a_build_ends_and_when_it_starts_again()
    {
        MakeADisc window = New(builder: new ABuilderThatMakesASet(), images: new ImagesThatWork());
        window.OutputFolder = @"C:\out";
        window.Add(AGame("A Big Game", 1000));

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        await window.MakeIt();
        Assert.Contains(nameof(MakeADisc.TheSetToBurn), told);

        told.Clear();
        window.StartAgain();
        Assert.Contains(nameof(MakeADisc.TheSetToBurn), told);
        Assert.Empty(window.TheSetToBurn);
    }

    [Fact]
    public void The_window_burns_the_set_the_screen_names_and_decides_nothing_itself()
    {
        // Read off disk, like the other guards over the code behind. The
        // decision about what counts as a set was in the window and wrong.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Assert.Contains("IReadOnlyList<string> set = _screen.TheSetToBurn;", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Discs.Count: > 1", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_reads_a_project_file_before_asking_whether_to_give_anything_up_for_it()
    {
        // Read off disk, like the other guards over the code behind. The
        // question used to come first, so somebody said yes to replacing an
        // afternoon's work and only then met the refusal, with nothing opened
        // and that answer spent.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        int read = code.IndexOf("Outcome<OpenedProject> read = AProjectFile.Read(", StringComparison.Ordinal);
        int asked = code.IndexOf("\"Replace what is on the window?\"", StringComparison.Ordinal);

        Assert.True(read > 0 && asked > 0, "The open handler is not where this looks for it.");
        Assert.True(
            read < asked,
            "The file is read after the question about replacing the window, so a file that is then refused "
                + "cost somebody that answer.");
    }

    [Fact]
    public async Task Says_why_the_image_stopped_even_though_the_disc_was_built()
    {
        MakeADisc window = Ready(new BuilderThatWorks(), new ImagesThatRefuse());

        await window.MakeIt();

        // The staged folder is real and usable; what failed is the one file
        // version of it, and saying so beats saying the build failed.
        Assert.NotNull(window.ItWentWrong);
        Assert.NotNull(window.Made);
    }

    [Fact]
    public async Task Keeps_what_the_image_had_to_say_about_itself()
    {
        MakeADisc window = Ready(
            new BuilderThatWorks(),
            new ImagesThatWork { Worry = "The older file systems were left off" });

        await window.MakeIt();

        // True of the disc somebody now holds, and it will not be said again.
        Assert.Equal("The older file systems were left off", window.AboutTheImage);
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
        await builder.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
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

    // ----------------------------------------------------------- opening one

    [Fact]
    public void Finds_the_images_that_are_already_there_when_one_is_opened()
    {
        MakeADisc window = New(disk: new ADiskWithAnImageOnIt());

        window.OpenThis(AReopenedDisc());

        // Opening a project that was finished and pressing Burn is the whole
        // point of being able to open one. It set the output folder behind
        // the property rather than through it, so nothing went looking, and
        // Burn to a disc sat there dead on a project ready to write.
        Assert.NotEmpty(window.TheImages);
        Assert.True(window.CanBurn);
    }

    // -------------------------------------------- a button that cannot act

    [Fact]
    public void Remove_is_dead_when_there_is_nothing_to_take_off()
    {
        MakeADisc window = New();

        Assert.False(window.CanTakeSomethingOff);

        // Everything on and nothing picked: pressing it would do nothing.
        window.Add(AGame("The Witcher", 500));
        Assert.False(window.CanTakeSomethingOff);
    }

    [Fact]
    public void Remove_wakes_up_for_a_picked_row_and_for_nothing_else()
    {
        MakeADisc window = New();
        window.Add(AGame("The Witcher", 500));

        window.WhatIsSelected = window.WhatGoesOnIt[0];
        Assert.True(window.CanTakeSomethingOff);

        window.WhatIsSelected = null;
        Assert.False(window.CanTakeSomethingOff);

        // It used to wake for this too, and took everything unticked when it
        // was pressed. One button, two meanings, and which one somebody got
        // depended on whether a row happened to be picked.
        window.WhatGoesOnIt[0].IsOn = false;
        Assert.False(window.CanTakeSomethingOff);
    }

    [Fact]
    public void The_button_for_what_is_left_off_is_there_only_when_something_is()
    {
        MakeADisc window = New();
        Assert.Null(window.WhatTakingTheLeftOffOnesSays);

        window.Add(AGame("The Witcher", 500, parts: 3));
        Assert.Null(window.WhatTakingTheLeftOffOnesSays);

        AThingOnTheDisc[] rows = [.. window.WhatGoesOnIt[0].EveryThing()];

        // The count is in the words, because the gesture takes several things
        // at once and none of them is picked, so the button is the only place
        // it can be seen before it happens.
        rows[0].IsOn = false;
        Assert.Equal("Remove the 1 thing left off", window.WhatTakingTheLeftOffOnesSays);

        rows[1].IsOn = false;
        Assert.Equal("Remove the 2 things left off", window.WhatTakingTheLeftOffOnesSays);

        rows[0].IsOn = true;
        rows[1].IsOn = true;
        Assert.Null(window.WhatTakingTheLeftOffOnesSays);
    }

    [Fact]
    public void Says_when_the_button_for_what_is_left_off_should_come_and_go()
    {
        MakeADisc window = New();
        window.Add(AGame("The Witcher", 500, parts: 2));

        List<string> told = [];
        window.PropertyChanged += (_, which) =>
        {
            if (which.PropertyName == nameof(MakeADisc.WhatTakingTheLeftOffOnesSays))
            {
                told.Add(which.PropertyName);
            }
        };

        // A property nobody is told about leaves the button saying whatever it
        // said the first time the window read it, which for this one is a
        // button offering to remove things that are back on.
        window.WhatGoesOnIt[0].EveryThing().First().IsOn = false;

        Assert.NotEmpty(told);
    }

    [Fact]
    public async Task The_drives_stop_moving_the_size_while_something_is_running()
    {
        ADriveHolding drive = new(3, true, 359_847);
        ABuilderThatWaits waits = new();
        MakeADisc window = New(drives: drive, builder: waits);

        await window.LookAtTheDrives();
        Assert.Equal("CD", window.Medium.Key);

        window.OutputFolder = @"C:\out";
        window.Add(AGame("A Game", 1000));

        Task building = window.MakeIt();
        await waits.Started.Task;
        Assert.True(window.IsWorking);

        // A set being burned asks for its next blank. One of a different kind
        // going in would otherwise move the size the window is describing out
        // from under a run of the old one.
        drive.NowHolds(9, true, 2_297_888);
        await window.LookAtTheDrives();

        Assert.Equal("CD", window.Medium.Key);

        waits.LetGo.SetResult();
        await building;

        // And the moment it is over, the drives decide again.
        await window.LookAtTheDrives();
        Assert.Equal("DVD5", window.Medium.Key);
    }

    /// <summary>A build that does not finish until it is let go.</summary>
    private sealed class ABuilderThatWaits : IBuildDiscs
    {
        internal TaskCompletionSource Started { get; } = new();

        internal TaskCompletionSource LetGo { get; } = new();

        public async Task<Outcome<DiscWright.Staging.BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            Started.TrySetResult();
            await LetGo.Task;
            return new DiscWright.Staging.BuiltDiscs([]);
        }
    }

    [Fact]
    public async Task Nothing_that_changes_the_disc_is_live_while_one_is_being_made()
    {
        bool? couldChangeItMidWay = null;
        MakeADisc? window = null;
        window = New(
            drives: new ADriveHolding(3, false, 0),
            eraser: new AnEraserThatAsks(() => couldChangeItMidWay = window!.NothingIsRunning));

        Assert.True(window.NothingIsRunning);

        await window.LookAtTheDrives();
        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        // Adding a game to a disc part way through being written, or pointing
        // the output somewhere else mid-burn, is not a thing to find out
        // about by trying it.
        Assert.False(couldChangeItMidWay);
        Assert.True(window.NothingIsRunning);
    }


    [Fact]
    public void Fills_itself_in_from_a_disc_that_was_made_before()
    {
        MakeADisc window = New();
        window.Add(AGame("Something Else", 500));

        window.OpenThis(AReopenedDisc());

        // Everything, including what it replaced.
        Assert.Equal("AN OLD DISC", window.Label);
        Assert.Equal(@"D:\out", window.OutputFolder);
        Assert.Equal(Media.Dvd9, window.Medium);
        Assert.Equal("The Witcher", Assert.Single(window.Games).Name);
        Assert.True(window.CutFiles);
    }

    [Fact]
    public void Carries_what_it_cannot_show_so_the_disc_comes_out_the_same()
    {
        MakeADisc window = New();

        window.OpenThis(AReopenedDisc());

        // There is no artwork or manual on the window yet. Dropping them
        // because this cannot edit them would mean rebuilding a disc without
        // the cover somebody picked.
        DiscProject again = window.AsAProject();
        Assert.Equal(@"D:\art\icon.png", again.Artwork?.IconPath);
        Assert.Equal(@"D:\manual.pdf", again.Extras?.ManualPath);
        Assert.Equal([@"D:\read me.txt"], again.Extras?.Loose);
        Assert.Single(again.AddOnsOrNone);
    }

    [Fact]
    public void Moves_the_dropdown_when_an_opened_project_names_a_size()
    {
        MakeADisc window = New();
        List<string> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        window.OpenThis(AReopenedDisc());

        // Opening a project sets the size without going through the setter,
        // so the dropdown has to be told separately or it goes on showing
        // whatever was there before the file was opened.
        Assert.Contains(nameof(MakeADisc.TheSizeChosen), told);
        Assert.Equal(window.Medium.Key, window.TheSizeChosen!.Medium.Key);
    }

    [Fact]
    public async Task Counts_the_size_an_opened_project_names_as_chosen()
    {
        // A blank CD in the drive, which would move the size on its own.
        MakeADisc window = New(drives: new ADriveHolding(2, true, 359_847));

        window.OpenThis(AReopenedDisc());
        Assert.Equal(Media.Dvd9, window.Medium);

        await window.LookAtTheDrives();

        // The size it was saved with was somebody's answer once, so the
        // drives only comment on it. Without this the next disc to go in or
        // come out moved the size the file had just chosen.
        Assert.Equal(Media.Dvd9, window.Medium);
    }

    [Fact]
    public async Task Lets_the_drives_choose_when_an_opened_project_was_saved_on_automatic()
    {
        MakeADisc window = New(drives: new ADriveHolding(2, true, 359_847));

        window.OpenThis(AReopenedDisc() with { Medium = null });

        await window.LookAtTheDrives();

        // A project saved on automatic chose nothing, so a disc in the drive
        // is still the best answer.
        Assert.Equal(Media.Cd, window.Medium);
    }

    [Fact]
    public void Leaves_the_disc_size_alone_when_the_file_was_saved_on_automatic()
    {
        MakeADisc window = New();
        window.Medium = Media.Bd25;

        window.OpenThis(AReopenedDisc() with { Medium = null });

        // A project saved on automatic says nothing about it, and the one
        // already on the window is a better answer than a guess.
        Assert.Equal(Media.Bd25, window.Medium);
    }

    [Fact]
    public void Takes_away_what_was_on_the_window_before()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 500));
        window.Add(AGame("Two", 500));
        window.AlsoOnTheDisc([@"C:\something.txt"]);

        window.OpenThis(AReopenedDisc());

        Assert.Single(window.Games);
        Assert.DoesNotContain(@"C:\something.txt", window.AsAProject().Extras?.Loose ?? []);
    }

    // ----------------------------------------------------------------- burning

    [Fact]
    public void Will_not_offer_to_burn_with_no_image_to_burn()
    {
        // An image on disk is the only thing worth checking. Whether a drive
        // exists and what is in it is asked when the button is pressed, because
        // a disc can be put in after the window opened.
        Assert.False(New().CanBurn);
    }

    [Fact]
    public async Task Writes_the_image_to_the_one_drive_that_is_ready()
    {
        ABurnerThatWorks burner = new();
        MakeADisc window = await Built(drives: new TheseDrives(ABlankIn("E:")), burner: burner);

        await window.BurnIt(@"C:\out\A GAME.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        Assert.Equal("E:", burner.WroteTo);
        Assert.Null(window.ItWentWrong);
    }

    [Fact]
    public async Task Reads_the_disc_back_and_says_it_is_the_disc()
    {
        MakeADisc window = await Built(drives: new TheseDrives(ABlankIn("E:")));

        await window.BurnIt(@"C:\out\A GAME.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        // Written is not the same as readable. This is the part that answers
        // the question somebody actually has, holding the disc.
        Assert.NotNull(window.HowTheDiscCameOut);
        Assert.True(window.HowTheDiscCameOut.Good);
    }

    // ------------------------------------- the line about the last burn

    [Fact]
    public async Task Leaves_how_the_disc_came_out_on_the_window()
    {
        MakeADisc window = await Built(drives: new TheseDrives(ABlankIn("E:")));

        await window.BurnIt(@"C:\out\A GAME.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        // The one sentence somebody holding the disc wants to look at twice,
        // so it is a line that stays rather than a box that is gone at OK.
        Assert.Equal(window.HowTheDiscCameOut?.InWords, window.HowTheBurnWent);
        Assert.StartsWith("All ", window.HowTheBurnWent, StringComparison.Ordinal);
        Assert.False(window.TheBurnWentBadly);
    }

    [Fact]
    public async Task Says_nothing_about_a_burn_before_there_has_been_one()
    {
        MakeADisc window = await Built(drives: new TheseDrives(ABlankIn("E:")));

        Assert.Equal(string.Empty, window.HowTheBurnWent);
        Assert.False(window.TheBurnWentBadly);
    }

    [Fact]
    public async Task Starting_a_new_disc_takes_the_line_about_the_last_burn_with_it()
    {
        MakeADisc window = await Built(drives: new TheseDrives(ABlankIn("E:")));
        await window.BurnIt(@"C:\out\A GAME.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        List<string> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        window.StartAgain();

        Assert.Equal(string.Empty, window.HowTheBurnWent);
        Assert.Contains(nameof(MakeADisc.HowTheBurnWent), told);
    }

    [Fact]
    public async Task Building_again_takes_the_line_about_the_last_burn_with_it()
    {
        MakeADisc window = await Built(drives: new TheseDrives(ABlankIn("E:")));
        await window.BurnIt(@"C:\out\A GAME.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        List<string> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        // The discs this line was about are about to be built again, so it
        // goes the way the drive's note goes, and says so.
        await window.MakeIt();

        Assert.Equal(string.Empty, window.HowTheBurnWent);
        Assert.Null(window.HowTheDiscCameOut);
        Assert.Contains(nameof(MakeADisc.HowTheBurnWent), told);
        Assert.Contains(nameof(MakeADisc.HowTheDiscCameOut), told);
    }

    [Fact]
    public void The_burn_button_leaves_its_verdict_on_the_window_rather_than_in_a_box()
    {
        // The screen says how the disc or the set came out and the window
        // shows it as a line that stays. Nothing in this project can reference
        // the code behind, so the handler is read off disk, like the markup
        // guards, and must put up no box of its own about it.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        int from = code.IndexOf("async void BurnIt(", StringComparison.Ordinal);
        Assert.True(from >= 0, "The window no longer has a Burn handler.");

        string handler = code[from..];
        handler = handler[..handler.IndexOf(Environment.NewLine + "    }", StringComparison.Ordinal)];

        Assert.DoesNotContain("MessageBox", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("HowTheDiscCameOut", handler, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checks_the_disc_that_was_burned_not_whichever_was_built_first()
    {
        ADiskThatRemembers disk = new();
        MakeADisc window = New(
            builder: new ABuilderThatMakesASet(),
            drives: new TheseDrives(ABlankIn("E:")),
            disk: disk);

        window.OutputFolder = @"C:\out";
        window.Label = "A Game";
        window.Add(AGame("A Game", 1000));
        await window.MakeIt();

        await window.BurnIt(@"C:\out\A Game D2.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        // This took the first disc of the set every time, so burning disc 2
        // compared it against disc 1 and called every file on it wrong. That
        // looks exactly like a ruined disc, and would send somebody to throw
        // away a disc that is perfect.
        Assert.Contains(disk.Listed, where => where.EndsWith("disc D2", StringComparison.Ordinal));
        Assert.DoesNotContain(disk.Listed, where => where.EndsWith("disc D1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Ticks_the_set_note_off_for_a_disc_burned_on_its_own()
    {
        ANoteInMemory note = new(
            new TheNoteBesideTheDiscs(
                "A GAME",
                [new NotedImage("A GAME D1.iso", 1000), new NotedImage("A GAME D2.iso", 1000)],
                1).AsText());

        MakeADisc window = New(
            drives: new TheseDrives(ABlankIn("E:")),
            disk: new ASetOfTwoWithADiscInTheDrive(),
            hasher: new AHasherThatAgrees(),
            note: note);

        window.OutputFolder = @"C:\out";
        window.Label = "A GAME";

        await window.BurnIt(@"C:\out\A GAME D2.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        Assert.True(window.HowTheDiscCameOut?.Good);

        // Only walking a set through from the beginning ever moved this note
        // on. Finishing one over two evenings, which is how anybody finishes
        // one, left it saying the last disc was still to do however well that
        // disc read back. Lazar had exactly this: disc 2 verified against its
        // own checksums and the note beside it went on saying "to do".
        TheNoteBesideTheDiscs moved = Assert.IsType<TheNoteBesideTheDiscs>(
            TheNoteBesideTheDiscs.Read(note.Note));

        Assert.Equal(2, moved.Written);
        Assert.False(moved.PartWayThrough);
    }

    [Fact]
    public void Offers_the_set_the_folder_holds_when_this_sitting_built_nothing()
    {
        MakeADisc window = New(disk: new ASetOfTwoWithADiscInTheDrive());

        window.OutputFolder = @"C:\out";
        Assert.Empty(window.TheSetToBurn);

        window.Label = "A GAME";

        // Opening a finished project and pressing Burn is the ordinary way to
        // finish a set. It got one image at a time from a list that did not
        // say which were done, and the note beside the discs was never
        // offered, because the window knew a set only from a build.
        Assert.Equal([@"C:\out\A GAME D1.iso", @"C:\out\A GAME D2.iso"], window.TheSetToBurn);
    }

    [Fact]
    public void Tells_the_window_about_the_set_in_the_folder_as_the_name_and_the_folder_change()
    {
        MakeADisc window = New(disk: new ASetOfTwoWithADiscInTheDrive());
        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.OutputFolder = @"C:\out";
        Assert.Contains(nameof(MakeADisc.TheSetToBurn), told);

        told.Clear();
        window.Label = "A GAME";
        Assert.Contains(nameof(MakeADisc.TheSetToBurn), told);
    }

    [Fact]
    public async Task Walks_a_reopened_set_and_offers_to_carry_on_where_the_note_says()
    {
        ANoteInMemory note = new(
            new TheNoteBesideTheDiscs(
                "A GAME",
                [new NotedImage("A GAME D1.iso", 1000), new NotedImage("A GAME D2.iso", 1000)],
                1).AsText());
        ABurnerThatWorks burner = new();

        MakeADisc window = New(
            drives: new TheseDrives(ABlankIn("E:")),
            disk: new ASetOfTwoWithADiscInTheDrive(),
            hasher: new AHasherThatAgrees(),
            burner: burner,
            note: note);

        window.OutputFolder = @"C:\out";
        window.Label = "A GAME";

        (int number, int of) asked = (0, 0);

        await window.BurnTheSet(
            window.TheSetToBurn, NeverAsked, NeverAskedAboutErasing, Closes,
            (number, of, drive) => Task.FromResult(true),
            (number, of) =>
            {
                asked = (number, of);
                return Task.FromResult(true);
            });

        // Asked to carry on from disc 2, which is what the note knows, and
        // the set is then whole.
        Assert.Equal((2, 2), asked);
        Assert.Equal("E:", burner.WroteTo);
        Assert.True(window.HowTheSetWent?.Whole);
        Assert.Equal(1, window.HowTheSetWent?.BroughtForward);
    }

    [Fact]
    public async Task Leaves_the_note_alone_when_the_disc_did_not_read_back()
    {
        ANoteInMemory note = new(
            new TheNoteBesideTheDiscs(
                "A GAME",
                [new NotedImage("A GAME D1.iso", 1000), new NotedImage("A GAME D2.iso", 1000)],
                1).AsText());

        MakeADisc window = New(
            drives: new TheseDrives(ABlankIn("E:")),
            disk: new ASetOfTwoWithADiscInTheDrive(),

            // Agrees with nothing, so every file on the disc comes back wrong.
            hasher: new NoHashing(),
            note: note);

        window.OutputFolder = @"C:\out";
        window.Label = "A GAME";

        await window.BurnIt(@"C:\out\A GAME D2.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        Assert.False(window.HowTheDiscCameOut?.Good);

        // And the line that stays says so, in worry ink.
        Assert.Contains("came back different", window.HowTheBurnWent, StringComparison.Ordinal);
        Assert.True(window.TheBurnWentBadly);

        // The whole point of the note is that it is believed later. One
        // written for a disc that did not read back would send somebody away
        // with a coaster in the set.
        Assert.Equal(1, TheNoteBesideTheDiscs.Read(note.Note)!.Written);
    }

    [Fact]
    public async Task Leaves_a_note_about_some_other_build_alone()
    {
        // The sizes do not match what is in the folder now, which is how a
        // note is already told to be stale when deciding whether to carry on
        // from it. Writing to it would make an old note look current.
        ANoteInMemory note = new(
            new TheNoteBesideTheDiscs(
                "A GAME",
                [new NotedImage("A GAME D1.iso", 999), new NotedImage("A GAME D2.iso", 999)],
                1).AsText());

        MakeADisc window = New(
            drives: new TheseDrives(ABlankIn("E:")),
            disk: new ASetOfTwoWithADiscInTheDrive(),
            hasher: new AHasherThatAgrees(),
            note: note);

        window.OutputFolder = @"C:\out";
        window.Label = "A GAME";

        await window.BurnIt(@"C:\out\A GAME D2.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        Assert.True(window.HowTheDiscCameOut?.Good);
        Assert.Equal(1, TheNoteBesideTheDiscs.Read(note.Note)!.Written);
    }

    [Fact]
    public async Task Checks_a_disc_against_its_own_list_when_nothing_was_built_here()
    {
        MakeADisc window = New(
            drives: new TheseDrives(ABlankIn("E:")),
            disk: new ADiscCarryingItsOwnList(),
            hasher: new AHasherThatAgrees());

        await window.BurnIt(@"C:\out\A GAME.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        // Opening a finished project and pressing Burn is the ordinary way to
        // finish a set, and it left nothing built in this sitting to compare
        // against. A disc that read back perfectly was reported as written
        // but not checked, and Lazar's disc 2 was exactly that: eleven files,
        // every one correct, and the window would not say so.
        Assert.NotNull(window.HowTheDiscCameOut);
        Assert.True(window.HowTheDiscCameOut.Good);
        Assert.Null(window.ItWentWrong);
    }

    [Fact]
    public async Task Will_not_claim_a_disc_is_good_when_there_is_nothing_to_compare_it_with()
    {
        // Burning an image with no record of what was built: the disc may be
        // perfect, and nothing here can say so.
        MakeADisc window = New(drives: new TheseDrives(ABlankIn("E:")));

        await window.BurnIt(@"C:\out\A GAME.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        Assert.Null(window.HowTheDiscCameOut);
        Assert.NotNull(window.ItWentWrong);
        Assert.Contains("not checked", window.ItWentWrong.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asks_which_drive_when_two_of_them_are_ready()
    {
        ABurnerThatWorks burner = new();
        MakeADisc window = New(drives: new TheseDrives(ABlankIn("D:"), ABlankIn("E:")), burner: burner);

        bool asked = false;

        await window.BurnIt(
            @"C:\out\A GAME.iso",
            ready =>
            {
                asked = true;
                Assert.Equal(2, ready.Count);
                return Task.FromResult<Recorder?>(ready[1]);
            },
            NeverAsked,
            Closes);

        // Choosing for somebody is how a disc ends up written in the wrong
        // drive, and the one in the other drive is often their only blank.
        Assert.True(asked);
        Assert.Equal("E:", burner.WroteTo);
    }

    [Fact]
    public async Task Writes_nothing_when_nobody_picks_a_drive()
    {
        ABurnerThatWorks burner = new();
        MakeADisc window = New(drives: new TheseDrives(ABlankIn("D:"), ABlankIn("E:")), burner: burner);

        await window.BurnIt(@"C:\out\A GAME.iso", _ => Task.FromResult<Recorder?>(null), NeverAskedAboutErasing, Closes);

        Assert.Null(burner.WroteTo);
    }

    [Fact]
    public async Task Offers_to_erase_a_disc_that_is_in_the_way()
    {
        AnEraserThatWorks eraser = new();
        ABurnerThatWorks burner = new();

        // A CD-RW with something on it, which becomes a blank once erased.
        TheseDrives drives = new(AUsedCdRwIn("E:"));
        drives.AfterErasing = ABlankIn("E:");

        MakeADisc window = await Built(drives: drives, burner: burner, eraser: eraser);

        bool asked = false;

        await window.BurnIt(
            @"C:\out\A GAME.iso",
            NeverAsked,
            could =>
            {
                asked = true;
                Assert.Equal("E:", Assert.Single(could).Drive);
                return Task.FromResult<Recorder?>(could[0]);
            },
            Closes);

        Assert.True(asked);
        Assert.Equal("E:", eraser.Erased);
        Assert.Equal("E:", burner.WroteTo);
    }

    [Fact]
    public async Task Asks_which_disc_to_erase_when_either_of_two_could_go()
    {
        AnEraserThatWorks eraser = new();
        ABurnerThatWorks burner = new();

        TheseDrives drives = new(AUsedCdRwIn("D:"), AUsedCdRwIn("E:"));
        drives.AfterErasing = ABlankIn("E:");

        MakeADisc window = await Built(drives: drives, burner: burner, eraser: eraser);

        IReadOnlyList<Recorder>? offered = null;

        await window.BurnIt(
            @"C:\out\A GAME.iso",
            NeverAsked,
            could =>
            {
                offered = could;
                return Task.FromResult<Recorder?>(could.First(each => each.Drive == "E:"));
            },
            Closes);

        // Both are offered, because which disc to destroy is not a choice to
        // make on somebody's behalf. Before this, the burn refused with "that
        // disc cannot be written to", which is false about a rewritable one.
        Assert.NotNull(offered);
        Assert.Equal(["D:", "E:"], offered.Select(each => each.Drive));
        Assert.Equal("E:", eraser.Erased);
        Assert.Equal("E:", burner.WroteTo);
    }

    [Fact]
    public async Task Erases_nothing_when_the_answer_is_no()
    {
        AnEraserThatWorks eraser = new();
        ABurnerThatWorks burner = new();

        MakeADisc window = New(drives: new TheseDrives(AUsedCdRwIn("E:")), burner: burner, eraser: eraser);

        await window.BurnIt(
            @"C:\out\A GAME.iso", NeverAsked, _ => Task.FromResult<Recorder?>(null), Closes);

        // Whatever is on that disc goes, so it is never assumed.
        Assert.Null(eraser.Erased);
        Assert.Null(burner.WroteTo);
        Assert.NotNull(window.ItWentWrong);
    }

    [Fact]
    public async Task Says_so_when_there_is_no_drive_at_all()
    {
        MakeADisc window = New(drives: new NoDrives());

        await window.BurnIt(@"C:\out\A GAME.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        Assert.NotNull(window.ItWentWrong);
    }

    [Fact]
    public async Task Says_a_disc_was_written_but_not_checked_when_it_never_comes_back()
    {
        MakeADisc window = New(drives: new TheseDrives(ABlankIn("E:")));

        // The tray never shuts, so nothing can be read back.
        await window.BurnIt(@"C:\out\A GAME.iso", NeverAsked, NeverAskedAboutErasing, _ => Task.FromResult(false));

        // Not a failure of the burn. What is missing is the proof, and saying
        // "done" without it would be claiming something nobody checked.
        Assert.NotNull(window.ItWentWrong);
        Assert.Contains("not checked", window.ItWentWrong.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Keeps_what_the_drive_said_about_its_own_reporting()
    {
        MakeADisc window = New(drives: new TheseDrives(ABlankIn("E:")), burner: new ABurnerThatWorks());

        await window.BurnIt(@"C:\out\A GAME.iso", NeverAsked, NeverAskedAboutErasing, Closes);

        // Said after every burn, not only the bad ones: a quiet drive and a
        // listener that never worked look the same on screen.
        Assert.NotNull(window.AboutTheImage);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A window with everything filled in, ready to press.</summary>
    // ---------------------------------------- what else goes on the disc

    [Fact]
    public void Can_be_told_about_the_things_that_are_not_games()
    {
        MakeADisc window = New();

        // Every one of these was reachable only by OPENING a project somebody
        // else had made. The pipeline has handled all of it from the start and
        // there was no way in from the window, so a disc made here could never
        // have had a background, an icon, music, a manual or extras.
        window.DiscIcon = @"C:\art\icon.png";
        window.Background = @"C:\art\bg.png";
        window.Music = @"C:\sound\theme.mp3";
        window.Manual = @"C:\things\manual.pdf";
        window.ExtrasFolder = @"C:\things\extras";
        window.Cover = @"C:\art\cover.png";
        window.DiscFace = @"C:\art\face.png";

        DiscProject project = window.AsAProject();

        Assert.Equal(@"C:\art\icon.png", project.Artwork?.IconPath);
        Assert.Equal(@"C:\art\bg.png", project.Artwork?.BackgroundPath);
        Assert.Equal(@"C:\art\cover.png", project.Artwork?.CoverPath);
        Assert.Equal(@"C:\art\face.png", project.Artwork?.DiscFacePath);
        Assert.Equal(@"C:\sound\theme.mp3", project.Extras?.MusicPath);
        Assert.Equal(@"C:\things\manual.pdf", project.Extras?.ManualPath);
        Assert.Equal(@"C:\things\extras", project.Extras?.ExtrasFolderPath);
    }

    [Fact]
    public void Carries_the_music_into_the_project_at_all()
    {
        MakeADisc window = New();

        // It did not. AsAProject built its extras without the music, so a
        // project saved with music lost it the moment it was opened again,
        // and nothing could have put any there in the first place.
        window.Music = @"C:\sound\theme.mp3";

        Assert.Equal(@"C:\sound\theme.mp3", window.AsAProject().Extras?.MusicPath);
    }

    [Fact]
    public void Forgets_a_picture_that_is_cleared_rather_than_keeping_an_empty_one()
    {
        MakeADisc window = New();
        window.DiscIcon = @"C:\art\icon.png";

        window.DiscIcon = null;

        // An artwork record holding nothing but empty strings is not the same
        // as no artwork, and the steps ask whether there IS any.
        Assert.Null(window.AsAProject().Artwork);
    }

    [Fact]
    public void Says_why_a_picture_cannot_be_used_when_it_is_picked()
    {
        MakeADisc window = New(pictures: new PicturesThatRead { Refuse = "Windows cannot read that file." });

        Assert.False(window.ThatPictureIsUsable(@"C:\art\broken.png"));
        Assert.Equal("Windows cannot read that file.", window.WhyNotThatPicture);
    }

    [Fact]
    public void Says_nothing_about_a_picture_that_is_fine()
    {
        MakeADisc window = New();

        Assert.True(window.ThatPictureIsUsable(@"C:\art\icon.png"));
        Assert.Null(window.WhyNotThatPicture);
    }

    [Fact]
    public void Starting_a_new_disc_forgets_the_artwork_and_the_extras()
    {
        MakeADisc window = New();
        window.DiscIcon = @"C:\art\icon.png";
        window.Music = @"C:\sound\theme.mp3";
        window.Manual = @"C:\things\manual.pdf";

        window.StartAgain();

        Assert.Null(window.DiscIcon);
        Assert.Null(window.Music);
        Assert.Null(window.Manual);
        Assert.False(window.HasAnythingOnIt);
    }

    // ------------------------------------------------------- More options

    [Fact]
    public async Task Asks_for_the_older_file_systems_only_when_that_was_asked()
    {
        ImagesThatWork images = new();
        MakeADisc window = Ready(new BuilderThatWorks(), images);

        await window.MakeIt();
        Assert.Equal([false], images.Older);

        window.LegacyFileSystems = true;
        await window.MakeIt();

        // The checkbox is the whole of this option: nothing else in the
        // program reads it, and an image made without the flag is readable on
        // Windows 11 and on nothing older.
        Assert.Equal([false, true], images.Older);
    }

    [Fact]
    public async Task Every_option_in_the_drawer_starts_where_it_should()
    {
        MakeADisc window = New();

        // A menu and a checksum list and the cover art are what almost
        // everybody wants. The other two cost something: one makes a disc an
        // older Windows can read at the price of shorter names, and the other
        // splits a file across two discs, which is only ever a last resort.
        Assert.True(window.Menu);
        Assert.True(window.Checksums);
        // The cover art always goes on now, so there is no setting left to
        // read back.
        Assert.False(window.LegacyFileSystems);
        Assert.False(window.CutFiles);
    }

    // ------------------------------------------------------ which drive

    [Fact]
    public async Task Offers_every_writer_on_the_machine()
    {
        MakeADisc window = New(drives: new TwoDrives());

        Assert.Empty(window.Drives);

        await window.LookAtTheDrives();

        Assert.Equal(["D:", "E:"], window.Drives.Select(each => each.Drive));

        // Nothing chosen is the right start. A machine with one writer has no
        // question to answer, and asking somebody to pick the only drive they
        // own is a decision shaped like a chore.
        Assert.Null(window.TheDrive);
    }

    [Fact]
    public async Task Moves_the_dropdown_when_a_disc_in_a_drive_settles_the_size()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, true, 359_847));
        List<string> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        await window.LookAtTheDrives();

        // A CD in the drive settles the size on its own, behind the setter so
        // that it does not count as a person choosing. The dropdown still has
        // to be told, or it reads DVD5 over a disc that holds a fifth of that.
        Assert.Equal(Media.Cd.Key, window.Medium.Key);
        Assert.Contains(nameof(MakeADisc.TheSizeChosen), told);
        Assert.Equal(Media.Cd.Key, window.TheSizeChosen!.Medium.Key);
    }

    [Fact]
    public async Task Keeps_the_chosen_drive_when_the_window_drops_it_as_the_list_is_replaced()
    {
        MakeADisc window = New(drives: new TwoDrives());
        await window.LookAtTheDrives();
        window.TheDrive = window.Drives.First(each => each.Drive == "E:");

        // What the real picker does. It is bound two ways to the drive and
        // to the list, and a fresh look hands it fresh records, none equal to
        // the one picked, so inside the announcement of the list it finds its
        // selection gone and writes null straight back. Measured with a
        // window bound exactly the way the main window is, and this is why
        // a drive chosen before a burn was forgotten the moment a blank went
        // in.
        window.PropertyChanged += (_, what) =>
        {
            if (what.PropertyName == nameof(MakeADisc.Drives))
            {
                window.TheDrive = null;
            }
        };

        await window.LookAtTheDrives();

        Assert.Equal("E:", window.TheDrive?.Drive);
    }

    [Fact]
    public async Task Forgets_a_drive_that_has_been_unplugged()
    {
        ADriveHolding one = new(3, true, 359_847);
        MakeADisc window = New(drives: one);

        await window.LookAtTheDrives();
        window.TheDrive = window.Drives[0];

        one.NowHolds(0, false, 0);
        one.Vanish();
        await window.LookAtTheDrives();

        // The slim writer here lives on a USB cable, and a drive that is not
        // there is not a choice.
        Assert.Null(window.TheDrive);
    }

    [Fact]
    public async Task Keeps_a_chosen_drive_up_to_date_with_what_is_in_it()
    {
        ADriveHolding one = new(0, false, 0);
        MakeADisc window = New(drives: one);

        await window.LookAtTheDrives();
        window.TheDrive = window.Drives[0];
        Assert.False(window.TheDrive!.Ready);

        one.NowHolds(3, true, 359_847);
        await window.LookAtTheDrives();

        // Kept by letter rather than by the record, because what is in a drive
        // has almost certainly changed since it was picked.
        Assert.True(window.TheDrive!.Ready);
    }

    [Fact]
    public async Task Burns_to_the_chosen_drive_without_asking_which()
    {
        ABurnerThatWorks burner = new();
        MakeADisc window = New(drives: new TwoDrives(), burner: burner);
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));

        await window.LookAtTheDrives();
        window.TheDrive = window.Drives.First(each => each.Drive == "E:");

        await window.BurnIt(
            @"C:\out\A GAME.iso",
            _ => throw new InvalidOperationException("It should not have had to ask which drive."),
            NeverAskedAboutErasing,
            Closes);

        Assert.Equal("E:", burner.WroteTo);
    }

    [Fact]
    public async Task Says_nothing_about_the_tray_of_a_drive_nobody_chose()
    {
        ATrayThatNeedsAPush tray = new();
        MakeADisc window = New(drives: new TwoDrives(), tray: tray);

        await window.LookAtTheDrives();
        Assert.StartsWith("D: and E:", window.AboutTheTray, StringComparison.Ordinal);

        window.TheDrive = window.Drives.First(each => each.Drive == "E:");
        await window.LookAtTheDrives();

        // A sentence about the drive somebody is not using is noise.
        Assert.Equal("E: needs the tray pushed in", window.AboutTheTray);
    }

    // --------------------------------------------- the disc that is in there

    [Fact]
    public async Task Starts_from_the_size_of_the_disc_that_is_already_in_a_drive()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, true, 359_847));

        // Before anything is asked, the default is the old one for everybody.
        Assert.Equal("DVD5", window.Medium.Key);

        await window.LookAtTheDrives();

        // A 700 MB disc in the tray planned as one 4.7 GB disc is how a set
        // came to be built as a single disc nothing would fit on.
        Assert.Equal("CD", window.Medium.Key);
        Assert.Equal(string.Empty, window.AboutTheDiscInTheDrive);
        Assert.False(window.TheDiscIsADifferentSize);
    }

    [Fact]
    public async Task Follows_the_disc_again_when_a_different_one_goes_in()
    {
        ADriveHolding drive = new(3, true, 359_847);
        MakeADisc window = New(drives: drive);

        await window.LookAtTheDrives();
        Assert.Equal("CD", window.Medium.Key);

        drive.NowHolds(9, true, 2_298_496);
        await window.LookAtTheDrives();

        // Still nobody has chosen a size, so taking a CD out and putting a DVD
        // in is still the drives answering the question.
        Assert.Equal("DVD5", window.Medium.Key);
    }

    [Fact]
    public async Task A_different_disc_changes_everything_the_size_decides()
    {
        ADriveHolding drive = new(3, true, 359_847);
        MakeADisc window = New(drives: drive);
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Big", 1_000_000_000));

        await window.LookAtTheDrives();
        Assert.Equal("CD", window.Medium.Key);

        // A gigabyte will not go on a CD at all, and the box that would cut
        // it is not ticked, so this is a refusal rather than a longer set.
        Assert.NotNull(window.WhyNot);
        string onACd = window.WhatItWillMake;

        drive.NowHolds(9, true, 2_298_496);
        await window.LookAtTheDrives();

        Assert.Equal("DVD5", window.Medium.Key);

        // The size is only the half of it. How many discs it takes, whether
        // anything has to be cut and whether it fits at all are all worked
        // out FROM the size, and none of them were worked out again, so the
        // window went on answering the question the old disc had asked.
        Assert.NotEqual(onACd, window.WhatItWillMake);
        Assert.Equal("one disc", window.WhatItWillMake);
        Assert.Null(window.WhyNot);
    }

    [Fact]
    public async Task A_disc_going_in_tells_the_window_about_the_plan_as_well()
    {
        ADriveHolding drive = new(3, true, 359_847);
        MakeADisc window = New(drives: drive);
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Big", 1_000_000_000));

        await window.LookAtTheDrives();

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        drive.NowHolds(9, true, 2_298_496);
        await window.LookAtTheDrives();

        // Asking the properties is not enough. Nothing asks them twice: the
        // red line and the count of discs are drawn once and redrawn only
        // when they are told, so a swap that recomputes in silence leaves
        // the old answer on the screen.
        Assert.Contains(nameof(MakeADisc.WhatItWillMake), told);
        Assert.Contains(nameof(MakeADisc.WhyNot), told);
        Assert.Contains(nameof(MakeADisc.CanMakeIt), told);
    }

    [Fact]
    public async Task Leaves_a_size_alone_once_somebody_has_chosen_one()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, true, 359_847));

        window.Medium = Media.Bd25;
        await window.LookAtTheDrives();

        // Somebody planning a Blu-ray with last week's CD still in the drive
        // is doing something perfectly ordinary.
        Assert.Equal("BD25", window.Medium.Key);
    }

    [Fact]
    public async Task Says_what_is_in_the_drive_when_it_is_not_what_was_chosen()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, true, 359_847));

        window.Medium = Media.Dvd5;
        await window.LookAtTheDrives();

        Assert.True(window.TheDiscIsADifferentSize);
        Assert.Equal(
            "The disc in D: is a CD-RW, and this is being built for DVD5 4.7 GB.",
            window.AboutTheDiscInTheDrive);
    }

    [Fact]
    public async Task Says_so_again_the_moment_the_size_is_changed()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, true, 359_847));

        await window.LookAtTheDrives();
        Assert.Equal(string.Empty, window.AboutTheDiscInTheDrive);

        window.Medium = Media.Bd50;

        // Without looking at a drive again, because the disc has not moved and
        // asking one is not free.
        Assert.True(window.TheDiscIsADifferentSize);
    }

    [Fact]
    public async Task Starting_a_new_disc_lets_the_drives_decide_again()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, true, 359_847));

        // On a window with something on it, because Start again is dead on an
        // empty one and a dead button does nothing. Choosing a size is not
        // something on the disc: it is how this person works.
        window.Add(AGame("A Game", 1000));
        window.Medium = Media.Bd25;

        Assert.True(window.CanStartAgain);
        window.StartAgain();
        await window.LookAtTheDrives();

        // A new disc is a new question.
        Assert.Equal("CD", window.Medium.Key);
    }

    [Fact]
    public async Task Says_nothing_and_changes_nothing_when_there_is_no_disc_in_anything()
    {
        MakeADisc window = New(drives: new ADriveHolding(0, false, 0));

        await window.LookAtTheDrives();

        Assert.Equal("DVD5", window.Medium.Key);
        Assert.Equal(string.Empty, window.AboutTheDiscInTheDrive);
    }

    /// <summary>A drive that pulls its own tray in, like most of them.</summary>
    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
    }

    /// <summary>A slim one somebody has to push.</summary>
    private sealed class ATrayThatNeedsAPush : IWorkTheTray
    {
        internal List<string> Asked { get; } = [];

        public bool ShutIt(Recorder drive) => false;

        public bool CanShutItsOwnTray(Recorder drive)
        {
            Asked.Add(drive.Drive);
            return false;
        }
    }

    /// <summary>
    /// One drive that shuts its own tray and one that does not, which is the
    /// machine this was found on: D: shuts its own and the Blu-ray writer
    /// never does.
    /// </summary>
    private sealed class OnlyOneOfThemShuts : IWorkTheTray
    {
        internal List<string> Asked { get; } = [];

        public bool ShutIt(Recorder drive) => drive.Drive == "D:";

        public bool CanShutItsOwnTray(Recorder drive)
        {
            Asked.Add(drive.Drive);
            return drive.Drive == "D:";
        }
    }

    [Fact]
    public async Task Choosing_a_different_drive_moves_the_tray_warning_with_it()
    {
        OnlyOneOfThemShuts tray = new();
        MakeADisc window = New(drives: new TwoDrives(), tray: tray);

        await window.LookAtTheDrives();
        Assert.Equal("E: needs the tray pushed in", window.AboutTheTray);

        window.TheDrive = window.Drives.First(each => each.Drive == "D:");

        // D: shuts its own tray, so there is nobody to keep in the room. Left
        // over from the drive chosen a moment ago, this would have somebody
        // standing by a drive that is never going to ask for them.
        Assert.Equal(string.Empty, window.AboutTheTray);

        window.TheDrive = window.Drives.First(each => each.Drive == "E:");

        Assert.Equal("E: needs the tray pushed in", window.AboutTheTray);
    }

    [Fact]
    public async Task Choosing_a_different_drive_goes_nowhere_near_a_drive()
    {
        OnlyOneOfThemShuts tray = new();
        MakeADisc window = New(drives: new TwoDrives(), tray: tray);

        await window.LookAtTheDrives();

        // Every drive worth asking is asked once, while the window is not
        // waiting on it.
        Assert.Equal(["D:", "E:"], tray.Asked);

        window.TheDrive = window.Drives.First(each => each.Drive == "D:");
        window.TheDrive = window.Drives.First(each => each.Drive == "E:");

        // Choosing is a click, and a drive that has spun down takes seconds
        // to answer. Whether a drive can shut its own tray is about the
        // drive and not about the disc, so the answer keeps.
        Assert.Equal(["D:", "E:"], tray.Asked);
    }

    [Fact]
    public async Task Tells_the_window_the_tray_warning_moved()
    {
        OnlyOneOfThemShuts tray = new();
        MakeADisc window = New(drives: new TwoDrives(), tray: tray);

        await window.LookAtTheDrives();

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.TheDrive = window.Drives.First(each => each.Drive == "D:");

        // The warning and the tooltip that carries the reason both move, and
        // a binding never told stays on the sentence before last.
        Assert.Contains(nameof(MakeADisc.AboutTheTray), told);
        Assert.Contains(nameof(MakeADisc.WhatTheDrivesAreDoing), told);
    }

    /// <summary>One drive, with whatever is said to be in it at the moment.</summary>
    private sealed class ADriveHolding(int mediaType, bool blank, long freeSectors) : IFindRecorders
    {
        private int _mediaType = mediaType;
        private bool _blank = blank;
        private long _freeSectors = freeSectors;

        internal void NowHolds(int type, bool isBlank, long free)
        {
            _mediaType = type;
            _blank = isBlank;
            _freeSectors = free;
        }

        private bool _gone;

        internal void Vanish() => _gone = true;

        public IReadOnlyList<Recorder> All() =>
            _gone ? [] : [new("id-D", "D:", "A Drive", _mediaType, _blank, _freeSectors, [750])];
    }

    /// <summary>
    /// A drive that says nothing the first few times it is asked, the way one
    /// does while it is still reading a disc somebody just pushed in.
    /// </summary>
    private sealed class ADriveStillThinking(int quietTimes, int mediaType, bool blank, long freeSectors)
        : IFindRecorders
    {
        internal int Asked { get; private set; }

        public IReadOnlyList<Recorder> All()
        {
            Asked++;

            return Asked <= quietTimes
                ? [new("id-D", "D:", "A Drive", 0, false, 0, [], "the device is not ready")]
                : [new("id-D", "D:", "A Drive", mediaType, blank, freeSectors, [750])];
        }
    }

    /// <summary>Two writers, which is the only time there is a question.</summary>
    private sealed class TwoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
        [
            new("id-D", "D:", "A Drive", 3, true, 359_847, [750]),
            new("id-E", "E:", "Another Drive", 3, true, 359_847, [750])
        ];
    }

    /// <summary>A blank DVD in D: and a used CD-RW in E:, as on the machine.</summary>
    private sealed class ADvdAndAUsedCd : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
        [
            new("id-D", "D:", "A Drive", 9, true, 2_297_888, [750]),
            new("id-E", "E:", "Another Drive", 3, false, 0, [750])
        ];
    }

    [Fact]
    public async Task Warns_about_the_disc_in_the_drive_that_would_take_the_burn()
    {
        MakeADisc window = New(drives: new ADvdAndAUsedCd());

        await window.LookAtTheDrives();
        window.Medium = Media.Cd;

        // The reported case. A used CD-RW in the other drive was the size
        // being aimed at, so nothing was said at all, while the drive that
        // would actually take the burn held a DVD.
        Assert.Equal(
            "The disc in D: is a DVD-R, and this is being built for CD-R 700 MB.",
            window.AboutTheDiscInTheDrive);

        Assert.True(window.TheDiscIsADifferentSize);
    }

    [Fact]
    public async Task Asks_about_the_disc_again_when_a_different_drive_is_chosen()
    {
        MakeADisc window = New(drives: new ADvdAndAUsedCd());

        await window.LookAtTheDrives();
        window.Medium = Media.Cd;
        Assert.Contains("D:", window.AboutTheDiscInTheDrive, StringComparison.Ordinal);

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.TheDrive = window.Drives.First(each => each.Drive == "E:");

        // E: holds a CD-RW and a CD is what is being built for, so there is
        // nothing left to say. Said about the drive somebody is not using,
        // this sentence is noise, and left over from the drive they were
        // using a moment ago it is worse than noise.
        Assert.Equal(string.Empty, window.AboutTheDiscInTheDrive);
        Assert.False(window.TheDiscIsADifferentSize);
        Assert.Contains(nameof(MakeADisc.AboutTheDiscInTheDrive), told);
    }

    [Fact]
    public async Task Says_before_anything_is_written_that_a_tray_needs_a_push()
    {
        ATrayThatNeedsAPush tray = new();
        MakeADisc window = New(drives: new ADriveHolding(3, true, 359_847), tray: tray);

        Assert.Equal(string.Empty, window.AboutTheTray);

        await window.LookAtTheDrives();

        // Told afterwards is told to an empty chair.
        Assert.Equal("D: needs the tray pushed in", window.AboutTheTray);
        Assert.Equal(["D:"], tray.Asked);
    }

    [Fact]
    public async Task Says_nothing_about_trays_when_no_drive_needs_a_hand()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, true, 359_847));

        await window.LookAtTheDrives();

        Assert.Equal(string.Empty, window.AboutTheTray);
    }

    [Fact]
    public async Task Hovering_the_picker_with_nothing_wrong_says_one_thing_once()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, true, 359_847));

        await window.LookAtTheDrives();

        // It read ". Every disc has to come out to be checked..." under the
        // answer: a full stop joined to nothing, a space after it, and a
        // paragraph warning about a tray that is perfectly fine.
        Assert.Equal(
            "Leave this alone and DiscWright works it out, and asks only if there is something to ask.",
            window.WhatTheDrivesAreDoing);
    }

    [Fact]
    public async Task Hovering_the_picker_says_why_a_tray_matters_when_one_does()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, true, 359_847), tray: new ATrayThatNeedsAPush());

        await window.LookAtTheDrives();

        // Whole, and joined exactly as it is joined. Contains() let a fault
        // that ran the two paragraphs into one line straight through: the
        // warning is the part somebody has to read before walking away, and
        // it only stands out because there is a blank line above it.
        Assert.Equal(
            "Leave this alone and DiscWright works it out, and asks only if there is something to ask."
                + "\n\nD: needs the tray pushed in. Every disc has to come out to be checked after "
                + "it is written, so this will stop and wait for you to push it back in.",
            window.WhatTheDrivesAreDoing);
    }

    [Fact]
    public async Task Does_not_go_near_the_tray_of_an_empty_drive()
    {
        ATrayThatNeedsAPush tray = new();
        MakeADisc window = New(drives: new ADriveHolding(0, false, 0), tray: tray);

        await window.LookAtTheDrives();

        Assert.Empty(tray.Asked);
        Assert.Equal(string.Empty, window.AboutTheTray);
    }

    // ------------------------------------ the guards a second press reaches

    [Fact]
    public async Task Will_not_start_a_second_burn_over_a_build_that_is_running()
    {
        // Pressed while something is already running. The buttons are dead
        // in the window now, but a dead button is still a method, and the
        // method is what writes to a disc.
        ABuilderThatWaits waits = new();
        MakeADisc window = New(builder: waits, drives: new ADriveHolding(3, true, 359_847));

        window.OutputFolder = @"C:\out";
        window.Add(AGame("A Game", 1000));

        Task building = window.MakeIt();
        await waits.Started.Task;

        await window.BurnIt(
            @"C:\out\A GAME.iso",
            could => Task.FromResult<Recorder?>(could.Count > 0 ? could[0] : null),
            _ => Task.FromResult<Recorder?>(null),
            _ => Task.FromResult(true));

        // Nothing was burned and nothing was said about burning, because it
        // never started.
        Assert.Null(window.HowTheDiscCameOut);

        waits.LetGo.SetResult();
        await building;
    }

    [Fact]
    public async Task Will_not_ask_about_a_new_version_twice_at_once()
    {
        AnAskerThatWaitsToAnswer asker = new();
        MakeADisc window = New(releases: asker);

        Task first = window.CheckForANewVersion();
        await asker.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The second press arrives while the first is still out. One question
        // to GitHub, not two, and the second press is simply nothing.
        Task second = window.CheckForANewVersion();

        // Answered before anything is asserted, and both presses waited for
        // with a deadline, so that this test leaves nothing pending however
        // it ends.
        //
        // That is not tidiness. Without the guard the second press waits for
        // an answer nobody has given yet, and this test used to await it
        // bare: the fault stopped the test finishing rather than making it
        // fail, the test host stayed alive with 138 tests after it never run,
        // and a mutation pass reported no verdict at all. A fault that hangs
        // a test is indistinguishable from a quiet machine.
        asker.Answer.SetResult(new WhatWasReleased("v2.1.0", "https://example.invalid/x"));

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, asker.Asked);
    }

    [Fact]
    public void Does_nothing_when_an_option_is_set_to_what_it_already_is()
    {
        MakeADisc window = New();

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        // On by default, set on again. Nothing changed, so nothing is
        // announced and nothing is worked out again.
        window.Menu = true;
        window.Checksums = true;
        window.LegacyFileSystems = false;

        Assert.Empty(told);
    }

    [Fact]
    public void Takes_off_every_turned_off_row_of_one_game_and_not_just_the_first()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000, parts: 2));

        AThingOnTheDisc[] rows = [.. window.WhatGoesOnIt[0].EveryThing()];
        Assert.Equal(3, rows.Length);

        rows[1].IsOn = false;
        rows[2].IsOn = false;

        // Two rows turned off, and the button that clears them says it will
        // take two. Taking a row off rebuilds the branch it was in, so every
        // row gathered before the first one went is a row that is no longer
        // in the tree.
        window.TakeOffWhatIsLeftOff();

        Assert.Single(window.Games);
        Assert.Single(window.Games[0].Files);
    }

    [Fact]
    public void Takes_off_only_the_rows_that_are_turned_off()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000, parts: 2));

        AThingOnTheDisc[] rows = [.. window.WhatGoesOnIt[0].EveryThing()];
        Assert.Equal(3, rows.Length);

        rows[1].IsOn = false;
        window.TakeOffWhatIsLeftOff();

        // One row off the disc, and the other two still on it. Taking off the
        // ones somebody kept is the same gesture with the opposite meaning,
        // and the window adds up either way afterwards, which is why the walk
        // cannot see the difference.
        Assert.Single(window.Games);
        Assert.Equal(2, window.Games[0].Files.Count);
    }

    [Fact]
    public void Takes_two_games_off_at_once_without_moving_the_rest_under_them()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 1000), [APatch("setup_one_patch.exe")]);
        window.Add(AGame("Two", 1000), [APatch("setup_two_patch.exe")]);
        window.Add(AGame("Three", 1000), [APatch("setup_three_patch.exe")]);

        foreach (int which in (int[])[0, 1])
        {
            foreach (AThingOnTheDisc row in window.WhatGoesOnIt[which].EveryThing())
            {
                row.IsOn = false;
            }
        }

        window.TakeOffWhatIsLeftOff();

        // Highest first, or taking the first one off moves the second one
        // under the hand reaching for it and the wrong game comes off.
        Assert.Single(window.Games);
        Assert.Equal("Three", window.Games[0].Name);

        // And the one add-on left belongs to the one game left, by its new
        // position rather than its old one.
        List<AddOn> addOns = Assert.IsType<List<AddOn>>(window.AsAProject().AddOns, exactMatch: false);

        Assert.Single(addOns);
        Assert.Equal(0, addOns[0].BelongsTo);
    }

    [Fact]
    public void Takes_a_game_off_when_every_row_under_it_is_unticked()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        Assert.Single(window.Games);

        foreach (AThingOnTheDisc thing in window.WhatGoesOnIt[0].EveryThing())
        {
            thing.IsOn = false;
        }

        // A game with nothing of it on the disc is not on the disc. Taking
        // off what is left off takes the whole branch rather than leaving an
        // empty one behind.
        window.TakeOffWhatIsLeftOff();

        Assert.Empty(window.Games);
        Assert.Empty(window.WhatGoesOnIt);
    }

    [Fact]
    public void Shrugs_at_being_asked_to_take_off_a_row_that_is_not_there_any_more()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        AThingOnTheDisc stale = window.WhatGoesOnIt[0];

        window.StartAgain();
        window.Add(AGame("Gothic", 1000));

        // The window holds a row from the tree as it was, and the tree has
        // been rebuilt since. Taking off something that is not on the list is
        // nothing rather than an exception or somebody else's game.
        window.TakeOff(stale);

        Assert.Single(window.Games);
        Assert.Equal("Gothic", window.Games[0].Name);
    }

    [Fact]
    public void Says_an_icon_and_artwork_to_print_are_among_what_is_chosen()
    {
        MakeADisc window = New();

        Assert.Equal("Nothing chosen. A disc with none of it is still a disc.", window.WhatElseIsChosen);

        window.DiscIcon = @"C:\art\icon.png";
        Assert.Contains("an icon", window.WhatElseIsChosen, StringComparison.Ordinal);

        window.Cover = @"C:\art\cover.png";
        Assert.Contains("artwork to print", window.WhatElseIsChosen, StringComparison.Ordinal);

        // And the disc face on its own says the same thing, because what is
        // being said is that something will be printed, not which of them.
        window.Cover = null;
        window.DiscFace = @"C:\art\face.png";
        Assert.Contains("artwork to print", window.WhatElseIsChosen, StringComparison.Ordinal);
    }

    /// <summary>One that does not answer until it is told to, and counts.</summary>
    private sealed class AnAskerThatWaitsToAnswer : IAskWhatIsReleased
    {
        internal int Asked { get; private set; }

        internal TaskCompletionSource Started { get; } = new();

        internal TaskCompletionSource<WhatWasReleased> Answer { get; } = new();

        public Task<WhatWasReleased> TheLatest(CancellationToken ct)
        {
            Asked++;
            Started.TrySetResult();

            return Answer.Task;
        }
    }

    // -------------------------------- what the window says, for a given state

    /// <summary>
    /// The whole of what the window says, for one state at a time, read as a
    /// table.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everywhere else asks one question of one property. This asks what the
    /// window looks like, which is the thing somebody is actually looking at,
    /// and it asks it for the state in the first column rather than for a
    /// state built up over nine lines of arranging.
    /// </para>
    /// <para>
    /// The question it exists to answer is "should a warning be showing with
    /// this chosen", which is not answerable by any test that looks at one
    /// line at a time: a warning that is correct and a warning that should
    /// have gone are the same sentence.
    /// </para>
    /// </remarks>
    [Theory]
    // what is on it          size    split   red line                                 splitting line
    [InlineData("nothing", "DVD5", false, "", "")]
    [InlineData("a small game", "DVD5", false, "", "")]
    [InlineData("a small game", "CD", false, "", "")]
    [InlineData("a big game", "DVD5", false, "", "")]
    [InlineData("a big game", "CD", false, "One file is bigger than a whole disc", "")]
    [InlineData("a big game", "CD", true, "", "cut into 2 pieces")]
    [InlineData("a big game", "DVD5", true, "", "Nothing here is bigger than one disc")]
    [InlineData("a small game", "CD", true, "", "Nothing here is bigger than one disc")]
    public void Says_the_right_things_for_what_is_chosen(
        string whatIsOnIt, string size, bool split, string redLine, string splittingLine)
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";

        switch (whatIsOnIt)
        {
            case "a small game": window.Add(AGame("Hollow Knight", 400_000_000)); break;
            case "a big game": window.Add(AGame("Hollow Knight", 1_000_000_000)); break;
        }

        window.Medium = size == "CD" ? Media.Cd : Media.Dvd5;
        window.CutFiles = split;

        Assert.Equal(redLine, window.WhyNot?.Headline ?? string.Empty);

        if (splittingLine.Length == 0)
        {
            Assert.Equal(string.Empty, window.WhatSplittingWouldDo);
        }
        else
        {
            Assert.Contains(splittingLine, window.WhatSplittingWouldDo, StringComparison.Ordinal);
        }

        // And the two buttons follow the red line, always.
        Assert.Equal(redLine.Length == 0 && whatIsOnIt != "nothing", window.CanMakeIt);
    }

    [Theory]
    // what is in the drive            aiming at   the amber line
    [InlineData("nothing", "CD", "")]
    [InlineData("a blank CD", "CD", "")]
    [InlineData("a blank CD", "DVD5", "The disc in D: is a CD-R, and this is being built for DVD5 4.7 GB.")]
    [InlineData("a blank DVD", "CD", "The disc in D: is a DVD-R, and this is being built for CD-R 700 MB.")]
    [InlineData("a blank DVD", "DVD5", "")]
    [InlineData("a used CD-RW", "CD", "")]
    [InlineData("a used CD-RW", "DVD5", "The disc in D: is a CD-RW, and this is being built for DVD5 4.7 GB.")]
    public async Task Says_the_right_thing_about_the_disc_in_the_drive(
        string holding, string aimingAt, string amber)
    {
        ADriveHolding drive = holding switch
        {
            "nothing" => new ADriveHolding(0, false, 0),
            "a blank CD" => new ADriveHolding(2, true, 359_847),
            "a blank DVD" => new ADriveHolding(9, true, 2_297_888),
            _ => new ADriveHolding(3, false, 0),
        };

        MakeADisc window = New(drives: drive);

        await window.LookAtTheDrives();
        window.Medium = aimingAt == "CD" ? Media.Cd : Media.Dvd5;

        Assert.Equal(amber, window.AboutTheDiscInTheDrive);

        // The ink it is drawn in follows the words, never separately.
        Assert.Equal(amber.Length > 0, window.TheDiscIsADifferentSize);

        // And erasing is offered only for something erasing would help.
        Assert.Equal(holding == "a used CD-RW", window.CanErase);
    }

    [Theory]
    // what is chosen                       what the line says
    [InlineData("", "Nothing chosen. A disc with none of it is still a disc.")]
    [InlineData("icon", "an icon")]
    [InlineData("background", "a background")]
    [InlineData("manual", "a manual")]
    [InlineData("extras", "extras")]
    [InlineData("music", "music")]
    [InlineData("cover", "artwork to print")]
    [InlineData("face", "artwork to print")]
    public void Says_what_else_is_chosen_for_each_one_of_them(string chosen, string says)
    {
        MakeADisc window = New();

        switch (chosen)
        {
            case "icon": window.DiscIcon = @"C:\art\icon.png"; break;
            case "background": window.Background = @"C:\art\back.png"; break;
            case "manual": window.Manual = @"C:\extras\how.txt"; break;
            case "extras": window.ExtrasFolder = @"C:\extras"; break;
            case "music": window.Music = @"C:\music\tune.mp3"; break;
            case "cover": window.Cover = @"C:\art\cover.png"; break;
            case "face": window.DiscFace = @"C:\art\face.png"; break;
        }

        Assert.Contains(says, window.WhatElseIsChosen, StringComparison.Ordinal);

        // And when it is put back it says nothing again, which is the half
        // that keeps being reported: chosen, cleared, and still named.
        switch (chosen)
        {
            case "icon": window.DiscIcon = null; break;
            case "background": window.Background = null; break;
            case "manual": window.Manual = null; break;
            case "extras": window.ExtrasFolder = null; break;
            case "music": window.Music = null; break;
            case "cover": window.Cover = null; break;
            case "face": window.DiscFace = null; break;
        }

        Assert.Equal("Nothing chosen. A disc with none of it is still a disc.", window.WhatElseIsChosen);
    }

    // ------------------------------------------ the files carried loose
    //
    // Nobody chooses these in that panel. They arrive when a folder holding
    // several games is taken apart, and the question about doing that promises
    // that the one file sitting loose in it goes on the disc either way. It
    // then went on with nothing on the window saying so anywhere: the total
    // above counts the tree alone, and every other extra is named in this line
    // while these were not. They were the only thing aboard a disc that the
    // window never mentioned.

    [Fact]
    public void Says_so_when_a_file_is_carried_loose()
    {
        MakeADisc window = New();

        window.AlsoOnTheDisc([@"C:\downloads\read me.txt"]);

        Assert.Equal("This disc has a file sitting loose.", window.WhatElseIsChosen);
    }

    [Theory]
    [InlineData(1, "This disc has a file sitting loose.")]
    [InlineData(2, "This disc has 2 files sitting loose.")]
    [InlineData(3, "This disc has 3 files sitting loose.")]
    public void Says_how_many_are_carried_loose(int many, string says)
    {
        MakeADisc window = New();

        window.AlsoOnTheDisc(Enumerable.Range(0, many).Select(at => $@"C:\downloads\{at}.txt"));

        Assert.Equal(says, window.WhatElseIsChosen);
    }

    [Fact]
    public void A_file_carried_loose_is_named_beside_everything_else()
    {
        MakeADisc window = New();

        window.Manual = @"C:\extras\how.txt";
        window.ExtrasFolder = @"C:\extras";
        window.Cover = @"C:\art\cover.png";
        window.AlsoOnTheDisc([@"C:\downloads\read me.txt"]);

        // Before the artwork, because the artwork is the one thing named in
        // this line that gets printed rather than going on the disc.
        Assert.Equal(
            "This disc has a manual, extras, a file sitting loose and artwork to print.",
            window.WhatElseIsChosen);
    }

    [Fact]
    public void Carrying_a_file_loose_says_the_line_changed()
    {
        MakeADisc window = New();
        List<string> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        window.AlsoOnTheDisc([@"C:\downloads\read me.txt"]);

        // A line nobody is told about keeps whatever the window read out of it
        // the first time, which for this one is the sentence about an empty
        // disc, sitting over a disc with a file on it.
        Assert.Contains(nameof(MakeADisc.WhatElseIsChosen), told);
    }

    [Fact]
    public void A_file_carried_loose_is_something_to_lose()
    {
        MakeADisc window = New();

        window.AlsoOnTheDisc([@"C:\downloads\read me.txt"]);

        // Every other extra was in HasAnythingOnIt and these were not, so a
        // window holding nothing but a loose file had Start a new disc grey,
        // and pressing it anyway threw the file away: the button said there
        // was nothing to lose and then lost something. The walk found it the
        // moment the line above learned to name them.
        Assert.True(window.HasAnythingOnIt);
        Assert.True(window.CanStartAgain);
    }

    [Fact]
    public void Starting_again_takes_the_loose_files_with_it()
    {
        MakeADisc window = New();
        window.AlsoOnTheDisc([@"C:\downloads\read me.txt"]);

        window.StartAgain();

        Assert.Equal("Nothing chosen. A disc with none of it is still a disc.", window.WhatElseIsChosen);
        Assert.False(window.HasAnythingOnIt);
    }

    [Fact]
    public void A_disc_opened_again_says_what_it_carries_loose()
    {
        MakeADisc window = New();

        // That fixture carries an icon, a manual and one loose file, so this is
        // the whole round trip: it went into the project file, and it is named
        // on the window when the project comes back.
        window.OpenThis(AReopenedDisc());

        Assert.Equal(
            "This disc has an icon, a manual and a file sitting loose.",
            window.WhatElseIsChosen);
    }

    // ------------------------------------------ what the disc will start

    [Fact]
    public void Says_so_when_a_folder_has_nothing_in_it_to_start()
    {
        MakeADisc window = New();
        window.Add(AFolderOfFiles("Some Game", candidates: 0));

        Assert.Equal(
            "The menu will copy Some Game and open the folder, because nothing in it was picked to "
                + "start the game.",
            window.WhatWillNotStartItself);
    }

    [Fact]
    public void Says_so_when_a_folder_has_more_than_one_and_nobody_picked()
    {
        MakeADisc window = New();
        window.Add(AFolderOfFiles("Some Game", candidates: 3));

        // Two behaved exactly like none, and nothing anywhere said so. Two
        // folders that look identical on this window made two discs that
        // behave differently in the drive.
        Assert.NotNull(window.WhatWillNotStartItself);
    }

    [Fact]
    public void Says_nothing_once_one_of_them_has_been_picked()
    {
        MakeADisc window = New();
        GameFolder many = AFolderOfFiles("Some Game", candidates: 3);

        // Which is what the window does with the answer: the candidates are
        // narrowed to the one chosen, and that is all the menu looks at.
        window.Add(many with { LauncherCandidates = [many.LauncherCandidates[1]] });

        Assert.Null(window.WhatWillNotStartItself);
    }

    [Fact]
    public void Says_nothing_about_a_folder_that_already_had_exactly_one()
    {
        MakeADisc window = New();
        window.Add(AFolderOfFiles("Some Game", candidates: 1));

        Assert.Null(window.WhatWillNotStartItself);
    }

    [Fact]
    public void Says_nothing_about_an_installer()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        // A GOG download has an installer to run and no candidates at all,
        // so counting candidates alone would have called it uncopyable.
        Assert.Null(window.WhatWillNotStartItself);
    }

    [Fact]
    public void Says_nothing_about_a_download_whose_installer_was_left_off()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        window.WhatGoesOnIt[0].EveryThing()
            .First(each => each.What == TheTreeOfWhatGoesOn.TheInstaller).IsOn = false;

        // The menu would copy it, which is true, and the line under it already
        // says the one thing worth saying about that tick. Two sentences about
        // one tick read as two complaints, and this one's words were written
        // for a folder of files.
        Assert.Null(window.WhatWillNotStartItself);
        Assert.NotNull(window.WhyThatMatters);
    }

    [Fact]
    public void Asks_before_opening_over_anything_on_the_window_and_not_only_over_games()
    {
        // The same rule Start a new disc keeps. A name typed, a cover chosen
        // and a manual attached are an afternoon too, and this asked only
        // once a game was on the list. Read off disk, like the other guards
        // over the code behind.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        int from = code.IndexOf("void OpenADisc", StringComparison.Ordinal);
        Assert.True(from >= 0, "The window no longer opens a disc.");

        string handler = code[from..];
        handler = handler[..handler.IndexOf("_screen.OpenThis(", StringComparison.Ordinal)];

        Assert.Contains("_screen.HasAnythingOnIt && !WorthAsking.Ask(", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Games.Count > 0 &&", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_all_of_them_when_several_will_only_be_copied()
    {
        MakeADisc window = New();
        window.Add(AFolderOfFiles("One", candidates: 0));
        window.Add(AFolderOfFiles("Two", candidates: 2));
        window.Add(AFolderOfFiles("Three", candidates: 0));

        Assert.Equal(
            "The menu will copy One, Two and Three and open their folders, because nothing in them "
                + "was picked to start them.",
            window.WhatWillNotStartItself);
    }

    [Fact]
    public void Tells_the_window_when_that_changes()
    {
        MakeADisc window = New();

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.Add(AFolderOfFiles("Some Game", candidates: 0));

        Assert.Contains(nameof(MakeADisc.WhatWillNotStartItself), told);
    }

    // ----------------------------------------- what the folder was really

    [Fact]
    public void Says_when_a_download_looks_unfinished()
    {
        MakeADisc window = New();

        window.Add(AGame("Hollow Knight", 1000) with
        {
            Warning = "INCOMPLETE: installer part(s) -3 are missing, so the download looks unfinished."
        });

        // Worked out from the first day and never shown. It builds a clean
        // looking ISO that fails only when the installer is run off the
        // burned disc, which is the worst possible place to find out.
        Assert.Equal(
            "INCOMPLETE: installer part(s) -3 are missing, so the download looks unfinished.",
            window.WhatTheDownloadsSay);
    }

    [Fact]
    public void Says_nothing_about_a_folder_there_was_nothing_to_say_about()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        Assert.Null(window.WhatTheDownloadsSay);
    }

    [Fact]
    public void Says_which_game_when_there_is_more_than_one()
    {
        MakeADisc window = New();

        window.Add(AGame("Hollow Knight", 1000));
        window.Add(AGame("Gothic", 1000) with { Warning = "2 installers in this folder, using the largest, a.exe." });

        // One game needs no name in front of it. Two do, or the sentence is
        // about whichever one the reader happens to be looking at.
        Assert.Equal(
            "Gothic: 2 installers in this folder, using the largest, a.exe.",
            window.WhatTheDownloadsSay);
    }

    [Fact]
    public void Says_all_of_them_when_more_than_one_folder_had_something_to_say()
    {
        MakeADisc window = New();

        window.Add(AGame("Hollow Knight", 1000) with { Warning = "INCOMPLETE: one." });
        window.Add(AGame("Gothic", 1000) with { Warning = "INCOMPLETE: two." });

        Assert.Equal(
            "Hollow Knight: INCOMPLETE: one. Gothic: INCOMPLETE: two.",
            window.WhatTheDownloadsSay);
    }

    [Fact]
    public void Stops_saying_it_once_the_game_comes_off()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000) with { Warning = "INCOMPLETE: something." });

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.StartAgain();

        Assert.Null(window.WhatTheDownloadsSay);

        // A line nobody is told about stays on the window under a list that
        // no longer holds the game it was about.
        Assert.Contains(nameof(MakeADisc.WhatTheDownloadsSay), told);
    }

    // ------------------------------------------------ somewhere to put it

    [Fact]
    public void The_first_game_names_the_folder_as_well_as_the_disc()
    {
        MakeADisc window = New(places: new WhereDiscsWouldGo(@"C:\d", @"C:\h", []));

        window.Add(AGame("Hollow Knight", 1000));

        // v1 filled this in and so "There is nowhere to put it" never
        // happened to anybody. Three reports of it as a stale message about
        // the disc came out of leaving the box empty instead.
        Assert.Equal(@"C:\d\Hollow Knight Disc", window.OutputFolder);
        Assert.Null(window.WhyNot);
        Assert.True(window.CanMakeIt);
    }

    [Fact]
    public void Leaves_a_folder_somebody_chose_themselves_alone()
    {
        MakeADisc window = New(places: new WhereDiscsWouldGo(@"C:\d", @"C:\h", []));
        window.OutputFolder = @"D:\my discs";

        window.Add(AGame("Hollow Knight", 1000));

        // The same rule the name follows. They said where, so that is where.
        Assert.Equal(@"D:\my discs", window.OutputFolder);
    }

    [Fact]
    public void Only_the_first_game_names_the_folder()
    {
        MakeADisc window = New(places: new WhereDiscsWouldGo(@"C:\d", @"C:\h", []));

        window.Add(AGame("Hollow Knight", 1000));
        window.Add(AGame("Gothic", 1000));

        Assert.Equal(@"C:\d\Hollow Knight Disc", window.OutputFolder);
    }

    [Fact]
    public void A_later_game_names_a_folder_the_first_one_could_not()
    {
        MakeADisc window = New(places: new WhereDiscsWouldGo(@"C:\d", @"C:\h", []));

        window.Add(AGame(":", 1000));
        Assert.Equal(string.Empty, window.OutputFolder);

        window.Add(AGame("Gothic", 1000));

        // Nothing is left of a game called ":" once what Windows will not
        // take in a folder name is out of it, so the box stayed empty and
        // the next game along is entitled to fill it.
        Assert.Equal(@"C:\d\Gothic Disc", window.OutputFolder);
    }

    [Fact]
    public void A_folder_that_was_only_suggested_goes_with_the_disc_it_was_suggested_for()
    {
        MakeADisc window = New(places: new WhereDiscsWouldGo(@"C:\d", @"C:\h", []));
        window.Add(AGame("Hollow Knight", 1000));
        Assert.Equal(@"C:\d\Hollow Knight Disc", window.OutputFolder);

        window.StartAgain();

        // Kept, the next game was built into "Hollow Knight Disc": that
        // disc's staging folder emptied, its project file written over, and
        // two images left for Burn to ask about.
        Assert.Equal(string.Empty, window.OutputFolder);

        window.Add(AGame("Gothic", 1000));
        Assert.Equal(@"C:\d\Gothic Disc", window.OutputFolder);
    }

    [Fact]
    public void A_folder_somebody_chose_stays_when_a_new_disc_is_started()
    {
        MakeADisc window = New(places: new WhereDiscsWouldGo(@"C:\d", @"C:\h", []));
        window.OutputFolder = @"D:\all my discs";
        window.Add(AGame("Hollow Knight", 1000));

        window.StartAgain();

        // How this person works rather than what they are making, and having
        // to find it again every time is its own small insult.
        Assert.Equal(@"D:\all my discs", window.OutputFolder);
    }

    [Fact]
    public void A_folder_chosen_over_a_suggestion_stays_too()
    {
        MakeADisc window = New(places: new WhereDiscsWouldGo(@"C:\d", @"C:\h", []));
        window.Add(AGame("Hollow Knight", 1000));
        window.OutputFolder = @"D:\all my discs";

        window.StartAgain();

        Assert.Equal(@"D:\all my discs", window.OutputFolder);
    }

    [Fact]
    public void A_folder_an_opened_project_named_stays_when_a_new_disc_is_started()
    {
        MakeADisc window = New(places: new WhereDiscsWouldGo(@"C:\d", @"C:\h", []));
        window.OpenThis(AReopenedDisc());
        Assert.Equal(@"D:\out", window.OutputFolder);

        window.StartAgain();

        // Somebody's answer once, so it counts as chosen.
        Assert.Equal(@"D:\out", window.OutputFolder);
    }

    [Fact]
    public void Still_asks_when_there_is_nowhere_worth_suggesting()
    {
        MakeADisc window = New(places: new WhereDiscsWouldGo(string.Empty, string.Empty, []));

        window.Add(AGame("Hollow Knight", 1000));

        // Better an empty box and a line saying to choose a folder than a
        // guess at somewhere gigabytes should not go.
        Assert.Equal(string.Empty, window.OutputFolder);
        Assert.Equal("There is nowhere to put it", window.WhyNot?.Headline);
        Assert.Equal(RemedyKind.PickFolder, window.TheWayOut);
    }

    [Fact]
    public void Tells_the_window_the_folder_was_filled_in()
    {
        MakeADisc window = New(places: new WhereDiscsWouldGo(@"C:\d", @"C:\h", []));

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.Add(AGame("Hollow Knight", 1000));

        // The box is bound to it, and a box never told stays empty over a
        // window that has already decided where the discs are going.
        Assert.Contains(nameof(MakeADisc.OutputFolder), told);
    }

    // ------------------------------------------------- what to do about it

    [Fact]
    public void Offers_a_way_out_of_having_nowhere_to_put_it()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        // Correct, current and reported twice as a stale message, because a
        // red line on its own reads as the program complaining. The refusal
        // has known how to be put right since the day it was written and
        // nothing had ever shown it.
        Assert.Equal("There is nowhere to put it", window.WhyNot?.Headline);
        Assert.Equal(RemedyKind.PickFolder, window.TheWayOut);
        Assert.Equal("Choose a folder", window.WhatTheWayOutIsCalled);
    }

    [Fact]
    public void Takes_the_way_out_away_once_it_has_been_taken()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.OutputFolder = @"C:\out";

        Assert.Null(window.WhyNot);
        Assert.Null(window.TheWayOut);
        Assert.Equal(string.Empty, window.WhatTheWayOutIsCalled);

        // Told with the refusal, because it is part of the refusal. A way
        // out nobody is told about stays on the window under nothing.
        Assert.Contains(nameof(MakeADisc.TheWayOut), told);
        Assert.Contains(nameof(MakeADisc.WhatTheWayOutIsCalled), told);
    }

    [Fact]
    public void Offers_nothing_at_all_when_there_is_no_refusal()
    {
        MakeADisc window = New();

        Assert.Null(window.WhyNot);
        Assert.Null(window.TheWayOut);
        Assert.Equal(string.Empty, window.WhatTheWayOutIsCalled);
    }

    [Fact]
    public void Offers_the_smallest_larger_disc_that_would_do()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = Media.Cd;
        window.Add(AGame("Hollow Knight", 1_000_000_000));

        // "Use a larger disc" carried no subject, so nothing could act on it
        // and the red line stood alone. The screen has the plan, so it can
        // say which disc: the smallest one up on which this would plan.
        Assert.Equal("One file is bigger than a whole disc", window.WhyNot?.Headline);
        Assert.Equal(RemedyKind.ChangeMedium, window.TheWayOut);
        Assert.Equal(Media.Dvd5, window.TheLargerDiscThatWouldDo);
        Assert.Equal("Use DVD5 4.7 GB instead", window.WhatTheWayOutIsCalled);
    }

    [Fact]
    public void Skips_the_sizes_that_would_not_do_either()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = Media.Cd;

        // Bigger than a DVD5 as well, so that is no answer.
        ListedFile huge = new("setup_big.exe", 5_000_000_000L);
        window.Add(new GameFolder(@"C:\games\Big", GameKind.GogInstaller, "Big", "Big", huge, [huge], [], null));

        Assert.Equal(Media.Dvd9, window.TheLargerDiscThatWouldDo);
        Assert.Equal("Use DVD9 8.5 GB (dual layer) instead", window.WhatTheWayOutIsCalled);
    }

    [Fact]
    public void Offers_no_larger_disc_when_none_this_program_aims_at_would_do()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";

        ListedFile vast = new("setup_vast.exe", 200_000_000_000L);
        window.Add(new GameFolder(@"C:\games\Vast", GameKind.GogInstaller, "Vast", "Vast", vast, [vast], [], null));

        // A word that did nothing when pressed would be worse than the red
        // line on its own.
        Assert.Equal("One file is bigger than a whole disc", window.WhyNot?.Headline);
        Assert.NotNull(window.WhyNot?.Remedy);
        Assert.Null(window.TheLargerDiscThatWouldDo);
        Assert.Null(window.TheWayOut);
        Assert.Equal(string.Empty, window.WhatTheWayOutIsCalled);
    }

    [Fact]
    public void Taking_the_larger_disc_aims_at_it_as_a_choice_and_clears_the_refusal()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = Media.Cd;
        window.Add(AGame("Hollow Knight", 1_000_000_000));

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.UseTheLargerDisc();

        Assert.Equal(Media.Dvd5, window.Medium);
        Assert.Null(window.WhyNot);
        Assert.Null(window.TheWayOut);
        Assert.Null(window.TheLargerDiscThatWouldDo);
        Assert.Contains(nameof(MakeADisc.Medium), told);
        Assert.Contains(nameof(MakeADisc.TheSizeChosen), told);
        Assert.Contains(nameof(MakeADisc.TheWayOut), told);
        Assert.Contains(nameof(MakeADisc.TheLargerDiscThatWouldDo), told);
    }

    [Fact]
    public void Taking_the_larger_disc_does_nothing_when_none_is_offered()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        window.UseTheLargerDisc();

        // The refusal here is about the folder, and the size is not touched.
        Assert.Equal(Media.Dvd5, window.Medium);
        Assert.Equal(RemedyKind.PickFolder, window.TheWayOut);
    }

    [Fact]
    public void Offers_to_name_a_disc_that_has_none()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));
        window.Label = string.Empty;

        Assert.Equal("The disc needs a name", window.WhyNot?.Headline);
        Assert.Equal(RemedyKind.FocusField, window.TheWayOut);
        Assert.Equal("Name the disc", window.WhatTheWayOutIsCalled);
    }

    [Fact]
    public void The_worry_about_the_name_offers_a_rename()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.Label = "\u0414\u0430\u043d\u043d\u044b\u0435";

        Assert.NotNull(window.Worry);
        Assert.Equal("Rename the disc", window.WhatTheWorrysWayOutIsCalled);
        Assert.Contains(nameof(MakeADisc.WhatTheWorrysWayOutIsCalled), told);

        window.Label = "HOLLOW KNIGHT";

        Assert.Null(window.Worry);
        Assert.Equal(string.Empty, window.WhatTheWorrysWayOutIsCalled);
    }

    // --------------------------------------------------- seeing it first

    [Fact]
    public void Will_not_offer_a_preview_of_a_set_that_cannot_be_planned()
    {
        MakeADisc window = New();
        window.Medium = Media.Cd;
        window.Add(AGame("Hollow Knight", 1_000_000_000));

        // A preview is a menu written for every disc of the set, and with a
        // file too big for the disc there is no set. The button stayed lit
        // and answered with a box saying exactly what the red line under it
        // was already saying.
        Assert.NotNull(window.WhyNot);
        Assert.False(window.CanPreview);

        // Which is what Build has always done in this state, so the box
        // could only ever have come from the preview.
        Assert.False(window.CanMakeIt);
    }

    [Fact]
    public void Offers_a_preview_even_with_nowhere_to_put_the_iso()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        Assert.Equal(string.Empty, window.OutputFolder);

        // A preview is written to a temporary folder of its own and has
        // never gone near that one. Waiting on the whole of CanMakeIt would
        // have made somebody choose a folder to look at a menu.
        Assert.False(window.CanMakeIt);
        Assert.True(window.CanPreview);
    }

    [Fact]
    public void Offers_the_preview_again_once_the_disc_is_big_enough()
    {
        MakeADisc window = New();
        window.Medium = Media.Cd;
        window.Add(AGame("Hollow Knight", 1_000_000_000));
        Assert.False(window.CanPreview);

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        window.Medium = Media.Dvd5;

        Assert.True(window.CanPreview);
        Assert.Contains(nameof(MakeADisc.CanPreview), told);
    }

    [Fact]
    public void Will_not_offer_a_preview_of_a_disc_with_no_name()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));
        Assert.True(window.CanPreview);

        window.Label = string.Empty;

        // Nothing is planned at all without a name, and the menu is a page
        // with the name of the disc across the top of it.
        Assert.False(window.CanPreview);
    }

    // ------------------------------------------------------ starting over

    [Fact]
    public void Says_whether_there_is_anything_on_it_worth_losing()
    {
        MakeADisc window = New();

        // An empty screen needs no confirming, and being asked to confirm
        // nothing is how somebody learns to click through the question that
        // matters.
        Assert.False(window.HasAnythingOnIt);

        window.Add(AGame("Hollow Knight", 1000));

        Assert.True(window.HasAnythingOnIt);
    }

    [Fact]
    public void Starting_a_new_disc_clears_the_games_and_the_name()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));
        window.Label = "MY COMPILATION";

        window.StartAgain();

        Assert.Empty(window.Games);
        Assert.Equal(string.Empty, window.Label);
        Assert.False(window.HasAnythingOnIt);
    }

    [Fact]
    public void Starting_a_new_disc_keeps_where_the_discs_get_built()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\somewhere\of\mine";
        window.Checksums = false;
        window.Add(AGame("Hollow Knight", 1000));

        window.StartAgain();

        // How somebody works, rather than what they are making. Having to find
        // that folder again every time is its own small insult.
        Assert.Equal(@"C:\somewhere\of\mine", window.OutputFolder);
        Assert.False(window.Checksums);
    }

    [Fact]
    public async Task Starting_a_new_disc_forgets_what_the_last_run_left_behind()
    {
        MakeADisc window = Ready(new BuilderThatWorks());

        await window.MakeIt();
        Assert.NotNull(window.Made);

        window.StartAgain();

        // Left lying about, the last build's discs are what the burn button
        // would reach for next.
        Assert.Null(window.Made);
        Assert.Empty(window.TheImages);
        Assert.Null(window.HowTheSetWent);
        Assert.Null(window.HowTheDiscCameOut);
        Assert.False(window.CanBurn);
    }

    [Fact]
    public async Task Will_not_clear_the_screen_out_from_under_a_build()
    {
        BuilderThatWaits builder = new();
        MakeADisc window = Ready(builder);

        Task building = window.MakeIt();

        // Bounded on purpose. Nothing completes this when the build never
        // starts, and a fault that left CanMakeIt false did exactly that:
        // sixteen tests objected to it and this one waited, so the run was
        // taken down instead of failed and the harness never got a verdict.
        await builder.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        window.StartAgain();

        // There is no sensible meaning for throwing the games away while they
        // are being copied onto a disc.
        Assert.Single(window.Games);

        builder.LetItFinish();
        await building;
    }

    private static MakeADisc Ready(IBuildDiscs builder, IMakeImages? images = null)
    {
        MakeADisc window = New(builder, images);
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));

        return window;
    }

    // ------------------------------------------- the window's own answers
    //
    // The buttons' own rules, the lines it starts with, and the things it says
    // while it works. A mutation pass found these one at a time: the stateful
    // walk cannot see them, because a rule about whether a button is alive
    // reads the same before and after a step, and what a build says while it
    // runs is gone by the time the step is over.

    [Fact]
    public void Will_not_offer_a_preview_until_there_is_a_plan_worth_previewing()
    {
        MakeADisc window = New();

        // Nothing on it. A preview of nothing is a window opening on an empty
        // menu, which tells somebody less than the button being dead.
        Assert.False(window.CanPreview);

        window.Add(AGame("A Game", 1000));
        Assert.True(window.CanPreview);

        // And a plan that cannot be made at all. One file bigger than the
        // disc it is being written to is a refusal, not a menu.
        window.Medium = Media.Cd;
        window.Add(AGame("Far Too Big", 900_000_000));

        Assert.NotNull(window.WhyNot);
        Assert.False(window.CanPreview);
    }

    [Fact]
    public async Task Will_not_offer_a_preview_while_it_is_building()
    {
        bool offeredDuring = true;
        MakeADisc window = null!;

        window = New(builder: new ABuilderThatLooksAtTheWindow(() => offeredDuring = window.CanPreview));

        window.Add(AGame("A Game", 1000));
        window.OutputFolder = @"C:\out";

        await window.MakeIt();

        // Previewing writes real files into a folder a build may be writing
        // into at that moment.
        Assert.False(offeredDuring, "it offered a preview in the middle of a build");
        Assert.True(window.CanPreview);
    }

    [Fact]
    public void Will_not_make_it_without_somewhere_to_put_it()
    {
        MakeADisc window = New();
        window.Add(AGame("A Game", 1000));

        // An empty folder box is not a folder.
        Assert.Equal(string.Empty, window.OutputFolder);
        Assert.False(window.CanMakeIt);

        window.OutputFolder = @"C:\out";
        Assert.True(window.CanMakeIt);
    }

    [Fact]
    public async Task Offers_an_erase_when_any_one_drive_holds_a_disc_worth_erasing()
    {
        // A blank DVD in one drive and somebody's used CD-RW in the other,
        // which is this machine on an ordinary evening. One of the two is
        // worth erasing, and one is all it takes.
        MakeADisc window = New(drives: new ADvdAndAUsedCd());

        await window.LookAtTheDrives();

        Assert.True(window.CanErase);
    }

    [Fact]
    public void Says_a_patch_has_nothing_to_patch_when_its_game_is_turned_off()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000), [APatch("setup_hollow_knight_hidden_dreams.exe")]);

        // A second game with an add-on of its own, which is what makes this
        // about the right game rather than about add-ons in general.
        window.Add(AGame("Gothic", 1000), [APatch("setup_gothic_patch.exe")]);

        Assert.Null(window.APatchWithNothingToPatch);

        // Every row of the game off, and the add-on left on. Allowed, because
        // this window refuses by explaining rather than by greying things out,
        // and somebody may be making a disc of patches on purpose.
        foreach (AThingOnTheDisc row in window.WhatGoesOnIt[0].EveryThing())
        {
            if (row.What != TheTreeOfWhatGoesOn.AnAddOn)
            {
                row.IsOn = false;
            }
        }

        Assert.Equal(
            "Hollow Knight is not on the disc, but its add-ons are. They will have nothing to install onto.",
            window.APatchWithNothingToPatch);
    }

    [Fact]
    public async Task Says_what_it_is_doing_and_how_far_through_while_it_builds()
    {
        List<string> said = [];
        List<double> far = [];
        MakeADisc window = null!;

        // The builder waits for each report to be heard before it goes on,
        // because Progress<T> hands them to the thread pool and a build this
        // fast would be over before the window had listened.
        ABuilderThatReportsAndWaits builder = new(() =>
        {
            lock (far)
            {
                return far.Contains(0.5);
            }
        });

        window = New(builder: builder);

        window.PropertyChanged += (sender, about) =>
        {
            if (about.PropertyName == nameof(MakeADisc.Doing) && window.Doing.Length > 0)
            {
                lock (said)
                {
                    said.Add(window.Doing);
                }
            }

            if (about.PropertyName == nameof(MakeADisc.HowFar))
            {
                lock (far)
                {
                    far.Add(window.HowFar);
                }
            }
        };

        window.Add(AGame("A Game", 1000));
        window.OutputFolder = @"C:\out";

        // Heard on one thread, in order, the way the window hears it.
        await OnTheWindowsThread.Run(() => window.MakeIt());

        // It says so before the first step reports anything, because a
        // builder takes a moment to get going and a window saying nothing is
        // a window that looks ignored.
        Assert.Equal("Starting", said[0]);
        Assert.Contains("step 1 of 2, copying the files", said);

        // Half way is half way. The bar is the step over the steps, which is
        // the only arithmetic on this window.
        Assert.Contains(0.5, far);

        Assert.Equal(string.Empty, window.Doing);
        Assert.Equal(0, window.HowFar);
    }

    [Fact]
    public void Hands_the_window_the_same_two_lists_every_time()
    {
        MakeADisc window = New();

        // The window binds to each collection once and the collection raises
        // its own changes. A new one on every read is a binding that shows
        // what it showed first and never moves again, which is how the program
        // came to build the right disc while showing an empty list.
        Assert.Same(window.Games, window.Games);
        Assert.Same(window.WhatGoesOnIt, window.WhatGoesOnIt);

        window.Add(AGame("A Game", 1000));

        Assert.Same(window.Games, window.Games);
        Assert.Single(window.Games);
    }

    [Fact]
    public void Starts_with_nothing_to_say()
    {
        MakeADisc window = New();

        // A window that opens with a sentence under a button nobody has
        // pressed is a window somebody has to read before they can start.
        Assert.Equal(string.Empty, window.WhatItWillMake);
        Assert.Equal(string.Empty, window.WhatSplittingWouldDo);
        Assert.Equal(string.Empty, window.Doing);
        Assert.Equal(string.Empty, window.WhatIsOnIt);
        Assert.Null(window.WhatIsLeftOff);

        // And no refusal either, because somebody who has not added a game
        // has not done anything wrong. Which is why the buttons count the
        // games themselves rather than reading a refusal that is not there.
        Assert.Null(window.WhyNot);
        Assert.False(window.CanMakeIt);
        Assert.False(window.CanPreview);
    }

    [Fact]
    public async Task Kills_the_buttons_while_it_erases_rather_than_afterwards()
    {
        List<string> happened = [];
        MakeADisc window = New(drives: new ADvdAndAUsedCd());

        window.PropertyChanged += (sender, about) =>
        {
            lock (happened)
            {
                happened.Add("told: " + about.PropertyName);

                if (about.PropertyName == nameof(MakeADisc.Doing) && window.Doing.Length > 0)
                {
                    happened.Add("said: " + window.Doing);
                }
            }
        };

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        // Erasing takes most of a minute. The buttons have to be dead for that
        // minute, which means saying so before the erase starts rather than
        // when it is over: the look at the drives afterwards announces all of
        // this again, so a test that only asked whether it was ever said would
        // be answered by the end of the job.
        List<string> inOrder;

        lock (happened)
        {
            inOrder = [.. happened];
        }

        int erasing = inOrder.FindIndex(each => each.StartsWith("said: erasing the", StringComparison.Ordinal));

        Assert.True(erasing > 0, string.Join(" | ", inOrder));
        Assert.Contains("told: " + nameof(MakeADisc.CanStartAgain), inOrder.GetRange(0, erasing));
        Assert.Contains("told: " + nameof(MakeADisc.CanTakeSomethingOff), inOrder.GetRange(0, erasing));
    }

    /// <summary>A builder that lets a test look at the window mid build.</summary>
    private sealed class ABuilderThatLooksAtTheWindow(Action look) : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            look();

            return Task.FromResult<Outcome<BuiltDiscs>>(
                new BuiltDiscs([new BuiltDisc(project.Label, 1, 1, outputFolder, 1000)]));
        }
    }

    /// <summary>
    /// A builder that reports the way the pipeline does and waits for the
    /// window to have heard it.
    /// </summary>
    private sealed class ABuilderThatReportsAndWaits(Func<bool> heardTheHalfWay) : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            progress?.Report(new BuildProgress(1, 2, "copying the files"));

            // Delivering while it waits when it is on the thread that delivers,
            // which under OnTheWindowsThread it is.
            Assert.True(OnTheWindowsThread.WaitUntil(heardTheHalfWay), "the build reported and nothing heard it");

            progress?.Report(new BuildProgress(2, 2, "writing the menu"));

            return Task.FromResult<Outcome<BuiltDiscs>>(
                new BuiltDiscs([new BuiltDisc(project.Label, 1, 1, outputFolder, 1000)]));
        }
    }

    internal static MakeADisc New(
        IBuildDiscs? builder = null,
        IMakeImages? images = null,
        IFindRecorders? drives = null,
        IBurnDiscs? burner = null,
        IEraseDiscs? eraser = null,
        IWorkTheTray? tray = null,
        IAskWhatIsReleased? releases = null,
        IShowTheMenu? preview = null,
        IImages? pictures = null,
        IFileSystem? disk = null,
        IHasher? hasher = null,
        IKeepTheSetsPlace? note = null,
        WhereDiscsWouldGo? places = null) =>
        new(new TheMachine(
            new Codepage1252(),
            builder ?? new BuilderThatWorks(),
            images ?? new ImagesThatWork(),
            drives ?? new NoDrives(),
            burner ?? new ABurnerThatWorks(),
            eraser ?? new AnEraserThatWorks(),
            disk ?? new ADiskWithADiscOnIt(),
            hasher ?? new NoHashing(),
            note ?? new NothingIsRemembered(),
            tray ?? new ATrayThatShuts(),
            releases ?? new NothingIsReleased(),
            preview ?? new NoPreview(),
            pictures ?? new PicturesThatRead(),
            "2.0.0",
            _ => 1000,
            places));

    /// <summary>The note beside the discs, kept in memory.</summary>
    private sealed class ANoteInMemory(string? says = null) : IKeepTheSetsPlace
    {
        internal string? Note { get; private set; } = says;

        public string? WhatItSays(string folder) => Note;

        public void Leave(string folder, string text) => Note = text;
    }

    /// <summary>
    /// Two images in the output folder, and a disc in the drive carrying the
    /// checksum list every disc carries.
    /// </summary>
    private sealed class ASetOfTwoWithADiscInTheDrive : IFileSystem
    {
        private static readonly string[] OnTheDisc = ["autorun.inf", "game.exe"];

        public bool FolderExists(string path) => true;

        public FolderListing List(string path) =>
            path.StartsWith("C:", StringComparison.OrdinalIgnoreCase)
                ? new FolderListing(path,
                [
                    new ListedFile("A GAME D1.iso", 1000),
                    new ListedFile("A GAME D2.iso", 1000)
                ])
                : new FolderListing(path,
                [
                    .. OnTheDisc.Select(file => new ListedFile(file, 10)),
                    new ListedFile(ChecksumList.FileName, 90)
                ]);

        public string? ReadText(string path)
        {
            if (!path.EndsWith(ChecksumList.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            System.Text.StringBuilder list = new();
            list.Append("# DiscWright checksum list for A GAME\n");
            list.Append("# ").Append(OnTheDisc.Length).Append(" files, 20 bytes, SHA-256, sha256sum format.\n");
            list.Append("#\n");

            foreach (string file in OnTheDisc)
            {
                list.Append(AHasherThatAgrees.TheAnswer).Append(" *").Append(file).Append('\n');
            }

            return list.ToString();
        }
    }

    /// <summary>
    /// A machine with nowhere to keep a set's place, which is most tests here:
    /// hardly any of them walks a set.
    /// </summary>
    private sealed class NothingIsRemembered : IKeepTheSetsPlace
    {
        public string? WhatItSays(string folder) => null;

        public void Leave(string folder, string text)
        {
        }
    }

    private static LeftBehind APatch(string name) =>
        new(new ListedFile(name, 200), LeftBehindKind.LooksLikeAnAddOn);

    private static Medium ADiscHolding(long bytes) => new("TEST", "a test disc", (2L * 1024 * 1024) + bytes);

    /// <summary>Something found beside a game and taken on as an add-on.</summary>
    private static LeftBehind AnExtra(string name, int bytes) =>
        new(new ListedFile(name, bytes), LeftBehindKind.LooksLikeAnAddOn);

    private static GameFolder AGame(string name, int bytes, int parts = 0)
    {
        List<ListedFile> files = [new($"setup_{name}.exe", bytes)];

        for (int part = 1; part <= parts; part++)
        {
            files.Add(new ListedFile($"setup_{name}-{part}.bin", bytes));
        }

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, files[0], files, [], null);
    }

    /// <summary>
    /// A folder of game files, with however many executables worth running.
    /// </summary>
    private static GameFolder AFolderOfFiles(string name, int candidates)
    {
        List<ListedFile> files = [new(@"data\things.pak", 1000)];
        List<ListedFile> could = [];

        for (int at = 1; at <= candidates; at++)
        {
            ListedFile exe = new($"{name}{at}.exe", 1000);
            files.Add(exe);
            could.Add(exe);
        }

        return new GameFolder(
            @"C:\games\" + name,
            could.Count > 0 ? GameKind.FilesWithLauncher : GameKind.FilesOnly,
            name,
            name,
            null,
            files,
            could,
            null);
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

    /// <summary>A build that comes out as three discs rather than one.</summary>
    private sealed class ABuilderThatMakesASet : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs(
            [
                new BuiltDisc(project.Label, 1, 3, System.IO.Path.Combine(outputFolder, "disc D1"), 1000),
                new BuiltDisc(project.Label, 2, 3, System.IO.Path.Combine(outputFolder, "disc D2"), 1000),
                new BuiltDisc(project.Label, 3, 3, System.IO.Path.Combine(outputFolder, "disc D3"), 1000)
            ]));
    }

    /// <summary>
    /// A build of several games that would not share a disc: a disc each,
    /// every one numbered one, each in a folder of its own.
    /// </summary>
    private sealed class ABuilderThatMakesADiscEach : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs(
            [
                new BuiltDisc("One", 1, 1, System.IO.Path.Combine(outputFolder, "One", "disc"), 1000),
                new BuiltDisc("Two", 1, 1, System.IO.Path.Combine(outputFolder, "Two", "disc"), 1000),
                new BuiltDisc("Three", 1, 1, System.IO.Path.Combine(outputFolder, "Three", "disc"), 1000)
            ]));
    }

    /// <summary>
    /// A disc each where one of the games needed a set of its own.
    /// </summary>
    private sealed class ABuilderThatMakesADiscEachAndOneSet : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs(
            [
                new BuiltDisc("Small", 1, 1, System.IO.Path.Combine(outputFolder, "Small", "disc"), 100),
                new BuiltDisc("Big", 1, 2, System.IO.Path.Combine(outputFolder, "Big", "disc D1"), 1000),
                new BuiltDisc("Big", 2, 2, System.IO.Path.Combine(outputFolder, "Big", "disc D2"), 1000)
            ]));
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

    private sealed class ImagesThatWork : IMakeImages
    {
        internal string? Worry { get; init; }

        internal int Made { get; private set; }

        /// <summary>Every image it was asked for, in order.</summary>
        internal List<string> Paths { get; } = [];

        /// <summary>
        /// Whether each one was asked for with the older file systems.
        /// </summary>
        /// <remarks>
        /// The only place that option goes. Nothing else in the program reads
        /// it, so a checkbox that failed to reach here would be a checkbox
        /// that did nothing at all.
        /// </remarks>
        internal List<bool> Older { get; } = [];

        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct)
        {
            Made++;
            Paths.Add(imagePath);
            Older.Add(wantsLegacyFileSystems);
            progress?.Report(new ImageProgress(1, 2));

            return Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 1024, Worry));
        }
    }

    private sealed class ImagesThatRefuse : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(
                Refusals.TheOldImageWillNotMoveAside(imagePath, "it is mounted"));
    }

    // ------------------------------------------------------------ the machine

    /// <summary>A window that has already built something, ready to burn it.</summary>
    private static async Task<MakeADisc> Built(
        IFindRecorders drives,
        IBurnDiscs? burner = null,
        IEraseDiscs? eraser = null)
    {
        MakeADisc window = New(drives: drives, burner: burner, eraser: eraser);
        window.OutputFolder = @"C:\out";
        window.Add(AGame("A Game", 1000));

        await window.MakeIt();

        return window;
    }

    private static Task<Recorder?> NeverAsked(IReadOnlyList<Recorder> ready) =>
        throw new InvalidOperationException("It should not have had to ask which drive.");

    private static Task<Recorder?> NeverAskedAboutErasing(IReadOnlyList<Recorder> could) =>
        throw new InvalidOperationException("It should not have had to ask about erasing.");

    private static Task<bool> Closes(Recorder drive) => Task.FromResult(true);

    /// <summary>A disc that was built before, read back off a file.</summary>
    private static Reopened AReopenedDisc()
    {
        GameFolder game = AGame("The Witcher", 1000);

        DiscProject was = new(
            "AN OLD DISC",
            [game],
            new DiscOptions(CutFiles: true),
            Artwork: new DiscArtwork(IconPath: @"D:\art\icon.png"),
            Extras: new DiscExtras(@"D:\manual.pdf", null, [@"D:\read me.txt"]),
            AddOns: [new AddOn(0, @"D:\patch_the_witcher_1_to_2.exe", "Update 2", 100)]);

        return new Reopened(was, Media.Dvd9, @"D:\out", [], []);
    }

    private static Recorder ABlankIn(string drive) =>
        new($"id-{drive}", drive, "A Drive", 2, true, 360_000, [750]);

    private static Recorder AUsedCdRwIn(string drive) =>
        new($"id-{drive}", drive, "A Drive", 3, false, 0, [750]);

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    /// <summary>
    /// A drive whose COM object has been pulled out from under the question.
    /// </summary>
    /// <remarks>
    /// Not a COMException. That is the point: everything in the burning code
    /// caught COMException, this is not one, and so it walked out of the
    /// listing, out of the view model and into a message box.
    /// </remarks>
    private sealed class ADriveThatComesApart : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
            throw new System.Runtime.InteropServices.InvalidComObjectException(
                "Attempt has been made to use a COM object that does not have a backing class factory.");
    }

    private sealed class TheseDrives(params Recorder[] drives) : IFindRecorders
    {
        private int _asked;

        /// <summary>
        /// What the drive holds the next time it is asked, which is how a disc
        /// that has just been erased stops being the one in the way.
        /// </summary>
        internal Recorder? AfterErasing { get; set; }

        public IReadOnlyList<Recorder> All() =>
            _asked++ > 0 && AfterErasing is not null ? [AfterErasing] : drives;
    }

    private sealed class ABurnerThatWorks : IBurnDiscs
    {
        internal string? WroteTo { get; private set; }

        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct)
        {
            WroteTo = onto.Drive;
            progress?.Report(new BurnProgress("writing the disc", 0.5, 30));

            return Task.FromResult<Outcome<BurnedDisc>>(new BurnedDisc(onto.Drive, 47, 16, true));
        }
    }

    /// <summary>An eraser that counts its way along, as a drive does.</summary>
    /// <summary>An eraser that throws the way COM interop does.</summary>
    /// <summary>An eraser that asks the window a question while it works.</summary>
    private sealed class AnEraserThatAsks(Action looking) : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct)
        {
            looking();
            return Task.FromResult<Outcome<string>>("blank again");
        }
    }

    private sealed class AnEraserThatFallsOver : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            throw new InvalidComObjectException(
                "Attempt has been made to use a COM object that does not have a backing class factory.");
    }

    private sealed class AnEraserThatCounts : IEraseDiscs
    {
        public async Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct)
        {
            for (int second = 0; second <= 30; second += 10)
            {
                progress?.Report(new EraseProgress($"erasing the disc, {second} of about 30 seconds", second / 30.0));

                // Progress<T> posts its callbacks rather than calling them,
                // so without a moment here they arrive after this returns,
                // by which time the window has stopped working and throws
                // them away. A real erase takes half a minute and has no such
                // problem; a fake that finishes instantly does.
                await Task.Delay(20, ct);
            }

            return "done";
        }
    }

    private sealed class AnEraserThatWorks : IEraseDiscs
    {
        internal string? Erased { get; private set; }

        public Task<Outcome<string>> BlankIt(Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct)
        {
            Erased = disc.Drive;
            progress?.Report(new EraseProgress("erasing the disc", 0));

            return Task.FromResult<Outcome<string>>("blank again");
        }
    }

    /// <summary>
    /// A disk where every folder holds the same one file, so that a disc
    /// checked against what was built comes back matching.
    /// </summary>
    private sealed class ADiskWithADiscOnIt : IFileSystem
    {
        public bool FolderExists(string path) => true;

        public FolderListing List(string path) => new(path, [new ListedFile("setup_a_game.exe", 1000)]);
    }

    /// <summary>An output folder with a built image already in it.</summary>
    private sealed class ADiskWithAnImageOnIt : IFileSystem
    {
        public bool FolderExists(string path) => true;

        public FolderListing List(string path) =>
            new(path, [new ListedFile("AN OLD DISC.iso", 4_000_000)]);
    }

    /// <summary>A disk that remembers which folders were looked in.</summary>
    private sealed class ADiskThatRemembers : IFileSystem
    {
        internal List<string> Listed { get; } = [];

        public bool FolderExists(string path) => true;

        public FolderListing List(string path)
        {
            Listed.Add(path);
            return new FolderListing(path, [new ListedFile("setup_a_game.exe", 1000)]);
        }
    }

    /// <summary>A disc with checksums.sha256 on it, as every disc has.</summary>
    private sealed class ADiscCarryingItsOwnList : IFileSystem
    {
        private static readonly string[] OnIt = ["autorun.inf", "game.exe"];

        public bool FolderExists(string path) => true;

        public FolderListing List(string path) =>
            new(path, [.. OnIt.Select(file => new ListedFile(file, 10)), new ListedFile(ChecksumList.FileName, 90)]);

        public string? ReadText(string path)
        {
            if (!path.EndsWith(ChecksumList.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            System.Text.StringBuilder text = new();
            text.Append("# DiscWright checksum list for A GAME\n");
            text.Append("# ").Append(OnIt.Length).Append(" files, 20 bytes, SHA-256, sha256sum format.\n");

            foreach (string file in OnIt)
            {
                text.Append(AHasherThatAgrees.TheAnswer).Append(" *").Append(file).Append('\n');
            }

            return text.ToString();
        }
    }

    /// <summary>A hasher whose answer always matches the list above.</summary>
    private sealed class AHasherThatAgrees : IHasher
    {
        internal const string TheAnswer = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        public string Sha256(string path, CancellationToken ct) => TheAnswer;
    }

    private sealed class NoHashing : IHasher
    {
        public string Sha256(string path, CancellationToken ct) => string.Empty;
    }

    /// <summary>Somewhere nothing has ever been published.</summary>
    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "no asking in a test"));
    }

    /// <summary>A preview nobody looks at.</summary>
    private sealed class NoPreview : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            Refusals.ThereIsNothingToPutOnADisc();
    }

    /// <summary>Pictures that can all be read, which is the ordinary case.</summary>
    private sealed class PicturesThatRead : IImages
    {
        internal string? Refuse { get; init; }

        public ImageFacts Read(string path) =>
            Refuse is null ? new ImageFacts(true, 512, 512, null) : new ImageFacts(false, 0, 0, Refuse);

        public void WriteIcon(string source, string destination)
        {
        }

        public void WriteBackground(string source, string destination, int width, int height, bool asIs)
        {
        }
    }

    [Fact]
    public void The_list_of_sizes_offers_sizes_and_goes_on_offering_them()
    {
        MakeADisc screen = New();

        ASizeToAimAt[] before = [.. screen.DiscSizes];

        screen.CutFiles = true;
        screen.CutFiles = false;
        screen.CutFiles = true;

        // Ticking the box used to reword every row, so the list read
        // "CD-R 700 MB, split across discs" throughout and the plain sizes
        // were gone from the one list whose job is to offer sizes. Whether a
        // file is split is the checkbox's business and is said at the
        // checkbox.
        Assert.All(screen.DiscSizes, size => Assert.DoesNotContain("split", size.InWords, StringComparison.Ordinal));
        Assert.Equal(Media.Cd.Name, screen.DiscSizes[0].InWords);

        // The same rows, and not merely rows that read the same. A dropdown
        // handed a list of new objects drops its selection, because the row
        // it is holding is not in the list it looks in.
        Assert.Equal(before.Length, screen.DiscSizes.Count);

        for (int at = 0; at < before.Length; at++)
        {
            Assert.Same(before[at], screen.DiscSizes[at]);
        }
    }

    [Fact]
    public void Keeps_the_size_that_was_chosen_when_the_split_box_is_tried()
    {
        MakeADisc screen = New();

        // Built again rather than taken from the list, the way a size comes
        // back out of a saved project file: equal, and not the same object.
        // A row looked up by identity would come back empty here and the
        // dropdown would show a blank where the choice used to be.
        //
        // Not DVD5, which is what the screen starts on. Setting a size that
        // is value-equal to the one already held changes nothing, so the
        // stranger would never be stored and this would prove nothing.
        Medium stranger = new(Media.Dvd9.Key, Media.Dvd9.Name, Media.Dvd9.Capacity);
        screen.Medium = stranger;
        Assert.False(ReferenceEquals(stranger, Media.Dvd9), "the premise of this test");

        screen.CutFiles = true;
        screen.CutFiles = false;

        // A chosen row compared by identity would come back empty here and
        // the dropdown would show a blank where the choice used to be.
        Assert.NotNull(screen.TheSizeChosen);
        Assert.Equal(Media.Dvd9.Key, screen.TheSizeChosen!.Medium.Key);
        Assert.Equal(Media.Dvd9.Name, screen.TheSizeChosen.InWords);
    }

    [Fact]
    public void Choosing_a_row_chooses_the_size_under_it()
    {
        MakeADisc screen = New();

        screen.TheSizeChosen = screen.DiscSizes.Single(size => size.Medium.Key == Media.Dvd9.Key);

        // What gets saved is a size and never a sentence about one.
        Assert.Equal(Media.Dvd9, screen.Medium);
    }

    // ------------------------------------------- what the whole list comes to

    [Fact]
    public void Adds_the_whole_list_up()
    {
        MakeADisc screen = New();
        screen.Add(AGame("Hollow Knight", 1_000_000));
        screen.Add(AGame("Gothic", 3_000_000));

        // Every row showed its own size and a game showed the sum of what was
        // under it. Nothing said what the lot came to, so with four games on
        // the window it was an addition somebody had to do by eye.
        Assert.Equal("3.8 MB in 2 files", screen.WhatIsOnIt);
    }

    [Fact]
    public void The_total_follows_the_ticks()
    {
        MakeADisc screen = New();
        screen.Add(AGame("Hollow Knight", 1_000_000, parts: 1));

        Assert.Equal("1.9 MB in 2 files", screen.WhatIsOnIt);

        // What goes on the disc is what is ticked. A total that counted rows
        // somebody had just turned off would be worse than no total.
        screen.WhatGoesOnIt[0].Inside[1].IsOn = false;

        Assert.Equal("976.6 KB in one file", screen.WhatIsOnIt);
    }

    [Fact]
    public void Says_nothing_about_a_total_with_nothing_to_total()
    {
        MakeADisc screen = New();

        Assert.Equal(string.Empty, screen.WhatIsOnIt);

        screen.Add(AGame("Hollow Knight", 1_000_000));
        screen.WhatGoesOnIt[0].IsOn = false;

        // Everything turned off is something somebody has just done on
        // purpose, and the line under this one already says so.
        Assert.Equal(string.Empty, screen.WhatIsOnIt);
        Assert.NotNull(screen.WhatIsLeftOff);
    }

    [Fact]
    public void Tells_the_window_the_total_changed()
    {
        MakeADisc screen = New();
        List<string> told = [];
        screen.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        screen.Add(AGame("Hollow Knight", 1_000_000));

        // Adding a game ends at Reconsider rather than at the ticks, which is
        // why this is told from there: a total nobody is told about keeps
        // whatever the window read out of it the first time, which on an
        // empty window is nothing at all.
        Assert.Contains(nameof(MakeADisc.WhatIsOnIt), told);

        told.Clear();
        screen.WhatGoesOnIt[0].IsOn = false;
        Assert.Contains(nameof(MakeADisc.WhatIsOnIt), told);
    }

    // ----------------------------------------- what splitting actually does

    [Fact]
    public void Says_how_many_pieces_a_file_will_really_be_cut_into()
    {
        MakeADisc screen = Split(ADiscHolding(1000), 2500);

        // The box used to say "across two discs" and this file needs three.
        // The planner has always cut into as many pieces as the file and the
        // disc come to between them; only the label said two.
        Assert.Contains("3 pieces", screen.WhatSplittingWouldDo, StringComparison.Ordinal);

        // Named and measured, because which file is about to be cut in half
        // is the thing somebody wants to know.
        Assert.Contains("setup_Big.exe", screen.WhatSplittingWouldDo, StringComparison.Ordinal);
        Assert.Contains("a disc each", screen.WhatSplittingWouldDo, StringComparison.Ordinal);
    }

    [Fact]
    public void The_number_of_pieces_follows_the_size_of_disc_that_was_chosen()
    {
        MakeADisc screen = Split(ADiscHolding(1000), 6000);

        Assert.Contains("6 pieces", screen.WhatSplittingWouldDo, StringComparison.Ordinal);

        // The same file, a bigger disc. Nothing about this is fixed at two,
        // and the thing that decides it is the size somebody chose.
        screen.Medium = ADiscHolding(3000);
        Assert.Contains("2 pieces", screen.WhatSplittingWouldDo, StringComparison.Ordinal);

        // Bigger again, and now it fits whole.
        screen.Medium = ADiscHolding(9000);
        Assert.Equal(
            "Nothing here is bigger than one disc, so nothing will be cut.",
            screen.WhatSplittingWouldDo);
    }

    [Fact]
    public void Counts_several_files_that_will_be_cut()
    {
        MakeADisc screen = New();
        screen.OutputFolder = @"C:\out";
        screen.Medium = ADiscHolding(1000);
        screen.CutFiles = true;
        screen.Add(AGame("Big", 2500));
        screen.Add(AGame("Huge", 2500));

        Assert.Equal("2 files will be cut, into 6 pieces between them.", screen.WhatSplittingWouldDo);
    }

    [Fact]
    public void Says_nothing_about_splitting_while_the_box_is_off()
    {
        MakeADisc screen = Split(ADiscHolding(1000), 2500);
        Assert.NotEqual(string.Empty, screen.WhatSplittingWouldDo);

        screen.CutFiles = false;

        // Unticked, a file too big for a disc is a refusal with its own
        // words, and this would be a second sentence about the same thing.
        Assert.Equal(string.Empty, screen.WhatSplittingWouldDo);
        Assert.NotNull(screen.WhyNot);
    }

    [Fact]
    public void Says_nothing_about_splitting_when_the_box_is_off_and_all_is_well()
    {
        // The case above cannot prove this on its own: with the box off, a
        // file too big for a disc is REFUSED, and the refusal clears this
        // line whatever the rule says. A planted fault that showed the line
        // whether the box was ticked or not walked straight through it.
        //
        // So: a project that plans perfectly well, with the box off. The
        // line has to stay quiet because nobody asked about splitting.
        MakeADisc screen = New();
        screen.OutputFolder = @"C:\out";
        screen.Medium = ADiscHolding(1000);
        screen.Add(AGame("Small", 100));

        Assert.Null(screen.WhyNot);
        Assert.False(screen.CutFiles);
        Assert.Equal(string.Empty, screen.WhatSplittingWouldDo);

        // And it speaks up the moment somebody does ask.
        screen.CutFiles = true;
        Assert.Equal(
            "Nothing here is bigger than one disc, so nothing will be cut.",
            screen.WhatSplittingWouldDo);
    }

    [Fact]
    public void Says_nothing_about_splitting_with_nothing_on_the_window()
    {
        MakeADisc screen = New();
        screen.CutFiles = true;

        // Somebody who has not added a game yet has done nothing wrong, and
        // an empty window saying anything at all is the program complaining
        // about its own starting state.
        Assert.Equal(string.Empty, screen.WhatSplittingWouldDo);
    }

    [Fact]
    public void Tells_the_window_when_what_splitting_would_do_changes()
    {
        MakeADisc screen = Split(ADiscHolding(1000), 2500);

        List<string> told = [];
        screen.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        screen.Medium = ADiscHolding(3000);

        // A line nobody is told about keeps whatever the window read out of
        // it the first time, which here is a count of pieces that is no
        // longer true.
        Assert.Contains(nameof(MakeADisc.WhatSplittingWouldDo), told);
    }

    /// <summary>A window with one game too big for the disc, and the box on.</summary>
    private static MakeADisc Split(Medium medium, int bytes)
    {
        MakeADisc screen = New();
        screen.OutputFolder = @"C:\out";
        screen.Medium = medium;
        screen.CutFiles = true;
        screen.Add(AGame("Big", bytes));

        return screen;
    }

    [Fact]
    public void Tells_the_window_what_ticking_the_split_box_changed()
    {
        MakeADisc screen = New();
        screen.OutputFolder = @"C:\out";
        screen.Medium = Media.Cd;
        screen.Add(AGame("Too Big", 800_000_000));

        List<string> told = [];
        screen.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        screen.CutFiles = true;

        // The box itself, the options it belongs to, and the line that says
        // what the project will now make, which is the thing ticking it
        // actually changes.
        Assert.Contains(nameof(MakeADisc.CutFiles), told);
        Assert.Contains(nameof(MakeADisc.Options), told);
        Assert.Contains(nameof(MakeADisc.WhatItWillMake), told);

        // And NOT the list of sizes, which no longer has anything to say.
        // It used to be reworded here, which both hid the plain sizes and
        // made the dropdown drop its selection.
        Assert.DoesNotContain(nameof(MakeADisc.DiscSizes), told);
    }

    // ------------------------------------------------- the tree of what goes on

    [Fact]
    public void Grows_a_branch_of_the_tree_for_every_game()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 500));
        window.Add(AGame("Two", 500));

        Assert.Equal(["One", "Two"], window.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void Taking_a_file_off_the_tree_changes_what_will_be_built()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Add(AGame("One", 900, parts: 2));

        long whole = window.Games[0].Bytes;
        AThingOnTheDisc thing = window.WhatGoesOnIt[0].EveryThing().Last();

        thing.IsOn = false;

        // Not a note kept beside the game: the game itself now carries fewer
        // files, which is what every size, refusal and disc count is worked
        // out from.
        Assert.True(window.Games[0].Bytes < whole);
        Assert.Equal(whole - thing.Bytes, window.Games[0].Bytes);
    }

    [Fact]
    public void Says_what_is_being_left_off_and_how_much_of_it()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 900, parts: 2));

        Assert.Null(window.WhatIsLeftOff);

        window.WhatGoesOnIt[0].EveryThing().Last().IsOn = false;

        Assert.StartsWith("Leaving off 1 thing", window.WhatIsLeftOff, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_plainly_when_a_disc_will_no_longer_install_the_game()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 900));

        AThingOnTheDisc installer = window.WhatGoesOnIt[0]
            .EveryThing()
            .Single(each => each.What == TheTreeOfWhatGoesOn.TheInstaller);

        installer.IsOn = false;

        // Allowed, because this window refuses by explaining rather than by
        // greying things out. Said out loud, because a disc that cannot
        // install the game is not something to find out afterwards.
        //
        // Said apart from the count, because the count is ordinary ink and
        // this is the only part worth a warning colour.
        Assert.Equal("Without its installer, the disc will not install the game.", window.WhyThatMatters);
        Assert.DoesNotContain("install", window.WhatIsLeftOff!, StringComparison.Ordinal);
        Assert.Null(window.Games[0].Installer);
    }

    [Fact]
    public void Tells_the_window_about_both_lines_when_a_tick_changes()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 900));
        List<string> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        window.WhatGoesOnIt[0]
            .EveryThing()
            .Single(each => each.What == TheTreeOfWhatGoesOn.TheInstaller)
            .IsOn = false;

        // Two separate pieces of text on the window, so both have to be
        // announced or one of them goes on saying what was true a tick ago.
        // The amber one is the one that would be missed, because it is only
        // ever there in the one case.
        Assert.Contains(nameof(MakeADisc.WhatIsLeftOff), told);
        Assert.Contains(nameof(MakeADisc.WhyThatMatters), told);
    }

    [Fact]
    public void Remove_takes_the_one_file_that_was_chosen_and_leaves_the_game()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 900, parts: 2));
        window.Add(AGame("Two", 500));

        AThingOnTheDisc aFile = window.WhatGoesOnIt[0].EveryThing().First();
        int was = window.WhatGoesOnIt[0].EveryThing().Count();

        window.TakeOff(aFile);

        // The first version took the whole game whatever was selected, so
        // clicking a file and pressing Remove emptied the window.
        Assert.Equal(["One", "Two"], window.Games.Select(game => game.Name));
        Assert.Equal(was - 1, window.WhatGoesOnIt[0].EveryThing().Count());
    }

    [Fact]
    public void Remove_takes_the_whole_game_when_the_game_is_what_was_chosen()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 900));
        window.Add(AGame("Two", 500));

        window.TakeOff(window.WhatGoesOnIt[0]);

        Assert.Equal(["Two"], window.Games.Select(game => game.Name));
        Assert.Equal(["Two"], window.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void The_other_button_clears_out_everything_left_off()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 900, parts: 2));

        AThingOnTheDisc[] rows = [.. window.WhatGoesOnIt[0].EveryThing()];
        rows[0].IsOn = false;
        int was = rows.Length;

        window.TakeOffWhatIsLeftOff();

        // The tidying-up gesture after deciding what is not going on.
        // Unticked is "not on this disc, still listed"; removed is gone.
        Assert.Equal(was - 1, window.WhatGoesOnIt[0].EveryThing().Count());
        Assert.All(window.WhatGoesOnIt[0].EveryThing(), each => Assert.True(each.IsOn));
    }

    [Fact]
    public void Remove_with_nothing_picked_takes_nothing_off()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 900, parts: 2));

        AThingOnTheDisc[] rows = [.. window.WhatGoesOnIt[0].EveryThing()];
        rows[0].IsOn = false;
        int was = rows.Length;

        // This used to clear out everything unticked, which is a different
        // thing wearing the same button. It is still one press away, on a
        // button that says what it will take.
        window.TakeOff(null);

        Assert.Equal(was, window.WhatGoesOnIt[0].EveryThing().Count());
        Assert.False(window.WhatGoesOnIt[0].EveryThing().First().IsOn);
    }

    [Fact]
    public void Says_when_a_patch_is_left_with_nothing_to_patch()
    {
        MakeADisc window = New();

        // Somewhere to put it, so that the refusal this test is about is the
        // only one that could be on the window. Without it the answer at the
        // end is "there is nowhere to put it", which is true and is not what
        // is being asked.
        window.OutputFolder = @"C:\out";
        window.Add(AGame("One", 900), [AnExtra("patch.exe", 70)]);

        Assert.Null(window.APatchWithNothingToPatch);

        // Every file of the game off, its add-on still on.
        foreach (AThingOnTheDisc thing in window.WhatGoesOnIt[0].EveryThing())
        {
            if (thing.What != TheTreeOfWhatGoesOn.AnAddOn) { thing.IsOn = false; }
        }

        // Allowed, because somebody may be making a disc of patches on
        // purpose. Said out loud, because it is the kind of disc somebody
        // makes by accident far more often than on purpose.
        Assert.Contains("nothing to install onto", window.APatchWithNothingToPatch ?? string.Empty, StringComparison.Ordinal);

        // A warning and never a refusal. Somebody making a disc of patches on
        // purpose is doing something unusual, not something wrong.
        Assert.Null(window.WhyNot);
    }

    [Fact]
    public void An_opened_project_shows_its_games_in_the_tree()
    {
        MakeADisc window = New();

        window.OpenThis(AReopenedDisc());

        // The tree is what the window shows. Without this, an opened project
        // is a window with nothing in it while everything else insists the
        // games are there: the size says one disc and Build it is lit.
        Assert.NotEmpty(window.Games);
        Assert.Equal(window.Games.Count, window.WhatGoesOnIt.Count);
        Assert.Equal(window.Games.Select(game => game.Name), window.WhatGoesOnIt.Select(each => each.Name));
    }

    [Fact]
    public void Opening_a_second_project_does_not_leave_the_first_ones_rows_behind()
    {
        MakeADisc window = New();

        window.OpenThis(AReopenedDisc());
        window.OpenThis(AReopenedDisc());

        // Two collections that have to agree, and the one that is easy to
        // forget to clear is the one that is easy to forget to fill.
        Assert.Equal(window.Games.Count, window.WhatGoesOnIt.Count);
    }

    [Fact]
    public void Starting_again_empties_the_tree_as_well_as_the_list()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 500));

        window.StartAgain();

        // Two collections that have to agree, so the one that is easy to
        // forget is the one worth a test.
        Assert.Empty(window.Games);
        Assert.Empty(window.WhatGoesOnIt);
    }

    // ------------------------------------------- a drive that is still reading

    [Fact]
    public async Task Waits_for_a_drive_that_has_not_finished_reading_the_disc()
    {
        ADriveStillThinking slow = new(quietTimes: 2, mediaType: 3, blank: true, freeSectors: 359_847);
        BuilderThatWorks builder = new();
        ABurnerThatWorks burner = new();
        MakeADisc window = New(builder: builder, drives: slow, burner: burner);

        window.OutputFolder = @"C:\out";
        window.Add(AGame("One", 500));
        await window.MakeIt();

        await window.BurnIt(
            @"C:\out\ONE.iso",
            _ => Task.FromResult<Recorder?>(null),
            _ => Task.FromResult<Recorder?>(null),
            _ => Task.FromResult(true));

        // Asked the moment a tray shuts, a drive can take ten seconds to say
        // what is in it. Taking the first silence as an answer turns a disc
        // that is merely still spinning up into a refusal.
        Assert.True(slow.Asked > 2, $"it gave up after {slow.Asked} ask(s)");
        Assert.Null(window.ItWentWrong);
        Assert.Equal("D:", burner.WroteTo);
    }

    [Fact]
    public async Task Still_offers_to_erase_a_used_disc_that_was_slow_to_be_read()
    {
        ADriveStillThinking slow = new(quietTimes: 2, mediaType: 3, blank: false, freeSectors: 0);
        MakeADisc window = New(drives: slow, builder: new BuilderThatWorks());

        window.OutputFolder = @"C:\out";
        window.Add(AGame("One", 500));
        await window.MakeIt();

        bool asked = false;

        await window.BurnIt(
            @"C:\out\ONE.iso",
            _ => Task.FromResult<Recorder?>(null),
            _ => { asked = true; return Task.FromResult<Recorder?>(null); },
            _ => Task.FromResult(true));

        // This is the whole point of waiting. A drive that has not answered
        // yet has no media type, so ErasingWouldHelp is false, so the offer to
        // erase never appears and somebody who deliberately put a used disc in
        // is told the drive would not say what is in it, with nothing to do
        // about it. Found exactly that way.
        Assert.True(asked, "it never offered to erase the disc");
        Assert.DoesNotContain("would not say", window.ItWentWrong?.Headline ?? string.Empty, StringComparison.Ordinal);
    }


    // --------------------------------------------- erasing a disc on its own

    [Fact]
    public async Task Can_erase_only_when_there_is_a_disc_erasing_would_help()
    {
        ADriveHolding drive = new(3, true, 359_847);
        MakeADisc window = New(drives: drive);
        await window.LookAtTheDrives();

        // A blank one needs no erasing.
        Assert.False(window.CanErase);

        drive.NowHolds(3, false, 0);
        await window.LookAtTheDrives();
        Assert.True(window.CanErase);

        // A pressed disc cannot be erased however much somebody wants it to.
        drive.NowHolds(1, false, 0);
        await window.LookAtTheDrives();
        Assert.False(window.CanErase);
    }

    [Fact]
    public async Task Tells_the_window_when_erasing_becomes_possible()
    {
        ADriveHolding drive = new(3, true, 359_847);
        MakeADisc window = New(drives: drive);
        await window.LookAtTheDrives();

        Assert.False(window.CanErase);

        List<string> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        drive.NowHolds(3, false, 0);
        await window.LookAtTheDrives();

        // The property alone was already right. Nothing announced it, so the
        // button was judged once at startup with no drives yet and stayed
        // dead for the rest of the run, with a disc sitting in the drive that
        // it could have erased. Reading the property in a test works without
        // any notification at all, which is why that test passed.
        Assert.True(window.CanErase);
        Assert.Contains(nameof(MakeADisc.CanErase), told);
    }

    [Fact]
    public async Task A_drive_that_throws_while_being_asked_is_still_an_answer()
    {
        // The box reading "Attempt has been made to use a COM object that
        // does not have a backing class factory" that Lazar got on pressing
        // Erase a disc. It came from asking the drives what they were
        // holding, which happens before anything is erased and which sat
        // outside the catch-all written for exactly this.
        //
        // InvalidComObjectException on purpose, and not any old exception:
        // it is not a COMException, which is what everything in the burning
        // code was catching, and that is the whole reason it got out.
        MakeADisc window = New(drives: new ADriveThatComesApart());

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could.Count > 0 ? could[0] : null));

        Refusal said = Assert.IsType<Refusal>(window.ItWentWrong);
        Assert.Contains("erasing the disc", said.Headline, StringComparison.Ordinal);

        // And the window is not left looking busy for the rest of the run.
        Assert.False(window.IsWorking);
    }

    [Fact]
    public void The_window_really_carries_out_the_way_out_it_is_offering()
    {
        // The screen decides which remedies are worth offering and the
        // window carries them out, and nothing in this project can reference
        // the code behind, so cutting that wire left the whole suite green
        // and a word on the window that did nothing when pressed. Read off
        // disk, like the markup guards.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        int from = code.IndexOf("void TakeTheWayOut", StringComparison.Ordinal);
        Assert.True(from >= 0, "The window no longer does anything about a refusal.");

        string handler = code[from..];
        handler = handler[..handler.IndexOf(Environment.NewLine + "    }", StringComparison.Ordinal)];

        Assert.Contains("RemedyKind.PickFolder", handler, StringComparison.Ordinal);
        Assert.Contains("ChooseWhereItGoes(", handler, StringComparison.Ordinal);

        // The two that were computed and never carried out: a name to put
        // the cursor in, and a larger disc to aim at.
        Assert.Contains("RemedyKind.FocusField", handler, StringComparison.Ordinal);
        Assert.Contains("PutTheCursorOnTheName(", handler, StringComparison.Ordinal);
        Assert.Contains("RemedyKind.ChangeMedium", handler, StringComparison.Ordinal);
        Assert.Contains("_screen.UseTheLargerDisc(", handler, StringComparison.Ordinal);

        // And the worry's, which is about the name too.
        int worry = code.IndexOf("void TakeTheWorrysWayOut", StringComparison.Ordinal);
        Assert.True(worry >= 0, "The window no longer does anything about a worry.");
        Assert.Contains("PutTheCursorOnTheName(", code[worry..], StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_does_something_about_every_kind_of_step()
    {
        // TakingFoldersOn decides what to do and the window does it. Every
        // kind of step it can answer with has to be handled, because a kind
        // the window quietly ignores is work silently thrown away: dropping
        // AlsoOnTheDisc would lose the files somebody was promised go on the
        // disc either way, and nothing else would notice.
        //
        // Read off disk, because nothing in this project can reference the
        // code behind.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Type[] kinds = [.. typeof(TakingFoldersOn.WhatToDo).GetNestedTypes()
            .Where(each => each.IsSubclassOf(typeof(TakingFoldersOn.WhatToDo)))];

        // Four today, the fourth being the same folder added again. The count
        // is asserted so a fifth cannot arrive and be handled nowhere while
        // this still reads as covering everything.
        Assert.Equal(4, kinds.Length);

        foreach (Type kind in kinds)
        {
            Assert.Contains(
                $"TakingFoldersOn.WhatToDo.{kind.Name}(",
                code,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_window_really_asks_which_file_starts_a_game()
    {
        // Nothing asked at all, and the menu ran a game only when a folder
        // happened to hold exactly one plausible executable. Nothing in this
        // project can reference the code behind, so it is read off disk.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        int from = code.IndexOf("GameFolder WhicheverStartsIt", StringComparison.Ordinal);
        Assert.True(from >= 0, "Nothing asks which file starts a folder of game files.");

        string asking = code[from..];
        asking = asking[..asking.IndexOf(";" + Environment.NewLine, StringComparison.Ordinal)];

        // The dialog, and an answer that narrows the candidates to the one
        // chosen, which is all the menu looks at.
        Assert.Contains("PickOne.From(", asking, StringComparison.Ordinal);
        Assert.Contains("LauncherCandidates = [chosen]", asking, StringComparison.Ordinal);

        // And it is really handed to the thing that decides when to ask.
        //
        // WHEN to ask used to be checked here as well, as the text
        // "LauncherCandidates.Count < 2" sitting in this method. It has moved
        // into TakingFoldersOn, where Fewer_than_two_and_nobody_is_asked runs
        // it rather than reading it. This guard is weaker than it was on
        // purpose: a real test took the part worth having.
        Assert.Contains("TakingFoldersOn.TheQuestions asking = new(", code, StringComparison.Ordinal);
        Assert.Contains("WhicheverStartsIt);", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_asks_the_drives_again_whenever_a_disc_goes_in_or_out()
    {
        // The hook is the only way any of the above is reached while the
        // window is open. Nothing in this project can reference the code
        // behind, so two faults that cut the wire, one by never matching the
        // message and one by matching it and doing nothing, both went by with
        // every test green. Read off disk, like the markup guards.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        // WM_DEVICECHANGE. A disc going into a drive is a device changing.
        Assert.Contains("ADeviceChanged = 0x0219", code, StringComparison.Ordinal);
        Assert.Contains("source.AddHook(WhenSomethingChanged)", code, StringComparison.Ordinal);

        int from = code.IndexOf("IntPtr WhenSomethingChanged", StringComparison.Ordinal);
        Assert.True(from >= 0, "The window no longer listens for anything at all.");

        string hook = code[from..];
        hook = hook[..hook.IndexOf("return IntPtr.Zero", StringComparison.Ordinal)];

        Assert.Contains("if (message == ADeviceChanged)", hook, StringComparison.Ordinal);
        Assert.Contains("_screen.LookAtTheDrives()", hook, StringComparison.Ordinal);
    }

    [Fact]
    public void The_words_standing_in_for_the_drive_picker_fade_the_right_way_round()
    {
        // Turning the two around leaves every markup guard passing: the
        // binding is still there, the converter is still named, and the words
        // are simply bright while everything around them is held and dim
        // while everything around them is live. Nothing in this project can
        // reference a converter in DiscWright.App, so it is read off disk.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "App.xaml.cs"));

        int from = code.IndexOf("class HeldBack", StringComparison.Ordinal);
        Assert.True(from >= 0, "Nothing fades the words that stand in for the picker any more.");

        string converter = code[from..];
        converter = converter[..converter.IndexOf("ConvertBack", StringComparison.Ordinal)];

        // false is "something is running", which is when it fades.
        Assert.Contains("value is false ? 0.45 : 1.0", converter, StringComparison.Ordinal);
    }

    [Fact]
    public void No_handler_in_the_window_shows_an_exception_on_its_own()
    {
        // Raw machine words in a box with a red cross read as the program
        // falling over. Every one of them is a refusal now: a sentence that
        // says what was happening and that nothing is lost, with whatever
        // Windows said carried inside it.
        //
        // Read off disk, like the markup guards, because nothing in this
        // project can reference the window's code behind.
        string code = System.IO.File.ReadAllText(
            Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Assert.DoesNotContain("MessageBox.Show(this, wrong.Message", code, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Erases_the_disc_that_was_chosen_and_nothing_else()
    {
        AnEraserThatWorks eraser = new();
        MakeADisc window = New(drives: new ADriveHolding(3, false, 0), eraser: eraser);
        await window.LookAtTheDrives();

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        Assert.Equal("D:", eraser.Erased);
        Assert.Null(window.ItWentWrong);
    }

    [Fact]
    public async Task Says_how_far_along_an_erase_is_and_not_only_how_long()
    {
        AnEraserThatCounts eraser = new();
        MakeADisc window = New(drives: new ADriveHolding(3, false, 0), eraser: eraser);
        await window.LookAtTheDrives();

        List<double> bar = [];
        window.PropertyChanged += (_, what) =>
        {
            if (what.PropertyName == nameof(MakeADisc.HowFar)) { bar.Add(window.HowFar); }
        };

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        // Strictly between nought and one, which is the only thing that
        // shows the bar moving WHILE it erases. "Greater than nought" was
        // satisfied by the "blank again" at the end, which sets it to one
        // whether or not a single step along the way was ever passed on, so
        // the first version of this test passed with the plumbing cut.
        Assert.Contains(bar, far => far > 0 && far < 1);
    }

    [Fact]
    public async Task Turns_something_nobody_expected_into_an_answer()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, false, 0), eraser: new AnEraserThatFallsOver());
        await window.LookAtTheDrives();

        // No exception out of here. This is the shape of what reached Lazar
        // as "Attempt has been made to use a COM object that does not have a
        // backing class factory" in a message box at the end of an erase:
        // raw exception text, which reads as a crash and says nothing he
        // could act on.
        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        Assert.NotNull(window.ItWentWrong);
        Assert.Contains("Something went wrong while erasing", window.ItWentWrong!.Headline, StringComparison.Ordinal);
        Assert.Contains("worth reporting", window.ItWentWrong.Because, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stops_working_even_when_something_nobody_expected_happens()
    {
        MakeADisc window = New(drives: new ADriveHolding(3, false, 0), eraser: new AnEraserThatFallsOver());
        await window.LookAtTheDrives();

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        // A window left thinking it is working is one where every button
        // stays dead and nothing says why.
        Assert.False(window.IsWorking);
    }

    [Fact]
    public async Task Is_working_from_the_press_rather_than_from_the_answer()
    {
        // TWO FAULTS IN ONE LINE OF ORDERING, and both of them reached Lazar.
        //
        // Say drops anything said while IsWorking is false, and IsWorking went
        // true only after a drive had been chosen. So the settling's own
        // "waiting for the drive" was thrown away for the whole of the wait: the
        // one sentence written for that moment, never once shown. He pressed
        // Erase, got fifteen seconds of nothing, and thought it had broken.
        //
        // And CanErase is !IsWorking, so the button stayed live through all of
        // it and a second press started the whole thing again underneath.
        AnEraserThatWorks eraser = new();
        MakeADisc window = New(drives: new ADriveHolding(3, false, 0), eraser: eraser);
        await window.LookAtTheDrives();

        Assert.True(window.CanErase, "there is a used rewritable in the drive");

        bool workingWhenAsked = false;
        bool couldPressAgain = true;

        await window.EraseADisc(could =>
        {
            workingWhenAsked = window.IsWorking;
            couldPressAgain = window.CanErase;

            return Task.FromResult<Recorder?>(could[0]);
        });

        Assert.True(workingWhenAsked, "the window has to be working before it asks, or it says nothing");
        Assert.False(couldPressAgain, "the button must be dead while the drives are being asked");

        // And it comes back afterwards, or the button is dead for good.
        Assert.False(window.IsWorking);
    }

    [Fact]
    public async Task Comes_back_to_itself_when_nobody_picked_a_disc()
    {
        // The press turns everything on before the question is asked, so
        // closing the question has to turn it all off again. Without this the
        // window sits working for ever over an erase nobody asked for.
        MakeADisc window = New(drives: new ADriveHolding(3, false, 0), eraser: new AnEraserThatWorks());
        await window.LookAtTheDrives();

        await window.EraseADisc(_ => Task.FromResult<Recorder?>(null));

        Assert.False(window.IsWorking);
        Assert.True(window.CanErase, "the disc is still there and still worth erasing");
    }
    [Fact]
    public async Task Erases_nothing_when_the_question_is_answered_no()
    {
        AnEraserThatWorks eraser = new();
        MakeADisc window = New(drives: new ADriveHolding(3, false, 0), eraser: eraser);
        await window.LookAtTheDrives();

        await window.EraseADisc(_ => Task.FromResult<Recorder?>(null));

        // Which disc gets wiped is never a choice to make on somebody's
        // behalf, and saying no has to mean nothing happened at all.
        Assert.Null(eraser.Erased);
        Assert.Null(window.ItWentWrong);
    }

    [Fact]
    public async Task Says_so_rather_than_throwing_when_there_is_nothing_to_erase()
    {
        AnEraserThatWorks eraser = new();
        MakeADisc window = New(drives: new ADriveHolding(1, false, 0), eraser: eraser);
        await window.LookAtTheDrives();

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could.Count > 0 ? could[0] : null));

        // A pressed disc and an empty tray are ordinary. Neither is a fault,
        // and neither may reach somebody as an exception shown raw.
        Assert.NotNull(window.ItWentWrong);
        Assert.Contains("erasing would help", window.ItWentWrong!.Headline, StringComparison.Ordinal);
        Assert.Null(eraser.Erased);
    }

    [Fact]
    public async Task Waits_for_a_drive_still_reading_before_saying_there_is_nothing_to_erase()
    {
        ADriveStillThinking slow = new(quietTimes: 2, mediaType: 3, blank: false, freeSectors: 0);
        AnEraserThatWorks eraser = new();
        MakeADisc window = New(drives: slow, eraser: eraser);

        await window.EraseADisc(could => Task.FromResult<Recorder?>(could[0]));

        // The same fault as the one that ended a set a disc short: a drive
        // that has not answered yet has no media type, so nothing looks
        // erasable and the honest answer would be wrong.
        Assert.Equal("D:", eraser.Erased);
        Assert.Null(window.ItWentWrong);
    }

}
