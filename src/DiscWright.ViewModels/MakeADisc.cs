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

    public MakeADisc(TheMachine machine)
    {
        _machine = machine;
        _settling = new SettlingOnADrive(machine.Drives, machine.Eraser, Say, machine.Patience);
        _reading = new ReadingBackWhatWasWritten(machine.Disk, machine.Hasher, Say);
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
            // Set from outside, so somebody chose it. Add puts the flag back
            // when it was this program's suggestion.
            _theFolderWasSuggested = false;
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
    /// All of them, not the first. The manual and the extras ride one disc of
    /// a set and the planner puts them on the last one, so a preview of disc
    /// one can never show the buttons for them however many somebody chose.
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
        ItWentWrong = null;

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
    public string WhatTheVersionSays => AboutTheVersion?.InWords ?? string.Empty;

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
        _asking ? "Asking GitHub"
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
    /// </remarks>
    public string? TheVersionPage =>
        AboutTheVersion is { WorthGetting: true } how ? how.Where : null;

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
        TellAboutTheVersion();

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

        TellAboutTheVersion();
    }

    /// <summary>
    /// Everything on the version line, which changes together or not at all.
    /// </summary>
    private void TellAboutTheVersion()
    {
        Tell(nameof(AboutTheVersion));
        Tell(nameof(WhatTheVersionSays));
        Tell(nameof(WhatTheVersionLinkSays));
        Tell(nameof(TheVersionPage));
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

        if (WhatTheDrivesMean.TheSizeToStartFrom(drives, _medium, _theSizeWasChosen, IsWorking)
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
    /// </remarks>
    public string WhatSplittingWouldDo { get; private set; } = string.Empty;

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

            IsWorking = true;
            Tell(nameof(IsWorking));
            Tell(nameof(NothingIsRunning));
            Tell(nameof(CanStartAgain));
            Tell(nameof(CanTakeSomethingOff));

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
            Tell(nameof(CanStartAgain));
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

        // The line about the last burn is about discs this build is about to
        // replace, so it goes with the drive's note rather than staying under
        // a bar that has started again.
        HowTheDiscCameOut = null;
        HowTheSetWent = null;
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
    /// <para>
    /// Exactly what is picked, so a button that does nothing when pressed
    /// looks like one.
    /// </para>
    /// <para>
    /// <strong>It used to mean two things.</strong> Remove took what was
    /// selected, or everything unticked when nothing was, so which of the two
    /// somebody got depended on whether a row happened to be picked, and
    /// nothing on the window said so. Clearing out what has been turned off
    /// is still worth having and now has a button of its own, wearing words
    /// that say what it will do.
    /// </para>
    /// </remarks>
    public bool CanTakeSomethingOff => !IsWorking && WhatIsSelected is not null;

    /// <summary>
    /// What the button that clears out the unticked rows says, or nothing when
    /// there are none and there is no such button.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The count is in the words on purpose. The gesture takes several things
    /// at once and none of them is selected, so the only place it can be seen
    /// beforehand is the button, and a button called Remove with a count
    /// somewhere else is the button this one was split out of.
    /// </para>
    /// <para>
    /// Said the way <see cref="WhatIsLeftOff"/> says it, because they are
    /// about the same rows and reading "left off" in one place and "turned
    /// off" in the other would sound like two different things.
    /// </para>
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

                    // Written is not the same as readable. The drive ejected so
                    // that whatever reads it next sees a freshly mounted volume.
                    Say("waiting for the disc to come back", 0);

                    if (!await shutTheTray(onto))
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
        [nameof(DiscIcon), nameof(Background), nameof(BackgroundAsIs), nameof(Cover), nameof(DiscFace)];

    private void ChangeTheArtwork(DiscArtwork now)
    {
        _artwork = now.IsAnything || now.BackgroundAsIs ? now : null;

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
        TheImages = [];

        foreach (string gone in new[]
        {
            nameof(Made), nameof(TheSetToBurn), nameof(AboutTheImage), nameof(ItWentWrong), nameof(HowTheDiscCameOut),
            nameof(HowTheSetWent), nameof(TheImages), nameof(HasAnythingOnIt)
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

                if (!await shutTheTray(drive))
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
            _addOnsAsFound.Add(new AddOn(
                at,
                System.IO.Path.Combine(known.Path, each.File.RelativePath),
                each.AsAnAddOn,
                each.Bytes));
        }

        _tree[at] = TheTreeOfWhatGoesOn.For(
            _asFound[at],
            _addOnsAsFound.Where(addOn => addOn.BelongsTo == at),
            TakeTheTicksAsTheAnswer);

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
        TakeTheRowOff(at, thing.ItsPath, thing.What == TheTreeOfWhatGoesOn.AnAddOn);
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
        // Both lists renumbered the same way, by the same sum, which is now
        // one thing that can be asked rather than the same loop written twice.
        Replace(_addOnsAsFound, AddOn.AfterTheGameAt(_addOnsAsFound, which));
        Replace(_addOns, AddOn.AfterTheGameAt(_addOns, which));

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

        Tell(nameof(Label));
        Tell(nameof(OutputFolder));
        Tell(nameof(Medium));
        Tell(nameof(TheSizeChosen));
        Tell(nameof(Options));
        Tell(nameof(Menu));
        Tell(nameof(Checksums));
        Tell(nameof(LegacyFileSystems));
        Tell(nameof(CutFiles));

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
        Worry = Label.Length == 0 ? null : DiscLabel.WhatWindowsWillMakeOfIt(Label, _machine.Encoding);

        // Settled below with the plan, and nothing until then.
        _theLargerDisc = null;

        if (_games.Count == 0)
        {
            // Not a refusal. Somebody who has not added a game yet has not done
            // anything wrong, and telling them so on an empty window is the
            // program complaining about its own starting state.
            WhyNot = null;
            WhatItWillMake = string.Empty;
            WhatSplittingWouldDo = string.Empty;
            _thePlanIsSound = false;
            Told();
            return;
        }

        if (Label.Length == 0)
        {
            WhyNot = Refusals.ADiscNeedsAName();
            WhatItWillMake = string.Empty;
            WhatSplittingWouldDo = string.Empty;

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

                // A plan, whether or not there is anywhere to put what it
                // would make. A preview writes to a temporary folder of its
                // own and has never needed that one.
                _thePlanIsSound = true;

                // Only while the box is ticked. Unticked, a file too big for
                // a disc is a refusal with its own words and this would be a
                // second sentence about the same thing.
                WhatSplittingWouldDo = Options.CutFiles
                    ? planned.WhatGetsCut.Length > 0
                        ? planned.WhatGetsCut
                        : "Nothing here is bigger than one disc, so nothing will be cut."
                    : string.Empty;

                break;

            case Outcome<PlannedBuild>.No refused:
                WhyNot = refused.Refusal;
                WhatItWillMake = string.Empty;
                WhatSplittingWouldDo = string.Empty;
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
        Tell(nameof(CanStartAgain));
        Tell(nameof(CanTakeSomethingOff));
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
        // and every one of them is something to start again FROM.
        Tell(nameof(HasAnythingOnIt));
        Tell(nameof(CanStartAgain));

        // Said about the folders on the list, so it follows the list.
        Tell(nameof(WhatTheDownloadsSay));
        Tell(nameof(WhatWillNotStartItself));

        // And the three about what is ticked. These used to be announced
        // beside the ticks and nowhere else, so every other way of changing
        // the list left them on the window saying something about a list that
        // was no longer there: starting a new disc kept "Leaving off 1 thing"
        // over an empty one. Everything that changes the list ends here, so
        // this is where they belong.
        Tell(nameof(WhatIsLeftOff));
        Tell(nameof(WhatTakingTheLeftOffOnesSays));
        Tell(nameof(WhyThatMatters));
        Tell(nameof(APatchWithNothingToPatch));

        // And whether Remove would take anything. It goes by what is picked,
        // and what is picked is gone the moment the list is rebuilt under it.
        Tell(nameof(CanTakeSomethingOff));

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
