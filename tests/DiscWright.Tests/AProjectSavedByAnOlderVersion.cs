using System.Text.Json;

using DiscWright.Core;
using DiscWright.Staging;

namespace DiscWright.Tests;

/// <summary>
/// A project file written by 2.0.4, kept as it was written, and opened by
/// whatever this version is.
/// </summary>
/// <remarks>
/// <para>
/// <c>tests/fixtures/saved-by-2.0.4.discproject.json</c> was written by the
/// code at the v2.0.4 tag, checked out beside this one on 2026-11-18: its
/// reader took the two folders below, its dialog's own suggestions answered
/// every row, its window saved the result. It is not to be regenerated or
/// tidied. What somebody saved last month is the thing being promised, and
/// only a file this version did not write can hold it to that.
/// </para>
/// <para>
/// The file holds paths and nothing else about what is behind them, so the
/// folders are a written list here.
/// </para>
/// <para>
/// It also holds the fault 2.0.4 had: the DLC's two parts are saved among
/// the game's extras, because that is what its dialog suggested for them.
/// </para>
/// </remarks>
public class AProjectSavedByAnOlderVersion
{
    private const long MB = 1024 * 1024;
    private const string Witcher = @"C:\Games\The Witcher 2";
    private const string Hollow = @"C:\Games\Hollow Knight";
    private const string Dlc = @"C:\Games\The Witcher 2\setup_the_witcher_2_dlc_pack_(21034).exe";

    [Fact]
    public void Opens_whole_with_its_games_under_the_names_they_had()
    {
        Reopened back = Reopened();

        Assert.True(back.WholeAgain, string.Join(", ", back.Gone));
        Assert.Equal(["The Witcher", "Hollow Knight"], back.Project.Games.Select(game => game.Name));
        Assert.Equal([Witcher, Hollow], back.Project.Games.Select(game => game.Path));
        Assert.Equal("WITCHER AND KNIGHT", back.Project.Label);
        Assert.Equal(Media.Dvd5.Key, back.Medium?.Key);
        Assert.Equal(@"C:\Discs\witcher and knight", back.OutputFolder);
    }

    [Fact]
    public void Each_game_is_still_its_installer_and_that_installers_parts()
    {
        Reopened back = Reopened();

        Assert.Equal(
            ["setup_the_witcher_2_3.3_(21034).exe", "setup_the_witcher_2_3.3_(21034)-1.bin"],
            back.Project.Games[0].Files.Select(file => file.RelativePath));
        Assert.Equal(
            ["setup_hollow_knight_1.5.78_(64bit)_(1).exe", "setup_hollow_knight_1.5.78_(64bit)_(1)-1.bin"],
            back.Project.Games[1].Files.Select(file => file.RelativePath));
    }

    [Fact]
    public void Its_add_ons_are_the_two_it_had_on_the_game_they_were_on()
    {
        IReadOnlyList<AddOn> addOns = Reopened().Project.AddOnsOrNone;

        Assert.Equal(
            [
                (0, Dlc, "the witcher 2 dlc pack (21034)", 2 * MB),
                (0, @"C:\Games\The Witcher 2\patch_the_witcher_2_3.3_to_3.4_(21034).exe", "Update 3.4 (21034)", 30 * MB)
            ],
            addOns.Select(addOn => (addOn.BelongsTo, addOn.Path, addOn.Name, addOn.Bytes)));
    }

    [Fact]
    public void Its_manual_and_its_artbook_are_still_the_games_own()
    {
        Reopened back = Reopened();

        Assert.Equal(
            [
                new GameExtra(@"C:\Games\The Witcher 2\manual.pdf", GameExtraKind.Manual, 5 * MB),
                new GameExtra(@"C:\Games\The Witcher 2\Artbook", GameExtraKind.Extra, 6 * MB, 2)
            ],
            back.Project.Games[0].OwnOrNone);
        Assert.Null(back.Project.Games[1].Own);
    }

    [Fact]
    public void The_DLC_comes_back_with_the_parts_it_was_saved_without()
    {
        AddOn dlc = Reopened().Project.AddOnsOrNone.Single(addOn => addOn.Path == Dlc);

        // Read off the disk, the way a game's own parts are, so a file
        // that never knew about them is repaired by being opened.
        Assert.Equal(
            [
                new AddOnPart(@"C:\Games\The Witcher 2\setup_the_witcher_2_dlc_pack_(21034)-1.bin", 700 * MB),
                new AddOnPart(@"C:\Games\The Witcher 2\setup_the_witcher_2_dlc_pack_(21034)-2.bin", 300 * MB)
            ],
            dlc.PartsOrNone);
    }

    [Fact]
    public void Every_file_is_planned_onto_the_disc_once_and_the_parts_beside_their_installer()
    {
        Reopened back = Reopened();

        PlannedOutput output = Assert.Single(Assert.IsType<Outcome<PlannedBuild>.Ok>(
            WhatWouldBeMade.For(back.Project, Media.Dvd5, _ => 0)).Value.Outputs);

        string[] planned = [.. output.Layout.Discs.SelectMany(disc => disc.Files).Select(file => file.RelativePath)];

        Assert.Equal(planned.Length, planned.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(
            [
                @"01 - The Witcher\Add-ons\patch_the_witcher_2_3.3_to_3.4_(21034).exe",
                @"01 - The Witcher\Add-ons\setup_the_witcher_2_dlc_pack_(21034)-1.bin",
                @"01 - The Witcher\Add-ons\setup_the_witcher_2_dlc_pack_(21034)-2.bin",
                @"01 - The Witcher\Add-ons\setup_the_witcher_2_dlc_pack_(21034).exe"
            ],
            planned.Where(path => path.Contains("Add-ons", StringComparison.Ordinal)).Order(StringComparer.Ordinal));

        // And nowhere else: among the extras they were a gigabyte of disc
        // that nothing could use.
        Assert.DoesNotContain(
            back.Project.Games.SelectMany(game => game.OwnOrNone),
            own => own.Path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Saved_again_the_file_says_nothing_an_older_version_does_not_know()
    {
        string saved = TheProjectAsJson.Of(
            Reopened().Project, "2.0.5", "DVD5", @"C:\Discs\witcher and knight", DateTimeOffset.UnixEpoch);

        using JsonDocument written = JsonDocument.Parse(saved);
        using JsonDocument before = JsonDocument.Parse(TheFile());

        JsonElement[] now = [.. written.RootElement.GetProperty("Games").EnumerateArray()];
        JsonElement[] then = [.. before.RootElement.GetProperty("Games").EnumerateArray()];

        // The same entries under the same keys, so 2.0.4 reads what this
        // version saves exactly as it read its own.
        Assert.Equal(then.Length, now.Length);

        for (int at = 0; at < then.Length; at++)
        {
            Assert.Equal(
                then[at].EnumerateObject().Select(each => each.Name),
                now[at].EnumerateObject().Select(each => each.Name));
        }

        Assert.Equal(
            before.RootElement.EnumerateObject().Select(each => each.Name),
            written.RootElement.EnumerateObject().Select(each => each.Name));

        // The add-ons are word for word what they were.
        Assert.Equal(then[2].GetRawText(), now[2].GetRawText());
        Assert.Equal(then[3].GetRawText(), now[3].GetRawText());
    }

    [Fact]
    public void Saved_again_and_opened_again_it_is_the_same_disc()
    {
        Reopened once = Reopened();

        Reopened twice = Reopened(TheProjectAsJson.Of(
            once.Project, "2.0.5", "DVD5", @"C:\Discs\witcher and knight", DateTimeOffset.UnixEpoch));

        Assert.True(twice.WholeAgain);
        Assert.Equal(
            once.Project.AddOnsOrNone.Select(addOn => (addOn.BelongsTo, addOn.Path, addOn.Name, addOn.BytesWithItsParts)),
            twice.Project.AddOnsOrNone.Select(addOn => (addOn.BelongsTo, addOn.Path, addOn.Name, addOn.BytesWithItsParts)));
        Assert.Equal(once.Project.Games[0].OwnOrNone, twice.Project.Games[0].OwnOrNone);
    }

    // ------------------------------------------------------------------ helpers

    private static string TheFile() =>
        File.ReadAllText(Repository.File("tests", "fixtures", "saved-by-2.0.4.discproject.json"));

    private static Reopened Reopened(string? json = null)
    {
        TheDiskAsItWas disk = new();

        return ReopenAProject.From(
            Assert.IsType<Outcome<OpenedProject>.Ok>(AProjectFile.Read(json ?? TheFile())).Value,
            GameFolderReader.InTheUsualOrder(new NoFacts()),
            disk,
            TheDiskAsItWas.HowBigIs);
    }

    private sealed class NoFacts : IFileFacts
    {
        public string? ProductNameOf(string path) => null;
    }

    /// <summary>The two folders, as they were when 2.0.4 was pointed at them.</summary>
    private sealed class TheDiskAsItWas : IFileSystem
    {
        private static readonly Dictionary<string, ListedFile[]> Folders = new(StringComparer.OrdinalIgnoreCase)
        {
            [Witcher] =
            [
                new("setup_the_witcher_2_3.3_(21034).exe", 40 * MB),
                new("setup_the_witcher_2_3.3_(21034)-1.bin", 1500 * MB),
                new("setup_the_witcher_2_dlc_pack_(21034).exe", 2 * MB),
                new("setup_the_witcher_2_dlc_pack_(21034)-1.bin", 700 * MB),
                new("setup_the_witcher_2_dlc_pack_(21034)-2.bin", 300 * MB),
                new("patch_the_witcher_2_3.3_to_3.4_(21034).exe", 30 * MB),
                new("manual.pdf", 5 * MB),
                new(@"Artbook\page 1.jpg", 3 * MB),
                new(@"Artbook\page 2.jpg", 3 * MB),
            ],
            [Witcher + @"\Artbook"] =
            [
                new("page 1.jpg", 3 * MB),
                new("page 2.jpg", 3 * MB),
            ],
            [Hollow] =
            [
                new("setup_hollow_knight_1.5.78_(64bit)_(1).exe", 60 * MB),
                new("setup_hollow_knight_1.5.78_(64bit)_(1)-1.bin", 800 * MB),
            ],
        };

        public bool FolderExists(string path) => Folders.ContainsKey(path);

        public FolderListing List(string path) => new(path, Folders.GetValueOrDefault(path) ?? []);

        public bool FileExists(string path) => Find(path) is not null;

        public static long HowBigIs(string path) => Find(path)?.Bytes ?? 0;

        private static ListedFile? Find(string path)
        {
            foreach ((string folder, ListedFile[] files) in Folders)
            {
                foreach (ListedFile file in files)
                {
                    if (string.Equals(Path.Combine(folder, file.RelativePath), path, StringComparison.OrdinalIgnoreCase))
                    {
                        return file;
                    }
                }
            }

            return null;
        }
    }
}
