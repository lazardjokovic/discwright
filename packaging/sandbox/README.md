# Testing the installer on a clean Windows

Smart App Control blocks an unsigned installer, and it is on this machine, so
until now the installer shipped having never been run. It cannot be turned off
and back on again: Windows only lets you turn it off, and getting it back means
reinstalling. Signing would not fix it either, since Smart App Control wants
reputation as well as a signature.

**Windows Sandbox is the way round it.** A throwaway Windows, with none of this
machine's policy, that exists while the window is open and is gone when it
closes. Nothing here changes, and every run starts from an image that has never
seen DiscWright, which is a better test than this machine could give anyway.

```powershell
.\packaging\sandbox\Test-Installer.ps1
.\packaging\sandbox\Test-Installer.ps1 -Installer C:\Downloads\DiscWright-0.8.0-setup.exe
```

Turn the sandbox on once, in an admin PowerShell, then reboot:

```powershell
Enable-WindowsOptionalFeature -Online -FeatureName "Containers-DisposableClientVM" -All
```

It needs Windows Pro or Enterprise, and virtualisation enabled in firmware.

## What it asks

`Install-Check.ps1` runs inside the sandbox and answers, in order:

1. does the installer run silently and exit cleanly, with no prompt and no
   administrator
2. does Windows list it as an installed program, with the right version
3. are the files there, and are they still scripts rather than an executable
4. is there a Start menu shortcut
5. are the Windows pieces DiscWright leans on present on this image at all
6. does the Start menu's launcher open the app, and does the app open when
   started without that launcher, which are two different questions
7. does it uninstall, and is the program, its folder and its shortcut gone

Every answer is PASS or FAIL and nothing stops early: a check that answers one
question and dies has told you almost nothing.

## What the first run found

0.8.0 shipped before this existed. The first run of it, against the published
`DiscWright-0.8.0-setup.exe`, passed every check but one:

```
FAIL  VBScript is on this image, which the .vbs launcher needs
FAIL  the Start menu's launcher opens the app  [no VBScript on this image, so wscript cannot run the .vbs]
PASS  the app itself opens when started without that launcher
```

`vbscript.dll` is simply not on a current Windows 11 image. VBScript became a
Feature on Demand in Windows 11 24H2, present on an ordinary install and absent
from a trimmed one, and Microsoft has said it will be disabled by default and
then removed. `DiscWright.vbs` is the silent launcher the Start menu shortcut
points at, so on an image without it the shortcut opens a Windows Script Host
error box and the app never starts.

It is invisible on the machine this is developed on, which has VBScript, and
the same run proves the app is fine: started directly it opens its window.

Two things it also settled, both good news:

- **The disc's menu is safe.** `mshta.exe`, `jscript.dll` and `scrrun.dll` are
  all on that image, and a real HTA ran JScript and created
  `Scripting.FileSystemObject`, `WScript.Shell` and `Shell.Application`, which
  is everything the menu uses.
- **The zip is safe.** `Run DiscWright.cmd` calls `powershell.exe` directly and
  never touches VBScript.
