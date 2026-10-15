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
    private readonly List<AddOn> _addOns = [];
    private readonly List<string> _loose = [];

    // Carried rather than chosen. There is no artwork or manual on the window
    // yet, and a disc opened from a file has both; dropping them because this
    // cannot edit them would mean rebuilding a disc without the cover somebody
    // picked, which is a worse answer than carrying something untouched.
    private DiscArtwork? _artwork;
    private string? _manual;
    private string? _extrasFolder;

    private string _label = string.Empty;
    private string _outputFolder = string.Empty;
    private Medium _medium = Media.Dvd5;
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

    /// <summary>The size of disc being aimed at.</summary>
    public Medium Medium
    {
        get => _medium;
        set => Change(ref _medium, value);
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

    /// <summary>Every size of disc that can be aimed at.</summary>
    /// <remarks>
    /// An instance property although it reads nothing of the instance, because
    /// a WPF binding cannot reach a static one. Static would mean every list in
    /// the window having to know the class it comes from, which is the window
    /// knowing more about this than it should.
    /// </remarks>
    [SuppressMessage("Performance", "CA1822", Justification = "A binding cannot reach a static property.")]
    public IReadOnlyList<Medium> DiscSizes => Media.All;

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

    /// <summary>Whether the cover art goes on the disc as well as on paper.</summary>
    public bool ArtworkOnDisc
    {
        get => Options.ArtworkOnDisc;
        set => Switch(Options with { ArtworkOnDisc = value });
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
            Recorder? onto = await Settle(_machine.HowBigIs(image), chooseADrive, whichToErase, ct);

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

                    await CheckIt(onto, ct);
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

    /// <summary>
    /// Which drive, asking only where there is something to ask.
    /// </summary>
    private async Task<Recorder?> Settle(
        long bytes,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> chooseADrive,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> whichToErase,
        CancellationToken ct)
    {
        IReadOnlyList<Recorder> all = _machine.Drives.All();

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
                            chosen, new Progress<string>(line => Say(line, 0)), ct);

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

    private async Task CheckIt(Recorder onto, CancellationToken ct)
    {
        string root = onto.Drive.TrimEnd((char)92) + (char)92;
        string staged = Made is { Discs.Count: > 0 } made ? made.Discs[0].Folder : string.Empty;

        if (staged.Length == 0 || !_machine.Disk.FolderExists(staged))
        {
            // Nothing to check it against. The disc is written, and that is all
            // that can honestly be said about it.
            ItWentWrong = Refusals.TheDiscWasNotChecked(onto.Drive);
            return;
        }

        HowTheDiscCameOut = await TheDiscAgainstWhatWasBuilt.Check(
            root,
            staged,
            _machine.Disk,
            _machine.Hasher,
            new Progress<CheckProgress>(step => Say(step.ToString(), step.HowFar)),
            ct);

        Tell(nameof(HowTheDiscCameOut));
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

        foreach (LeftBehind each in asAddOns ?? [])
        {
            _addOns.Add(new AddOn(
                _games.Count - 1,
                System.IO.Path.Combine(game.Path, each.File.RelativePath),
                each.AsAnAddOn,
                each.Bytes));
        }

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
        _addOns.RemoveAll(addOn => addOn.BelongsTo == which);

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
            Extras: _manual is null && _extrasFolder is null && _loose.Count == 0
                ? null
                : new DiscExtras(_manual, _extrasFolder, _loose),
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

        DiscProject was = back.Project;

        foreach (GameFolder game in was.Games)
        {
            _games.Add(game);
        }

        _addOns.AddRange(was.AddOnsOrNone);
        _loose.AddRange(was.Extras?.Loose ?? []);
        _artwork = was.Artwork;
        _manual = was.Extras?.ManualPath;
        _extrasFolder = was.Extras?.ExtrasFolderPath;

        _label = was.Label;
        _options = was.Options;

        if (back.OutputFolder is string where)
        {
            _outputFolder = where;
        }

        if (back.Medium is Medium chosen)
        {
            _medium = chosen;
        }

        Tell(nameof(Label));
        Tell(nameof(OutputFolder));
        Tell(nameof(Medium));
        Tell(nameof(Options));
        Tell(nameof(Menu));
        Tell(nameof(Checksums));
        Tell(nameof(ArtworkOnDisc));
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
                WhyNot = null;
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

            string image = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(disc.Folder) ?? disc.Folder,
                OnTheDisc.Tidied(disc.Label) + ".iso");

            Outcome<MadeImage> one = await _machine.Images.Make(
                disc.Folder, image, disc.Label, Options.LegacyFileSystems, saying, ct);

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
        LookForImages();

        Tell(nameof(IsWorking));
        Tell(nameof(Doing));
        Tell(nameof(HowFar));
        Tell(nameof(Made));
        Tell(nameof(ItWentWrong));
        Tell(nameof(CanMakeIt));
        Tell(nameof(CanBurn));
    }

    private void Told()
    {
        Tell(nameof(WhatItWillMake));
        Tell(nameof(WhyNot));
        Tell(nameof(Worry));
        Tell(nameof(CanMakeIt));
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
