using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DiscWright.Tests;

/// <summary>
/// The release workflow and the script it runs.
/// </summary>
/// <remarks>
/// <para>
/// A release cannot be tried out: the first time the workflow really runs is
/// the day a tag is pushed, and whatever it gets wrong is then public. So the
/// script's refusals are run here against this repository's real version, and
/// the workflow is read as text and held to the few things that would be
/// expensive to get wrong.
/// </para>
/// <para>
/// The script is run with Windows PowerShell, which every machine this suite
/// runs on has, the hosted runner included.
/// </para>
/// </remarks>
public class MakingARelease
{
    private static string Workflow() => File.ReadAllText(Repository.File(".github/workflows/release.yml"));

    private static string TheVersion() =>
        Regex.Match(File.ReadAllText(Repository.File("Directory.Build.props")), "<Version>([^<]+)</Version>").Groups[1].Value;

    // ------------------------------------------------------------ the script

    [Fact]
    public void A_tag_that_agrees_with_the_app_is_taken()
    {
        (int code, string said) = Run($"-Tag v{TheVersion()} -CheckOnly -WithoutNotes");

        Assert.True(code == 0, said);
        Assert.Contains("the tag and the app agree", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tag_that_disagrees_with_the_app_is_refused()
    {
        (int code, string said) = Run("-Tag v9.9.9 -CheckOnly -WithoutNotes");

        Assert.NotEqual(0, code);
        Assert.Contains("REFUSED", said, StringComparison.Ordinal);
        Assert.Contains($"Directory.Build.props says {TheVersion()}", said, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("v2.0.0-beta.1")]
    [InlineData("2.0.0")]
    [InlineData("v2.0")]
    public void A_tag_that_is_not_a_plain_version_is_refused(string tag)
    {
        (int code, string said) = Run($"-Tag {tag} -CheckOnly -WithoutNotes");

        // A pre-release above all: the app's update check reads v2.0.0-beta.1
        // as 2.0.0, so whoever ran it would never hear about the real one.
        Assert.NotEqual(0, code);
        Assert.Contains("is not vX.Y.Z", said, StringComparison.Ordinal);
    }

    [Fact]
    public void A_release_with_no_notes_is_refused_and_one_with_notes_is_taken()
    {
        string notes = Path.Combine(Path.GetTempPath(), "dw-notes-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(notes);

        try
        {
            string asked = $"-Tag v{TheVersion()} -CheckOnly -NotesFolder \"{notes}\"";

            (int none, string saidNone) = Run(asked);
            Assert.NotEqual(0, none);
            Assert.Contains("no release notes", saidNone, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(notes, TheVersion() + ".md"), "   \r\n");
            (int empty, string saidEmpty) = Run(asked);
            Assert.NotEqual(0, empty);
            Assert.Contains("are empty", saidEmpty, StringComparison.Ordinal);

            File.WriteAllText(Path.Combine(notes, TheVersion() + ".md"), "What is new.");
            (int written, string saidWritten) = Run(asked);
            Assert.True(written == 0, saidWritten);
        }
        finally
        {
            Directory.Delete(notes, recursive: true);
        }
    }

    // ---------------------------------------------------------- the workflow

    [Fact]
    public void Runs_on_a_version_tag_and_by_hand_and_on_nothing_else()
    {
        string on = Section(Workflow(), "on");

        Assert.Contains("tags: ['v*']", on, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", on, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request", on, StringComparison.Ordinal);
        Assert.DoesNotContain("branches", on, StringComparison.Ordinal);
    }

    [Fact]
    public void Makes_a_draft_named_after_the_version_carrying_all_three_files()
    {
        string draft = Step(Workflow(), "Draft the release");

        // A draft, so a person publishes it; only then is it "latest".
        Assert.Contains("--draft", draft, StringComparison.Ordinal);
        Assert.Contains("--title \"DiscWright ${{ steps.which.outputs.version }}\"", draft, StringComparison.Ordinal);
        Assert.Contains("-setup.exe", draft, StringComparison.Ordinal);
        Assert.Contains("-portable.exe", draft, StringComparison.Ordinal);
        Assert.Contains("SHA256SUMS.txt", draft, StringComparison.Ordinal);
        Assert.Contains("--notes-file packaging/release-notes/", draft, StringComparison.Ordinal);

        // And only for a tag, so a dry run makes nothing.
        Assert.Contains("if: github.ref_type == 'tag'", draft, StringComparison.Ordinal);
    }

    [Fact]
    public void Checks_the_tag_and_runs_the_suite_before_building_anything()
    {
        string workflow = Workflow();

        int checks = workflow.IndexOf("-CheckOnly", StringComparison.Ordinal);
        int suite = workflow.IndexOf("dotnet test", StringComparison.Ordinal);
        int builds = workflow.IndexOf("name: Build the release", StringComparison.Ordinal);

        Assert.True(checks > 0 && suite > 0 && builds > 0, "The workflow is missing the check, the suite or the build.");
        Assert.True(checks < suite && suite < builds, "The tag is checked, then the suite runs, then the release is built, in that order.");
    }

    // --------------------------------------------------------------- helpers

    private static (int Code, string Said) Run(string arguments)
    {
        ProcessStartInfo how = new(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Repository.File("packaging/Build-TheRelease.ps1")}\" {arguments}")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process ran = Process.Start(how) ?? throw new InvalidOperationException("PowerShell would not start.");

        Task<string> output = ran.StandardOutput.ReadToEndAsync();
        Task<string> errors = ran.StandardError.ReadToEndAsync();

        if (!ran.WaitForExit(60_000))
        {
            ran.Kill(entireProcessTree: true);
            throw new TimeoutException("The release script did not finish in a minute.");
        }

        return (ran.ExitCode, output.Result + errors.Result);
    }

    /// <summary>A top-level key of the workflow, up to the next one.</summary>
    private static string Section(string yaml, string key)
    {
        Match found = Regex.Match(yaml, $"(?ms)^{key}:\\s*$(.*?)(?=^[a-z])");
        Assert.True(found.Success, $"The workflow has no '{key}:' section.");
        return found.Groups[1].Value;
    }

    /// <summary>One step of the workflow, by its name, up to the next step.</summary>
    private static string Step(string yaml, string name)
    {
        Match found = Regex.Match(yaml, $"(?ms)- name: {Regex.Escape(name)}\\s*$(.*?)(?=^\\s*- (name|uses):|\\z)");
        Assert.True(found.Success, $"The workflow has no step called '{name}'.");
        return found.Groups[1].Value;
    }
}
