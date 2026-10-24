using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// A project narrowed to some of its games, which is what every disc of a
/// disc-each build is made from.
/// </summary>
/// <remarks>
/// An add-on remembers its game by position. Cut down to one game with the
/// add-on list left whole, every disc offered the add-ons of whichever game
/// sat first, and its own, whose files were on its disc, were on no button.
/// The builder and the preview both cut projects down this way.
/// </remarks>
public class KeepingSomeOfTheGames
{
    private static readonly GameFolder One = AGame("One");
    private static readonly GameFolder Two = AGame("Two");
    private static readonly GameFolder Three = AGame("Three");

    private static readonly DiscProject Whole = new(
        "COMPILATION",
        [One, Two, Three],
        new DiscOptions(),
        Artwork: new DiscArtwork(IconPath: @"D:\icon.png"),
        Extras: new DiscExtras(ManualPath: @"D:\manual.pdf"),
        AddOns:
        [
            new AddOn(0, @"D:\one\patch_one.exe", "Update 2 (one)", 10),
            new AddOn(2, @"D:\three\patch_three.exe", "Update 2 (three)", 10),
            new AddOn(2, @"D:\three\setup_three_soundtrack.exe", "Three Soundtrack", 10)
        ]);

    [Fact]
    public void Keeps_the_add_ons_of_the_games_kept_and_points_them_at_where_those_games_sit_now()
    {
        DiscProject narrowed = Whole.KeepingOnly([Three], "Three");

        AddOn[] addOns = [.. narrowed.AddOnsOrNone];

        Assert.Equal(["Update 2 (three)", "Three Soundtrack"], addOns.Select(addOn => addOn.Name));
        Assert.All(addOns, addOn => Assert.Equal(0, addOn.BelongsTo));
        Assert.Equal(["Three Soundtrack", "Update 2 (three)"], narrowed.AddOnsFor(0).Select(addOn => addOn.Name).Order());
    }

    [Fact]
    public void Drops_the_add_ons_of_the_games_left_out()
    {
        DiscProject narrowed = Whole.KeepingOnly([Two], "Two");

        // Two has none of its own, and One's must not become Two's.
        Assert.Empty(narrowed.AddOnsOrNone);
    }

    [Fact]
    public void Renumbers_when_several_games_are_kept_out_of_order()
    {
        DiscProject narrowed = Whole.KeepingOnly([Three, One], "SOME");

        Assert.Equal(["Update 2 (three)", "Three Soundtrack"], narrowed.AddOnsFor(0).Select(addOn => addOn.Name));
        Assert.Equal(["Update 2 (one)"], narrowed.AddOnsFor(1).Select(addOn => addOn.Name));
    }

    [Fact]
    public void Takes_the_label_and_the_games_and_carries_everything_else()
    {
        DiscProject narrowed = Whole.KeepingOnly([Two], "Two");

        Assert.Equal("Two", narrowed.Label);
        Assert.Equal([Two], narrowed.Games);
        Assert.Equal(Whole.Artwork, narrowed.Artwork);
        Assert.Equal(Whole.Extras, narrowed.Extras);
        Assert.Equal(Whole.Options, narrowed.Options);
    }

    [Fact]
    public void Matches_a_game_by_which_folder_it_is_and_not_by_its_name()
    {
        // Two folders can report the same name, and a patch stays with the
        // folder it was taken from.
        GameFolder twin = AGame("One");
        DiscProject twins = Whole with { Games = [One, twin] };

        Assert.Equal(["Update 2 (one)"], twins.KeepingOnly([One], "One").AddOnsOrNone.Select(addOn => addOn.Name));
        Assert.Empty(twins.KeepingOnly([twin], "One").AddOnsOrNone);
    }

    private static GameFolder AGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, only, [only], [], null);
    }
}
