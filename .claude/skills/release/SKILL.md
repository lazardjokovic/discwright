---
name: release
description: Cut a DiscWright release, from the version bump to the published GitHub release and the website. Use when asked to release, to cut a version, or to ship what is on main.
---

# Cutting a release

The runbook for the artifacts themselves is `packaging/README.md`, and it is the
authority on how the zip and the installer are built. This is the order of the
whole thing, including the parts that are easy to skip and expensive to skip:
the window suite, the installer on a clean Windows, and the website.

Ten steps. Stop and ask at any point something does not match.

## 0. Before touching anything

- `main` clean, pulled, no open PR in this repository.
- Decide the number from what is being released, not the other way round. A fix
  is a patch. A feature, or anything that moves the project file's schema, is a
  minor while the version is below 1.0.0.
- Read `CHANGELOG.md`: whatever sits under **Unreleased** is what is shipping. If
  it is empty, the release has no contents and the question is what is actually
  wanted.

## 1. The version, in the two places that hold it

- `DiscWright.ps1`: `$APP_VERSION  = 'x.y.z'`
- `CHANGELOG.md`: turn `## [Unreleased]` into `## [x.y.z] — <today>`, and add the
  two link lines at the bottom, the new one and the moved `[Unreleased]`.

Nothing else carries the number. `packaging/DiscWright.iss` takes it on the
command line, and the website is step 9.

## 2. The suites, in full

```powershell
.\tests\Invoke-Tests.ps1 -SkipUI     # logic
.\tests\Invoke-Tests.ps1 -UIOnly     # the window, about five minutes
```

**Say the machine is going to be unusable before starting the window half, and
say when it is free again.** Kill any leftover DiscWright window first, or every
window test fails on the foreground guard.

Also the parse check over every `.ps1` under Windows PowerShell 5.1, and
PSScriptAnalyzer with the repository's settings: 0 errors.

## 3. The release PR

Branch `release/x.y.z`, one commit, the message saying what the release contains
in a few lines. PR title `release: x.y.z`. Wait for CI, squash-merge, delete the
branch, pull `main`.

## 4. The tag

```powershell
git tag -a vx.y.z -m "DiscWright x.y.z"
git push origin vx.y.z
```

The tag build runs `version-matches-tag`, which fails if step 1 was missed, then
builds the zip and the installer, attaches them to a **draft** release and prints
both checksums in the run summary. Pushing a tag does not create a release, so
the job creates the draft itself; that took three attempts to get right, and
`packaging/README.md` tells that story.

## 5. The artifacts, in hand

```powershell
gh release download vx.y.z --dir <somewhere in the scratchpad>
```

Hash both files and keep the values for the notes. They must match what the run
summary printed.

## 6. The installer, on a clean Windows

```powershell
.\packaging\sandbox\Test-Installer.ps1 -Installer <the downloaded setup.exe>
```

**This is the step 0.8.0 did not have, and it is why 0.8.1 exists.** Smart App
Control blocks an unsigned installer on this machine, so a sandbox is the only
place the installer can be run at all. Every check must pass, on the artifact
that was downloaded rather than one rebuilt locally. A sandbox window opens and
closes itself; it does not take the pointer.

## 7. The zip, opened

Unpack it, check `$APP_VERSION` in the shipped script, and start it: the window
title must read `DiscWright x.y.z`. The zip is the main download and nothing
else proves it was built from what was tagged.

## 8. Publish

```powershell
gh release edit vx.y.z --draft=false --title "DiscWright x.y.z" --notes-file <notes>
```

Notes in the house style, which previous releases show: what changed and why, in
prose rather than a bullet list of commits, then a checksums block, then a line
naming the test counts. Say plainly what was tested where. If a claim rests on
CI rather than a local run, say so.

## 9. The website

`discwright.com`, `site/index.html`, one line names the version. Commit, push,
then wait for the deploy and confirm the live page says the new number. **The
release is not done until it does.**

## 10. Finish

Play the completion sound. Report what was run and where: which suites locally,
what CI covered, and what the sandbox proved.

## winget, which is not part of this

It comes later and on its own:

- Only when the previous submission has merged. One version at a time.
- Only after everything above, installer included.
- Never wired to publishing a release. An update to an existing package merges
  without a human, so a bad one installs itself on other people's machines.

`packaging/README.md` has the manifest commands and the reasoning.
