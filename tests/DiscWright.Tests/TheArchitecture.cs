using System.Xml.Linq;

namespace DiscWright.Tests;

/// <summary>
/// The one rule, guarded rather than remembered: arrows point inward, and the
/// middle points at nothing.
/// </summary>
/// <remarks>
/// These read the project files rather than the compiled assemblies, on
/// purpose. <c>Assembly.GetReferencedAssemblies</c> lists only the references
/// the compiler actually needed, so a project could reference another, use
/// nothing from it yet, and pass. A dependency that exists on paper is already
/// a dependency, and it is the one somebody will use next week.
/// </remarks>
public class TheArchitecture
{
    /// <summary>
    /// What each project under src is allowed to reference. The window is not
    /// in here because it may reference everything; it has its own test below.
    /// </summary>
    private static readonly Dictionary<string, string[]> Allowed = new()
    {
        ["DiscWright.Core"] = [],
        // Staging may reach the Menu, and only Staging may. It is the one
        // place that assembles a whole disc, and a menu is one of the things on
        // a disc. The alternative was the builder living in Core and Core
        // reaching for Menu, which is the rule this whole file exists to stop.
        // Written down here because the test is where somebody will meet it.
        ["DiscWright.Staging"] = ["DiscWright.Core", "DiscWright.Menu"],
        ["DiscWright.Menu"] = ["DiscWright.Core"],
        ["DiscWright.Platform"] = ["DiscWright.Core"],
        ["DiscWright.Imaging"] = ["DiscWright.Core"],
        ["DiscWright.Burning"] = ["DiscWright.Core"],
        ["DiscWright.Printing"] = ["DiscWright.Core"]
    };

    [Fact]
    public void Core_references_nothing_of_ours()
    {
        string[] references = ReferencesOf(ProjectFile("DiscWright.Core"));

        Assert.True(
            references.Length == 0,
            "Core must reference nothing of ours, and now references: " + string.Join(", ", references));
    }

    [Fact]
    public void Core_is_not_tied_to_Windows()
    {
        string? framework = TargetFrameworkOf(ProjectFile("DiscWright.Core"));

        // Not merely a convention. On net10.0 the compiler cannot see a Windows
        // API at all, so the rule holds even when nobody is looking at the test.
        Assert.True(
            framework == "net10.0",
            $"Core must target net10.0 so a Windows API is out of its reach, and it targets {framework}.");
    }

    [Fact]
    public void Every_project_references_only_what_it_is_allowed_to()
    {
        List<string> wrong = [];

        foreach ((string project, string[] references) in ProjectsUnderSource())
        {
            if (!Allowed.TryGetValue(project, out string[]? allowed))
            {
                continue;
            }

            foreach (string reference in references.Except(allowed))
            {
                wrong.Add($"{project} -> {reference}");
            }
        }

        Assert.True(
            wrong.Count == 0,
            "These dependencies are not allowed by ARCHITECTURE.md: " + string.Join("; ", wrong));
    }

    [Fact]
    public void Nothing_references_the_window()
    {
        string[] offenders =
        [
            .. ProjectsUnderSource()
                .Where(project => project.References.Contains("DiscWright.App"))
                .Select(project => project.Project)
        ];

        Assert.True(
            offenders.Length == 0,
            "The window must be referenced by nothing, and is referenced by: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_project_in_the_tree_is_in_the_solution()
    {
        string? solutionFile = SolutionFile(RepositoryRoot());
        Assert.NotNull(solutionFile);

        string solution = File.ReadAllText(solutionFile);

        string[] missing =
        [
            .. Directory
                .EnumerateFiles(RepositoryRoot(), "DiscWright.*.csproj", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .Where(name => name is not null && !solution.Contains(name, StringComparison.OrdinalIgnoreCase))
                .Select(name => name!)
        ];

        // A project outside the solution still builds on this machine and is
        // invisible in Visual Studio and to dotnet test, which is the worst of
        // both: it compiles for whoever wrote it and nobody else runs it.
        Assert.True(
            missing.Length == 0,
            "These projects are not in DiscWright.sln: " + string.Join(", ", missing));
    }

    private static IEnumerable<(string Project, string[] References)> ProjectsUnderSource()
    {
        string source = Path.Combine(RepositoryRoot(), "src");

        foreach (string file in Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories))
        {
            yield return (Path.GetFileNameWithoutExtension(file), ReferencesOf(file));
        }
    }

    private static string ProjectFile(string project) =>
        Path.Combine(RepositoryRoot(), "src", project, project + ".csproj");

    private static string[] ReferencesOf(string projectFile) =>
    [
        .. XDocument
            .Load(projectFile)
            .Descendants("ProjectReference")
            .Select(reference => (string?)reference.Attribute("Include"))
            .Where(include => include is not null)
            .Select(include => Path.GetFileNameWithoutExtension(include!))
            .Order()
    ];

    private static string? TargetFrameworkOf(string projectFile) =>
        XDocument.Load(projectFile).Descendants("TargetFramework").FirstOrDefault()?.Value;

    /// <summary>
    /// Walks up from the test assembly until it finds the solution. The tests
    /// run out of bin, which is four folders deep and moves whenever the
    /// configuration or framework changes, so the path cannot be counted.
    /// </summary>
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && SolutionFile(directory.FullName) is null)
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Found no DiscWright solution above " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// The solution in this folder, whichever format it is in, or null.
    /// </summary>
    /// <remarks>
    /// .NET 10's <c>dotnet new sln</c> writes the newer XML format,
    /// DiscWright.slnx, and this test was first written looking for the classic
    /// DiscWright.sln and failed on all five counts. Accepting either means a
    /// change of format is not a day of confusing failures later.
    /// </remarks>
    private static string? SolutionFile(string folder)
    {
        foreach (string name in (string[])["DiscWright.slnx", "DiscWright.sln"])
        {
            string candidate = Path.Combine(folder, name);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
