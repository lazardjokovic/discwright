namespace DiscWright.Core;

/// <summary>
/// The one thing about a file that cannot be read from a listing.
/// </summary>
/// <remarks>
/// A port, and a deliberately tiny one. Everything else about recognising a
/// folder is arithmetic over names and sizes, which is why it is all testable
/// by writing a list. This is the exception: an installer's product name lives
/// inside the file, and it is worth reaching for, because it is the difference
/// between a disc called "The Witcher: Enhanced Edition" and one called
/// "setup the witcher enhanced edition directors cut".
/// </remarks>
public interface IFileFacts
{
    /// <summary>
    /// The product name an executable reports about itself, or null when it
    /// reports none, is not an executable, or cannot be read.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception when the file will not open. A folder that
    /// has gone away between the listing and this call is an ordinary thing on
    /// a network drive or a USB stick, and the name derived from the file name
    /// is a perfectly good answer.
    /// </remarks>
    string? ProductNameOf(string path);
}
