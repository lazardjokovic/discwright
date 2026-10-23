using DiscWright.Core;

namespace DiscWright.ViewModels;

/// <summary>
/// What the window makes of what the drives last said: which size to aim at,
/// what became of the drive somebody picked, which drives are worth a tray
/// warning, and what the picker says when somebody hovers it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Asking a drive and deciding what it means are different jobs.</strong>
/// The asking is a pile of COM calls that takes seconds and cannot happen on
/// the thread the window is drawn on; the deciding is four small rules that
/// could be asked a thousand times a second. They lived in one method
/// together, so every one of these rules could only be reached by a test that
/// had a drive in it.
/// </para>
/// <para>
/// None of it touches a drive. Everything here is worked out from what the
/// drives said when they were last asked, which is also how the window
/// manages to answer these questions while it is being drawn.
/// </para>
/// </remarks>
public static class WhatTheDrivesMean
{
    /// <summary>
    /// The size to start aiming at, from what is in the drives, or nothing to
    /// leave it where it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>It never overrules a person.</strong> The default was DVD5 for
    /// everybody always, which is how a 700 MB disc in the tray came to be
    /// planned as one 4.7 GB disc. The moment somebody picks a size
    /// themselves, this stops moving it and only comments.
    /// </para>
    /// <para>
    /// And not while something is running. A set being burned asks for its
    /// next blank, and a blank of a different kind going in would move the
    /// size the window is describing out from under a burn of the old one.
    /// </para>
    /// </remarks>
    /// <param name="drives">What the drives said when they were last asked.</param>
    /// <param name="aimingAt">The size the window is aiming at now.</param>
    /// <param name="somebodyChose">Whether a person has picked a size themselves.</param>
    /// <param name="working">Whether a build or a burn is running.</param>
    public static Medium? TheSizeToStartFrom(
        IReadOnlyList<Recorder> drives, Medium aimingAt, bool somebodyChose, bool working)
    {
        if (somebodyChose || working)
        {
            return null;
        }

        return TheDiscsThatAreIn.TheSizeToStartFrom(drives) is Medium found && found != aimingAt
            ? found
            : null;
    }

    /// <summary>
    /// The drive somebody picked, as it is now, or nothing when it has gone.
    /// </summary>
    /// <remarks>
    /// By letter rather than by the record, because what is in it has almost
    /// certainly changed since it was picked: the record is a photograph of a
    /// drive at a moment, and the moment has passed. A drive that has been
    /// unplugged is not a choice any more, which is not hypothetical here,
    /// because the slim writer lives on a USB cable.
    /// </remarks>
    public static Recorder? TheSameDriveAgain(Recorder? chosen, IReadOnlyList<Recorder> drives) =>
        chosen is null
            ? null
            : drives.FirstOrDefault(each =>
                string.Equals(each.Drive, chosen.Drive, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The drives a tray warning could be about: the ones that were asked,
    /// narrowed to the one somebody picked once they have picked one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Once a drive is chosen the warning is about that one. A sentence about
    /// the drive somebody is not using is noise, and one left over from the
    /// drive they were using a moment ago is worse than noise.
    /// </para>
    /// <para>
    /// Only drives that were really asked are in the question at all, so
    /// there is no drive to guess about. A default either way would be a rule
    /// nothing could reach, and one of the two defaults would have somebody
    /// standing by a drive that never wanted them.
    /// </para>
    /// </remarks>
    /// <param name="lastSeen">What the drives said when they were last asked.</param>
    /// <param name="answered">Which drives said whether they can shut their own tray.</param>
    /// <param name="chosen">The letter of the drive somebody picked, or nothing.</param>
    public static IReadOnlyList<Recorder> TheOnesToAskAboutTrays(
        IReadOnlyList<Recorder> lastSeen, IReadOnlyDictionary<string, bool> answered, string? chosen) =>
    [
        .. lastSeen
            .Where(each => answered.ContainsKey(each.Drive))
            .Where(each => chosen is null
                || string.Equals(each.Drive, chosen, StringComparison.OrdinalIgnoreCase))
    ];

    /// <summary>
    /// What the drive picker says when somebody hovers it: what it does when
    /// nobody chooses, and any tray that needs a push.
    /// </summary>
    /// <remarks>
    /// The tray warning had a line of its own on the window and that was
    /// still a line, on a window where every row was being counted. It
    /// belongs on the picker: the warning is about a drive, the picker is
    /// where a drive is chosen, and hovering it is where somebody already
    /// goes to ask what the drives are doing.
    /// </remarks>
    /// <param name="aboutTheTray">The tray warning, or nothing at all.</param>
    /// <param name="whyItMatters">The reason behind the warning.</param>
    public static string WhatThePickerSays(string aboutTheTray, string whyItMatters)
    {
        const string leaveIt =
            "Leave this alone and DiscWright works it out, and asks only if there is something to ask.";

        // Length rather than "is string", which an empty one also is. With no
        // tray to warn about, that joined a full stop and a space to nothing
        // and hung a paragraph about a problem nobody had under a sentence
        // that had already answered the question.
        return aboutTheTray.Length > 0
            ? leaveIt + "\n\n" + aboutTheTray + ". " + whyItMatters
            : leaveIt;
    }
}
