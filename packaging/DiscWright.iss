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

#define AppName    "DiscWright"
#define AppPublisher "Lazar Djokovic"
#define AppUrl     "https://discwright.com"

[Setup]
; Never change AppId. It is how Windows and winget recognise an existing
; install as the same product, so a new one here would leave the old install
; behind as an orphan that nothing can upgrade or remove.
AppId={{20F71A81-9D66-445E-BDC3-394B940F190E}
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
OutputBaseFilename=DiscWright-{#AppVersion}-setup
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
Source: "..\README.md";           DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE";             DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Pointed at the .vbs rather than the .cmd because that is the launcher whose
; whole job is starting the app without a console window flashing up first, and
; because wscript.exe -> powershell.exe is the Microsoft-signed chain that keeps
; this working under Smart App Control. Naming wscript.exe explicitly rather
; than letting the shell resolve the .vbs association means a machine that has
; had .vbs re-associated with an editor still launches the app.
Name: "{group}\{#AppName}"; \
    Filename: "{sys}\wscript.exe"; \
    Parameters: """{app}\DiscWright.vbs"""; \
    WorkingDir: "{app}"; \
    IconFilename: "{app}\DiscWright.ico"; \
    Comment: "Turn a GOG installer folder into a burnable game disc"

Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

Name: "{autodesktop}\{#AppName}"; \
    Filename: "{sys}\wscript.exe"; \
    Parameters: """{app}\DiscWright.vbs"""; \
    WorkingDir: "{app}"; \
    IconFilename: "{app}\DiscWright.ico"; \
    Tasks: desktopicon

[Run]
Description: "{cm:LaunchProgram,{#AppName}}"; \
    Filename: "{sys}\wscript.exe"; \
    Parameters: """{app}\DiscWright.vbs"""; \
    WorkingDir: "{app}"; \
    Flags: postinstall nowait skipifsilent
