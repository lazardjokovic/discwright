using System.Globalization;
using System.Xml.Linq;

using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The window on a screen smaller than the one it was drawn for, which the
/// owner met on an older Windows 10 laptop: the window only just fitted, with
/// no way to make it smaller.
/// </summary>
public class FittingASmallScreen
{
    private static readonly (double Width, double Height) Drawn = (920, 740);
    private static readonly (double Width, double Height) Smallest = (920, 560);

    [Fact]
    public void Opens_at_its_drawn_size_where_there_is_room_and_centred()
    {
        // 1920 by 1080 at 100%, the taskbar along the bottom.
        FittingTheScreen.Fitted fitted = FittingTheScreen.In(Drawn, Smallest, new(0, 0, 1920, 1032));

        Assert.Equal(new FittingTheScreen.Fitted(500, 146, 920, 740, 920, 560), fitted);
    }

    [Fact]
    public void Opens_as_tall_as_an_older_laptop_has_room_for()
    {
        // 1366 by 768 at 100%, the taskbar taking 40: 728 of room for a
        // window drawn 740 tall. It used to open cut off at the bottom.
        FittingTheScreen.Fitted fitted = FittingTheScreen.In(Drawn, Smallest, new(0, 0, 1366, 728));

        Assert.Equal(728, fitted.Height);
        Assert.Equal(0, fitted.Top);
        Assert.Equal(920, fitted.Width);
        Assert.Equal(223, fitted.Left);
    }

    [Fact]
    public void Opens_as_wide_as_a_scaled_screen_has_room_for()
    {
        // 1280 by 720 of room is a 1920 by 1080 screen at 150%, which is
        // what Windows picks for a lot of small laptop screens, with the
        // taskbar's 48 taken off.
        FittingTheScreen.Fitted fitted = FittingTheScreen.In(Drawn, Smallest, new(0, 0, 1280, 672));

        Assert.Equal((920, 672), (fitted.Width, fitted.Height));
        Assert.Equal((180, 0), (fitted.Left, fitted.Top));
    }

    [Fact]
    public void Lets_the_screen_win_when_even_the_smallest_size_does_not_fit()
    {
        // A screen smaller than the layout's smallest: cramped beats cut off,
        // and the minimum comes down with it, or Windows would hold the
        // window at a size there is no room for.
        FittingTheScreen.Fitted fitted = FittingTheScreen.In(Drawn, Smallest, new(0, 0, 1024, 520));

        Assert.Equal(new FittingTheScreen.Fitted(52, 0, 920, 520, 920, 520), fitted);

        FittingTheScreen.Fitted narrow = FittingTheScreen.In(Drawn, Smallest, new(0, 0, 640, 900));

        Assert.Equal((640, 640), (narrow.Width, narrow.MinWidth));
        Assert.Equal(0, narrow.Left);
    }

    [Fact]
    public void Centres_in_the_room_and_not_on_the_screen()
    {
        // A taskbar on the left or along the top moves where the room starts,
        // and a window centred on the whole screen would sit under it.
        FittingTheScreen.Fitted fitted = FittingTheScreen.In(Drawn, Smallest, new(62, 40, 1304, 728));

        Assert.Equal((62 + 192, 40), (fitted.Left, fitted.Top));
    }

    [Fact]
    public void The_window_can_be_made_shorter_and_is_held_to_the_width_its_list_was_drawn_for()
    {
        XElement window = XDocument.Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml")).Root!;

        Assert.Equal("CanResize", (string?)window.Attribute("ResizeMode"));
        Assert.Equal(Drawn.Width.ToString(CultureInfo.InvariantCulture), (string?)window.Attribute("Width"));
        Assert.Equal(Drawn.Height.ToString(CultureInfo.InvariantCulture), (string?)window.Attribute("Height"));
        Assert.Equal(Smallest.Width.ToString(CultureInfo.InvariantCulture), (string?)window.Attribute("MinWidth"));
        Assert.Equal(Smallest.Height.ToString(CultureInfo.InvariantCulture), (string?)window.Attribute("MinHeight"));

        // The list lines its sizes up at a fixed width, so the window does
        // not get wider either, and has no maximise button to make it so.
        Assert.Equal(Drawn.Width.ToString(CultureInfo.InvariantCulture), (string?)window.Attribute("MaxWidth"));
        string main = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        Assert.Contains("TakingAwayMaximise.From(", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_windows_are_fitted_before_they_are_shown()
    {
        // In the constructor, after the markup has given the sizes, so the
        // window never appears at one size and jumps to another.
        string main = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        string artwork = File.ReadAllText(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml.cs"));

        Assert.True(
            main.IndexOf("OnThisScreen.Fit(this);", StringComparison.Ordinal)
                > main.IndexOf("InitializeComponent();", StringComparison.Ordinal),
            "MainWindow has to be fitted after InitializeComponent has read its sizes.");
        Assert.Contains("InitializeComponent();", main, StringComparison.Ordinal);

        Assert.True(
            artwork.IndexOf("OnThisScreen.NoTallerThanTheScreen(this);", StringComparison.Ordinal)
                > artwork.IndexOf("InitializeComponent();", StringComparison.Ordinal),
            "The artwork window has to be fitted after InitializeComponent has read its size.");
        Assert.Contains("OnThisScreen.NoTallerThanTheScreen(this);", artwork, StringComparison.Ordinal);
    }
}
