using System.Diagnostics;
using System.IO;
using System.Windows;

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

    public MainWindow(MakeADisc screen, GameFolderReader reader, IFileSystem disk)
    {
        _screen = screen;
        _reader = reader;
        _disk = disk;

        InitializeComponent();
        DataContext = screen;
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
