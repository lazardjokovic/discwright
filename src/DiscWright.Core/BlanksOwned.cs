using System.Globalization;

namespace DiscWright.Core;

/// <summary>
/// How many blank discs of each size somebody has.
/// </summary>
/// <remarks>
/// <para>
/// discwright#98, the first thing asked for in it: "you tell the application
/// how many empty cd-r, dvd, dvd-9, bluray, bluray dual or bluray 100gb discs
/// you have in total, so that a match relating to what you have is given
/// which wastes less space. If a game is 800MB, for the app to suggest 2 x
/// CD if you have them available instead of 1 x DVD."
/// </para>
/// <para>
/// About the person and the shelf beside them, not about a disc, so it is
/// kept with the program and never in a project file: a project opened on
/// another machine must not bring somebody else's shelf with it.
/// </para>
/// <para>
/// Never changed in place. Each answer is a new one, so what the window
/// shows and what is written down cannot drift apart half way through.
/// </para>
/// </remarks>
public sealed class BlanksOwned
{
    /// <summary>The most of one size anybody is taken to have. A spindle is 100.</summary>
    public const int TheMostOfOneSize = 9999;

    private readonly Dictionary<string, int> _counts;

    private BlanksOwned(Dictionary<string, int> counts) => _counts = counts;

    /// <summary>Nothing said, which is what everybody starts with.</summary>
    public static BlanksOwned None { get; } = new(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Whether any size has any at all. With none, nothing is chosen by them.</summary>
    public bool IsAnything => _counts.Values.Any(count => count > 0);

    /// <summary>How many of this size.</summary>
    public int Of(Medium medium) => _counts.GetValueOrDefault(medium.Key);

    /// <summary>These, with this many of one size, kept inside what a count can be.</summary>
    public BlanksOwned With(Medium medium, int count)
    {
        Dictionary<string, int> now = new(_counts, StringComparer.OrdinalIgnoreCase);
        // None is kept as a nought. Everything that reads these asks for
        // more than none, so a nought and never having said are the same.
        now[medium.Key] = Math.Clamp(count, 0, TheMostOfOneSize);

        return new BlanksOwned(now);
    }

    /// <summary>
    /// These after so many of one size have been written to. Never below
    /// none: a disc burned from a box nobody counted is not minus one.
    /// </summary>
    public BlanksOwned Spending(Medium medium, int howMany = 1) =>
        With(medium, Math.Max(0, Of(medium) - Math.Max(0, howMany)));

    /// <summary>
    /// As one line of text, the sizes in the order the window lists them:
    /// <c>CD=10;DVD5=25</c>. Nothing at all for none.
    /// </summary>
    public string AsText() =>
        string.Join(
            ';',
            Media.All
                .Where(medium => Of(medium) > 0)
                .Select(medium => string.Create(CultureInfo.InvariantCulture, $"{medium.Key}={Of(medium)}")));

    /// <summary>
    /// What a line of text says, taking only what it can make sense of. A
    /// file somebody edited, or one from a version with a size this one
    /// does not know, costs the parts that are wrong and nothing else.
    /// </summary>
    public static BlanksOwned FromText(string? text)
    {
        BlanksOwned owned = None;

        foreach (string part in (text ?? string.Empty).Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string[] halves = part.Split('=', 2);

            if (halves.Length == 2
                && Media.ByKey(halves[0].Trim()) is Medium medium
                && int.TryParse(halves[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            {
                owned = owned.With(medium, count);
            }
        }

        return owned;
    }
}

/// <summary>
/// Somewhere the count of blanks is kept between one sitting and the next.
/// </summary>
public interface IKeepTheBlanks
{
    /// <summary>What was kept, or nothing when nothing was or it cannot be read.</summary>
    string? Recalled();

    /// <summary>Keeps it. Failing to is not worth stopping anything for.</summary>
    void Keep(string text);
}
