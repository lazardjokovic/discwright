using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;

using System.Threading.Tasks;

using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Platform;
using DiscWright.Staging;
using DiscWright.ViewModels;

using Microsoft.Win32;

namespace DiscWright.App;

/// <summary>
/// The main window, and nothing more than the window.
/// </summary>
/// <remarks>
/// <para>
/// Every method here is the same three steps: ask the operating system
/// something, hand the answer to <see cref="MakeADisc"/>, and let the bindings
/// catch up. There is no arithmetic, no judgement and no refusal wording in
/// this file, because none of that can be tested from here.
/// </para>
/// <para>
/// If this file starts deciding things, the decision has escaped to the one
/// place in the program where a test cannot reach it.
/// </para>
/// </remarks>
public partial class MainWindow : Window
{
    private readonly MakeADisc _screen;
    private readonly GameFolderReader _reader;
    private readonly IFileSystem _disk;
    private readonly IWorkTheTray _tray;
    private readonly ChoosingTheLook _look;

    public MainWindow(
        MakeADisc screen, GameFolderReader reader, IFileSystem disk, IWorkTheTray tray, ChoosingTheLook look)
    {
        _screen = screen;
        _reader = reader;
        _disk = disk;
        _tray = tray;
        _look = look;

        InitializeComponent();
        DataContext = screen;

        // Before it is shown, so it never appears at a size and then jumps.
        OnThisScreen.Fit(this);
        SourceInitialized += (_, _) => TakingAwayMaximise.From(new WindowInteropHelper(this).Handle);

        // The one control not about the disc being made, so it reads its
        // words from the choice rather than from the screen.
        HowItDresses.DataContext = look;
    }

    private void ChooseTheNextLook(object sender, RoutedEventArgs e) => _look.Next();

    // --------------------------------------------- renaming a game in place
    //
    // The window's share is only the gestures: a double click opens the row
    // the screen allows, Enter and Escape close it, and wandering off
    // commits like Enter does. Who may rename and what a name is are the
    // screen's decisions.

    private void RenameWhatIsPicked(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        _screen.BeginRenaming(_screen.WhatIsSelected);

    /// <summary>
    /// The box waking up: it takes the name as it stands, so a rename
    /// abandoned last time leaves nothing stale, and the whole name is
    /// selected because the commonest rename replaces all of it.
    /// </summary>
    private void AwakenTheRenameBox(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box
            && box.IsVisible
            && box.DataContext is AThingOnTheDisc row)
        {
            // The width the name it stands in for would have had. Declared
            // in the markup it would be summed by the row width guard beside
            // a column that is never shown at the same time.
            box.Width = row.HowWideTheNameIs;
            box.Text = row.Name;
            box.Focus();
            box.SelectAll();
        }
    }

    private void TheRenameKeys(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box || box.DataContext is not AThingOnTheDisc row)
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            _screen.FinishRenaming(row, box.Text);
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            _screen.CancelRenaming(row);
            e.Handled = true;
        }
    }

    private void TheRenameBoxLostItsFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box
            && box.DataContext is AThingOnTheDisc row
            && row.IsBeingRenamed)
        {
            _screen.FinishRenaming(row, box.Text);
        }
    }

    /// <summary>
    /// Opens a disc that was built before, from the file it carries.
    /// </summary>
    /// <remarks>
    /// The file is read as bytes and handed over as text, because every one of
    /// them starts with a byte order mark and the reader is the thing that
    /// knows what to do about that.
    /// </remarks>
    private void OpenADisc(object sender, RoutedEventArgs e)
    {
        OpenFileDialog asking = new()
        {
            Title = "Which disc?",
            Filter = "A disc DiscWright made (discproject.json)|discproject.json|Any project file|*.json",
            CheckFileExists = true
        };

        if (asking.ShowDialog(this) != true)
        {
            return;
        }

        // Read before anybody is asked to give anything up for it. A file
        // this cannot open, the PowerShell DiscWright's among them, is refused
        // here with the window untouched and nobody having said yes to
        // replacing an afternoon's work with it.
        Outcome<OpenedProject> read = AProjectFile.Read(
            System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(asking.FileName)));

        if (read is Outcome<OpenedProject>.No refused)
        {
            Explain(refused.Refusal);
            return;
        }

        // Asked before anything is replaced, because what is on the window may
        // be somebody's afternoon. Anything at all, the same rule Start a new
        // disc keeps: a name typed, a cover chosen and a manual attached are an
        // afternoon too, and this asked only once a game was on the list.
        if (_screen.HasAnythingOnIt && !WorthAsking.Ask(
            this,
            "Replace what is on the window?",
            "Opening a disc puts its games, its name and its settings here, and takes away what is here now. "
                + "Nothing on disk is touched either way.",
            "Open it",
            "Leave this alone"))
        {
            return;
        }

        Reopened back = ReopenAProject.From(
            ((Outcome<OpenedProject>.Ok)read).Value,
            _reader,
            _disk,
            path => _disk.FolderExists(path) ? _disk.List(path).Bytes : SizeOfOne(path));

        if (!back.AnythingLeft)
        {
            Explain(Refusals.NothingOnThatDiscIsWhereItWas(back.Gone));
            return;
        }

        // Opened even when something is missing, and the screen says what.
        // Refusing a whole disc over one game somebody moved would be a poor
        // answer to the commonest thing that happens to a project a year
        // later, and the news lands as a line under the list rather than in
        // a box, because a box is gone at OK while the games are still gone
        // an hour later.
        _screen.OpenThis(back);
    }

    private static long SizeOfOne(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private void AddAGame(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog asking = new()
        {
            Title = "Which folder is the game in?",
            Multiselect = true
        };

        if (asking.ShowDialog(this) != true)
        {
            return;
        }

        Add(asking.FolderNames);
    }

    /// <summary>
    /// Whether a drop would land, said while the drag is still in the air.
    /// </summary>
    /// <remarks>
    /// The pointer wears no-entry over the list during a run, for the same
    /// reason every box is held then: a list that cannot be changed must not
    /// look like it can.
    /// </remarks>
    private void SayWhetherADropWouldLand(object sender, DragEventArgs e)
    {
        e.Effects = _screen.NothingIsRunning && e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// Folders dropped onto the list, discwright#98. What a drop means is
    /// WhatWasDropped's judgement; everything after that is the same road
    /// Add a game takes, questions and all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Handled the moment it arrives, whatever becomes of it.</strong>
    /// The list sits inside the panel and both take drops with this same
    /// handler, and Drop bubbles: left unhandled, a drop over the list ran
    /// here twice, once for the list and again for the panel. xniwo found it
    /// on 2.0.1, discwright#98: every folder dragged in was added twice, ten
    /// games meant ten "already on the disc" boxes, and "Add all separately"
    /// seemed to come back, because the second run asked its own question.
    /// A drop on the panel below the rows ran once, which is why it was not
    /// every time.
    /// </para>
    /// </remarks>
    private void SomethingWasDropped(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (!_screen.NothingIsRunning || e.Data.GetData(DataFormats.FileDrop) is not string[] dropped)
        {
            return;
        }

        Add(WhatWasDropped.AsFolders(dropped, _disk.FolderExists));
    }

    /// <summary>
    /// Adds folders somebody named on the command line, so DiscWright can be
    /// opened with a game rather than opened and then pointed at one.
    /// </summary>
    /// <remarks>
    /// Anything that is not a folder is ignored rather than complained about.
    /// A program started with a switch it does not know should open, not stop.
    /// </remarks>
    internal void AddWhatWeWereOpenedWith(IEnumerable<string> arguments) =>
        Add([.. arguments.Where(_disk.FolderExists)]);

    private void Add(IEnumerable<string> folders)
    {
        // Which folders are games, which hold games, what is asked about each
        // and in what order all live in TakingFoldersOn, where a test can
        // reach them. What is left here is the translation: a question becomes
        // a dialog, and a step becomes a change to the screen.
        //
        // The steps arrive one at a time, so a game appears on the list before
        // the next folder is asked about, which is how this has always
        // behaved.
        TakingFoldersOn.TheQuestions asking = new(
            AddingThemSeparately,
            LeavingItForItsGame,
            (game, left) => WhatElseIsInThere.Ask(this, game, left),
            WhicheverStartsIt);

        foreach (TakingFoldersOn.WhatToDo step in new TakingFoldersOn(_disk, _reader).From(folders, asking, _screen.IsOnAlready))
        {
            switch (step)
            {
                case TakingFoldersOn.WhatToDo.PutItOn(GameFolder game, IReadOnlyList<LeftBehind> asAddOns):
                    _screen.Add(game, asAddOns);
                    break;

                case TakingFoldersOn.WhatToDo.AlsoOnTheDisc(IReadOnlyList<string> paths):
                    _screen.AlsoOnTheDisc(paths);
                    break;

                case TakingFoldersOn.WhatToDo.ItIsOnAlready(GameFolder game, IReadOnlyList<LeftBehind> left):
                    OfferBackWhatIsMissing(game, left);
                    break;

                case TakingFoldersOn.WhatToDo.Explain(Refusal refusal):
                    Explain(refusal);
                    break;
            }
        }
    }

    /// <summary>
    /// A folder that is on the list already: offers back whatever it holds
    /// that the disc does not, or says that there is nothing more in it.
    /// </summary>
    /// <remarks>
    /// What is missing and the words are the screen's, so a test can read
    /// them. What is left here is one dialog, and the one answer that puts
    /// things back.
    /// </remarks>
    private void OfferBackWhatIsMissing(GameFolder game, IReadOnlyList<LeftBehind> left)
    {
        if (_screen.WhatIsMissingFrom(game, left) is not TheSameFolderAgain missing)
        {
            return;
        }

        if (missing.NothingIsMissing)
        {
            Explain(missing.AlreadyWhole);
        }
        else if (Ask(missing.Question))
        {
            _screen.PutBack(missing);
        }
    }

    private void WhatIsPickedChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _screen.WhatIsSelected = e.NewValue as AThingOnTheDisc;

    private void TakeOffThisRow(object sender, RoutedEventArgs e)
    {
        // The row whose cross was pressed, which is the row the button was
        // drawn on: no selection in between, so there is no way to press one
        // row's cross and lose a different row.
        if (sender is FrameworkElement pressed && pressed.DataContext is AThingOnTheDisc row)
        {
            _screen.TakeOff(row);
        }
    }

    private void TheListKeys(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Delete takes the picked row off, the keyboard's half of the cross
        // on the row. Not from inside the rename box, where Delete is an
        // editing key: the box handles it itself, but a handled key still
        // bubbles here when the box leaves it alone at the end of its text.
        if (e.Key == System.Windows.Input.Key.Delete
            && e.OriginalSource is not System.Windows.Controls.TextBox
            && _screen.CanTakeSomethingOff)
        {
            _screen.TakeOff(_screen.WhatIsSelected);
            e.Handled = true;
        }
    }

    private void TakeOffWhatIsLeftOff(object sender, RoutedEventArgs e) =>
        _screen.TakeOffWhatIsLeftOff();

    /// <summary>One of a game's own, turned from its manual into an extra or back.</summary>
    private void SwapWhatItIs(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AThingOnTheDisc row)
        {
            _screen.SwapWhatItIs(row);
        }
    }

    /// <summary>A manual of the picked game's own, from anywhere on the machine.</summary>
    private void AddAManualToThePickedGame(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog asking = new()
        {
            Title = "The manual of " + _screen.ThePickedGame,
            Filter = "A manual|*.pdf;*.txt;*.htm;*.html;*.rtf;*.chm|Anything|*.*",
            Multiselect = true
        };

        if (asking.ShowDialog(this) == true)
        {
            _screen.AddTheGamesOwn(asking.FileNames, GameExtraKind.Manual);
        }
    }

    /// <summary>
    /// Extras of the picked game's own: folders, each going on whole, the
    /// way an artbook or a folder of maps is kept.
    /// </summary>
    private void AddExtrasToThePickedGame(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFolderDialog asking = new()
        {
            Title = "Extras of " + _screen.ThePickedGame,
            Multiselect = true
        };

        if (asking.ShowDialog(this) == true)
        {
            _screen.AddTheGamesOwn(asking.FolderNames, GameExtraKind.Extra);
        }
    }

    /// <summary>
    /// Erases a disc and nothing else.
    /// </summary>
    /// <remarks>
    /// Its own button rather than something only reachable half way through a
    /// burn. Everything it can go wrong with is a refusal with a sentence,
    /// never an exception shown raw, which is what an error box full of
    /// machine words reads as.
    /// </remarks>
    private async void EraseADisc(object sender, RoutedEventArgs e)
    {
        try
        {
            await _screen.EraseADisc(WhichToErase);
        }
        catch (Exception wrong)
        {
            // A sentence, never the exception's own words on their own. Raw
            // machine text in a box with a red cross reads as the program
            // falling over, and that is how "Attempt has been made to use a
            // COM object that does not have a backing class factory" reached
            // Lazar. The refusal says what was happening and that nothing is
            // lost, and still carries what Windows said.
            Explain(Refusals.SomethingNobodyExpected("erasing the disc", wrong.Message));
            return;
        }

        if (_screen.ItWentWrong is Refusal refusal)
        {
            Explain(refusal);
        }
    }

    /// <summary>
    /// Opens the window for everything that is not a game.
    /// </summary>
    private void WhatElseGoesOnIt(object sender, RoutedEventArgs e) =>
        new WhatElseGoesOnIt(_screen) { Owner = this }.ShowDialog();

    /// <summary>
    /// Everything that is not a game.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A picture is looked at the moment it is picked rather than at build
    /// time. The icon used to be checked when it was chosen and the background
    /// never was, so a file Windows cannot decode sailed through the whole
    /// window and failed at the end as "A generic error occurred in GDI+",
    /// which tells somebody nothing about which file is at fault.
    /// </para>
    /// <para>
    /// Cancelling leaves what was there. Choosing the same file again is how
    /// somebody confirms it, and there is a Clear beside nothing because the
    /// picker itself can be pointed at the thing they want instead.
    /// </para>
    /// </remarks>
    private void ChooseTheDiscIcon(object sender, RoutedEventArgs e) =>
        TakeAPicture("The icon the disc wears", path => _screen.DiscIcon = path);

    private void ChooseTheBackground(object sender, RoutedEventArgs e) =>
        TakeAPicture("The picture the menu is drawn on", path => _screen.Background = path);

    private void ChooseTheCover(object sender, RoutedEventArgs e) =>
        TakeAPicture("The picture for the printed cover", path => _screen.Cover = path);

    private void ChooseTheDiscFace(object sender, RoutedEventArgs e) =>
        TakeAPicture("The picture for the printed disc face", path => _screen.DiscFace = path);

    private void ChooseTheMusic(object sender, RoutedEventArgs e) =>
        TakeAFile(
            "What plays behind the menu",
            "Music|*.mp3;*.wav;*.wma;*.m4a|Anything|*.*",
            path => _screen.Music = path);

    private void ChooseTheManual(object sender, RoutedEventArgs e) =>
        TakeAFile(
            "The manual",
            "A manual|*.pdf;*.txt;*.htm;*.html;*.rtf|Anything|*.*",
            path => _screen.Manual = path);

    private void ChooseTheExtras(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog asking = new() { Title = "A folder of extras to put on the disc" };

        if (asking.ShowDialog(this) == true)
        {
            _screen.ExtrasFolder = asking.FolderName;
        }
    }

    private void TakeAPicture(string title, Action<string> keep)
    {
        TakeAFile(
            title,
            "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Anything|*.*",
            path =>
            {
                // Said now, with the file named, rather than at the end of a
                // build in somebody else's words.
                if (_screen.ThatPictureIsUsable(path))
                {
                    keep(path);
                }
            });
    }

    private void TakeAFile(string title, string kinds, Action<string> keep)
    {
        OpenFileDialog asking = new()
        {
            Title = title,
            Filter = kinds,
            CheckFileExists = true
        };

        if (asking.ShowDialog(this) == true)
        {
            keep(asking.FileName);
        }
    }

    /// <summary>
    /// Does whatever the refusal on the window says would put it right.
    /// </summary>
    /// <remarks>
    /// Which remedies are worth offering is the screen's judgement, handed
    /// over as a kind this window knows how to carry out. Anything else is
    /// not offered at all, so there is nothing here that can be pressed and
    /// do nothing.
    /// </remarks>
    private void TakeTheWayOut(object sender, RoutedEventArgs e)
    {
        switch (_screen.TheWayOut)
        {
            case RemedyKind.PickFolder:
                ChooseWhereItGoes(sender, e);
                break;

            case RemedyKind.FocusField:
                PutTheCursorOnTheName();
                break;

            case RemedyKind.ChangeMedium:
                _screen.UseTheLargerDisc();
                break;
        }
    }

    /// <summary>
    /// The worry's way out, which is always about the name: Windows cannot
    /// show it, and the fix is to type another.
    /// </summary>
    private void TakeTheWorrysWayOut(object sender, RoutedEventArgs e)
    {
        if (_screen.Worry?.Remedy is { Kind: RemedyKind.FocusField })
        {
            PutTheCursorOnTheName();
        }
    }

    /// <summary>
    /// Puts the cursor in the name and selects what is there, so that typing
    /// replaces it. FocusField is what every remedy about a name asks for.
    /// </summary>
    private void PutTheCursorOnTheName()
    {
        TheName.Focus();
        TheName.SelectAll();
    }

    private void ChooseWhereItGoes(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog asking = new() { Title = "Where should the discs go?" };

        if (asking.ShowDialog(this) == true)
        {
            _screen.OutputFolder = asking.FolderName;
        }
    }

    private async void MakeTheDisc(object sender, RoutedEventArgs e)
    {
        // The only async void in the program, and it has to be one: this is an
        // event handler and WPF has nowhere to put a Task. Everything it can
        // throw is caught here rather than left to take the program down.
        try
        {
            await _screen.MakeIt();
        }
        catch (Exception wrong)
        {
            // A sentence, never the exception's own words on their own. Raw
            // machine text in a box with a red cross reads as the program
            // falling over, and that is how "Attempt has been made to use a
            // COM object that does not have a backing class factory" reached
            // Lazar. The refusal says what was happening and that nothing is
            // lost, and still carries what Windows said.
            Explain(Refusals.SomethingNobodyExpected("building the discs", wrong.Message));
            return;
        }

        if (_screen.ItWentWrong is Refusal refusal)
        {
            Explain(refusal);
            return;
        }

        if (_screen.Made is BuiltDiscs made)
        {
            Say(made);
        }
    }

    /// <summary>
    /// Writes an image to a disc, and reads the disc back.
    /// </summary>
    /// <remarks>
    /// Everything that needs a person to answer it is handed in from here, and
    /// only from here: which drive, whether a disc may be erased, and getting
    /// the tray shut again. The screen asks; it never decides.
    /// </remarks>
    private async void BurnIt(object sender, RoutedEventArgs e)
    {
        // A set is written as a set: disc 1, then ask, disc 2, then ask.
        // Burning one disc of three and calling it done is the thing most
        // likely to go wrong, so it is not offered.
        IReadOnlyList<string> set = _screen.TheSetToBurn;

        try
        {
            if (set.Count > 1)
            {
                await _screen.BurnTheSet(
                    set, ChooseADrive, WhichToErase, ShutTheTray, TheNextBlankPlease, CarryOnFromDisc);
            }
            else
            {
                string? image = WhichImage();

                if (image is null)
                {
                    return;
                }

                await _screen.BurnIt(image, ChooseADrive, WhichToErase, ShutTheTray);
            }
        }
        catch (Exception wrong)
        {
            // A sentence, never the exception's own words on their own. Raw
            // machine text in a box with a red cross reads as the program
            // falling over, and that is how "Attempt has been made to use a
            // COM object that does not have a backing class factory" reached
            // Lazar. The refusal says what was happening and that nothing is
            // lost, and still carries what Windows said.
            Explain(Refusals.SomethingNobodyExpected("burning the disc", wrong.Message));
            return;
        }

        // How the disc or the set came out is HowTheBurnWent, a line under
        // the buttons that stays, rather than a box that is gone at OK. A
        // set's line already carries the reason it stopped, so a refusal is
        // explained on its own only for a disc burned on its own.
        if (_screen.HowTheSetWent is null && _screen.ItWentWrong is Refusal refusal)
        {
            Explain(refusal);
        }
    }

    /// <summary>
    /// Asks for the next blank, between the discs of a set.
    /// </summary>
    /// <remarks>
    /// The disc just written is still in the drive, and on most drives it has
    /// been ejected. Saying which disc is next and how many there are is what
    /// stops somebody losing count halfway through a set of four.
    /// </remarks>
    /// <summary>
    /// What Windows sends when a drive gains or loses a disc.
    /// </summary>
    private const int ADeviceChanged = 0x0219;

    /// <summary>
    /// Starts listening for discs going in and coming out.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows says so, rather than this asking over and over. Polling optical
    /// drives spins them up, so a window that checked every few seconds would
    /// keep somebody's drive awake all day to answer a question nobody asked.
    /// </para>
    /// <para>
    /// The handle only exists once the window is on screen, which is why this
    /// is hooked here and not in the constructor.
    /// </para>
    /// </remarks>
    private async void ListenForDiscs(object sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WhenSomethingChanged);
        }

        await _screen.LookAtTheDrives();
    }

    private IntPtr WhenSomethingChanged(IntPtr hwnd, int message, IntPtr big, IntPtr small, ref bool handled)
    {
        if (message == ADeviceChanged)
        {
            // Not handled: this is listening in, and the rest of Windows is
            // still entitled to hear about it. Not waited for either, because
            // this is Windows' thread and it wants it back.
            _ = _screen.LookAtTheDrives();
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Writes the menu this disc would get and opens it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Somewhere temporary, and one folder per project rather than one per
    /// press. A preview left in somebody's output folder is a file they
    /// have to wonder about later, and a folder stamped with the time is a
    /// new one every press that nothing ever removes: twenty eight of them
    /// had collected in three days before this was changed.
    /// </para>
    /// <para>
    /// The objection to reusing one was that a preview which refuses to plan
    /// would leave the last one sitting there looking like an answer. That is
    /// why the folder is emptied before the plan is attempted rather than
    /// after it succeeds, so a refusal leaves nothing behind to be mistaken
    /// for this one.
    /// </para>
    /// <para>
    /// Opened with mshta, which is what the disc itself uses, so what is on
    /// screen is the thing that would be on the disc rather than an
    /// approximation of it in a browser.
    /// </para>
    /// </remarks>
    private void PreviewTheMenu(object sender, RoutedEventArgs e)
    {
        string into = Path.Combine(
            Path.GetTempPath(),
            WhereAPreviewGoes.TheFolderTheyAllGoIn,
            _screen.WhereAPreviewWouldGo);

        _screen.PreviewTheMenu(into);

        if (_screen.ItWentWrong is Refusal refusal)
        {
            Explain(refusal);
            return;
        }

        string[] menus = [.. _screen.ThePreviews.Where(File.Exists)];

        if (menus.Length == 0)
        {
            return;
        }

        // A set asks before any window opens, the owner's design on
        // 2026-11-09: the windows land exactly on top of one another, and
        // every menu at once with no warning read to him as a bug. One
        // window, disc 1, is the likely answer, and the question says the
        // manual and the extras are on every disc, so one window misses
        // nothing of them.
        if (WhatAPreviewAsks.ForASetOf(menus.Length) is AQuestion crowd && Ask(crowd))
        {
            menus = [menus[0]];
        }

        foreach (string where in menus)
        {
            try
            {
                Process.Start(new ProcessStartInfo("mshta.exe", $"\"{where}\"") { UseShellExecute = false });
            }
            catch (Exception wrong) when (wrong is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                MessageBox.Show(
                    this,
                    (menus.Length == 1 ? "The menu was written" : "The menus were written")
                        + " but nothing would open them. They are in:"
                        + Environment.NewLine + Environment.NewLine + into,
                    "DiscWright",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }
        }
    }

    /// <summary>
    /// Asks whether anything newer has been released, or opens the page for
    /// the newer one that the last ask found.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One link doing both, because they are one thought: it offers the ask,
    /// and once it has an answer worth acting on it offers the page. Which of
    /// the two it is doing is the screen's decision, handed over as a page or
    /// as nothing, so nothing in this file judges a version.
    /// </para>
    /// <para>
    /// It offers the page and never fetches anything. Replacing a running
    /// program is a far bigger thing than reading a number, and doing it to an
    /// unsigned one Windows already distrusts would be worse than not
    /// offering it at all.
    /// </para>
    /// </remarks>
    private async void CheckForANewVersion(object sender, RoutedEventArgs e)
    {
        // Once asking has found an update this copy can take, the same link
        // fetches it. What it does is the screen's decision, including
        // refusing while a burn runs, so the window only routes the press.
        if (_screen.TheUpdateOnOffer)
        {
            await _screen.UpdateThisCopy();
            return;
        }

        if (_screen.TheVersionPage is not string page)
        {
            await _screen.CheckForANewVersion();
            return;
        }

        OpenTheReleasePage(page);
    }

    /// <summary>What's new, beside Update, so the release page is never lost to the button.</summary>
    private void OpenWhatsNew(object sender, RoutedEventArgs e)
    {
        if (_screen.TheVersionPage is string page)
        {
            OpenTheReleasePage(page);
        }
    }

    private void OpenTheReleasePage(string page)
    {
        try
        {
            Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });
        }
        catch (Exception wrong) when (wrong is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A browser that will not open is not worth losing the address
            // over, so the address itself is the fallback.
            MessageBox.Show(
                this,
                "No browser would open. The page is:" + Environment.NewLine + Environment.NewLine + page,
                "DiscWright",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    /// <summary>The log the app keeps, for the link that opens it.</summary>
    internal TheLogOnDisk? TheLog { get; init; }

    /// <summary>
    /// Opens today's log, or the folder when today has nothing in it yet.
    /// </summary>
    private void OpenTheLog(object sender, RoutedEventArgs e)
    {
        if (TheLog is not TheLogOnDisk log)
        {
            return;
        }

        string target = File.Exists(log.Today) ? log.Today : log.Folder;

        try
        {
            Directory.CreateDirectory(log.Folder);
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception wrong) when (wrong is System.ComponentModel.Win32Exception or InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                "Nothing would open the log. It is in:" + Environment.NewLine + Environment.NewLine + log.Folder,
                "DiscWright",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    /// <summary>
    /// Clears the screen and starts a new disc.
    /// </summary>
    /// <remarks>
    /// Asked first, but only when there is something to lose. Confirming an
    /// empty screen is how somebody learns to click through the question that
    /// matters, and the question that matters here is the one about erasing a
    /// disc.
    /// </remarks>
    private void StartANewDisc(object sender, RoutedEventArgs e)
    {
        if (_screen.HasAnythingOnIt
            && WorthAsking.Ask(
                this,
                "Start a new disc?",
                "What is on the screen now goes: the games, the name, the artwork and the manual. "
                    + "Nothing already built or burned is touched. A folder you chose for the discs stays; "
                    + "one that was only suggested from the game goes with it.",
                "Keep what is here",
                "Start a new disc"))
        {
            return;
        }

        _screen.StartAgain();
    }

    /// <summary>
    /// The recipe without the discs. The screen decides everything; this is
    /// only the press.
    /// </summary>
    private void SaveItForLater(object sender, RoutedEventArgs e) => _screen.SaveItForLater();

    /// <summary>
    /// Asks whether to carry on part way through a set, or start it again.
    /// </summary>
    /// <remarks>
    /// Offered, never assumed. The note beside the discs says they were
    /// written and read back clean, which is true and is still not the same as
    /// them being in the room: they may have been thrown away, or somebody may
    /// be making a second copy on purpose. Both are ordinary things.
    /// </remarks>
    private Task<bool> CarryOnFromDisc(int number, int ofDiscs) =>
        Task.FromResult(Ask(WhatASetAsks.WhetherToCarryOn(number, ofDiscs)));

    private Task<bool> TheNextBlankPlease(int number, int ofDiscs, Recorder drive) =>
        Task.FromResult(Ask(WhatASetAsks.ForTheNextBlank(new WhichDisc.OfASet(number, ofDiscs), drive.Drive)));

    /// <summary>
    /// Asks a question the screen worded, and answers true for the likely one.
    /// </summary>
    /// <remarks>
    /// The words and which answer is the likely one are the screen's, so a
    /// test can read them. Decided in here, two of these had the accent and
    /// Enter on the answer that walks away from a half-made set.
    /// </remarks>
    private bool Ask(AQuestion question) =>
        WorthAsking.Ask(this, question.Headline, question.Because, question.TheLikelyOne, question.TheOtherWay);

    /// <summary>
    /// Which image to burn, asked only when there is more than one.
    /// </summary>
    private string? WhichImage()
    {
        IReadOnlyList<string> images = _screen.TheImages;

        if (images.Count <= 1)
        {
            return images.Count == 1 ? images[0] : null;
        }

        return PickOne.From(
            this,
            "Which disc of the set?",
            "The output folder holds more than one image. They are meant to be burned in order.",
            images,
            System.IO.Path.GetFileNameWithoutExtension);
    }

    private Task<Recorder?> ChooseADrive(IReadOnlyList<Recorder> ready) =>
        Task.FromResult(PickOne.From(
            this,
            "Which drive?",
            "More than one drive has a blank disc in it. Nothing is written until you say which.",
            ready,
            drive => drive.ToString()));

    /// <summary>
    /// Which disc to erase, out of the ones an erase would make usable.
    /// </summary>
    /// <remarks>
    /// With one there is a question to answer, and with two there is a choice
    /// to make. Neither is a decision to take on somebody's behalf, because
    /// whatever is on the disc goes and cannot be got back.
    /// </remarks>
    private Task<Recorder?> WhichToErase(IReadOnlyList<Recorder> could)
    {
        // The yes-or-no form only on a machine with one writer, where there
        // is no "which" to answer. With two, naming the drive in a sentence
        // is not the same as being asked to choose one: Lazar pressed Erase a
        // second time and it went to E: without him feeling he had picked it.
        if (could.Count == 1 && _screen.Drives.Count <= 1)
        {
            Recorder only = could[0];

            return Task.FromResult(WorthAsking.Ask(
                this,
                $"Erase the {only.Holding} in {only.Drive}?",
                "Everything on it goes, and it cannot be got back. A quick erase takes under a minute, and "
                    + "then the disc can be written to again.",
                "Leave it alone",
                "Erase it")
                ? null
                : only);
        }

        return Task.FromResult(PickOne.From(
            this,
            "Which disc should be erased?",
            could.Count == 1
                ? "Everything on it goes, and it cannot be got back. A quick erase takes under a minute, and "
                    + "then the disc can be written to again."
                : "Everything on the one you choose goes, and it cannot be got back. A quick erase takes "
                    + "under a minute, and then the disc can be written to again.",
            could,
            disc => $"{disc.Drive}  the {disc.Holding} in the {disc.Name}",

            // Nothing chosen to begin with. Everything on whichever disc
            // this names goes and cannot be got back, so it is answered by
            // somebody rather than defaulted to whichever drive came first.
            pickTheFirst: false));
    }

    /// <summary>
    /// Gets the tray shut again so the disc can be read, and says whether it
    /// is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The drive is asked first, because a drive that can shut its own tray
    /// should not make somebody walk over to it. Most slim drives cannot, and
    /// then it is a person's job.
    /// </para>
    /// <para>
    /// Asked through <see cref="WorthAsking"/> like every other question, so
    /// the log has it and the moment it was answered. It was a bare message
    /// box, the one question the log never heard, and on an older laptop the
    /// log showed ten minutes of nothing between a burn and its check with
    /// no way to tell whether that was the box waiting or the drive.
    /// </para>
    /// <para>
    /// Waiting for the drive to read the disc in is not here any more. It is
    /// <see cref="WaitingForTheDiscToComeBack"/>, which says the wait as it
    /// goes and never asks the drive on this thread.
    /// </para>
    /// </remarks>
    private Task<bool> ShutTheTray(Recorder drive) =>
        Task.FromResult(_tray.ShutIt(drive) || WorthAsking.Ask(
            this,
            $"Push the tray of {drive.Drive} shut",
            $"The disc is written, and {drive.Drive} has pushed it out so it can be read back properly. "
                + "This drive cannot pull its own tray in. Once it is shut, every file on the disc is checked.",
            "The tray is shut",
            "Do not check it"));

    private void Say(BuiltDiscs made)
    {
        string where = made.Discs[0].Folder;
        string folder = made.Discs.Count == 1 ? where : System.IO.Path.GetDirectoryName(where) ?? where;

        MessageBoxResult open = MessageBox.Show(
            this,
            made.Discs.Count == 1
                ? "The disc is ready in:" + Environment.NewLine + folder + Environment.NewLine + Environment.NewLine + "Open the folder?"
                : "All " + made.Discs.Count + " discs are ready in:" + Environment.NewLine + folder + Environment.NewLine + Environment.NewLine + "Open the folder?",
            "DiscWright",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (open == MessageBoxResult.Yes)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", Quoted(folder)) { UseShellExecute = true });
        }
    }

    /// <summary>
    /// A path in quotes, for handing to something else.
    /// </summary>
    /// <remarks>
    /// Built from a character code rather than typed, because a quotation mark
    /// written into generated code has a habit of arriving halved.
    /// </remarks>
    private static string Quoted(string path)
    {
        char quote = (char)34;

        return quote + path + quote;
    }

    /// <summary>
    /// Which file starts a folder of game files, when more than one could.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Nothing used to ask.</strong> The recogniser deliberately
    /// never picks one, and no part of the program asked either, so the menu
    /// ran a game only when a folder happened to hold exactly one plausible
    /// executable. Two behaved identically to none: it copied the folder and
    /// opened it, and nothing anywhere said so.
    /// </para>
    /// <para>
    /// The answer narrows the candidates to the one chosen, which is all the
    /// menu has ever looked at, so nothing downstream had to change.
    /// </para>
    /// <para>
    /// Not asked for one candidate, because there is nothing to choose
    /// between, and not asked for none, because there is nothing to choose.
    /// Saying no is allowed and leaves the disc copying the folder, which the
    /// window then says out loud.
    /// </para>
    /// </remarks>
    private GameFolder WhicheverStartsIt(GameFolder game) =>
        PickOne.From(
            this,
            $"Which file starts {game.Name}?",
            "Nothing is guessed here. A disc whose only button runs the wrong file is worse than one that "
                + "copies the folder and opens it, which is what this does if you would rather not say.",
            game.LauncherCandidates,
            file => file.RelativePath) is ListedFile chosen
            ? game with { LauncherCandidates = [chosen] }
            : game;

    private bool AddingThemSeparately(string folder, IReadOnlyList<string> inside, int loose) =>
        WorthAsking.Ask(
            this,
            $"That folder holds {inside.Count} games",
            $"{System.IO.Path.GetFileName(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar))} "
                + "is not a game itself, it is where these are kept. Added separately they each get their own "
                + "entry on the disc and its menu. Added as one, the disc holds a single entry named after the "
                + "folder, with everything inside it."
                + (loose == 0
                    ? string.Empty
                    : loose == 1
                        ? " The one file sitting loose in it goes on the disc either way."
                        : $" The {loose} files sitting loose in it go on the disc either way."),
            $"Add all {inside.Count} separately",
            "Add it as one game");

    /// <summary>
    /// Whether to leave an add-on off and wait for the game it belongs to.
    /// </summary>
    /// <remarks>
    /// Not a refusal. It still goes on if somebody wants it to; it just must
    /// not go on in silence, because on the disc it becomes a game in the menu
    /// and running it does nothing.
    /// </remarks>
    private bool LeavingItForItsGame(GameFolder game) =>
        WorthAsking.Ask(
            this,
            "That looks like an add-on, not a game",
            $"{game.Installer?.Name} installs onto a game rather than being one. On a disc of its own it appears "
                + "in the menu as a game, and running it does nothing, because the game it belongs to is not "
                + "there. To put it with its game, add the game first and attach this to it.",
            "Leave it for now",
            "Put it on anyway");

    private void Explain(Refusal refusal) =>
        MessageBox.Show(
            this,
            refusal.Because.Length == 0 ? refusal.Headline : $"{refusal.Headline}\n\n{refusal.Because}",
            "DiscWright",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}
