using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Opening the project files on the discs in the testing kit.
/// </summary>
/// <remarks>
/// <para>
/// Eleven discs built by v1, ten at the current schema and one deliberately
/// older. They are the only cases that prove this reads what DiscWright
/// actually wrote rather than what somebody imagined it wrote, and the first
/// run of them found that every one begins with a byte order mark.
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
    public void Opens_every_disc_in_the_kit()
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
                    says.WriteLine(
                        $"  {Path.GetFileName(disc),-46} v{opened.Version,-3} "
                            + $"{opened.Games.Count()} game(s), {opened.Entries.Count - opened.Games.Count()} add-on(s), "
                            + $"label '{opened.Label}', {(opened.Medium?.Key ?? "automatic")}");
                    break;

                case Outcome<OpenedProject>.No refused:
                    wrong.Add($"{Path.GetFileName(disc)}: {refused.Refusal.Headline}");
                    break;
            }
        }

        Assert.True(wrong.Count == 0, "These would not open: " + string.Join("; ", wrong));
    }

    [Fact]
    public void Finds_the_games_of_every_disc_in_the_kit_still_on_the_disk()
    {
        if (!Directory.Exists(TheKit))
        {
            says.WriteLine("The kit is not on this machine, so nothing was reopened.");
            return;
        }

        GameFolderReader reader = GameFolderReader.InTheUsualOrder(new DiscWright.Platform.FileVersionFacts());
        DiscWright.Platform.WindowsFileSystem disk = new();

        List<string> wrong = [];

        foreach (string disc in Directory.GetDirectories(TheKit).Order(StringComparer.OrdinalIgnoreCase))
        {
            string file = Path.Combine(disc, "discproject.json");

            if (!File.Exists(file))
            {
                continue;
            }

            OpenedProject opened = Assert.IsType<Outcome<OpenedProject>.Ok>(
                AProjectFile.Read(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)))).Value;

            Reopened back = ReopenAProject.From(
                opened, reader, disk, path => File.Exists(path) ? new FileInfo(path).Length : 0);

            says.WriteLine($"  {Path.GetFileName(disc),-46} {back.InWords}");

            // The kit's source folders are all still where they were, so every
            // one of these should come back whole. A disc that does not is
            // either a reader that is wrong or a kit that has moved, and both
            // are worth knowing.
            if (!back.WholeAgain)
            {
                wrong.Add($"{Path.GetFileName(disc)}: {back.InWords}");
            }
        }

        Assert.True(wrong.Count == 0, "These did not come back whole: " + string.Join("; ", wrong));
    }

    [Fact]
    public void Reads_the_older_one_as_an_older_one()
    {
        string file = Path.Combine(TheKit, "11 an older project (schema 11)", "discproject.json");

        if (!File.Exists(file))
        {
            says.WriteLine("The kit is not on this machine, so nothing was opened.");
            return;
        }

        OpenedProject opened = Assert.IsType<Outcome<OpenedProject>.Ok>(
            AProjectFile.Read(System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(file)))).Value;

        says.WriteLine($"  version {opened.Version}, label '{opened.Label}'");

        // It is in the kit precisely because it predates four of the options,
        // and what it describes is a disc built without them.
        Assert.Equal(11, opened.Version);
        Assert.False(opened.Options.ArtworkOnDisc);
        Assert.False(opened.Options.CutFiles);
        Assert.False(opened.WantsTheSetFile);

        // And the caption, which is the one that would have gone the wrong way.
        Assert.True(opened.Look.ShowCaption);
    }
}
