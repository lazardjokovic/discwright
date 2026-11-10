using System.Text.RegularExpressions;
using DiscWright.Core;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Comparing the build somebody is running with the newest one released.
/// </summary>
/// <remarks>
/// Nothing here goes near a network. The comparison is handed what was said
/// and works out a sentence, which is the whole reason it is a separate thing
/// from the asking.
/// </remarks>
public class AskingIfThereIsANewerOne
{
    [Fact]
    public void Says_so_when_there_is_a_newer_one()
    {
        HowThisBuildCompares how = new("2.0.0", "v2.1.0", "https://example.invalid/releases/v2.1.0");

        Assert.True(how.WorthGetting);
        Assert.Equal("There is a newer DiscWright: 2.1.0. This one is 2.0.0.", how.InWords);
    }

    [Fact]
    public void Says_so_when_this_is_the_newest()
    {
        HowThisBuildCompares how = new("2.0.0", "v2.0.0", "https://example.invalid/releases/v2.0.0");

        Assert.False(how.WorthGetting);
        Assert.Equal("This is the newest one, 2.0.0.", how.InWords);
    }

    [Fact]
    public void Says_so_when_this_is_newer_than_anything_released()
    {
        // Which is every build between two releases, and exactly where this
        // program stands today: the newest thing published is v1's 0.10.0.
        HowThisBuildCompares how = new("2.0.0", "v0.10.0", "https://example.invalid/releases/v0.10.0");

        Assert.False(how.WorthGetting);
        Assert.Equal(
            "This is 2.0.0, which is newer than anything released. The newest release is 0.10.0.",
            how.InWords);
    }

    [Fact]
    public void Says_why_when_it_could_not_ask()
    {
        HowThisBuildCompares how = new("2.0.0", null, null, "There is no way out to the internet from here.");

        Assert.False(how.WorthGetting);
        Assert.Equal(
            "DiscWright could not ask about newer versions just now. "
                + "There is no way out to the internet from here.",
            how.InWords);
    }

    [Fact]
    public void Says_so_when_nothing_has_been_released_at_all()
    {
        HowThisBuildCompares how = new("2.0.0", null, null);

        Assert.False(how.WorthGetting);
        Assert.Equal("Nothing has been released yet, so this is as new as it gets.", how.InWords);
    }

    [Fact]
    public void Will_not_compare_a_tag_that_is_not_a_version()
    {
        HowThisBuildCompares how = new("2.0.0", "the summer release", "https://example.invalid/x");

        // Tags are written by people. Guessing which of two unrelated words is
        // the larger would be worse than saying it cannot tell.
        Assert.False(how.WorthGetting);
        Assert.Contains("not a version number this understands", how.InWords, StringComparison.Ordinal);
    }

    [Fact]
    public void Will_not_offer_a_newer_one_with_nowhere_to_get_it()
    {
        HowThisBuildCompares how = new("2.0.0", "v2.1.0", null);

        // A button that opens nothing is worse than no button.
        Assert.False(how.WorthGetting);
    }

    [Theory]
    [InlineData("v2.1.0", 2, 1, 0)]
    [InlineData("2.1.0", 2, 1, 0)]
    [InlineData("V2.1.0", 2, 1, 0)]
    [InlineData("  v2.1.0  ", 2, 1, 0)]
    [InlineData("v2.1.0-beta.2", 2, 1, 0)]
    [InlineData("2.1.0+a1b2c3d", 2, 1, 0)]
    [InlineData("v2.1", 2, 1, -1)]
    public void Reads_a_version_out_of_however_it_was_written(string written, int major, int minor, int build)
    {
        Version found = Assert.IsType<Version>(HowThisBuildCompares.Reading(written));

        Assert.Equal(major, found.Major);
        Assert.Equal(minor, found.Minor);
        Assert.Equal(build, found.Build);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("v")]
    [InlineData("2")]
    [InlineData("the summer release")]
    public void Reads_nothing_out_of_what_is_not_a_version(string? written) =>
        Assert.Null(HowThisBuildCompares.Reading(written));

    // --------------------------------------------------------- the screen

    [Fact]
    public async Task Asks_nobody_anything_until_the_button_is_pressed()
    {
        ARecordingAsker asker = new(new WhatWasReleased("v2.1.0", "https://example.invalid/x"));
        MakeADisc window = TheScreen(asker);

        // A network call nobody made, from a program whose whole point is
        // putting things on a disc so they work with no network at all.
        Assert.Equal(0, asker.Asked);
        Assert.Null(window.AboutTheVersion);

        await window.CheckForANewVersion();

        Assert.Equal(1, asker.Asked);
        Assert.True(Assert.IsType<HowThisBuildCompares>(window.AboutTheVersion).WorthGetting);
    }

    [Fact]
    public async Task Does_not_fall_over_when_the_asking_throws()
    {
        MakeADisc window = TheScreen(new AnAskerThatThrows());

        await window.CheckForANewVersion();

        // A button that takes the program down because a web request
        // misbehaved would be a poor trade for a version number.
        HowThisBuildCompares how = Assert.IsType<HowThisBuildCompares>(window.AboutTheVersion);
        Assert.False(how.WorthGetting);
        Assert.Contains("went wrong", how.InWords, StringComparison.Ordinal);
    }

    // -------------------------------------------- the line in the header

    [Fact]
    public void Offers_the_asking_before_anybody_has_asked()
    {
        MakeADisc window = TheScreen(new ARecordingAsker(new WhatWasReleased("v2.1.0", "https://example.invalid/x")));

        // Nothing to report yet, so nothing is said. An empty line would take
        // the height of a line and look like something had gone missing.
        Assert.Equal(string.Empty, window.WhatTheVersionSays);
        Assert.Equal("Check for new version", window.WhatTheVersionLinkSays);
        Assert.Null(window.TheVersionPage);
    }

    [Fact]
    public async Task Offers_the_page_once_it_knows_there_is_a_newer_one()
    {
        MakeADisc window = TheScreen(
            new ARecordingAsker(new WhatWasReleased("v2.1.0", "https://example.invalid/x")));

        await window.CheckForANewVersion();

        Assert.Equal("There is a newer DiscWright: 2.1.0. This one is 2.0.0.", window.WhatTheVersionSays);
        Assert.Equal("Get it", window.WhatTheVersionLinkSays);
        Assert.Equal("https://example.invalid/x", window.TheVersionPage);
    }

    [Fact]
    public async Task Offers_to_ask_again_when_this_is_already_the_newest()
    {
        MakeADisc window = TheScreen(
            new ARecordingAsker(new WhatWasReleased("v2.0.0", "https://example.invalid/x")));

        await window.CheckForANewVersion();

        Assert.Equal("This is the newest one, 2.0.0.", window.WhatTheVersionSays);

        // The line is the only way in. One that went quiet after its first
        // press would be a capability that works once a session.
        Assert.Equal("Check again", window.WhatTheVersionLinkSays);

        // There is a page, and it is this version's own. Offering it would be
        // offering somebody what they are already running.
        Assert.Null(window.TheVersionPage);
    }

    [Fact]
    public async Task Offers_to_ask_again_when_it_could_not_ask()
    {
        MakeADisc window = TheScreen(new AnAskerThatThrows());

        await window.CheckForANewVersion();

        Assert.Contains("went wrong", window.WhatTheVersionSays, StringComparison.Ordinal);
        Assert.Equal("Check again", window.WhatTheVersionLinkSays);
        Assert.Null(window.TheVersionPage);
    }

    [Fact]
    public async Task Says_that_it_is_asking_while_it_is_asking()
    {
        AnAskerThatWaits asker = new();
        MakeADisc window = TheScreen(asker);

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        Task asking = window.CheckForANewVersion();
        await asker.Started.Task;

        // It crosses a network and can take a second or two. A link that
        // looks untouched after a press reads as a link that did nothing.
        Assert.Equal("Asking GitHub", window.WhatTheVersionLinkSays);

        // And saying so to nobody is the same as not saying it. The binding
        // only redraws when it is told, and the telling here happens before
        // the answer rather than with it.
        Assert.Contains(nameof(MakeADisc.WhatTheVersionLinkSays), told);

        asker.Answer.SetResult(new WhatWasReleased("v2.1.0", "https://example.invalid/x"));
        await asking;

        Assert.Equal("Get it", window.WhatTheVersionLinkSays);
    }

    [Fact]
    public async Task Tells_the_window_about_every_part_of_the_version_line()
    {
        MakeADisc window = TheScreen(
            new ARecordingAsker(new WhatWasReleased("v2.1.0", "https://example.invalid/x")));

        List<string?> told = [];
        window.PropertyChanged += (_, what) => told.Add(what.PropertyName);

        await window.CheckForANewVersion();

        // All four change together, and a binding never told about one of
        // them shows the answer to the question before last.
        Assert.Contains(nameof(MakeADisc.AboutTheVersion), told);
        Assert.Contains(nameof(MakeADisc.WhatTheVersionSays), told);
        Assert.Contains(nameof(MakeADisc.WhatTheVersionLinkSays), told);
        Assert.Contains(nameof(MakeADisc.TheVersionPage), told);
    }

    [Fact]
    public void The_window_never_judges_a_version_itself()
    {
        // The wording and the judgement both live on the screen, where a test
        // can reach them. The window asks for a page and either gets one or
        // asks again.
        //
        // Read off disk, like the markup guards, because nothing in this
        // project can reference the window's code behind.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        // Not InWords, which every refusal in the window also has.
        Assert.DoesNotContain("HowThisBuildCompares", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WorthGetting", code, StringComparison.Ordinal);
        Assert.DoesNotContain("AboutTheVersion", code, StringComparison.Ordinal);
    }

    // ------------------------------------------------- the real thing

    /// <summary>
    /// Really asks GitHub, when told to.
    /// </summary>
    /// <remarks>
    /// Off unless DISCWRIGHT_ASK_GITHUB is set, because a test suite that
    /// needs the internet fails on a train and teaches everybody to ignore it.
    /// It is here because every other test in this file is handed an answer,
    /// and nothing proves the asking itself works.
    /// </remarks>
    [Fact]
    public async Task Really_asks_github_when_it_is_told_to()
    {
        if (Environment.GetEnvironmentVariable("DISCWRIGHT_ASK_GITHUB") is not "yes")
        {
            return;
        }

        using DiscWright.Platform.WhatGitHubHasReleased asking = new();

        WhatWasReleased said = await asking.TheLatest(CancellationToken.None);

        Assert.Null(said.WhyNot);
        Assert.NotNull(said.Tag);
        Assert.NotNull(said.Where);

        // It has to be something the comparison can actually read, which is
        // the half that a written answer in a test cannot prove.
        Assert.NotNull(HowThisBuildCompares.Reading(said.Tag));
        Assert.StartsWith("https://github.com/", said.Where, StringComparison.Ordinal);
    }

    private static MakeADisc TheScreen(IAskWhatIsReleased asker) =>
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
            asker,
            new NoPreview(),
            new PicturesThatRead(),
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience));

    private sealed class ARecordingAsker(WhatWasReleased said) : IAskWhatIsReleased
    {
        internal int Asked { get; private set; }

        public Task<WhatWasReleased> TheLatest(CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(said);
        }
    }

    private sealed class AnAskerThatThrows : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            throw new InvalidOperationException("the internet fell over");
    }

    /// <summary>One that does not answer until it is told to.</summary>
    private sealed class AnAskerThatWaits : IAskWhatIsReleased
    {
        internal TaskCompletionSource Started { get; } = new();

        internal TaskCompletionSource<WhatWasReleased> Answer { get; } = new();

        public Task<WhatWasReleased> TheLatest(CancellationToken ct)
        {
            Started.TrySetResult();

            return Answer.Task;
        }
    }

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
    }

    private sealed class ABuilderThatDoesNothing : DiscWright.Staging.IBuildDiscs
    {
        public Task<Outcome<DiscWright.Staging.BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<DiscWright.Core.Staging.BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<DiscWright.Staging.BuiltDiscs>>(new DiscWright.Staging.BuiltDiscs([]));
    }

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
    }

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(new BurnedDisc(onto.Drive, 0, 0, true));
    }

    private sealed class AnEraserThatDoesNothing : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>("blank");
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

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
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
    public void This_build_says_a_version_of_its_own()
    {
        string props = File.ReadAllText(Repository.File("Directory.Build.props"));
        Match said = Regex.Match(props, @"<Version>([^<]+)</Version>");

        Assert.True(said.Success, "Directory.Build.props sets no version at all.");

        string version = said.Groups[1].Value;

        // With none set, .NET stamps 1.0.0 on everything, and a program that
        // cannot say which version it is cannot tell whether a newer one
        // exists. That is the failure worth guarding, not the number itself:
        // the number changes every release and a test asserting it would be
        // churn rather than a guard.
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
        Assert.NotEqual("1.0.0", version);
    }
}
