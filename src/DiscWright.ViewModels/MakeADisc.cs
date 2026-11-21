using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Staging;

namespace DiscWright.ViewModels;

/// <summary>
/// Everything the main window knows and can do, with no window in it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>There is no WPF in this project at all.</strong> That is the point:
/// "what happens when you pick a folder with no game in it" is a test that runs
/// in milliseconds with no window, no desktop and no machine, and what is left
/// for an automated window test to do is only the clicking.
/// </para>
/// <para>
/// It is also what makes the choice of window framework cheap to change. If WPF
/// turns out to be wrong, what gets thrown away is the markup; none of this
/// moves.
/// </para>
/// </remarks>
public sealed class MakeADisc : INotifyPropertyChanged
{
    private readonly TheMachine _machine;

    /// <summary>Held while a build starts or stops, and while a report lands.</summary>
    private readonly Lock _oneAtATime = new();
    private readonly ObservableCollection<GameFolder> _games = [];

    // What was found, before anything was turned off, and the tree of ticks
    // over it. _games holds what will actually be written, which is these
    // trimmed to whatever is still ticked.
    private readonly List<GameFolder> _asFound = [];
    private readonly List<AddOn> _addOnsAsFound = [];
    private readonly ObservableCollection<AThingOnTheDisc> _tree = [];
    private ReadOnlyObservableCollection<AThingOnTheDisc>? _treeView;
    private readonly List<AddOn> _addOns = [];
    private readonly List<string> _loose = [];

    // Carried as well as chosen. The window picks these now, in
    // WhatElseGoesOnIt, and a disc opened from a project file arrives with
    // them already set; either way they are kept rather than dropped, because
    // rebuilding a disc without the cover somebody picked is the one outcome
    // nobody wants.
    private DiscArtwork? _artwork;
    private string? _manual;
    private string? _extrasFolder;
    private string? _music;

    /// <summary>
    /// The colour box's raw text, kept apart from the artwork so the box
    /// never fights the person typing in it.
    /// </summary>
    private string _backgroundColourText = string.Empty;

    /// <summary>
    /// The menu's heading as it sits in its box, spaces and all, so a space
    /// typed between two words is still there when the second word arrives.
    /// </summary>
    private string _menuHeadingText = string.Empty;

    private string _label = string.Empty;
    private string _outputFolder = string.Empty;

    /// <summary>
    /// Whether the output folder is one this program suggested from a game's
    /// name, rather than one a person chose or a project file carried.
    /// </summary>
    /// <remarks>
    /// Starting a new disc keeps a folder somebody chose, because that is how
    /// they work rather than what they are making. A folder suggested from the
    /// game's name is the other thing: it is named for the disc that was just
    /// cleared, and kept, the next game built into "Hollow Knight Disc",
    /// emptied that disc's staging folder, wrote over its project file and
    /// left two images for Burn to ask about.
    /// </remarks>
    private bool _theFolderWasSuggested;
    private Medium _medium = Media.Dvd5;

    /// <summary>
    /// The smallest larger disc the last plan would have fitted on, settled
    /// with the plan and read through <see cref="TheLargerDiscThatWouldDo"/>.
    /// </summary>
    private Medium? _theLargerDisc;
    private bool _theSizeWasChosen;

    /// <summary>
    /// Whether the size follows what is on the window, smallest that holds
    /// it, rather than being picked. See <see cref="ChooseTheSizeForMe"/>.
    /// </summary>
    private bool _theSizeFollowsWhatIsOnIt;

    /// <summary>Whether, the last time it was worked out, some size held it all on one disc.</summary>
    private bool _someSizeHoldsItAll = true;

    // The blank discs somebody has, and what was chosen out of them the
    // last time a size was chosen, or nothing when none of them would do.
    // How many discs what is on the window takes at the size chosen, or
    // none while there is no plan to say.
    private int _discsItWillTake;

    // What building what is on the window would make, or nothing while
    // there is no plan to say.
    private PlannedBuild? _whatItWouldMake;

    private BlanksOwned _blanks;
    private AChoiceFromTheBlanks? _fromTheBlanks;
    private bool _lookingAtTheDrives;

    /// <summary>Whether the last look worked out a set of discs at all.</summary>
    /// <remarks>
    /// Kept rather than asked for again, because working it out is the
    /// expensive half of Reconsider and this is read every time the window
    /// redraws a button.
    /// </remarks>
    private bool _thePlanIsSound;
    private Recorder? _theDrive;
    private bool _asking;
    private TheUpdate? _update;
    private Updating _updating;
    private long _downloaded;
    private string? _downloadedInstaller;
    private string? _updateWentWrong;
    private IReadOnlyList<Recorder> _lastSeen = [];

    /// <summary>
    /// What each drive said about shutting its own tray, by letter.
    /// </summary>
    /// <remarks>
    /// Remembered because the answer costs a call to the drive and belongs
    /// nowhere near the thread the window is drawn on, and because it is
    /// about the drive rather than about the disc in it: D: shuts its own
    /// tray and the Blu-ray writer here never does, whatever is loaded.
    /// Keeping it means choosing a different drive can narrow the warning
    /// straight away instead of waiting for the next disc to go in.
    /// </remarks>
    private Dictionary<string, bool> _traysThatShut = new(StringComparer.OrdinalIgnoreCase);
    private DiscOptions _options = new();
    private ReadOnlyObservableCollection<GameFolder>? _view;

    /// <summary>
    /// Finding a drive with a disc in it that will take what is to be written.
    /// </summary>
    /// <remarks>
    /// A class of its own, because every way into it ran through a burn and a
    /// mutation pass found branches of it that nothing reached at all. It
    /// still reports through <see cref="Say"/>, so what somebody watching the
    /// window sees is what they saw before.
    /// </remarks>
    private readonly SettlingOnADrive _settling;

    /// <summary>
    /// Reading a disc back against the right thing, once it is written.
    /// </summary>
    /// <remarks>
    /// Out here for the same reason as the settling: the only way in was to
    /// burn a disc, and the choice it makes was written twice in this file.
    /// </remarks>
    private readonly ReadingBackWhatWasWritten _reading;

    /// <summary>
    /// Waiting for a written disc to be read back in once its tray is shut,
    /// and saying so while it takes.
    /// </summary>
    private readonly WaitingForTheDiscToComeBack _comingBack;

    public MakeADisc(TheMachine machine)
    {
        _machine = machine;
        _blanks = BlanksOwned.FromText(machine.Blanks?.Recalled());
        TheBlanks =
        [
            .. Media.All.Select(medium => new ABlankCount(medium, _blanks.Of(medium), ThereAreNow))
        ];
        _settling = new SettlingOnADrive(machine.Drives, machine.Eraser, Say, machine.Patience);
        _reading = new ReadingBackWhatWasWritten(machine.Disk, machine.Hasher, Say);
        _comingBack = new WaitingForTheDiscToComeBack(
            machine.Tray,
            (machine.Patience ?? HowLongADriveGets.WhatARealOneNeeds) with
            {
                AtMost = WaitingForTheDiscToComeBack.AQuarterOfAnHour
            });
        Reconsider();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The games, in the order they were added.</summary>
    /// <remarks>
    /// A collection that says when it changes, rather than a list that does
    /// not. Saying the whole property changed is not enough: the answer is the
    /// same object every time, so a list bound to it has no way to tell that
    /// what is inside it is different, and shows what it showed before. The
    /// window built the right disc while showing an empty list until this was
    /// found by driving a real one.
    /// </remarks>
    public ReadOnlyObservableCollection<GameFolder> Games => _view ??= new(_games);

    /// <summary>
    /// What goes on the disc, as a tree that can be edited: a game, the
    /// folders inside it, its files and its add-ons, each with a tick.
    /// </summary>
    /// <remarks>
    /// The tree holds everything that was found and never forgets a row that
    /// was turned off, because turning it back on has to put the same file
    /// back. <see cref="Games"/> is what will be written, read off the tree
    /// whenever a tick changes.
    /// </remarks>
    public ReadOnlyObservableCollection<AThingOnTheDisc> WhatGoesOnIt => _treeView ??= new(_tree);

    /// <summary>What This PC will show when the disc goes in.</summary>
    public string Label
    {
        get => _label;
        set => Change(ref _label, value);
    }

    /// <summary>
    /// Whether the disc has no name, spaces being none. xniwo, discwright#98,
    /// 15 November: a name of one space was warned about as a name Windows
    /// would mangle, and then built, because only an empty box counted as no
    /// name at all.
    /// </summary>
    private bool HasNoName => string.IsNullOrWhiteSpace(Label);

    /// <summary>Where the discs get written.</summary>
    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            // Set from outside, so somebody chose it. Add puts the flag back
            // when it was this program's suggestion.
            _theFolderWasSuggested = false;
            Change(ref _outputFolder, value);
            LookForImages();

            // Saving lands the recipe here, so whether there is anywhere to
            // save follows this box.
            Tell(nameof(CanSaveItForLater));
        }
    }

    /// <summary>
    /// The size of disc being aimed at.
    /// </summary>
    /// <remarks>
    /// Setting it counts as somebody choosing, and from then on the drives stop
    /// deciding it. Opening a project file counts too: the size it was saved
    /// with was somebody's answer once, and this program does not know better
    /// than the file.
    /// </remarks>
    public Medium Medium
    {
        get => _medium;
        set
        {
            _theSizeWasChosen = true;
            Change(ref _medium, value);

            // The row of the list under it moves as well, and a dropdown only
            // redraws for a property it was told about.
            Tell(nameof(TheSizeChosen));
            SayWhatIsInTheDrives();
        }
    }

    /// <summary>
    /// What the discs in the drives have to say about that size, or nothing
    /// when they have nothing to say.
    /// </summary>
    public string AboutTheDiscInTheDrive { get; private set; } = string.Empty;

    /// <summary>
    /// Whether there is anything to preview.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A preview is a menu written for every disc of the set, so it needs a
    /// set. With a file too big for the disc being aimed at there is no set
    /// and nothing to write, and the button used to stay lit and answer with
    /// a box saying so, word for word what the red line under the buttons
    /// had been saying all along.
    /// </para>
    /// <para>
    /// Build is already grey in that state and always has been, so the box
    /// could only ever come from here. Grey is the answer the rest of this
    /// window gives, and the red line is where the reason lives.
    /// </para>
    /// <para>
    /// Somewhere to put the ISO is not part of it. A preview is written to a
    /// temporary folder of its own and never goes near that one, so it is
    /// the plan this waits on rather than the whole of CanMakeIt.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// There is nothing here about there being a game, because a plan with no
    /// games in it is not a sound plan: Reconsider says so in the one place
    /// that decides it. Saying it twice is two things to keep in step, and a
    /// mutation pass can prove the second one cannot be wrong, which is how
    /// this came to be one condition shorter.
    /// </para>
    /// <para>
    /// Somewhere to put the discs is deliberately not in it either. A preview
    /// writes into a temporary folder of its own and has never needed that
    /// one.
    /// </para>
    /// </remarks>
    public bool CanPreview => !IsWorking && _thePlanIsSound;

    /// <summary>
    /// What to call the folder this project's preview goes in.
    /// </summary>
    /// <remarks>
    /// One per project, so previewing the same thing six times leaves one
    /// folder rather than six. The name is worked out here because this is
    /// what knows the label, where it builds to and what is on it.
    /// </remarks>
    public string WhereAPreviewWouldGo =>
        WhereAPreviewGoes.For(Label, OutputFolder, _games.Count > 0 ? _games[0].Path : null);

    /// <summary>
    /// The menus the last preview wrote, in the order the discs would be
    /// burned, or nothing at all.
    /// </summary>
    /// <remarks>
    /// All of them, not the first, so the window can open one or every one,
    /// as the person answers. Every disc carries the manual and the extras
    /// since 2026-11-10, so disc 1 alone shows their buttons too.
    /// </remarks>
    public IReadOnlyList<string> ThePreviews { get; private set; } = [];

    /// <summary>
    /// Writes the menu this project would get, for somebody to look at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Before anything is built</strong>, which is the whole point:
    /// seeing the menu after burning a disc is seeing it too late. So the
    /// discs are planned exactly as building would plan them, and every one of
    /// their menus is written out.
    /// </para>
    /// <para>
    /// It goes somewhere temporary. A preview is not an output and has no
    /// business sitting in the folder somebody keeps their discs in.
    /// </para>
    /// </remarks>
    public void PreviewTheMenu(string into)
    {
        if (!CanPreview)
        {
            return;
        }

        ThePreviews = [];
        ItWentWrong = AChosenFileIsGone();

        if (ItWentWrong is not null)
        {
            Tell(nameof(ThePreviews));
            Tell(nameof(ItWentWrong));
            return;
        }

        try
        {
            switch (_machine.Preview.Write(AsAProject(), Medium, _machine.HowBigIs, into))
            {
                case Outcome<IReadOnlyList<string>>.Ok(IReadOnlyList<string> where):
                    ThePreviews = where;
                    break;

                case Outcome<IReadOnlyList<string>>.No refused:
                    ItWentWrong = refused.Refusal;
                    break;
            }
        }
        catch (Exception nobodyExpected) when (nobodyExpected is not OperationCanceledException)
        {
            // A preview writes real files: the artwork and the music are
            // copied, and either of them can have been moved or renamed since
            // somebody chose it. Found by previewing a project whose tune was
            // not where it said: the FileNotFoundException went straight out
            // of here, past a window that catches nothing around this, and
            // took the program down with a stack trace in a box.
            ItWentWrong = Refusals.SomethingNobodyExpected(
                "writing the preview", nobodyExpected.Message);
        }

        Tell(nameof(ThePreviews));
        Tell(nameof(ItWentWrong));
    }

    /// <summary>
    /// Which version this is, for the corner of the window.
    /// </summary>
    /// <remarks>
    /// Shown without anybody asking, because "which one am I running" is a
    /// question somebody should not have to press a button to answer.
    /// </remarks>
    /// <summary>
    /// What a preview that wrote so many menus asks before it opens any of
    /// them, or nothing when there is one.
    /// </summary>
    public AQuestion? WhatThePreviewAsks(int menus) => WhatAPreviewAsks.For(_whatItWouldMake, menus);

    public string ThisBuild => _machine.ThisBuild;

    /// <summary>
    /// How this build compares with the newest released, once somebody has
    /// asked, or nothing because nobody has.
    /// </summary>
    public HowThisBuildCompares? AboutTheVersion { get; private set; }

    /// <summary>
    /// What the header says about this build, once somebody has asked, or
    /// nothing at all because nobody has.
    /// </summary>
    /// <remarks>
    /// The answer belongs where the question was asked. It used to arrive in a
    /// message box, which made it the only box in the program that was not
    /// reporting a problem, and every other field in this window says what it
    /// has to say in a quiet line under itself.
    /// </remarks>
    public string WhatTheVersionSays =>
        _update is TheUpdate update ? AboutTheUpdate(update) : AboutTheVersion?.InWords ?? string.Empty;

    /// <summary>
    /// What the one pressable thing on the version line is called.
    /// </summary>
    /// <remarks>
    /// Never empty, because that line is the only way in and a link that
    /// disappears once it has been used is a capability that works once.
    /// While it is asking it says so: the ask crosses a network, it can take a
    /// second or two, and a link that looks untouched after a press reads as
    /// broken.
    /// </remarks>
    public string WhatTheVersionLinkSays =>
        _machine.StorePage is not null ? "See it in the Store"
        : _asking ? "Asking GitHub"
            : _update is TheUpdate update
                ? _updating switch
                {
                    Updating.Downloading or Updating.Checking or Updating.Closing => "Updating",
                    Updating.Waiting => $"Install {update.Version}",
                    _ => $"Update to {update.Version}",
                }
            : AboutTheVersion is null ? "Check for new version"
            : AboutTheVersion.WorthGetting ? "Get it"
            : "Check again";

    /// <summary>
    /// The page for a newer release, when there is one worth getting, and
    /// nothing otherwise.
    /// </summary>
    /// <remarks>
    /// Whether a page is worth offering is a judgement, so it is made here
    /// rather than in the window. The window asks for a page and either gets
    /// one or asks again, and never sees <see cref="HowThisBuildCompares"/>.
    /// A copy from the Microsoft Store always has one, its Store page, which
    /// is where its updates come from.
    /// </remarks>
    public string? TheVersionPage =>
        _machine.StorePage
        ?? (AboutTheVersion is { WorthGetting: true } how ? how.Where : null);

    /// <summary>
    /// Whether the version link updates this copy when pressed, rather than
    /// opening a page or asking again.
    /// </summary>
    /// <remarks>
    /// True once a newer release has been found that publishes an installer
    /// with its checksum, and only on a copy the installer put here. The
    /// release page is then a second link of its own, What's new, so the page
    /// is never lost to the button.
    /// </remarks>
    public bool TheUpdateOnOffer => _update is not null;

    /// <summary>
    /// Asks what the newest released version is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Only when somebody asks.</strong> Checking on every start is a
    /// network call nobody made, from a program whose whole point is putting
    /// things on a disc so they keep working with no network at all.
    /// </para>
    /// <para>
    /// It never acts on the answer. Saying a newer one exists is the whole of
    /// it, and offering Update when this copy can take one. Fetching it waits
    /// for a second press, <see cref="UpdateThisCopy"/>.
    /// </para>
    /// </remarks>
    public async Task CheckForANewVersion(CancellationToken ct = default)
    {
        // A copy from the Store is updated by the Store, and asking GitHub
        // about it would be a network call that could only ever say
        // something the Store already decides.
        if (_machine.StorePage is not null || _asking || _updating != Updating.NotYet)
        {
            return;
        }

        _asking = true;
        TellAboutTheVersion();

        try
        {
            WhatWasReleased said = await _machine.Releases.TheLatest(ct);

            AboutTheVersion = new HowThisBuildCompares(
                _machine.ThisBuild, said.Tag, said.Where, said.WhyNot);

            _update = _machine.Updater is { CanUpdateItself: true }
                ? TheUpdate.Offered(AboutTheVersion, said.Files)
                : null;
        }
        catch (Exception wrong) when (wrong is not OperationCanceledException)
        {
            // The port is meant to report rather than throw, and a button that
            // takes the program down because a web request misbehaved would be
            // a poor trade for a version number.
            AboutTheVersion = new HowThisBuildCompares(
                _machine.ThisBuild, null, null, "Something went wrong asking.");
            _update = null;
        }
        finally
        {
            _asking = false;
        }

        _updateWentWrong = null;
        TellAboutTheVersion();
    }

    /// <summary>
    /// Downloads the newer installer, checks it against the release's own
    /// checksum, and runs it, closing this copy so it can be replaced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Only when somebody presses Update</strong>, after asking found
    /// one. Nothing about it starts by itself.
    /// </para>
    /// <para>
    /// <strong>Never while something is running.</strong> Updating closes the
    /// program, and closing it during a burn ruins the disc. Pressed during a
    /// build, a burn or an erase it says so and does nothing. If one starts
    /// while the download is on its way, the checked installer waits, and the
    /// link says Install until it is pressed again with nothing running.
    /// </para>
    /// <para>
    /// <strong>A download that does not match is never run.</strong> It is
    /// thrown away and the line says why, with the release page still a link
    /// away. So is one whose checksums do not list it, and one that did not
    /// finish.
    /// </para>
    /// </remarks>
    public async Task UpdateThisCopy(CancellationToken ct = default)
    {
        if (_update is not TheUpdate update
            || _machine.Updater is not IUpdateThisCopy updater
            || _updating is Updating.Downloading or Updating.Checking or Updating.Closing)
        {
            return;
        }

        if (_updating == Updating.Waiting && _downloadedInstaller is string ready)
        {
            InstallOnceNothingRuns(updater, ready);
            return;
        }

        if (IsWorking)
        {
            _updateWentWrong = "Something is running. Updating closes DiscWright, so press "
                + $"Update to {update.Version} once it is done.";
            TellAboutTheVersion();
            return;
        }

        _updateWentWrong = null;
        _downloaded = 0;
        _updating = Updating.Downloading;
        TellAboutTheVersion();

        string installer = updater.WhereTheDownloadGoes(update.Installer.Name);
        string? why;

        try
        {
            why = await Fetch(update, updater, installer, ct);
        }
        catch (Exception wrong) when (wrong is not OperationCanceledException)
        {
            why = "The download did not finish. Try again, or get it from What's new.";
        }

        if (why is not null)
        {
            try
            {
                updater.ThrowAway(installer);
            }
            catch (Exception wrong) when (wrong is IOException or UnauthorizedAccessException)
            {
                // A file that will not delete in the temporary folder is
                // Windows' to tidy. It is never run, which is what matters.
            }

            _updating = Updating.NotYet;
            _updateWentWrong = why;
            TellAboutTheVersion();
            return;
        }

        _downloadedInstaller = installer;
        InstallOnceNothingRuns(updater, installer);
    }

    /// <summary>
    /// The checksums, then the installer, then whether the two agree, or why
    /// it cannot go on.
    /// </summary>
    private async Task<string?> Fetch(TheUpdate update, IUpdateThisCopy updater, string installer, CancellationToken ct)
    {
        string sums = await updater.Read(update.Sums.Url, ct);

        if (TheUpdate.TheHashIn(sums, update.Installer.Name) is not string expected)
        {
            return "The release's checksums do not list its installer, so nothing was downloaded. "
                + "Get it from What's new instead.";
        }

        await updater.Download(
            update.Installer.Url,
            installer,
            new Progress<long>(bytes =>
            {
                _downloaded = bytes;
                Tell(nameof(WhatTheVersionSays));
            }),
            ct);

        _updating = Updating.Checking;
        TellAboutTheVersion();

        string hashed = await Task.Run(() => _machine.Hasher.Sha256(installer, ct), ct);

        return TheUpdate.Matches(expected, hashed)
            ? null
            : "The download did not match the checksum the release publishes, so it was thrown away "
                + "and nothing ran. Try again, or get it from What's new.";
    }

    private void InstallOnceNothingRuns(IUpdateThisCopy updater, string installer)
    {
        if (IsWorking)
        {
            _updating = Updating.Waiting;
            TellAboutTheVersion();
            return;
        }

        _updating = Updating.Closing;
        TellAboutTheVersion();

        try
        {
            updater.InstallAndClose(installer);
        }
        catch (Exception wrong) when (wrong is not OperationCanceledException)
        {
            _updating = Updating.NotYet;
            _downloadedInstaller = null;
            _updateWentWrong = $"The installer would not start, so this is still {_machine.ThisBuild}. "
                + "Try again, or get it from What's new.";
            TellAboutTheVersion();
        }
    }

    /// <summary>What the line says once an update is on offer.</summary>
    private string AboutTheUpdate(TheUpdate update) => _updating switch
    {
        Updating.Downloading => $"Downloading DiscWright {update.Version}, {TheDownloadPercent(update)}%.",
        Updating.Checking => "Checking the download against the checksum the release publishes.",
        Updating.Waiting => $"DiscWright {update.Version} is downloaded and checked. Updating closes "
            + $"DiscWright, so it waits until nothing is running. Press Install {update.Version} then.",
        Updating.Closing => $"DiscWright closes now, updates to {update.Version} and opens again by itself.",
        _ => _updateWentWrong ?? AboutTheVersion?.InWords ?? string.Empty,
    };

    private int TheDownloadPercent(TheUpdate update) =>
        update.Installer.Size > 0
            ? (int)Math.Clamp(_downloaded * 100 / update.Installer.Size, 0, 100)
            : 0;

    /// <summary>Everything on the version line, which changes together or not at all.</summary>
    private void TellAboutTheVersion()
    {
        Tell(nameof(AboutTheVersion));
        Tell(nameof(WhatTheVersionSays));
        Tell(nameof(WhatTheVersionLinkSays));
        Tell(nameof(TheVersionPage));
        Tell(nameof(TheUpdateOnOffer));
    }

    /// <summary>Where an update is, between being offered and the program closing for it.</summary>
    private enum Updating
    {
        NotYet,
        Downloading,
        Checking,
        Waiting,
        Closing,
    }

    /// <summary>
    /// Every writer on the machine, as of the last look.
    /// </summary>
    /// <remarks>
    /// For choosing one BEFORE pressing burn. Being asked in the middle of a
    /// burn is being asked at the worst moment: the question arrives after
    /// somebody has walked away, and on a machine with two writers the answer
    /// decides whether the run needs a hand at all.
    /// </remarks>
    public IReadOnlyList<Recorder> Drives { get; private set; } = [];

    /// <summary>
    /// The one to write to, or nothing to let it work that out.
    /// </summary>
    /// <remarks>
    /// Nothing is the right default. A machine with one writer has no question
    /// to answer, and asking somebody to pick the only drive they own is a
    /// decision shaped like a chore.
    /// </remarks>
    public Recorder? TheDrive
    {
        get => _theDrive;
        set
        {
            _theDrive = value;
            Tell(nameof(TheDrive));
            SayWhatTheTraysNeed();

            // The sentence under the size is about the drive being written
            // to, so choosing a different drive asks it again. Off what was
            // last seen rather than off the drives themselves: this is the
            // thread the window is drawn on, and asking a drive that has
            // spun down costs seconds of a window that has stopped moving.
            SayWhatIsInTheDrives();
        }
    }

    /// <summary>
    /// What is worth saying about trays, or nothing when no drive needs a hand.
    /// </summary>
    /// <remarks>
    /// Said before anything is written, because the point of saying it is that
    /// somebody can decide whether to stay in the room.
    /// </remarks>
    public string AboutTheTray { get; private set; } = string.Empty;

    /// <summary>
    /// Whether what is in the drives is a different size from the one chosen.
    /// </summary>
    /// <remarks>
    /// For the window to pick an ink with. It is a worry and never a refusal:
    /// somebody planning a DVD with last week's CD still in the drive is doing
    /// something perfectly ordinary, and the disc gets built for what was asked
    /// for either way.
    /// </remarks>
    public bool TheDiscIsADifferentSize { get; private set; }

    /// <summary>
    /// Looks at what is in the drives, and takes the size from it while nobody
    /// has chosen one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called when the window opens and whenever a disc goes in or comes out,
    /// and nowhere else. Asking a drive is not free, and asking one on every
    /// keystroke would spin a disc up to answer a question nobody asked.
    /// </para>
    /// <para>
    /// <strong>Off the thread the window is drawn on.</strong> Asking two
    /// drives with discs in them took thirteen seconds here, because a drive
    /// that has spun down has to spin up to answer, and done in front of the
    /// window that is thirteen seconds of nothing on screen at all. It was
    /// exactly that on the first go at this.
    /// </para>
    /// <para>
    /// <strong>It never overrules a person.</strong> The default was DVD5 for
    /// everybody always, which is how a 700 MB disc in the tray came to be
    /// planned as one 4.7 GB disc, so now it starts from what is actually
    /// there. The moment somebody picks a size themselves, this stops moving
    /// it and only comments.
    /// </para>
    /// </remarks>
    public async Task LookAtTheDrives()
    {
        if (_lookingAtTheDrives)
        {
            return;
        }

        // NEVER WHILE A DRIVE IS AT WORK. Windows says a device changed every
        // time a tray opens or a disc goes in, a burn's own included, and
        // answering it asked every drive what it held while one of them was
        // writing. The burning drive answered "this drive cannot write data
        // discs" in the log of the owner's set on 2026-11-10, a second into
        // writing disc 2, and two programs asking one busy drive is how the
        // window froze that night. Remembered instead, and looked at once the
        // work is done.
        if (IsWorking)
        {
            _aDiscMovedMeanwhile = true;
            return;
        }

        _lookingAtTheDrives = true;

        IReadOnlyList<Recorder> drives;
        Dictionary<string, bool> shut;

        try
        {
            // The trays are asked about out here too. Each answer costs a
            // call to a drive, and doing it in front of the window is how the
            // window came to take thirteen seconds to open.
            //
            // Every drive worth asking, not only the chosen one. Narrowing
            // happens afterwards, where it is free, and asking about all of
            // them is what lets choosing a different drive narrow to it
            // without going near a drive again.
            (drives, shut) = await Task.Run(() =>
            {
                IReadOnlyList<Recorder> found = _machine.Drives.All();

                Dictionary<string, bool> said = new(StringComparer.OrdinalIgnoreCase);

                foreach (Recorder drive in found.Where(each => each.Ready || each.ErasingWouldHelp))
                {
                    said[drive.Drive] = _machine.Tray.CanShutItsOwnTray(drive);
                }

                return (found, said);
            });
        }
        catch (Exception)
        {
            // A drive that will not answer is not a reason to fail: the size
            // simply stays whatever it was, which is where it started anyway.
            return;
        }
        finally
        {
            _lookingAtTheDrives = false;
        }

        // What is on the window decides the size when that was asked for,
        // and then a blank going into the tray is not a reason to move it.
        if (WhatTheDrivesMean.TheSizeToStartFrom(
                drives, _medium, _theSizeWasChosen || _theSizeFollowsWhatIsOnIt, IsWorking)
            is Medium found)
        {
            // Not through the setter, which would count as a person choosing
            // and stop the drives mattering ever again.
            _medium = found;
            Tell(nameof(Medium));
            Tell(nameof(TheSizeChosen));
        }

        SayWhatIsInTheDrives(drives);

        // Remembered BEFORE the list is replaced, because replacing the list
        // is what loses it. The picker is bound two ways to the drive and to
        // the list, and a fresh look hands it a list of fresh records: a
        // record is a photograph of a drive at a moment, so none of them is
        // equal to the one that was picked, the picker finds its selection
        // gone and writes null straight back into TheDrive. All of that
        // happens inside the announcement below, before anything after it
        // runs, which is how a drive chosen before a burn was forgotten the
        // moment a blank went in. Measured with a window bound exactly this
        // way: the setter was called with null during Tell(Drives) every
        // time the list changed.
        Recorder? chosen = _theDrive;

        Drives = drives;
        Tell(nameof(Drives));

        // Whether there is anything to erase changes with them, and a button
        // nobody tells about is judged once at startup, when there are no
        // drives yet, and stays dead for the rest of the run.
        Tell(nameof(CanErase));
        Tell(nameof(WhatTheDrivesAreDoing));

        if (chosen is not null)
        {
            // Through the setter, after the list, so that the choice is put
            // back over whatever the picker did to it. The same drive again
            // when it is still there, which is a fresher photograph of the
            // drive somebody picked, and nothing when it has gone, because
            // losing the drive changes the tray warning and the line about
            // the disc in it, and the setter is what says all of that.
            TheDrive = WhatTheDrivesMean.TheSameDriveAgain(chosen, drives);
        }

        _traysThatShut = shut;
        SayWhatTheTraysNeed();

        // Last, and the whole point of the ones above it. A disc swapped for
        // a bigger one moves the size this is aiming at, and the size decides
        // how many discs it will take, whether anything has to be cut and
        // whether any of it fits at all. None of that was worked out again,
        // so the window went on showing the answer to the question the
        // previous disc had asked: "a set of 3 discs" over a drive now
        // holding a DVD, and a refusal about a file too big for a disc it is
        // no longer being written to.
        //
        // It is here rather than in the window because every path into this
        // method is a drive changing, and a drive changing is exactly when
        // all of it stops being true.
        Reconsider();
    }

    /// <summary>
    /// Works out the tray warning for the drive this would be written to.
    /// </summary>
    /// <remarks>
    /// Off what the drives last said rather than off the drives, so it can
    /// run on the thread the window is drawn on. Only drives that were asked
    /// are in the question at all, so there is no drive to guess about: a
    /// default either way would be a rule nothing could reach, and one of
    /// them would have somebody standing by a drive that never wanted them.
    /// </remarks>
    private void SayWhatTheTraysNeed()
    {
        IReadOnlyList<Recorder> asking = WhatTheDrivesMean.TheOnesToAskAboutTrays(
            _lastSeen, _traysThatShut, _theDrive?.Drive);

        AboutTheTray = TheDiscsThatAreIn.WhatToSayAboutTheTray(
            asking, drive => _traysThatShut[drive.Drive]) ?? string.Empty;

        Tell(nameof(AboutTheTray));
        Tell(nameof(WhatTheDrivesAreDoing));
    }

    private void SayWhatIsInTheDrives(IReadOnlyList<Recorder>? drives = null)
    {
        string said = TheDiscsThatAreIn.WhatIsWorthSaying(
            drives ?? _lastSeen, _medium, _theDrive?.Drive) ?? string.Empty;

        if (drives is not null)
        {
            _lastSeen = drives;
        }

        AboutTheDiscInTheDrive = said;
        TheDiscIsADifferentSize = said.Length > 0;

        Tell(nameof(AboutTheDiscInTheDrive));
        Tell(nameof(TheDiscIsADifferentSize));
    }

    /// <summary>Everything that has an answer already.</summary>
    public DiscOptions Options
    {
        get => _options;
        set => Change(ref _options, value);
    }

    /// <summary>
    /// What pressing the button would produce: "one disc", "a set of 3 discs",
    /// "4 games, a disc each". Empty when it would produce nothing.
    /// </summary>
    public string WhatItWillMake { get; private set; } = string.Empty;

    /// <summary>
    /// What splitting a file is going to do to this project, or nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The box used to say "Split a file across two discs", which is a promise
    /// about two. The planner has never worked that way: it cuts a file into
    /// however many pieces the file and the chosen disc come to between them,
    /// and a DVD image on CDs is seven of them.
    /// </para>
    /// <para>
    /// So the box says what it does and this says what it will do, here, with
    /// these files and this size of disc. It is worked out from the same plan
    /// the disc count comes from, so the two cannot disagree.
    /// </para>
    /// <para>
    /// It also says when the disc chosen means more than one, ticked or not,
    /// because that is where the size was chosen; see
    /// <see cref="PlannedBuild.WhatCuttingComesTo"/>.
    /// </para>
    /// </remarks>
    public string WhatSplittingWouldDo { get; private set; } = string.Empty;

    /// <summary>
    /// Which games land on which disc, when several share a set, and that
    /// they go on in the order of the list. Empty for anything else.
    /// </summary>
    /// <remarks>
    /// Said before anything is built. Which games share a disc is the
    /// person's to decide, and the list is how they decide it: this is what
    /// the list comes to, and moving a game changes it.
    /// </remarks>
    public string WhereEachGameGoes { get; private set; } = string.Empty;

    /// <summary>
    /// Whether "Give each game a disc of its own" has anything to decide:
    /// several games, and more than one disc between them. Shown too while
    /// it is ticked or the plan is refused, so there is always a way back
    /// out of it.
    /// </summary>
    public bool ADiscForEachGameIsAChoice { get; private set; }

    /// <summary>
    /// Why the button is dead, or null when it is not.
    /// </summary>
    /// <remarks>
    /// Worked out as things are chosen rather than when the button is pressed.
    /// A refusal that arrives after a click is a refusal somebody has already
    /// committed to, and the same refusal shown beforehand is just a sentence.
    /// </remarks>
    public Refusal? WhyNot { get; private set; }

    /// <summary>
    /// Something true and worth saying that does not stop anything.
    /// </summary>
    /// <remarks>
    /// Today that is only the disc's name: AutoRun has no Unicode at all, so a
    /// name it cannot carry arrives in This PC as question marks. The disc
    /// still works, still installs, and somebody may genuinely not mind. What
    /// they must not do is find out afterwards.
    /// </remarks>
    public Refusal? Worry { get; private set; }

    /// <summary>Whether there is anything to press the button for.</summary>
    /// <remarks>
    /// An empty window says nothing, because somebody who has not added a game
    /// yet has done nothing wrong. The button still has to be dead: saying
    /// nothing is not the same as being ready, and this told them it was ready
    /// until a real window was driven with nothing on the list.
    /// </remarks>
    /// <remarks>
    /// Nothing here about somewhere to put it, because with nowhere to put it
    /// WhyNot already says so, in words somebody can read and with the remedy
    /// attached. The games are still counted: an empty window has no refusal
    /// on purpose, since somebody who has not added a game yet has not done
    /// anything wrong.
    /// </remarks>
    public bool CanMakeIt => WhyNot is null && Games.Count > 0 && !IsWorking;

    /// <summary>
    /// What the window can do about the refusal it is showing, or nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every refusal in this program has carried a remedy since the day they
    /// were written, and not one of them was ever shown. So a refusal that
    /// was perfectly correct, said what was wrong, and knew exactly how to
    /// put it right, arrived as a red line and nothing else, and read as the
    /// program complaining. "There is nowhere to put it" was reported twice
    /// as a stale message while the empty box it was about sat on the same
    /// screen.
    /// </para>
    /// <para>
    /// Only the kinds the window can really act on. RemedyKind is written as
    /// what a window can DO rather than why a refusal happened, and a remedy
    /// carries no subject with it: ChangeMedium does not say which size. So
    /// the subject is worked out here, where the plan is: the smallest disc
    /// larger than the one aimed at on which the same disc would plan, which
    /// is <see cref="TheLargerDiscThatWouldDo"/>. With no such size there is
    /// no way out to offer, and a word that did nothing when pressed would be
    /// worse than the red line on its own. ChangeOption still says nothing
    /// about which option, and is not offered.
    /// </para>
    /// </remarks>
    public RemedyKind? TheWayOut => WhyNot?.Remedy?.Kind switch
    {
        RemedyKind.PickFolder => RemedyKind.PickFolder,
        RemedyKind.FocusField => RemedyKind.FocusField,
        RemedyKind.ChangeMedium when _theLargerDisc is not null => RemedyKind.ChangeMedium,
        _ => null
    };

    /// <summary>
    /// What that way out is called, or nothing when there is none. A larger
    /// disc is named, because "use a larger disc" leaves the person to find
    /// out which, and the screen already knows.
    /// </summary>
    public string WhatTheWayOutIsCalled => TheWayOut switch
    {
        null => string.Empty,
        RemedyKind.ChangeMedium => $"Use {_theLargerDisc!.Name} instead",
        _ => WhyNot!.Remedy!.Label
    };

    /// <summary>
    /// The smallest disc larger than the one aimed at on which what is on
    /// the window would plan, or nothing: when nothing is refused, when the
    /// refusal is not about the size, or when no size this program aims at
    /// would do.
    /// </summary>
    /// <remarks>
    /// Worked out when the plan is, rather than every time it is read,
    /// because finding it means planning the disc again for each size up and
    /// the window reads its properties far more often than the list changes.
    /// </remarks>
    public Medium? TheLargerDiscThatWouldDo => _theLargerDisc;

    /// <summary>
    /// Takes the way out a size refusal offers: aims at the larger disc, as a
    /// choice, so the drives stop moving it.
    /// </summary>
    public void UseTheLargerDisc()
    {
        if (TheWayOut is RemedyKind.ChangeMedium && _theLargerDisc is Medium larger)
        {
            Medium = larger;
        }
    }

    /// <summary>
    /// What a worry offers to be put right by, or nothing. A worry carries a
    /// remedy the way a refusal does, and the one that matters says "Rename
    /// the disc" beside the name Windows will really show.
    /// </summary>
    public string WhatTheWorrysWayOutIsCalled =>
        Worry?.Remedy is { Kind: RemedyKind.FocusField } remedy ? remedy.Label : string.Empty;

    /// <summary>
    /// Every size of disc that can be aimed at, worded as the list shows them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The words are worked out here rather than in the window because a
    /// dropdown's open list lives in a visual tree of its own, and a binding
    /// that reaches out of it to ask the screen a question is a classic way to
    /// end up with rows that quietly show nothing.
    /// </para>
    /// <para>
    /// <strong>The rows never change, and that is the point.</strong> They used
    /// to be reworded whenever the option to split a file was ticked, so every
    /// row read "CD-R 700 MB, split across discs" and the plain sizes were gone
    /// from a list whose whole job is to offer sizes. Whether a file gets split
    /// is the checkbox's business, and it is said at the checkbox.
    /// </para>
    /// <para>
    /// Rewording them also handed WPF a list whose rows were all new objects,
    /// which is the standard way to make a dropdown lose its selection: the row
    /// that was chosen is not in the list it is then looked for in. Built once,
    /// that cannot happen.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ASizeToAimAt> DiscSizes { get; } =
        [.. Media.All.Select(each => new ASizeToAimAt(each, each.Name))];

    /// <summary>
    /// Whether the size of disc is chosen for whoever is using this: the
    /// smallest that holds everything on the window on one disc, moving as
    /// games are added and taken off, discwright#98.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tick box under the list rather than a row in it. A row called
    /// "choose for me" would be the one row of a list of sizes that is not a
    /// size, and the list would stop saying which size the disc is.
    /// </para>
    /// <para>
    /// Unticking it keeps the size it had arrived at and counts as somebody
    /// choosing that size, so the drives do not move it afterwards.
    /// </para>
    /// </remarks>
    public bool ChooseTheSizeForMe
    {
        get => _theSizeFollowsWhatIsOnIt;
        set
        {
            if (_theSizeFollowsWhatIsOnIt == value)
            {
                return;
            }

            _theSizeFollowsWhatIsOnIt = value;

            if (!value)
            {
                _theSizeWasChosen = true;
            }

            Tell(nameof(ChooseTheSizeForMe));
            Tell(nameof(TheSizeCanBePicked));
            Reconsider();
        }
    }

    /// <summary>
    /// Whether the list of sizes is live: not during a run, and not while
    /// the size is being chosen for whoever is using this.
    /// </summary>
    public bool TheSizeCanBePicked => NothingIsRunning && !_theSizeFollowsWhatIsOnIt;

    /// <summary>
    /// Why the size is what it is, while it is being chosen, or nothing.
    /// </summary>
    public string WhyThatSize =>
        !_theSizeFollowsWhatIsOnIt || _games.Count == 0
            ? string.Empty
            : _fromTheBlanks is not null
                ? _fromTheBlanks.InWords
                : (_blanks.IsAnything ? "None of the blanks you have holds it. " : string.Empty)
                    + (_someSizeHoldsItAll
                        ? "The smallest that holds all of it on one disc."
                        : "No size holds all of it on one disc, so the largest, which takes the fewest.");

    /// <summary>
    /// How many blank discs of each size somebody has, one small box for
    /// each. discwright#98, the first thing asked for in it.
    /// </summary>
    /// <remarks>
    /// With any of them filled in, "Choose the smallest that fits" chooses
    /// out of these and no others, the one that wastes least, and a disc
    /// burned takes one off. Kept with the program and never in a project.
    /// </remarks>
    public IReadOnlyList<ABlankCount> TheBlanks { get; }

    /// <summary>
    /// How many blanks of the size chosen there are, in words, or nothing
    /// while no blanks have been counted at all.
    /// </summary>
    public string WhatThereIsOfThatSize
    {
        get
        {
            if (!_blanks.IsAnything)
            {
                return string.Empty;
            }

            int have = _blanks.Of(Medium);

            // More discs than there are blanks for. discwright#98, 3
            // October: "a yes or no to continue if you were running out of
            // inventory discs to use to complete a backup". Said here,
            // before anything is pressed, where the count already is.
            if (have > 0 && _discsItWillTake > have)
            {
                return $"This takes {_discsItWillTake} {Medium.Key} and you have {have} counted.";
            }

            return have switch
            {
                0 => $"You have no blank {Medium.Key} counted.",
                _ => $"You have {have} blank {Medium.Key}.",
            };
        }
    }

    /// <summary>A box was typed in: there are now so many of one size.</summary>
    private void ThereAreNow(Medium medium, int count)
    {
        _blanks = _blanks.With(medium, count);
        _machine.Blanks?.Keep(_blanks.AsText());
        Tell(nameof(WhatThereIsOfThatSize));

        // The size may be one chosen out of them.
        Reconsider();
    }

    /// <summary>
    /// A disc was written to, so there is one blank fewer of the size
    /// chosen. Written, whether or not it read back well: a disc that
    /// failed its check is not a blank any more either.
    /// </summary>
    private void ABlankWasUsed()
    {
        if (_blanks.Of(Medium) == 0)
        {
            return;
        }

        _blanks = _blanks.Spending(Medium);
        _machine.Blanks?.Keep(_blanks.AsText());

        foreach (ABlankCount box in TheBlanks)
        {
            box.Show(_blanks.Of(box.Medium));
        }

        Tell(nameof(WhatThereIsOfThatSize));
    }

    /// <summary>
    /// Moves the size to the one that suits what is on the window, when that
    /// was asked for. Never during a run: a set being burned is a set of one
    /// size, whatever is ticked.
    /// </summary>
    private void FollowWhatIsOnIt()
    {
        // Nor with nothing on the window, when there is nothing to choose a
        // size for: every size refuses an empty disc, and the answer to that
        // was the largest there is.
        if (!_theSizeFollowsWhatIsOnIt || IsWorking || _games.Count == 0)
        {
            return;
        }

        // Out of the blanks somebody has, when they have said, and
        // otherwise, or when none of those would do, the smallest there is.
        _fromTheBlanks = _blanks.IsAnything
            ? ChoosingASize.FromTheBlanksOwned(AsAProject(), _machine.HowBigIs, _blanks)
            : null;

        Medium? holdsItAll = ChoosingASize.TheSmallestThatHoldsItOnOneDisc(AsAProject(), _machine.HowBigIs);
        Medium suits = _fromTheBlanks?.Medium ?? holdsItAll ?? Media.All[^1];

        _someSizeHoldsItAll = holdsItAll is not null;

        if (suits != _medium)
        {
            // Not through the setter, which would count as a person choosing.
            _medium = suits;
            Tell(nameof(Medium));
            Tell(nameof(TheSizeChosen));
        }
    }

    /// <summary>
    /// The row of that list which is chosen, for the window to bind to.
    /// </summary>
    /// <remarks>
    /// <see cref="Medium"/> stays the one true answer. This is a view of it,
    /// so that saved project files and everything downstream go on naming a
    /// size rather than a sentence about one.
    /// </remarks>
    public ASizeToAimAt? TheSizeChosen
    {
        get => DiscSizes.FirstOrDefault(each => each.Medium.Key == Medium.Key);
        set
        {
            if (value is not null)
            {
                Medium = value.Medium;
            }
        }
    }

    /// <summary>
    /// Reads the tree back into what will actually be written.
    /// </summary>
    /// <remarks>
    /// Everything downstream reads a game's file list: the size, the
    /// refusals, how many discs it takes, what the builder writes and what
    /// goes in the ISO. So a game carrying fewer files is the whole of
    /// leaving a file off, and nothing else had to learn about it.
    /// </remarks>
    private void TakeTheTicksAsTheAnswer()
    {
        for (int at = 0; at < _asFound.Count && at < _tree.Count; at++)
        {
            _games[at] = _tree[at].AsChosen(_asFound[at]);
        }

        _addOns.Clear();
        for (int at = 0; at < _tree.Count; at++)
        {
            _addOns.AddRange(_tree[at].AddOnsChosen(_addOnsAsFound.Where(addOn => addOn.BelongsTo == at)));
        }

        // Which ends at Told, where those three are announced along with
        // everything else about the list.
        Reconsider();
    }

    /// <summary>
    /// What the whole list comes to, or nothing when the list is empty.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every row of the tree shows its own size and a game shows the sum of
    /// what is under it, and there was nowhere at all that said what the lot
    /// came to. With four games on the window somebody had to add them up by
    /// eye to work out whether it would fit.
    /// </para>
    /// <para>
    /// <strong>It follows the ticks.</strong> What goes on the disc is what is
    /// ticked, and a total that counted rows somebody had just turned off
    /// would be worse than no total at all.
    /// </para>
    /// <para>
    /// The files alone, which is what the tree is a list of. The menu, the
    /// artwork and the extras take their own room on top of this, and what
    /// that comes to is already said where it matters: in the disc count, and
    /// in the refusal when it will not fit.
    /// </para>
    /// </remarks>
    public string WhatIsOnIt => _tree.WhatItAllComesTo();

    /// <summary>
    /// What has been taken off the disc, said plainly, or nothing when the
    /// answer is everything.
    /// </summary>
    /// <remarks>
    /// Leaving the installer off is allowed, because this window refuses by
    /// explaining rather than by greying things out, and somebody making a
    /// disc of just the extras has a reason nobody here can guess. It is said
    /// out loud because a disc that cannot install the game is not what
    /// anybody sets out to make by accident.
    /// </remarks>
    public string? WhatIsLeftOff => _tree.WhatIsTurnedOff();

    /// <summary>
    /// What pressing Build or Burn will go without, said beside those
    /// buttons, or nothing when nothing is left off.
    /// </summary>
    /// <remarks>
    /// The sentence above counts the unticked rows under the list, and the
    /// owner pressed Build not knowing whether the unticked row was going
    /// along. This line is the same fact where the finger is.
    /// </remarks>
    public string? WhatBuildingGoesWithout => _tree.WhatBuildingGoesWithout();

    /// <summary>
    /// The one thing about leaving rows off that is worth a warning colour, or
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Apart from <see cref="WhatIsLeftOff"/> because the two are different
    /// kinds of statement, and a window where everything is amber is a window
    /// where amber means nothing.
    ///
    /// Leaving a file off is not a mistake, it is the thing somebody just
    /// deliberately did, so it is said in ordinary ink. A disc that cannot
    /// install the game it is a disc of is a different matter, and is the
    /// only part of this that changes what the disc can do.
    /// </remarks>
    /// <summary>
    /// A disc carrying a patch for a game that is not on it.
    /// </summary>
    /// <remarks>
    /// Allowed, because somebody may be making a disc of patches on purpose
    /// and this window refuses by explaining rather than by greying things
    /// out. Said out loud, because a patch with nothing to patch is the kind
    /// of disc somebody makes by accident far more often than on purpose.
    /// </remarks>
    public string? APatchWithNothingToPatch
    {
        get
        {
            for (int at = 0; at < _tree.Count && at < _games.Count; at++)
            {
                bool theGameIsOff = _games[at].Files.Count == 0;
                bool anAddOnIsOn = _addOns.Any(addOn => addOn.BelongsTo == at);

                if (theGameIsOff && anAddOnIsOn)
                {
                    return $"{_tree[at].Name} is not on the disc, but its add-ons are. "
                        + "They will have nothing to install onto.";
                }
            }

            return null;
        }
    }

    /// <summary>
    /// What the recogniser noticed about the folders these games came out of,
    /// or nothing when it noticed nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two things reach this. A download that stopped early leaves a gap in
    /// its numbered parts, which builds a clean looking ISO that fails only
    /// when somebody runs the installer off the burned disc, by which time
    /// the disc is spent. And a folder holding several installers, where
    /// which one was taken is worth seeing before a disc is burned rather
    /// than after.
    /// </para>
    /// <para>
    /// Both were worked out from the first day and <strong>neither was ever
    /// shown to anybody</strong>. The field they live on appeared in the
    /// recogniser, in its tests, and in no other file in the program.
    /// </para>
    /// <para>
    /// On the window rather than in a box at the moment of adding, because a
    /// box is dismissed and the burn is an hour later. It belongs beside the
    /// list for as long as the game is on it.
    /// </para>
    /// </remarks>
    public string? WhatTheDownloadsSay
    {
        get
        {
            string[] said =
            [
                .. _games
                    .Where(game => !string.IsNullOrWhiteSpace(game.Warning))
                    .Select(game => _games.Count == 1
                        ? game.Warning!
                        : $"{game.Name}: {game.Warning}")
            ];

            return said.Length == 0 ? null : string.Join(" ", said);
        }
    }

    /// <summary>
    /// The names worn by more than one game on the disc, or nothing when
    /// every game answers to its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked for in discwright#98. Two rows reading alike here become two
    /// menu buttons reading alike on the burned disc, and the person holding
    /// it has nothing but their order to tell them apart.
    /// </para>
    /// <para>
    /// A notice and never a refusal, which is what was asked for in as many
    /// words: nothing is actually wrong, two editions of one game are an
    /// ordinary thing to burn. Worry ink rather than ordinary, because
    /// unlike a disc that copies instead of starting, this is almost never
    /// what somebody meant.
    /// </para>
    /// </remarks>
    public string? WhoWearsTheSameName
    {
        get
        {
            string[] worn =
            [
                .. _games
                    .GroupBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                    .Where(same => same.Count() > 1)
                    .Select(same => $"{same.Count()} games on this disc answer to the same name, {same.Key}.")
            ];

            return worn.Length == 0
                ? null
                : string.Join(" ", worn)
                    + " Their menu buttons will read alike, and nothing but their order tells them apart.";
        }
    }

    /// <summary>
    /// What the last opened project named that could not be brought back, or
    /// nothing, because everything came back or nothing was opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked for in discwright#98, about files "more prone to being moved
    /// around": being told, after loading a project, that something it lists
    /// is missing. The games that did not come back are the one part of a
    /// project that cannot sit on the window as rows, so this line is their
    /// only trace. The news used to arrive in a box, and a box is gone at OK
    /// while the games are still gone an hour later.
    /// </para>
    /// <para>
    /// It stays through builds, because a disc built while something is
    /// missing is a disc built without it, which is exactly when the line is
    /// worth reading again. A new disc, or another project opened, is a new
    /// story, and each starts the line over.
    /// </para>
    /// </remarks>
    public string? WhatDidNotComeBack { get; private set; }

    /// <summary>The line about it, or nothing when it all came back.</summary>
    private static string? TheNewsAbout(Reopened back)
    {
        if (back.WholeAgain)
        {
            return null;
        }

        List<string> said = [];

        if (back.Gone.Count > 0)
        {
            string named = back.Gone.Count <= 5
                ? string.Join(", ", back.Gone)
                : string.Join(", ", back.Gone.Take(5)) + $" and {back.Gone.Count - 5} more";

            said.Add(back.Gone.Count == 1
                ? $"1 thing this project points at is not where it left it: {named}."
                : $"{back.Gone.Count} things this project points at are not where it left them: {named}.");
        }

        if (back.Trouble.Count > 0)
        {
            said.Add(back.Trouble.Count == 1
                ? $"1 folder is still there but could not be read as a game: {back.Trouble[0].Headline}."
                : $"{back.Trouble.Count} folders are still there but could not be read as games: "
                    + string.Join("; ", back.Trouble.Select(each => each.Headline)) + ".");
        }

        said.Add("The saved file still names all of it, so putting it back and opening the project again brings it back.");

        return string.Join(" ", said);
    }

    /// <summary>
    /// Which games the disc will copy rather than start, or nothing when it
    /// will start all of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The menu runs an executable only when a folder of game files has
    /// exactly one worth running, because it will not guess between several.
    /// With none, and with more than one that nobody picked, the big button
    /// copies the folder and opens it instead.
    /// </para>
    /// <para>
    /// That is a reasonable thing for it to do and it was completely silent.
    /// Two folders that look identical on this window made two discs that
    /// behave differently in the drive, and the only way to find out which
    /// was to burn one and press the button.
    /// </para>
    /// <para>
    /// Ordinary ink. A disc of files to copy is a thing somebody may well
    /// mean to make, and a window where everything is amber is a window where
    /// amber means nothing.
    /// </para>
    /// </remarks>
    public string? WhatWillNotStartItself
    {
        get
        {
            // Folders of files only. A GOG download whose installer has been
            // unticked has no installer either, and the menu would copy it,
            // but WhyThatMatters already says the one thing worth saying
            // about it, and this line beneath it read as a second complaint
            // about the same tick, with words meant for a folder.
            string[] copied =
            [
                .. _games
                    .Where(game => game.Kind != GameKind.GogInstaller)
                    .Where(game => game.Installer is null && game.LauncherCandidates.Count != 1)
                    .Select(game => game.Name)
            ];

            if (copied.Length == 0)
            {
                return null;
            }

            string which = copied.Length == 1
                ? copied[0]
                : string.Join(", ", copied[..^1]) + " and " + copied[^1];

            return copied.Length == 1
                ? $"The menu will copy {which} and open the folder, because nothing in it was picked to "
                    + "start the game."
                : $"The menu will copy {which} and open their folders, because nothing in them was picked "
                    + "to start them.";
        }
    }

    public string? WhyThatMatters =>
        _tree.Any(game => game.ItsInstallerIsOff())
            ? "Without its installer, the disc will not install the game."
            : null;

    /// <summary>Whether the disc gets a menu that opens when it goes in.</summary>
    public bool Menu
    {
        get => Options.Menu;
        set => Switch(Options with { Menu = value });
    }

    /// <summary>Whether the disc carries a list of what every file should be.</summary>
    public bool Checksums
    {
        get => Options.Checksums;
        set => Switch(Options with { Checksums = value });
    }

    /// <summary>Whether to add the file systems an older Windows can read.</summary>
    public bool LegacyFileSystems
    {
        get => Options.LegacyFileSystems;
        set => Switch(Options with { LegacyFileSystems = value });
    }

    /// <summary>
    /// Whether a file too big for the room left may be split across two discs.
    /// </summary>
    public bool CutFiles
    {
        get => Options.CutFiles;
        set => Switch(Options with { CutFiles = value });
    }

    /// <summary>
    /// Whether the disc folder is deleted once its image is made, leaving
    /// the image alone. discwright#98, 15 November 2026: "ISO only, without
    /// the disc folder, to halve the space needed".
    /// </summary>
    /// <remarks>
    /// Off unless somebody asks. The folder is what a person opens to see
    /// what is on the disc, and what a burned disc is compared with, so
    /// taking it away is never the default. What is left is half the size;
    /// the build itself still needs room for both, because the image is
    /// made from the folder.
    /// </remarks>
    /// <summary>
    /// Whether several games that do not fit on one disc get a disc, or a
    /// set, each, named after its own game, and not one set between them.
    /// </summary>
    /// <remarks>
    /// Off as it comes. They got a disc each always until 2026-11-21, when
    /// the owner's three games came to five discs where three hold them.
    /// Still here for whoever wants each game's disc to stand by itself.
    /// </remarks>
    public bool ADiscForEachGame
    {
        get => Options.ADiscForEachGame;
        set => Switch(Options with { ADiscForEachGame = value });
    }

    public bool OnlyTheImage
    {
        get => Options.OnlyTheImage;
        set => Switch(Options with { OnlyTheImage = value });
    }

    /// <summary>Whether a disc with a menu carries Start Here.hta at its top.</summary>
    public bool StartHere
    {
        get => Options.StartHere;
        set => Switch(Options with { StartHere = value });
    }

    /// <summary>
    /// Whether the menu's list of buttons sits against the left edge rather
    /// than the right, discwright#98.
    /// </summary>
    public bool ListOnTheLeft
    {
        get => Options.LookOrPlain.ListOn == MenuSide.Left;
        set => SwitchTheLook(Options.LookOrPlain with { ListOn = value ? MenuSide.Left : MenuSide.Right });
    }

    /// <summary>
    /// Whether the menu's small buttons sit in the bottom corner rather than
    /// the top one.
    /// </summary>
    public bool CornerAtTheBottom
    {
        get => Options.LookOrPlain.CornerAtTheBottom;
        set => SwitchTheLook(Options.LookOrPlain with { CornerAtTheBottom = value });
    }

    /// <summary>
    /// Where the small buttons will be, in words, because the side they take
    /// is decided by the list and nobody should have to work that out.
    /// </summary>
    public string WhereTheCornerButtonsSit =>
        "The small buttons, music and the disc check among them, sit "
        + (CornerAtTheBottom ? "bottom " : "top ")
        + (ListOnTheLeft ? "right" : "left")
        + ", away from the list.";

    /// <summary>
    /// Whether the disc's own manual and extras are small buttons in the
    /// corner of the main menu rather than rows in its list.
    /// </summary>
    public bool DiscsOwnAsSmallButtons
    {
        get => Options.LookOrPlain.DiscsOwnAsSmallButtons;
        set
        {
            SwitchTheLook(Options.LookOrPlain with { DiscManualAsASmallButton = value, DiscExtrasAsASmallButton = value });
            Tell(nameof(DiscManualAsASmallButton));
            Tell(nameof(DiscExtrasAsASmallButton));
        }
    }

    /// <summary>
    /// Whether the disc's own manual is a small button in the corner of the
    /// main menu rather than a row in its list. A tick of its own since the
    /// owner asked on 2026-11-20.
    /// </summary>
    public bool DiscManualAsASmallButton
    {
        get => Options.LookOrPlain.DiscManualAsASmallButton;
        set
        {
            SwitchTheLook(Options.LookOrPlain with { DiscManualAsASmallButton = value });
            Tell(nameof(DiscsOwnAsSmallButtons));
        }
    }

    /// <summary>The same for the disc's own extras folder.</summary>
    public bool DiscExtrasAsASmallButton
    {
        get => Options.LookOrPlain.DiscExtrasAsASmallButton;
        set
        {
            SwitchTheLook(Options.LookOrPlain with { DiscExtrasAsASmallButton = value });
            Tell(nameof(DiscsOwnAsSmallButtons));
        }
    }

    /// <summary>Whether the corner has a button that opens the disc in Explorer.</summary>
    public bool OpenTheDiscButton
    {
        get => Options.LookOrPlain.OpenTheDiscButton;
        set => SwitchTheLook(Options.LookOrPlain with { OpenTheDiscButton = value });
    }

    /// <summary>
    /// What the main menu is headed with, as typed. Empty for the disc's own
    /// name, discwright#98.
    /// </summary>
    public string MenuHeading
    {
        get => _menuHeadingText;
        set
        {
            _menuHeadingText = value ?? string.Empty;
            SwitchTheLook(Options.LookOrPlain with { Heading = MenuLook.TidiedHeading(_menuHeadingText) });
        }
    }

    /// <summary>Whether the main menu has no heading at all.</summary>
    public bool MenuHeadingHidden
    {
        get => Options.LookOrPlain.HeadingHidden;
        set
        {
            SwitchTheLook(Options.LookOrPlain with { HeadingHidden = value });
            Tell(nameof(MenuHeadingCanBeTyped));
        }
    }

    /// <summary>
    /// Whether the heading's box is live: not while the menu is to have no
    /// heading, when whatever is typed in it goes nowhere.
    /// </summary>
    public bool MenuHeadingCanBeTyped => !MenuHeadingHidden;

    /// <summary>
    /// Whether the menu's window has no title bar and no edge, discwright#98.
    /// </summary>
    /// <remarks>
    /// For a menu that is all picture. It gets a cross of its own in the
    /// top right corner and is moved by dragging its picture, so nothing a
    /// window is for is lost with the frame.
    /// </remarks>
    public bool MenuWithoutAFrame
    {
        get => Options.LookOrPlain.NoFrame;
        set => SwitchTheLook(Options.LookOrPlain with { NoFrame = value });
    }

    private string _menuInkText = string.Empty;

    /// <summary>
    /// The colour the menu's heading and buttons are written in, as typed.
    /// discwright#98: "more menu themes (layout / font / font colour)".
    /// </summary>
    /// <remarks>
    /// The raw text, so the box never fights the person typing in it. What
    /// reaches the disc is only ever a colour, or nothing while the text is
    /// half typed or wrong, with <see cref="WhatTheInkSays"/> saying so.
    /// Emptied, the menu is written in its own colours again.
    /// </remarks>
    public string MenuInkText
    {
        get => _menuInkText;
        set
        {
            _menuInkText = value ?? string.Empty;
            SwitchTheLook(Options.LookOrPlain with { Ink = SolidColour.Tidied(_menuInkText) });
            Tell(nameof(MenuInkText));
            Tell(nameof(MenuInk));
            Tell(nameof(WhatTheInkSays));
        }
    }

    /// <summary>The colour as the disc will get it, or nothing.</summary>
    public string? MenuInk => Options.LookOrPlain.Ink;

    /// <summary>Why the typed text is not a colour yet, or nothing.</summary>
    public string? WhatTheInkSays =>
        _menuInkText.Trim().Length > 0 && !SolidColour.Fits(_menuInkText)
            ? "Not a colour yet. Six hex figures name one, like FFE9B0."
            : null;

    /// <summary>
    /// A handful of colours that read on a dark menu, for the swatches
    /// beside the box. Every one of them must satisfy SolidColour, which a
    /// test holds.
    /// </summary>
    public IReadOnlyList<string> TheInksOffered { get; } =
        ["#ffffff", "#ffe9b0", "#ffc46b", "#9fe3e8", "#c8f0b4", "#f6b6c8"];

    /// <summary>Every face the menu can be written in, for the list.</summary>
    public IReadOnlyList<AMenuFont> TheMenusFonts { get; } = MenuFonts.All;

    /// <summary>
    /// The face the menu is written in, as the row of the list that stands
    /// for it. discwright#98: "more menu themes (layout / font / font
    /// colour)".
    /// </summary>
    public AMenuFont? TheMenusFont
    {
        get => MenuFonts.For(Options.LookOrPlain.Font);
        set
        {
            // A list empties its choice for a moment whenever its rows are
            // read again, and that is not somebody choosing nothing.
            if (value is not null)
            {
                SwitchTheLook(Options.LookOrPlain with { Font = value.Which });
            }
        }
    }

    private void SwitchTheLook(MenuLook now, [CallerMemberName] string? name = null)
    {
        Switch(Options with { Look = now.OrNothingWhenPlain() }, name);
        Tell(nameof(WhereTheCornerButtonsSit));
    }

    /// <summary>
    /// Everything worth knowing about the drives, for the picker to hover
    /// with: what it does when nobody chooses, and any tray that needs a push.
    /// </summary>
    /// <remarks>
    /// The tray warning had a line of its own and that was still a line, on a
    /// window where every row was being counted. It belongs on the picker:
    /// the warning is about a drive, the picker is where a drive is chosen,
    /// and hovering it is where somebody already goes to ask what the drives
    /// are doing.
    ///
    /// Nothing is lost by moving it. A burn stops and asks when a tray needs
    /// a push, so the fact still reaches whoever is standing there, and this
    /// is the part that reaches them beforehand.
    /// </remarks>
    public string WhatTheDrivesAreDoing =>
        WhatTheDrivesMean.WhatThePickerSays(AboutTheTray, WhyTheTrayMatters);

    /// <summary>
    /// Why a tray that needs pushing matters, for the window to hover with.
    /// </summary>
    /// <remarks>
    /// The warning itself is short enough not to wrap, because it has to be
    /// read before an unattended run is started. This is the reason behind
    /// it, which only has to be findable by somebody who wonders.
    /// </remarks>
    [SuppressMessage("Performance", "CA1822", Justification = "A binding cannot reach a static property.")]
    public string WhyTheTrayMatters => TheDiscsThatAreIn.WhyTheTrayMatters;

    /// <summary>
    /// Whether there is a disc that erasing would make usable, and nothing
    /// else going on.
    /// </summary>
    /// <remarks>
    /// Nothing to do with having a disc to write. Somebody with a drawer of
    /// used rewritables wants them blank again, and until now the only way to
    /// erase one was to be half way through a burn and be asked.
    /// </remarks>
    public bool CanErase => !IsWorking && Drives.Any(each => each.ErasingWouldHelp);

    /// <summary>
    /// Erases a disc, and nothing else. No image, no burn, no set.
    /// </summary>
    /// <remarks>
    /// The disc is named by whoever asks, because which disc gets wiped is
    /// never a choice to make on somebody's behalf.
    /// </remarks>
    public async Task EraseADisc(
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> whichToErase,
        CancellationToken ct = default)
    {
        ItWentWrong = null;
        Tell(nameof(ItWentWrong));

        try
        {
            // Inside the catch-all, and that is the fix rather than a tidy-up.
            //
            // Asking the drives what they are holding is a pile of COM calls,
            // and it sat out here where nothing caught anything. So the box
            // reading "Attempt has been made to use a COM object that does not
            // have a backing class factory" arrived before a single thing had
            // been erased, from the sentence below rather than from the erase
            // the catch-all was written for.
            // BEFORE THE ASKING, NOT AFTER THE CHOOSING, and that is two
            // faults rather than tidiness.
            //
            // Say drops anything said while this is false, so the
            // "waiting for the drive" that the settling says on the line below was
            // thrown away every time: the one sentence written for this moment,
            // never once shown. And CanErase is !IsWorking, so the button stayed
            // live through the whole wait and a second press started a second
            // run of all of it.
            IsWorking = true;
            Tell(nameof(IsWorking));
            Tell(nameof(NothingIsRunning));
            Tell(nameof(TheSizeCanBePicked));
            Tell(nameof(CanStartAgain));
            Tell(nameof(CanSaveItForLater));
            Tell(nameof(CanTakeSomethingOff));
            TellHowTheOrderCanChange();
            Tell(nameof(CanErase));

            Outcome<IReadOnlyList<Recorder>> couldBe = await _settling.WhichCouldBeErased(ct);

            if (couldBe is Outcome<IReadOnlyList<Recorder>>.No nothingToErase)
            {
                ItWentWrong = nothingToErase.Refusal;
                Tell(nameof(ItWentWrong));
                return;
            }

            // Either there was nothing to choose between, which cannot happen
            // past the refusal above, or somebody closed the question, which
            // erases nothing. Closing it is an answer and not a fault, so
            // there is nothing to say about it.
            if (couldBe is not Outcome<IReadOnlyList<Recorder>>.Ok(IReadOnlyList<Recorder> could)
                || await whichToErase(could) is not Recorder chosen)
            {
                return;
            }

            // The same erase a burn offers, with the same words in front of
            // it, because it is the same act.
            if (await _settling.Erase(chosen, ct) is Refusal why)
            {
                ItWentWrong = why;
                Tell(nameof(ItWentWrong));
                return;
            }

            Say($"{chosen.Drive} is blank again", 1);
        }
        catch (Exception nobodyExpected) when (nobodyExpected is not OperationCanceledException)
        {
            // Anything that gets this far is something nobody wrote a
            // sentence for, and it still has to reach the window as an
            // answer. Letting it out turns into raw exception text in a
            // message box, which reads as a crash: that is how
            // "Attempt has been made to use a COM object that does not have
            // a backing class factory" reached Lazar.
            ItWentWrong = Refusals.SomethingNobodyExpected("erasing the disc", nobodyExpected.Message);
            Tell(nameof(ItWentWrong));
        }
        finally
        {
            IsWorking = false;
            Tell(nameof(IsWorking));
            Tell(nameof(NothingIsRunning));
            Tell(nameof(TheSizeCanBePicked));
            Tell(nameof(CanStartAgain));
            Tell(nameof(CanSaveItForLater));
            Tell(nameof(CanTakeSomethingOff));
            TellHowTheOrderCanChange();
            Tell(nameof(CanErase));
            _aDiscMovedMeanwhile = false;
            await LookAtTheDrives();
        }
    }

    /// <summary>Whether a build is running right now.</summary>
    public bool IsWorking { get; private set; }

    /// <summary>Whether a disc went in or out while something was running.</summary>
    private bool _aDiscMovedMeanwhile;

    /// <summary>
    /// The look a run put off, once the run is over, and nothing when no disc
    /// moved: a build that touched no drive must not spin one up afterwards.
    /// </summary>
    private async Task LookAgainIfADiscMovedMeanwhile()
    {
        if (!_aDiscMovedMeanwhile)
        {
            return;
        }

        _aDiscMovedMeanwhile = false;
        await LookAtTheDrives();
    }

    /// <summary>
    /// What the build is doing, in the words the build itself used.
    /// </summary>
    /// <remarks>
    /// Passed straight through rather than reworded here. The step knows what
    /// it is doing and this does not, and a window that invents its own account
    /// of somebody else's work is a window that will one day be wrong about it.
    /// </remarks>
    public string Doing { get; private set; } = string.Empty;

    /// <summary>How far along, from nothing to one.</summary>
    public double HowFar { get; private set; }

    /// <summary>What the last build produced, or null when none has.</summary>
    public BuiltDiscs? Made { get; private set; }

    /// <summary>
    /// The images of the one set the last build made, in the order its discs
    /// are burned, or nothing when it made no set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A set is the discs that continue one another, and only those. The
    /// window took any build with more than one disc for a set, so several
    /// games that each got a disc of their own were walked as "disc 1 of 3,
    /// disc 2 of 3", and from the second disc on each was read back against
    /// the first game's folder and called wrong. Here rather than in the
    /// window, where that decision could not be tested.
    /// </para>
    /// <para>
    /// Discs that stand alone are burned one at a time, chosen from whatever
    /// images are in the folder, which is the ordinary way to burn one disc.
    /// </para>
    /// <para>
    /// <strong>When this sitting built nothing, the folder answers.</strong>
    /// Opening a finished project and pressing Burn is the ordinary way to
    /// finish a set, and it got one image at a time from a list that did not
    /// say which were done, with the note beside the discs never offered. The
    /// images in the folder that are named as this disc's set, 1 upwards with
    /// no gap, are the set.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> TheSetToBurn =>
        Made is null
            ? TheImageOf.TheSetAmong(TheImages, Label)
            : [.. Made.Discs.Where(disc => disc.OfDiscs > 1).OrderBy(disc => disc.Number).Select(TheImageOf.That)];

    /// <summary>
    /// Something the image had to say that did not stop it, or null.
    /// </summary>
    public string? AboutTheImage { get; private set; }

    /// <summary>
    /// Whether the disc folders of what was just built are still there. Not
    /// after a build that kept only its images.
    /// </summary>
    public bool TheDiscFoldersWereKept { get; private set; } = true;

    /// <summary>
    /// What to tell somebody when a build has finished, and the folder to
    /// offer to open, or nothing when nothing was built.
    /// </summary>
    /// <remarks>
    /// Worded here so a test can read it. It named the disc folder whatever
    /// had become of it, and offered to open a folder that a build keeping
    /// only its image had just deleted.
    /// </remarks>
    public (string Words, string Folder)? WhatToSayOfWhatWasMade()
    {
        if (Made is not { Discs.Count: > 0 } made)
        {
            return null;
        }

        string disc = made.Discs[0].Folder;
        string beside = System.IO.Path.GetDirectoryName(disc) ?? disc;
        bool one = made.Discs.Count == 1;

        if (!TheDiscFoldersWereKept)
        {
            return (one ? "The disc image is ready in:" : $"All {made.Discs.Count} disc images are ready in:", beside);
        }

        return one
            ? ("The disc is ready in:", disc)
            : ($"All {made.Discs.Count} discs are ready in:", beside);
    }

    /// <summary>
    /// Deletes the disc folders of a build whose images are all made.
    /// </summary>
    /// <remarks>
    /// A folder that will not go is not a failed build. The image is made
    /// and is what was asked for, so the folder is left and a line says so,
    /// in words that name it and say what to do.
    /// </remarks>
    private async Task LeaveOnlyTheImages(BuiltDiscs made)
    {
        if (_machine.Remover is not IRemoveADiscFolder remover)
        {
            return;
        }

        Doing = "Deleting the disc folder";
        Tell(nameof(Doing));

        List<string> stayed = [];

        // On the pool: sixty gigabytes of files take a while to delete, and
        // this is the thread the window is drawn on.
        await Task.Run(() =>
        {
            foreach (BuiltDisc disc in made.Discs)
            {
                try
                {
                    remover.Remove(disc.Folder);
                }
                catch (Exception held) when (held is System.IO.IOException or UnauthorizedAccessException)
                {
                    stayed.Add(disc.Folder);
                }
            }
        });

        TheDiscFoldersWereKept = stayed.Count > 0;

        if (stayed.Count > 0)
        {
            string left = stayed.Count == 1
                ? $"The image is made, but {stayed[0]} could not be deleted because something has it open. "
                    + "Close whatever is using it and delete the folder by hand."
                : $"The images are made, but {stayed.Count} disc folders could not be deleted because something "
                    + $"has them open, {stayed[0]} among them. Close whatever is using them and delete them by hand.";

            AboutTheImage = AboutTheImage is null ? left : AboutTheImage + " " + left;
            Tell(nameof(AboutTheImage));
        }

        Tell(nameof(TheDiscFoldersWereKept));
    }

    /// <summary>
    /// Deletes what an earlier build left in this folder that this build did
    /// not write over: the discs of a longer set, the set that games now
    /// given a disc each used to share, and the folders of games that now
    /// share a set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window asks before a build over an earlier one and says building
    /// deletes it. It did so only by writing over it, so whatever the new
    /// build did not happen to write stayed: a set of five rebuilt as three
    /// kept discs four and five, with their images, and looked like a set of
    /// five to anybody who opened the folder.
    /// </para>
    /// <para>
    /// After this build and never before it. A build that is stopped or
    /// fails leaves the earlier one as it was, wherever it had not yet
    /// written.
    /// </para>
    /// <para>
    /// Something that will not go is not a failed build. The discs are made,
    /// and a line names what stayed and says what to do about it.
    /// </para>
    /// </remarks>
    private async Task ClearAwayWhatWasNotReplaced(WhatAnEarlierBuildLeft before, BuiltDiscs made)
    {
        WhatAnEarlierBuildLeft left = before.NotAmong(
            made.Discs.Select(disc => disc.Folder), made.Discs.Select(TheImageOf.That));

        if (!left.IsThere || _machine.Remover is not IRemoveADiscFolder remover)
        {
            return;
        }

        Doing = "Deleting what the earlier build left";
        Tell(nameof(Doing));

        List<string> stayed = [];

        // On the pool, as every deleting here is: a disc folder is gigabytes.
        await Task.Run(() =>
        {
            foreach (string image in left.Images)
            {
                try
                {
                    remover.RemoveTheImage(image);
                }
                catch (Exception held) when (held is System.IO.IOException or UnauthorizedAccessException)
                {
                    stayed.Add(image);
                }
            }

            foreach (string folder in left.Folders)
            {
                try
                {
                    remover.Remove(folder);
                }
                catch (Exception held) when (held is System.IO.IOException or UnauthorizedAccessException)
                {
                    stayed.Add(folder);
                }
            }

            // A folder made for one game's discs, when nothing is left in
            // it. One this build wrote into is not empty, and stays.
            foreach (string above in left.OfOneGame)
            {
                remover.RemoveIfEmpty(above);
            }
        });

        if (stayed.Count == 0)
        {
            return;
        }

        string says = stayed.Count == 1
            ? $"An earlier build left {stayed[0]}, which could not be deleted because something has it open. "
                + "Close whatever is using it and delete it by hand."
            : $"An earlier build left {stayed.Count} things that could not be deleted because something has them "
                + $"open, {stayed[0]} among them. Close whatever is using them and delete them by hand.";

        AboutTheImage = AboutTheImage is null ? says : AboutTheImage + " " + says;
        Tell(nameof(AboutTheImage));
    }

    /// <summary>Why the last build stopped, or null when it did not.</summary>
    public Refusal? ItWentWrong { get; private set; }

    /// <summary>
    /// Makes the discs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything that can be known beforehand already is: by the time this
    /// runs, <see cref="CanMakeIt"/> has been true for as long as somebody has
    /// been looking at the window. So this is not where refusals are found, it
    /// is where the work happens and where the things that can only go wrong
    /// during the work are caught.
    /// </para>
    /// <para>
    /// A build that is cancelled is not a failure and says nothing. Somebody
    /// who pressed stop knows why it stopped.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The first thing chosen under Artwork, music and extras that is no
    /// longer where it was chosen from, as a refusal naming it, or nothing.
    /// </summary>
    /// <remarks>
    /// Asked before a preview and before a build, which both use every one of
    /// them. In the order the window lists them, so the one named is the one
    /// somebody finds first.
    /// </remarks>
    public Refusal? AChosenFileIsGone()
    {
        (string What, string? Path, bool AFolder)[] chosen =
        [
            ("disc icon", DiscIcon, false),
            ("menu background", Background, false),
            ("menu font", MenuFontFile, false),
            ("music", Music, false),
            ("manual", Manual, false),
            ("extras folder", ExtrasFolder, true),
            .. _loose.Select(loose => ("extra", (string?)loose, false)),
            ("cover", Cover, false),
            ("disc face", DiscFace, false)
        ];

        foreach ((string what, string? path, bool aFolder) in chosen)
        {
            if (path is null || path.Length == 0)
            {
                continue;
            }

            bool there = aFolder
                ? _machine.Disk.FolderExists(path)
                : _machine.Disk.FileExists(path) || (what == "extra" && _machine.Disk.FolderExists(path));

            if (!there)
            {
                return Refusals.ThatChosenFileIsNotThere(what, path);
            }
        }

        // Each game's own, after the disc's, in the order of the list. A
        // file or a folder can be either, so both are looked for, and the
        // refusal names the game, because "the manual" alone would send
        // somebody to Artwork, music and extras, where it is not.
        foreach (GameFolder game in _games)
        {
            foreach (GameExtra own in game.OwnOrNone)
            {
                if (!_machine.Disk.FileExists(own.Path) && !_machine.Disk.FolderExists(own.Path))
                {
                    return Refusals.ThatGamesOwnIsNotThere(game.Name, own);
                }
            }
        }

        return null;
    }

    // What stops the build that is running, or nothing while none is.
    private CancellationTokenSource? _stopping;

    /// <summary>
    /// Whether a build is running that can be stopped. The window shows Stop
    /// in place of Build it while this is so.
    /// </summary>
    /// <remarks>
    /// Only a build. A burn has its own way of being given up, at the drive,
    /// and stopping one half way leaves a disc that is spent.
    /// </remarks>
    public bool CanStopTheBuild => _stopping is { IsCancellationRequested: false };

    /// <summary>How the last stop went: what it took away, or what it could not. Null when nothing was stopped.</summary>
    public string? HowTheStopWent { get; private set; }

    /// <summary>
    /// What stopping the build would lose, asked before it is stopped.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The owner, 21 November 2026, forty minutes into a build with a game
    /// of 150 GB in it: "should there be a way to stop the build? That
    /// would of course delete everything that was created, with the
    /// previous warning, and if the user clicks yes the build stops and
    /// everything that was built is deleted." There was no way at all:
    /// the builder had always known how to stop when told, and nothing on
    /// the window ever told it.
    /// </para>
    /// <para>
    /// The likely answer is to stop, because that is what was just pressed.
    /// Shutting the question is the other answer, so a press that was a
    /// mistake costs nothing.
    /// </para>
    /// </remarks>
    public AQuestion? WhatStoppingWouldLose() =>
        CanStopTheBuild
            ? new AQuestion(
                "Stop the build, and delete what it has made so far?",
                "The disc folders and images this build has written are deleted, because a disc stopped part way "
                    + "is not a disc and looks like one. It cannot be picked up where it left off. Your games "
                    + "are not touched, and neither is anything else in the folder.",
                "Stop the build",
                "Keep building")
            : null;

    /// <summary>
    /// Stops the build that is running. What it had written is taken away
    /// as it stops, and <see cref="HowTheStopWent"/> says how that went.
    /// </summary>
    public void StopTheBuild()
    {
        if (!CanStopTheBuild)
        {
            return;
        }

        _stopping!.Cancel();

        lock (_oneAtATime)
        {
            Doing = "Stopping, and deleting what was built";
        }

        Tell(nameof(Doing));
        Tell(nameof(CanStopTheBuild));
    }

    public async Task MakeIt(CancellationToken ct = default)
    {
        // CanMakeIt is already false while a build is running, so this is the
        // whole of the guard. Saying it twice was two things to keep in step.
        if (!CanMakeIt)
        {
            return;
        }

        // Before anything starts, so a file moved since it was chosen is
        // named rather than met half way through a build.
        if (AChosenFileIsGone() is Refusal gone)
        {
            ItWentWrong = gone;
            Tell(nameof(ItWentWrong));
            return;
        }

        IsWorking = true;
        Made = null;
        ItWentWrong = null;
        AboutTheImage = null;
        TheDiscFoldersWereKept = true;

        // The line about the last burn is about discs this build is about to
        // replace, so it goes with the drive's note rather than staying under
        // a bar that has started again. The saved line goes the same way: the
        // build writes the same file again, so the sentence about the old
        // save is about to stop being the story.
        HowTheDiscCameOut = null;
        HowTheSetWent = null;
        HowTheSaveWent = null;
        HowTheStopWent = null;
        HowFar = 0;
        Doing = "Starting";

        // Stopped by the Stop button, or by whoever called this and wants
        // it stopped: either is a stop.
        using CancellationTokenSource stopping = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _stopping = stopping;
        ct = stopping.Token;

        // What the image step has begun, and what the builder finished, for
        // a stop that comes after the folders are built.
        List<string> imagesBegun = [];
        BuiltDiscs? built = null;

        Started();
        Tell(nameof(CanStopTheBuild));
        Tell(nameof(HowTheStopWent));

        Progress<BuildProgress> saying = new(step =>
        {
            // A report can be in flight on another thread while the build
            // finishes. Looking at whether it is still running and then writing
            // afterwards is two steps, and the end can happen between them,
            // which leaves a finished build showing a bar stuck halfway. So the
            // looking and the writing happen together.
            bool worth;

            lock (_oneAtATime)
            {
                worth = IsWorking;

                if (worth)
                {
                    Doing = step.ToString();
                    HowFar = step.OfSteps == 0 ? 0 : (double)step.Step / step.OfSteps;
                }
            }

            // Outside the lock: whoever is listening is not this class's
            // business, and a window that took a while to redraw would hold up
            // the build.
            if (worth)
            {
                Tell(nameof(Doing));
                Tell(nameof(HowFar));
            }
        });

        // Read on this thread, before the pool gets involved: the window's
        // thread is free while the build runs, so these can change mid-build,
        // and the build must be of what was on the window at the press.
        DiscProject project = AsAProject();
        Medium medium = Medium;
        string into = OutputFolder;

        // Looked for before anything is written, because afterwards this
        // build's own discs are there too and nothing tells the two apart.
        WhatAnEarlierBuildLeft before = WhatAnEarlierBuildLeft.In(
            _machine.Disk, into, project.Label, project.Games.Select(game => game.Name));

        try
        {
            // On the pool, not on this thread. The staging copy is
            // synchronous by design, and this is the thread the window is
            // drawn on: run here, pressing Build froze the window for the
            // whole copy, "Starting" was set and never painted, and the
            // first thing anybody saw was the image writer's report seconds
            // later, watched on 2026-11-09. The reports still land on this
            // thread, because the Progress above was made on it.
            switch (await Task.Run(() => _machine.Builder.Build(project, medium, into, saying, ct), CancellationToken.None))
            {
                case Outcome<BuiltDiscs>.Ok(BuiltDiscs made):
                    built = made;

                    // The folder is the disc and the image is the same disc as
                    // one file. Both, every time, because the folder is what
                    // somebody checks and the image is what they burn, and
                    // having to ask for the second one is a step nobody wants.
                    ItWentWrong = await Images(made, imagesBegun, ct);
                    Made = made;

                    // Only once every image is made, and only then: a
                    // folder deleted before its image was written would be
                    // a build that left nothing.
                    if (ItWentWrong is null && project.Options.OnlyTheImage)
                    {
                        await LeaveOnlyTheImages(made);
                    }

                    // Only once this build is whole. Until then the earlier
                    // one is the only finished disc there is.
                    if (ItWentWrong is null)
                    {
                        await ClearAwayWhatWasNotReplaced(before, made);
                    }

                    break;

                case Outcome<BuiltDiscs>.No stopped:
                    ItWentWrong = stopped.Refusal;
                    break;
            }
        }
        catch (OperationCanceledException stopped)
        {
            // Not a failure. But what it had written is not a disc, so it
            // goes, and the line under the bar says that it went: somebody
            // who pressed stop knows why it stopped and not what became of
            // forty minutes of copying.
            List<string> stayed = [.. (stopped as ABuildThatWasStopped)?.LeftBehind ?? []];

            if (built is not null)
            {
                stayed.AddRange(await TakeAwayWhatWasBuilt(built, imagesBegun, into));
            }

            // Nothing was made: Made was emptied when the build began, and
            // is only filled once every image is written.
            HowTheStopWent = stayed.Count == 0
                ? "The build was stopped, and what it had written was deleted."
                : "The build was stopped. Something has these open, so they could not be deleted: "
                    + string.Join(", ", stayed) + ".";
        }
        finally
        {
            _stopping = null;

            lock (_oneAtATime)
            {
                IsWorking = false;
                Doing = string.Empty;
                HowFar = 0;
            }

            Started();
            Tell(nameof(CanStopTheBuild));
            Tell(nameof(HowTheStopWent));
            Tell(nameof(Made));
            await LookAgainIfADiscMovedMeanwhile();
        }
    }

    /// <summary>
    /// Takes away the disc folders a stopped build had finished and the
    /// images it had begun, for a stop that came while the images were
    /// being written. Says which it could not.
    /// </summary>
    /// <remarks>
    /// The project file stays. It is the recipe and not the disc, it is
    /// small, and it is how somebody builds the same thing again.
    /// </remarks>
    private async Task<List<string>> TakeAwayWhatWasBuilt(BuiltDiscs built, List<string> images, string into)
    {
        List<string> stayed = [];

        if (_machine.Remover is not IRemoveADiscFolder remover)
        {
            return stayed;
        }

        // On the pool: a disc folder of sixty gigabytes takes a while to
        // delete, and this is the thread the window is drawn on.
        await Task.Run(() =>
        {
            foreach (string image in images)
            {
                try
                {
                    remover.RemoveTheImage(image);
                }
                catch (Exception held) when (held is System.IO.IOException or UnauthorizedAccessException)
                {
                    stayed.Add(image);
                }
            }

            foreach (BuiltDisc disc in built.Discs)
            {
                try
                {
                    remover.Remove(disc.Folder);
                }
                catch (Exception held) when (held is System.IO.IOException or UnauthorizedAccessException)
                {
                    stayed.Add(disc.Folder);
                }
            }

            // A folder made for one game's discs, when nothing is left in it.
            foreach (string above in built.Discs
                         .Select(disc => System.IO.Path.GetDirectoryName(disc.Folder))
                         .Where(above => above is not null
                                         && !string.Equals(
                                             above.TrimEnd((char)92), into.TrimEnd((char)92), StringComparison.OrdinalIgnoreCase))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Cast<string>())
            {
                remover.RemoveIfEmpty(above);
            }
        });

        return stayed;
    }

    /// <summary>
    /// Whether there is anything to burn.
    /// </summary>
    /// <remarks>
    /// An image on disk is the only thing worth checking here. Whether a
    /// recorder exists and what is in it is asked at the moment of pressing,
    /// because a disc can be put in after the window opened, and asking the
    /// burning service on every keystroke would make the whole window crawl.
    /// </remarks>
    public bool CanBurn => TheImages.Count > 0 && !IsWorking;

    /// <summary>Whether pressing Delete would take anything off.</summary>
    /// <remarks>
    /// <para>
    /// Exactly what is picked, so a key that does nothing when pressed is a
    /// key with nothing to do rather than a surprise.
    /// </para>
    /// <para>
    /// <strong>This used to enable a Remove button</strong>, and before that
    /// the button meant two things, what was selected or everything unticked
    /// when nothing was. The button is gone: every row carries its own way
    /// off at its right edge, so removing is a gesture on the thing itself,
    /// and this rule now belongs to the Delete key, which still works on
    /// whatever is picked.
    /// </para>
    /// </remarks>
    public bool CanTakeSomethingOff => !IsWorking && WhatIsSelected is { IsOnlyToLookAt: false };

    /// <summary>
    /// What the link that clears out the unticked rows says, or nothing when
    /// there are none and there is no such link.
    /// </summary>
    /// <remarks>
    /// It sits at the end of <see cref="WhatIsLeftOff"/>, the sentence that
    /// already counts those rows and weighs them, which is why these words
    /// carry no count of their own: the count is an inch to the left.
    /// </remarks>
    public string? WhatTakingTheLeftOffOnesSays => _tree.WhatTakingThoseOffWouldSay();

    /// <summary>The row the tree has picked, or nothing.</summary>
    private AThingOnTheDisc? _selected;

    public AThingOnTheDisc? WhatIsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            Tell(nameof(CanTakeSomethingOff));
            TellHowTheOrderCanChange();
            Tell(nameof(ThePickedGame));
        }
    }

    /// <summary>
    /// The name of the game the picked row belongs to, for the line under the
    /// list that adds a manual or extras to it, or nothing.
    /// </summary>
    public string? ThePickedGame => GameOf(_selected) is int at and >= 0 ? _tree[at].Name : null;

    private int GameOf(AThingOnTheDisc? row)
    {
        if (row is null)
        {
            return -1;
        }

        for (int at = 0; at < _tree.Count; at++)
        {
            // A part of an installer is under its installer's row to be
            // looked at and is not one of the branch's own rows, so it is
            // found by the row it is shown under.
            if (_tree[at].EveryRow().Contains(row)
                || (row.IsOnlyToLookAt && _tree[at].EveryRow().Any(each => each.ShownUnderIt.Contains(row))))
            {
                return at;
            }
        }

        return -1;
    }

    /// <summary>
    /// Gives the picked game manuals or extras of its own, from anywhere on
    /// the machine.
    /// </summary>
    /// <remarks>
    /// For goodies kept apart from the installer, the way the owner's test
    /// kit keeps each game's manual and extras in folders of their own. One
    /// already its own is not added twice.
    /// </remarks>
    public void AddTheGamesOwn(IEnumerable<string> paths, GameExtraKind kind)
    {
        int at = GameOf(_selected);

        if (at < 0 || IsWorking)
        {
            return;
        }

        List<GameExtra> own = [.. _asFound[at].OwnOrNone];

        foreach (string path in paths)
        {
            if (!own.Any(each => string.Equals(each.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                own.Add(new GameExtra(
                    path,
                    kind,
                    _machine.HowBigIs(path),
                    _machine.Disk.FolderExists(path) ? _machine.Disk.List(path).Files.Count : 1));
            }
        }

        _asFound[at] = _asFound[at] with { Own = own.Count > 0 ? own : null };
        BuildTheBranchAgain(at);
    }

    /// <summary>
    /// Attaches installers to the picked game as its add-ons, from anywhere
    /// on the machine: a DLC, a patch, a mod.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reported against 2.0.4 on Reddit: add-ons kept in a folder apart from
    /// the game could not be put under the game's Install button. Adding the
    /// folder made a game of it, and the question that stops that said to
    /// "add the game first and attach this to it" when nothing in the window
    /// attached anything. This is that.
    /// </para>
    /// <para>
    /// Each brings the numbered parts sitting beside it. One already on the
    /// game is not attached twice, nor is one of the game's own files, which
    /// is the game, nor a part some add-on of the game already carries.
    /// </para>
    /// </remarks>
    public void AttachToThePickedGame(IEnumerable<string> installers)
    {
        int at = GameOf(_selected);

        if (at < 0 || IsWorking)
        {
            return;
        }

        GameFolder game = _asFound[at];

        foreach (string path in installers)
        {
            bool onAlready = _addOnsAsFound.Any(addOn =>
                addOn.BelongsTo == at
                && (TheSameFile(addOn.Path, path) || addOn.PartsOrNone.Any(part => TheSameFile(part.Path, path))));

            bool theGameItself = game.Files.Any(file =>
                TheSameFile(System.IO.Path.Combine(game.Path, file.RelativePath), path));

            if (onAlready || theGameItself)
            {
                continue;
            }

            _addOnsAsFound.Add(new AddOn(
                at,
                path,
                AddOn.NameFor(path),
                _machine.HowBigIs(path),
                AddOn.PartsBeside(path, _machine.Disk)));
        }

        BuildTheBranchAgain(at);
    }

    private static bool TheSameFile(string one, string other) =>
        string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Turns one of a game's own from its manual into one of its extras, or
    /// back, which is a click on what the row says it is.
    /// </summary>
    public void SwapWhatItIs(AThingOnTheDisc row)
    {
        int at = GameOf(row);

        if (at < 0 || IsWorking || !row.CanBeEither)
        {
            return;
        }

        _asFound[at] = _asFound[at] with
        {
            Own =
            [
                .. _asFound[at].OwnOrNone.Select(each =>
                    string.Equals(each.Path, row.ItsPath, StringComparison.OrdinalIgnoreCase)
                        ? each with { Kind = each.Kind == GameExtraKind.Manual ? GameExtraKind.Extra : GameExtraKind.Manual }
                        : each)
            ]
        };

        BuildTheBranchAgain(at);
    }

    /// <summary>
    /// The game's branch built again from what it now holds, remembering
    /// what the old rows held, with the game itself picked so the line under
    /// the list stays on it.
    /// </summary>
    private void BuildTheBranchAgain(int at)
    {
        _tree[at] = TheTreeOfWhatGoesOn.For(
            _asFound[at],
            _addOnsAsFound.Where(addOn => addOn.BelongsTo == at),
            TakeTheTicksAsTheAnswer).Remembering(_tree[at]);

        WhatIsSelected = _tree[at];

        // Which carries the ticks into the games and ends at Reconsider.
        TakeTheTicksAsTheAnswer();
    }

    /// <summary>
    /// Whether the project can be changed at all just now.
    /// </summary>
    /// <remarks>
    /// Adding a game to a disc that is part way through being written, or
    /// pointing the output somewhere else mid-burn, is not something to find
    /// out about by trying it.
    /// </remarks>
    public bool NothingIsRunning => !IsWorking;

    /// <summary>The images sitting in the output folder.</summary>
    public IReadOnlyList<string> TheImages { get; private set; } = [];

    /// <summary>What reading the last burned disc back found, or null.</summary>
    public DiscCheck? HowTheDiscCameOut { get; private set; }

    /// <summary>
    /// How the last burn came out, in one line that stays on the window, or
    /// nothing when no disc has been burned since the window last changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It used to arrive in a message box and be gone at OK, and it is the
    /// one sentence somebody holding the disc wants to look at twice.
    /// Everything else the window has to say is a quiet line that stays, and
    /// this is the line that most deserved to.
    /// </para>
    /// <para>
    /// A set's sentence over a single disc's, because the set's is about all
    /// of them and already carries the disc that stopped it. While a set is
    /// still being written the last disc's own result shows, so the line
    /// under the bar says how disc 1 came out while disc 2 writes.
    /// </para>
    /// </remarks>
    public string HowTheBurnWent =>
        HowTheSetWent?.InWords ?? HowTheDiscCameOut?.InWords ?? string.Empty;

    /// <summary>
    /// Whether that line is bad news, so the window can colour it the way it
    /// colours a disc of the wrong size. A disc that could not be checked at
    /// all is a refusal, not this.
    /// </summary>
    public bool TheBurnWentBadly =>
        HowTheSetWent is TheSetSoFar set ? !set.Whole : HowTheDiscCameOut is { Good: false };

    /// <summary>
    /// Writes an image to a disc, and reads the disc back afterwards.
    /// </summary>
    /// <param name="image">Which image, out of whatever is in the folder.</param>
    /// <param name="chooseADrive">
    /// Asked only when more than one drive is ready, because choosing for
    /// somebody is how a disc ends up written in the wrong one.
    /// </param>
    /// <param name="whichToErase">
    /// Asked only when nothing is ready and an erase would fix it, and given
    /// every disc that an erase would fix. With one there is a question to
    /// answer; with two there is a choice to make, and neither is a decision to
    /// take on somebody's behalf. Answering with nothing erases nothing.
    /// </param>
    /// <param name="shutTheTray">
    /// Called after the disc is ejected, to get it back in so it can be read.
    /// It answers whether the disc came back.
    /// </param>
    public async Task BurnIt(
        string image,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> chooseADrive,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> whichToErase,
        Func<Recorder, Task<bool>> shutTheTray,
        CancellationToken ct = default)
    {
        if (IsWorking)
        {
            return;
        }

        IsWorking = true;
        ItWentWrong = null;
        HowTheDiscCameOut = null;
        HowTheSetWent = null;
        Doing = "looking at the drives";
        HowFar = 0;
        Started();

        try
        {
            // A drive chosen in the window is the answer, so nothing is asked.
            Recorder? onto = _theDrive is Recorder picked
                ? TheDriveIn(await _settling.OnThatOne(
                    picked.Drive, _machine.HowBigIs(image), whichToErase, ct))
                : TheDriveIn(await _settling.OnWhicheverIsReady(
                    _machine.HowBigIs(image), chooseADrive, whichToErase, ct));

            if (onto is null)
            {
                return;
            }

            Progress<BurnProgress> saying = new(step => Say(step.ToString(), step.HowFar ?? 0));

            switch (await _machine.Burner.Burn(image, onto, saying, ct))
            {
                case Outcome<BurnedDisc>.No stopped:
                    ItWentWrong = stopped.Refusal;
                    return;

                case Outcome<BurnedDisc>.Ok(BurnedDisc burned):
                    AboutTheImage = burned.AboutTheReporting;
                    Tell(nameof(AboutTheImage));
                    ABlankWasUsed();

                    // Written is not the same as readable. The drive ejected so
                    // that whatever reads it next sees a freshly mounted volume.
                    Say("waiting for the disc to come back", 0);

                    if (!await shutTheTray(onto)
                        || await _comingBack.For(onto, said => Say(said, 0), ct) is null)
                    {
                        ItWentWrong = Refusals.TheDiscWasNotChecked(onto.Drive);
                        return;
                    }

                    await HowTheDiscCameBack(onto, new WhichDisc.TheImageCalled(image), ct);

                    // A disc burned on its own counts towards the set too.
                    //
                    // Only walking a set from the beginning ever moved the
                    // note on, so finishing one over two evenings left it
                    // saying the last disc was still to do however well that
                    // disc read back. Lazar hit exactly that: disc 2 verified
                    // against its own checksums and the note beside it went
                    // on saying "to do".
                    if (HowTheDiscCameOut is { Good: true })
                    {
                        TickThatDiscOff(image);
                    }

                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            lock (_oneAtATime)
            {
                IsWorking = false;
                Doing = string.Empty;
                HowFar = 0;
            }

            Started();
            await LookAgainIfADiscMovedMeanwhile();
        }
    }

    // ------------------------------------------- what else goes on the disc
    //
    // Every one of these was reachable only by OPENING a project somebody else
    // had made. The build pipeline has handled all of it from the start; there
    // was simply no way in from the window, so a disc made here could never
    // have a background, an icon, music, a manual or extras on it.

    /// <summary>The picture the disc wears in This PC and on the menu.</summary>
    public string? DiscIcon
    {
        get => _artwork?.IconPath;
        set => ChangeTheArtwork(Art() with { IconPath = Tidy(value) });
    }

    /// <summary>The picture the menu is drawn on.</summary>
    /// <remarks>
    /// A picture and a colour cannot both be the backdrop, so choosing one
    /// takes the other off, in both directions, rather than leaving two
    /// choices on the window and letting the build pick quietly.
    /// </remarks>
    public string? Background
    {
        get => _artwork?.BackgroundPath;
        set
        {
            string? path = Tidy(value);

            if (path is not null)
            {
                _backgroundColourText = string.Empty;
            }

            // Which part is kept belongs to the picture it was framed on.
            // Another picture starts in the middle, as every picture does.
            bool theSamePicture = string.Equals(path, Art().BackgroundPath, StringComparison.OrdinalIgnoreCase);

            ChangeTheArtwork(Art() with
            {
                BackgroundPath = path,
                BackgroundColour = path is null ? Art().BackgroundColour : null,
                Kept = theSamePicture ? Art().Kept : null,
            });

            Tell(nameof(BackgroundColourText));
            Tell(nameof(WhatTheColourSays));
        }
    }

    /// <summary>
    /// The colour the menu is drawn on instead of a picture, as typed,
    /// discwright#98: "a solid color option by choosing a hex value".
    /// </summary>
    /// <remarks>
    /// The raw text, so the box never fights the person typing in it. What
    /// actually reaches the disc is only ever what <see cref="SolidColour"/>
    /// makes of it: a colour, or nothing while the text is half typed or
    /// wrong, with <see cref="WhatTheColourSays"/> saying so.
    /// </remarks>
    public string BackgroundColourText
    {
        get => _backgroundColourText;
        set
        {
            _backgroundColourText = value ?? string.Empty;

            string? colour = SolidColour.Tidied(_backgroundColourText);

            ChangeTheArtwork(Art() with
            {
                BackgroundColour = colour,
                BackgroundPath = colour is null ? Art().BackgroundPath : null,

                // The part kept goes with the picture it was framed on.
                Kept = colour is null ? Art().Kept : null,
            });

            Tell(nameof(BackgroundColourText));
            Tell(nameof(WhatTheColourSays));
        }
    }

    /// <summary>The colour as the disc will get it, or nothing.</summary>
    public string? BackgroundColour => _artwork?.BackgroundColour;

    /// <summary>
    /// Why the typed text is not a colour yet, or nothing while it is one or
    /// there is none.
    /// </summary>
    public string? WhatTheColourSays =>
        _backgroundColourText.Trim().Length > 0 && !SolidColour.Fits(_backgroundColourText)
            ? "Not a colour yet. Six hex figures name one, like 1B3A4B."
            : null;

    /// <summary>
    /// A handful of colours that suit the menu's panel, for the swatches
    /// beside the box. Every one of them must satisfy SolidColour, which a
    /// test holds.
    /// </summary>
    public IReadOnlyList<string> TheColoursOffered { get; } =
        ["#0e2430", "#1b3a4b", "#2e1f3a", "#143321", "#3a2a1b", "#3a1b24"];

    /// <summary>
    /// Whether the background is stretched to fit exactly rather than cropped.
    /// </summary>
    public bool BackgroundAsIs
    {
        get => _artwork?.BackgroundAsIs == true;
        set => ChangeTheArtwork(Art().Fitting(value ? BackgroundFit.Stretch : BackgroundFit.Fill));
    }

    /// <summary>
    /// Every way a background can be made to cover the menu, for the list.
    /// </summary>
    public IReadOnlyList<AWayToFit> TheWaysABackgroundFits { get; } = WaysToFit.All;

    /// <summary>
    /// How the background covers the menu, as the row of the list that
    /// stands for it: fill, fit, stretch or tile, discwright#98.
    /// </summary>
    public AWayToFit? HowTheBackgroundFits
    {
        get => WaysToFit.For(Art().HowItFits);
        set
        {
            // A list empties its choice for a moment whenever its rows are
            // read again, and that is not somebody choosing nothing.
            if (value is not null)
            {
                ChangeTheArtwork(Art().Fitting(value.How));
            }
        }
    }

    /// <summary>What the way chosen does, in a sentence for under the list.</summary>
    public string WhatThatFitDoes => WaysToFit.For(Art().HowItFits).WhatItDoes;

    /// <summary>
    /// How the small picture in the dialog is drawn, so it looks like the
    /// way chosen: all of it inside for Fit, pulled for Stretch, at its own
    /// size for Tile.
    /// </summary>
    /// <remarks>
    /// It was filled and cropped whatever the list said, and from the left
    /// at that, so a wide picture fitted inside showed its left third in
    /// the dialog and all of itself on the disc. Seen in the real window on
    /// 2026-11-20. Filled, the frame draws it instead, and this is not read.
    /// </remarks>
    public string HowTheSmallPictureIsDrawn => Art().HowItFits switch
    {
        BackgroundFit.Fit => "Uniform",
        BackgroundFit.Stretch => "Fill",
        BackgroundFit.Tile => "None",
        _ => "UniformToFill",
    };

    /// <summary>
    /// Whether the small picture is drawn as a picture in one piece: fitted
    /// inside or stretched. Filled, the frame draws it, and tiled, the tiles do.
    /// </summary>
    public bool TheSmallPictureIsInOnePiece =>
        Art().BackgroundPath is not null && Art().HowItFits is BackgroundFit.Fit or BackgroundFit.Stretch;

    /// <summary>Whether the small picture is drawn as tiles.</summary>
    public bool TheSmallPictureIsTiled =>
        Art().BackgroundPath is not null && Art().HowItFits == BackgroundFit.Tile;

    /// <summary>
    /// One tile of the small picture, in the small picture's own pixels: the
    /// background at a quarter of its size, from the top left, which is the
    /// menu's 760 by 480 drawn at 190 by 120.
    /// </summary>
    /// <remarks>
    /// The small picture showed one copy of a tiled background, at whatever
    /// size it had been read at, while the menu repeated it. The owner, on
    /// the recording of the four ways to fit, 2026-11-20: what the list is
    /// changed to should show here as well as in the preview. A picture
    /// that cannot be read is one tile the size of the frame.
    /// </remarks>
    public Kept TheTileInTheSmallPicture
    {
        get
        {
            (int width, int height) = TheSizeOfTheBackground();

            return width > 0 && height > 0
                ? new Kept(0, 0, width / TheSmallPictureIsThisMuchSmaller, height / TheSmallPictureIsThisMuchSmaller)
                : new Kept(0, 0, CopyTheArtworkStep.MenuWidth / TheSmallPictureIsThisMuchSmaller, CopyTheArtworkStep.MenuHeight / TheSmallPictureIsThisMuchSmaller);
        }
    }

    /// <summary>The menu is 760 by 480 and the small picture of it 190 by 120.</summary>
    public const double TheSmallPictureIsThisMuchSmaller = 4.0;

    /// <summary>
    /// Whether there is a part to choose: a picture, filled and cropped.
    /// Every other way to fit shows all of the picture or none of the
    /// choice, and a colour has no parts.
    /// </summary>
    public bool TheBackgroundCanBeFramed =>
        Art().BackgroundPath is not null && Art().HowItFits == BackgroundFit.Fill;

    /// <summary>
    /// How far into the background the menu is zoomed, from 1, which is the
    /// least that covers it, to <see cref="KeptPart.MostZoom"/>. Reddit, 20
    /// November 2026: "the option to select the area that gets cropped".
    /// </summary>
    public double BackgroundZoom
    {
        get => Art().WhatIsKept.Zoom;
        set => Keep(Art().WhatIsKept with { Zoom = value });
    }

    /// <summary>
    /// Where the menu's frame sits across the background, 0 for its left
    /// edge to 1 for its right.
    /// </summary>
    public double BackgroundAcross
    {
        get => Art().WhatIsKept.Across;
        set => Keep(Art().WhatIsKept with { Across = value });
    }

    /// <summary>The same from the top, 0, to the bottom, 1.</summary>
    public double BackgroundDown
    {
        get => Art().WhatIsKept.Down;
        set => Keep(Art().WhatIsKept with { Down = value });
    }

    /// <summary>
    /// The part of the background the menu will show, as four fractions of
    /// the picture, for the small frame to show the same part.
    /// </summary>
    /// <remarks>
    /// Worked out from the picture's real size by the same sum the disc is
    /// drawn with, so what the frame shows is what the menu gets. All of it
    /// when the picture cannot be read: the picker has already said why.
    /// </remarks>
    public Kept WhatTheMenuKeeps
    {
        get
        {
            (int width, int height) = TheSizeOfTheBackground();

            return Art().WhatIsKept
                .In(width, height, CopyTheArtworkStep.MenuWidth, CopyTheArtworkStep.MenuHeight)
                .AsFractionsOf(width, height);
        }
    }

    /// <summary>Whether the part kept is anything but the middle, which is when there is something to put back.</summary>
    public bool TheBackgroundIsFramed => Art().Kept is not null;

    /// <summary>
    /// The picture dragged under the frame, by a fraction of the frame each
    /// way. A drag in a direction with nothing more to show changes nothing.
    /// </summary>
    public void DragTheBackground(double across, double down)
    {
        if (!TheBackgroundCanBeFramed)
        {
            return;
        }

        (int width, int height) = TheSizeOfTheBackground();

        Keep(Art().WhatIsKept.Dragged(
            across, down, width, height, CopyTheArtworkStep.MenuWidth, CopyTheArtworkStep.MenuHeight));
    }

    /// <summary>The middle of the picture at no zoom again.</summary>
    public void KeepTheMiddleOfTheBackground() => Keep(null);

    private void Keep(KeptPart? part)
    {
        // Nothing to frame, nothing kept: a slider moved over a colour or a
        // tiled picture must not leave a crop waiting for the next picture.
        ChangeTheArtwork(Art().Keeping(TheBackgroundCanBeFramed ? part : null));
    }

    private (string Path, int Width, int Height)? _theBackgroundMeasured;

    /// <summary>
    /// How big the background really is, asked once for each picture. A
    /// drag asks for it on every move of the pointer, and reading a file
    /// each time would make the picture trail the hand.
    /// </summary>
    private (int Width, int Height) TheSizeOfTheBackground()
    {
        string? path = Art().BackgroundPath;

        if (path is null)
        {
            return (0, 0);
        }

        if (_theBackgroundMeasured is not { } measured
            || !string.Equals(measured.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            ImageFacts facts = _machine.Pictures.Read(path);

            measured = facts.Readable ? (path, facts.Width, facts.Height) : (path, 0, 0);
            _theBackgroundMeasured = measured;
        }

        return (measured.Width, measured.Height);
    }

    /// <summary>The picture for the printed cover.</summary>
    public string? Cover
    {
        get => _artwork?.CoverPath;
        set => ChangeTheArtwork(Art() with { CoverPath = Tidy(value) });
    }

    /// <summary>The picture for the printed disc face.</summary>
    public string? DiscFace
    {
        get => _artwork?.DiscFacePath;
        set => ChangeTheArtwork(Art() with { DiscFacePath = Tidy(value) });
    }

    /// <summary>
    /// A font file of somebody's own for the menu, or nothing. The owner,
    /// 2026-11-20: the list of faces stays, and this goes beside it.
    /// </summary>
    /// <remarks>
    /// It goes onto the disc, so the menu wears it on a machine that has
    /// never had it. The face chosen in the list is what a machine too old
    /// to load it falls back to.
    /// </remarks>
    public string? MenuFontFile
    {
        get => _artwork?.MenuFontPath;
        set => ChangeTheArtwork(Art() with { MenuFontPath = Tidy(value) });
    }

    /// <summary>
    /// Looks at a font file before it is accepted, and says why not if not.
    /// </summary>
    public bool ThatFontIsUsable(string path)
    {
        WhyNotThatFont = _machine.Fonts?.WhyNot(path);
        Tell(nameof(WhyNotThatFont));

        return WhyNotThatFont is null;
    }

    /// <summary>Why the last font file picked was turned away, or nothing.</summary>
    public string? WhyNotThatFont { get; private set; }

    /// <summary>What plays behind the menu.</summary>
    public string? Music
    {
        get => _music;
        set
        {
            _music = Tidy(value);
            Tell(nameof(Music));
            Told();
        }
    }

    /// <summary>The manual, which the menu offers a button for.</summary>
    public string? Manual
    {
        get => _manual;
        set
        {
            _manual = Tidy(value);
            Tell(nameof(Manual));
            Told();
        }
    }

    /// <summary>A folder of extras that rides one disc of a set.</summary>
    public string? ExtrasFolder
    {
        get => _extrasFolder;
        set
        {
            _extrasFolder = Tidy(value);
            Tell(nameof(ExtrasFolder));
            Told();
        }
    }

    /// <summary>
    /// What has been chosen, in a line, for the window that does not show it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// So that somebody can see at a glance whether their disc has a
    /// background on it without opening anything.
    /// </para>
    /// <para>
    /// <strong>The files carried loose are in here although nobody chose
    /// them in that panel.</strong> They arrive when a folder holding several
    /// games is taken apart, and they were the one thing that reached the
    /// disc with nothing on the window ever mentioning it again: the manual,
    /// the extras, the tune and the artwork are all named here, and the total
    /// above counts the tree alone, so a file sitting loose was carried in
    /// silence. The line is about what the disc will have on it rather than
    /// about which panel it came from, and a file nobody can see is a file
    /// nobody can decide about.
    /// </para>
    /// </remarks>
    public string WhatElseIsChosen
    {
        get
        {
            List<string> got = [];

            if (DiscIcon is not null) { got.Add("an icon"); }
            if (Background is not null) { got.Add("a background"); }
            if (Music is not null) { got.Add("music"); }
            if (Manual is not null) { got.Add("a manual"); }
            if (ExtrasFolder is not null) { got.Add("extras"); }

            // His own words for them, from the question that asks about taking
            // a folder apart: "The one file sitting loose in it goes on the
            // disc either way." Said the same way here, so the line answers
            // the question that dialog raised rather than introducing a second
            // name for the same thing.
            if (_loose.Count > 0)
            {
                got.Add(_loose.Count == 1
                    ? "a file sitting loose"
                    : $"{_loose.Count} files sitting loose");
            }

            if (Cover is not null || DiscFace is not null) { got.Add("artwork to print"); }

            return got.Count switch
            {
                0 => "Nothing chosen. A disc with none of it is still a disc.",
                1 => "This disc has " + got[0] + ".",
                _ => "This disc has " + string.Join(", ", got.Take(got.Count - 1)) + " and " + got[^1] + "."
            };
        }
    }

    /// <summary>
    /// Why a picture cannot be used, or nothing.
    /// </summary>
    /// <remarks>
    /// Asked the moment a file is picked rather than at build time. The icon
    /// used to be checked when it was chosen and the background never was, so
    /// a file Windows cannot decode sailed through the whole window and failed
    /// at the end as "A generic error occurred in GDI+", which tells somebody
    /// nothing about which file is at fault.
    /// </remarks>
    public string? WhyNotThatPicture { get; private set; }

    private DiscArtwork Art() => _artwork ?? new DiscArtwork();

    private static string? Tidy(string? path) =>
        string.IsNullOrWhiteSpace(path) ? null : path;

    /// <summary>
    /// Everything a picture can be chosen for, which is announced together.
    /// </summary>
    /// <remarks>
    /// One record holds all five, so any change to it moves all five as far as
    /// the window is concerned. Named here rather than in each place that has
    /// to say so, because the second place that had to say so did not.
    /// </remarks>
    private static readonly string[] ThePicturesOnIt =
        [nameof(DiscIcon), nameof(Background), nameof(BackgroundAsIs), nameof(Cover), nameof(DiscFace),
         nameof(BackgroundColour), nameof(BackgroundColourText), nameof(WhatTheColourSays),
         nameof(HowTheBackgroundFits), nameof(WhatThatFitDoes),
         nameof(TheBackgroundCanBeFramed), nameof(BackgroundZoom), nameof(BackgroundAcross),
         nameof(BackgroundDown), nameof(WhatTheMenuKeeps), nameof(TheBackgroundIsFramed),
         nameof(MenuFontFile), nameof(HowTheSmallPictureIsDrawn), nameof(TheSmallPictureIsInOnePiece),
         nameof(TheSmallPictureIsTiled), nameof(TheTileInTheSmallPicture)];

    private void ChangeTheArtwork(DiscArtwork now)
    {
        _artwork = now.IsAnything || now.HowItFits != BackgroundFit.Fill ? now : null;

        foreach (string named in ThePicturesOnIt)
        {
            Tell(named);
        }

        Told();
    }

    /// <summary>
    /// Looks at a picture before it is accepted, and says why not if not.
    /// </summary>
    public bool ThatPictureIsUsable(string path)
    {
        ImageFacts facts = _machine.Pictures.Read(path);

        WhyNotThatPicture = facts.Readable ? null : facts.Why;
        Tell(nameof(WhyNotThatPicture));

        return facts.Readable;
    }

    /// <summary>
    /// What to ask before building, when the folder chosen already holds an
    /// earlier build of this disc, or nothing when it holds none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A build empties its disc folder and writes over its image, and did
    /// both without a word. discwright#98, 15 November 2026, and the owner's
    /// answer on the 20th: always ask.
    /// </para>
    /// <para>
    /// A method the window calls when the button is pressed, and not part of
    /// <see cref="MakeIt"/>: the screen never puts up a dialog, and a test
    /// that builds twice is not answering questions. The likely answer is to
    /// build, because building again after changing something is what the
    /// button is nearly always pressed for. Nothing is asked while the
    /// button could not build anyway.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Whether the drive has room for what is about to be built, asked
    /// before it starts. Nothing when there is room, or when the drive will
    /// not say.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The owner's own build, 21 November 2026: four games, 153 GB written
    /// over forty minutes onto a drive that could not take the last 37. A
    /// build writes every disc twice, the folder and an image as big again,
    /// and nothing had looked.
    /// </para>
    /// <para>
    /// A question and not a refusal. The sum cannot know what else will
    /// come or go on the drive, so it says the figures and somebody who
    /// knows better builds anyway. Shutting it is the other answer, so
    /// nothing is built by walking away from it.
    /// </para>
    /// </remarks>
    public AQuestion? WhetherThereIsRoom()
    {
        if (!CanMakeIt || _whatItWouldMake is not PlannedBuild build
            || RoomForABuild.For(build, _machine.Disk, OutputFolder) is not RoomForABuild room
            || room.IsEnough)
        {
            return null;
        }

        string drive = System.IO.Path.GetPathRoot(OutputFolder) is { Length: > 0 } root ? root.TrimEnd((char)92) : "that drive";

        return new AQuestion(
            $"There is not room on {drive} for this build.",
            $"It needs about {ByteSize.Describe(room.Needed)}: the disc "
                + (build.Discs == 1 ? "folder and an image of it" : "folders and an image of each")
                + $", which is as big again. {drive} has {ByteSize.Describe(room.Free)} free"
                + (room.ComesBack > 0
                    ? $", and {ByteSize.Describe(room.ComesBack)} more comes back from the earlier build this replaces"
                    : string.Empty)
                + $". That is {ByteSize.Describe(room.TooLittleBy)} short, and a drive that fills part way stops the build "
                + "with nothing finished. Make room, or cancel and choose another folder under WHERE TO SAVE THE ISO.",
            "Build anyway",
            "Cancel");
    }

    public AQuestion? WhatBuildingHereReplaces()
    {
        if (!CanMakeIt)
        {
            return null;
        }

        // Wherever an earlier build of these games may have put them: at
        // the top, and in a folder for each game. All of it goes when this
        // build is made, so all of it is counted here.
        WhatAnEarlierBuildLeft there = WhatAnEarlierBuildLeft.In(
            _machine.Disk, OutputFolder, Label, _games.Select(game => game.Name));

        if (!there.IsThere)
        {
            return null;
        }

        bool one = there.Folders.Count + there.Images.Count == 1;

        return new AQuestion(
            "Build over the disc that is already in this folder?",
            $"{OutputFolder} holds {there.InWords} from an earlier build. Building here deletes "
                + (one ? "it" : "them")
                + " and makes the disc again. To keep the earlier one, cancel and choose another folder.",
            "Build over it",
            "Cancel");
    }

    /// <summary>
    /// Whether there is anything on this screen worth losing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked by the window before it offers to throw it away. A screen with
    /// nothing on it needs no confirming, and being asked to confirm nothing
    /// teaches somebody to click through the question that matters.
    /// </para>
    /// <para>
    /// The files carried loose count. Every other extra was in this list and
    /// they were not, so a window holding nothing but a file sitting loose
    /// had Start a new disc grey, and starting again threw that file away
    /// anyway: the button said there was nothing to lose and then lost
    /// something. Found by the walk the moment the line above learned to name
    /// them, which is the whole argument for saying a thing out loud.
    /// </para>
    /// </remarks>
    public bool HasAnythingOnIt =>
        _games.Count > 0 || Label.Length > 0 || Made is not null || _artwork is not null
        || _manual is not null || _extrasFolder is not null || _music is not null
        || _loose.Count > 0;

    /// <summary>
    /// Whether starting again would change anything.
    /// </summary>
    /// <remarks>
    /// Pressed on a window with nothing on it, Start a new disc asked nothing
    /// and cleared nothing, which is correct and is indistinguishable from a
    /// button that does not work. Every other button in this window goes grey
    /// when it has nothing to do, and this was the one that stayed lit and
    /// said nothing at all.
    /// </remarks>
    public bool CanStartAgain => NothingIsRunning && HasAnythingOnIt;

    /// <summary>
    /// Whether there is a project worth keeping and somewhere to keep it.
    /// </summary>
    /// <remarks>
    /// The folder is part of the question rather than a refusal after the
    /// press: the recipe lands where the discs would, so with nowhere named
    /// there is nowhere to save, and a dead button says so the way every
    /// other button on this window does.
    /// </remarks>
    public bool CanSaveItForLater =>
        NothingIsRunning && HasAnythingOnIt && OutputFolder.Trim().Length > 0;

    /// <summary>Where the recipe went when it was saved, or null.</summary>
    public string? HowTheSaveWent { get; private set; }

    /// <summary>
    /// Writes <c>discproject.json</c> where the discs would go, and builds
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Asked for in discwright#98: saving a project to come back to used to
    /// mean building the whole thing first, minutes of discs for a file of
    /// recipe. The build writes the same file to the same place, so a saved
    /// project and a built one are opened the same way and there is no
    /// second kind of project file to teach anything about.
    /// </remarks>
    public void SaveItForLater()
    {
        if (!CanSaveItForLater)
        {
            return;
        }

        _machine.Builder.SaveTheRecipe(AsAProject(), Medium, OutputFolder);

        HowTheSaveWent = $"Saved. Open one you built finds it in {OutputFolder}.";
        Tell(nameof(HowTheSaveWent));
    }

    /// <summary>
    /// Clears this disc and leaves the machine alone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The games, the name, the artwork, the manual and everything the last
    /// run left behind all go, because they are this disc. <strong>Where the
    /// discs get built and what is in More options stay</strong>, because they
    /// are how this person works rather than what they are making, and having
    /// to find that folder again every time is its own small insult.
    /// </para>
    /// <para>
    /// It does nothing while a build or a burn is running. There is no sensible
    /// meaning for clearing the screen out from under a laser.
    /// </para>
    /// <para>
    /// <strong>And nothing at all when its own button is dead</strong>, which
    /// is the rule every other button on this window keeps. There is nothing
    /// on an empty disc to clear, and clearing it anyway took the refusal left
    /// over from the last thing that went wrong off the screen, which is a
    /// dead button quietly tidying away the only explanation somebody had.
    /// </para>
    /// <para>
    /// Found by the walk, the first time it could reach a window that was
    /// empty and still had something to say.
    /// </para>
    /// </remarks>
    public void StartAgain()
    {
        if (!CanStartAgain)
        {
            return;
        }

        _games.Clear();
        _addOns.Clear();
        _asFound.Clear();
        _addOnsAsFound.Clear();
        _tree.Clear();
        _loose.Clear();

        // Through the things that already say they changed, rather than
        // through the fields behind them. Cleared as fields, the panel went
        // on showing a manual that was no longer on the disc, and the line on
        // the window went on saying the disc had one. A second list of what
        // to announce is a list that drifts from the first, which is how this
        // was missed: every one of these has a setter that announces, and
        // none of them was in the list below.
        Manual = null;
        ExtrasFolder = null;
        Music = null;
        _backgroundColourText = string.Empty;
        ChangeTheArtwork(new DiscArtwork());

        Label = string.Empty;

        // A new disc is a new question, so the drives get to answer it again.
        _theSizeWasChosen = false;

        // And a folder named after the disc that has just gone is not where
        // the next one should land. Only a suggested one: a folder somebody
        // chose is how they work and stays.
        if (_theFolderWasSuggested)
        {
            OutputFolder = string.Empty;
        }

        Made = null;
        AboutTheImage = null;
        ItWentWrong = null;
        HowTheDiscCameOut = null;
        HowTheSetWent = null;
        HowTheSaveWent = null;
        WhatDidNotComeBack = null;
        TheImages = [];

        foreach (string gone in new[]
        {
            nameof(Made), nameof(TheSetToBurn), nameof(AboutTheImage), nameof(ItWentWrong), nameof(HowTheDiscCameOut),
            nameof(HowTheSetWent), nameof(HowTheSaveWent), nameof(WhatDidNotComeBack), nameof(TheImages), nameof(HasAnythingOnIt)
        })
        {
            Tell(gone);
        }

        // Which puts the window back to the sentence it opens with, and works
        // the refusal out again from nothing.
        //
        // Reconsider rather than leaning on the name being cleared to do it.
        // Clearing a name that is already empty changes nothing and so
        // reconsiders nothing, and an empty window was left showing "The disc
        // needs a name" from when it had games and no name.
        Reconsider();
        Started();
        LookForImages();
    }

    /// <summary>How far through a set the last burn got, or null.</summary>
    public TheSetSoFar? HowTheSetWent { get; private set; }

    /// <summary>
    /// Writes a whole set, one disc at a time, in order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A set is only a set once every disc is written</strong>, and the
    /// things most likely to go wrong are burning them out of order and losing
    /// count. So this walks them: disc 1, then ask, disc 2, then ask.
    /// </para>
    /// <para>
    /// <strong>The drive is settled once, before the first disc.</strong> A set
    /// that wanders between two writers is a set whose discs were written by
    /// two different lasers at two different speeds.
    /// </para>
    /// <para>
    /// <strong>A disc that does not read back stops the walk.</strong> Disc 2
    /// is no use at all if disc 1 came out wrong, and carrying on would spend
    /// the rest of somebody's blanks on a set that is already broken.
    /// </para>
    /// </remarks>
    /// <param name="theNextBlankPlease">
    /// Asked between discs, to take the one just written out and put a blank
    /// in. Answering no stops the walk where it is, which is a decision and not
    /// a failure.
    /// </param>
    /// <param name="carryOnFromDisc">
    /// Asked once, at the start, when the note beside the discs says some of
    /// them are already written: carry on from that disc, or start again from
    /// the first. Never decided here, because a note saying a disc was written
    /// and checked is not the same as that disc still being in the room.
    /// </param>
    public async Task BurnTheSet(
        IReadOnlyList<string> images,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> chooseADrive,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> whichToErase,
        Func<Recorder, Task<bool>> shutTheTray,
        Func<int, int, Recorder, Task<bool>> theNextBlankPlease,
        Func<int, int, Task<bool>> carryOnFromDisc,
        CancellationToken ct = default)
    {
        if (IsWorking || images.Count == 0)
        {
            return;
        }

        IsWorking = true;
        ItWentWrong = null;
        HowTheDiscCameOut = null;
        HowTheSetWent = null;
        Started();

        int written = 0;
        int broughtForward = 0;
        string? stopped = null;

        try
        {
            // What is beside the discs now, which is what decides whether a
            // note left there before is still about this set.
            NotedImage[] now =
            [
                .. images.Select(each => new NotedImage(
                    System.IO.Path.GetFileName(each), _machine.HowBigIs(each)))
            ];

            // Asked before a drive is settled. Somebody who is about to carry
            // on from disc 3 should not have to answer questions about which
            // drive first, and the disc that has to fit in it is disc 3's.
            TheNoteBesideTheDiscs? note = TheNoteBesideTheDiscs.Read(
                _machine.SetsPlace.WhatItSays(OutputFolder));

            if (note is not null
                && note.PartWayThrough
                && note.Matches(Label, now)
                && await carryOnFromDisc(note.CarryOnFrom, images.Count))
            {
                broughtForward = note.Written;
                written = broughtForward;
            }

            // Once, before the first disc this run writes, and not at all
            // when the window already says which drive.
            Recorder? drive = _theDrive is Recorder picked
                ? TheDriveIn(await _settling.OnThatOne(
                    picked.Drive, _machine.HowBigIs(images[broughtForward]), whichToErase, ct))
                : TheDriveIn(await _settling.OnWhicheverIsReady(
                    _machine.HowBigIs(images[broughtForward]), chooseADrive, whichToErase, ct));

            if (drive is null)
            {
                stopped = ItWentWrong?.Headline ?? "no drive was chosen";
                return;
            }

            for (int at = broughtForward; at < images.Count; at++)
            {
                ct.ThrowIfCancellationRequested();

                // Which disc of how many, said the same way by every line
                // below it, the ones about writing and the ones about reading
                // it back. The window has nothing else telling somebody where
                // in the half hour they are.
                WhichDisc.OfASet where = new(at + 1, images.Count);

                // The first disc this run writes goes into the drive that was
                // just settled, whether that is disc 1 or disc 3. It is the
                // ones after it that need somebody to swap.
                if (at > broughtForward)
                {
                    Say(where.WaitingForABlank, 0);

                    if (!await theNextBlankPlease(where.Number, where.OfThem, drive))
                    {
                        stopped = "nobody put the next blank in";
                        return;
                    }

                    // Asked again, because what is in that drive has changed:
                    // the disc just written came out and something else went
                    // in, and it may not be blank.
                    switch (await _settling.OnThatOne(
                        drive.Drive, _machine.HowBigIs(images[at]), whichToErase, ct))
                    {
                        case Outcome<Recorder>.Ok(Recorder next):
                            drive = next;
                            break;

                        // Nobody was asked which drive, so there is no answer
                        // here but the disc taking it or a refusal saying why
                        // not. The sentence somebody reads is the refusal's
                        // own, which names the drive and the disc in it.
                        case Outcome<Recorder>.No refused:
                            ItWentWrong = refused.Refusal;
                            stopped = refused.Refusal.Headline;
                            return;
                    }
                }

                Say(where.InWords, 0);

                Progress<BurnProgress> saying = new(step =>
                    Say(where.Saying(step.ToString()), step.HowFar ?? 0));

                switch (await _machine.Burner.Burn(images[at], drive, saying, ct))
                {
                    case Outcome<BurnedDisc>.No refused:
                        ItWentWrong = refused.Refusal;
                        stopped = refused.Refusal.Headline;
                        return;
                }

                // Written, so a blank of that size is gone, however the
                // disc reads back.
                ABlankWasUsed();

                // Said before the tray, which may wait for a hand, so the
                // window stops repeating the last thing the burn reported.
                // It said "flushing the drive's buffer" for ten minutes on an
                // older laptop while this went on.
                Say(where.Saying("waiting for the disc to come back"), 0);

                if (!await shutTheTray(drive)
                    || await _comingBack.For(drive, said => Say(where.Saying(said), 0), ct) is null)
                {
                    ItWentWrong = Refusals.TheDiscWasNotChecked(drive.Drive);
                    stopped = "the disc could not be read back";
                    return;
                }

                await HowTheDiscCameBack(drive, where, ct);

                // Disc 2 is no use at all if disc 1 came out wrong.
                if (HowTheDiscCameOut is not DiscCheck came || !came.Good)
                {
                    stopped = HowTheDiscCameOut?.InWords ?? "the disc did not read back";
                    return;
                }

                written++;

                // After the disc was read back, and never before it. A note
                // written on the way in would count a coaster as done, and the
                // whole point of the note is that it gets believed later.
                KeepThePlace(now, written);
            }
        }
        catch (OperationCanceledException)
        {
            stopped = "it was stopped";
        }
        finally
        {
            HowTheSetWent = new TheSetSoFar(written, images.Count, stopped, broughtForward);

            lock (_oneAtATime)
            {
                IsWorking = false;
                Doing = string.Empty;
                HowFar = 0;
            }

            TellHowTheBurnWent();
            Started();
            await LookAgainIfADiscMovedMeanwhile();
        }
    }

    /// <summary>
    /// Moves the note beside the discs on by one, after a disc burned on its
    /// own read back clean.
    /// </summary>
    /// <remarks>
    /// Only a note that still describes the images sitting there now, by the
    /// same rule the walk through uses to decide whether to believe one. A
    /// note about some earlier build of the set is already ignored for
    /// carrying on, and writing to it would make it look current.
    /// </remarks>
    private void TickThatDiscOff(string image)
    {
        if (OutputFolder.Length == 0)
        {
            return;
        }

        NotedImage[] now =
        [
            .. TheImages.Select(each => new NotedImage(
                System.IO.Path.GetFileName(each), _machine.HowBigIs(each)))
        ];

        TheNoteBesideTheDiscs? note = TheNoteBesideTheDiscs.Read(
            _machine.SetsPlace.WhatItSays(OutputFolder));

        if (note is null || !note.Matches(Label, now))
        {
            return;
        }

        if (note.WithThatOneDone(System.IO.Path.GetFileName(image)) is TheNoteBesideTheDiscs moved)
        {
            _machine.SetsPlace.Leave(OutputFolder, moved.AsText());
        }
    }

    /// <summary>
    /// Leaves the note saying how far the set has got.
    /// </summary>
    private void KeepThePlace(IReadOnlyList<NotedImage> images, int written) =>
        _machine.SetsPlace.Leave(
            OutputFolder,
            new TheNoteBesideTheDiscs(Label, images, written).AsText());

    /// <summary>
    /// The drive out of an answer, having kept the refusal when there is one.
    /// </summary>
    /// <remarks>
    /// Nothing to show for a question answered with no drive, which is a
    /// decision somebody took and not a thing that went wrong. A refusal is
    /// kept rather than shown here, because every caller is inside a build
    /// whose end tells the window about it.
    /// </remarks>
    private Recorder? TheDriveIn(Settled settled)
    {
        if (settled is Settled.No refused)
        {
            ItWentWrong = refused.Why;
        }

        return (settled as Settled.ThisOne)?.Drive;
    }

    /// <inheritdoc cref="TheDriveIn(Settled)"/>
    /// <remarks>
    /// The same thing for a drive somebody already named, where there is no
    /// question to answer with no drive and so no third answer to handle.
    /// </remarks>
    private Recorder? TheDriveIn(Outcome<Recorder> settled)
    {
        if (settled is Outcome<Recorder>.No refused)
        {
            ItWentWrong = refused.Refusal;
        }

        return settled is Outcome<Recorder>.Ok(Recorder ready) ? ready : null;
    }

    /// <summary>
    /// Reads the disc back and says how it came out, or says that nobody can
    /// speak for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both ways in land here: a disc burned on its own, known by the image it
    /// came from, and a disc of a set, known by its number. What differs
    /// between them is in <see cref="WhichDisc"/> and nowhere else.
    /// </para>
    /// <para>
    /// <strong>It announces either way.</strong> The half of this that read a
    /// disc of a set against the list the disc carries set the answer and said
    /// nothing, so a window showing the previous disc's result went on showing
    /// it. Nothing caught that, because nothing can press Burn.
    /// </para>
    /// </remarks>
    private async Task HowTheDiscCameBack(Recorder onto, WhichDisc which, CancellationToken ct)
    {
        switch (await _reading.HowItCameOut(onto, which, Made, ct))
        {
            case Outcome<DiscCheck>.Ok(DiscCheck how):
                HowTheDiscCameOut = how;
                break;

            case Outcome<DiscCheck>.No refused:
                // The disc is written, and that is all that can honestly be
                // said about it.
                HowTheDiscCameOut = null;
                ItWentWrong = refused.Refusal;
                break;
        }

        TellHowTheBurnWent();
    }

    /// <summary>
    /// Says that how the last burn came out has changed, in all four of the
    /// ways the window reads it: the two records, the line and its colour.
    /// </summary>
    /// <remarks>
    /// One place, because the line follows two records and whichever of them
    /// moves, the line has moved. Said from every start and every end through
    /// <see cref="Started"/>, and from the middle of a set when a disc has
    /// just been read back, which is the one change that happens between.
    /// </remarks>
    private void TellHowTheBurnWent()
    {
        Tell(nameof(HowTheDiscCameOut));
        Tell(nameof(HowTheSetWent));
        Tell(nameof(HowTheBurnWent));
        Tell(nameof(TheBurnWentBadly));
    }

    /// <summary>One line about what is happening, and how far through it is.</summary>
    private void Say(string doing, double far)
    {
        bool worth;

        lock (_oneAtATime)
        {
            worth = IsWorking;

            if (worth)
            {
                Doing = doing;
                HowFar = far;
            }
        }

        if (worth)
        {
            Tell(nameof(Doing));
            Tell(nameof(HowFar));
        }
    }

    /// <summary>Adds a game, and keeps the order they were added in.</summary>
    /// <param name="asAddOns">
    /// Files from the same folder to attach to it: a patch, a DLC, a mod. They
    /// land in the game's Add-ons folder and stay dead until the game is
    /// installed.
    /// </param>
    public void Add(GameFolder game, IEnumerable<LeftBehind>? asAddOns = null)
    {
        _games.Add(game);
        _asFound.Add(game);

        // Its updates lowest build first, which is the order they are
        // installed in and not the order their file names sort in.
        foreach (AddOn addOn in AddOn.WithUpdatesInTheirOrder(
                     [.. (asAddOns ?? []).Select(each => each.AttachedTo(_games.Count - 1, game.Path))]))
        {
            _addOns.Add(addOn);
            _addOnsAsFound.Add(addOn);
        }

        // Its branch of the tree, over everything that was found.
        _tree.Add(TheTreeOfWhatGoesOn.For(
            game,
            _addOnsAsFound.Where(addOn => addOn.BelongsTo == _games.Count - 1),
            TakeTheTicksAsTheAnswer));

        // The first game names the disc, because that is nearly always what
        // somebody would have typed. Only the first: renaming it afterwards is
        // their decision, and a second game must not overwrite it.
        if (_games.Count == 1 && HasNoName)
        {
            _label = game.Name.ToUpperInvariant();
            Tell(nameof(Label));
        }

        // And it names the folder, for the same reason.
        //
        // v1 did this, so nobody ever met "There is nowhere to put it".
        // Leaving the box empty made a refusal out of a question that had
        // not been asked yet, and it was reported three times as a stale
        // message about the disc rather than about the box.
        //
        // The whole rule is that the box is empty, with no "only the first
        // game" beside it as the name above has. A game called ":" leaves
        // nothing worth suggesting, and the next one along should be allowed
        // to fill in a box that first one left empty.
        if (OutputFolder.Length == 0
            && _machine.Places?.ForAGameCalled(game.Name) is string where)
        {
            OutputFolder = where;
            _theFolderWasSuggested = true;
        }

        Reconsider();
    }

    /// <summary>Whether that folder is on the list already, by its path.</summary>
    /// <remarks>
    /// By path and nothing else. Two different folders can hold the same game,
    /// and somebody may well want both on one disc, an original and a
    /// re-release. The same folder twice is the thing nobody wants.
    /// </remarks>
    public bool IsOnAlready(string folder) => WhereIs(folder) >= 0;

    /// <summary>
    /// What a folder that is on the list already holds that the disc does
    /// not, or nothing when the folder is not on the list at all.
    /// </summary>
    /// <remarks>
    /// Against what was found when the folder went on, less what has been
    /// taken off since, which is what Remove changes. An unticked row is not
    /// missing: it is still on the window and one click from coming back.
    /// </remarks>
    /// <param name="fresh">The folder as it reads now.</param>
    /// <param name="left">What sat beside the game in it, as it reads now.</param>
    public TheSameFolderAgain? WhatIsMissingFrom(GameFolder fresh, IReadOnlyList<LeftBehind> left)
    {
        int at = WhereIs(fresh.Path);

        if (at < 0)
        {
            return null;
        }

        GameFolder known = _asFound[at];

        ListedFile[] files = [.. fresh.Files.Where(file => !known.Holds(file.RelativePath))];

        LeftBehind[] beside =
        [
            .. left.Where(each =>
                !known.Holds(each.File.RelativePath)
                && !_addOnsAsFound.Any(addOn =>
                    addOn.BelongsTo == at
                    && string.Equals(
                        addOn.Path,
                        System.IO.Path.Combine(known.Path, each.File.RelativePath),
                        StringComparison.OrdinalIgnoreCase)))
        ];

        return new TheSameFolderAgain(at, known.Name, files, beside);
    }

    /// <summary>
    /// Puts back what a folder holds that the disc does not: the game's own
    /// files as its files, and what sat beside it the way it would have gone
    /// on the first time, an add-on as an add-on and the rest with the game.
    /// </summary>
    /// <remarks>
    /// Exactly what was missing and nothing else, so nothing goes on twice.
    /// The game's branch of the tree is built again from what it now holds,
    /// the same way a row coming off rebuilds it, which is the only way the
    /// tree and the list cannot drift apart.
    /// </remarks>
    public void PutBack(TheSameFolderAgain missing)
    {
        int at = missing.At;

        if (at < 0 || at >= _asFound.Count)
        {
            return;
        }

        GameFolder known = _asFound[at];

        _asFound[at] = WhatWasLeftBehind.Taking(
            known with { Files = [.. known.Files, .. missing.Files] },
            missing.Left.Where(each => each.Kind == LeftBehindKind.LooksLikeSomethingToKeep));

        foreach (LeftBehind each in missing.Left.Where(each => each.Kind == LeftBehindKind.LooksLikeAnAddOn))
        {
            _addOnsAsFound.Add(each.AttachedTo(at, known.Path));
        }

        // Remembering what the old rows held, so putting two patches back
        // does not re-tick a row somebody had left off or shut what they
        // had open. What is put back is new, and arrives ticked.
        _tree[at] = TheTreeOfWhatGoesOn.For(
            _asFound[at],
            _addOnsAsFound.Where(addOn => addOn.BelongsTo == at),
            TakeTheTicksAsTheAnswer).Remembering(_tree[at]);

        TakeTheTicksAsTheAnswer();
    }

    private int WhereIs(string folder) => _asFound.FindIndex(game => TheSamePlace(game.Path, folder));

    /// <summary>
    /// Whether two paths name one folder, however they were typed: a trailing
    /// separator and the case of the letters are not a different folder.
    /// </summary>
    private static bool TheSamePlace(string one, string other) =>
        string.Equals(
            one.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
            other.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Takes one thing off the list: the one that is picked, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tick and this button mean different things. Unticked is <em>not on
    /// this disc</em>, still listed, and one click from coming back. Removed
    /// is <em>gone</em>, and comes back only by adding the folder again.
    /// </para>
    /// <para>
    /// <strong>Nothing picked takes nothing off.</strong> This used to clear
    /// out everything unticked in that case, which made one button two
    /// buttons: which of the two somebody got depended on whether a row
    /// happened to be selected, and the window never said as much. That
    /// gesture is worth keeping and is <see cref="TakeOffWhatIsLeftOff"/>,
    /// which has a button of its own that says what it will take.
    /// </para>
    /// <para>
    /// The first version of this took the whole game whatever was selected,
    /// so clicking a file and pressing Remove emptied the window.
    /// </para>
    /// </remarks>
    public void TakeOff(AThingOnTheDisc? selected)
    {
        if (selected is not null)
        {
            TakeOffJustThis(selected);
            TakeTheTicksAsTheAnswer();
        }
    }

    /// <summary>
    /// Takes off everything that has been turned off, and nothing that has not.
    /// </summary>
    /// <remarks>
    /// The tidying-up gesture after deciding what is not going on the disc.
    /// Its own thing rather than what Remove falls back to, so that it can be
    /// offered only when there is something for it to do and can say how much
    /// that is before it is pressed.
    /// </remarks>
    public void TakeOffWhatIsLeftOff()
    {
        // Settled before anything comes off, and by position rather than by
        // row, for the two reasons written down where it is worked out.
        IReadOnlyList<int> wholly = _tree.WhichGamesAreWhollyOff();

        // Gathered as a position and a path rather than as a row, for the
        // same reason the games above are gathered as positions.
        //
        // Taking a row off rebuilds the branch it was in, so every row
        // gathered before the first one went belongs to a branch the tree no
        // longer holds, and TakeOffJustThis cannot find a branch that is not
        // there any more: it returns, quietly, having done nothing. So two
        // rows turned off came off one at a time, and the button that says
        // "Remove the 2 things left off" took one of them off and left the
        // other sitting there with its tick still clear.
        //
        // A position and a path survive the rebuild, because that is what the
        // rebuild is built from.
        List<(int At, string Path, bool AnAddOn)> going = [];

        for (int at = 0; at < _tree.Count; at++)
        {
            foreach (AThingOnTheDisc thing in _tree[at].EveryThing())
            {
                if (thing.IsOn == false)
                {
                    going.Add((at, thing.ItsPath, thing.What == TheTreeOfWhatGoesOn.AnAddOn));
                }
            }
        }

        foreach ((int at, string path, bool anAddOn) in going)
        {
            TakeTheRowOff(at, path, anAddOn);
        }

        // A game with every row unticked is itself unticked, and goes.
        // Highest first, so taking one off does not move the next.
        foreach (int at in wholly.OrderByDescending(each => each))
        {
            if (at < _games.Count)
            {
                Remove(_games[at]);
            }
        }

        TakeTheTicksAsTheAnswer();
    }

    private void TakeOffJustThis(AThingOnTheDisc thing)
    {
        int at = _tree.IndexOf(thing.TheGameItIsIn);

        // A part of an installer is not taken off by itself, whoever asks:
        // the installer without one of its parts installs nothing.
        if (at < 0 || at >= _games.Count || thing.IsOnlyToLookAt)
        {
            return;
        }

        if (thing.IsAWholeGame)
        {
            Remove(_games[at]);
            return;
        }

        // A file's cross takes the file, a folder's cross takes everything
        // under it, and a file is a folder of one here: EveryThing on a
        // row with nothing under it yields the row itself.
        //
        // It used to hand the folder's own path to TakeTheRowOff, which
        // removes a file by its exact path, and no file carries a folder's
        // path, so the cross on Add-ons did nothing at all, silently
        // (watching the owner, 2026-11-09).
        //
        // Gathered as paths before the first removal, because taking a row
        // off rebuilds the branch and rows gathered from it die with it,
        // which is the "Remove the 2 things left off" lesson.
        (string Path, bool AnAddOn)[] going =
        [
            .. thing.EveryThing().Select(each =>
                (each.ItsPath, each.What == TheTreeOfWhatGoesOn.AnAddOn))
        ];

        foreach ((string path, bool anAddOn) in going)
        {
            TakeTheRowOff(at, path, anAddOn);
        }

        // A game with nothing left in it is not on the disc, so it does
        // not stay on the list as a name with no files and no size.
        if (_asFound[at].Files.Count == 0 && !_addOnsAsFound.Any(addOn => addOn.BelongsTo == at))
        {
            Remove(_games[at]);
        }
    }

    /// <summary>
    /// Takes one row off the game at that position, by the path the row has
    /// inside it.
    /// </summary>
    /// <remarks>
    /// By position and path rather than by row, so that it can be called
    /// again after the branch has been rebuilt. A row is an object in a tree
    /// that is thrown away and built again every time anything comes off it;
    /// a position and a path are what it is built from.
    /// </remarks>
    /// <remarks>
    /// The bound below cannot be reached from either caller today: the tree
    /// and the list of games are the same length, and the whole games that go
    /// are taken off after the rows and highest first. It stays because a
    /// position is the only thing holding those two lists together, and a
    /// mutation pass will go on reporting its second half as a fault nothing
    /// objects to, which is the honest answer rather than a missing test.
    /// </remarks>
    private void TakeTheRowOff(int at, string path, bool anAddOn)
    {
        if (at < 0 || at >= _games.Count)
        {
            return;
        }

        if (anAddOn)
        {
            _addOnsAsFound.RemoveAll(addOn =>
                addOn.BelongsTo == at && string.Equals(addOn.Path, path, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _asFound[at] = _asFound[at].Without(path);
        }

        // Built again from what is left, which is the only way the tree and
        // the list cannot drift apart, and remembering what the rows it
        // replaces held: which were open, and which were turned off.
        _tree[at] = TheTreeOfWhatGoesOn.For(
            _asFound[at],
            _addOnsAsFound.Where(addOn => addOn.BelongsTo == at),
            TakeTheTicksAsTheAnswer).Remembering(_tree[at]);
    }

    /// <summary>Takes a game back off the list, and its add-ons with it.</summary>
    /// <remarks>
    /// An add-on remembers which game it belongs to by where that game sits in
    /// the list, so taking one out moves every game after it. Without this, a
    /// patch for the game that was removed would quietly become a patch for
    /// the next one along.
    /// </remarks>
    public void Remove(GameFolder game)
    {
        int which = _games.IndexOf(game);

        if (which < 0)
        {
            return;
        }

        _games.RemoveAt(which);
        _asFound.RemoveAt(which);
        _tree.RemoveAt(which);
        // Both lists renumbered the same way, by the same sum, which is now
        // one thing that can be asked rather than the same loop written twice.
        Replace(_addOnsAsFound, AddOn.AfterTheGameAt(_addOnsAsFound, which));
        Replace(_addOns, AddOn.AfterTheGameAt(_addOns, which));

        Reconsider();
    }

    // ------------------------------------------------- the order of the games
    //
    // discwright#98: the menu lists the games in the order they were added,
    // and the only way to change it was to take them all off and add them
    // again in the order wanted. The list is the order: what moves here
    // moves in the menu, in the project file and across the discs of a set.

    /// <summary>
    /// The order names are sorted in: no capitals, and a number read as a
    /// number, so "Gothic 2" comes before "Gothic 10". The same on every
    /// machine, whatever language it is set to.
    /// </summary>
    private static readonly StringComparer ByName = StringComparer.Create(
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.NumericOrdering);

    /// <summary>Whether the picked row's game can go one place up.</summary>
    public bool CanMoveTheGameUp => !IsWorking && GameOf(_selected) > 0;

    /// <summary>Whether the picked row's game can go one place down.</summary>
    public bool CanMoveTheGameDown =>
        !IsWorking && GameOf(_selected) is int at && at >= 0 && at < _tree.Count - 1;

    /// <summary>
    /// Where the picked row is among the things it can change places with,
    /// and how many of those there are: an add-on among its game's add-ons,
    /// and anything else as its game among the games.
    /// </summary>
    private (int At, int Of) WhereThePickedIs()
    {
        int game = GameOf(_selected);

        // A file, a part or a folder inside a game is in no order, and the
        // arrows are grey for it: they moved its whole game, and on the
        // recording of 21 November 2026 that read as nothing happening and
        // the list jumping to the top.
        if (game < 0 || !HasAnOrder(_selected))
        {
            return (-1, 0);
        }

        if (_selected!.What != TheTreeOfWhatGoesOn.AnAddOn)
        {
            return (game, _tree.Count);
        }

        List<AThingOnTheDisc> its = TheAddOnRowsOf(game);

        return (its.FindIndex(each => ReferenceEquals(each, _selected)), its.Count);
    }

    private List<AThingOnTheDisc> TheAddOnRowsOf(int game) =>
        [.. _tree[game].EveryThing().Where(each => each.What == TheTreeOfWhatGoesOn.AnAddOn)];

    /// <summary>Whether what is picked can go one place up.</summary>
    public bool CanMoveThePickedUp => !IsWorking && WhereThePickedIs().At > 0;

    /// <summary>Whether what is picked can go one place down.</summary>
    public bool CanMoveThePickedDown =>
        !IsWorking && WhereThePickedIs() is var (at, of) && at >= 0 && at < of - 1;

    /// <summary>
    /// Whether anything on the window has an order that could be another
    /// order: two games, or a game with two add-ons. The two arrows beside
    /// the list of ways to sort show only then, so a disc of one game with
    /// nothing under it has no dead buttons in its corner.
    /// </summary>
    public bool ThereIsAnOrderToChange =>
        _tree.Count > 1 || Enumerable.Range(0, _tree.Count).Any(game => TheAddOnRowsOf(game).Count > 1);

    /// <summary>
    /// Moves what is picked one place up or down: an add-on among its own
    /// game's add-ons, and anything else as its whole game among the games.
    /// </summary>
    /// <remarks>
    /// The two arrows and Alt with an arrow moved only games. An add-on
    /// could be dragged since the day before, and dragging needs a mouse and
    /// has to be guessed at, so the visible way and the keyboard's way move
    /// whatever dragging moves.
    /// </remarks>
    /// <param name="by">Minus one for up, one for down.</param>
    /// <returns>The moved row, for the window to keep picked, or nothing when nothing moved.</returns>
    public AThingOnTheDisc? MoveThePicked(int by)
    {
        if (!HasAnOrder(_selected))
        {
            return null;
        }

        if (_selected is not { What: TheTreeOfWhatGoesOn.AnAddOn } picked)
        {
            return MoveTheGame(by);
        }

        (int from, int of) = WhereThePickedIs();
        int to = from + Math.Sign(by);

        if (IsWorking || by == 0 || from < 0 || to < 0 || to >= of)
        {
            return null;
        }

        return MoveTheAddOnTo(picked, TheAddOnRowsOf(GameOf(picked))[to], after: by > 0);
    }

    /// <summary>
    /// Whether there is anything to sort: two games or more, and nothing
    /// running. The list of ways shows only then.
    /// </summary>
    public bool CanSortTheGames => !IsWorking && _tree.Count > 1;

    /// <summary>Every way to put the games in order, for the list.</summary>
    public IReadOnlyList<AWayToSort> TheWaysToSort { get; } = AWayToSort.All;

    // Whether somebody put the games in an order of their own, by moving
    // one, since the last time they were sorted. A sort would replace it.
    private bool _theOrderIsSomebodysOwn;

    /// <summary>
    /// What to ask before sorting, when the games are in an order somebody
    /// arranged by hand and sorting would change it, or nothing.
    /// </summary>
    /// <remarks>
    /// The owner, on the recording of 2026-11-20: a sort overrides the order
    /// arranged by dragging, so there should be a warning first. Only when
    /// there is such an order to lose, and only when this sort would change
    /// it: asking every time teaches people to press Enter. The likely
    /// answer is to sort, because that is what was just chosen.
    /// </remarks>
    public AQuestion? WhatSortingWouldLose(HowToSort how) =>
        !IsWorking && _theOrderIsSomebodysOwn && !InOrder(TheGamesInOrder(how))
            ? new AQuestion(
                "Sort the games, and lose the order you put them in?",
                "You moved games into an order of your own. Sorting replaces it, and the only way back is to "
                    + "move them again.",
                "Sort them",
                "Keep my order")
            : null;

    /// <summary>
    /// Moves one game to where another is: before it, or after. For a game
    /// dragged in the list and let go over another.
    /// </summary>
    /// <param name="moved">Any row of the game that is moved.</param>
    /// <param name="onto">Any row of the game it was let go over.</param>
    /// <param name="after">Whether it goes after that game rather than before it.</param>
    /// <returns>The moved game's row, for the window to keep picked, or nothing when nothing moved.</returns>
    public AThingOnTheDisc? MoveTheGameTo(AThingOnTheDisc? moved, AThingOnTheDisc? onto, bool after)
    {
        int from = GameOf(moved);

        if (IsWorking || TheOrderWith(from, GameOf(onto), after) is not int[] order)
        {
            return null;
        }

        AThingOnTheDisc game = _tree[from];
        _theOrderIsSomebodysOwn = true;

        return PutInOrder(order, game);
    }

    /// <summary>
    /// The games' order with one of them moved to where another is, or
    /// nothing when that is the order they are already in.
    /// </summary>
    private int[]? TheOrderWith(int from, int target, bool after)
    {
        if (from < 0 || target < 0)
        {
            return null;
        }

        List<int> order = [.. Enumerable.Range(0, _tree.Count)];
        order.RemoveAt(from);

        // Where the game it was let go over is, now that the moved one is out.
        int at = order.IndexOf(target);
        order.Insert(target == from ? Math.Min(from, order.Count) : (after ? at + 1 : at), from);

        return InOrder([.. order]) ? null : [.. order];
    }

    /// <summary>
    /// Lets what is carried go over a row: it takes that row's place. Going
    /// down the list it lands after the one it was let go over, going up it
    /// lands before it, so either way it ends where that one was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It went by halves: before the thing when over its upper half, after
    /// it over its lower. For a game that was the half of the whole game,
    /// files and all, so the owner, on a recording of 21 November 2026,
    /// carried the first game onto the second game's name and was refused:
    /// the name is in the upper half, which means before it, which is where
    /// the first game already was. To put it below he had to aim at the
    /// files further down. "It should be possible, the arrow is really hard
    /// to set up." Carried upward onto the same name, it worked.
    /// </para>
    /// <para>
    /// With no halves there is nowhere over another game, or over another of
    /// a game's add-ons, where letting go does nothing, so wherever the
    /// pointer says yes it means it and it says yes over the whole of them.
    /// </para>
    /// </remarks>
    /// <returns>The moved row, for the window to keep picked, or nothing when nothing moved.</returns>
    public AThingOnTheDisc? LetGoOver(AThingOnTheDisc? carried, AThingOnTheDisc? over)
    {
        if (!MayBeLetGoOver(carried, over))
        {
            return null;
        }

        if (carried!.What != TheTreeOfWhatGoesOn.AnAddOn)
        {
            return MoveTheGameTo(carried, over, after: GameOf(carried) < GameOf(over));
        }

        List<AThingOnTheDisc> its = TheAddOnRowsOf(GameOf(carried));

        return MoveTheAddOnTo(
            carried,
            over,
            after: its.FindIndex(each => ReferenceEquals(each, carried)) < its.FindIndex(each => ReferenceEquals(each, over)));
    }

    /// <summary>
    /// Whether a row is one of the two things that have an order: a game,
    /// by its own row, or an add-on. The menu lists games and, under each,
    /// its add-ons. Nothing else on the list is in any order that reaches
    /// the disc.
    /// </summary>
    public static bool HasAnOrder(AThingOnTheDisc? row) =>
        row is not null && (row.IsAWholeGame || row.What == TheTreeOfWhatGoesOn.AnAddOn);

    /// <summary>
    /// What a drag that starts on a row carries: the row itself when it is a
    /// game's own row or an add-on, and nothing from anywhere else.
    /// </summary>
    /// <remarks>
    /// A drag from any row inside a game used to carry the whole game. On
    /// the recording of 21 November 2026 the owner took hold of an
    /// installer's part and of an extra, meaning to move those, and what he
    /// was carrying without knowing it was the game: no-entry over every row
    /// near it, and no way to tell why. "Should a person even be able to
    /// move this up and down? There is no reason." There is none: a file
    /// inside a game is in no order that reaches the disc. So it does not
    /// start a drag, and the game is moved by its own row.
    /// </remarks>
    public static AThingOnTheDisc? WhatADragCarries(AThingOnTheDisc? row) => HasAnOrder(row) ? row : null;

    /// <summary>
    /// Whether what is carried would move if it were let go over a row. The
    /// window asks while the drag is in the air, and wears no-entry where
    /// the answer is no.
    /// </summary>
    /// <remarks>
    /// An add-on moves among its own game's add-ons and nowhere else: not
    /// above its game, not into another game, not onto the folder it is in.
    /// A game moves to where another game is, so over its own rows, which
    /// is the whole list when it is the only game, there is nowhere to go.
    /// The owner, 20 November 2026, dragging in a list of one game: nothing
    /// happened and nothing said why.
    /// </remarks>
    public bool MayBeLetGoOver(AThingOnTheDisc? carried, AThingOnTheDisc? over)
    {
        // Only what has an order is carried at all. The window never starts
        // a drag from anything else, and this does not lean on that.
        if (IsWorking || !HasAnOrder(carried) || over is null || GameOf(carried) < 0 || GameOf(over) < 0)
        {
            return false;
        }

        return carried!.What == TheTreeOfWhatGoesOn.AnAddOn
            ? over.What == TheTreeOfWhatGoesOn.AnAddOn
              && !ReferenceEquals(carried, over)
              && GameOf(carried) == GameOf(over)
            : GameOf(carried) != GameOf(over);
    }

    /// <summary>
    /// Moves one add-on to where another of the same game is: before it, or
    /// after. The menu lists a game's add-ons in the order they are in here.
    /// </summary>
    /// <returns>The moved add-on's row, for the window to keep picked, or nothing when nothing moved.</returns>
    public AThingOnTheDisc? MoveTheAddOnTo(AThingOnTheDisc? moved, AThingOnTheDisc? onto, bool after)
    {
        if (!MayBeLetGoOver(moved, onto) || moved!.What != TheTreeOfWhatGoesOn.AnAddOn)
        {
            return null;
        }

        int at = GameOf(moved);
        string path = moved.ItsPath;

        if (AddOn.WithOneMoved(_addOnsAsFound, at, path, onto!.ItsPath, after) is not IReadOnlyList<AddOn> now)
        {
            return null;
        }

        Replace(_addOnsAsFound, now);

        // The branch again, remembering its ticks and what was open, and the
        // ticks carried into what goes on the disc in the new order.
        BuildTheBranchAgain(at);

        AThingOnTheDisc? row = _tree[at].EveryThing().FirstOrDefault(each =>
            each.What == TheTreeOfWhatGoesOn.AnAddOn && TheSameFile(each.ItsPath, path));

        if (row is not null)
        {
            WhatIsSelected = row;
        }

        return row;
    }

    /// <summary>
    /// Moves the game the picked row belongs to one place up or down, with
    /// everything that is its own. Returns its row, for the window to keep
    /// picked, or nothing when it could not move.
    /// </summary>
    /// <param name="by">Minus one for up, one for down.</param>
    public AThingOnTheDisc? MoveTheGame(int by)
    {
        int from = GameOf(_selected);
        int to = from + Math.Sign(by);

        if (IsWorking || by == 0 || from < 0 || to < 0 || to >= _tree.Count)
        {
            return null;
        }

        int[] order = [.. Enumerable.Range(0, _tree.Count)];
        (order[from], order[to]) = (order[to], order[from]);

        _theOrderIsSomebodysOwn = true;

        return PutInOrder(order, _tree[from]);
    }

    /// <summary>
    /// Puts the games in order, by name unless another way is asked for.
    /// Two that come out level stay as they were. Returns the picked row's
    /// game, for the window to keep picked, or nothing when they were in
    /// that order already.
    /// </summary>
    public AThingOnTheDisc? SortTheGames(HowToSort how = HowToSort.ByNameAToZ)
    {
        int[] order = TheGamesInOrder(how);

        if (IsWorking || InOrder(order))
        {
            return null;
        }

        int picked = GameOf(_selected);

        // Sorted, they are in nobody's own order any more.
        _theOrderIsSomebodysOwn = false;

        return PutInOrder(order, picked >= 0 ? _tree[picked] : null) ?? (_tree.Count > 0 ? _tree[0] : null);
    }

    /// <summary>
    /// The old place of each game, in the order asked for. By size is by
    /// what the list shows beside the game, add-ons and all, which is what
    /// somebody is looking at when they ask.
    /// </summary>
    private int[] TheGamesInOrder(HowToSort how)
    {
        IEnumerable<int> places = Enumerable.Range(0, _tree.Count);

        return
        [
            .. how switch
            {
                HowToSort.ByNameZToA => places.OrderByDescending(at => _tree[at].Name, ByName),
                HowToSort.BiggestFirst => places.OrderByDescending(at => _tree[at].Bytes),
                HowToSort.SmallestFirst => places.OrderBy(at => _tree[at].Bytes),
                _ => places.OrderBy(at => _tree[at].Name, ByName),
            }
        ];
    }

    private static bool InOrder(int[] order) => order.Select((old, now) => old == now).All(same => same);

    /// <summary>
    /// Every list that is kept by a game's place, put in one new order
    /// together, and the add-ons told where their games went.
    /// </summary>
    /// <param name="order">For each new place, the old place of the game that goes there.</param>
    /// <param name="picked">The row to leave picked.</param>
    private AThingOnTheDisc? PutInOrder(int[] order, AThingOnTheDisc? picked)
    {
        GameFolder[] games = [.. order.Select(old => _games[old])];
        GameFolder[] asFound = [.. order.Select(old => _asFound[old])];
        AThingOnTheDisc[] tree = [.. order.Select(old => _tree[old])];

        int[] whereEachWent = new int[order.Length];

        for (int now = 0; now < order.Length; now++)
        {
            whereEachWent[order[now]] = now;
        }

        for (int at = 0; at < order.Length; at++)
        {
            _games[at] = games[at];
            _asFound[at] = asFound[at];

            // Only the rows that moved, so a list of forty redraws two.
            if (!ReferenceEquals(_tree[at], tree[at]))
            {
                _tree[at] = tree[at];
            }
        }

        Replace(_addOnsAsFound, AddOn.AfterTheGamesMoved(_addOnsAsFound, whereEachWent));
        Replace(_addOns, AddOn.AfterTheGamesMoved(_addOns, whereEachWent));

        // The tree lets go of a row that is replaced, so it is picked again
        // here and the window picks it again on its side.
        _selected = picked;

        Reconsider();
        Tell(nameof(CanTakeSomethingOff));

        return picked;
    }

    private void TellHowTheOrderCanChange()
    {
        Tell(nameof(CanMoveTheGameUp));
        Tell(nameof(CanMoveTheGameDown));
        Tell(nameof(CanMoveThePickedUp));
        Tell(nameof(CanMoveThePickedDown));
        Tell(nameof(ThereIsAnOrderToChange));
        Tell(nameof(CanSortTheGames));
    }

    // --------------------------------------------- renaming a game in place
    //
    // v1 lets a double click rename any row, and discwright#98 leaned on
    // that when the duplicate name notice was asked for: a notice is only
    // half an answer while the way to act on it is to take the game off and
    // add it again. Only a game's own row renames, because a file keeps the
    // name it has on disk whatever the list says, and the registered
    // MatchName is never touched, because what GOG wrote in the registry
    // does not change when somebody retitles a menu button.

    /// <summary>
    /// Opens a game's row for renaming, when there is nothing running and
    /// the row really is a game of this disc.
    /// </summary>
    public void BeginRenaming(AThingOnTheDisc? row)
    {
        if (row is null || !NothingIsRunning || _tree.IndexOf(row) < 0)
        {
            return;
        }

        row.BeginRenaming();
    }

    /// <summary>Shuts the row with its old name kept.</summary>
    public void CancelRenaming(AThingOnTheDisc row)
    {
        if (_tree.IndexOf(row) < 0)
        {
            return;
        }

        row.StopRenaming();
    }

    /// <summary>
    /// Takes what was typed as the game's name, everywhere at once: the row,
    /// the list the disc is built from, and the list the ticks rebuild it
    /// from, so the next tick cannot quietly put the old name back.
    /// </summary>
    /// <remarks>
    /// Nothing typed, or only spaces, keeps the old name rather than making
    /// a nameless menu button. The disc's label is left alone: it was named
    /// once, when the first game arrived, and renaming a game afterwards is
    /// a decision about the game.
    /// </remarks>
    public void FinishRenaming(AThingOnTheDisc row, string typed)
    {
        row.StopRenaming();

        int at = _tree.IndexOf(row);
        string name = typed?.Trim() ?? string.Empty;

        if (at < 0 || name.Length == 0 || name == _asFound[at].Name)
        {
            return;
        }

        _asFound[at] = _asFound[at] with { Name = name };
        _games[at] = _games[at] with { Name = name };
        row.AnswerTo(name);

        // Which is where the duplicate name notice, the plan and everything
        // else that reads a game's name catches up.
        Reconsider();
    }

    /// <summary>
    /// Things that go on the disc without belonging to any game.
    /// </summary>
    /// <remarks>
    /// A README sitting loose in the folder somebody pointed at belongs to the
    /// disc rather than to one of the games inside it, and would otherwise be
    /// left behind when that folder is taken apart.
    /// </remarks>
    public void AlsoOnTheDisc(IEnumerable<string> paths)
    {
        _loose.AddRange(paths);
        Reconsider();
    }

    /// <summary>The project as it stands, for whoever builds it.</summary>
    public DiscProject AsAProject() =>
        new(Label,
            _games,
            Options,
            Artwork: _artwork,
            Extras: _manual is null && _extrasFolder is null && _loose.Count == 0 && _music is null
                ? null
                : new DiscExtras(_manual, _extrasFolder, _loose, _music),
            AddOns: _addOns);

    /// <summary>
    /// Fills the window in from a disc that was made before.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Everything goes, including the things the window cannot show yet. A
    /// disc opened and built again should come out the same disc, and
    /// quietly leaving out the artwork because there is nowhere to display it
    /// would make that false.
    /// </para>
    /// <para>
    /// The size of disc is only taken when the file chose one. A project saved
    /// on automatic says nothing about it, and the one already on the window is
    /// a better answer than a guess.
    /// </para>
    /// </remarks>
    public void OpenThis(Reopened back)
    {
        _games.Clear();
        _addOns.Clear();
        _loose.Clear();
        _asFound.Clear();
        _addOnsAsFound.Clear();
        _tree.Clear();

        DiscProject was = back.Project;

        foreach (GameFolder game in was.Games)
        {
            _games.Add(game);
            _asFound.Add(game);
        }

        _addOns.AddRange(was.AddOnsOrNone);
        _addOnsAsFound.AddRange(was.AddOnsOrNone);

        // The tree is what the window shows, so a project opened without one
        // is a project whose games are invisible while everything else on the
        // window insists they are there: the size says one disc and the button
        // that builds them is lit.
        for (int at = 0; at < _asFound.Count; at++)
        {
            _tree.Add(TheTreeOfWhatGoesOn.For(
                _asFound[at],
                _addOnsAsFound.Where(addOn => addOn.BelongsTo == at),
                TakeTheTicksAsTheAnswer));
        }
        _loose.AddRange(was.Extras?.Loose ?? []);
        _artwork = was.Artwork;

        // The box shows the colour the project carries, so a project opened
        // and saved again keeps it without anybody retyping it.
        _backgroundColourText = was.Artwork?.BackgroundColour ?? string.Empty;

        _manual = was.Extras?.ManualPath;
        _extrasFolder = was.Extras?.ExtrasFolderPath;
        _music = was.Extras?.MusicPath;

        _label = was.Label;
        _options = was.Options;

        if (back.OutputFolder is string where)
        {
            // Somebody's answer once, so it is a chosen folder and not a
            // suggestion, and Start a new disc keeps it.
            _outputFolder = where;
            _theFolderWasSuggested = false;
        }

        // The images are already sitting in that folder, and without this
        // nothing goes looking for them, so Burn to a disc stayed dead after
        // opening a project that was finished and ready to write. The setter
        // for OutputFolder does this; assigning the field behind it does not.
        LookForImages();

        if (back.Medium is Medium chosen)
        {
            _medium = chosen;

            // And it counts as somebody choosing, which the setter would have
            // said and this bypass of it did not. The size a project was
            // saved with was somebody's answer once, so the drives only
            // comment on it from here. Without this line the next disc to
            // go in or come out moved the size the file had just chosen.
            _theSizeWasChosen = true;
        }

        // And nothing chooses it for anybody now: the file's size is the
        // answer, and a tick left on from the last disc would move it the
        // moment the project had opened.
        _theSizeFollowsWhatIsOnIt = false;
        Tell(nameof(ChooseTheSizeForMe));
        Tell(nameof(TheSizeCanBePicked));

        Tell(nameof(Label));
        Tell(nameof(OutputFolder));
        Tell(nameof(Medium));
        Tell(nameof(TheSizeChosen));
        Tell(nameof(Options));
        Tell(nameof(Menu));
        Tell(nameof(Checksums));
        Tell(nameof(LegacyFileSystems));
        Tell(nameof(CutFiles));
        Tell(nameof(StartHere));
        Tell(nameof(ListOnTheLeft));
        Tell(nameof(CornerAtTheBottom));
        Tell(nameof(DiscsOwnAsSmallButtons));
        Tell(nameof(DiscManualAsASmallButton));
        Tell(nameof(DiscExtrasAsASmallButton));
        Tell(nameof(OpenTheDiscButton));

        // The box shows the heading the project carries, so a project
        // opened and saved again keeps it without anybody retyping it.
        _menuHeadingText = _options.LookOrPlain.Heading ?? string.Empty;
        _menuInkText = _options.LookOrPlain.Ink ?? string.Empty;
        Tell(nameof(MenuInkText));
        Tell(nameof(MenuInk));
        Tell(nameof(WhatTheInkSays));
        Tell(nameof(MenuHeading));
        Tell(nameof(MenuHeadingHidden));
        Tell(nameof(MenuHeadingCanBeTyped));
        Tell(nameof(WhereTheCornerButtonsSit));

        // The artwork, the manual, the tune and the folder of extras, which a
        // project file carries and which nothing here announced.
        //
        // So a project opened with an icon, a cover and a manual on it showed
        // a panel with none of them named, while the disc it built got all
        // three. Every one of those pickers reads its own property once and
        // then believes what it read.
        //
        // Found by the walk, the first time it was taught to open a project.
        foreach (string named in ThePicturesOnIt)
        {
            Tell(named);
        }

        Tell(nameof(Manual));
        Tell(nameof(ExtrasFolder));
        Tell(nameof(Music));

        // The sentence about the last save was about the project this one
        // has just replaced.
        HowTheSaveWent = null;
        Tell(nameof(HowTheSaveWent));

        // What the file named that could not be brought back. The games that
        // did not come back cannot be rows, so this line is their only trace.
        WhatDidNotComeBack = TheNewsAbout(back);
        Tell(nameof(WhatDidNotComeBack));

        Reconsider();
    }

    /// <summary>
    /// Works out everything the window says back, after anything changes.
    /// </summary>
    /// <remarks>
    /// One method rather than a rule per property, because the answers depend
    /// on each other: a bigger disc can turn a refusal into a build, and a
    /// game removed can turn three discs into one.
    /// </remarks>
    /// <summary>Puts a worked out list back into the one the window holds.</summary>
    /// <remarks>
    /// The list itself is kept rather than swapped, because it is a field the
    /// rest of this class reads by reference and a new one would leave half of
    /// it looking at the old.
    /// </remarks>
    private static void Replace<T>(List<T> what, IReadOnlyList<T> with)
    {
        what.Clear();
        what.AddRange(with);
    }

    private void Reconsider()
    {
        // The picked row may have gone with whatever changed, and the line
        // under the list that names its game goes with it.
        Tell(nameof(ThePickedGame));
        TellHowTheOrderCanChange();

        if (_tree.Count < 2)
        {
            _theOrderIsSomebodysOwn = false;
        }

        Worry = HasNoName ? null : DiscLabel.WhatWindowsWillMakeOfIt(Label, _machine.Encoding);

        // Settled below with the plan, and nothing until then.
        _theLargerDisc = null;
        _discsItWillTake = 0;
        _whatItWouldMake = null;

        // Before the plan, so the plan is of the size it moved to. And the
        // line about it is said again either way: it goes when the last
        // game does.
        FollowWhatIsOnIt();
        Tell(nameof(WhyThatSize));
        Tell(nameof(WhatThereIsOfThatSize));

        if (_games.Count == 0)
        {
            // Not a refusal. Somebody who has not added a game yet has not done
            // anything wrong, and telling them so on an empty window is the
            // program complaining about its own starting state.
            WhyNot = null;
            WhatItWillMake = string.Empty;
            WhatSplittingWouldDo = string.Empty;
            WhereEachGameGoes = string.Empty;
            ADiscForEachGameIsAChoice = false;
            _thePlanIsSound = false;
            Told();
            return;
        }

        if (HasNoName)
        {
            WhyNot = Refusals.ADiscNeedsAName();
            WhatItWillMake = string.Empty;
            WhatSplittingWouldDo = string.Empty;
            WhereEachGameGoes = string.Empty;
            ADiscForEachGameIsAChoice = _games.Count > 1 && Options.ADiscForEachGame;

            // Nothing was planned, because a disc with no name is refused
            // before the planning. The menu carries the name, so there is
            // nothing honest to show either.
            _thePlanIsSound = false;
            Told();
            return;
        }

        switch (WhatWouldBeMade.For(AsAProject(), Medium, _machine.HowBigIs))
        {
            case Outcome<PlannedBuild>.Ok(PlannedBuild planned):
                // Last, and only once the plan itself is sound. Somewhere to
                // put it cannot be the complaint while a file is too big for
                // any disc, because choosing a folder would not help.
                //
                // What it will make is still said. The plan is good and only
                // the destination is missing, so "one disc" and "there is
                // nowhere to put it" are both true and both worth reading.
                WhyNot = OutputFolder.Length == 0 ? Refusals.ThereIsNowhereToPutIt() : null;
                WhatItWillMake = planned.InWords;
                _discsItWillTake = planned.Discs;
                _whatItWouldMake = planned;
                Tell(nameof(WhatThereIsOfThatSize));

                // A plan, whether or not there is anywhere to put what it
                // would make. A preview writes to a temporary folder of its
                // own and has never needed that one.
                _thePlanIsSound = true;

                // How many discs, and whether anything is cut to get there.
                // The plan decides the words, ticked or not.
                WhatSplittingWouldDo = planned.WhatCuttingComesTo(Options.CutFiles);

                WhereEachGameGoes = planned.WhereEachGameGoes.Length > 0
                    ? planned.WhereEachGameGoes + " They go on in the order of the list."
                    : string.Empty;
                ADiscForEachGameIsAChoice = _games.Count > 1 && (planned.Discs > 1 || Options.ADiscForEachGame);

                break;

            case Outcome<PlannedBuild>.No refused:
                WhyNot = refused.Refusal;
                WhatItWillMake = string.Empty;
                WhatSplittingWouldDo = string.Empty;
                WhereEachGameGoes = string.Empty;

                // Refused one way may not be refused the other: a game's
                // own extras ride every disc of a set of its own, and go on
                // once when the games share one.
                ADiscForEachGameIsAChoice = _games.Count > 1;
                _thePlanIsSound = false;

                if (refused.Refusal.Remedy is { Kind: RemedyKind.ChangeMedium })
                {
                    _theLargerDisc = TheSmallestLargerDiscThatWouldDo();
                }

                break;
        }

        Told();
    }

    /// <summary>
    /// The smallest size this program aims at, above the one aimed at now,
    /// on which what is on the window would plan, or nothing when none would.
    /// </summary>
    /// <remarks>
    /// Planned for real on each size up rather than compared against the
    /// largest file, because what refuses a size is the whole plan: the menu
    /// and the artwork take room on every disc, and the extras may be what
    /// does not fit. Sizes are walked smallest first, so the answer is the
    /// smallest that works, the way a recommendation walks them.
    /// </remarks>
    private Medium? TheSmallestLargerDiscThatWouldDo()
    {
        DiscProject project = AsAProject();

        return Media.All
            .Where(size => size.Capacity > Medium.Capacity)
            .FirstOrDefault(size => WhatWouldBeMade.For(project, size, _machine.HowBigIs) is Outcome<PlannedBuild>.Ok);
    }

    /// <summary>
    /// Takes a changed option, and says both which one changed and that the
    /// whole set did, because the window binds to each one by name and to the
    /// set as a whole.
    /// </summary>
    private void Switch(DiscOptions to, [CallerMemberName] string? name = null)
    {
        if (_options == to)
        {
            return;
        }

        _options = to;
        Tell(name);
        Tell(nameof(Options));

        // Splitting a file changes what would be made, so the line saying so
        // has to be worked out again.
        Reconsider();
    }

    /// <summary>
    /// Makes an image of each disc that was built, and says what went wrong if
    /// anything did.
    /// </summary>
    /// <remarks>
    /// A worry from the image is kept, because the thing it has to say, that
    /// the older file systems were left off, is true of the disc somebody now
    /// holds and will not be said again.
    /// </remarks>
    private async Task<Refusal?> Images(BuiltDiscs made, List<string> begun, CancellationToken ct)
    {
        foreach (BuiltDisc disc in made.Discs)
        {
            ct.ThrowIfCancellationRequested();

            Progress<ImageProgress> saying = new(step =>
            {
                bool worth;

                lock (_oneAtATime)
                {
                    worth = IsWorking;

                    if (worth)
                    {
                        Doing = $"{disc.Label}: {step}";
                        HowFar = step.HowFar;
                    }
                }

                if (worth)
                {
                    Tell(nameof(Doing));
                    Tell(nameof(HowFar));
                }
            });

            string image = TheImageOf.That(disc);

            // Begun before it is made, so a stop in the middle of this one
            // takes it away with the ones that were finished.
            begun.Add(image);

            Outcome<MadeImage> one = await _machine.Images.Make(
                disc.Folder,
                image,
                disc.Label,
                Options.LegacyFileSystems,
                disc.Number,
                disc.OfDiscs,
                saying,
                ct);

            if (one is Outcome<MadeImage>.No stopped)
            {
                return stopped.Refusal;
            }

            if (((Outcome<MadeImage>.Ok)one).Value.Worry is string worry)
            {
                AboutTheImage = worry;
                Tell(nameof(AboutTheImage));
            }
        }

        return null;
    }

    /// <summary>
    /// What images are sitting in the output folder.
    /// </summary>
    /// <remarks>
    /// Looked at whenever the folder box changes, which is every keystroke,
    /// so only the folder itself is read and never what is under it: the
    /// images are written straight into it. Reading everything under it made
    /// typing <c>C:\</c> walk the whole drive on the window's thread.
    /// </remarks>
    private void LookForImages()
    {
        TheImages = OutputFolder.Length > 0 && _machine.Disk.FolderExists(OutputFolder)
            ?
            [
                .. _machine.Disk.ListHere(OutputFolder).Files
                    .Where(file => file.RelativePath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                    .Select(file => System.IO.Path.Combine(OutputFolder, file.RelativePath))
                    .Order(StringComparer.OrdinalIgnoreCase)
            ]
            : [];

        Tell(nameof(TheImages));
        Tell(nameof(CanBurn));

        // Which of them are a set follows the list, and follows the name, so
        // it is said again from Told as well.
        Tell(nameof(TheSetToBurn));
    }

    private void Started()
    {
        Tell(nameof(CanErase));
        LookForImages();

        Tell(nameof(IsWorking));
        Tell(nameof(NothingIsRunning));
        Tell(nameof(TheSizeCanBePicked));
        Tell(nameof(CanStartAgain));
        Tell(nameof(CanSaveItForLater));
        Tell(nameof(HowTheSaveWent));
        Tell(nameof(CanTakeSomethingOff));
        TellHowTheOrderCanChange();
        Tell(nameof(Doing));
        Tell(nameof(HowFar));
        Tell(nameof(Made));
        Tell(nameof(TheSetToBurn));
        Tell(nameof(ItWentWrong));
        Tell(nameof(CanMakeIt));
        Tell(nameof(CanBurn));
        Tell(nameof(CanPreview));

        // What the drive said about its own reporting, which starting
        // anything clears and nothing announced. The line is about the burn
        // that has just been left behind, so a build started after one left
        // it on screen underneath a bar that had gone back to nothing.
        //
        // Found by the walk, once it could press Make and Burn.
        Tell(nameof(AboutTheImage));

        // And the line about the last burn, which every start clears and
        // every end settles.
        TellHowTheBurnWent();
    }

    private void Told()
    {
        Tell(nameof(WhatItWillMake));

        // Here rather than beside the ticks, because adding a game changes
        // the total too and adding ends at Reconsider rather than there.
        Tell(nameof(WhatIsOnIt));
        Tell(nameof(WhatSplittingWouldDo));
        Tell(nameof(WhereEachGameGoes));
        Tell(nameof(ADiscForEachGameIsAChoice));
        Tell(nameof(WhyNot));
        Tell(nameof(Worry));
        Tell(nameof(CanMakeIt));

        // With the refusal, because it is part of the refusal. The worry's
        // too, because it is part of the worry.
        Tell(nameof(TheWayOut));
        Tell(nameof(WhatTheWayOutIsCalled));
        Tell(nameof(TheLargerDiscThatWouldDo));
        Tell(nameof(WhatTheWorrysWayOutIsCalled));

        // A name typed, a game added, a tune chosen: all of them end here,
        // and every one of them is something to start again FROM, and
        // something worth keeping.
        Tell(nameof(HasAnythingOnIt));
        Tell(nameof(CanStartAgain));
        Tell(nameof(CanSaveItForLater));

        // Said about the folders on the list, so it follows the list.
        Tell(nameof(WhatTheDownloadsSay));
        Tell(nameof(WhatWillNotStartItself));
        Tell(nameof(WhoWearsTheSameName));

        // And the three about what is ticked. These used to be announced
        // beside the ticks and nowhere else, so every other way of changing
        // the list left them on the window saying something about a list that
        // was no longer there: starting a new disc kept "Leaving off 1 thing"
        // over an empty one. Everything that changes the list ends here, so
        // this is where they belong.
        Tell(nameof(WhatIsLeftOff));
        Tell(nameof(WhatBuildingGoesWithout));
        Tell(nameof(WhatTakingTheLeftOffOnesSays));
        Tell(nameof(WhyThatMatters));
        Tell(nameof(APatchWithNothingToPatch));

        // And whether Remove would take anything. It goes by what is picked,
        // and what is picked is gone the moment the list is rebuilt under it.
        Tell(nameof(CanTakeSomethingOff));
        TellHowTheOrderCanChange();

        // Adding a game is what makes previewing possible, and this is the
        // path adding takes. A property nobody is told about keeps whatever
        // the window read out of it the first time, which for this one was a
        // button that stayed dead for ever.
        Tell(nameof(CanPreview));

        // Here, rather than in the four setters that used to announce it one
        // at a time. It names the files carried loose now, and those arrive
        // through AlsoOnTheDisc and go through StartAgain and OpenThis, none
        // of which is a setter with a line to add. Four places that announce
        // a line is four places to forget the fifth, which is the shape of
        // every bug the walk has found on this window. All of them end here.
        Tell(nameof(WhatElseIsChosen));

        // The set in the folder is the set named after this disc, so a name
        // typed moves it.
        Tell(nameof(TheSetToBurn));
    }

    private void Change<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Tell(name);
        Reconsider();
    }

    private void Tell(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
