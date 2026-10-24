using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DiscWright.Core;

/// <summary>
/// The file every disc of a set carries, describing the whole set.
/// </summary>
/// <remarks>
/// <para>
/// The same file on every disc, so <strong>any one disc can answer every
/// question</strong>: what this set is, what is on this disc, what is still
/// missing, and whether the folder somebody copied the discs into is complete
/// and undamaged.
/// </para>
/// <para>
/// Human first, because somebody will open it in Notepad on a machine that has
/// never heard of this program. Machine-readable second, in the shape
/// <c>checksums.sha256</c> already uses, so the menu reads it with parsing it
/// already has.
/// </para>
/// <para>
/// <strong>Three parsers read this file</strong>: the snippet inside it that
/// can be pasted into PowerShell, the menu on the disc, and a person. All three
/// take a table row to be a line that <em>starts with a digit and holds a
/// star</em>. Every other line is therefore indented, which is what keeps the
/// prose out of the parsers.
/// </para>
/// </remarks>
public static class SetManifest
{
    /// <summary>At the root of every disc in the set.</summary>
    public const string FileName = "Disc set.txt";

    /// <summary>
    /// A short name for this particular burn, the same on every disc of it and
    /// different for any other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Twelve hexadecimal characters. Not a security measure, a label: enough
    /// that two burns of the same game in the same week cannot be confused for
    /// one another.
    /// </para>
    /// <para>
    /// It answers the one thing a failed join cannot. A join that produces the
    /// wrong hash says the finished file is wrong; it does not say that disc 2
    /// came from a later download than disc 1. Two discs that disagree about
    /// this were built from different sources, and the menu can say so when the
    /// second disc goes in rather than after four minutes of copying.
    /// </para>
    /// </remarks>
    public static string FingerprintOf(IEnumerable<SetEntry> entries)
    {
        string rows = string.Join(
            "\n",
            entries
                .OrderBy(entry => entry.Disc)
                .ThenBy(entry => entry.RelativePath, StringComparer.Ordinal)
                .Select(entry => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{entry.Disc}|{entry.RelativePath.Replace('\\', '/')}|{entry.Bytes}|{entry.Sha256}")));

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rows)))[..12];
    }

    /// <summary>The whole file, as text, for one disc of the set.</summary>
    public static string For(
        string label,
        IReadOnlyList<SetEntry> entries,
        int thisDisc,
        int discsInSet,
        string mediaName,
        DateTimeOffset written,
        IReadOnlyList<SetJoin>? joins = null)
    {
        joins ??= [];
        long total = entries.Sum(entry => entry.Bytes);
        int mine = entries.Count(entry => entry.Disc == thisDisc);

        StringBuilder text = new();

        void Line(string line = "") => text.Append(line).Append('\n');

        Line("DiscWright disc set");
        Line("===================");
        Line();
        Line("  " + label);
        Line(string.Create(CultureInfo.InvariantCulture,
            $"  disc {thisDisc} of {discsInSet}.  {entries.Count} files in the set, {ByteSize.Describe(total)} in all."));
        Line();

        // What made this disc, and when. A disc that cannot say what wrote it
        // is a disc nobody can reason about when it turns out to have been
        // written by a version with a bug in it.
        Line("  Set id      " + FingerprintOf(entries));
        Line("              the same on every disc of this burn, and on no other");
        Line("  Written     " + written.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " by DiscWright");

        if (mediaName.Length > 0)
        {
            Line("  Planned for " + mediaName);
        }

        Line();
        Line("Every disc in this set carries this same file, describing every file on");
        Line("every disc. Any one disc can tell you what the whole set should be.");
        Line();
        Line("This disc cannot install the game on its own, and neither can any other");
        Line(string.Create(CultureInfo.InvariantCulture, $"disc in the set. The parts are spread across {discsInSet} discs."));
        Line();
        Line("To put the game back:");
        Line();
        Line("  1. Make one empty folder on a hard drive.");
        Line("  2. Copy the contents of every disc in the set into that one folder.");
        Line("     The order does not matter.");

        if (joins.Count > 0)
        {
            Line("  3. Put the files that were cut to fit back together. How is below.");
            Line("  4. Run the installer from that folder, never from a disc.");
        }
        else
        {
            Line("  3. Run the installer from that folder, never from a disc.");
        }

        Line();
        Line("The menu on any disc in the set does all of that for you: press Install on");
        Line("any disc, put the others in when it asks, and it installs from the folder.");
        Line("This file is here so it can be done by hand as well.");
        Line();
        Line(string.Create(CultureInfo.InvariantCulture, $"On this disc: {mine} of the {entries.Count} files."));
        Line();
        Line("What each disc holds");
        Line("--------------------");
        Line();
        Line("  disc   files  bytes");

        for (int disc = 1; disc <= discsInSet; disc++)
        {
            SetEntry[] on = [.. entries.Where(entry => entry.Disc == disc)];
            string here = disc == thisDisc ? "   <- the disc you are holding" : string.Empty;

            Line(string.Create(
                CultureInfo.InvariantCulture,
                $"  {disc,-6} {on.Length,-6} {on.Sum(entry => entry.Bytes),-14}{here}"));
        }

        Line(string.Create(CultureInfo.InvariantCulture, $"  {"all",-6} {entries.Count,-6} {total,-14}"));
        Line();
        Line("Every file in the set");
        Line("---------------------");
        Line();
        Line("disc  sha256                                                            bytes         file");

        foreach (SetEntry entry in entries.OrderBy(entry => entry.Disc).ThenBy(entry => entry.RelativePath, StringComparer.Ordinal))
        {
            // The only lines in the file that start with a digit, which is how
            // all three readers of it tell a row from a sentence.
            Line(string.Create(
                CultureInfo.InvariantCulture,
                $"{entry.Disc,-5} {entry.Sha256} {entry.Bytes,-13} *{entry.RelativePath.Replace('\\', '/')}"));
        }

        Line();

        if (joins.Count > 0)
        {
            TheFilesThatWereCut(Line, joins);
        }

        Line("Each line is the disc number, the SHA-256, the size in bytes, then the");
        Line("file. To check a folder you have copied the discs into, run this from");
        Line("inside that folder:");
        Line();
        Line("  Get-Content '" + FileName + "' | Where-Object { $_ -match '^[0-9]' } | ForEach-Object {");
        Line("    $d, $h, $b, $f = $_ -split ' +', 4");
        Line("    $f = $f.Substring(1)");
        Line("    if (-not (Test-Path $f)) { \"MISSING (disc $d)  $f\" }");
        Line("    elseif ((Get-Item $f).Length -ne [long]$b) { \"WRONG SIZE (disc $d)  $f\" }");
        Line("    elseif ((Get-FileHash $f -Algorithm SHA256).Hash -ne $h) { \"DAMAGED (disc $d)  $f\" }");
        Line("  }");
        Line();
        Line("Silence means every file is there and every one of them is right.");

        return text.ToString();
    }

    /// <summary>
    /// The files that were cut to fit, the pieces each goes back together
    /// from, and how to do that by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three readers again. The menu reads the JOIN and PART lines, which
    /// start with a letter and so are walked past by the row parsers; a
    /// person reads the copy /b and cat lines; and the finished file's own
    /// hash is here because the pieces being right does not prove the join
    /// was: they can go back together in the wrong order, or one of them
    /// twice.
    /// </para>
    /// <para>
    /// The copy /b line gets Windows paths with every name quoted. Pasted at
    /// a prompt with forward slashes it was "The syntax of the command is
    /// incorrect", because cmd reads a slash after the first argument as a
    /// switch, and unquoted a name with a space in it is two files that are
    /// not there. A GOG installer has no space in its name, which is how that
    /// went out on two real sets in v1 before it was caught.
    /// </para>
    /// </remarks>
    private static void TheFilesThatWereCut(Action<string> line, IReadOnlyList<SetJoin> joins)
    {
        const char backslash = (char)92;
        const char quote = (char)34;

        line("Files that were cut to fit");
        line("--------------------------");
        line("");
        line("These are on the discs in pieces, because no disc in this set is big");
        line("enough to hold one of them whole. The menu puts them back together after");
        line("the last disc is copied, and checks the result against the hash below.");
        line("");
        line("To do it by hand, from inside the folder you copied the discs into.");
        line("On Windows:");
        line("");

        foreach (SetJoin join in joins)
        {
            line("  copy /b "
                + string.Join("+", join.Pieces.Select(piece => quote + piece.Replace('/', backslash) + quote))
                + " " + quote + join.RelativePath.Replace('/', backslash) + quote);
        }

        line("");
        line("On Linux or macOS:");
        line("");

        foreach (SetJoin join in joins)
        {
            line("  cat "
                + string.Join(" ", join.Pieces.Select(piece => "'" + piece.Replace(backslash, '/') + "'"))
                + " > '" + join.RelativePath.Replace(backslash, '/') + "'");
        }

        line("");
        line("and then delete the pieces. Each JOIN line below is the finished file:");
        line("its SHA-256, its size in bytes, and its name. Each PART line under it is");
        line("one piece, in the order they go back together.");
        line("");

        foreach (SetJoin join in joins)
        {
            line(string.Create(CultureInfo.InvariantCulture,
                $"JOIN {join.Sha256} {join.Bytes,-13} *{join.RelativePath.Replace(backslash, '/')}"));

            for (int at = 0; at < join.Pieces.Count; at++)
            {
                line(string.Create(CultureInfo.InvariantCulture, $"PART {at + 1,-3} *{join.Pieces[at].Replace(backslash, '/')}"));
            }
        }

        line("");
        line("Check the finished file afterwards. It should come back with exactly the");
        line("SHA-256 printed above:");
        line("");

        foreach (SetJoin join in joins)
        {
            line("  (Get-FileHash '" + join.RelativePath.Replace('/', backslash) + "' -Algorithm SHA256).Hash");
            line("  sha256sum '" + join.RelativePath.Replace(backslash, '/') + "'");
        }

        line("");
    }
}

/// <summary>One file that was cut to fit, and the pieces it goes back together from.</summary>
/// <param name="RelativePath">Where the finished file sits, relative to the root of the folder.</param>
/// <param name="Sha256">What the finished file hashes to, lowercase hexadecimal.</param>
/// <param name="Bytes">How big the finished file is.</param>
/// <param name="Pieces">The pieces, in the order they go back together, as paths relative to the root.</param>
public sealed record SetJoin(string RelativePath, string Sha256, long Bytes, IReadOnlyList<string> Pieces);

/// <summary>One file somewhere in a set.</summary>
/// <param name="Disc">Which disc it is on, from one.</param>
/// <param name="RelativePath">Where it sits on that disc.</param>
/// <param name="Sha256">Lowercase hexadecimal, as sha256sum writes it.</param>
/// <param name="Bytes">How big it is.</param>
public sealed record SetEntry(int Disc, string RelativePath, string Sha256, long Bytes);
