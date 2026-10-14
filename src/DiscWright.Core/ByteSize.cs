using System.Globalization;

namespace DiscWright.Core;

/// <summary>
/// How a number of bytes is written wherever a person will read it.
/// </summary>
/// <remarks>
/// <para>
/// One formatter, used by every refusal figure and every size label, because
/// the alternative is sizes reading three different ways in three places and
/// nobody able to compare them.
/// </para>
/// <para>
/// <strong>Binary units labelled the way Explorer labels them.</strong> 1 KB is
/// 1024 bytes and it is called KB, not KiB, because every size somebody will
/// compare this against came out of Explorer, which does exactly that. Being
/// technically correct here would mean our "4.37 GiB" sitting beside
/// Explorer's "4.37 GB" for the same file, and a person wondering which is
/// lying.
/// </para>
/// <para>
/// The number a disc is <em>sold</em> as is a different thing and never comes
/// from here: "DVD5 4.7 GB" is the product's name and lives in
/// <see cref="Medium.Name"/>. So a refusal can say a DVD5 holds 4.37 GB while
/// the disc in the list is called 4.7 GB, and both are right.
/// </para>
/// <para>
/// Formatted in the current culture, so this reads 4,37 GB on a Serbian
/// machine. That is also why <c>InvariantGlobalization</c> is deliberately off
/// for the whole solution.
/// </para>
/// </remarks>
public static class ByteSize
{
    private const long Kilobyte = 1024;
    private const long Megabyte = Kilobyte * 1024;
    private const long Gigabyte = Megabyte * 1024;

    /// <summary>
    /// Writes a size for a person: "0 bytes", "817 bytes", "412.5 KB",
    /// "1.2 MB", "4.37 GB".
    /// </summary>
    /// <remarks>
    /// Gigabytes get two decimals and everything else gets one. Not a
    /// flourish: disc capacities differ in the hundredths, so 4.37 GB and
    /// 4.7 GB are two different discs and one decimal would print both as 4.4
    /// and 4.7, or worse, both as 4.4.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A negative size is a mistake in the caller rather than something to
    /// print, so it is thrown rather than formatted.
    /// </exception>
    public static string Describe(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        CultureInfo culture = CultureInfo.CurrentCulture;

        if (bytes < Kilobyte)
        {
            return bytes == 1
                ? "1 byte"
                : string.Create(culture, $"{bytes:0} bytes");
        }

        if (bytes < Megabyte)
        {
            return string.Create(culture, $"{(double)bytes / Kilobyte:0.#} KB");
        }

        if (bytes < Gigabyte)
        {
            return string.Create(culture, $"{(double)bytes / Megabyte:0.#} MB");
        }

        return string.Create(culture, $"{(double)bytes / Gigabyte:0.##} GB");
    }
}
