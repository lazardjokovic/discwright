using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DiscWright.Tests;

/// <summary>
/// The winget manifests, as packaging\winget\New-WingetManifest.ps1 writes them.
/// </summary>
/// <remarks>
/// <para>
/// Run rather than read. What goes into microsoft/winget-pkgs is the files this
/// script writes, and the one bug it had in the PowerShell app was invisible in
/// the source: a reworded description made the YAML unparseable, and it sat
/// broken for six weeks.
/// </para>
/// <para>
/// The script came over from the PowerShell DiscWright for 2.0, when the
/// package DiscWright.DiscWright moved to this app. Writing the files submits
/// nothing; submitting is by hand, one version at a time.
/// </para>
/// </remarks>
public sealed class MakingAWingetManifest : IDisposable
{
    private readonly string _out = Path.Combine(Path.GetTempPath(), "dw-winget-" + Guid.NewGuid().ToString("N")[..8]);

    private readonly Dictionary<string, string> _files;

    public MakingAWingetManifest()
    {
        ProcessStartInfo how = new(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Repository.File("packaging/winget/New-WingetManifest.ps1")}\" -Version 9.9.9 -Sha256 {new string('a', 64)} -OutDir \"{_out}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process ran = Process.Start(how) ?? throw new InvalidOperationException("PowerShell would not start.");
        string said = ran.StandardOutput.ReadToEnd() + ran.StandardError.ReadToEnd();
        ran.WaitForExit(60_000);
        Assert.True(ran.ExitCode == 0, "The manifest script failed:\n" + said);

        _files = Directory.GetFiles(_out, "*.yaml").ToDictionary(path => Path.GetFileName(path), File.ReadAllText);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_out, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Iss() => File.ReadAllText(Repository.File("packaging/DiscWright.iss"));

    [Fact]
    public void Writes_the_three_files_a_multi_file_manifest_is()
    {
        Assert.Equal(
            ["DiscWright.DiscWright.installer.yaml", "DiscWright.DiscWright.locale.en-US.yaml", "DiscWright.DiscWright.yaml"],
            _files.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Carries_the_installers_own_AppId_as_the_product_code()
    {
        // How winget recognises a copy it installed, the PowerShell app's
        // included, which is what makes 2.0.0 an upgrade rather than a second
        // program. Read from the .iss by the script, held to it here.
        string appId = Regex.Match(Iss(), @"(?m)^AppId=\{(\{[0-9A-F-]+\})\s*$").Groups[1].Value;

        Assert.Equal("{20F71A81-9D66-445E-BDC3-394B940F190E}", appId);
        Assert.Contains($"ProductCode: '{appId}_is1'", _files["DiscWright.DiscWright.installer.yaml"], StringComparison.Ordinal);
    }

    [Fact]
    public void Says_x64_because_the_installer_allows_nothing_else()
    {
        Assert.Matches(@"(?m)^ArchitecturesAllowed=x64compatible\s*$", Iss());
        Assert.Contains("- Architecture: x64", _files["DiscWright.DiscWright.installer.yaml"], StringComparison.Ordinal);
    }

    [Fact]
    public void Points_at_the_file_the_release_workflow_attaches()
    {
        Assert.Contains(
            "InstallerUrl: https://github.com/lazardjokovic/discwright/releases/download/v9.9.9/DiscWright-9.9.9-setup.exe",
            _files["DiscWright.DiscWright.installer.yaml"],
            StringComparison.Ordinal);

        // The same name Build-TheRelease.ps1 gives the installer.
        Assert.Contains(
            "\"DiscWright-$version-setup.exe\"",
            File.ReadAllText(Repository.File("packaging/Build-TheRelease.ps1")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Names_the_identifier_that_cannot_be_renamed_and_the_publisher_the_installer_declares()
    {
        string publisher = Regex.Match(Iss(), "(?m)^#define AppPublisher\\s+\"([^\"]+)\"").Groups[1].Value;

        Assert.Equal("DiscWright", publisher);

        foreach (string text in _files.Values)
        {
            Assert.Contains("PackageIdentifier: DiscWright.DiscWright", text, StringComparison.Ordinal);
        }

        Assert.Contains("Publisher: " + publisher, _files["DiscWright.DiscWright.locale.en-US.yaml"], StringComparison.Ordinal);
    }

    [Fact]
    public void Quotes_every_value_that_carries_a_colon()
    {
        List<string> unsafeLines = [];

        foreach ((string name, string text) in _files)
        {
            string[] lines = text.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                Match pair = Regex.Match(lines[i].TrimEnd('\r'), @"^\s*([A-Za-z][\w-]*):\s+(\S.*)$");

                if (!pair.Success)
                {
                    continue;
                }

                string value = pair.Groups[2].Value;

                // A quoted scalar, or a block scalar, may carry anything.
                if (value.Length > 0 && "'\"|>".Contains(value[0], StringComparison.Ordinal))
                {
                    continue;
                }

                if (Regex.IsMatch(value, @":\s"))
                {
                    unsafeLines.Add($"{name} line {i + 1}");
                }
            }
        }

        Assert.Empty(unsafeLines);
    }

    [Fact]
    public void Says_one_manifest_version_in_every_manifest_and_every_schema_line()
    {
        List<string> versions = [];

        foreach (string text in _files.Values)
        {
            versions.AddRange(Regex.Matches(text, @"ManifestVersion:\s*([\d.]+)").Select(m => m.Groups[1].Value));
            versions.AddRange(Regex.Matches(text, @"winget-manifest\.[A-Za-z]+\.([\d.]+)\.schema\.json").Select(m => m.Groups[1].Value));
        }

        Assert.Equal(6, versions.Count);
        Assert.Equal(["1.28.0"], versions.Distinct());
    }

    [Fact]
    public void Passes_winget_validate_where_winget_is_installed()
    {
        // The real check, and the only reason the colon was ever found. The
        // hosted runner has no winget, so there the two tests above stand in.
        string? winget = WhereIs("winget.exe");

        if (winget is null)
        {
            return;
        }

        ProcessStartInfo how = new(winget, $"validate --manifest \"{_out}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process ran = Process.Start(how)!;
        string said = ran.StandardOutput.ReadToEnd() + ran.StandardError.ReadToEnd();
        ran.WaitForExit(120_000);

        Assert.Contains("succeeded", said, StringComparison.OrdinalIgnoreCase);
    }

    private static string? WhereIs(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder.Trim(), exe))
            .FirstOrDefault(File.Exists);
}
