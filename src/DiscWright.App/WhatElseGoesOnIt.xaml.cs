using DiscWright.Core;
using System.Windows;
using System.Windows.Input;
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

        // Drawn 700 tall, which is more than an older laptop has once the
        // taskbar has its share. What is in it scrolls, so shorter loses
        // nothing.
        OnThisScreen.NoTallerThanTheScreen(this);
    }

    private void ChooseTheDiscIcon(object sender, RoutedEventArgs e) =>
        TakeAPicture("The icon the disc wears", path => _screen.DiscIcon = path);

    private void ClearTheDiscIcon(object sender, RoutedEventArgs e) => _screen.DiscIcon = null;

    private void ChooseTheBackground(object sender, RoutedEventArgs e) =>
        TakeAPicture("The picture the menu is drawn on", path => _screen.Background = path);

    private void ClearTheBackground(object sender, RoutedEventArgs e) => _screen.Background = null;

    // ---------------------------------------- the part of the picture kept
    //
    // Every sum is the screen's. This turns a pointer's travel into a
    // fraction of the frame, which is all the screen is ever told.

    private Point? _heldAt;

    private void TakeHoldOfThePicture(object sender, MouseButtonEventArgs e)
    {
        _heldAt = e.GetPosition(TheFrame);

        TheFrame.Focus();
        TheFrame.CaptureMouse();
        e.Handled = true;
    }

    private void DragThePicture(object sender, MouseEventArgs e)
    {
        if (_heldAt is not { } from || TheFrame.ActualWidth <= 0 || TheFrame.ActualHeight <= 0)
        {
            return;
        }

        Point now = e.GetPosition(TheFrame);

        _screen.DragTheBackground(
            (now.X - from.X) / TheFrame.ActualWidth,
            (now.Y - from.Y) / TheFrame.ActualHeight);

        _heldAt = now;
    }

    private void LetGoOfThePicture(object sender, MouseButtonEventArgs e) => TheFrame.ReleaseMouseCapture();

    // Also when Windows takes the pointer away, a dialog opening or Alt and
    // Tab: without this the picture would go on following a pointer that is
    // no longer held down.
    private void ThePictureWasLetGo(object sender, MouseEventArgs e) => _heldAt = null;

    private void ZoomWithTheWheel(object sender, MouseWheelEventArgs e)
    {
        _screen.BackgroundZoom += e.Delta > 0 ? 0.1 : -0.1;

        // Or the dialog scrolls under a pointer that is zooming.
        e.Handled = true;
    }

    private void NudgeThePicture(object sender, KeyEventArgs e)
    {
        // The arrows move the picture the way they point, as a drag does.
        const double AStep = 0.05;

        (double across, double down) = e.Key switch
        {
            Key.Left => (-AStep, 0.0),
            Key.Right => (AStep, 0.0),
            Key.Up => (0.0, -AStep),
            Key.Down => (0.0, AStep),
            _ => (0.0, 0.0),
        };

        if (across != 0 || down != 0)
        {
            _screen.DragTheBackground(across, down);
            e.Handled = true;
        }
    }

    private void KeepTheMiddle(object sender, RoutedEventArgs e) => _screen.KeepTheMiddleOfTheBackground();

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
            MusicForTheMenu.ForThePicker,
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
