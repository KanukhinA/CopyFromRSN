#define MyAppName      "SP CopyFromRSN"
#define MyAppVersion   "1.1.6"
#define MyAppPublisher "SmartProject"

[Setup]
AppId={{E7B4C2A1-5F3D-4E8B-9C1A-2D6F8E0B4A71}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\SP-CopyFromRSN
DisableDirPage=yes
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=C:\Users\Kanuhin_A\source\repos\SP\SP_CopyFromRSN\dist
OutputBaseFilename=SP_CopyFromRSN-{#MyAppVersion}-Setup
Compression=lzma
SolidCompression=yes
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
; При той же AppId Inno обновляет установку; перед копированием файлов
; старая версия снимается явно (см. [Code])
UsePreviousAppDir=yes
SetupIconFile=C:\Users\Kanuhin_A\source\repos\SP\SP_CopyFromRSN\Assets\icon.ico
UninstallDisplayIcon={app}\icon.ico

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Types]
Name: "full"; Description: "Все поддерживаемые версии Revit"
Name: "custom"; Description: "Выбранные версии"; Flags: iscustom

[Components]
Name: "revit2022"; Description: "Плагин для Autodesk Revit 2022"; Types: full custom
Name: "revit2023"; Description: "Плагин для Autodesk Revit 2023"; Types: full custom
Name: "revit2025"; Description: "Плагин для Autodesk Revit 2025"; Types: full custom
Name: "revit2026"; Description: "Плагин для Autodesk Revit 2026"; Types: full custom

[Files]
Source: "C:\Users\Kanuhin_A\source\repos\SP\SP_CopyFromRSN\Assets\icon.ico"; DestDir: "{app}"; Flags: ignoreversion
Source: "C:\Users\Kanuhin_A\source\repos\SP\SP_CopyFromRSN\dist\R2022\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2022"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: revit2022; Excludes: "Install-Standalone.ps1"
Source: "C:\Users\Kanuhin_A\source\repos\SP\SP_CopyFromRSN\dist\R2023\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2023"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: revit2023; Excludes: "Install-Standalone.ps1"
Source: "C:\Users\Kanuhin_A\source\repos\SP\SP_CopyFromRSN\dist\R2025\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2025"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: revit2025; Excludes: "Install-Standalone.ps1"
Source: "C:\Users\Kanuhin_A\source\repos\SP\SP_CopyFromRSN\dist\R2026\*"; DestDir: "{userappdata}\Autodesk\Revit\Addins\2026"; Flags: ignoreversion recursesubdirs createallsubdirs; Components: revit2026; Excludes: "Install-Standalone.ps1"

[Messages]
russian.FinishedLabel=Перезапустите Revit, чтобы загрузить плагин SP CopyFromRSN.

[Code]
// Если плагин уже установлен — сначала тихая деинсталляция, затем новая установка
function GetUninstallString(): String;
var
  UninstallKey: String;
  UninstallString: String;
begin
  Result := '';
  UninstallKey := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{#emit SetupSetting("AppId")}_is1');
  UninstallString := '';
  // PrivilegesRequired=lowest → запись обычно в HKCU
  if RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', UninstallString) then
    Result := UninstallString
  else if RegQueryStringValue(HKLM, UninstallKey, 'UninstallString', UninstallString) then
    Result := UninstallString;
end;

function UninstallPreviousVersion(): Boolean;
var
  UninstallString: String;
  ResultCode: Integer;
begin
  Result := True;
  UninstallString := GetUninstallString();
  if UninstallString = '' then
    Exit;

  UninstallString := RemoveQuotes(UninstallString);
  Log('Найдена предыдущая установка, удаление: ' + UninstallString);
  if not Exec(UninstallString, '/SILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Не удалось удалить предыдущую версию SP CopyFromRSN. Закройте Revit и повторите установку.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if ResultCode <> 0 then
    Log('Код выхода деинсталлятора: ' + IntToStr(ResultCode));
end;

function InitializeSetup(): Boolean;
begin
  Result := UninstallPreviousVersion();
end;
