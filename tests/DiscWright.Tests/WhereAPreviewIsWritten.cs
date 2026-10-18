using DiscWright.Core;
using DiscWright.Menu;
using DiscWright.Staging;

namespace DiscWright.Tests;

/// <summary>
/// One preview folder per project, rather than one per press.
/// </summary>
/// <remarks>
/// <para>
/// Previews went into a folder stamped with the time, so every press made a
/// new one and nothing ever removed any of them. Twenty eight folders and
/// 14 MB had collected in three days on one machine.
/// </para>
/// <para>
/// Reusing a folder was argued against once, and the argument was sound: a
/// preview that refuses to plan would leave the previous one sitting there
/// looking like the answer. That is why these check the emptying as well as
/// the naming, and check it happens even when nothing is written.
/// </para>
/// </remarks>
public class WhereAPreviewIsWritten
{
    [Fact]
    public void The_same_project_goes_to_the_same_folder_every_time()
    {
        // The whole point. Two presses, one folder.
        Assert.Equal(
            WhereAPreviewGoes.For("HOLLOW KNIGHT", @"F:\sets\hk", @"C:\games\Hollow Knight"),
            WhereAPreviewGoes.For("HOLLOW KNIGHT", @"F:\sets\hk", @"C:\games\Hollow Knight"));
    }

    [Fact]
    public void Two_projects_with_the_same_name_do_not_share_one()
    {
        // Sharing would be worse than the mess this replaces: a preview
        // showing the other project's games, with nothing to say so.
        Assert.NotEqual(
            WhereAPreviewGoes.For("SETUP", @"F:\sets\one", @"C:\games\one"),
            WhereAPreviewGoes.For("SETUP", @"F:\sets\two", @"C:\games\two"));
    }

    [Fact]
    public void Tells_two_projects_apart_before_either_has_anywhere_to_build()
    {
        // A preview can be asked for with no output folder chosen, so the
        // name cannot depend on there being one.
        Assert.NotEqual(
            WhereAPreviewGoes.For("SETUP", "", @"C:\games\one"),
            WhereAPreviewGoes.For("SETUP", "", @"C:\games\two"));
    }

    [Fact]
    public void Choosing_where_it_builds_settles_which_project_it_is()
    {
        // Where it builds to is the better answer, so once there is one it
        // decides, even though the games are the same.
        Assert.NotEqual(
            WhereAPreviewGoes.For("SETUP", @"F:\sets\one", @"C:\games\same"),
            WhereAPreviewGoes.For("SETUP", @"F:\sets\two", @"C:\games\same"));
    }

    [Theory]
    [InlineData(@"HOLLOW KNIGHT: VOIDHEART")]
    [InlineData(@"C:\somewhere\else")]
    [InlineData("what? *now*")]
    [InlineData("a/b")]
    public void The_name_is_one_a_folder_can_have(string label)
    {
        string name = WhereAPreviewGoes.For(label, @"F:\sets\x", null);

        // Not a list of what to strip. Whatever Windows refuses, this refuses,
        // because the test asking the same question the operating system does
        // cannot fall behind it.
        Assert.DoesNotContain(name, each => System.IO.Path.GetInvalidFileNameChars().Contains(each));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("???")]
    public void A_label_that_amounts_to_nothing_still_makes_a_name(string? label)
    {
        string name = WhereAPreviewGoes.For(label, @"F:\sets\x", null);

        Assert.StartsWith("a disc-", name, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_never_ends_in_a_dot_or_a_space()
    {
        // Windows makes the folder and then cannot open it by the name it was
        // asked for, which is a fault nobody would look for here.
        string name = WhereAPreviewGoes.For("Fallout 2. ", @"F:\sets\x", null);

        Assert.False(name.EndsWith('.') || name.EndsWith(' '));
    }

    [Fact]
    public void A_very_long_name_is_cut_down()
    {
        string name = WhereAPreviewGoes.For(new string('x', 300), @"F:\sets\x", null);

        // Room for a temporary folder in front of it and a menu path behind.
        Assert.True(name.Length <= 64, $"the folder name is {name.Length} characters");
    }

    [Fact]
    public void The_label_is_still_readable_in_it()
    {
        // Somebody opening their temporary folder should be able to see what
        // these are without opening them.
        Assert.StartsWith("HOLLOW KNIGHT-", WhereAPreviewGoes.For(
            "HOLLOW KNIGHT", @"F:\sets\hk", null), StringComparison.Ordinal);
    }

    [Fact]
    public void The_folder_is_emptied_before_the_preview_is_written()
    {
        RecordingWriter wrote = new();

        new APreviewOfTheMenu(wrote, new PicturesThatRead())
            .Write(AProject(), Media.Dvd5, _ => 1000, Look);

        Assert.Contains(Look, wrote.Emptied);
    }

    [Fact]
    public void The_folder_is_emptied_even_when_there_is_nothing_to_show()
    {
        RecordingWriter wrote = new();

        // One file bigger than a whole disc, so planning refuses and nothing
        // is written at all. The size has to be on the file, because that is
        // what planning reads.
        ListedFile huge = new("setup_enormous.exe", 9L * 1024 * 1024 * 1024);

        DiscProject enormous = AProject() with
        {
            Games = [new GameFolder(
                @"C:\games\x", GameKind.GogInstaller, "Enormous", "Enormous", huge, [huge], [], null)]
        };

        Outcome<IReadOnlyList<string>> went = new APreviewOfTheMenu(wrote, new PicturesThatRead())
            .Write(enormous, Media.Dvd5, _ => huge.Bytes, Look);

        Assert.IsType<Outcome<IReadOnlyList<string>>.No>(went);

        // The reason the time stamp was there. Left alone, the last preview of
        // this project would still be sitting in the folder, and the next
        // thing to open that path would show it as though it were an answer to
        // what was just asked.
        Assert.Contains(Look, wrote.Emptied);
        Assert.Empty(wrote.Texts);
    }

    [Fact]
    public void The_window_asks_where_the_preview_goes_rather_than_inventing_it()
    {
        // Everything above tests the rule and the wiring behind the window.
        // Nothing tests the window, which is where the fault actually lived:
        // one line composing a folder out of the clock. Read off disk, the
        // way the markup guards read the windows, because nothing here may
        // reference the app.
        string code = System.IO.File.ReadAllText(
            Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Assert.Contains("_screen.WhereAPreviewWouldGo", code, StringComparison.Ordinal);

        // The exact thing it used to do.
        Assert.DoesNotContain("yyyyMMdd-HHmmss", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// Where a preview really goes.
    /// </summary>
    /// <remarks>
    /// An invented path was used here, and the writer these tests stand on
    /// would refuse to empty it, which is how a preview that wrote nothing
    /// at all went green through every one of them.
    /// </remarks>
    private static readonly string Look = WhereAPreviewGoes.Under(
        System.IO.Path.GetTempPath(), "HOLLOW KNIGHT", @"F:\sets\hk", null);

    private static DiscProject AProject() =>
        new("HOLLOW KNIGHT", [AGame("Hollow Knight")], new DiscOptions());

    private static GameFolder AGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, only, [only], [], null);
    }

    /// <summary>Pictures that can all be read, which is the ordinary case.</summary>
    private sealed class PicturesThatRead : IImages
    {
        public ImageFacts Read(string path) => new(true, 512, 512, null);

        public void WriteIcon(string source, string destination)
        {
        }

        public void WriteBackground(string source, string destination, int width, int height, bool asIs)
        {
        }
    }
}
