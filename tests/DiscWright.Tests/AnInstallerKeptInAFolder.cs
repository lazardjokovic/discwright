using DiscWright.Core;
using DiscWright.Staging;
using DiscWright.ViewModels;

using Step = DiscWright.ViewModels.TakingFoldersOn.WhatToDo;

namespace DiscWright.Tests;

/// <summary>
/// A game's DLC kept in a folder of its own inside the download, which is
/// how a tidy person keeps it.
/// </summary>
/// <remarks>
/// Reported against 2.0.4 on Reddit: "If I have them in a separate folder,
/// it doesn't add them under the game's install button." A folder beside the
/// installer was one row, and a folder can only become an extra, so nothing
/// inside it could be an add-on however it was answered.
/// </remarks>
public class AnInstallerKeptInAFolder
{
    private const string Folder = @"C:\games\a game";

    // ----------------------------------------------------------- the rows

    [Fact]
    public void Is_a_row_of_its_own_called_by_where_it_is_and_suggested_as_an_add_on()
    {
        IReadOnlyList<LeftBehind> rows = RowsFor(
            ("setup_game.exe", 40),
            (@"DLC\setup_game_dlc.exe", 2),
            (@"DLC\setup_game_dlc-1.bin", 700));

        // And a folder holding nothing else gets no row at all.
        LeftBehind dlc = Assert.Single(rows);

        Assert.Equal(@"DLC\setup_game_dlc.exe", dlc.Name);
        Assert.False(dlc.IsAFolder);
        Assert.Equal(WhatBecomesOfIt.AddOn, dlc.Suggested);
        Assert.Equal([@"DLC\setup_game_dlc-1.bin"], dlc.Parts!.Select(part => part.RelativePath));
        Assert.Equal(702, dlc.Bytes);
    }

    [Fact]
    public void Leaves_the_rest_of_its_folder_as_one_row_where_its_first_file_was()
    {
        IReadOnlyList<LeftBehind> rows = RowsFor(
            ("setup_game.exe", 40),
            (@"DLC\read me.txt", 1),
            (@"DLC\setup_game_dlc.exe", 2),
            (@"DLC\notes\credits.txt", 3),
            ("manual.pdf", 5));

        Assert.Equal(["DLC", @"DLC\setup_game_dlc.exe", "manual.pdf"], rows.Select(row => row.Name));

        LeftBehind rest = rows[0];
        Assert.True(rest.IsAFolder);
        Assert.Equal([@"DLC\read me.txt", @"DLC\notes\credits.txt"], rest.Inside!.Select(file => file.RelativePath));
        Assert.Equal(4, rest.Bytes);
    }

    [Theory]
    [InlineData(@"DLC\setup_game_dlc.exe", true)]
    [InlineData(@"Patches\patch_game_1.0_to_1.1.exe", true)]
    [InlineData(@"Extras\DLC\setup_game_soundtrack.exe", true)]
    [InlineData(@"DLC\SETUP_GAME_DLC.EXE", true)]
    [InlineData(@"DLC\Patch_Game_1.0_to_1.1.Exe", true)]

    // A folder of tools or a mod is still a folder: taking it apart into a
    // row for everything that runs is the forty questions gathering spares.
    [InlineData(@"Tools\config.exe", false)]
    [InlineData(@"Mods\overhaul.exe", false)]
    [InlineData(@"Mods\unsetup_game.exe", false)]

    // And the name is not enough without being something that runs.
    [InlineData(@"DLC\setup_notes.txt", false)]
    [InlineData(@"DLC\patch_notes.pdf", false)]
    public void Only_what_is_named_like_an_installer_leaves_its_folder(string path, bool onItsOwn)
    {
        LeftBehind row = Assert.Single(RowsFor(("setup_game.exe", 40), (path, 9)));

        Assert.Equal(!onItsOwn, row.IsAFolder);
        Assert.Equal(onItsOwn ? path : path[..path.IndexOf('\\')], row.Name);
    }

    [Fact]
    public void Two_installers_in_two_folders_are_told_apart_by_where_they_are()
    {
        IReadOnlyList<LeftBehind> rows = RowsFor(
            ("setup_game.exe", 40),
            (@"English\setup_game_dlc.exe", 2),
            (@"German\setup_game_dlc.exe", 2));

        Assert.Equal([@"English\setup_game_dlc.exe", @"German\setup_game_dlc.exe"], rows.Select(row => row.Name));
    }

    [Theory]
    [InlineData("setup_game_dlc.exe", true)]
    [InlineData("patch_game_1_to_2.exe", true)]
    [InlineData("SETUP_GAME.EXE", true)]
    [InlineData("PATCH_GAME.EXE", true)]
    [InlineData("setup.exe", false)]
    [InlineData("game_setup_.exe", false)]
    [InlineData("setup_game.bin", false)]
    [InlineData("patch_notes.txt", false)]
    [InlineData("overhaul.exe", false)]
    public void Knows_what_GOG_names_an_installer(string name, bool isOne) =>
        Assert.Equal(isOne, AddOn.IsNamedLikeAnInstaller(name));

    // ---------------------------------------------------- what becomes of it

    [Fact]
    public void Attached_it_points_into_its_folder_and_is_named_without_it()
    {
        LeftBehind dlc = Assert.Single(RowsFor(
            ("setup_game.exe", 40),
            (@"DLC\setup_game_dlc.exe", 2),
            (@"DLC\setup_game_dlc-1.bin", 700)));

        AddOn attached = dlc.AttachedTo(0, Folder);

        Assert.Equal(@"C:\games\a game\DLC\setup_game_dlc.exe", attached.Path);
        Assert.Equal("game dlc", attached.Name);
        Assert.Equal(
            [new AddOnPart(@"C:\games\a game\DLC\setup_game_dlc-1.bin", 700)],
            attached.PartsOrNone);
    }

    [Fact]
    public void On_the_disc_it_is_in_Add_ons_like_any_other_and_not_in_a_folder_of_its_own()
    {
        LeftBehind dlc = Assert.Single(RowsFor(
            ("setup_game.exe", 40),
            (@"DLC\setup_game_dlc.exe", 2),
            (@"DLC\setup_game_dlc-1.bin", 700)));

        DiscProject project = new("A GAME", [TheGame()], new DiscOptions(), AddOns: [dlc.AttachedTo(0, Folder)]);

        PlannedOutput output = Assert.Single(Assert.IsType<Outcome<PlannedBuild>.Ok>(
            WhatWouldBeMade.For(project, new Medium("TEST", "a test disc", 100 * 1024 * 1024), _ => 0)).Value.Outputs);

        // The menu's button runs Add-ons\<its name>, so that is where it
        // has to be, whatever folder somebody kept it in.
        Assert.Equal(
            [
                (@"Add-ons\setup_game_dlc-1.bin", @"C:\games\a game\DLC\setup_game_dlc-1.bin"),
                (@"Add-ons\setup_game_dlc.exe", @"C:\games\a game\DLC\setup_game_dlc.exe"),
                ("setup_game.exe", @"C:\games\a game\setup_game.exe")
            ],
            output.Layout.Discs.SelectMany(disc => disc.Files)
                .Select(file => (file.RelativePath, file.Source))
                .OrderBy(file => file.RelativePath, StringComparer.Ordinal));
    }

    // ------------------------------------------------- the whole of adding it

    [Fact]
    public void Pressing_OK_on_what_the_dialog_suggests_attaches_it_to_its_game()
    {
        ADisk disk = new();
        disk.Holds(Folder,
            ("setup_game.exe", 40),
            ("setup_game-1.bin", 900),
            (@"DLC\setup_game_dlc.exe", 2),
            (@"DLC\setup_game_dlc-1.bin", 700),
            (@"DLC\read me.txt", 1));

        Step.PutItOn put = Assert.IsType<Step.PutItOn>(Assert.Single(
            new TakingFoldersOn(disk, GameFolderReader.InTheUsualOrder(new NoFacts()))
                .From([Folder], TheQuestionsAnsweredAsSuggested())));

        // The report, start to finish: a folder pointed at, OK pressed, and
        // the DLC is the game's add-on with its part.
        LeftBehind attached = Assert.Single(put.AsAddOns);
        Assert.Equal(@"DLC\setup_game_dlc.exe", attached.File.RelativePath);
        Assert.Equal([@"DLC\setup_game_dlc-1.bin"], attached.Parts!.Select(part => part.RelativePath));

        // The game is its installer and its part, and the note that was in
        // the folder is its extra, the folder whole.
        Assert.Equal(["setup_game.exe", "setup_game-1.bin"], put.Game.Files.Select(file => file.RelativePath));
        Assert.Equal(
            [new GameExtra(@"C:\games\a game\DLC", GameExtraKind.Extra, 1)],
            put.Game.OwnOrNone);
    }

    [Fact]
    public void The_same_answers_for_the_next_game_attach_its_DLC_too()
    {
        ADisk disk = new();
        disk.Holds(@"C:\games\one",
            ("setup_one.exe", 40),
            (@"DLC\setup_one_dlc.exe", 2));
        disk.Holds(@"C:\games\two",
            ("setup_two.exe", 40),
            (@"Add ons\setup_two_dlc.exe", 2),
            (@"Add ons\setup_two_dlc-1.bin", 300));

        Step[] steps =
        [
            .. new TakingFoldersOn(disk, GameFolderReader.InTheUsualOrder(new NoFacts()))
                .From([@"C:\games\one", @"C:\games\two"], TheQuestionsAnsweredAsSuggested(andTheSameForTheRest: true))
        ];

        // Asked once, answered for both.
        Assert.Equal(2, steps.Length);

        Step.PutItOn second = Assert.IsType<Step.PutItOn>(steps[1]);
        LeftBehind attached = Assert.Single(second.AsAddOns);

        Assert.Equal(@"Add ons\setup_two_dlc.exe", attached.File.RelativePath);
        Assert.Equal(302, attached.Bytes);
    }

    // ------------------------------------------------------------------ helpers

    private static GameFolder TheGame()
    {
        ListedFile installer = new("setup_game.exe", 40);

        return new GameFolder(Folder, GameKind.GogInstaller, "a game", "a game", installer, [installer], [], null);
    }

    /// <summary>
    /// The rows the dialog would show for a folder whose game is
    /// <c>setup_game.exe</c>.
    /// </summary>
    private static IReadOnlyList<LeftBehind> RowsFor(params (string Path, long Bytes)[] files) =>
        WhatWasLeftBehind.Gathered(WhatWasLeftBehind.In(
            new FolderListing(Folder, [.. files.Select(file => new ListedFile(file.Path, file.Bytes))]),
            TheGame()));

    /// <summary>
    /// Every question answered the way the dialog suggests, which is what
    /// pressing OK without touching anything does.
    /// </summary>
    private static TakingFoldersOn.TheQuestions TheQuestionsAnsweredAsSuggested(bool andTheSameForTheRest = false)
    {
        int asked = 0;

        return new(
            (_, _, _) => false,
            _ => false,
            (game, left, _) =>
            {
                // Asked once when the same answers were asked for, and
                // a second question is the thing that must not happen.
                Assert.True(!andTheSameForTheRest || asked++ == 0, "Asked again after the same answers were asked for.");

                IReadOnlyList<LeftBehind> rows = WhatWasLeftBehind.Gathered(left);

                (GameFolder taken, IReadOnlyList<LeftBehind> addOns) = WhatWasLeftBehind.Answered(
                    game, rows.Select(row => (row, row.Suggested)));

                return (
                    taken,
                    addOns,
                    andTheSameForTheRest
                        ? TheSameAnswers.From(rows.Select(row => (row, true, row.Suggested)), game.Installer?.Name)
                        : null);
            },
            game => game);
    }

    private sealed class NoFacts : IFileFacts
    {
        public string? ProductNameOf(string path) => null;
    }

    private sealed class ADisk : IFileSystem
    {
        private readonly Dictionary<string, FolderListing> _folders = new(StringComparer.OrdinalIgnoreCase);

        public void Holds(string path, params (string Path, long Bytes)[] files) =>
            _folders[path] = new FolderListing(path, [.. files.Select(file => new ListedFile(file.Path, file.Bytes))]);

        public bool FolderExists(string path) => _folders.ContainsKey(path);

        public FolderListing List(string path) => _folders.GetValueOrDefault(path) ?? new FolderListing(path, []);
    }
}
