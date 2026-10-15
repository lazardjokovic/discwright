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
public sealed record TheSetSoFar(int Written, int OfDiscs, string? WhyItStopped)
{
    /// <summary>Whether every disc of the set is written and read back.</summary>
    public bool Whole => WhyItStopped is null && Written == OfDiscs && OfDiscs > 0;

    /// <summary>Which disc to start from if somebody comes back to it.</summary>
    public int CarryOnFrom => Written + 1;

    public string InWords
    {
        get
        {
            if (Whole)
            {
                return $"All {OfDiscs} discs are written and every file on them checked.";
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
}
