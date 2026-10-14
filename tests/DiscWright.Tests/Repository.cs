namespace DiscWright.Tests;

/// <summary>
/// Where the source lives, for the tests that read it rather than run it.
/// </summary>
/// <remarks>
/// <para>
/// Some tests read the source tree on purpose: the architecture tests read the
/// project files, because a reference that exists on paper is already a
/// dependency, and the window tests read the markup, because nothing may
/// reference the window project.
/// </para>
/// <para>
/// Here rather than in each of them because it was written twice and the second
/// copy repeated a bug the first one's comment warns about: looking for
/// <c>DiscWright.sln</c> when .NET 10 writes <c>DiscWright.slnx</c>.
/// </para>
/// </remarks>
internal static class Repository
{
    /// <summary>
    /// The folder the solution is in.
    /// </summary>
    /// <remarks>
    /// Walked up to rather than counted down from, because tests run out of
    /// bin, which is four folders deep and moves whenever the configuration or
    /// framework changes.
    /// </remarks>
    internal static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && SolutionFile(directory.FullName) is null)
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Found no DiscWright solution above " + AppContext.BaseDirectory);
    }

    /// <summary>A file in the source tree, by the parts of its path.</summary>
    internal static string File(params string[] parts) => Path.Combine([Root(), .. parts]);

    /// <summary>
    /// The solution in this folder, whichever format it is in, or null.
    /// </summary>
    /// <remarks>
    /// .NET 10's <c>dotnet new sln</c> writes the newer XML format,
    /// DiscWright.slnx, and the first test to look for one looked for the
    /// classic DiscWright.sln and failed on all five counts. Accepting either
    /// means a change of format is not a day of confusing failures later.
    /// </remarks>
    internal static string? SolutionFile(string folder)
    {
        foreach (string name in (string[])["DiscWright.slnx", "DiscWright.sln"])
        {
            string candidate = Path.Combine(folder, name);

            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
