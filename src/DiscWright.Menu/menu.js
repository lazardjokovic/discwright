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
//
// And only files of 16 MB or more. The first files on a disc are the small
// ones, autorun.inf, the icon, the set's own list, and they are read while
// the drive is still spinning up: 60 KB in a second and a half is a rate of
// 40 KB a second, which says nothing about the drive and everything about
// the moment. At that rate a 4 GB file barely moves the bar, and on the
// owner's Dead Space set both big files sat at 0% until they were done.
function rateFrom(samples, seedRate) {
    var bytes = 0;
    var seconds = 0;

    for (var i = 0; i < samples.length; i++) {
        if (samples[i].seconds > 1 && samples[i].bytes >= ENOUGH_TO_TIME) {
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

// The smallest file whose time says anything about the drive: see rateFrom.
var ENOUGH_TO_TIME = 16777216;

// The guess to start with for a disc holding this many bytes. More than a
// CD can hold is a DVD, and a DVD drive reads several times faster than the
// 8x CD guess, which crawled across a DVD's big files: 4x DVD, 5.5 MB a
// second, which nearly every drive beats, so the bar is more likely to jump
// forward at the end of a file than to run ahead and wait.
function startingRateFor(bytesOnTheDisc) {
    return bytesOnTheDisc > 943718400 ? 5540000 : startingRate();
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

// How tall the list of games may be on the screen that picks one, in pixels.
//
// The panel is 480 high and never scrolls, and each game takes 47 of it, so
// a disc of more than seven games used to lose the rest off the bottom with
// no way to reach them. xniwo found it on 2.0.1, discwright#98, together
// with the screen having no Exit at all. The list now gets whatever is left
// above the Exit button, and scrolls when the games need more than that.
// Never less than one game's worth, so a heading that wraps over three lines
// still leaves something to pick.
function chooserListRoom(panelHeight, bottomPadding, listTop, belowTheList) {
    var room = panelHeight - bottomPadding - listTop - belowTheList;

    return room < 47 ? 47 : room;
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

    // NOTHING WAS PICKED TO START IT, so nothing is going to be started.
    // The menu copies the folder and opens it, and the window that built
    // the disc says exactly that when the question of which file starts the
    // game is cancelled. The button said "Copy and play" regardless, so the
    // disc promised a game it was never going to run and the two halves of
    // the program disagreed about the same disc. Seen on a real disc,
    // 2026-11-05, case 7 part 3.
    if (!game.plays || String(game.plays).length === 0) {
        return "Copy and open";
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

// A way into WMI that exists in the engine this menu actually runs in.
//
// NOT GetObject, AND THE REASON IS THE DOCUMENT MODE. menu.hta asks for
// IE=9, because mode 9 is the only one with rgba that still applies the
// disc's icon. GetObject is a JScript 5 host global and the mode 9 engine
// does not expose it: every call throws ReferenceError, 'GetObject' is
// undefined. ActiveXObject survives, which is what hid this, because the
// FileSystemObject a line below kept working.
//
// What that cost: whereTheGameIs threw on its first line every time and
// returned null, so no game was ever seen as installed, so the big button
// always said Install and every add-on stayed dead for ever. Found on a
// real disc on 2026-11-04, with Hollow Knight installed and registered
// and the menu still refusing to believe it.
function wmiFor(area) {
    return new ActiveXObject("WbemScripting.SWbemLocator")
        .ConnectServer(".", "root" + BACKSLASH + area);
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
        registry = wmiFor("default").Get("StdRegProv");
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

    // A disc at the top of its drive is "D:\", never "D:". To Windows, "D:"
    // means whichever folder is current on D, so a set copied robocopy "D:"
    // from D:\AUTORUN when somebody had opened the disc in Explorer and
    // double-clicked menu.hta there, and said its files "would not copy" and
    // the disc "may be scratched". The check and Copy and play read the same
    // root. AutoPlay starts the menu from the top of the disc, which is why
    // every disc burned so far installed. Found on the VM on 2026-11-12.
    if (/^[A-Za-z]:$/.test(cleaned)) {
        cleaned += BACKSLASH;
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
// A game's own folder on the disc.
//
// THE EMPTY ONE IS THE ORDINARY ONE. A game that is the only thing on a disc
// sits at the root, beside the menu and the autorun, and its folder is the
// empty string. onTheDisc answers an empty relative with an empty path, which
// is right for a file that is not there and wrong for the root itself: the
// copy was handed nothing, walked nothing, found nothing, and said "There is
// nothing in that folder to copy" about a disc with the game sitting on it.
//
// So no folder means the disc. Found on a real disc on 2026-11-05, by case 7.
function theGamesFolder(discFolder, folder) {
    return folder && String(folder).length > 0
        ? onTheDisc(discFolder, folder)
        : String(discFolder || "");
}

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
            from: theGamesFolder(discFolder, game.folder)
        };
    }

    // Nothing to install, so it gets copied off the disc and played from
    // where it landed. A game run from the disc cannot save: the disc is read
    // only, and the first time it tries to write a save it either fails or
    // silently writes nowhere.
    return { act: "copy", run: theGamesFolder(discFolder, game.folder), from: "" };
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

// ===========================================================================
// The patches fold into one Update button.
//
// A disc with four patches showed four buttons, and the person holding it
// had to know to press them oldest first: that ordering lived in a sort
// function and a hope. The owner settled it on 2026-11-09: one button runs
// the chain in order by itself, and everything that is not a patch keeps a
// button of its own. Patches the installer already covers never reach this
// file at all, the disc was written without them on the menu.
// ===========================================================================

// The patches of a game in the order they run: the front half of
// addOnsInOrder, which already puts them first and earliest first.
function theUpdateChain(addOns) {
    var ordered = addOnsInOrder(addOns || []), chain = [], i;

    for (i = 0; i < ordered.length; i++) {
        if (ordered[i].name.indexOf("Update ") === 0) {
            chain.push(ordered[i]);
        }
    }

    return chain;
}

// Everything that is not a patch, in the order somebody chose.
function theOtherAddOns(addOns) {
    var ordered = addOnsInOrder(addOns || []), others = [], i;

    for (i = 0; i < ordered.length; i++) {
        if (ordered[i].name.indexOf("Update ") !== 0) {
            others.push(ordered[i]);
        }
    }

    return others;
}

// What the one button says: the version the chain ends at, without the
// build number, which is bookkeeping nobody updates "to". Nothing when
// there is no chain and no button.
function whatTheUpdateButtonSays(addOns) {
    var chain = theUpdateChain(addOns), last, cut;

    if (chain.length === 0) {
        return null;
    }

    last = String(chain[chain.length - 1].name).substring("Update ".length);
    cut = last.indexOf(" (");

    if (cut > 0) {
        last = last.substring(0, cut);
    }

    return last.length > 0 ? "Update to " + last : "Update";
}

// What hovering the button says: the whole chain, in the order it runs.
function whatTheUpdateChainIs(addOns) {
    var chain = theUpdateChain(addOns), names = [], i;

    for (i = 0; i < chain.length; i++) {
        names.push(chain[i].name);
    }

    return names.join(", then ");
}

// The one command line that runs the whole chain in order, or nothing
// while the game is not installed, the same gate every add-on obeys.
//
// cmd does the sequencing, so the menu fires it and has no further part,
// the way it starts everything else: each installer is waited for by cmd,
// not by this page, and && stops the chain at the first patch whose own
// installer refuses. /s so the outer quotes come off exactly once however
// many quotes the paths add.
function theUpdateCommand(addOns, gameIsInstalled, discFolder) {
    var chain = theUpdateChain(addOns), paths = [], i;

    if (chain.length === 0 || !canRunAddOnYet(gameIsInstalled)) {
        return null;
    }

    for (i = 0; i < chain.length; i++) {
        paths.push('"' + onTheDisc(discFolder, chain[i].runs) + '"');
    }

    return 'cmd /s /c "' + paths.join(" && ") + '"';
}

// Starts a command line exactly as it stands, for the one button that
// runs more than one thing: startIt quotes a single path, and a composed
// line is already quoted piece by piece. The console window is hidden,
// because the windows worth seeing are the installers' own.
function startTheCommand(line) {
    var shell;
    var preview = whyNothingRuns();

    if (preview) {
        return preview;
    }

    if (!line || String(line).length === 0) {
        return "There is nothing on this disc for that button to start.";
    }

    try {
        shell = new ActiveXObject("WScript.Shell");
    } catch (noShell) {
        return "This machine will not let the menu start anything.";
    }

    try {
        shell.Run(String(line), 0, false);
        return null;
    } catch (wrong) {
        return "That would not start: " + line + " (" + wrong.message + ")";
    }
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

    // Looked for before anything runs. Handed a path that is not there,
    // Windows answered with "That would not start: ... (0)", a number that
    // means nothing to anybody, which is what the owner met pressing Manual
    // on disc 2 of his set on 2026-11-10.
    if (thereIsNo(what, false)) {
        return "That file is not where the menu expected it: " + what;
    }

    try {
        shell = new ActiveXObject("WScript.Shell");
    } catch (noShell) {
        return "This machine will not let the menu start anything.";
    }

    // The folder it starts in, and then straight back out of it. Left there,
    // the menu itself was standing in the copy of the installer, and Windows
    // will not remove a folder a running program is standing in: Remove the
    // copy took every file and then said "Permission denied" about the
    // folder, the owner's set on 2026-11-10. What was started keeps the
    // folder it was started in either way.
    var was = "";

    if (from && String(from).length > 0) {
        try {
            was = shell.CurrentDirectory;
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
    } finally {
        if (was.length > 0) {
            try {
                shell.CurrentDirectory = was;
            } catch (cannotGoBack) {
            }
        }
    }
}

// Whether a file, or a folder when folder is true, is certainly missing.
// False when it cannot be told, because a machine that will not let the menu
// look should still be allowed to try.
function thereIsNo(path, folder) {
    try {
        var files = new ActiveXObject("Scripting.FileSystemObject");

        return folder ? !files.FolderExists(String(path)) : !files.FileExists(String(path));
    } catch (cannotLook) {
        return false;
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

    // Explorer handed a folder that does not exist opens the person's
    // Documents instead, without a word. The owner pressed Extras on disc 2
    // on 2026-11-10 and got his own Documents, "opening something random,
    // which makes no sense". Looked for first, and said plainly.
    if (thereIsNo(where, true)) {
        return "That folder is not where the menu expected it: " + where;
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

// What to do once a game has finished copying.
//
// THE DECISION IS HERE AND THE PAGE ONLY ACTS ON IT. All of this used to be
// written out in the page, where no test could reach it, and the cost showed
// the first time it was judged: part 3 of case 7 says the menu copies the
// folder and opens it, and after the press there was nothing left to look at.
// The copy was there and nothing had started, which is two thirds of an
// answer, and whether Explorer ever opened could not be established at all.
// A decision that can be asked the question directly can be judged.
//
// Returns {act, what, say}. act is "stop" when the copy is not whole, "run"
// when the disc knows what starts the game, and "open" when it does not, which
// is the ordinary answer for a folder nobody picked a launcher out of. say is
// a warning to show first, or nothing.
function whatToDoAfterACopy(game, where, bad, stuck, missing) {
    var plays;

    // Not whole, so nothing is started on top of it. A half copied game that
    // runs is worse than one that does not: it fails somewhere later and
    // nobody connects it to the disc.
    if (bad > 0 || missing) {
        return {
            act: "stop",
            what: where,
            say: (missing
                ? "Some of it did not arrive, starting with " + missing + "."
                : bad + " of the files would not copy.")
                + "\n\nIt is in " + where + "."
        };
    }

    plays = whatToRunAfterCopying(game, where);

    return {
        act: plays ? "run" : "open",
        what: plays || where,

        // Said rather than swallowed. A game whose files are still read-only
        // runs and then cannot save, and that is a fault nobody would connect
        // to a disc they copied an hour earlier.
        say: stuck > 0
            ? stuck + " of the copied files are still read-only, so the game may not be able to save."
                + "\n\nRight click the folder, Properties, and clear Read-only: " + where
            : null
    };
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

// The rows that are the game, with the disc's own files taken out.
//
// The set file lists every file on every disc, the menu and the autorun
// included, because that is what it is for: any one disc can say what the
// whole set should be. None of that belongs in the folder the game is put
// back into. Found on the first real set: AUTORUN, Start Here.hta and
// autorun.inf would have been copied in beside the installer, and the
// installer has to run in a folder holding nothing but the game.
//
// ours is the list the disc carries of its own names, the same one that
// keeps them out of a copy of a game that stands alone.
function theGamesPartOf(rows, ours) {
    var kept = [];
    var i, k, top, mine;

    for (i = 0; i < rows.length; i++) {
        top = String(rows[i].path).split("/")[0].toLowerCase();
        mine = false;

        for (k = 0; k < (ours || []).length; k++) {
            if (String(ours[k]).toLowerCase() === top) {
                mine = true;
                break;
            }
        }

        if (!mine) {
            kept.push(rows[i]);
        }
    }

    return kept;
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
// verb is what the game's own button would say on one disc, "Install" for
// a game with an installer and "Copy and play" for a folder of files, so a
// folder of files split across discs says "Copy and play, disc 1 of 2" and
// not something about installing that never happens.
function restoreButtonSays(scan, discIn, discsInSet, installed, verb) {
    if (installed === true) {
        return "Play";
    }

    if (scan && scan.complete) {
        return verb || "Install";
    }

    // No comma: "Install disc 1 of 2", one thing to do, the way the owner
    // read it on 2026-11-10 and asked for it.
    return (verb || "Install") + " disc " + (discIn > 0 ? discIn : 1) + " of " + discsInSet;
}

// Opens the tray the disc is in, so that "put disc 2 in" arrives with the
// drive already open and disc 1 already out. Through Windows Media Player's
// control, which has been on every Windows since XP and is the one eject
// a script can ask for without a console. False when it could not, which
// is not a failure: the person opens the tray themselves, as before.
function ejectTheDisc(discRoot) {
    var letter = String(discRoot || "").substring(0, 2).toUpperCase();
    var player, drives, i, drive;

    if (!/^[A-Z]:$/.test(letter)) {
        return false;
    }

    try {
        player = new ActiveXObject("WMPlayer.OCX");
        drives = player.cdromCollection;

        for (i = 0; i < drives.count; i++) {
            drive = drives.item(i);

            if (String(drive.driveSpecifier).toUpperCase() === letter) {
                drive.eject();
                return true;
            }
        }
    } catch (noPlayer) {
    }

    return false;
}

// Whether a process of that name is running, which is how the menu knows
// the installer it started is still going. By name rather than by the
// process it started, because a GOG installer asks for elevation and the
// elevated process is not the one the menu was handed.
function installerIsRunning(exeName) {
    var wmi, found;

    try {
        wmi = wmiFor("cimv2");
        found = wmi.ExecQuery("SELECT ProcessId FROM Win32_Process WHERE Name = '" + String(exeName).replace(/'/g, "''") + "'");

        return found.Count > 0;
    } catch (cannotAsk) {
        return false;
    }
}

// When the menu stops waiting for the installer: once it has been seen
// running and is gone, or after five looks without ever seeing it, which
// is an installer that refused to start or finished inside ten seconds.
function whenTheInstallerIsDone(seenRunning, running, polls) {
    return !running && (seenRunning || polls >= 5);
}

// The line under the buttons while the installer runs, and after.
function whatInstallingSays(name) {
    return "Installing " + name + ". This menu waits for the installer and says when it is done.";
}

// copyBytes and folder say what the set left on the hard drive, when it
// left anything: the whole game, copied off the discs so that it could be
// installed. Installed, it is not needed any more, and the line says so
// beside a button that removes it. Nothing is removed without that press.
function whatTheInstallerLeft(installed, name, copyBytes, folder) {
    if (!installed) {
        return "The installer closed, and " + name + " is not on this machine. Install runs it again.";
    }

    if (copyBytes > 0 && folder) {
        return name + " is installed. Play starts it. The " + bytesInWords(copyBytes)
            + " copied to " + folder + " is not needed any more.";
    }

    return name + " is installed. Play starts it.";
}

// A size the way a person says it. Whole numbers of MB, one decimal of GB.
function bytesInWords(bytes) {
    if (bytes >= 1073741824) {
        return (Math.round(bytes / 107374182.4) / 10) + " GB";
    }

    if (bytes >= 1048576) {
        return Math.round(bytes / 1048576) + " MB";
    }

    if (bytes >= 1024) {
        return Math.round(bytes / 1024) + " KB";
    }

    return bytes + " bytes";
}

function removeTheCopyButtonSays(bytes) {
    return "Remove the copy, " + bytesInWords(bytes);
}

// Whether a folder is one this menu made and may remove: a drive root is
// not, the person's profile is not, and nothing is unless it is named the
// way the menu names one, "<game> installer" anywhere below a drive, or a
// folder directly under Games, where menus before 2026-11-10 put them. The
// guard between a button and somebody's files.
function mayRemoveTheCopy(dir) {
    var parts = String(dir || "").split("/").join(BACKSLASH).split(BACKSLASH);
    var last;

    while (parts.length && parts[parts.length - 1] === "") {
        parts.pop();
    }

    if (parts.length < 2) {
        return false;
    }

    last = parts[parts.length - 1];

    if (/ installer$/i.test(last) && last.length > " installer".length) {
        return true;
    }

    return parts.length >= 3 && parts[parts.length - 2].toLowerCase() === "games" && last.length > 0;
}

// Moves this program's own working folder out of dir when it is in it, to
// the folder above. Windows will not remove a folder a running program is
// standing in, and "Permission denied" about a folder already emptied is
// what the owner met on 2026-11-10. startIt no longer leaves the menu
// standing there; this is the second lock on the same door.
function stepOutOf(dir) {
    var shell, here, there, cut;

    try {
        shell = new ActiveXObject("WScript.Shell");
        here = String(shell.CurrentDirectory).toLowerCase();
        there = String(dir).toLowerCase();

        while (there.length > 3 && there.charAt(there.length - 1) === BACKSLASH) {
            there = there.substring(0, there.length - 1);
        }

        if (here === there || here.indexOf(there + BACKSLASH) === 0) {
            cut = String(dir).replace(/[\\\/]+$/, "").lastIndexOf(BACKSLASH);
            shell.CurrentDirectory = cut > 2 ? String(dir).substring(0, cut) : String(dir).substring(0, 3);
            return true;
        }
    } catch (cannotTell) {
    }

    return false;
}

// How much is in a folder, or 0 when it is not there.
function bytesIn(files, dir) {
    try {
        return files.FolderExists(dir) ? files.GetFolder(dir).Size : 0;
    } catch (cannotTell) {
        return 0;
    }
}

// Removes the copy, and says what went wrong rather than nothing. Forced,
// because the files came off a CD and are read-only.
function removeTheCopy(files, dir) {
    if (!mayRemoveTheCopy(dir)) {
        return "That is not a folder this menu made, so it is left alone: " + dir;
    }

    stepOutOf(dir);

    try {
        if (files.FolderExists(dir)) {
            files.DeleteFolder(dir, true);
        }

        return files.FolderExists(dir) ? "Some of it would not go. It may still be open in the installer: " + dir : null;
    } catch (wrong) {
        return "The copy could not be removed: " + wrong.message;
    }
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

    // Where it is going, named while it goes. The owner on 2026-11-10: "I
    // had no idea where it will be copied", and on somebody else's machine
    // it would be stranger still.
    // And how far, when the page knows: "file 2 of 2" sat still under a bar
    // the owner wanted a number beside on 2026-11-10.
    if (busy.copying) {
        return "Copying file " + busy.copying + " off this disc" + (folder ? " into " + folder : "")
            + (typeof busy.percent === "number" ? ", " + busy.percent + "%" : "") + ".";
    }

    // A disc just finished, said for a moment before the tray opens.
    if (busy.copied) {
        return busy.copied;
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

// How far the bar is along while a file copies.
//
// Nothing on a disc can see inside a copy that is happening: robocopy sizes
// the destination to its full length the moment it starts, so the file's
// size says nothing, and on the first real set the bar sat at nothing for
// four minutes under "Copying file 1 of 1" and then the disc was done. The
// same estimate the whole menu uses, then: the bytes finished, plus the
// seconds on the current file at the rate measured so far, and a guess of
// 8x CD before anything has been measured. Never 100.
//
// progress is what copyTheSetDisc says: done, all, startedAt in
// milliseconds and rate in bytes a second. now is the clock, handed in so a
// test can hold it still.
//
// UNLESS IT IS KNOWN. Since 2026-11-12 the copy asks Windows how many bytes
// robocopy has read (bytesReadSoFar), and when it has an answer the page is
// handed it as read, with the size of the file in flight as bytes. Then the
// number is exact: the files done, and as much of this one as has been read.
// Still never 100, for the same reason.
function restoreBarPercent(progress, now) {
    if (typeof progress.read === "number" && progress.read >= 0 && progress.all > 0) {
        var read = progress.read;

        if (typeof progress.bytes === "number" && read > progress.bytes) {
            read = progress.bytes;
        }

        var exact = Math.floor(((progress.done + read) * 100) / progress.all);

        return exact > 99 ? 99 : (exact < 0 ? 0 : exact);
    }

    var seconds = progress.startedAt > 0 ? Math.max(0, (now - progress.startedAt) / 1000) : 0;

    return percentDone(progress.done, seconds, progress.rate || startingRate(), progress.all);
}

// What the install page says before a set copies anything: where the
// installer is put back together, how much room that needs, and that the
// game's own installer asks about the game afterwards.
//
// A PAGE OF ITS OWN, since 2026-11-10, the owner's design after installing
// his Hollow Knight set: "since we have no idea to which PC the disc will be
// inserted, it should always ask where to install, like it was back in the
// day". The first answer was an OK / Cancel box, where Cancel had to mean
// "somewhere else", and the box said "copied" about something everybody
// thinks of as installing. There are two "where" questions for a set and
// this is the first of them: where the installer is joined. The game's own
// installer asks the second, so the page says so, or the same question would
// seem to come twice.
//
// scan is setScan's answer for that folder, free is the bytes free on its
// drive or -1 when that cannot be told, and discs is how many the set has.
// Returns {headline, says, go, note, short}: short is true when the drive
// cannot hold what is needed.
function installPageSays(name, folder, needBytes, free, scan, discs) {
    var game = String(name || "This game");
    var short = free >= 0 && free < needBytes;
    var room = "It needs " + bytesInWords(needBytes) + (free >= 0 ? ", and that drive has " + bytesInWords(free) + " free." : ".");
    var says, go;

    if (scan && scan.complete) {
        says = "A complete copy of the installer is already in " + folder + ". Install runs it from there.";
        go = "Install";
        short = false;
    } else if (scan && scan.ok > 0) {
        says = "Part of the installer is already copied to " + folder + ". Copying carries on there. " + room;
        go = "Copy and install";
    } else {
        says = game + " comes on " + discs + " discs. Its installer is copied off them and put back together in "
            + folder + ". " + room;
        go = "Copy and install";
    }

    if (short) {
        says += " That is not enough. Change the folder to a drive with more room.";
    }

    return {
        headline: "Where the installer goes",
        says: says,
        go: go,
        note: "Afterwards, " + game + "'s own installer asks where to install the game.",
        short: short
    };
}

// The line while the installer is being started, said before it is.
//
// Starting a 1.2 GB installer is not instant: Windows checks a file that
// size before it lets it run, and the page cannot redraw while it waits. On
// 2026-11-10 the owner looked at "Every file is here. Install runs the
// installer from there." under a grey button for twenty seconds, which reads
// like a button to press, and said a person would think it was stuck.
function startingTheInstallerSays(folder) {
    return "Starting the installer" + (folder ? " from " + folder : "")
        + ". Windows checks a file this size before it runs it, which can take a minute.";
}

// What the folder the installer is joined in is called: the game's name and
// the word installer, so nobody mistakes it for the game itself, and so the
// menu can tell a folder it made from somebody else's when it offers to
// remove the copy.
function installerFolderName(name) {
    return folderNameFor(name) + " installer";
}

// The line under the buttons once a disc of the set is copied, shown for a
// moment before the tray opens. A disc that copies in seconds, because the
// check had just read it and Windows still had it in memory, went straight
// to an open tray and read to the owner as nothing having happened.
function discCopiedSays(number, bytes, nextDisc) {
    return "Disc " + number + " is copied, " + bytesInWords(bytes) + "."
        + (nextDisc ? " Opening the tray for disc " + nextDisc + "." : "");
}

// Where to suggest putting the installer back together.
//
// The fixed drive with the most room, in a folder named after the game and
// the word installer. Not the person's profile and not Program Files: the
// disc goes into machines nobody here has seen, a set is by definition bigger
// than one disc, and Program Files wants an administrator. The name comes
// from the game and nothing else, so every disc of the set proposes the same
// folder without anything being remembered between them. It is only a
// suggestion: the install page shows it and Change folder picks anywhere.
//
// drives is a list of {letter: "D:", free: bytes}, and home is the profile
// folder, the answer when no drive can be read, so this can be worked out
// without a disk in a test.
function restoreFolderFor(home, name, drives, needBytes) {
    var folder = installerFolderName(name);
    var i, best = null;

    for (i = 0; i < drives.length; i++) {
        if (best === null || drives[i].free > best.free) {
            best = drives[i];
        }
    }

    if (best === null) {
        return onTheDisc(String(home || ""), folder);
    }

    return onTheDisc(best.letter + BACKSLASH, folder);
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

// How many bytes the program a job started has read so far, or -1 when that
// cannot be told.
//
// Nothing on a disc can see inside a copy that is happening, which is why the
// bar used to be an estimate: robocopy sizes the destination to its full
// length the moment it starts and holds its own percentages back until it
// ends when its output goes to a file. But Windows counts the bytes every
// process reads, and WMI will say how many. Measured on the test machine on
// 2026-11-12: a 400 MB robocopy read 65, 129, 194, 258, 322 and 386 MB at
// one second apart. certutil reads its file through a mapping that the count
// does not see, so the check cannot do this and stays an estimate.
//
// A job is started through cmd with no handle on it, so the program is found
// by its parent: the one cmd whose command line names this job's flag file,
// and its child of that name. Found once and remembered on the job. Any
// failure at all, WMI missing or refused or the program already gone, is -1,
// and the caller goes on estimating.
function bytesReadSoFar(job, program) {
    try {
        // Through wmiFor, never GetObject, which the menu's engine does not
        // have: see wmiFor.
        var wmi = wmiFor("cimv2");
        var name, parents, children, found;

        if (!job.reader) {
            name = String(job.flag).split(BACKSLASH).join("/").split("/").pop();
            parents = new Enumerator(wmi.ExecQuery(
                "SELECT ProcessId FROM Win32_Process WHERE Name = 'cmd.exe' AND CommandLine LIKE '%" + name + "%'"));

            for (; !parents.atEnd() && !job.reader; parents.moveNext()) {
                children = new Enumerator(wmi.ExecQuery(
                    "SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = " + parents.item().ProcessId
                        + " AND Name = '" + program + "'"));

                if (!children.atEnd()) {
                    job.reader = children.item().ProcessId;
                }
            }

            if (!job.reader) {
                return -1;
            }
        }

        found = new Enumerator(wmi.ExecQuery(
            "SELECT ReadTransferCount FROM Win32_Process WHERE ProcessId = " + job.reader));

        return found.atEnd() ? -1 : Number(found.item().ReadTransferCount);
    } catch (cannotTell) {
        return -1;
    }
}

// Looks at whatever disc is in the drive without the window waiting for it.
//
// While a set waits for its next disc, the menu asks every second and a half
// which disc is in, by reading the set file at the top of it. Read from the
// page, that read is the window's own: a disc just pushed in spins up for
// seconds before Windows answers, and the window is frozen until it does. On
// the owner's laptop, on 2026-11-12, the menu went "Not responding" for a
// moment when disc 2 of his Dead Space set went in.
//
// So the read is a job, like every copy: cmd copies the set file into the
// temp folder, and the window only ever reads that copy, off the hard drive,
// once the job is done. A drive taking its time holds the job, never the
// window. Hands back {job, copy} for whatTheLookFound.
function lookAtTheDisc(discRoot) {
    var shell = new ActiveXObject("WScript.Shell");
    var copy = onTheDisc(
        shell.ExpandEnvironmentStrings("%TEMP%"),
        "dwlook_" + (new Date()).getTime() + "_" + Math.floor(Math.random() * 1000000) + ".txt");

    return {
        job: startAJob("copy /y " + setQuote(onTheDisc(discRoot, "Disc set.txt")) + " " + setQuote(copy) + " >nul"),
        copy: copy
    };
}

// What a look found: the set file's text, or nothing when there was no disc,
// no set file on it or no copy. Only asked once the job is done, and it
// tidies up after itself, the copy and the job's flag both.
function whatTheLookFound(look) {
    var files = new ActiveXObject("Scripting.FileSystemObject");
    var text = "";

    jobCode(look.job);

    try {
        if (files.FileExists(look.copy)) {
            var reading = files.OpenTextFile(look.copy, 1);
            text = reading.AtEndOfStream ? "" : reading.ReadAll();
            reading.Close();
        }
    } catch (unreadable) {
        text = "";
    }

    try { if (files.FileExists(look.copy)) { files.DeleteFile(look.copy, true); } } catch (left) { }

    return text;
}

// Waits for a job the way everything in here waits: on a timer where there
// is a window to keep alive, and in place where there is not. A window whose
// script is sitting in a blocking call cannot redraw and cannot see a disc
// being swapped, and on a real restore mshta answered a blocking copy with
// "Stop running this script?", which somebody reading it presses Yes to.
//
// meanwhile, when given, is called on every look that finds the job still
// running, so that something can be said while one long file works: the
// disc check's percentage moves inside a file because of it.
function waitForJob(job, after, meanwhile) {
    if (jobIsDone(job)) {
        after(jobCode(job));
        return;
    }

    if (typeof window !== "undefined" && window.setTimeout) {
        if (meanwhile) {
            meanwhile();
        }

        window.setTimeout(function () { waitForJob(job, after, meanwhile); }, 400);
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

// Takes the read-only off a file that has just been copied.
//
// A file on a CD is read-only, and every way of copying one carries the
// attribute across: robocopy does, and so does FileSystemObject.CopyFile.
//
// WHICH DEFEATS THE WHOLE POINT OF COPYING IT. The comment above
// whatToDoWith says why that button exists: a game run off a disc cannot
// save, because the disc is read-only and the first thing it writes either
// fails or goes nowhere. The copy was read-only too, so a game copied onto
// the machine so that it could save still could not save over anything that
// came off the disc. Three comments in this file knew that files off a CD
// arrive read-only and none of them did anything about it.
//
// Not fatal when it fails: a game that cannot save is worse than a game
// whose attribute would not change, and the copy is there either way.
function makeItWritable(files, path) {
    try {
        var file = files.GetFile(path);

        // Bit 1 is read-only, and it is cleared rather than the whole set
        // being replaced, because hidden and system belong to whoever made
        // the file and are none of this menu's business.
        if (file.Attributes & 1) {
            file.Attributes = file.Attributes - 1;
        }

        return null;
    } catch (wrong) {
        return String(path);
    }
}

// Copies a game off a disc onto the machine, one file at a time, WITHOUT
// holding on to the thread while each one goes.
//
// WHY A JOB AND NOT CopyFile. FileSystemObject.CopyFile does not return
// until the file is there, and a script that has not returned cannot repaint
// its own window. A game that is one large file therefore froze the bar at
// whatever the file before it had reached, and Windows painted the menu with
// the not-responding ghost: the hung-program treatment, on the one screen
// whose entire job is to say that something is happening. Watched on a real
// disc on 2026-11-05, 126 MB in one file, case 7.
//
// The comment this replaces said one file per tick was enough to keep the
// window alive. It is not, and the set restore has known so for longer:
// "a bar that only moves when a file finishes is a bar that never moves on a"
// "disc with one file on it". This is the same machinery that fixed it there,
// finally brought across, so the program has one way of copying rather than
// a good one and a bad one.
//
// work is {files, items, from, destination, say, after}. items is what
// whatThereIsToCopy gathered, from is the folder on the disc they came out
// of, say is called with {copying, done, all, startedAt, rate} as it goes,
// and after with how many files would not copy and how many would not be
// made writable.
function copyAGameOff(work) {
    var sizeOf = sizeOfUnder(work.files, work.destination);
    var state = { at: 0, bad: 0, stuck: 0, done: 0, all: howMuchInAll(work.items), startedAt: 0, samples: [] };

    function next() {
        var one, relative, landing;

        if (state.at >= work.items.length) {
            work.say({ copying: "", done: state.done, all: state.all });
            work.after(state.bad, state.stuck);
            return;
        }

        one = work.items[state.at];

        // The list carries Windows separators because it is read back off a
        // disk; everything that builds a path here speaks in forward slashes.
        relative = String(one.to).split(BACKSLASH).join("/");
        landing = onTheDisc(work.destination, relative);

        state.startedAt = (new Date()).getTime();

        var saying = {
            copying: (state.at + 1) + " of " + work.items.length,
            done: state.done,
            all: state.all,
            startedAt: state.startedAt,
            rate: rateFrom(state.samples, startingRateFor(state.all))
        };

        work.say(saying);

        try {
            makeTheWholePath(work.files, work.files.GetParentFolderName(landing));
        } catch (cannotMake) {
        }

        var job = startAJob(setCopyCommand(work.from, work.destination, relative));

        waitForJob(job, function (code) {
            // Both questions, as the set copy asks them: robocopy says 8 and
            // up for a failure and the size says whether the bytes are there,
            // and neither answer is enough on its own.
            if (code >= 8 || sizeOf(relative) !== one.bytes) {
                state.bad++;
            } else {
                state.done += one.bytes;
                state.samples.push({ bytes: one.bytes, seconds: ((new Date()).getTime() - state.startedAt) / 1000 });

                if (makeItWritable(work.files, landing)) {
                    state.stuck++;
                }
            }

            state.at++;
            next();
        }, howMuchHasBeenRead(job, saying, one.bytes, work.say));
    }

    next();
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
    var state = { at: 0, bad: 0, done: 0, all: 0, startedAt: 0, samples: [] };
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

        state.startedAt = (new Date()).getTime();

        var saying = {
            copying: (state.at + 1) + " of " + mine.length,
            done: state.done,
            all: state.all,
            startedAt: state.startedAt,
            rate: rateFrom(state.samples, startingRateFor(state.all))
        };

        work.say(saying);

        try {
            makeTheWholePath(work.files, work.files.GetParentFolderName(onTheDisc(work.dir, row.path)));
        } catch (cannotMake) {
        }

        var job = startAJob(setCopyCommand(work.discRoot, work.dir, row.path));

        waitForJob(job, function (code) {
            if (code >= 8 || sizeOf(row.path) !== row.bytes) {
                state.bad++;
            } else {
                state.done += row.bytes;
                state.samples.push({ bytes: row.bytes, seconds: ((new Date()).getTime() - state.startedAt) / 1000 });
            }

            state.at++;
            next();
        }, howMuchHasBeenRead(job, saying, row.bytes, work.say));
    }

    next();
}

// What a copy does while one file works: about once a second, it asks how
// much robocopy has read and, when that can be told, says the same progress
// again with read and the file's size in it, so the bar is drawn from the
// bytes rather than the clock. Every other look, because a look comes every
// 400 ms and a WMI question is not free.
function howMuchHasBeenRead(job, saying, bytes, say) {
    var looks = 0;

    return function () {
        looks++;

        if (looks % 2 !== 0) {
            return;
        }

        var read = bytesReadSoFar(job, "robocopy.exe");

        if (read >= 0) {
            say({
                copying: saying.copying,
                done: saying.done,
                all: saying.all,
                startedAt: saying.startedAt,
                rate: saying.rate,
                read: read,
                bytes: bytes
            });
        }
    };
}

// Puts every cut file back together whose pieces are all here, one at a
// time, and deletes the pieces only once the finished file is the right
// size, because a join that half worked and then removed its own evidence is
// the one failure here that cannot be recovered from.
//
// The delete is forced, because a file copied off a CD arrives read-only,
// robocopy carries the attribute over, and DeleteFile without the force flag
// refuses a read-only file. On the first real set the join was perfect and
// both pieces stayed behind, 1.2 GB of them.
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
                    try { work.files.DeleteFile(onTheDisc(work.dir, todo[at].pieces[piece]), true); } catch (stillOpen) { }
                }
            } else {
                failed.push(todo[at].path);
            }

            next(at + 1);
        });
    }

    next(0);
}

// ------------------------------------------------- checking the disc itself
//
// A disc built with checksums carries checksums.sha256 at its root, in
// sha256sum format, which is the same list a person could check by hand with
// sha256sum or Get-FileHash. This checks it without either. xniwo, 4 October:
// "Next to the mute music icon in the splash menu, an optional one click
// windows or linux sha256 verification might be useful for some."
//
// The disc, not a copy of it: this answers "is this disc still good", which
// is a different question from the set's own checking, and the one somebody
// asks with a disc in their hand years later.

// The list, read out of the text the disc carries. The declared count is
// kept to be CHECKED, not only read: a list that has lost its tail verifies
// the files it still mentions and reports every one of them right, which is
// the single way a checksum list can lie to you.
function sumsFrom(text) {
    var out = { rows: [], declared: -1 };
    var lines = String(text || "").split("\n");
    var i, line, star, said;

    for (i = 0; i < lines.length; i++) {
        line = lines[i];

        if (line.length > 0 && line.charAt(line.length - 1) === "\r") {
            line = line.substring(0, line.length - 1);
        }

        if (line.length === 0) {
            continue;
        }

        if (line.charAt(0) === "#") {
            said = line.match(/^#\s+(\d+)\s+files\b/);

            if (said) {
                out.declared = parseInt(said[1], 10);
            }

            continue;
        }

        // A hash is 64 characters and the star is sha256sum's mark for
        // binary mode, so anything shorter in front of " *" is not a row.
        star = line.indexOf(" *");

        if (star < 64) {
            continue;
        }

        out.rows.push({ hash: line.substring(0, star), path: line.substring(star + 2) });
    }

    return out;
}

// What is wrong with the list itself, or nothing when it can be relied on.
function sumsTrouble(sums) {
    if (sums.rows.length === 0) {
        return "This disc does not carry a checksum list, so there is nothing to check against.";
    }

    if (sums.declared >= 0 && sums.declared !== sums.rows.length) {
        return "The checksum list on this disc says it covers " + sums.declared
            + " files and lists " + sums.rows.length
            + ". It is damaged, so nothing it says about this disc can be relied on.";
    }

    return "";
}

// certutil prints the hash on a line of its own, with no spaces in it.
function hashOutOf(text) {
    var lines = String(text || "").split("\n");
    var i, line;

    for (i = 0; i < lines.length; i++) {
        line = lines[i];

        if (line.length > 0 && line.charAt(line.length - 1) === "\r") {
            line = line.substring(0, line.length - 1);
        }

        if (line.length >= 64 && line.indexOf(" ") < 0) {
            return line;
        }
    }

    return "";
}

// Which file the check is on, because minutes of a still window is how
// somebody decides it has hung.
//
// THE PERCENTAGE LEADS. On 2026-11-10 the owner watched "Checking file 39 of
// 39" sit still for minutes: the last file was the second piece of a cut
// installer, hundreds of megabytes, and a count of files cannot move inside
// one. It read as stuck. The percentage is of the bytes, estimated inside a
// file the same way the copy's bar is, so it keeps moving to the end.
function checkStep(at, of, percent) {
    return "Checking the disc, " + (percent || 0) + "%: file " + at + " of " + of + " against what was burned.";
}

// How far the check is through the disc's bytes: exact for the files done,
// and the one being hashed estimated at rate, in bytes a second. Times are in
// milliseconds and handed in, so a test can hold the clock still.
//
// The rate used to be everything done so far over the time it took, which on
// a disc whose first files are tiny is the drive spinning up: 60 KB in a
// second and a half. The owner's Dead Space check sat at 0% through both of
// its big files that way. checkTheDisc now hands in rateFrom's answer, from
// files big enough to time, or the guess for a disc that size.
function checkPercent(done, all, rate, fileBegan, now) {
    return percentDone(done, Math.max(0, (now - fileBegan) / 1000), rate, all);
}

// The verdict, once every file has been looked at.
function checkVerdict(listed, bad) {
    if (bad.length === 0) {
        return listed + " files checked. Every one of them is byte for byte what was burned.";
    }

    return bad.length + " of " + listed + " files on this disc are wrong or missing:\n\n"
        + bad.join("\n")
        + "\n\nA disc that reads badly is usually scratched or was burned badly.";
}

// The check itself: one certutil at a time, the same way the set copies one
// file at a time, so the window stays answerable between files. Progress
// goes through saying and the verdict through done, because nothing in this
// file may touch a document.
function checkTheDisc(dir, sums, saying, done) {
    var files = new ActiveXObject("Scripting.FileSystemObject");
    var shell = new ActiveXObject("WScript.Shell");
    var here = [];
    var gone = [];
    var i, full;

    var all = 0;
    var hashed = 0;
    var samples = [];
    var size;

    for (i = 0; i < sums.rows.length; i++) {
        full = onTheDisc(dir, sums.rows[i].path);

        if (files.FileExists(full)) {
            try {
                size = files.GetFile(full).Size;
            } catch (cannotSize) {
                size = 0;
            }

            all += size;
            here.push({ hash: sums.rows[i].hash, full: full, path: sums.rows[i].path, bytes: size });
        } else {
            gone.push(sums.rows[i].path);
        }
    }

    function howFar(fileBegan) {
        return checkPercent(hashed, all, rateFrom(samples, startingRateFor(all)), fileBegan, (new Date()).getTime());
    }

    var tmp = onTheDisc(
        shell.ExpandEnvironmentStrings("%TEMP%"),
        "dwcheck_" + (new Date()).getTime() + "_" + Math.floor(Math.random() * 1000000) + ".txt");

    function walk(at, bad) {
        if (at >= here.length) {
            try { if (files.FileExists(tmp)) { files.DeleteFile(tmp); } } catch (left) { }
            done(checkVerdict(sums.rows.length, bad.concat(gone)));
            return;
        }

        var fileBegan = (new Date()).getTime();

        saying(checkStep(at + 1, here.length, howFar(fileBegan)));

        waitForJob(
            startAJob("certutil -hashfile " + setQuote(here[at].full) + " SHA256 > " + setQuote(tmp)),
            function () {
                hashed += here[at].bytes;
                samples.push({ bytes: here[at].bytes, seconds: ((new Date()).getTime() - fileBegan) / 1000 });
                var said = "";
                var reading;

                try {
                    reading = files.OpenTextFile(tmp, 1);
                    said = reading.ReadAll();
                    reading.Close();
                } catch (unreadable) {
                    said = "";
                }

                var hash = hashOutOf(said);

                if (hash.length === 0 || hash.toLowerCase() !== String(here[at].hash).toLowerCase()) {
                    bad.push(here[at].path);
                }

                walk(at + 1, bad);
            },
            function () {
                saying(checkStep(at + 1, here.length, howFar(fileBegan)));
            });
    }

    walk(0, []);
}
