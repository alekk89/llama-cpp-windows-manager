#define AppName "llama.cpp Windows Manager"
#define AppExeName "LlamaCppWindowsManager.exe"
#ifndef AppVersion
#define AppVersion "2.5.0"
#endif
#ifndef SourceDir
#define SourceDir "..\dist\LlamaCppWindowsManager-win-x64"
#endif
#ifndef OutputDir
#define OutputDir "..\dist\installer"
#endif

[Setup]
AppId={{5C6D440C-0EE0-4FEC-8D86-6AADEAA24620}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=llama.cpp Windows Manager contributors
AppPublisherURL=https://github.com/alekk89/llama-cpp-windows-manager
AppSupportURL=https://github.com/alekk89/llama-cpp-windows-manager/issues
AppUpdatesURL=https://github.com/alekk89/llama-cpp-windows-manager/releases
DefaultDirName={code:GetDefaultDirName}
DefaultGroupName={#AppName}
UsePreviousAppDir=yes
UsePreviousGroup=no
UsePreviousTasks=yes
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=LlamaCppWindowsManager-Setup-{#AppVersion}-win-x64
SetupIconFile=..\src\LocalLlmConsole.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
AppMutex=Local\llama.cpp-console-single-instance
MinVersion=10.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
VersionInfoVersion={#AppVersion}
VersionInfoCompany=llama.cpp Windows Manager contributors
VersionInfoDescription={#AppName} Installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Startup options:"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\llwmctl.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\AGENTS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\agent.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\docs\CONTROL_API.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\sbom.spdx.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: files; Name: "{app}\LlamaCppConsole.exe"
Type: files; Name: "{userprograms}\llama.cpp Console\llama.cpp Console.lnk"
Type: dirifempty; Name: "{userprograms}\llama.cpp Console"
Type: files; Name: "{userdesktop}\llama.cpp Console.lnk"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LlamaCppWindowsManager"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DeleteAppDataOnUninstall: Boolean;

function GetDefaultDirName(Param: string): string;
begin
  if DirExists('D:\') then
    Result := 'D:\LlamaCppWindowsManager'
  else
    Result := ExpandConstant('{localappdata}\Programs\LlamaCppWindowsManager');
end;

function InitializeUninstall(): Boolean;
var
  DataDir: string;
begin
  Result := True;
  DeleteAppDataOnUninstall := False;
  DataDir := ExpandConstant('{app}\data');

  if DirExists(DataDir) then
  begin
    DeleteAppDataOnUninstall :=
      SuppressibleMsgBox(
        'Uninstall llama.cpp Windows Manager?' + #13#10 + #13#10 +
        'Your models, runtimes, logs, cache, and settings in:' + #13#10 +
        DataDir + #13#10 + #13#10 +
        'will be kept by default. Delete this data too?',
        mbConfirmation,
        MB_YESNO or MB_DEFBUTTON2,
        IDNO) = IDYES;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and DeleteAppDataOnUninstall then
    DelTree(ExpandConstant('{app}\data'), True, True, True);
end;
