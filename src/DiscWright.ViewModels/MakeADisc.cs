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

    // Carried rather than chosen. There is no artwork or manual on the window
    // yet, and a disc opened from a file has both; dropping them because this
    // cannot edit them would mean rebuilding a disc without the cover somebody
    // picked, which is a worse answer than carrying something untouched.
    private DiscArtwork? _artwork;
    private string? _manual;
    private string? _extrasFolder;
    private string? _music;

    private string _label = string.Empty;
    private string _outputFolder = string.Empty;
    private Medium _medium = Media.Dvd5;
    private bool _theSizeWasChosen;
    private bool _lookingAtTheDrives;
    private Recorder? _theDrive;
    private bool _asking;
    private IReadOnlyList<Recorder> _lastSeen = [];
    private DiscOptions _options = new();
    private ReadOnlyObservableCollection<GameFolder>? _view;

    public MakeADisc(TheMachine machine)
    {
        _machine = machine;
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

    /// <summary>Where the discs get written.</summary>
    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            Change(ref _outputFolder, value);
            LookForImages();
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
    /// The same answer as whether there is anything to build. A preview of
    /// nothing is an empty window somebody has to close.
    /// </remarks>
    public bool CanPreview => !IsWorking && _games.Count > 0;

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

    /// <summary>Where the last preview was written, or nothing.</summary>
    public string? ThePreview { get; private set; }

    /// <summary>
    /// Writes the menu this project would get, for somebody to look at.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Before anything is built</strong>, which is the whole point:
    /// seeing the menu after burning a disc is seeing it too late. So the
    /// discs are planned exactly as building would plan them, and the menu for
    /// the first one is written out.
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

        ThePreview = null;
        ItWentWrong = null;

        switch (_machine.Preview.Write(AsAProject(), Medium, _machine.HowBigIs, into))
        {
            case Outcome<string>.Ok(string where):
                ThePreview = where;
                break;

            case Outcome<string>.No refused:
                ItWentWrong = refused.Refusal;
                break;
        }

        Tell(nameof(ThePreview));
        Tell(nameof(ItWentWrong));
    }

    /// <summary>
    /// Which version this is, for the corner of the window.
    /// </summary>
    /// <remarks>
    /// Shown without anybody asking, because "which one am I running" is a
    /// question somebody should not have to press a button to answer.
    /// </remarks>
    public string ThisBuild => _machine.ThisBuild;

    /// <summary>
    /// How this build compares with the newest released, once somebody has
    /// asked, or nothing because nobody has.
    /// </summary>
    public HowThisBuildCompares? AboutTheVersion { get; private set; }

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
    /// it: replacing a running program is a far bigger thing than reading a
    /// number, and doing it to an unsigned one that Windows already distrusts
    /// would be worse than not offering it.
    /// </para>
    /// </remarks>
    public async Task CheckForANewVersion(CancellationToken ct = default)
    {
        if (_asking)
        {
            return;
        }

        _asking = true;

        try
        {
            WhatWasReleased said = await _machine.Releases.TheLatest(ct);

            AboutTheVersion = new HowThisBuildCompares(
                _machine.ThisBuild, said.Tag, said.Where, said.WhyNot);
        }
        catch (Exception wrong) when (wrong is not OperationCanceledException)
        {
            // The port is meant to report rather than throw, and a button that
            // takes the program down because a web request misbehaved would be
            // a poor trade for a version number.
            AboutTheVersion = new HowThisBuildCompares(
                _machine.ThisBuild, null, null, "Something went wrong asking.");
        }
        finally
        {
            _asking = false;
        }

        Tell(nameof(AboutTheVersion));
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
            Tell(nameof(AboutTheTray));
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

        _lookingAtTheDrives = true;

        IReadOnlyList<Recorder> drives;
        string trays;

        try
        {
            // The trays are asked about out here too. Each answer costs a
            // call to a drive, and doing it in front of the window is how the
            // window came to take thirteen seconds to open.
            string? chosen = _theDrive?.Drive;

            (drives, trays) = await Task.Run(() =>
            {
                IReadOnlyList<Recorder> found = _machine.Drives.All();

                // Once a drive is chosen, the warning is about THAT one. A
                // sentence about the drive somebody is not using is noise.
                IReadOnlyList<Recorder> asking = chosen is null
                    ? found
                    : [.. found.Where(each => string.Equals(each.Drive, chosen, StringComparison.OrdinalIgnoreCase))];

                return (found, TheDiscsThatAreIn.WhatToSayAboutTheTray(
                    asking, _machine.Tray.CanShutItsOwnTray) ?? string.Empty);
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

        if (!_theSizeWasChosen
            && TheDiscsThatAreIn.TheSizeToStartFrom(drives) is Medium found
            && found != _medium)
        {
            // Not through the setter, which would count as a person choosing
            // and stop the drives mattering ever again.
            _medium = found;
            Tell(nameof(Medium));
            Tell(nameof(TheSizeChosen));
        }

        SayWhatIsInTheDrives(drives);

        Drives = drives;
        Tell(nameof(Drives));

        // Whether there is anything to erase changes with them, and a button
        // nobody tells about is judged once at startup, when there are no
        // drives yet, and stays dead for the rest of the run.
        Tell(nameof(CanErase));
        Tell(nameof(WhatTheDrivesAreDoing));

        // A drive that has been unplugged is not a choice any more. The slim
        // one here lives on a USB cable.
        if (_theDrive is not null
            && !drives.Any(each => string.Equals(each.Drive, _theDrive.Drive, StringComparison.OrdinalIgnoreCase)))
        {
            TheDrive = null;
        }
        else if (_theDrive is not null)
        {
            // Kept by letter rather than by the record, because what is in it
            // has almost certainly changed since it was picked.
            _theDrive = drives.First(each =>
                string.Equals(each.Drive, _theDrive.Drive, StringComparison.OrdinalIgnoreCase));

            Tell(nameof(TheDrive));
        }

        AboutTheTray = trays;
        Tell(nameof(AboutTheTray));
    }

    private void SayWhatIsInTheDrives(IReadOnlyList<Recorder>? drives = null)
    {
        string said = TheDiscsThatAreIn.WhatIsWorthSaying(drives ?? _lastSeen, _medium) ?? string.Empty;

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
    public bool CanMakeIt => WhyNot is null && Games.Count > 0 && OutputFolder.Length > 0 && !IsWorking;

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
    /// They change with the option to split a file: a size that will be split
    /// across discs is a different answer from the same size that will not,
    /// and saying so where the size is chosen beats a refusal further down the
    /// window that somebody has to work backwards from.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ASizeToAimAt> DiscSizes { get; private set; } =
        [.. Media.All.Select(each => new ASizeToAimAt(each, each.Name))];

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

        Reconsider();
        Tell(nameof(WhatIsLeftOff));
        Tell(nameof(WhyThatMatters));
        Tell(nameof(APatchWithNothingToPatch));
    }

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
    public string? WhatIsLeftOff
    {
        get
        {
            int left = 0;
            long bytes = 0;
            bool noInstaller = false;

            for (int at = 0; at < _tree.Count; at++)
            {
                foreach (AThingOnTheDisc thing in _tree[at].EveryThing())
                {
                    if (thing.IsOn == false)
                    {
                        left++;
                        bytes += thing.Bytes;
                    }
                }

                noInstaller |= _tree[at].ItsInstallerIsOff();
            }

            if (left == 0)
            {
                return null;
            }

            string what = left == 1 ? "1 thing" : $"{left} things";

            return $"Leaving off {what}, {ByteSize.Describe(bytes)}.";
        }
    }

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

    public string? WhyThatMatters =>
        _tree.Any(game => game.ItsInstallerIsOff())
            ? "Without its installer, the disc will not install the game."
            : null;

    /// <summary>
    /// Words the sizes again, because what they mean has changed.
    /// </summary>
    private void SayTheSizesAgain()
    {
        string also = Options.CutFiles ? ", split across discs" : string.Empty;

        DiscSizes = [.. Media.All.Select(each => new ASizeToAimAt(each, each.Name + also))];

        Tell(nameof(DiscSizes));
        Tell(nameof(TheSizeChosen));
    }

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
    public string WhatTheDrivesAreDoing
    {
        get
        {
            const string leaveIt =
                "Leave this alone and DiscWright works it out, and asks only if there is something to ask.";

            return AboutTheTray is string tray
                ? leaveIt + "\n\n" + tray + ". " + WhyTheTrayMatters
                : leaveIt;
        }
    }

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

        IReadOnlyList<Recorder> all = await WaitForTheDriveToSaySomething(_machine.Drives.All(), null, ct);

        Recorder[] could = [.. all.Where(each => each.ErasingWouldHelp)];

        if (could.Length == 0)
        {
            ItWentWrong = Refusals.ThereIsNothingToErase(
                [.. all.Select(each => each.Drive)]);
            Tell(nameof(ItWentWrong));
            return;
        }

        if (await whichToErase(could) is not Recorder chosen)
        {
            return;
        }

        try
        {
            IsWorking = true;
            Tell(nameof(IsWorking));
            Tell(nameof(NothingIsRunning));
            Tell(nameof(CanTakeSomethingOff));
            Say($"erasing the {chosen.Holding} in {chosen.Drive}", 0);

            if (await _machine.Eraser.BlankIt(chosen, new Progress<EraseProgress>(step => Say(step.Doing, step.HowFar ?? 0)), ct)
                is Outcome<string>.No stopped)
            {
                ItWentWrong = stopped.Refusal;
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
            // a backing class factory" reached Lazar at the end of an erase.
            ItWentWrong = Refusals.SomethingNobodyExpected("erasing the disc", nobodyExpected.Message);
            Tell(nameof(ItWentWrong));
        }
        finally
        {
            IsWorking = false;
            Tell(nameof(IsWorking));
            Tell(nameof(NothingIsRunning));
            Tell(nameof(CanTakeSomethingOff));
            await LookAtTheDrives();
        }
    }

    /// <summary>Whether a build is running right now.</summary>
    public bool IsWorking { get; private set; }

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
    /// Something the image had to say that did not stop it, or null.
    /// </summary>
    public string? AboutTheImage { get; private set; }

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
    public async Task MakeIt(CancellationToken ct = default)
    {
        // CanMakeIt is already false while a build is running, so this is the
        // whole of the guard. Saying it twice was two things to keep in step.
        if (!CanMakeIt)
        {
            return;
        }

        IsWorking = true;
        Made = null;
        ItWentWrong = null;
        AboutTheImage = null;
        HowFar = 0;
        Doing = "Starting";
        Started();

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

        try
        {
            switch (await _machine.Builder.Build(AsAProject(), Medium, OutputFolder, saying, ct))
            {
                case Outcome<BuiltDiscs>.Ok(BuiltDiscs made):
                    // The folder is the disc and the image is the same disc as
                    // one file. Both, every time, because the folder is what
                    // somebody checks and the image is what they burn, and
                    // having to ask for the second one is a step nobody wants.
                    ItWentWrong = await Images(made, ct);
                    Made = made;
                    break;

                case Outcome<BuiltDiscs>.No stopped:
                    ItWentWrong = stopped.Refusal;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // Not a failure, and not worth a word. Somebody who pressed stop
            // knows why it stopped.
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
        }
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

    /// <summary>Whether anything would come off if Remove were pressed.</summary>
    /// <remarks>
    /// Remove takes what is selected, or everything unticked when nothing is,
    /// so there is nothing for it to do on an empty list, and nothing for it
    /// to do when everything is ticked and no row is picked. A button that
    /// does nothing when pressed should look like one.
    /// </remarks>
    public bool CanTakeSomethingOff =>
        !IsWorking
        && (WhatIsSelected is not null
            || _tree.SelectMany(game => game.EveryThing()).Any(each => each.IsOn == false));

    /// <summary>The row the tree has picked, or nothing.</summary>
    private AThingOnTheDisc? _selected;

    public AThingOnTheDisc? WhatIsSelected
    {
        get => _selected;
        set
        {
            _selected = value;
            Tell(nameof(CanTakeSomethingOff));
        }
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
        Doing = "looking at the drives";
        HowFar = 0;
        Started();

        try
        {
            // A drive chosen in the window is the answer, so nothing is asked.
            Recorder? onto = _theDrive is Recorder picked
                ? await SettleThatOne(picked.Drive, _machine.HowBigIs(image), whichToErase, ct)
                : await Settle(_machine.HowBigIs(image), chooseADrive, whichToErase, ct);

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

                    // Written is not the same as readable. The drive ejected so
                    // that whatever reads it next sees a freshly mounted volume.
                    Say("waiting for the disc to come back", 0);

                    if (!await shutTheTray(onto))
                    {
                        ItWentWrong = Refusals.TheDiscWasNotChecked(onto.Drive);
                        return;
                    }

                    await CheckIt(onto, image, ct);

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
    public string? Background
    {
        get => _artwork?.BackgroundPath;
        set => ChangeTheArtwork(Art() with { BackgroundPath = Tidy(value) });
    }

    /// <summary>
    /// Whether the background is stretched to fit exactly rather than cropped.
    /// </summary>
    public bool BackgroundAsIs
    {
        get => _artwork?.BackgroundAsIs == true;
        set => ChangeTheArtwork(Art() with { BackgroundAsIs = value });
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

    /// <summary>What plays behind the menu.</summary>
    public string? Music
    {
        get => _music;
        set
        {
            _music = Tidy(value);
            Tell(nameof(Music));
            Tell(nameof(WhatElseIsChosen));
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
            Tell(nameof(WhatElseIsChosen));
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
            Tell(nameof(WhatElseIsChosen));
            Told();
        }
    }

    /// <summary>
    /// What has been chosen, in a line, for the window that does not show it.
    /// </summary>
    /// <remarks>
    /// So that somebody can see at a glance whether their disc has a
    /// background on it without opening anything.
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

    private void ChangeTheArtwork(DiscArtwork now)
    {
        _artwork = now.IsAnything || now.BackgroundAsIs ? now : null;

        foreach (string named in new[]
        {
            nameof(DiscIcon), nameof(Background), nameof(BackgroundAsIs), nameof(Cover), nameof(DiscFace),
            nameof(WhatElseIsChosen)
        })
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
    /// Whether there is anything on this screen worth losing.
    /// </summary>
    /// <remarks>
    /// Asked by the window before it offers to throw it away. A screen with
    /// nothing on it needs no confirming, and being asked to confirm nothing
    /// teaches somebody to click through the question that matters.
    /// </remarks>
    public bool HasAnythingOnIt =>
        _games.Count > 0 || Label.Length > 0 || Made is not null || _artwork is not null
        || _manual is not null || _extrasFolder is not null || _music is not null;

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
    /// </remarks>
    public void StartAgain()
    {
        if (IsWorking)
        {
            return;
        }

        _games.Clear();
        _addOns.Clear();
        _asFound.Clear();
        _addOnsAsFound.Clear();
        _tree.Clear();
        _loose.Clear();

        _artwork = null;
        _manual = null;
        _extrasFolder = null;
        _music = null;

        Label = string.Empty;

        // A new disc is a new question, so the drives get to answer it again.
        _theSizeWasChosen = false;

        Made = null;
        AboutTheImage = null;
        ItWentWrong = null;
        HowTheDiscCameOut = null;
        HowTheSetWent = null;
        TheImages = [];

        foreach (string gone in new[]
        {
            nameof(Made), nameof(AboutTheImage), nameof(ItWentWrong), nameof(HowTheDiscCameOut),
            nameof(HowTheSetWent), nameof(TheImages), nameof(HasAnythingOnIt)
        })
        {
            Tell(gone);
        }

        // Which puts the window back to the sentence it opens with, and works
        // the refusal out again from nothing.
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
                ? await SettleThatOne(picked.Drive, _machine.HowBigIs(images[broughtForward]), whichToErase, ct)
                : await Settle(_machine.HowBigIs(images[broughtForward]), chooseADrive, whichToErase, ct);

            if (drive is null)
            {
                stopped = ItWentWrong?.Headline ?? "no drive was chosen";
                return;
            }

            for (int at = broughtForward; at < images.Count; at++)
            {
                ct.ThrowIfCancellationRequested();

                // The first disc this run writes goes into the drive that was
                // just settled, whether that is disc 1 or disc 3. It is the
                // ones after it that need somebody to swap.
                if (at > broughtForward)
                {
                    Say($"waiting for a blank disc for disc {at + 1} of {images.Count}", 0);

                    if (!await theNextBlankPlease(at + 1, images.Count, drive))
                    {
                        stopped = "nobody put the next blank in";
                        return;
                    }

                    // Asked again, because what is in that drive has changed:
                    // the disc just written came out and something else went
                    // in, and it may not be blank.
                    Recorder? again = await SettleThatOne(drive.Drive, _machine.HowBigIs(images[at]), whichToErase, ct);

                    if (again is null)
                    {
                        stopped = ItWentWrong?.Headline ?? "the next disc could not be used";
                        return;
                    }

                    drive = again;
                }

                Say($"disc {at + 1} of {images.Count}", 0);

                Progress<BurnProgress> saying = new(step =>
                    Say($"disc {at + 1} of {images.Count}: {step}", step.HowFar ?? 0));

                switch (await _machine.Burner.Burn(images[at], drive, saying, ct))
                {
                    case Outcome<BurnedDisc>.No refused:
                        ItWentWrong = refused.Refusal;
                        stopped = refused.Refusal.Headline;
                        return;
                }

                if (!await shutTheTray(drive))
                {
                    ItWentWrong = Refusals.TheDiscWasNotChecked(drive.Drive);
                    stopped = "the disc could not be read back";
                    return;
                }

                await CheckThatDisc(drive, at + 1, ct);

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

            Tell(nameof(HowTheSetWent));
            Started();
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
    /// The one drive again, after whatever is in it has changed.
    /// </summary>
    private async Task<Recorder?> SettleThatOne(
        string drive,
        long bytes,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> whichToErase,
        CancellationToken ct)
    {
        IReadOnlyList<Recorder> all = await WaitForTheDriveToSaySomething(
            _machine.Drives.All(), drive, ct);

        for (int attempt = 0; attempt < 2; attempt++)
        {
            switch (WhichRecorder.For(all, bytes, drive))
            {
                case Outcome<Recorder>.Ok(Recorder ready):
                    return ready;

                case Outcome<Recorder>.No refused:
                    Recorder[] erasable =
                    [
                        .. all.Where(each =>
                            each.ErasingWouldHelp
                            && string.Equals(each.Drive, drive, StringComparison.OrdinalIgnoreCase))
                    ];

                    if (attempt == 0 && erasable.Length > 0 && await whichToErase(erasable) is Recorder chosen)
                    {
                        Say($"erasing the {chosen.Holding} in {chosen.Drive}", 0);

                        Outcome<string> erased = await _machine.Eraser.BlankIt(
                            chosen, new Progress<EraseProgress>(step => Say(step.Doing, step.HowFar ?? 0)), ct);

                        if (erased is Outcome<string>.No stopped)
                        {
                            ItWentWrong = stopped.Refusal;
                            return null;
                        }

                        all = _machine.Drives.All();
                        continue;
                    }

                    ItWentWrong = refused.Refusal;
                    return null;
            }
        }

        return null;
    }

    /// <summary>Reads back one disc of a set, against the folder it was built from.</summary>
    private async Task CheckThatDisc(Recorder onto, int number, CancellationToken ct)
    {
        string root = onto.Drive.TrimEnd((char)92) + (char)92;

        string staged = Made is { Discs.Count: > 0 } made
            ? (made.Discs.FirstOrDefault(disc => disc.Number == number) ?? made.Discs[0]).Folder
            : string.Empty;

        if (staged.Length == 0 || !_machine.Disk.FolderExists(staged))
        {
            // Walking a set after opening the project it was built from is
            // the ordinary way to finish one, so this is the common case and
            // not the odd one.
            HowTheDiscCameOut = TheDiscAgainstItsOwnList.IsOn(root, _machine.Disk)
                ? await TheDiscAgainstItsOwnList.Check(
                    root,
                    _machine.Disk,
                    _machine.Hasher,
                    new Progress<CheckProgress>(step => Say($"disc {number}: {step}", step.HowFar)),
                    ct)
                : null;

            if (HowTheDiscCameOut is null)
            {
                ItWentWrong = Refusals.TheDiscWasNotChecked(onto.Drive);
            }

            return;
        }

        HowTheDiscCameOut = await TheDiscAgainstWhatWasBuilt.Check(
            root,
            staged,
            _machine.Disk,
            _machine.Hasher,
            new Progress<CheckProgress>(step => Say($"disc {number}: {step}", step.HowFar)),
            ct);

        Tell(nameof(HowTheDiscCameOut));
    }

    /// <summary>
    /// Which drive, asking only where there is something to ask.
    /// </summary>
    /// <summary>
    /// How long to let a drive think before taking its silence as an answer.
    /// </summary>
    /// <remarks>
    /// A drive asked the moment a tray shuts can take ten seconds to say what
    /// is in it, measured on the ASUS writer here. Fifteen is that with room
    /// to spare, and it only ever costs anything when something is wrong.
    /// </remarks>
    private static readonly TimeSpan LongEnoughForADriveToWakeUp = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Reads the drives again until the one being waited on has something to
    /// say, or until it has had long enough.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A disc that has just been pushed in is still being read, and asking
    /// then throws rather than answering, which becomes
    /// <see cref="Recorder.WhyNot"/>.
    /// </para>
    /// <para>
    /// That is not a refusal, it is a drive that has not finished thinking,
    /// and treating it as one is worse than it sounds: WhyNot also makes
    /// <see cref="Recorder.ErasingWouldHelp"/> false, so the offer to erase
    /// the disc never appears and somebody who put a used disc in is told the
    /// drive would not say what is in it, with nothing to do about it.
    ///
    /// Found by Lazar putting a used disc in for the second disc of a set on
    /// purpose, and getting a dead end instead of the question.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<Recorder>> WaitForTheDriveToSaySomething(
        IReadOnlyList<Recorder> all, string? onlyThisOne, CancellationToken ct)
    {
        bool StillThinking(IReadOnlyList<Recorder> drives) => drives.Any(each =>
            each.WhyNot is not null
            && (onlyThisOne is null || string.Equals(each.Drive, onlyThisOne, StringComparison.OrdinalIgnoreCase)));

        if (!StillThinking(all))
        {
            return all;
        }

        Say("waiting for the drive", 0);

        for (DateTimeOffset until = DateTimeOffset.UtcNow + LongEnoughForADriveToWakeUp;
             DateTimeOffset.UtcNow < until;)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), ct);

            all = _machine.Drives.All();

            if (!StillThinking(all))
            {
                return all;
            }
        }

        return all;
    }

    private async Task<Recorder?> Settle(
        long bytes,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> chooseADrive,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> whichToErase,
        CancellationToken ct)
    {
        IReadOnlyList<Recorder> all = await WaitForTheDriveToSaySomething(
            _machine.Drives.All(), null, ct);

        // Twice at most: once as it stands, and once more after an erase,
        // because erasing changes the answer.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            switch (WhichRecorder.For(all, bytes))
            {
                case Outcome<Recorder>.Ok(Recorder ready):
                    return ready;

                case Outcome<Recorder>.No refused:
                    Recorder[] waiting = [.. all.Where(each => each.Ready)];

                    if (waiting.Length > 1)
                    {
                        return await chooseADrive(waiting);
                    }

                    Recorder[] erasable = [.. all.Where(each => each.ErasingWouldHelp)];

                    if (attempt == 0 && erasable.Length > 0 && await whichToErase(erasable) is Recorder chosen)
                    {
                        Say("erasing the " + chosen.Holding + " in " + chosen.Drive, 0);

                        Outcome<string> erased = await _machine.Eraser.BlankIt(
                            chosen, new Progress<EraseProgress>(step => Say(step.Doing, step.HowFar ?? 0)), ct);

                        if (erased is Outcome<string>.No stopped)
                        {
                            ItWentWrong = stopped.Refusal;
                            return null;
                        }

                        all = _machine.Drives.All();
                        continue;
                    }

                    ItWentWrong = refused.Refusal;
                    return null;
            }
        }

        return null;
    }

    private async Task CheckIt(Recorder onto, string image, CancellationToken ct)
    {
        string root = onto.Drive.TrimEnd((char)92) + (char)92;

        // The disc that was burned, not whichever was built first. This took
        // Discs[0] every time, so burning disc 2 of a set made in the same
        // sitting compared it against disc 1 and called every file on it
        // wrong, which looks exactly like a ruined disc.
        string staged = Made is { Discs.Count: > 0 } made
            ? (TheOneThatMade(made, image) ?? made.Discs[0]).Folder
            : string.Empty;

        HowTheDiscCameOut = await ReadItBack(root, staged, onto, ct);
        Tell(nameof(HowTheDiscCameOut));
    }

    /// <summary>Which built disc an image came from, by its name.</summary>
    /// <remarks>
    /// Every disc in a set carries the same label, so the label alone cannot
    /// tell them apart. The name is rebuilt the way TheImageOf builds it, so
    /// the two cannot drift: "{label} D{number}.iso" in a set, "{label}.iso"
    /// for a disc on its own.
    /// </remarks>
    private static BuiltDisc? TheOneThatMade(BuiltDiscs made, string image)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(image);

        return made.Discs.FirstOrDefault(disc =>
            string.Equals(
                disc.OfDiscs > 1 ? $"{disc.Label} D{disc.Number}" : disc.Label,
                name,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads a disc back, against the folder it was built from when that is
    /// still here, and against the list the disc carries when it is not.
    /// </summary>
    /// <remarks>
    /// Opening a finished project and pressing Burn leaves nothing built in
    /// this sitting, so there was no folder to compare with and a disc that
    /// read back perfectly was reported as written but not checked. The same
    /// went for a disc from another machine, or one whose staging folder had
    /// been tidied away, and always would have.
    /// </remarks>
    private async Task<DiscCheck?> ReadItBack(string root, string staged, Recorder onto, CancellationToken ct)
    {
        Progress<CheckProgress> saying = new(step => Say(step.ToString(), step.HowFar));

        if (staged.Length > 0 && _machine.Disk.FolderExists(staged))
        {
            return await TheDiscAgainstWhatWasBuilt.Check(
                root, staged, _machine.Disk, _machine.Hasher, saying, ct);
        }

        if (TheDiscAgainstItsOwnList.IsOn(root, _machine.Disk))
        {
            return await TheDiscAgainstItsOwnList.Check(
                root, _machine.Disk, _machine.Hasher, saying, ct);
        }

        // Neither. The disc is written, and that is all that can honestly be
        // said about it.
        ItWentWrong = Refusals.TheDiscWasNotChecked(onto.Drive);
        return null;
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

        foreach (LeftBehind each in asAddOns ?? [])
        {
            AddOn addOn = new(
                _games.Count - 1,
                System.IO.Path.Combine(game.Path, each.File.RelativePath),
                each.AsAnAddOn,
                each.Bytes);

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
        if (_games.Count == 1 && Label.Length == 0)
        {
            _label = game.Name.ToUpperInvariant();
            Tell(nameof(Label));
        }

        Reconsider();
    }

    /// <summary>
    /// Takes things off the list: the one that is selected, or everything
    /// that has been unticked when nothing is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A tick and this button mean different things. Unticked is <em>not on
    /// this disc</em>, still listed, and one click from coming back. Removed
    /// is <em>gone</em>, and comes back only by adding the folder again.
    /// </para>
    /// <para>
    /// With nothing selected it clears out everything unticked, which is the
    /// tidying-up gesture after deciding what is not going on. The first
    /// version of this took the whole game whatever was selected, so clicking
    /// a file and pressing Remove emptied the window.
    /// </para>
    /// </remarks>
    public void TakeOff(AThingOnTheDisc? selected)
    {
        if (selected is not null)
        {
            TakeOffJustThis(selected);
        }
        else
        {
            foreach (AThingOnTheDisc off in
                _tree.SelectMany(game => game.EveryThing()).Where(each => each.IsOn == false).ToList())
            {
                TakeOffJustThis(off);
            }

            // A game with every row unticked is itself unticked, and goes.
            foreach (AThingOnTheDisc game in _tree.Where(each => each.IsOn == false).ToList())
            {
                TakeOffJustThis(game);
            }
        }

        TakeTheTicksAsTheAnswer();
    }

    private void TakeOffJustThis(AThingOnTheDisc thing)
    {
        int at = _tree.IndexOf(thing.TheGameItIsIn);

        if (at < 0 || at >= _games.Count)
        {
            return;
        }

        if (thing.IsAWholeGame)
        {
            Remove(_games[at]);
            return;
        }

        // One row out of a game. The game stays, carrying one thing less.
        if (thing.What == TheTreeOfWhatGoesOn.AnAddOn)
        {
            _addOnsAsFound.RemoveAll(addOn =>
                addOn.BelongsTo == at && string.Equals(addOn.Path, thing.ItsPath, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            _asFound[at] = _asFound[at] with
            {
                Files = [.. _asFound[at].Files.Where(file =>
                    !string.Equals(file.RelativePath, thing.ItsPath, StringComparison.OrdinalIgnoreCase))]
            };
        }

        // Built again from what is left, which is the only way the tree and
        // the list cannot drift apart.
        _tree[at] = TheTreeOfWhatGoesOn.For(
            _asFound[at],
            _addOnsAsFound.Where(addOn => addOn.BelongsTo == at),
            TakeTheTicksAsTheAnswer);
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
        _addOnsAsFound.RemoveAll(addOn => addOn.BelongsTo == which);
        _addOns.RemoveAll(addOn => addOn.BelongsTo == which);

        for (int at = 0; at < _addOnsAsFound.Count; at++)
        {
            if (_addOnsAsFound[at].BelongsTo > which)
            {
                _addOnsAsFound[at] = _addOnsAsFound[at] with { BelongsTo = _addOnsAsFound[at].BelongsTo - 1 };
            }
        }

        for (int at = 0; at < _addOns.Count; at++)
        {
            if (_addOns[at].BelongsTo > which)
            {
                _addOns[at] = _addOns[at] with { BelongsTo = _addOns[at].BelongsTo - 1 };
            }
        }

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
        _manual = was.Extras?.ManualPath;
        _extrasFolder = was.Extras?.ExtrasFolderPath;
        _music = was.Extras?.MusicPath;

        _label = was.Label;
        _options = was.Options;

        if (back.OutputFolder is string where)
        {
            _outputFolder = where;
        }

        // The images are already sitting in that folder, and without this
        // nothing goes looking for them, so Burn to a disc stayed dead after
        // opening a project that was finished and ready to write. The setter
        // for OutputFolder does this; assigning the field behind it does not.
        LookForImages();

        if (back.Medium is Medium chosen)
        {
            _medium = chosen;
        }

        Tell(nameof(Label));
        Tell(nameof(OutputFolder));
        Tell(nameof(Medium));
        Tell(nameof(TheSizeChosen));
        Tell(nameof(Options));
        Tell(nameof(Menu));
        Tell(nameof(Checksums));
        Tell(nameof(LegacyFileSystems));
        Tell(nameof(CutFiles));

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
    private void Reconsider()
    {
        Worry = Label.Length == 0 ? null : DiscLabel.WhatWindowsWillMakeOfIt(Label, _machine.Encoding);

        if (_games.Count == 0)
        {
            // Not a refusal. Somebody who has not added a game yet has not done
            // anything wrong, and telling them so on an empty window is the
            // program complaining about its own starting state.
            WhyNot = null;
            WhatItWillMake = string.Empty;
            Told();
            return;
        }

        if (Label.Length == 0)
        {
            WhyNot = Refusals.ADiscNeedsAName();
            WhatItWillMake = string.Empty;
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
                break;

            case Outcome<PlannedBuild>.No refused:
                WhyNot = refused.Refusal;
                WhatItWillMake = string.Empty;
                break;
        }

        Told();
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

        // A size that will be split across discs is a different answer from
        // the same size that will not, and the list says which.
        SayTheSizesAgain();

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
    private async Task<Refusal?> Images(BuiltDiscs made, CancellationToken ct)
    {
        foreach (BuiltDisc disc in made.Discs)
        {
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
    /// Looked at when the folder changes and when a build finishes, rather than
    /// on every keystroke.
    /// </remarks>
    private void LookForImages()
    {
        TheImages = OutputFolder.Length > 0 && _machine.Disk.FolderExists(OutputFolder)
            ?
            [
                .. _machine.Disk.List(OutputFolder).Files
                    .Where(file => file.RelativePath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                    .Select(file => System.IO.Path.Combine(OutputFolder, file.RelativePath))
                    .Order(StringComparer.OrdinalIgnoreCase)
            ]
            : [];

        Tell(nameof(TheImages));
        Tell(nameof(CanBurn));
    }

    private void Started()
    {
        Tell(nameof(CanErase));
        LookForImages();

        Tell(nameof(IsWorking));
            Tell(nameof(NothingIsRunning));
            Tell(nameof(CanTakeSomethingOff));
        Tell(nameof(Doing));
        Tell(nameof(HowFar));
        Tell(nameof(Made));
        Tell(nameof(ItWentWrong));
        Tell(nameof(CanMakeIt));
        Tell(nameof(CanBurn));
        Tell(nameof(CanPreview));
    }

    private void Told()
    {
        Tell(nameof(WhatItWillMake));
        Tell(nameof(WhyNot));
        Tell(nameof(Worry));
        Tell(nameof(CanMakeIt));

        // Adding a game is what makes previewing possible, and this is the
        // path adding takes. A property nobody is told about keeps whatever
        // the window read out of it the first time, which for this one was a
        // button that stayed dead for ever.
        Tell(nameof(CanPreview));
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
