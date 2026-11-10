using DiscWright.Core;
using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// Reading what is directly in a folder without going down into it.
/// </summary>
/// <remarks>
/// The window asks this every time the folder box changes, which is every
/// keystroke. Going down a level there is what made typing <c>C:\</c> walk the
/// whole drive, so the real disk is held to not going down, and the default
/// the test fakes inherit is held to the same answer.
/// </remarks>
public class ListingOnlyTheFolderItself
{
    [Fact]
    public void The_real_disk_gives_back_the_folders_own_files_and_none_from_below()
    {
        string folder = Path.Combine(Path.GetTempPath(), "dw-here-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "disc", "AUTORUN"));
        File.WriteAllText(Path.Combine(folder, "HOLLOW KNIGHT.iso"), "a");
        File.WriteAllText(Path.Combine(folder, "discproject.json"), "bb");
        File.WriteAllText(Path.Combine(folder, "disc", "OLDER.iso"), "ccc");
        File.WriteAllText(Path.Combine(folder, "disc", "AUTORUN", "menu.hta"), "dddd");

        try
        {
            WindowsFileSystem disk = new();

            string[] here = [.. disk.ListHere(folder).Files.Select(file => file.RelativePath).Order(StringComparer.Ordinal)];
            string[] all = [.. disk.List(folder).Files.Select(file => file.RelativePath).Order(StringComparer.Ordinal)];

            Assert.Equal(["HOLLOW KNIGHT.iso", "discproject.json"], here);

            // And the full listing still goes all the way down, because
            // putting a game on a disc needs every file in it.
            Assert.Equal(4, all.Length);
            Assert.Contains(Path.Combine("disc", "AUTORUN", "menu.hta"), all);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void The_default_drops_everything_below_whichever_way_its_path_is_written()
    {
        IFileSystem disk = new AFolderWithThingsBelowIt();

        string[] here = [.. disk.ListHere(@"C:\Discs").Files.Select(file => file.RelativePath)];

        Assert.Equal(["A.iso"], here);
    }

    private sealed class AFolderWithThingsBelowIt : IFileSystem
    {
        public bool FolderExists(string path) => true;

        public FolderListing List(string path) => new(
            path,
            [
                new ListedFile("A.iso", 1),
                new ListedFile(@"disc\B.iso", 1),
                new ListedFile("disc/C.iso", 1)
            ]);
    }
}
