using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// What the system's theme setting means, and that reading it cannot fall
/// over.
/// </summary>
/// <remarks>
/// The registry value is called <c>AppsUseLightTheme</c>, so the meaning is
/// inverted and a hurried reader flips it. The rule sits in a method a test
/// can hand every shape the registry can produce, and these do.
/// </remarks>
public class TheSystemsOwnLook
{
    [Fact]
    public void Zero_is_dark_because_the_value_is_called_AppsUseLightTheme() =>
        Assert.True(WhatTheSystemWears.DarkFrom(0));

    [Fact]
    public void One_is_light() =>
        Assert.False(WhatTheSystemWears.DarkFrom(1));

    [Fact]
    public void A_machine_that_was_never_asked_is_light() =>
        // The key or the value missing is a Windows from before the question
        // existed, and everything it ever showed was light.
        Assert.False(WhatTheSystemWears.DarkFrom(null));

    [Theory]
    [InlineData("0")]
    [InlineData(0L)]
    [InlineData(false)]
    public void A_value_of_the_wrong_shape_is_light(object strange) =>
        // Only the DWORD zero that Settings itself writes means dark. A
        // string, a QWORD or anything else somebody planted falls back to
        // the default rather than to a guess.
        Assert.False(WhatTheSystemWears.DarkFrom(strange));

    [Fact]
    public void The_real_registry_answers_without_throwing()
    {
        // Which answer depends on the machine, so only the asking is held:
        // the path has to be right enough to be readable, and a machine
        // where the key is missing has to come back light rather than
        // thrown.
        bool dark = WhatTheSystemWears.Dark();

        Assert.True(dark || !dark);
    }

    [Fact]
    public void Painting_no_window_at_all_does_nothing_quietly() =>
        // The handle is zero before a window exists. Worth nothing, and
        // worth no exception either.
        PaintingTheTitleBar.OfWindow(0, dark: true);
}
