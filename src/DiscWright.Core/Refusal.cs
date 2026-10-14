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
/// What the window can <em>do</em> about a refusal.
/// </summary>
/// <remarks>
/// <para>
/// These are deliberately the window's capabilities and not the reasons a
/// refusal happened. The first version of this enum listed reasons
/// (ChooseBiggerMedium, SplitAcrossDiscs, DropAnOption, NotOurs) and it was
/// already short of three: it could not express "rename the disc" for a label
/// AutoRun cannot write, "rename that folder" for a game called AUTORUN, or
/// "choose somewhere else" for an output folder with no room. Framed as reasons
/// it grows a case per refusal for ever. Framed as capabilities it is closed,
/// because it is a list of what one window can be asked to do.
/// </para>
/// <para>
/// The wording always stays in <see cref="Remedy.Label"/>. This only decides
/// what pressing it does.
/// </para>
/// <para>
/// <strong>A known overlap, left alone on purpose.</strong> <c>EnableSet</c> is
/// a special case of <c>ChangeOption</c>, because making it a set is turning an
/// option on. The two collapse into one the day a refusal has to say
/// <em>which</em> option it means, and that needs a field on
/// <see cref="Remedy"/> which does not exist yet. No refusal uses either of
/// them today, so adding that field now would be guessing at a shape before
/// anything needs it.
/// </para>
/// </remarks>
public enum RemedyKind
{
    /// <summary>Change the target disc. "Use a double layer DVD, 8.5 GB".</summary>
    ChangeMedium,

    /// <summary>Turn the disc set option on. "Make it a set of 3".</summary>
    EnableSet,

    /// <summary>Turn an option on or off. "Leave out Windows XP support", "Let it cut the file".</summary>
    ChangeOption,

    /// <summary>
    /// Put the cursor in the field that has to change, and select what is in
    /// it. "Rename the disc", "Rename that folder".
    /// </summary>
    FocusField,

    /// <summary>Reopen a folder chooser. "Choose another folder".</summary>
    PickFolder,

    /// <summary>
    /// There is nothing the window can do; the person has to change something
    /// outside it. Still worth saying, so the refusal does not read as a bug.
    /// </summary>
    Nothing
}
