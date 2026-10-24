using System.Globalization;
using System.Text.RegularExpressions;
using DiscWright.Core;

namespace DiscWright.Staging;

/// <summary>
/// What a built disc's image is called, and where it sits.
/// </summary>
/// <remarks>
/// <para>
/// Every disc of a set carries the same label, so a name made from the label
/// alone is the same name for all of them: the second disc's image lands on
/// top of the first, and the set is one file that says it is three.
/// </para>
/// <para>
/// Beside the disc folders rather than inside one, and named the way those
/// folders are: <c>disc</c> and <c>disc D1</c> there, <c>LABEL.iso</c> and
/// <c>LABEL D1.iso</c> here.
/// </para>
/// </remarks>
public static partial class TheImageOf
{
    public static string That(BuiltDisc disc)
    {
        string beside = System.IO.Path.GetDirectoryName(disc.Folder) ?? disc.Folder;
        string name = OnTheDisc.Tidied(disc.Label);

        return System.IO.Path.Combine(
            beside,
            disc.OfDiscs > 1 ? $"{name} D{disc.Number}.iso" : $"{name}.iso");
    }

    /// <summary>
    /// The images of a set with this label, out of whatever images are in a
    /// folder, in the order its discs are burned, or nothing when the folder
    /// holds no such set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a set that was built in another sitting. The set walk, with its
    /// question about carrying on from the disc the note says is next, only
    /// ever ran on a set built moments before, because the window knew which
    /// images belonged together only from the build. Opening a finished
    /// project and pressing Burn is the ordinary way to finish a set, and it
    /// got one image at a time from a list that did not say which were done.
    /// </para>
    /// <para>
    /// Named here, beside the naming it reads back, so the two cannot drift.
    /// A set is every disc from 1 upwards with no gap: "D1, D3" is two sets'
    /// leftovers or a disc gone missing, and neither is a set to walk.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> TheSetAmong(IEnumerable<string> images, string label)
    {
        string wanted = OnTheDisc.Tidied(label);
        SortedDictionary<int, string> found = [];

        foreach (string image in images)
        {
            Match match = ADiscOfASet().Match(System.IO.Path.GetFileNameWithoutExtension(image));

            if (match.Success
                && string.Equals(match.Groups["label"].Value, wanted, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && !found.ContainsKey(number))
            {
                found[number] = image;
            }
        }

        if (found.Count < 2 || found.Keys.Where((key, at) => key != at + 1).Any())
        {
            return [];
        }

        return [.. found.Values];
    }

    [GeneratedRegex(@"^(?<label>.+) D(?<number>[0-9]+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ADiscOfASet();
}
