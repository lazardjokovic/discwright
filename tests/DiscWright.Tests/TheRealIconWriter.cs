using System.Drawing;
using System.Drawing.Imaging;

using DiscWright.Platform;

using DiscWright.Core;

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

        new WindowsImages().WriteBackground(APicture(1920, 1080), made, 760, 480, BackgroundFit.Fill);

        using Image background = Image.FromFile(made);

        Assert.Equal(760, background.Width);
        Assert.Equal(480, background.Height);
    }

    [Fact]
    public void Makes_a_background_from_a_picture_of_the_wrong_shape_entirely()
    {
        string made = Path.Combine(_folder, "tall.png");

        // The deliberately wrong shape in the testing kit: 1920x500.
        new WindowsImages().WriteBackground(APicture(1920, 500), made, 760, 480, BackgroundFit.Fill);

        using Image background = Image.FromFile(made);

        Assert.Equal(760, background.Width);
        Assert.Equal(480, background.Height);
    }

    [Theory]
    [InlineData(96f)]
    [InlineData(300f)]
    public void Stretches_a_small_picture_so_all_of_it_shows(float dotsPerInch)
    {
        // xniwo, discwright#98, 15 November: a small banner behind the menu
        // looked the same with Stretch to fit on and off. Nothing here had
        // ever drawn a real picture stretched, or one smaller than the menu,
        // or looked at a single pixel of what came out. At two resolutions,
        // because a picture saved for print says so in its file and GDI+
        // listens when it is not told a size.
        string made = Path.Combine(_folder, "stretched.png");

        new WindowsImages().WriteBackground(ABanner(dotsPerInch), made, 760, 480, BackgroundFit.Stretch);

        using Bitmap background = new(made);

        Assert.Equal(760, background.Width);
        Assert.Equal(480, background.Height);

        // Both ends of the banner are on the menu, and so is its middle.
        Assert.Equal(Color.Red.ToArgb(), background.GetPixel(10, 240).ToArgb());
        Assert.Equal(Color.White.ToArgb(), background.GetPixel(380, 240).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), background.GetPixel(750, 240).ToArgb());

        // And top to bottom, so it reached the edges rather than sitting
        // small in a corner.
        Assert.Equal(Color.Red.ToArgb(), background.GetPixel(10, 5).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), background.GetPixel(750, 475).ToArgb());
    }

    [Theory]
    [InlineData(96f)]
    [InlineData(300f)]
    public void Fills_the_menu_with_a_small_picture_and_crops_both_ends_evenly(float dotsPerInch)
    {
        string made = Path.Combine(_folder, "filled.png");

        new WindowsImages().WriteBackground(ABanner(dotsPerInch), made, 760, 480, BackgroundFit.Fill);

        using Bitmap background = new(made);

        // A banner four times as wide as it is tall, grown until it is as
        // tall as the menu, is far wider than the menu. Its two ends go, the
        // same amount from each, and what is left is its middle.
        Assert.Equal(Color.White.ToArgb(), background.GetPixel(10, 240).ToArgb());
        Assert.Equal(Color.White.ToArgb(), background.GetPixel(750, 240).ToArgb());
        Assert.Equal(Color.White.ToArgb(), background.GetPixel(380, 5).ToArgb());
        Assert.Equal(Color.White.ToArgb(), background.GetPixel(380, 475).ToArgb());
    }

    [Theory]
    [InlineData(96f)]
    [InlineData(300f)]
    public void Fits_a_small_picture_inside_the_menu_whole_with_the_menus_dark_round_it(float dotsPerInch)
    {
        // discwright#98, 15 November: "fit (keeps ratio and doesn't lose
        // image, may show black border)". A banner of 200 by 50 grows 3.8
        // times to the menu's width, 760 by 190, and sits in the middle:
        // rows 145 to 334.
        string made = Path.Combine(_folder, "fitted.png");

        new WindowsImages().WriteBackground(ABanner(dotsPerInch), made, 760, 480, BackgroundFit.Fit);

        using Bitmap background = new(made);

        Assert.Equal(760, background.Width);
        Assert.Equal(480, background.Height);

        // All of it, end to end, across the middle.
        Assert.Equal(Color.Red.ToArgb(), background.GetPixel(10, 240).ToArgb());
        Assert.Equal(Color.White.ToArgb(), background.GetPixel(380, 240).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), background.GetPixel(750, 240).ToArgb());

        // In proportion: it stops well short of the top and the bottom,
        // and is there just inside where it should begin and end.
        Assert.Equal(Color.White.ToArgb(), background.GetPixel(380, 150).ToArgb());
        Assert.Equal(Color.White.ToArgb(), background.GetPixel(380, 330).ToArgb());

        // And what it does not reach is the menu's own dark, solid.
        int dark = Color.FromArgb(255, 0x0C, 0x13, 0x16).ToArgb();

        Assert.Equal(dark, background.GetPixel(380, 5).ToArgb());
        Assert.Equal(dark, background.GetPixel(380, 140).ToArgb());
        Assert.Equal(dark, background.GetPixel(380, 340).ToArgb());
        Assert.Equal(dark, background.GetPixel(10, 475).ToArgb());
    }

    [Fact]
    public void Fits_a_tall_picture_inside_the_menu_with_the_dark_at_its_sides()
    {
        // The other way about: 300 by 600 shrinks to 240 by 480 and sits
        // in the middle, columns 260 to 499.
        string made = Path.Combine(_folder, "fitted tall.png");

        new WindowsImages().WriteBackground(APicture(300, 600), made, 760, 480, BackgroundFit.Fit);

        using Bitmap background = new(made);

        int dark = Color.FromArgb(255, 0x0C, 0x13, 0x16).ToArgb();

        Assert.Equal(dark, background.GetPixel(250, 240).ToArgb());
        Assert.Equal(dark, background.GetPixel(510, 240).ToArgb());
        Assert.Equal(Color.CornflowerBlue.ToArgb(), background.GetPixel(266, 4).ToArgb());
        Assert.Equal(Color.CornflowerBlue.ToArgb(), background.GetPixel(494, 476).ToArgb());
        Assert.Equal(Color.White.ToArgb(), background.GetPixel(380, 240).ToArgb());
    }

    [Theory]
    [InlineData(96f)]
    [InlineData(300f)]
    public void Tiles_a_small_picture_at_its_own_size_pixel_for_pixel(float dotsPerInch)
    {
        // "tile (to repeat the image to fill the window without changing
        // its size)". Every pixel of the menu is the banner's own pixel at
        // the same place within a copy, so nothing was smoothed, nothing
        // was resized and no copy starts a pixel late.
        string made = Path.Combine(_folder, "tiled.png");
        string banner = ABanner(dotsPerInch);

        new WindowsImages().WriteBackground(banner, made, 760, 480, BackgroundFit.Tile);

        using Bitmap background = new(made);
        using Bitmap original = new(banner);

        Assert.Equal(760, background.Width);
        Assert.Equal(480, background.Height);

        for (int across = 0; across < background.Width; across++)
        {
            for (int down = 0; down < background.Height; down++)
            {
                int wanted = original.GetPixel(across % 200, down % 50).ToArgb();

                if (background.GetPixel(across, down).ToArgb() != wanted)
                {
                    Assert.Fail($"The pixel at {across}, {down} is not the banner's own at {across % 200}, {down % 50}.");
                }
            }
        }
    }

    [Fact]
    public void Tiles_a_picture_larger_than_the_menu_as_its_own_top_left_corner()
    {
        string made = Path.Combine(_folder, "tiled large.png");
        string picture = APicture(1920, 1080);

        new WindowsImages().WriteBackground(picture, made, 760, 480, BackgroundFit.Tile);

        using Bitmap background = new(made);
        using Bitmap original = new(picture);

        foreach ((int across, int down) in new[] { (0, 0), (759, 0), (0, 479), (759, 479), (500, 300) })
        {
            Assert.Equal(original.GetPixel(across, down).ToArgb(), background.GetPixel(across, down).ToArgb());
        }
    }

    [Theory]
    [InlineData(BackgroundFit.Stretch, false)]
    [InlineData(BackgroundFit.Fill, false)]
    [InlineData(BackgroundFit.Fit, false)]
    [InlineData(BackgroundFit.Tile, false)]
    [InlineData(BackgroundFit.Stretch, true)]
    [InlineData(BackgroundFit.Fill, true)]
    [InlineData(BackgroundFit.Fit, true)]
    [InlineData(BackgroundFit.Tile, true)]
    public void Leaves_no_part_of_the_background_see_through(BackgroundFit how, bool larger)
    {
        // Found by the two tests above the first time they ran: the rim of a
        // picture grown to fit came out part see-through, and the menu's dark
        // page showed through it as a fringe. Every pixel is asked, for a
        // picture smaller than the menu and for one larger.
        string made = Path.Combine(_folder, "opaque.png");

        new WindowsImages().WriteBackground(
            larger ? APicture(1920, 1080) : ABanner(96f), made, 760, 480, how);

        using Bitmap background = new(made);

        for (int across = 0; across < background.Width; across++)
        {
            for (int down = 0; down < background.Height; down++)
            {
                if (background.GetPixel(across, down).A != 255)
                {
                    Assert.Fail($"The pixel at {across}, {down} is {background.GetPixel(across, down).A} opaque out of 255.");
                }
            }
        }
    }

    // ------------------------------------------- the part somebody framed

    [Theory]
    [InlineData(0.0, "red")]
    [InlineData(1.0, "blue")]
    public void Filled_it_keeps_the_side_of_a_wide_picture_that_was_framed(double across, string colour)
    {
        // Reddit, 20 November 2026: "the option to select the area that gets
        // cropped for the menu image". Twice the menu's width, red on the
        // left half and blue on the right: framed at either end, the menu is
        // one colour from edge to edge, where the middle would be both.
        string made = Path.Combine(_folder, $"kept {colour}.png");

        new WindowsImages().WriteBackground(
            TwoHalves(1520, 480, sideBySide: true), made, 760, 480, BackgroundFit.Fill, new KeptPart(Across: across));

        using Bitmap background = new(made);
        int wanted = (colour == "red" ? Color.Red : Color.Blue).ToArgb();

        foreach ((int x, int y) in new[] { (0, 0), (759, 0), (0, 479), (759, 479), (380, 240), (5, 240), (754, 240) })
        {
            Assert.Equal(wanted, background.GetPixel(x, y).ToArgb());
        }
    }

    [Theory]
    [InlineData(0.0, "red")]
    [InlineData(1.0, "blue")]
    public void And_the_top_or_the_bottom_of_a_tall_one(double down, string colour)
    {
        string made = Path.Combine(_folder, $"kept tall {colour}.png");

        new WindowsImages().WriteBackground(
            TwoHalves(760, 960, sideBySide: false), made, 760, 480, BackgroundFit.Fill, new KeptPart(Down: down));

        using Bitmap background = new(made);
        int wanted = (colour == "red" ? Color.Red : Color.Blue).ToArgb();

        foreach ((int x, int y) in new[] { (0, 0), (759, 0), (0, 479), (759, 479), (380, 240) })
        {
            Assert.Equal(wanted, background.GetPixel(x, y).ToArgb());
        }
    }

    [Fact]
    public void Zoomed_in_twice_on_a_corner_the_menu_is_that_quarter_of_the_picture()
    {
        // The menu's own shape, so without the zoom there would be nothing
        // to choose: red on the left and blue on the right, and the left
        // half zoomed into is red alone.
        string made = Path.Combine(_folder, "kept zoomed.png");

        new WindowsImages().WriteBackground(
            TwoHalves(760, 480, sideBySide: true), made, 760, 480, BackgroundFit.Fill, new KeptPart(0, 0, 2));

        using Bitmap background = new(made);

        // Not the last few columns: the part kept ends exactly where the
        // blue begins, and smoothing two pixels into one there is the
        // picture's own neighbour showing, which is right.
        foreach ((int x, int y) in new[] { (0, 0), (740, 0), (0, 479), (740, 479), (380, 240) })
        {
            Assert.Equal(Color.Red.ToArgb(), background.GetPixel(x, y).ToArgb());
        }

        // And it is the left half, not the whole pulled in: unzoomed, the
        // same place is blue.
        string unzoomed = Path.Combine(_folder, "kept unzoomed.png");

        new WindowsImages().WriteBackground(
            TwoHalves(760, 480, sideBySide: true), unzoomed, 760, 480, BackgroundFit.Fill);

        using Bitmap whole = new(unzoomed);

        Assert.Equal(Color.Blue.ToArgb(), whole.GetPixel(740, 240).ToArgb());
    }

    [Fact]
    public void The_middle_framed_is_pixel_for_pixel_what_no_framing_gives()
    {
        // Every disc made before had the middle. Saying so out loud must
        // not move a single pixel of it.
        string picture = APicture(1920, 1080);
        string plain = Path.Combine(_folder, "plain.png");
        string said = Path.Combine(_folder, "said.png");

        new WindowsImages().WriteBackground(picture, plain, 760, 480, BackgroundFit.Fill);
        new WindowsImages().WriteBackground(picture, said, 760, 480, BackgroundFit.Fill, KeptPart.TheMiddle);

        Assert.Equal(File.ReadAllBytes(plain), File.ReadAllBytes(said));
    }

    [Theory]
    [InlineData(BackgroundFit.Stretch)]
    [InlineData(BackgroundFit.Fit)]
    [InlineData(BackgroundFit.Tile)]
    public void No_other_way_to_fit_reads_the_part_framed(BackgroundFit how)
    {
        string picture = TwoHalves(1520, 480, sideBySide: true);
        string plain = Path.Combine(_folder, $"plain {how}.png");
        string framed = Path.Combine(_folder, $"framed {how}.png");

        new WindowsImages().WriteBackground(picture, plain, 760, 480, how);
        new WindowsImages().WriteBackground(picture, framed, 760, 480, how, new KeptPart(0, 0, 3));

        Assert.Equal(File.ReadAllBytes(plain), File.ReadAllBytes(framed));
    }

    [Fact]
    public void A_part_framed_leaves_nothing_see_through_either()
    {
        string made = Path.Combine(_folder, "kept opaque.png");

        new WindowsImages().WriteBackground(
            ABanner(96f), made, 760, 480, BackgroundFit.Fill, new KeptPart(0.9, 0.1, 1.5));

        using Bitmap background = new(made);

        for (int across = 0; across < background.Width; across++)
        {
            for (int down = 0; down < background.Height; down++)
            {
                if (background.GetPixel(across, down).A != 255)
                {
                    Assert.Fail($"The pixel at {across}, {down} is {background.GetPixel(across, down).A} opaque out of 255.");
                }
            }
        }
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

    /// <summary>
    /// A banner smaller than the menu in both directions, 200 by 50, white
    /// with a red tenth at its left end and a blue tenth at its right, so
    /// where each end landed can be read off one pixel.
    /// </summary>
    private string ABanner(float dotsPerInch)
    {
        string path = Path.Combine(_folder, $"banner at {dotsPerInch}.png");

        using (Bitmap bitmap = new(200, 50))
        {
            bitmap.SetResolution(dotsPerInch, dotsPerInch);

            using (Graphics drawing = Graphics.FromImage(bitmap))
            {
                drawing.Clear(Color.White);
                drawing.FillRectangle(Brushes.Red, 0, 0, 20, 50);
                drawing.FillRectangle(Brushes.Blue, 180, 0, 20, 50);
            }

            bitmap.Save(path, ImageFormat.Png);
        }

        return path;
    }

    /// <summary>Red and blue, half each, side by side or one above the other.</summary>
    private string TwoHalves(int width, int height, bool sideBySide)
    {
        string path = Path.Combine(_folder, $"halves {width}x{height} {sideBySide}.png");

        using (Bitmap bitmap = new(width, height))
        using (Graphics drawing = Graphics.FromImage(bitmap))
        {
            drawing.Clear(Color.Blue);
            drawing.FillRectangle(Brushes.Red, 0, 0, sideBySide ? width / 2 : width, sideBySide ? height : height / 2);
            bitmap.Save(path, ImageFormat.Png);
        }

        return path;
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
