using DiscWright.Core;
using DiscWright.Core.Staging;
using DiscWright.Menu;
using DiscWright.Staging;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// Looking at the menu a disc would get, before making one.
/// </summary>
/// <remarks>
/// The thing worth proving is not that a preview appears. It is that the
/// preview is <strong>the same menu</strong>, because a preview written by a
/// second piece of code drifts from the disc, and the day it drifts is the day
/// somebody burns a disc because the preview looked right.
/// </remarks>
public class SeeingTheMenuFirst
{
    [Fact]
    public void Draws_the_background_it_carries_rather_than_only_carrying_it()
    {
        RecordingWriter wrote = new();

        DiscProject withArt = AProject() with
        {
            Artwork = new DiscArtwork(BackgroundPath: @"C:\art\bg.png")
        };

        new APreviewOfTheMenu(wrote, new PicturesThatRead())
            .Write(withArt, Media.Dvd5, _ => 1000, Look);

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // The background was copied onto every disc and the page never
        // referred to it, so a picture somebody chose was written, carried
        // and never once shown. The file arriving is not the thing worth
        // testing; the page naming it is.
        Assert.Contains("url(\"background.png\")", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Names_no_picture_when_there_is_none_to_name()
    {
        RecordingWriter wrote = new();

        new APreviewOfTheMenu(wrote, new PicturesThatRead())
            .Write(AProject(), Media.Dvd5, _ => 1000, Look);

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // A background-image that does not resolve leaves the panel floating
        // on whatever the browser decides, and the disc looks broken rather
        // than plain.
        Assert.DoesNotContain("background-image", page, StringComparison.Ordinal);
    }

    [Fact]
    public void A_preview_tells_the_script_it_is_one_before_drawing_anything()
    {
        RecordingWriter previewed = new();

        new APreviewOfTheMenu(previewed, new PicturesThatRead())
            .Write(AProject(), Media.Dvd5, _ => 1000, Look);

        string page = previewed.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // The script refuses to start anything once it has been told, and the
        // page is the only thing that can tell it. Without this line the
        // guard is present, correct, tested, and never switched on.
        Assert.Contains("thisIsAPreview();", page, StringComparison.Ordinal);
        Assert.Contains("onADisc: false", page, StringComparison.Ordinal);

        // Before the icon, before the games, before anything can be pressed.
        Assert.True(
            page.IndexOf("thisIsAPreview();", StringComparison.Ordinal)
                < page.IndexOf("wearTheRightIcon();", StringComparison.Ordinal),
            "the script is told after the page has started drawing");
    }

    [Fact]
    public async Task Writes_the_same_menu_the_disc_would_get()
    {
        RecordingWriter previewed = new();
        RecordingWriter burned = new();

        APreviewOfTheMenu preview = new(previewed, new PicturesThatRead());
        preview.Write(AProject(), Media.Dvd5, _ => 1000, Look);

        await new WriteTheMenuStep().Run(ADiscBeingMade(burned), CancellationToken.None);

        string onTheDisc = burned.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        string inThePreview = previewed.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // Two differences, and both have to differ.
        //
        // singleinstance, or previewing a second disc refocuses the first
        // one's stale window. And onADisc, which is how the script knows to
        // start nothing: a preview stages the menu and the artwork and
        // nothing else, so every button points at a file that is not there.
        Assert.Equal(
            onTheDisc
                .Replace("singleinstance=\"yes\"", "singleinstance=\"no\"", StringComparison.Ordinal)
                .Replace("onADisc: true", "onADisc: false", StringComparison.Ordinal),
            inThePreview);
    }

    [Fact]
    public void Lets_a_second_preview_open_beside_the_first()
    {
        RecordingWriter wrote = new();

        new APreviewOfTheMenu(wrote, new PicturesThatRead()).Write(AProject(), Media.Dvd5, _ => 1000, Look);

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // A disc insists on being the only one of itself open, so putting the
        // same disc in twice refocuses rather than opening a second window.
        // A preview must not: it would refocus the stale window left over from
        // previewing something else a minute ago, and show the wrong games.
        Assert.Contains("singleinstance=\"no\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_the_disc_rather_than_a_menu_drawn_on_nothing()
    {
        RecordingWriter wrote = new();
        PicturesThatRead pictures = new();

        DiscProject withArt = AProject() with
        {
            Artwork = new DiscArtwork(IconPath: @"C:\art\icon.png", BackgroundPath: @"C:\art\bg.png"),
            Extras = new DiscExtras(MusicPath: @"C:\sound\theme.mp3")
        };

        new APreviewOfTheMenu(wrote, pictures).Write(withArt, Media.Dvd5, _ => 1000, Look);

        // A preview that leaves the artwork out shows a menu drawn on
        // nothing, which is the one thing somebody choosing a background most
        // wants to look at.
        //
        // The icon and the background go through the pictures port rather than
        // the writer, because turning a photograph into an .ico is the
        // operating system's job and not this program's.
        Assert.Contains(pictures.Icons, path => path.EndsWith("HOLLOWKNIGHT.ico", StringComparison.Ordinal));
        Assert.Contains(
            pictures.Backgrounds, path => path.EndsWith(@"AUTORUN\background.png", StringComparison.Ordinal));

        // The music is a plain copy.
        Assert.Contains(wrote.Everything, path => path.EndsWith(@"AUTORUN\music.mp3", StringComparison.Ordinal));
    }

    [Fact]
    public void Says_where_it_put_it()
    {
        Outcome<IReadOnlyList<string>> went = new APreviewOfTheMenu(new RecordingWriter(), new PicturesThatRead())
            .Write(AProject(), Media.Dvd5, _ => 1000, Look);

        IReadOnlyList<string> where = Assert.IsType<Outcome<IReadOnlyList<string>>.Ok>(went).Value;

        // One disc, one menu, and it sits where it always did.
        Assert.Equal([System.IO.Path.Combine(Look, "AUTORUN", "menu.hta")], where);
    }

    [Fact]
    public void Writes_the_logic_beside_it_or_the_menu_does_nothing()
    {
        RecordingWriter wrote = new();

        new APreviewOfTheMenu(wrote, new PicturesThatRead()).Write(AProject(), Media.Dvd5, _ => 1000, Look);

        // The page is a shell around menu.js. Without it the preview opens to
        // an empty window, which is a worse lie than no preview at all.
        Assert.Contains(
            wrote.Texts,
            text => text.Path.EndsWith(@"AUTORUN\menu.js", StringComparison.Ordinal));
    }

    [Fact]
    public void Shows_the_first_disc_of_a_set()
    {
        RecordingWriter wrote = new();

        // Two games that cannot share a CD, so they get a disc each.
        DiscProject big = AProject() with
        {
            Games = [AGame("Hollow Knight"), AGame("Grim Fandango")]
        };

        new APreviewOfTheMenu(wrote, new PicturesThatRead()).Write(big, Media.Cd, _ => 600L * 1024 * 1024, Look);

        string page = wrote.Texts.Single(
            text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)).Text;

        // Whichever game lands on disc one is what somebody is previewing, and
        // which that is was decided by planning rather than by the order they
        // were added.
        Assert.Contains("Hollow Knight", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_each_disc_of_a_disc_each_build_with_its_own_add_ons()
    {
        RecordingWriter wrote = new();

        // Big enough that the two will not share a CD and each fits alone,
        // so each gets a disc of its own. The planner weighs the files
        // themselves, not the size function, which only prices the extras.
        GameFolder knight = ABigGame("Hollow Knight");
        GameFolder grim = ABigGame("Grim Fandango");

        DiscProject big = AProject() with
        {
            Games = [knight, grim],
            AddOns =
            [
                new AddOn(0, @"C:\games\Hollow Knight\patch.exe", "Update 2 (knight)", 10),
                new AddOn(1, @"C:\games\Grim Fandango\soundtrack.exe", "Grim Soundtrack", 10)
            ]
        };

        new APreviewOfTheMenu(wrote, new PicturesThatRead()).Write(big, Media.Cd, _ => 600L * 1024 * 1024, Look);

        string[] pages = [.. wrote.Texts
            .Where(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal))
            .Select(text => text.Text)];

        // The preview cut the project down the same way the builder did, so
        // the second window showed the first game's add-ons.
        string ofGrim = Assert.Single(pages, page => page.Contains("name: \"Grim Fandango\"", StringComparison.Ordinal));
        Assert.Contains("Grim Soundtrack", ofGrim, StringComparison.Ordinal);
        Assert.DoesNotContain("Update 2 (knight)", ofGrim, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_rather_than_previewing_nothing()
    {
        Outcome<IReadOnlyList<string>> went = new APreviewOfTheMenu(new RecordingWriter(), new PicturesThatRead())
            .Write(new DiscProject("EMPTY", [], new DiscOptions()), Media.Dvd5, _ => 1000, Look);

        Refusal refused = Assert.IsType<Outcome<IReadOnlyList<string>>.No>(went).Refusal;

        Assert.Contains("nothing", refused.Headline, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Passes_on_the_refusal_rather_than_inventing_one()
    {
        // A file bigger than any disc is refused at planning, and previewing
        // has to say the same thing building would say rather than a vaguer
        // version of it.
        ListedFile huge = new("setup_enormous.exe", 9L * 1024 * 1024 * 1024);

        DiscProject enormous = AProject() with
        {
            Games = [new GameFolder(@"C:\games\x", GameKind.GogInstaller, "Enormous", "Enormous", huge, [huge], [], null)]
        };

        Outcome<IReadOnlyList<string>> went = new APreviewOfTheMenu(new RecordingWriter(), new PicturesThatRead())
            .Write(enormous, Media.Dvd5, _ => huge.Bytes, Look);

        Assert.IsType<Outcome<IReadOnlyList<string>>.No>(went);
    }

    [Fact]
    public void The_screen_hands_the_window_every_menu_that_was_written()
    {
        MakeADisc screen = TheScreen(new TwoMenus());
        screen.Add(AGame("Hollow Knight"));

        List<string> told = [];
        screen.PropertyChanged += (_, what) => told.Add(what.PropertyName ?? string.Empty);

        screen.PreviewTheMenu(Look);

        Assert.Equal([@"X:\one\menu.hta", @"X:\two\menu.hta"], screen.ThePreviews);
        Assert.Contains(nameof(MakeADisc.ThePreviews), told);
    }

    [Fact]
    public void Forgets_the_last_preview_before_it_writes_the_next()
    {
        MakeADisc screen = TheScreen(new AMenuThenNothing());
        screen.Add(AGame("Hollow Knight"));

        screen.PreviewTheMenu(Look);
        Assert.NotEmpty(screen.ThePreviews);

        // Pressed again, and this time it cannot be written. The window must
        // not go on offering last time's menu: those files have just been
        // cleared out of that folder, so every one of them is a link to
        // nothing.
        screen.PreviewTheMenu(Look);

        Assert.Empty(screen.ThePreviews);
        Assert.NotNull(screen.ItWentWrong);
    }

    /// <summary>A preview that works once and refuses after that.</summary>
    private sealed class AMenuThenNothing : IShowTheMenu
    {
        private int _asked;

        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            _asked++ == 0
                ? (string[])[@"X:\one\menu.hta"]
                : Refusals.ThereIsNothingToPutOnADisc();
    }

    [Fact]
    public void The_window_opens_all_of_them_rather_than_the_first()
    {
        // A set is previewed a disc at a time, in a window each, and the
        // manual and the extras are on the last of them. Opening only the
        // first is the bug with the fix in place.
        //
        // Read off disk, because nothing here may reference the window.
        string code = System.IO.File.ReadAllText(
            Repository.File("src", "DiscWright.App", "MainWindow.xaml.cs"));

        Assert.Contains("foreach (string where in menus)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tune_that_has_been_moved_since_it_was_chosen_is_a_sentence()
    {
        // Found by previewing a real project whose music file had gone. A
        // preview writes real files, so the artwork and the music are copied
        // and either can have been moved or renamed since it was chosen. The
        // FileNotFoundException went straight out of the screen, past a
        // window that catches nothing around this, and took the program down
        // with a stack trace in a box.
        MakeADisc screen = TheScreen(new APreviewThatCannotFindTheTune());
        screen.Add(AGame("Hollow Knight"));

        screen.PreviewTheMenu(Look);

        Refusal said = Assert.IsType<Refusal>(screen.ItWentWrong);
        Assert.Contains("writing the preview", said.Headline, StringComparison.Ordinal);
        Assert.Empty(screen.ThePreviews);
    }

    /// <summary>A preview whose music went missing between choosing and looking.</summary>
    private sealed class APreviewThatCannotFindTheTune : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            throw new System.IO.FileNotFoundException(
                @"Could not find file 'C:\stuff\the tune.mp3'.");
    }

    /// <summary>A preview that wrote two menus, as a set's would.</summary>
    private sealed class TwoMenus : IShowTheMenu
    {
        public Outcome<IReadOnlyList<string>> Write(
            DiscProject project, Medium medium, Func<string, long> sizeOf, string into) =>
            new List<string> { @"X:\one\menu.hta", @"X:\two\menu.hta" };
    }

    // ------------------------------------------------ a preview of the whole set

    [Fact]
    public void Previews_every_disc_of_a_set_and_not_only_the_first()
    {
        RecordingWriter wrote = new();

        IReadOnlyList<string> menus = Previewing(wrote, ASetOfTwo());

        // Two discs, two menus. Disc one stays where it always was, so the
        // ordinary one-disc case is untouched; the rest get a folder each,
        // named so somebody opening their temporary folder can tell which is
        // which.
        Assert.Equal(
            [
                System.IO.Path.Combine(Look, "AUTORUN", "menu.hta"),
                System.IO.Path.Combine(Look, "HOLLOW KNIGHT D2", "AUTORUN", "menu.hta")
            ],
            menus);
    }

    [Fact]
    public void Previews_a_set_whose_discs_have_an_icon()
    {
        RecordingWriter wrote = new();
        PicturesThatRead pictures = new() { Beside = wrote };

        // Reported from the running program: "Could not find a part of the
        // path ...\HOLLOW KNIGHT D2\HOLLOWKNIGHT2.ico". Every disc after the
        // first was given a folder of its own in the path and nothing ever
        // made it, so the first thing written into it threw.
        //
        // It took an icon to show. Everything else on a disc goes into a
        // subfolder, and making AUTORUN makes the disc's folder on the way;
        // the icon is the one thing that sits at the root, so it is the one
        // thing that needs the root to be there already.
        Outcome<IReadOnlyList<string>> went = new APreviewOfTheMenu(wrote, pictures)
            .Write(ASetOfTwo() with { Artwork = new DiscArtwork(IconPath: @"C:\art\hk.png") },
                Media.Cd, TheSizeOf, Look);

        Assert.IsType<Outcome<IReadOnlyList<string>>.Ok>(went);

        // One icon per disc, each in its own disc's folder, each named for
        // its own number so Explorer cannot cache one over the other.
        Assert.Equal(
            [
                System.IO.Path.Combine(Look, "HOLLOWKNIGHT1.ico"),
                System.IO.Path.Combine(Look, "HOLLOW KNIGHT D2", "HOLLOWKNIGHT2.ico")
            ],
            pictures.Icons);
    }

    // The rig itself, because a net nobody has tested is a net nobody should
    // lean on. These two are why the preview bug above can only happen once:
    // the fakes now refuse what the disk refuses, so the next thing that
    // writes into nowhere fails in a test rather than in front of somebody.
    // Planting a fault that disables the picture check survived everything
    // else in this file, which is exactly what an untested guard looks like.

    [Fact]
    public void The_rig_refuses_a_file_written_where_no_folder_was_made()
    {
        RecordingWriter wrote = new();

        DirectoryNotFoundException went = Assert.Throws<DirectoryNotFoundException>(
            () => wrote.WriteText(System.IO.Path.Combine(Look, "nowhere", "a file.txt"), "anything"));

        Assert.Contains("Could not find a part of the path", went.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rig_refuses_a_picture_written_where_no_folder_was_made()
    {
        RecordingWriter wrote = new();
        PicturesThatRead pictures = new() { Beside = wrote };

        Assert.Throws<DirectoryNotFoundException>(
            () => pictures.WriteIcon(@"C:\art\hk.png", System.IO.Path.Combine(Look, "nowhere", "d.ico")));

        // And accepts it once something has made the folder, or it would be a
        // guard that refuses everything and proves nothing.
        wrote.MakeFolder(System.IO.Path.Combine(Look, "nowhere"));
        pictures.WriteIcon(@"C:\art\hk.png", System.IO.Path.Combine(Look, "nowhere", "d.ico"));

        Assert.Single(pictures.Icons);
    }

    [Fact]
    public void Makes_a_folder_for_every_disc_it_previews()
    {
        RecordingWriter wrote = new();

        Previewing(wrote, ASetOfTwo());

        // Said as its own guard rather than left to the icon to discover.
        // The folder is the preview's job whatever happens to go in it, and
        // the next thing that writes to a disc's root will not come with a
        // bug report attached.
        Assert.Contains(System.IO.Path.Combine(Look, "HOLLOW KNIGHT D2"), wrote.Folders);
    }

    [Fact]
    public void Shows_the_manual_and_the_extras_on_the_disc_that_has_them()
    {
        RecordingWriter wrote = new();

        Previewing(wrote, ASetOfTwo());

        // Every disc of a set carries them. They rode the last disc, then
        // disc 1, and each time another disc's menu was missing them or, on
        // a burned disc, offered buttons with nothing behind them: the owner
        // pressed Manual on disc 2 on 2026-11-10 and got "That would not
        // start". So whichever window somebody opens shows them.
        foreach (int disc in new[] { 1, 2 })
        {
            string page = PageOf(wrote, disc);
            Assert.Contains("manual: \"Extras/How to play.pdf\"", page, StringComparison.Ordinal);
            Assert.Contains("extras: \"Extras\"", page, StringComparison.Ordinal);
            Assert.Contains("\"Manual\"", page, StringComparison.Ordinal);
            Assert.Contains("\"Extras\"", page, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Only_the_first_window_of_a_preview_plays_the_tune()
    {
        RecordingWriter wrote = new();

        Previewing(wrote, ASetOfTwo());

        // Every disc of a set carries the tune and every disc's menu plays
        // it, so a faithful preview of two discs would open two windows
        // playing the same bar over each other.
        Assert.Contains("music: \"AUTORUN/music.mp3\"", PageOf(wrote, 1), StringComparison.Ordinal);
        Assert.Contains("music: \"\"", PageOf(wrote, 2), StringComparison.Ordinal);

        // And the file is written once rather than copied into every folder
        // for nobody to play.
        Assert.Single(wrote.Copies, copy => copy.To.EndsWith("music.mp3", StringComparison.Ordinal));
    }

    [Fact]
    public void Numbers_each_disc_as_the_disc_it_is()
    {
        RecordingWriter wrote = new();

        Previewing(wrote, ASetOfTwo());

        Assert.Contains("discNumber: 1", PageOf(wrote, 1), StringComparison.Ordinal);
        Assert.Contains("discNumber: 2", PageOf(wrote, 2), StringComparison.Ordinal);
        Assert.Contains("discsInSet: 2", PageOf(wrote, 2), StringComparison.Ordinal);
    }

    [Fact]
    public void Gives_two_games_that_will_not_share_a_disc_a_menu_each()
    {
        RecordingWriter wrote = new();

        DiscProject two = AProject() with
        {
            Games = [ABigGame("Hollow Knight"), ABigGame("Grim Fandango")]
        };

        IReadOnlyList<string> menus = new APreviewOfTheMenu(wrote, new PicturesThatRead())
            .Write(two, Media.Cd, TheSizeOf, Look) is Outcome<IReadOnlyList<string>>.Ok(var where)
                ? where
                : throw new Xunit.Sdk.XunitException("the preview refused");

        // Neither of these continues the other, so each is named after its
        // own game rather than numbered.
        Assert.Equal(
            [
                System.IO.Path.Combine(Look, "AUTORUN", "menu.hta"),
                System.IO.Path.Combine(Look, "Grim Fandango", "AUTORUN", "menu.hta")
            ],
            menus);
    }

    [Fact]
    public void Two_games_of_the_same_name_do_not_land_on_each_other()
    {
        RecordingWriter wrote = new();

        // Folders are named by people, and three of them called Setup is
        // not a strange thing to have. Named from the game alone, the third
        // preview would be written over the second and one of the menus
        // would be the wrong menu.
        //
        // THREE, not two. With two, the first disc goes in the preview
        // folder itself and only one name is ever worked out, so there is
        // nothing for it to collide with: taking the uniqueness out broke
        // nothing and a planted fault walked straight through.
        DiscProject thrice = AProject() with
        {
            Games = [ABigGame("Setup"), ABigGame("Setup"), ABigGame("Setup")]
        };

        IReadOnlyList<string> menus = new APreviewOfTheMenu(wrote, new PicturesThatRead())
            .Write(thrice, Media.Cd, TheSizeOf, Look) is Outcome<IReadOnlyList<string>>.Ok(var where)
                ? where
                : throw new Xunit.Sdk.XunitException("the preview refused");

        Assert.Equal(3, menus.Count);
        Assert.Equal(3, menus.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // And the menus really are three separate files, not one written
        // over twice.
        Assert.Equal(
            3,
            wrote.Texts.Count(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal)));
    }

    /// <summary>A game too big to share a CD with another.</summary>
    private static GameFolder ABigGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 600L * 1024 * 1024);

        return new GameFolder(
            @"C:\games\" + name, GameKind.GogInstaller, name, name, only, [only], [], null);
    }

    /// <summary>A project whose one game needs two CDs, with extras on it.</summary>
    private static DiscProject ASetOfTwo()
    {
        ListedFile big = new("setup_hollow_knight.exe", 900L * 1024 * 1024);

        return new DiscProject(
            "HOLLOW KNIGHT",
            [new GameFolder(@"C:\games\hk", GameKind.GogInstaller, "Hollow Knight", "Hollow Knight", big, [big], [], null)],
            new DiscOptions(CutFiles: true))
        {
            Extras = new DiscExtras(
                ManualPath: @"C:\stuff\How to play.pdf",
                ExtrasFolderPath: @"C:\stuff\wallpapers",
                MusicPath: @"C:\stuff\the tune.mp3")
        };
    }

    private static IReadOnlyList<string> Previewing(RecordingWriter wrote, DiscProject project) =>
        new APreviewOfTheMenu(wrote, new PicturesThatRead { Beside = wrote })
            .Write(project, Media.Cd, TheSizeOf, Look) is Outcome<IReadOnlyList<string>>.Ok(var where)
            ? where
            : throw new Xunit.Sdk.XunitException("the preview refused");

    /// <summary>
    /// The manual, the music and the extras are small. Nothing else is asked
    /// about: a game's size comes off its own file list.
    /// </summary>
    private static long TheSizeOf(string path) => 1024 * 1024;

    /// <summary>The page written for one disc, counting from one.</summary>
    private static string PageOf(RecordingWriter wrote, int disc) =>
        wrote.Texts
            .Where(text => text.Path.EndsWith("menu.hta", StringComparison.Ordinal))
            .ElementAt(disc - 1)
            .Text;

    [Fact]
    public void Tells_the_window_when_previewing_becomes_possible()
    {
        MakeADisc screen = TheScreen();
        List<string> changed = [];
        screen.PropertyChanged += (_, what) => changed.Add(what.PropertyName ?? string.Empty);

        Assert.False(screen.CanPreview);

        screen.Add(AGame("Hollow Knight"));

        // A property nobody is told about keeps whatever the window read out
        // of it the first time, which here is a button that is dead for ever.
        // The real window found this; reading the property in a test does not.
        Assert.True(screen.CanPreview);
        Assert.Contains(nameof(MakeADisc.CanPreview), changed, StringComparer.Ordinal);
    }

    [Fact]
    public void Asks_for_the_same_preview_folder_until_the_project_changes()
    {
        // The window hands this to the preview as the folder to write in, so
        // a name that wandered between presses would put the old behaviour
        // back without anybody noticing: a folder per press, and none of
        // them ever removed.
        MakeADisc screen = TheScreen();
        screen.Add(AGame("Hollow Knight"));
        screen.OutputFolder = @"F:\sets\hk";

        string first = screen.WhereAPreviewWouldGo;

        Assert.Equal(first, screen.WhereAPreviewWouldGo);

        screen.OutputFolder = @"F:\sets\somewhere else";
        Assert.NotEqual(first, screen.WhereAPreviewWouldGo);

        // And back, because a folder that depended on the order things were
        // typed in would be a folder per press wearing a different hat.
        screen.OutputFolder = @"F:\sets\hk";
        Assert.Equal(first, screen.WhereAPreviewWouldGo);
    }

    // ------------------------------------------------------------- helpers

    /// <summary>
    /// Where a preview really goes.
    /// </summary>
    /// <remarks>
    /// An invented path was used here, and the writer these tests stand on
    /// would refuse to empty it, which is how a preview that wrote nothing
    /// at all went green through every one of them.
    /// </remarks>
    private static readonly string Look = WhereAPreviewGoes.Under(
        System.IO.Path.GetTempPath(), "HOLLOW KNIGHT", @"F:\sets\hk", null);

    private static DiscProject AProject() =>
        new("HOLLOW KNIGHT", [AGame("Hollow Knight")], new DiscOptions());

    private static GameFolder AGame(string name)
    {
        ListedFile only = new($"setup_{name}.exe", 1000);

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, only, [only], [], null);
    }

    /// <summary>The same project, as the builder would hand it to the step.</summary>
    private static BuildContext ADiscBeingMade(IStageWriter writer)
    {
        DiscProject project = AProject();
        PlannedDisc disc = new(1, [], 0);

        return new BuildContext(project, new DiscLayout([disc], Media.Dvd5.Capacity, true), disc, Look, writer);
    }

    private static MakeADisc TheScreen(IShowTheMenu? preview = null) =>
        new(new TheMachine(
            new NoEncoding(),
            new ABuilderThatDoesNothing(),
            new ImagesThatDoNothing(),
            new NoDrives(),
            new ABurnerThatDoesNothing(),
            new AnEraserThatDoesNothing(),
            new ADiskWithNothingOnIt(),
            new NoHashing(),
            new NothingIsRemembered(),
            new ATrayThatShuts(),
            new NothingIsReleased(),
            preview ?? new APreviewOfTheMenu(new RecordingWriter(), new PicturesThatRead()),
            new PicturesThatRead(),
            "2.0.0",
            _ => 1000,
            Patience: new AClockThatMovesItself().Patience));

    private sealed class NoEncoding : IAutorunEncoding
    {
        public string AsAutorunWillSeeIt(string text) => text;
    }

    private sealed class ABuilderThatDoesNothing : IBuildDiscs
    {
        public Task<Outcome<BuiltDiscs>> Build(
            DiscProject project, Medium medium, string outputFolder,
            IProgress<BuildProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BuiltDiscs>>(new BuiltDiscs([]));
    }

    private sealed class ImagesThatDoNothing : IMakeImages
    {
        public Task<Outcome<MadeImage>> Make(
            string stagedFolder, string imagePath, string label, bool wantsLegacyFileSystems,
            int discNumber, int ofDiscs, IProgress<ImageProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<MadeImage>>(new MadeImage(imagePath, 0, null));
    }

    private sealed class NoDrives : IFindRecorders
    {
        public IReadOnlyList<Recorder> All() => [];
    }

    private sealed class ABurnerThatDoesNothing : IBurnDiscs
    {
        public Task<Outcome<BurnedDisc>> Burn(
            string imagePath, Recorder onto, IProgress<BurnProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<BurnedDisc>>(new BurnedDisc(onto.Drive, 0, 0, true));
    }

    private sealed class AnEraserThatDoesNothing : IEraseDiscs
    {
        public Task<Outcome<string>> BlankIt(Recorder disc, IProgress<EraseProgress>? progress, CancellationToken ct) =>
            Task.FromResult<Outcome<string>>("blank");
    }

    private sealed class ADiskWithNothingOnIt : IFileSystem
    {
        public bool FolderExists(string path) => false;

        public FolderListing List(string path) => new(path, []);
    }

    private sealed class NoHashing : IHasher
    {
        public string Sha256(string path, CancellationToken ct) => string.Empty;
    }

    private sealed class NothingIsRemembered : IKeepTheSetsPlace
    {
        public string? WhatItSays(string folder) => null;

        public void Leave(string folder, string text)
        {
        }
    }

    private sealed class ATrayThatShuts : IWorkTheTray
    {
        public bool ShutIt(Recorder drive) => true;

        public bool CanShutItsOwnTray(Recorder drive) => true;
    }

    private sealed class NothingIsReleased : IAskWhatIsReleased
    {
        public Task<WhatWasReleased> TheLatest(CancellationToken ct) =>
            Task.FromResult(new WhatWasReleased(null, null, "no asking in a test"));
    }

    /// <summary>Pictures that can all be read, which is the ordinary case.</summary>
    /// <remarks>
    /// Given the writer beside it, it refuses to put a picture into a folder
    /// nobody made, which is what the disk does. Pictures reach the same
    /// folders as everything else through a different port, so a fake that
    /// never asked was the one way left to write into nowhere and be believed.
    /// </remarks>
    private sealed class PicturesThatRead : IImages
    {
        internal string? Refuse { get; init; }

        /// <summary>The writer making the folders these go into, when there is one.</summary>
        internal RecordingWriter? Beside { get; init; }

        public ImageFacts Read(string path) =>
            Refuse is null ? new ImageFacts(true, 512, 512, null) : new ImageFacts(false, 0, 0, Refuse);

        internal List<string> Icons { get; } = [];

        internal List<string> Backgrounds { get; } = [];

        public void WriteIcon(string source, string destination)
        {
            MustBeSomewhere(destination);
            Icons.Add(destination);
        }

        public void WriteBackground(string source, string destination, int width, int height, bool asIs)
        {
            MustBeSomewhere(destination);
            Backgrounds.Add(destination);
        }

        private void MustBeSomewhere(string path)
        {
            string? folder = System.IO.Path.GetDirectoryName(path);

            if (Beside is not null && folder is not null && !Beside.ThereIsAFolderAt(folder))
            {
                throw new DirectoryNotFoundException(
                    $"Could not find a part of the path '{path}'. Nothing made {folder}.");
            }
        }
    }
}
