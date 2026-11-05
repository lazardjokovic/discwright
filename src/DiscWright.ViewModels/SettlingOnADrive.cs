using DiscWright.Core;

namespace DiscWright.ViewModels;

/// <summary>
/// Finding a drive that will take what is about to be written: waiting for one
/// that has not said what is in it yet, asking which when there is a choice,
/// and offering to erase the disc that is in the way.
/// </summary>
/// <remarks>
/// <para>
/// This was three private methods in <see cref="MakeADisc"/>, and the only way
/// into any of them was to burn something. <strong>A mutation pass found half
/// of what is here untouched by any test at all:</strong> the offer to erase
/// the disc in a drive somebody had already picked, which is the branch
/// somebody meets when the second disc of a set turns out to be a used one.
/// That is the branch Lazar hit on purpose, and the one worth being sure of.
/// </para>
/// <para>
/// <strong>It answers, it does not act.</strong> What comes back is the drive,
/// a refusal to show somebody, or the fact that they were asked and said none
/// of them. Nothing here burns anything, and nothing here touches a window.
/// </para>
/// <para>
/// The questions arrive as delegates, the way <see cref="TakingFoldersOn"/>
/// takes its own, so the window supplies dialogs and a test supplies answers.
/// </para>
/// </remarks>
/// <param name="drives">What says which recorders are on the machine.</param>
/// <param name="eraser">What blanks a rewritable one, when somebody says so.</param>
/// <param name="say">
/// One line about what is happening, and how far through it is. Erasing a disc
/// takes most of a minute, and a window that says nothing for that long looks
/// like a window that has died.
/// </param>
/// <param name="patience">
/// How long a drive gets to answer. What a real one needs, unless a test hands
/// in something quicker.
/// </param>
public sealed class SettlingOnADrive(
    IFindRecorders drives,
    IEraseDiscs eraser,
    Action<string, double> say,
    HowLongADriveGets? patience = null)
{
    private readonly HowLongADriveGets _patience = patience ?? HowLongADriveGets.WhatARealOneNeeds;

    /// <summary>
    /// Whichever drive can take it, asking when there is a choice to make.
    /// </summary>
    /// <param name="bytes">
    /// What is to be written, so it can be measured against the disc.
    /// </param>
    /// <param name="chooseADrive">
    /// Asked only when more than one drive is ready, because choosing for
    /// somebody is how a disc ends up written in the wrong one.
    /// </param>
    /// <param name="whichToErase">
    /// Asked only when nothing is ready and an erase would fix it, and given
    /// every disc that an erase would fix. With one there is a question to
    /// answer; with two there is a choice to make, and neither is a decision
    /// to take on somebody's behalf. Answering with nothing erases nothing.
    /// </param>
    public Task<Settled> OnWhicheverIsReady(
        long bytes,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> chooseADrive,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> whichToErase,
        CancellationToken ct = default) =>
        Settle(null, bytes, chooseADrive, whichToErase, ct);

    /// <summary>
    /// This drive and no other, whatever else is on the machine.
    /// </summary>
    /// <remarks>
    /// For a drive somebody picked in the window, and for every disc of a set
    /// after the first: a set that wanders between two writers is a set whose
    /// discs were written by two different lasers at two different speeds. So
    /// nothing is asked about which drive here. There is nothing to ask.
    /// </remarks>
    /// <remarks>
    /// <strong>An outcome rather than a <see cref="Settled"/>, because two of
    /// the three answers cannot happen.</strong> Nobody is asked which drive,
    /// so nobody can answer with none of them. Saying that in the type rather
    /// than in a comment took a branch out of the window that nothing could
    /// reach and nothing could test.
    /// </remarks>
    public async Task<Outcome<Recorder>> OnThatOne(
        string drive,
        long bytes,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> whichToErase,
        CancellationToken ct = default) =>
        await Settle(drive, bytes, null, whichToErase, ct) switch
        {
            Settled.ThisOne(Recorder ready) => ready,
            Settled.No(Refusal why) => why,
            _ => Refusals.ThereIsNoDiscWriter()
        };

    /// <summary>
    /// What the drives are holding, once the one being waited on has something
    /// to say or has had long enough.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A disc that has just been pushed in is still being read, and asking
    /// then throws rather than answering, which becomes
    /// <see cref="Recorder.WhyNot"/>.
    /// </para>
    /// <para>
    /// That is not a refusal, it is a drive that has not finished thinking,
    /// and treating it as one is worse than it sounds: WhyNot also makes
    /// <see cref="Recorder.ErasingWouldHelp"/> false, so the offer to erase
    /// the disc never appears and somebody who put a used disc in is told the
    /// drive would not say what is in it, with nothing to do about it.
    ///
    /// Found by Lazar putting a used disc in for the second disc of a set on
    /// purpose, and getting a dead end instead of the question.
    /// </para>
    /// <para>
    /// Public because erasing a disc on its own needs the same patience and
    /// none of the rest of this. It used to ask the drives out in the open,
    /// where a drive that answered with a COM failure reached somebody as raw
    /// exception text.
    /// </para>
    /// </remarks>
    /// <param name="onlyThisOne">
    /// The drive being waited on, or nothing to wait on any of them.
    /// </param>
    public async Task<IReadOnlyList<Recorder>> OnceTheyHaveAnswered(
        string? onlyThisOne, CancellationToken ct = default)
    {
        IReadOnlyList<Recorder> all = drives.All();

        // AN EMPTY TRAY HAS ANSWERED. It arrives as a WhyNot like a disc that
        // is still being read, and it is not one: nothing is going to change
        // about a drive with nothing in it. Waiting on it cost fifteen seconds
        // of silence on every erase and every burn on any machine with a spare
        // drive, which is most of them. Found by Lazar pressing Erase with an
        // empty second drive and watching nothing happen.
        bool StillThinking(IReadOnlyList<Recorder> found) => found.Any(each =>
            each.WhyNot is not null
            && !each.NothingInIt
            && (onlyThisOne is null
                || string.Equals(each.Drive, onlyThisOne, StringComparison.OrdinalIgnoreCase)));

        if (!StillThinking(all))
        {
            return all;
        }

        say("waiting for the drive", 0);

        for (DateTimeOffset until = _patience.Now() + _patience.AtMost; _patience.Now() < until;)
        {
            await _patience.Waiting(_patience.Between, ct);

            all = drives.All();

            if (!StillThinking(all))
            {
                return all;
            }
        }

        return all;
    }

    /// <summary>
    /// Every disc an erase would make usable, or why there is not one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For erasing on its own, which has nothing to do with writing anything:
    /// somebody with a drawer of used rewritables wants them blank again, and
    /// until there was a button for it the only way to erase one was to be
    /// half way through a burn and be asked.
    /// </para>
    /// <para>
    /// Here rather than in the window because it is the same question the
    /// settling asks on the way to a burn, with the same patience in front of
    /// it. In the window it was four lines nothing could reach without a drive
    /// and a used disc.
    /// </para>
    /// </remarks>
    public async Task<Outcome<IReadOnlyList<Recorder>>> WhichCouldBeErased(CancellationToken ct = default)
    {
        IReadOnlyList<Recorder> all = await OnceTheyHaveAnswered(null, ct);

        Recorder[] could = [.. all.Where(each => each.ErasingWouldHelp)];

        if (could.Length > 0)
        {
            return could;
        }

        // Which drives were asked, because "there is nothing to erase" about a
        // machine with two drives in it is a sentence somebody will argue with.
        return Refusals.ThereIsNothingToErase([.. all.Select(each => each.Drive)]);
    }

    /// <summary>
    /// The whole decision, with the one difference between the two ways in
    /// being whether there is a drive to ask about.
    /// </summary>
    /// <remarks>
    /// One method rather than the two it used to be. They were the same twenty
    /// lines with the filter narrowed, which meant the erase offer existed
    /// twice, was tested once, and could drift.
    /// </remarks>
    private async Task<Settled> Settle(
        string? onlyThisOne,
        long bytes,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>>? chooseADrive,
        Func<IReadOnlyList<Recorder>, Task<Recorder?>> whichToErase,
        CancellationToken ct)
    {
        IReadOnlyList<Recorder> all = await OnceTheyHaveAnswered(onlyThisOne, ct);

        // Twice at most: once as it stands, and once more after an erase,
        // because erasing changes the answer. Never a third time. A disc that
        // still cannot be written to after being blanked has something else
        // wrong with it, and offering the same erase again is a loop with
        // somebody's evening in it.
        for (bool mayErase = true; ; mayErase = false)
        {
            Settled settled = WhichOfThemWillDo(all, onlyThisOne, bytes);

            if (settled is Settled.ThisOne)
            {
                return settled;
            }

            // Two blanks in two drives, which is a choice and not a problem.
            // Never asked about a drive somebody already named, where there is
            // nothing to choose between.
            if (chooseADrive is not null)
            {
                Recorder[] ready = [.. all.Where(each => each.Ready)];

                if (ready.Length > 1)
                {
                    return await chooseADrive(ready) is Recorder said
                        ? new Settled.ThisOne(said)
                        : new Settled.NobodyChose();
                }
            }

            // The disc in the way, and only the one in the drive being settled
            // on. A used disc in the other drive is nobody's business here,
            // and offering to wipe it would be offering to wipe a disc nothing
            // is being written to.
            Recorder[] erasable =
            [
                .. all.Where(each =>
                    each.ErasingWouldHelp
                    && (onlyThisOne is null
                        || string.Equals(each.Drive, onlyThisOne, StringComparison.OrdinalIgnoreCase)))
            ];

            if (mayErase && erasable.Length > 0 && await whichToErase(erasable) is Recorder chosen)
            {
                if (await Erase(chosen, ct) is Refusal why)
                {
                    return new Settled.No(why);
                }

                all = drives.All();
                continue;
            }

            return settled;
        }
    }

    /// <summary>The drive out of what they are holding, or the refusal to show.</summary>
    private static Settled WhichOfThemWillDo(
        IReadOnlyList<Recorder> all, string? onlyThisOne, long bytes)
    {
        switch (WhichRecorder.For(all, bytes, onlyThisOne ?? string.Empty))
        {
            case Outcome<Recorder>.Ok(Recorder ready):
                return new Settled.ThisOne(ready);

            case Outcome<Recorder>.No refused:
                return new Settled.No(refused.Refusal);

            default:
                return new Settled.No(Refusals.ThereIsNoDiscWriter());
        }
    }

    /// <summary>
    /// Blanks a disc, saying so while it happens, and says why not when it
    /// will not.
    /// </summary>
    /// <remarks>
    /// <strong>Nothing calls this on its own initiative.</strong> Somebody has
    /// said which disc, either in answer to the offer above or by choosing one
    /// to erase on its own, and whatever is on it goes.
    /// </remarks>
    public async Task<Refusal?> Erase(Recorder chosen, CancellationToken ct = default)
    {
        say($"erasing the {chosen.Holding} in {chosen.Drive}", 0);

        Outcome<string> erased = await eraser.BlankIt(
            chosen, new Progress<EraseProgress>(step => say(step.Doing, step.HowFar ?? 0)), ct);

        return erased is Outcome<string>.No stopped ? stopped.Refusal : null;
    }
}

/// <summary>What came of looking for a drive to write to.</summary>
public abstract record Settled
{
    private Settled()
    {
    }

    /// <summary>
    /// This drive, with a disc in it that will take what is to be written.
    /// </summary>
    public sealed record ThisOne(Recorder Drive) : Settled;

    /// <summary>Why nothing can be written, in words somebody can act on.</summary>
    public sealed record No(Refusal Why) : Settled;

    /// <summary>Asked which drive, and the answer was none of them.</summary>
    /// <remarks>
    /// Not a refusal, and nothing to show anybody: a question answered with no
    /// is a decision. Declining to erase a disc is a different thing and comes
    /// back as a <see cref="No"/>, because the disc in the way is still in the
    /// way and the refusal is what says so.
    /// </remarks>
    public sealed record NobodyChose : Settled;
}

/// <summary>
/// How long a drive gets to say what is in it, and how the waiting is done.
/// </summary>
/// <remarks>
/// <para>
/// A drive asked the moment a tray shuts can take ten seconds to answer,
/// measured on the ASUS writer here. Fifteen is that with room to spare, and
/// it only ever costs anything when something is already wrong.
/// </para>
/// <para>
/// <strong>The waiting and the clock are handed in so that a test can have
/// both instantly.</strong> The two tests of a drive that is slow to read the
/// next disc sat through three real seconds each, and nothing could reach the
/// giving up at the end at all without making the suite wait a quarter of a
/// minute for it. A fake that moves its own clock does both in no time, which
/// is how the giving up came to be tested.
/// </para>
/// </remarks>
/// <param name="Between">How long to leave it between asking a drive and asking again.</param>
/// <param name="AtMost">How long to go on asking before taking silence for an answer.</param>
/// <param name="Waiting">How the waiting happens, which is <c>Task.Delay</c> on a real machine.</param>
/// <param name="Now">What the time is, for working out when long enough has passed.</param>
public sealed record HowLongADriveGets(
    TimeSpan Between,
    TimeSpan AtMost,
    Func<TimeSpan, CancellationToken, Task> Waiting,
    Func<DateTimeOffset> Now)
{
    /// <summary>What a real drive on a real machine needs.</summary>
    public static HowLongADriveGets WhatARealOneNeeds { get; } = new(
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(15),
        Task.Delay,
        () => DateTimeOffset.UtcNow);
}
