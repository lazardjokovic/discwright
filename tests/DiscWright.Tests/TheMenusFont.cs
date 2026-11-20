using DiscWright.Core;
using DiscWright.Menu;

namespace DiscWright.Tests;

/// <summary>
/// The face the menu is written in, discwright#98: "more menu themes
/// (layout / font / font colour)".
/// </summary>
/// <remarks>
/// The menu is read off the disc on whatever machine the disc is put in and
/// cannot bring a font with it, so the list is short and every face on it
/// came with Windows. What matters most here is that nothing a project file
/// says can reach the page's style as anything but one of the six lines this
/// program wrote.
/// </remarks>
public class TheMenusFont
{
    private static readonly string[] TheKindsOfFace = ["sans-serif", "serif", "monospace"];

    [Fact]
    public void The_list_offers_six_with_the_one_every_disc_has_worn_first()
    {
        Assert.Equal(
            ["Tahoma", "Verdana", "Trebuchet MS", "Georgia", "Times New Roman", "Courier New"],
            MenuFonts.All.Select(each => each.InWords));
        Assert.Equal(MenuFont.Tahoma, MenuFonts.All[0].Which);
        Assert.Equal(MenuFont.Tahoma, new MenuLook().Font);
        Assert.Equal(MenuFont.Tahoma, default(MenuFont));
    }

    [Fact]
    public void Every_face_there_is_has_a_row_drawn_in_itself()
    {
        foreach (MenuFont which in Enum.GetValues<MenuFont>())
        {
            AMenuFont row = MenuFonts.For(which);

            Assert.Equal(which, row.Which);
            Assert.Equal(row.InWords, row.Family);
            Assert.Equal(row.InWords, row.ToString());
        }

        Assert.Equal(Enum.GetValues<MenuFont>().Length, MenuFonts.All.Count);
    }

    [Fact]
    public void Each_asks_for_itself_first_and_ends_on_the_kind_of_face_it_is()
    {
        // A machine that has lost the face still draws one of the same
        // sort, never whatever the browser falls back to.
        foreach (AMenuFont each in MenuFonts.All)
        {
            string asked = each.AsThePageAsksForIt;
            string first = asked.Split(',')[0].Trim().Trim('"');
            string last = asked.Split(',')[^1].Trim();

            Assert.Equal(each.InWords, first);
            Assert.Contains(last, TheKindsOfFace);

            // A name of two words is in quotes, or a style reads it as two.
            if (each.InWords.Contains(' ', StringComparison.Ordinal))
            {
                Assert.StartsWith("\"" + each.InWords + "\"", asked, StringComparison.Ordinal);
            }

            // Nothing that could end the rule it is written into.
            Assert.DoesNotContain(';', asked);
            Assert.DoesNotContain('}', asked);
            Assert.DoesNotContain('<', asked);
        }
    }

    [Theory]
    [InlineData("Georgia", MenuFont.Georgia)]
    [InlineData("georgia", MenuFont.Georgia)]
    [InlineData("  Trebuchet MS ", MenuFont.TrebuchetMs)]
    [InlineData("TrebuchetMs", MenuFont.TrebuchetMs)]
    [InlineData("TIMES NEW ROMAN", MenuFont.TimesNewRoman)]
    [InlineData("Courier New", MenuFont.CourierNew)]
    public void A_face_is_named_by_its_own_name_whatever_its_capitals(string word, MenuFont which) =>
        Assert.Equal(which, MenuFonts.Named(word));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Comic Sans MS")]
    [InlineData("3")]
    [InlineData("Georgia; } body { display: none")]
    [InlineData("Georgia, serif")]
    public void A_word_that_names_none_of_the_six_names_nothing(string? word) =>
        Assert.Null(MenuFonts.Named(word));

    // ---------------------------------------------------------------- the page

    [Fact]
    public void A_menu_with_no_look_is_written_in_Tahoma_as_every_disc_was()
    {
        string page = MenuWriter.MenuPage(AMenu(null));

        Assert.Contains("font-family: Tahoma, sans-serif;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("%%FONT%%", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MenuFont.Tahoma, "font-family: Tahoma, sans-serif;")]
    [InlineData(MenuFont.Verdana, "font-family: Verdana, Tahoma, sans-serif;")]
    [InlineData(MenuFont.TrebuchetMs, "font-family: \"Trebuchet MS\", Tahoma, sans-serif;")]
    [InlineData(MenuFont.Georgia, "font-family: Georgia, \"Times New Roman\", serif;")]
    [InlineData(MenuFont.TimesNewRoman, "font-family: \"Times New Roman\", serif;")]
    [InlineData(MenuFont.CourierNew, "font-family: \"Courier New\", monospace;")]
    public void The_page_asks_for_the_face_chosen_in_the_rule_everything_inherits(MenuFont which, string rule)
    {
        string page = MenuWriter.MenuPage(AMenu(new MenuLook(Font: which)));

        // Once, and in the body's rule, which is where every word on the
        // page gets its face from.
        int at = page.IndexOf(rule, StringComparison.Ordinal);

        Assert.True(at > 0, $"The page does not say: {rule}");
        Assert.Equal(at, page.LastIndexOf(rule, StringComparison.Ordinal));

        int body = page.LastIndexOf("body {", at, StringComparison.Ordinal);

        Assert.True(body > 0 && !page[body..at].Contains('}', StringComparison.Ordinal), "The rule is not the body's.");
    }

    [Fact]
    public void The_small_buttons_keep_the_face_their_pictures_come_from()
    {
        // They are letters of Wingdings, and in Georgia they would be an
        // ampersand and a 3.
        string page = MenuWriter.MenuPage(AMenu(new MenuLook(Font: MenuFont.Georgia)));

        Assert.Contains(".corner.wing { font-family: Wingdings;", page, StringComparison.Ordinal);
    }

    // ------------------------------------------------------- the project file

    [Theory]
    [InlineData(MenuFont.Tahoma, "\"Font\": \"Tahoma\"")]
    [InlineData(MenuFont.TrebuchetMs, "\"Font\": \"Trebuchet MS\"")]
    [InlineData(MenuFont.CourierNew, "\"Font\": \"Courier New\"")]
    public void A_saved_project_names_the_face_and_comes_back_with_it(MenuFont which, string written)
    {
        string saved = Saved(new MenuLook(Font: which));

        Assert.Contains(written, saved, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value;

        Assert.Equal(which, back.Options.LookOrPlain.Font);
    }

    [Fact]
    public void A_face_alone_is_a_look_worth_keeping()
    {
        // The look is written as nothing when it is plain, and a face is
        // not plain.
        Assert.Null(new MenuLook(Font: MenuFont.Tahoma).OrNothingWhenPlain());
        Assert.NotNull(new MenuLook(Font: MenuFont.Georgia).OrNothingWhenPlain());
    }

    [Theory]
    [InlineData("\"Font\": \"Comic Sans MS\"")]
    [InlineData("\"Font\": \"Georgia; } body { display: none\"")]
    [InlineData("\"Font\": 3")]
    [InlineData("\"Font\": null")]
    public void A_file_that_asks_for_anything_else_gets_Tahoma_and_nothing_of_it_reaches_the_page(string doctored)
    {
        string saved = Saved(new MenuLook(Font: MenuFont.Georgia, ListOn: MenuSide.Left))
            .Replace("\"Font\": \"Georgia\"", doctored, StringComparison.Ordinal);

        Assert.Contains(doctored, saved, StringComparison.Ordinal);

        OpenedProject back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value;

        Assert.Equal(MenuFont.Tahoma, back.Options.LookOrPlain.Font);

        // The rest of the look in the same file is still read.
        Assert.Equal(MenuSide.Left, back.Options.LookOrPlain.ListOn);

        string page = MenuWriter.MenuPage(AMenu(back.Options.Look));

        Assert.Contains("font-family: Tahoma, sans-serif;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("body { display", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Comic Sans", page, StringComparison.Ordinal);

        // And the page is, to the letter, the page of a disc that asked for
        // nothing: whatever the file said has left no mark on it at all.
        Assert.Equal(MenuWriter.MenuPage(AMenu(new MenuLook(ListOn: MenuSide.Left))), page);
    }

    [Fact]
    public void A_file_from_before_there_was_a_choice_is_Tahoma()
    {
        string saved = Saved(new MenuLook(ListOn: MenuSide.Left))
            .Replace("\"Font\": \"Tahoma\"", "\"SomethingElse\": 1", StringComparison.Ordinal);

        Assert.DoesNotContain("\"Font\"", saved, StringComparison.Ordinal);
        Assert.Equal(
            MenuFont.Tahoma,
            Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value.Options.LookOrPlain.Font);
    }

    // -------------------------------------------------------------- the dialog

    [Fact]
    public void The_dialog_has_a_list_of_faces_with_each_row_drawn_in_its_own()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement list = Assert.Single(
            System.Xml.Linq.XDocument.Load(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml"))
                .Root!.Descendants(xaml + "ComboBox"),
            each => (string?)each.Attribute("AutomationProperties.AutomationId") == "MenuFont");

        Assert.Equal("{Binding TheMenusFonts}", (string?)list.Attribute("ItemsSource"));
        Assert.Equal("{Binding TheMenusFont}", (string?)list.Attribute("SelectedItem"));

        System.Xml.Linq.XElement row = Assert.Single(list.Descendants(xaml + "TextBlock"));

        Assert.Equal("{Binding InWords}", (string?)row.Attribute("Text"));
        Assert.Equal("{Binding Family}", (string?)row.Attribute("FontFamily"));

        // And a reader says the face's name, not what the row prints as.
        Assert.Contains(
            list.Descendants(xaml + "Setter"),
            each => (string?)each.Attribute("Property") == "AutomationProperties.Name"
                && (string?)each.Attribute("Value") == "{Binding InWords}");
    }

    // ------------------------------------------------------------ the ink
    //
    // discwright#98, the first post: "more menu themes (layout / font /
    // font colour)". What the heading and the buttons are written in.

    [Fact]
    public void A_menu_nobody_coloured_is_written_in_its_own_two_shades()
    {
        string page = MenuWriter.MenuPage(AMenu(null));

        Assert.Contains("    color: #DFE9EE;", page, StringComparison.Ordinal);
        Assert.Contains("text-transform: uppercase; color: #CBD6DB;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("%%INK%%", page, StringComparison.Ordinal);
        Assert.DoesNotContain("%%BUTTONINK%%", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#FFE9B0")]
    [InlineData("#ffe9b0")]
    [InlineData("ffe9b0")]
    public void A_colour_chosen_is_what_the_heading_and_the_buttons_are_written_in(string ink)
    {
        string page = MenuWriter.MenuPage(AMenu(new MenuLook(Ink: ink)));
        string tidy = SolidColour.Tidied(ink)!;

        Assert.Contains($"    color: {tidy};", page, StringComparison.Ordinal);
        Assert.Contains($"text-transform: uppercase; color: {tidy};", page, StringComparison.Ordinal);

        // The quiet lines and a dead button keep their greys, which mean
        // something: a hint, and a button that will not press.
        Assert.Contains(".btn.dead { color: #5E727A;", page, StringComparison.Ordinal);
        Assert.Contains(".hint {", page, StringComparison.Ordinal);
        Assert.Contains("color: #5E727A; }", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#FFE9B0; } body { display: none")]
    [InlineData("")]
    public void What_is_not_a_colour_never_reaches_the_page(string ink)
    {
        // It is written into the page's style, so only ever six hex figures.
        string page = MenuWriter.MenuPage(AMenu(new MenuLook(Ink: ink)));

        Assert.Equal(MenuWriter.MenuPage(AMenu(new MenuLook())), page);
    }

    [Fact]
    public void A_saved_project_names_the_ink_and_comes_back_with_it()
    {
        string saved = Saved(new MenuLook(Ink: "#ffe9b0"));

        Assert.Contains("\"Ink\": \"#ffe9b0\"", saved, StringComparison.Ordinal);
        Assert.Equal(
            "#ffe9b0",
            Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value.Options.LookOrPlain.Ink);
        Assert.NotNull(new MenuLook(Ink: "#ffe9b0").OrNothingWhenPlain());
    }

    [Theory]
    [InlineData("\"Ink\": \"mauve\"")]
    [InlineData("\"Ink\": \"#FFE9B0; } body { display: none\"")]
    [InlineData("\"Ink\": 7")]
    public void A_file_that_names_no_colour_is_the_menus_own_and_the_rest_of_its_look_is_still_read(string doctored)
    {
        string saved = Saved(new MenuLook(Ink: "#ffe9b0", ListOn: MenuSide.Left))
            .Replace("\"Ink\": \"#ffe9b0\"", doctored, StringComparison.Ordinal);

        Assert.Contains(doctored, saved, StringComparison.Ordinal);

        MenuLook back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value.Options.LookOrPlain;

        Assert.Null(back.Ink);
        Assert.Equal(MenuSide.Left, back.ListOn);
    }

    [Fact]
    public void A_file_from_before_there_was_a_choice_has_no_ink_of_its_own()
    {
        string saved = Saved(new MenuLook(ListOn: MenuSide.Left));

        Assert.Contains("\"Ink\": null", saved, StringComparison.Ordinal);
        Assert.Null(Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value.Options.LookOrPlain.Ink);
    }

    [Fact]
    public void The_dialog_has_swatches_and_a_box_for_it_and_a_line_for_text_that_is_no_colour()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement dialog = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml")).Root!;

        string? Id(System.Xml.Linq.XElement each) => (string?)each.Attribute("AutomationProperties.AutomationId");

        System.Xml.Linq.XElement box = Assert.Single(dialog.Descendants(xaml + "TextBox"), each => Id(each) == "MenuInkText");

        Assert.Equal("{Binding MenuInkText, UpdateSourceTrigger=PropertyChanged}", (string?)box.Attribute("Text"));
        Assert.False(string.IsNullOrWhiteSpace((string?)box.Attribute("AutomationProperties.Name")));

        System.Xml.Linq.XElement swatches = Assert.Single(dialog.Descendants(xaml + "ItemsControl"), each => Id(each) == "TheInksOffered");

        Assert.Equal("{Binding TheInksOffered}", (string?)swatches.Attribute("ItemsSource"));
        Assert.Equal("PickAnInk", (string?)Assert.Single(swatches.Descendants(xaml + "Button")).Attribute("Click"));
        Assert.Equal(
            "{Binding WhatTheInkSays}",
            (string?)Assert.Single(dialog.Descendants(xaml + "TextBlock"), each => Id(each) == "WhatTheInkSays").Attribute("Text"));
    }

    // ---------------------------------------------------------------- helpers

    private static MenuFacts AMenu(MenuLook? look) =>
        new("Hollow Knight", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true, Look: look);

    private static string Saved(MenuLook look)
    {
        ListedFile only = new("setup_hollow_knight.exe", 1000);

        return TheProjectAsJson.Of(
            new DiscProject(
                "HOLLOW KNIGHT",
                [new GameFolder(@"C:\games\Hollow Knight", GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", only, [only], [], null)],
                new DiscOptions(Look: look),
                Artwork: null,
                Extras: null,
                AddOns: []),
            "2.0.5",
            "DVD5",
            @"C:\out",
            DateTimeOffset.UtcNow);
    }
}
