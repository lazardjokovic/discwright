using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// A font file read off this machine and written as the menu's.
/// </summary>
/// <remarks>
/// Deliberately dull: it reads a file and writes one. Everything about what
/// a font is and how it is wrapped is <see cref="AFontForTheMenu"/>'s.
/// </remarks>
public sealed class WindowsFonts : IFonts
{
    public string? WhyNot(string path)
    {
        try
        {
            return AFontForTheMenu.WhyNot(File.ReadAllBytes(path));
        }
        catch (Exception cannotRead) when (cannotRead is IOException or UnauthorizedAccessException)
        {
            return "It cannot be read: " + cannotRead.Message;
        }
    }

    public void WriteForTheMenu(string source, string destination)
    {
        if (AFontForTheMenu.AsWoff(File.ReadAllBytes(source)) is byte[] woff)
        {
            File.WriteAllBytes(destination, woff);
        }
    }
}
