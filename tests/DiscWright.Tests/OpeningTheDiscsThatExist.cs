using System.Text.Json;
using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Opening the project files on the discs in the testing kit.
/// </summary>
/// <remarks>
/// <para>
/// Eleven discs built by v1, ten at the current schema and one deliberately
/// older. They are the only cases that prove this recognises what the
/// PowerShell DiscWright actually wrote rather than what somebody imagined it
/// wrote, and the first run of them found that every one begins with a byte
/// order mark. Every one of them is refused by name now, with the version
/// that wrote it: the look of v1's menu is in each file, this menu has none
/// of it, and the owner chose saying so over opening the disc as something
/// else.
/// </para>
/// <para>
/// The kit lives outside the repository and is not on the test VM, so these
/// say so and stop when it is not there. The rules themselves are covered by
/// <c>OpeningAProjectFile</c>, which needs nothing but text.
/// </para>
/// </remarks>
public class OpeningTheDiscsThatExist(Xunit.Abstractions.ITestOutputHelper says)
{
    /// <summary>
    /// Where the kit is, looked for rather than written down.
    /// </summary>
    /// <remarks>
    /// A path with somebody's name in it means this never runs anywhere but
    /// the one machine, and a test that never runs reports as a pass. The
    /// variable is for putting the kit somewhere else; the default is beside
    /// whoever is logged in, which is where it lives on the test VM too.
    /// </remarks>
    private static readonly string TheKit = Path.Combine(
        Environment.GetEnvironmentVariable("DISCWRIGHT_KIT")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DiscWright-Kit"),
        "2 DISCS - open these");

    [Fact]
    public void Refuses_every_disc_in_the_kit_by_name()
    {
        if (!Directory.Exists(TheKit))
        {
            says.WriteLine($"The kit is not on this machine, so nothing was opened. Looked in {TheKit}.");
            return;
        }

        string[] discs = [.. Directory.GetDirectories(TheKit).Order(StringComparer.OrdinalIgnoreCase)];

        Assert.NotEmpty(discs);

        List<string> wrong = [];

        foreach (string disc in discs)
        {
            string file = Path.Combine(disc, "discproject.json");

            if (!File.Exists(file))
            {
                wrong.Add($"{Path.GetFileName(disc)}: no project file");
                continue;
            }

            // Read as bytes and handed over as text, which is how this would be
            // reached from anywhere that is not .NET's own file reading, and
            // the way that keeps the byte order mark in play.
            string json = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file));

            switch (AProjectFile.Read(json))
            {
                case Outcome<OpenedProject>.Ok(OpenedProject opened):
                    wrong.Add($"{Path.GetFileName(disc)}: opened as v{opened.Version} by {opened.MadeBy}");
                    break;

                case Outcome<OpenedProject>.No(Refusal refusal):
                    says.WriteLine($"  {Path.GetFileName(disc),-46} {refusal.Headline}");

                    // Refused as the other program's, not as something that
                    // is not a project: a file this cannot read past its mark
                    // would land in the other refusal, and so would a file
                    // the recogniser no longer knows.
                    if (refusal.Headline != "That project is from the PowerShell DiscWright")
                    {
                        wrong.Add($"{Path.GetFileName(disc)}: {refusal.Headline}");
                    }

                    // And it names the version that wrote it, read straight
                    // off the file here so the test cannot agree with the
                    // reader by accident.
                    string wroteIt = JsonDocument.Parse(json.TrimStart((char)0xFEFF)).RootElement.GetProperty("AppVersion").GetString()!;

                    if (!refusal.Because.Contains($"written by DiscWright {wroteIt}, the PowerShell version", StringComparison.Ordinal))
                    {
                        wrong.Add($"{Path.GetFileName(disc)}: does not say {wroteIt} wrote it: {refusal.Because}");
                    }

                    break;
            }
        }

        Assert.True(wrong.Count == 0, "These were not refused by name: " + string.Join("; ", wrong));
    }

    [Fact]
    public void Names_the_older_version_that_wrote_the_older_one()
    {
        string file = Path.Combine(TheKit, "11 an older project (schema 11)", "discproject.json");

        if (!File.Exists(file))
        {
            says.WriteLine("The kit is not on this machine, so nothing was opened.");
            return;
        }

        Refusal refusal = Assert.IsType<Outcome<OpenedProject>.No>(
            AProjectFile.Read(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)))).Refusal;

        says.WriteLine($"  {refusal.Because}");

        // It is in the kit precisely because it predates four of the options,
        // and the look of the menu predates all of them: v1 wrote it from
        // 0.1.0, so the older disc is known the same way as the rest.
        Assert.Contains("DiscWright 0.9.2, the PowerShell version", refusal.Because, StringComparison.Ordinal);
    }
}
