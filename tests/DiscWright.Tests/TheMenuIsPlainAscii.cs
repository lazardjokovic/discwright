using DiscWright.Menu;

namespace DiscWright.Tests;

/// <summary>
/// Every byte of the menu a disc carries is plain ASCII, whatever the game is
/// called.
/// </summary>
/// <remarks>
/// <para>
/// The menu is written as UTF-8 with no byte order mark and no charset, and
/// mshta reads such a file in the machine's ANSI codepage. So anything past
/// ASCII arrives as its UTF-8 bytes read one at a time. On 2026-11-12 the
/// owner installed Hollow Knight from discs burned on his laptop and the
/// button read "Starting" followed by "â€¦", the bytes of an
/// ellipsis written straight into the template. A game with an accent or a
/// trademark sign in its name would have shown the same on every disc.
/// </para>
/// <para>
/// So the page holds codes rather than characters: <c>…</c> inside
/// script and <c>&amp;#8482;</c> in markup, which read the same on every
/// codepage there is.
/// </para>
/// </remarks>
public class TheMenuIsPlainAscii
{
    private const string AName = "Pokémon™ Đorđe ゲーム \U0001F600";

    [Fact]
    public void A_menu_for_a_name_with_every_kind_of_character_is_all_ASCII()
    {
        string page = MenuWriter.MenuPage(new MenuFacts(
            AName,
            "DiscWright." + AName,
            1,
            2,
            "Install …",
            ["Install …", "Manual", "Exit"],
            true,
            Games: [new MenuGame(AName, "setup_é.exe", "Café")],
            Manual: "Manual é.pdf",
            Extras: "Extras é",
            Music: "é.mp3"));

        int first = page.ToList().FindIndex(character => character >= 0x80);

        Assert.True(
            first < 0,
            first < 0 ? string.Empty : $"U+{(int)page[first]:X4} is in the page: {page[Math.Max(0, first - 40)..Math.Min(page.Length, first + 20)]}");
    }

    [Fact]
    public void The_name_in_markup_is_written_as_numbered_characters_whole()
    {
        string page = MenuWriter.MenuPage(new MenuFacts(AName, "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], true));

        // The title is markup. A character outside the first plane is one
        // reference, never its two halves, which markup does not accept.
        Assert.Contains(
            "<title>Pok&#233;mon&#8482; &#272;or&#273;e &#12466;&#12540;&#12512; &#128512;</title>",
            page,
            StringComparison.Ordinal);

        // And the escaping that was there before still comes first.
        string risky = MenuWriter.MenuPage(new MenuFacts("A & B <c> \"d\"", "DiscWright.Test", 1, 1, "Install", ["Install", "Exit"], true));
        Assert.Contains("<title>A &amp; B &lt;c&gt; &quot;d&quot;</title>", risky, StringComparison.Ordinal);
    }

    [Fact]
    public void A_name_in_script_is_written_as_codes()
    {
        Assert.Equal("\"Caf\\u00E9 \\u2122 \\uD83D\\uDE00\"", JScript.Literal("Café ™ \U0001F600"));

        // Plain ASCII stays as it is, tilde included, so nothing a name has
        // always been written as changes.
        Assert.Equal("\"Hollow Knight ~ 100%\"", JScript.Literal("Hollow Knight ~ 100%"));
    }

    [Theory]
    [InlineData("menu.hta.template")]
    [InlineData("menu.js")]
    [InlineData("start-here.hta")]
    public void Every_file_the_menu_is_built_from_is_ASCII(string file)
    {
        // The template's own ellipsis is the one the owner saw. A byte order
        // mark at the front is allowed: it is how the file is read here, and
        // it never reaches a disc, which gets what the writer writes.
        byte[] bytes = File.ReadAllBytes(Repository.File("src", "DiscWright.Menu", file));
        int start = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;

        for (int at = start; at < bytes.Length; at++)
        {
            Assert.True(
                bytes[at] < 0x80,
                $"{file} has byte 0x{bytes[at]:X2} at {at}, past plain ASCII, which mshta reads in the machine's codepage.");
        }
    }
}
