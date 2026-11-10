using System.Reflection;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

using DiscWright.Core;
using DiscWright.Burning;
using DiscWright.Imaging;
using DiscWright.Platform;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.App;

/// <summary>
/// Where the real implementations are chosen, and the only place they are.
/// </summary>
/// <remarks>
/// <para>
/// Everything inside the program asks for what it needs by its interface, so
/// this is the one file that knows the answers are Windows ones. That is what
/// lets the same code run under a test with a pretend disk, and it is why there
/// is no <c>new WindowsFileSystem()</c> anywhere else.
/// </para>
/// </remarks>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Before any window exists, so the first thing anybody sees is
        // already wearing the right look rather than flashing the light one.
        // The choice is read back from the one small note this program keeps
        // about itself, and following Windows when there is none.
        ANoteAboutTheLook keptLook = ANoteAboutTheLook.ForThisUser();
        ChoosingTheLook look = new(keptLook.Recalled(), keptLook.Keep);
        WearingTheTheme.Follow(this, look);

        // First, so that everything after it has somewhere to write. One file
        // a day under the user's local application data, never sent anywhere.
        TheLogOnDisk log = new(TheLogOnDisk.ForThisUser());
        log.Write($"DiscWright {WhatThisBuildIs()} started.");
        WorthAsking.Log = log;

        // A fault inside a click would otherwise close the program with no
        // word about why, which is the one thing worse than a crash.
        DispatcherUnhandledException += (_, wrong) =>
        {
            // Whole, in the log, because the box below is gone at OK and the
            // stack is the one thing that says where it happened.
            log.Write($"Stopped by something nobody expected: {wrong.Exception}");

            MessageBox.Show(
                MainWindow,
                wrong.Exception.ToString(),
                "DiscWright stopped",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            wrong.Handled = true;
        };

        WindowsFileSystem disk = new();
        GameFolderReader reader = GameFolderReader.InTheUsualOrder(new FileVersionFacts());

        // The version goes in, because every disc it builds writes a project
        // file that says which build made it.
        DiscBuilder builder = new(
            disk, new WindowsStageWriter(), new WindowsImages(), new Sha256Hasher(), WhatThisBuildIs());

        WindowsBurner burner = new();

        MakeADisc screen = new(new TheMachine(
            new WindowsAutorunEncoding(),
            builder,
            new WindowsImageMaker(disk),
            new TheDrivesWrittenDown(new WindowsRecorders(log), log),
            burner,
            burner,
            disk,
            new Sha256Hasher(),
            new ANoteOnDisk(),
            burner,
            new WhatGitHubHasReleased(),
            new APreviewOfTheMenu(new WindowsStageWriter(), new WindowsImages()),
            new WindowsImages(),
            WhatThisBuildIs(),
            HowBigIs(disk),
            WhereADiscWouldGo()));

        // Everything the window says, as it says it.
        WhatTheLogHears.Listen(screen, log);

        // Previews nobody has looked at in a week. One folder per project
        // keeps them from multiplying; this is what stops them collecting
        // for ever. It cannot throw and its answer is not worth waiting
        // for, so nothing here is checked.
        TidyingOldPreviews.In(Path.GetTempPath(), DateTime.Now);

        MainWindow window = new(screen, reader, disk, burner, look) { TheLog = log };
        window.Show();

        // After it is on screen, because anything this asks about needs a
        // window to sit in front of.
        // Queued rather than called, because this is still OnStartup: the
        // message loop has not begun, and anything that wants to put a window
        // in front of somebody has nowhere to put it yet.
        window.Dispatcher.BeginInvoke(() => window.AddWhatWeWereOpenedWith(e.Args));
    }

    /// <summary>
    /// What this build calls itself.
    /// </summary>
    /// <remarks>
    /// The informational version, which carries the commit after a plus, cut
    /// back to the part that is a version. Read from the assembly rather than
    /// written down twice, because two places that both say what the version
    /// is will disagree eventually and the wrong one will be the one shown.
    /// </remarks>
    /// <summary>
    /// The environment variables a sync client sets to say where it watches.
    /// </summary>
    private static readonly string[] TheSyncedOnes =
        ["OneDrive", "OneDriveCommercial", "OneDriveConsumer"];

    /// <summary>
    /// The folders to suggest a place to build in, as this machine has them.
    /// </summary>
    /// <remarks>
    /// Asked here because this is the only place in the program allowed to
    /// ask Windows anything. The rule about which of them to use is in Core,
    /// where a test can get at it.
    /// </remarks>
    private static WhereDiscsWouldGo WhereADiscWouldGo() =>
        new(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            [
                .. TheSyncedOnes
                    .Select(Environment.GetEnvironmentVariable)
                    .OfType<string>()
                    .Where(each => each.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ]);

    private static string WhatThisBuildIs()
    {
        Assembly? app = Assembly.GetEntryAssembly();

        string? said = app?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? app?.GetName().Version?.ToString();

        if (said is null)
        {
            return string.Empty;
        }

        int plus = said.IndexOf('+', StringComparison.Ordinal);

        return plus >= 0 ? said[..plus] : said;
    }

    /// <summary>
    /// How big a thing is, answered once per path and then remembered.
    /// </summary>
    /// <remarks>
    /// The screen asks this again every time anything changes, which on a
    /// folder of extras means walking it on every keystroke. The sizes it asks
    /// about are of things somebody chose, so they do not move while the window
    /// is open, and the one that would be wrong is a file edited underneath us
    /// between choosing it and building: the build reads it again anyway.
    /// </remarks>
    private static Func<string, long> HowBigIs(WindowsFileSystem disk)
    {
        Dictionary<string, long> known = new(StringComparer.OrdinalIgnoreCase);

        return path =>
        {
            if (known.TryGetValue(path, out long remembered))
            {
                return remembered;
            }

            long bytes = 0;

            try
            {
                bytes = disk.FolderExists(path)
                    ? disk.List(path).Files.Sum(file => file.Bytes)
                    : new FileInfo(path).Length;
            }
            catch (IOException)
            {
                // A thing that has gone away since it was chosen counts as
                // nothing. The build will say so properly; a window must not
                // fall over while somebody is still typing.
            }
            catch (UnauthorizedAccessException)
            {
            }

            known[path] = bytes;
            return bytes;
        };
    }
}

/// <summary>
/// A small picture of a file somebody chose, for showing it back to them.
/// </summary>
/// <remarks>
/// <para>
/// Loaded whole and then let go, rather than left attached to the file. Bound
/// straight to a path, WPF keeps the file open, and the first thing somebody
/// does after choosing the wrong picture is try to replace it.
/// </para>
/// <para>
/// Decoded small on purpose. A cover is a couple of thousand pixels tall and
/// this shows it at forty eight.
/// </para>
/// </remarks>
public sealed class APicture : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            BitmapImage small = new();
            small.BeginInit();
            small.CacheOption = BitmapCacheOption.OnLoad;
            small.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            small.DecodePixelHeight = 96;
            small.UriSource = new Uri(path);
            small.EndInit();
            small.Freeze();

            return small;
        }
        catch (Exception wrong) when (wrong is NotSupportedException or ArgumentException or IOException
            or UnauthorizedAccessException)
        {
            // Why it cannot be read is said beside the picker, by the screen,
            // in words that name the file. A broken thumbnail says it twice
            // and worse.
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Nothing becomes a file by being shown.");
}

/// <summary>
/// A colour the screen keeps as text, shown as the brush it names.
/// </summary>
/// <remarks>
/// For the swatches and the backdrop preview. The screen only ever holds a
/// colour SolidColour tidied, so anything unreadable here is a fault worth
/// seeing, and it comes out transparent rather than thrown because a
/// preview must not take the window down.
/// </remarks>
public sealed class AsABrush : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || text.Length == 0)
        {
            return null;
        }

        try
        {
            System.Windows.Media.SolidColorBrush brush = new((System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(text));
            brush.Freeze();

            return brush;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Nothing becomes a colour by being painted.");
}

/// <summary>
/// Visible when there is something to show, collapsed when there is not.
/// </summary>
/// <remarks>
/// For the refusal and the worry. Left visible, an empty line still takes the
/// height of a line, and two of them under the progress bar were most of the
/// empty space at the bottom of the window.
/// </remarks>
public sealed class WhenAnything : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || (value is string words && string.IsNullOrWhiteSpace(words))
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Nothing comes into being by being shown.");
}

/// <summary>
/// Visible when there is nothing, collapsed when there is something.
/// </summary>
/// <remarks>
/// For the line that stands in for an empty box. An empty dropdown reads as
/// broken, and this says what it will do instead of saying nothing at all.
/// </remarks>
public sealed class WhenNothing : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || (value is string words && string.IsNullOrWhiteSpace(words))
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Nothing ceases to be by being hidden.");
}

/// <summary>Visible when something is true, collapsed otherwise.</summary>
public sealed class WhenSo : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Nothing becomes true by being shown.");
}

/// <summary>Visible when something is false, collapsed otherwise.</summary>
public sealed class WhenNot : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Nothing becomes false by being hidden.");
}

/// <summary>
/// Visible when a count is zero, collapsed otherwise.
/// </summary>
/// <remarks>
/// For the line on the empty list. A converter rather than a property on the
/// screen, because "should this label be visible" is a question about the
/// window, and the screen is deliberately the part that does not know there is
/// one.
/// </remarks>
public sealed class WhenNone : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Nothing sets a count by changing a label.");
}

/// <summary>
/// Visible when a count is more than zero, collapsed otherwise.
/// </summary>
/// <remarks>
/// The mirror of <see cref="WhenNone"/>, for the figures under a refusal. Most
/// refusals carry some and a few carry none, and a row of nothing still takes
/// its margin.
/// </remarks>
public sealed class HeldBack : IValueConverter
{
    /// <summary>
    /// The same dimming the held inputs get, for a thing that cannot be held
    /// itself.
    /// </summary>
    /// <remarks>
    /// The words standing in for an empty drive picker are a sibling of it
    /// rather than a child, so holding the picker during a run left them the
    /// one live looking thing in a column of dead ones. The figure is the one
    /// in Look.xaml and is here because a TextBlock has no disabled state of
    /// its own to trigger on.
    /// </remarks>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is false ? 0.45 : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Nothing starts running by being faded.");
}

/// <summary>
/// Visible when a count is more than zero, collapsed otherwise.
/// </summary>
/// <remarks>
/// The mirror of <see cref="WhenNone"/>, for the figures under a refusal. Most
/// refusals carry some and a few carry none, and a row of nothing still takes
/// its margin.
/// </remarks>
public sealed class WhenSome : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Nothing sets a count by changing a label.");
}
