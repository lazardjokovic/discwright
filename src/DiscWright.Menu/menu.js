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

// Which discs of a set are still wanted.
//
// Given the disc numbers already copied and how many there are in all, returns
// the ones missing, in order. The menu turns this into "insert disc 2", and
// into the line that says what is still needed when somebody puts in the wrong
// one.
function discsStillWanted(discsCopied, discsInSet) {
    var wanted = [];

    for (var number = 1; number <= discsInSet; number++) {
        var found = false;

        for (var i = 0; i < discsCopied.length; i++) {
            if (discsCopied[i] === number) {
                found = true;
            }
        }

        if (!found) {
            wanted.push(number);
        }
    }

    return wanted;
}

// What to say about what is still missing. Written here rather than in the HTA
// so the wording is tested with the arithmetic that decides it.
function whatIsStillWanted(discsCopied, discsInSet) {
    var wanted = discsStillWanted(discsCopied, discsInSet);

    if (wanted.length === 0) {
        return "";
    }

    if (wanted.length === 1) {
        return "Insert disc " + wanted[0] + ".";
    }

    if (wanted.length === 2) {
        return "Still needs discs " + wanted[0] + " and " + wanted[1] + ".";
    }

    var all = "";

    for (var i = 0; i < wanted.length - 1; i++) {
        if (all !== "") {
            all += ", ";
        }
        all += wanted[i];
    }

    return "Still needs discs " + all + " and " + wanted[wanted.length - 1] + ".";
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
