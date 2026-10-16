using DiscWright.Core;
using DiscWright.Core.Staging;

namespace DiscWright.Tests;

/// <summary>
/// Writing <c>discproject.json</c>, and getting the same project back.
/// </summary>
/// <remarks>
/// The round trip is the only guard worth having here. A writer tested against
/// a list of expected field names agrees with whatever the test author believed
/// the reader wanted, which is how a file that nothing can open gets written
/// with full confidence. This writes a project and reads it back with the real
/// reader, so the two cannot drift apart without a test noticing.
/// </remarks>
public class TheProjectFileComesBack
{
    [Fact]
    public void What_was_written_reads_back_as_the_same_project()
    {
        DiscProject was = AProject();

        OpenedProject back = Written(was);

        Assert.Equal("A BIG GAME", back.Label);
        Assert.Equal(TheProjectAsJson.Version, back.Version);
        Assert.Equal("2.0.0", back.MadeBy);
        Assert.Equal("CD", back.MediaKey);
        Assert.Equal(@"F:\out", back.OutputFolder);
    }

    [Fact]
    public void Every_option_comes_back_the_way_it_went_in()
    {
        DiscProject was = AProject() with
        {
            Options = new DiscOptions(
                Menu: true,
                Checksums: false,
                ArtworkOnDisc: true,
                LegacyFileSystems: true,
                CutFiles: true)
        };

        DiscOptions back = Written(was).Options;

        // Written even when they carry a default, because the reader treats a
        // missing field as "what the version that lacked it did". A writer
        // that leaves out whatever happens to be false is writing a file that
        // means something else.
        Assert.True(back.Menu);
        Assert.False(back.Checksums);
        Assert.True(back.ArtworkOnDisc);
        Assert.True(back.LegacyFileSystems);
        Assert.True(back.CutFiles);
    }

    [Fact]
    public void A_game_comes_back_with_its_folder_its_name_and_its_installer()
    {
        OpenedProject back = Written(AProject());

        ProjectEntry game = back.Entries.Single(each => !each.IsAnAddOn);

        Assert.Equal(@"C:\games\A Big Game", game.Folder);
        Assert.Equal("A Big Game", game.Name);
        Assert.Equal("A Big Game", game.MatchName);

        // A full path, not a folder. An add-on shares a folder with the game
        // it patches, so re-reading from the folder alone would find the game
        // again and turn the patch into a second copy of it.
        Assert.Equal(@"C:\games\A Big Game\setup_a_big_game.exe", game.Installer);
        Assert.True(game.WasAGogDownload);
    }

    [Fact]
    public void An_add_on_comes_back_pointing_at_the_game_it_belongs_to()
    {
        OpenedProject back = Written(AProject());

        ProjectEntry addOn = back.Entries.Single(each => each.IsAnAddOn);

        // By where the game sits in the list, never by name. Two GOG folders
        // can report the same name, and a name would bind the patch to the
        // wrong game.
        Assert.Equal(0, addOn.BelongsTo);
        Assert.Equal("A Patch", addOn.Name);
        Assert.Equal(@"C:\games\A Big Game\patch.exe", addOn.Installer);
    }

    [Fact]
    public void The_pictures_and_the_extras_come_back()
    {
        DiscProject was = AProject() with
        {
            Artwork = new DiscArtwork(
                IconPath: @"C:\art\disc.ico",
                BackgroundPath: @"C:\art\bg.png",
                BackgroundAsIs: true,
                CoverPath: @"C:\art\cover.png",
                DiscFacePath: @"C:\art\face.png"),
            Extras = new DiscExtras(
                ManualPath: @"C:\extras\manual.pdf",
                ExtrasFolderPath: @"C:\extras\more",
                LooseItems: [@"C:\extras\map.png"],
                MusicPath: @"C:\extras\theme.mp3")
        };

        OpenedProject back = Written(was);

        Assert.Equal(@"C:\art\disc.ico", back.Artwork.IconPath);
        Assert.True(back.Artwork.IconIsAlreadyAnIcon);
        Assert.Equal(@"C:\art\bg.png", back.Artwork.BackgroundPath);
        Assert.True(back.Artwork.BackgroundAsItIs);
        Assert.Equal(@"C:\art\cover.png", back.Artwork.CoverPath);
        Assert.Equal(@"C:\art\face.png", back.Artwork.DiscFacePath);

        Assert.Equal(@"C:\extras\manual.pdf", back.ManualPath);
        Assert.Equal(@"C:\extras\more", back.ExtrasFolderPath);
        Assert.Equal([@"C:\extras\map.png"], back.LooseExtras);
        Assert.Equal(@"C:\extras\theme.mp3", back.Look.MusicFile);
    }

    [Fact]
    public void A_project_with_no_pictures_still_reads_back()
    {
        OpenedProject back = Written(AProject() with { Artwork = null, Extras = null });

        // Nothing chosen is an ordinary disc, not a broken file.
        Assert.Null(back.Artwork.IconPath);
        Assert.Null(back.ManualPath);
        Assert.Empty(back.LooseExtras);
    }

    [Fact]
    public async Task Is_written_beside_the_discs_and_only_once_for_a_whole_set()
    {
        RecordingWriter wrote = new();
        WriteTheProjectFileStep step = new("2.0.0", "CD", @"F:\out", () => DateTimeOffset.UnixEpoch);

        await step.Run(ContextFor(wrote, disc: 1), CancellationToken.None);

        // Beside them, not on one. A disc is a copy of a game; the project is
        // the recipe, and a recipe on the disc is a file on every copy that
        // nobody asked for.
        Assert.Equal(@"F:\out\discproject.json", wrote.Texts.Single().Path);

        // A set is one project, so the other discs have nothing to add.
        Assert.True(step.AppliesTo(ContextFor(wrote, disc: 1)));
        Assert.False(step.AppliesTo(ContextFor(wrote, disc: 2)));
    }

    [Fact]
    public async Task Says_which_build_wrote_it_and_when()
    {
        RecordingWriter wrote = new();

        await new WriteTheProjectFileStep("2.0.0+abc123", "DVD5", @"F:\out", () => DateTimeOffset.UnixEpoch)
            .Run(ContextFor(wrote, disc: 1), CancellationToken.None);

        string json = wrote.Texts.Single().Text;

        Assert.Contains("2.0.0+abc123", json, StringComparison.Ordinal);
        Assert.Contains("1970-01-01T00:00:00", json, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ helpers

    private static OpenedProject Written(DiscProject project)
    {
        string json = TheProjectAsJson.Of(project, "2.0.0", "CD", @"F:\out", DateTimeOffset.UnixEpoch);

        return Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(json)).Value;
    }

    private static BuildContext ContextFor(RecordingWriter wrote, int disc) =>
        new(AProject(),
            new DiscLayout([new PlannedDisc(1, [], 0), new PlannedDisc(2, [], 0)], 700_000_000, 1),
            new PlannedDisc(disc, [], 0),
            @"F:\out\disc D" + disc,
            wrote);

    private static DiscProject AProject() => new(
        Label: "A BIG GAME",
        Games:
        [
            new GameFolder(
                Path: @"C:\games\A Big Game",
                Kind: GameKind.GogInstaller,
                Name: "A Big Game",
                MatchName: "A Big Game",
                Installer: new ListedFile("setup_a_big_game.exe", 900),
                Files: [new ListedFile("setup_a_big_game.exe", 900)],
                LauncherCandidates: [],
                Warning: null)
        ],
        Options: new DiscOptions(Menu: true, Checksums: true),
        AddOns: [new AddOn(0, @"C:\games\A Big Game\patch.exe", "A Patch", 70)]);
}
