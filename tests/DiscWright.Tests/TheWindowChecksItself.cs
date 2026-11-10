using System.Collections;
using System.Reflection;

using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Drives the screen through thousands of random sequences of the things a
/// person can do, and checks after every single step that it still adds up and
/// that it said what changed.
/// </summary>
/// <remarks>
/// <para>
/// Every other test in this project names a case. This one names none. It
/// generates sequences of ticks, untickings, additions, removals, renamings
/// and option changes, and after each step asks two questions that no list of
/// cases can ask:
/// </para>
/// <list type="number">
/// <item>
/// <strong>Does it still add up.</strong> A dozen relationships that have to
/// hold whatever has been done, worked out independently rather than copied
/// from the code they are about.
/// </item>
/// <item>
/// <strong>Did it say what changed.</strong> Every public property is read
/// before and after each step, and anything that moved without being announced
/// is a fault. This is one assertion over sixty three properties, and it is the
/// whole of the class of bug that has cost the most here: a property nobody is
/// told about keeps whatever the window read out of it the first time, so a
/// button stays dead, or a line stays on the answer to the question before
/// last.
/// </item>
/// </list>
/// <para>
/// <strong>Why there is no property testing library here.</strong> The useful
/// part of one is shrinking, and shrinking a sequence of commands is removing
/// commands while it still fails, which is the forty lines below. The generated
/// values are drawn from small fixed sets, so there is nothing in them left to
/// shrink. What a library cannot do is the announcement check, which is all of
/// this file. This project writes its own mutation runner for the same reason.
/// </para>
/// <para>
/// Every run is seeded and the seed is printed, so a failure is a sequence
/// somebody can replay exactly rather than a story about one.
/// </para>
/// </remarks>
public class TheWindowChecksItself
{
    /// <summary>How many sequences, and how long each one is.</summary>
    /// <remarks>
    /// Chosen so the whole thing is under a second. Set DISCWRIGHT_SOAK to run
    /// far more of them, which is what to do after changing the screen.
    /// </remarks>
    private const int Sequences = 200;

    private const int Steps = 40;

    [Fact]
    public void Adds_up_and_says_so_after_everything_anybody_can_do()
    {
        int sequences = Environment.GetEnvironmentVariable("DISCWRIGHT_SOAK") is not null
            ? Sequences * 50
            : Sequences;

        _announcementsChecked = 0;
        _sumsChecked = 0;

        for (int seed = 1; seed <= sequences; seed++)
        {
            if (WhatWentWrong(Walk(seed, Steps)) is not string first)
            {
                continue;
            }

            // The sequence that broke it, cut down to the shortest one that
            // still does. A fault in forty steps is a story; a fault in three
            // is something to fix.
            IReadOnlyList<ADoing> least = TheShortestThatStillFails(Walk(seed, Steps));

            Assert.Fail(
                $"Seed {seed} broke it.{Environment.NewLine}{Environment.NewLine}"
                + $"{first}{Environment.NewLine}{Environment.NewLine}"
                + $"The shortest sequence that still does it:{Environment.NewLine}"
                + string.Join(Environment.NewLine, least.Select((each, at) => $"  {at + 1}. {each.Named}")));
        }

        // Proof that it really looked, because a check that is never reached
        // is a test that passes for the wrong reason. Three faults walked
        // through this file by turning one of these off, and none of them
        // could be caught from inside the walk they were turning off.
        Assert.True(
            _announcementsChecked >= sequences * Steps,
            $"Only {_announcementsChecked} steps were checked for what they announced.");

        Assert.True(
            _sumsChecked >= sequences * Steps,
            $"Only {_sumsChecked} steps were checked for adding up.");

        Assert.True(
            Readable().Length >= 55,
            $"Only {Readable().Length} properties are being watched, so most of the window is unguarded.");
    }

    [Fact]
    public void Notices_a_thing_that_moved_without_saying_so()
    {
        // The guard on the guard. Everything above is one long argument that
        // the screen behaves, and it is worth nothing if the thing doing the
        // arguing has been quietly switched off.
        Dictionary<string, string> before = new() { ["Label"] = "A", ["WhyNot"] = "«nothing»" };
        Dictionary<string, string> after = new() { ["Label"] = "B", ["WhyNot"] = "«nothing»" };

        Assert.Null(WhatMovedWithoutSayingSo(before, after, ["Label"]));
        Assert.Null(WhatMovedWithoutSayingSo(before, before, []));

        string? caught = WhatMovedWithoutSayingSo(before, after, []);

        Assert.NotNull(caught);
        Assert.Contains("Label", caught, StringComparison.Ordinal);

        // And the plain difference, which the dead button check leans on.
        Assert.Null(WhatMoved(before, before, "nothing"));
        Assert.NotNull(WhatMoved(before, after, "something"));
    }

    // ------------------------------- chosen, then unchosen, and really gone

    /// <summary>
    /// Every single thing a person can choose, chosen and then put back, with
    /// the window compared against itself before and after.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the shape of fault that has cost the most and been reported the
    /// most: choose a background and clear it and the panel still offers it;
    /// start a new disc and the manual is still named; change one and the old
    /// one is still there. Every one of them is the same sentence, which is
    /// that something was put back and the window did not notice.
    /// </para>
    /// <para>
    /// The walk through random sequences catches these too, but only when it
    /// happens to walk past one, and it can never name the case. This names
    /// every one of them, and is exhaustive by construction rather than by
    /// luck: the list below is every choosable thing on the window, and
    /// anything added to the window without being added here is caught by
    /// <see cref="Every_choosable_thing_is_in_that_list"/>.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("the menu background")]
    [InlineData("the menu colour")]
    [InlineData("stretch the background to fit")]
    [InlineData("the disc icon")]
    [InlineData("the cover")]
    [InlineData("the disc face")]
    [InlineData("the manual")]
    [InlineData("the folder of extras")]
    [InlineData("the tune")]
    [InlineData("the name")]
    [InlineData("where the discs go")]
    [InlineData("the size of disc")]
    [InlineData("splitting a file too big for one disc")]
    [InlineData("a menu on the disc")]
    [InlineData("a list of what every file should be")]
    [InlineData("readable by Windows 98")]
    [InlineData("a Start Here file")]
    public void Choosing_it_and_putting_it_back_leaves_the_window_exactly_as_it_was(string what)
    {
        ADeskWithAScreenOnIt desk = new();
        MakeADisc screen = desk.Screen;

        // On a window with something on it, because an empty one hides most
        // of what could go stale.
        screen.Add(AGogGame("Hollow Knight", 1000));
        screen.OutputFolder = @"C:\out";

        Dictionary<string, string> before = Everything(screen);

        Choose(screen, what);

        // It has to do something, or the case is proving nothing at all.
        Assert.NotNull(WhatMoved(before, Everything(screen), $"choosing {what}"));

        PutItBack(screen, what);

        string? left = WhatMoved(before, Everything(screen), $"{what} was put back and");

        Assert.True(left is null, left);
    }

    [Fact]
    public void Adding_a_game_and_taking_it_off_leaves_only_what_is_meant_to_survive()
    {
        ADeskWithAScreenOnIt desk = new();
        MakeADisc screen = desk.Screen;

        Dictionary<string, string> before = Everything(screen);

        screen.Add(AGogGame("Hollow Knight", 1000));
        screen.TakeOff(screen.WhatGoesOnIt[0]);

        string? left = WhatMoved(before, Everything(screen), "a game added and taken off");

        // The name and the folder are the two things that stay on purpose:
        // the first game names both, and having to find that folder again
        // every time is its own small insult. Everything else goes.
        Assert.Equal(string.Empty, screen.WhatIsOnIt);
        Assert.Empty(screen.Games);
        Assert.Equal("HOLLOW KNIGHT", screen.Label);

        if (left is not null)
        {
            string[] allowed =
            [
                nameof(MakeADisc.Label), nameof(MakeADisc.Worry), nameof(MakeADisc.OutputFolder),
                nameof(MakeADisc.WhyNot), nameof(MakeADisc.TheWayOut),
                nameof(MakeADisc.WhatTheWayOutIsCalled), nameof(MakeADisc.HasAnythingOnIt),
                nameof(MakeADisc.CanStartAgain),
            ];

            foreach (string line in left.Split(Environment.NewLine).Skip(1))
            {
                string moved = line.Trim().Split(':')[0];

                Assert.True(
                    allowed.Contains(moved),
                    $"{moved} survived a game being added and taken off, and should not have.{left}");
            }
        }
    }

    [Fact]
    public void Every_choosable_thing_is_in_that_list()
    {
        // The list above is only exhaustive while it is. Anything a person can
        // set on this screen has to be in it, so a new picker on the window
        // cannot quietly arrive without a case saying it can be put back.
        string[] settable =
        [
            .. typeof(MakeADisc)
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                // A public setter, which is what a window can write to. A
                // private one is the screen telling itself something.
                .Where(each => each.SetMethod?.IsPublic == true && each.GetIndexParameters().Length == 0)
                .Select(each => each.Name)
                .Where(each => !SetByTheWindowRatherThanChosen.Contains(each))
        ];

        List<string> missing = [.. settable.Where(each => !TheThingsThatCanBePutBack.Contains(each))];

        Assert.True(
            missing.Count == 0,
            "These can be set and no case puts them back again: " + string.Join(", ", missing));
    }

    /// <summary>What the window writes to that is not somebody choosing.</summary>
    private static readonly HashSet<string> SetByTheWindowRatherThanChosen =
    [
        // The tree's own selection, which the window writes from the control
        // and nothing binds to.
        nameof(MakeADisc.WhatIsSelected),

        // Which drive, which is a choice but has no "put it back" that leaves
        // the window as it was: choosing none is its own state and the
        // warnings that follow it are different, not absent.
        nameof(MakeADisc.TheDrive),

        // The five options as one record. Nothing binds to it; the window
        // binds to each tick, and each tick is a case above.
        nameof(MakeADisc.Options),
    ];

    /// <summary>Everything the cases above really put back.</summary>
    private static readonly HashSet<string> TheThingsThatCanBePutBack =
    [
        nameof(MakeADisc.Background), nameof(MakeADisc.BackgroundColourText), nameof(MakeADisc.BackgroundAsIs),
        nameof(MakeADisc.DiscIcon),
        nameof(MakeADisc.Cover), nameof(MakeADisc.DiscFace), nameof(MakeADisc.Manual),
        nameof(MakeADisc.ExtrasFolder), nameof(MakeADisc.Music), nameof(MakeADisc.Label),
        nameof(MakeADisc.OutputFolder), nameof(MakeADisc.Medium), nameof(MakeADisc.TheSizeChosen),
        nameof(MakeADisc.CutFiles),
        nameof(MakeADisc.Menu), nameof(MakeADisc.Checksums), nameof(MakeADisc.LegacyFileSystems),
        nameof(MakeADisc.StartHere),
    ];

    /// <summary>The row of the dropdown that stands for this size.</summary>
    private static ASizeToAimAt ASizeFor(Medium medium) => new(medium, medium.Name);

    private static void Choose(MakeADisc screen, string what)
    {
        switch (what)
        {
            case "the menu background": screen.Background = @"C:\art\back.png"; break;
            case "the menu colour": screen.BackgroundColourText = "#1B3A4B"; break;
            case "stretch the background to fit": screen.BackgroundAsIs = true; break;
            case "the disc icon": screen.DiscIcon = @"C:\art\icon.png"; break;
            case "the cover": screen.Cover = @"C:\art\cover.png"; break;
            case "the disc face": screen.DiscFace = @"C:\art\face.png"; break;
            case "the manual": screen.Manual = @"C:\extras\how.txt"; break;
            case "the folder of extras": screen.ExtrasFolder = @"C:\extras"; break;
            case "the tune": screen.Music = @"C:\music\tune.mp3"; break;
            case "the name": screen.Label = "SOMETHING ELSE"; break;
            case "where the discs go": screen.OutputFolder = @"D:\elsewhere"; break;
            case "the size of disc": screen.TheSizeChosen = ASizeFor(Media.Bd25); break;
            case "splitting a file too big for one disc": screen.CutFiles = true; break;
            case "a menu on the disc": screen.Menu = false; break;
            case "a list of what every file should be": screen.Checksums = false; break;
            case "readable by Windows 98": screen.LegacyFileSystems = true; break;
            case "a Start Here file": screen.StartHere = false; break;
            default: throw new ArgumentOutOfRangeException(nameof(what), what, "No such thing to choose.");
        }
    }

    private static void PutItBack(MakeADisc screen, string what)
    {
        switch (what)
        {
            case "the menu background": screen.Background = null; break;
            case "the menu colour": screen.BackgroundColourText = string.Empty; break;
            case "stretch the background to fit": screen.BackgroundAsIs = false; break;
            case "the disc icon": screen.DiscIcon = null; break;
            case "the cover": screen.Cover = null; break;
            case "the disc face": screen.DiscFace = null; break;
            case "the manual": screen.Manual = null; break;
            case "the folder of extras": screen.ExtrasFolder = null; break;
            case "the tune": screen.Music = null; break;
            case "the name": screen.Label = "HOLLOW KNIGHT"; break;
            case "where the discs go": screen.OutputFolder = @"C:\out"; break;
            case "the size of disc": screen.TheSizeChosen = ASizeFor(Media.Dvd5); break;
            case "splitting a file too big for one disc": screen.CutFiles = false; break;
            case "a menu on the disc": screen.Menu = true; break;
            case "a list of what every file should be": screen.Checksums = true; break;
            case "readable by Windows 98": screen.LegacyFileSystems = false; break;
            case "a Start Here file": screen.StartHere = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(what), what, "No such thing to put back.");
        }
    }

    // ------------------------------------------------------- what can be done

    /// <summary>One thing a person can do, and what it is called.</summary>
    private sealed record ADoing(string Named, Action<ADeskWithAScreenOnIt> Do);

    /// <summary>
    /// The sequence this seed produces. The same seed is always the same
    /// sequence, so a failure can be read, replayed and cut down.
    /// </summary>
    private static List<ADoing> Walk(int seed, int howMany)
    {
        Random dice = new(seed);
        List<ADoing> doings = [];

        for (int at = 0; at < howMany; at++)
        {
            doings.Add(Something(dice));
        }

        return doings;
    }

    private static readonly string[] Names =
        ["Hollow Knight", "Gothic", "Baldur's Gate", "A", "Hollow Knight", "Иванко"];

    private static readonly string[] Folders =
        [@"C:\out", @"D:\somewhere else", @"C:\out"];

    private static ADoing Something(Random dice)
    {
        int which = dice.Next(40);
        string name = Names[dice.Next(Names.Length)];
        int bytes = new[] { 1000, 400_000_000, 1_000_000_000 }[dice.Next(3)];
        int row = dice.Next(4);

        return which switch
        {
            0 => new($"add the GOG game {name} at {bytes} bytes",
                desk => desk.Screen.Add(AGogGame(name, bytes))),

            1 => new($"add the folder of files {name} with {row} worth running",
                desk => desk.Screen.Add(AFolderOfFiles(name, row))),

            2 or 21 => new($"add the GOG game {name} with two add-ons",
                desk => desk.Screen.Add(AGogGame(name, bytes), [AnAddOn(1), AnAddOn(2)])),

            3 => new($"pick row {row}", desk => desk.PickTheRow(row)),

            4 => new($"tick row {row}", desk => desk.TickTheRow(row, true)),

            5 => new($"untick row {row}", desk => desk.TickTheRow(row, false)),

            6 => new("take off whatever Remove would take",
                desk => desk.Screen.TakeOff(desk.Screen.WhatIsSelected)),

            // Its own doing rather than something picking a row and hoping.
            // Rows are mostly files, so a walk that only picks rows takes a
            // whole game off perhaps once in a hundred steps, and taking one
            // off the MIDDLE with add-ons after it, which is the case that
            // renumbers them, practically never. Stryker found that: the line
            // that shuffles every add-on down survived a thousand tests.
            22 or 23 => new($"take game {row} off the list", desk => desk.TakeAGameOff(row)),

            7 => new($"type the name {name}", desk => desk.Screen.Label = name),

            8 => new("clear the name", desk => desk.Screen.Label = string.Empty),

            9 => new($"choose {Media.All[dice.Next(Media.All.Count)].Name}",
                Choosing(Media.All[dice.Next(Media.All.Count)])),

            10 => new("turn splitting on or off",
                desk => desk.Screen.CutFiles = !desk.Screen.CutFiles),

            11 => new("turn the menu on or off",
                desk => desk.Screen.Menu = !desk.Screen.Menu),

            12 => new("turn the checksum list on or off",
                desk => desk.Screen.Checksums = !desk.Screen.Checksums),

            13 => new("turn Windows 98 on or off",
                desk => desk.Screen.LegacyFileSystems = !desk.Screen.LegacyFileSystems),

            14 => new($"say the discs go in {Folders[dice.Next(Folders.Length)]}",
                Putting(Folders[dice.Next(Folders.Length)])),

            15 => new("say the discs go nowhere",
                desk => desk.Screen.OutputFolder = string.Empty),

            16 => new("choose a background, or clear it",
                desk => desk.Screen.Background = desk.Screen.Background is null ? @"C:\art\back.png" : null),

            17 => new("choose a manual, or clear it",
                desk => desk.Screen.Manual = desk.Screen.Manual is null ? @"C:\extras\how.txt" : null),

            18 => new("choose a tune, or clear it",
                desk => desk.Screen.Music = desk.Screen.Music is null ? @"C:\music\tune.mp3" : null),

            19 => new("choose a folder of extras, or clear it",
                desk => desk.Screen.ExtrasFolder = desk.Screen.ExtrasFolder is null ? @"C:\extras" : null),

            20 => new("put a loose file on the disc",
                desk => desk.Screen.AlsoOnTheDisc([@"C:\loose\a note.txt"])),

            // The four buttons the walk could not press, and the reason it
            // could not was the machine under it rather than the walk itself.
            // Every one of them decides something and has to say so.
            25 => new("make the discs", desk => desk.Screen.MakeIt().GetAwaiter().GetResult()),

            26 => new("burn whatever there is to burn", desk => desk.BurnOne()),

            27 => new("burn the whole set", desk => desk.BurnTheSet()),

            28 => new("erase a disc", desk => desk.EraseOne()),

            29 => new($"open a project with {(row % 3) + 1} games in it", desk => desk.OpenAProject(row)),

            // Five more doors into the window. The last five added here killed
            // sixteen faults between them without a test being written about
            // any of them, because every one of these decides something and
            // has to say so.
            30 => new("clear out everything turned off",
                desk => desk.Screen.TakeOffWhatIsLeftOff()),

            31 => new("look at the menu it would make", desk => desk.PreviewIt()),

            32 => new("ask whether there is a newer version",
                desk => desk.Screen.CheckForANewVersion().GetAwaiter().GetResult()),

            33 => new("look at the drives again",
                desk => desk.Screen.LookAtTheDrives().GetAwaiter().GetResult()),

            34 => new($"pick drive {row}, or none", desk => desk.PickTheDrive(row)),

            // The same folder added again, which used to put the same game on
            // twice. It is the one door that puts rows back without adding a
            // game, so what it changes has to be announced like everything
            // else.
            35 => new($"add the folder of game {row} again", desk => desk.AddTheSameFolderAgain(row)),

            // The way out of a size refusal, which the screen carries out
            // itself: aiming at the larger disc it named.
            36 => new("use the larger disc the refusal names", desk => desk.Screen.UseTheLargerDisc()),

            // Saving the recipe without building. It decides whether it may,
            // writes through the builder, and has a sentence to say, so the
            // walk holds it to announcing all of that like everything else.
            37 => new("save it for later", desk => desk.Screen.SaveItForLater()),

            // A project that named more than came back, discwright#98. The
            // games that did not come back cannot be rows, so the line about
            // them is their only trace, and it has to be announced and taken
            // down like everything else.
            38 => new("open a project that is short two things",
                desk => desk.OpenAProjectShortOfSomething()),

            // Renaming a game in place, the duplicate name notice's
            // companion. It changes the one thing every other answer reads,
            // a game's name, so everything that follows from it has to be
            // announced like everything else.
            39 => new($"rename game {row} in place", desk => desk.RenameOne(row)),

            _ => new("start a new disc", desk => desk.Screen.StartAgain()),
        };
    }

    private static Action<ADeskWithAScreenOnIt> Choosing(Medium medium) =>
        desk => desk.Screen.Medium = medium;

    private static Action<ADeskWithAScreenOnIt> Putting(string folder) =>
        desk => desk.Screen.OutputFolder = folder;

    // ------------------------------------------------------------ the walking

    /// <summary>
    /// Runs the sequence and returns the first thing that was wrong, or
    /// nothing when all of it held.
    /// </summary>
    private static int _announcementsChecked;

    private static int _sumsChecked;

    private static string? WhatWentWrong(IReadOnlyList<ADoing> doings)
    {
        ADeskWithAScreenOnIt desk = new();

        for (int at = 0; at < doings.Count; at++)
        {
            Dictionary<string, string> before = Everything(desk.Screen);

            desk.Told.Clear();

            try
            {
                doings[at].Do(desk);
            }
            catch (Exception wrong)
            {
                return $"Step {at + 1}, {doings[at].Named}, threw {wrong.GetType().Name}: {wrong.Message}";
            }

            Dictionary<string, string> after = Everything(desk.Screen);

            if (WhatMovedWithoutSayingSo(before, after, desk.Told) is string quiet)
            {
                return $"Step {at + 1}, {doings[at].Named}.{Environment.NewLine}{quiet}";
            }

            if (WhatDoesNotAddUp(desk.Screen) is string wrongNow)
            {
                return $"Step {at + 1}, {doings[at].Named}.{Environment.NewLine}  {wrongNow}";
            }
        }

        return null;
    }

    /// <summary>
    /// The shortest head-and-tail of this sequence that still fails, found by
    /// taking steps out one at a time and keeping every cut that still breaks.
    /// </summary>
    private static List<ADoing> TheShortestThatStillFails(IReadOnlyList<ADoing> doings)
    {
        List<ADoing> least = [.. doings];

        for (int at = least.Count - 1; at >= 0; at--)
        {
            List<ADoing> without = [.. least];
            without.RemoveAt(at);

            if (WhatWentWrong(without) is not null)
            {
                least = without;
            }
        }

        return least;
    }

    // ------------------------------------- did it say what changed about it

    /// <summary>
    /// Every public property the window could bind to, as text that can be
    /// compared.
    /// </summary>
    private static Dictionary<string, string> Everything(MakeADisc screen)
    {
        Dictionary<string, string> said = [];

        foreach (PropertyInfo property in Readable())
        {
            said[property.Name] = AsText(Value(property, screen));
        }

        return said;
    }

    private static object? Value(PropertyInfo property, MakeADisc screen)
    {
        try
        {
            return property.GetValue(screen);
        }
        catch (Exception wrong)
        {
            return "«threw " + (wrong.InnerException ?? wrong).GetType().Name + "»";
        }
    }

    private static PropertyInfo[]? _readable;

    private static PropertyInfo[] Readable() =>
        _readable ??=
        [
            .. typeof(MakeADisc)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(each => each.CanRead && each.GetIndexParameters().Length == 0)
                .Where(each => !NotBoundToAnything.Contains(each.Name))
                .OrderBy(each => each.Name, StringComparer.Ordinal)
        ];

    /// <summary>
    /// Properties no window reads, so nothing has to be told when they move.
    /// </summary>
    /// <remarks>
    /// Named one at a time and with a reason each, because the point of this
    /// test is that the list of things that must be announced is everything
    /// else. A property added here without a reason is this test being turned
    /// off a little at a time.
    /// </remarks>
    private static readonly HashSet<string> NotBoundToAnything =
    [
        // The lists themselves never change identity: the window binds to the
        // collection once and the collection raises its own changes.
        nameof(MakeADisc.Games),
        nameof(MakeADisc.WhatGoesOnIt),

        // Read by the window in a handler rather than bound, when somebody
        // presses the button that is about to use it.
        nameof(MakeADisc.WhereAPreviewWouldGo),

        // Written by the window from the tree's own selection and read back
        // in one handler. Nothing binds to it, so nothing has to hear it.
        // Found by this test on its first run, which is the right way round:
        // the list of what need not be announced is argued for one at a time
        // rather than assumed.
        nameof(MakeADisc.WhatIsSelected),
    ];

    /// <summary>
    /// What is different between two snapshots, in words, or nothing when
    /// they are the same.
    /// </summary>
    /// <remarks>
    /// Named rather than compared with Assert.Equal, which prints two
    /// dictionaries of sixty three entries and an ellipsis where the
    /// difference is.
    /// </remarks>
    private static string? WhatMoved(
        Dictionary<string, string> before,
        Dictionary<string, string> after,
        string saying)
    {
        List<string> moved =
        [
            .. before.Keys
                .Where(name => !string.Equals(before[name], after[name], StringComparison.Ordinal))
                .Select(name => $"    {name}: {Short(before[name])}  ->  {Short(after[name])}")
        ];

        return moved.Count == 0
            ? null
            : saying + " changed:" + Environment.NewLine + string.Join(Environment.NewLine, moved);
    }

    private static string? WhatMovedWithoutSayingSo(
        Dictionary<string, string> before,
        Dictionary<string, string> after,
        IReadOnlyCollection<string> told)
    {
        // Counted here rather than where it is called, because a fault that
        // stops calling it leaves a count taken outside looking exactly as it
        // did. Two mutants walked through that on the way to this line.
        _announcementsChecked++;

        List<string> quiet =
        [
            .. before.Keys
                .Where(name => !string.Equals(before[name], after[name], StringComparison.Ordinal))
                .Where(name => !told.Contains(name))
        ];

        if (quiet.Count == 0)
        {
            return null;
        }

        List<string> lines =
        [
            "  These moved and nothing said so, so the window would still be showing the old one:",
            .. quiet.Select(name => $"    {name}: {Short(before[name])}  ->  {Short(after[name])}"),
        ];

        return string.Join(Environment.NewLine, lines);
    }

    // -------------------------------------------------- does it still add up

    /// <summary>
    /// What has to be true of the screen whatever has been done to it.
    /// </summary>
    /// <remarks>
    /// Worked out here rather than copied from the code they are about, as far
    /// as that is possible: a rule restated is a rule that cannot fail.
    /// </remarks>
    private static string? WhatDoesNotAddUp(MakeADisc screen)
    {
        _sumsChecked++;

        // What the list says it comes to, against adding it up by hand.
        long bytes = 0;
        int things = 0;
        int off = 0;

        foreach (AThingOnTheDisc game in screen.WhatGoesOnIt)
        {
            foreach (AThingOnTheDisc thing in game.EverythingOn())
            {
                things++;
                bytes += thing.Bytes;
            }

            foreach (AThingOnTheDisc thing in game.EveryThing())
            {
                if (thing.IsOn == false)
                {
                    off++;
                }
            }
        }

        string total = things == 0
            ? string.Empty
            : things == 1
                ? $"{ByteSize.Describe(bytes)} in one file"
                : $"{ByteSize.Describe(bytes)} in {things} files";

        if (!string.Equals(screen.WhatIsOnIt, total, StringComparison.Ordinal))
        {
            return $"The list says it comes to \"{screen.WhatIsOnIt}\" and adding it up gives \"{total}\".";
        }

        if ((off > 0) != (screen.WhatIsLeftOff is not null))
        {
            return off > 0
                ? $"{off} things are unticked and nothing says anything is being left off."
                : $"Nothing is unticked and it says \"{screen.WhatIsLeftOff}\".";
        }

        // And it counts them the way English does.
        if (off > 0 && screen.WhatIsLeftOff is string leftOff)
        {
            string many = off == 1 ? "1 thing" : $"{off} things";

            if (!leftOff.StartsWith($"Leaving off {many},", StringComparison.Ordinal))
            {
                return $"{off} things are off and it says \"{leftOff}\".";
            }
        }

        // Every add-on belongs to the game it came out of, which is the game
        // whose folder it sits in. Taking a game off the middle of the list
        // shuffles the rest down, and an add-on left pointing at the old
        // number is an update attached to somebody else's game.
        DiscProject project = screen.AsAProject();

        foreach (AddOn addOn in project.AddOnsOrNone)
        {
            if (addOn.BelongsTo < 0 || addOn.BelongsTo >= project.Games.Count)
            {
                return $"The add-on {addOn.Name} belongs to game {addOn.BelongsTo} "
                    + $"and there are {project.Games.Count} games.";
            }

            if (!addOn.Path.StartsWith(project.Games[addOn.BelongsTo].Path, StringComparison.Ordinal))
            {
                return $"The add-on at {addOn.Path} is attached to "
                    + $"{project.Games[addOn.BelongsTo].Name} at {project.Games[addOn.BelongsTo].Path}.";
            }
        }

        // Nothing to build means nothing to build, whatever else is set.
        if (screen.CanMakeIt && screen.Games.Count == 0)
        {
            return "Build it is live over a window with no games on it.";
        }

        if (screen.CanMakeIt && screen.OutputFolder.Length == 0)
        {
            return "Build it is live with nowhere to put what it makes.";
        }

        // A refusal standing means it cannot be built, whatever else is true.
        if (screen.WhyNot is not null && screen.CanMakeIt)
        {
            return $"Build it is live over the refusal \"{screen.WhyNot.Headline}\".";
        }

        // Nothing on it is nothing to complain about.
        if (screen.Games.Count == 0 && screen.WhyNot is not null)
        {
            return $"An empty window is refusing: \"{screen.WhyNot.Headline}\".";
        }

        // A way out belongs to a refusal.
        if (screen.TheWayOut is not null && screen.WhyNot is null)
        {
            return "There is a way out of a refusal that is not there.";
        }

        if ((screen.TheWayOut is not null) != (screen.WhatTheWayOutIsCalled.Length > 0))
        {
            return "The way out and the words on it disagree about whether there is one.";
        }

        // A larger disc is offered only when there is one, and it is larger.
        if (screen.TheWayOut is RemedyKind.ChangeMedium
            && (screen.TheLargerDiscThatWouldDo is not Medium larger || larger.Capacity <= screen.Medium.Capacity))
        {
            return "The way out names a larger disc that is not there or not larger.";
        }

        if (screen.TheLargerDiscThatWouldDo is not null && screen.WhyNot is null)
        {
            return "There is a larger disc on offer with nothing refused.";
        }

        // A worry's way out belongs to a worry.
        if ((screen.WhatTheWorrysWayOutIsCalled.Length > 0) != (screen.Worry?.Remedy is { Kind: RemedyKind.FocusField }))
        {
            return "The worry's way out and the worry disagree about whether there is one.";
        }

        // A preview is a menu for a set, so it needs a set.
        if (screen.CanPreview && (screen.Games.Count == 0 || screen.Label.Length == 0))
        {
            return "The preview is live with no game or no name.";
        }

        // The line under the split box belongs to the split box.
        if (screen.WhatSplittingWouldDo.Length > 0 && !screen.CutFiles)
        {
            return $"The splitting line is up with the box unticked: \"{screen.WhatSplittingWouldDo}\".";
        }

        // The amber line and the ink it is drawn in have to agree.
        if (screen.TheDiscIsADifferentSize != (screen.AboutTheDiscInTheDrive.Length > 0))
        {
            return "The line about the disc in the drive and the colour it is drawn in disagree.";
        }

        // Starting again is only worth offering when it would take something.
        if (screen.CanStartAgain != (screen.HasAnythingOnIt && !screen.IsWorking))
        {
            return $"Start a new disc is {(screen.CanStartAgain ? "live" : "dead")} with "
                + $"{(screen.HasAnythingOnIt ? "something" : "nothing")} on the window.";
        }

        // The size is always one of the six, however it got there.
        if (!Media.All.Contains(screen.Medium))
        {
            return $"The size is {screen.Medium.Name}, which is not one of the six.";
        }

        // What will only be copied is about folders of files with no one
        // thing to run, and nothing else.
        if (screen.WhatWillNotStartItself is not null
            && !screen.Games.Any(game =>
                game.Kind != GameKind.GogInstaller && game.Installer is null && game.LauncherCandidates.Count != 1))
        {
            return "It says a game will only be copied and no game on it would be.";
        }

        return null;
    }

    [Fact]
    public void Will_not_do_anything_when_the_button_that_would_is_dead()
    {
        // The other half of a dead button: it is not enough that it greys, the
        // thing behind it has to refuse as well, because a greyed button in a
        // window is still a method anybody can call.
        for (int seed = 1; seed <= 60; seed++)
        {
            List<ADoing> doings = Walk(seed, 12);
            ADeskWithAScreenOnIt desk = new();

            foreach (ADoing doing in doings)
            {
                doing.Do(desk);
            }

            MakeADisc screen = desk.Screen;

            string after = Environment.NewLine + "After:" + Environment.NewLine
                + string.Join(Environment.NewLine, doings.Select((each, at) => $"  {at + 1}. {each.Named}"));

            if (!screen.CanStartAgain)
            {
                Dictionary<string, string> was = Everything(screen);
                screen.StartAgain();

                string? moved = WhatMoved(was, Everything(screen), "Start a new disc is dead and it still");

                Assert.True(moved is null, moved + after);
            }

            if (!screen.CanTakeSomethingOff)
            {
                Dictionary<string, string> was = Everything(screen);
                screen.TakeOff(screen.WhatIsSelected);

                string? moved = WhatMoved(was, Everything(screen), "Remove is dead and it still");

                Assert.True(moved is null, moved + after);
            }
        }
    }

    // ------------------------------------------------------------ the fixtures

    private static int _folders;

    /// <summary>
    /// A GOG download in a folder of its own, even when two of them are
    /// called the same thing, so an add-on can be traced back to the game it
    /// came from by where it sits.
    /// </summary>
    private static GameFolder AGogGame(string name, int bytes)
    {
        ListedFile installer = new($"setup_{name}.exe", bytes);
        string path = @"C:\games\" + name + "-" + Interlocked.Increment(ref _folders);

        return new GameFolder(path, GameKind.GogInstaller, name, name, installer, [installer], [], null);
    }

    private static GameFolder AFolderOfFiles(string name, int candidates)
    {
        string path = @"C:\games\" + name + "-" + Interlocked.Increment(ref _folders);
        List<ListedFile> files = [new(@"data\things.pak", 1000)];
        List<ListedFile> could = [];

        for (int at = 1; at <= candidates; at++)
        {
            ListedFile exe = new($"{name}{at}.exe", 1000);
            files.Add(exe);
            could.Add(exe);
        }

        return new GameFolder(
            path,
            could.Count > 0 ? GameKind.FilesWithLauncher : GameKind.FilesOnly,
            name, name, null, files, could, null);
    }

    private static LeftBehind AnAddOn(int which) =>
        new(new ListedFile($"patch_1.0_to_1.{which}.exe", 50_000), LeftBehindKind.LooksLikeAnAddOn);

    // --------------------------------------------------------------- the desk

    /// <summary>A screen, and everything it has said since the last step.</summary>
    private sealed class ADeskWithAScreenOnIt
    {
        internal ADeskWithAScreenOnIt()
        {
            Screen = TheScreen();
            Screen.PropertyChanged += (_, what) => Told.Add(what.PropertyName ?? string.Empty);
        }

        internal MakeADisc Screen { get; }

        internal HashSet<string> Told { get; } = [];

        internal void PickTheRow(int which)
        {
            AThingOnTheDisc[] rows = [.. Screen.WhatGoesOnIt.SelectMany(game => game.EveryThing())];

            Screen.WhatIsSelected = rows.Length == 0 ? null : rows[which % rows.Length];
        }

        internal void TakeAGameOff(int which)
        {
            if (Screen.WhatGoesOnIt.Count > 0)
            {
                Screen.TakeOff(Screen.WhatGoesOnIt[which % Screen.WhatGoesOnIt.Count]);
            }
        }

        /// <summary>
        /// Adds a folder that is on the list already, reading one more file
        /// in it and a patch beside it, the way somebody adds a folder again
        /// to get back what they took off. The second time round nothing is
        /// missing, so both answers get walked.
        /// </summary>
        internal void AddTheSameFolderAgain(int which)
        {
            if (Screen.Games.Count == 0)
            {
                return;
            }

            GameFolder known = Screen.Games[which % Screen.Games.Count];
            GameFolder fresh = known with { Files = [.. known.Files, new ListedFile("one more.txt", 10)] };

            if (Screen.WhatIsMissingFrom(fresh, [AnAddOn(9)]) is TheSameFolderAgain missing
                && !missing.NothingIsMissing)
            {
                Screen.PutBack(missing);
            }
        }

        internal void BurnOne()
        {
            if (Screen.TheImages.Count == 0)
            {
                return;
            }

            Screen.BurnIt(Screen.TheImages[0], TheFirstDrive, WhicheverCouldBeErased, TheTrayShuts)
                .GetAwaiter().GetResult();
        }

        internal void BurnTheSet()
        {
            if (Screen.TheImages.Count == 0)
            {
                return;
            }

            Screen.BurnTheSet(
                Screen.TheImages,
                TheFirstDrive,
                WhicheverCouldBeErased,
                TheTrayShuts,
                AnotherBlankIsIn,
                CarryOnFromThere).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Looks at the menu the disc would get, into a folder of the kind the
        /// window would really use.
        /// </summary>
        internal void PreviewIt() => Screen.PreviewTheMenu(
            System.IO.Path.Combine(@"C:\Temp", Screen.WhereAPreviewWouldGo));

        /// <summary>
        /// Picks one of the drives, or none, which is its own state rather
        /// than the absence of one: the warnings that follow are different.
        /// </summary>
        internal void PickTheDrive(int which) =>
            Screen.TheDrive = Screen.Drives.Count == 0 || which % (Screen.Drives.Count + 1) == 0
                ? null
                : Screen.Drives[(which - 1) % Screen.Drives.Count];

        internal void EraseOne() =>
            Screen.EraseADisc(WhicheverCouldBeErased).GetAwaiter().GetResult();

        /// <summary>
        /// Opens a project file, with everything a project file can carry on
        /// it: artwork, a manual, extras, a tune, a loose file and a size.
        /// </summary>
        /// <remarks>
        /// Everything at once on purpose. What this step is for is the window
        /// taking on a state it did not build up itself one picker at a time,
        /// which is where a thing set without being announced hides.
        /// </remarks>
        internal void OpenAProject(int games)
        {
            DiscProject project = new(
                "OPENED",
                [.. Enumerable.Range(1, (games % 3) + 1).Select(at => AGogGame($"Opened {at}", 1000))],
                new DiscOptions(Menu: false, Checksums: false, LegacyFileSystems: true, CutFiles: true, StartHere: false),
                Artwork: new DiscArtwork(
                    @"C:\art\icon.png",
                    @"C:\art\back.png",
                    true,
                    @"C:\art\cover.png",
                    @"C:\art\face.png"),
                Extras: new DiscExtras(@"C:\extras\how.txt", @"C:\extras", [@"C:\loose\a note.txt"], @"C:\music\tune.mp3"),
                AddOns: []);

            Screen.OpenThis(new Reopened(project, Media.Bd25, @"C:\out", [], []));
        }

        /// <summary>
        /// Renames whichever game the dice landed on, through the same two
        /// steps the window takes.
        /// </summary>
        internal void RenameOne(int which)
        {
            if (Screen.WhatGoesOnIt.Count == 0)
            {
                return;
            }

            AThingOnTheDisc row = Screen.WhatGoesOnIt[which % Screen.WhatGoesOnIt.Count];

            Screen.BeginRenaming(row);
            Screen.FinishRenaming(row, $"Renamed {which}");
        }

        /// <summary>
        /// Opens a project whose file named more than came back, so the line
        /// about what is missing has something to say.
        /// </summary>
        internal void OpenAProjectShortOfSomething()
        {
            DiscProject project = new(
                "SHORT",
                [AGogGame("Still Here", 1000)],
                new DiscOptions(Menu: true, Checksums: false, LegacyFileSystems: false, CutFiles: false),
                Artwork: null,
                Extras: null,
                AddOns: []);

            Screen.OpenThis(new Reopened(
                project, null, null, [@"C:\gone\one", @"C:\gone\two"], []));
        }

        private static Task<Recorder?> TheFirstDrive(IReadOnlyList<Recorder> ready) =>
            Task.FromResult(ready.Count == 0 ? null : ready[0]);

        private static Task<Recorder?> WhicheverCouldBeErased(IReadOnlyList<Recorder> could) =>
            Task.FromResult(could.Count == 0 ? null : could[0]);

        private static Task<bool> TheTrayShuts(Recorder drive) => Task.FromResult(true);

        private static Task<bool> AnotherBlankIsIn(int number, int of, Recorder drive) => Task.FromResult(true);

        private static Task<bool> CarryOnFromThere(int from, int of) => Task.FromResult(true);

        internal void TickTheRow(int which, bool on)
        {
            AThingOnTheDisc[] rows = [.. Screen.WhatGoesOnIt.SelectMany(game => game.EveryThing())];

            if (rows.Length > 0)
            {
                rows[which % rows.Length].IsOn = on;
            }
        }
    }

    /// <summary>
    /// The machine the walk runs on, where every button really does something.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>It used to refuse everything.</strong> No drives, a disk with
    /// nothing on it, a builder that made no discs, so the walk could press
    /// nothing but the pickers and the ticks. Half the window was out of its
    /// reach, and a mutation pass said what that cost: forty six faults, every
    /// one of them the window deciding something and telling nobody, in Make,
    /// Burn, Erase and opening a project.
    /// </para>
    /// <para>
    /// So the fakes answer the way a working machine answers: one blank disc
    /// in one drive, a builder that makes a disc, images that are made, a disk
    /// whose folders match what was built, a tray that shuts itself. What the
    /// walk is checking is not that burning works, which has its own tests,
    /// but that the window says what moved however it was driven.
    /// </para>
    /// <para>
    /// The clock moves itself, so a drive is never really waited for.
    /// </para>
    /// </remarks>
    private static MakeADisc TheScreen() =>
        new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatMakesADisc(),
            new ImagesThatDoNothing(),
            new OneBlankDisc(),
            new ABurnerThatDoesNothing(),
            new AnEraserThatDoesNothing(),
            new ADiskThatMatchesWhatWasBuilt(),
            new NoHashing(),
            new NothingIsRemembered(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            new APreviewThatWorks(),
            new PicturesThatRead(),
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience));

    // --------------------------------------------------------------- the words

    private static string Short(string what) =>
        what.Length <= 200 ? what : what[..199] + "\u2026";

    private static string AsText(object? what) =>
        what switch
        {
            null => "«nothing»",
            string words => words,
            IEnumerable many => "[" + string.Join(", ", many.Cast<object?>().Select(AsText)) + "]",
            _ => what.ToString() ?? "«nothing»",
        };

    // ------------------------------------------------- the machine it runs on

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
    }

    private sealed class ABuilderThatMakesADisc : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs(
                project.Games.Count == 0
                    ? []
                    : [new BuiltDisc(project.Label, 1, 1, System.IO.Path.Combine(outputFolder, "disc 1"), 1000)]));

        // The walk presses save and only cares what the screen says about
        // it; what lands on a disk is SavingItForLater's business.
        public void SaveTheRecipe(DiscProject project, Medium medium, string outputFolder)
        {
        }
    }

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
    }

    /// <summary>One drive with a blank in it, which is the ordinary machine.</summary>
    private sealed class OneBlankDisc : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() =>
            [new("id-E", "E:", "A Drive", 2, true, 360_000, [750])];
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(new BurnedDisc(onto.Drive, 0, 0, true));
    }

    private sealed class AnEraserThatDoesNothing : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>("blank");
    }

    /// <summary>
    /// A disk where what was built is where it was built, and a disc read back
    /// out of the drive matches it.
    /// </summary>
    /// <remarks>
    /// The image beside them is what the window finds when it looks for
    /// something to burn, so Burn and the set walk are reachable at all.
    /// </remarks>
    private sealed class ADiskThatMatchesWhatWasBuilt : IFileSystem
    {
        public bool FolderExists(string path) => true;

        public FolderListing List(string path) =>
            new(path,
            [
                new ListedFile("setup_a_game.exe", 1000),
                new ListedFile("A DISC.iso", 1000)
            ]);
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

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
    }

    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "nobody asked"));
    }

    /// <summary>
    /// A preview that says where it put the menus, without writing any.
    /// </summary>
    /// <remarks>
    /// It used to refuse everything, which meant pressing the button only ever
    /// moved the refusal. The window has to say what it wrote as well.
    /// </remarks>
    private sealed class APreviewThatWorks : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            project.Games.Count == 0
                ? Refusals.ThereIsNothingToPutOnADisc()
                : (string[])[System.IO.Path.Combine(into, "AUTORUN", "menu.hta")];
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
