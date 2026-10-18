using System.Security.Cryptography;
using System.Text;

namespace DiscWright.Core;

/// <summary>
/// What to call the folder a preview is written into, so that previewing the
/// same project twice writes to the same place twice.
/// </summary>
/// <remarks>
/// <para>
/// Previews used to go into a folder stamped with the time, which meant a new
/// one every press and none of them ever removed. Twenty eight of them, 14 MB,
/// had collected in three days on the machine this was written on, and nothing
/// in the program would ever have tidied them.
/// </para>
/// <para>
/// A name has two halves and needs both. The readable half is the label, so
/// somebody who opens their temporary folder can see what these are. The other
/// half is a fingerprint of the thing that makes it <em>this</em> project
/// rather than another one with the same name, because two games both called
/// Setup sharing a preview folder would be worse than the mess this replaces.
/// </para>
/// <para>
/// <strong>The fingerprint is not a secret and does not need to be hard to
/// reverse.</strong> SHA-256 is used because it is the hash already in this
/// program, and four bytes of it are plenty to keep two folders on one PC
/// apart.
/// </para>
/// </remarks>
public static class WhereAPreviewGoes
{
    /// <summary>
    /// The one folder every preview goes inside, under the temporary
    /// folder.
    /// </summary>
    /// <remarks>
    /// Named here rather than where it is used, because two places need to
    /// agree about it and they are in different projects: the window builds
    /// the path, and the writer will only empty a folder it can see is one
    /// of ours.
    /// </remarks>
    public const string TheFolderTheyAllGoIn = "DiscWright preview";

    /// <summary>Enough to tell any two projects on one machine apart.</summary>
    private const int HexDigits = 8;

    /// <summary>
    /// How much of the label to keep. Long enough to recognise, short enough
    /// that the whole path stays well inside what Windows will open.
    /// </summary>
    private const int LongestReadablePart = 40;

    /// <summary>When there is nothing to read, something rather than blank.</summary>
    private const string Unnamed = "a disc";

    /// <summary>
    /// The folder name for this project's preview.
    /// </summary>
    /// <param name="label">What the disc is called, for somebody to read.</param>
    /// <param name="outputFolder">
    /// Where the project builds to, which is the best answer to which project
    /// this is, and is empty until somebody chooses one.
    /// </param>
    /// <param name="firstGame">
    /// The first game's folder, used when there is no output folder yet. A
    /// preview can be asked for before anywhere to build has been chosen, so
    /// this cannot rely on there being one.
    /// </param>
    public static string For(string? label, string? outputFolder, string? firstGame)
    {
        string whatMakesItThis =
            Something(outputFolder) ?? Something(firstGame) ?? Something(label) ?? Unnamed;

        return Readable(label) + "-" + Fingerprint(whatMakesItThis);
    }

    /// <summary>
    /// The whole path this project's preview goes to, under the
    /// temporary folder given.
    /// </summary>
    public static string Under(string temporaryFolder, string? label, string? outputFolder, string? firstGame) =>
        System.IO.Path.Combine(temporaryFolder, TheFolderTheyAllGoIn, For(label, outputFolder, firstGame));

    private static string? Something(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The label, cut down to what may safely be a folder name.
    /// </summary>
    /// <remarks>
    /// Trailing dots and spaces go as well as the characters Windows refuses
    /// outright: a folder named with one is created and then cannot be opened
    /// by the name it was asked for. A reserved name like CON is no danger
    /// here, because the fingerprint is always on the end of it.
    /// </remarks>
    private static string Readable(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return Unnamed;
        }

        StringBuilder kept = new();

        foreach (char each in label.Trim())
        {
            if (kept.Length == LongestReadablePart)
            {
                break;
            }

            kept.Append(char.IsLetterOrDigit(each) || each is ' ' or '-' or '_' ? each : ' ');
        }

        string name = kept.ToString().Trim().TrimEnd('.');

        return name.Length == 0 ? Unnamed : name;
    }

    private static string Fingerprint(string of)
    {
        byte[] all = SHA256.HashData(Encoding.UTF8.GetBytes(of.ToLowerInvariant()));

        return Convert.ToHexString(all)[..HexDigits].ToLowerInvariant();
    }
}
