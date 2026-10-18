namespace DiscWright.Core;

/// <summary>
/// Reads a burned disc and compares every file on it with the folder it was
/// built from.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Written is not the same as readable.</strong> A write that reports
/// success can still produce a disc with a file that will not come back off it,
/// and the only way to know is to read the disc, not a copy of it and not the
/// image it came from.
/// </para>
/// <para>
/// Hashed rather than compared byte by byte, because the hash is the same
/// answer the disc's own checksum list gives and somebody can repeat it by hand
/// with sha256sum.
/// </para>
/// </remarks>
public static class TheDiscAgainstWhatWasBuilt
{
    public static Task<DiscCheck> Check(
        string discRoot,
        string staged,
        IFileSystem files,
        IHasher hasher,
        IProgress<CheckProgress>? progress,
        CancellationToken ct)
    {
        return Task.Run(() => Reading(discRoot, staged, files, hasher, progress, ct), ct);
    }

    private static DiscCheck Reading(
        string discRoot,
        string staged,
        IFileSystem files,
        IHasher hasher,
        IProgress<CheckProgress>? progress,
        CancellationToken ct)
    {
        if (!files.FolderExists(discRoot))
        {
            return new DiscCheck(0, [discRoot], [], [], [], "The disc could not be read at all.");
        }

        FolderListing shouldBe = files.List(staged);
        FolderListing isThere = files.List(discRoot);

        Dictionary<string, ListedFile> onTheDisc = new(StringComparer.OrdinalIgnoreCase);

        foreach (ListedFile file in isThere.Files)
        {
            onTheDisc[Tidied(file.RelativePath)] = file;
        }

        List<string> missing = [];
        List<string> wrongSize = [];
        List<string> wrongContents = [];
        List<string> unreadable = [];

        int done = 0;
        int matched = 0;

        foreach (ListedFile wanted in shouldBe.Files)
        {
            ct.ThrowIfCancellationRequested();

            string where = Tidied(wanted.RelativePath);
            progress?.Report(new CheckProgress(++done, shouldBe.Files.Count, wanted.RelativePath));

            if (!onTheDisc.TryGetValue(where, out ListedFile? found))
            {
                missing.Add(wanted.RelativePath);
                continue;
            }

            // Checked before hashing, because a wrong size is the same answer
            // for none of the reading time.
            if (found.Bytes != wanted.Bytes)
            {
                wrongSize.Add(wanted.RelativePath);
                continue;
            }

            try
            {
                string onDisc = hasher.Sha256(System.IO.Path.Combine(discRoot, found.RelativePath), ct);
                string built = hasher.Sha256(System.IO.Path.Combine(staged, wanted.RelativePath), ct);

                if (string.Equals(onDisc, built, StringComparison.OrdinalIgnoreCase))
                {
                    matched++;
                }
                else
                {
                    wrongContents.Add(wanted.RelativePath);
                }
            }
            catch (Exception wrong) when (wrong is System.IO.IOException or UnauthorizedAccessException)
            {
                // The one failure this exists to catch: the file is on the disc,
                // it is the right size, and the drive cannot read it back.
                unreadable.Add(wanted.RelativePath);
            }
        }

        // Anything on the disc that nobody put there. Not a failure on its own,
        // but worth saying: it means the disc was not blank, or something else
        // wrote to it.
        List<string> unexpected =
        [
            .. onTheDisc.Keys
                .Except(shouldBe.Files.Select(file => Tidied(file.RelativePath)), StringComparer.OrdinalIgnoreCase)
        ];

        return new DiscCheck(matched, unreadable, missing, unexpected, wrongSize.Concat(wrongContents).ToList(), null);
    }

    /// <summary>
    /// A path written the one way, since a disc gives them back with whichever
    /// separator its file system uses.
    /// </summary>
    private static string Tidied(string relativePath) =>
        relativePath.Replace('/', (char)92).TrimStart((char)92);
}

/// <summary>What reading the disc back found.</summary>
/// <param name="Matched">How many files came back exactly as built.</param>
/// <param name="Unreadable">On the disc, the right size, and will not read.</param>
/// <param name="Missing">Built, and not on the disc at all.</param>
/// <param name="Unexpected">On the disc, and nobody put them there.</param>
/// <param name="Wrong">On the disc, and not what was built.</param>
/// <param name="WhyNot">Why nothing could be checked, when nothing could.</param>
public sealed record DiscCheck(
    int Matched,
    IReadOnlyList<string> Unreadable,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Unexpected,
    IReadOnlyList<string> Wrong,
    string? WhyNot)
{
    /// <summary>
    /// Whether the disc is the disc.
    /// </summary>
    /// <remarks>
    /// Something extra on it does not make it wrong. Everything that was meant
    /// to be there being there, readable, and the same, does make it right.
    /// </remarks>
    public bool Good =>
        WhyNot is null && Unreadable.Count == 0 && Missing.Count == 0 && Wrong.Count == 0 && Matched > 0;

    /// <summary>What to say about it, in one line.</summary>
    public string InWords
    {
        get
        {
            if (WhyNot is not null)
            {
                return WhyNot;
            }

            if (Good)
            {
                return Unexpected.Count == 0
                    ? $"All {Matched} files on the disc are exactly what was built."
                    : $"All {Matched} files are exactly what was built. {Unexpected.Count} more were already on the disc.";
            }

            List<string> wrong = [];

            if (Unreadable.Count > 0)
            {
                wrong.Add($"{Files(Unreadable.Count)} could not be read off it");
            }

            if (Missing.Count > 0)
            {
                wrong.Add($"{Files(Missing.Count)} did not reach it");
            }

            if (Wrong.Count > 0)
            {
                wrong.Add($"{Files(Wrong.Count)} came back different");
            }

            // Named, and not only counted. "2 could not be read off the disc"
            // is a figure somebody can do nothing with: it does not even say
            // 2 of what, and it reads as a disc number. Which files they are
            // is the difference between a disc to throw away and a game that
            // was going to fail to install.
            return "This disc is not the disc it was meant to be: "
                + string.Join(", ", wrong) + "." + Naming();
        }
    }

    /// <summary>Which files, up to a few of them.</summary>
    /// <remarks>
    /// Three, because this goes in a box somebody reads rather than a log
    /// somebody searches, and a disc that went wrong usually went wrong in
    /// hundreds of files at once.
    /// </remarks>
    private string Naming()
    {
        string[] named = [.. Unreadable.Concat(Missing).Concat(Wrong).Take(3)];

        if (named.Length == 0)
        {
            return string.Empty;
        }

        int more = Unreadable.Count + Missing.Count + Wrong.Count - named.Length;

        return " " + string.Join(", ", named) + (more > 0 ? $", and {more} more." : ".");
    }

    /// <summary>A count of files, with the word on it.</summary>
    private static string Files(int how) => how == 1 ? "1 file" : $"{how} files";
}

/// <summary>How far through reading the disc back.</summary>
public sealed record CheckProgress(int Done, int OfFiles, string File)
{
    public double HowFar => OfFiles == 0 ? 0 : (double)Done / OfFiles;

    public override string ToString() => $"checking the disc, {Done} of {OfFiles}: {File}";
}
