using DiscWright.Core;
using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// Previews nobody has looked at in a week going away on their own.
/// </summary>
/// <remarks>
/// <para>
/// One folder per project stopped them multiplying. This stops them
/// collecting: fifty projects over a year is fifty folders, and nothing in
/// the program would ever have removed one.
/// </para>
/// <para>
/// The walk is tested on real files in a folder of this test's own, rather
/// than against a pretend disk. The last thing that went wrong here was a
/// fake being kinder than the real thing, and the whole point of this code is
/// that it deletes folders.
/// </para>
/// </remarks>
public class TidyingUpOldPreviews : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Local);

    private readonly string _temp = Path.Combine(
        Path.GetTempPath(), "DiscWright tidying test " + Guid.NewGuid().ToString("N")[..8]);

    private string Root => Path.Combine(_temp, WhereAPreviewGoes.TheFolderTheyAllGoIn);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_temp))
            {
                Directory.Delete(_temp, recursive: true);
            }
        }
        catch (IOException)
        {
        }

        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(400, true)]
    public void A_week_is_how_long_one_is_left_alone(int daysAgo, bool shouldGo) =>
        Assert.Equal(shouldGo, OldPreviews.HadItsDay(Now.AddDays(-daysAgo), Now));

    [Fact]
    public void A_folder_dated_in_the_future_is_left_alone()
    {
        // A clock that has gone backwards should not cost somebody a folder.
        // Not knowing how old a thing is, is a reason to leave it.
        Assert.False(OldPreviews.HadItsDay(Now.AddDays(3), Now));
    }

    [Fact]
    public void Old_ones_go_and_recent_ones_stay()
    {
        APreviewFolder("HOLLOW KNIGHT-aaaa1111", 30);
        APreviewFolder("GOTHIC-bbbb2222", 8);
        APreviewFolder("ARCANUM-cccc3333", 2);
        APreviewFolder("FALLOUT-dddd4444", 0);

        int went = TidyingOldPreviews.In(_temp, Now);

        Assert.Equal(2, went);
        Assert.Equal(
            ["ARCANUM-cccc3333", "FALLOUT-dddd4444"],
            Directory.GetDirectories(Root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void What_is_inside_goes_with_it()
    {
        string old = APreviewFolder("HOLLOW KNIGHT-aaaa1111", 30);
        Directory.CreateDirectory(Path.Combine(old, "AUTORUN"));
        File.WriteAllText(Path.Combine(old, "AUTORUN", "menu.hta"), "<html></html>");
        Directory.SetLastWriteTime(old, Now.AddDays(-30));

        TidyingOldPreviews.In(_temp, Now);

        Assert.False(Directory.Exists(old));
    }

    [Fact]
    public void The_root_they_all_live_in_is_never_removed()
    {
        APreviewFolder("HOLLOW KNIGHT-aaaa1111", 30);

        TidyingOldPreviews.In(_temp, Now);

        // Every other project's preview lives in here. Taking it would be a
        // far bigger thing than the tidying it was asked for.
        Assert.True(Directory.Exists(Root));
    }

    [Fact]
    public void It_does_nothing_at_all_when_there_is_nothing_there()
    {
        // The ordinary case on a machine that has never previewed anything,
        // and it runs at startup, so it has to be quiet about it.
        Assert.Equal(0, TidyingOldPreviews.In(_temp, Now));
    }

    [Fact]
    public void It_never_looks_outside_the_folder_previews_live_in()
    {
        // This is the whole of the safety here, and it is worth being
        // plain about what it is: the walk is scoped to one folder, so
        // nothing beside that folder is ever a candidate, however old.
        //
        // It was written as though a second check inside the loop were
        // doing the work. It was not, and could not: everything in that
        // loop is a child of the root and so passes any such check. The
        // scope is the guard.
        Directory.CreateDirectory(Root);
        string beside = Path.Combine(_temp, "somebody elses old folder");
        Directory.CreateDirectory(beside);
        Directory.SetLastWriteTime(beside, Now.AddDays(-400));

        TidyingOldPreviews.In(_temp, Now);

        Assert.True(Directory.Exists(beside));
    }

    [Fact]
    public void The_program_actually_does_this_when_it_starts()
    {
        // Everything above proves the tidying works. Nothing above proves
        // anybody ever calls it, and the one line that does is in a file no
        // test can reference. Read off disk, like the guard on the window.
        string code = File.ReadAllText(
            Repository.File("src", "DiscWright.App", "App.xaml.cs"));

        Assert.Contains("TidyingOldPreviews.In(", code, StringComparison.Ordinal);
    }

    private string APreviewFolder(string name, int daysAgo)
    {
        string path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "Start Here.hta"), "<html></html>");
        Directory.SetLastWriteTime(path, Now.AddDays(-daysAgo));

        return path;
    }
}
