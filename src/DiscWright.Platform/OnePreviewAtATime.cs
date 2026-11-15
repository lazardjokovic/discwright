using System.Diagnostics;

namespace DiscWright.Platform;

/// <summary>
/// The preview windows this program opened, closed before the next ones open.
/// </summary>
/// <remarks>
/// <para>
/// xniwo, discwright#98, 15 November: preview, add more games, preview again,
/// and both windows were open, the first one now drawn from files the second
/// preview had just replaced. A preview is a question about the disc as it
/// stands, so the old answer goes when a new one is asked for.
/// </para>
/// <para>
/// Closed before the new preview is written, not after, because a menu still
/// playing its tune holds that file, and the folder the next preview empties
/// is the same folder.
/// </para>
/// <para>
/// Only the windows this program started. A menu somebody opened themselves,
/// from Start Here on a disc or a disc folder, is theirs and is never touched.
/// </para>
/// </remarks>
public sealed class OnePreviewAtATime(Func<string, Process?> open)
{
    private readonly List<Process> _shown = [];

    /// <summary>The preview windows this program opened, through mshta as the disc does.</summary>
    public static OnePreviewAtATime WithMshta() =>
        new(menu => Process.Start(new ProcessStartInfo("mshta.exe", $"\"{menu}\"") { UseShellExecute = false }));

    /// <summary>How many of the windows opened last are still open.</summary>
    public int StillOpen => _shown.Count(each => !HasGone(each));

    /// <summary>Closes every preview window this program opened, and waits until each has gone.</summary>
    public void CloseTheLast()
    {
        foreach (Process shown in _shown)
        {
            try
            {
                if (!shown.HasExited)
                {
                    shown.Kill(entireProcessTree: true);
                    shown.WaitForExit(5000);
                }
            }
            catch (Exception gone) when (gone is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Closed by hand in the meantime, which is what was wanted.
            }
            finally
            {
                shown.Dispose();
            }
        }

        _shown.Clear();
    }

    /// <summary>Opens one window for each menu, remembering them for the next time.</summary>
    /// <exception cref="System.ComponentModel.Win32Exception">Nothing would open them.</exception>
    public void Open(IEnumerable<string> menus)
    {
        foreach (string menu in menus)
        {
            if (open(menu) is Process started)
            {
                _shown.Add(started);
            }
        }
    }

    private static bool HasGone(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
