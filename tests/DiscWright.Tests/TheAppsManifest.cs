using System.Xml.Linq;

namespace DiscWright.Tests;

/// <summary>
/// What the exe tells Windows about itself, read off app.manifest.
/// </summary>
/// <remarks>
/// Microsoft's certification kit warned on 2026-11-12 that the exe "is not
/// DPI Aware". Run again on a package built with this manifest, that check
/// passed.
/// </remarks>
public class TheAppsManifest
{
    private static readonly XNamespace V1 = "urn:schemas-microsoft-com:asm.v1";
    private static readonly XNamespace V3 = "urn:schemas-microsoft-com:asm.v3";
    private static readonly XNamespace Settings2005 = "http://schemas.microsoft.com/SMI/2005/WindowsSettings";
    private static readonly XNamespace Settings2016 = "http://schemas.microsoft.com/SMI/2016/WindowsSettings";
    private static readonly XNamespace Compatibility = "urn:schemas-microsoft-com:compatibility.v1";

    [Fact]
    public void Says_it_draws_itself_again_for_every_screen_scale()
    {
        XElement settings = Manifest().Descendants(V3 + "windowsSettings").Single();

        Assert.Equal("PerMonitorV2", (string?)settings.Element(Settings2016 + "dpiAwareness"));

        // For a Windows 10 build that reads only the older element.
        Assert.Equal("true/pm", (string?)settings.Element(Settings2005 + "dpiAware"));
    }

    [Fact]
    public void Runs_as_whoever_started_it_and_never_asks_to_be_an_administrator()
    {
        XElement level = Manifest().Descendants(V3 + "requestedExecutionLevel").Single();

        Assert.Equal("asInvoker", (string?)level.Attribute("level"));
        Assert.Equal("false", (string?)level.Attribute("uiAccess"));
    }

    [Fact]
    public void Is_written_for_Windows_10_and_11()
    {
        Assert.Equal(
            ["{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"],
            Manifest().Descendants(Compatibility + "supportedOS").Select(os => (string?)os.Attribute("Id")));
    }

    [Fact]
    public void The_exe_is_built_with_it()
    {
        XDocument project = XDocument.Load(Repository.File("src", "DiscWright.App", "DiscWright.App.csproj"));

        Assert.Equal("app.manifest", project.Descendants("ApplicationManifest").Single().Value);
        Assert.Equal(V1 + "assembly", Manifest().Name);
    }

    private static XElement Manifest() =>
        XDocument.Load(Repository.File("src", "DiscWright.App", "app.manifest")).Root!;
}
