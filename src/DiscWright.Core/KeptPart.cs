using System.Globalization;

namespace DiscWright.Core;

/// <summary>
/// Which part of a picture is kept when it is filled and cropped: where the
/// frame sits over it, and how far in it is zoomed.
/// </summary>
/// <remarks>
/// <para>
/// Asked for on Reddit on 20 November 2026: "the option to select the area
/// that gets cropped for the menu image". Until then the overflow came off
/// both sides evenly, which cuts the head off a tall cover and the title off
/// a wide one, and the only cure was an image editor.
/// </para>
/// <para>
/// Kept as fractions and never as pixels, so it means the same thing over
/// the small frame somebody drags in and over the 760 by 480 that is drawn,
/// and a project file carries it to a machine with a different screen.
/// </para>
/// </remarks>
/// <param name="Across">
/// Where the frame sits across the slack: 0 keeps the left edge, 1 the right,
/// a half the middle.
/// </param>
/// <param name="Down">The same from top to bottom.</param>
/// <param name="Zoom">
/// How far in: 1 is the least that covers the frame, and
/// <see cref="MostZoom"/> the most.
/// </param>
public sealed record KeptPart(double Across = 0.5, double Down = 0.5, double Zoom = 1.0)
{
    /// <summary>
    /// The most it zooms. Past four times, a 760 wide frame is showing fewer
    /// than 200 of an ordinary picture's pixels, which is a blur.
    /// </summary>
    public const double MostZoom = 4.0;

    /// <summary>The middle at no zoom, which is what every disc had before.</summary>
    public static KeptPart TheMiddle { get; } = new();

    /// <summary>Whether this is the middle at no zoom, as near as a drag can tell.</summary>
    public bool IsTheMiddle =>
        Math.Abs(Across - 0.5) < Hair && Math.Abs(Down - 0.5) < Hair && Math.Abs(Zoom - 1.0) < Hair;

    /// <summary>
    /// This, with every number inside what it can mean. A file somebody
    /// edited by hand can say anything, and a number that is not a number
    /// drawn with is a blank menu.
    /// </summary>
    public KeptPart Tidied() =>
        new(Within(Across, 0, 1, 0.5), Within(Down, 0, 1, 0.5), Within(Zoom, 1, MostZoom, 1));

    /// <summary>
    /// The rectangle of the picture that is kept, in the picture's own
    /// pixels, for a frame of this shape.
    /// </summary>
    /// <remarks>
    /// Always the frame's shape, so nothing is ever pulled out of
    /// proportion, and always inside the picture, so the menu's dark never
    /// shows down one side.
    /// </remarks>
    public Kept In(int pictureWidth, int pictureHeight, int frameWidth, int frameHeight)
    {
        if (pictureWidth <= 0 || pictureHeight <= 0 || frameWidth <= 0 || frameHeight <= 0)
        {
            return new Kept(0, 0, Math.Max(pictureWidth, 0), Math.Max(pictureHeight, 0));
        }

        KeptPart tidy = Tidied();

        double scale = Math.Max((double)frameWidth / pictureWidth, (double)frameHeight / pictureHeight) * tidy.Zoom;
        double width = Math.Min(pictureWidth, frameWidth / scale);
        double height = Math.Min(pictureHeight, frameHeight / scale);

        return new Kept(
            (pictureWidth - width) * tidy.Across,
            (pictureHeight - height) * tidy.Down,
            width,
            height);
    }

    /// <summary>
    /// This after the picture is dragged under the frame, by a fraction of
    /// the frame each way: to the right and down are positive.
    /// </summary>
    /// <remarks>
    /// Dragging the picture right brings what was to the left into the
    /// frame, so the frame moves left over the picture. It stops at the
    /// picture's edge, and where there is no slack in a direction, a drag
    /// that way changes nothing rather than dividing by it.
    /// </remarks>
    public KeptPart Dragged(
        double across, double down, int pictureWidth, int pictureHeight, int frameWidth, int frameHeight)
    {
        Kept now = In(pictureWidth, pictureHeight, frameWidth, frameHeight);
        KeptPart tidy = Tidied();

        double slackAcross = pictureWidth - now.Width;
        double slackDown = pictureHeight - now.Height;

        return tidy with
        {
            Across = slackAcross > Hair
                ? Within((now.X - (across * now.Width)) / slackAcross, 0, 1, tidy.Across)
                : tidy.Across,
            Down = slackDown > Hair
                ? Within((now.Y - (down * now.Height)) / slackDown, 0, 1, tidy.Down)
                : tidy.Down,
        };
    }

    private const double Hair = 0.0005;

    private static double Within(double value, double least, double most, double otherwise) =>
        double.IsFinite(value) ? Math.Clamp(value, least, most) : otherwise;
}

/// <summary>A rectangle of a picture, in whatever the picture is measured in.</summary>
public readonly record struct Kept(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// The same rectangle as fractions of a picture this big, which is how a
    /// window shows it over a thumbnail of any size.
    /// </summary>
    public Kept AsFractionsOf(int pictureWidth, int pictureHeight) =>
        pictureWidth <= 0 || pictureHeight <= 0
            ? new Kept(0, 0, 1, 1)
            : new Kept(X / pictureWidth, Y / pictureHeight, Width / pictureWidth, Height / pictureHeight);

    /// <summary>Four numbers with commas, the same on every machine.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{X:0.####},{Y:0.####},{Width:0.####},{Height:0.####}");
}
