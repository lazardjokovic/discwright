using System.Diagnostics;

using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// A preview is replaced by the next one, never piled beside it.
/// </summary>
/// <remarks>
/// xniwo, discwright#98, 15 November: preview, add more games, preview again,
/// and both windows were open, the first drawn from files the second had just
/// replaced. Real processes stand in for mshta here, sitting still the way a
/// menu window does, so closing them is closing something real.
/// </remarks>
public sealed class OnePreviewOnScreen : IDisposable
{
    private readonly List<Process> _everyOneStarted = [];

    public void Dispose()
    {
        foreach (Process each in _everyOneStarted)
        {
            try
            {
                if (!each.HasExited)
                {
                    each.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }

            each.Dispose();
        }
    }

    [Fact]
    public void Closes_the_last_preview_s_windows_and_only_those()
    {
        List<string> asked = [];
        List<int> opened = [];
        OnePreviewAtATime previews = new(menu =>
        {
            asked.Add(menu);
            Process window = SittingStill();
            opened.Add(window.Id);
            return window;
        });

        // Somebody's own menu, opened from Start Here, which is theirs.
        Process theirs = SittingStill();

        // A set of two, both windows.
        previews.Open([@"X:\preview\disc 1\AUTORUN\menu.hta", @"X:\preview\disc 2\AUTORUN\menu.hta"]);
        Assert.Equal(2, previews.StillOpen);

        previews.CloseTheLast();

        // Asked of the processes themselves, not of what the class remembers:
        // it forgets them as it closes them, so its own count says nothing
        // about whether they really went.
        Assert.All(opened, id => Assert.False(Running(id), "A preview window was left open."));
        Assert.Equal(0, previews.StillOpen);
        Assert.False(theirs.HasExited, "A menu this program did not open was closed.");

        // The next preview opens as before, and is the one remembered.
        previews.Open([@"X:\preview\disc 1\AUTORUN\menu.hta"]);
        Assert.Equal(1, previews.StillOpen);
        Assert.Equal(3, asked.Count);
    }

    [Fact]
    public void Closing_a_window_somebody_already_closed_is_nothing()
    {
        OnePreviewAtATime previews = new(_ => SittingStill());
        previews.Open([@"X:\preview\AUTORUN\menu.hta"]);

        foreach (Process each in _everyOneStarted)
        {
            each.Kill(entireProcessTree: true);
            each.WaitForExit();
        }

        previews.CloseTheLast();
        previews.CloseTheLast();

        Assert.Equal(0, previews.StillOpen);
    }

    [Fact]
    public void The_window_closes_the_last_preview_before_writing_the_next()
    {
        // Before, not after: a menu playing its tune holds that file, and the
        // next preview empties the same folder. And every window goes through
        // the one that remembers them, so none is opened past it.
        //
        // Read off disk, because nothing here may reference the window.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        string preview = code[code.IndexOf("private void PreviewTheMenu(", StringComparison.Ordinal)..];
        preview = preview[..preview.IndexOf("/// <summary>", StringComparison.Ordinal)];

        int closing = preview.IndexOf("_previews.CloseTheLast();", StringComparison.Ordinal);
        int writing = preview.IndexOf("_screen.PreviewTheMenu(into);", StringComparison.Ordinal);

        Assert.True(closing >= 0 && writing > closing, "The last preview is not closed before the next is written.");
        Assert.Contains("_previews.Open(", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("\"mshta.exe\"", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a process of that id is still running, asked of Windows. The
    /// class disposes what it closes, so the objects it was handed cannot be
    /// asked afterwards.
    /// </summary>
    private static bool Running(int id)
    {
        try
        {
            using Process found = Process.GetProcessById(id);

            return !found.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private Process SittingStill()
    {
        Process started = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 120 127.0.0.1 >nul")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

        _everyOneStarted.Add(started);

        return started;
    }
}
