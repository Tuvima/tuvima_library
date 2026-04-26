; ─────────────────────────────────────────────────────────────────────────────
; Tuvima Library — Inno Setup 6 installer script
;
; To build:   build-installer.bat   (or run ISCC.exe installer.iss directly)
; Output:     dist\TuvimaLibrary-Setup.exe
; ─────────────────────────────────────────────────────────────────────────────

#define AppName       "Tuvima Library"
#define AppVersion    "1.0.0"
#define AppPublisher  "Tuvima"
#define AppURL        "https://github.com/Tuvima/tuvima_library"
#define EnginePort    "61495"
#define DashboardPort "5016"
#define EngineSvc     "TuvimaEngine"
#define DashboardSvc  "TuvimaDashboard"

[Setup]
AppId={{B7E4F2A1-9C3D-4E6B-8A0F-D1234567890A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
DefaultDirName={autopf}\Tuvima Library
DefaultGroupName=Tuvima Library
AllowNoIcons=no
CloseApplications=yes
PrivilegesRequired=admin
OutputDir=dist
OutputBaseFilename=TuvimaLibrary-Setup-{#AppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "dist\win\engine\*";                DestDir: "{app}\engine";                      Flags: ignoreversion recursesubdirs createallsubdirs
Source: "dist\win\dashboard\*";             DestDir: "{app}\dashboard";                   Flags: ignoreversion recursesubdirs createallsubdirs
Source: "config\*";                          DestDir: "{tmp}\tuvima-config-default";        Flags: ignoreversion recursesubdirs createallsubdirs deleteafterinstall

[Dirs]
Name: "{commonappdata}\Tuvima\config";  Permissions: everyone-modify
Name: "{commonappdata}\Tuvima\db";      Permissions: everyone-modify
Name: "{commonappdata}\Tuvima\watch";   Permissions: everyone-modify
Name: "{commonappdata}\Tuvima\library"; Permissions: everyone-modify

[Icons]
Name: "{group}\Open Tuvima Library";      Filename: "{app}\open-tuvima.bat"
Name: "{group}\Uninstall Tuvima Library"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Tuvima Library";     Filename: "{app}\open-tuvima.bat"; Tasks: desktopicon

[Run]
; Install services
Filename: "{sys}\sc.exe"; Parameters: "create {#EngineSvc} binpath= ""{app}\engine\MediaEngine.Api.exe"" start= auto DisplayName= ""Tuvima Library Engine"""; Flags: runhidden; StatusMsg: "Installing Engine service..."
Filename: "{sys}\sc.exe"; Parameters: "description {#EngineSvc} ""Tuvima Library intelligence engine"""; Flags: runhidden
Filename: "{sys}\sc.exe"; Parameters: "create {#DashboardSvc} binpath= ""{app}\dashboard\MediaEngine.Web.exe"" start= auto DisplayName= ""Tuvima Library Dashboard"""; Flags: runhidden; StatusMsg: "Installing Dashboard service..."
Filename: "{sys}\sc.exe"; Parameters: "description {#DashboardSvc} ""Tuvima Library browser dashboard"""; Flags: runhidden
Filename: "{sys}\reg.exe"; Parameters: "add ""HKLM\SYSTEM\CurrentControlSet\Services\{#EngineSvc}"" /v Environment /t REG_MULTI_SZ /d ""TUVIMA_CONFIG_DIR={commonappdata}\Tuvima\config\0TUVIMA_DB_PATH={commonappdata}\Tuvima\db\library.db\0TUVIMA_WATCH_FOLDER={commonappdata}\Tuvima\watch\0TUVIMA_LIBRARY_ROOT={commonappdata}\Tuvima\library\0TUVIMA_CORS_ORIGINS=http://localhost:{#DashboardPort}\0ASPNETCORE_URLS=http://+:{#EnginePort}\0ASPNETCORE_ENVIRONMENT=Production"" /f"; Flags: runhidden; StatusMsg: "Configuring Engine service..."
Filename: "{sys}\reg.exe"; Parameters: "add ""HKLM\SYSTEM\CurrentControlSet\Services\{#DashboardSvc}"" /v Environment /t REG_MULTI_SZ /d ""Engine__BaseUrl=http://localhost:{#EnginePort}\0ASPNETCORE_URLS=http://+:{#DashboardPort}\0ASPNETCORE_ENVIRONMENT=Production"" /f"; Flags: runhidden; StatusMsg: "Configuring Dashboard service..."
Filename: "{sys}\sc.exe"; Parameters: "start {#EngineSvc}";    Flags: runhidden; StatusMsg: "Starting Engine..."
Filename: "{sys}\sc.exe"; Parameters: "start {#DashboardSvc}"; Flags: runhidden; StatusMsg: "Starting Dashboard..."
; Open browser after install
Filename: "{app}\open-tuvima.bat"; Description: "Open Tuvima Library in your browser"; Flags: postinstall nowait shellexec skipifsilent


[Code]

{ ── Stop and delete both services (safe to call if they don't exist) ─────── }
procedure RemoveServices;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#EngineSvc}',    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#DashboardSvc}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(2000);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#EngineSvc}',    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#DashboardSvc}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1000);
end;

{ ── Copy config → ProgramData\Tuvima\config on first install ─────── }
procedure SeedConfig;
var
  ResultCode: Integer;
  SrcDir, DstDir: string;
begin
  DstDir := ExpandConstant('{commonappdata}\Tuvima\config');
  SrcDir := ExpandConstant('{tmp}\tuvima-config-default');
  if (not FileExists(DstDir + '\library.json')) and DirExists(SrcDir) then
    Exec(ExpandConstant('{sys}\xcopy.exe'),
         '/E /I /Y "' + SrcDir + '" "' + DstDir + '"',
         '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ ── Write a one-click launch batch file ─────────────────────────────────── }
procedure WriteOpenBatch;
var
  BatchFile: string;
  Lines: TArrayOfString;
begin
  BatchFile := ExpandConstant('{app}\open-tuvima.bat');
  SetArrayLength(Lines, 2);
  Lines[0] := '@echo off';
  Lines[1] := 'start "" "http://localhost:{#DashboardPort}"';
  SaveStringsToFile(BatchFile, Lines, False);
end;

{ ── Stop services before uninstall removes files ────────────────────────── }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveServices;
end;

{ ── Main install flow hooks ─────────────────────────────────────────────── }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Before file copy: remove any previous service registrations
  if CurStep = ssInstall then
    RemoveServices;
  // After file copy: seed config and write the launch batch
  if CurStep = ssPostInstall then
  begin
    SeedConfig;
    WriteOpenBatch;
  end;
end;
