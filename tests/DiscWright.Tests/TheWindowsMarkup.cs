using System.Xml.Linq;

namespace DiscWright.Tests;

/// <summary>
/// The window kept drivable, guarded rather than remembered.
/// </summary>
/// <remarks>
/// <para>
/// These read <c>MainWindow.xaml</c> as XML from the source tree rather than
/// loading the built window, for two reasons. Nothing may reference the window
/// project, including this; and Smart App Control on the machine this is written
/// on refuses to load our own unsigned assemblies, so a test that instantiated
/// a WPF window would pass on the test VM and fail here.
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

    /// <summary>
    /// The controls somebody uses, as opposed to the ones that only arrange
    /// things. A Grid has no business carrying an id; a Button does.
    /// </summary>
    private static readonly string[] Interactive =
        ["Button", "TextBox", "CheckBox", "ComboBox", "ListBox", "RadioButton", "Expander", "Slider", "PasswordBox"];

    [Theory]
    [MemberData(nameof(EveryWindow))]
    public void Every_control_somebody_uses_can_be_found_by_a_test(string window)
    {
        string[] unreachable =
        [
            .. Markup(window)
                .Descendants()
                .Where(element => Interactive.Contains(element.Name.LocalName))
                .Where(element => IdOf(element) is null)
                .Select(Describe)
        ];

        Assert.True(
            unreachable.Length == 0,
            "Every control somebody uses needs an AutomationId, or no window test can reach it. "
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
                .Where(element => element.Name.LocalName is "ListBox" or "ComboBox")
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

    // ------------------------------------------------------------------ helpers

    private static XElement Window() => Markup("MainWindow.xaml");

    private static XElement Markup(string window) =>
        XDocument.Load(Repository.File("src", "DiscWright.App", window)).Root!;

    private static string? IdOf(XElement element) =>
        (string?)element.Attribute("AutomationProperties.AutomationId");

    private static string Describe(XElement element)
    {
        string what = element.Name.LocalName;
        string? called = (string?)element.Attribute("Content") ?? (string?)element.Attribute("Header");

        return called is null ? what : $"{what} \"{called}\"";
    }
}
