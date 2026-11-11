; Установщик Dynamic Island (Inno Setup 6)
; Сборка: installer\build.ps1  →  dist\DynamicIsland-Setup-<версия>.exe

#define AppName "Dynamic Island"
#ifndef AppVersion
  #define AppVersion "1.0.0" ; обычно передаётся из build.ps1 (берётся из DynamicIsland.csproj)
#endif
#define AppExe "DynamicIsland.exe"
#define AppPublisher "Dynamic Island"

[Setup]
AppId={{6F1C2A7E-4B7D-4E8B-9C1A-7D3E2B5A9F10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
; Ставим для текущего пользователя: права администратора не нужны, Windows меньше ругается
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Dynamic Island
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\dist
OutputBaseFilename=DynamicIsland-Setup-{#AppVersion}
SetupIconFile=..\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Закрыть запущенный островок перед обновлением/удалением
CloseApplications=force
RestartApplications=no

[Languages]
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Запускать вместе с Windows"; GroupDescription: "Дополнительно:"
Name: "desktopicon"; Description: "Ярлык на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked

[Files]
Source: "..\dist\app\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Автозапуск — тот же ключ, что и переключатель в настройках островка
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "DynamicIsland"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Запустить {#AppName}"; Flags: nowait postinstall skipifsilent
; Тихое обновление из самого островка (/UPDATE=1): после установки сразу запускаем новую версию
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsUpdate

[UninstallRun]
; Островок держит одну копию — закрываем его, чтобы удалить файл
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillIsland"

[UninstallDelete]
Type: dirifempty; Name: "{app}"

[Code]
function IsUpdate: Boolean;
begin
  Result := ExpandConstant('{param:UPDATE|0}') = '1';
end;

// При удалении спросить, стереть ли настройки и заметки
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  dir, runValue: string;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // Автозапуск мог включиться из настроек самого островка — убираем, если он указывает на эту установку
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DynamicIsland', runValue) then
      if Pos(Lowercase(ExpandConstant('{app}')), Lowercase(runValue)) > 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'DynamicIsland');
    dir := ExpandConstant('{userappdata}\DynamicIsland');
    // При тихом удалении (например, при обновлении) настройки не трогаем
    if DirExists(dir) and not UninstallSilent then
      if MsgBox('Удалить также настройки, заметки и напоминания Dynamic Island?', mbConfirmation, MB_YESNO) = IDYES then
        DelTree(dir, True, True, True);
  end;
end;
