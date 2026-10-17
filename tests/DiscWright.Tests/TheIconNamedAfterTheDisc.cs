using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// What a disc's icon file is called.
/// </summary>
/// <remarks>
/// The whole point of the rule is that two discs never arrive at the same
/// answer, because Explorer caches icons by path and a shared name means the
/// second disc into a drive letter wears the first one's picture. So most of
/// what is checked here is two labels coming out different.
/// </remarks>
public sealed class TheIconNamedAfterTheDisc
{
    [Fact]
    public void Is_the_label_with_nothing_in_it_but_letters_and_digits()
    {
        Assert.Equal("HollowKnight.ico", DiscIconName.For("Hollow Knight"));
        Assert.Equal("CommandConquerRedAlert.ico", DiscIconName.For("Command & Conquer: Red Alert"));
        Assert.Equal("Quake3Arena.ico", DiscIconName.For("Quake 3 Arena"));
    }

    [Fact]
    public void Reduces_an_accented_letter_rather_than_throwing_it_away()
    {
        // v1 found this one the hard way. Stripping before folding deletes an
        // accented letter outright, because it is not in A to Z, and the
        // Polish spelling of Wiedzmin came out "Wiedmin" with the z missing.
        Assert.Equal("Wiedzmin.ico", DiscIconName.For("Wiedźmin"));
        Assert.Equal("Pokemon.ico", DiscIconName.For("Pokémon"));
    }

    [Fact]
    public void Is_never_longer_than_forty_characters_and_the_extension()
    {
        string name = DiscIconName.For(new string('A', 200));

        Assert.Equal(44, name.Length);
        Assert.Equal(new string('A', 40) + ".ico", name);
    }

    [Fact]
    public void Gives_each_disc_of_a_set_its_own_name()
    {
        // The reason the whole rule exists, in one line: disc one and disc two
        // go into the same drive letter one after the other.
        Assert.Equal("HollowKnight1.ico", DiscIconName.For("Hollow Knight", 1, 2));
        Assert.Equal("HollowKnight2.ico", DiscIconName.For("Hollow Knight", 2, 2));
    }

    [Fact]
    public void Says_no_number_at_all_for_a_disc_that_is_on_its_own()
    {
        Assert.Equal("HollowKnight.ico", DiscIconName.For("Hollow Knight", 1, 1));
    }

    [Fact]
    public void Keeps_room_for_the_number_when_the_label_fills_the_whole_name()
    {
        // Cut to forty and THEN number it and the name is forty-one. Number it
        // and then cut to forty and the number comes off the end, which puts
        // every disc of the set back on one name: the exact bug the volume
        // name already had to be fixed for.
        string one = DiscIconName.For(new string('A', 200), 1, 2);
        string two = DiscIconName.For(new string('A', 200), 2, 2);

        Assert.Equal(new string('A', 39) + "1.ico", one);
        Assert.Equal(new string('A', 39) + "2.ico", two);
        Assert.NotEqual(one, two);
    }

    [Fact]
    public void Still_tells_two_discs_apart_when_a_set_runs_past_nine()
    {
        string nine = DiscIconName.For(new string('A', 200), 9, 12);
        string twelve = DiscIconName.For(new string('A', 200), 12, 12);

        Assert.NotEqual(nine, twelve);
        Assert.EndsWith("9.ico", nine, StringComparison.Ordinal);
        Assert.EndsWith("12.ico", twelve, StringComparison.Ordinal);
    }

    [Fact]
    public void Hashes_a_label_that_has_no_Latin_letters_in_it_at_all()
    {
        string cyrillic = DiscIconName.For("Ведьмак");
        string japanese = DiscIconName.For("ポケモン");

        // Not "disc.ico". Falling back to a constant here would put every
        // disc with a non-Latin name back onto one shared cache key, which is
        // the thing this is here to prevent.
        Assert.StartsWith("disc", cyrillic, StringComparison.Ordinal);
        Assert.EndsWith(".ico", cyrillic, StringComparison.Ordinal);
        Assert.NotEqual("disc.ico", cyrillic);
        Assert.NotEqual(cyrillic, japanese);
    }

    [Fact]
    public void Hashes_a_label_that_is_nothing_but_punctuation_too()
    {
        Assert.NotEqual("disc.ico", DiscIconName.For("!!! ??? ..."));
        Assert.NotEqual(DiscIconName.For("!!!"), DiscIconName.For("???"));
    }

    [Fact]
    public void Says_the_same_thing_every_time_it_is_asked()
    {
        // It has to. The name is written into autorun.inf by one step and the
        // file is written by another, and a name that wandered between the two
        // would give every disc an icon entry pointing at nothing.
        Assert.Equal(DiscIconName.For("ポケモン"), DiscIconName.For("ポケモン"));
        Assert.Equal(DiscIconName.For("Hollow Knight", 2, 3), DiscIconName.For("Hollow Knight", 2, 3));
    }

    [Fact]
    public void Copes_with_a_label_that_is_not_there()
    {
        string name = DiscIconName.For(null);

        Assert.EndsWith(".ico", name, StringComparison.Ordinal);
        Assert.NotEqual(".ico", name);
    }

    [Fact]
    public void Knows_its_own_name_when_a_loose_extra_tries_to_use_it()
    {
        Assert.True(DiscIconName.Claims("HollowKnight.ico", "Hollow Knight"));

        // The same file, shouted. Windows does not care and neither does this.
        Assert.True(DiscIconName.Claims("HOLLOWKNIGHT.ICO", "Hollow Knight"));

        Assert.False(DiscIconName.Claims("HollowKnight.png", "Hollow Knight"));
        Assert.False(DiscIconName.Claims("something else.ico", "Hollow Knight"));
    }

    [Fact]
    public void Is_refused_as_a_loose_extra_by_the_name_the_disc_keeps()
    {
        Assert.True(ReservedDiscNames.Claims("HollowKnight.ico", "Hollow Knight"));

        // And the fixed names are still refused with no label to go on, which
        // is how everything that cannot know the label asks.
        Assert.True(ReservedDiscNames.Claims("autorun.inf"));
        Assert.True(ReservedDiscNames.Claims("disc.ico"));
        Assert.False(ReservedDiscNames.Claims("HollowKnight.ico"));
    }
}
