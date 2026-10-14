namespace DiscWright.Core;

/// <summary>
/// Everything on a disc that is not the game and not the artwork.
/// </summary>
/// <param name="ManualPath">
/// The manual, which lands in <c>Extras</c> and gets a button on the menu.
/// </param>
/// <param name="ExtrasFolderPath">
/// A folder whose <em>contents</em> go into <c>Extras</c>. Its own name is not
/// kept, because somebody picking a folder called "stuff for the disc" did not
/// mean to put a folder called "stuff for the disc" on it.
/// </param>
/// <param name="LooseItems">
/// Files and folders that go at the root of the disc exactly as they are: a
/// community patch, a fan translation, somebody's save from 2011.
/// </param>
/// <param name="MusicPath">
/// Something the menu plays. Unlike everything else here it rides
/// <strong>every</strong> disc of a set, because every disc has its own menu,
/// which is also why it counts as overhead rather than as payload.
/// </param>
public sealed record DiscExtras(
    string? ManualPath = null,
    string? ExtrasFolderPath = null,
    IReadOnlyList<string>? LooseItems = null,
    string? MusicPath = null)
{
    /// <summary>The loose items, or none.</summary>
    public IReadOnlyList<string> Loose => LooseItems ?? [];

    /// <summary>
    /// Whether anything rides one disc of the set. Music is not counted: it
    /// rides all of them.
    /// </summary>
    public bool IsAnythingOnOneDisc =>
        ManualPath is not null || ExtrasFolderPath is not null || Loose.Count > 0;
}
