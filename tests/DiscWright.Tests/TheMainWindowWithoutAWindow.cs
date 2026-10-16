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

        // And the size says what it is now going to do.
        Assert.EndsWith(", split across discs", window.TheSizeChosen!.InWords, StringComparison.Ordinal);
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
        await builder.Started.Task;

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

    // ----------------------------------------------------------- opening one

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
        Assert.True(window.ArtworkOnDisc);
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

        window.Medium = Media.Bd25;
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
    public async Task Does_not_go_near_the_tray_of_an_empty_drive()
    {
        ATrayThatNeedsAPush tray = new();
        MakeADisc window = New(drives: new ADriveHolding(0, false, 0), tray: tray);

        await window.LookAtTheDrives();

        Assert.Empty(tray.Asked);
        Assert.Equal(string.Empty, window.AboutTheTray);
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
        await builder.Started.Task;

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

    private static MakeADisc New(
        IBuildDiscs? builder = null,
        IMakeImages? images = null,
        IFindRecorders? drives = null,
        IBurnDiscs? burner = null,
        IEraseDiscs? eraser = null,
        IWorkTheTray? tray = null,
        IAskWhatIsReleased? releases = null,
        IShowTheMenu? preview = null,
        IImages? pictures = null) =>
        new(new TheMachine(
            new Codepage1252(),
            builder ?? new BuilderThatWorks(),
            images ?? new ImagesThatWork(),
            drives ?? new NoDrives(),
            burner ?? new ABurnerThatWorks(),
            eraser ?? new AnEraserThatWorks(),
            new ADiskWithADiscOnIt(),
            new NoHashing(),
            new NothingIsRemembered(),
            tray ?? new ATrayThatShuts(),
            releases ?? new NothingIsReleased(),
            preview ?? new NoPreview(),
            pictures ?? new PicturesThatRead(),
            "2.0.0",
            _ => 1000));

    /// <summary>
    /// A machine with nowhere to keep a set's place, which is every test here:
    /// none of them walks a set.
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

    private sealed class AnEraserThatWorks : IEraseDiscs
    {
        internal string? Erased { get; private set; }

        public Task<Outcome<string>> BlankIt(Recorder disc, IProgress<string>? progress, CancellationToken ct)
        {
            Erased = disc.Drive;
            progress?.Report("erasing the disc");

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
        public Outcome<string> Write(DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
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
    public void Says_in_the_list_of_sizes_that_files_will_be_split()
    {
        MakeADisc screen = New();

        Assert.All(screen.DiscSizes, size => Assert.DoesNotContain("split", size.InWords, StringComparison.Ordinal));

        screen.CutFiles = true;

        // Somebody choosing a size is the person who wants to know a file is
        // about to be cut in half. Saying it there beats a refusal further
        // down the window that has to be worked backwards from.
        Assert.All(screen.DiscSizes, size => Assert.EndsWith(", split across discs", size.InWords, StringComparison.Ordinal));
        Assert.StartsWith("CD-R", screen.DiscSizes[0].InWords, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_the_size_that_was_chosen_when_the_words_change()
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

        // The rows are built again, so a chosen row that was compared by
        // identity would come back empty and the dropdown would look blank.
        Assert.NotNull(screen.TheSizeChosen);
        Assert.Equal(Media.Dvd9.Key, screen.TheSizeChosen!.Medium.Key);
        Assert.EndsWith(", split across discs", screen.TheSizeChosen.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public void Choosing_a_row_chooses_the_size_under_it()
    {
        MakeADisc screen = New();

        screen.TheSizeChosen = screen.DiscSizes.Single(size => size.Medium.Key == Media.Dvd9.Key);

        // What gets saved is a size and never a sentence about one.
        Assert.Equal(Media.Dvd9, screen.Medium);
    }

    [Fact]
    public void Tells_the_window_the_list_of_sizes_changed()
    {
        MakeADisc screen = New();
        List<string> told = [];
        screen.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        screen.CutFiles = true;

        // Without both of these the dropdown goes on showing the old words,
        // or shows nothing at all where the choice used to be.
        Assert.Contains(nameof(MakeADisc.DiscSizes), told);
        Assert.Contains(nameof(MakeADisc.TheSizeChosen), told);
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
    public void Remove_clears_out_everything_unticked_when_nothing_is_chosen()
    {
        MakeADisc window = New();
        window.Add(AGame("One", 900, parts: 2));

        AThingOnTheDisc[] rows = [.. window.WhatGoesOnIt[0].EveryThing()];
        rows[0].IsOn = false;
        int was = rows.Length;

        window.TakeOff(null);

        // The tidying-up gesture after deciding what is not going on.
        // Unticked is "not on this disc, still listed"; removed is gone.
        Assert.Equal(was - 1, window.WhatGoesOnIt[0].EveryThing().Count());
        Assert.All(window.WhatGoesOnIt[0].EveryThing(), each => Assert.True(each.IsOn));
    }

    [Fact]
    public void Says_when_a_patch_is_left_with_nothing_to_patch()
    {
        MakeADisc window = New();
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
