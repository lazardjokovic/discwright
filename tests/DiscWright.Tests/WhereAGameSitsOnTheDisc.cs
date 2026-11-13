using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Whether a game sits at the root of the disc or in a folder of its own.
/// </summary>
/// <remarks>
/// One home for this, because the builder and the menu both have to answer it
/// and a disc where they disagree is a menu pointing at nothing.
/// </remarks>
public class WhereAGameSitsOnTheDisc
{
    [Fact]
    public void One_game_is_the_disc_so_it_sits_at_the_root_of_it()
    {
        Assert.Equal(string.Empty, OnTheDisc.FolderFor(1, "Hollow Knight", 1, ["setup_hollow_knight.exe"]));
    }

    [Fact]
    public void A_lone_game_with_extras_of_its_own_gets_a_folder_and_with_only_a_manual_keeps_the_root()
    {
        ListedFile setup = new("setup_hollow_knight.exe", 10);
        GameFolder game = new(@"C:\g", GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", setup, [setup], [], null);

        // Its own Extras would meet the disc's at the root. Manual belongs to
        // nothing else there, so a manual alone moves nothing.
        Assert.Equal(
            "Hollow Knight",
            OnTheDisc.FolderFor(1, game with { Own = [new GameExtra(@"C:\x\Artbook", GameExtraKind.Extra)] }, 1));
        Assert.Equal(
            string.Empty,
            OnTheDisc.FolderFor(1, game with { Own = [new GameExtra(@"C:\x\manual.pdf", GameExtraKind.Manual)] }, 1));
        Assert.Equal(string.Empty, OnTheDisc.FolderFor(1, game, 1));
        Assert.Equal("02 - Hollow Knight", OnTheDisc.FolderFor(2, game, 3));
    }

    [Fact]
    public void Several_games_each_get_a_numbered_folder()
    {
        Assert.Equal("01 - Gothic", OnTheDisc.FolderFor(1, "Gothic", 3, ["setup_gothic.exe"]));
        Assert.Equal("02 - Arcanum", OnTheDisc.FolderFor(2, "Arcanum", 3, ["setup_arcanum.exe"]));
    }

    [Theory]
    [InlineData("autorun.inf")]
    [InlineData("AUTORUN.INF")]
    [InlineData("disc.ico")]
    [InlineData("checksums.sha256")]
    [InlineData("Disc set.txt")]
    public void A_lone_game_carrying_a_file_the_disc_writes_gets_a_folder_of_its_own(string name)
    {
        // Otherwise the disc writes its own over the top and nobody is told.
        // The disc builds, it burns, and it is missing something somebody
        // chose to put on it.
        Assert.Equal(
            "Old Game",
            OnTheDisc.FolderFor(1, "Old Game", 1, ["setup_old_game.exe", name]));
    }

    [Theory]
    [InlineData("AUTORUN")]
    [InlineData("ARTWORK")]
    [InlineData("Extras")]
    [InlineData("Games")]
    [InlineData("Add-ons")]
    public void A_lone_game_carrying_a_folder_the_disc_writes_gets_a_folder_of_its_own(string folder)
    {
        // A game with its own AUTORUN folder would otherwise have its contents
        // mixed in beside the menu.
        Assert.Equal(
            "A Game",
            OnTheDisc.FolderFor(1, "A Game", 1, ["setup_a_game.exe", folder + @"\something.dat"]));
    }

    [Fact]
    public void Minds_only_the_root_of_the_disc()
    {
        // A file called autorun.inf inside a folder of the game's own collides
        // with nothing, and a game should not be moved for it.
        Assert.Equal(
            string.Empty,
            OnTheDisc.FolderFor(1, "A Game", 1, ["setup_a_game.exe", @"data\autorun.inf"]));
    }

    [Fact]
    public void Does_not_move_a_game_for_being_called_setup_something()
    {
        // ReservedDiscNames.Claims refuses anything beginning setup_, which is
        // right for a loose extra somebody dropped on the disc and would be
        // catastrophic here: every GOG installer ever made begins setup_, so
        // every game on earth would be given a folder it does not need.
        Assert.Equal(
            string.Empty,
            OnTheDisc.FolderFor(1, "Hollow Knight", 1, ["setup_hollow_knight_1.5.12620.exe"]));
    }

    [Fact]
    public void Gives_it_a_name_that_can_be_opened_again()
    {
        Assert.Equal(
            "A Game",
            OnTheDisc.FolderFor(1, "A Game...  ", 1, ["autorun.inf"]));
    }
}
