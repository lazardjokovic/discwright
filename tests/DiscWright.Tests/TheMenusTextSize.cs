using System.Text.RegularExpressions;

using DiscWright.Core;
using DiscWright.Menu;

namespace DiscWright.Tests;

/// <summary>
/// How large the menu's heading and buttons are written. xniwo,
/// discwright#98, 23 November 2026: some fonts of his own came out "quite
/// small", and he asked to adjust the size "slightly", "up to the limit of
/// its confined space".
/// </summary>
/// <remarks>
/// Four steps and no free number, so nothing a project file says can reach
/// the page's style as anything but one of the sizes this program chose.
/// </remarks>
public class TheMenusTextSize
{
    [Fact]
    public void Four_steps_and_the_menu_as_it_always_was_is_the_one_every_disc_has()
    {
        Assert.Equal(["Smaller", "As it is", "Larger", "Largest"], MenuTextSizes.All.Select(each => each.InWords));
        Assert.Equal(MenuTextSize.AsItIs, default(MenuTextSize));
        Assert.Equal(MenuTextSize.AsItIs, new MenuLook().TextSize);

        AMenuTextSize asItWas = MenuTextSizes.For(MenuTextSize.AsItIs);

        Assert.Equal(11, asItWas.ButtonPixels);
        Assert.Equal(15, asItWas.HeadingPixels);
        Assert.Equal(20, asItWas.HeadingLinePixels);
        Assert.Equal("As it is", asItWas.ToString());
        Assert.Equal(Enum.GetValues<MenuTextSize>().Length, MenuTextSizes.All.Count);
    }

    [Fact]
    public void Each_step_is_larger_than_the_one_before_and_the_largest_still_fits_a_button()
    {
        for (int at = 1; at < MenuTextSizes.All.Count; at++)
        {
            Assert.True(MenuTextSizes.All[at].ButtonPixels > MenuTextSizes.All[at - 1].ButtonPixels);
            Assert.True(MenuTextSizes.All[at].HeadingPixels > MenuTextSizes.All[at - 1].HeadingPixels);
        }

        // A button is 38 high: its words with room above and below.
        Assert.True(MenuTextSizes.All[^1].ButtonPixels <= 14);

        // A heading's line always has room for its letters, so two lines of
        // it never write over each other, and never less than it had.
        foreach (AMenuTextSize each in MenuTextSizes.All)
        {
            Assert.True(each.HeadingLinePixels >= each.HeadingPixels + 5);
            Assert.True(each.HeadingLinePixels >= 20);
        }
    }

    [Theory]
    [InlineData("Larger", MenuTextSize.Larger)]
    [InlineData("largest", MenuTextSize.Largest)]
    [InlineData("As it is", MenuTextSize.AsItIs)]
    [InlineData("AsItIs", MenuTextSize.AsItIs)]
    [InlineData(" smaller ", MenuTextSize.Smaller)]
    [InlineData("huge", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("14px", null)]
    public void A_word_names_a_step_or_none(string? word, MenuTextSize? named) =>
        Assert.Equal(named, MenuTextSizes.Named(word));

    [Fact]
    public void A_menu_as_it_always_was_is_written_at_the_sizes_it_always_had()
    {
        string page = MenuWriter.MenuPage(AMenu(null));

        Assert.Contains("font-size: 15px; font-weight: bold; letter-spacing: 1.5px;", page, StringComparison.Ordinal);
        Assert.Contains("text-transform: uppercase; line-height: 20px; }", page, StringComparison.Ordinal);
        Assert.Contains("padding-left: 14px; font-size: 11px; font-weight: bold;", page, StringComparison.Ordinal);
        Assert.Contains("buttonSize: 11", page, StringComparison.Ordinal);
        Assert.DoesNotContain("%%", page, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MenuTextSize.Smaller, "13px", "20px", "10px", "10")]
    [InlineData(MenuTextSize.Larger, "17px", "22px", "13px", "13")]
    [InlineData(MenuTextSize.Largest, "19px", "24px", "14px", "14")]
    public void A_size_chosen_reaches_the_heading_the_buttons_and_the_squeeze(
        MenuTextSize which, string heading, string line, string button, string squeezedFrom)
    {
        string page = MenuWriter.MenuPage(AMenu(new MenuLook(TextSize: which)));

        Assert.Contains($"font-size: {heading}; font-weight: bold; letter-spacing: 1.5px;", page, StringComparison.Ordinal);
        Assert.Contains($"text-transform: uppercase; line-height: {line}; }}", page, StringComparison.Ordinal);
        Assert.Contains($"padding-left: 14px; font-size: {button}; font-weight: bold;", page, StringComparison.Ordinal);
        Assert.Contains($"buttonSize: {squeezedFrom}", page, StringComparison.Ordinal);
    }

    [Fact]
    public void It_is_saved_with_the_project_and_comes_back()
    {
        string saved = Saved(new MenuLook(TextSize: MenuTextSize.Largest));

        Assert.Contains("\"TextSize\": \"Largest\"", saved, StringComparison.Ordinal);
        Assert.Equal(
            MenuTextSize.Largest,
            Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value.Options.LookOrPlain.TextSize);
    }

    [Fact]
    public void A_file_from_before_there_was_a_choice_or_with_a_word_that_names_none_is_the_size_as_it_was()
    {
        string saved = Saved(new MenuLook(ListOn: MenuSide.Left, TextSize: MenuTextSize.Larger));

        foreach (string instead in new[] { string.Empty, "\"TextSize\": \"enormous\",", "\"TextSize\": 40," })
        {
            // The key as written, and the comma that separates it from the
            // one before, taken out or swapped: the file stays readable.
            string doctored = Regex.Replace(
                saved,
                ",(\\s*)\"TextSize\": \"Larger\"",
                match => instead.Length == 0 ? string.Empty : "," + match.Groups[1].Value + instead.TrimEnd(','));

            Assert.NotEqual(saved, doctored);

            MenuLook back = Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(doctored)).Value.Options.LookOrPlain;

            Assert.Equal(MenuTextSize.AsItIs, back.TextSize);
            Assert.Equal(MenuSide.Left, back.ListOn);
        }
    }

    [Fact]
    public void The_dialog_has_a_list_for_it_and_the_sample_follows()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement dialog = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml")).Root!;

        string? Id(System.Xml.Linq.XElement each) => (string?)each.Attribute("AutomationProperties.AutomationId");

        System.Xml.Linq.XElement list = Assert.Single(dialog.Descendants(xaml + "ComboBox"), each => Id(each) == "MenuTextSize");

        Assert.Equal("{Binding TheMenusTextSizes}", (string?)list.Attribute("ItemsSource"));
        Assert.Equal("{Binding TheMenusTextSize}", (string?)list.Attribute("SelectedItem"));
        Assert.Equal("InWords", (string?)list.Attribute("DisplayMemberPath"));

        Assert.Equal(
            "{Binding TheSampleHeadingSize}",
            (string?)Assert.Single(dialog.Descendants(xaml + "TextBlock"), each => Id(each) == "TheSampleHeading").Attribute("FontSize"));
        Assert.Equal(
            "{Binding TheSampleButtonSize}",
            (string?)Assert.Single(dialog.Descendants(xaml + "TextBlock"), each => Id(each) == "TheSampleButton").Attribute("FontSize"));
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
            "2.0.6",
            "DVD5",
            @"C:\out",
            DateTimeOffset.UtcNow);
    }
}
