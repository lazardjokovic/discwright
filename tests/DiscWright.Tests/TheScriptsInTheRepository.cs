using System.Text.RegularExpressions;

namespace DiscWright.Tests;

/// <summary>
/// The PowerShell scripts in this repository, held to working somewhere other
/// than one machine.
/// </summary>
/// <remarks>
/// <para>
/// Six of them named a folder under the owner's profile: where to put what they
/// produce, where the built app is, and which checkout to ask git about. They
/// worked perfectly, and only ever for one person. Two of them open with
/// <c>Remove-Item -Recurse -Force</c> on that path, which is a poor thing to
/// hand somebody whose profile is somewhere else.
/// </para>
/// <para>
/// It matters twice over because this repository is going public, and going
/// public publishes the whole history, so a path under a profile cannot be
/// taken back out of it afterwards.
/// </para>
/// <para>
/// Each of these reads the scripts rather than running them. Running one takes
/// the desktop for several minutes and some of them burn a disc, so the thing
/// being held here is what they say, which is where the fault was.
/// </para>
/// </remarks>
public class TheScriptsInTheRepository
{
    /// <summary>A lone backslash, built rather than typed.</summary>
    /// <remarks>
    /// It is the one character that does not survive being generated: it
    /// arrives halved, the file still parses, and nothing complains. The same
    /// note is on <c>TheReadme</c> and in CLAUDE.md.
    /// </remarks>
    private static readonly string Slash = ((char)92).ToString();

    [Fact]
    public void Name_no_folder_under_somebodys_profile()
    {
        string under = "C:" + Slash + "Users" + Slash;
        List<string> named = [];

        foreach (string script in EveryScript())
        {
            string[] lines = File.ReadAllLines(script);

            for (int at = 0; at < lines.Length; at++)
            {
                if (lines[at].Contains(under, StringComparison.OrdinalIgnoreCase))
                {
                    named.Add(Where(script) + ":" + (at + 1));
                }
            }
        }

        Assert.True(
            named.Count == 0,
            "These lines name a folder under somebody's profile, which exists on one machine only: "
                + string.Join(", ", named));
    }

    [Fact]
    public void Find_the_app_from_their_own_place()
    {
        // $PSScriptRoot is where a script is, which is the only thing it can
        // know without being told. The repository is its parent and the build
        // output is under that, so a checkout anywhere works. A line naming the
        // app and a drive letter has decided whose checkout it is instead.
        string[] guessing = [.. LinesNaming("DiscWright.App.exe").Where(Absolute)];

        Assert.True(
            guessing.Length == 0,
            "These name the built app at a fixed place rather than working it out from their own: "
                + string.Join(", ", guessing.Select(Said)));
    }

    [Fact]
    public void Write_what_they_make_into_the_lab_folder()
    {
        // Everything produced goes to DiscWright-Lab, never into OneDrive and
        // never into the repository. Which folder that is depends on whose
        // profile it is, so it is worked out rather than written down.
        string[] fixedUp = [.. LinesNaming("DiscWright-Lab").Where(Absolute)];

        Assert.True(
            fixedUp.Length == 0,
            "These write into a fixed DiscWright-Lab rather than the one belonging to whoever runs them: "
                + string.Join(", ", fixedUp.Select(Said)));
    }

    /// <summary>
    /// Every line of every script that mentions something, as "where	text".
    /// </summary>
    /// <remarks>
    /// Line by line rather than file by file, because the first version of
    /// these two asked whether the file worked anything out anywhere, and a
    /// planted fault walked straight through it: one hardcoded line in a script
    /// whose other defaults were derived. The guard was watched failing, which
    /// is the only reason that was known.
    /// </remarks>
    private static IEnumerable<string> LinesNaming(string what)
    {
        foreach (string script in EveryScript())
        {
            string[] lines = File.ReadAllLines(script);

            for (int at = 0; at < lines.Length; at++)
            {
                if (lines[at].Contains(what, StringComparison.Ordinal))
                {
                    yield return $"{Where(script)}:{at + 1}	{lines[at].Trim()}";
                }
            }
        }
    }

    /// <summary>Whether a line carries a path starting at a drive letter.</summary>
    /// <remarks>
    /// Three absolute paths in this repository are right to be absolute: the
    /// Windows Kits folder, and the two that name a folder inside the test VM.
    /// None of them names the app or the lab, so scoping the rule to those two
    /// leaves them alone.
    /// </remarks>
    private static bool Absolute(string line) =>
        Regex.IsMatch(line, "[A-Za-z]:" + Regex.Escape(Slash));

    /// <summary>Just the place, for a failure message.</summary>
    private static string Said(string line) => line.Split('	')[0];

    /// <summary>Every script in the source tree, build output left out.</summary>
    /// <remarks>
    /// <c>artifacts</c> holds test results that carry the full path of whoever
    /// ran them, and bin and obj are copies. None of them is source, and git
    /// ignores all three.
    /// </remarks>
    private static IEnumerable<string> EveryScript()
    {
        string[] notSource = ["bin", "obj", ".git", "artifacts"];

        return Directory
            .EnumerateFiles(Repository.Root(), "*.ps1", SearchOption.AllDirectories)
            .Where(script => !Where(script)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => notSource.Contains(part, StringComparer.OrdinalIgnoreCase)));
    }

    /// <summary>The path to say in a failure, relative to the repository.</summary>
    private static string Where(string script) => Path.GetRelativePath(Repository.Root(), script);

    private static string Read(string script) => File.ReadAllText(script);
}
