using System.Reflection;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

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

        // A fault inside a click would otherwise close the program with no
        // word about why, which is the one thing worse than a crash.
        DispatcherUnhandledException += (_, wrong) =>
        {
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

        DiscBuilder builder = new(disk, new WindowsStageWriter(), new WindowsImages(), new Sha256Hasher());

        WindowsBurner burner = new();

        MakeADisc screen = new(new TheMachine(
            new WindowsAutorunEncoding(),
            builder,
            new WindowsImageMaker(disk),
            new WindowsRecorders(),
            burner,
            burner,
            disk,
            new Sha256Hasher(),
            new ANoteOnDisk(),
            burner,
            new WhatGitHubHasReleased(),
            new APreviewOfTheMenu(new WindowsStageWriter()),
            new WindowsImages(),
            WhatThisBuildIs(),
            HowBigIs(disk)));

        MainWindow window = new(screen, reader, disk, burner);
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
