using DiscWright.Burning;
using DiscWright.Core;
using DiscWright.Imaging;
using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// Writing a real disc, in a real drive, and reading it back.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This one spends a disc, so it does nothing unless it is told to.</strong>
/// Set <c>DISCWRIGHT_SPEND_A_DISC</c> to the drive letter and it will erase
/// what is in that drive if it has to, write to it, and check every file on it
/// afterwards. Left unset, which is every ordinary run and every run on the
/// test VM, it says what it would have done and stops.
/// </para>
/// <para>
/// It is a test rather than a script because it is the only way to find out
/// whether any of the burning works, and something nobody can run is something
/// nobody runs.
/// </para>
/// </remarks>
public class BurningARealDisc(Xunit.Abstractions.ITestOutputHelper says)
{
    private const string TheGoAhead = "DISCWRIGHT_SPEND_A_DISC";

    [Fact]
    public async Task Writes_a_disc_and_reads_every_file_back_off_it()
    {
        string? wanted = Environment.GetEnvironmentVariable(TheGoAhead);

        if (string.IsNullOrWhiteSpace(wanted))
        {
            says.WriteLine($"Nothing was burned: {TheGoAhead} is not set.");
            says.WriteLine("Set it to a drive letter to spend a disc in that drive.");
            return;
        }

        WindowsRecorders asking = new();
        WindowsBurner burner = new();

        Recorder drive = Assert.Single(
            asking.All(),
            each => string.Equals(each.Drive.TrimEnd('\\'), wanted.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

        says.WriteLine($"{drive.Drive} {drive.Name}, holding {drive.Holding}");

        // Erased only because somebody said so, by setting the variable to this
        // drive. Whatever is on it goes.
        if (drive.ErasingWouldHelp)
        {
            says.WriteLine("  erasing it, which takes under a minute...");

            Outcome<string> erased = await burner.BlankIt(
                drive, new Progress<string>(line => says.WriteLine("    " + line)), default);

            says.WriteLine("  " + Assert.IsType<Outcome<string>.Ok>(erased).Value);

            drive = Assert.Single(
                asking.All(),
                each => string.Equals(each.Drive.TrimEnd('\\'), wanted.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

            says.WriteLine($"  it now holds {drive.Holding}, blank={drive.Blank}, {drive.FreeSectors} sectors free");
        }

        // Something small and real, staged and imaged here rather than borrowed,
        // so that what goes on the disc is known exactly.
        string root = Path.Combine(Path.GetTempPath(), "dw-burn-" + Guid.NewGuid().ToString("N")[..8]);
        string staged = Path.Combine(root, "disc");
        Directory.CreateDirectory(staged);

        try
        {
            foreach ((string name, int bytes) in (ValueTuple<string, int>[])
                [("setup_a_game.exe", 400_000), (@"AUTORUN\menu.hta", 8_000), ("READ ME.txt", 300)])
            {
                string full = Path.Combine(staged, name);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);

                byte[] contents = new byte[bytes];
                Random.Shared.NextBytes(contents);
                await File.WriteAllBytesAsync(full, contents);
            }

            string image = Path.Combine(root, "A REAL BURN.iso");

            Outcome<MadeImage> made = await new WindowsImageMaker(new WindowsFileSystem())
                .Make(staged, image, "A REAL BURN", false, null, default);

            MadeImage iso = Assert.IsType<Outcome<MadeImage>.Ok>(made).Value;
            says.WriteLine($"  made {ByteSize.Describe(iso.Bytes)} of image");

            Outcome<Recorder> onto = WhichRecorder.For(asking.All(), iso.Bytes, drive.Drive);
            Recorder target = Assert.IsType<Outcome<Recorder>.Ok>(onto).Value;

            int? speed = WriteSpeeds.Choose(target.Speeds, target.MediaType);
            says.WriteLine($"  writing at {(speed is int s ? WriteSpeeds.InWords(s, target.MediaType) : "the drive's own choice")}");

            List<BurnProgress> reported = [];

            Outcome<BurnedDisc> burned = await burner.Burn(
                image,
                target,
                new Progress<BurnProgress>(step =>
                {
                    reported.Add(step);
                    says.WriteLine("    " + step);
                }),
                default);

            BurnedDisc disc = Assert.IsType<Outcome<BurnedDisc>.Ok>(burned).Value;
            says.WriteLine($"  written in {disc.Seconds} seconds");
            says.WriteLine("  " + disc.AboutTheReporting);

            // Written is not the same as readable, which is the entire point.
            // The drive ejected so that what reads it next sees a freshly
            // mounted volume, so the tray has to go back in.
            says.WriteLine("");

            if (burner.ShutIt(drive))
            {
                says.WriteLine("  the drive shut its own tray. Waiting for the disc...");
            }
            else
            {
                says.WriteLine("  this drive cannot shut its own tray. CLOSE IT BY HAND.");
                Console.Beep(880, 200);
                Console.Beep(660, 400);
            }

            string on = drive.Drive.TrimEnd('\\') + Path.DirectorySeparatorChar;

            if (!await WaitForTheDisc(on))
            {
                says.WriteLine($"  {on} never came back, so the disc was written but not checked.");
                return;
            }

            says.WriteLine($"  {on} is readable again. Checking every file...");

            int checkedFiles = 0;

            foreach (string file in Directory.GetFiles(staged, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(staged, file);
                string onDisc = Path.Combine(on, relative);

                Assert.True(File.Exists(onDisc), $"{relative} is not on the disc");
                Assert.Equal(await File.ReadAllBytesAsync(file), await File.ReadAllBytesAsync(onDisc));
                checkedFiles++;
            }

            says.WriteLine($"  all {checkedFiles} files on the disc match what was built.");
            Console.Beep(1175, 250);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// Waits for the tray to be closed and Windows to read what is in it.
    /// </summary>
    private static async Task<bool> WaitForTheDisc(string on)
    {
        for (int tries = 0; tries < 120; tries++)
        {
            try
            {
                if (Directory.Exists(on) && Directory.GetFileSystemEntries(on).Length > 0)
                {
                    // Read twice over a second, because a disc Windows has only
                    // just noticed can list as empty for a moment.
                    await Task.Delay(1000);

                    if (Directory.GetFileSystemEntries(on).Length > 0)
                    {
                        return true;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            await Task.Delay(1000);
        }

        return false;
    }
}
