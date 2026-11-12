using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Platform;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Updating an installed copy from its own version link, when somebody presses it.
/// </summary>
/// <remarks>
/// <para>
/// Asked for by the owner on 2026-11-12: Check for new version, and when there
/// is one, Update, which downloads the release's installer, checks it and runs
/// it. Never on its own. Three things matter more than the rest, and each has
/// tests of its own here: it only ever fetches this program's own release, it
/// never runs a file that does not match the release's checksum, and it never
/// closes the program while something is being written.
/// </para>
/// <para>
/// What none of this can show is the real installer replacing a running copy
/// and opening the new one. That was watched on the VM, a 2.0.0 install
/// updated to a stand-in release, and is written up in the pull request.
/// </para>
/// </remarks>
public sealed class UpdatingFromTheApp : IDisposable
{
    private const string Tag = "v2.0.1";
    private const string Setup = "DiscWright-2.0.1-setup.exe";
    private const string TheHash = "3f1c0a9e5b7d2468ace13579bdf02468ace13579bdf02468ace13579bdf0246a";
    private const string Page = "https://github.com/lazardjokovic/discwright/releases/tag/v2.0.1";

    private static readonly string From = TheUpdate.Downloads + Tag + "/";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "dw-update-" + Guid.NewGuid().ToString("N")[..8]);

    public UpdatingFromTheApp() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ---- What would be fetched -------------------------------------------------

    [Fact]
    public void A_newer_release_with_its_installer_and_checksums_offers_an_update()
    {
        TheUpdate? update = TheUpdate.Offered(Newer(), TheFiles());

        Assert.NotNull(update);
        Assert.Equal("2.0.1", update.Version);
        Assert.Equal(From + Setup, update.Installer.Url);
        Assert.Equal(From + "SHA256SUMS.txt", update.Sums.Url);
    }

    [Theory]
    [InlineData("v2.0.0")]
    [InlineData("v1.9.9")]
    [InlineData("the summer release")]
    public void A_release_that_is_not_newer_offers_none(string tag)
    {
        HowThisBuildCompares how = new("2.0.0", tag, Page);

        Assert.Null(TheUpdate.Offered(how, TheFiles(tag)));
    }

    [Fact]
    public void A_release_missing_either_file_offers_none()
    {
        Assert.Null(TheUpdate.Offered(Newer(), [TheInstaller()]));
        Assert.Null(TheUpdate.Offered(Newer(), [TheSums()]));
        Assert.Null(TheUpdate.Offered(Newer(), []));
        Assert.Null(TheUpdate.Offered(Newer(), null));
    }

    [Fact]
    public void Only_the_installer_the_release_workflow_names_counts()
    {
        // The portable exe, or anything else attached, is not an installer.
        Assert.Null(TheUpdate.Offered(Newer(),
        [
            new AReleasedFile("DiscWright-2.0.1-portable.exe", From + "DiscWright-2.0.1-portable.exe", 10),
            TheSums(),
        ]));

        // An installer of another version in a release is a mistake to stop at.
        Assert.Null(TheUpdate.Offered(Newer(),
        [
            new AReleasedFile("DiscWright-2.0.0-setup.exe", From + "DiscWright-2.0.0-setup.exe", 10),
            TheSums(),
        ]));
    }

    [Theory]
    [InlineData("https://github.com/somebody-else/discwright/releases/download/v2.0.1/DiscWright-2.0.1-setup.exe")]
    [InlineData("https://github.com/lazardjokovic/discwright/releases/download/v2.0.0/DiscWright-2.0.1-setup.exe")]
    [InlineData("https://example.invalid/DiscWright-2.0.1-setup.exe")]
    [InlineData("http://github.com/lazardjokovic/discwright/releases/download/v2.0.1/DiscWright-2.0.1-setup.exe")]
    public void An_installer_from_anywhere_but_that_release_offers_none(string url)
    {
        Assert.Null(TheUpdate.Offered(Newer(), [new AReleasedFile(Setup, url, 10), TheSums()]));
    }

    [Fact]
    public void Checksums_from_anywhere_but_that_release_offer_none()
    {
        Assert.Null(TheUpdate.Offered(Newer(),
            [TheInstaller(), new AReleasedFile("SHA256SUMS.txt", "https://example.invalid/SHA256SUMS.txt", 10)]));
    }

    [Fact]
    public void Two_files_of_one_name_offer_none()
    {
        Assert.Null(TheUpdate.Offered(Newer(), [TheInstaller(), TheInstaller(), TheSums()]));
    }

    [Fact]
    public void Reads_the_checksums_the_release_workflow_writes()
    {
        // Exactly the shape packaging/Build-TheRelease.ps1 writes: a hash, two
        // spaces, the name, LF, no BOM.
        string sums = $"{TheHash}  {Setup}\n{new string('b', 64)}  DiscWright-2.0.1-portable.exe\n";

        Assert.Equal(TheHash, TheUpdate.TheHashIn(sums, Setup));
        Assert.Equal(new string('b', 64), TheUpdate.TheHashIn(sums, "DiscWright-2.0.1-portable.exe"));
    }

    [Fact]
    public void Reads_checksums_written_any_way_sha256sum_reads_them()
    {
        Assert.Equal(TheHash, TheUpdate.TheHashIn($"{TheHash.ToUpperInvariant()} *{Setup}\r\n", Setup));
        Assert.Equal(TheHash, TheUpdate.TheHashIn($"garbage\n\n{TheHash}  {Setup}", Setup));
    }

    [Fact]
    public void Checksums_that_do_not_name_it_or_disagree_give_no_hash()
    {
        Assert.Null(TheUpdate.TheHashIn($"{TheHash}  something-else.exe\n", Setup));
        Assert.Null(TheUpdate.TheHashIn($"{TheHash}  {Setup}\n{new string('c', 64)}  {Setup}\n", Setup));
        Assert.Null(TheUpdate.TheHashIn($"{TheHash[..63]}  {Setup}\n", Setup));
        Assert.Null(TheUpdate.TheHashIn($"{TheHash[..63]}g  {Setup}\n", Setup));
        Assert.Null(TheUpdate.TheHashIn(string.Empty, Setup));

        // The name is the whole name, not the end of a longer one.
        Assert.Null(TheUpdate.TheHashIn($"{TheHash}  old-{Setup}\n", Setup));
    }

    [Fact]
    public void A_match_is_the_same_hash_in_either_case_and_nothing_else()
    {
        Assert.True(TheUpdate.Matches(TheHash, TheHash.ToUpperInvariant()));
        Assert.False(TheUpdate.Matches(TheHash, new string('a', 64)));
        Assert.False(TheUpdate.Matches(null, TheHash));
        Assert.False(TheUpdate.Matches(TheHash, null));
        Assert.False(TheUpdate.Matches(TheHash, string.Empty));
    }

    // ---- What the window offers ------------------------------------------------

    [Fact]
    public async Task An_installed_copy_offers_Update_and_keeps_the_page_a_link_away()
    {
        AnUpdater updater = new();
        MakeADisc window = AWindow(updater);

        await window.CheckForANewVersion();

        Assert.True(window.TheUpdateOnOffer);
        Assert.Equal("Update to 2.0.1", window.WhatTheVersionLinkSays);
        Assert.Equal("There is a newer DiscWright: 2.0.1. This one is 2.0.0.", window.WhatTheVersionSays);
        Assert.Equal(Page, window.TheVersionPage);

        // Asking is not updating. Nothing has been fetched by the first press.
        Assert.Empty(updater.Read);
        Assert.Empty(updater.Downloaded);
    }

    [Fact]
    public async Task A_portable_copy_is_offered_the_page_as_before()
    {
        MakeADisc window = AWindow(new AnUpdater { CanUpdate = false });

        await window.CheckForANewVersion();

        Assert.False(window.TheUpdateOnOffer);
        Assert.Equal("Get it", window.WhatTheVersionLinkSays);
        Assert.Equal(Page, window.TheVersionPage);
    }

    [Fact]
    public async Task A_copy_with_nothing_to_update_it_is_offered_the_page_as_before()
    {
        MakeADisc window = TheMainWindowWithoutAWindow.New(releases: new ARelease(Released()));

        await window.CheckForANewVersion();

        Assert.False(window.TheUpdateOnOffer);
        Assert.Equal("Get it", window.WhatTheVersionLinkSays);
    }

    [Fact]
    public async Task A_release_without_checksums_is_offered_as_a_page()
    {
        MakeADisc window = AWindow(new AnUpdater(), Released([TheInstaller()]));

        await window.CheckForANewVersion();

        Assert.False(window.TheUpdateOnOffer);
        Assert.Equal("Get it", window.WhatTheVersionLinkSays);
    }

    [Fact]
    public async Task The_newest_already_offers_nothing()
    {
        MakeADisc window = AWindow(new AnUpdater(), new WhatWasReleased("v2.0.0", Page, Files: TheFiles("v2.0.0")));

        await window.CheckForANewVersion();

        Assert.False(window.TheUpdateOnOffer);
        Assert.Equal("Check again", window.WhatTheVersionLinkSays);
    }

    [Fact]
    public async Task Update_fetches_the_checksums_then_the_installer_checks_it_and_runs_it()
    {
        AnUpdater updater = new();
        MakeADisc window = AWindow(updater);
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        Assert.Equal([From + "SHA256SUMS.txt"], updater.Read);
        Assert.Equal([From + Setup], updater.Downloaded);
        Assert.Equal([updater.WhereTheDownloadGoes(Setup)], updater.Installed);
        Assert.Empty(updater.ThrownAway);

        Assert.Equal("Updating", window.WhatTheVersionLinkSays);
        Assert.Equal("DiscWright closes now, updates to 2.0.1 and opens again by itself.", window.WhatTheVersionSays);
    }

    [Fact]
    public async Task A_download_that_does_not_match_is_thrown_away_and_never_run()
    {
        AnUpdater updater = new();
        MakeADisc window = AWindow(updater, hasher: new AHasher(new string('d', 64)));
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        Assert.Empty(updater.Installed);
        Assert.Equal([updater.WhereTheDownloadGoes(Setup)], updater.ThrownAway);
        Assert.StartsWith("The download did not match the checksum", window.WhatTheVersionSays, StringComparison.Ordinal);

        // And it can be tried again, with the page still there.
        Assert.Equal("Update to 2.0.1", window.WhatTheVersionLinkSays);
        Assert.True(window.TheUpdateOnOffer);
    }

    [Fact]
    public async Task Checksums_that_do_not_list_the_installer_download_nothing()
    {
        AnUpdater updater = new() { Sums = $"{TheHash}  something-else.exe\n" };
        MakeADisc window = AWindow(updater);
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        Assert.Empty(updater.Downloaded);
        Assert.Empty(updater.Installed);
        Assert.StartsWith("The release's checksums do not list its installer", window.WhatTheVersionSays, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_download_that_fails_is_thrown_away_and_said()
    {
        AnUpdater updater = new() { DownloadFails = true };
        MakeADisc window = AWindow(updater);
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        Assert.Empty(updater.Installed);
        Assert.Equal([updater.WhereTheDownloadGoes(Setup)], updater.ThrownAway);
        Assert.Equal("The download did not finish. Try again, or get it from What's new.", window.WhatTheVersionSays);
        Assert.Equal("Update to 2.0.1", window.WhatTheVersionLinkSays);
    }

    [Fact]
    public async Task Checksums_that_will_not_download_are_said()
    {
        AnUpdater updater = new() { ReadFails = true };
        MakeADisc window = AWindow(updater);
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        Assert.Empty(updater.Downloaded);
        Assert.Empty(updater.Installed);
        Assert.Equal("The download did not finish. Try again, or get it from What's new.", window.WhatTheVersionSays);
    }

    [Fact]
    public async Task An_installer_that_will_not_start_leaves_this_copy_running_and_says_so()
    {
        AnUpdater updater = new() { InstallFails = true };
        MakeADisc window = AWindow(updater);
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        Assert.Equal(
            "The installer would not start, so this is still 2.0.0. Try again, or get it from What's new.",
            window.WhatTheVersionSays);
        Assert.Equal("Update to 2.0.1", window.WhatTheVersionLinkSays);
    }

    [Fact]
    public async Task Update_does_nothing_until_asking_has_found_one()
    {
        AnUpdater updater = new();
        MakeADisc window = AWindow(updater);

        await window.UpdateThisCopy();

        Assert.Empty(updater.Read);
        Assert.Empty(updater.Downloaded);
        Assert.Empty(updater.Installed);
    }

    [Fact]
    public async Task Says_how_far_the_download_has_got()
    {
        string? heard = null;
        MakeADisc? window = null;

        AnUpdater updater = new()
        {
            WhileDownloading = sofar =>
            {
                sofar.Report(TheInstaller().Size / 2);
                OnTheWindowsThread.WaitUntil(() => window!.WhatTheVersionSays.Contains("50%", StringComparison.Ordinal));
                heard = window!.WhatTheVersionSays;
            },
        };

        window = AWindow(updater);

        await OnTheWindowsThread.Run(async () =>
        {
            await window.CheckForANewVersion();
            await window.UpdateThisCopy();
        });

        Assert.Equal("Downloading DiscWright 2.0.1, 50%.", heard);
    }

    [Fact]
    public async Task A_second_press_while_it_downloads_is_nothing()
    {
        TaskCompletionSource letGo = new();
        AnUpdater updater = new() { Holding = letGo.Task };
        MakeADisc window = AWindow(updater);
        await window.CheckForANewVersion();

        Task first = window.UpdateThisCopy();
        await updater.StartedDownloading.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Task second = window.UpdateThisCopy();
        Task asking = window.CheckForANewVersion();

        // Let go before asserting, so nothing is left pending however it ends.
        letGo.SetResult();
        await Task.WhenAll(first, second, asking).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Single(updater.Downloaded);
        Assert.Single(updater.Installed);
    }

    // ---- Never while something is being written --------------------------------

    [Fact]
    public async Task Will_not_start_while_something_is_running()
    {
        AnUpdater updater = new();
        ABuildThatWaits building = new();
        MakeADisc window = AWindow(updater, builder: building);
        await window.CheckForANewVersion();

        Task made = StartABuild(window, building);
        await building.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await window.UpdateThisCopy();

            Assert.Empty(updater.Read);
            Assert.Empty(updater.Downloaded);
            Assert.Empty(updater.Installed);
            Assert.Equal(
                "Something is running. Updating closes DiscWright, so press Update to 2.0.1 once it is done.",
                window.WhatTheVersionSays);
        }
        finally
        {
            building.LetGo.TrySetResult();
            await made.WaitAsync(TimeSpan.FromSeconds(10));
        }

        // Once it is done, the same press updates.
        await window.UpdateThisCopy();
        Assert.Single(updater.Installed);
    }

    [Fact]
    public async Task A_run_that_starts_during_the_download_makes_the_installer_wait()
    {
        TaskCompletionSource letTheDownloadGo = new();
        AnUpdater updater = new() { Holding = letTheDownloadGo.Task };
        ABuildThatWaits building = new();
        MakeADisc window = AWindow(updater, builder: building);
        await window.CheckForANewVersion();

        Task updating = window.UpdateThisCopy();
        await updater.StartedDownloading.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Task made = StartABuild(window, building);
        await building.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            letTheDownloadGo.SetResult();
            await updating.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Empty(updater.Installed);
            Assert.Equal("Install 2.0.1", window.WhatTheVersionLinkSays);
            Assert.Contains("it waits until nothing is running", window.WhatTheVersionSays, StringComparison.Ordinal);

            // Pressed again while it still runs, it still waits.
            await window.UpdateThisCopy();
            Assert.Empty(updater.Installed);
        }
        finally
        {
            building.LetGo.TrySetResult();
            await made.WaitAsync(TimeSpan.FromSeconds(10));
        }

        // Nothing running: the installer already checked runs, without a second download.
        await window.UpdateThisCopy();

        Assert.Single(updater.Downloaded);
        Assert.Equal([updater.WhereTheDownloadGoes(Setup)], updater.Installed);
    }

    // ---- The Windows side ------------------------------------------------------

    [Fact]
    public void Only_a_copy_with_the_installers_uninstaller_beside_it_can_update_itself()
    {
        using UpdatingFromGitHub portable = new(_folder, () => { });
        Assert.False(portable.CanUpdateItself);

        File.WriteAllText(Path.Combine(_folder, "unins000.exe"), "x");

        using UpdatingFromGitHub installed = new(_folder, () => { });
        Assert.True(installed.CanUpdateItself);

        using UpdatingFromGitHub nowhere = new(Path.Combine(_folder, "not here"), () => { });
        Assert.False(nowhere.CanUpdateItself);
    }

    [Fact]
    public async Task Downloads_the_whole_file_and_says_how_far()
    {
        byte[] content = new byte[3 * 1024 * 1024];
        new Random(5).NextBytes(content);

        using UpdatingFromGitHub fetching = new(_folder, () => { }, Path.Combine(_folder, "down"),
            new HttpClient(new AServer(content)));

        string into = fetching.WhereTheDownloadGoes(Setup);
        List<long> heard = [];

        await fetching.Download(From + Setup, into, new Heard(heard), CancellationToken.None);

        Assert.Equal(content, await File.ReadAllBytesAsync(into));
        Assert.Equal(content.Length, heard[^1]);
        Assert.True(heard.Count >= 3, "It should say how far more than once on the way.");
        Assert.Equal(heard.Order(), heard);
    }

    [Fact]
    public async Task Fetches_nothing_from_anywhere_but_this_programs_releases()
    {
        AServer server = new([1, 2, 3]);
        using UpdatingFromGitHub fetching = new(_folder, () => { }, Path.Combine(_folder, "down"), new HttpClient(server));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fetching.Read("https://example.invalid/SHA256SUMS.txt", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fetching.Download("https://example.invalid/x.exe", Path.Combine(_folder, "x.exe"), new Heard([]), CancellationToken.None));

        Assert.Equal(0, server.Asked);
    }

    [Fact]
    public async Task A_refused_download_is_an_error_and_not_a_file()
    {
        using UpdatingFromGitHub fetching = new(_folder, () => { }, Path.Combine(_folder, "down"),
            new HttpClient(new AServer([], HttpStatusCode.NotFound)));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            fetching.Read(From + "SHA256SUMS.txt", CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            fetching.Download(From + Setup, Path.Combine(_folder, "x.exe"), new Heard([]), CancellationToken.None));
    }

    [Fact]
    public void Clears_out_what_an_earlier_press_left_before_the_next_download()
    {
        string downloads = Path.Combine(_folder, "down");
        Directory.CreateDirectory(downloads);
        File.WriteAllText(Path.Combine(downloads, "DiscWright-2.0.0-setup.exe"), "old");

        using UpdatingFromGitHub fetching = new(_folder, () => { }, downloads);

        string into = fetching.WhereTheDownloadGoes(Setup);

        Assert.Equal(Path.Combine(downloads, Setup), into);
        Assert.Empty(Directory.EnumerateFiles(downloads));
    }

    [Fact]
    public void Clears_the_installer_an_earlier_update_ran_and_minds_nothing_if_there_is_none()
    {
        string downloads = Path.Combine(_folder, "down");
        Directory.CreateDirectory(downloads);
        File.WriteAllText(Path.Combine(downloads, "DiscWright-2.0.0-setup.exe"), "ran last time");

        using UpdatingFromGitHub fetching = new(_folder, () => { }, downloads);

        fetching.ClearWhatAnEarlierUpdateLeft();
        Assert.False(Directory.Exists(downloads));

        fetching.ClearWhatAnEarlierUpdateLeft();
    }

    [Fact]
    public void A_download_still_in_use_is_left_for_the_next_start()
    {
        string downloads = Path.Combine(_folder, "down");
        Directory.CreateDirectory(downloads);
        string running = Path.Combine(downloads, "DiscWright-2.0.0-setup.exe");

        using UpdatingFromGitHub fetching = new(_folder, () => { }, downloads);

        using (new FileStream(running, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fetching.ClearWhatAnEarlierUpdateLeft();
            Assert.True(File.Exists(running));
        }

        fetching.ClearWhatAnEarlierUpdateLeft();
        Assert.False(Directory.Exists(downloads));
    }

    [Fact]
    public void Throws_away_a_download_and_minds_nothing_if_it_is_not_there()
    {
        string path = Path.Combine(_folder, Setup);
        File.WriteAllText(path, "x");

        using UpdatingFromGitHub fetching = new(_folder, () => { });

        fetching.ThrowAway(path);
        Assert.False(File.Exists(path));

        fetching.ThrowAway(path);
    }

    [Fact]
    public void Starts_the_installer_quietly_and_asks_it_to_open_the_new_copy()
    {
        // These are Inno Setup's own switches, and RELAUNCH is ours, read by
        // the installer script below. Interactive pages on an update would
        // ask questions the first install already answered.
        string[] switches = UpdatingFromGitHub.Quietly.Split(' ');

        Assert.Contains("/SILENT", switches);
        Assert.Contains("/SUPPRESSMSGBOXES", switches);
        Assert.Contains("/NORESTART", switches);
        Assert.Contains("/RELAUNCH", switches);
        Assert.DoesNotContain("/VERYSILENT", switches);
    }

    [Fact]
    public void The_installer_opens_the_new_copy_only_after_an_update_the_app_asked_for()
    {
        string script = File.ReadAllText(Repository.File("packaging/DiscWright.iss"));

        // A [Run] entry with no postinstall, so a silent install runs it,
        // checked by a function that wants both the silence and the switch,
        // so winget's quiet install opens nothing.
        Match entry = Regex.Match(
            script,
            @"(?m)^Filename: ""\{app\}\\DiscWright\.App\.exe""; \\\r?\n\s+WorkingDir: ""\{app\}""; \\\r?\n\s+Flags: nowait; \\\r?\n\s+Check: ReopenAfterwards\r?$");
        Assert.True(entry.Success, "DiscWright.iss has no [Run] entry that reopens the app after an update.");

        Match check = Regex.Match(script, @"(?s)function ReopenAfterwards\(\): Boolean;.*?\nend;");
        Assert.True(check.Success, "DiscWright.iss has no ReopenAfterwards.");
        Assert.Contains("WizardSilent()", check.Value, StringComparison.Ordinal);
        Assert.Contains("'/RELAUNCH'", check.Value, StringComparison.Ordinal);

        // And the ordinary entry still skips a silent install, so there is
        // never a second copy opened beside the first.
        Assert.Contains("Flags: postinstall nowait skipifsilent", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_sends_the_link_to_Update_first_and_the_app_hands_the_screen_an_updater()
    {
        // No test can press the real link, so the handler is read: once an
        // update is on offer the press goes to the screen, which decides
        // everything, before the window would open a page or ask again.
        string window = File.ReadAllText(Repository.File("src/DiscWright.App/MainWindow.xaml.cs"));
        Match handler = Regex.Match(window, @"(?s)private async void CheckForANewVersion\(.*?\r?\n    }\r?\n");
        Assert.True(handler.Success, "MainWindow.xaml.cs has no CheckForANewVersion handler.");

        int offer = handler.Value.IndexOf("if (_screen.TheUpdateOnOffer)", StringComparison.Ordinal);
        int update = handler.Value.IndexOf("await _screen.UpdateThisCopy();", StringComparison.Ordinal);
        int ask = handler.Value.IndexOf("await _screen.CheckForANewVersion();", StringComparison.Ordinal);

        Assert.True(offer >= 0 && update > offer && ask > update, handler.Value);

        Match whatsNew = Regex.Match(window, @"(?s)private void OpenWhatsNew\(.*?\r?\n    }\r?\n");
        Assert.True(whatsNew.Success, "MainWindow.xaml.cs has no OpenWhatsNew handler.");
        Assert.Contains("_screen.TheVersionPage", whatsNew.Value, StringComparison.Ordinal);

        // And the real program can update itself: without this the screen is
        // handed no updater and every copy behaves as a portable one.
        string app = File.ReadAllText(Repository.File("src/DiscWright.App/App.xaml.cs"));
        Assert.Contains("UpdatingFromGitHub updater = new(", app, StringComparison.Ordinal);
        Assert.Contains("AppContext.BaseDirectory", app, StringComparison.Ordinal);
        // Through the log, so every step of an update is written down.
        Assert.Contains("Updater: new TheUpdateWrittenDown(updater, log)));", app, StringComparison.Ordinal);

        // And it tidies the installer an earlier update ran, as it starts.
        Assert.Contains("updater.ClearWhatAnEarlierUpdateLeft();", app, StringComparison.Ordinal);
    }

    /// <summary>
    /// The real newest release, fetched and checked the way Update would.
    /// </summary>
    /// <remarks>
    /// Off unless DISCWRIGHT_ASK_GITHUB is set, with the other test that asks
    /// GitHub. Every other test here is handed a release; this one proves the
    /// release workflow's real files are what <see cref="TheUpdate"/> expects,
    /// by offering the newest release to a build older than all of them and
    /// downloading its real installer against its real checksum.
    /// </remarks>
    [Fact]
    public async Task Really_fetches_the_newest_release_and_it_matches_its_checksum()
    {
        if (Environment.GetEnvironmentVariable("DISCWRIGHT_ASK_GITHUB") is not "yes")
        {
            return;
        }

        using WhatGitHubHasReleased asking = new();
        WhatWasReleased said = await asking.TheLatest(CancellationToken.None);
        Assert.Null(said.WhyNot);

        TheUpdate? update = TheUpdate.Offered(new HowThisBuildCompares("0.0.1", said.Tag, said.Where), said.Files);
        Assert.NotNull(update);

        using UpdatingFromGitHub fetching = new(_folder, () => { }, Path.Combine(_folder, "down"));
        string sums = await fetching.Read(update.Sums.Url, CancellationToken.None);
        string into = fetching.WhereTheDownloadGoes(update.Installer.Name);
        await fetching.Download(update.Installer.Url, into, new Heard([]), CancellationToken.None);

        Assert.Equal(update.Installer.Size, new FileInfo(into).Length);
        Assert.True(TheUpdate.Matches(
            TheUpdate.TheHashIn(sums, update.Installer.Name),
            new Sha256Hasher().Sha256(into, CancellationToken.None)));
    }

    [Fact]
    public async Task The_link_says_Updating_while_the_download_is_checked()
    {
        // Stryker found this one link unwatched on 2026-11-12: a press during
        // the check would have read as a fresh offer.
        string? whileChecking = null;
        MakeADisc? window = null;

        window = AWindow(new AnUpdater(), hasher: new AHasherThatLooks(TheHash, () => whileChecking = window!.WhatTheVersionLinkSays));
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        Assert.Equal("Updating", whileChecking);
    }

    [Fact]
    public async Task An_installer_whose_size_is_not_known_says_none_of_it_has_arrived_rather_than_failing()
    {
        string? heard = null;
        MakeADisc? window = null;

        AnUpdater updater = new()
        {
            WhileDownloading = sofar =>
            {
                sofar.Report(1_000);
                OnTheWindowsThread.WaitUntil(() => window!.WhatTheVersionSays.StartsWith("Downloading", StringComparison.Ordinal));
                heard = window!.WhatTheVersionSays;
            },
        };

        window = AWindow(updater, released: Released([new AReleasedFile(Setup, From + Setup, 0), TheSums()]));

        await OnTheWindowsThread.Run(async () =>
        {
            await window.CheckForANewVersion();
            await window.UpdateThisCopy();
        });

        Assert.Equal("Downloading DiscWright 2.0.1, 0%.", heard);
        Assert.Equal("DiscWright closes now, updates to 2.0.1 and opens again by itself.", window.WhatTheVersionSays);
    }

    [Fact]
    public async Task A_download_that_will_not_be_thrown_away_is_still_never_run()
    {
        AnUpdater updater = new() { ThrowAwayFails = true };
        MakeADisc window = AWindow(updater, hasher: new AHasher(new string('d', 64)));
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        // Windows tidies its temporary folder; what matters is that it never ran.
        Assert.Empty(updater.Installed);
        Assert.StartsWith("The download did not match the checksum", window.WhatTheVersionSays, StringComparison.Ordinal);
        Assert.Equal("Update to 2.0.1", window.WhatTheVersionLinkSays);
    }

    // ---- What the log says -----------------------------------------------------

    [Fact]
    public async Task A_whole_update_is_written_step_by_step()
    {
        List<string> log = [];
        MakeADisc? window = null;

        AnUpdater updater = new()
        {
            WhileDownloading = sofar =>
            {
                sofar.Report(TheInstaller().Size);
                OnTheWindowsThread.WaitUntil(() => window!.WhatTheVersionSays.Contains("100%", StringComparison.Ordinal));
            },
        };

        window = AWindow(new TheUpdateWrittenDown(updater, new Into(log)));
        WhatTheLogHears.Listen(window, new Into(log));

        await OnTheWindowsThread.Run(async () =>
        {
            await window.CheckForANewVersion();
            await window.UpdateThisCopy();
        });

        string into = updater.WhereTheDownloadGoes(Setup);

        Assert.Equal(
            [
                "Update: this copy was installed, so it can update itself.",
                "Version: " + Newer().InWords,
                "Version: Downloading DiscWright 2.0.1, 0%.",
                $"Update: reading the checksums at {From}SHA256SUMS.txt",
                "Update: the checksums list 2 file(s).",
                $"Update: downloading {From}{Setup} into {into}",
                "Version: Downloading DiscWright 2.0.1, 100%.",
                "Update: downloaded 26000000 bytes.",
                "Version: Checking the download against the checksum the release publishes.",
                "Version: DiscWright closes now, updates to 2.0.1 and opens again by itself.",
                $"Update: starting {into} and closing.",
            ],
            log);
    }

    [Fact]
    public async Task A_download_that_does_not_match_is_written_and_so_is_throwing_it_away()
    {
        List<string> log = [];
        AnUpdater updater = new();
        MakeADisc window = AWindow(new TheUpdateWrittenDown(updater, new Into(log)), hasher: new AHasher(new string('d', 64)));
        WhatTheLogHears.Listen(window, new Into(log));
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        Assert.Contains(log, each => each.StartsWith("Version: The download did not match the checksum", StringComparison.Ordinal));
        Assert.Contains($"Update: threw away {updater.WhereTheDownloadGoes(Setup)}", log);
        Assert.DoesNotContain(log, each => each.StartsWith("Update: starting", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_download_that_fails_is_written_with_how_far_it_got_and_why()
    {
        List<string> log = [];
        AnUpdater updater = new()
        {
            DownloadFails = true,
            WhileDownloading = sofar => sofar.Report(1_234_567),
        };

        MakeADisc window = AWindow(new TheUpdateWrittenDown(updater, new Into(log)));
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        Assert.Contains("Update: the download stopped after 1234567 bytes, IOException: the line dropped", log);
        Assert.DoesNotContain(log, each => each.StartsWith("Update: downloaded", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Checksums_that_will_not_download_are_written_with_why()
    {
        List<string> log = [];
        MakeADisc window = AWindow(new TheUpdateWrittenDown(new AnUpdater { ReadFails = true }, new Into(log)));
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        Assert.Contains("Update: reading the checksums failed, HttpRequestException: no way out", log);
        Assert.DoesNotContain(log, each => each.StartsWith("Update: downloading", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_installer_that_will_not_start_is_written_with_why()
    {
        List<string> log = [];
        MakeADisc window = AWindow(new TheUpdateWrittenDown(new AnUpdater { InstallFails = true }, new Into(log)));
        await window.CheckForANewVersion();

        await window.UpdateThisCopy();

        // Windows' own words, which are in the language of the machine.
        Assert.Contains(log, each => each.StartsWith("Update: the installer would not start, Win32Exception: ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, "Update: this copy was installed, so it can update itself.")]
    [InlineData(false, "Update: this copy was not installed by the installer, so it is offered the page instead.")]
    public async Task Whether_this_copy_can_update_itself_is_written_once(bool installed, string line)
    {
        List<string> log = [];
        MakeADisc window = AWindow(new TheUpdateWrittenDown(new AnUpdater { CanUpdate = installed }, new Into(log)));

        await window.CheckForANewVersion();
        await window.CheckForANewVersion();

        Assert.Equal([line], log.Where(each => each.StartsWith("Update: this copy", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task The_download_is_written_at_every_tenth_and_not_once_per_report()
    {
        List<string> log = [];
        MakeADisc? window = null;
        long size = TheInstaller().Size;

        AnUpdater updater = new()
        {
            WhileDownloading = sofar =>
            {
                foreach (int percent in new[] { 1, 5, 9, 10, 14, 55, 56, 99, 100 })
                {
                    sofar.Report(size * percent / 100);
                }

                OnTheWindowsThread.WaitUntil(() => window!.WhatTheVersionSays.Contains("100%", StringComparison.Ordinal));
            },
        };

        window = AWindow(updater);
        WhatTheLogHears.Listen(window, new Into(log));

        await OnTheWindowsThread.Run(async () =>
        {
            await window.CheckForANewVersion();
            await window.UpdateThisCopy();
        });

        Assert.Equal(
            ["0%", "10%", "55%", "99%", "100%"],
            log.Where(each => each.StartsWith("Version: Downloading", StringComparison.Ordinal))
               .Select(each => each.Split(", ")[^1].TrimEnd('.')));
    }

    [Fact]
    public async Task A_line_that_differs_only_in_its_version_is_written_and_the_same_line_is_not()
    {
        List<string> log = [];
        MakeADisc window = TheMainWindowWithoutAWindow.New(releases: new OneAfterAnother(
            new WhatWasReleased("v2.0.1", Page),
            new WhatWasReleased("v2.0.1", Page),
            new WhatWasReleased("v2.0.2", Page)));
        WhatTheLogHears.Listen(window, new Into(log));

        await window.CheckForANewVersion();
        await window.CheckForANewVersion();
        await window.CheckForANewVersion();

        Assert.Equal(
            [
                "Version: " + new HowThisBuildCompares("2.0.0", "v2.0.1", Page).InWords,
                "Version: " + new HowThisBuildCompares("2.0.0", "v2.0.2", Page).InWords,
            ],
            log.Where(each => each.StartsWith("Version: ", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Every_failure_goes_on_unchanged_after_it_is_written()
    {
        List<string> log = [];
        IOException dropped = new("the line dropped");
        TheUpdateWrittenDown writing = new(new Throwing(dropped), new Into(log));

        Assert.Same(dropped, await Assert.ThrowsAsync<IOException>(() => writing.Read("x", CancellationToken.None)));
        Assert.Same(dropped, await Assert.ThrowsAsync<IOException>(
            () => writing.Download("x", "y", new Heard([]), CancellationToken.None)));
        Assert.Same(dropped, Assert.Throws<IOException>(() => writing.ThrowAway("y")));
        Assert.Same(dropped, Assert.Throws<IOException>(() => writing.InstallAndClose("y")));

        Assert.Contains("Update: could not throw away y, IOException: the line dropped", log);
        Assert.Equal("because it was cancelled.", TheUpdateWrittenDown.Why(new OperationCanceledException()));
        Assert.Equal("because it was cancelled.", TheUpdateWrittenDown.Why(new TaskCanceledException()));
    }

    // ---- Helpers ---------------------------------------------------------------

    private static HowThisBuildCompares Newer() => new("2.0.0", Tag, Page);

    private static AReleasedFile TheInstaller() => new(Setup, From + Setup, 26_000_000);

    private static AReleasedFile TheSums() => new("SHA256SUMS.txt", From + "SHA256SUMS.txt", 200);

    private static AReleasedFile[] TheFiles(string tag = Tag)
    {
        string version = tag.TrimStart('v');
        string from = TheUpdate.Downloads + tag + "/";

        return
        [
            new AReleasedFile($"DiscWright-{version}-setup.exe", from + $"DiscWright-{version}-setup.exe", 26_000_000),
            new AReleasedFile($"DiscWright-{version}-portable.exe", from + $"DiscWright-{version}-portable.exe", 65_000_000),
            new AReleasedFile("SHA256SUMS.txt", from + "SHA256SUMS.txt", 200),
        ];
    }

    private static WhatWasReleased Released(AReleasedFile[]? files = null) =>
        new(Tag, Page, Files: files ?? TheFiles());

    private static MakeADisc AWindow(
        IUpdateThisCopy updater, WhatWasReleased? released = null, IHasher? hasher = null, IBuildDiscs? builder = null) =>
        TheMainWindowWithoutAWindow.New(
            releases: new ARelease(released ?? Released()),
            hasher: hasher ?? new AHasher(TheHash),
            builder: builder,
            updater: updater);

    private static Task StartABuild(MakeADisc window, ABuildThatWaits building)
    {
        window.OutputFolder = @"C:\out";
        window.Add(TheMainWindowWithoutAWindow.AGame("A Game", 1000));

        Task made = window.MakeIt();
        return made;
    }

    private sealed class ARelease(WhatWasReleased said) : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) => Task.FromResult(said);
    }

    private sealed class AHasher(string says) : IHasher
    {
        public string Sha256(string path, CancellationToken ct) => says;
    }

    /// <summary>A hasher that looks at something while it hashes.</summary>
    private sealed class AHasherThatLooks(string says, Action look) : IHasher
    {
        public string Sha256(string path, CancellationToken ct)
        {
            look();
            return says;
        }
    }

    /// <summary>Each answer in turn, the last one for ever after.</summary>
    private sealed class OneAfterAnother(params WhatWasReleased[] said) : IAskWhatIsReleased
    {
        private int _asked;

        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(said[Math.Min(_asked++, said.Length - 1)]);
    }

    /// <summary>A log that is a list.</summary>
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

    /// <summary>An update where every step throws the one exception it is given.</summary>
    private sealed class Throwing(Exception wrong) : IUpdateThisCopy
    {
        public bool CanUpdateItself => true;

        public string WhereTheDownloadGoes(string named) => named;

        public Task<string> Read(string url, CancellationToken ct) => Task.FromException<string>(wrong);

        public Task Download(string url, string into, IProgress<long> sofar, CancellationToken ct) => Task.FromException(wrong);

        public void ThrowAway(string path) => throw wrong;

        public void InstallAndClose(string installer) => throw wrong;
    }

    /// <summary>Everything an update would do, written down instead of done.</summary>
    private sealed class AnUpdater : IUpdateThisCopy
    {
        internal bool CanUpdate { get; init; } = true;

        internal string Sums { get; init; } = $"{TheHash}  {Setup}\n{new string('e', 64)}  DiscWright-2.0.1-portable.exe\n";

        internal bool ReadFails { get; init; }

        internal bool DownloadFails { get; init; }

        internal bool InstallFails { get; init; }

        internal bool ThrowAwayFails { get; init; }

        internal Task? Holding { get; init; }

        internal Action<IProgress<long>>? WhileDownloading { get; init; }

        internal TaskCompletionSource StartedDownloading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal List<string> Read { get; } = [];

        internal List<string> Downloaded { get; } = [];

        internal List<string> ThrownAway { get; } = [];

        internal List<string> Installed { get; } = [];

        public bool CanUpdateItself => CanUpdate;

        public string WhereTheDownloadGoes(string named) => @"C:\Temp\DiscWright update\" + named;

        Task<string> IUpdateThisCopy.Read(string url, CancellationToken ct)
        {
            Read.Add(url);

            return ReadFails
                ? Task.FromException<string>(new HttpRequestException("no way out"))
                : Task.FromResult(Sums);
        }

        public async Task Download(string url, string into, IProgress<long> sofar, CancellationToken ct)
        {
            Downloaded.Add(url);
            StartedDownloading.TrySetResult();

            WhileDownloading?.Invoke(sofar);

            if (Holding is not null)
            {
                await Holding;
            }

            if (DownloadFails)
            {
                throw new IOException("the line dropped");
            }
        }

        public void ThrowAway(string path)
        {
            if (ThrowAwayFails)
            {
                throw new IOException("in use");
            }

            ThrownAway.Add(path);
        }

        public void InstallAndClose(string installer)
        {
            if (InstallFails)
            {
                throw new System.ComponentModel.Win32Exception(2);
            }

            Installed.Add(installer);
        }
    }

    /// <summary>A build that does not finish until it is let go.</summary>
    private sealed class ABuildThatWaits : IBuildDiscs
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource LetGo { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct)
        {
            Started.TrySetResult();
            await LetGo.Task;
            return new BuiltDiscs([]);
        }
    }

    /// <summary>One answer to every request, counting them.</summary>
    private sealed class AServer(byte[] content, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        internal int Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(content) });
        }
    }

    /// <summary>Heard on the thread that downloaded, so nothing is lost to the pool.</summary>
    private sealed class Heard(List<long> heard) : IProgress<long>
    {
        public void Report(long value) => heard.Add(value);
    }
}
