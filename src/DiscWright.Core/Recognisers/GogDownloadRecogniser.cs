using System.Text.RegularExpressions;

namespace DiscWright.Core.Recognisers;

/// <summary>
/// A GOG download: one <c>setup_*.exe</c> at the top of the folder, and the
/// numbered <c>.bin</c> parts that belong to it.
/// </summary>
public sealed partial class GogDownloadRecogniser(IFileFacts facts) : IGameFolderRecogniser
{
    public string Describes => "a GOG download";

    public Outcome<GameFolder>? Recognise(FolderListing folder)
    {
        List<ListedFile> installers =
        [
            .. folder.AtTheTop
                .Where(file => file.Extension == ".exe"
                               && file.Name.StartsWith("setup_", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.Bytes)
        ];

        if (installers.Count == 0)
        {
            // Two folders in a project look alike and sit beside each other: the
            // GOG download, and the folder DiscWright writes discs into. The
            // second holds a disc subfolder with the installer one level down.
            // This is claimed rather than passed on, because the next recogniser
            // would accept it as an ordinary folder of game files and put a disc
            // inside a disc.
            // Written out rather than as a ternary: the conversion from Refusal
            // to an Outcome cannot take a null, so a ternary with a null branch
            // makes the compiler pick the conversion and then complain about it.
            if (LooksLikeADiscWeBuilt(folder))
            {
                return Refusals.ThatIsADiscRatherThanADownload(folder);
            }

            return null;
        }

        ListedFile installer = installers[0];

        // Parts belong to ONE installer and are named "<installer>-1.bin".
        // Taking every .bin in the folder swept in the parts of a DLC or of a
        // second game stored alongside. Matched as plain strings and never as
        // wildcards, because GOG names are full of brackets and dots that a
        // pattern would read as syntax.
        string stem = installer.NameWithoutExtension + "-";

        List<ListedFile> parts =
        [
            .. folder.AtTheTop
                .Where(file => file.Extension == ".bin"
                               && file.Name.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
        ];

        string name = NameOf(folder, installer);

        return new GameFolder(
            folder.Path,
            GameKind.GogInstaller,
            name,
            name,
            installer,
            [installer, .. parts],
            [],
            WarningAbout(installers, installer, parts));
    }

    private static bool LooksLikeADiscWeBuilt(FolderListing folder) =>
        folder.Files.Any(file =>
            file.Extension == ".exe"
            && file.Name.StartsWith("setup_", StringComparison.OrdinalIgnoreCase)
            && file.RelativePath.StartsWith("disc", StringComparison.OrdinalIgnoreCase)
            && file.RelativePath.Length > 4
            && (file.RelativePath[4] == '\\' || file.RelativePath[4] == '/'));

    /// <summary>
    /// What to call it. The installer's own product name where it has one, and
    /// otherwise the file name tidied up.
    /// </summary>
    private string NameOf(FolderListing folder, ListedFile installer)
    {
        string? reported = facts.ProductNameOf(System.IO.Path.Combine(folder.Path, installer.RelativePath));

        // Inno pads its version strings with trailing spaces, and untrimmed they
        // leak into folder names: "...Edition                    Disc".
        if (!string.IsNullOrWhiteSpace(reported))
        {
            return reported.Trim();
        }

        // setup_hollow_knight_1.5.12620_(64bit)_(89718) -> Hollow Knight.
        // Cutting at the first number takes the version, the architecture and
        // the build with it. A game with a numeral in its name loses it too,
        // which is why the reported name is preferred when there is one.
        string tidied = installer.NameWithoutExtension;

        if (tidied.StartsWith("setup_", StringComparison.OrdinalIgnoreCase))
        {
            tidied = tidied["setup_".Length..];
        }

        tidied = tidied.Replace('_', ' ');
        tidied = FromTheFirstNumberOnwards().Replace(tidied, string.Empty);

        return tidied.Trim();
    }

    private static string? WarningAbout(
        List<ListedFile> installers,
        ListedFile chosen,
        List<ListedFile> parts)
    {
        // A download that stopped early leaves a gap in the numbering. Built, it
        // produces a clean looking ISO that fails only when somebody runs the
        // installer off the burned disc, by which time the disc is spent. Games
        // with no parts at all are normal; only a gap in a sequence is wrong.
        List<int> numbered =
        [
            .. parts
                .Select(part => TrailingNumber().Match(part.NameWithoutExtension))
                .Where(match => match.Success)
                .Select(match => int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .Distinct()
                .Order()
        ];

        if (numbered.Count > 0)
        {
            int[] missing = [.. Enumerable.Range(1, numbered[^1]).Except(numbered)];

            if (missing.Length > 0)
            {
                return "INCOMPLETE: installer part(s) "
                    + string.Join(", ", missing.Select(number => "-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    + " are missing, so the download looks unfinished.";
            }
        }

        // Several installers in one folder is normal, a base game beside its
        // DLC. Say which was picked, so a wrong guess is visible before a disc
        // is burned rather than after.
        return installers.Count > 1
            ? $"{installers.Count} installers in this folder, using the largest, {chosen.Name}."
            : null;
    }

    [GeneratedRegex(@"\s*\d.*$")]
    private static partial Regex FromTheFirstNumberOnwards();

    [GeneratedRegex(@"-(\d+)$")]
    private static partial Regex TrailingNumber();
}
