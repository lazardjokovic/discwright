; DiscWright, the rewrite - Inno Setup script
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

; Plain "DiscWright", because this is not a second product. It is the same
; program rewritten, and from 2.0 it is the one called DiscWright. The
; PowerShell one, where it is still installed fresh, is "DiscWright
; (PowerShell)" with an AppId and a folder of its own.
#define AppName      "DiscWright"

; The AppId the rewrite carried while it was installed beside v1 for testing,
; as "DiscWright (rewrite)". Whatever is installed under it is removed before
; this installs, so a machine that tried the rewrite does not end up with two
; programs both called DiscWright.
#define RewriteAppId "{14016040-B21A-4EA6-844E-FF7AE4E18176}"
#define AppPublisher "DiscWright"
#define AppUrl       "https://discwright.com"

[Setup]
; v1's AppId, deliberately. Never change it.
;
; AppId is how Windows and winget recognise an install as the same product, so
; carrying v1's makes 2.0 an upgrade: on a machine with the PowerShell
; DiscWright installed, this takes over its Add/Remove Programs row, its Start
; Menu entry and its folder, and winget sees the same package move to a new
; version. With a new AppId that install would be left behind as a second
; "DiscWright" nothing upgrades. Settled with the owner on 2026-11-10.
AppId={{20F71A81-9D66-445E-BDC3-394B940F190E}
AppName={#AppName}
AppVersion={#AppVersion}
; Add/Remove Programs shows the version in a column of its own, from
; AppVersion above, so repeating it in the name says it twice.
AppVerName={#AppName}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL=https://github.com/lazardjokovic/discwright/issues
AppUpdatesURL=https://github.com/lazardjokovic/discwright/releases
VersionInfoVersion={#AppVersion}

; Per-user, so installing never shows a UAC prompt and never needs an admin
; standing there. With PrivilegesRequired=lowest, {autopf} resolves to
; %LOCALAPPDATA%\Programs, a real location the person owns.
;
; The app keeps no state beside itself: a project is written into the output
; folder that was chosen, never into the install directory. So per-user is
; simply the friendlier of two paths that both work.
PrivilegesRequired=lowest

; The same folder v1 installs to, because this replaces it there. On an
; upgrade Inno keeps whatever folder v1 was put in, and [InstallDelete] below
; clears v1's scripts out of it.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Nothing here needs a decision, so the wizard opens on the one page that does:
; where it goes, and whether a desktop icon is wanted.
DisableWelcomePage=yes
DisableReadyPage=yes

; MIT, the same page v1's installer shows.
LicenseFile=..\LICENSE
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

; What the PowerShell DiscWright put in this folder, every version of it, so an
; upgrade leaves the C# app alone in it rather than beside a dead script. Its
; README.md is replaced by this one's, and nothing of the person's lives here:
; v1 wrote every project into the output folder that was chosen.
[InstallDelete]
Type: files;           Name: "{app}\DiscWright.ps1"
Type: files;           Name: "{app}\DiscWright.vbs"
Type: files;           Name: "{app}\Run DiscWright.cmd"
Type: files;           Name: "{app}\DiscWright.ico"
Type: files;           Name: "{app}\LICENSE"
Type: filesandordirs;  Name: "{app}\burn"
Type: filesandordirs;  Name: "{app}\print"

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

[Code]
// The rewrite's own uninstaller, run quietly before anything is written, when
// this machine has the rewrite installed under its testing AppId. Per-user,
// like this installer, so it is under HKCU. Its Start Menu entry has the same
// name as this one's, so it has to go first or removing it later would take
// this one's shortcut with it.
procedure RemoveTheRewrite();
var
  Key, Uninstaller: String;
  Code: Integer;
begin
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#RewriteAppId}_is1';
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
    RemoveTheRewrite();
end;
