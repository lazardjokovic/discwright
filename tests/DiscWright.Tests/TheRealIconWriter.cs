using System.Drawing;
using System.Drawing.Imaging;

using DiscWright.Platform;

namespace DiscWright.Tests;

/// <summary>
/// The icon and background writers against real pictures, because the one thing
/// a recording stand-in cannot tell you is whether the file that comes out is
/// something Windows will actually open.
/// </summary>
public sealed class TheRealIconWriter : IDisposable
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "dwart_" + Guid.NewGuid().ToString("N")[..8]);

    public TheRealIconWriter() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Writes_an_icon_Windows_can_open()
    {
        string made = Path.Combine(_folder, "disc.ico");

        new WindowsImages().WriteIcon(APicture(512, 512), made);

        // Loaded back with Windows' own icon reader, which is the only opinion
        // that matters: a container written slightly wrong still looks like a
        // file and fails the moment Explorer tries to draw it.
        using Icon icon = new(made);

        Assert.True(icon.Width > 0);
    }

    [Fact]
    public void Carries_every_size_Explorer_asks_for()
    {
        string made = Path.Combine(_folder, "disc.ico");

        new WindowsImages().WriteIcon(APicture(512, 512), made);

        // Read out of the file itself rather than through System.Drawing.Icon.
        // That class picks a frame by its own rules and will hand back the 128
        // one when asked for 256, so asking it would be testing its selector
        // rather than what was written.
        byte[] bytes = File.ReadAllBytes(made);

        // The count lives in the third and fourth bytes.
        Assert.Equal(7, BitConverter.ToInt16(bytes, 4));

        // Each directory entry is 16 bytes and starts with its own width. 256
        // is written as 0, because one byte holds the size and 256 does not fit
        // in one byte, so the format spends its only spare value on it.
        int[] sizes = [.. Enumerable.Range(0, 7).Select(at => (int)bytes[6 + (at * 16)])];

        Assert.Equal([16, 24, 32, 48, 64, 128, 0], sizes);

        // And every frame's data really is inside the file, which is the way a
        // container written slightly wrong still looks like a file.
        for (int at = 0; at < 7; at++)
        {
            int length = BitConverter.ToInt32(bytes, 6 + (at * 16) + 8);
            int offset = BitConverter.ToInt32(bytes, 6 + (at * 16) + 12);

            Assert.True(offset + length <= bytes.Length, $"Frame {at} runs past the end of the file.");
        }
    }

    [Fact]
    public void Draws_the_icon_the_right_way_up()
    {
        // A bitmap stores its rows bottom first, and writing them in the order
        // they are read is the single most common way to produce an icon that
        // is upside down. Nothing else here would notice: the file is the right
        // size, Windows opens it, and every frame is where it should be.
        string source = Path.Combine(_folder, "halves.png");

        using (Bitmap bitmap = new(64, 64))
        using (Graphics drawing = Graphics.FromImage(bitmap))
        {
            drawing.FillRectangle(Brushes.Red, 0, 0, 64, 32);
            drawing.FillRectangle(Brushes.Blue, 0, 32, 64, 32);
            bitmap.Save(source, ImageFormat.Png);
        }

        string made = Path.Combine(_folder, "halves.ico");
        new WindowsImages().WriteIcon(source, made);

        using Icon icon = new(new Icon(made), 64, 64);
        using Bitmap drawn = icon.ToBitmap();

        Assert.True(drawn.GetPixel(32, 8).R > 200, "The top of the icon should be red.");
        Assert.True(drawn.GetPixel(32, 56).B > 200, "The bottom of the icon should be blue.");
    }

    [Fact]
    public void Makes_a_square_icon_out_of_a_picture_that_is_not_one()
    {
        string made = Path.Combine(_folder, "wide.ico");

        new WindowsImages().WriteIcon(APicture(1920, 500), made);

        using Icon icon = new(new Icon(made), 64, 64);

        Assert.Equal(icon.Width, icon.Height);
    }

    [Fact]
    public void Makes_a_background_the_exact_size_of_the_menu()
    {
        string made = Path.Combine(_folder, "background.png");

        new WindowsImages().WriteBackground(APicture(1920, 1080), made, 760, 480, asIs: false);

        using Image background = Image.FromFile(made);

        Assert.Equal(760, background.Width);
        Assert.Equal(480, background.Height);
    }

    [Fact]
    public void Makes_a_background_from_a_picture_of_the_wrong_shape_entirely()
    {
        string made = Path.Combine(_folder, "tall.png");

        // The deliberately wrong shape in the testing kit: 1920x500.
        new WindowsImages().WriteBackground(APicture(1920, 500), made, 760, 480, asIs: false);

        using Image background = Image.FromFile(made);

        Assert.Equal(760, background.Width);
        Assert.Equal(480, background.Height);
    }

    [Fact]
    public void Says_what_is_wrong_with_a_file_it_cannot_read()
    {
        string notAPicture = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(notAPicture, "this is not a picture");

        ImageFactsAssert(new WindowsImages().Read(notAPicture));
    }

    [Fact]
    public void Measures_a_picture_it_can_read()
    {
        DiscWright.Core.ImageFacts facts = new WindowsImages().Read(APicture(800, 600));

        Assert.True(facts.Readable);
        Assert.Equal(800, facts.Width);
        Assert.Equal(600, facts.Height);
        Assert.False(facts.IsSquare);
    }

    private static void ImageFactsAssert(DiscWright.Core.ImageFacts facts)
    {
        Assert.False(facts.Readable);
        Assert.NotNull(facts.Why);

        // Not GDI+'s own words. It reports "Out of memory" for any file it
        // cannot decode, which sends people hunting a problem they do not have.
        Assert.DoesNotContain("memory", facts.Why, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PNG", facts.Why, StringComparison.Ordinal);
    }

    private string APicture(int width, int height)
    {
        string path = Path.Combine(_folder, $"source {width}x{height}.png");

        using (Bitmap bitmap = new(width, height))
        using (Graphics drawing = Graphics.FromImage(bitmap))
        {
            drawing.Clear(Color.CornflowerBlue);
            drawing.FillEllipse(Brushes.White, width / 4, height / 4, width / 2, height / 2);
            bitmap.Save(path, ImageFormat.Png);
        }

        return path;
    }
}
