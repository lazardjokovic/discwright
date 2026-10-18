using System.Text;

using DiscWright.Core;
using DiscWright.Core.Staging;

namespace DiscWright.Tests;

public class TheAutorunFile
{
    [Fact]
    public void Gives_the_disc_its_name_and_its_icon()
    {
        string file = AutorunFile.For("HOLLOW KNIGHT", "disc.ico", hasMenu: true);

        Assert.Contains("label=HOLLOW KNIGHT", file, StringComparison.Ordinal);
        Assert.Contains("icon=disc.ico", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Points_at_the_menu_and_says_what_running_it_means()
    {
        string file = AutorunFile.For("HOLLOW KNIGHT", "disc.ico", hasMenu: true);

        Assert.Contains(@"shellexecute=AUTORUN\menu.hta", file, StringComparison.Ordinal);

        // Without this Windows offers "Run menu.hta", which tells somebody
        // nothing about what is in their hand.
        Assert.Contains("action=Run HOLLOW KNIGHT", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Leaves_the_menu_out_when_there_is_none()
    {
        string file = AutorunFile.For("HOLLOW KNIGHT", "disc.ico", hasMenu: false);

        // The disc still has a name and an icon. It simply opens in Explorer.
        Assert.DoesNotContain("shellexecute", file, StringComparison.Ordinal);
        Assert.DoesNotContain("action=", file, StringComparison.Ordinal);
        Assert.Contains("label=HOLLOW KNIGHT", file, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Leaves_the_icon_out_when_the_disc_carries_none(string? icon)
    {
        // icon= used to be written whatever it was handed, so a disc with no
        // icon on it got a line pointing Windows at a file that was not
        // there. A directive naming nothing is worse than no directive: the
        // file reads as though somebody meant it.
        string file = AutorunFile.For("HOLLOW KNIGHT", icon, hasMenu: true);

        Assert.DoesNotContain("icon=", file, StringComparison.Ordinal);

        // Everything else about the disc is unchanged.
        Assert.Contains("label=HOLLOW KNIGHT", file, StringComparison.Ordinal);
        Assert.Contains(@"shellexecute=AUTORUN\menu.hta", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_an_icon_that_sits_in_a_folder()
    {
        // Ours lives beside the menu rather than at the root, so the path
        // has a separator in it and autorun.inf has to carry it whole.
        string file = AutorunFile.For("X", @"AUTORUN\menu.ico", hasMenu: true);

        Assert.Contains(@"icon=AUTORUN\menu.ico", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Turns_off_the_prompts_about_the_wrong_thing()
    {
        string file = AutorunFile.For("X", "disc.ico", hasMenu: true);

        // Explorer otherwise offers to import the disc's pictures or play its
        // music, which for a game disc is a question about the wrong thing.
        Assert.Contains("MusicFiles=false", file, StringComparison.Ordinal);
        Assert.Contains("PictureFiles=false", file, StringComparison.Ordinal);
        Assert.Contains("VideoFiles=false", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_written_one_directive_to_a_line_with_Windows_line_endings()
    {
        string file = AutorunFile.For("X", "disc.ico", hasMenu: true);

        Assert.StartsWith("[autorun]\r\n", file, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", file, StringComparison.Ordinal);

        // Every line ending is a pair. A bare newline in a Windows
        // configuration file, read by a part of Windows older than most of it,
        // is asking for a directive to be misread.
        Assert.Equal(file.Split('\n').Length, file.Split("\r\n").Length);
    }

    [Fact]
    public void Will_not_let_a_label_write_a_directive_of_its_own()
    {
        // The one that matters. autorun.inf is one directive per line, so a
        // label carrying a newline writes further directives, and the one
        // below would be chosen by whoever wrote the project file, on a disc
        // the person burning it believed was theirs.
        string file = AutorunFile.For(
            "My Game\r\nopen=Extras\\payload.exe", "disc.ico", hasMenu: false);

        // Asserted per LINE rather than on the whole file. The stripped label
        // really does still contain the text "open=", harmlessly, as part of
        // the disc's name. What must never happen is a line that STARTS with
        // it, because that is what AutoRun reads as a directive.
        Assert.DoesNotContain(
            file.Split("\r\n"),
            line => line.StartsWith("open=", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(@"label=My Gameopen=Extras\payload.exe", file, StringComparison.Ordinal);
    }

    [Fact]
    public void Will_not_let_an_icon_name_do_it_either()
    {
        string file = AutorunFile.For("X", "disc.ico\r\nopen=payload.exe", hasMenu: false);

        Assert.DoesNotContain("\r\nopen=", file, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Numbers_the_discs_of_a_set_so_a_drawer_full_makes_sense()
    {
        RecordingWriter wrote = new();

        await new WriteTheAutorunStep().Run(ContextFor(wrote, disc: 2, of: 3), CancellationToken.None);

        Assert.Contains("label=HOLLOW KNIGHT 2", Written(wrote), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leaves_a_single_disc_unnumbered()
    {
        RecordingWriter wrote = new();

        await new WriteTheAutorunStep().Run(ContextFor(wrote, disc: 1, of: 1), CancellationToken.None);

        Assert.Contains("label=HOLLOW KNIGHT\r\n", Written(wrote), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Is_written_the_way_AutoRun_reads_rather_than_the_way_everything_else_does()
    {
        RecordingWriter wrote = new();

        await new WriteTheAutorunStep().Run(ContextFor(wrote, disc: 1, of: 1), CancellationToken.None);

        // Asked for through the method that means "AutoRun will read this", not
        // the ordinary one. The two are different encodings and only the port
        // knows which.
        Assert.Single(wrote.AutorunTexts);
        Assert.Empty(wrote.Texts);
    }

    private static string Written(RecordingWriter wrote) => wrote.AutorunTexts.Single().Text;

    private static BuildContext ContextFor(RecordingWriter wrote, int disc, int of)
    {
        List<PlannedDisc> discs = [.. Enumerable.Range(1, of).Select(number => new PlannedDisc(number, [], 0))];

        return new BuildContext(
            new DiscProject("HOLLOW KNIGHT", [], new DiscOptions()),
            new DiscLayout(discs, 1_000_000, 0),
            discs[disc - 1],
            @"C:\stage",
            wrote);
    }
}

public class TheDiscLabel
{
    [Fact]
    public void Is_happy_with_a_name_the_codepage_can_carry()
    {
        Assert.Null(DiscLabel.WhatWindowsWillMakeOfIt("Hollow Knight", new Codepage1252()));
    }

    [Fact]
    public void Is_happy_with_accents_because_the_codepage_carries_those()
    {
        // ASCII turned these into question marks, so "Uber Alles" reached
        // Explorer as "?ber Alles". The ANSI codepage carries them.
        Assert.Null(DiscLabel.WhatWindowsWillMakeOfIt("\u00dcber Alles", new Codepage1252()));
    }

    [Fact]
    public void Shows_what_the_disc_will_actually_say()
    {
        Refusal? worry = DiscLabel.WhatWindowsWillMakeOfIt("\u0414\u0430\u043d\u043d\u044b\u0435", new Codepage1252());

        Assert.NotNull(worry);

        // The surviving text, not a sentence about characters being lost.
        // Seeing "??????" is what makes the decision obvious.
        Assert.Contains(worry.Figures, figure => figure.Value == "??????");
        Assert.Equal(RemedyKind.FocusField, worry.Remedy?.Kind);
    }

    [Fact]
    public void Wants_a_name_at_all()
    {
        Assert.NotNull(DiscLabel.WhatWindowsWillMakeOfIt("   ", new Codepage1252()));
    }

    /// <summary>
    /// Both machines this runs on are 1252, but a test that asked the machine
    /// would pass or fail depending on where it ran.
    /// </summary>
    private sealed class Codepage1252 : IAutorunEncoding
    {
        private static readonly Encoding Ansi = MakeIt();

        public string AsAutorunWillSeeIt(string text) => Ansi.GetString(Ansi.GetBytes(text));

        private static Encoding MakeIt()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
    }
}
