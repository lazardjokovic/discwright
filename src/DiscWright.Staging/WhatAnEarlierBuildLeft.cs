using DiscWright.Core;

namespace DiscWright.Staging;

/// <summary>
/// Everything earlier builds of these games left under an output folder,
/// wherever a build may have put it: at the top, where one disc or a set
/// goes, and in a folder for each game, where a disc each goes.
/// </summary>
/// <remarks>
/// <para>
/// A build writes over what it writes over and used to leave the rest. So a
/// set that came to five discs and now comes to three kept its fourth and
/// fifth, and games built a disc each and then as one set kept every game's
/// folder beside the set. The window asked "Build over the disc that is
/// already in this folder?" and said building deleted them, which was true
/// of some and not of others.
/// </para>
/// <para>
/// It mattered the day several games came to share a set, 21 November 2026,
/// which changed what every such project makes: the owner's own folder
/// would have held three games' folders from the day before and a set of
/// three discs beside them, in a folder he had already been asked about
/// once ("it was built, has only 2 discs?").
/// </para>
/// <para>
/// <strong>Only what a build of this program names</strong>: folders called
/// "disc" or "disc DN" and images named after the disc or after a game,
/// which are the only things <see cref="IRemoveADiscFolder"/> agrees to
/// delete. A folder for a game is looked into only when it is one of the
/// games on the window now.
/// </para>
/// </remarks>
/// <param name="Folders">The disc folders, as whole paths.</param>
/// <param name="Images">The images, as whole paths.</param>
/// <param name="OfOneGame">The folders made for one game's discs that held any of them.</param>
public sealed record WhatAnEarlierBuildLeft(
    IReadOnlyList<string> Folders,
    IReadOnlyList<string> Images,
    IReadOnlyList<string> OfOneGame)
{
    public bool IsThere => Folders.Count > 0 || Images.Count > 0;

    /// <summary>How many of each, as the question says it.</summary>
    public string InWords => new AnEarlierBuild(Folders, Images).InWords;

    public static WhatAnEarlierBuildLeft In(
        IFileSystem disk, string outputFolder, string label, IEnumerable<string> games)
    {
        List<string> folders = [];
        List<string> images = [];
        List<string> ofOneGame = [];

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            return new WhatAnEarlierBuildLeft(folders, images, ofOneGame);
        }

        AnEarlierBuild top = AnEarlierBuild.In(disk, outputFolder, label);

        folders.AddRange(top.Folders.Select(folder => System.IO.Path.Combine(outputFolder, folder)));
        images.AddRange(top.Images.Select(image => System.IO.Path.Combine(outputFolder, image)));

        foreach (string game in games
                     .Select(OnTheDisc.Tidied)
                     .Where(name => name.Length > 0)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string where = System.IO.Path.Combine(outputFolder, game);
            AnEarlierBuild its = AnEarlierBuild.In(disk, where, game);

            if (!its.IsThere)
            {
                continue;
            }

            ofOneGame.Add(where);
            folders.AddRange(its.Folders.Select(folder => System.IO.Path.Combine(where, folder)));
            images.AddRange(its.Images.Select(image => System.IO.Path.Combine(where, image)));
        }

        return new WhatAnEarlierBuildLeft(folders, images, ofOneGame);
    }

    /// <summary>
    /// What of it a build did not write over, given the disc folders and
    /// the images that build made. Those are the new build now, and the
    /// rest is what it left behind.
    /// </summary>
    public WhatAnEarlierBuildLeft NotAmong(IEnumerable<string> folders, IEnumerable<string> images)
    {
        HashSet<string> made = new(folders.Select(Tidied), StringComparer.OrdinalIgnoreCase);
        HashSet<string> written = new(images.Select(Tidied), StringComparer.OrdinalIgnoreCase);

        return new WhatAnEarlierBuildLeft(
            [.. Folders.Where(folder => !made.Contains(Tidied(folder)))],
            [.. Images.Where(image => !written.Contains(Tidied(image)))],
            OfOneGame);
    }

    private static string Tidied(string path) =>
        path.Replace('/', System.IO.Path.DirectorySeparatorChar).TrimEnd(System.IO.Path.DirectorySeparatorChar);
}
