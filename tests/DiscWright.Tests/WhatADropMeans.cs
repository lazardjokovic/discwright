using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// What a drop onto the list of games means, which is the one piece of
/// judgement in the whole gesture: everything after it is the same road the
/// Add a game button takes.
/// </summary>
public class WhatADropMeans
{
    [Fact]
    public void A_folder_means_itself()
    {
        IReadOnlyList<string> folders = WhatWasDropped.AsFolders(
            [@"C:\games\Hollow Knight"], _ => true);

        Assert.Equal([@"C:\games\Hollow Knight"], folders);
    }

    [Fact]
    public void A_file_means_the_folder_it_is_in()
    {
        // The likeliest drag of all, discwright#98: the setup exe itself,
        // pulled straight out of a Downloads window.
        IReadOnlyList<string> folders = WhatWasDropped.AsFolders(
            [@"C:\games\Hollow Knight\setup_hollow_knight.exe"], _ => false);

        Assert.Equal([@"C:\games\Hollow Knight"], folders);
    }

    [Fact]
    public void Two_files_out_of_one_folder_are_one_game()
    {
        // The installer and its bin part land together, and they are one
        // game, not two. Spelled with different cases, still one: Windows
        // paths do not care and neither may this.
        IReadOnlyList<string> folders = WhatWasDropped.AsFolders(
            [
                @"C:\games\Hollow Knight\setup_hollow_knight.exe",
                @"C:\GAMES\HOLLOW KNIGHT\setup_hollow_knight-1.bin"
            ],
            _ => false);

        Assert.Single(folders);
    }

    [Fact]
    public void Folders_arrive_in_the_order_they_were_dropped()
    {
        IReadOnlyList<string> folders = WhatWasDropped.AsFolders(
            [@"C:\b\setup_b.exe", @"C:\a", @"C:\b"],
            path => !path.EndsWith(".exe", StringComparison.Ordinal));

        Assert.Equal([@"C:\b", @"C:\a"], folders);
    }

    [Fact]
    public void A_bare_name_with_no_folder_part_is_nothing()
    {
        // "setup.exe" alone points nowhere a game could be, and a drop is
        // not a place to explain that from.
        IReadOnlyList<string> folders = WhatWasDropped.AsFolders(["setup.exe"], _ => false);

        Assert.Empty(folders);
    }
}
