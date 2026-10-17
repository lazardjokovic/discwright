namespace DiscWright.Core.Staging;

/// <summary>
/// Puts the pictures on the disc: the icon This PC shows, the background the
/// menu sits on, and the cover and disc face somebody will print.
/// </summary>
public sealed class CopyTheArtworkStep(IImages images) : IBuildStep
{
    /// <summary>
    /// Where the printable artwork lives on the disc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This folder is the ten year promise made real. Whoever is holding the
    /// disc can print its cover <strong>without this program, without a
    /// project file, and without us</strong>. Everything else on the disc
    /// needs something; this needs a printer.
    /// </para>
    /// <para>
    /// Which is why it is a plain folder of ordinary pictures with a readme
    /// beside them, and not a format of ours.
    /// </para>
    /// </remarks>
    public const string ArtworkFolder = "ARTWORK";

    /// <summary>Where the menu lives on a disc, and so where its icon lives too.</summary>
    private const string MenuFolder = "AUTORUN";

    /// <summary>
    /// What separates a folder from a file on the disc. Written as a code
    /// rather than typed, because a lone backslash is the one character that
    /// does not survive being generated.
    /// </summary>
    private const char Separator = (char)92;

    /// <summary>The menu's own size, which is what the background is made to fit.</summary>
    private const int MenuWidth = 760;

    private const int MenuHeight = 480;

    public string Says => "putting the artwork on";

    public bool AppliesTo(BuildContext context) => context.Project.Artwork?.IsAnything == true;

    public Task Run(BuildContext context, CancellationToken ct)
    {
        DiscArtwork artwork = context.Project.Artwork!;

        ct.ThrowIfCancellationRequested();

        if (artwork.IconPath is not null)
        {
            // Once, at the root, and the menu in AUTORUN points up at it.
            //
            // v1 writes a second copy beside the menu as well, on the grounds
            // that an HTA will not resolve an icon path that climbs out of
            // the page's own folder. That was checked here and it is not
            // true: a page in AUTORUN asking for "../" and the icon's name
            // wears it correctly. A second copy would be another third of a
            // megabyte on every disc for nothing.
            images.WriteIcon(artwork.IconPath, context.On(context.IconFileName));
        }

        // Only when there is a menu to put it behind. A disc with no menu that
        // carried a background would be carrying a file nothing ever opens.
        if (artwork.BackgroundPath is not null && context.Project.Options.Menu)
        {
            context.Writer.MakeFolder(context.On(MenuFolder));

            images.WriteBackground(
                artwork.BackgroundPath,
                context.On(MenuFolder + Separator + "background.png"),
                MenuWidth,
                MenuHeight,
                artwork.BackgroundAsIs);
        }

        ct.ThrowIfCancellationRequested();

        // Always, with no option to turn it off.
        //
        // A cover and a label are small, and a disc that has them is a disc
        // somebody can reprint the sleeve from in ten years without still
        // having the folder they built it out of. Making that a setting meant
        // the usual answer was no, because the usual answer to a setting
        // nobody understands is to leave it alone.
        if (artwork.HasSomethingToPrint)
        {
            PutThePrintableArtworkOn(context, artwork);
        }

        return Task.CompletedTask;
    }

    private static void PutThePrintableArtworkOn(BuildContext context, DiscArtwork artwork)
    {
        context.Writer.MakeFolder(context.On(ArtworkFolder));

        // Copied exactly as they were given, at whatever size they were given.
        // Resizing them would be deciding what somebody's printer can do, from
        // here, years early.
        if (artwork.CoverPath is not null)
        {
            context.Writer.Copy(artwork.CoverPath, context.On(ArtworkFolder + @"\cover" + ExtensionOf(artwork.CoverPath)));
        }

        if (artwork.DiscFacePath is not null)
        {
            context.Writer.Copy(artwork.DiscFacePath, context.On(ArtworkFolder + @"\disc face" + ExtensionOf(artwork.DiscFacePath)));
        }

        context.Writer.WriteText(context.On(ArtworkFolder + @"\READ ME.txt"), Readme(context.Project.Label));
    }

    private static string ExtensionOf(string path)
    {
        string extension = System.IO.Path.GetExtension(path);

        return string.IsNullOrEmpty(extension) ? ".png" : extension.ToLowerInvariant();
    }

    /// <summary>
    /// Written for somebody who has never heard of this program, because in ten
    /// years that is who will be reading it.
    /// </summary>
    private static string Readme(string label) =>
        $"""
        The printed artwork for {label}

        cover       the sleeve, for the case
        disc face   the label for the disc itself

        These are ordinary picture files. Open them with anything, print them
        from anything. Nothing here needs the program that made this disc, and
        that is deliberate: a disc that can only be reprinted by one program is
        a disc that stops being printable the day that program stops running.

        Print them at their own size rather than fitting them to the page, or
        the sleeve comes out slightly too small for the case.

        """.ReplaceLineEndings("\r\n");
}
