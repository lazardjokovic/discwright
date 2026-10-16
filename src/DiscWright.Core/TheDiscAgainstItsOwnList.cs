namespace DiscWright.Core;

/// <summary>
/// Reads a burned disc and checks every file on it against the checksum list
/// the disc itself carries.
/// </summary>
/// <remarks>
/// <para>
/// The other checker compares a disc with the folder it was built from, which
/// is the better answer when that folder is there: it also notices files on
/// the disc that nobody put there. It is only available while the build that
/// made the folder is still in hand.
/// </para>
/// <para>
/// <strong>Which it was not.</strong> Opening a finished project and pressing
/// Burn left nothing to check against, so a disc that read back perfectly was
/// reported as written but not checked. A disc from another machine, or from a
/// build whose staging folder was tidied away months ago, had the same problem
/// and always would have.
/// </para>
/// <para>
/// Every disc carries <c>checksums.sha256</c> at its root for exactly this, so
/// a disc can always answer for itself.
/// </para>
/// </remarks>
public static class TheDiscAgainstItsOwnList
{
    public static Task<DiscCheck> Check(
        string discRoot,
        IFileSystem files,
        IHasher hasher,
        IProgress<CheckProgress>? progress,
        CancellationToken ct) =>
        Task.Run(() => Reading(discRoot, files, hasher, progress, ct), ct);

    /// <summary>Whether a disc has a list to be checked against.</summary>
    public static bool IsOn(string discRoot, IFileSystem files) =>
        files.ReadText(System.IO.Path.Combine(discRoot, ChecksumList.FileName)) is not null;

    private static DiscCheck Reading(
        string discRoot,
        IFileSystem files,
        IHasher hasher,
        IProgress<CheckProgress>? progress,
        CancellationToken ct)
    {
        if (!files.FolderExists(discRoot))
        {
            return new DiscCheck(0, [discRoot], [], [], [], "The disc could not be read at all.");
        }

        string? text = files.ReadText(System.IO.Path.Combine(discRoot, ChecksumList.FileName));

        if (text is null)
        {
            return new DiscCheck(
                0, [], [], [], [],
                $"There is no {ChecksumList.FileName} on the disc to check it against.");
        }

        IReadOnlyList<ReadEntry> wanted = ChecksumList.Read(text);

        // The count in the header is there to be CHECKED. A list that has lost
        // its tail verifies every file it still mentions and reports them all
        // right, which is the one way a checksum list can lie.
        if (HowManyItSaysItHas(text) is int said && said != wanted.Count)
        {
            return new DiscCheck(
                0, [], [], [], [],
                $"The checksum list on the disc says it has {said} files and lists {wanted.Count}, "
                    + "so the list itself did not come off the disc whole.");
        }

        if (wanted.Count == 0)
        {
            return new DiscCheck(
                0, [], [], [], [],
                $"The {ChecksumList.FileName} on the disc lists nothing.");
        }

        Dictionary<string, ListedFile> onTheDisc = new(StringComparer.OrdinalIgnoreCase);

        foreach (ListedFile file in files.List(discRoot).Files)
        {
            onTheDisc[Tidied(file.RelativePath)] = file;
        }

        List<string> missing = [];
        List<string> wrong = [];
        List<string> unreadable = [];
        int done = 0;
        int matched = 0;

        foreach (ReadEntry entry in wanted)
        {
            ct.ThrowIfCancellationRequested();

            string where = Tidied(entry.RelativePath);
            progress?.Report(new CheckProgress(++done, wanted.Count, entry.RelativePath));

            if (!onTheDisc.TryGetValue(where, out ListedFile? found))
            {
                missing.Add(entry.RelativePath);
                continue;
            }

            try
            {
                string got = hasher.Sha256(System.IO.Path.Combine(discRoot, found.RelativePath), ct);

                if (string.Equals(got, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    matched++;
                }
                else
                {
                    wrong.Add(entry.RelativePath);
                }
            }
            catch (Exception bad) when (bad is System.IO.IOException or UnauthorizedAccessException)
            {
                // The one failure this exists to catch: the file is on the
                // disc and the drive will not read it back.
                unreadable.Add(entry.RelativePath);
            }
        }

        // Two files DiscWright writes are not in the list it writes: the list
        // cannot contain its own hash, and the set note is written after it.
        // Counting them as strangers made a disc report that something was
        // already on it, which reads as a disc that was not blank. Found by
        // running this against a real disc, where the fakes all said nothing.
        List<string> unexpected =
        [
            .. onTheDisc.Keys
                .Except(wanted.Select(entry => Tidied(entry.RelativePath)), StringComparer.OrdinalIgnoreCase)
                .Where(path => !Ours(path))
        ];

        return new DiscCheck(matched, unreadable, missing, unexpected, wrong, null);
    }

    /// <summary>The file count out of the list's own header, if it says one.</summary>
    private static int? HowManyItSaysItHas(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');

            if (!trimmed.StartsWith("# ", StringComparison.Ordinal))
            {
                continue;
            }

            string[] words = trimmed[2..].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words is [string count, "files,", ..]
                && int.TryParse(count, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int said))
            {
                return said;
            }
        }

        return null;
    }

    /// <summary>Whether DiscWright put it there itself, outside the list.</summary>
    private static bool Ours(string path) =>
        string.Equals(path, ChecksumList.FileName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(path, SetManifest.FileName, StringComparison.OrdinalIgnoreCase);

    private static string Tidied(string relativePath) =>
        relativePath.Replace('/', (char)92).TrimStart((char)92);
}
