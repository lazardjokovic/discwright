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
}
