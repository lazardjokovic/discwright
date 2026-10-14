using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

using DiscWright.Core;

namespace DiscWright.Platform;

/// <summary>
/// Reads pictures with Windows' own decoder, and writes the two a disc needs.
/// </summary>
public sealed class WindowsImages : IImages
{
    /// <summary>
    /// The sizes a Windows icon carries. Explorer asks for 16 in a list and 256
    /// on the desktop, and given only one it scales the rest itself, badly.
    /// </summary>
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    public ImageFacts Read(string path)
    {
        try
        {
            using Image image = Image.FromFile(path);

            return new ImageFacts(true, image.Width, image.Height, null);
        }
        catch (FileNotFoundException)
        {
            return new ImageFacts(false, 0, 0, "That file is not there any more.");
        }
        catch (OutOfMemoryException)
        {
            // GDI+ reports "Out of memory" for ANY file it cannot decode, which
            // sends people hunting a memory problem they do not have. Say what
            // is actually wrong instead.
            return new ImageFacts(false, 0, 0,
                "Windows cannot read that file as an image. It may be corrupt, still downloading, or a format "
                + "Windows does not support: WebP and HEIC are the usual culprits. PNG, JPG and BMP always work.");
        }
        catch (ArgumentException)
        {
            return new ImageFacts(false, 0, 0, "Windows cannot read that file as an image.");
        }
    }

    public void WriteIcon(string source, string destination)
    {
        using Image original = Image.FromFile(source);

        List<byte[]> frames = [];

        foreach (int size in IconSizes)
        {
            frames.Add(DibOf(SquareOf(original, size)));
        }

        using FileStream file = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        using BinaryWriter write = new(file);

        // ICONDIR: reserved, type 1 meaning icon, how many frames.
        write.Write((short)0);
        write.Write((short)1);
        write.Write((short)frames.Count);

        // Every entry is written before any pixel data, so each one has to know
        // where its own data will land.
        int offset = 6 + (16 * frames.Count);

        for (int at = 0; at < frames.Count; at++)
        {
            int size = IconSizes[at];

            // 256 is written as 0. One byte holds the size, and 256 does not fit
            // in one byte, so the format spends its only spare value on it.
            write.Write((byte)(size == 256 ? 0 : size));
            write.Write((byte)(size == 256 ? 0 : size));
            write.Write((byte)0);
            write.Write((byte)0);
            write.Write((short)1);
            write.Write((short)32);
            write.Write(frames[at].Length);
            write.Write(offset);

            offset += frames[at].Length;
        }

        foreach (byte[] frame in frames)
        {
            write.Write(frame);
        }
    }

    public void WriteBackground(string source, string destination, int width, int height, bool asIs)
    {
        using Image original = Image.FromFile(source);
        using Bitmap canvas = new(width, height, PixelFormat.Format32bppArgb);
        using Graphics drawing = Graphics.FromImage(canvas);

        Nicely(drawing);

        if (asIs)
        {
            // Stretched to the shape of the menu, proportions and all.
            drawing.DrawImage(original, 0, 0, width, height);
        }
        else
        {
            // Filled and cropped evenly. Keeping the proportions is almost
            // always what somebody wants from a screenshot or cover art, and
            // the overflow is taken off both sides rather than one.
            double scale = Math.Max((double)width / original.Width, (double)height / original.Height);
            int drawnWidth = (int)Math.Ceiling(original.Width * scale);
            int drawnHeight = (int)Math.Ceiling(original.Height * scale);

            drawing.DrawImage(
                original,
                (width - drawnWidth) / 2,
                (height - drawnHeight) / 2,
                drawnWidth,
                drawnHeight);
        }

        canvas.Save(destination, ImageFormat.Png);
    }

    /// <summary>One square frame of an icon, cropped from the middle if it has to be.</summary>
    private static Bitmap SquareOf(Image original, int size)
    {
        Bitmap frame = new(size, size, PixelFormat.Format32bppArgb);

        using Graphics drawing = Graphics.FromImage(frame);

        Nicely(drawing);

        int side = Math.Min(original.Width, original.Height);

        drawing.DrawImage(
            original,
            new Rectangle(0, 0, size, size),
            new Rectangle((original.Width - side) / 2, (original.Height - side) / 2, side, side),
            GraphicsUnit.Pixel);

        return frame;
    }

    /// <summary>
    /// One frame as an icon file wants it: a bitmap header, the pixels upside
    /// down, and a mask.
    /// </summary>
    private static byte[] DibOf(Bitmap frame)
    {
        using (frame)
        {
            int width = frame.Width;
            int height = frame.Height;

            BitmapData pixels = frame.LockBits(
                new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            byte[] rows = new byte[pixels.Stride * height];
            Marshal.Copy(pixels.Scan0, rows, 0, rows.Length);
            frame.UnlockBits(pixels);

            using MemoryStream made = new();
            using BinaryWriter write = new(made);

            // BITMAPINFOHEADER. The height is DOUBLED, because the format
            // expects the colour rows and the mask rows stacked together and
            // describes both at once.
            write.Write(40);
            write.Write(width);
            write.Write(height * 2);
            write.Write((short)1);
            write.Write((short)32);
            write.Write(0);
            write.Write(0);
            write.Write(0);
            write.Write(0);
            write.Write(0);
            write.Write(0);

            // Bottom row first, which is how a bitmap has stored its rows since
            // 1990 and is the single most common way to get an icon upside down.
            for (int row = height - 1; row >= 0; row--)
            {
                write.Write(rows, row * pixels.Stride, width * 4);
            }

            // The mask, one bit a pixel, each row padded out to four bytes. All
            // zeroes, because the alpha in the pixels above already says what is
            // transparent, but it has to be here and it has to be the right
            // size or everything after it is read at the wrong offset.
            int maskRow = ((width + 31) / 32) * 4;
            write.Write(new byte[maskRow * height]);

            return made.ToArray();
        }
    }

    private static void Nicely(Graphics drawing)
    {
        drawing.InterpolationMode = InterpolationMode.HighQualityBicubic;
        drawing.PixelOffsetMode = PixelOffsetMode.HighQuality;
        drawing.SmoothingMode = SmoothingMode.HighQuality;
        drawing.CompositingQuality = CompositingQuality.HighQuality;
    }
}
