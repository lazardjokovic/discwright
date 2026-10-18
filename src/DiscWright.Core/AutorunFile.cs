namespace DiscWright.Core;

/// <summary>
/// The <c>autorun.inf</c> a disc carries: what it is called, what icon it
/// shows, and what happens when it goes in.
/// </summary>
public static class AutorunFile
{
    /// <summary>
    /// Where the menu sits on a disc, as autorun.inf has to spell it.
    /// </summary>
    public static string MenuPath => @"AUTORUN\menu.hta";

    /// <summary>
    /// The whole file, as text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Control characters are stripped <strong>here</strong>, rather than only
    /// where the label was typed, because this is the function that turns text
    /// into directives and so it is the last place that can be sure.
    /// </para>
    /// <para>
    /// It matters more than it looks. autorun.inf is one directive per line, so
    /// a label carrying a newline writes further directives:
    /// </para>
    /// <code>
    /// label=My Game
    /// open=Extras\payload.exe        &lt;- came from inside the "label"
    /// </code>
    /// <para>
    /// Windows 7 and later will not silently run an <c>open=</c> from optical
    /// media, but the wording of the AutoPlay prompt and what it points at
    /// would both be chosen by whoever wrote the project file, on a disc the
    /// person burning it believed was theirs. Project files get passed around:
    /// v1's own README suggests attaching one to a bug report.
    /// </para>
    /// <para>
    /// This is not a restriction on what may be typed. The label box is one
    /// line and cannot produce these. It is a filter on what a <em>file</em>
    /// may carry, which is the path that actually reaches a build.
    /// </para>
    /// </remarks>
    /// <param name="label">What This PC shows when the disc goes in.</param>
    /// <param name="iconFileName">
    /// The icon the disc really carries, or <c>null</c> when it carries none.
    /// Null leaves the directive out altogether rather than naming a file that
    /// is not there. <c>icon=</c> used to be written either way, and a disc
    /// built with no chosen artwork pointed Windows at a file nothing wrote.
    /// </param>
    /// <param name="hasMenu">Whether this disc has a menu to open.</param>
    public static string For(string label, string? iconFileName, bool hasMenu)
    {
        string safeLabel = WithoutControlCharacters(label);

        List<string> lines = ["[autorun]"];

        if (hasMenu)
        {
            lines.Add("shellexecute=" + MenuPath);
        }

        if (!string.IsNullOrEmpty(iconFileName))
        {
            lines.Add("icon=" + WithoutControlCharacters(iconFileName));
        }

        lines.Add("label=" + safeLabel);

        if (hasMenu)
        {
            // What the AutoPlay prompt offers. Without it Windows says "Run
            // menu.hta", which tells somebody nothing about what is in their
            // hand.
            lines.Add("action=Run " + safeLabel);
        }

        // Explorer otherwise offers to import the disc's pictures or play its
        // music, which for a game disc is a prompt about the wrong thing.
        lines.Add("");
        lines.Add("[Content]");
        lines.Add("MusicFiles=false");
        lines.Add("PictureFiles=false");
        lines.Add("VideoFiles=false");

        // CRLF, because this is a Windows configuration file read by a part of
        // Windows older than most of it.
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string WithoutControlCharacters(string value) =>
        new([.. value.Where(character => character >= ' ' && character != '\u007F')]);
}
