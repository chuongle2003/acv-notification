#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define AppName "Task Tracker"
#define AppExeName "TaskTracker.exe"

[Setup]
AppId={{7A0F6CE6-733E-4D4F-B68A-A78C397250A1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=TaskTracker
DefaultDirName={localappdata}\Programs\TaskTracker
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=TaskTracker-Setup-{#AppVersion}
SetupIconFile=..\src\TaskTracker.Windows\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#AppVersion}

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "TaskTracker"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Mở {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('Bạn có muốn xóa dữ liệu cục bộ (cài đặt, lịch sử và trạng thái đã xem)?',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      DelTree(ExpandConstant('{localappdata}\TaskTracker'), True, True, True);
    end;
  end;
end;
