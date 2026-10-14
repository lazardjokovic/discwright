namespace DiscWright.Core;

/// <summary>
/// What a disc is called, and whether Windows can carry it.
/// </summary>
public static class DiscLabel
{
    /// <summary>
    /// Whether the label will reach somebody as the thing that was typed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null when it is fine. A refusal when it is not, and the window shows
    /// that one as a <em>warning</em> rather than a wall: a disc called
    /// <c>??????? ????</c> still works, still installs, and somebody may
    /// genuinely not mind. What they must not do is find out afterwards.
    /// </para>
    /// <para>
    /// It shows the surviving text rather than saying characters were lost,
    /// because seeing <c>??????? ????</c> is what makes the decision obvious.
    /// </para>
    /// </remarks>
    public static Refusal? WhatWindowsWillMakeOfIt(string label, IAutorunEncoding encoding)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return Refusals.ADiscNeedsAName();
        }

        string surviving = encoding.AsAutorunWillSeeIt(label);

        if (string.Equals(surviving, label, StringComparison.Ordinal))
        {
            return null;
        }

        return Refusals.ThatNameWillNotSurviveOnTheDisc(label, surviving);
    }
}
