"""Plants a deliberate fault, runs the suite, and insists on being told what
happened.

Why this exists rather than a one-off loop each time: the obvious way to write
it is wrong twice over, and both ways were written and believed here.

The first was reading stdout only. xUnit writes "[FAIL]" to stderr, so every
mutant looked like a survivor and the whole suite looked worthless.

The second is subtler and is the reason this file is in the repository. Smart
App Control on this machine refuses to load a freshly rebuilt, unsigned test
assembly: "An Application Control policy has blocked this file" (0x800711C7).
The run never starts. There is no "[FAIL]" in the output, because there is no
output, and a harness looking only for failure calls that a survivor.

So a surviving mutant and a blocked run look exactly alike, and the harness
reported "nothing caught this" about a test that was never given the chance.

The fix is to refuse to draw a conclusion without evidence that the suite
actually ran. xUnit prints "Passed!" or "Failed!" with a count when it does.
Anything else is INCONCLUSIVE, which is retried, and said out loud if it never
resolves, rather than being quietly counted as bad news or good.

Two more, both learned the same way, by a run that sat for nineteen hours.

A fault need not make a test fail. It can stop one finishing, and a wait with
no bound on it looks exactly like a machine nobody is using. The suite now has
a deadline and a run past it is HUNG, which is a verdict rather than a silence.

And planting a fault must not change anything else about the file. Reading with
universal newlines and writing back plain ones quietly turned every file it
touched from CRLF to LF, against the eol=crlf this repository declares. The
ending a file arrived with is the ending it leaves with.
"""

import io
import os
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Proof that the suite ran at all.
RAN = ("Passed!", "Failed!")

# Proof that something objected.
OBJECTED = ("[FAIL]", ": error")

BLOCKED = ("0x800711C7", "Application Control policy")

# How long the suite may take before the run is called hung. It takes about a
# minute on this machine, so ten is generous and, unlike no limit at all, is a
# number.
HUNG_AFTER = 600


def run_once(env):
    """Runs the suite once. Returns what it said, or None when it hung."""
    started = subprocess.Popen(
        ["dotnet", "test", "--nologo", "-v", "q"],
        cwd=REPO, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
        text=True, errors="replace")

    try:
        return started.communicate(timeout=HUNG_AFTER)[0]
    except subprocess.TimeoutExpired:
        # Killing the child on its own leaves testhost holding the test
        # assembly, and the next build then fails on a locked file for a
        # reason that looks nothing like the real one.
        subprocess.run(["taskkill", "/F", "/T", "/PID", str(started.pid)],
                       capture_output=True)
        started.wait()

        return None


def run_the_suite(tries=4):
    """Runs the suite. 'caught', 'survived', 'hung' or 'inconclusive'."""
    env = dict(os.environ, DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")

    for attempt in range(tries):
        out = run_once(env)

        if out is None:
            return "hung"

        if any(word in out for word in OBJECTED):
            return "caught"

        if any(word in out for word in RAN):
            # It ran, it finished, and nothing objected.
            return "survived"

        # It did not run, which is nothing to do with the mutant. Until
        # 2026-10-16 this was usually Smart App Control refusing the rebuilt
        # assembly; the check stays, because a machine that has it on still
        # does exactly that.
        why = "blocked by Application Control" if any(w in out for w in BLOCKED) else "did not report a result"
        print("      attempt %d %s, trying again" % (attempt + 1, why))

    return "inconclusive"


def read_source(path):
    """The file as text, and the line ending it is written with.

    Read with universal newlines so a fault spanning two lines can be
    written with plain ones and still match, and the real ending handed
    back so putting the file down does not change it.
    """
    ending = "\r\n" if b"\r\n" in io.open(path, "rb").read() else "\n"

    return io.open(path, encoding="utf-8").read(), ending


def write_source(path, text, ending):
    """Puts the file back exactly as it was found, ending and all."""
    io.open(path, "w", encoding="utf-8", newline=ending).write(text)


# Written while a fault is in the tree and deleted the moment it is taken
# back out. The finally below handles Ctrl-C, but nothing in the process
# survives being killed outright, and a run stopped from outside has twice
# now left a deliberate fault behind in a file nobody was watching.
PLANTED = os.path.join(REPO, "tools", ".a-fault-is-planted")


def mutate(mutants):
    """Plants each fault in turn and says whether a test objected.

    Each mutant is (name, path, old, new).
    """
    if os.path.exists(PLANTED):
        print("A fault from an earlier run may still be in the tree:")
        print("  " + io.open(PLANTED, encoding="utf-8").read().strip())
        print("Put it back, delete " + PLANTED + ", and start again.")

        return 1

    survived = []
    unknown = []

    for name, path, old, new in mutants:
        was, ending = read_source(path)

        if old not in was:
            print("  ?? could not plant: " + name)
            unknown.append(name + " (could not plant)")
            continue

        io.open(PLANTED, "w", encoding="utf-8").write(path + nl_note(name))
        write_source(path, was.replace(old, new, 1), ending)

        try:
            verdict = run_the_suite()
        finally:
            # Always put it back, including on Ctrl-C. A mutant left in the
            # working tree is a deliberate fault nobody remembers planting.
            write_source(path, was, ending)
            if os.path.exists(PLANTED):
                os.remove(PLANTED)

        if verdict == "caught":
            print("  caught: " + name)
        elif verdict == "survived":
            print("  SURVIVED: " + name)
            survived.append(name)
        elif verdict == "hung":
            # Not a survivor and not a catch. A test that waits instead of
            # failing has to be fixed before this mutant means anything.
            print("  HUNG, no verdict: " + name)
            unknown.append(name + " (hung)")
        else:
            print("  INCONCLUSIVE, never ran: " + name)
            unknown.append(name)

    print("")
    if not survived and not unknown:
        print("all caught")
    else:
        if survived:
            print("SURVIVORS: " + "; ".join(survived))
        if unknown:
            print("NO ANSWER FOR: " + "; ".join(unknown))

    return 0 if not survived and not unknown else 1


def nl_note(name):
    """The fault, written where a reader will meet the file first."""
    return "\n  " + name + "\n"


if __name__ == "__main__":
    sys.exit("Import mutate() and hand it the faults to plant.")
