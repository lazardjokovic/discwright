namespace DiscWright.Core;

/// <summary>
/// Every no the app can say, in one file.
/// </summary>
/// <remarks>
/// Together rather than scattered at the point each one is raised, so the whole
/// voice of the program can be read in one sitting and kept consistent. It is
/// also the only way to notice that two refusals contradict each other, which
/// is impossible to see when each is a string built where it is thrown.
/// </remarks>
public static class Refusals
{
    /// <summary>
    /// The disc is so small that the menu and artwork leave nothing behind.
    /// </summary>
    public static Refusal NoRoomLeftOnceTheMenuIsOn(Medium medium, long overheadPerDisc) =>
        new("There is no room on a disc that size",
            $"The menu, the icon and the artwork come to {ByteSize.Describe(overheadPerDisc)} on every disc, "
                + $"and a {medium.Name} holds {ByteSize.Describe(medium.Capacity)}.",
            [
                new Figure("a disc holds", ByteSize.Describe(medium.Capacity)),
                new Figure("the menu and artwork need", ByteSize.Describe(overheadPerDisc))
            ],
            new Remedy("Use a larger disc", RemedyKind.ChangeMedium));

    /// <summary>
    /// One file is bigger than a whole disc, and cutting files was not allowed.
    /// </summary>
    /// <remarks>
    /// Named, because the answer is a bigger blank and the person needs to know
    /// which file decided that. The numbers are figures as well as prose: the
    /// window has 655 pixels for this line and the sentence wants 930, so it
    /// shows the figures, and reading them back out of our own prose would be
    /// parsing our own writing.
    /// </remarks>
    public static Refusal ThatOneFileIsBiggerThanADisc(PlanFile file, Medium medium, long roomPerDisc) =>
        new("One file is bigger than a whole disc",
            $"{file.RelativePath} is {ByteSize.Describe(file.Bytes)}, and a {medium.Name} leaves "
                + $"{ByteSize.Describe(roomPerDisc)} for files once the menu is on it. Letting it cut the file "
                + "across discs is the other way out, and then every disc of the set is needed to put it back together.",
            [
                new Figure("the file", ByteSize.Describe(file.Bytes)),
                new Figure("a disc leaves", ByteSize.Describe(roomPerDisc))
            ],
            new Remedy("Use a larger disc", RemedyKind.ChangeMedium));

    /// <summary>
    /// The manual and extras alone are bigger than a disc, so no disc can carry
    /// them and there is nowhere to put them.
    /// </summary>
    public static Refusal TheExtrasAloneWillNotFitOnADisc(long bytesForTheExtras, Medium medium, long roomPerDisc) =>
        new("The manual and extras will not fit on a disc",
            $"They come to {ByteSize.Describe(bytesForTheExtras)}, and a {medium.Name} leaves "
                + $"{ByteSize.Describe(roomPerDisc)} for files. Leave some of them off, or use a larger disc.",
            [
                new Figure("the manual and extras", ByteSize.Describe(bytesForTheExtras)),
                new Figure("a disc leaves", ByteSize.Describe(roomPerDisc))
            ],
            new Remedy("Use a larger disc", RemedyKind.ChangeMedium));

    /// <summary>
    /// A loose extra whose name is one the disc keeps for itself.
    /// </summary>
    /// <remarks>
    /// Refused by name rather than renamed around. Quietly putting their file
    /// somewhere else is worse: they would burn a disc believing it holds a
    /// file it does not hold, under a name they could find it by.
    /// </remarks>
    public static Refusal ThatNameBelongsToTheDisc(string name) =>
        new("The disc already uses that name",
            $"\"{name}\" is one of the names a disc keeps for itself, and a file of that name at the root would "
                + "replace part of what makes the disc work. Rename it and add it again.",
            [],
            new Remedy("Choose something else", RemedyKind.PickFolder));

    /// <summary>A disc with no name at all.</summary>
    public static Refusal ADiscNeedsAName() =>
        new("The disc needs a name",
            "It is what This PC shows when the disc goes in, and what somebody reads off the printed cover.",
            [],
            new Remedy("Name the disc", RemedyKind.FocusField));

    /// <summary>
    /// The label holds characters the system codepage cannot carry, so AutoRun
    /// will show something else.
    /// </summary>
    /// <remarks>
    /// Shown as a warning rather than a wall. A disc called "??????? ????"
    /// still works and still installs, and somebody may genuinely not mind;
    /// what they must not do is find out afterwards. The surviving text is in
    /// the figures, because seeing it is what makes the decision obvious.
    /// </remarks>
    public static Refusal ThatNameWillNotSurviveOnTheDisc(string label, string surviving) =>
        new("Windows cannot show that name on a disc",
            "AutoRun reads a disc's name in this machine's codepage and has no Unicode at all, so this is what "
                + "This PC will show. The menu, the cover and the files are unaffected: it is only the name on the disc.",
            [
                new Figure("you typed", label),
                new Figure("the disc will say", surviving)
            ],
            new Remedy("Rename the disc", RemedyKind.FocusField));

    /// <summary>Nothing was chosen at all.</summary>
    public static Refusal NoFolderWasChosen() =>
        new("No folder was chosen", "Pick the folder the game was downloaded into.",
            [],
            new Remedy("Choose a folder", RemedyKind.PickFolder));

    /// <summary>
    /// There is no folder at that path.
    /// </summary>
    /// <remarks>
    /// Separate from "that folder is empty" on purpose: a USB stick that has
    /// been pulled out and a folder somebody emptied are different mistakes,
    /// and only one of them is worth going to look for the folder again.
    /// </remarks>
    public static Refusal ThatFolderIsNotThere(string path) =>
        new("That folder is not there",
            $"Nothing was found at {path}. It may have been moved, renamed, or be on a drive that is no longer attached.",
            [],
            new Remedy("Choose another folder", RemedyKind.PickFolder));

    /// <summary>
    /// The folder holds no setup_*.exe, but there is one inside a "disc"
    /// subfolder, which means this is a disc we built rather than a download.
    /// </summary>
    /// <remarks>
    /// Named rather than passed on. The next recogniser in the chain would take
    /// it happily as an ordinary folder of game files and put a whole disc
    /// inside a disc, and the generic "no installer here" sends somebody
    /// looking for a fault in their download instead of at which folder they
    /// picked.
    /// </remarks>
    public static Refusal ThatIsADiscRatherThanADownload(FolderListing folder) =>
        new("That is a disc, not a download",
            $"{folder.Path} holds a disc DiscWright built. The folder wanted here is the one downloaded from GOG; "
                + "this is where the disc gets written to.",
            [],
            new Remedy("Choose another folder", RemedyKind.PickFolder));

    /// <summary>There is nothing in the folder at all.</summary>
    public static Refusal ThatFolderIsEmpty(FolderListing folder) =>
        new("That folder is empty", $"There is nothing in {folder.Path} to put on a disc.",
            [],
            new Remedy("Choose another folder", RemedyKind.PickFolder));

    /// <summary>
    /// Every recogniser passed. Reached only for a folder that exists, holds
    /// files, and still fits nothing, which today means it cannot happen: the
    /// last recogniser in the chain claims any folder with files in it. Kept
    /// because the chain is meant to grow, and a reader should not have to work
    /// out what happens when nothing claims a folder.
    /// </summary>
    public static Refusal NothingRecognisedIn(FolderListing folder) =>
        new("Nothing in that folder looks like a game",
            $"Nothing in {folder.Path} looks like an installer or a program.",
            [],
            new Remedy("Choose another folder", RemedyKind.PickFolder));

    /// <summary>
    /// Asked to plan nothing. Worth saying rather than handing back a set of no
    /// discs, which every caller would then have to notice for itself.
    /// </summary>
    public static Refusal ThereIsNothingToPutOnADisc() =>
        new("There is nothing to put on a disc", "No files were chosen.");

    /// <summary>
    /// The image from last time will not move out of the way.
    /// </summary>
    /// <remarks>
    /// Nearly always because somebody mounted it to look inside, which leaves
    /// Windows holding it open. Saying so is the whole value of this refusal:
    /// the message Windows gives on its own names a file and no reason.
    /// </remarks>
    public static Refusal TheOldImageWillNotMoveAside(string path, string windowsSaid) =>
        new("The image from last time cannot be replaced",
            $"Something is still using {path}. If it was mounted to look inside, eject it first: "
                + "right-click the drive in This PC and choose Eject, then build again."
                + Environment.NewLine + Environment.NewLine + "Windows said: " + windowsSaid);

    /// <summary>Windows refused to make the image, in its own words.</summary>
    public static Refusal WindowsCouldNotMakeTheImage(string windowsSaid) =>
        new("Windows could not make the image", "Windows said: " + windowsSaid);

    // ------------------------------------------------------- opening a disc

    /// <remarks>
    /// Refused whole rather than read in part. Half a project is a disc that
    /// rebuilds without something somebody chose, and the one thing worse than
    /// not opening a file is opening it wrongly.
    /// </remarks>
    public static Refusal ThatIsNotAProjectFile(string why) =>
        new("That is not a DiscWright project",
            "The file could not be read as one. " + why);

    /// <remarks>
    /// Said instead of opening an empty window. A disc whose every game has
    /// moved is still a disc somebody can find the games for, and an empty
    /// window tells them nothing about which ones to look for.
    /// </remarks>
    public static Refusal NothingOnThatDiscIsWhereItWas(IReadOnlyList<string> gone) =>
        new("Nothing on that disc is where it was",
            (gone.Count == 1
                ? "The one thing it names is not there any more:"
                : $"None of the {gone.Count} things it names are there any more:")
                + Environment.NewLine + string.Join(Environment.NewLine, gone.Take(10)));

    public static Refusal ThatProjectHasNoGamesInIt() =>
        new("That project has no games in it",
            "It was read, and there is nothing in it to put on a disc. The file may have been written by "
                + "something else, or cut short.");

    // ---------------------------------------------------------------- burning

    public static Refusal ThereIsNoDiscWriter() =>
        new("There is no disc writer on this machine",
            "Nothing here can write a disc. The image is still in the output folder, and any machine with a "
                + "writer can burn it.");

    public static Refusal ThatDriveIsNotHere(string drive) =>
        new($"There is no {drive} to write to",
            "The drive that was chosen is not on this machine any more. Choose again.");

    public static Refusal ThatDriveSaidNothing(string drive, string why) =>
        new($"{drive} would not say what is in it",
            "Usually that means the tray is empty or the disc is still being read. Windows said: " + why);

    /// <remarks>
    /// Named, so somebody knows which tray to open. Listing them and leaving
    /// the choice is this one's whole job: picking for them would be wrong half
    /// the time, and the disc in the other drive is often their only blank.
    /// </remarks>
    public static Refusal MoreThanOneDriveIsReady(IReadOnlyList<string> drives) =>
        new("More than one drive has a blank disc in it",
            "Choose which one to write to: " + string.Join(", ", drives) + ".");

    public static Refusal ThereIsNoBlankDisc(IReadOnlyList<string> drives) =>
        new("There is no blank disc to write to",
            drives.Count == 1
                ? $"Put a blank disc in {drives[0]} and try again."
                : "Put a blank disc in one of " + string.Join(", ", drives) + " and try again.");

    /// <remarks>
    /// The one thing worth saying when nothing is ready but something could be.
    /// It stops here: erasing somebody's disc without asking is not a thing to
    /// do quietly, so this says an erase would help and the person holding it
    /// decides.
    /// </remarks>
    public static Refusal ThereIsADiscThatCouldBeErased(string drive, string what) =>
        new($"The {what} in {drive} has something on it",
            "It can be erased and used again, which takes under a minute. Nothing has been erased: that is "
                + "yours to decide, because whatever is on it now goes.");

    /// <remarks>
    /// Which disc to destroy is not a choice to make for somebody, so this
    /// names them and stops. The old answer fell through to "that disc cannot
    /// be written to", which is false about a rewritable one and is exactly the
    /// wording that makes somebody bin a usable disc.
    /// </remarks>
    public static Refusal MoreThanOneDiscCouldBeErased(IReadOnlyList<string> drives) =>
        new("More than one disc could be erased",
            "There is no blank disc, and the discs in " + string.Join(" and ", drives)
                + " could each be erased and used again. Which one goes is yours to decide, so nothing "
                + "has been erased.");

    /// <remarks>
    /// Said when somebody asks to erase and there is nothing an erase would
    /// help. A pressed disc and an empty tray are both ordinary, and neither
    /// is a fault worth more than a sentence.
    /// </remarks>
    public static Refusal ThereIsNothingToErase(IReadOnlyList<string> drives) =>
        new("There is no disc here that erasing would help",
            drives.Count == 0
                ? "There is no disc writer on this machine."
                : "A disc has to be rewritable and have something on it. Put one in "
                    + string.Join(" or ", drives) + " and try again.");

    public static Refusal ThatDiscCannotBeWrittenTo(string drive, string what) =>
        new($"The {what} in {drive} cannot be written to",
            "It is either a pressed disc or one that has already been written and cannot be erased. A blank "
                + "disc of the same kind will work.");

    public static Refusal TheDiscIsTooSmallForThat(string drive, string what, long needs, long has) =>
        new($"The {what} in {drive} is too small for this image",
            $"The image is {ByteSize.Describe(needs)} and the disc holds {ByteSize.Describe(has)}. "
                + "Nothing was written.",
            [
                new Figure("the image", ByteSize.Describe(needs)),
                new Figure("the disc", ByteSize.Describe(has))
            ],
            null);

    /// <remarks>
    /// Not a failure of the burn. The disc is written and may well be perfect;
    /// what is missing is the proof, and saying "done" without it would be
    /// claiming something nobody checked.
    /// </remarks>
    public static Refusal TheDiscWasNotChecked(string drive) =>
        new("The disc was written but not checked",
            $"Nothing could be read back from {drive}, so nothing here knows whether the disc reads. "
                + "Put it back in and check it before trusting it.");

    public static Refusal TheDriveCouldNotWriteIt(string drive, string windowsSaid) =>
        new($"{drive} could not write the disc",
            "The disc in it may now be unusable. Windows said: " + windowsSaid);
}
