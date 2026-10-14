using System.Text.RegularExpressions;

using DiscWright.Menu;

namespace DiscWright.Tests;

public class TheMenuWriter
{
    [Fact]
    public void Leaves_no_placeholder_behind_in_a_built_menu()
    {
        string page = MenuWriter.MenuPage(Facts());

        // The lesson of %%SINGLE%%. A placeholder left in a built menu is a disc
        // that opens to nothing, and it cannot be fixed once the disc is burned.
        Assert.DoesNotMatch(MenuWriter.PlaceholderPattern, page);
    }

    [Fact]
    public void Leaves_no_placeholder_behind_for_an_awkward_name_either()
    {
        string page = MenuWriter.MenuPage(Facts(name: "100% complete & <b>bold</b>"));

        Assert.DoesNotMatch(MenuWriter.PlaceholderPattern, page);
    }

    [Fact]
    public void Includes_the_logic_rather_than_repeating_it()
    {
        string page = MenuWriter.MenuPage(Facts());

        Assert.Contains("src=\"menu.js\"", page, StringComparison.Ordinal);

        // If a function from menu.js were pasted in here as well, the disc
        // would carry two copies and only one of them would be tested.
        Assert.DoesNotContain("function discTitle", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Hands_back_the_logic_file_untouched()
    {
        string script = MenuWriter.MenuScript();

        // Nothing is substituted into it, so the file the cscript tests run and
        // the file that ends up on the disc are the same bytes.
        Assert.DoesNotMatch(MenuWriter.PlaceholderPattern, script);
        Assert.Contains("function discTitle", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Carries_the_same_logic_the_tests_run()
    {
        // The embedded copy against the file on disk. If these ever drift, the
        // cscript tests would be testing something the disc does not carry.
        Assert.Equal(
            File.ReadAllText(MenuScriptInTheSourceTree()).ReplaceLineEndings("\n"),
            MenuWriter.MenuScript().ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Sizes_the_window_before_the_body_exists()
    {
        string page = MenuWriter.MenuPage(Facts());

        int sizes = page.IndexOf("window.resizeTo", StringComparison.Ordinal);
        int body = page.IndexOf("<body", StringComparison.Ordinal);

        // From body onload instead, mshta shows its own default window first:
        // measured on a real CD-RW at 1440x753 for 2.6 seconds before the menu
        // appeared at all.
        Assert.True(sizes > 0 && sizes < body, "The window must be sized in the head, before the body.");
    }

    [Fact]
    public void Keeps_one_disc_menu_at_a_time_and_lets_previews_pile_up()
    {
        Assert.Contains("singleinstance=\"yes\"", MenuWriter.MenuPage(Facts(onlyOne: true)), StringComparison.Ordinal);

        // Every preview of every disc shared one application name with this on,
        // so previewing a second disc silently refocused the first one's stale
        // window and the new menu was never shown at all.
        Assert.Contains("singleinstance=\"no\"", MenuWriter.MenuPage(Facts(onlyOne: false)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Rock & Roll")]
    [InlineData("STAR WARS™: Empire at War")]
    [InlineData("A name with \"quotes\" in it")]
    [InlineData("A name with a \\ backslash")]
    [InlineData("100% complete (v2)")]
    [InlineData("Данные игры")]
    public void Survives_a_name_that_has_broken_something_before(string name)
    {
        string page = MenuWriter.MenuPage(Facts(name: name));

        Assert.DoesNotMatch(MenuWriter.PlaceholderPattern, page);

        // Nothing may close the script block early. Counted as "</script"
        // WITHOUT the closing bracket, on purpose: counting the whole tag once
        // let an unescaped "<" through, because the still-escaped ">" meant the
        // complete tag never appeared and the count stayed at two. A parser is
        // less fussy than that, and so is this now.
        Assert.Equal(2, Regex.Count(page, "</script", RegexOptions.IgnoreCase));
    }

    [Fact]
    public void Does_not_let_a_name_close_the_script_block()
    {
        string page = MenuWriter.MenuPage(Facts(name: "Nasty </script><h1>take over"));

        // Two script blocks in the template, and a name cannot make a third end.
        Assert.Equal(2, Regex.Count(page, "</script", RegexOptions.IgnoreCase));

        // And the generated block carries no raw angle bracket at all, which is
        // the assertion that does not depend on which of the two brackets
        // happens to be escaped. Asserted on the block rather than the whole
        // page, because the page is markup and is full of honest brackets.
        Match data = Regex.Match(page, @"var DISC = (\{.*?\n\});", RegexOptions.Singleline);
        Assert.True(data.Success, "No data block found in the generated menu.");
        Assert.DoesNotContain('<', data.Groups[1].Value);
        Assert.DoesNotContain('>', data.Groups[1].Value);
    }

    [Fact]
    public void Does_not_let_a_control_character_break_the_script()
    {
        // The window cannot produce one. A project file read off disk can, and
        // that is the path that reaches a build. A newline inside a literal is
        // an unterminated string, and the whole HTA fails to parse.
        string page = MenuWriter.MenuPage(Facts(name: "Two\r\nLines"));

        Assert.Contains("TwoLines", page, StringComparison.Ordinal);
    }

    private static MenuFacts Facts(string name = "Hollow Knight", bool onlyOne = true) =>
        new(name, "DiscWright." + name, 1, 1, "Install", ["Install", "Exit"], onlyOne);

    internal static string MenuScriptInTheSourceTree()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "DiscWright.slnx"))
               && !File.Exists(Path.Combine(directory.FullName, "DiscWright.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException("no solution above " + AppContext.BaseDirectory),
            "src", "DiscWright.Menu", "menu.js");
    }
}
