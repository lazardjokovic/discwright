namespace DiscWright.Core;

/// <summary>
/// The kinds of file the picker offers for what plays behind the menu.
/// </summary>
/// <remarks>
/// <para>
/// The menu plays its tune with the Windows Media Player control, so this
/// is a list of what that control plays, and each kind was measured rather
/// than assumed: handed the file under cscript, muted, the control reports
/// that it is playing and its position moves.
/// </para>
/// <para>
/// <strong>FLAC since 2.0.5</strong>, asked for on discwright#98, 6 November:
/// "I tried to add an audio track to the menu and it didn't show in DW,
/// before I eventually realised it was flac." Measured on Windows 11 on
/// 2026-11-19: playing, position 2.3 seconds after 2.5. Windows 10 and 11
/// carry the decoder. Windows 7 and 8 do not, and there the menu is silent
/// and everything else about it works, which is what it does on any machine
/// that cannot play the tune it was given.
/// </para>
/// <para>
/// <strong>Not OGG.</strong> Measured the same day: the control puts up a
/// box of its own about the file and plays nothing.
/// </para>
/// <para>
/// The picker also offers Anything, so this narrows what is shown first
/// and forbids nothing.
/// </para>
/// </remarks>
public static class MusicForTheMenu
{
    /// <summary>The extensions offered, the commonest first.</summary>
    public static IReadOnlyList<string> Kinds { get; } = [".mp3", ".flac", ".wav", ".wma", ".m4a"];

    /// <summary>The picker's filter: those kinds, and then anything at all.</summary>
    public static string ForThePicker { get; } =
        "Music|" + string.Join(';', Kinds.Select(kind => "*" + kind)) + "|Anything|*.*";
}
