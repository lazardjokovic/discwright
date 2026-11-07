using DiscWright.Platform;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// The look somebody chose for the program, discwright#98's "application
/// themes", and the one small note that keeps it.
/// </summary>
/// <remarks>
/// Following Windows is the default and stayed the default when the choice
/// arrived: nobody meets the question unasked, and a file edited into
/// nonsense must not pick a side.
/// </remarks>
public class TheLookSomebodyChose
{
    [Fact]
    public void Follows_Windows_when_nothing_was_ever_chosen()
    {
        ChoosingTheLook choice = new(null, _ => { });

        Assert.Equal(ALook.FollowingWindows, choice.Chosen);
        Assert.Equal("Appearance: following Windows", choice.InWords);
    }

    [Theory]
    [InlineData("light", ALook.Light)]
    [InlineData("dark", ALook.Dark)]
    [InlineData("Dark", ALook.Dark)]
    [InlineData("following windows", ALook.FollowingWindows)]
    public void Starts_where_it_was_left(string kept, ALook expected) =>
        Assert.Equal(expected, new ChoosingTheLook(kept, _ => { }).Chosen);

    [Theory]
    [InlineData("")]
    [InlineData("mauve")]
    [InlineData("  ")]
    public void Takes_nonsense_in_the_file_as_the_default(string kept) =>
        Assert.Equal(ALook.FollowingWindows, new ChoosingTheLook(kept, _ => { }).Chosen);

    [Fact]
    public void Goes_round_the_circle_following_light_dark_and_back()
    {
        ChoosingTheLook choice = new(null, _ => { });

        choice.Next();
        Assert.Equal(ALook.Light, choice.Chosen);
        Assert.Equal("Appearance: light", choice.InWords);

        choice.Next();
        Assert.Equal(ALook.Dark, choice.Chosen);
        Assert.Equal("Appearance: dark", choice.InWords);

        choice.Next();
        Assert.Equal(ALook.FollowingWindows, choice.Chosen);
    }

    [Fact]
    public void Keeps_every_choice_the_moment_it_is_made()
    {
        List<string> kept = [];
        ChoosingTheLook choice = new(null, kept.Add);

        choice.Next();
        choice.Next();
        choice.Next();

        Assert.Equal(["light", "dark", "following windows"], kept);
    }

    [Fact]
    public void A_kept_choice_read_back_is_the_same_choice()
    {
        // The words Keep writes are the words FromText reads, held together
        // here so neither can drift without this going red.
        List<string> kept = [];
        ChoosingTheLook first = new(null, kept.Add);
        first.Next();
        first.Next();

        ChoosingTheLook second = new(kept[^1], _ => { });

        Assert.Equal(ALook.Dark, second.Chosen);
    }

    [Fact]
    public void The_window_hears_about_a_new_choice()
    {
        ChoosingTheLook choice = new(null, _ => { });
        List<string> told = [];
        choice.PropertyChanged += (_, moved) => told.Add(moved.PropertyName!);

        choice.Next();

        Assert.Contains(nameof(ChoosingTheLook.InWords), told, StringComparer.Ordinal);
        Assert.Contains(nameof(ChoosingTheLook.Chosen), told, StringComparer.Ordinal);
    }

    [Theory]
    [InlineData(ALook.FollowingWindows, true, true)]
    [InlineData(ALook.FollowingWindows, false, false)]
    [InlineData(ALook.Light, true, false)]
    [InlineData(ALook.Light, false, false)]
    [InlineData(ALook.Dark, true, true)]
    [InlineData(ALook.Dark, false, true)]
    public void Decides_the_dark_from_the_choice_and_only_then_from_Windows(
        ALook chosen, bool windowsWearsIt, bool dark)
    {
        ChoosingTheLook choice = new(AsKept(chosen), _ => { });

        Assert.Equal(dark, choice.WearsTheDark(windowsWearsIt));
    }

    // ------------------------------------------------------- the note itself

    [Fact]
    public void The_note_remembers_nothing_it_was_never_told()
    {
        string folder = AFreshFolder();

        Assert.Null(new ANoteAboutTheLook(folder).Recalled());
    }

    [Fact]
    public void The_note_gives_back_the_word_it_kept_and_replaces_it()
    {
        string folder = AFreshFolder();

        try
        {
            ANoteAboutTheLook note = new(folder);

            note.Keep("dark");
            Assert.Equal("dark", note.Recalled());

            note.Keep("light");
            Assert.Equal("light", note.Recalled());
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // ------------------------------------------------------------- helpers

    private static string AsKept(ALook look) => look switch
    {
        ALook.Light => "light",
        ALook.Dark => "dark",
        _ => "following windows",
    };

    private static string AFreshFolder() =>
        Path.Combine(Path.GetTempPath(), "dw-look-" + Guid.NewGuid().ToString("N")[..8]);
}
