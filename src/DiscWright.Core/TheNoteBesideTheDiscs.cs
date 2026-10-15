using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DiscWright.Core;

/// <summary>
/// One image of a set, as the note records it.
/// </summary>
/// <param name="Name">The image's file name, without the folder it sits in.</param>
/// <param name="Bytes">How big it is.</param>
public sealed record NotedImage(string Name, long Bytes);

/// <summary>
/// The note DiscWright leaves beside a set it is part way through, so that
/// coming back to it does not mean writing the discs that are already good
/// again.
/// </summary>
/// <remarks>
/// <para>
/// Written as something a person can read, because the thing it exists for is
/// somebody coming back tomorrow wanting to know where they stopped, and that
/// is worth answering whether or not this program is what they ask.
/// </para>
/// <para>
/// <strong>It is believed only while it still describes the discs beside it.</strong>
/// Build them again and the images change, the sizes stop matching and the note
/// is ignored rather than trusted. That is the safe way round: ignoring a good
/// note costs one disc that did not need writing, and trusting a stale one puts
/// somebody else's disc 1 in the set.
/// </para>
/// <para>
/// What it records is that a disc was written <em>and read back clean</em>,
/// which is true and is still not the same as that disc being in the room. So
/// it is offered to a person and never acted on by itself.
/// </para>
/// </remarks>
/// <param name="Label">The set's volume name, as the discs carry it.</param>
/// <param name="Images">Every image of the set, in order, disc 1 first.</param>
/// <param name="Written">How many came out right, counting from disc 1.</param>
public sealed record TheNoteBesideTheDiscs(string Label, IReadOnlyList<NotedImage> Images, int Written)
{
    /// <summary>What the note is called, wherever the discs were built.</summary>
    public const string FileName = "Written so far.txt";

    private const string Done = "done";

    private const string NotYet = "to do";

    /// <summary>How many discs the set has.</summary>
    public int OfDiscs => Images.Count;

    /// <summary>Which disc somebody coming back would start from.</summary>
    public int CarryOnFrom => Written + 1;

    /// <summary>
    /// Whether there is something to carry on from: some of the set written and
    /// some of it not.
    /// </summary>
    /// <remarks>
    /// A finished set is deliberately not part way through. Pressing burn on
    /// one again is how somebody makes a second copy, and being asked whether
    /// to carry on from disc 6 of 5 would be nonsense.
    /// </remarks>
    public bool PartWayThrough => Written > 0 && Written < OfDiscs;

    /// <summary>
    /// Whether this note is about the set that is there now.
    /// </summary>
    /// <remarks>
    /// The label and every image's name and size, which is as much as can be
    /// known without reading a few hundred megabytes back off the disk. Two
    /// different builds that agree on all of that agree on their contents too,
    /// near enough that the disc would be the same disc.
    /// </remarks>
    public bool Matches(string label, IReadOnlyList<NotedImage> now)
    {
        if (!string.Equals(Label, label, StringComparison.Ordinal) || now.Count != Images.Count)
        {
            return false;
        }

        for (int at = 0; at < Images.Count; at++)
        {
            if (Images[at].Bytes != now[at].Bytes
                || !string.Equals(Images[at].Name, now[at].Name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The note as it goes on disk.</summary>
    public string AsText()
    {
        StringBuilder text = new();

        text.Append("DiscWright: what is written so far").Append(Environment.NewLine);
        text.Append("==================================").Append(Environment.NewLine);
        text.Append(Environment.NewLine);

        text.Append("  ").Append(Label).Append(", a set of ").Append(OfDiscs)
            .Append(" discs.").Append(Environment.NewLine);
        text.Append(Environment.NewLine);

        for (int at = 0; at < Images.Count; at++)
        {
            text.Append("  disc ").Append(at + 1).Append(" of ").Append(OfDiscs).Append("  ")
                .Append((at < Written ? Done : NotYet).PadRight(5)).Append("  ")
                .Append(Images[at].Name).Append("  ")
                .Append(Images[at].Bytes.ToString(CultureInfo.InvariantCulture))
                .Append(Environment.NewLine);
        }

        text.Append(Environment.NewLine);
        text.Append("  DiscWright reads the lines above when you come back to finish this set, so").Append(Environment.NewLine);
        text.Append("  that it can carry on from the first disc that is not done instead of writing").Append(Environment.NewLine);
        text.Append("  the ones that are already good again. It always asks first.").Append(Environment.NewLine);
        text.Append(Environment.NewLine);
        text.Append("  Build these discs again and this note stops being true. The sizes will no").Append(Environment.NewLine);
        text.Append("  longer match the images beside it, and DiscWright starts from disc 1 rather").Append(Environment.NewLine);
        text.Append("  than believing it.").Append(Environment.NewLine);

        return text.ToString();
    }

    /// <summary>
    /// The note back out of its text, or nothing when it cannot be believed.
    /// </summary>
    /// <remarks>
    /// Lines it does not recognise are passed over rather than refused, because
    /// the prose around the list is there for a person and may well be reworded.
    /// What it will not do is guess: a list with a gap in it, or one that
    /// disagrees with itself about how many discs there are, is no note at all.
    /// </remarks>
    public static TheNoteBesideTheDiscs? Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string[] lines = text.Split('\n');
        string? label = null;
        List<(int Number, bool Done, NotedImage Image)> discs = [];
        int said = 0;

        foreach (string line in lines)
        {
            Match heading = TheLabelLine.Match(line);

            if (heading.Success)
            {
                label = heading.Groups[1].Value;
                said = int.Parse(heading.Groups[2].Value, CultureInfo.InvariantCulture);
                continue;
            }

            Match disc = ADiscLine.Match(line);

            if (!disc.Success)
            {
                continue;
            }

            discs.Add((
                int.Parse(disc.Groups[1].Value, CultureInfo.InvariantCulture),
                disc.Groups[3].Value == Done,
                new NotedImage(
                    disc.Groups[4].Value.Trim(),
                    long.Parse(disc.Groups[5].Value, CultureInfo.InvariantCulture))));

            if (int.Parse(disc.Groups[2].Value, CultureInfo.InvariantCulture) != said)
            {
                return null;
            }
        }

        if (label is null || said <= 0 || discs.Count != said)
        {
            return null;
        }

        // Every disc once, numbered one to however many, or it is not a list of
        // a set's discs and the order below would be a guess.
        for (int at = 0; at < discs.Count; at++)
        {
            if (discs[at].Number != at + 1)
            {
                return null;
            }
        }

        // Counted from the front and stopping at the first one that is not
        // done. A gap means the ones after it cannot be relied on, whatever
        // they say about themselves.
        int written = 0;

        while (written < discs.Count && discs[written].Done)
        {
            written++;
        }

        return new TheNoteBesideTheDiscs(label, [.. discs.Select(each => each.Image)], written);
    }

    private static readonly Regex TheLabelLine = new(
        @"^\s*(.+), a set of (\d+) discs\.\s*$", RegexOptions.Compiled);

    private static readonly Regex ADiscLine = new(
        @"^\s*disc\s+(\d+)\s+of\s+(\d+)\s+(done|to do)\s+(.+?)\s+(\d+)\s*$", RegexOptions.Compiled);
}
