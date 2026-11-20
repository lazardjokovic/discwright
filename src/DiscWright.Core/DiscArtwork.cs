namespace DiscWright.Core;

/// <summary>
/// The pictures a disc carries.
/// </summary>
/// <param name="IconPath">
/// What This PC shows beside the disc. Any picture; it is turned into a real
/// icon with every size Explorer asks for.
/// </param>
/// <param name="BackgroundPath">What the menu shows behind itself.</param>
/// <param name="BackgroundAsIs">
/// True to stretch the background to the menu's shape, false to fill and crop.
/// </param>
/// <param name="CoverPath">The printed sleeve.</param>
/// <param name="DiscFacePath">The printed label for the disc itself.</param>
/// <param name="BackgroundColour">
/// A colour the menu is drawn on instead of a picture, as
/// <see cref="SolidColour"/> keeps one, or nothing. Never set beside
/// <paramref name="BackgroundPath"/>: the screen clears one when the other
/// is chosen, and the menu writer lets the picture win if a file somebody
/// edited carries both.
/// </param>
/// <param name="Shape">
/// <para>
/// Fit or tile, the two ways to cover the menu that
/// <paramref name="BackgroundAsIs"/> cannot say, or nothing. Read
/// <see cref="HowItFits"/>, which settles the two.
/// </para>
/// <para>
/// Beside the yes or no rather than in its place, because the PowerShell app
/// and every 2.0 before this read the yes or no out of a project file and
/// know nothing else: a file that only said "Fit" would open there stretched
/// or not by accident.
/// </para>
/// </param>
/// <param name="MenuFontPath">
/// A font file of somebody's own for the menu to be written in, or nothing.
/// It goes onto the disc beside the menu, wrapped as WOFF, and the face
/// chosen from the list is what a machine that cannot load it falls back to.
/// </param>
/// <param name="Kept">
/// Which part of the background is kept when it is filled and cropped, or
/// nothing for the middle. It belongs to the picture: choosing another
/// picture takes it off, and no other way to fit reads it.
/// </param>
public sealed record DiscArtwork(
    string? IconPath = null,
    string? BackgroundPath = null,
    bool BackgroundAsIs = false,
    string? CoverPath = null,
    string? DiscFacePath = null,
    string? BackgroundColour = null,
    BackgroundFit? Shape = null,
    KeptPart? Kept = null,
    string? MenuFontPath = null)
{
    /// <summary>The part of the background that is kept, the middle when none was chosen.</summary>
    public KeptPart WhatIsKept => Kept ?? KeptPart.TheMiddle;

    /// <summary>
    /// This artwork keeping one part of its background, with the middle
    /// written as nothing so two discs that look alike are alike.
    /// </summary>
    public DiscArtwork Keeping(KeptPart? part)
    {
        KeptPart? tidy = part?.Tidied();

        return this with { Kept = tidy is null || tidy.IsTheMiddle ? null : tidy };
    }

    /// <summary>
    /// How the background covers the menu: the shape when one was chosen,
    /// and otherwise stretched or filled as the yes or no says.
    /// </summary>
    public BackgroundFit HowItFits => Shape ?? (BackgroundAsIs ? BackgroundFit.Stretch : BackgroundFit.Fill);

    /// <summary>
    /// This artwork made to fit one way, with the yes or no and the shape
    /// agreeing: the yes for Stretch alone, and the shape only for the two
    /// the yes or no cannot say.
    /// </summary>
    public DiscArtwork Fitting(BackgroundFit how) =>
        this with
        {
            BackgroundAsIs = how == BackgroundFit.Stretch,
            Shape = how is BackgroundFit.Fit or BackgroundFit.Tile ? how : null,
        };

    /// <summary>Whether there is anything to put on the disc at all.</summary>
    public bool IsAnything =>
        IconPath is not null || BackgroundPath is not null || CoverPath is not null || DiscFacePath is not null
        || BackgroundColour is not null || MenuFontPath is not null;

    /// <summary>Whether there is anything worth printing.</summary>
    public bool HasSomethingToPrint => CoverPath is not null || DiscFacePath is not null;
}
