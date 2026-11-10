using System.Security.Cryptography;

using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// SHA-256, read once and hashed as it goes.
/// </summary>
/// <remarks>
/// Hand-rolled rather than the one-line call, for a measured reason carried
/// over from v1: the convenient one runs at roughly half the speed, which on a
/// filled Blu-ray is the difference between four minutes and eight. The
/// cancellation check sits inside the read loop for the same reason, because
/// four minutes is far too long for Cancel to do nothing.
/// </remarks>
public sealed class Sha256Hasher : IHasher
{
    /// <summary>
    /// Read in bites of a megabyte. Larger stopped helping and starts costing
    /// memory on a machine doing something else at the time.
    /// </summary>
    private const int Bite = 1024 * 1024;

    public string Sha256(string path, CancellationToken ct) => Sha256(path, null, ct);

    /// <summary>How often the bytes read are said: every 8 MB, a few times a second off a disc.</summary>
    private const long SayEvery = 8L * 1024 * 1024;

    public string Sha256(string path, IProgress<long>? read, CancellationToken ct)
    {
        using SHA256 sha = System.Security.Cryptography.SHA256.Create();

        // Shared read, so hashing a file does not stop anything else opening
        // it, including the copy that may still be reading from the same disk.
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, Bite, FileOptions.SequentialScan);

        byte[] buffer = new byte[Bite];
        int got;
        long sofar = 0;
        long said = 0;

        while ((got = file.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.TransformBlock(buffer, 0, got, null, 0);
            sofar += got;

            if (read is not null && sofar - said >= SayEvery)
            {
                read.Report(sofar);
                said = sofar;
            }
        }

        read?.Report(sofar);

        sha.TransformFinalBlock([], 0, 0);

        // Lowercase hexadecimal, the way sha256sum writes it, so the file this
        // ends up in can be checked by the real tool without translation.
        return Convert.ToHexStringLower(sha.Hash ?? []);
    }
}
