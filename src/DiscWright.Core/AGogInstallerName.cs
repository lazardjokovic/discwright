using System.Text.RegularExpressions;

namespace DiscWright.Core;

/// <summary>
/// What the name of a GOG installer says: which product it is, which version,
/// and which build.
/// </summary>
/// <remarks>
/// <para>
/// <c>setup_metro_exodus_-_sams_story_3.0.8.39_(64bit)_(62413).exe</c> is the
/// product <c>metro_exodus_-_sams_story</c>, version <c>3.0.8.39</c>, build
/// <c>62413</c>. GOG writes every installer this way: the product, the
/// version, then anything in brackets, and last of all the build in brackets
/// on its own.
/// </para>
/// <para>
/// Read from the name and never from inside the file, for the reason an
/// add-on's name is: every DLC reports the product name of the game it
/// installs onto, so all of them say "Metro Exodus" and the file cannot tell
/// the game from what goes on it.
/// </para>
/// <para>
/// <strong>Measured on a real download on 2026-11-20</strong>, Metro Exodus
/// Gold as GOG Galaxy saves it: two editions of the game and four DLC, all
/// in one folder, 45 files. Everything of the first edition carries
/// <c>1.0.8.39_(64bit)_(62346)</c> and everything of the Enhanced Edition
/// <c>3.0.8.39_(64bit)_(62413)</c>. Nothing else on the files says which DLC
/// goes with which game.
/// </para>
/// </remarks>
/// <param name="Product">What it is, as GOG spells it, in small letters.</param>
/// <param name="Version">Its version, or nothing when the name has none.</param>
/// <param name="Build">GOG's build number, or nothing when the name has none.</param>
public sealed partial record AGogInstallerName(string Product, string? Version, string? Build)
{
    /// <summary>
    /// What a file's name says, or nothing for a file that is not named as
    /// GOG names an installer.
    /// </summary>
    public static AGogInstallerName? Of(string fileName)
    {
        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string bare = System.IO.Path.GetFileNameWithoutExtension(fileName);

        if (!bare.StartsWith("setup_", StringComparison.OrdinalIgnoreCase) || bare.Length == "setup_".Length)
        {
            return null;
        }

        string rest = bare["setup_".Length..].ToLowerInvariant();

        // The build is the last thing in the name, in brackets, all figures.
        string? build = null;
        Match last = TheBuildAtTheEnd().Match(rest);

        if (last.Success)
        {
            build = last.Groups[1].Value;
            rest = rest[..last.Index];
        }

        // Everything else in brackets is a language or an architecture, and
        // whatever follows the first of them is GOG's own business.
        int bracket = rest.IndexOf("_(", StringComparison.Ordinal);

        if (bracket >= 0)
        {
            rest = rest[..bracket];
        }

        // What is left is the product, an underscore and the version. A name
        // with no version is all product.
        int cut = rest.LastIndexOf('_');
        string? version = null;

        if (cut > 0 && cut < rest.Length - 1 && LooksLikeAVersion().IsMatch(rest[(cut + 1)..]))
        {
            version = rest[(cut + 1)..];
            rest = rest[..cut];
        }

        return rest.Length == 0 ? null : new AGogInstallerName(rest, version, build);
    }

    /// <summary>
    /// Whether this installs onto that: its product is that product with
    /// more after it, as <c>metro_exodus_-_sams_story</c> is
    /// <c>metro_exodus</c> with more.
    /// </summary>
    /// <remarks>
    /// How GOG names a DLC, and the only thing that tells a DLC from its game
    /// when the DLC is the bigger of the two. A sequel kept in the same
    /// folder is named this way too, <c>gothic</c> and <c>gothic_2</c>, and
    /// that folder was two games taken for one before this and still is.
    /// </remarks>
    public bool IsBuiltOn(AGogInstallerName other) =>
        Product.Length > other.Product.Length
        && Product.StartsWith(other.Product + "_", StringComparison.Ordinal);

    /// <summary>
    /// Whether the two are the same product in two builds, which is two
    /// versions of one thing.
    /// </summary>
    public bool IsAnotherBuildOf(AGogInstallerName other) =>
        string.Equals(Product, other.Product, StringComparison.Ordinal)
        && Build is not null
        && other.Build is not null
        && !string.Equals(Build, other.Build, StringComparison.Ordinal);

    /// <summary>The version, or failing that the build, to tell somebody which one is meant.</summary>
    public string Which => Version ?? (Build is null ? "another one" : "build " + Build);

    [GeneratedRegex(@"_\(([0-9]+)\)$", RegexOptions.CultureInvariant)]
    private static partial Regex TheBuildAtTheEnd();

    [GeneratedRegex(@"^v?[0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex LooksLikeAVersion();
}
