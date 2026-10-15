using System.Diagnostics;
using System.IO;
using System.Windows;

using System.Threading.Tasks;

using DiscWright.Core;
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

    public MainWindow(MakeADisc screen, GameFolderReader reader, IFileSystem disk, IWorkTheTray tray)
    {
        _screen = screen;
        _reader = reader;
        _disk = disk;
        _tray = tray;

        InitializeComponent();
        DataContext = screen;
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

        // Asked before anything is replaced, because what is on the window may
        // be somebody's afternoon.
        if (_screen.Games.Count > 0 && !WorthAsking.Ask(
            this,
            "Replace what is on the window?",
            "Opening a disc puts its games, its name and its settings here, and takes away what is here now. "
                + "Nothing on disk is touched either way.",
            "Open it",
            "Leave this alone"))
        {
            return;
        }

        Outcome<OpenedProject> read = AProjectFile.Read(
            System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(asking.FileName)));

        if (read is Outcome<OpenedProject>.No refused)
        {
            Explain(refused.Refusal);
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

        _screen.OpenThis(back);

        if (!back.WholeAgain)
        {
            // Opened anyway, and said. Refusing a whole disc over one game
            // somebody moved would be a poor answer to the commonest thing
            // that happens to a project a year later.
            MessageBox.Show(
                this,
                back.InWords + Environment.NewLine + Environment.NewLine
                    + string.Join(Environment.NewLine, back.Gone.Take(10)),
                "Opened",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
        foreach (string folder in folders)
        {
            FolderListing listing = _disk.List(folder);

            // Pointing at the folder that HOLDS the downloads rather than at
            // one of them. Never refused, because a real game can have an
            // installer buried in it too, and taking that whole is better than
            // turning it away.
            IReadOnlyList<string> inside = GamesInside.Of(listing);

            if (inside.Count > 1)
            {
                // Anything sitting loose in the folder belongs to none of the
                // games inside it, so it belongs to the disc. Carried over
                // rather than dropped, because taking a folder apart must not
                // cost somebody a file.
                string[] loose = [.. listing.AtTheTop.Select(file => System.IO.Path.Combine(folder, file.RelativePath))];

                if (AddingThemSeparately(folder, inside, loose.Length))
                {
                    Add([.. inside.Select(each => System.IO.Path.Combine(folder, each))]);
                    _screen.AlsoOnTheDisc(loose);
                    continue;
                }
            }

            switch (_reader.Read(listing))
            {
                case Outcome<GameFolder>.Ok(GameFolder game):
                    // Something that installs onto a game, with no game. It
                    // still goes on if somebody wants it to; it just must not
                    // go on silently, because on the disc it becomes a game in
                    // the menu and installing it does nothing.
                    if (game.Installer is ListedFile installer
                        && AddOn.LooksLikeOne(installer.Name)
                        && LeavingItForItsGame(game))
                    {
                        break;
                    }

                    // What else was in there is asked about before the game
                    // goes on the list, because the answer changes what the
                    // game is.
                    (GameFolder with, IReadOnlyList<LeftBehind> asAddOns) =
                        WhatElseIsInThere.Ask(this, game, WhatWasLeftBehind.In(listing, game));

                    _screen.Add(with, asAddOns);
                    break;

                // Said here and now, about the one folder it is about, rather
                // than kept for the bottom line. The bottom line is about what
                // the disc will be; this is about a folder somebody just
                // picked and can pick again.
                case Outcome<GameFolder>.No refused:
                    Explain(refused.Refusal);
                    break;
            }
        }
    }

    private void TakeItOff(object sender, RoutedEventArgs e)
    {
        // Copied first, because removing from the list while walking the
        // selection is walking a list that is changing underneath.
        foreach (GameFolder game in Games.SelectedItems.OfType<GameFolder>().ToList())
        {
            _screen.Remove(game);
        }
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
            MessageBox.Show(this, wrong.Message, "DiscWright", MessageBoxButton.OK, MessageBoxImage.Error);
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
        string? image = WhichImage();

        if (image is null)
        {
            return;
        }

        try
        {
            await _screen.BurnIt(image, ChooseADrive, MayIEraseIt, ShutTheTray);
        }
        catch (Exception wrong)
        {
            MessageBox.Show(this, wrong.Message, "DiscWright", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_screen.ItWentWrong is Refusal refusal)
        {
            Explain(refusal);
        }

        if (_screen.HowTheDiscCameOut is DiscCheck check)
        {
            MessageBox.Show(
                this,
                check.InWords,
                "DiscWright",
                MessageBoxButton.OK,
                check.Good ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }

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

    private Task<bool> MayIEraseIt(Recorder disc) =>
        Task.FromResult(!WorthAsking.Ask(
            this,
            $"Erase the {disc.Holding} in {disc.Drive}?",
            "Everything on it goes, and it cannot be got back. A quick erase takes under a minute, and then "
                + "the disc can be written to again.",
            "Leave it alone",
            "Erase it"));

    /// <summary>
    /// Gets the disc back in so it can be read, and says whether it came back.
    /// </summary>
    /// <remarks>
    /// The drive is asked first, because a drive that can shut its own tray
    /// should not make somebody walk over to it. Most slim drives cannot, and
    /// then it is a person's job.
    /// </remarks>
    private async Task<bool> ShutTheTray(Recorder drive)
    {
        if (_tray.ShutIt(drive))
        {
            return await WaitForTheDisc(drive);
        }

        MessageBoxResult go = MessageBox.Show(
            this,
            $"The disc is written, and {drive.Drive} has ejected it so it can be read back properly."
                + Environment.NewLine + Environment.NewLine
                + "Close the tray, then press OK to check every file on it.",
            "Written",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Information);

        return go == MessageBoxResult.OK && await WaitForTheDisc(drive);
    }

    private static async Task<bool> WaitForTheDisc(Recorder drive)
    {
        string root = drive.Drive.TrimEnd(System.IO.Path.DirectorySeparatorChar)
            + System.IO.Path.DirectorySeparatorChar;

        for (int tries = 0; tries < 60; tries++)
        {
            try
            {
                if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                {
                    return true;
                }
            }
            catch (IOException)
            {
            }

            await Task.Delay(1000);
        }

        return false;
    }

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
