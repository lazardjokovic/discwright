using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// What the picker shows first for the tune behind the menu, discwright#98,
/// 6 November: "I tried to add an audio track to the menu and it didn't show
/// in DW, before I eventually realised it was flac."
/// </summary>
/// <remarks>
/// Whether the menu's player plays a kind is measured on a real machine and
/// written down in <see cref="MusicForTheMenu"/>. This holds that the picker
/// offers what was measured, in both places a tune is chosen.
/// </remarks>
public class TheMusicThePickerOffers
{
    [Theory]
    [InlineData(".mp3")]
    [InlineData(".flac")]
    [InlineData(".wav")]
    [InlineData(".wma")]
    [InlineData(".m4a")]
    public void A_kind_the_menus_player_plays_is_shown_without_asking_for_anything(string kind)
    {
        Assert.Contains(kind, MusicForTheMenu.Kinds);

        // In the first row of the filter, which is the one a picker opens on.
        string first = MusicForTheMenu.ForThePicker.Split('|')[1];

        Assert.Contains("*" + kind, first.Split(';'));
    }

    [Fact]
    public void A_kind_the_player_was_measured_not_to_play_is_not_offered()
    {
        Assert.DoesNotContain(".ogg", MusicForTheMenu.Kinds);
    }

    [Fact]
    public void The_filter_is_a_name_and_its_kinds_and_then_anything_at_all()
    {
        // The shape a Windows picker wants: name, patterns, name, patterns.
        Assert.Equal(
            ["Music", "*.mp3;*.flac;*.wav;*.wma;*.m4a", "Anything", "*.*"],
            MusicForTheMenu.ForThePicker.Split('|'));
    }

    [Theory]
    [InlineData("MainWindow.xaml.cs")]
    [InlineData("WhatElseGoesOnIt.xaml.cs")]
    public void Both_places_a_tune_is_chosen_ask_the_one_list(string window)
    {
        // There were two copies of the filter, one in each window, and a
        // kind added to one would have been offered in half the program.
        string code = File.ReadAllText(Repository.File("src", "DiscWright.App", window));

        int chooses = code.IndexOf("private void ChooseTheMusic(", StringComparison.Ordinal);

        Assert.True(chooses > 0, $"{window} has nowhere a tune is chosen.");

        string handler = code[chooses..code.IndexOf(';', chooses)];

        Assert.Contains("MusicForTheMenu.ForThePicker,", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("*.mp3", code, StringComparison.Ordinal);
    }
}
