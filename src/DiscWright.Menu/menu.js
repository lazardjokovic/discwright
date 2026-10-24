// The disc menu's own logic.
//
// This file is carried onto every disc as AUTORUN\menu.js and included by
// menu.hta. It is IDENTICAL on every disc: nothing is substituted into it, no
// placeholder reaches it, and the parts that differ from disc to disc live in a
// data block in the HTA instead.
//
// That split is the whole point. In v1 this logic was embedded in the HTA
// alongside the generated data, where nothing could reach it: the menu is
// invisible to UI Automation, so neither xUnit nor the window suite can drive
// it, and it has never had a test in either version. As a file of its own it
// can be run under cscript.exe against written inputs, which is what
// TheMenuScript does.
//
// WHAT MAY NOT BE IN HERE. It has to parse and run under both mshta and
// cscript, so nothing at the top level may touch window, document, or WScript.
// Functions only, and arithmetic over values handed in. Anything that needs a
// window belongs in the HTA.
//
// JScript, not modern JavaScript. The oldest machine this has to run on is
// Windows XP: no let, no const, no arrow functions, no JSON, no Array.forEach,
// no trailing commas.

// What the menu's window is called. A disc on its own is just the game; a disc
// in a set says which one it is, because somebody holding three discs needs to
// know what is in their hand without putting it in a drive.
// The separator a registry path is made of, built from its code so that
// nothing that copies this file can halve it.
var BACKSLASH = String.fromCharCode(92);

function discTitle(gameName, discNumber, discsInSet) {
    if (discsInSet <= 1) {
        return gameName;
    }
    return gameName + " (disc " + discNumber + " of " + discsInSet + ")";
}

// How fast this drive is actually reading, in bytes per second.
//
// Only files that took more than a second count. A thousand tiny files copied
// in no measurable time would otherwise divide by nearly nothing and claim a
// rate of gigabytes a second, and the bar would jump to 99 and sit there.
function rateFrom(samples, seedRate) {
    var bytes = 0;
    var seconds = 0;

    for (var i = 0; i < samples.length; i++) {
        if (samples[i].seconds > 1 && samples[i].bytes > 0) {
            bytes += samples[i].bytes;
            seconds += samples[i].seconds;
        }
    }

    if (seconds <= 0) {
        return seedRate;
    }

    return Math.floor(bytes / seconds);
}

// The rate to start with, before anything has been copied: 8x CD.
//
// A guess, and it says so. It is only used until the first file big enough to
// time has finished, after which rateFrom takes over with something measured.
function startingRate() {
    return 1228800;
}

// How far along the copy is, as a whole number.
//
// Exact for the files that have finished, and estimated for the one in flight,
// because nothing on a disc can see inside a copy that is happening: robocopy
// writes its percentages at the end and locks its log, the destination is
// pre-sized to its full length the moment the copy starts, and no script host
// on a disc can read part of a binary file. There is no performance counter for
// an optical drive either.
//
// Never 100. The last thing to happen is the check, and a bar that says 100
// while the machine is still working reads as a program that has stopped
// responding.
function percentDone(bytesFinished, secondsOnTheCurrentFile, rate, bytesInAll) {
    if (bytesInAll <= 0) {
        return 0;
    }

    var guessed = bytesFinished + Math.floor(secondsOnTheCurrentFile * rate);

    if (guessed > bytesInAll) {
        guessed = bytesInAll;
    }

    var percent = Math.floor((guessed * 100) / bytesInAll);

    if (percent > 99) {
        percent = 99;
    }

    if (percent < 0) {
        percent = 0;
    }

    return percent;
}

// Which screen the menu opens on.
//
// One game and it opens on that game, because a chooser offering one choice is
// a door with one door behind it. Several and it opens on the chooser, because
// picking is the first thing somebody has to do.
function whichScreenToOpenOn(games) {
    if (!games || games.length <= 1) {
        return "game";
    }
    return "chooser";
}

// One game out of the list, or nothing.
//
// Bounds checked, because the index comes from whatever the menu was told, and
// an index past the end in JScript is not an error: it is undefined, and the
// next line asks undefined for its name and the whole menu stops.
function gameAt(games, index) {
    if (!games || index < 0 || index >= games.length) {
        return null;
    }
    return games[index];
}

// What the one coloured button on a game's screen says.
//
// A game with an installer is installed. A game with nothing to install is
// copied and played from wherever it was copied to, because a game run off a
// disc cannot save: the disc is read only, and the first time it tries to write
// a save it either fails or silently writes nowhere.
function whatToDoWith(game, installed) {
    if (!game) {
        return "";
    }

    // Already on the machine, so the thing somebody came to do is play it.
    // Offering to install it again over the top is how a save game dies.
    if (installed === true) {
        return "Play";
    }

    if (game.runs && game.runs.length > 0) {
        return "Install";
    }

    return "Copy and play";
}

// Whether an add-on can be pressed yet.
//
// THE GAP THIS FIXES. On the menu as it was, an add-on was just another button:
// press the soundtrack installer before the game and it runs, and a patch run
// before its base is worse than that, because it half-applies to nothing and
// leaves no sign that it did.
//
// So an add-on is dead until its game is on the machine, and says so in the one
// line it has.
function canRunAddOnYet(gameIsInstalled) {
    return gameIsInstalled === true;
}

// What an add-on's line says underneath it.
//
// Empty when it can be pressed, because a button that works needs no
// explanation and a line of text under every button is a menu nobody reads.
function whatAnAddOnSays(gameIsInstalled) {
    if (canRunAddOnYet(gameIsInstalled)) {
        return "";
    }
    return "Install the game first, then this.";
}

// How the add-ons of one game are ordered.
//
// Patches before everything else, and among patches the earliest version first,
// because a patch applies to the version before it and running 1.3 before 1.2
// is how somebody ends up with a game that will not start. Everything else
// keeps the order it was added in, which is the order somebody chose.
function addOnsInOrder(addOns) {
    var patches = [], others = [], i;

    for (i = 0; i < addOns.length; i++) {
        if (addOns[i].name.indexOf("Update ") === 0) {
            patches.push(addOns[i]);
        } else {
            others.push(addOns[i]);
        }
    }

    patches.sort(function (a, b) {
        if (a.name === b.name) { return 0; }
        return a.name < b.name ? -1 : 1;
    });

    return patches.concat(others);
}

// ===========================================================================
// Is this game already on the machine?
//
// The one question the menu could not answer. Until now every add-on stayed
// dead for ever and the main button always said Install, even to somebody who
// had the game installed and only wanted to play it.
//
// GOG writes what it installs into the registry, and that is what this reads.
// ===========================================================================

// A name with everything but letters and digits taken out.
//
// GOG's registry name drops and changes punctuation: it writes "The Witcher
// Enhanced Edition Director's Cut" where the folder said "The Witcher -
// Enhanced Edition". Compared as written, those are two different games.
//
// The trademark marks come out first and on their own, because they are the
// one kind of punctuation that leaves a LETTER behind. GOG registers Empire at
// War as "STAR WARS(R): Empire At War(TM)", and dropping only the brackets
// leaves an r and a tm wedged into the middle of the name, which matches
// nothing.
function nameAsItCompares(name) {
    return String(name)
        .toLowerCase()
        .replace(/\((r|tm|c)\)/g, "")
        .replace(/[\u00ae\u2122\u00a9]/g, "")
        .replace(/[^a-z0-9]+/g, "");
}

// Whether the name in the registry and the name on the disc are the same game.
//
// Either may be the longer: GOG sometimes registers more words than the folder
// had, and sometimes fewer. So it is a containment test both ways round, with
// one guard: a registry name shorter than six letters is not allowed to swallow
// ours, because "Gothic" inside "Gothic 3" is a match worth having and "AI"
// inside anything is not.
function isTheSameGame(registryName, ourName) {
    var theirs = nameAsItCompares(registryName);
    var ours = nameAsItCompares(ourName);

    if (!theirs || !ours) {
        return false;
    }

    if (theirs.indexOf(ours) >= 0) {
        return true;
    }

    return theirs.length >= 6 && ours.indexOf(theirs) >= 0;
}

// Where an installed copy of this game is, or null.
//
// Read through WMI rather than with WScript.Shell, because the games are under
// a key named by a number nobody knows in advance, and only WMI can list the
// keys under a key. Both views are looked in: a 32 bit game on a 64 bit Windows
// registers under WOW6432Node and a 64 bit one does not.
//
// The folder matters as much as the executable. It is where GOG's own
// goggame-<id>.info sits, and that file is the only place a bundle admits to
// holding more than one game.
function whereTheGameIs(match) {
    var UNDER_LOCAL_MACHINE = 0x80000002;
    var bases = ["SOFTWARE" + BACKSLASH + "WOW6432Node" + BACKSLASH + "GOG.com" + BACKSLASH + "Games",
                 "SOFTWARE" + BACKSLASH + "GOG.com" + BACKSLASH + "Games"];
    var files, registry, at, ids, which, key, name, folder, exe, named;

    try {
        files = new ActiveXObject("Scripting.FileSystemObject");
        registry = GetObject("winmgmts:" + BACKSLASH + BACKSLASH + "." + BACKSLASH
            + "root" + BACKSLASH + "default").Get("StdRegProv");
    } catch (noRegistry) {
        // No WMI, or no permission to ask it. Not knowing is not the same as
        // knowing it is absent, but the menu has only one honest move either
        // way: leave the add-ons alone.
        return null;
    }

    for (at = 0; at < bases.length; at++) {
        ids = keysUnder(registry, UNDER_LOCAL_MACHINE, bases[at]);

        for (which = 0; which < ids.length; which++) {
            key = bases[at] + BACKSLASH + ids[which];
            name = oneStringFrom(registry, UNDER_LOCAL_MACHINE, key, "gameName");

            if (!name || !isTheSameGame(name, match)) {
                continue;
            }

            folder = oneStringFrom(registry, UNDER_LOCAL_MACHINE, key, "path");
            exe = oneStringFrom(registry, UNDER_LOCAL_MACHINE, key, "exe");

            // Checked on the disk, not believed. An entry left behind by an
            // uninstall that did not finish is the common case, and it would
            // otherwise light up a Play button that cannot work.
            if (exe && files.FileExists(exe)) {
                return { exe: exe, dir: folder || files.GetParentFolderName(exe) };
            }

            named = oneStringFrom(registry, UNDER_LOCAL_MACHINE, key, "exeFile");

            if (folder && named && files.FileExists(files.BuildPath(folder, named))) {
                return { exe: files.BuildPath(folder, named), dir: folder };
            }
        }
    }

    return null;
}

// The names of the keys under one key, or an empty list.
function keysUnder(registry, where, key) {
    var asking, answer;

    try {
        asking = registry.Methods_.Item("EnumKey").InParameters.SpawnInstance_();
        asking.hDefKey = where;
        asking.sSubKeyName = key;
        answer = registry.ExecMethod_("EnumKey", asking);

        if (answer.ReturnValue !== 0 || answer.sNames === null) {
            return [];
        }

        return new VBArray(answer.sNames).toArray();
    } catch (nothingThere) {
        return [];
    }
}

// One string value, or null.
function oneStringFrom(registry, where, key, value) {
    var asking, answer;

    try {
        asking = registry.Methods_.Item("GetStringValue").InParameters.SpawnInstance_();
        asking.hDefKey = where;
        asking.sSubKeyName = key;
        asking.sValueName = value;
        answer = registry.ExecMethod_("GetStringValue", asking);

        if (answer.ReturnValue === 0) {
            return answer.sValue;
        }
    } catch (nothingThere) {
    }

    return null;
}

// Where the disc is, worked out from the menu's own address.
//
// AutoRun can start a disc file with no drive letter, so a relative path is no
// use and this has to come from the URL. The menu lives in AUTORUN, so the
// disc itself is one folder above it.
//
// A file URL arrives with leading slashes that are not part of the path,
// except on a network share, where two of them are. Learned in v1's launcher
// and worth having in one place: the launcher at the top of the disc solves
// the same problem for itself.
function theDiscIsAbove(menuUrl) {
    var cleaned = String(menuUrl || "").replace(/^file:/i, "").split("/").join(BACKSLASH);
    var leading = 0;
    var rest, cut;

    try {
        cleaned = decodeURIComponent(cleaned);
    } catch (notEncoded) {
    }

    while (cleaned.charAt(leading) === BACKSLASH) {
        leading++;
    }

    if (leading) {
        rest = cleaned.substring(leading);

        if (/^[A-Za-z]:/.test(rest)) {
            cleaned = rest;
        } else if (leading >= 3) {
            cleaned = BACKSLASH + BACKSLASH + rest;
        }
    }

    // Up twice: out of the file, then out of AUTORUN.
    for (cut = 0; cut < 2; cut++) {
        var at = cleaned.lastIndexOf(BACKSLASH);

        if (at <= 0) {
            return cleaned;
        }

        cleaned = cleaned.substring(0, at);
    }

    return cleaned;
}

// A path on the disc, out of the one the builder wrote into the facts.
//
// Those are written with forward slashes, because they go into a script rather
// than into a Windows API, and they are relative to the root of the disc
// because a disc does not know which letter it will be given.
function onTheDisc(discFolder, relative) {
    var parts, at, path;

    if (!relative) {
        return "";
    }

    parts = String(relative).split("/");
    path = String(discFolder || "");

    for (at = 0; at < parts.length; at++) {
        if (parts[at] === "") {
            continue;
        }

        if (path.length > 0 && path.charAt(path.length - 1) !== BACKSLASH) {
            path += BACKSLASH;
        }

        path += parts[at];
    }

    return path;
}

// What pressing the one big button should actually do.
//
// Three different acts wearing one button, and which it is was already decided
// by whatToDoWith: this says where the thing to do it to actually lives. Kept
// apart from the doing so that it can be checked without running anything.
function whatPressingItDoes(game, where, discFolder) {
    if (!game) {
        return null;
    }

    // Already on the machine. Play what is installed, from where it is
    // installed, and do not offer to install over the top of a save game.
    if (where) {
        return { act: "play", run: where.exe, from: where.dir };
    }

    if (game.runs && String(game.runs).length > 0) {
        return {
            act: "install",
            run: onTheDisc(discFolder, game.runs),
            from: onTheDisc(discFolder, game.folder)
        };
    }

    // Nothing to install, so it gets copied off the disc and played from
    // where it landed. A game run from the disc cannot save: the disc is read
    // only, and the first time it tries to write a save it either fails or
    // silently writes nowhere.
    return { act: "copy", run: onTheDisc(discFolder, game.folder), from: "" };
}

// What an add-on's button runs, or nothing when it must not be pressed yet.
//
// Asked here as well as drawn dead in the page, because a dead button somebody
// clicks anyway is still a click, and a patch run before its game half-applies
// to nothing and leaves no sign that it did.
function whatAnAddOnRuns(addOn, gameIsInstalled, discFolder) {
    if (!addOn || !canRunAddOnYet(gameIsInstalled)) {
        return null;
    }

    return onTheDisc(discFolder, addOn.runs);
}

// Whether this is a disc or a preview of one.
//
// A preview stages the menu, the artwork and the music and nothing else: no
// installer, no manual, no extras. Every button therefore points at a file
// that is not there, and pressing one used to produce whatever Windows says
// about a missing path, which reads as the disc being broken rather than as
// the preview doing its job.
//
// A variable rather than a look at DISC, because DISC lives in the page and
// the page cannot be tested: an HTA is invisible to UI Automation, so nothing
// can press its buttons. This file runs under cscript, where there is no page
// at all.
var thisIsADisc = true;

// Said by the page when it is drawing a preview.
function thisIsAPreview() {
    thisIsADisc = false;
}

// Why nothing runs, or null when things do.
function whyNothingRuns() {
    return thisIsADisc
        ? null
        : "This is a preview of the menu. Nothing starts from here, because the disc has not been made yet.";
}

// Starts something, and says what went wrong rather than nothing.
//
// Here rather than in the page because the page cannot be tested: an HTA is
// invisible to UI Automation, so nothing can press its buttons. What lives
// here runs under cscript against a real executable.
//
// Returns null when it started, or a sentence for the page to show.
function startIt(what, from) {
    var shell;
    var preview = whyNothingRuns();

    if (preview) {
        return preview;
    }

    if (!what || String(what).length === 0) {
        return "There is nothing on this disc for that button to start.";
    }

    try {
        shell = new ActiveXObject("WScript.Shell");
    } catch (noShell) {
        return "This machine will not let the menu start anything.";
    }

    if (from && String(from).length > 0) {
        try {
            shell.CurrentDirectory = from;
        } catch (cannotGoThere) {
            // An installer run from the wrong folder still installs. Not being
            // able to get there is not a reason to refuse to try.
        }
    }

    try {
        // Quoted, because the path has spaces in it more often than not, and
        // not waited for, because the menu has no further part in it.
        shell.Run('"' + what + '"', 1, false);
        return null;
    } catch (wrong) {
        return "That would not start: " + what + " (" + wrong.message + ")";
    }
}

// Opens a folder in Explorer. Same shape, same reason.
function openTheFolder(where) {
    var preview = whyNothingRuns();

    if (preview) {
        return preview;
    }

    if (!where || String(where).length === 0) {
        return "There is no folder on this disc for that button to open.";
    }

    try {
        new ActiveXObject("WScript.Shell").Run('explorer.exe "' + where + '"', 1, false);
        return null;
    } catch (wrong) {
        return "That folder would not open: " + where + " (" + wrong.message + ")";
    }
}

// Where to offer to copy a game to.
//
// Under the person's own profile, because a game copied somewhere they cannot
// write to is a game that half copies and then stops, and because a game run
// from a folder it cannot write to cannot save, which is the whole reason it
// is being copied off the disc in the first place.
function whereToCopyTo(home, name) {
    return onTheDisc(onTheDisc(String(home || ""), "Games"), folderNameFor(name));
}

// A game's name as a folder Windows will make.
//
// On its own because two places need it and only one of them had it: the
// suggestion above tidied the name, and the folder actually made from the
// one somebody picked did not, so a game with a colon in its name, which is
// most of the ones with a subtitle, could not be copied off the disc at all.
function folderNameFor(name) {
    var tidy = String(name || "Game").replace(/[\/:*?"<>|]/g, " ");

    // Trailing dots and spaces are legal in a string and not in a folder name.
    while (tidy.length > 0 && (tidy.charAt(tidy.length - 1) === "." || tidy.charAt(tidy.length - 1) === " ")) {
        tidy = tidy.substring(0, tidy.length - 1);
    }

    if (tidy.length === 0) {
        tidy = "Game";
    }

    return tidy;
}

// How big to make the window so that all of the stage shows.
//
// resizeTo sets the OUTER size, frame and title bar included, so a window
// asked to be 480 tall has a client area 480 less the title bar, and the
// bottom of a 480 tall stage is under the frame. Measured on a real disc at
// about 38 pixels, which was enough to hide anything at the bottom of the
// panel. Handed what the page is and what it wants, this says what to ask
// for: the difference added on, and never less than was asked for, because
// a client area already bigger than the stage is nothing to shrink.
function sizeToShowAllOf(wantedWidth, wantedHeight, clientWidth, clientHeight) {
    var dw = wantedWidth - clientWidth;
    var dh = wantedHeight - clientHeight;

    if (dw < 0) {
        dw = 0;
    }

    if (dh < 0) {
        dh = 0;
    }

    return { width: wantedWidth + dw, height: wantedHeight + dh };
}

// What to run once a game has been copied, or nothing.
//
// Only when the disc knows exactly one thing to run. Several candidates is a
// question nobody has answered yet, and guessing which of six executables
// starts a game is how somebody ends up running an uninstaller.
function whatToRunAfterCopying(game, destination) {
    if (!game || !game.plays || String(game.plays).length === 0) {
        return null;
    }

    return onTheDisc(destination, game.plays);
}

// Everything that has to be copied, as a flat list with sizes.
//
// Gathered before anything is copied, because the bar needs to know the whole
// before it can say how far along it is, and because a folder that cannot be
// read is better found now than half way through.
function whatThereIsToCopy(files, from, notTheGame) {
    var found = [];

    // A game that is the only thing on a disc sits at the root, beside the
    // menu, the autorun and the checksums. Copying it should not carry those
    // onto somebody's hard disk.
    function oursRatherThanTheirs(name) {
        var at;

        if (!notTheGame) {
            return false;
        }

        for (at = 0; at < notTheGame.length; at++) {
            if (String(name).toLowerCase() === String(notTheGame[at]).toLowerCase()) {
                return true;
            }
        }

        return false;
    }

    function walk(folder, under) {
        var subs, items;

        try {
            items = new Enumerator(folder.Files);
        } catch (cannotRead) {
            return;
        }

        for (; !items.atEnd(); items.moveNext()) {
            if (under.length === 0 && oursRatherThanTheirs(items.item().Name)) {
                continue;
            }

            found.push({
                from: items.item().Path,
                to: under.length > 0 ? under + BACKSLASH + items.item().Name : items.item().Name,
                bytes: items.item().Size
            });
        }

        subs = new Enumerator(folder.SubFolders);

        for (; !subs.atEnd(); subs.moveNext()) {
            if (under.length === 0 && oursRatherThanTheirs(subs.item().Name)) {
                continue;
            }

            walk(subs.item(), under.length > 0 ? under + BACKSLASH + subs.item().Name : subs.item().Name);
        }
    }

    try {
        walk(files.GetFolder(from), "");
    } catch (noSuchFolder) {
        return [];
    }

    return found;
}

// The sum of a list from whatThereIsToCopy.
function howMuchInAll(items) {
    var total = 0;

    for (var at = 0; at < items.length; at++) {
        total += items[at].bytes;
    }

    return total;
}

// Whether a copy finished with everything the same size it started.
//
// Not a hash: a disc menu has no business reading several gigabytes twice, and
// the thing this catches is the copy that stopped half way, which is what
// happens when somebody takes the disc out.
function everythingArrived(files, items, destination) {
    for (var at = 0; at < items.length; at++) {
        var landed = onTheDisc(destination, items[at].to.split(BACKSLASH).join("/"));

        try {
            if (!files.FileExists(landed) || files.GetFile(landed).Size !== items[at].bytes) {
                return items[at].to;
            }
        } catch (cannotTell) {
            return items[at].to;
        }
    }

    return null;
}

// A name short enough to sit on a button.
//
// An add-on is named after the file it came from, and a GOG installer's name
// carries its version and its build number: "Grim Fandango Remastered
// Soundtrack 2.0.0.9 (31045)" is fifty characters for a button that is one of
// three. Cut, the buttons are the same size and the list reads as a list.
//
// Cut at a word where there is one, because a name chopped mid-word looks
// like a fault rather than a choice.
function shortEnoughForAButton(name, room) {
    var text = String(name || "");
    var most = room || 26;
    var cut;

    if (text.length <= most) {
        return text;
    }

    cut = text.substring(0, most);

    // Back to the last space, unless that throws most of it away.
    if (cut.lastIndexOf(" ") > most / 2) {
        cut = cut.substring(0, cut.lastIndexOf(" "));
    }

    // Trailing punctuation before an ellipsis reads as a typo.
    while (cut.length > 0 && " .,-(".indexOf(cut.charAt(cut.length - 1)) >= 0) {
        cut = cut.substring(0, cut.length - 1);
    }

    return cut + "...";
}

// Whether this game is on the machine the disc is in.
//
// Asked about the name it was REGISTERED under, which is not always the name on
// the disc: the folder may have been renamed, and the disc remembers both.
function isInstalled(game) {
    if (!game) {
        return false;
    }

    return whereTheGameIs(game.match || game.name) !== null;
}

// ===========================================================================
// A set: putting the game back together.
//
// A game bigger than one disc is spread across several, and no disc in a set
// can install it on its own. Every disc carries "Disc set.txt", which names
// every file on every disc with its size and hash, and any cut file with the
// pieces it goes back together from. The menu reads that file once, copies
// each disc's share into one folder on the hard drive, asks for the next
// disc, joins the pieces after the last one, and then installs from the
// folder. From the person's side the button says "Install, disc 1 of 3" and
// the rest is the installer's business, which is how a game on several discs
// always worked.
//
// Nothing in here touches the page. The engine takes callbacks for what to
// say and what to do when it is done, so cscript can run it against real
// folders and the page can draw it.
// ===========================================================================

// The rows of the set file: every file on every disc.
//
// A row is a line that starts with a digit and holds a star, which is the
// rule all three readers of the file keep. Everything after the star is the
// name, because a file name can hold spaces and splitting on them would cut
// it in half.
function setRowsFrom(text) {
    var rows = [];
    var lines = String(text || "").split("\n");
    var i, line, star, head, k, kept;

    for (i = 0; i < lines.length; i++) {
        line = lines[i].replace(/\r$/, "");

        if (line.length === 0 || line.charAt(0) < "0" || line.charAt(0) > "9") {
            continue;
        }

        star = line.indexOf("*");

        if (star < 0) {
            continue;
        }

        head = line.substring(0, star).split(" ");
        kept = [];

        for (k = 0; k < head.length; k++) {
            if (head[k].length) {
                kept.push(head[k]);
            }
        }

        if (kept.length < 3) {
            continue;
        }

        rows.push({
            disc: parseInt(kept[0], 10),
            sha256: kept[1],
            bytes: parseFloat(kept[2]),
            path: line.substring(star + 1)
        });
    }

    return rows;
}

// The files that were cut to fit, and the pieces each goes back together
// from, read off the JOIN and PART lines the row parser walks past because
// they start with a letter.
//
//   JOIN <sha256 of the finished file> <bytes> *name
//   PART <n> *name.dwpart01
function setJoinsFrom(text) {
    var joins = [];
    var lines = String(text || "").split("\n");
    var i, line, star, head, bits, k, current = null;

    for (i = 0; i < lines.length; i++) {
        line = lines[i].replace(/\r$/, "");
        star = line.indexOf("*");

        if (star < 0) {
            continue;
        }

        head = line.substring(0, star);

        if (head.substring(0, 5) === "JOIN ") {
            bits = [];
            head = head.substring(5).split(" ");

            for (k = 0; k < head.length; k++) {
                if (head[k].length) {
                    bits.push(head[k]);
                }
            }

            if (bits.length < 2) {
                continue;
            }

            current = { path: line.substring(star + 1), sha256: bits[0], bytes: parseFloat(bits[1]), pieces: [] };
            joins.push(current);
        } else if (head.substring(0, 5) === "PART " && current) {
            current.pieces.push(line.substring(star + 1));
        }
    }

    return joins;
}

// Which disc of the set a set file belongs to, from its own "disc N of M"
// line, or 0 when it has none. Read off the disc rather than baked into the
// menu, because one menu drives the whole set: disc 1's page stays open while
// disc 2 goes into the drive, and the only thing it asks the drive is which
// disc is in it.
function whichDiscTheSetFileSays(text) {
    var match = String(text || "").match(/^\s*disc\s+(\d+)\s+of\s+(\d+)\./im);

    return match ? parseInt(match[1], 10) : 0;
}

// How many discs the set file says there are, or 0.
function howManyDiscsTheSetFileSays(text) {
    var match = String(text || "").match(/^\s*disc\s+(\d+)\s+of\s+(\d+)\./im);

    return match ? parseInt(match[2], 10) : 0;
}

// Where the set stands in the folder it is being put back into.
//
// sizeOf answers a path relative to that folder with the file's size, or a
// negative number when the file is not there, so this can be asked of a
// real folder and of a written list alike.
function setScan(rows, joins, discIn, sizeOf) {
    var scan = {
        total: rows.length, ok: 0, missing: 0, bad: 0, need: [], pending: 0,
        complete: false, mineTotal: 0, mineHere: 0, bytes: 0, discs: 0
    };
    var joined = {};
    var needed = {};
    var i, k, row, got, all, join;

    // A piece whose finished file is already here is not missing: it was
    // used and deleted. And every piece here with the file not yet joined is
    // work still to do on this machine, not a disc still to come.
    for (i = 0; i < joins.length; i++) {
        join = joins[i];

        if (sizeOf(join.path) === join.bytes) {
            for (k = 0; k < join.pieces.length; k++) {
                joined[join.pieces[k]] = true;
            }
        } else {
            all = true;

            for (k = 0; k < join.pieces.length; k++) {
                if (sizeOf(join.pieces[k]) < 0) {
                    all = false;
                    break;
                }
            }

            if (all) {
                scan.pending++;
            }
        }
    }

    for (i = 0; i < rows.length; i++) {
        row = rows[i];
        scan.bytes += row.bytes;

        if (row.disc > scan.discs) {
            scan.discs = row.disc;
        }

        if (row.disc === discIn) {
            scan.mineTotal++;
        }

        if (joined[row.path]) {
            scan.ok++;

            if (row.disc === discIn) {
                scan.mineHere++;
            }

            continue;
        }

        got = sizeOf(row.path);

        if (got < 0) {
            scan.missing++;
            needed[row.disc] = true;
            continue;
        }

        // A half-copied file is the likeliest thing to go wrong, and it is a
        // different problem from a disc nobody has put in yet.
        if (got !== row.bytes) {
            scan.bad++;
            needed[row.disc] = true;
            continue;
        }

        scan.ok++;

        if (row.disc === discIn) {
            scan.mineHere++;
        }
    }

    for (i = 1; i <= scan.discs; i++) {
        if (needed[i]) {
            scan.need.push(i);
        }
    }

    scan.complete = scan.total > 0 && scan.ok === scan.total && scan.pending === 0;

    return scan;
}

// The rows that belong to one disc.
function setRowsOf(rows, disc) {
    var mine = [];

    for (var i = 0; i < rows.length; i++) {
        if (rows[i].disc === disc) {
            mine.push(rows[i]);
        }
    }

    return mine;
}

// What the one big button says on a disc of a set.
//
// "Install, disc 2 of 3" and nothing about copying, because from the
// person's side that is what it does: the copying is how the install works
// rather than what they came to do. "Install" alone once every file is in
// the folder, and "Play" once the game is on the machine.
function restoreButtonSays(scan, discIn, discsInSet, installed) {
    if (installed === true) {
        return "Play";
    }

    if (scan && scan.complete) {
        return "Install";
    }

    return "Install, disc " + (discIn > 0 ? discIn : 1) + " of " + discsInSet;
}

// One sentence about where the set stands, for the line under the buttons.
//
// It ends on what to do next rather than on a fact: "Now put disc 2 in" is
// the same information as "still needed: disc 2", said to somebody standing
// at a drive with a disc in their hand.
//
// busy says what is happening right now, when something is: copying is
// "3 of 9", joining is a file name, wanting is the disc being waited for.
function restoreSentence(scan, discIn, busy, folder) {
    var rest, i, line;

    busy = busy || {};

    if (busy.joining) {
        return "Putting " + busy.joining + " back together. On a large file this takes a few minutes.";
    }

    if (busy.copying) {
        return "Copying file " + busy.copying + " off this disc.";
    }

    if (busy.wanting) {
        return "Put disc " + busy.wanting + " in. This carries on by itself once it is there.";
    }

    if (!scan || !scan.total) {
        return "This disc says it is part of a set, but its list of files is missing.";
    }

    if (scan.complete) {
        return "Every file is here" + (folder ? ", in " + folder : "") + ". Install runs the installer from there.";
    }

    if (scan.pending && scan.ok === scan.total) {
        return "Every file is here. "
            + (scan.pending === 1
                ? "One file was cut to fit and still has to be put back together."
                : scan.pending + " files were cut to fit and still have to be put back together.");
    }

    if (scan.ok === 0 && scan.bad === 0) {
        line = "Nothing is copied yet.";
    } else {
        line = scan.ok + " of " + scan.total + " files copied so far.";

        if (scan.bad > 0) {
            line += " " + scan.bad + " copied badly and will be done again.";
        }
    }

    rest = [];

    for (i = 0; i < scan.need.length; i++) {
        if (scan.need[i] !== discIn) {
            rest.push(scan.need[i]);
        }
    }

    if (scan.need.length > rest.length) {
        // This disc is one of the ones wanted.
        if (rest.length === 0) {
            line += " Install copies this disc and finishes.";
        } else if (rest.length === 1) {
            line += " Install copies this disc, then asks for disc " + rest[0] + ".";
        } else {
            line += " Install copies this disc, then asks for the rest: discs " + rest.join(", ") + ".";
        }
    } else if (scan.need.length === 1) {
        line += " Now put disc " + scan.need[0] + " in.";
    } else if (scan.need.length > 1) {
        line += " Still to come: discs " + scan.need.join(", ") + ".";
    }

    return line;
}

// Where to put the game back together.
//
// Under the person's own profile when that drive has the room, the same
// place a game with nothing to install is copied to, because a folder
// somebody cannot write to half copies and stops. When it does not, the
// drive with the most room, because a set is by definition bigger than one
// disc. The name comes from the game and nothing else, so every disc of the
// set proposes the same folder without anything being remembered between
// them. Offered rather than asked, because asking is a dialog on every disc.
//
// drives is a list of {letter: "D:", free: bytes}, and home is the profile
// folder, so this can be worked out without a disk in a test.
function restoreFolderFor(home, name, drives, needBytes) {
    var folder = folderNameFor(name);
    var homeDrive = String(home || "").substring(0, 2).toUpperCase();
    var i, best = null;

    for (i = 0; i < drives.length; i++) {
        if (drives[i].letter.toUpperCase() === homeDrive && drives[i].free > needBytes) {
            return onTheDisc(onTheDisc(String(home), "Games"), folder);
        }
    }

    for (i = 0; i < drives.length; i++) {
        if (best === null || drives[i].free > best.free) {
            best = drives[i];
        }
    }

    if (best === null) {
        return onTheDisc(onTheDisc(String(home || ""), "Games"), folder);
    }

    return onTheDisc(onTheDisc(best.letter + BACKSLASH, "Games"), folder);
}

// The hard drives on this machine with how much room each has, for
// restoreFolderFor. Fixed drives only: a game put back onto a USB stick
// that is pulled out is a game gone.
function hardDrivesWithRoom(files) {
    var drives = [];
    var each, drive;

    try {
        each = new Enumerator(files.Drives);

        for (; !each.atEnd(); each.moveNext()) {
            drive = each.item();

            try {
                if (drive.DriveType === 2 && drive.IsReady) {
                    drives.push({ letter: String(drive.DriveLetter) + ":", free: drive.FreeSpace });
                }
            } catch (notReady) {
            }
        }
    } catch (noDrives) {
    }

    return drives;
}

// What the whole set needs before it is finished with, which is more than
// the discs hold: a cut file is written out whole while its pieces are all
// still there, so the high point is the set plus the largest finished file.
function setBytesNeeded(rows, joins) {
    var bytes = 0;
    var biggest = 0;
    var i;

    for (i = 0; i < rows.length; i++) {
        bytes += rows[i].bytes;
    }

    for (i = 0; i < joins.length; i++) {
        if (joins[i].bytes > biggest) {
            biggest = joins[i].bytes;
        }
    }

    return bytes + biggest;
}

// A path in quotes for the command line, which is not as simple as putting
// quotes round it. A backslash immediately before a closing quote escapes
// that quote, so a quoted drive root does not mean the root of the drive, it
// means a quote character and everything after it runs on into the same
// argument. Every disc is a drive root, so every real disc hits this.
// Doubling the trailing run is the documented escape.
function setQuote(path) {
    var quote = String.fromCharCode(34);
    var run = 0;
    var i, tail = "";

    for (i = path.length - 1; i >= 0 && path.charAt(i) === BACKSLASH; i--) {
        run++;
    }

    for (i = 0; i < run; i++) {
        tail += BACKSLASH;
    }

    return quote + path + tail + quote;
}

// The command that copies one file of the set off the disc.
//
// robocopy, one call per file, because the disc holds a subset of the game:
// copying the folder would bring AUTORUN, the set file and the icon along
// with it, and the installer has to run in a folder holding nothing but the
// game. /R:1 /W:1 so a scratched file fails in two seconds rather than in a
// million retries, /NJH /NJS because nothing reads its report.
function setCopyCommand(discRoot, dir, relativePath) {
    var parts = String(relativePath).split("/");
    var name = parts[parts.length - 1];
    var sub = [];
    var i;

    for (i = 0; i < parts.length - 1; i++) {
        sub.push(parts[i]);
    }

    var from = sub.length ? onTheDisc(discRoot, sub.join("/")) : discRoot;
    var to = sub.length ? onTheDisc(dir, sub.join("/")) : dir;

    return "robocopy " + setQuote(from) + " " + setQuote(to) + " " + setQuote(name) + " /R:1 /W:1 /NJH /NJS";
}

// The command that puts a cut file back together. copy /b is the whole
// mechanism: it is in Windows, it needs nothing installed, and it is what the
// set file tells somebody to type if they are doing this by hand.
function setJoinCommand(dir, join) {
    var names = [];

    for (var i = 0; i < join.pieces.length; i++) {
        names.push(setQuote(onTheDisc(dir, join.pieces[i])));
    }

    return "copy /b " + names.join("+") + " " + setQuote(onTheDisc(dir, join.path));
}

// ------------------------------------------------- running a command and waiting

var setJobNumber = 0;

// Starts a command with no window and nothing listening to it, and hands
// back a flag file to watch for.
//
// Not Exec, which shows a console on top of the menu and gives the child a
// pipe nobody reads: robocopy fills the pipe with its running percentage and
// then blocks for ever, measured at five minutes of a copy that had finished
// 692 MB in. Run with window style 0 is hidden and has no pipes. What it
// does not hand back is anything to poll, so the command writes its exit
// code into a small file when it is finished, and that file is what gets
// polled. Written under another name and renamed into place, because a
// redirection creates its file the instant it opens it and a poll landing
// in that window read a refused, empty flag as a failure.
function startAJob(command) {
    var shell = new ActiveXObject("WScript.Shell");
    var files = new ActiveXObject("Scripting.FileSystemObject");
    var flag = onTheDisc(
        shell.ExpandEnvironmentStrings("%TEMP%"),
        "dwjob" + (++setJobNumber) + "_" + (new Date()).getTime() + "_" + Math.floor(Math.random() * 1000000) + ".done");

    try { if (files.FileExists(flag)) { files.DeleteFile(flag); } } catch (stale) { }
    try { if (files.FileExists(flag + ".part")) { files.DeleteFile(flag + ".part"); } } catch (stalePart) { }

    // Wrapped in brackets before the ampersand, or a command with an operator
    // of its own takes the echo with it. call is what makes the exit code
    // survive being read on the same line it was set.
    shell.Run(
        "cmd /c (" + command + ") & call echo %^errorlevel%> " + setQuote(flag + ".part")
            + " & move /y " + setQuote(flag + ".part") + " " + setQuote(flag),
        0,
        false);

    return { flag: flag };
}

// Finished, rather than merely present: the rename is what says so, and a
// size of nothing is the one case the rename cannot speak for.
function jobIsDone(job) {
    try {
        var files = new ActiveXObject("Scripting.FileSystemObject");

        return files.FileExists(job.flag) && files.GetFile(job.flag).Size > 0;
    } catch (cannotTell) {
        return false;
    }
}

// What the command said on its way out, and then the flag is gone. Anything
// unreadable counts as a failure rather than as a nought, because the one
// thing worse than a copy that failed is a copy that failed quietly.
function jobCode(job) {
    var files = new ActiveXObject("Scripting.FileSystemObject");
    var code = 8;
    var reading, said, number;

    try {
        reading = files.OpenTextFile(job.flag, 1);
        said = reading.ReadAll();
        reading.Close();
        number = parseInt(said, 10);

        if (!isNaN(number)) {
            code = number;
        }
    } catch (unreadable) {
        code = 8;
    }

    try { files.DeleteFile(job.flag); } catch (gone) { }
    try { if (files.FileExists(job.flag + ".part")) { files.DeleteFile(job.flag + ".part"); } } catch (gonePart) { }

    return code;
}

// Waits for a job the way everything in here waits: on a timer where there
// is a window to keep alive, and in place where there is not. A window whose
// script is sitting in a blocking call cannot redraw and cannot see a disc
// being swapped, and on a real restore mshta answered a blocking copy with
// "Stop running this script?", which somebody reading it presses Yes to.
function waitForJob(job, after) {
    if (jobIsDone(job)) {
        after(jobCode(job));
        return;
    }

    if (typeof window !== "undefined" && window.setTimeout) {
        window.setTimeout(function () { waitForJob(job, after); }, 400);
        return;
    }

    // No window means cscript, which means a test. A test that waits for
    // ever on a job that never finishes is worse than one that fails, so
    // this one gives up after five minutes and says the job failed.
    var until = (new Date()).getTime() + 5 * 60 * 1000;

    while (!jobIsDone(job)) {
        if ((new Date()).getTime() > until) {
            after(8);
            return;
        }

        WScript.Sleep(50);
    }

    after(jobCode(job));
}

// ----------------------------------------------------------- the engine

// Makes a folder and every folder above it.
function makeTheWholePath(files, path) {
    var parent = files.GetParentFolderName(path);

    if (parent && parent.length > 0 && !files.FolderExists(parent)) {
        makeTheWholePath(files, parent);
    }

    if (!files.FolderExists(path)) {
        files.CreateFolder(path);
    }
}

// The size of a file under a folder, or -1 when it is not there, which is
// what setScan asks.
function sizeOfUnder(files, dir) {
    return function (relativePath) {
        try {
            var path = onTheDisc(dir, relativePath);

            return files.FileExists(path) ? files.GetFile(path).Size : -1;
        } catch (cannotTell) {
            return -1;
        }
    };
}

// Copies one disc's share of the set into the folder, one file at a time,
// each started and then waited for.
//
// work is {files, discRoot, dir, rows, discIn, say, after}. say is called
// with {copying: "3 of 9", done: bytes, all: bytes} as it goes, and after
// with how many files would not copy. Both questions are asked of every
// file, because neither answer is enough on its own: robocopy says 8 and up
// for a real failure, and a disc that goes unreadable part way leaves a
// short file that robocopy pre-sized to its full length.
function copyTheSetDisc(work) {
    var mine = setRowsOf(work.rows, work.discIn);
    var sizeOf = sizeOfUnder(work.files, work.dir);
    var state = { at: 0, bad: 0, done: 0, all: 0 };
    var i;

    for (i = 0; i < mine.length; i++) {
        state.all += mine[i].bytes;
    }

    function next() {
        var row;

        if (state.at >= mine.length) {
            work.say({ copying: "", done: state.done, all: state.all });
            work.after(state.bad);
            return;
        }

        row = mine[state.at];

        // Already here and the right size, from an earlier go at this disc.
        if (sizeOf(row.path) === row.bytes) {
            state.done += row.bytes;
            state.at++;
            next();
            return;
        }

        work.say({ copying: (state.at + 1) + " of " + mine.length, done: state.done, all: state.all });

        try {
            makeTheWholePath(work.files, work.files.GetParentFolderName(onTheDisc(work.dir, row.path)));
        } catch (cannotMake) {
        }

        waitForJob(startAJob(setCopyCommand(work.discRoot, work.dir, row.path)), function (code) {
            if (code >= 8 || sizeOf(row.path) !== row.bytes) {
                state.bad++;
            } else {
                state.done += row.bytes;
            }

            state.at++;
            next();
        });
    }

    next();
}

// Puts every cut file back together whose pieces are all here, one at a
// time, and deletes the pieces only once the finished file is the right
// size, because a join that half worked and then removed its own evidence is
// the one failure here that cannot be recovered from.
//
// work is {files, dir, joins, say, after}. after is called with the names of
// the files that could not be put back together.
function joinThePieces(work) {
    var sizeOf = sizeOfUnder(work.files, work.dir);
    var todo = [];
    var failed = [];
    var i, k, all, join;

    for (i = 0; i < work.joins.length; i++) {
        join = work.joins[i];

        if (sizeOf(join.path) === join.bytes) {
            continue;
        }

        all = true;

        for (k = 0; k < join.pieces.length; k++) {
            if (sizeOf(join.pieces[k]) < 0) {
                all = false;
                break;
            }
        }

        if (all) {
            todo.push(join);
        }
    }

    function next(at) {
        var piece;

        if (at >= todo.length) {
            work.say({ joining: "" });
            work.after(failed);
            return;
        }

        work.say({ joining: todo[at].path });

        waitForJob(startAJob(setJoinCommand(work.dir, todo[at])), function () {
            if (sizeOf(todo[at].path) === todo[at].bytes) {
                for (piece = 0; piece < todo[at].pieces.length; piece++) {
                    try { work.files.DeleteFile(onTheDisc(work.dir, todo[at].pieces[piece])); } catch (stillOpen) { }
                }
            } else {
                failed.push(todo[at].path);
            }

            next(at + 1);
        });
    }

    next(0);
}
