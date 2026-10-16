namespace DiscWright.Core;

/// <summary>
/// What a disc carries before any of the game goes on it.
/// </summary>
/// <remarks>
/// Taken off the capacity once, up front, because every disc in a set carries
/// the same thing: the menu, the autorun, the icon, the artwork and the
/// checksum list. A file that will not fit one disc will not fit any of them.
/// </remarks>
public static class DiscOverhead
{
    /// <summary>
    /// The menu, the autorun file, the checksum list and room to be wrong in.
    /// </summary>
    /// <remarks>
    /// Carried over from v1. The menu and autorun are a few kilobytes between
    /// them, and the checksum list is roughly ninety bytes a file, so ten
    /// thousand files is under a megabyte. The rest is slack, and slack on a
    /// disc is cheaper than a build that fails at ninety nine per cent.
    /// </remarks>
    private const long TheMenuAndRoomToBeWrongIn = 2L * 1024 * 1024;

    /// <summary>
    /// How much of a disc is gone before the game is on it.
    /// </summary>
    /// <param name="sizeOf">
    /// How big a file is, asked of the caller rather than measured here,
    /// because this has to stay testable without a disk.
    /// </param>
    public static long For(DiscProject project, Func<string, long> sizeOf)
    {
        long bytes = TheMenuAndRoomToBeWrongIn;

        // Music is overhead rather than payload: it belongs to the menu, and
        // every disc of a set has a menu of its own to play it, so every disc
        // carries a copy.
        if (project.Extras?.MusicPath is not null && project.Options.Menu)
        {
            bytes += sizeOf(project.Extras.MusicPath);
        }

        DiscArtwork? artwork = project.Artwork;

        if (artwork is null)
        {
            return bytes;
        }

        // Twice the source, because what lands on the disc is not the file that
        // was picked. An icon becomes a .ico with seven frames in it and a
        // background is redrawn at the menu's size, and either can come out
        // larger than it went in.
        if (artwork.IconPath is not null)
        {
            bytes += 2 * sizeOf(artwork.IconPath);
        }

        if (artwork.BackgroundPath is not null && project.Options.Menu)
        {
            bytes += 2 * sizeOf(artwork.BackgroundPath);
        }

        // The printable artwork is copied exactly as it was given, so it counts
        // once and at its own size. v1 left these out of the sum, which is fine
        // until somebody picks a 60 MB scan of a sleeve and the last disc of a
        // set comes up short.
        // Counted always, because it is copied always.
        {
            if (artwork.CoverPath is not null)
            {
                bytes += sizeOf(artwork.CoverPath);
            }

            if (artwork.DiscFacePath is not null)
            {
                bytes += sizeOf(artwork.DiscFacePath);
            }
        }

        return bytes;
    }
}
