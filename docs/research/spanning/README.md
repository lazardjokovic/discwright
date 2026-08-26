# Can one game be split across several discs?

No. This folder is why.

The decision, and the argument for it, is in [ROADMAP.md](../../../ROADMAP.md) under
*Not planned*. **Read that first** — this folder is the evidence behind it, kept so the
claim can be checked rather than taken on trust, and so nobody has to run the experiment
a second time to find out.

Nothing here is part of DiscWright. These are throwaway harnesses, preserved as they were
run. They are recorded here rather than tidied because a tidied harness is no longer the
thing that produced the numbers.

---

## What was found

GOG ships **two packaging formats**, and the first eight bytes of the first `.bin` tell
them apart:

| Bytes | Format | Spans? |
|---|---|---|
| `idska32` | Inno Setup's own disk slices | yes |
| `Rar!\x1a\x07\x00` | RAR multi-volume, unpacked by `unrar.dll` | **no, and no mechanism exists** |

Never read those bytes with `[IO.File]::ReadAllBytes()`. The files are ~4 GB and .NET
refuses an array that large; an early harness reported `payload format : unknown ( )` for
exactly this reason. Open a `FileStream` and read 8 bytes.

**Inno slices span, and the swap is one click** — but it takes ten swaps for three discs,
out of order, and the count is not stable between runs. **RAR volumes do not span at
all** — a missing volume reports a corrupt download, and Inno then logs success and
registers the game anyway.

What fraction of GOG's catalogue is each format is **unknown**. Two estimates were
produced from GOG's public product API and **both were withdrawn**: for the one game
proven to be RAR, the API reports its parts as 4096 MiB when they are really 4000 MiB and
inflates the total to match, so the size-cluster method it rested on is invalid. The only
sound test is an HTTP Range request for bytes 0–7 of each installer, which needs an
authenticated GOG session and only covers games the account owns.

---

## The artifacts

| Path | What it shows |
|---|---|
| `witcher-run-result.txt` | The Witcher across three **folders**. 7 prompts, `3→2→3→2→3→2→3`. 2685 files / 13.97 GB. |
| `witcher-mount-swap/` | The Witcher across three **real UDF ISOs**, mounted one at a time on a single drive letter. 10 prompts, `3→1→2→1→2→3→2→3→2→3`. Same 2685 files / 13.97 GB, from a completely different staging mechanism. |
| `deadspace-void-run/` | Dead Space, `/VERYSILENT`, **1 volume of 3**. 0 prompts. 796 of 4297 files. Inno log says `Installation process succeeded`. |
| `deadspace-interactive-run/` | Dead Space, interactive, 1 volume of 3. 0 prompts. 9 of 4297 files. `windows.txt` records every dialog, including `Installation failed with code: -3` blaming the user's download. |

The two Witcher runs matching **file for file and byte for byte** from unrelated staging
mechanisms is the check that both genuinely completed.

There is no `deadspace-control-run/` directory — the control's output went to the console
and is summarised in ROADMAP.md: all three volumes present, 4298 files, 100%, zero
dialogs. Re-run `spancontrol.ps1` to reproduce it.

## The harnesses

| Script | Job |
|---|---|
| `spanmount.ps1` | Builds three UDF ISOs mirroring DiscWright's own IMAPI2FS builder, mounts one at a time forced onto `R:`, and clicks OK at each prompt **without editing the path** — the test of whether the dialog's stale default still resolves. It does. |
| `spancontrol.ps1` | **The control.** Same installer, same switches, all volumes in one folder. Without it the `-3` failure is only *correlated* with the missing volume. If this ever fails, every conclusion here is void — the script says so itself. |
| `spanharness.ps1` | First attempt. Its conclusions were wrong: it trusted the Inno log. |
| `spanharness2.ps1` | `/SILENT` attempt. Proved GOG's installers refuse it (`Failed to proceed to next wizard page; aborting`). Measures against the archive TOC, not the log. |
| `spanharness3.ps1` | Interactive driver, foreground-window-only with per-window rules. |
| `spancleanup.ps1`, `spanshortcuts.ps1` | Undo a test install: registry, Start Menu, desktop. |

### Why the Inno log cannot be trusted for a RAR game

`Installation process succeeded` is true and useless. The payload is not an Inno file
entry — it is a RAR archive unpacked by `unrar.dll` from GOG's install script, and Inno
knows nothing about it. Inno's own file list for Dead Space is three files and six icons.
Measure against the archive's table of contents instead:

```powershell
# after hardlinking the .bin files to .partN.rar names
7z l ds.part1.rar        # 4297 files / 10,295,918,859 bytes
```

`spanharness2.ps1` and `spanharness3.ps1` do this. `spanharness.ps1` does not, which is
why its first Dead Space run reported a false success.

---

## If you re-run any of this

- **Never run a harness against a game that is actually installed.** A test install
  rewrites that product's registry key to the throwaway path, and cleanup then deletes
  the real install's entry too.
- **Staging must be on `C:`.** A hardlink must sit on the same volume as its target.
- **UAC is unavoidable** — the installer self-elevates, and a non-elevated process cannot
  drive an elevated window.
- **The desktop must be untouched** for the whole run.
- **`[` and `]` are wildcards in PowerShell paths.** `Remove-Item 'X [GOG.com]\y.lnk'`
  matches nothing, deletes nothing, and does not error — and `Test-Path` lies the same
  way. Use `-LiteralPath` everywhere and verify after deleting. This cost a cleanup run
  that reported removing twelve shortcuts and removed none.
- **All GOG installer controls surface to UI Automation as bare `Pane`s** — no Button
  type, no patterns. They are found by name and clicked by coordinate, which means a
  click must be constrained to the foreground window or it lands on whatever is in front.
  GOG's EULA screen exposes three *unnamed* Panes, so it cannot be automated at all; the
  interactive run needed a human for that one step.

## The games these runs used

| Game | Total | Parts | Format |
|---|---|---|---|
| Hollow Knight | 1.16 GB | 1 file, no slices | n/a |
| Alan Wake | 7.79 GB | 4.00 + 3.79 | Inno |
| Dead Space | 8.13 GB | 3.91 + 3.91 + 0.29 | **RAR** |
| The Witcher | 9.48 GB | 4.00 + 4.00 + 1.48 | Inno |

Dead Space's installer was downloaded 2026-08-13, so RAR packaging is current rather than
historical — worth stating, because it is sometimes said that GOG moved away from it.
