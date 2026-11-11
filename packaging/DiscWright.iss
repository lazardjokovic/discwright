; DiscWright - Inno Setup script
;
; Why there is an installer at all, for an app whose README opens with "there is
; nothing to install": winget will not take a folder of scripts. Its only route
; for a plain zip is NestedInstallerType: portable, and that rejects every
; extension except .exe outright - .cmd, .vbs, .ps1 and .bat are all refused at
; manifest validation, not at run time. So the choice is an installer or no
; winget. The zip stays the primary download and nothing about it changes.
;
; What this deliberately does NOT do is turn DiscWright into an executable. The
; installed app is still the same scripts, still started through Microsoft-signed
; wscript.exe and powershell.exe, which is what lets it run on a Smart App
; Control machine (see the note at the top of DiscWright.ps1). Only the installer
; itself is an .exe, run once. Somebody on a locked-down PC who cannot run it
; still has the zip, working exactly as before.
;
; Built by packaging/Build-Release.ps1, which passes the version in:
;     ISCC.exe /DAppVersion=0.5.1 packaging\DiscWright.iss

#ifndef AppVersion
  #error AppVersion must be passed in with /DAppVersion=x.y.z
#endif

; "DiscWright (PowerShell)" since 2.0, when the C# app became the one called
; DiscWright. The two can be installed side by side, and a Start Menu with two
; entries both called DiscWright gives nobody a way to tell them apart.
#define AppName    "DiscWright (PowerShell)"

; The AppId every installer of this app used up to 0.11.1. The C# app's
; installer carries it from 2.0, so that it upgrades an old install in place;
; this one has its own below, so that it can never be mistaken for the C# app
; or install over it.
#define OldAppId   "{20F71A81-9D66-445E-BDC3-394B940F190E}"
// The project publishes itself. This is what Add/Remove Programs shows and what
// the .exe's file properties carry, so it matches the winget publisher and the
// DiscWright.DiscWright identifier rather than naming a person.
#define AppPublisher "DiscWright"
#define AppUrl     "https://discwright.com"

[Setup]
; Its own AppId since the C# app took the old one on 2026-11-11. AppId is how
; Windows recognises an existing install as the same product, so sharing one
; with the C# app would let either installer overwrite the other. An install
; under the old AppId that really is this app is removed first, in [Code]
; below, so nobody is left with an orphan. Never change this one.
AppId={{6FC4469E-5231-4F83-BC18-AE868E923003}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL=https://github.com/lazardjokovic/discwright/issues
AppUpdatesURL=https://github.com/lazardjokovic/discwright/releases
VersionInfoVersion={#AppVersion}

; Per-user, so installing never shows a UAC prompt and never needs an admin to
; be standing there. With PrivilegesRequired=lowest, {autopf} resolves to
; %LOCALAPPDATA%\Programs - a real install location that the user owns.
;
; It also sidesteps a question a machine-wide install would raise: DiscWright
; writes discproject.json into the OUTPUT folder the user picks, never beside
; itself, so it keeps no state in its install directory either way. Per-user is
; simply the friendlier of two paths that both work.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Nothing here needs a decision from the person installing, so the wizard opens
; on the one page that does: where it goes, and whether they want a desktop icon.
DisableWelcomePage=yes
DisableReadyPage=yes

LicenseFile=..\LICENSE
SetupIconFile=..\DiscWright.ico
UninstallDisplayIcon={app}\DiscWright.ico
UninstallDisplayName={#AppName} {#AppVersion}

OutputDir=..\build
; Named for the app, because it shares a releases page with the C# app's
; DiscWright-<version>-setup.exe.
OutputBaseFilename=DiscWright-PowerShell-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; The app is a Windows PowerShell 5.1 WinForms script: no native code, nothing
; architecture-specific. x64compatible keeps {sys} pointing at the real System32
; on 64-bit Windows rather than SysWOW64, so the shortcut below starts 64-bit
; wscript.exe and therefore 64-bit PowerShell.
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Source paths are relative to this .iss file, which lives in packaging/.
Source: "..\DiscWright.ps1";      DestDir: "{app}"; Flags: ignoreversion
Source: "..\DiscWright.ico";      DestDir: "{app}"; Flags: ignoreversion
Source: "..\DiscWright.vbs";      DestDir: "{app}"; Flags: ignoreversion
Source: "..\Run DiscWright.cmd";  DestDir: "{app}"; Flags: ignoreversion
; The burning module, which the Burn to disc button dot-sources. Shipped in
; its own folder so the ISO builder stays separable from it: the app reaches
; into it, it never reaches back.
Source: "..\burn\DiscWright.Burn.ps1";   DestDir: "{app}\burn";  Flags: ignoreversion
Source: "..\README.md";           DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE";             DestDir: "{app}"; Flags: ignoreversion

; The print module went in 0.11.0, when the artwork stopped being printed here
; and started going onto the disc as it was given. Installing over 0.10.0 or
; older would otherwise leave its folder behind, a module nothing loads, in a
; folder an uninstall then has to know about.
[InstallDelete]
Type: filesandordirs; Name: "{app}\print"

[Icons]
; Two launchers, and the machine picks. Where VBScript is present the shortcut
; goes through wscript.exe and DiscWright.vbs, whose whole job is starting the
; app with no console window flashing up first; wscript.exe -> powershell.exe is
; also the Microsoft-signed chain that keeps this working under Smart App
; Control. Naming wscript.exe explicitly rather than letting the shell resolve
; the .vbs association means a machine that has had .vbs re-associated with an
; editor still launches the app.
;
; Where VBScript is absent that shortcut opens a Windows Script Host error box
; and nothing else, which is what 0.8.0 shipped: vbscript.dll is not on a
; current Windows 11 image, VBScript having become a Feature on Demand in 24H2
; with Microsoft saying it will be disabled by default and then removed. Found
; by installing 0.8.0 in Windows Sandbox; see packaging\sandbox.
;
; Those machines get powershell.exe directly. Its console is created and then
; hidden, so there is a brief flash, which is worth accepting only where the
; quieter way cannot run at all.
Name: "{group}\{#AppName}"; \
    Filename: "{sys}\wscript.exe"; \
    Parameters: """{app}\DiscWright.vbs"""; \
    WorkingDir: "{app}"; \
    IconFilename: "{app}\DiscWright.ico"; \
    Comment: "Turn a GOG installer folder into a burnable game disc"; \
    Check: HasVBScript

Name: "{group}\{#AppName}"; \
    Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File ""{app}\DiscWright.ps1"""; \
    WorkingDir: "{app}"; \
    IconFilename: "{app}\DiscWright.ico"; \
    Comment: "Turn a GOG installer folder into a burnable game disc"; \
    Check: not HasVBScript

Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

Name: "{autodesktop}\{#AppName}"; \
    Filename: "{sys}\wscript.exe"; \
    Parameters: """{app}\DiscWright.vbs"""; \
    WorkingDir: "{app}"; \
    IconFilename: "{app}\DiscWright.ico"; \
    Tasks: desktopicon; Check: HasVBScript

Name: "{autodesktop}\{#AppName}"; \
    Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File ""{app}\DiscWright.ps1"""; \
    WorkingDir: "{app}"; \
    IconFilename: "{app}\DiscWright.ico"; \
    Tasks: desktopicon; Check: not HasVBScript

[Run]
; The tick box at the end of the install, which has to pick its launcher the
; same way, or finishing the install is where somebody meets the error box.
Description: "{cm:LaunchProgram,{#AppName}}"; \
    Filename: "{sys}\wscript.exe"; \
    Parameters: """{app}\DiscWright.vbs"""; \
    WorkingDir: "{app}"; \
    Flags: postinstall nowait skipifsilent; Check: HasVBScript

Description: "{cm:LaunchProgram,{#AppName}}"; \
    Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File ""{app}\DiscWright.ps1"""; \
    WorkingDir: "{app}"; \
    Flags: postinstall nowait skipifsilent; Check: not HasVBScript

[Code]
// Whether this machine can run a .vbs at all. VBScript is a Feature on Demand
// from Windows 11 24H2: present on an ordinary install, absent from a trimmed
// image, and on its way out altogether. Checked in {sys}, which is where the
// wscript.exe these shortcuts name lives, so the answer is about the same pair.
function HasVBScript(): Boolean;
begin
  Result := FileExists(ExpandConstant('{sys}\vbscript.dll'));
end;

// This app installed under the old AppId, by 0.11.1 or earlier, removed
// quietly before anything is written, so that installing it again leaves one
// DiscWright (PowerShell) and not that plus an orphan called DiscWright.
//
// The old AppId is the C# app's from 2.0, and that must be left alone. They
// are told apart by what is in the folder rather than by the version number:
// this app is DiscWright.ps1 and the C# app is DiscWright.App.exe, which no
// renumbering can change.
procedure RemoveTheOldInstall();
var
  Key, Folder, Uninstaller: String;
  Code: Integer;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#OldAppId}_is1';
  if not RegQueryStringValue(HKCU, Key, 'InstallLocation', Folder) then
    exit;
  Folder := AddBackslash(RemoveQuotes(Folder));
  if (not FileExists(Folder + 'DiscWright.ps1')) or FileExists(Folder + 'DiscWright.App.exe') then
    exit;
  if RegQueryStringValue(HKCU, Key, 'UninstallString', Uninstaller) then
  begin
    Uninstaller := RemoveQuotes(Uninstaller);
    if FileExists(Uninstaller) then
      Exec(Uninstaller, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
    RemoveTheOldInstall();
end;
