namespace DiscWright.Core;

/// <summary>
/// Everything in a folder, already read, so that deciding what the folder is
/// needs no disk at all.
/// </summary>
/// <remarks>
/// Recursive, with each file's path relative to the folder. One walk, handed to
/// every recogniser in turn. Walking a game folder twice costs real seconds on
/// a cold cache and a 20 GB game, and more importantly it means a recogniser
/// can be tested by writing a list rather than by making files.
/// </remarks>
public sealed record FolderListing(string Path, IReadOnlyList<ListedFile> Files)
{
    /// <summary>
    /// The files sitting directly in the folder, with nothing between them and
    /// it. A GOG download is recognised from these: an installer buried three
    /// folders down belongs to something else.
    /// </summary>
    public IEnumerable<ListedFile> AtTheTop =>
        Files.Where(file => !file.RelativePath.Contains('/', StringComparison.Ordinal)
                            && !file.RelativePath.Contains('\\', StringComparison.Ordinal));

    /// <summary>Total size of everything in the folder.</summary>
    public long Bytes => Files.Sum(file => file.Bytes);
}

/// <summary>One file, as the listing saw it.</summary>
/// <param name="RelativePath">
/// Where it sits inside the folder. The file's own name when it is at the top.
/// </param>
/// <param name="Bytes">How big it is.</param>
public sealed record ListedFile(string RelativePath, long Bytes)
{
    /// <summary>The file's name, without the folders in front of it.</summary>
    public string Name => RelativePath[(RelativePath.LastIndexOfAny(['/', '\\']) + 1)..];

    /// <summary>The name with its extension taken off.</summary>
    public string NameWithoutExtension
    {
        get
        {
            string name = Name;
            int dot = name.LastIndexOf('.');

            return dot <= 0 ? name : name[..dot];
        }
    }

    /// <summary>The extension, lowercased, with its dot. Empty when there is none.</summary>
    public string Extension
    {
        get
        {
            string name = Name;
            int dot = name.LastIndexOf('.');

            return dot <= 0 ? string.Empty : name[dot..].ToLowerInvariant();
        }
    }
}
