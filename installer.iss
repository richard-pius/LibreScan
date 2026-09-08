; ═══════════════════════════════════════════════════════════════════════════════
; LibreScan Security — Inno Setup Installer Script
; Requires: Inno Setup 6.x (https://jrsoftware.org/isdl.php)
; ═══════════════════════════════════════════════════════════════════════════════

#define MyAppName      "LibreScan Security"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "LibreScan Security Project"
#define MyAppURL       "https://github.com/librescan/librescan-security"
#define MyAppExeName   "LibreScan.exe"

; ─── Setup Configuration ─────────────────────────────────────────────────────
[Setup]
AppId={{7F3A91C2-D4E8-4B5A-9C6F-1E2D3A4B5C6D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=LibreScan_Setup_{#MyAppVersion}
SetupIconFile=Assets\librescan.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
; Use Restart Manager to gracefully close the app if running
CloseApplications=force
RestartApplications=no

; ─── Languages ───────────────────────────────────────────────────────────────
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

; ─── Tasks (User-Selectable) ────────────────────────────────────────────────
[Tasks]
Name: "desktopicon";  Description: "Create a &desktop shortcut";       GroupDescription: "Additional shortcuts:"
Name: "startupentry"; Description: "Start {#MyAppName} with Windows";  GroupDescription: "System integration:"; Flags: checkedonce

; ─── Directory Permissions ───────────────────────────────────────────────────
; Grant modify permissions so the application can:
;   - Write virus definition updates to database/
;   - Move infected files to Quarantine/
[Dirs]
Name: "{app}\clamav_bin\database"; Permissions: users-modify
Name: "{app}\Quarantine";         Permissions: users-modify

; ─── Files ───────────────────────────────────────────────────────────────────
; Source from the dotnet publish output directory
[Files]
Source: "publish\{#MyAppExeName}";       DestDir: "{app}"; Flags: ignoreversion
Source: "publish\clamav_bin\*";          DestDir: "{app}\clamav_bin"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "publish\Assets\*";              DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "publish\Quarantine\*";          DestDir: "{app}\Quarantine"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
; Include any remaining published support files (PDBs, configs, etc.)
Source: "publish\*.json";                DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "publish\*.dll";                 DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist

; ─── Start Menu & Desktop Icons ──────────────────────────────────────────────
[Icons]
Name: "{group}\{#MyAppName}";            Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}";  Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";      Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; ─── Registry: Auto-Start on Login (CRITICAL — NOT done in C#) ──────────────
; Writes to HKCU so it does NOT require elevation at login.
; The --startup flag triggers silent boot (tray-only, no main window).
; uninsdeletevalue: automatically cleaned up on uninstall.
[Registry]
Root: HKCU; \
  Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; \
  ValueName: "{#MyAppName}"; \
  ValueData: """{app}\{#MyAppExeName}"" --startup"; \
  Flags: uninsdeletevalue; \
  Tasks: startupentry

; ─── Post-Install Actions ────────────────────────────────────────────────────
[Run]
; Optional: launch the application after installation
Filename: "{app}\{#MyAppExeName}"; \
  Description: "Launch {#MyAppName}"; \
  Flags: nowait postinstall skipifsilent unchecked

; ─── Uninstall: Process Kill & Cleanup ───────────────────────────────────────
[UninstallRun]
Filename: "taskkill"; \
  Parameters: "/F /IM {#MyAppExeName}"; \
  Flags: runhidden; \
  RunOnceId: "KillLibreScanUninstall"
Filename: "taskkill"; \
  Parameters: "/F /IM freshclam.exe"; \
  Flags: runhidden; \
  RunOnceId: "KillFreshClamUninstall"
Filename: "taskkill"; \
  Parameters: "/F /IM clamscan.exe"; \
  Flags: runhidden; \
  RunOnceId: "KillClamScanUninstall"

[UninstallDelete]
; Clean up user-generated data (virus definitions, quarantined files)
Type: filesandirs; Name: "{app}\clamav_bin\database"
Type: filesandirs; Name: "{app}\Quarantine"

; ─── Pascal Script: Pre-Install Process Kill ("File in Use" Fix) ─────────────
[Code]
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Kill any running instance BEFORE files are copied.
  // This prevents "File in Use" errors during upgrades.
  Exec('taskkill', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill', '/F /IM freshclam.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill', '/F /IM clamscan.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  // Brief pause to let the processes fully terminate and release file handles
  Sleep(750);

  Result := '';
end;
