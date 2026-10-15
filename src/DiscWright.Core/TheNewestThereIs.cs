namespace DiscWright.Core;

/// <summary>
/// What the place releases are published said about the newest one.
/// </summary>
/// <param name="Tag">What that release is called, as published.</param>
/// <param name="Where">The page to send somebody to, if there is one.</param>
/// <param name="WhyNot">Why it could not be asked, when it could not.</param>
public sealed record WhatWasReleased(string? Tag, string? Where, string? WhyNot = null);

/// <summary>
/// How the build somebody is running compares with the newest one released.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It only ever says that a version exists.</strong> Downloading and
/// replacing a running program is a far bigger thing than reading a number,
/// and doing it to an unsigned program that Windows already distrusts would be
/// worse than not offering it at all.
/// </para>
/// <para>
/// Nothing here goes near a network. It is handed what was said and works out
/// a sentence, which is why every case below can be tested without one.
/// </para>
/// </remarks>
/// <param name="Mine">The version running now.</param>
/// <param name="Theirs">The newest released, as published, or nothing.</param>
/// <param name="Where">The page for it, or nothing.</param>
/// <param name="WhyNot">Why nothing could be found out, when nothing could.</param>
public sealed record HowThisBuildCompares(string Mine, string? Theirs, string? Where, string? WhyNot = null)
{
    /// <summary>
    /// Whether there is a newer one than this, and somewhere to get it.
    /// </summary>
    public bool WorthGetting =>
        WhyNot is null
        && Where is not null
        && Reading(Theirs) is Version theirs
        && Reading(Mine) is Version mine
        && theirs > mine;

    /// <summary>What to tell somebody who pressed the button.</summary>
    public string InWords
    {
        get
        {
            if (WhyNot is not null)
            {
                return $"DiscWright could not ask about newer versions just now. {WhyNot}";
            }

            if (Reading(Mine) is not Version mine)
            {
                return $"This build does not say which version it is, so there is nothing to compare.";
            }

            if (Reading(Theirs) is not Version theirs)
            {
                return Theirs is null
                    ? "Nothing has been released yet, so this is as new as it gets."
                    : $"The newest release is called '{Theirs}', which is not a version number this "
                        + "understands, so they cannot be compared.";
            }

            if (theirs > mine)
            {
                return $"There is a newer DiscWright: {Short(theirs)}. This one is {Short(mine)}.";
            }

            // Which is what every build between two releases looks like, and
            // saying "you are up to date" to somebody running something newer
            // than the shop sells would be a small lie.
            return theirs < mine
                ? $"This is {Short(mine)}, which is newer than anything released. The newest release is "
                    + $"{Short(theirs)}."
                : $"This is the newest one, {Short(mine)}.";
        }
    }

    /// <summary>
    /// A version out of whatever it was written as, or nothing.
    /// </summary>
    /// <remarks>
    /// Tags are written by people. A leading v is the usual habit and this
    /// project's own, and anything after a dash or a plus is a pre-release
    /// marker or a build number rather than part of the version.
    /// </remarks>
    public static Version? Reading(string? written)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return null;
        }

        string text = written.Trim();

        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        int marker = text.IndexOfAny(['-', '+']);

        if (marker >= 0)
        {
            text = text[..marker];
        }

        return Version.TryParse(text, out Version? found) ? found : null;
    }

    /// <summary>
    /// Written the way somebody would say it, without the trailing noughts
    /// .NET adds to make four numbers out of three.
    /// </summary>
    private static string Short(Version version) =>
        version.Revision > 0
            ? version.ToString(4)
            : $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
}

/// <summary>
/// Asking what the newest released version is.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only ever when somebody asks.</strong> Checking on every start is a
/// network call somebody did not make, from a program whose whole point is
/// putting things on a disc so they keep working with no network at all.
/// </para>
/// <para>
/// Nothing identifying is sent. There is no account, no key and nothing about
/// the machine beyond what asking for a web page unavoidably reveals.
/// </para>
/// </remarks>
public interface IAskWhatIsReleased
{
    /// <summary>
    /// The newest release, or why it could not be found out.
    /// </summary>
    /// <remarks>
    /// It reports a failure rather than throwing one, because not being able
    /// to reach GitHub is an ordinary afternoon and not a fault in anything.
    /// </remarks>
    Task<WhatWasReleased> TheLatest(CancellationToken ct);
}
