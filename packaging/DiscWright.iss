; DiscWright 2.0 - Inno Setup script
;
; What goes in is ONE FILE. The app is published self-contained and single
; file, and that is not a packaging preference, it is the only thing that runs
; here. Smart App Control is on and enforcing on a current Windows 11, and a
; freshly built unsigned DLL has no reputation, so Code Integrity refuses it by
; name: ids 3033 and 3077 in Microsoft-Windows-CodeIntegrity/Operational. The
; exe is allowed to start and then dies with 0xE0434352 a second later, which
; looks like a crash rather than a block and cost an evening to pin down.
;
; Inside a single-file bundle the managed assemblies load from memory, so there
; is no file for Code Integrity to check and nothing is refused. 62.5 MB, and
; worth every one of them.
;
; This does NOT replace signing. An unsigned exe has no reputation anywhere
; else either, so a certificate is still what stands between this and somebody
; who is not Lazar running it.
;
; Built by packaging/Build-TheInstaller.ps1, which publishes first and passes
; the version in:
;     ISCC.exe /DAppVersion=2.0.0 packaging\DiscWright.iss

#ifndef AppVersion
  #error AppVersion must be passed in with /DAppVersion=x.y.z
#endif

; "DiscWright 2.0" while it is being tested beside v1, so the two are tellable
; apart in the Start Menu. The release that actually replaces v1 goes back to
; plain "DiscWright" and takes v1's AppId, so an existing install upgrades.
#define AppName      "DiscWright 2.0"
#define AppPublisher "DiscWright"
#define AppUrl       "https://discwright.com"

[Setup]
; A NEW AppId, deliberately, and not the one in the v1 repo.
;
; AppId is how Windows recognises an install as the same product. Reusing v1's
; would make this an upgrade: it would take over the Start Menu entry, the
; Add/Remove Programs row and the install folder of a working app, while 2.0 is
; still being tested. So 2.0 sits beside v1 and either can be removed without
; touching the other.
;
; When 2.0 is genuinely the one to run, the release that replaces v1 takes
; v1's AppId {{20F71A81-9D66-445E-BDC3-394B940F190E} so that an existing
; install upgrades in place rather than being left behind as an orphan nothing
; can find. That is a decision for then, not a default for now.
AppId={{14016040-B21A-4EA6-844E-FF7AE4E18176}
AppName={#AppName}
AppVersion={#AppVersion}
; Not "{#AppName} {#AppVersion}", which reads "DiscWright 2.0 2.0.0" because
; the name already carries the 2.0. Add/Remove Programs shows the version in a
; column of its own anyway, from AppVersion above.
AppVerName={#AppName}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL=https://github.com/lazardjokovic/discwright-csharp/issues
AppUpdatesURL=https://github.com/lazardjokovic/discwright-csharp/releases
VersionInfoVersion={#AppVersion}

; Per-user, so installing never shows a UAC prompt and never needs an admin
; standing there. With PrivilegesRequired=lowest, {autopf} resolves to
; %LOCALAPPDATA%\Programs, a real location the person owns.
;
; The app keeps no state beside itself: a project is written into the output
; folder that was chosen, never into the install directory. So per-user is
; simply the friendlier of two paths that both work.
PrivilegesRequired=lowest

; Its own folder, not v1's. Two products installed at once must not write into
; one directory, or removing either takes pieces of the other with it.
DefaultDirName={autopf}\DiscWright 2.0
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Nothing here needs a decision, so the wizard opens on the one page that does:
; where it goes, and whether a desktop icon is wanted.
DisableWelcomePage=yes
DisableReadyPage=yes

; No LicenseFile. This repository has no LICENSE yet, and a wizard page that
; demands agreement to a document nobody has written would be a lie. It goes
; back the moment there is one.
SetupIconFile=..\src\DiscWright.App\DiscWright.ico
UninstallDisplayIcon={app}\DiscWright.App.exe
UninstallDisplayName={#AppName}

OutputDir=..\build
OutputBaseFilename=DiscWright-{#AppVersion}-setup
; The payload is one already-compressed 62 MB file, so solid compression buys
; very little and costs a long build. lzma2/max alone still halves it.
Compression=lzma2/max
WizardStyle=modern

; Published win-x64 and self-contained, so there is nothing to run on anything
; else. Saying so here means a 32-bit or ARM machine is told plainly instead of
; installing something that cannot start.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; One file. Everything the app needs is inside it, which is the point.
Source: "..\build\payload\DiscWright.App.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md";                        DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; \
    Filename: "{app}\DiscWright.App.exe"; \
    WorkingDir: "{app}"; \
    Comment: "Turn a folder of installers into a burnable game disc"

Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"

Name: "{autodesktop}\{#AppName}"; \
    Filename: "{app}\DiscWright.App.exe"; \
    WorkingDir: "{app}"; \
    Tasks: desktopicon

[Run]
Description: "{cm:LaunchProgram,{#AppName}}"; \
    Filename: "{app}\DiscWright.App.exe"; \
    WorkingDir: "{app}"; \
    Flags: postinstall nowait skipifsilent
