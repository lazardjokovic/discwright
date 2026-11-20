using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// The count of blank discs, in one small file beside the other things this
/// program keeps about itself.
/// </summary>
/// <remarks>
/// A file of its own rather than a line in the note about the look. That
/// note is one word, read before any window exists, and anything added to it
/// would have to be read past by every version that wrote only the word.
/// </remarks>
public sealed class ANoteAboutTheBlanks(string folder) : IKeepTheBlanks
{
    private const string FileName = "blank-discs.txt";

    /// <summary>Where it is kept for whoever is running this.</summary>
    public static ANoteAboutTheBlanks ForThisUser() =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DiscWright"));

    public string? Recalled()
    {
        try
        {
            string where = Path.Combine(folder, FileName);

            return File.Exists(where) ? File.ReadAllText(where) : null;
        }
        catch (Exception cannotRead) when (cannotRead is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Keep(string text)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, FileName), text);
        }
        catch (Exception cannotWrite) when (cannotWrite is IOException or UnauthorizedAccessException)
        {
            // A count that cannot be kept is still used until the program
            // closes. Nothing else turns on it.
        }
    }
}
