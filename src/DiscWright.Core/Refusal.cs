namespace DiscWright.Core;

/// <summary>
/// A no, carrying everything somebody needs in order to act on it.
/// </summary>
/// <remarks>
/// A type rather than a string, because a type has fields that somebody has to
/// fill in. Every refusal in the design names the thing, gives both numbers,
/// and offers the fix; left as a string, the second and third quietly stop
/// happening the first time anyone is in a hurry.
/// </remarks>
/// <param name="Headline">One line, in plain words, saying what will not work.</param>
/// <param name="Because">Why, naming the file or the game or the option at fault.</param>
/// <param name="Figures">The numbers, so nobody has to take it on trust.</param>
/// <param name="Remedy">What to do about it, or null when there is nothing to offer.</param>
public sealed record Refusal(
    string Headline,
    string Because,
    IReadOnlyList<Figure> Figures,
    Remedy? Remedy)
{
    /// <summary>
    /// A refusal with nothing to count and nothing to offer. Rare: most
    /// refusals in this app are about a size, and a size has two numbers.
    /// </summary>
    public Refusal(string headline, string because)
        : this(headline, because, [], null)
    {
    }
}

/// <summary>
/// One number worth putting beside a refusal, such as "needs" and "2.4 GB".
/// </summary>
/// <remarks>
/// Both halves are already strings because the unit is part of the answer. The
/// person reading it wants "4.37 GiB", not 4692251770, and the formatting
/// decision belongs where the refusal is made rather than in the window.
/// </remarks>
public sealed record Figure(string Label, string Value);

/// <summary>
/// What to do about a refusal, worded the way the button will read.
/// </summary>
public sealed record Remedy(string Label, RemedyKind Kind);

/// <summary>
/// The kinds of fix the app can actually offer. The window uses this to decide
/// what pressing the remedy does; the wording stays in <see cref="Remedy.Label"/>.
/// </summary>
public enum RemedyKind
{
    /// <summary>Aim at a bigger disc. "Use a double layer DVD, 8.5 GB".</summary>
    ChooseBiggerMedium,

    /// <summary>Let it span discs. "Make it a set of 3".</summary>
    SplitAcrossDiscs,

    /// <summary>Turn an option off. "Leave out Windows XP support".</summary>
    DropAnOption,

    /// <summary>
    /// Nothing the app can do from here; the person has to change something
    /// outside it. Still worth saying, so the refusal does not look like a bug.
    /// </summary>
    NotOurs
}
