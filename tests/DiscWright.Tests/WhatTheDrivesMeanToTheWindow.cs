using DiscWright.Core;
using DiscWright.ViewModels;

namespace DiscWright.Tests;

/// <summary>
/// What the window makes of what the drives last said.
/// </summary>
/// <remarks>
/// <para>
/// Four rules that used to sit inside the method that asks the drives, which
/// is a pile of COM calls taking seconds and running off the thread the window
/// is drawn on. Reaching any of these rules meant having a drive.
/// </para>
/// <para>
/// None of this touches a drive. It is all worked out from what they said when
/// they were last asked, which is also how the window answers these questions
/// while it is being drawn.
/// </para>
/// </remarks>
public class WhatTheDrivesMeanToTheWindow
{
    // ------------------------------------------------- the size to aim at

    [Fact]
    public void Takes_the_size_from_the_disc_that_is_in_the_drive()
    {
        // The default was DVD5 for everybody always, which is how a 700 MB
        // disc in the tray came to be planned as one 4.7 GB disc.
        Medium? found = WhatTheDrivesMean.TheSizeToStartFrom(
            [ABlankCd], Media.Dvd5, somebodyChose: false, working: false);

        Assert.Equal("CD", Assert.IsType<Medium>(found).Key);
    }

    [Fact]
    public void Leaves_the_size_alone_once_somebody_has_chosen_one_themselves()
    {
        // It never overrules a person. The moment somebody picks a size, this
        // stops moving it and only comments.
        Assert.Null(WhatTheDrivesMean.TheSizeToStartFrom(
            [ABlankCd], Media.Dvd5, somebodyChose: true, working: false));
    }

    [Fact]
    public void Leaves_the_size_alone_while_something_is_running()
    {
        // A set being burned asks for its next blank, and a blank of another
        // kind going in would move the size the window is describing out from
        // under a burn of the old one.
        Assert.Null(WhatTheDrivesMean.TheSizeToStartFrom(
            [ABlankCd], Media.Dvd5, somebodyChose: false, working: true));
    }

    [Fact]
    public void Says_nothing_when_the_size_is_already_the_one_in_the_drive()
    {
        // Nothing to say rather than the same answer again, because the
        // window announces whatever comes back from this.
        Assert.Null(WhatTheDrivesMean.TheSizeToStartFrom(
            [ABlankCd], Media.Cd, somebodyChose: false, working: false));
    }

    [Fact]
    public void Says_nothing_when_no_drive_holds_a_disc_to_judge()
    {
        Assert.Null(WhatTheDrivesMean.TheSizeToStartFrom(
            [], Media.Dvd5, somebodyChose: false, working: false));
    }

    // --------------------------------------- the drive somebody picked

    [Fact]
    public void Finds_the_same_drive_again_with_whatever_is_in_it_now()
    {
        Recorder asPicked = new("id-D", "D:", "A Drive", 2, true, 359_847, [750]);
        Recorder asItIsNow = new("id-D", "D:", "A Drive", 3, false, 0, [750]);

        Recorder again = Assert.IsType<Recorder>(
            WhatTheDrivesMean.TheSameDriveAgain(asPicked, [asItIsNow, AnotherDrive]));

        // By letter, not by the record. The record is a photograph of a drive
        // at a moment, and the moment has passed: this one has had a used
        // disc put in it since it was picked.
        Assert.Equal("D:", again.Drive);
        Assert.False(again.Blank);
        Assert.Same(asItIsNow, again);
    }

    [Fact]
    public void Loses_the_drive_that_has_been_unplugged()
    {
        Recorder asPicked = new("id-D", "D:", "A Drive", 2, true, 359_847, [750]);

        // Not hypothetical: the slim writer here lives on a USB cable.
        Assert.Null(WhatTheDrivesMean.TheSameDriveAgain(asPicked, [AnotherDrive]));
    }

    [Fact]
    public void Has_no_drive_to_find_when_nobody_picked_one()
    {
        Assert.Null(WhatTheDrivesMean.TheSameDriveAgain(null, [ABlankCd, AnotherDrive]));
    }

    [Fact]
    public void Knows_a_drive_by_its_letter_whatever_case_it_is_written_in()
    {
        Recorder asPicked = new("id-D", "d:", "A Drive", 2, true, 359_847, [750]);

        Assert.NotNull(WhatTheDrivesMean.TheSameDriveAgain(asPicked, [ABlankCd]));
    }

    // ----------------------------------------- which trays to warn about

    [Fact]
    public void Asks_about_the_trays_of_every_drive_that_answered()
    {
        IReadOnlyList<Recorder> asking = WhatTheDrivesMean.TheOnesToAskAboutTrays(
            [ABlankCd, AnotherDrive], BothAnswered, chosen: null);

        Assert.Equal<IEnumerable<string>>(["D:", "E:"], asking.Select(each => each.Drive));
    }

    [Fact]
    public void Leaves_out_a_drive_that_never_said_whether_its_tray_shuts()
    {
        // Only drives that were really asked are in the question at all, so
        // there is no drive to guess about. A default either way would be a
        // rule nothing could reach, and one of them would have somebody
        // standing by a drive that never wanted them.
        IReadOnlyList<Recorder> asking = WhatTheDrivesMean.TheOnesToAskAboutTrays(
            [ABlankCd, AnotherDrive], OnlyDAnswered, chosen: null);

        Assert.Equal<IEnumerable<string>>(["D:"], asking.Select(each => each.Drive));
    }

    [Fact]
    public void Narrows_the_tray_question_to_the_drive_somebody_picked()
    {
        // A sentence about the drive somebody is not using is noise, and one
        // left over from the drive they were using a moment ago is worse.
        IReadOnlyList<Recorder> asking = WhatTheDrivesMean.TheOnesToAskAboutTrays(
            [ABlankCd, AnotherDrive], BothAnswered, chosen: "E:");

        Assert.Equal<IEnumerable<string>>(["E:"], asking.Select(each => each.Drive));
    }

    // -------------------------------------------- what the picker says

    [Fact]
    public void Says_what_the_picker_does_when_there_is_no_tray_to_warn_about()
    {
        string says = WhatTheDrivesMean.WhatThePickerSays(string.Empty, "because trays matter");

        // One sentence and nothing else. An empty warning used to join a full
        // stop and a space to nothing and hang a paragraph about a problem
        // nobody had under a sentence that had already answered the question.
        Assert.Equal(
            "Leave this alone and DiscWright works it out, and asks only if there is something to ask.",
            says);

        Assert.DoesNotContain("because trays matter", says, StringComparison.Ordinal);
    }

    [Fact]
    public void Hangs_the_tray_warning_under_it_when_there_is_one()
    {
        string says = WhatTheDrivesMean.WhatThePickerSays(
            "E: cannot shut its own tray", "somebody has to push it");

        Assert.StartsWith("Leave this alone", says, StringComparison.Ordinal);
        Assert.Contains("E: cannot shut its own tray. somebody has to push it", says, StringComparison.Ordinal);

        // A blank line between them, because the two are a sentence about the
        // picker and a warning about a drive, and running them together reads
        // as one thought.
        Assert.Contains("\n\n", says, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- the rig

    private static readonly Recorder ABlankCd = new("id-D", "D:", "A Drive", 2, true, 359_847, [750]);

    private static readonly Recorder AnotherDrive =
        new("id-E", "E:", "Another Drive", 2, true, 359_847, [750]);

    private static readonly IReadOnlyDictionary<string, bool> BothAnswered =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["D:"] = true, ["E:"] = false };

    private static readonly IReadOnlyDictionary<string, bool> OnlyDAnswered =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["D:"] = true };
}
