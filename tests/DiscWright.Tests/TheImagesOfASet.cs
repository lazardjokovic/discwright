using DiscWright.Staging;

namespace DiscWright.Tests;

/// <summary>
/// Which images in a folder are the set named after a disc.
/// </summary>
/// <remarks>
/// For a set built in another sitting. The set walk, with its question about
/// carrying on from the disc the note says is next, only ever ran on a set
/// built moments before, because the window knew which images belonged
/// together only from the build. Opening a finished project and pressing Burn
/// got one image at a time from a list that did not say which were done.
/// </remarks>
public class TheImagesOfASet
{
    [Fact]
    public void Finds_the_discs_of_the_set_in_order_however_the_folder_lists_them()
    {
        string[] images =
        [
            @"C:\out\THE WITCHER D3.iso",
            @"C:\out\THE WITCHER D1.iso",
            @"C:\out\THE WITCHER D2.iso"
        ];

        Assert.Equal(
            [@"C:\out\THE WITCHER D1.iso", @"C:\out\THE WITCHER D2.iso", @"C:\out\THE WITCHER D3.iso"],
            TheImageOf.TheSetAmong(images, "THE WITCHER"));
    }

    [Fact]
    public void Leaves_out_images_of_other_discs_and_the_disc_that_stands_alone()
    {
        string[] images =
        [
            @"C:\out\THE WITCHER D1.iso",
            @"C:\out\THE WITCHER D2.iso",
            @"C:\out\THE WITCHER.iso",
            @"C:\out\GOTHIC D1.iso",
            @"C:\out\GOTHIC D2.iso"
        ];

        Assert.Equal(
            [@"C:\out\THE WITCHER D1.iso", @"C:\out\THE WITCHER D2.iso"],
            TheImageOf.TheSetAmong(images, "THE WITCHER"));
    }

    [Fact]
    public void A_set_starts_at_one_and_has_no_gap()
    {
        // "D1, D3" is two sets' leftovers or a disc gone missing, and neither
        // is a set to walk.
        Assert.Empty(TheImageOf.TheSetAmong([@"C:\out\A D1.iso", @"C:\out\A D3.iso"], "A"));
        Assert.Empty(TheImageOf.TheSetAmong([@"C:\out\A D2.iso", @"C:\out\A D3.iso"], "A"));
    }

    [Fact]
    public void One_disc_is_not_a_set()
    {
        Assert.Empty(TheImageOf.TheSetAmong([@"C:\out\A D1.iso"], "A"));
        Assert.Empty(TheImageOf.TheSetAmong([@"C:\out\A.iso"], "A"));
        Assert.Empty(TheImageOf.TheSetAmong([], "A"));
    }

    [Fact]
    public void Matches_the_name_the_way_the_image_was_named()
    {
        // A colon cannot be in a file name, so the image was named with the
        // label tidied, and the same tidying has to be applied to match it.
        Assert.Equal(
            [@"C:\out\Command & Conquer  Red Alert D1.iso", @"C:\out\Command & Conquer  Red Alert D2.iso"],
            TheImageOf.TheSetAmong(
                [@"C:\out\Command & Conquer  Red Alert D1.iso", @"C:\out\Command & Conquer  Red Alert D2.iso"],
                "Command & Conquer: Red Alert"));

        // And without minding case, the way Windows names files.
        Assert.Equal(2, TheImageOf.TheSetAmong([@"C:\out\a game d1.iso", @"C:\out\A GAME D2.iso"], "A Game").Count);
    }

    [Fact]
    public void Names_the_set_the_way_a_built_disc_is_named_so_the_two_cannot_drift()
    {
        BuiltDisc[] set =
        [
            new("A GAME", 1, 2, @"C:\out\disc D1", 10),
            new("A GAME", 2, 2, @"C:\out\disc D2", 10)
        ];

        string[] built = [.. set.Select(TheImageOf.That)];

        Assert.Equal(built, TheImageOf.TheSetAmong(built, "A GAME"));
    }
}
