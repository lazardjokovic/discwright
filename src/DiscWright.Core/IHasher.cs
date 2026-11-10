namespace DiscWright.Core;

/// <summary>
/// Working out what a file hashes to.
/// </summary>
/// <remarks>
/// A port because it reads a file, and because how it reads one matters: on a
/// filled Blu-ray the obvious call and a hand-rolled read are the difference
/// between four minutes and eight.
/// </remarks>
public interface IHasher
{
    /// <summary>
    /// SHA-256 of a whole file, as lowercase hexadecimal, the way sha256sum
    /// writes it.
    /// </summary>
    /// <remarks>
    /// Takes a cancellation token because this is the one thing in a build that
    /// can run for minutes on a single file, and Cancel has to mean something
    /// during it rather than after it.
    /// </remarks>
    string Sha256(string path, CancellationToken ct);

    /// <summary>
    /// The same, saying how many bytes it has read as it goes.
    /// </summary>
    /// <remarks>
    /// For reading a disc back, where one file can be most of a disc and a
    /// count of files sat still on it for minutes: the owner watched
    /// "checking the disc, 5 of 41" for two and a half on 2026-11-10. A
    /// hasher that cannot say simply does not, and is still a hasher.
    /// </remarks>
    string Sha256(string path, IProgress<long>? read, CancellationToken ct) => Sha256(path, ct);
}
