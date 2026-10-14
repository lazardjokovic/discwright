using System.Diagnostics;

using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// Reads the product name an executable reports about itself.
/// </summary>
/// <remarks>
/// The one fact about a file that a listing cannot carry, and worth the trip to
/// disk: it is the difference between a disc called "The Witcher: Enhanced
/// Edition" and one called "setup the witcher enhanced edition directors cut".
/// </remarks>
public sealed class FileVersionFacts : IFileFacts
{
    public string? ProductNameOf(string path)
    {
        try
        {
            string? reported = FileVersionInfo.GetVersionInfo(path).ProductName;

            return string.IsNullOrWhiteSpace(reported) ? null : reported;
        }
        catch (FileNotFoundException)
        {
            // Null, never an exception. A folder that has gone away between the
            // listing and this call is an ordinary thing on a USB stick or a
            // network drive, and the name worked out from the file name is a
            // perfectly good answer.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
