using DiscWright.Core;

namespace DiscWright.Tests;

public class TheAddOns
{
    [Theory]
    [InlineData("setup_grim_fandango_soundtrack_2.0.exe", "grim fandango soundtrack 2.0")]
    [InlineData("setup_hollow_knight_hidden_dreams.exe", "hollow knight hidden dreams")]
    public void Is_named_from_its_file_rather_than_from_inside_it(string file, string expected)
    {
        // Never from the product name inside the file. Every GOG patch reports
        // the product name of the GAME it patches, so all four Hollow Knight
        // patches call themselves "Hollow Knight", and a disc built from them
        // would offer four identical buttons.
        Assert.Equal(expected, AddOn.NameFor(file));
    }

    [Theory]
    [InlineData("patch_hollow_knight_1.5.12620_to_1.5.12700.exe", "Update 1.5.12700")]
    [InlineData("patch_the_witcher_2_3.3_to_3.4_(21034).exe", "Update 3.4 (21034)")]
    public void Is_named_after_the_version_a_patch_moves_to(string file, string expected)
    {
        // The version it moves TO is what tells two patches apart, and it goes
        // at the front where a button will not clip it.
        Assert.Equal(expected, AddOn.NameFor(file));
    }

    [Theory]
    [InlineData("Better Textures Overhaul.exe", "Better Textures Overhaul")]
    [InlineData("install.exe", "install")]
    public void Takes_a_name_that_follows_no_convention_at_all(string file, string expected)
    {
        // Any executable is accepted. The setup_ rule exists to identify a GOG
        // game FOLDER, and an add-on's identity comes from the game it was
        // attached to. That is what lets a mod or an overhaul, which is never
        // called setup_ anything, go on the disc at all.
        Assert.Equal(expected, AddOn.NameFor(file));
    }

    [Theory]
    [InlineData("___.exe")]
    [InlineData("_.exe")]
    public void Never_produces_a_button_with_no_words_on_it(string file)
    {
        // A file name that is nothing but underscores leaves an empty label,
        // and a button with no text is a button nobody can identify.
        Assert.NotEmpty(AddOn.NameFor(file));
    }

    [Fact]
    public void Collapses_the_runs_of_spaces_underscores_leave_behind()
    {
        Assert.Equal("a game", AddOn.NameFor("setup_a___game.exe"));
    }

    [Fact]
    public void Belongs_to_exactly_one_game()
    {
        DiscProject project = new(
            "TWO GAMES",
            [AGame("One"), AGame("Two")],
            new DiscOptions(),
            null,
            null,
            [
                new AddOn(0, @"C:\a\patch_one_1_to_2.exe", "Update 2", 100),
                new AddOn(1, @"C:\b\setup_two_soundtrack.exe", "two soundtrack", 200),
                new AddOn(0, @"C:\a\setup_one_dlc.exe", "one dlc", 300)
            ]);

        // A disc holding four games keeps each one's patches with it, rather
        // than in one pile at the root where nobody can tell which belongs to
        // what.
        Assert.Equal(["Update 2", "one dlc"], project.AddOnsFor(0).Select(addOn => addOn.Name));
        Assert.Equal(["two soundtrack"], project.AddOnsFor(1).Select(addOn => addOn.Name));
        Assert.Empty(project.AddOnsFor(2));
    }

    private static GameFolder AGame(string name) =>
        new(@"C:\games\" + name, GameKind.GogInstaller, name, name, null, [], [], null);
}
