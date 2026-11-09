using System.Globalization;
using System.Xml.Linq;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The window kept drivable, guarded rather than remembered.
/// </summary>
/// <remarks>
/// <para>
/// These read <c>MainWindow.xaml</c> as XML from the source tree rather than
/// loading the built window. Nothing may reference the window project,
/// including this, which is reason enough on its own. Until 2026-10-16 there
/// was a second reason: Smart App Control on the machine this is written on
/// refused to load our own unsigned assemblies, so a test that instantiated a
/// WPF window passed on the test VM and failed here.
/// </para>
/// <para>
/// <strong>This is the test that stops v1 happening again.</strong> v1's window
/// suite ended up unable to see parts of its own window, and nothing failed
/// when that happened: somebody added a control, UI Automation could not reach
/// it, and the only sign was a test that had to be written a different way.
/// Here, a control a test cannot reach fails the build.
/// </para>
/// </remarks>
public class TheWindowsMarkup
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>
    /// The controls somebody uses, as opposed to the ones that only arrange
    /// things. A Grid has no business carrying an id; a Button does.
    /// </summary>
    private static readonly string[] Interactive =
    [
        "Button", "TextBox", "CheckBox", "ComboBox", "ListBox", "TreeView",
        "RadioButton", "Expander", "Slider", "PasswordBox"
    ];

    [Theory]
    [MemberData(nameof(EveryWindow))]
    public void Every_control_somebody_uses_can_be_found_by_a_test(string window)
    {
        string[] unreachable =
        [
            .. Markup(window)
                .Descendants()
                .Where(element => Interactive.Contains(element.Name.LocalName))
                .Where(element => !CanBeFound(element))
                .Select(Describe)
        ];

        Assert.True(
            unreachable.Length == 0,
            "Every control somebody uses needs an AutomationId, or an AutomationProperties.Name when it "
                + "lives in a row template and there is one of it per row, or no window test can reach it. "
                + $"Missing in {window} on: " + string.Join(", ", unreachable));
    }

    [Theory]
    [MemberData(nameof(EveryWindow))]
    public void No_two_controls_answer_to_the_same_name(string window)
    {
        string[] twice =
        [
            .. Markup(window)
                .Descendants()
                .Select(IdOf)
                .Where(id => id is not null)
                .GroupBy(id => id!, StringComparer.Ordinal)
                .Where(same => same.Count() > 1)
                .Select(same => same.Key)
        ];

        // Two controls with one id is worse than a missing id, because a test
        // finds the first one, passes, and says nothing about the other.
        Assert.True(twice.Length == 0, $"Used more than once in {window}: " + string.Join(", ", twice));
    }

    [Theory]
    [MemberData(nameof(EveryWindow))]
    public void Says_every_colour_by_name_rather_than_in_figures(string window)
    {
        string[] literal =
        [
            .. Markup(window)
                .Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute => ((string?)attribute.Value)?.StartsWith('#') == true)
                .Select(attribute => Describe(attribute.Parent!) + "." + attribute.Name.LocalName)
        ];

        // A colour typed into a window is a colour that will not change when
        // the palette does, and the palette is the whole design.
        Assert.True(literal.Length == 0, $"Colour written out in full in {window} on: " + string.Join(", ", literal));
    }

    [Fact]
    public void The_option_that_depends_on_somebody_elses_settings_says_so()
    {
        // A disc opens its menu by itself only where AutoRun is on, and
        // Windows has been turning that off for years. Somebody whose disc
        // does nothing when it goes in has no way of knowing the reason is on
        // their own machine rather than in the disc, unless this says so.
        XElement? menu = Window()
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName == "CheckBox"
                && (string?)element.Attribute("AutomationProperties.AutomationId") == "Menu");

        Assert.NotNull(menu);

        Assert.True(
            menu.Elements().Any(child => child.Name.LocalName.EndsWith(".ToolTip", StringComparison.Ordinal))
                || menu.Attribute("ToolTip") is not null,
            "The menu option has nothing explaining that it needs AutoRun switched on.");
    }

    [Fact]
    public void The_window_has_an_icon_of_its_own()
    {
        // Without one, the title bar, the taskbar and Alt and Tab all show
        // Windows' blank default, which is what a program looks like when
        // nobody finished it.
        Assert.Equal("DiscWright.ico", (string?)Window().Attribute("Icon"));
    }

    [Theory]
    [MemberData(nameof(EveryWindow))]
    public void No_button_decides_its_own_size(string window)
    {
        // A button wearing its own padding is a button that stops matching the
        // one next to it the moment either is touched. The accent button was
        // a size bigger than the button beside it for exactly this reason, and
        // it read as a different kind of thing rather than a louder one.
        string[] sized =
        [
            .. Markup(window)
                .Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Where(element =>
                    element.Attribute("Padding") is not null
                    || element.Attribute("Width") is not null
                    || element.Attribute("Height") is not null
                    || element.Attribute("FontSize") is not null)
                .Select(Describe)
        ];

        Assert.True(
            sized.Length == 0,
            $"These buttons in {window} size themselves instead of taking it from a style: "
                + string.Join(", ", sized));
    }

    [Theory]
    [MemberData(nameof(EveryWindow))]
    public void Every_list_of_things_says_what_each_row_is(string window)
    {
        // A row takes its name from whatever the thing in it prints as, and a
        // record prints as all of itself. DisplayMemberPath and an ItemTemplate
        // both change what is SHOWN and neither changes what is SAID, so a
        // reader announces "Medium { Key = CD, Name = CD-R 700 MB, Capacity =
        // 730144440 }" and a window test sees that instead of a name.
        //
        // Caught twice by driving the window, in two different lists. This is
        // the third time it cannot happen.
        string[] silent =
        [
            .. Markup(window)
                .Descendants()
                .Where(element => element.Name.LocalName is "ListBox" or "ComboBox" or "TreeView")
                .Where(element => element.Attribute("ItemsSource") is not null)
                .Where(element => !NamesItsRows(element))
                .Select(Describe)
        ];

        Assert.True(
            silent.Length == 0,
            $"These lists in {window} do not say what each row is, so a reader announces the whole object: "
                + string.Join(", ", silent));
    }

    /// <summary>
    /// Whether a list gives its rows a name of their own.
    /// </summary>
    private static bool NamesItsRows(XElement list) =>
        list.Descendants()
            .Where(element => element.Name.LocalName == "Setter")
            .Any(setter => (string?)setter.Attribute("Property") == "AutomationProperties.Name");

    /// <summary>
    /// Every window in the program. Found rather than listed, so a new one is
    /// guarded the day it is written rather than the day somebody remembers.
    /// </summary>
    public static TheoryData<string> EveryWindow()
    {
        TheoryData<string> windows = [];

        foreach (string file in Directory.GetFiles(
            Repository.File("src", "DiscWright.App"), "*.xaml"))
        {
            // App.xaml is the program itself and Look.xaml is the palette.
            // Neither has a control on it.
            if (XDocument.Load(file).Root?.Name.LocalName == "Window")
            {
                windows.Add(Path.GetFileName(file));
            }
        }

        return windows;
    }

    [Fact]
    public void Is_the_size_the_design_was_drawn_at()
    {
        XElement window = Window();

        // The design was drawn at exactly this size and the window does not
        // resize, so every position in it was chosen against these numbers.
        Assert.Equal("920", (string?)window.Attribute("Width"));
        Assert.Equal("740", (string?)window.Attribute("Height"));

        // And it cannot be stretched into a shape nobody drew. Every position
        // in this window is fixed, so a maximise button would be an invitation
        // to break it. Minimising is a different question and is allowed.
        Assert.Equal("CanMinimize", (string?)window.Attribute("ResizeMode"));
    }

    [Fact]
    public void Has_exactly_one_button_wearing_the_accent_colour()
    {
        int accented = Window()
            .Descendants(Xaml + "Button")
            .Count(button => (string?)button.Attribute("Style") == "{StaticResource TheButton}");

        // The accent is how somebody finds the thing they came to do. Two of
        // them is two first choices, which is none.
        Assert.Equal(1, accented);
    }

    [Fact]
    public void Shows_a_refusal_and_a_worry_in_different_ink()
    {
        XElement[] lines =
        [
            .. Window().Descendants(Xaml + "TextBlock")
                .Where(line => IdOf(line) is "WhyNot" or "Worry")
        ];

        Assert.Equal(2, lines.Length);

        // One stops the build and the other is only worth knowing, so they must
        // not read as the same kind of thing.
        Assert.Equal(2, lines.Select(line => (string?)line.Attribute("Foreground")).Distinct().Count());
    }

    [Theory]
    [InlineData("WhyNot")]
    [InlineData("Worry")]
    public void Shows_everything_a_refusal_knows_and_not_just_its_headline(string which)
    {
        // A Refusal has always carried a headline, a sentence saying why, and
        // its figures. This window drew the headline and threw the rest away:
        // "One file is bigger than a whole disc" with no name of the file, no
        // size, and nothing to compare it against. Thirty one refusals, ten of
        // them carrying figures, and not one figure reached the screen.
        XElement line = Assert.Single(Window().Descendants(Xaml + "TextBlock"), each => IdOf(each) == which);

        Assert.Equal($"{{Binding {which}.Because}}", (string?)line.Attribute("ToolTip"));

        XElement figures = Assert.Single(
            Window().Descendants(Xaml + "ItemsControl"), each => IdOf(each) == which + "Figures");

        Assert.Equal($"{{Binding {which}.Figures}}", (string?)figures.Attribute("ItemsSource"));

        // Gone entirely when there are none, because a row of nothing still
        // takes its margin at the bottom of a window drawn to the pixel.
        Assert.Equal(
            $"{{Binding {which}.Figures.Count, Converter={{StaticResource WhenSome}}}}",
            (string?)figures.Attribute("Visibility"));
    }

    [Fact]
    public void The_heading_over_the_list_has_its_row_to_itself()
    {
        // The preview button used to share this row, which made the header as
        // tall as the button and needed a bottom alignment and a negative
        // margin to keep the heading on the list it names. The button lives on
        // the toolbar now; anything moved back up here brings all of that back,
        // so the heading stays a plain row of the panel with nothing beside it.
        XElement heading = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => (string?)each.Attribute("Text") == "WHAT GOES ON IT");

        Assert.Equal("DockPanel", heading.Parent!.Name.LocalName);
        Assert.Null(heading.Attribute("VerticalAlignment"));
    }

    [Fact]
    public void Every_converter_a_window_asks_for_has_been_declared()
    {
        // A converter named in a binding and missing from App.xaml is not a
        // compile error. It is a window that throws the moment that binding is
        // evaluated, which for a refusal is the moment somebody is already
        // being told something has gone wrong.
        string[] declared =
        [
            .. XDocument.Load(Repository.File("src", "DiscWright.App", "App.xaml"))
                .Descendants()
                .Select(each => (string?)each.Attribute(Named + "Key"))
                .Where(key => key is not null)
                .Select(key => key!)
        ];

        string[] asked =
        [
            .. Directory.GetFiles(Repository.File("src", "DiscWright.App"), "*.xaml")
                .SelectMany(file => XDocument.Load(file).Descendants().SelectMany(each => each.Attributes()))
                .SelectMany(attribute => System.Text.RegularExpressions.Regex.Matches(
                    attribute.Value, @"Converter=\{StaticResource (\w+)\}"))
                .Select(found => found.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
        ];

        string[] missing = [.. asked.Where(each => !declared.Contains(each, StringComparer.Ordinal))];

        Assert.True(missing.Length == 0, "Asked for and never declared: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_button_that_makes_it_is_dead_rather_than_hidden_when_it_cannot_be_pressed()
    {
        XElement button = Assert.Single(
            Window().Descendants(Xaml + "Button"),
            each => IdOf(each) == "MakeTheDisc");

        // Bound to whether it can be made, rather than to whether it is shown.
        // A button that disappears leaves somebody with nothing to work
        // towards, and no way to ask why.
        Assert.Equal("{Binding CanMakeIt}", (string?)button.Attribute("IsEnabled"));
        Assert.Null(button.Attribute("Visibility"));
    }

    // ------------------------------------------------- what it does not say

    [Fact]
    public void The_tray_warning_lives_on_the_drive_picker_and_not_on_a_line_of_its_own()
    {
        // A line of its own was still a line, on a window where every row was
        // being counted. The warning is about a drive and the picker is where
        // a drive is chosen, so that is where it hovers.
        Assert.DoesNotContain("AboutTheTray", Window().Descendants().Select(IdOf));

        XElement picker = Window()
            .Descendants(Xaml + "ComboBox")
            .Single(each => IdOf(each) == "WhichDrive");

        Assert.Equal("{Binding WhatTheDrivesAreDoing}", (string?)picker.Attribute("ToolTip"));
    }

    [Fact]
    public void The_place_for_the_iso_is_named_for_what_lands_in_it()
    {
        // "WHERE IT GOES" read as if it were about the disc, which is the one
        // thing that folder never holds.
        Assert.Contains(
            "WHERE TO SAVE THE ISO",
            Window().Descendants(Xaml + "TextBlock").Select(each => (string?)each.Attribute("Text")));
    }

    [Fact]
    public void The_name_row_is_the_name_and_the_version_and_nothing_else()
    {
        // The link that used to sit beside the number is on its own line
        // underneath now. Whether there is a newer one is a different thought
        // from what this one is called, and the two were one row.
        XElement row = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => IdOf(each) == "ThisBuild")
            .Parent!;

        Assert.Equal(
            ["TextBlock", "TextBlock"],
            row.Elements().Select(each => each.Name.LocalName));
    }

    [Fact]
    public void The_version_line_is_under_the_name_and_is_the_way_to_ask()
    {
        // It is the only way in. There is no menu, and a check reachable only
        // from a package manager is no check at all for somebody who ran the
        // installer.
        XElement block = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => IdOf(each) == "ThisBuild")
            .Parent!
            .Parent!;

        Assert.Equal(
            ["StackPanel", "TextBlock", "Button", "Button"],
            block.Elements().Select(each => each.Name.LocalName));

        Assert.Equal(
            ["WhatTheVersionSays", "CheckForANewVersion", "Appearance"],
            block.Elements().Skip(1).Select(IdOf));

        // And it is there to be used. A link in the markup that the markup
        // then hides or disables is the exact thing this change is undoing:
        // a working, tested capability with no way in. The first round of
        // faults walked straight through the assertions above by collapsing
        // it, which is as good as deleting it and leaves no trace in a diff.
        XElement link = block.Elements().ElementAt(2);

        Assert.Null(link.Attribute("Visibility"));
        Assert.Null(link.Attribute("IsEnabled"));
    }

    [Fact]
    public void The_version_line_is_worded_by_the_screen_and_not_by_the_window()
    {
        // Four states and one control. Spelling any of them here would put
        // wording where no test can reach it.
        XElement link = Window()
            .Descendants(Xaml + "Button")
            .Single(each => IdOf(each) == "CheckForANewVersion");

        Assert.Equal("{Binding WhatTheVersionLinkSays}", (string?)link.Attribute("Content"));
    }

    [Fact]
    public void The_version_line_says_nothing_until_there_is_something_to_say()
    {
        // Left visible, an empty line still takes the height of a line, and
        // the gap between the name and the link would read as a thing that
        // had gone missing.
        XElement said = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => IdOf(each) == "WhatTheVersionSays");

        Assert.Equal(
            "{Binding WhatTheVersionSays, Converter={StaticResource WhenAnything}}",
            (string?)said.Attribute("Visibility"));
    }

    [Fact]
    public void Nothing_is_chosen_for_somebody_in_the_list_they_choose_from()
    {
        // THE ANSWER TO THIS QUESTION CAN DESTROY A DISC. The list used to
        // start on its first row, and "Use that one" is the default button, so
        // Enter erased whichever drive happened to come first. After the
        // fifteen second wait this dialog used to sit behind, somebody
        // impatient pressed Enter the moment it appeared.
        //
        // It had already half happened: pressing Erase a second time went to
        // E: without Lazar feeling he had picked it. The answer then was to
        // ask with a list rather than a yes or no, and the list answered for
        // him anyway.
        //
        // Which one starts chosen is decided in the code now, per question,
        // so the markup must not decide it for every question at once.
        XElement list = Assert.Single(
            Markup("PickOne.xaml").Descendants(),
            each => each.Name.LocalName == "ListBox");

        Assert.Null(list.Attribute("SelectedIndex"));
        Assert.Equal("SomethingWasChosen", (string?)list.Attribute("SelectionChanged"));

        // AND THIS ONE, WHICH IS THE HALF THAT WAS MISSED. Dropping
        // SelectedIndex is not enough: a Selector leaves this null, WPF reads
        // null as true once the items come from a binding, and a fresh view's
        // current item is its first one. So the list quietly selected row
        // nought anyway.
        //
        // The two assertions above passed while the running dialog still
        // chose a disc for somebody. It was caught by pressing Erase in the
        // real window and asking the list what was selected, which is the
        // argument for doing that rather than trusting markup read as XML.
        Assert.Equal("False", (string?)list.Attribute("IsSynchronizedWithCurrentItem"));
    }

    [Fact]
    public void The_erase_question_starts_with_no_disc_chosen()
    {
        // And the window asks it that way. The markup leaving it open is only
        // half the guard: something still has to say so for the one question
        // whose answer cannot be taken back.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        int asking = code.IndexOf("Which disc should be erased?", StringComparison.Ordinal);

        Assert.True(asking > 0, "the erase question has moved");

        // Inside that call rather than anywhere in the file, so the picker for
        // an image is not what satisfies this.
        string theCall = code[asking..];
        int ends = theCall.IndexOf("));", StringComparison.Ordinal);

        Assert.Contains("pickTheFirst: false", theCall[..ends], StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("PickOne.xaml")]
    [InlineData("WhatElseGoesOnIt.xaml")]
    [InlineData("WhatElseIsInThere.xaml")]
    [InlineData("WorthAsking.xaml")]
    public void Every_resource_a_window_asks_for_is_somewhere_to_be_found(string window)
    {
        // A style key that nothing defines compiles, and then takes the whole
        // program down the moment the window is built. No other guard here
        // would see it: these read the markup as XML, and XML does not care
        // whether a name resolves.
        //
        // Nearly earned: Quietly was deleted as the only thing using it, and
        // the version line that came back a day later asks for it again.
        HashSet<string> known =
        [
            .. new[] { "Look.xaml", "App.xaml", window }
                .SelectMany(each => Markup(each).DescendantsAndSelf())
                .Select(each => (string?)each.Attribute(X + "Key"))
                .OfType<string>()
        ];

        List<string> missing =
        [
            .. Markup(window).DescendantsAndSelf()
                .SelectMany(each => each.Attributes())
                .SelectMany(each => System.Text.RegularExpressions.Regex.Matches(
                    each.Value, @"\{StaticResource (\w+)\}"))
                .Select(each => each.Groups[1].Value)
                .Where(each => !known.Contains(each))
                .Distinct()
        ];

        Assert.True(
            missing.Count == 0,
            window + " asks for resources nothing defines: " + string.Join(", ", missing));
    }

    [Fact]
    public void Start_a_new_disc_goes_grey_when_there_is_nothing_to_start_again_from()
    {
        // NothingIsRunning was not enough. On a window with nothing on it the
        // button asked nothing and cleared nothing, which reads exactly like
        // a button that does not work, and that is how it was reported.
        XElement start = Window()
            .Descendants(Xaml + "Button")
            .Single(each => IdOf(each) == "StartANewDisc");

        Assert.Equal("{Binding CanStartAgain}", (string?)start.Attribute("IsEnabled"));
    }

    [Fact]
    public void No_property_element_sits_in_the_middle_of_an_elements_content()
    {
        // A XAML rule that XML knows nothing about: <Style.Triggers> and the
        // like must come before everything else or after it, never with
        // content on both sides. Put among the Setters it is valid XML, it
        // reads perfectly well, every guard in this file passes it, and
        // WPF refuses to compile it. The app simply does not open.
        //
        // This project's tests cannot reference DiscWright.App at all, by
        // design, so nothing in a test run ever compiles this markup. A
        // Style.Triggers block dropped into the middle of Quietly took the
        // whole app out with all 956 tests green, and was found by looking
        // at the window.
        List<string> wrong = [];

        foreach (string file in Directory.GetFiles(Repository.File("src", "DiscWright.App"), "*.xaml"))
        {
            foreach (XElement element in XDocument.Load(file).Descendants())
            {
                XElement[] children = [.. element.Elements()];

                bool IsProperty(XElement each) => each.Name.LocalName.Contains('.');

                int first = Array.FindIndex(children, each => !IsProperty(each));
                int last = Array.FindLastIndex(children, each => !IsProperty(each));

                if (first < 0)
                {
                    continue;
                }

                for (int at = first; at < last; at++)
                {
                    if (IsProperty(children[at]))
                    {
                        wrong.Add($"{System.IO.Path.GetFileName(file)}: "
                            + $"<{children[at].Name.LocalName}> inside <{element.Name.LocalName}>");
                    }
                }
            }
        }

        Assert.True(wrong.Count == 0, "Markup WPF will not compile: " + string.Join("; ", wrong));
    }

    [Fact]
    public void The_way_out_of_a_refusal_sits_with_the_refusal()
    {
        // Under the red line and above the figures would read as one of the
        // figures. Under the lot of it, where somebody who has read the line
        // is looking next.
        XElement found = Window()
            .Descendants(Xaml + "Button")
            .Single(each => IdOf(each) == "TheWayOut");

        Assert.Equal("{Binding WhatTheWayOutIsCalled}", (string?)found.Attribute("Content"));

        Assert.Equal(
            "{Binding WhatTheWayOutIsCalled, Converter={StaticResource WhenAnything}}",
            (string?)found.Attribute("Visibility"));

        // The refusal it belongs to is the previous thing on the panel.
        Assert.Equal("WhyNot", IdOf(found.ElementsBeforeSelf().Last()));
    }

    [Fact]
    public void The_way_out_of_a_worry_sits_under_its_figures()
    {
        // "Rename the disc" beside the name the disc will really show, which
        // was computed from the first day and never shown.
        XElement found = Window()
            .Descendants(Xaml + "Button")
            .Single(each => IdOf(each) == "TheWorrysWayOut");

        Assert.Equal("{Binding WhatTheWorrysWayOutIsCalled}", (string?)found.Attribute("Content"));
        Assert.Equal("TakeTheWorrysWayOut", (string?)found.Attribute("Click"));
        Assert.Equal(
            "{Binding WhatTheWorrysWayOutIsCalled, Converter={StaticResource WhenAnything}}",
            (string?)found.Attribute("Visibility"));
        Assert.Equal("WorryFigures", IdOf(found.ElementsBeforeSelf().Last()));
    }

    [Fact]
    public void The_name_can_be_found_by_the_code_that_puts_the_cursor_in_it()
    {
        // FocusField is what every remedy about the name asks for, and the
        // code behind reaches the box by this name.
        XElement box = Window()
            .Descendants(Xaml + "TextBox")
            .Single(each => IdOf(each) == "Label");

        Assert.Equal("TheName", (string?)box.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")));
    }

    [Theory]
    [InlineData("Games")]
    [InlineData("Label")]
    [InlineData("DiscSize")]
    [InlineData("CutFiles")]
    [InlineData("WhichDrive")]
    [InlineData("OutputFolder")]
    [InlineData("Menu")]
    [InlineData("Checksums")]
    [InlineData("LegacyFileSystems")]
    [InlineData("TakeOffWhatIsLeftOff")]
    public void Nothing_that_changes_the_disc_can_be_touched_while_one_is_being_made(string which)
    {
        // Only the buttons went dead. Every box, tick and dropdown stayed
        // live, so the window could be made to describe a plan for something
        // other than the thing being written, and the worst of them retyped
        // where the ISO goes while a build was already writing somewhere
        // else.
        XElement control = Window().Descendants().Single(each => IdOf(each) == which);

        Assert.Equal("{Binding NothingIsRunning}", (string?)control.Attribute("IsEnabled"));
    }

    [Theory]
    [InlineData("TextBox")]
    [InlineData("CheckBox")]
    [InlineData("ComboBox")]
    [InlineData("TreeView")]
    public void An_input_that_can_be_held_looks_held(string kind)
    {
        // The same fault as the buttons had, in the inputs. Measured off two
        // renders of the real window: bound to IsEnabled and left alone, the
        // words stayed in exactly the ink of a live control, and a trigger
        // setting Foreground did not reach them either, because the theme
        // draws the content through a brush of its own.
        //
        // Opacity is applied to the whole control and nothing inside it can
        // set it back, which is why all four use it.
        XElement look = XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!;

        XElement style = look
            .Elements(Xaml + "Style")
            .Single(each => (string?)each.Attribute("TargetType") == kind
                && each.Attribute(Named + "Key") is null);

        bool dims = style
            .Elements(Xaml + "Style.Triggers")
            .Elements(Xaml + "Trigger")
            .Where(trigger => (string?)trigger.Attribute("Property") == "IsEnabled"
                && (string?)trigger.Attribute("Value") == "False")
            .Elements(Xaml + "Setter")
            .Any(setter => (string?)setter.Attribute("Property") == "Opacity");

        Assert.True(dims, $"A disabled {kind} looks exactly like a live one.");
    }

    [Fact]
    public void The_words_standing_in_for_an_empty_drive_picker_are_held_too()
    {
        // A sibling of the picker rather than a child, so holding the picker
        // left these the one live looking thing in a column of dead ones.
        XElement said = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => (string?)each.Attribute("Text") == "Whichever one is ready");

        Assert.Equal(
            "{Binding NothingIsRunning, Converter={StaticResource HeldBack}}",
            (string?)said.Attribute("Opacity"));
    }

    [Fact]
    public void What_the_recogniser_noticed_is_on_the_window()
    {
        // It was worked out and dropped. Nothing but the recogniser and its
        // own tests ever mentioned the field it lives on.
        XElement said = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => IdOf(each) == "WhatTheDownloadsSay");

        Assert.Equal("{Binding WhatTheDownloadsSay}", (string?)said.Attribute("Text"));

        // Amber. An unfinished download is not a refusal, because the disc
        // builds, and it is not ordinary ink either, because the disc is
        // spent by the time anybody finds out.
        Assert.Equal("{DynamicResource WorryInk}", (string?)said.Attribute("Foreground"));

        Assert.Equal(
            "{Binding WhatTheDownloadsSay, Converter={StaticResource WhenAnything}}",
            (string?)said.Attribute("Visibility"));
    }

    [Fact]
    public void What_the_disc_will_only_copy_is_on_the_window()
    {
        XElement said = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => IdOf(each) == "WhatWillNotStartItself");

        Assert.Equal("{Binding WhatWillNotStartItself}", (string?)said.Attribute("Text"));

        // Ordinary ink. A disc of files to copy is a thing somebody may well
        // mean to make, and a window where everything is amber is a window
        // where amber means nothing.
        Assert.Null(said.Attribute("Foreground"));
        Assert.Equal("{StaticResource Aside}", (string?)said.Attribute("Style"));

        Assert.Equal(
            "{Binding WhatWillNotStartItself, Converter={StaticResource WhenAnything}}",
            (string?)said.Attribute("Visibility"));
    }

    [Fact]
    public void What_the_image_and_the_drive_had_to_say_is_on_the_window()
    {
        // The screen worked both of these out from the first day, the older
        // file systems left off because one file is too big for them and how
        // many times the drive reported while it wrote, and the tests heard
        // both announced. Nothing on the window showed either, so the one
        // warning that a disc asked to be readable on an old machine is not
        // reached nobody.
        XElement said = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => IdOf(each) == "AboutTheImage");

        Assert.Equal("{Binding AboutTheImage}", (string?)said.Attribute("Text"));
        Assert.Equal("{StaticResource Aside}", (string?)said.Attribute("Style"));

        Assert.Equal(
            "{Binding AboutTheImage, Converter={StaticResource WhenAnything}}",
            (string?)said.Attribute("Visibility"));
    }

    [Fact]
    public void How_the_last_burn_came_out_stays_on_the_window()
    {
        // It arrived in a message box and was gone at OK, and it is the one
        // sentence somebody holding the disc wants to look at twice. A line
        // under the buttons, in the aside style, in worry ink when the disc
        // or the set is not right.
        XElement said = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => IdOf(each) == "HowTheBurnWent");

        Assert.Equal("{Binding HowTheBurnWent}", (string?)said.Attribute("Text"));
        Assert.Equal(
            "{Binding HowTheBurnWent, Converter={StaticResource WhenAnything}}",
            (string?)said.Attribute("Visibility"));

        XElement style = said.Descendants(Xaml + "Style").Single();
        Assert.Equal("{StaticResource Aside}", (string?)style.Attribute("BasedOn"));

        XElement worry = style.Descendants(Xaml + "DataTrigger").Single();
        Assert.Equal("{Binding TheBurnWentBadly}", (string?)worry.Attribute("Binding"));
        Assert.Equal("True", (string?)worry.Attribute("Value"));

        XElement ink = worry.Elements(Xaml + "Setter").Single();
        Assert.Equal("Foreground", (string?)ink.Attribute("Property"));
        Assert.Equal("{DynamicResource WorryInk}", (string?)ink.Attribute("Value"));
    }

    [Fact]
    public void A_rows_open_state_lives_on_the_row_itself()
    {
        // A game opens and the folders inside it stay shut, which is
        // IsOpened's own default. It used to be a setter and a trigger
        // here, but open state the window kept alone died when a branch
        // was rebuilt, and taking a row off rebuilds its branch: the
        // folder the cross was pressed in snapped shut under the mouse.
        // Two way, or what somebody opens by hand never reaches the row
        // and there is nothing for the rebuild to carry across.
        XElement style = Window()
            .Descendants(Xaml + "TreeView")
            .Single(each => IdOf(each) == "Games")
            .Descendants(Xaml + "Style")
            .Single(each => (string?)each.Attribute("TargetType") == "TreeViewItem");

        XElement open = style.Elements(Xaml + "Setter")
            .Single(each => (string?)each.Attribute("Property") == "IsExpanded");
        Assert.Equal("{Binding IsOpened, Mode=TwoWay}", (string?)open.Attribute("Value"));
    }

    [Fact]
    public void The_more_in_that_folder_dialog_explains_patches_once_above_the_rows()
    {
        // The explanation of what a patch is lives where the choice is
        // made, said once for the group rather than once per row, and only
        // when patches are among the files: a dialog that explains patches
        // over a lone README is noise.
        XElement said = Markup("WhatElseIsInThere.xaml")
            .Descendants(Xaml + "TextBlock")
            .Single(each => IdOf(each) == "AboutPatches");

        Assert.Equal("{Binding AboutPatches}", (string?)said.Attribute("Text"));
        Assert.Equal(
            "{Binding AboutPatches, Converter={StaticResource WhenAnything}}",
            (string?)said.Attribute("Visibility"));
        Assert.Equal("Wrap", (string?)said.Attribute("TextWrapping"));

        // And the line under each row wraps too: "so it adds nothing to
        // this disc" was clipped to "so it adds", which reads as the exact
        // opposite of what it says. Caught by photograph.
        XElement explains = Markup("WhatElseIsInThere.xaml")
            .Descendants(Xaml + "TextBlock")
            .Single(each => (string?)each.Attribute("Text") == "{Binding Explains}");
        Assert.Equal("Wrap", (string?)explains.Attribute("TextWrapping"));
    }

    [Fact]
    public void The_dialogs_buttons_follow_the_ticks()
    {
        XElement[] buttons = [.. Markup("WhatElseIsInThere.xaml").Descendants(Xaml + "Button")];

        // With nothing ticked, "Put these on too" would put nothing on,
        // which is the other button's job, so it greys instead of lying.
        XElement takeThem = buttons.Single(each => IdOf(each) == "TakeThem");
        Assert.Equal("{Binding AnyWanted}", (string?)takeThem.Attribute("IsEnabled"));

        // And the way back from the patch defaults, for the archive disc
        // that wants everything: one link ticks every row, grey when
        // there is nothing left for it to tick.
        XElement all = buttons.Single(each => IdOf(each) == "TickThemAll");
        Assert.Equal("Tick them all", (string?)all.Attribute("Content"));
        Assert.Equal("{Binding CanTickThemAll}", (string?)all.Attribute("IsEnabled"));
        Assert.Equal("TickThemAll", (string?)all.Attribute("Click"));

        // The window hears every tick, or both of those freeze on their
        // first answer. Read off disk, because nothing here can press a
        // box in a window.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "WhatElseIsInThere.xaml.cs"));
        Assert.Contains("nameof(AnyWanted)", code, StringComparison.Ordinal);
        Assert.Contains("nameof(CanTickThemAll)", code, StringComparison.Ordinal);
        Assert.Contains("each.Wanted = true;", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_header_row_carries_a_rule_under_it()
    {
        // Whatever is in it, the header is a band the full width of the
        // window with text at one end and buttons at the other. The rule is
        // what makes the space between them read as a header bar rather than
        // as somewhere a thing used to be.
        XElement band = Window()
            .Descendants(Xaml + "Border")
            .Single(each => (string?)each.Attribute("Grid.Row") == "0");

        Assert.Equal("0,0,0,1", (string?)band.Attribute("BorderThickness"));
        Assert.Equal("{DynamicResource Edge}", (string?)band.Attribute("BorderBrush"));
    }

    [Theory]
    [InlineData("Put a game on a disc that still works in ten years.")]
    [InlineData("Leave this alone and DiscWright works it out, and asks only if there is something to ask.")]
    public void Does_not_spend_a_row_on_what_a_hover_or_nothing_can_carry(string gone)
    {
        // The first sold the app to somebody who has already opened it. The
        // second explained a placeholder that says the same thing itself, and
        // is now the dropdown's tooltip.
        Assert.DoesNotContain(
            gone,
            Window().Descendants().Select(each => (string?)each.Attribute("Text")));
    }

    [Fact]
    public void The_preview_sits_on_the_toolbar_with_the_other_list_buttons()
    {
        XElement preview = Window()
            .Descendants(Xaml + "Button")
            .Single(each => IdOf(each) == "PreviewTheMenu");

        // On the row that acts on the list, level with Add a game, rather
        // than buried in More options where nobody found it or sharing the
        // header row, where its height was the header's height and the list
        // paid for it.
        Assert.Contains(
            "AddAGame",
            preview.Parent!.Descendants(Xaml + "Button").Select(IdOf));

        // At the far end of that row in a column of its own. The toolbar is
        // two buttons for good now, removing is a gesture on the rows
        // themselves, so nothing can crowd this one off the edge again the
        // way the fourth button did on 2026-11-09.
        Assert.Equal("1", (string?)preview.Attribute("Grid.Column"));
        Assert.Equal("Right", (string?)preview.Attribute("HorizontalAlignment"));

        string[] onTheToolbar =
            [.. preview.Parent!.Descendants(Xaml + "Button").Select(each => IdOf(each) ?? string.Empty)];
        Assert.Equal(["AddAGame", "PreviewTheMenu"], onTheToolbar);

        // Still dead unless there is a menu to preview, so moving it cannot
        // make it show something that is not going to be on the disc.
        Assert.Equal("{Binding CanPreview}", (string?)preview.Attribute("IsEnabled"));
    }

    [Fact]
    public void The_line_under_FOR_PRINTING_says_what_choosing_them_does_do()
    {
        // It said only "Choosing them is not printing them.", which answers
        // what this is NOT for and leaves what it IS for unsaid.
        XElement said = Markup("WhatElseGoesOnIt.xaml")
            .Descendants(Xaml + "TextBlock")
            .Single(each => ((string?)each.Attribute("Text"))?.StartsWith(
                "Choosing them", StringComparison.Ordinal) == true);

        string words = (string)said.Attribute("Text")!;

        Assert.Equal(
            "Choosing them is not printing them. They go on the disc in an ARTWORK folder to print later.",
            words);

        // One row, which is the whole constraint on it. Measured on the real
        // dialog: 493px of text in a slot 591px wide, so the budget below is
        // where it would start to be in danger rather than a guess. NoWrap
        // with no trimming does not wrap when it runs out of room, it is
        // simply cut off, and nothing on screen says it was.
        Assert.Equal("NoWrap", (string?)said.Attribute("TextWrapping"));

        Assert.True(
            words.Length <= 105,
            $"\"{words}\" is {words.Length} characters, and about 105 is as much as that row holds.");
    }

    [Fact]
    public void Does_not_claim_the_printing_artwork_never_reaches_the_disc()
    {
        // It does reach it, the moment somebody ticks "The cover art on the
        // disc as well", which writes cover and label into an ARTWORK folder.
        // It was true of the default and false of the option when there was
        // one. The option is gone and the artwork always goes on, so the
        // sentence is now simply false, and a window that says something
        // untrue about its own behaviour is worse than one that says nothing.
        Assert.DoesNotContain(
            "These never go on the disc. Choosing them now is not printing them.",
            Markup("WhatElseGoesOnIt.xaml").Descendants().Select(each => (string?)each.Attribute("Text")));

        // And there is no setting left to make it conditional on.
        Assert.DoesNotContain("ArtworkOnDisc", Window().Descendants().Select(IdOf));
    }

    [Fact]
    public void A_row_that_had_to_be_trimmed_can_be_read_on_hover()
    {
        // The name is given a fixed width so a long one cannot push the row
        // past the panel, which means it is trimmed and readable nowhere
        // else. A row that trims without a tooltip loses what it trimmed.
        XElement name = Window()
            .Descendants(Xaml + "HierarchicalDataTemplate")
            .Single()
            .Descendants(Xaml + "TextBlock")
            .First(each => (string?)each.Attribute("Text") == "{Binding Name}");

        Assert.Equal("CharacterEllipsis", (string?)name.Attribute("TextTrimming"));
        Assert.Equal("{Binding InFull}", (string?)name.Attribute("ToolTip"));

        // And a screen reader gets the same thing, which a tooltip alone
        // does not give it.
        Assert.Equal(
            "{Binding InFull}",
            (string?)name.Attribute(AutomationHelpText));
    }

    private static readonly XName AutomationHelpText =
        XName.Get("AutomationProperties.HelpText");

    [Fact]
    public void The_total_is_shown_inside_the_panel_it_adds_up()
    {
        // The screen can work the total out perfectly and the window can
        // simply not show it, which is what a planted fault proved by taking
        // the line off the markup while every test about the total stayed
        // green.
        XElement tree = Window()
            .Descendants(Xaml + "TreeView")
            .Single(each => IdOf(each) == "Games");

        // In the panel the list is in, rather than floating underneath it
        // among the lines about what was left off. It is about the list.
        XElement panel = tree.Ancestors(Xaml + "Border").First();

        XElement total = panel
            .Descendants(Xaml + "TextBlock")
            .Single(each => IdOf(each) == "WhatIsOnIt");

        Assert.Equal("{Binding WhatIsOnIt}", (string?)total.Attribute("Text"));
    }

    [Fact]
    public void No_tick_box_promises_a_number_it_does_not_decide()
    {
        // "Split a file across two discs" was a promise about two, and the
        // planner has never worked that way: it cuts a file into as many
        // pieces as the file and the chosen disc come to between them. How
        // many is a fact about this project, so it is said under the box by
        // something that can count, not written into the label.
        XElement box = Window()
            .Descendants(Xaml + "CheckBox")
            .Single(each => IdOf(each) == "CutFiles");

        string label = (string?)box.Attribute("Content") ?? string.Empty;

        Assert.DoesNotContain("two", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("2", label, StringComparison.Ordinal);

        // And the counting line is really there, right under it.
        Assert.Contains(
            "WhatSplittingWouldDo",
            Window().Descendants(Xaml + "TextBlock").Select(IdOf));
    }

    [Fact]
    public void No_label_on_a_tick_box_is_long_enough_to_wrap()
    {
        // A label that wraps makes a row twice as tall as the box in it, and
        // the artwork window is already seven rows of the same shape.
        foreach (string window in new[] { "MainWindow.xaml", "WhatElseGoesOnIt.xaml" })
        {
            foreach (XElement box in Markup(window).Descendants(Xaml + "CheckBox"))
            {
                string? label = (string?)box.Attribute("Content");
                if (label is null)
                {
                    continue;
                }

                Assert.True(
                    label.Length <= 40,
                    $"\"{label}\" is {label.Length} characters, which wraps. The reason belongs under it or in a tooltip.");
            }
        }
    }

    [Fact]
    public void A_control_with_neither_a_name_nor_an_id_is_not_reachable()
    {
        // The guard above is the one that stops v1 happening again, so it
        // needs a guard of its own. Turning it into "yes, always" broke
        // nothing, which means it was being trusted rather than tested.
        XElement loose = new(Xaml + "Button");
        Assert.False(TheWindowsMarkup.CanBeFound(loose));

        XElement withAnId = new(Xaml + "Button");
        withAnId.SetAttributeValue("AutomationProperties.AutomationId", "Something");
        Assert.True(TheWindowsMarkup.CanBeFound(withAnId));

        // A name alone is not enough out in the window, where there is one of
        // each control and an id is what a test looks it up by.
        XElement named = new(Xaml + "Button");
        named.SetAttributeValue("AutomationProperties.Name", "Something");
        Assert.False(TheWindowsMarkup.CanBeFound(named));

        // Inside a row template there is one per row, so a name bound to the
        // row is the only thing that could work.
        XElement template = new(Xaml + "HierarchicalDataTemplate");
        XElement inARow = new(Xaml + "CheckBox");
        inARow.SetAttributeValue("AutomationProperties.Name", "{Binding Name}");
        template.Add(inARow);
        Assert.True(TheWindowsMarkup.CanBeFound(inARow));
    }

    [Fact]
    public void A_name_in_a_tree_row_cannot_push_the_rest_of_the_row_off_the_edge()
    {
        // A TreeViewItem's header sits in an Auto column of its own template,
        // so a row is only ever as wide as what is in it and
        // HorizontalContentAlignment has nothing to stretch into. A long file
        // name grew the row past the panel and everything after the name was
        // clipped, with no scrollbar and nothing else to say so.
        //
        // That was tried, photographed, and still wrong, which is why the
        // guard is on the thing that actually prevents it.
        XElement row = Window()
            .Descendants(Xaml + "HierarchicalDataTemplate")
            .Single()
            .Descendants(Xaml + "TextBlock")
            .First(each => (string?)each.Attribute("Text") == "{Binding Name}");

        Assert.NotNull(row.Attribute("Width"));
        Assert.Equal("CharacterEllipsis", (string?)row.Attribute("TextTrimming"));
    }

    [Fact]
    public void The_room_a_name_gets_is_decided_by_the_row_and_not_by_this_file()
    {
        XElement name = Row()
            .Descendants(Xaml + "TextBlock")
            .First(each => (string?)each.Attribute("Text") == "{Binding Name}");

        // A fixed width here is what put a file three folders down nearly
        // sixty pixels right of a game, and nothing in this file can be asked
        // a question by a test: nothing may reference this project. The row
        // works it out, so the arithmetic that lines the sizes up is somewhere
        // a test can reach.
        Assert.Equal("{Binding HowWideTheNameIs}", (string?)name.Attribute("Width"));
    }

    [Fact]
    public void Every_size_in_the_list_ends_in_the_same_column()
    {
        XElement size = Row()
            .Descendants(Xaml + "TextBlock")
            .Single(each => (string?)each.Attribute("Text") == "{Binding HowBig}");

        // Both halves are needed and neither is enough. Without a width it is
        // as wide as the figure in it and the next row's figure starts
        // somewhere else; without the alignment the figures start together and
        // end nowhere near each other, which is a column that cannot be read
        // down.
        Assert.NotNull(size.Attribute("Width"));
        Assert.Equal("Right", (string?)size.Attribute("TextAlignment"));
    }

    [Fact]
    public void A_row_of_the_list_cannot_be_wider_than_the_list()
    {
        // Worked out from the window rather than stated, so widening a column
        // or adding one fails here instead of on somebody's screen with the
        // size clipped off the edge. The list cannot scroll sideways, by
        // deliberate choice, so anything past the edge is simply gone.
        double window = Figure(Window().Attribute("Width"));
        double[] margin = [.. ((string)Window().Element(Xaml + "Grid")!.Attribute("Margin")!)
            .Split(',').Select(each => double.Parse(each, CultureInfo.InvariantCulture))];

        XElement columns = Window()
            .Descendants(Xaml + "Grid")
            .First(grid => (string?)grid.Attribute("Grid.Row") == "1");

        double besideTheList = columns
            .Element(Xaml + "Grid.ColumnDefinitions")!
            .Elements(Xaml + "ColumnDefinition")
            .Select(each => (string?)each.Attribute("Width"))
            .Where(each => each != "*")
            .Sum(each => double.Parse(each!, CultureInfo.InvariantCulture));

        double list = window - margin[0] - margin[2] - besideTheList;

        // What the list spends on itself before a row gets any of it: the
        // panel's border, the tree's padding, the column a tree keeps for the
        // little arrow, and the scrollbar that appears the moment there are
        // more rows than fit.
        const double TheBorder = 2;
        const double ThePadding = 8;
        const double TheArrow = 19;
        const double TheScrollbar = 17;

        double room = list - TheBorder - ThePadding - TheArrow - TheScrollbar;

        // Everything a row declares for itself, and the box, which takes the
        // size WPF gives it.
        const double TheTickBox = 16;

        double row = TheTickBox + Row()
            .Descendants()
            .Sum(each => WideAs(each.Attribute("Width"))
                + Across(each.Attribute("Margin"))
                + StyledWidth(each));

        Assert.True(
            row <= room,
            $"A row declares {row} and the list has {room} to give it.");
    }

    /// <summary>
    /// The width an element takes from its style, for the parts of a row
    /// whose size lives in Look.xaml because the buttons rule says a button
    /// may not size itself. Without this the cross on every row would be a
    /// width this guard never counted.
    /// </summary>
    private static double StyledWidth(XElement element)
    {
        if (element.Attribute("Width") is not null)
        {
            return 0;
        }

        System.Text.RegularExpressions.Match wearing =
            System.Text.RegularExpressions.Regex.Match(
                (string?)element.Attribute("Style") ?? string.Empty,
                @"^\{StaticResource (\w+)\}$");

        if (!wearing.Success)
        {
            return 0;
        }

        return XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!
            .Elements(Xaml + "Style")
            .Where(style => (string?)style.Attribute(Named + "Key") == wearing.Groups[1].Value)
            .SelectMany(style => style.Elements(Xaml + "Setter"))
            .Where(setter => (string?)setter.Attribute("Property") == "Width")
            .Sum(setter => Figure(setter.Attribute("Value")));
    }

    /// <summary>
    /// How wide a part of the row is, the widest of them included.
    /// </summary>
    /// <remarks>
    /// The name does not state a width, it asks the row for one, and leaving
    /// it out of the sum would leave out the widest column on the row and make
    /// this a guard over the small ones.
    /// </remarks>
    private static double WideAs(XAttribute? width) =>
        (string?)width == "{Binding HowWideTheNameIs}"
            ? new AThingOnTheDisc("a name", 0, null, "a name").HowWideTheNameIs
            : Figure(width);

    /// <summary>A width or nought for something that does not state one.</summary>
    private static double Figure(XAttribute? width) =>
        width is null || !double.TryParse((string)width, CultureInfo.InvariantCulture, out double how)
            ? 0
            : how;

    /// <summary>What a margin takes left and right of the thing it is on.</summary>
    private static double Across(XAttribute? margin)
    {
        if (margin is null)
        {
            return 0;
        }

        double[] sides =
        [
            .. ((string)margin).Split(',').Select(each => double.Parse(each, CultureInfo.InvariantCulture))
        ];

        return sides.Length == 2 ? sides[0] * 2 : sides[0] + sides[2];
    }

    /// <summary>One row of the list of what goes on the disc.</summary>
    private static XElement Row() => Window().Descendants(Xaml + "HierarchicalDataTemplate").Single();

    [Fact]
    public void The_empty_list_asks_for_a_game_and_one_button_takes_one()
    {
        XElement window = Window();

        XElement asked = window.Descendants(Xaml + "TextBlock").Single(each => IdOf(each) == "NothingYet");

        // The words carry their own coming and going, because there is
        // nothing in there with them to come and go alongside.
        Assert.Equal(
            "{Binding Games.Count, Converter={StaticResource WhenNone}}",
            (string?)asked.Attribute("Visibility"));

        // And exactly one button on the window takes a game.
        //
        // A second one sat beside those words for a day, on the argument that
        // the one thing there is to do should not be under a large empty box.
        // It could not be pressed: the tree is drawn over that cell, so a
        // mouse reached the tree and never the button, while automation could
        // invoke it perfectly well. That is the shape of a thing that looks
        // tested and is not, and this is the guard against it coming back.
        XElement[] adding =
        [
            .. window.Descendants(Xaml + "Button")
                .Where(each => (string?)each.Attribute("Content") == "Add a game")
        ];

        Assert.Single(adding);
        Assert.Equal("AddAGame", IdOf(adding[0]));
    }

    [Fact]
    public void Every_row_carries_its_own_way_off()
    {
        // Removing used to be select it, then walk up to a Remove button, a
        // two-step the owner called strange twice in the 2026-11-09 session,
        // with a second Remove coming and going beside it. The cross on the
        // row is the gesture on the thing itself, and the buttons it
        // replaces are gone from the toolbar.
        XElement cross = Assert.Single(
            Row().Descendants(Xaml + "Button"), each => IdOf(each) == "TakeOffThisRow");

        Assert.Equal("{StaticResource OffTheRow}", (string?)cross.Attribute("Style"));
        Assert.Equal("TakeOffThisRow", (string?)cross.Attribute("Click"));
        Assert.NotNull(cross.Attribute("ToolTip"));
        Assert.NotNull(cross.Attribute("AutomationProperties.Name"));

        // The handler acts on the row the cross was drawn on, never on the
        // selection, so there is no way to press one row's cross and lose a
        // different row.
        string handlers = File.ReadAllText(
            Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));
        Assert.Contains(
            "pressed.DataContext is AThingOnTheDisc row", handlers, StringComparison.Ordinal);

        // And nothing called Remove sits on the toolbar any more.
        Assert.DoesNotContain(
            Window().Descendants(Xaml + "Button"), each => IdOf(each) == "TakeItOff");
    }

    [Fact]
    public void The_cross_hides_without_moving_the_columns()
    {
        XElement look = XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!;

        XElement style = Assert.Single(
            look.Elements(Xaml + "Style"),
            each => (string?)each.Attribute(Named + "Key") == "OffTheRow");

        // Hidden, never collapsed: a collapsed cross gives its width back,
        // and the size column would step left and right as the mouse moves
        // down the list.
        Assert.Contains(style.Elements(Xaml + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility"
            && (string?)setter.Attribute("Value") == "Hidden");

        // Shown for the row under the mouse, read off the Border each row's
        // template wraps its own header in, not off the TreeViewItem, which
        // is over its children too and would light every ancestor's cross
        // while a file is hovered.
        XElement shows = Assert.Single(style.Descendants(Xaml + "DataTrigger"));
        Assert.Equal(
            "{Binding IsMouseOver, RelativeSource={RelativeSource AncestorType=Border}}",
            (string?)shows.Attribute("Binding"));
        Assert.Contains(shows.Elements(Xaml + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility"
            && (string?)setter.Attribute("Value") == "Visible");

        // Its size lives here so the buttons rule holds, and the row width
        // guard reads it from here as well: a cross with no width in its
        // style is a cross that guard cannot count.
        Assert.Contains(style.Elements(Xaml + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Width");
    }

    [Fact]
    public void Clearing_out_the_unticked_rows_is_a_link_on_the_sentence_that_counts_them()
    {
        // It was a toolbar button that came and went, crowded the preview
        // off the edge, and said a count the sentence was already saying.
        // It is that sentence's own remedy now, discwright#123's manner,
        // and it wears link ink rather than Quietly's grey, because grey
        // in a corner is what the owner called not that visible.
        XElement link = Assert.Single(
            Window().Descendants(Xaml + "Button"), each => IdOf(each) == "TakeOffWhatIsLeftOff");

        Assert.Equal("{StaticResource AsALink}", (string?)link.Attribute("Style"));
        Assert.Equal("{Binding WhatTakingTheLeftOffOnesSays}", (string?)link.Attribute("Content"));
        Assert.Equal(
            "{Binding WhatTakingTheLeftOffOnesSays, Converter={StaticResource WhenAnything}}",
            (string?)link.Attribute("Visibility"));

        // Beside the sentence, in one panel, so they read as one line.
        XElement beside = link.Parent!;
        Assert.Contains(beside.Elements(Xaml + "TextBlock"), each => IdOf(each) == "WhatIsLeftOff");
    }

    [Fact]
    public void The_count_of_what_is_left_off_sits_on_the_totals_bar()
    {
        // Under the panel in a corner of its own the owner called it not
        // that visible, and the eye that checks the size is already on the
        // bar that says what the list comes to. One wrapping panel, so a
        // long count takes a second line rather than clipping.
        XElement[] text = [.. Window().Descendants(Xaml + "TextBlock")];
        XElement total = text.Single(each => IdOf(each) == "WhatIsOnIt");
        XElement count = text.Single(each => IdOf(each) == "WhatIsLeftOff");

        Assert.Equal("WrapPanel", total.Parent!.Name.LocalName);
        Assert.Same(total.Parent, count.Parent);
    }

    [Fact]
    public void The_separator_dot_needs_something_on_both_sides_of_it()
    {
        // With everything unticked the total says nothing at all, and a
        // separator with nothing on its left is a smudge leading the line.
        // Collapsed unless both sentences are there to separate.
        XElement dot = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => (string?)each.Attribute("Text") == "·");

        XElement style = dot.Descendants(Xaml + "Style").Single();

        XElement fallback = style.Elements(Xaml + "Setter").Single();
        Assert.Equal("Visibility", (string?)fallback.Attribute("Property"));
        Assert.Equal("Collapsed", (string?)fallback.Attribute("Value"));

        XElement[] both = [.. style.Descendants(Xaml + "Condition")];
        Assert.Equal(2, both.Length);
        Assert.Contains(both, each =>
            ((string?)each.Attribute("Binding") ?? string.Empty).Contains("WhatIsOnIt", StringComparison.Ordinal));
        Assert.Contains(both, each =>
            ((string?)each.Attribute("Binding") ?? string.Empty).Contains("WhatIsLeftOff", StringComparison.Ordinal));
        Assert.All(both, each => Assert.Equal("Visible", (string?)each.Attribute("Value")));
    }

    [Fact]
    public void The_build_buttons_say_what_they_will_go_without()
    {
        // The owner pressed Build in the second watching session not
        // knowing whether the unticked row was going along: the sentence
        // that knew sat under the list, and nothing at the button said it.
        // Ordinary ink, because leaving a row off is the thing somebody
        // just deliberately did.
        XElement said = Window()
            .Descendants(Xaml + "TextBlock")
            .Single(each => IdOf(each) == "WhatBuildingGoesWithout");

        Assert.Equal("{Binding WhatBuildingGoesWithout}", (string?)said.Attribute("Text"));
        Assert.Equal(
            "{Binding WhatBuildingGoesWithout, Converter={StaticResource WhenAnything}}",
            (string?)said.Attribute("Visibility"));
        Assert.Equal("{StaticResource Aside}", (string?)said.Attribute("Style"));
        Assert.Null(said.Attribute("Foreground"));

        // On the strip the buttons live on, which is the whole point.
        XElement home = said.Ancestors(Xaml + "Grid").First();
        Assert.Contains(home.Descendants(Xaml + "Button"), each => IdOf(each) == "MakeTheDisc");
    }

    [Fact]
    public void The_link_ink_is_the_accent_over_the_quiet_links_shape()
    {
        // AsALink exists for a link that was being missed: the accent ink
        // at rest is what says it can be pressed. Everything else, the
        // hand, the underline on hover, the dead grey, is Quietly's, and
        // building on it is what keeps the two from drifting apart.
        XElement style = XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!
            .Elements(Xaml + "Style")
            .Single(each => (string?)each.Attribute(X + "Key") == "AsALink");

        Assert.Equal("{StaticResource Quietly}", (string?)style.Attribute("BasedOn"));

        XElement ink = style.Elements(Xaml + "Setter").Single();
        Assert.Equal("Foreground", (string?)ink.Attribute("Property"));
        Assert.Equal("{DynamicResource Accent}", (string?)ink.Attribute("Value"));
    }

    [Fact]
    public void The_delete_key_is_the_keyboards_half_of_the_cross()
    {
        XElement tree = Assert.Single(Window().Descendants(Xaml + "TreeView"));
        Assert.Equal("TheListKeys", (string?)tree.Attribute("KeyDown"));

        string handlers = File.ReadAllText(
            Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        // The rule the Remove button used to wear is the key's rule now, and
        // the rename box keeps Delete for itself.
        Assert.Contains("Key.Delete", handlers, StringComparison.Ordinal);
        Assert.Contains("_screen.CanTakeSomethingOff", handlers, StringComparison.Ordinal);
        Assert.Contains(
            "e.OriginalSource is not System.Windows.Controls.TextBox",
            handlers,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Says_what_was_left_off_in_ordinary_ink_and_warns_only_about_the_installer()
    {
        XElement[] text = [.. Window().Descendants(Xaml + "TextBlock")];

        XElement count = text.Single(each => IdOf(each) == "WhatIsLeftOff");
        XElement matters = text.Single(each => IdOf(each) == "WhyThatMatters");

        // Leaving a file off is not a mistake. It is the thing somebody just
        // deliberately did, and a window where everything is amber is a
        // window where amber means nothing, including on the tray line.
        Assert.Null(count.Attribute("Foreground"));
        Assert.Equal("{DynamicResource WorryInk}", (string?)matters.Attribute("Foreground"));
    }

    [Fact]
    public void The_preview_says_a_set_asks_before_opening_windows()
    {
        XElement preview = Window()
            .Descendants(Xaml + "Button")
            .Single(each => IdOf(each) == "PreviewTheMenu");

        // The windows of a set land exactly behind one another, and the
        // same surprise caught the owner from both directions: disc 1 of 2
        // taken for the whole answer, then the crowd of windows read as a
        // bug. A set asks first now, and the hover says so.
        string hover = (string?)preview.Attribute("ToolTip") ?? string.Empty;

        Assert.Contains("A set asks first", hover, StringComparison.Ordinal);

        // And where the manual and the extras are: on disc 1, which is the
        // one window most people open, so choosing one window costs nothing
        // somebody would miss.
        Assert.Contains("Disc 1 carries the manual and the extras", hover, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("EraseADisc", "{Binding CanErase}")]
    [InlineData("BurnIt", "{Binding CanBurn}")]
    [InlineData("PreviewTheMenu", "{Binding CanPreview}")]
    public void A_button_that_touches_a_disc_is_dead_when_it_cannot_be_pressed(string id, string bound)
    {
        XElement button = Window()
            .Descendants(Xaml + "Button")
            .Single(each => IdOf(each) == id);

        // Hard-enabled, these invite somebody to press them when there is
        // nothing to press them for, and the answer arrives as an error
        // afterwards instead of a button that was never offered.
        Assert.Equal(bound, (string?)button.Attribute("IsEnabled"));
        Assert.Null(button.Attribute("Visibility"));
    }

    [Fact]
    public void Splitting_across_discs_is_on_the_window_and_not_folded_away()
    {
        XElement split = Window()
            .Descendants(Xaml + "CheckBox")
            .Single(each => IdOf(each) == "CutFiles");

        // It is the answer to "that will not fit" and it changes what every
        // row of the size dropdown says. Folded into More options it was a
        // setting somebody had to already know about to go looking for.
        Assert.DoesNotContain("Expander", split.Ancestors().Select(up => up.Name.LocalName));
    }

    // ------------------------------------------------------------------ helpers

    private static XElement Window() => Markup("MainWindow.xaml");

    private static XElement Markup(string window) =>
        XDocument.Load(Repository.File("src", "DiscWright.App", window)).Root!;

    /// <summary>The palette brushes a dictionary declares, key to hex.</summary>
    private static Dictionary<string, string> BrushesOf(string look) =>
        XDocument.Load(Repository.File("src", "DiscWright.App", look)).Root!
            .Elements(Xaml + "SolidColorBrush")
            .Where(brush => brush.Attribute(Named + "Key") is not null
                && ((string?)brush.Attribute("Color"))?.Length == 7)
            .ToDictionary(
                brush => (string)brush.Attribute(Named + "Key")!,
                brush => (string)brush.Attribute("Color")!);

    /// <summary>
    /// The palette as the window would resolve it wearing this look: the
    /// light brushes with the look's own laid over them. For Look.xaml that
    /// is the light palette itself.
    /// </summary>
    private static Dictionary<string, string> PaletteWearing(string look)
    {
        Dictionary<string, string> palette = BrushesOf("Look.xaml");

        foreach ((string key, string hex) in BrushesOf(look))
        {
            palette[key] = hex;
        }

        return palette;
    }

    [Fact]
    public void The_list_takes_a_drop_everywhere_it_looks_like_the_list()
    {
        // AllowDrop is per element, so the panel alone would refuse a drop
        // over the tree that covers it. Both say yes, with the same pair of
        // handlers, or whether a drop lands depends on which pixel it ends
        // over, which is indistinguishable from flaky.
        XElement tree = Assert.Single(
            Window().Descendants(Xaml + "TreeView"), each => IdOf(each) == "Games");
        XElement panel = tree.Ancestors(Xaml + "Border").First();

        foreach (XElement target in new[] { tree, panel })
        {
            Assert.Equal("True", (string?)target.Attribute("AllowDrop"));
            Assert.Equal("SayWhetherADropWouldLand", (string?)target.Attribute("DragOver"));
            Assert.Equal("SomethingWasDropped", (string?)target.Attribute("Drop"));
        }
    }

    [Fact]
    public void A_drop_walks_the_same_road_as_the_add_button()
    {
        // The one piece of judgement in a drop is WhatWasDropped's, where a
        // test can hold it. The rest is the shared Add, questions and all,
        // and a running window takes no drops at all.
        string code = File.ReadAllText(
            Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Assert.Contains(
            "Add(WhatWasDropped.AsFolders(dropped, _disk.FolderExists))",
            code, StringComparison.Ordinal);
        Assert.Contains(
            "!_screen.NothingIsRunning || e.Data.GetData(DataFormats.FileDrop)",
            code, StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_for_later_is_a_quiet_press_beside_the_two_big_ones()
    {
        // The recipe without the discs, discwright#98. Quiet rather than a
        // third box, because the row above is already as wide as its column
        // and saving is the small act next to the two big ones.
        XElement save = Assert.Single(
            Window().Descendants(Xaml + "Button"), each => IdOf(each) == "SaveItForLater");

        Assert.Equal("{Binding CanSaveItForLater}", (string?)save.Attribute("IsEnabled"));
        Assert.Equal("SaveItForLater", (string?)save.Attribute("Click"));

        // Where the file lands is said before the press, because the one
        // surprising thing about this button is that it uses the ISO folder.
        Assert.NotNull(save.Attribute("ToolTip"));

        XElement said = Assert.Single(
            Window().Descendants(Xaml + "TextBlock"), each => IdOf(each) == "HowTheSaveWent");

        Assert.Equal(
            "{Binding HowTheSaveWent, Converter={StaticResource WhenAnything}}",
            (string?)said.Attribute("Visibility"));
    }

    [Fact]
    public void What_did_not_come_back_is_a_worry_line_that_stays_on_the_window()
    {
        // The news about a project that named more than came back,
        // discwright#98. It used to be a box, and a box is gone at OK while
        // the games are still gone an hour later.
        XElement news = Assert.Single(
            Window().Descendants(Xaml + "TextBlock"), each => IdOf(each) == "WhatDidNotComeBack");

        Assert.Equal("{Binding WhatDidNotComeBack}", (string?)news.Attribute("Text"));
        Assert.Equal("{DynamicResource WorryInk}", (string?)news.Attribute("Foreground"));
        Assert.Equal("Wrap", (string?)news.Attribute("TextWrapping"));
        Assert.Equal(
            "{Binding WhatDidNotComeBack, Converter={StaticResource WhenAnything}}",
            (string?)news.Attribute("Visibility"));
    }

    [Fact]
    public void The_rename_box_stands_exactly_where_the_name_was()
    {
        // In place renaming, v1's double click brought across. The box and
        // the name swap on the same row state, so only ever one of them is
        // on the row, and the double click that opens it is on the tree.
        XElement box = Assert.Single(
            Window().Descendants(Xaml + "TextBox"), each => IdOf(each) == "RenameBox");

        Assert.Equal(
            "{Binding IsBeingRenamed, Converter={StaticResource WhenSo}}",
            (string?)box.Attribute("Visibility"));
        Assert.Equal("AwakenTheRenameBox", (string?)box.Attribute("IsVisibleChanged"));
        Assert.Equal("TheRenameKeys", (string?)box.Attribute("KeyDown"));
        Assert.Equal("TheRenameBoxLostItsFocus", (string?)box.Attribute("LostFocus"));

        XElement name = Assert.Single(
            Window().Descendants(Xaml + "TextBlock"),
            each => (string?)each.Attribute("Text") == "{Binding Name}"
                && each.Attribute("Visibility") is not null);

        Assert.Equal(
            "{Binding IsBeingRenamed, Converter={StaticResource WhenNot}}",
            (string?)name.Attribute("Visibility"));

        XElement tree = Assert.Single(Window().Descendants(Xaml + "TreeView"));
        Assert.Equal("RenameWhatIsPicked", (string?)tree.Attribute("MouseDoubleClick"));
    }

    [Fact]
    public void The_dark_look_answers_for_every_brush_the_light_one_names()
    {
        // The dark look is brushes and nothing else, merged over Look.xaml
        // when Windows says apps are dark. A key it misses stays light in a
        // dark window, and a key of its own answers no reference at all, so
        // either direction of disagreement is a fault.
        string[] light = [.. BrushesOf("Look.xaml").Keys.Order(StringComparer.Ordinal)];
        string[] dark = [.. BrushesOf("LookInTheDark.xaml").Keys.Order(StringComparer.Ordinal)];

        string[] missing = [.. light.Except(dark, StringComparer.Ordinal)];
        string[] extra = [.. dark.Except(light, StringComparer.Ordinal)];

        Assert.True(missing.Length == 0,
            "Brushes that stay light in the dark: " + string.Join(", ", missing));
        Assert.True(extra.Length == 0,
            "Brushes only the dark look knows about, which nothing can be asking for: "
                + string.Join(", ", extra));
    }

    [Fact]
    public void Each_look_is_the_brightness_it_claims()
    {
        // Parity alone would pass a dark look that was the light palette
        // copied out. The names mean something: in the light, ink is darker
        // than the page it sits on; in the dark it is the page that recedes.
        Dictionary<string, string> light = PaletteWearing("Look.xaml");
        Dictionary<string, string> dark = PaletteWearing("LookInTheDark.xaml");

        Assert.True(Lightness(light["Ink"]) < Lightness(light["Page"]),
            "The light look writes in ink no darker than its own page.");
        Assert.True(Lightness(dark["Ink"]) > Lightness(dark["Page"]),
            "The dark look writes in ink no lighter than its own page.");
        Assert.True(Lightness(dark["Page"]) < Lightness(light["Page"]),
            "The dark page is not darker than the light one, so the two looks are not two.");
        Assert.True(Lightness(dark["Panel"]) < Lightness(light["Panel"]),
            "The dark panel is not darker than the light one.");
    }

    [Fact]
    public void A_brush_from_the_palette_is_only_ever_asked_for_dynamically()
    {
        // A StaticResource is resolved once, when the markup loads, and a
        // dictionary merged afterwards changes nothing it already answered.
        // One palette brush asked for statically is one control that stays
        // light in a dark window, which is worse than no dark look at all:
        // it reads as a defect rather than as an absence.
        string[] keys = [.. BrushesOf("Look.xaml").Keys];

        List<string> frozen = [];

        foreach (string file in Directory.GetFiles(Repository.File("src", "DiscWright.App"), "*.xaml"))
        {
            foreach (System.Text.RegularExpressions.Match found in System.Text.RegularExpressions.Regex.Matches(
                File.ReadAllText(file), @"\{StaticResource (\w+)\}"))
            {
                if (keys.Contains(found.Groups[1].Value, StringComparer.Ordinal))
                {
                    frozen.Add($"{Path.GetFileName(file)} asks for {found.Groups[1].Value} statically");
                }
            }
        }

        Assert.True(frozen.Count == 0,
            "Palette brushes resolved once and for ever, so the dark look can never reach them: "
                + string.Join("; ", frozen.Distinct(StringComparer.Ordinal)));
    }

    [Fact]
    public void The_list_wears_a_template_of_its_own()
    {
        // The theme's tree answers IsEnabled=False by painting its own
        // background from system colours, over whatever Background says.
        // Held while a build ran, the whole list went system grey under the
        // dark look, light ink on a light panel, watched on 2026-11-09. A
        // template of ours has no such trigger, so there is nothing to go
        // wrong: this asks that both the tree and its rows carry one.
        XElement look = XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!;

        foreach (string dressed in (string[])["TreeView", "TreeViewItem"])
        {
            XElement style = Assert.Single(
                look.Elements(Xaml + "Style"),
                each => (string?)each.Attribute("TargetType") == dressed
                    && each.Attribute(Named + "Key") is null);

            Assert.True(
                style.Descendants(Xaml + "ControlTemplate")
                    .Any(template => (string?)template.Attribute("TargetType") == dressed),
                $"{dressed} has no template of its own, so the system dresses it.");
        }
    }

    [Fact]
    public void The_chosen_row_is_painted_from_the_palette()
    {
        // Selected and unfocused, the theme's row wore the system's pale
        // grey under our light ink, a row that could not be read in the dark
        // look at all. The chosen row wears what the dialogs' rows wear, the
        // deeper paper and the short accent band, asked for dynamically so
        // the dark look reaches both.
        XElement row = TheRowTemplate();

        XElement chosen = Assert.Single(
            row.Element(Xaml + "ControlTemplate.Triggers")!.Elements(Xaml + "Trigger"),
            each => (string?)each.Attribute("Property") == "IsSelected"
                && (string?)each.Attribute("Value") == "True");

        Assert.Contains(chosen.Elements(Xaml + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Background"
            && (string?)setter.Attribute("TargetName") is not null
            && (string?)setter.Attribute("Value") == "{DynamicResource Page}");
        Assert.Contains(chosen.Elements(Xaml + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "Visibility"
            && (string?)setter.Attribute("TargetName") == "Chosen"
            && (string?)setter.Attribute("Value") == "Visible");
    }

    [Fact]
    public void The_rows_indent_by_what_the_name_arithmetic_assumes()
    {
        // Every row gives its name back whatever the tree pushed it in by,
        // at nineteen a level, so the sizes end in one column. That
        // arithmetic lives in AThingOnTheDisc and is held to nineteen by
        // TheTreeOfWhatGoesOnIt; this holds the markup to the same figure,
        // because the two only line anything up while they agree.
        XElement row = TheRowTemplate();

        XElement inside = Assert.Single(row.Descendants(Xaml + "ItemsPresenter"));
        Assert.Equal("19,0,0,0", (string?)inside.Attribute("Margin"));

        XElement[] columns = [.. row.Descendants(Xaml + "ColumnDefinition")];
        Assert.Equal("19", (string?)columns[0].Attribute("Width"));
    }

    [Fact]
    public void The_container_style_builds_on_the_rows_own_look()
    {
        // The tree gives its rows a container style for the automation name
        // and which rows open. A container style with no BasedOn replaces
        // the implicit style wholesale, and the rows would go back to the
        // system's brushes while every other control wears the palette,
        // with nothing on the window saying why.
        XElement container = Assert.Single(
            Window().Descendants(Xaml + "TreeView.ItemContainerStyle").Elements(Xaml + "Style"));

        Assert.Equal(
            "{StaticResource {x:Type TreeViewItem}}",
            (string?)container.Attribute("BasedOn"));
    }

    /// <summary>The template the list's rows wear, from Look.xaml.</summary>
    private static XElement TheRowTemplate()
    {
        XElement style = Assert.Single(
            XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!
                .Elements(Xaml + "Style"),
            each => (string?)each.Attribute("TargetType") == "TreeViewItem");

        return Assert.Single(
            style.Descendants(Xaml + "ControlTemplate"),
            template => (string?)template.Attribute("TargetType") == "TreeViewItem");
    }

    [Fact]
    public void The_app_dresses_for_the_system_and_hears_it_change()
    {
        // The mechanism in one place, held there: the system is asked once at
        // the start and again every time it announces a change, the dark
        // brushes go on and come off as one dictionary, and the title bar,
        // which no brush of ours can reach, is painted through dwmapi for
        // every window as it loads, dialogs included.
        string wearing = File.ReadAllText(
            Repository.File("src", "DiscWright.App", "WearingTheTheme.cs"));
        string app = File.ReadAllText(
            Repository.File("src", "DiscWright.App", "App.xaml.cs"));

        Assert.Contains("WhatTheSystemWears.Dark()", wearing, StringComparison.Ordinal);
        Assert.Contains("UserPreferenceChanged", wearing, StringComparison.Ordinal);
        Assert.Contains("LookInTheDark.xaml", wearing, StringComparison.Ordinal);
        Assert.Contains("RegisterClassHandler", wearing, StringComparison.Ordinal);
        Assert.Contains("PaintingTheTitleBar", wearing, StringComparison.Ordinal);
        Assert.Contains("WearingTheTheme.Follow(this, look)", app, StringComparison.Ordinal);

        // Whose decision the dark is: the choice's, every time, so a chosen
        // dark survives a Windows that is light, and the person's change of
        // mind lands exactly like the system's.
        Assert.Contains("choice.WearsTheDark(WhatTheSystemWears.Dark())", wearing, StringComparison.Ordinal);
        Assert.Contains("choice.PropertyChanged", wearing, StringComparison.Ordinal);

        // And the choice is read back before any window exists, from the one
        // note the program keeps, so the first thing on screen is already
        // dressed the way it was left.
        Assert.Contains("ANoteAboutTheLook.ForThisUser()", app, StringComparison.Ordinal);
        Assert.Contains("keptLook.Recalled(), keptLook.Keep", app, StringComparison.Ordinal);
    }

    [Fact]
    public void The_appearance_is_a_quiet_press_in_the_programs_own_corner()
    {
        // About the program rather than about the disc being made, so it
        // lives where the program talks about itself, under the version and
        // the update link, and reads its words from ChoosingTheLook rather
        // than from the screen.
        XElement dresses = Assert.Single(
            Window().Descendants(Xaml + "Button"), each => IdOf(each) == "Appearance");

        Assert.Equal("{StaticResource Quietly}", (string?)dresses.Attribute("Style"));
        Assert.Equal("{Binding InWords}", (string?)dresses.Attribute("Content"));
        Assert.Equal("ChooseTheNextLook", (string?)dresses.Attribute("Click"));
        Assert.NotNull(dresses.Attribute("ToolTip"));
    }

    [Fact]
    public void The_list_buttons_sit_above_the_list_they_act_on()
    {
        // Under the list they were the last thing in reading order and the
        // first thing a new window needs: on an empty window the one thing
        // to do sat below a large blank box. Above the panel rather than
        // inside it, because a button drawn inside is covered by the tree
        // and cannot be clicked, which is recorded beside the empty-list
        // words in the markup.
        XElement adding = Assert.Single(
            Window().Descendants(Xaml + "Button"), each => IdOf(each) == "AddAGame");

        // The three that change the list stand together in a panel, and the
        // row carrying them is docked over the list. The row is a grid now
        // rather than the panel itself, because the preview button sits at
        // its far end.
        XElement group = adding.Parent!;
        XElement row = group.Parent!;

        Assert.Equal("StackPanel", group.Name.LocalName);
        Assert.Equal("Grid", row.Name.LocalName);
        Assert.Equal("Top", (string?)row.Attribute("DockPanel.Dock"));
    }

    [Fact]
    public void No_trigger_inside_a_template_is_beaten_by_the_style_holding_it()
    {
        // A Setter inside ControlTemplate.Triggers with no TargetName on it
        // loses to the style's own Setter for the same property. It compiles,
        // it reads exactly as though it works, and it does nothing at all.
        //
        // Ordinary set Foreground to the ink colour and then greyed it on
        // IsEnabled=False from inside the template, so Remove and Burn to a
        // disc were disabled and still drew their words in #16191C, the same
        // ink as the live button beside them. Measured off a photograph of
        // the window, because nothing else could tell the two apart. The
        // accent button had the same hole in it for its cursor.
        XElement look = XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!;

        List<string> dead = [];

        foreach (XElement style in look.Descendants(Xaml + "Style"))
        {
            string[] setByTheStyle =
            [
                .. style.Elements(Xaml + "Setter")
                    .Select(setter => (string?)setter.Attribute("Property"))
                    .Where(property => property is not null)
                    .Select(property => property!)
            ];

            IEnumerable<XElement> inTheTemplate = style
                .Descendants(Xaml + "ControlTemplate")
                .Elements(Xaml + "ControlTemplate.Triggers")
                .Descendants(Xaml + "Setter");

            foreach (XElement setter in inTheTemplate)
            {
                if (setter.Attribute("TargetName") is not null)
                {
                    continue;
                }

                string? property = (string?)setter.Attribute("Property");

                if (property is not null && setByTheStyle.Contains(property, StringComparer.Ordinal))
                {
                    dead.Add($"{(string?)style.Attribute(Named + "Key")} sets {property} itself "
                        + "and then tries to change it from inside its own template");
                }
            }
        }

        Assert.True(dead.Count == 0, "Markup that reads as though it works and does nothing: "
            + string.Join("; ", dead));
    }

    [Fact]
    public void A_button_that_can_be_disabled_looks_it()
    {
        // The guard above closes one half of this, a setter that cannot take
        // effect. This closes the other, which is no setter at all. A trigger
        // deleted outright leaves perfectly valid markup and a button that
        // goes dead without looking any different, and that is the fault
        // itself rather than the shape it happened to arrive in. A mutation
        // run found this hole by walking straight through it.
        //
        // Either place will do. Ordinary greys from its own Style.Triggers,
        // where a trigger beats the style's own setter. TheButton greys from
        // inside its template, which takes because the setter names the
        // Border it paints. Neither place is what this refuses.
        XElement look = XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!;

        Dictionary<string, XElement> styles = look
            .Descendants(Xaml + "Style")
            .Where(style => style.Attribute(Named + "Key") is not null)
            .ToDictionary(style => (string)style.Attribute(Named + "Key")!);

        List<string> silent = [];

        foreach (string file in Directory.GetFiles(Repository.File("src", "DiscWright.App"), "*.xaml"))
        {
            foreach (XElement button in XDocument.Load(file).Descendants(Xaml + "Button"))
            {
                if (button.Attribute("IsEnabled") is null
                    || StyleWornBy(button) is not string wearing
                    || !styles.TryGetValue(wearing, out XElement? style)
                    || WithWhatItBuildsOn(style, styles).Any(ChangesWhenDisabled))
                {
                    continue;
                }

                silent.Add($"{IdOf(button) ?? "a button"}, wearing {wearing}");
            }
        }

        Assert.True(silent.Count == 0, "Buttons that can be dead and never look it: "
            + string.Join("; ", silent.Distinct(StringComparer.Ordinal)));
    }

    [Fact]
    public void A_buttons_own_colour_can_reach_its_own_words()
    {
        // The third guard on the same fault, and the one that matters most,
        // because the other two were satisfied by markup that did nothing.
        //
        // This file declares an implicit TextBlock style that sets
        // Foreground. A bare ContentPresenter makes its own TextBlock for
        // string content, that TextBlock takes the implicit style, and a
        // style setter beats an inherited value. So the button's Foreground
        // never arrives: the greying was correct, in the right place, and
        // invisible.
        //
        // Proved by painting Ordinary bright red and photographing the
        // window. Not one pixel changed. Quietly had always drawn its own
        // TextBlock and was the only button whose colour ever worked.
        XElement look = XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!;

        bool textIsColouredForUs = look
            .Elements(Xaml + "Style")
            .Where(style => (string?)style.Attribute("TargetType") == "TextBlock"
                && style.Attribute(Named + "Key") is null)
            .SelectMany(style => style.Elements(Xaml + "Setter"))
            .Any(setter => (string?)setter.Attribute("Property") == "Foreground");

        if (!textIsColouredForUs)
        {
            // No implicit style, no clash, and a ContentPresenter is then
            // the ordinary thing to write.
            return;
        }

        List<string> unreachable = [];

        foreach (XElement style in look.Elements(Xaml + "Style"))
        {
            if ((string?)style.Attribute("TargetType") != "Button")
            {
                continue;
            }

            string named = (string?)style.Attribute(Named + "Key") ?? "a button style";

            foreach (XElement template in style.Descendants(Xaml + "ControlTemplate"))
            {
                if (template.Descendants(Xaml + "ContentPresenter").Any())
                {
                    unreachable.Add($"{named} shows its words with a ContentPresenter");
                }
            }
        }

        Assert.True(unreachable.Count == 0,
            "An implicit TextBlock style sets Foreground, so these cannot colour their own text: "
                + string.Join("; ", unreachable.Distinct(StringComparer.Ordinal)));
    }

    [Fact]
    public void Nothing_wears_the_accent_colour_when_it_cannot_be_pressed()
    {
        // The accent is spoken for. Look.xaml says so in as many words:
        // there is exactly one button on the window wearing it, and it is
        // the one that makes the thing. A button painted press-me and
        // unable to be pressed is the same lie as a black word on a dead
        // button, wearing different clothes.
        //
        // Needed as well as the guard above, not instead of it. Giving the
        // accent button a readable colour for its words satisfied that one
        // on its own, and the background greying could then be deleted
        // without a single test objecting. A mutant walked through the gap
        // the same afternoon it was opened.
        XElement look = XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!;

        List<string> stillAccented = [];

        foreach (XElement style in look.Elements(Xaml + "Style"))
        {
            bool wearsIt = style.Elements(Xaml + "Setter").Any(setter =>
                (string?)setter.Attribute("Property") == "Background"
                && (string?)setter.Attribute("Value")
                    is "{StaticResource Accent}" or "{DynamicResource Accent}");

            if (!wearsIt || TakesOffItsBackground(style))
            {
                continue;
            }

            stillAccented.Add((string?)style.Attribute(Named + "Key") ?? "a style");
        }

        Assert.True(stillAccented.Count == 0,
            "Still painted press-me while disabled: " + string.Join("; ", stillAccented));
    }

    /// <summary>
    /// Whether a style stops painting its own background when the control
    /// is disabled, from a place where the change actually takes.
    /// </summary>
    private static bool TakesOffItsBackground(XElement style) =>
        WhenDisabled(style.Elements(Xaml + "Style.Triggers"))
            .SelectMany(trigger => trigger.Elements(Xaml + "Setter"))
            .Any(setter => (string?)setter.Attribute("Property") == "Background")
        || WhenDisabled(style
                .Descendants(Xaml + "ControlTemplate")
                .Elements(Xaml + "ControlTemplate.Triggers"))
            .SelectMany(trigger => trigger.Elements(Xaml + "Setter"))
            .Any(setter => (string?)setter.Attribute("Property") == "Background"
                && setter.Attribute("TargetName") is not null);

    [Theory]
    [InlineData("Look.xaml", true)]
    [InlineData("LookInTheDark.xaml", false)]
    public void A_dead_button_still_has_words_on_it(string wearing, bool wordsAreDarker)
    {
        // Not a contrast threshold. Disabled controls are low contrast on
        // purpose, and Ordinary greyed on white is about 1.8 to 1, which is
        // exactly what greyed out is supposed to look like.
        //
        // The rule is the one each look is built on. In the light, words are
        // darker than the paper they sit on; in the dark it is the paper that
        // recedes, so the rule flips with the look and both wearings are
        // judged. Once the accent button could colour its own text at all,
        // its disabled state put White on the dead grey and the label all but
        // vanished. Nothing had ever objected, because for years the colour
        // never arrived and the label came out in full ink by accident.
        XElement look = XDocument.Load(Repository.File("src", "DiscWright.App", "Look.xaml")).Root!;

        Dictionary<string, string> palette = PaletteWearing(wearing);

        List<string> vanished = [];

        foreach (XElement style in look.Elements(Xaml + "Style"))
        {
            if ((string?)style.Attribute("TargetType") != "Button")
            {
                continue;
            }

            string? ink = AColour(SetterFor(style, "Foreground"), palette);
            string? paper = AColour(SetterFor(style, "Background"), palette);

            foreach (XElement trigger in WhenDisabled(style.Elements(Xaml + "Style.Triggers")))
            {
                ink = AColour(SetterFor(trigger, "Foreground"), palette) ?? ink;
                paper = AColour(SetterFor(trigger, "Background"), palette) ?? paper;
            }

            foreach (XElement trigger in WhenDisabled(style
                .Descendants(Xaml + "ControlTemplate")
                .Elements(Xaml + "ControlTemplate.Triggers")))
            {
                // Only a named setter reaches the Border it paints.
                paper = AColour(trigger.Elements(Xaml + "Setter")
                    .Where(setter => setter.Attribute("TargetName") is not null
                        && (string?)setter.Attribute("Property") == "Background")
                    .Select(setter => (string?)setter.Attribute("Value"))
                    .FirstOrDefault(), palette) ?? paper;
            }

            if (ink is null || paper is null)
            {
                // Transparent, or a colour nothing here can resolve. A guard
                // that cannot see should not shout.
                continue;
            }

            bool unreadable = wordsAreDarker
                ? Lightness(ink) >= Lightness(paper)
                : Lightness(ink) <= Lightness(paper);

            if (unreadable)
            {
                vanished.Add($"{(string?)style.Attribute(Named + "Key")} goes dead with {ink} "
                    + $"on {paper}, which does not read against what it sits on");
            }
        }

        Assert.True(vanished.Count == 0, $"Dead buttons nobody can read wearing {wearing}: "
            + string.Join("; ", vanished));
    }

    /// <summary>A hex colour for a markup value, or null when it is not one.</summary>
    private static string? AColour(string? value, Dictionary<string, string> palette)
    {
        if (value is null)
        {
            return null;
        }

        if (value.StartsWith('#') && value.Length == 7)
        {
            return value;
        }

        if (string.Equals(value, "White", StringComparison.Ordinal))
        {
            return "#FFFFFF";
        }

        // Both spellings, because every palette brush is asked for
        // dynamically now, and a helper that only knew the old one would
        // return null everywhere and quietly blind every guard that uses it.
        System.Text.RegularExpressions.Match found =
            System.Text.RegularExpressions.Regex.Match(value, @"^\{(?:Static|Dynamic)Resource (\w+)\}$");

        return found.Success && palette.TryGetValue(found.Groups[1].Value, out string? hex)
            ? hex
            : null;
    }

    /// <summary>How light a colour is, the way an eye weighs it.</summary>
    private static double Lightness(string hex)
    {
        double Channel(int at)
        {
            double v = Convert.ToInt32(hex.Substring(at, 2), 16) / 255.0;

            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(1)) + (0.7152 * Channel(3)) + (0.0722 * Channel(5));
    }

    /// <summary>What a style or a trigger sets a property to, if it does.</summary>
    private static string? SetterFor(XElement holder, string property) =>
        holder.Elements(Xaml + "Setter")
            .Where(setter => (string?)setter.Attribute("Property") == property)
            .Select(setter => (string?)setter.Attribute("Value"))
            .FirstOrDefault();

    /// <summary>The key of the style a control wears, when it names one.</summary>
    private static string? StyleWornBy(XElement control)
    {
        string worn = (string?)control.Attribute("Style") ?? string.Empty;

        if (worn.Length == 0)
        {
            // An inline style wears its parent through BasedOn, and a button
            // dressed that way is still a button this file's guards are
            // about: without this, moving a Style into an element quietly
            // took the button out from under A_button_that_can_be_disabled_looks_it.
            worn = control
                .Elements(control.Name.Namespace + (control.Name.LocalName + ".Style"))
                .Elements(Xaml + "Style")
                .Select(style => (string?)style.Attribute("BasedOn") ?? string.Empty)
                .FirstOrDefault() ?? string.Empty;
        }

        System.Text.RegularExpressions.Match found =
            System.Text.RegularExpressions.Regex.Match(worn, @"^\{StaticResource (\w+)\}$");

        return found.Success ? found.Groups[1].Value : null;
    }

    /// <summary>
    /// Whether this style changes something a person can see when the control
    /// is disabled, from a place where the change actually takes.
    /// </summary>
    /// <summary>
    /// The style and everything it builds on, nearest first. A style with
    /// no disabled look of its own can still look dead through the
    /// template it inherits, which is what AsALink does with Quietly's.
    /// </summary>
    private static IEnumerable<XElement> WithWhatItBuildsOn(
        XElement style, Dictionary<string, XElement> styles)
    {
        for (XElement? at = style; at is not null;)
        {
            yield return at;

            string basedOn = (string?)at.Attribute("BasedOn") ?? string.Empty;
            at = basedOn.StartsWith("{StaticResource ", StringComparison.Ordinal)
                && styles.TryGetValue(
                    basedOn["{StaticResource ".Length..].TrimEnd('}', ' '), out XElement? up)
                ? up
                : null;
        }
    }

    private static bool ChangesWhenDisabled(XElement style) =>
        WhenDisabled(style.Elements(Xaml + "Style.Triggers"))
            .Any(trigger => Visibly(trigger).Any())
        || WhenDisabled(style
                .Descendants(Xaml + "ControlTemplate")
                .Elements(Xaml + "ControlTemplate.Triggers"))
            .Any(trigger => Visibly(trigger).Any()
                && Visibly(trigger).All(setter => setter.Attribute("TargetName") is not null));

    /// <summary>
    /// The setters in a trigger that change something a person can see
    /// without first moving the mouse onto it.
    /// </summary>
    /// <remarks>
    /// The cursor does not count. A button that goes dead and answers only
    /// by changing the pointer still looks exactly like one that works, and
    /// nobody puts the pointer on a button to find out whether it is worth
    /// putting the pointer on.
    /// </remarks>
    private static IEnumerable<XElement> Visibly(XElement trigger) =>
        trigger.Elements(Xaml + "Setter")
            .Where(setter => (string?)setter.Attribute("Property") != "Cursor");

    private static IEnumerable<XElement> WhenDisabled(IEnumerable<XElement> holders) =>
        holders.Elements(Xaml + "Trigger").Where(trigger =>
            (string?)trigger.Attribute("Property") == "IsEnabled"
            && (string?)trigger.Attribute("Value") == "False");

    private static readonly XNamespace Named = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string? IdOf(XElement element) =>
        (string?)element.Attribute("AutomationProperties.AutomationId");

    /// <summary>
    /// Whether a window test could get hold of this control.
    /// </summary>
    /// <remarks>
    /// An id normally, because there is one of each control. Inside a row
    /// template there is one PER ROW, so a single id would name every row the
    /// same thing and finding one of them would be luck. Such a control earns
    /// its place with a name bound to the row instead.
    /// </remarks>
    internal static bool CanBeFound(XElement element)
    {
        if (IdOf(element) is not null)
        {
            return true;
        }

        bool inARow = element.Ancestors().Any(
            up => up.Name.LocalName is "DataTemplate" or "HierarchicalDataTemplate");

        return inARow && (string?)element.Attribute("AutomationProperties.Name") is not null;
    }

    private static string Describe(XElement element)
    {
        string what = element.Name.LocalName;
        string? called = (string?)element.Attribute("Content") ?? (string?)element.Attribute("Header");

        return called is null ? what : $"{what} \"{called}\"";
    }
}
