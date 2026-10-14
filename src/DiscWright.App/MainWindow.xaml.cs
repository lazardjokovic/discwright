using System.Windows;

using DiscWright.Core;
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

        foreach (string folder in asking.FolderNames)
        {
            switch (_reader.Read(_disk.List(folder)))
            {
                case Outcome<GameFolder>.Ok(GameFolder game):
                    _screen.Add(game);
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

    private void MakeTheDisc(object sender, RoutedEventArgs e) =>
        // Next: the working screen, and the build behind it. The button is
        // wired now so that nothing about the window has to change when the
        // build arrives, and so an automated test can already find it.
        MessageBox.Show(
            this,
            $"{_screen.WhatItWillMake}, into {_screen.OutputFolder}.",
            "DiscWright",
            MessageBoxButton.OK,
            MessageBoxImage.Information);

    private void Explain(Refusal refusal) =>
        MessageBox.Show(
            this,
            refusal.Because.Length == 0 ? refusal.Headline : $"{refusal.Headline}\n\n{refusal.Because}",
            "DiscWright",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
}
