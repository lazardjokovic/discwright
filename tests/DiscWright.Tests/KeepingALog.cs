using DiscWright.Core;
using DiscWright.Platform;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The log the app keeps: what goes in it, and how little room it takes.
/// </summary>
/// <remarks>
/// Asked for by the owner on 2026-11-10, after a night of rebuilding a failed
/// set from screenshots and the Windows System log, with the question of
/// whether it would take much storage. These hold the answer to that.
/// </remarks>
public sealed class KeepingALog : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dw-log-" + Guid.NewGuid().ToString("N"));
    private DateTime _now = new(2026, 11, 10, 1, 11, 2, 345);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // --------------------------------------------------------- on the disk

    [Fact]
    public void Writes_one_file_a_day_with_the_time_in_front_of_every_line()
    {
        TheLogOnDisk log = new(_folder, () => _now);

        log.Write("Drives: Windows listed none.");

        string[] lines = File.ReadAllLines(Path.Combine(_folder, "2026-11-10.txt"));
        Assert.Equal("01:11:02.345  Drives: Windows listed none.", Assert.Single(lines));
    }

    [Fact]
    public void A_new_day_starts_a_new_file()
    {
        TheLogOnDisk log = new(_folder, () => _now);

        log.Write("before midnight");
        _now = _now.AddDays(1);
        log.Write("after midnight");

        Assert.True(File.Exists(Path.Combine(_folder, "2026-11-10.txt")));
        Assert.Contains("after midnight", File.ReadAllText(Path.Combine(_folder, "2026-11-11.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public void A_sentence_with_a_newline_stays_one_line()
    {
        TheLogOnDisk log = new(_folder, () => _now);

        log.Write("first\r\nsecond\nthird");

        // Every entry starts with a time, or the log cannot be read by the minute.
        Assert.Single(File.ReadAllLines(log.Today));
    }

    [Fact]
    public void Keeps_a_fortnight_and_drops_what_is_older()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "2026-10-27.txt"), "fifteen days ago");
        File.WriteAllText(Path.Combine(_folder, "2026-10-28.txt"), "fourteen days ago, today included");
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "somebody else's file");

        new TheLogOnDisk(_folder, () => _now).Write("today");

        Assert.False(File.Exists(Path.Combine(_folder, "2026-10-27.txt")));
        Assert.True(File.Exists(Path.Combine(_folder, "2026-10-28.txt")));

        // Only files it named itself are its to delete.
        Assert.True(File.Exists(Path.Combine(_folder, "notes.txt")));
    }

    [Fact]
    public void The_days_before_today_never_take_more_than_their_half()
    {
        Directory.CreateDirectory(_folder);
        string big = new('x', 1024 * 1024);

        for (int day = 1; day <= 4; day++)
        {
            File.WriteAllText(Path.Combine(_folder, $"2026-11-0{day + 4}.txt"), big);
        }

        new TheLogOnDisk(_folder, () => _now).Write("today");

        long before = new DirectoryInfo(_folder).GetFiles("2026-11-0?.txt").Sum(each => each.Length);

        Assert.True(before <= TheLogOnDisk.ADayAtMost, $"{before} bytes of older days kept");

        // The oldest go first.
        Assert.False(File.Exists(Path.Combine(_folder, "2026-11-05.txt")));
        Assert.True(File.Exists(Path.Combine(_folder, "2026-11-08.txt")));
    }

    [Fact]
    public void A_day_that_fills_its_share_says_so_once_and_stops()
    {
        TheLogOnDisk log = new(_folder, () => _now);
        string line = new('x', 1000);

        for (int written = 0; written < 3000; written++)
        {
            log.Write(line);
        }

        string today = File.ReadAllText(log.Today);

        Assert.True(new FileInfo(log.Today).Length <= TheLogOnDisk.ADayAtMost);
        Assert.EndsWith(TheLogOnDisk.Full + Environment.NewLine, today, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(today, TheLogOnDisk.Full));
    }

    [Fact]
    public void The_whole_log_can_never_pass_five_megabytes()
    {
        Assert.Equal(5L * 1024 * 1024, TheLogOnDisk.AtMostBytes);
        Assert.Equal(TheLogOnDisk.AtMostBytes, TheLogOnDisk.ADayAtMost * 2);
    }

    [Fact]
    public void A_line_that_cannot_be_written_takes_nothing_down()
    {
        // A file where the folder should be: every write fails.
        File.WriteAllText(_folder, "in the way");

        try
        {
            new TheLogOnDisk(_folder, () => _now).Write("lost, and nothing more");
        }
        finally
        {
            File.Delete(_folder);
        }
    }

    // ----------------------------------------------------------- the drives

    [Fact]
    public void Writes_down_what_every_drive_answered()
    {
        List<string> log = [];

        new TheDrivesWrittenDown(new Answering(
            [
                new("id-D", "D:", "ASUS DRW-24D5MT", 2, true, 359_846, [24]),
                new("id-E", "E:", "HL-DT-ST BD-RE BU40N", 0, false, 0, [], "There is no media in the device.",
                    NothingInIt: true)
            ]), new Into(log)).All();

        Assert.Equal(
            "Drives: D: ASUS DRW-24D5MT, blank CD-R, 359,846 sectors free; E: HL-DT-ST BD-RE BU40N, nothing in it.",
            Assert.Single(log));
    }

    [Fact]
    public void Writes_down_that_windows_listed_none()
    {
        // The line that would have answered the night's first question.
        List<string> log = [];

        new TheDrivesWrittenDown(new Answering([]), new Into(log)).All();

        Assert.Equal("Drives: Windows listed none.", Assert.Single(log));
    }

    [Fact]
    public void Writes_down_a_drive_that_did_not_answer_and_why()
    {
        List<string> log = [];

        new TheDrivesWrittenDown(new Answering(
            [new("id-D", "D:", "A Drive", 0, false, 0, [], "the device is not ready")]), new Into(log)).All();

        Assert.Contains("D: A Drive, did not answer (the device is not ready)", Assert.Single(log), StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_answer_twice_is_written_once()
    {
        // The settling asks every second while it waits. Fifteen identical
        // lines say less than one.
        List<string> log = [];
        TheDrivesWrittenDown drives = new(new Answering([]), new Into(log));

        drives.All();
        drives.All();
        drives.All();

        Assert.Single(log);
    }

    [Fact]
    public void A_slow_answer_is_written_every_time_with_how_long_it_took()
    {
        List<string> log = [];
        DateTime clock = DateTime.UnixEpoch;
        Answering slow = new([], () => clock = clock.AddSeconds(2.5));
        TheDrivesWrittenDown drives = new(slow, new Into(log), () => clock);

        drives.All();
        drives.All();

        Assert.Equal(2, log.Count);
        Assert.All(log, each => Assert.EndsWith("(took 2.5 s)", each, StringComparison.Ordinal));
    }

    [Fact]
    public void A_changed_answer_is_written_again()
    {
        List<string> log = [];
        Answering drives = new([]);
        TheDrivesWrittenDown written = new(drives, new Into(log));

        written.All();
        drives.Now = [new("id-D", "D:", "A Drive", 2, true, 1000, [24])];
        written.All();

        Assert.Equal(2, log.Count);
    }

    // ----------------------------------------------------------- the window

    [Fact]
    public async Task A_refusal_is_written_with_its_reason()
    {
        List<string> log = [];
        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        WhatTheLogHears.Listen(screen, new Into(log));

        await screen.EraseADisc(could => Task.FromResult<Recorder?>(null));

        // No drives, so nothing to erase: the sentence the window showed,
        // and why.
        Assert.Contains(log, each => each.StartsWith("Refused: There is no disc here that erasing would help.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_the_window_is_doing_is_written_and_not_once_per_percent()
    {
        List<string> log = [];
        MakeADisc screen = TheMainWindowWithoutAWindow.New();
        WhatTheLogHears.Listen(screen, new Into(log));

        await screen.EraseADisc(could => Task.FromResult<Recorder?>(null));

        // The settling says it is waiting, once, however many times it asks.
        Assert.Equal(1, log.Count(each => each == "Doing: waiting for the drive"));
    }

    [Theory]
    [InlineData("writing disc 2 of 2, 45%", "writing disc 2 of 2, 46%", true)]
    [InlineData("checking file 38 of 39", "checking file 39 of 39", true)]
    [InlineData("writing disc 2 of 2, 99%", "checking disc 2 of 2", false)]
    public void Progress_that_differs_only_in_its_numbers_is_one_stage(string before, string after, bool same)
    {
        // A burn reports its percentage hundreds of times. The log wants when
        // each stage began, not every step of it.
        Assert.Equal(same, WhatTheLogHears.ShapeOf(before) == WhatTheLogHears.ShapeOf(after));
    }

    [Fact]
    public void A_climb_inside_one_stage_is_written_at_every_tenth()
    {
        // The check of the owner's set on 2026-11-11 spent two and a half
        // minutes on one 600 MB file, 0% to 87%, and the log held one line for
        // all of it, so it could not say whether the number had moved. Every
        // percent of that climb is heard here, as the window hears it.
        TheProgressWorthWriting progress = new();
        List<string> written = [];

        for (int percent = 0; percent <= 87; percent++)
        {
            string line = $"disc 1 of 2: checking the disc, {percent}%, file 5 of 41: big.dwpart01";

            if (progress.Worth(line))
            {
                written.Add(line);
            }
        }

        string next = "disc 1 of 2: checking the disc, 88%, file 6 of 41: Start Here.hta";

        if (progress.Worth(next))
        {
            written.Add(next);
        }

        Assert.Equal(
            [
                .. Enumerable.Range(0, 9).Select(tenth => $"disc 1 of 2: checking the disc, {tenth * 10}%, file 5 of 41: big.dwpart01"),
                next,
            ],
            written);
    }

    [Fact]
    public void A_stage_without_a_percentage_is_still_one_line()
    {
        TheProgressWorthWriting progress = new();

        Assert.True(progress.Worth("waiting for the drive"));
        Assert.False(progress.Worth("waiting for the drive"));

        Assert.True(progress.Worth("checking file 38 of 39"));
        Assert.False(progress.Worth("checking file 39 of 39"));

        // And a percentage that goes back within a stage is not news.
        Assert.True(progress.Worth("writing disc 2 of 2, 45%"));
        Assert.False(progress.Worth("writing disc 2 of 2, 41%"));
        Assert.False(progress.Worth("writing disc 2 of 2, 49%"));
        Assert.True(progress.Worth("writing disc 2 of 2, 50%"));

        Assert.False(progress.Worth(string.Empty));
    }

    [Fact]
    public void A_read_back_names_every_file_that_failed()
    {
        // The window shows the first few. The night of 2026-11-10 had seven,
        // and four of them were "and 4 more".
        string line = WhatTheLogHears.AboutTheCheck(new DiscCheck(
            32,
            ["setup.exe.dwpart02", "ARTWORK\\cover.png", "Manual.pdf", "music.mp3"],
            [],
            [],
            ["autorun.inf", "AUTORUN\\menu.hta", "AUTORUN\\menu.js"],
            null));

        Assert.Equal(
            "Read back: 32 matched; unreadable: setup.exe.dwpart02, ARTWORK\\cover.png, Manual.pdf, music.mp3; "
                + "different: autorun.inf, AUTORUN\\menu.hta, AUTORUN\\menu.js. NOT GOOD.",
            line);
    }

    [Fact]
    public void A_read_back_names_what_is_missing_what_was_not_expected_and_why_it_could_not_check()
    {
        // Stryker found nothing reaching these three on 2026-11-12, so a log
        // that dropped them would have passed every test.
        string line = WhatTheLogHears.AboutTheCheck(new DiscCheck(
            5,
            [],
            ["setup-1.bin"],
            ["stray.txt"],
            [],
            "the drive stopped answering"));

        Assert.Equal(
            "Read back: 5 matched; missing: setup-1.bin; not expected: stray.txt; "
                + "could not check: the drive stopped answering. NOT GOOD.",
            line);
    }

    [Fact]
    public void A_good_read_back_says_so()
    {
        Assert.Equal(
            "Read back: 39 matched. Good.",
            WhatTheLogHears.AboutTheCheck(new DiscCheck(39, [], [], [], [], null)));
    }

    // --------------------------------------------------------- the wiring

    [Fact]
    public void The_app_writes_its_log_from_the_first_line_and_hears_the_drives_and_the_window()
    {
        // Nothing here can reference the app, so it is read off disk, like
        // the markup guards.
        string app = File.ReadAllText(Repository.File("src", "DiscWright.App", "App.xaml.cs"));

        Assert.Contains("TheLogOnDisk log = new(TheLogOnDisk.ForThisUser(package));", app, StringComparison.Ordinal);
        Assert.Contains("new TheDrivesWrittenDown(new WindowsRecorders(log), log)", app, StringComparison.Ordinal);
        Assert.Contains("WhatTheLogHears.Listen(screen, log)", app, StringComparison.Ordinal);
        Assert.Contains("WorthAsking.Log = log", app, StringComparison.Ordinal);
        Assert.Contains("log.Write($\"Stopped by something nobody expected", app, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_question_and_its_answer_are_written()
    {
        string asking = File.ReadAllText(Repository.File("src", "DiscWright.App", "WorthAsking.xaml.cs"));

        Assert.Contains("Log.Write($\"Asked: {headline}\")", asking, StringComparison.Ordinal);
        Assert.Contains("Log.Write($\"Answered: {(likelyOne ? likely : other)}\")", asking, StringComparison.Ordinal);
    }

    [Fact]
    public void A_choice_out_of_several_is_written_with_what_was_offered_and_taken()
    {
        // Which disc to erase, with two drives, came through the chooser and
        // not the yes or no, so the log of 2026-11-10 had the erase but not
        // the question.
        string picking = File.ReadAllText(Repository.File("src", "DiscWright.App", "PickOne.xaml.cs"));

        Assert.Contains("WorthAsking.Log.Write($\"Asked: {headline} ({string.Join(\"; \", asking.Choices)})\")", picking, StringComparison.Ordinal);
        Assert.Contains("\"Answered: none of them\"", picking, StringComparison.Ordinal);
        Assert.Contains("$\"Answered: {asking.Choices[asking.Chosen]}\"", picking, StringComparison.Ordinal);
    }

    [Fact]
    public void Windows_refusing_to_list_the_drives_is_written_with_its_code()
    {
        string recorders = File.ReadAllText(Repository.File("src", "DiscWright.Burning", "WindowsRecorders.cs"));

        // Every way out of the catch-all that answers with no drives says why.
        Assert.Equal(3, CountOf(recorders, "WhyNone(wrong);"));
        Assert.Contains("0x{wrong.HResult:X8}", recorders, StringComparison.Ordinal);
    }

    private static int CountOf(string text, string what)
    {
        int count = 0;

        for (int at = text.IndexOf(what, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private sealed class Into(List<string> lines) : IKeepALog
    {
        public void Write(string line)
        {
            lock (lines)
            {
                lines.Add(line);
            }
        }
    }

    private sealed class Answering(IReadOnlyList<Recorder> now, Action? whileAsked = null) : IFindRecorders
    {
        internal IReadOnlyList<Recorder> Now { get; set; } = now;

        public IReadOnlyList<Recorder> All()
        {
            whileAsked?.Invoke();
            return Now;
        }
    }
}
