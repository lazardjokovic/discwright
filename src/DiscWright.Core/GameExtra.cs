namespace DiscWright.Core;

/// <summary>
/// Something of one game's own to read or look at: its manual, or one of its
/// extras.
/// </summary>
/// <remarks>
/// <para>
/// Asked for twice, by xniwo on discwright#98 and by somebody on Reddit, and
/// approved by the owner on 2026-11-13. Before it, the only manual and extras
/// were the disc's, so on a disc of nine games every game's screen offered the
/// same Manual button and none of them had its own.
/// </para>
/// <para>
/// <strong>Unlike an add-on, it installs nothing.</strong> A manual is read
/// and an artbook is looked at, so their buttons work before the game is
/// installed, and a folder is as welcome as a file: an artbook is usually a
/// folder of pictures.
/// </para>
/// <para>
/// It rides every disc of its game's set, the rule the owner chose for the
/// disc's own extras in #167 after Manual on disc 2 said "That would not
/// start". So the planner counts it against every disc and the build copies it
/// onto every one.
/// </para>
/// </remarks>
/// <param name="Path">Where it is now: a file, or a folder taken whole.</param>
/// <param name="Kind">Whether it is the game's manual or one of its extras.</param>
public sealed record GameExtra(string Path, GameExtraKind Kind)
{
    /// <summary>Where a game's manuals sit, inside the game's own folder.</summary>
    public const string ManualFolder = "Manual";

    /// <summary>Where a game's extras sit, inside the game's own folder.</summary>
    public const string ExtrasFolder = "Extras";

    /// <summary>The folder it goes in, beside the game's Add-ons.</summary>
    public string Folder => Kind == GameExtraKind.Manual ? ManualFolder : ExtrasFolder;

    /// <summary>
    /// Its own name, kept. Somebody looking for the manual in ten years is
    /// looking for whatever it was called.
    /// </summary>
    public string Name =>
        System.IO.Path.GetFileName(Path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    /// <summary>
    /// Where it lands on the disc, given the folder its game landed in, which
    /// is empty for a game that has the disc to itself.
    /// </summary>
    public string OnTheDisc(string gameFolder) =>
        gameFolder.Length == 0
            ? System.IO.Path.Combine(Folder, Name)
            : System.IO.Path.Combine(gameFolder, Folder, Name);
}

/// <summary>What a game's own thing is.</summary>
public enum GameExtraKind
{
    /// <summary>Its manual, which the menu's Manual button opens.</summary>
    Manual,

    /// <summary>One of its extras, which the menu's Extras button shows.</summary>
    Extra
}
