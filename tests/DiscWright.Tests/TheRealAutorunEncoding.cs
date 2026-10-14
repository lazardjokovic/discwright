using System.Text;

using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// The real codepage adapter, against the real machine.
/// </summary>
/// <remarks>
/// The rest of the autorun tests use a 1252 stand-in, so that they say the same
/// thing wherever they run. These exercise the thing that actually reaches the
/// disc, because this is the one place the trap lives: in .NET
/// <c>Encoding.Default</c> is UTF-8, not the ANSI codepage it is in Windows
/// PowerShell, and a disc written with it would look right everywhere except in
/// This PC.
/// </remarks>
public class TheRealAutorunEncoding
{
    [Fact]
    public void Is_not_UTF_8_whatever_dot_NET_calls_the_default()
    {
        // The whole reason this class exists. If this ever starts failing,
        // somebody has reached for Encoding.Default and the disc's name will be
        // wrong in a way nothing else notices.
        Assert.NotEqual(Encoding.UTF8.CodePage, WindowsAutorunEncoding.ForAutorunFiles.CodePage);
    }

    [Fact]
    public void Writes_no_byte_order_mark()
    {
        // AutoRun does not understand one, and it would be the first three
        // bytes of the file, in front of "[autorun]".
        Assert.Empty(WindowsAutorunEncoding.ForAutorunFiles.GetPreamble());
    }

    [Fact]
    public void Keeps_the_accents_that_ASCII_turned_into_question_marks()
    {
        // "Uber Alles" reached Explorer as "?ber Alles" when this was ASCII.
        Assert.Equal("Über Alles", new WindowsAutorunEncoding().AsAutorunWillSeeIt("Über Alles"));
    }

    [Fact]
    public void Admits_what_it_cannot_carry_rather_than_pretending()
    {
        string seen = new WindowsAutorunEncoding().AsAutorunWillSeeIt("Данные");

        // Both machines here are codepage 1252. Rather than asserting the exact
        // question marks, which would tie this test to that, it asserts the
        // thing that matters: it is no longer what was typed, so the person
        // gets told before the disc is burned instead of after.
        Assert.NotEqual("Данные", seen);
    }

    [Fact]
    public void Leaves_a_plain_name_exactly_alone()
    {
        Assert.Equal("HOLLOW KNIGHT", new WindowsAutorunEncoding().AsAutorunWillSeeIt("HOLLOW KNIGHT"));
    }
}
