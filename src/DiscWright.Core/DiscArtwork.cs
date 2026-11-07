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
public sealed record DiscArtwork(
    string? IconPath = null,
    string? BackgroundPath = null,
    bool BackgroundAsIs = false,
    string? CoverPath = null,
    string? DiscFacePath = null,
    string? BackgroundColour = null)
{
    /// <summary>Whether there is anything to put on the disc at all.</summary>
    public bool IsAnything =>
        IconPath is not null || BackgroundPath is not null || CoverPath is not null || DiscFacePath is not null
        || BackgroundColour is not null;

    /// <summary>Whether there is anything worth printing.</summary>
    public bool HasSomethingToPrint => CoverPath is not null || DiscFacePath is not null;
}
