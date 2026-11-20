using DiscWright.Core;

namespace DiscWright.Staging;

/// <summary>
/// What a folder already holds that building a disc there would replace.
/// </summary>
/// <remarks>
/// <para>
/// A build empties its <c>disc</c> folder and writes its image over one of
/// the same name, and it used to do both without a word. Somebody who built
/// a disc on Monday, changed the list on Tuesday and pressed the button again
/// lost Monday's disc, which may be the one they had not burned yet. Raised
/// on discwright#98 on 15 November 2026 and settled by the owner on the
/// 20th: always ask first.
/// </para>
/// <para>
/// Only what this build would really touch. A folder of somebody's other
/// images is not an earlier build of this disc, and asking about those would
/// teach people to press Enter at a question that sometimes matters.
/// </para>
/// <para>
/// Asked of the folder itself and never of what is under it. A disc folder
/// can hold sixty gigabytes in thousands of files, and counting them to word
/// a question would be the slowest thing the button did.
/// </para>
/// </remarks>
/// <param name="Folders">The disc folders there, by name: <c>disc</c>, <c>disc D1</c>.</param>
/// <param name="Images">The images there that carry this disc's name, by name.</param>
public sealed record AnEarlierBuild(IReadOnlyList<string> Folders, IReadOnlyList<string> Images)
{
    /// <summary>
    /// How many discs of a set are looked for. More than any set anybody has
    /// built, and still only a handful of questions to the disk.
    /// </summary>
    private const int TheMostDiscsLookedFor = 40;

    /// <summary>Whether there is anything there to replace.</summary>
    public bool IsThere => Folders.Count > 0 || Images.Count > 0;

    /// <summary>
    /// What is there, in words: "a disc folder and 2 images".
    /// </summary>
    public string InWords
    {
        get
        {
            string? folders = Folders.Count switch
            {
                0 => null,
                1 => "a disc folder",
                _ => $"{Folders.Count} disc folders",
            };

            string? images = Images.Count switch
            {
                0 => null,
                1 => "its image",
                _ => $"{Images.Count} images",
            };

            return (folders, images) switch
            {
                (null, null) => "nothing",
                (null, _) => Images.Count == 1 ? "a disc image" : images!,
                (_, null) => folders,
                _ => $"{folders} and {images}",
            };
        }
    }

    /// <summary>
    /// What is in a folder from an earlier build of a disc with this label.
    /// </summary>
    public static AnEarlierBuild In(IFileSystem disk, string outputFolder, string label)
    {
        if (string.IsNullOrWhiteSpace(outputFolder) || !disk.FolderExists(outputFolder))
        {
            return new AnEarlierBuild([], []);
        }

        List<string> folders = [];

        if (disk.FolderExists(System.IO.Path.Combine(outputFolder, "disc")))
        {
            folders.Add("disc");
        }

        // A set is numbered from 1 with no gap, so the first one missing is
        // the end of it.
        for (int number = 1; number <= TheMostDiscsLookedFor; number++)
        {
            string name = $"disc D{number}";

            if (!disk.FolderExists(System.IO.Path.Combine(outputFolder, name)))
            {
                break;
            }

            folders.Add(name);
        }

        List<string> images =
        [
            .. disk.ListHere(outputFolder).Files
                .Select(file => file.Name)
                .Where(name => TheImageOf.IsOneOfADiscCalled(label, name))
                .Order(StringComparer.OrdinalIgnoreCase)
        ];

        return new AnEarlierBuild(folders, images);
    }
}
