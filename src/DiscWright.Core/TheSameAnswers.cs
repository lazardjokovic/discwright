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
/// <strong>A patch is still judged against its own installer</strong>, as
/// long as the person went along with how the first game's patch was
/// judged. Whether a patch adds anything depends on the installer it sits
/// beside, so one the first game's installer already covered says nothing
/// about the next game's patches. But a patch the person turned off when it
/// was judged worth having, which is what None of them does, is an answer
/// about add-ons and not about that installer, and every later game's
/// add-ons stay off too. Found on the VM on 2026-11-14: None of them, with
/// the tick, still put Ember Fall's update on the disc.
/// </para>
/// </remarks>
public sealed class TheSameAnswers
{
    private readonly Dictionary<(WhatBecomesOfIt, bool), Answer> _byLook;

    private TheSameAnswers(Dictionary<(WhatBecomesOfIt, bool), Answer> byLook) =>
        _byLook = byLook;

    /// <summary>
    /// One look's answer, and whether its tick was only the installer's
    /// judgement left as it was, which is all that marks a later patch to be
    /// judged again.
    /// </summary>
    private readonly record struct Answer(bool Wanted, WhatBecomesOfIt As, bool AsJudged);

    /// <summary>
    /// The answers given, row by row, kept by what each row looked like. The
    /// first row of each look decides it.
    /// </summary>
    /// <param name="answers">Each row, whether it was taken, and what it became.</param>
    /// <param name="installer">The answered game's installer, its patches' judge.</param>
    public static TheSameAnswers From(
        IEnumerable<(LeftBehind What, bool Wanted, WhatBecomesOfIt As)> answers, string? installer)
    {
        Dictionary<(WhatBecomesOfIt, bool), Answer> byLook = [];

        foreach ((LeftBehind what, bool wanted, WhatBecomesOfIt becomes) in answers)
        {
            byLook.TryAdd(
                (what.Suggested, what.IsAFolder),
                new Answer(wanted, becomes, wanted == what.WantedAgainst(installer)));
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
            bool known = _byLook.TryGetValue((row.Suggested, row.IsAFolder), out Answer answer);

            bool wanted = !known || (row.Suggested == WhatBecomesOfIt.AddOn && answer.AsJudged)
                ? row.WantedAgainst(installer)
                : answer.Wanted;

            if (wanted)
            {
                yield return (row, known ? answer.As : row.Suggested);
            }
        }
    }
}
