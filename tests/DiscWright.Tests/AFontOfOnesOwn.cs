using System.Buffers.Binary;
using System.IO.Compression;

using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;
using DiscWright.Platform;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// A font file of somebody's own for the disc's menu. The owner, 2026-11-20:
/// "would it work on every Windows machine if the user uploads their own
/// font?"
/// </summary>
/// <remarks>
/// <para>
/// Measured that day in a real menu window: a TrueType file loaded only when
/// its own flag says it may be installed, and the same font wrapped as WOFF
/// loaded whatever the flag said. So the disc carries WOFF, and most of this
/// is that the wrapping loses nothing: every table of the font comes back
/// out of it byte for byte.
/// </para>
/// <para>
/// That the menu then wears it is the menu's, which no test can see. It was
/// measured in a real one with a font this code wrapped.
/// </para>
/// </remarks>
public sealed class AFontOfOnesOwn : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "dwfont_" + Guid.NewGuid().ToString("N")[..8]);

    public AFontOfOnesOwn() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // ------------------------------------------------------------ the wrapping

    [Theory]
    [InlineData(0x00010000u)]
    [InlineData(0x4F54544Fu)]
    [InlineData(0x74727565u)]
    public void Every_table_comes_back_out_of_the_wrapping_byte_for_byte(uint kind)
    {
        Dictionary<string, byte[]> tables = SomeTables();
        byte[] font = AFontOf(kind, tables);

        byte[] woff = Assert.IsType<byte[]>(AFontForTheMenu.AsWoff(font));

        Assert.Equal(tables, Unwrapped(woff, out uint flavour, out uint saysItsLength, out uint saysUnwrapped));
        Assert.Equal(kind, flavour);
        Assert.Equal((uint)woff.Length, saysItsLength);

        // What it would come to unwrapped: the header, the list, and every
        // table on a boundary of four.
        Assert.Equal((uint)(12 + (16 * tables.Count) + tables.Values.Sum(each => (each.Length + 3) & ~3)), saysUnwrapped);
    }

    [Fact]
    public void The_tables_are_listed_in_the_order_of_their_names_and_each_starts_on_a_boundary_of_four()
    {
        byte[] woff = AFontForTheMenu.AsWoff(AFontOf(0x00010000, SomeTables()))!;
        int count = BinaryPrimitives.ReadUInt16BigEndian(woff.AsSpan(12));
        List<uint> tags = [];

        for (int each = 0; each < count; each++)
        {
            Span<byte> entry = woff.AsSpan(44 + (20 * each));

            tags.Add(BinaryPrimitives.ReadUInt32BigEndian(entry));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(entry[4..]) % 4);
        }

        Assert.Equal(tags.Order(), tags);
        Assert.Equal(0, woff.Length % 4);
    }

    [Fact]
    public void A_table_that_will_not_squeeze_is_kept_as_it_is()
    {
        // The format says a table whose two lengths agree was stored plain,
        // and one stored squeezed at the same length would be read as plain.
        byte[] noise = new byte[512];
        new Random(4).NextBytes(noise);

        byte[] woff = AFontForTheMenu.AsWoff(AFontOf(0x00010000, new() { ["glyf"] = noise, ["name"] = new byte[2000] }))!;

        (int stored, int original) glyf = LengthsOf(woff, "glyf");
        (int stored, int original) name = LengthsOf(woff, "name");

        Assert.Equal(glyf.original, glyf.stored);
        Assert.True(name.stored < name.original);
        Assert.Equal(noise, Unwrapped(woff, out _, out _, out _)["glyf"]);
    }

    [Fact]
    public void A_file_that_is_WOFF_already_goes_on_as_it_is()
    {
        byte[] woff = AFontForTheMenu.AsWoff(AFontOf(0x00010000, SomeTables()))!;

        Assert.Null(AFontForTheMenu.WhyNot(woff));
        Assert.Equal(woff, AFontForTheMenu.AsWoff(woff));
    }

    [Fact]
    public void A_real_font_off_this_machine_is_wrapped_and_comes_back_whole()
    {
        string georgia = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "georgia.ttf");

        Assert.True(File.Exists(georgia), "Georgia has been on every Windows since 2000 and is not on this one.");

        byte[] font = File.ReadAllBytes(georgia);
        byte[] woff = Assert.IsType<byte[]>(AFontForTheMenu.AsWoff(font));

        Assert.True(woff.Length < font.Length, "Wrapped, it is squeezed, and came out no smaller.");

        Dictionary<string, byte[]> back = Unwrapped(woff, out uint flavour, out _, out _);

        Assert.Equal(BinaryPrimitives.ReadUInt32BigEndian(font), flavour);
        Assert.Equal(TablesOf(font), back);
        Assert.Contains("glyf", back.Keys);
    }

    // -------------------------------------------------------- what is not one

    [Theory]
    [InlineData("", "It is not a font file.")]
    [InlineData("short", "It is not a font file.")]
    [InlineData("this is a text file and not a font", "It is not a TrueType, OpenType or WOFF font.")]
    public void What_is_not_a_font_is_said_in_words(string text, string why)
    {
        byte[] file = System.Text.Encoding.ASCII.GetBytes(text);

        Assert.Equal(why, AFontForTheMenu.WhyNot(file));
        Assert.Null(AFontForTheMenu.AsWoff(file));
    }

    [Fact]
    public void A_collection_of_several_fonts_is_turned_away_and_says_what_to_choose()
    {
        byte[] collection = new byte[64];
        BinaryPrimitives.WriteUInt32BigEndian(collection, 0x74746366);

        Assert.Equal(
            "It is a collection of several fonts in one file. Choose a file that holds one.",
            AFontForTheMenu.WhyNot(collection));
    }

    [Fact]
    public void A_font_cut_short_is_damaged_and_never_read_past_its_end()
    {
        byte[] font = AFontOf(0x00010000, SomeTables());

        foreach (int kept in new[] { 12, 20, font.Length / 2, font.Length - 1 })
        {
            byte[] cut = font[..kept];

            Assert.Equal("The font file is damaged: its list of tables runs past its end.", AFontForTheMenu.WhyNot(cut));
            Assert.Null(AFontForTheMenu.AsWoff(cut));
        }
    }

    [Fact]
    public void A_font_that_says_it_has_no_tables_is_damaged()
    {
        byte[] none = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(none, 0x00010000);

        Assert.NotNull(AFontForTheMenu.WhyNot(none));
    }

    // ---------------------------------------------------------- the real files

    [Fact]
    public void The_real_writer_puts_WOFF_on_the_disc_from_a_TrueType_file()
    {
        string source = Path.Combine(_folder, "mine.ttf");
        string onTheDisc = Path.Combine(_folder, AFontForTheMenu.FileName);
        byte[] font = AFontOf(0x00010000, SomeTables());
        File.WriteAllBytes(source, font);

        WindowsFonts fonts = new();

        Assert.Null(fonts.WhyNot(source));

        fonts.WriteForTheMenu(source, onTheDisc);

        byte[] written = File.ReadAllBytes(onTheDisc);

        Assert.Equal(0x774F4646u, BinaryPrimitives.ReadUInt32BigEndian(written));
        Assert.Equal(SomeTables(), Unwrapped(written, out _, out _, out _));
    }

    [Fact]
    public void The_real_writer_says_why_not_and_writes_nothing_for_a_file_that_is_no_font()
    {
        string source = Path.Combine(_folder, "notes.txt");
        string onTheDisc = Path.Combine(_folder, AFontForTheMenu.FileName);
        File.WriteAllText(source, "this is a text file and not a font");

        WindowsFonts fonts = new();

        Assert.Equal("It is not a TrueType, OpenType or WOFF font.", fonts.WhyNot(source));

        fonts.WriteForTheMenu(source, onTheDisc);

        Assert.False(File.Exists(onTheDisc));
    }

    [Fact]
    public void The_real_writer_says_a_file_that_is_gone_cannot_be_read()
    {
        Assert.StartsWith(
            "It cannot be read: ",
            new WindowsFonts().WhyNot(Path.Combine(_folder, "gone.ttf")),
            StringComparison.Ordinal);
    }

    // --------------------------------------------------------------- the page

    [Fact]
    public void A_menu_with_a_font_of_its_own_loads_it_from_beside_the_page_and_wears_it_first()
    {
        string page = MenuWriter.MenuPage(AMenu() with { HasAFontOfItsOwn = true, Look = new MenuLook(Font: MenuFont.Georgia) });

        Assert.Contains(
            "@font-face { font-family: \"DiscWright Menu Font\"; src: url(\"menu-font.woff\") format(\"woff\"); }",
            page,
            StringComparison.Ordinal);

        // First its own, and after it the face from the list, for a machine
        // too old to load a font from a disc.
        Assert.Contains(
            "font-family: \"DiscWright Menu Font\", Georgia, \"Times New Roman\", serif;",
            page,
            StringComparison.Ordinal);
        Assert.DoesNotContain("%%OWNFONT%%", page, StringComparison.Ordinal);
        Assert.True(
            page.IndexOf("@font-face", StringComparison.Ordinal)
                < page.IndexOf("font-family: \"DiscWright Menu Font\", Georgia", StringComparison.Ordinal));
    }

    [Fact]
    public void A_menu_without_one_says_nothing_about_loading_a_font()
    {
        string page = MenuWriter.MenuPage(AMenu());

        Assert.DoesNotContain("@font-face", page, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscWright Menu Font", page, StringComparison.Ordinal);
        Assert.DoesNotContain("%%OWNFONT%%", page, StringComparison.Ordinal);
        Assert.Contains("font-family: Tahoma, sans-serif;", page, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fonts_file_name_never_reaches_the_page()
    {
        // The page is told only that there is one. It is always called the
        // same on the disc, so a name with a quote in it is nobody's worry.
        Assert.Equal("menu-font.woff", AFontForTheMenu.FileName);
        Assert.DoesNotContain('"', AFontForTheMenu.FamilyOnThePage);
        Assert.DoesNotContain(';', AFontForTheMenu.FamilyOnThePage);
    }

    // -------------------------------------------------------- the project file

    [Fact]
    public void A_saved_project_names_the_font_file_and_comes_back_with_it()
    {
        string saved = Saved(new DiscArtwork(MenuFontPath: @"D:\fonts\Mine Regular.ttf"));

        Assert.Contains("\"MenuFontFile\": \"D:\\\\fonts\\\\Mine Regular.ttf\"", saved, StringComparison.Ordinal);
        Assert.Equal(
            @"D:\fonts\Mine Regular.ttf",
            Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value.Artwork.MenuFontPath);
    }

    [Fact]
    public void A_project_with_no_font_of_its_own_says_nothing_about_one()
    {
        string saved = Saved(null);

        Assert.DoesNotContain("MenuFontFile", saved, StringComparison.Ordinal);
        Assert.Null(Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(saved)).Value.Artwork.MenuFontPath);
    }

    [Fact]
    public void A_font_alone_is_artwork_worth_keeping()
    {
        Assert.True(new DiscArtwork(MenuFontPath: @"D:\fonts\mine.ttf").IsAnything);
        Assert.False(new DiscArtwork().IsAnything);
    }

    // ---------------------------------------------------------- room on a disc

    [Fact]
    public void The_font_counts_towards_what_a_disc_holds_only_when_there_is_a_menu_to_wear_it()
    {
        DiscProject with = AProject(new DiscArtwork(MenuFontPath: @"D:\fonts\mine.ttf"), menu: true);
        DiscProject without = AProject(null, menu: true);
        DiscProject noMenu = AProject(new DiscArtwork(MenuFontPath: @"D:\fonts\mine.ttf"), menu: false);

        long each = 700_000;

        Assert.Equal(each, DiscOverhead.For(with, _ => each) - DiscOverhead.For(without, _ => each));
        Assert.Equal(DiscOverhead.For(without, _ => each), DiscOverhead.For(noMenu, _ => each));
    }

    // ---------------------------------------------------------------- helpers

    private static MenuFacts AMenu() =>
        new("Hollow Knight", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], OnlyOneAtATime: true);

    private static DiscProject AProject(DiscArtwork? artwork, bool menu = true)
    {
        ListedFile only = new("setup_hollow_knight.exe", 1000);

        return new DiscProject(
            "HOLLOW KNIGHT",
            [new GameFolder(@"C:\games\Hollow Knight", GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", only, [only], [], null)],
            new DiscOptions(Menu: menu),
            Artwork: artwork,
            Extras: null,
            AddOns: []);
    }

    private static string Saved(DiscArtwork? artwork) =>
        TheProjectAsJson.Of(AProject(artwork), "2.0.5", "DVD5", @"C:\out", DateTimeOffset.UtcNow);

    /// <summary>Tables of several sizes, one of a length that is no multiple of four.</summary>
    private static Dictionary<string, byte[]> SomeTables()
    {
        Random random = new(11);
        Dictionary<string, byte[]> tables = [];

        foreach ((string tag, int length, bool noisy) in new[]
                 {
                     ("name", 301, false), ("cmap", 64, false), ("glyf", 4096, true), ("head", 54, false), ("OS/2", 96, false),
                 })
        {
            byte[] bytes = new byte[length];

            if (noisy)
            {
                random.NextBytes(bytes);
            }
            else
            {
                for (int at = 0; at < length; at++)
                {
                    bytes[at] = (byte)(at % 7);
                }
            }

            tables[tag] = bytes;
        }

        return tables;
    }

    /// <summary>A font file of this kind holding these tables, in an order that is not the order of their names.</summary>
    private static byte[] AFontOf(uint kind, Dictionary<string, byte[]> tables)
    {
        string[] order = [.. tables.Keys.OrderByDescending(each => each, StringComparer.Ordinal)];
        int at = 12 + (16 * order.Length);
        using MemoryStream font = new();
        Span<byte> four = stackalloc byte[4];
        Span<byte> two = stackalloc byte[2];

        BinaryPrimitives.WriteUInt32BigEndian(four, kind);
        font.Write(four);
        BinaryPrimitives.WriteUInt16BigEndian(two, (ushort)order.Length);
        font.Write(two);
        font.Write(new byte[6]);

        foreach (string tag in order)
        {
            font.Write(System.Text.Encoding.ASCII.GetBytes(tag));
            BinaryPrimitives.WriteUInt32BigEndian(four, (uint)tag.GetHashCode(StringComparison.Ordinal));
            font.Write(four);
            BinaryPrimitives.WriteUInt32BigEndian(four, (uint)at);
            font.Write(four);
            BinaryPrimitives.WriteUInt32BigEndian(four, (uint)tables[tag].Length);
            font.Write(four);

            at += (tables[tag].Length + 3) & ~3;
        }

        foreach (string tag in order)
        {
            font.Write(tables[tag]);
            font.Write(new byte[((tables[tag].Length + 3) & ~3) - tables[tag].Length]);
        }

        return font.ToArray();
    }

    /// <summary>The tables of a font file as they sit in it.</summary>
    private static Dictionary<string, byte[]> TablesOf(byte[] font)
    {
        Dictionary<string, byte[]> tables = [];
        int count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));

        for (int each = 0; each < count; each++)
        {
            Span<byte> entry = font.AsSpan(12 + (16 * each));
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);

            tables[System.Text.Encoding.ASCII.GetString(entry[..4])] = font[offset..(offset + length)];
        }

        return tables;
    }

    /// <summary>Everything a WOFF file holds, read the way whatever loads it reads it.</summary>
    private static Dictionary<string, byte[]> Unwrapped(byte[] woff, out uint flavour, out uint length, out uint unwrapped)
    {
        Assert.Equal(0x774F4646u, BinaryPrimitives.ReadUInt32BigEndian(woff));

        flavour = BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(4));
        length = BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(8));
        unwrapped = BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(16));

        int count = BinaryPrimitives.ReadUInt16BigEndian(woff.AsSpan(12));
        Dictionary<string, byte[]> tables = [];

        for (int each = 0; each < count; each++)
        {
            Span<byte> entry = woff.AsSpan(44 + (20 * each));
            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[4..]);
            int stored = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            int original = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);
            byte[] bytes = woff[offset..(offset + stored)];

            if (stored != original)
            {
                using ZLibStream opening = new(new MemoryStream(bytes), CompressionMode.Decompress);
                using MemoryStream plain = new();
                opening.CopyTo(plain);
                bytes = plain.ToArray();
            }

            Assert.Equal(original, bytes.Length);
            tables[System.Text.Encoding.ASCII.GetString(entry[..4])] = bytes;
        }

        return tables;
    }

    private static (int Stored, int Original) LengthsOf(byte[] woff, string tag)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(woff.AsSpan(12));

        for (int each = 0; each < count; each++)
        {
            Span<byte> entry = woff.AsSpan(44 + (20 * each));

            if (System.Text.Encoding.ASCII.GetString(entry[..4]) == tag)
            {
                return ((int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]), (int)BinaryPrimitives.ReadUInt32BigEndian(entry[12..]));
            }
        }

        throw new InvalidOperationException("No table called " + tag);
    }

    // --------------------------------------------------------------- the screen

    [Fact]
    public void A_font_chosen_reaches_the_disc_and_the_window_hears()
    {
        MakeADisc screen = TheScreen(new FontsThatSay(null));
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        Assert.Null(screen.MenuFontFile);

        screen.MenuFontFile = @"D:\fonts\mine.ttf";

        Assert.Equal(@"D:\fonts\mine.ttf", screen.AsAProject().Artwork?.MenuFontPath);
        Assert.Contains(nameof(MakeADisc.MenuFontFile), told, StringComparer.Ordinal);

        screen.MenuFontFile = null;

        Assert.Null(screen.AsAProject().Artwork);
    }

    [Fact]
    public void A_file_that_is_no_font_is_turned_away_with_its_reason_and_the_next_good_one_clears_it()
    {
        FontsThatSay fonts = new("It is not a TrueType, OpenType or WOFF font.");
        MakeADisc screen = TheScreen(fonts);
        List<string> told = [];
        screen.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        Assert.False(screen.ThatFontIsUsable(@"D:\notes.txt"));
        Assert.Equal("It is not a TrueType, OpenType or WOFF font.", screen.WhyNotThatFont);
        Assert.Contains(nameof(MakeADisc.WhyNotThatFont), told, StringComparer.Ordinal);
        Assert.Equal([@"D:\notes.txt"], fonts.Asked);

        fonts.Why = null;

        Assert.True(screen.ThatFontIsUsable(@"D:\fonts\mine.ttf"));
        Assert.Null(screen.WhyNotThatFont);
    }

    [Fact]
    public void A_machine_with_nothing_to_read_fonts_with_takes_a_file_at_its_word()
    {
        MakeADisc screen = TheScreen(null);

        Assert.True(screen.ThatFontIsUsable(@"D:\anything.ttf"));
        Assert.Null(screen.WhyNotThatFont);
    }

    [Fact]
    public void A_font_file_that_has_gone_since_it_was_chosen_is_named_before_a_build_starts()
    {
        MakeADisc screen = TheScreen(new FontsThatSay(null), new ADiskWhereThisIsGone(@"D:\fonts\mine.ttf"));
        ListedFile only = new("setup_a_game.exe", 1000);
        screen.Add(new GameFolder(@"C:\games\A Game", GameKind.GogInstaller, "A Game", "A Game", only, [only], [], null));
        screen.OutputFolder = @"C:\out";
        screen.MenuFontFile = @"D:\fonts\mine.ttf";

        Refusal gone = Assert.IsType<Refusal>(screen.AChosenFileIsGone());

        Assert.Contains("menu font", gone.ToString(), StringComparison.Ordinal);
        Assert.Contains(@"D:\fonts\mine.ttf", gone.ToString(), StringComparison.Ordinal);
    }

    // --------------------------------------------------------- the build step

    [Theory]
    [InlineData(true, true, 1)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    public async Task The_font_goes_beside_the_menu_only_on_a_disc_that_has_both(bool chosen, bool menu, int written)
    {
        FontsThatSay fonts = new(null);
        RecordingWriter wrote = new();
        PlannedDisc disc = new(1, [], 0);

        await new CopyTheArtworkStep(new PicturesOfOneSize(512, 512), fonts).Run(
            new BuildContext(
                AProject(chosen ? new DiscArtwork(MenuFontPath: @"D:\fonts\mine.ttf") : new DiscArtwork(), menu),
                new DiscLayout([disc], 1_000_000, true),
                disc,
                @"C:\stage",
                wrote),
            CancellationToken.None);

        Assert.Equal(written, fonts.Written.Count);

        if (written == 1)
        {
            Assert.Equal((@"D:\fonts\mine.ttf", @"C:\stage\AUTORUN\menu-font.woff"), fonts.Written[0]);
        }
    }

    [Fact]
    public async Task With_nothing_to_wrap_a_font_with_the_step_writes_none_and_does_not_fall_over()
    {
        RecordingWriter wrote = new();
        PlannedDisc disc = new(1, [], 0);

        await new CopyTheArtworkStep(new PicturesOfOneSize(512, 512)).Run(
            new BuildContext(
                AProject(new DiscArtwork(MenuFontPath: @"D:\fonts\mine.ttf")),
                new DiscLayout([disc], 1_000_000, true),
                disc,
                @"C:\stage",
                wrote),
            CancellationToken.None);

        Assert.DoesNotContain(wrote.Folders, each => each.EndsWith("AUTORUN", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task The_menu_is_told_it_has_a_font_of_its_own_exactly_when_the_disc_carries_one(bool chosen, bool told)
    {
        RecordingWriter wrote = new();
        PlannedDisc disc = new(1, [], 0);

        await new WriteTheMenuStep().Run(
            new BuildContext(
                AProject(chosen ? new DiscArtwork(MenuFontPath: @"D:\fonts\mine.ttf") : null),
                new DiscLayout([disc], 1_000_000, true),
                disc,
                @"C:\stage",
                wrote),
            CancellationToken.None);

        string page = wrote.Texts.Single(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        Assert.Equal(told, page.Contains("@font-face", StringComparison.Ordinal));
    }

    // -------------------------------------------------------------- the dialog

    [Fact]
    public void The_dialog_has_a_row_for_the_file_beside_the_list_and_says_what_putting_it_on_a_disc_means()
    {
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XElement dialog = System.Xml.Linq.XDocument
            .Load(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml")).Root!;

        string? Id(System.Xml.Linq.XElement each) => (string?)each.Attribute("AutomationProperties.AutomationId");

        Assert.Equal("ChooseTheMenuFont", (string?)Assert.Single(dialog.Descendants(xaml + "Button"), each => Id(each) == "ChooseTheMenuFont").Attribute("Click"));
        Assert.Equal("ClearTheMenuFont", (string?)Assert.Single(dialog.Descendants(xaml + "Button"), each => Id(each) == "ClearTheMenuFont").Attribute("Click"));
        Assert.Equal(
            "{Binding MenuFontFile, TargetNullValue=Nothing chosen}",
            (string?)Assert.Single(dialog.Descendants(xaml + "TextBlock"), each => Id(each) == "TheMenuFontFile").Attribute("Text"));
        Assert.Equal(
            "{Binding WhyNotThatFont}",
            (string?)Assert.Single(dialog.Descendants(xaml + "TextBlock"), each => Id(each) == "WhyNotThatFont").Attribute("Text"));

        // The list of faces is still there, and above it.
        List<string?> order = [.. dialog.Descendants().Select(Id).Where(each => each is "MenuFont" or "ChooseTheMenuFont")];

        Assert.Equal(["MenuFont", "ChooseTheMenuFont"], order);

        string says = string.Join(" ", dialog.Descendants(xaml + "TextBlock").Select(each => (string?)each.Attribute("Text")));

        Assert.Contains("check that its licence lets you put it there", says, StringComparison.Ordinal);
        Assert.Contains("A PC too old to load it wears the font chosen above", says, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dialog_looks_at_a_font_before_it_keeps_it()
    {
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "WhatElseGoesOnIt.xaml.cs"));
        string choosing = code[code.IndexOf("private void ChooseTheMenuFont(", StringComparison.Ordinal)..];
        choosing = choosing[..choosing.IndexOf("private void ClearTheMenuFont(", StringComparison.Ordinal)];

        Assert.Contains("AFontForTheMenu.ForThePicker", choosing, StringComparison.Ordinal);
        Assert.True(
            choosing.IndexOf("if (_screen.ThatFontIsUsable(path))", StringComparison.Ordinal)
                < choosing.IndexOf("_screen.MenuFontFile = path;", StringComparison.Ordinal));
        Assert.Contains("*.ttf;*.otf;*.woff", AFontForTheMenu.ForThePicker, StringComparison.Ordinal);
    }

    private static MakeADisc TheScreen(FontsThatSay? fonts, IFileSystem? disk = null) =>
        new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatDoesNothing(),
            new ImagesThatDoNothing(),
            new NoDrives(),
            new ABurnerThatDoesNothing(),
            new AnEraserThatDoesNothing(),
            disk ?? new ADiskWithNothingOnIt(),
            new NoHashing(),
            new NothingIsRemembered(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            new NoPreview(),
            new PicturesOfOneSize(512, 512),
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience,
            Fonts: fonts));

    private sealed class FontsThatSay(string? why) : IFonts
    {
        public string? Why { get; set; } = why;

        public List<string> Asked { get; } = [];

        public List<(string Source, string Destination)> Written { get; } = [];

        public string? WhyNot(string path)
        {
            Asked.Add(path);

            return Why;
        }

        public void WriteForTheMenu(string source, string destination) => Written.Add((source, destination));
    }

    /// <summary>Everything is there but the one file.</summary>
    private sealed class ADiskWhereThisIsGone(string gone) : IFileSystem
    {
        public bool FolderExists(string path) => true;

        public bool FileExists(string path) => !string.Equals(path, gone, StringComparison.OrdinalIgnoreCase);

        public FolderListing List(string path) => new(path, []);
    }

    private sealed class PicturesOfOneSize(int wide, int tall, bool readable = true) : IImages
    {
        public int TimesAsked { get; private set; }

        public ImageFacts Read(string path)
        {
            TimesAsked++;

            return new ImageFacts(readable, wide, tall, readable ? null : "It cannot be read.");
        }

        public void WriteIcon(string source, string destination)
        {
        }

        public void WriteBackground(
            string source, string destination, int width, int height, BackgroundFit how, KeptPart? kept = null)
        {
        }
    }

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
    }

    private sealed class ABuilderThatDoesNothing : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs([]));
    }

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class AnEraserThatDoesNothing : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(
            Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>(Refusals.ThereIsNoDiscWriter());
    }

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
    }

    private sealed class ADiskWithNothingOnIt : IFileSystem
    {
        public bool FolderExists(string path) => false;

        public FolderListing List(string path) => new(path, []);
    }

    private sealed class NoHashing : IHasher
    {
        public string Sha256(string path, CancellationToken ct) => string.Empty;
    }

    private sealed class NothingIsRemembered : IKeepTheSetsPlace
    {
        public string? WhatItSays(string folder) => null;

        public void Leave(string folder, string text)
        {
        }
    }

    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "no asking in a test"));
    }

    private sealed class NoPreview : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            Refusals.ThereIsNothingToPutOnADisc();
    }
}
