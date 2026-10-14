using System.Globalization;
using System.Text;

namespace DiscWright.Core;

/// <summary>
/// The list a disc carries so that a copy taken off it years from now can be
/// proved identical to what went on.
/// </summary>
/// <remarks>
/// <para>
/// <strong>sha256sum format</strong>, so nothing from this program is needed to
/// read it: GNU coreutils checks it directly with <c>sha256sum -c</c>, and the
/// header lines are comments that format already skips. That is the ten year
/// promise in one file.
/// </para>
/// <para>
/// This builds the text and hashes nothing, so every rule about the format can
/// be tested without a disk.
/// </para>
/// </remarks>
public static class ChecksumList
{
    /// <summary>At the root of the disc, where anything looking for it will look.</summary>
    public const string FileName = "checksums.sha256";

    /// <summary>
    /// The whole list, as text.
    /// </summary>
    public static string For(string label, IReadOnlyList<ChecksumEntry> entries)
    {
        // Sorted, so two builds of the same disc produce the same list and a
        // difference between them means something.
        List<ChecksumEntry> inOrder =
            [.. entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal)];

        long bytes = inOrder.Sum(entry => entry.Bytes);

        StringBuilder text = new();

        text.Append("# DiscWright checksum list for ").Append(label).Append('\n');

        // The count is here to be CHECKED, not only read. A list that has lost
        // its tail verifies the files it still mentions and reports every one
        // of them right, which is the single way a checksum list can lie to
        // you. Anything reading this has to compare.
        text.Append("# ")
            .Append(inOrder.Count.ToString(CultureInfo.InvariantCulture))
            .Append(" files, ")
            .Append(bytes.ToString(CultureInfo.InvariantCulture))
            .Append(" bytes, SHA-256, sha256sum format.\n");

        text.Append("#\n");
        text.Append("# To check a copy, from the folder holding these files:\n");
        text.Append("#   sha256sum -c ").Append(FileName).Append('\n');
        text.Append("# or in PowerShell, with no extra tools:\n");
        text.Append("#   Get-Content ").Append(FileName).Append(" | Where-Object { $_ -notmatch '^#' } | ForEach-Object {\n");
        text.Append("#     $h, $f = $_ -split ' \\*', 2\n");
        text.Append("#     $a = (Get-FileHash $f -Algorithm SHA256).Hash\n");
        text.Append("#     \"{0}  {1}\" -f $(if ($a -eq $h) { \"OK  \" } else { \"FAIL\" }), $f }\n");
        text.Append("#\n");

        foreach (ChecksumEntry entry in inOrder)
        {
            // Forward slashes, because that is what sha256sum expects and
            // Windows accepts them in a path just as happily. The star is
            // sha256sum's mark for binary mode, which every one of these is.
            text.Append(entry.Sha256)
                .Append(" *")
                .Append(entry.RelativePath.Replace('\\', '/'))
                .Append(LineEnding);
        }

        return text.ToString();
    }

    /// <summary>
    /// Reads a list back.
    /// </summary>
    /// <remarks>
    /// Comments are skipped, which is the same thing sha256sum does with them,
    /// and a line that is not a hash and a path is skipped rather than thrown
    /// over: a list with one odd line in it is still worth every line that is
    /// good, and a disc is not worth refusing over a stray blank.
    /// </remarks>
    public static IReadOnlyList<ReadEntry> Read(string text)
    {
        List<ReadEntry> found = [];

        foreach (string line in text.Split(LineEnding))
        {
            string trimmed = line.TrimEnd('\r');

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            int star = trimmed.IndexOf(" *", StringComparison.Ordinal);

            if (star <= 0)
            {
                continue;
            }

            found.Add(new ReadEntry(trimmed[..star], trimmed[(star + 2)..]));
        }

        return found;
    }

    /// <summary>
    /// Newlines, never carriage returns, which is the opposite of every other
    /// file this program writes.
    /// </summary>
    /// <remarks>
    /// <c>sha256sum -c</c> takes a carriage return as part of the filename and
    /// reports every single line as a missing file. Found with the real tool,
    /// which is the only way it could have been found. Notepad has handled a
    /// bare newline since 2018 and nothing else ever minded.
    /// </remarks>
    public const char LineEnding = '\n';
}

/// <summary>One file, and what it should hash to.</summary>
/// <param name="RelativePath">Where it sits on the disc.</param>
/// <param name="Sha256">Lowercase hexadecimal, as sha256sum writes it.</param>
/// <param name="Bytes">How big it is, for the total in the header.</param>
public sealed record ChecksumEntry(string RelativePath, string Sha256, long Bytes);

/// <summary>
/// One line read back out of a list: a hash and the file it belongs to.
/// </summary>
/// <remarks>
/// No size, because the format does not carry one. Whoever needs it has the
/// file and can ask it.
/// </remarks>
public sealed record ReadEntry(string Sha256, string RelativePath);
