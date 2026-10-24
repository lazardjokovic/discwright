using DiscWright.Core;

namespace DiscWright.Tests;

public class TheMojibake
{
    [Theory]
    [InlineData("MicrosoftÂ® WindowsÂ® Operating System", "Microsoft® Windows® Operating System")]
    [InlineData("CafÃ©", "Café")]
    [InlineData("PokÃ©mon", "Pokémon")]
    [InlineData("â\u0080\u0098quotedâ\u0080\u0099", "‘quoted’")]
    public void Undoes_utf8_that_was_read_as_latin1(string arrived, string meant)
    {
        // Found on the first real set: Windows' own Character Map reports its
        // product name this way, and so do other people's installers.
        Assert.Equal(meant, Mojibake.Undone(arrived));
    }

    [Theory]
    [InlineData("Hollow Knight")]
    [InlineData("Café")]
    [InlineData("STAR WARS™: Empire at War")]
    [InlineData("Данные игры")]
    [InlineData("ゲームデータ")]
    [InlineData("")]
    public void Leaves_a_name_that_was_not_double_encoded_alone(string name)
    {
        // Strict on purpose: an é with nothing after it is not well formed
        // UTF-8, a ™ does not fit in a byte, and neither may be touched.
        Assert.Equal(name, Mojibake.Undone(name));
    }
}
