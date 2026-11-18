namespace DiscWright.Core;

/// <summary>
/// Turns what a project file says back into a disc that could be built.
/// </summary>
/// <remarks>
/// <para>
/// The file holds paths. The games behind them are read again, which is the
/// only way to know they are still there, still the same size, and still the
/// same game. <strong>It is also how a project repairs itself</strong>: a name
/// that was wrong in an older file is replaced by the one the folder gives now.
/// </para>
/// <para>
/// A folder that has gone is not a failure. It is the commonest thing that
/// happens to a project a year later, and the answer is to say which ones and
/// open the rest, rather than refuse the whole disc over one game somebody
/// moved.
/// </para>
/// </remarks>
public static class ReopenAProject
{
    public static Reopened From(
        OpenedProject opened,
        GameFolderReader reader,
        IFileSystem files,
        Func<string, long> howBigIs)
    {
        List<GameFolder> games = [];
        List<AddOn> addOns = [];
        List<string> gone = [];
        List<Refusal> trouble = [];

        // Which game each entry became, so that an add-on can be pointed back
        // at it. The file counts entries, including the add-ons themselves;
        // a disc counts games. They are not the same number as soon as an
        // add-on sits anywhere but last.
        Dictionary<int, int> whichGame = [];

        for (int at = 0; at < opened.Entries.Count; at++)
        {
            ProjectEntry entry = opened.Entries[at];

            if (entry.IsAnAddOn)
            {
                continue;
            }

            if (!files.FolderExists(entry.Folder))
            {
                gone.Add(entry.Folder);
                continue;
            }

            switch (reader.Read(files.List(entry.Folder)))
            {
                case Outcome<GameFolder>.Ok(GameFolder found):
                    whichGame[at] = games.Count;

                    // The name the file remembers wins, because somebody may
                    // have typed it. The match name does not: an older file
                    // has none, and the one found by reading the folder is the
                    // registered one.
                    // Its own manual and extras come back if they are still
                    // where they were, and are named as gone if not, the same
                    // as a game folder that moved.
                    List<GameExtra> own = [];

                    foreach (GameExtra each in entry.OwnOrNone)
                    {
                        if (files.FolderExists(each.Path))
                        {
                            FolderListing inside = files.List(each.Path);

                            own.Add(each with
                            {
                                Bytes = inside.Files.Sum(file => file.Bytes),
                                Files = inside.Files.Count
                            });
                        }
                        else if (files.FileExists(each.Path))
                        {
                            own.Add(each with { Bytes = howBigIs(each.Path) });
                        }
                        else
                        {
                            gone.Add(each.Path);
                        }
                    }

                    found = found with { Own = own.Count > 0 ? own : null };

                    games.Add(entry.Name is string named && named.Length > 0
                        ? found with { Name = named }
                        : found);
                    break;

                case Outcome<GameFolder>.No refused:
                    trouble.Add(refused.Refusal);
                    break;
            }
        }

        foreach (ProjectEntry entry in opened.Entries.Where(each => each.IsAnAddOn))
        {
            if (entry.Installer is not string installer || installer.Length == 0)
            {
                continue;
            }

            if (!whichGame.TryGetValue(entry.BelongsTo, out int belongsTo))
            {
                // The game it was attached to did not come back, so neither
                // does it. An add-on with no game is a button that does
                // nothing, which is the thing the window asks about.
                gone.Add(installer);
                continue;
            }

            addOns.Add(new AddOn(
                belongsTo,
                installer,
                entry.Name is string named && named.Length > 0 ? named : AddOn.NameFor(installer),
                howBigIs(installer),
                AddOn.PartsBeside(installer, files)));
        }

        // A project saved by 2.0.4 or older could hold an installer's parts
        // as add-ons of their own, or as the game's extras, because that is
        // what its dialog offered. The installer carries them now, and a part
        // written twice is two files wanting one name in Add-ons.
        addOns = [.. addOns.Where(addOn => !addOns.Any(other =>
            other.BelongsTo == addOn.BelongsTo && Carries(other, addOn.Path)))];

        // And one that is a file of its own game is the game. 2.0.4 could
        // take a DLC for the game and save the real installer as its add-on;
        // the folder read again finds the real one, and attaching it to
        // itself would put the game on its own disc twice.
        addOns = [.. addOns.Where(addOn => !games[addOn.BelongsTo].Files.Any(file => string.Equals(
            System.IO.Path.Combine(games[addOn.BelongsTo].Path, file.RelativePath),
            addOn.Path,
            StringComparison.OrdinalIgnoreCase)))];

        for (int at = 0; at < games.Count; at++)
        {
            if (games[at].Own is not { } own)
            {
                continue;
            }

            GameExtra[] kept =
            [
                .. own.Where(each => !addOns.Any(addOn => addOn.BelongsTo == at && Carries(addOn, each.Path)))
            ];

            if (kept.Length != own.Count)
            {
                games[at] = games[at] with { Own = kept.Length > 0 ? kept : null };
            }
        }

        DiscProject project = new(
            opened.Label,
            games,
            opened.Options,
            Artwork: new DiscArtwork(
                opened.Artwork.IconPath,
                opened.Artwork.BackgroundPath,
                opened.Artwork.BackgroundAsItIs,
                opened.Artwork.CoverPath,
                opened.Artwork.DiscFacePath,
                opened.Artwork.BackgroundColour),
            Extras: new DiscExtras(
                opened.ManualPath,
                opened.ExtrasFolderPath,
                opened.LooseExtras,
                opened.MusicPath),
            AddOns: addOns);

        return new Reopened(project, opened.Medium, opened.OutputFolder, gone, trouble);
    }

    private static bool Carries(AddOn addOn, string path) =>
        addOn.PartsOrNone.Any(part => string.Equals(part.Path, path, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A project file, read and looked up again.</summary>
/// <param name="Project">What could be built from it now.</param>
/// <param name="Medium">The size of disc it was made for, or none.</param>
/// <param name="OutputFolder">Where it was built last time.</param>
/// <param name="Gone">What it named that is no longer there.</param>
/// <param name="Trouble">What is still there and could not be read.</param>
public sealed record Reopened(
    DiscProject Project,
    Medium? Medium,
    string? OutputFolder,
    IReadOnlyList<string> Gone,
    IReadOnlyList<Refusal> Trouble)
{
    /// <summary>Whether everything the file described came back.</summary>
    public bool WholeAgain => Gone.Count == 0 && Trouble.Count == 0;

    /// <summary>Whether there is anything left to build.</summary>
    public bool AnythingLeft => Project.Games.Count > 0;

    /// <summary>What to say about it, in one line.</summary>
    public string InWords
    {
        get
        {
            if (WholeAgain)
            {
                return Project.Games.Count == 1
                    ? "Opened, with its game where it was left."
                    : $"Opened, with all {Project.Games.Count} games where they were left.";
            }

            if (!AnythingLeft)
            {
                return Gone.Count == 1
                    ? "Nothing on that disc is where it was: " + Gone[0]
                    : $"None of the {Gone.Count} things on that disc are where they were.";
            }

            List<string> missing = [];

            if (Gone.Count > 0)
            {
                missing.Add($"{Gone.Count} not where {(Gone.Count == 1 ? "it was" : "they were")}");
            }

            if (Trouble.Count > 0)
            {
                missing.Add($"{Trouble.Count} that could not be read");
            }

            return $"Opened with {Project.Games.Count} game(s), " + string.Join(" and ", missing) + ".";
        }
    }
}
