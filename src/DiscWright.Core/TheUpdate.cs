namespace DiscWright.Core;

/// <summary>
/// One file attached to a release, as the place releases are published lists it.
/// </summary>
/// <param name="Name">What the file is called.</param>
/// <param name="Url">Where it downloads from.</param>
/// <param name="Size">How big it says it is, in bytes.</param>
public sealed record AReleasedFile(string Name, string Url, long Size);

/// <summary>
/// The installer of a newer release, and the checksums it has to match.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Only ever when somebody presses Update.</strong> Nothing here asks,
/// downloads or runs anything by itself; it decides what would be fetched if
/// somebody did.
/// </para>
/// <para>
/// <strong>Our own release and nothing else.</strong> The installer has to be
/// called exactly what the release workflow calls it, and both files have to
/// download from that release of this repository. A release carrying anything
/// else offers no update, only its page, because running a file from somewhere
/// unexpected is the one thing a button like this must never do.
/// </para>
/// <para>
/// The checksums are what the release workflow writes beside the installer, in
/// the format <c>sha256sum</c> reads. Matching them proves the download is the
/// file the release published, whole. It does not prove who published it: that
/// is what signing the installer would add, and until then it rests on the
/// GitHub account.
/// </para>
/// </remarks>
/// <param name="Version">The version it updates to, as it is written in the file names.</param>
/// <param name="Installer">The setup to download and run.</param>
/// <param name="Sums">The checksums published beside it.</param>
public sealed record TheUpdate(string Version, AReleasedFile Installer, AReleasedFile Sums)
{
    /// <summary>Where every file of every release of this program downloads from.</summary>
    public const string Downloads = "https://github.com/lazardjokovic/discwright/releases/download/";

    /// <summary>What the release workflow calls the checksums.</summary>
    public const string SumsName = "SHA256SUMS.txt";

    /// <summary>
    /// The update a newer release offers, or nothing when it offers none.
    /// </summary>
    /// <remarks>
    /// Nothing when this build is not older, when the tag is not a version,
    /// when either file is missing, and when either would download from
    /// anywhere but that release.
    /// </remarks>
    public static TheUpdate? Offered(HowThisBuildCompares how, IReadOnlyList<AReleasedFile>? files)
    {
        if (!how.WorthGetting || how.Theirs is not string tag || files is null)
        {
            return null;
        }

        string version = tag.Trim();

        if (version.StartsWith('v') || version.StartsWith('V'))
        {
            version = version[1..];
        }

        AReleasedFile? installer = TheOneCalled($"DiscWright-{version}-setup.exe", files);
        AReleasedFile? sums = TheOneCalled(SumsName, files);

        if (installer is null || sums is null || !FromThat(tag, installer) || !FromThat(tag, sums))
        {
            return null;
        }

        return new TheUpdate(version, installer, sums);
    }

    /// <summary>
    /// The hash the checksums give for one file, or nothing when they give
    /// none or disagree with themselves.
    /// </summary>
    /// <remarks>
    /// A line is a hash, a space, and the name, with a star in front of it in
    /// binary mode. A name listed twice with two different hashes counts as
    /// not listed, because either answer would be a guess.
    /// </remarks>
    public static string? TheHashIn(string sums, string named)
    {
        string? found = null;

        foreach (string raw in sums.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            int gap = line.IndexOf(' ');

            if (gap != 64 || !line[..64].All(Uri.IsHexDigit))
            {
                continue;
            }

            string name = line[(gap + 1)..].TrimStart(' ');

            if (name.StartsWith('*'))
            {
                name = name[1..];
            }

            if (!string.Equals(name, named, StringComparison.Ordinal))
            {
                continue;
            }

            string hash = line[..64].ToLowerInvariant();

            if (found is not null && found != hash)
            {
                return null;
            }

            found = hash;
        }

        return found;
    }

    /// <summary>Whether a file hashed to what the checksums said it would.</summary>
    public static bool Matches(string? expected, string? hashed) =>
        expected is not null
        && hashed is not null
        && string.Equals(expected.Trim(), hashed.Trim(), StringComparison.OrdinalIgnoreCase);

    private static AReleasedFile? TheOneCalled(string name, IReadOnlyList<AReleasedFile> files)
    {
        AReleasedFile[] called = [.. files.Where(file => string.Equals(file.Name, name, StringComparison.Ordinal))];

        // Two files of one name in one release is not something GitHub allows,
        // and a list that says otherwise is not one to pick from.
        return called.Length == 1 ? called[0] : null;
    }

    private static bool FromThat(string tag, AReleasedFile file) =>
        string.Equals(file.Url, Downloads + tag + "/" + file.Name, StringComparison.Ordinal);
}

/// <summary>
/// Updating the copy of the program that is running, when somebody asks.
/// </summary>
/// <remarks>
/// <para>
/// Only an installed copy can update itself. A portable one has no installer
/// to run, so it is offered the release page as before.
/// </para>
/// <para>
/// Everything that touches the network, the disk or another process is behind
/// this, so the whole of deciding what to do and what to say can be tested
/// with none of them.
/// </para>
/// </remarks>
public interface IUpdateThisCopy
{
    /// <summary>Whether this copy was put here by the installer, and so can be updated by one.</summary>
    bool CanUpdateItself { get; }

    /// <summary>Where a downloaded file of this name is kept until it runs.</summary>
    string WhereTheDownloadGoes(string named);

    /// <summary>A small text file, read whole.</summary>
    Task<string> Read(string url, CancellationToken ct);

    /// <summary>A file downloaded to <paramref name="into"/>, saying how many bytes have arrived.</summary>
    Task Download(string url, string into, IProgress<long> sofar, CancellationToken ct);

    /// <summary>Deletes a download that is not going to run.</summary>
    void ThrowAway(string path);

    /// <summary>
    /// Starts the installer quietly, asking it to open the program again once
    /// it is done, and closes this one so its file can be replaced.
    /// </summary>
    void InstallAndClose(string installer);
}
