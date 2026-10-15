using System.Diagnostics;

using DiscWright.Core;
using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// Listing the root of a drive, which is the one shape that was wrong.
/// </summary>
/// <remarks>
/// <para>
/// Every folder in this program is listed the same way, and it worked for
/// every one of them for days, because a drive root is the only path whose
/// trailing separator cannot be taken off. Trimmed to <c>E:</c> it stops
/// meaning the root and starts meaning "wherever this process last was on drive
/// E", Windows hands back paths with no separator in them, and the arithmetic
/// that worked out where the name began took one character too many.
/// </para>
/// <para>
/// A burned disc is read back from a drive root and nothing else is, which is
/// why the first time it mattered was with a disc in a drive.
/// </para>
/// <para>
/// A drive is made for the test with <c>subst</c>, which maps a letter to a
/// folder and is undone at the end. If that cannot be done, the test says so
/// and stops rather than pretending.
/// </para>
/// </remarks>
public class ListingADriveRoot(Xunit.Abstractions.ITestOutputHelper says)
{
    [Fact]
    public void Gives_back_the_names_that_are_actually_on_it()
    {
        string folder = Path.Combine(Path.GetTempPath(), "dw-root-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "AUTORUN"));
        File.WriteAllText(Path.Combine(folder, "autorun.inf"), "a");
        File.WriteAllText(Path.Combine(folder, "READ ME.txt"), "bb");
        File.WriteAllText(Path.Combine(folder, "AUTORUN", "menu.hta"), "ccc");

        string? letter = APretendDrive(folder);

        try
        {
            if (letter is null)
            {
                says.WriteLine("No spare drive letter, or subst would not run. Nothing was checked.");
                return;
            }

            FolderListing listing = new WindowsFileSystem().List(letter + Path.DirectorySeparatorChar);

            string[] names = [.. listing.Files.Select(file => file.RelativePath).Order(StringComparer.Ordinal)];

            foreach (string name in names)
            {
                says.WriteLine($"  [{name}]");
            }

            // In the order an ordinal sort puts them, which is capitals first.
            Assert.Equal([@"AUTORUN\menu.hta", "READ ME.txt", "autorun.inf"], names);
        }
        finally
        {
            if (letter is not null)
            {
                Run("subst", letter + " /d");
            }

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>A drive letter standing in for a folder, or null.</summary>
    private static string? APretendDrive(string folder)
    {
        foreach (char letter in "WXYV")
        {
            string drive = letter + ":";

            if (Directory.Exists(drive + Path.DirectorySeparatorChar))
            {
                continue;
            }

            if (Run("subst", $"{drive} \"{folder}\"") && Directory.Exists(drive + Path.DirectorySeparatorChar))
            {
                return drive;
            }
        }

        return null;
    }

    private static bool Run(string what, string how)
    {
        try
        {
            using Process? ran = Process.Start(new ProcessStartInfo(what, how)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (ran is null)
            {
                return false;
            }

            ran.WaitForExit(10_000);

            return ran.ExitCode == 0;
        }
        catch (Exception wrong) when (wrong is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
