using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Any colour at all, from Windows' own colour dialog. xniwo, discwright#98,
/// 23 November 2026: "a custom colour option, which you click on for options
/// (eg a colour wheel), which changes the value of the hash".
/// </summary>
/// <remarks>
/// The dialog itself is Windows' and modal, so no test here opens it; it was
/// driven for real when this was written, and the pass by hand has a case for
/// each of the two. What is held here is the colour going in and coming back
/// out, and that each button is where it belongs and fills its own box.
/// </remarks>
public class AnyColourAtAll
{
    [Theory]
    [InlineData(0x000000, "#000000")]
    [InlineData(0xFFFFFF, "#ffffff")]
    [InlineData(0x0000FF, "#ff0000")]
    [InlineData(0x00FF00, "#00ff00")]
    [InlineData(0xFF0000, "#0000ff")]
    [InlineData(0x6BC4FF, "#ffc46b")]
    public void A_colour_Windows_hands_back_is_red_then_green_then_blue(int windows, string kept) =>
        Assert.Equal(kept, SolidColour.FromWindows(windows));

    [Fact]
    public void Whatever_Windows_puts_in_the_top_byte_is_not_a_colour()
    {
        // The byte above blue means nothing to a colour and is not to be
        // read as part of one.
        Assert.Equal("#ffc46b", SolidColour.FromWindows(unchecked((int)0x7F6BC4FF)));
        Assert.Equal("#ffffff", SolidColour.FromWindows(-1));
    }

    [Theory]
    [InlineData("#ffc46b", 0x6BC4FF)]
    [InlineData("FFC46B", 0x6BC4FF)]
    [InlineData(" #ff0000 ", 0x0000FF)]
    [InlineData("#0000ff", 0xFF0000)]
    [InlineData("#ffffff", 0xFFFFFF)]
    public void The_dialog_starts_at_the_colour_in_the_box(string box, int windows) =>
        Assert.Equal(windows, SolidColour.ToWindows(box));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FFC4")]
    [InlineData("mauve")]
    public void It_starts_at_black_when_the_box_holds_no_colour(string? box) =>
        Assert.Equal(0, SolidColour.ToWindows(box));

    [Fact]
    public void Every_colour_offered_goes_there_and_back_unchanged()
    {
        foreach (string each in new[] { "#ffffff", "#ffe9b0", "#ffc46b", "#9fe3e8", "#c8f0b4", "#f6b6c8", "#123456", "#a1b2c3" })
        {
            Assert.Equal(each, SolidColour.FromWindows(SolidColour.ToWindows(each)));
        }
    }

    [Theory]
    [InlineData("AnyBackgroundColour", "PickAnyBackgroundColour", "BackgroundColourText", "TheColoursOffered")]
    [InlineData("AnyInk", "PickAnyInk", "MenuInkText", "TheInksOffered")]
    public void Each_row_of_swatches_ends_with_one_that_opens_the_dialog_for_its_own_box(
        string id, string click, string box, string swatches)
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement dialog = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml")).Root!;

        string? Id(System.Xml.Linq.XElement each) => (string?)each.Attribute("AutomationProperties.AutomationId");

        System.Xml.Linq.XElement any = Assert.Single(dialog.Descendants(xaml + "Button"), each => Id(each) == id);

        Assert.Equal(click, (string?)any.Attribute("Click"));
        Assert.Equal("Any colour", (string?)any.Attribute("AutomationProperties.Name"));

        // Between the row of swatches and the box it fills, in one line.
        System.Xml.Linq.XElement[] line = [.. any.Parent!.Elements()];
        int at = Array.IndexOf(line, any);

        Assert.Equal(swatches, Id(line[at - 1]));
        Assert.Equal($"{{Binding {box}, UpdateSourceTrigger=PropertyChanged}}", (string?)line[at + 1].Attribute("Text"));

        // And what it does: asks Windows, starting at that box, and fills
        // that box only when something was chosen.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml.cs"));
        int starts = code.IndexOf($"private void {click}(", StringComparison.Ordinal);
        int ends = code.IndexOf("\n    }", starts, StringComparison.Ordinal);
        string handler = code[starts..ends];

        Assert.Contains($"WindowsColourDialog.Ask(", handler, StringComparison.Ordinal);
        Assert.Contains($".Handle, _screen.{box})", handler, StringComparison.Ordinal);
        Assert.Contains("if (picked is not null)", handler, StringComparison.Ordinal);
        Assert.Contains($"_screen.{box} = picked;", handler, StringComparison.Ordinal);
    }
}
