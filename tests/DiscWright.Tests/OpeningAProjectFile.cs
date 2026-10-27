using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Reading the file a disc carries that says how it was made.
/// </summary>
/// <remarks>
/// Every default here is the behaviour of the version that lacked the field,
/// not a value that seemed sensible, so each of these is really a question
/// about a disc somebody already owns. The discs the PowerShell DiscWright
/// built are the other kind of question: recognised, and refused by name.
/// </remarks>
public class OpeningAProjectFile
{
    [Fact]
    public void Reads_what_a_recent_one_says()
    {
        OpenedProject opened = Open("""
            {
              "Version": 14,
              "AppVersion": "0.10.0",
              "Label": "HOLLOW KNIGHT",
              "Games": [{ "Folder": "D:\\games\\Hollow Knight", "GameName": "Hollow Knight",
                          "MatchName": "Hollow Knight", "Setup": "D:\\games\\Hollow Knight\\setup.exe",
                          "Kind": "Game", "Source": "Files", "Parent": -1 }],
              "Menu": true, "Checksums": true, "ArtworkOnDisc": false,
              "LegacyFs": true, "CutFiles": false, "DiscSet": false,
              "MediaKey": "DVD5", "OutDir": "D:\\out"
            }
            """);

        Assert.Equal(14, opened.Version);
        Assert.Equal("0.10.0", opened.MadeBy);
        Assert.Equal("HOLLOW KNIGHT", opened.Label);
        Assert.Equal(@"D:\out", opened.OutputFolder);

        ProjectEntry game = Assert.Single(opened.Entries);
        Assert.Equal(@"D:\games\Hollow Knight", game.Folder);
        Assert.Equal("Hollow Knight", game.Name);
        Assert.False(game.IsAnAddOn);

        Assert.True(opened.Options.Menu);
        Assert.True(opened.Options.LegacyFileSystems);
        Assert.False(opened.Options.CutFiles);
        Assert.Equal(Media.Dvd5, opened.Medium);
    }

    [Fact]
    public void Reads_the_very_first_shape_that_held_one_game_and_no_list()
    {
        // Version 1 kept the game in SourceFolder. It comes back as the same
        // kind of list as everything else, so nothing downstream has to know
        // which version it was reading.
        OpenedProject opened = Open("""
            { "SourceFolder": "D:\\games\\Gothic", "GameName": "Gothic", "Label": "GOTHIC" }
            """);

        ProjectEntry game = Assert.Single(opened.Entries);
        Assert.Equal(@"D:\games\Gothic", game.Folder);
        Assert.Equal("Gothic", game.Name);
        Assert.Equal(1, opened.Version);
    }

    [Fact]
    public void Takes_a_file_with_no_kind_on_its_entries_as_all_games()
    {
        // Version 3 gave an entry a kind and a parent. Without them, every
        // entry is a plain game, which is what those discs held.
        OpenedProject opened = Open("""
            { "Games": [{ "Folder": "D:\\a" }, { "Folder": "D:\\b" }] }
            """);

        Assert.All(opened.Entries, entry => Assert.False(entry.IsAnAddOn));
        Assert.All(opened.Entries, entry => Assert.Equal(-1, entry.BelongsTo));
        Assert.Equal(2, opened.Games.Count());
    }

    [Fact]
    public void Keeps_an_add_on_with_the_game_it_was_attached_to()
    {
        OpenedProject opened = Open("""
            { "Games": [{ "Folder": "D:\\game" },
                        { "Folder": "D:\\patch", "Kind": "AddOn", "Parent": 0 }] }
            """);

        Assert.Single(opened.Games);
        ProjectEntry addOn = Assert.Single(opened.AddOnsFor(0));
        Assert.Equal(@"D:\patch", addOn.Folder);
    }

    [Fact]
    public void Takes_an_entry_with_no_source_as_a_gog_download()
    {
        // Version 9 added Source. Anything older could only hold GOG
        // downloads, so a file without it describes one.
        OpenedProject opened = Open("""{ "Games": [{ "Folder": "D:\\a" }] }""");

        Assert.True(Assert.Single(opened.Entries).WasAGogDownload);
    }

    [Fact]
    public void Leaves_a_name_to_be_found_again_when_the_file_never_recorded_one()
    {
        // Version 6 added MatchName. Older files have none, which leaves the
        // name found by reading the folder, and that is the registered one. So
        // a project renamed under the old code repairs itself on opening.
        OpenedProject opened = Open("""{ "Games": [{ "Folder": "D:\\a", "GameName": "Whatever" }] }""");

        Assert.Null(Assert.Single(opened.Entries).MatchName);
    }

    [Fact]
    public void Leaves_the_disc_size_automatic_when_the_file_never_chose_one()
    {
        // Version 5. Absent is the automatic setting, which is what those
        // projects always described.
        OpenedProject opened = Open("""{ "Games": [{ "Folder": "D:\\a" }] }""");

        Assert.Equal(string.Empty, opened.MediaKey);
        Assert.Null(opened.Medium);
    }

    // ------------------------------------- a disc the PowerShell DiscWright built

    [Fact]
    public void Refuses_a_project_the_PowerShell_app_wrote_and_says_which_one_wrote_it()
    {
        // Shaped as v1 writes it, with the look of its menu in it, which this
        // version's menu has no home for. Until 2026-10-27 all of that was
        // read and thrown away, and the disc that came back was not the disc
        // the file described.
        Outcome<OpenedProject> read = AProjectFile.Read("""
            {
              "Version": 14, "AppVersion": "0.10.0", "SavedUtc": "2026-10-18T09:00:00",
              "Games": [{ "Folder": "D:\\games\\Hollow Knight", "GameName": "Hollow Knight",
                          "Kind": "Game", "Source": "GOG", "Parent": -1, "Manual": null, "Extras": null }],
              "Label": "HOLLOW KNIGHT",
              "PanelSide": "Right", "Divider": false, "ShowTitle": false, "TitleText": "",
              "WindowBorder": true, "ButtonStyle": "Bordered", "ShowCaption": true,
              "Buttons": ["Install", "Manual", "Exit"],
              "Menu": true, "LinuxInfo": false, "MediaKey": "DVD5"
            }
            """);

        Refusal why = Assert.IsType<Outcome<OpenedProject>.No>(read).Refusal;

        Assert.Equal("That project is from the PowerShell DiscWright", why.Headline);
        Assert.Contains("written by DiscWright 0.10.0, the PowerShell version", why.Because, StringComparison.Ordinal);
        Assert.Contains("cannot be opened here", why.Because, StringComparison.Ordinal);
        Assert.Null(why.Remedy);
    }

    [Theory]
    [InlineData("PanelSide", "\"Right\"")]
    [InlineData("ButtonStyle", "\"Bordered\"")]
    [InlineData("Buttons", "[]")]
    [InlineData("Divider", "false")]
    [InlineData("ShowTitle", "false")]
    [InlineData("TitleText", "\"\"")]
    [InlineData("WindowBorder", "true")]
    [InlineData("ShowCaption", "true")]
    public void Knows_the_PowerShell_app_by_any_one_key_of_the_look_it_writes(string key, string value)
    {
        // v1 writes all of them, every time, since 0.1.0; this version writes
        // none, because its menu has none. Any one is the other program's hand,
        // whatever it is set to: a file with the default look in it is still
        // a file describing a look.
        Outcome<OpenedProject> read = AProjectFile.Read(
            $$"""{ "Games": [{ "Folder": "D:\\a" }], "{{key}}": {{value}} }""");

        Assert.Contains("PowerShell", Assert.IsType<Outcome<OpenedProject>.No>(read).Refusal.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_the_PowerShell_version_wrote_it_even_when_the_file_does_not_say_which()
    {
        Outcome<OpenedProject> read = AProjectFile.Read("""{ "Games": [{ "Folder": "D:\\a" }], "PanelSide": "Left" }""");

        string because = Assert.IsType<Outcome<OpenedProject>.No>(read).Refusal.Because;

        Assert.Contains("written by the PowerShell version of DiscWright", because, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscWright ,", because, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_the_PowerShell_app_before_anything_else_it_might_say_about_the_file()
    {
        // No games in it either, and that is the lesser fact: a file from the
        // other program is named as one however else it is shaped.
        Outcome<OpenedProject> read = AProjectFile.Read("""{ "Version": 14, "Games": [], "ButtonStyle": "Flat" }""");

        Assert.Contains("PowerShell", Assert.IsType<Outcome<OpenedProject>.No>(read).Refusal.Headline, StringComparison.Ordinal);
    }

    [Fact]
    public void Does_not_mistake_a_file_this_version_wrote_for_the_PowerShell_apps()
    {
        // The two programs stamp the same schema and carry on the same version
        // numbers, so the only thing telling them apart is the look, and this
        // writer must go on writing none of it. Written and read back here,
        // so that the writer gaining a look key fails this rather than
        // refusing every disc this version has built.
        DiscProject project = new(
            "GOTHIC",
            [new GameFolder(
                @"D:\games\Gothic", GameKind.GogInstaller, "Gothic", "Gothic",
                new ListedFile("setup_gothic.exe", 1000), [new ListedFile("setup_gothic.exe", 1000)], [], null)],
            new DiscOptions(),
            Extras: new DiscExtras(null, null, [], @"D:\tune.mp3"));

        string json = TheProjectAsJson.Of(project, "0.12.0", "DVD5", @"D:\out", DateTimeOffset.UnixEpoch);

        OpenedProject back = Open(json);

        Assert.Equal("0.12.0", back.MadeBy);
        Assert.Equal(@"D:\tune.mp3", back.MusicPath);
    }

    [Fact]
    public void Keeps_the_artwork_and_the_extras_somebody_pointed_at()
    {
        OpenedProject opened = Open("""
            {
              "Games": [{ "Folder": "D:\\a" }],
              "IconPath": "D:\\art\\icon.png", "IconIsIco": false,
              "BgPath": "D:\\art\\bg.png", "BgAsIs": true,
              "CoverPath": null, "DiscArtPath": "D:\\art\\face.png",
              "ManualPath": "D:\\a\\manual.pdf", "ExtrasPath": "D:\\a\\extras",
              "ExtraItems": ["D:\\read me.txt", "", "D:\\also.txt"]
            }
            """);

        Assert.Equal(@"D:\art\icon.png", opened.Artwork.IconPath);
        Assert.True(opened.Artwork.BackgroundAsItIs);
        Assert.Null(opened.Artwork.CoverPath);
        Assert.Equal(@"D:\art\face.png", opened.Artwork.DiscFacePath);
        Assert.Equal(@"D:\a\manual.pdf", opened.ManualPath);

        // The empty one is dropped: a path to nowhere is not an extra.
        Assert.Equal([@"D:\read me.txt", @"D:\also.txt"], opened.LooseExtras);
    }

    [Fact]
    public void Skips_an_entry_with_nowhere_to_read_from()
    {
        OpenedProject opened = Open("""
            { "Games": [{ "GameName": "nothing to read" }, { "Folder": "D:\\real" }] }
            """);

        Assert.Equal(@"D:\real", Assert.Single(opened.Entries).Folder);
    }

    [Fact]
    public void Refuses_something_that_is_not_a_project_at_all()
    {
        Assert.IsType<Outcome<OpenedProject>.No>(AProjectFile.Read("this is not json"));
        Assert.IsType<Outcome<OpenedProject>.No>(AProjectFile.Read("[1, 2, 3]"));
    }

    [Fact]
    public void Refuses_a_project_with_nothing_in_it_rather_than_opening_an_empty_disc()
    {
        // Refused whole rather than read in part: half a project is a disc that
        // rebuilds without something somebody chose.
        Outcome<OpenedProject> read = AProjectFile.Read("""{ "Version": 14, "Label": "EMPTY", "Games": [] }""");

        Refusal why = Assert.IsType<Outcome<OpenedProject>.No>(read).Refusal;
        Assert.Contains("no games", why.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reads_a_file_that_starts_with_a_byte_order_mark()
    {
        // Windows PowerShell writes one, and v1 is Windows PowerShell.
        OpenedProject opened = Open("\uFEFF" + """{ "Games": [{ "Folder": "D:\\a" }], "Label": "MARKED" }""");

        Assert.Equal("MARKED", opened.Label);
    }

    private static OpenedProject Open(string json) =>
        Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(json)).Value;
}
