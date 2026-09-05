# Packaging

DiscWright ships two ways, and the zip is still the main one. Nothing here
changes how the app runs or how it is downloaded from the README.

| | what it is | who it is for |
| --- | --- | --- |
| `DiscWright-<v>.zip` | the six files that matter, in one folder | anyone; unzip and double-click, exactly as before |
| `DiscWright-<v>-setup.exe` | Inno Setup installer, per-user, no UAC | Start menu entry, clean uninstall, and **winget** |

## Why there is an installer at all

The README opens with "there is nothing to install", and that stays true. The
installer exists for one reason: **winget will not take a folder of scripts.**

Its only route for a plain zip is `NestedInstallerType: portable`, and that
rejects every extension except `.exe`. Not at run time - at manifest validation:

```
> winget validate --manifest .
Manifest Error: The file type of the referenced file is not allowed.
                [RelativeFilePath] Value: Run DiscWright.cmd
```

`.vbs`, `.ps1` and `.bat` are refused the same way
([winget-cli#3386](https://github.com/microsoft/winget-cli/issues/3386), open).
So it is an installer or no winget.

What the installer deliberately does **not** do is turn DiscWright into an
executable. The installed app is still the same scripts, and the Start menu
shortcut runs `wscript.exe DiscWright.vbs` - the Microsoft-signed chain that
lets the app work on a Smart App Control machine, as the header of
`DiscWright.ps1` explains. Only the installer itself is an `.exe`, run once.
Somebody on a locked-down PC who cannot run it still has the zip.

The install is per-user (`PrivilegesRequired=lowest`), so it never shows a UAC
prompt and never needs an admin. That is safe because DiscWright keeps no state
beside itself: `discproject.json` is written into whichever output folder the
user picks.

## Cutting a release

1. Tag as usual. The `package` job in `.github/workflows/checks.yml` builds both
   artifacts, attaches them to the release, and prints the installer's SHA256 in
   the run summary.
2. Build the manifests with that hash:

   ```powershell
   .\packaging\winget\New-WingetManifest.ps1 -Version 0.5.2 -Sha256 <hash>
   ```

3. Check them, on this machine, before they go anywhere:

   ```powershell
   winget validate --manifest .\build\winget\0.5.2
   winget install  --manifest .\build\winget\0.5.2   # really installs it
   winget uninstall DiscWright
   ```

4. Submit: fork [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs),
   copy the folder to `manifests\l\LazarDjokovic\DiscWright\<version>\`, open a
   PR. Their CI runs the install on a clean VM and a human reviews it.

Step 4 is not automated on purpose - it publishes to somebody else's repository,
and that should be a decision rather than a side effect of pushing a tag.

## First submission

The first PR to `winget-pkgs` claims the publisher folder `LazarDjokovic`, so
the identifier `LazarDjokovic.DiscWright` is fixed from then on. `AppId` in
`DiscWright.iss` and `ProductCode` in the generated installer manifest have to
stay in step with each other forever after, or winget stops recognising an
installed copy as the same product.

## What is not here

No code signing. An unsigned installer means SmartScreen shows "Windows
protected your PC" until the download builds reputation, and a Smart App Control
machine refuses it outright. A certificate is roughly $200-400 a year because OV
signing now requires a hardware token or cloud HSM. The Microsoft Store is the
cheaper way out of that - Microsoft signs what it distributes - but the Store
listing would need screenshots that contain no game artwork, which is a separate
problem from the one the README disclaimer solves.
