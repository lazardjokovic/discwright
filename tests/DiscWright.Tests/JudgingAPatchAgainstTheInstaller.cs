using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// Telling a patch that matters from one the installer already covers,
/// from the two file names alone, and the words the dialog says about it.
/// </summary>
/// <remarks>
/// The owner's design on 2026-11-09. A wrong "adds nothing" costs somebody
/// the one patch they needed, so everything the names do not say cleanly
/// answers that there is no way to tell, and no way to tell keeps the file.
/// </remarks>
public class JudgingAPatchAgainstTheInstaller
{
    private const string Installer = "setup_hollow_knight_1.5.12620_(64bit)_(89718).exe";

    [Theory]
    [InlineData("patch_hollow_knight_1.5.12459_to_1.5.12618_(89712).exe")]
    [InlineData("patch_hollow_knight_1.5.12618_to_1.5.12620_(89718).exe")]
    public void A_patch_whose_build_the_installer_carries_adds_nothing(string patch)
    {
        // The builds in parentheses are trusted first, because GOG's builds
        // only ever grow. The second case is the exact build the installer
        // carries, which is just as included as an older one.
        Assert.Equal(WhatAPatchAdds.NothingTheInstallerLacks, APatchAgainstTheInstaller.For(Installer, patch));
    }

    [Fact]
    public void A_patch_whose_build_is_newer_goes_past_the_installer()
    {
        Assert.Equal(
            WhatAPatchAdds.SomethingPastTheInstaller,
            APatchAgainstTheInstaller.For(Installer, "patch_hollow_knight_1.5.12620_to_1.6.0_(90001).exe"));
    }

    [Fact]
    public void The_build_wins_even_when_the_versions_would_say_otherwise()
    {
        // The versions here say the patch reaches less than the installer
        // carries, and the build says it is newer. When both are present
        // the build is the one GOG itself counts by, so the build wins.
        Assert.Equal(
            WhatAPatchAdds.SomethingPastTheInstaller,
            APatchAgainstTheInstaller.For(Installer, "patch_hollow_knight_1.5.12620_to_1.5.12619_(89720).exe"));
    }

    [Theory]
    [InlineData("patch_a_game_1.0_to_1.1.exe", "setup_a_game_1.2.exe", WhatAPatchAdds.NothingTheInstallerLacks)]
    [InlineData("patch_a_game_1.2_to_1.3.exe", "setup_a_game_1.2.exe", WhatAPatchAdds.SomethingPastTheInstaller)]
    [InlineData("patch_a_game_1.1_to_1.2.exe", "setup_a_game_1.2.exe", WhatAPatchAdds.NothingTheInstallerLacks)]
    [InlineData("patch_a_game_1.5_to_1.5.1.exe", "setup_a_game_1.5.exe", WhatAPatchAdds.SomethingPastTheInstaller)]
    [InlineData("patch_a_game_1.31_to_1.31a.exe", "setup_a_game_1.31.exe", WhatAPatchAdds.SomethingPastTheInstaller)]
    public void Without_builds_the_dotted_versions_decide(string patch, string installer, WhatAPatchAdds verdict)
    {
        Assert.Equal(verdict, APatchAgainstTheInstaller.For(installer, patch));
    }

    [Theory]
    [InlineData("patch_a_game_1.0_to_1.1.exe", "setup_doom_2.exe")]
    [InlineData("patch_a_game_1.0_to_1.1.exe", "setup_gwent.exe")]
    [InlineData("patch_a_game_1.0_to_2.x.exe", "setup_a_game_1.2.exe")]
    [InlineData("soundtrack_a_game.exe", "setup_a_game_1.2.exe")]
    public void What_the_names_do_not_say_cleanly_is_no_way_to_tell(string patch, string installer)
    {
        // The 2 in doom_2 is the title, not a version: a rule that took any
        // number would read it as one and call a useful patch covered. A
        // number with no dot in it proves nothing here.
        Assert.Equal(WhatAPatchAdds.NoWayToTell, APatchAgainstTheInstaller.For(installer, patch));
    }

    [Fact]
    public void A_game_with_no_installer_judges_nothing()
    {
        Assert.Equal(
            WhatAPatchAdds.NoWayToTell,
            APatchAgainstTheInstaller.For(null, "patch_a_game_1.0_to_1.1.exe"));
    }

    [Fact]
    public void Says_what_a_patch_reaches_and_what_the_installer_installs()
    {
        Assert.Equal(
            "1.5.12618",
            APatchAgainstTheInstaller.WhatItReaches("patch_hollow_knight_1.5.12459_to_1.5.12618_(89712).exe"));
        Assert.Equal("1.5.12620", APatchAgainstTheInstaller.WhatTheInstallerInstalls(Installer));
        Assert.Null(APatchAgainstTheInstaller.WhatItReaches("soundtrack_a_game.exe"));
        Assert.Null(APatchAgainstTheInstaller.WhatTheInstallerInstalls("setup_gwent.exe"));
    }

    // ------------------------------------------------ the dialog's words

    [Fact]
    public void A_covered_patch_arrives_unticked_and_says_why()
    {
        LeftBehind patch = ALeftBehind("patch_hollow_knight_1.5.12459_to_1.5.12618_(89712).exe");

        Assert.False(patch.WantedAgainst(Installer));
        Assert.Equal(
            "Reaches 1.5.12618, which the installer already includes, so it adds nothing to this disc.",
            patch.ExplainedAgainst(Installer));
    }

    [Fact]
    public void A_patch_going_past_the_installer_arrives_ticked_and_says_what_it_updates_to()
    {
        LeftBehind patch = ALeftBehind("patch_hollow_knight_1.5.12620_to_1.6.0_(90001).exe");

        Assert.True(patch.WantedAgainst(Installer));
        Assert.Equal(
            "Updates the installed game past this disc's installer, to 1.6.0.",
            patch.ExplainedAgainst(Installer));
    }

    [Fact]
    public void A_patch_nobody_can_judge_keeps_the_old_words_and_the_tick()
    {
        LeftBehind patch = ALeftBehind("patch_a_game_1.0_to_1.1.exe");

        Assert.True(patch.WantedAgainst("setup_gwent.exe"));
        Assert.Equal(
            "Looks like something that installs onto the game, so: Update 1.1",
            patch.ExplainedAgainst("setup_gwent.exe"));
    }

    [Fact]
    public void A_file_to_keep_is_untouched_by_any_of_this()
    {
        LeftBehind readme = new(new ListedFile("READ ME.txt", 10), LeftBehindKind.LooksLikeSomethingToKeep);

        Assert.True(readme.WantedAgainst(Installer));
        Assert.Equal("Looks like something to read or look at", readme.ExplainedAgainst(Installer));
    }

    // -------------------------------------- the sentence above the rows

    [Fact]
    public void The_dialog_explains_patches_only_when_there_are_any()
    {
        Assert.Null(WhatWasLeftBehind.AboutThePatches(
            Installer,
            [ALeftBehind("soundtrack_a_game.exe"), new LeftBehind(new ListedFile("READ ME.txt", 10), LeftBehindKind.LooksLikeSomethingToKeep)]));

        Assert.Equal(
            "A patch is GOG's small installer that takes the game from one version to the next. "
                + "The installer going on this disc already installs 1.5.12620.",
            WhatWasLeftBehind.AboutThePatches(Installer, [ALeftBehind("patch_a_game_1.0_to_1.1.exe")]));
    }

    [Fact]
    public void The_dialog_is_humbler_when_the_installer_names_no_version()
    {
        Assert.Equal(
            "A patch takes an installed game from one version to the next. DiscWright cannot "
                + "tell which version this game's installer carries, so it suggests keeping every patch.",
            WhatWasLeftBehind.AboutThePatches("setup_gwent.exe", [ALeftBehind("patch_a_game_1.0_to_1.1.exe")]));
    }

    [Fact]
    public void The_window_hands_the_dialog_the_installer_and_reads_the_defaults_from_the_screen()
    {
        // Nothing in this project can reference the code behind, so it is
        // read off disk, like the markup guards: the dialog's rows take
        // their tick and their line from the one place that can be tested,
        // instead of deciding either themselves.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", "WhatElseIsInThere.xaml.cs"));

        Assert.Contains("game.Installer?.Name", code, StringComparison.Ordinal);
        Assert.Contains("what.WantedAgainst(installer)", code, StringComparison.Ordinal);
        Assert.Contains("What.ExplainedAgainst(_installer)", code, StringComparison.Ordinal);
        Assert.Contains("WhatWasLeftBehind.AboutThePatches(installer, left)", code, StringComparison.Ordinal);
    }

    private static LeftBehind ALeftBehind(string name) =>
        new(new ListedFile(name, 1000), LeftBehindKind.LooksLikeAnAddOn);
}
