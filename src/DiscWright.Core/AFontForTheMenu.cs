using System.Buffers.Binary;
using System.IO.Compression;

namespace DiscWright.Core;

/// <summary>
/// A font file somebody chose for the disc's menu, made into the one form
/// the menu will load from a disc on any machine.
/// </summary>
/// <remarks>
/// <para>
/// The owner, 2026-11-20, after the list of six faces: "would it work on
/// every Windows machine if the user uploads their own font?" Measured the
/// same day in a real menu window, which is Internet Explorer's engine in
/// its version 9 manner: a TrueType file named in the page's style loaded
/// only when the font's own embedding flag says it may be installed. One
/// flagged restricted or editable was passed over without a word, and the
/// menu fell back to the next face. The very same restricted font, wrapped
/// as WOFF, loaded.
/// </para>
/// <para>
/// So whatever is chosen goes onto the disc as WOFF. That is a wrapping and
/// not a conversion: the font's own tables, each squeezed, behind a header
/// that says where they are. Nothing about the letters changes, and nothing
/// here reads them. A WOFF file is taken as it is.
/// </para>
/// <para>
/// Whether somebody may put a font on a disc is between them and whoever
/// made it. The window says so where the file is chosen.
/// </para>
/// </remarks>
public static class AFontForTheMenu
{
    /// <summary>What the font is called on the disc, beside the menu's page.</summary>
    public const string FileName = "menu-font.woff";

    /// <summary>What the page's style calls it, which is no font any machine has.</summary>
    public const string FamilyOnThePage = "DiscWright Menu Font";

    /// <summary>The kinds of file the picker offers.</summary>
    public const string ForThePicker = "A font|*.ttf;*.otf;*.woff|Anything|*.*";

    private const uint Woff = 0x774F4646;      // wOFF
    private const uint TrueType = 0x00010000;
    private const uint AppleTrueType = 0x74727565; // true
    private const uint OpenTypeCff = 0x4F54544F; // OTTO
    private const uint Collection = 0x74746366; // ttcf

    /// <summary>
    /// Why these bytes cannot be the menu's font, in words somebody can act
    /// on, or nothing when they can.
    /// </summary>
    public static string? WhyNot(ReadOnlySpan<byte> file)
    {
        if (file.Length < 12)
        {
            return "It is not a font file.";
        }

        uint kind = BinaryPrimitives.ReadUInt32BigEndian(file);

        if (kind == Collection)
        {
            return "It is a collection of several fonts in one file. Choose a file that holds one.";
        }

        if (kind == Woff)
        {
            return null;
        }

        if (kind is not (TrueType or AppleTrueType or OpenTypeCff))
        {
            return "It is not a TrueType, OpenType or WOFF font.";
        }

        return TablesOf(file) is null ? "The font file is damaged: its list of tables runs past its end." : null;
    }

    /// <summary>
    /// The font as WOFF, or nothing for a file <see cref="WhyNot"/> turns
    /// away. A file that is WOFF already comes back as it is.
    /// </summary>
    public static byte[]? AsWoff(ReadOnlySpan<byte> file)
    {
        if (WhyNot(file) is not null)
        {
            return null;
        }

        if (BinaryPrimitives.ReadUInt32BigEndian(file) == Woff)
        {
            return file.ToArray();
        }

        List<(uint Tag, uint Checksum, int Offset, int Length)> tables = TablesOf(file)!;

        // Each table squeezed, and kept squeezed only when that is smaller:
        // the format says a table whose two lengths agree was stored plain.
        List<byte[]> stored = [];

        foreach ((uint _, uint _, int offset, int length) in tables)
        {
            ReadOnlySpan<byte> plain = file.Slice(offset, length);
            byte[] squeezed = Squeezed(plain);

            stored.Add(squeezed.Length < length ? squeezed : plain.ToArray());
        }

        const int Header = 44;
        const int Entry = 20;

        int at = Header + (Entry * tables.Count);
        int[] offsets = new int[tables.Count];

        for (int each = 0; each < tables.Count; each++)
        {
            offsets[each] = at;
            at += Padded(stored[each].Length);
        }

        byte[] woff = new byte[at];
        Span<byte> into = woff;

        // What the font would come to unwrapped: its own header and list,
        // then every table on a boundary of four.
        int unwrapped = 12 + (16 * tables.Count) + tables.Sum(table => Padded(table.Length));

        BinaryPrimitives.WriteUInt32BigEndian(into, Woff);
        BinaryPrimitives.WriteUInt32BigEndian(into[4..], BinaryPrimitives.ReadUInt32BigEndian(file));
        BinaryPrimitives.WriteUInt32BigEndian(into[8..], (uint)woff.Length);
        BinaryPrimitives.WriteUInt16BigEndian(into[12..], (ushort)tables.Count);
        BinaryPrimitives.WriteUInt32BigEndian(into[16..], (uint)unwrapped);

        // The version, and no metadata and no private block: all noughts,
        // which the array already is.
        BinaryPrimitives.WriteUInt16BigEndian(into[20..], 1);

        for (int each = 0; each < tables.Count; each++)
        {
            Span<byte> entry = into[(Header + (Entry * each))..];

            BinaryPrimitives.WriteUInt32BigEndian(entry, tables[each].Tag);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)offsets[each]);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)stored[each].Length);
            BinaryPrimitives.WriteUInt32BigEndian(entry[12..], (uint)tables[each].Length);
            BinaryPrimitives.WriteUInt32BigEndian(entry[16..], tables[each].Checksum);

            stored[each].CopyTo(into[offsets[each]..]);
        }

        return woff;
    }

    /// <summary>
    /// The font's tables in the order of their names, which is the order
    /// the format asks for, or nothing when the list or a table runs past
    /// the end of the file.
    /// </summary>
    private static List<(uint Tag, uint Checksum, int Offset, int Length)>? TablesOf(ReadOnlySpan<byte> file)
    {
        int count = BinaryPrimitives.ReadUInt16BigEndian(file[4..]);

        if (count == 0 || 12 + (16 * count) > file.Length)
        {
            return null;
        }

        List<(uint Tag, uint Checksum, int Offset, int Length)> tables = [];

        for (int each = 0; each < count; each++)
        {
            ReadOnlySpan<byte> entry = file[(12 + (16 * each))..];
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(entry[12..]);

            if ((ulong)offset + length > (ulong)file.Length)
            {
                return null;
            }

            tables.Add((
                BinaryPrimitives.ReadUInt32BigEndian(entry),
                BinaryPrimitives.ReadUInt32BigEndian(entry[4..]),
                (int)offset,
                (int)length));
        }

        tables.Sort((one, other) => one.Tag.CompareTo(other.Tag));

        return tables;
    }

    private static byte[] Squeezed(ReadOnlySpan<byte> plain)
    {
        using MemoryStream into = new();

        using (ZLibStream squeezing = new(into, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            squeezing.Write(plain);
        }

        return into.ToArray();
    }

    private static int Padded(int length) => (length + 3) & ~3;
}

/// <summary>Reading a font file, and making it the menu's.</summary>
/// <remarks>
/// A port, because a file is read off the machine. What is done with its
/// bytes is <see cref="AFontForTheMenu"/>'s, which a test can hold without
/// one.
/// </remarks>
public interface IFonts
{
    /// <summary>Why this file cannot be the menu's font, or nothing when it can.</summary>
    string? WhyNot(string path);

    /// <summary>Writes it as the menu's font. Nothing is written for a file that cannot be one.</summary>
    void WriteForTheMenu(string source, string destination);
}
