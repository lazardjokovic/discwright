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

    [Fact]
    public void Every_control_somebody_uses_can_be_found_by_a_test()
    {
        string[] unreachable =
        [
            .. Window()
                .Descendants()
                .Where(element => Interactive.Contains(element.Name.LocalName))
                .Where(element => IdOf(element) is null)
                .Select(Describe)
        ];

        Assert.True(
            unreachable.Length == 0,
            "Every control somebody uses needs an AutomationId, or no window test can reach it. "
                + "Missing on: " + string.Join(", ", unreachable));
    }

    [Fact]
    public void No_two_controls_answer_to_the_same_name()
    {
        string[] twice =
        [
            .. Window()
                .Descendants()
                .Select(IdOf)
                .Where(id => id is not null)
                .GroupBy(id => id!, StringComparer.Ordinal)
                .Where(same => same.Count() > 1)
                .Select(same => same.Key)
        ];

        // Two controls with one id is worse than a missing id, because a test
        // finds the first one, passes, and says nothing about the other.
        Assert.True(twice.Length == 0, "Used more than once: " + string.Join(", ", twice));
    }

    [Fact]
    public void Is_the_size_the_design_was_drawn_at()
    {
        XElement window = Window();

        // The design was drawn at exactly this size and the window does not
        // resize, so every position in it was chosen against these numbers.
        Assert.Equal("920", (string?)window.Attribute("Width"));
        Assert.Equal("620", (string?)window.Attribute("Height"));
        Assert.Equal("NoResize", (string?)window.Attribute("ResizeMode"));
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
    public void Says_every_colour_by_name_rather_than_in_figures()
    {
        string[] literal =
        [
            .. Window()
                .Descendants()
                .SelectMany(element => element.Attributes())
                .Where(attribute => ((string?)attribute.Value)?.StartsWith('#') == true)
                .Select(attribute => Describe(attribute.Parent!) + "." + attribute.Name.LocalName)
        ];

        // A colour typed into the window is a colour that will not change when
        // the palette does, and the palette is the whole design.
        Assert.True(literal.Length == 0, "Colour written out in full on: " + string.Join(", ", literal));
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

    private static XElement Window() =>
        XDocument.Load(Repository.File("src", "DiscWright.App", "MainWindow.xaml")).Root!;

    private static string? IdOf(XElement element) =>
        (string?)element.Attribute("AutomationProperties.AutomationId");

    private static string Describe(XElement element)
    {
        string what = element.Name.LocalName;
        string? called = (string?)element.Attribute("Content") ?? (string?)element.Attribute("Header");

        return called is null ? what : $"{what} \"{called}\"";
    }
}
