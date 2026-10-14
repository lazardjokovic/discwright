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
function whatToDoWith(game) {
    if (!game) {
        return "";
    }
    if (game.runs && game.runs.length > 0) {
        return "Install";
    }
    return "Copy and play";
}
