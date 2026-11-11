using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using Xunit.Sdk;

namespace DiscWright.Tests;

/// <summary>
/// The README held to the thing it describes.
/// </summary>
/// <remarks>
/// <para>
/// It had drifted a long way before this existed. It said 1153 tests while the
/// suite discovered 1456, it drew a dependency diagram with a whole project
/// missing from it, and it opened by saying nothing was finished about an app
/// that had burned real discs and installed a game off them. Nobody noticed,
/// because a document is the one part of a repository that nothing checks.
/// </para>
/// <para>
/// So every number and every project name in it is worked out here and
/// compared, the same way <c>TheTestMatrix</c> holds the pass by hand to the
/// windows it is about. A test added, a project renamed or a reference newly
/// declared now breaks the build until the README says so, which is the only
/// mechanism that has ever kept a document up to date.
/// </para>
/// <para>
/// The last two are about this repository going public. A README is the first
/// thing anybody reads, and going public publishes the whole history, so an
/// email address or a path under somebody's profile cannot be taken back out
/// of it afterwards.
/// </para>
/// </remarks>
public class TheReadme
{
    /// <summary>What separates a folder from a file, built rather than typed.</summary>
    /// <remarks>
    /// A lone backslash is the one character that does not survive being
    /// generated: it arrives halved, the file still parses, and nothing
    /// complains. CLAUDE.md has the whole trap.
    /// </remarks>
    private const char Separator = (char)92;

    /// <summary>The file for people working on the code, which the facts below are held to.</summary>
    private static string Readme() => File.ReadAllText(Repository.File("DEVELOPING.md"));

    /// <summary>Both files that go public, the one for people using the app and the one for people working on it.</summary>
    public static TheoryData<string> WhatGoesPublic => new() { "README.md", "DEVELOPING.md" };

    [Fact]
    public void Says_how_many_tests_there_are()
    {
        MatchCollection said = Regex.Matches(Readme(), "([0-9]+) tests");

        // Once, so two copies of the number cannot drift apart, and the one
        // copy is the one this compares.
        Assert.True(
            said.Count == 1,
            $"The README should say how many tests there are exactly once, and says it {said.Count} times.");

        int claimed = int.Parse(said[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        int real = EveryTestCase();

        Assert.True(
            claimed == real,
            $"The README says {claimed} tests and the suite is {real} of them.");
    }

    [Fact]
    public void Names_every_project_and_only_the_projects_there_are()
    {
        HashSet<string> real = [.. EveryProject().Keys];
        HashSet<string> said = [.. TheProjectTable().Keys];

        string[] missing = [.. real.Except(said).Order()];
        string[] gone = [.. said.Except(real).Order()];

        Assert.True(
            missing.Length == 0,
            "The README's table of projects does not mention: " + string.Join(", ", missing));

        // The other way round, because a row about a project somebody deleted
        // sends a reader looking for something that is not there.
        Assert.True(
            gone.Length == 0,
            "The README's table names projects that do not exist: " + string.Join(", ", gone));
    }

    [Fact]
    public void Says_what_each_project_really_references()
    {
        Dictionary<string, string[]> real = EveryProject();
        List<string> wrong = [];

        foreach ((string project, string[] said) in TheProjectTable())
        {
            if (!real.TryGetValue(project, out string[]? declared))
            {
                continue;
            }

            if (!said.Order().SequenceEqual(declared.Order(), StringComparer.Ordinal))
            {
                wrong.Add($"{project} declares [{string.Join(", ", declared.Order())}] "
                    + $"and the README says [{string.Join(", ", said.Order())}]");
            }
        }

        // The declared references rather than the compiled ones, for the same
        // reason TheArchitecture reads them: a reference that exists on paper
        // is already a dependency, and the compiler drops the ones nothing uses.
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    [Fact]
    public void Says_the_version_the_build_stamps()
    {
        Match said = Regex.Match(Readme(), "version is `([0-9]+\\.[0-9]+\\.[0-9]+)`");

        Assert.True(said.Success, "The README should say which version this is.");

        string? stamped = XDocument
            .Load(Repository.File("Directory.Build.props"))
            .Descendants("Version")
            .FirstOrDefault()
            ?.Value;

        Assert.True(
            said.Groups[1].Value == stamped,
            $"The README says version {said.Groups[1].Value} and Directory.Build.props says {stamped}.");
    }

    [Fact]
    public void Offers_the_files_the_release_really_attaches()
    {
        // The README is the download page as much as the release is, and the
        // names it gives are the ones Build-TheRelease.ps1 makes from the
        // version the build stamps. A README still offering last version's
        // file sends people looking for something that is not there.
        string readme = File.ReadAllText(Repository.File("README.md"));
        string? stamped = XDocument.Load(Repository.File("Directory.Build.props")).Descendants("Version").FirstOrDefault()?.Value;

        Assert.Contains($"DiscWright-{stamped}-setup.exe", readme, StringComparison.Ordinal);
        Assert.Contains($"DiscWright-{stamped}-portable.exe", readme, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"DiscWright-(?!" + Regex.Escape(stamped ?? string.Empty) + @"-)[0-9]+\.[0-9]+\.[0-9]+-(setup|portable)\.exe", readme);
    }

    [Fact]
    public void Says_how_many_cases_are_still_a_persons_job()
    {
        string matrix = File.ReadAllText(Repository.File("tests", "by-hand", "test-matrix.html"));

        int run = Regex.Count(matrix, "class=\"case run\"");
        int byHand = Regex.Count(matrix, "class=\"case\"");

        Match said = Regex.Match(
            Readme(),
            "([0-9]+) cases, ([0-9]+) of them run by the suite and ([0-9]+) still by hand");

        Assert.True(
            said.Success,
            "The README should say how many cases there are, how many the suite runs, and how many are left.");

        int[] claimed =
        [
            .. said.Groups.Values
                .Skip(1)
                .Select(group => int.Parse(group.Value, System.Globalization.CultureInfo.InvariantCulture))
        ];

        // A count of what is left to do is the number somebody plans an evening
        // around, so it is worth more than the others and goes stale fastest.
        Assert.True(
            claimed[0] == run + byHand && claimed[1] == run && claimed[2] == byHand,
            $"The README says {claimed[0]} cases, {claimed[1]} run and {claimed[2]} by hand, "
                + $"and the matrix holds {run + byHand} cases, {run} run and {byHand} by hand.");
    }

    [Theory]
    [MemberData(nameof(WhatGoesPublic))]
    public void Uses_no_dash_between_clauses(string file)
    {
        string readme = File.ReadAllText(Repository.File(file));

        // The owner's rule for anything written to be read by somebody else:
        // a comma, a full stop, or the sentence written again.
        Assert.True(
            !readme.Contains(" - ", StringComparison.Ordinal),
            "The README joins clauses with a spaced hyphen, which this project writes as a comma or a full stop.");

        Assert.True(
            !readme.Contains('—') && !readme.Contains('–'),
            "The README carries an em or en dash, which this project writes as a comma or a full stop.");
    }

    [Theory]
    [MemberData(nameof(WhatGoesPublic))]
    public void Carries_nothing_that_cannot_be_made_public(string file)
    {
        string readme = File.ReadAllText(Repository.File(file));

        Match address = Regex.Match(readme, "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+[.][A-Za-z]{2,}");

        Assert.True(
            !address.Success,
            "The README carries an email address, and going public publishes it: " + address.Value);

        Assert.True(
            !readme.Contains("C:" + Separator + "Users", StringComparison.OrdinalIgnoreCase),
            "The README carries a path under somebody's profile, which says who built this and where.");

        foreach (Match carried in Regex.Matches(readme, "PASSWORD[^=]*=\\s*'([^']*)'"))
        {
            // The runner needs the variable named, and naming it is not the
            // same as writing the password down beside it.
            Assert.True(
                carried.Groups[1].Value == "...",
                "The README shows a password rather than a placeholder: " + carried.Value);
        }
    }

    /// <summary>
    /// Every test case in this assembly, counted the way the runner counts
    /// them: a fact is one, and a theory is as many as its data rows.
    /// </summary>
    /// <remarks>
    /// Through <see cref="DataAttribute.GetData"/> rather than by counting
    /// <c>InlineData</c> attributes, because two theories here take their rows
    /// from a member instead, one of them the forty PICT rows, and counting
    /// attributes would miss sixty five cases and call the README wrong.
    /// </remarks>
    private static int EveryTestCase()
    {
        int cases = 0;

        foreach (Type type in typeof(TheReadme).Assembly.GetExportedTypes())
        {
            foreach (MethodInfo method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttribute<FactAttribute>() is null)
                {
                    continue;
                }

                DataAttribute[] rows = [.. method.GetCustomAttributes<DataAttribute>()];

                cases += rows.Length == 0 ? 1 : rows.Sum(each => each.GetData(method).Count());
            }
        }

        return cases;
    }

    /// <summary>Every project in the tree, and what its file declares it references.</summary>
    private static Dictionary<string, string[]> EveryProject()
    {
        Dictionary<string, string[]> projects = [];

        foreach (string file in Directory.EnumerateFiles(
            Repository.Root(), "*.csproj", SearchOption.AllDirectories))
        {
            projects[Path.GetFileNameWithoutExtension(file)] =
            [
                .. XDocument
                    .Load(file)
                    .Descendants("ProjectReference")
                    .Select(reference => (string?)reference.Attribute("Include"))
                    .OfType<string>()
                    .Select(Path.GetFileNameWithoutExtension)
                    .OfType<string>()
            ];
        }

        return projects;
    }

    /// <summary>
    /// The README's own table, read as what it claims: a project per row, and
    /// the projects it says that one references.
    /// </summary>
    private static Dictionary<string, string[]> TheProjectTable()
    {
        Dictionary<string, string[]> said = [];

        foreach (string line in File.ReadAllLines(Repository.File("DEVELOPING.md")))
        {
            Match row = Regex.Match(line, "^[|][^`]*`(DiscWright[.][A-Za-z]+)`[^|]*[|]([^|]*)[|]([^|]*)[|]");

            if (!row.Success)
            {
                continue;
            }

            said[row.Groups[1].Value] =
            [
                .. Regex
                    .Matches(row.Groups[3].Value, "`([A-Za-z.]+)`")
                    .Select(found => found.Groups[1].Value)
                    .Select(name => name.StartsWith("DiscWright.", StringComparison.Ordinal)
                        ? name
                        : "DiscWright." + name)
            ];
        }

        return said;
    }
}
