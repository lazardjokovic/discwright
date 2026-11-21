namespace DiscWright.Core.Staging;

/// <summary>
/// Puts each game's own manual and extras inside that game's folder, on every
/// disc.
/// </summary>
/// <remarks>
/// <para>
/// Beside the game's Add-ons, in <c>Manual</c> and <c>Extras</c>, so somebody
/// looking at the disc in Explorer years from now finds each game's things
/// with the game rather than in a heap at the root.
/// </para>
/// <para>
/// On every disc, because every disc of a set has a menu, and a Manual button
/// with no manual behind it is the promise the owner found broken on disc 2 of
/// his set (#167). The planner has already taken their room off every disc.
/// </para>
/// </remarks>
public sealed class CopyEachGamesOwnStep(IFileSystem files) : IBuildStep
{
    public string Says => "adding each game's manual and extras";

    public bool AppliesTo(BuildContext context) =>
        context.Layout.EachGamesOwnIsPlaced
            ? context.Disc.Files.Any(file => file.GoesWhole)
            : context.Project.Games.Any(game => game.OwnOrNone.Count > 0);

    public Task Run(BuildContext context, CancellationToken ct)
    {
        // On a set several games share, each game's own went into the plan
        // and landed on one disc with the game. This disc gets the ones the
        // plan gave it and no others: every game's artbook on every disc
        // costs more discs than the games do.
        if (context.Layout.EachGamesOwnIsPlaced)
        {
            foreach (PlannedFile own in context.Disc.Files.Where(file => file.GoesWhole))
            {
                ct.ThrowIfCancellationRequested();

                context.Writer.MakeFolder(context.On(System.IO.Path.GetDirectoryName(own.RelativePath)!));

                if (files.FolderExists(own.Source))
                {
                    CopyInto(context, own.Source, own.RelativePath, ct);
                }
                else
                {
                    context.Writer.Copy(own.Source, context.On(own.RelativePath));
                }
            }

            return Task.CompletedTask;
        }

        IReadOnlyList<GameFolder> games = context.Project.Games;

        for (int at = 0; at < games.Count; at++)
        {
            string folder = OnTheDisc.FolderFor(at + 1, games[at], games.Count);

            foreach (GameExtra own in games[at].OwnOrNone)
            {
                ct.ThrowIfCancellationRequested();

                string landing = own.OnTheDisc(folder);
                context.Writer.MakeFolder(context.On(System.IO.Path.GetDirectoryName(landing)!));

                if (files.FolderExists(own.Path))
                {
                    // A folder keeps its own name and goes whole: an artbook
                    // is a folder of pictures, and "Artbook" is what somebody
                    // looks for.
                    CopyInto(context, own.Path, landing, ct);
                }
                else
                {
                    context.Writer.Copy(own.Path, context.On(landing));
                }
            }
        }

        return Task.CompletedTask;
    }

    private void CopyInto(BuildContext context, string from, string into, CancellationToken ct)
    {
        context.Writer.MakeFolder(context.On(into));

        foreach (ListedFile file in files.List(from).Files)
        {
            ct.ThrowIfCancellationRequested();

            string landing = context.On(System.IO.Path.Combine(into, file.RelativePath));
            string? folder = System.IO.Path.GetDirectoryName(landing);

            if (!string.IsNullOrEmpty(folder))
            {
                context.Writer.MakeFolder(folder);
            }

            context.Writer.Copy(System.IO.Path.Combine(from, file.RelativePath), landing);
        }
    }
}
