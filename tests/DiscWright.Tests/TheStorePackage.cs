using System.Buffers.Binary;

namespace DiscWright.Tests;

/// <summary>
/// The package the Microsoft Store is sent: its identity, its name and its
/// logos, held to what Partner Center reserved and to the sizes Windows asks
/// for.
/// </summary>
/// <remarks>
/// <para>
/// A submission whose identity differs from the reservation by one character
/// is rejected, and a logo missing at one scale is drawn by Windows as a
/// blank or a blur somewhere nobody here looks. Neither shows in the app's
/// own window, so both are read off the files here.
/// </para>
/// <para>
/// Run on the test machine on 2026-11-12 with these logos and this identity:
/// registered, started the way Windows starts a Store app, it knew it was the
/// Store's copy, its link opened the Store, its log opened in Notepad from
/// the package folder, and its preview opened in mshta.
/// </para>
/// </remarks>
public class TheStorePackage
{
    private static readonly int[] Scales = [100, 125, 150, 200, 400];

    private static readonly int[] TargetSizes = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256];

    private static readonly string[] Forms = [string.Empty, "_altform-unplated", "_altform-lightunplated"];

    private static readonly string[] Scaled = ["StoreLogo", "Square44x44Logo", "Square150x150Logo"];

    [Theory]
    [InlineData("StoreLogo", 50)]
    [InlineData("Square44x44Logo", 44)]
    [InlineData("Square150x150Logo", 150)]
    public void Every_logo_is_there_at_every_scale_at_exactly_its_size(string name, int atOneHundred)
    {
        foreach (int scale in Scales)
        {
            string file = Asset($"{name}.scale-{scale}.png");
            int side = (int)Math.Round(atOneHundred * scale / 100.0, MidpointRounding.AwayFromZero);

            Assert.True(File.Exists(file), $"{Path.GetFileName(file)} is missing.");
            Assert.Equal((side, side), SizeOf(file));
        }
    }

    [Fact]
    public void The_small_icon_is_there_at_every_size_Windows_draws_it_plated_or_not()
    {
        foreach (int side in TargetSizes)
        {
            foreach (string form in Forms)
            {
                string file = Asset($"Square44x44Logo.targetsize-{side}{form}.png");

                Assert.True(File.Exists(file), $"{Path.GetFileName(file)} is missing.");
                Assert.Equal((side, side), SizeOf(file));
            }
        }
    }

    [Fact]
    public void Nothing_else_sits_among_the_logos()
    {
        // Every file there is packed, and one the manifest never names is
        // weight in every download.
        string[] expected =
        [
            .. Scaled.SelectMany(name => Scales.Select(scale => $"{name}.scale-{scale}.png")),
            .. TargetSizes.SelectMany(side => Forms.Select(form => $"Square44x44Logo.targetsize-{side}{form}.png"))
        ];

        string[] there = [.. Directory.GetFiles(Asset(string.Empty)).Select(Path.GetFileName).OfType<string>()];

        Assert.Equal(expected.Order(StringComparer.Ordinal), there.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_package_carries_the_identity_Partner_Center_reserved()
    {
        string script = Script();

        // Reserved on 2026-11-12, Store ID 9NS2FK1N3KKC.
        Assert.Contains("[string] $Identity = 'LazarDjokovic.DiscWright',", script, StringComparison.Ordinal);
        Assert.Contains("[string] $Publisher = 'CN=E9DAEFBA-A2F5-4E0B-8F70-D637310F2D3F',", script, StringComparison.Ordinal);
        Assert.Contains("[string] $PublisherDisplayName = 'Lazar Djokovic',", script, StringComparison.Ordinal);

        // Called what the Store calls it, in the Start menu as everywhere.
        Assert.Contains("<DisplayName>DiscWright</DisplayName>", script, StringComparison.Ordinal);
        Assert.Contains("DisplayName=\"DiscWright\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("(packaged)", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_package_is_built_from_the_drawn_logos_with_one_index_for_them()
    {
        string script = Script();

        Assert.Contains("Join-Path $PSScriptRoot 'msix\\Assets'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("New-Object Drawing.Icon", script, StringComparison.Ordinal);

        // resources.pri is what lets Windows find a logo by scale at all, and
        // it is one file: the split MakePri writes by default is for bundles.
        Assert.Contains("& $pri new /pr $into /cf $priConfig /mn $manifestPath", script, StringComparison.Ordinal);
        Assert.Contains("$config.resources.RemoveChild($split)", script, StringComparison.Ordinal);
    }

    private static string Script() =>
        File.ReadAllText(Repository.File("packaging", "Build-TheMsix.ps1"));

    private static string Asset(string name) =>
        name.Length == 0
            ? Repository.File("packaging", "msix", "Assets")
            : Repository.File("packaging", "msix", "Assets", name);

    /// <summary>A PNG's width and height, read from its header.</summary>
    private static (int Width, int Height) SizeOf(string png)
    {
        byte[] head = new byte[24];

        using (FileStream reading = File.OpenRead(png))
        {
            reading.ReadExactly(head);
        }

        // The eight byte signature, then the IHDR chunk's length and name,
        // then the width and the height, big endian.
        return (BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(16, 4)),
                BinaryPrimitives.ReadInt32BigEndian(head.AsSpan(20, 4)));
    }
}
