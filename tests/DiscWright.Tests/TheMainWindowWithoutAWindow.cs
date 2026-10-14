using System.Text;

using DiscWright.Core;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// What the main window says back, tested with no window.
/// </summary>
/// <remarks>
/// Every one of these would otherwise be a test that needs a desktop, a VM and
/// a workaround for the foreground lock, which is what v1's window suite needs
/// and why it costs minutes rather than milliseconds.
/// </remarks>
public class TheMainWindowWithoutAWindow
{
    [Fact]
    public void Says_nothing_at_all_about_an_empty_window()
    {
        MakeADisc window = New();

        // Somebody who has not added a game yet has not done anything wrong,
        // and telling them so is the program complaining about its own
        // starting state.
        Assert.Null(window.WhyNot);
        Assert.Null(window.Worry);
        Assert.Equal(string.Empty, window.WhatItWillMake);
    }

    [Fact]
    public void Names_the_disc_after_the_first_game_and_then_leaves_it_alone()
    {
        MakeADisc window = New();

        window.Add(AGame("Hollow Knight", 1000));
        Assert.Equal("HOLLOW KNIGHT", window.Label);

        window.Add(AGame("Gothic", 1000));

        // Renaming it is their decision, and a second game must not undo it.
        Assert.Equal("HOLLOW KNIGHT", window.Label);
    }

    [Fact]
    public void Keeps_a_name_somebody_typed_themselves()
    {
        MakeADisc window = New();
        window.Label = "MY COMPILATION";

        window.Add(AGame("Hollow Knight", 1000));

        Assert.Equal("MY COMPILATION", window.Label);
    }

    [Fact]
    public void Says_what_it_would_make_before_anything_is_pressed()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));

        Assert.Equal("one disc", window.WhatItWillMake);
        Assert.True(window.CanMakeIt);
    }

    [Fact]
    public void Counts_the_discs_of_a_set_before_anything_is_pressed()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(3000);
        window.Add(AGame("Big", 1200, parts: 4));

        // A sentence somebody reads, not a figure they compare, so "a set of"
        // rather than a bare number.
        Assert.StartsWith("a set of ", window.WhatItWillMake, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_when_several_games_will_each_get_a_disc()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(1500);
        window.Add(AGame("One", 1000));
        window.Add(AGame("Two", 1000));

        Assert.Equal("2 games, a disc each", window.WhatItWillMake);
    }

    [Fact]
    public void Changes_its_answer_when_the_disc_changes()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(1500);
        window.Add(AGame("One", 1000));
        window.Add(AGame("Two", 1000));

        Assert.Equal("2 games, a disc each", window.WhatItWillMake);

        // A bigger disc turns two discs into one, which is exactly the thing
        // somebody is trying out when they change that dropdown.
        window.Medium = ADiscHolding(5000);

        Assert.Equal("one disc", window.WhatItWillMake);
    }

    [Fact]
    public void Will_not_build_without_a_name()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));
        window.Label = string.Empty;

        Assert.NotNull(window.WhyNot);
        Assert.False(window.CanMakeIt);
    }

    [Fact]
    public void Will_not_build_without_somewhere_to_put_it()
    {
        MakeADisc window = New();
        window.Add(AGame("Hollow Knight", 1000));

        Assert.Null(window.WhyNot);
        Assert.False(window.CanMakeIt);
    }

    [Fact]
    public void Shows_a_refusal_as_things_are_chosen_rather_than_when_the_button_is_pressed()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(500);
        window.Add(AGame("Too Big", 4000));

        // A refusal that arrives after a click is one somebody has already
        // committed to. The same refusal beforehand is just a sentence.
        Assert.NotNull(window.WhyNot);
        Assert.Contains("bigger than a whole disc", window.WhyNot.Headline, StringComparison.OrdinalIgnoreCase);
        Assert.False(window.CanMakeIt);
    }

    [Fact]
    public void Warns_about_a_name_Windows_cannot_show_without_stopping_anything()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Add(AGame("Hollow Knight", 1000));
        window.Label = "\u0414\u0430\u043d\u043d\u044b\u0435";

        // The disc still works, still installs, and somebody may genuinely not
        // mind. What they must not do is find out afterwards.
        Assert.NotNull(window.Worry);
        Assert.Null(window.WhyNot);
        Assert.True(window.CanMakeIt);
    }

    [Fact]
    public void Tells_the_window_when_anything_it_shows_has_changed()
    {
        MakeADisc window = New();
        List<string> told = [];
        window.PropertyChanged += (_, changed) => told.Add(changed.PropertyName ?? string.Empty);

        window.Add(AGame("Hollow Knight", 1000));

        // Without this the window shows the answer to a question somebody has
        // already changed, which is worse than showing nothing.
        Assert.Contains(nameof(MakeADisc.WhatItWillMake), told);
        Assert.Contains(nameof(MakeADisc.CanMakeIt), told);
        Assert.Contains(nameof(MakeADisc.Label), told);
    }

    [Fact]
    public void Forgets_a_game_that_was_taken_off_the_list()
    {
        MakeADisc window = New();
        window.OutputFolder = @"C:\out";
        window.Medium = ADiscHolding(1500);

        GameFolder one = AGame("One", 1000);
        window.Add(one);
        window.Add(AGame("Two", 1000));
        Assert.Equal("2 games, a disc each", window.WhatItWillMake);

        window.Remove(one);

        Assert.Equal("one disc", window.WhatItWillMake);
    }

    // ------------------------------------------------------------------ helpers

    private static MakeADisc New() => new(new Codepage1252(), _ => 0);

    private static Medium ADiscHolding(long bytes) => new("TEST", "a test disc", (2L * 1024 * 1024) + bytes);

    private static GameFolder AGame(string name, int bytes, int parts = 0)
    {
        List<ListedFile> files = [new($"setup_{name}.exe", bytes)];

        for (int part = 1; part <= parts; part++)
        {
            files.Add(new ListedFile($"setup_{name}-{part}.bin", bytes));
        }

        return new GameFolder(@"C:\games\" + name, GameKind.GogInstaller, name, name, files[0], files, [], null);
    }

    private sealed class Codepage1252 : IAutorunEncoding
    {
        private static readonly Encoding Ansi = MakeIt();

        public string AsAutorunWillSeeIt(string text) => Ansi.GetString(Ansi.GetBytes(text));

        private static Encoding MakeIt()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
    }
}
