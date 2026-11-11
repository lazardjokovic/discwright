using System.Text.RegularExpressions;

namespace DiscWright.Tests;

/// <summary>
/// The installer as the upgrade from the PowerShell DiscWright.
/// </summary>
/// <remarks>
/// <para>
/// Settled with the owner on 2026-11-10: 2.0 carries v1's AppId, so on a
/// machine with v1 installed it is the same product at a new version rather
/// than a second program called DiscWright. Three things make that true, and
/// each is one line that a tidy-up could quietly undo: the AppId, the folder,
/// and the list of v1's files cleared out of it. A fourth removes the testing
/// install under the rewrite's old AppId, whose Start Menu entry has the same
/// name and took over v1's when both were installed.
/// </para>
/// <para>
/// What these hold is the script. What it does was watched on the Windows VM on
/// 2026-11-11 with v1 0.11.1's real installer: v1 alone upgraded in place,
/// v1 with the rewrite installed became one entry and one shortcut, a clean
/// machine installed to Programs\DiscWright, the app opened each time, and
/// every uninstall left nothing behind.
/// </para>
/// </remarks>
public class AnInstallerThatTakesOver
{
    /// <summary>v1's AppId, as packaging\DiscWright.iss in the discwright repository has it.</summary>
    private const string V1 = "{20F71A81-9D66-445E-BDC3-394B940F190E}";

    /// <summary>The AppId the rewrite was tested under, installed as "DiscWright (rewrite)".</summary>
    private const string TheRewrite = "{14016040-B21A-4EA6-844E-FF7AE4E18176}";

    private static string Script() => File.ReadAllText(Repository.File("packaging/DiscWright.iss"));

    private static string Setting(string name)
    {
        Match found = Regex.Match(Script(), $"(?m)^{name}=(.*)$");
        Assert.True(found.Success, $"DiscWright.iss sets no {name}.");
        return found.Groups[1].Value.Trim();
    }

    [Fact]
    public void Carries_the_PowerShell_DiscWrights_AppId()
    {
        // Doubled brace: Inno reads {{ as a literal brace.
        Assert.Equal("{" + V1, Setting("AppId"));
    }

    [Fact]
    public void Installs_where_v1_installs_under_the_plain_name()
    {
        Assert.Equal(@"{autopf}\{#AppName}", Setting("DefaultDirName"));
        Assert.Matches("(?m)^#define AppName +\"DiscWright\"\\s*$", Script());
    }

    [Theory]
    [InlineData("files", "DiscWright.ps1")]
    [InlineData("files", "DiscWright.vbs")]
    [InlineData("files", "Run DiscWright.cmd")]
    [InlineData("files", "DiscWright.ico")]
    [InlineData("files", "LICENSE")]
    [InlineData("filesandordirs", "burn")]
    [InlineData("filesandordirs", "print")]
    public void Clears_what_v1_put_in_the_folder(string type, string name)
    {
        // Every file v1's installer has ever written beside itself, from its
        // [Files], plus the print folder 0.11.0 stopped shipping.
        string delete = Section("InstallDelete");

        Assert.Matches(
            $"(?m)^Type: +{type}; +Name: +\"\\{{app\\}}\\\\{Regex.Escape(name)}\"\\s*$",
            delete);
    }

    [Fact]
    public void Removes_the_rewrites_testing_install_before_writing_anything()
    {
        string script = Script();

        Assert.Contains($"#define RewriteAppId \"{TheRewrite}\"", script, StringComparison.Ordinal);

        string code = Section("Code");
        Assert.Contains(@"Uninstall\{#RewriteAppId}_is1", code, StringComparison.Ordinal);
        Assert.Contains("RegQueryStringValue(HKCU", code, StringComparison.Ordinal);
        Assert.Contains("/VERYSILENT", code, StringComparison.Ordinal);
        Assert.Contains("ewWaitUntilTerminated", code, StringComparison.Ordinal);

        // At ssInstall, before the files and the shortcuts are written: the
        // rewrite's uninstaller removes a Start Menu entry of the same name.
        Assert.Matches(@"(?s)if CurStep = ssInstall then\s+RemoveTheRewrite\(\);", code);
    }

    [Fact]
    public void Points_people_at_the_public_repository()
    {
        Assert.Equal("https://github.com/lazardjokovic/discwright/issues", Setting("AppSupportURL"));
        Assert.Equal("https://github.com/lazardjokovic/discwright/releases", Setting("AppUpdatesURL"));
    }

    [Fact]
    public void Shows_the_licence_that_is_really_there()
    {
        string licence = Setting("LicenseFile");

        Assert.Equal(@"..\LICENSE", licence);
        Assert.True(File.Exists(Repository.File("LICENSE")), "The installer names a LICENSE the repository does not have.");
    }

    /// <summary>One [Section] of the script, up to the next.</summary>
    private static string Section(string name)
    {
        Match found = Regex.Match(Script(), $"(?ms)^\\[{name}\\]\\s*$(.*?)(?=^\\[[A-Za-z]+\\]\\s*$|\\z)");
        Assert.True(found.Success, $"DiscWright.iss has no [{name}] section.");
        return found.Groups[1].Value;
    }
}
