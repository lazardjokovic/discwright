namespace DiscWright.Core;

/// <summary>
/// Asks each recogniser in turn what a folder is, and stops at the first one
/// that claims it.
/// </summary>
/// <remarks>
/// <para>
/// The order is data, handed in, and it matters: a folder that could be read
/// two ways goes to the earlier recogniser. A GOG download is also, strictly, a
/// folder with files in it, so the one that recognises folders of game files
/// has to come last or it would claim everything.
/// </para>
/// <para>
/// Adding a kind of source is one class and one entry in that list. Nothing
/// here changes, which is the whole point of the arrangement.
/// </para>
/// </remarks>
public sealed class GameFolderReader(IReadOnlyList<IGameFolderRecogniser> recognisers)
{
    public Outcome<GameFolder> Read(FolderListing folder)
    {
        foreach (IGameFolderRecogniser recogniser in recognisers)
        {
            Outcome<GameFolder>? answer = recogniser.Recognise(folder);

            // null is "not mine, ask the next one". A refusal is "mine, and
            // broken", and it stops here: passing it on would let a later,
            // less fussy recogniser accept something the one that understood
            // it has already rejected.
            if (answer is not null)
            {
                return answer;
            }
        }

        return Refusals.NothingRecognisedIn(folder);
    }

    /// <summary>
    /// The recognisers, in the order they are asked. Named so a test can pin
    /// the order rather than re-stating it, and so the window does not have to
    /// know how to build one.
    /// </summary>
    public static GameFolderReader InTheUsualOrder(IFileFacts facts) =>
        new(
        [
            new Recognisers.GogDownloadRecogniser(facts),
            new Recognisers.GameFilesRecogniser()
        ]);
}
