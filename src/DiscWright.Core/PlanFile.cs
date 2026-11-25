namespace DiscWright.Core;

/// <summary>
/// One file waiting to go on a disc, as the planner needs to see it.
/// </summary>
/// <remarks>
/// Three strings and a number, and deliberately nothing else. The planner is
/// arithmetic over sizes: it never opens a file, never asks what kind of file it
/// is, and never cares which game it belongs to. That is what lets every case in
/// the catalogue be tested without a single byte on disk.
/// </remarks>
/// <param name="RelativePath">Where it goes on the disc, relative to the root.</param>
/// <param name="Source">Where it comes from. Carried through untouched, for whoever copies it.</param>
/// <param name="Bytes">How big it is.</param>
/// <param name="KeepWhole">
/// Whether it goes on a disc as one thing or not at all: one of a game's own
/// manual or extras, which may be a folder and is never cut between discs.
/// The planner still knows nothing about what it is, only that it will not
/// divide.
/// </param>
/// <param name="EarlierWhenThereIsRoom">
/// Laid down in the order of the list like every file, and then, once every
/// disc is planned, moved to disc 1 when disc 1 has the room, or else to the
/// first disc of the game it belongs to when that is earlier and has it. A
/// game's own manual and extras on a set several games share: disc 1 is where
/// somebody goes through the games and wants to read about one before
/// choosing it. Moved after the plan and never before it, so every game's
/// installer is on exactly the discs it would have been on, and no game
/// installs any worse for its manual being easy to reach. Taken out of the
/// list first, a game could start a disc earlier and spread over one more
/// disc, measured on 25 November 2026. The owner asked for it that day, after
/// a real set put Dead Space's manual on disc 3.
/// </param>
/// <param name="Near">
/// The folder of the game it belongs to, to find that game's first disc by.
/// </param>
public sealed record PlanFile(
    string RelativePath,
    string Source,
    long Bytes,
    bool KeepWhole = false,
    bool EarlierWhenThereIsRoom = false,
    string Near = "");
