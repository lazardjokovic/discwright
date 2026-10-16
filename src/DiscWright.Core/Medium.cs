namespace DiscWright.Core;

/// <summary>
/// A size of disc to aim at.
/// </summary>
/// <remarks>
/// This is a target, not a disc in a drive. Whether the disc somebody actually
/// put in the tray is blank, or rewritable, or already holds something, is read
/// off the drive at burn time and belongs to that reading, which is why neither
/// is a field here. The window asks "how big a disc are you using"; the drive
/// answers "here is what is in me".
/// </remarks>
/// <param name="Key">
/// Short and stable, because it is written into saved project files and has to
/// read back years later: CD, DVD5, DVD9, BD25, BD50, BDXL.
/// </param>
/// <param name="Name">What the window shows, with the size as it is sold.</param>
/// <param name="Capacity">
/// Usable bytes, which is less than the number on the box. See <see cref="Media"/>.
/// </param>
public sealed record Medium(string Key, string Name, long Capacity);

/// <summary>
/// One row of the list of disc sizes, as the window shows it.
/// </summary>
/// <remarks>
/// A size and the words for it, kept together. The words are not part of the
/// <see cref="Medium"/> because they depend on what else has been asked for:
/// the same disc reads differently when a file is going to be split across
/// two of them.
/// </remarks>
/// <param name="Medium">The size itself, which is what gets saved.</param>
/// <param name="InWords">What the list shows for it.</param>
public sealed record ASizeToAimAt(Medium Medium, string InWords)
{
    /// <summary>What a list shows when it is given no template.</summary>
    public override string ToString() => InWords;
}
