using System.Windows;
using DiscWright.ViewModels;
using Microsoft.Win32;

namespace DiscWright.App;

/// <summary>
/// Everything that goes on a disc and is not a game.
/// </summary>
/// <remarks>
/// <para>
/// It holds no decisions either. Every choice goes straight to
/// <see cref="MakeADisc"/>, which is also what the main window is bound to, so
/// closing this window needs no handing anything back: both are looking at the
/// same screen.
/// </para>
/// <para>
/// A picture is checked the moment it is picked rather than at build time. The
/// icon used to be checked when it was chosen and the background never was, so
/// a file Windows cannot decode sailed through the whole window and failed at
/// the end as "A generic error occurred in GDI+", which names no file.
/// </para>
/// </remarks>
public partial class WhatElseGoesOnIt : Window
{
    private readonly MakeADisc _screen;

    public WhatElseGoesOnIt(MakeADisc screen)
    {
        _screen = screen;

        InitializeComponent();

        DataContext = screen;
    }

    private void ChooseTheDiscIcon(object sender, RoutedEventArgs e) =>
        TakeAPicture("The icon the disc wears", path => _screen.DiscIcon = path);

    private void ClearTheDiscIcon(object sender, RoutedEventArgs e) => _screen.DiscIcon = null;

    private void ChooseTheBackground(object sender, RoutedEventArgs e) =>
        TakeAPicture("The picture the menu is drawn on", path => _screen.Background = path);

    private void ClearTheBackground(object sender, RoutedEventArgs e) => _screen.Background = null;

    private void PickAColour(object sender, RoutedEventArgs e) =>
        _screen.BackgroundColourText = (string)((FrameworkElement)sender).DataContext;

    private void ChooseTheCover(object sender, RoutedEventArgs e) =>
        TakeAPicture("The picture for the printed cover", path => _screen.Cover = path);

    private void ClearTheCover(object sender, RoutedEventArgs e) => _screen.Cover = null;

    private void ChooseTheDiscFace(object sender, RoutedEventArgs e) =>
        TakeAPicture("The picture for the printed disc face", path => _screen.DiscFace = path);

    private void ClearTheDiscFace(object sender, RoutedEventArgs e) => _screen.DiscFace = null;

    private void ChooseTheMusic(object sender, RoutedEventArgs e) =>
        TakeAFile(
            "What plays behind the menu",
            "Music|*.mp3;*.wav;*.wma;*.m4a|Anything|*.*",
            path => _screen.Music = path);

    private void ClearTheMusic(object sender, RoutedEventArgs e) => _screen.Music = null;

    private void ChooseTheManual(object sender, RoutedEventArgs e) =>
        TakeAFile(
            "The manual",
            "A manual|*.pdf;*.txt;*.htm;*.html;*.rtf;*.chm|Anything|*.*",
            path => _screen.Manual = path);

    private void ClearTheManual(object sender, RoutedEventArgs e) => _screen.Manual = null;

    private void ChooseTheExtras(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog asking = new() { Title = "A folder of extras to put on the disc" };

        if (asking.ShowDialog(this) == true)
        {
            _screen.ExtrasFolder = asking.FolderName;
        }
    }

    private void ClearTheExtras(object sender, RoutedEventArgs e) => _screen.ExtrasFolder = null;

    private void Done(object sender, RoutedEventArgs e) => Close();

    private void TakeAPicture(string title, Action<string> keep) =>
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
}
