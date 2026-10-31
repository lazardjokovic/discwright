using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DiscWright.Tests;

/// <summary>
/// The list of things to try by hand, held against the windows it is about.
/// </summary>
/// <remarks>
/// <para>
/// <c>tests/by-hand/test-matrix.html</c> is the pass somebody works through
/// with the real program in front of them, and a list like that is worth
/// nothing the moment it stops matching the app. Keeping it current by
/// remembering to is the part that does not happen.
/// </para>
/// <para>
/// So every interactive control that carries an AutomationId has to appear in
/// it, named by that id in the row that covers it. Adding a button to a window
/// and not saying when it is live now breaks the build, which is the only
/// mechanism that has ever kept a document up to date.
/// </para>
/// </remarks>
public class TheTestMatrix
{
    private static readonly XNamespace Xaml =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>
    /// Things a person operates. A TextBlock carrying an id is something to
    /// read, and the matrix covers those through the scenarios rather than
    /// through the list of controls.
    /// </summary>
    private static readonly string[] Operated =
    [
        "Button", "CheckBox", "ComboBox", "TextBox", "TreeView",
        "ListBox", "Expander", "RadioButton", "Slider", "ToggleButton"
    ];

    [Fact]
    public void Says_when_every_control_in_every_window_is_live()
    {
        string matrix = File.ReadAllText(Repository.File("tests", "by-hand", "test-matrix.html"));

        List<string> unsaid = [];
        HashSet<string> kinds = [];
        int held = 0;

        foreach (string file in Directory.GetFiles(Repository.File("src", "DiscWright.App"), "*.xaml"))
        {
            foreach (XElement control in XDocument.Load(file).Descendants())
            {
                if (!Operated.Contains(control.Name.LocalName) || IdOf(control) is not string id)
                {
                    continue;
                }

                // A row inside a template takes its id from the thing it is
                // showing, so there is no fixed name for the matrix to carry.
                // Those two are covered as a kind rather than one by one.
                if (id.StartsWith('{'))
                {
                    continue;
                }

                // Named inside a data-controls attribute, which is how a row
                // of the matrix says which controls it is about. Looked for
                // with its quotes or spaces around it, so OpenADisc is not
                // satisfied by OpenADiscSomethingElse.
                held++;
                kinds.Add(control.Name.LocalName);

                if (!Mentions(matrix, id))
                {
                    unsaid.Add($"{Path.GetFileName(file)}: {control.Name.LocalName} {id}");
                }
            }
        }

        Assert.True(
            unsaid.Count == 0,
            "These are in a window and not in tests/by-hand/test-matrix.html, so nobody testing by hand "
                + "is told when they are live: " + string.Join("; ", unsaid.Distinct(StringComparer.Ordinal)));

        // How far this reached, because a guard can be made weaker without
        // being made wrong. Cutting the list of kinds down to buttons alone
        // leaves every assertion above passing and quietly stops holding the
        // boxes and the dropdowns to anything, which is what a mutation run
        // walked straight through.
        //
        // Seven kinds and 46 of them today. Both figures go up when a window
        // gains something, and changing them is the moment to look at the
        // matrix rather than a number to bump.
        //
        // It was 47 until the Add a game button inside the empty list came
        // out, which is the other direction and worth saying: a window can
        // lose a control on purpose, and then this number comes down with it
        // rather than the guard being left loose.
        Assert.True(
            held >= 46 && kinds.Count >= 7,
            $"This held {held} controls of {kinds.Count} kinds to the matrix, which is fewer than it did. "
                + "Either a window has lost controls, or this guard has stopped looking at some of them.");
    }

    [Fact]
    public void Names_no_control_that_is_not_there_any_more()
    {
        // The other way round, because a row about a button somebody deleted
        // sends a person looking for something that does not exist.
        HashSet<string> real =
        [
            .. Directory.GetFiles(Repository.File("src", "DiscWright.App"), "*.xaml")
                .SelectMany(file => XDocument.Load(file).Descendants())
                .Select(IdOf)
                .OfType<string>()
        ];

        string matrix = File.ReadAllText(Repository.File("tests", "by-hand", "test-matrix.html"));

        List<string> gone =
        [
            .. System.Text.RegularExpressions.Regex
                .Matches(matrix, "data-controls=\"([^\"]+)\"")
                .SelectMany(found => found.Groups[1].Value.Split(
                    ' ', StringSplitOptions.RemoveEmptyEntries))
                .Distinct(StringComparer.Ordinal)
                .Where(named => !real.Contains(named))
        ];

        Assert.True(
            gone.Count == 0,
            "The matrix names controls no window has: " + string.Join(", ", gone));
    }

    [Fact]
    public void Is_a_page_a_browser_will_open_on_its_own()
    {
        // It lives in the repository as a file somebody double clicks, not
        // inside anything that wraps it, so it carries its own skeleton.
        string matrix = File.ReadAllText(Repository.File("tests", "by-hand", "test-matrix.html"));

        Assert.StartsWith("<!doctype html>", matrix, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<meta charset=\"utf-8\">", matrix, StringComparison.Ordinal);
        Assert.Contains("<title>", matrix, StringComparison.Ordinal);
        Assert.Contains("</html>", matrix, StringComparison.Ordinal);
    }

    [Fact]
    public void Lists_every_file_in_the_folder_it_describes()
    {
        // The heading said "The three files here" above a table of four, in a
        // folder holding seven besides the README, and three of them went
        // undescribed for as long as they have existed. Nothing read that
        // document, so nothing minded.
        // A row of its own, not a mention. The first version of this asked
        // whether the name appeared anywhere in the file, and a planted fault
        // walked through it: the row describing a-disc.pict was deleted and
        // the name was still there, inside the row about the script that
        // reads it.
        string[] rows =
        [
            .. File
                .ReadAllLines(Repository.File("tests", "by-hand", "README.md"))
                .Select(line => line.TrimStart())
                .Where(line => line.StartsWith("| `", StringComparison.Ordinal)),
        ];

        string[] missing =
        [
            .. Directory
                .GetFiles(Repository.File("tests", "by-hand"))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(name => !name.Equals("README.md", StringComparison.OrdinalIgnoreCase))
                .Where(name => !rows.Any(row => row.StartsWith("| `" + name + "`", StringComparison.Ordinal)))
                .Order(),
        ];

        Assert.True(
            missing.Length == 0,
            "tests/by-hand/README.md has no row of its own for: " + string.Join(", ", missing));
    }

    [Fact]
    public void Counts_the_cases_that_need_a_disc_the_same_way_twice()
    {
        // Its own title said seven above eight of them, and the README beside
        // it agreed with the title rather than with the list. A number written
        // in words in two places and counted in neither is going to be wrong.
        string[] lines = File.ReadAllLines(Repository.File("tests", "by-hand", "WITH-A-REAL-DISC.md"));

        int cases = lines.Count(line => Regex.IsMatch(line, @"^## [0-9]+\. "));

        string[] words =
        [
            "zero", "one", "two", "three", "four", "five", "six", "seven",
            "eight", "nine", "ten", "eleven", "twelve",
        ];

        Assert.True(cases > 0 && cases < words.Length, $"Counted {cases} cases, which this test cannot put in words.");

        string said = words[cases];

        Assert.True(
            lines[0].Contains(said, StringComparison.OrdinalIgnoreCase),
            $"WITH-A-REAL-DISC.md holds {cases} cases and its title says: {lines[0]}");

        string readme = File.ReadAllText(Repository.File("tests", "by-hand", "README.md"));

        Assert.True(
            readme.Contains("the " + said + " that need a disc", StringComparison.OrdinalIgnoreCase),
            $"There are {cases} cases needing a disc, and tests/by-hand/README.md does not say \"the {said}\".");
    }

    private static bool Mentions(string matrix, string id) =>
        System.Text.RegularExpressions.Regex
            .Matches(matrix, "data-controls=\"([^\"]+)\"")
            .SelectMany(found => found.Groups[1].Value.Split(
                ' ', StringSplitOptions.RemoveEmptyEntries))
            .Any(named => string.Equals(named, id, StringComparison.Ordinal));

    /// <summary>
    /// The AutomationId on this element, or nothing.
    /// </summary>
    /// <remarks>
    /// The attribute has no prefix in the markup, so its local name is the
    /// whole of "AutomationProperties.AutomationId". Comparing against
    /// "AutomationId" matched nothing at all, and the guard above went green
    /// over every window in the program because it had nothing to check.
    /// </remarks>
    private static string? IdOf(XElement element) =>
        element.Attributes()
            .Where(each => each.Name.LocalName.EndsWith("AutomationId", StringComparison.Ordinal))
            .Select(each => each.Value)
            .FirstOrDefault();
}
