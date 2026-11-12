using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Suggesting somewhere to build, so nobody is refused for not having said.
/// </summary>
public class WhereADiscGetsBuilt
{
    [Fact]
    public void Names_the_folder_after_the_game()
    {
        WhereDiscsWouldGo places = new(@"C:\Users\x\Desktop", @"C:\Users\x", []);

        // v1 did exactly this and so "There is nowhere to put it" never
        // happened to anybody. The rewrite left the box empty.
        Assert.Equal(@"C:\Users\x\Desktop\Hollow Knight Disc", places.ForAGameCalled("Hollow Knight"));
    }

    [Fact]
    public void Keeps_away_from_the_desktop_when_the_desktop_is_in_the_cloud()
    {
        WhereDiscsWouldGo places = new(
            @"C:\Users\x\OneDrive\Desktop", @"C:\Users\x", [@"C:\Users\x\OneDrive"]);

        // Which is this machine. A redirected Desktop would hand a sync
        // client several gigabytes of ISO nobody asked it to upload.
        Assert.Equal(@"C:\Users\x\Hollow Knight Disc", places.ForAGameCalled("Hollow Knight"));
    }

    [Fact]
    public void Does_not_care_how_the_synced_folder_is_spelled()
    {
        WhereDiscsWouldGo places = new(
            @"C:\Users\x\OneDrive\Desktop", @"C:\Users\x", [@"c:\users\x\onedrive"]);

        Assert.Equal(@"C:\Users\x\Hollow Knight Disc", places.ForAGameCalled("Hollow Knight"));
    }

    [Fact]
    public void Uses_the_desktop_when_the_cloud_folder_is_somewhere_else()
    {
        WhereDiscsWouldGo places = new(
            @"C:\Users\x\Desktop", @"C:\Users\x", [@"C:\Users\x\OneDrive"]);

        // Having OneDrive is not the problem. The Desktop being inside it is.
        Assert.Equal(@"C:\Users\x\Desktop\Hollow Knight Disc", places.ForAGameCalled("Hollow Knight"));
    }

    [Theory]
    [InlineData("Baldur's Gate: Enhanced", "Baldur's Gate Enhanced Disc")]
    [InlineData("Where/Is/It", "Where Is It Disc")]
    [InlineData("  spaced   out  ", "spaced out Disc")]
    public void Takes_out_what_windows_will_not_have_in_a_folder_name(string game, string folder)
    {
        WhereDiscsWouldGo places = new(@"C:\d", @"C:\h", []);

        // Dropped rather than turned into underscores, because "Baldur_s
        // Gate" is somebody's folder for ever and "Baldurs Gate" is a name.
        Assert.Equal(@"C:\d\" + folder, places.ForAGameCalled(game));
    }

    [Fact]
    public void Suggests_nothing_rather_than_a_folder_with_no_name_in_it()
    {
        WhereDiscsWouldGo places = new(@"C:\d", @"C:\h", []);

        // A game called ":" leaves nothing at all, and " Disc" is not a
        // folder anybody would recognise as theirs.
        Assert.Null(places.ForAGameCalled(":"));
        Assert.Null(places.ForAGameCalled("   "));
    }

    [Fact]
    public void The_app_really_hands_over_the_folders_this_machine_has()
    {
        // The rule is tested above, over made up paths, and would be worth
        // nothing if the app handed it a blank one. Nothing in this project
        // can reference DiscWright.App, so this is read off disk like the
        // markup guards.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "App.xaml.cs"));

        // The call site, not just the method: naming it and then handing
        // the machine a null instead left every test above perfectly green
        // over an app that suggests nothing.
        Assert.Contains(
            "HowBigIs(disk)," + Environment.NewLine + "            WhereADiscWouldGo(),",
            code,
            StringComparison.Ordinal);
        Assert.Contains("SpecialFolder.DesktopDirectory", code, StringComparison.Ordinal);
        Assert.Contains("SpecialFolder.UserProfile", code, StringComparison.Ordinal);

        // The Desktop on the machine this was found on is inside OneDrive,
        // and without this the suggestion is a sync client's problem.
        Assert.Contains("OneDrive", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Suggests_nothing_when_there_is_nowhere_to_suggest()
    {
        WhereDiscsWouldGo nothing = new(string.Empty, string.Empty, []);

        Assert.Null(nothing.ForAGameCalled("Hollow Knight"));

        WhereDiscsWouldGo onlyCloud = new(@"C:\Users\x\OneDrive\Desktop", string.Empty, [@"C:\Users\x\OneDrive"]);

        // Better an empty box and the line that says to choose a folder than
        // a guess that drops gigabytes into a sync client.
        Assert.Null(onlyCloud.ForAGameCalled("Hollow Knight"));
    }
}
