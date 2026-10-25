using DiscWright.Core;

namespace DiscWright.ViewModels;

/// <summary>
/// What happens when folders are added: which are games, which hold games,
/// what is asked about each, and in what order.
/// </summary>
/// <remarks>
/// <para>
/// This lived in the window's code-behind, where no test could reach it. Every
/// piece it calls was well covered and the order it called them in was not,
/// which is the one place in this program where "no test presses a button"
/// cost something real. The questions arrive as delegates, the way
/// <see cref="BurnTheSet"/> takes its own, so the window supplies dialogs and
/// a test supplies answers.
/// </para>
/// <para>
/// <strong>It answers with steps rather than doing anything.</strong> Nothing
/// here touches the screen, so what a test reads is the decision: which games
/// go on, with what attached, what else the disc carries, and what the person
/// is told.
/// </para>
/// <para>
/// <strong>The steps are yielded lazily, and that is load-bearing.</strong>
/// The window applies each one as it arrives, so a game appears on the list
/// before the next folder is asked about, exactly as it did before the lift.
/// Gathering them all first would make somebody answer five dialogs and then
/// watch five games appear at once, which is a different program. A test that
/// wants the whole list only has to enumerate it.
/// </para>
/// </remarks>
public sealed class TakingFoldersOn(IFileSystem disk, GameFolderReader reader)
{
    /// <summary>What the window is to do about a folder.</summary>
    public abstract record WhatToDo
    {
        private WhatToDo()
        {
        }

        /// <summary>Put this game on the list, with these attached to it.</summary>
        public sealed record PutItOn(GameFolder Game, IReadOnlyList<LeftBehind> AsAddOns) : WhatToDo;

        /// <summary>Carry these files on the disc, belonging to no game.</summary>
        public sealed record AlsoOnTheDisc(IReadOnlyList<string> Paths) : WhatToDo;

        /// <summary>
        /// The folder is on the list already. Here is what it holds now, so
        /// whatever was taken off can be offered back rather than the same
        /// game going on twice.
        /// </summary>
        public sealed record ItIsOnAlready(GameFolder Game, IReadOnlyList<LeftBehind> Left) : WhatToDo;

        /// <summary>Say why this folder is not going on.</summary>
        public sealed record Explain(Refusal Refusal) : WhatToDo;
    }

    /// <summary>
    /// The four things somebody has to be asked, and nothing else.
    /// </summary>
    /// <param name="AddingThemSeparately">
    /// Given the folder, the games inside it and how many files sit loose:
    /// true to add each game on its own, false to add the folder as one game.
    /// </param>
    /// <param name="LeavingItForItsGame">
    /// Given something that installs onto a game rather than being one: true
    /// to leave it off and wait for its game, false to put it on anyway. Not a
    /// refusal, so false really does put it on.
    /// </param>
    /// <param name="WhatElseIsInThere">
    /// Given the game and what the folder held beyond it: the game as it
    /// should now be, and whatever is to be attached to it. Not asked when
    /// the folder left nothing behind.
    /// </param>
    /// <param name="WhicheverStartsIt">
    /// Given a folder of files with more than one plausible launcher: the game
    /// narrowed to the chosen one, or unchanged to let the disc copy the
    /// folder and open it. Not asked with fewer than two, so an implementation
    /// does not have to check.
    /// </param>
    public sealed record TheQuestions(
        Func<string, IReadOnlyList<string>, int, bool> AddingThemSeparately,
        Func<GameFolder, bool> LeavingItForItsGame,
        Func<GameFolder, IReadOnlyList<LeftBehind>, (GameFolder Game, IReadOnlyList<LeftBehind> AddOns)> WhatElseIsInThere,
        Func<GameFolder, GameFolder> WhicheverStartsIt)
    {
        /// <summary>
        /// Every question answered the way somebody who closed the dialog
        /// answered it: add as one, put the add-on on, attach nothing, say
        /// nothing about the launcher.
        /// </summary>
        public static TheQuestions NobodyIsAsked { get; } = new(
            (folder, inside, loose) => false,
            game => false,
            (game, left) => (game, []),
            game => game);
    }

    /// <summary>
    /// Works out what to do about each folder, asking as it goes.
    /// </summary>
    /// <param name="folders">The folders somebody pointed at.</param>
    /// <param name="asking">How each question is put.</param>
    /// <param name="isOnAlready">
    /// Whether a folder is on the list already, by its path. One that is
    /// comes back as <see cref="WhatToDo.ItIsOnAlready"/> with nothing asked,
    /// so the screen can offer back whatever was taken off instead of the
    /// same game going on twice. Nothing is on the list when nothing is given.
    /// </param>
    public IEnumerable<WhatToDo> From(
        IEnumerable<string> folders, TheQuestions asking, Func<string, bool>? isOnAlready = null)
    {
        foreach (string folder in folders)
        {
            FolderListing listing = disk.List(folder);

            // Settled before the folder is taken apart. A folder of several
            // games that went on as one game is on the list by its own path,
            // and asking again whether to add them separately would only
            // lead to each of them going on beside it.
            bool onAlready = isOnAlready?.Invoke(folder) ?? false;

            // Pointing at the folder that HOLDS the downloads rather than at
            // one of them. Never refused, because a real game can have an
            // installer buried in it too, and taking that whole is better than
            // turning it away.
            IReadOnlyList<string> inside = onAlready ? [] : GamesInside.Of(listing);

            if (inside.Count > 1)
            {
                // Anything sitting loose in the folder belongs to none of the
                // games inside it, so it belongs to the disc. Carried over
                // rather than dropped, because taking a folder apart must not
                // cost somebody a file.
                string[] loose = [.. listing.AtTheTop.Select(file => Path.Combine(folder, file.RelativePath))];

                if (asking.AddingThemSeparately(folder, inside, loose.Length))
                {
                    foreach (WhatToDo step in From([.. inside.Select(each => Path.Combine(folder, each))], asking, isOnAlready))
                    {
                        yield return step;
                    }

                    yield return new WhatToDo.AlsoOnTheDisc(loose);
                    continue;
                }
            }

            switch (reader.Read(listing))
            {
                case Outcome<GameFolder>.Ok(GameFolder game):
                    // On the list already. What the folder holds goes back
                    // whole, with everything that sat beside the game, and
                    // nothing is asked here: the only question is whether to
                    // put back what was taken off, and the screen words that,
                    // because only the screen knows what is missing.
                    if (onAlready)
                    {
                        yield return new WhatToDo.ItIsOnAlready(game, WhatWasLeftBehind.In(listing, game));
                        break;
                    }

                    // Something that installs onto a game, with no game. It
                    // still goes on if somebody wants it to; it just must not
                    // go on silently, because on the disc it becomes a game in
                    // the menu and installing it does nothing.
                    if (game.Installer is ListedFile installer
                        && AddOn.LooksLikeOne(installer.Name)
                        && asking.LeavingItForItsGame(game))
                    {
                        break;
                    }

                    // What else was in there is asked about before the game
                    // goes on the list, because the answer changes what the
                    // game is.
                    IReadOnlyList<LeftBehind> left = WhatWasLeftBehind.In(listing, game);

                    // Nothing is asked when nothing was left behind, which is
                    // every folder of game files and most downloads. A dialog
                    // that appears to say there is nothing to tell you is
                    // worse than silence. The dialog guards this too; the
                    // decision belongs here, where it can be tested.
                    (GameFolder with, IReadOnlyList<LeftBehind> asAddOns) =
                        left.Count > 0 ? asking.WhatElseIsInThere(game, left) : (game, []);

                    // Asked only with something to choose between. With one
                    // plausible launcher the menu runs it, with none it copies
                    // the folder and opens it, and a dialog offering a choice
                    // of one is a dialog nobody should see.
                    yield return new WhatToDo.PutItOn(
                        with.LauncherCandidates.Count >= 2 ? asking.WhicheverStartsIt(with) : with,
                        asAddOns);
                    break;

                // Said about the one folder it is about, rather than kept for
                // the bottom line. The bottom line is about what the disc will
                // be; this is about a folder somebody just picked and can pick
                // again.
                case Outcome<GameFolder>.No refused:
                    yield return new WhatToDo.Explain(refused.Refusal);
                    break;
            }
        }
    }
}
