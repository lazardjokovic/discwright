namespace DiscWright.Platform;

/// <summary>
/// The one small thing this program keeps about itself on a machine: which
/// look was chosen.
/// </summary>
/// <remarks>
/// <para>
/// A text file holding one word, because the fact is one word. The first
/// setting this program has ever kept, so the rules are set here: one file
/// per fact, under one DiscWright folder in local application data, and
/// losing the file loses nothing but a preference.
/// </para>
/// <para>
/// Nothing here throws. A machine where the folder cannot be written still
/// gets a working program that forgets its look, which is better than a
/// program that will not start over a preference.
/// </para>
/// </remarks>
public sealed class ANoteAboutTheLook(string folder)
{
    private const string FileName = "appearance.txt";

    /// <summary>Where the real program keeps it.</summary>
    public static ANoteAboutTheLook ForThisUser() =>
        new(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DiscWright"));

    /// <summary>What was kept, or nothing at all.</summary>
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

    /// <summary>Keeps one word, replacing the last one.</summary>
    public void Keep(string text)
    {
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, FileName), text);
        }
        catch (Exception cannotWrite) when (cannotWrite is IOException or UnauthorizedAccessException)
        {
            // A look that cannot be kept is still worn until the program
            // closes. Nothing else turns on it.
        }
    }
}
