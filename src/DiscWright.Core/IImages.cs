namespace DiscWright.Core;

/// <summary>
/// Reading pictures, and writing the two the disc needs.
/// </summary>
/// <remarks>
/// A port, because decoding an image is the operating system's job and because
/// which formats work is a property of the machine rather than of this program.
/// </remarks>
public interface IImages
{
    /// <summary>
    /// How big a picture is, and whether it can be read at all.
    /// </summary>
    /// <remarks>
    /// Asked the moment a file is picked rather than at build time. The icon
    /// was validated when it was chosen and the background never was, so a
    /// corrupt or unsupported image sailed through the whole window and failed
    /// at the end as "A generic error occurred in GDI+", which tells somebody
    /// nothing about which file is at fault.
    /// </remarks>
    ImageFacts Read(string path);

    /// <summary>
    /// Writes a Windows icon file, with every size Explorer asks for.
    /// </summary>
    /// <remarks>
    /// A real <c>.ico</c> with several frames rather than one picture renamed.
    /// Explorer asks for 16 pixels in a list and 256 on the desktop, and given
    /// one size it scales the others itself, badly.
    /// </remarks>
    void WriteIcon(string source, string destination);

    /// <summary>
    /// Writes the picture the menu shows behind itself, at the menu's size.
    /// </summary>
    /// <param name="how">
    /// How it is made to cover a frame that is almost never its own shape:
    /// see <see cref="BackgroundFit"/>.
    /// </param>
    void WriteBackground(string source, string destination, int width, int height, BackgroundFit how);
}

/// <summary>What could be learned about a picture without opening the window.</summary>
/// <param name="Readable">False when Windows cannot decode it.</param>
/// <param name="Why">
/// Why not, in words somebody can act on. Deliberately not GDI+'s own text,
/// which reports "Out of memory" for any file it cannot decode and sends people
/// hunting a memory problem they do not have.
/// </param>
public sealed record ImageFacts(bool Readable, int Width, int Height, string? Why)
{
    /// <summary>An icon made from a picture that is not square gets cropped.</summary>
    public bool IsSquare => Width == Height;
}
