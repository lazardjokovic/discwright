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


def run_the_suite(tries=4):
    """Runs the suite. Returns 'caught', 'survived', or 'inconclusive'."""
    env = dict(os.environ, DOTNET_NOLOGO="1", DOTNET_CLI_TELEMETRY_OPTOUT="1")

    for attempt in range(tries):
        done = subprocess.run(
            ["dotnet", "test", "--nologo", "-v", "q"],
            cwd=REPO, env=env, capture_output=True, text=True, errors="replace")

        out = done.stdout + done.stderr

        if any(word in out for word in OBJECTED):
            return "caught"

        if any(word in out for word in RAN):
            # It ran, it finished, and nothing objected.
            return "survived"

        # It did not run. Usually Smart App Control refusing the rebuilt
        # assembly, which is nothing to do with the mutant.
        why = "blocked by Application Control" if any(w in out for w in BLOCKED) else "did not report a result"
        print("      attempt %d %s, trying again" % (attempt + 1, why))

    return "inconclusive"


def mutate(mutants):
    """Plants each fault in turn and says whether a test objected.

    Each mutant is (name, path, old, new).
    """
    survived = []
    unknown = []

    for name, path, old, new in mutants:
        was = io.open(path, encoding="utf-8").read()

        if old not in was:
            print("  ?? could not plant: " + name)
            unknown.append(name + " (could not plant)")
            continue

        io.open(path, "w", encoding="utf-8", newline="\n").write(was.replace(old, new, 1))

        try:
            verdict = run_the_suite()
        finally:
            # Always put it back, including on Ctrl-C. A mutant left in the
            # working tree is a deliberate fault nobody remembers planting.
            io.open(path, "w", encoding="utf-8", newline="\n").write(was)

        if verdict == "caught":
            print("  caught: " + name)
        elif verdict == "survived":
            print("  SURVIVED: " + name)
            survived.append(name)
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


if __name__ == "__main__":
    sys.exit("Import mutate() and hand it the faults to plant.")
