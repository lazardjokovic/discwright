namespace DiscWright.Core;

/// <summary>
/// How far through a set the burning got.
/// </summary>
/// <remarks>
/// A set half burned is a thing somebody comes back to tomorrow needing to know
/// where they stopped, so every way out of the walk leaves a sentence here.
/// </remarks>
/// <param name="Written">How many discs came out right, in order from the first.</param>
/// <param name="OfDiscs">How many the set has.</param>
/// <param name="WhyItStopped">Why it went no further, or null when it finished.</param>
/// <param name="BroughtForward">
/// How many of those were already written before this run, carried on from a
/// note left beside the discs. Nought when the whole set was written just now.
/// </param>
public sealed record TheSetSoFar(int Written, int OfDiscs, string? WhyItStopped, int BroughtForward = 0)
{
    /// <summary>Whether every disc of the set is written and read back.</summary>
    public bool Whole => WhyItStopped is null && Written == OfDiscs && OfDiscs > 0;

    /// <summary>Which disc to start from if somebody comes back to it.</summary>
    public int CarryOnFrom => Written + 1;

    public string InWords
    {
        get
        {
            if (Whole && BroughtForward == 0)
            {
                return $"All {OfDiscs} discs are written and every file on them checked.";
            }

            // Which discs were done when matters. Saying every file was
            // checked, in one breath, about discs this run never saw would be
            // claiming more than it knows: they were checked when they were
            // written, and they are not in the drive now.
            if (Whole)
            {
                return $"All {OfDiscs} discs are written and checked. "
                    + $"{Of(BroughtForward + 1, OfDiscs, true)} {Was(BroughtForward + 1, OfDiscs)} written just now, "
                    + $"and {Of(1, BroughtForward, false)} earlier.";
            }

            string got = Written switch
            {
                0 => "Nothing was written.",
                1 => "Disc 1 is written and checked.",
                _ => $"Discs 1 to {Written} are written and checked."
            };

            return WhyItStopped is null
                ? got
                : $"{got} It stopped at disc {CarryOnFrom} of {OfDiscs}: {WhyItStopped}";
        }
    }

    /// <summary>One disc or a run of them, named the way a person would.</summary>
    private static string Of(int from, int to, bool starting) =>
        from == to
            ? (starting ? "Disc" : "disc") + $" {from}"
            : (starting ? "Discs" : "discs") + $" {from} to {to}";

    private static string Was(int from, int to) => from == to ? "was" : "were";
}
