using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

using DiscWright.Core;
using DiscWright.Platform;
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

        WindowsFileSystem disk = new();
        GameFolderReader reader = GameFolderReader.InTheUsualOrder(new FileVersionFacts());

        MakeADisc screen = new(new WindowsAutorunEncoding(), HowBigIs(disk));

        new MainWindow(screen, reader, disk).Show();
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
