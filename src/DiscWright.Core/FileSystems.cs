namespace DiscWright.Core;

/// <summary>
/// What a filesystem written to a disc can carry.
/// </summary>
/// <remarks>
/// A ceiling here belongs to the filesystem and never to the medium. A 6 GB
/// file is impossible on ISO9660 and perfectly fine on UDF, on the same disc,
/// burned in the same drive. That is why this is its own type and not a field
/// on <see cref="Medium"/>, and it is the reason the "readable on Windows XP
/// and older" option can be dead while everything else still works.
/// </remarks>
public static class FileSystems
{
    /// <summary>
    /// The largest single file ISO9660 can describe.
    /// </summary>
    /// <remarks>
    /// Measured rather than assumed. The comment in v1 once claimed the
    /// opposite and a real build died on it after copying 7.79 GB;
    /// tools/Measure-IsoFileCeiling.ps1 in that repository measures it again on
    /// any machine. This is the figure the shipped app compares against.
    /// </remarks>
    public const long Iso9660MaxFile = 2_147_483_648;

    /// <summary>
    /// The largest single file FAT32 can describe, one byte under 4 GiB.
    /// </summary>
    /// <remarks>
    /// FAT32 keeps a file's size in 32 bits, so the largest it can describe is
    /// 2^32 - 1. That is one byte short of twice the ISO9660 ceiling, and it is
    /// the reason GOG splits its installers at 4,294,967,294 bytes: one byte
    /// under this. So every part of a multi-part GOG download clears FAT32 and
    /// none of them clears ISO9660, which means no multi-part GOG download can
    /// carry the older filesystems at all. The option is for discs whose
    /// biggest single file is 2 GiB or less.
    /// </remarks>
    public const long Fat32MaxFile = 4_294_967_295;

    /// <summary>
    /// Where GOG cuts a download into .bin parts. Not a limit of ours, and kept
    /// here because it explains the two above and turns up in every size sum.
    /// </summary>
    public const long GogPartSize = 4_294_967_294;
}
