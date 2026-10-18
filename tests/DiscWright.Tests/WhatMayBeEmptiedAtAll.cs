using DiscWright.Core;

namespace DiscWright.Tests;

/// <summary>
/// The guard on the only destructive thing this program does.
/// </summary>
/// <remarks>
/// <para>
/// It had no test at all, which is a strange thing to be able to say about
/// the one piece of code standing between a bug and somebody's documents. It
/// was found by tripping it: a preview folder named after the project was
/// refused, the preview wrote nothing, and nothing anywhere said so.
/// </para>
/// <para>
/// Both directions matter here and the refusing direction matters more. A
/// guard that lets the right things through is convenient; a guard that keeps
/// the wrong things out is the point of it.
/// </para>
/// </remarks>
public class WhatMayBeEmptiedAtAll
{
    private const string Temp = @"C:\Users\someone\AppData\Local\Temp";

    [Theory]
    [InlineData(@"C:\out\disc")]
    [InlineData(@"C:\out\disc D1")]
    [InlineData(@"C:\out\DISC D12")]
    public void A_staging_folder_may_be_emptied(string path) =>
        Assert.True(WhatMayBeEmptied.Allows(path, Temp));

    [Fact]
    public void A_preview_folder_may_be_emptied_whatever_the_project_is_called() =>
        Assert.True(WhatMayBeEmptied.Allows(
            WhereAPreviewGoes.Under(Temp, "HOLLOW KNIGHT", @"F:\sets\hk", null), Temp));

    [Fact]
    public void A_trailing_separator_changes_nothing() =>
        Assert.True(WhatMayBeEmptied.Allows(
            WhereAPreviewGoes.Under(Temp, "HOLLOW KNIGHT", @"F:\sets\hk", null) + @"\", Temp));

    [Theory]
    [InlineData(@"C:\Users\someone\Documents")]
    [InlineData(@"C:\Users\someone\Documents\My Games")]
    [InlineData(@"C:\")]
    [InlineData(@"F:\DiscWright Testing\Hollow Knight Second test")]
    [InlineData("")]
    [InlineData("   ")]
    public void Anything_else_is_refused(string path) =>
        Assert.False(WhatMayBeEmptied.Allows(path, Temp));

    [Theory]
    [InlineData(@"C:\music\Discography")]
    [InlineData(@"F:\DiscWright Testing")]
    [InlineData(@"C:\out\discs")]
    [InlineData(@"C:\out\disc D1 old")]
    [InlineData(@"C:\out\disconnected")]
    public void A_name_that_merely_begins_with_disc_is_not_a_staging_folder(string path)
    {
        // The rule was StartsWith("disc"), which reads like it names the
        // staging folders and does not. Every one of these would have been
        // emptied, and one of them is somebody's music.
        Assert.False(WhatMayBeEmptied.Allows(path, Temp));
    }

    [Fact]
    public void The_fake_writer_refuses_what_the_real_one_refuses()
    {
        // Testing a test double looks odd until you have been caught by
        // one. Every test that touches staging runs through this fake, so
        // its honesty is load bearing: when it emptied whatever it was
        // handed, eighteen tests went green over a preview that wrote
        // nothing at all.
        //
        // Nothing else can catch this. Taking the guard out of a fake makes
        // no test fail, it only stops them being worth anything, which is
        // exactly what a planted fault proved by walking through it.
        RecordingWriter wrote = new();

        Assert.Throws<ArgumentException>(
            () => wrote.EmptyTheFolder(@"C:\Users\someone\Documents"));

        Assert.Empty(wrote.Emptied);
    }

    [Fact]
    public void A_folder_of_the_same_name_somewhere_else_is_not_ours()
    {
        // Known by where it sits, not by what it is called. Somebody keeping
        // a folder of that name in their documents is keeping their own
        // folder, and it is not this program's to empty.
        string elsewhere = System.IO.Path.Combine(
            @"C:\Users\someone\Documents", WhereAPreviewGoes.TheFolderTheyAllGoIn, "HOLLOW KNIGHT-abcd1234");

        Assert.False(WhatMayBeEmptied.Allows(elsewhere, Temp));
    }

    [Fact]
    public void The_root_the_previews_live_in_is_not_itself_emptied()
    {
        // Emptying the root would take every other project's preview with it.
        // Only a folder inside it is ever ours to clear.
        Assert.False(WhatMayBeEmptied.Allows(
            System.IO.Path.Combine(Temp, WhereAPreviewGoes.TheFolderTheyAllGoIn), Temp));
    }

    [Fact]
    public void A_folder_deeper_inside_a_preview_is_not_the_preview()
    {
        // AUTORUN sits inside a preview folder. It is not the thing to empty,
        // and a rule that matched it would be matching by nothing at all.
        string deeper = System.IO.Path.Combine(
            WhereAPreviewGoes.Under(Temp, "HOLLOW KNIGHT", @"F:\sets\hk", null), "AUTORUN");

        Assert.False(WhatMayBeEmptied.Allows(deeper, Temp));
    }
}
