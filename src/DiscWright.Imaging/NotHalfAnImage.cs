namespace DiscWright.Imaging;

/// <summary>
/// Takes away an image that was not finished.
/// </summary>
/// <remarks>
/// An image is written from its first byte to its last, and one that stopped
/// in between is a file with an image's name that is no image. Nothing about
/// it says so: it mounts as far as it goes and burns as far as it goes.
/// </remarks>
public static class NotHalfAnImage
{
    /// <summary>
    /// Deletes what was written, if anything was. Never throws: this is
    /// called on the way out of something that has already gone wrong, and
    /// what went wrong is the thing to say.
    /// </summary>
    public static void Leave(string imagePath)
    {
        try
        {
            if (System.IO.File.Exists(imagePath))
            {
                System.IO.File.Delete(imagePath);
            }
        }
        catch (Exception held) when (held is System.IO.IOException or UnauthorizedAccessException)
        {
        }
    }
}
