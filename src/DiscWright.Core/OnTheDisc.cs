namespace DiscWright.Core;

/// <summary>
/// Where things land on a disc, worked out in one place.
/// </summary>
/// <remarks>
/// It has one home because two parts of the program need the same answer and
/// have to agree exactly: the builder, which copies a game's files into a
/// folder, and the menu, which has to name that folder in order to run what is
/// in it. Worked out twice, they agree until the day somebody changes one of
/// them, and then the menu's only button points at a folder that is not there.
/// </remarks>
public static class OnTheDisc
{
    /// <summary>
    /// The folder one game's files go in, or empty when the game has the disc
    /// to itself.
    /// </summary>
    /// <remarks>
    /// One game keeps its own shape at the root. Several go into numbered
    /// folders, because two games are perfectly entitled to both contain a
    /// <c>Data</c> folder and the second must not land on top of the first.
    /// Numbered rather than just named, so the order somebody added them in is
    /// the order they appear in a file listing.
    /// </remarks>
    /// <param name="itsFiles">
    /// The game's files, as they would sit on the disc. Needed because a lone
    /// game only earns the root if nothing it carries would be written over
    /// there.
    /// </param>
    public static string FolderFor(int number, string name, int howManyGames, IEnumerable<string> itsFiles)
    {
        if (howManyGames > 1)
        {
            return $"{number:00} - {Tidied(name)}";
        }

        // One game is the disc, so its files sit at the root of it, which is
        // what somebody expects when they put the disc in.
        //
        // Unless it carries a name the disc writes itself. A game that brings
        // its own autorun.inf, or its own AUTORUN folder, would have that file
        // quietly replaced by ours and nobody would be told: the disc would
        // build, burn, and be missing something somebody chose to put on it.
        // It gets a folder of its own instead, and keeps everything.
        return ReservedDiscNames.WouldBeWrittenOver(itsFiles) ? Tidied(name) : string.Empty;
    }

    /// <summary>A name that is safe to use as a folder on a disc.</summary>
    /// <remarks>
    /// Characters Windows will not have in a file name become spaces rather
    /// than disappearing, so "Command &amp; Conquer: Red Alert" keeps its shape
    /// as "Command &amp; Conquer  Red Alert" instead of running two words
    /// together. A name that is nothing but those characters falls back to a
    /// word rather than to an empty folder name.
    /// </remarks>
    public static string Tidied(string name)
    {
        char[] bad = System.IO.Path.GetInvalidFileNameChars();

        string tidied = new([.. name.Select(character => bad.Contains(character) ? ' ' : character)]);

        // Trailing dots and spaces are legal to write and impossible to open
        // again on Windows, which is a worse failure than a refusal: the disc
        // builds, burns, and holds a folder nobody can get into.
        tidied = tidied.Trim().TrimEnd('.').Trim();

        return tidied.Length == 0 ? "game" : tidied;
    }
}
