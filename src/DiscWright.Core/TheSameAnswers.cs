namespace DiscWright.Core;

/// <summary>
/// How one game's "There is more in that folder" was answered, kept to answer
/// the games after it without asking again.
/// </summary>
/// <remarks>
/// <para>
/// xniwo on discwright#98: a compilation of thirty games asked the same
/// question thirty times. The owner approved answering once for all of them
/// on 2026-11-13, for games added together.
/// </para>
/// <para>
/// <strong>By what a row looks like, not by its name.</strong> No two games'
/// manuals are called the same, so the first game's answer for the row that
/// looked like a manual is the answer for every later row that looks like
/// one, and likewise for extras and add-ons. A folder and a file are told
/// apart, so an artbook put beside the installer says nothing about a README
/// left off. A kind of row the first game did not have gets its own
/// suggestion, ticked as it would have been.
/// </para>
/// <para>
/// <strong>A patch is still judged against its own installer.</strong>
/// Whether a patch adds anything depends on the installer it sits beside, so
/// one the first game's installer already covered says nothing about the
/// next game's patches. Only what an add-on becomes carries over; whether it
/// is ticked is worked out again, row by row.
/// </para>
/// </remarks>
public sealed class TheSameAnswers
{
    private readonly Dictionary<(WhatBecomesOfIt, bool), (bool Wanted, WhatBecomesOfIt As)> _byLook;

    private TheSameAnswers(Dictionary<(WhatBecomesOfIt, bool), (bool Wanted, WhatBecomesOfIt As)> byLook) =>
        _byLook = byLook;

    /// <summary>
    /// The answers given, row by row, kept by what each row looked like. The
    /// first row of each look decides it.
    /// </summary>
    public static TheSameAnswers From(IEnumerable<(LeftBehind What, bool Wanted, WhatBecomesOfIt As)> answers)
    {
        Dictionary<(WhatBecomesOfIt, bool), (bool Wanted, WhatBecomesOfIt As)> byLook = [];

        foreach ((LeftBehind what, bool wanted, WhatBecomesOfIt becomes) in answers)
        {
            byLook.TryAdd((what.Suggested, what.IsAFolder), (wanted, becomes));
        }

        return new TheSameAnswers(byLook);
    }

    /// <summary>
    /// Another game's rows, answered the same way: only the ones to go on,
    /// each with what it becomes.
    /// </summary>
    /// <param name="rows">The rows, gathered the way the dialog shows them.</param>
    /// <param name="installer">This game's installer, which its patches are judged against.</param>
    public IEnumerable<(LeftBehind What, WhatBecomesOfIt As)> For(IReadOnlyList<LeftBehind> rows, string? installer)
    {
        foreach (LeftBehind row in rows)
        {
            bool known = _byLook.TryGetValue((row.Suggested, row.IsAFolder), out (bool Wanted, WhatBecomesOfIt As) answer);

            bool wanted = !known || row.Suggested == WhatBecomesOfIt.AddOn
                ? row.WantedAgainst(installer)
                : answer.Wanted;

            if (wanted)
            {
                yield return (row, known ? answer.As : row.Suggested);
            }
        }
    }
}
