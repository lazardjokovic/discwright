using System.Text.RegularExpressions;

namespace DiscWright.Core;

/// <summary>
/// Something installed after a game, and only after it: a patch, a DLC, a
/// soundtrack, somebody's overhaul mod.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Picked as a file, not a folder.</strong> A patch or a DLC usually
/// sits in the same folder as the game it belongs to, so pointing at the folder
/// would just find the game again.
/// </para>
/// <para>
/// Any executable is accepted. The <c>setup_</c> rule exists to identify a GOG
/// <em>game folder</em>, and an add-on's identity comes from the game it was
/// attached to rather than from its file name. That is what lets a mod or an
/// overhaul, which is never called <c>setup_</c> anything, go on the disc at
/// all.
/// </para>
/// </remarks>
/// <param name="BelongsTo">
/// Which game in the project it sits under, from zero. An add-on with no game
/// is not an add-on, it is a game of its own.
/// </param>
/// <param name="Path">Where the installer is now.</param>
/// <param name="Name">What its button says.</param>
/// <param name="Bytes">How big it is.</param>
public sealed partial record AddOn(int BelongsTo, string Path, string Name, long Bytes)
{
    /// <summary>Where add-ons sit, beside the game they belong to.</summary>
    public const string Folder = "Add-ons";

    /// <summary>
    /// What to call an add-on, worked out from its file name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Never from the product name inside the file.</strong> Every GOG
    /// patch reports the product name of the game it patches, so all four
    /// Hollow Knight patches call themselves "Hollow Knight", and a disc built
    /// from them would offer four identical buttons.
    /// </para>
    /// <para>
    /// A patch is named after the version it moves <em>to</em>, because that is
    /// what tells two patches apart, and it goes at the front where a button
    /// will not clip it.
    /// </para>
    /// </remarks>
    public static string NameFor(string fileName)
    {
        string raw = System.IO.Path.GetFileNameWithoutExtension(fileName);
        string name = raw;

        Match patch = PatchToVersion().Match(name);

        if (patch.Success)
        {
            name = "Update " + patch.Groups[1].Value;
        }
        else if (name.StartsWith("setup_", StringComparison.OrdinalIgnoreCase))
        {
            name = name["setup_".Length..];
        }

        name = RunsOfSpace().Replace(name.Replace('_', ' '), " ").Trim();

        // A file name that is nothing but underscores leaves an empty label,
        // and a button with no text is a button nobody can identify.
        if (name.Length == 0)
        {
            name = raw.Trim();
        }

        return name.Length == 0 ? "Add-on" : name;
    }

    [GeneratedRegex(@"^patch_.+_to_(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PatchToVersion();

    [GeneratedRegex(@"\s+")]
    private static partial Regex RunsOfSpace();
}
