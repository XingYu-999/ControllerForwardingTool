; 编译器：Inno Setup 6.5.0+；载荷：win-x64 / .NET 10 自包含文件夹发布。
#define MyAppName "ControllerForwardingTool"
#define MyAppExeName "ControllerForwardingTool.exe"
#ifndef MyPublishDir
  #define MyPublishDir "D:\Temp\Publish\ControllerForwardingTool\Release"
#endif
#ifndef MyOutputDir
  #define MyOutputDir "D:\Temp\Publish\ControllerForwardingTool"
#endif
#if !FileExists(MyPublishDir + "\" + MyAppExeName)
  #error "未找到 ControllerForwardingTool.exe，请先在 Visual Studio 手动发布到 D:\Temp\Publish\ControllerForwardingTool，或指定 MyPublishDir。"
#endif
#if !FileExists(MyPublishDir + "\coreclr.dll") || !FileExists(MyPublishDir + "\hostfxr.dll")
  #error "MyPublishDir 必须是 .NET 自包含文件夹发布目录。"
#endif
#if !FileExists(MyPublishDir + "\SDL3.dll") || !FileExists(MyPublishDir + "\libusb-1.0.dll") || !FileExists(MyPublishDir + "\drivers\viiper\viiper-haptic.exe") || !FileExists(MyPublishDir + "\drivers\usbip-win2\v0.9.7.7\USBip-0.9.7.7-x64.exe")
  #error "发布目录缺少 SDL、libusb、VIIPER 或 USB/IP 安装器，请完整发布项目。"
#endif
#ifndef MyAppVersion
  #define MyAppVersion GetVersionNumbersString(MyPublishDir + "\" + MyAppExeName)
#endif

[Setup]
; 本产品独立的固定 AppId；后续升级保持不变。
AppId={{0D3DCEB5-043B-4C82-ABF0-75BB9F9CBEB3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName=ControllerForwardingTool {#MyAppVersion}
; 根据安装范围自动选择当前用户的 Programs 或系统 Program Files 目录。
DefaultDirName={autopf}\ControllerForwardingTool
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
; 首次安装默认仅当前用户；选择所有用户时才请求管理员权限。
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; 升级沿用已有安装范围，避免默认切换范围而产生两份安装。
UsePreviousPrivileges=yes
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
UsePreviousAppDir=yes
UsePreviousTasks=yes
SetupLogging=yes
UninstallLogging=yes
SetupIconFile=..\Assets\ControllerForwardingTool.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir={#MyOutputDir}
OutputBaseFilename=ControllerForwardingTool-Setup-{#MyAppVersion}-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120,120
WizardKeepAspectRatio=yes
SetupMutex=ControllerForwardingTool.Setup
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: checkedonce
Name: "taskbaricon"; Description: "{cm:PinToTaskbar}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{code:StartupTaskDescription}"; GroupDescription: "{cm:OtherOptions}"; Flags: checkedonce
Name: "runasadmin"; Description: "{cm:RunAsAdmin}"; GroupDescription: "{cm:OtherOptions}"; Flags: unchecked

[Files]
; 完整发布目录包含 .NET、Avalonia、原生 DLL、drivers 和许可证。
; 用户配置、诊断和开发产物不属于程序载荷，绝不作为升级文件覆盖。
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.tmp,*.log,bridge-settings.json,window-placement.json,diagnostics,logs,*.tar.gz,*.tar.bz2"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\手柄转发工具"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\手柄转发工具"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
Name: "{autostartup}\手柄转发工具"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: autostart

[InstallDelete]
; Apply unchecked tasks on upgrade as well; remove only this product's shortcuts.
Type: files; Name: "{autodesktop}\手柄转发工具.lnk"; Tasks: not desktopicon
Type: files; Name: "{autostartup}\手柄转发工具.lnk"; Tasks: not autostart

[Registry]
; Current-user installs migrate the two known legacy Run entries to Startup.
; Elevated all-users installs must not edit another account's per-user startup.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Controller Forwarding Tool"; Flags: deletevalue; Check: not IsAdminInstallMode
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "NS2ProWin11"; Flags: deletevalue; Check: not IsAdminInstallMode

[Run]
; 由原登录用户运行，避免把用户配置初始化到提升后的管理员账户。
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser

; 配置由程序写入 %LOCALAPPDATA%\ControllerForwardingTool。
; 不开放 Program Files 写权限、不在安装器中创建用户数据、不随卸载删除用户配置。
; USB/IP 驱动由应用现有的“配置 USB/IP 驱动”入口按需安装/卸载。

[CustomMessages]
chinesesimplified.OtherOptions=其他选项：
chinesesimplified.PinToTaskbar=尝试固定到任务栏（取决于 Windows 支持）
chinesesimplified.StartupCurrentUser=开机时自动启动（当前用户）
chinesesimplified.StartupAllUsers=开机时自动启动（所有用户）
chinesesimplified.RunAsAdmin=以管理员身份启动
chinesesimplified.StartupAdminConflict=Startup 文件夹自启无法保证启动需要管理员权限的程序。请取消“开机时自动启动”或“以管理员身份启动”中的一项。
english.OtherOptions=Other options:
english.PinToTaskbar=Try to pin to taskbar (depends on Windows support)
english.StartupCurrentUser=Start automatically at sign-in (current user)
english.StartupAllUsers=Start automatically at sign-in (all users)
english.RunAsAdmin=Run as administrator
english.StartupAdminConflict=Startup folder shortcuts cannot reliably start an application that requires elevation. Please deselect either automatic startup or running as administrator.
chinesesimplified.InstallDirNotWritable=无法写入以下安装目录：%n%1%n%n请确认目录可用，并选择有写入权限的目录。
chinesesimplified.InstallDirCurrentUserHint=如果需要安装到受保护的目录，请退出并重新启动安装程序，选择“为所有用户安装”。升级已有安装时会沿用原范围，可使用 /ALLUSERS 指定所有用户模式。
english.InstallDirNotWritable=Setup cannot write to this installation folder:%n%1%n%nMake sure the location is available and choose a folder you can write to.
english.InstallDirCurrentUserHint=To install in a protected folder, exit and restart Setup, then select "Install for all users". Upgrades keep the previous install mode; use /ALLUSERS to select all-users mode explicitly.

[Code]
const
  CompatibilityKey = 'Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers';

function StartupTaskDescription(Param: String): String;
begin
  if IsAdminInstallMode then Result := CustomMessage('StartupAllUsers')
  else Result := CustomMessage('StartupCurrentUser');
end;

function AdditionalTasksError: String;
begin
  Result := '';
  if WizardIsTaskSelected('autostart') and WizardIsTaskSelected('runasadmin') then
    Result := CustomMessage('StartupAdminConflict');
end;

{ Preserve unrelated compatibility flags when changing RUNASADMIN. }
function WithRunAsAdmin(const Layers: String; Enabled: Boolean): String;
var
  Remaining, Token: String;
  P: Integer;
begin
  Result := '';
  Remaining := Trim(Layers);
  while Remaining <> '' do
  begin
    P := Pos(' ', Remaining);
    if P = 0 then P := Length(Remaining) + 1;
    Token := Copy(Remaining, 1, P - 1);
    Delete(Remaining, 1, P);
    Remaining := Trim(Remaining);
    if (CompareText(Token, 'RUNASADMIN') <> 0) and (Token <> '~') then
      Result := Trim(Result + ' ' + Token);
  end;
  if Enabled then Result := Trim(Result + ' RUNASADMIN');
  if Result <> '' then Result := '~ ' + Result;
end;

procedure SetRunAsAdmin(Enabled: Boolean);
var
  Existing, Updated, ExePath: String;
begin
  ExePath := ExpandConstant('{app}\{#MyAppExeName}');
  RegQueryStringValue(HKA, CompatibilityKey, ExePath, Existing);
  Updated := WithRunAsAdmin(Existing, Enabled);
  if Updated = Existing then Exit;
  if Updated = '' then
  begin
    if not RegDeleteValue(HKA, CompatibilityKey, ExePath) then
      RaiseException('Cannot remove administrator startup setting: ' + ExePath);
  end
  else if not RegWriteStringValue(HKA, CompatibilityKey, ExePath, Updated) then
    RaiseException('Cannot save administrator startup setting: ' + ExePath);
end;

procedure TryPinToTaskbar;
var
  ScriptPath, ScriptText: String;
  ExitCode: Integer;
begin
  { Execute the Shell request as the original user, including elevated installs. }
  ScriptPath := ExpandConstant('{tmp}\cft-pin-taskbar.vbs');
  ScriptText := 'Option Explicit' + #13#10 +
    'Dim sh, fso, folder, item, link' + #13#10 +
    'link = WScript.Arguments(0)' + #13#10 +
    'Set fso = CreateObject("Scripting.FileSystemObject")' + #13#10 +
    'Set sh = CreateObject("Shell.Application")' + #13#10 +
    'Set folder = sh.NameSpace(fso.GetParentFolderName(link))' + #13#10 +
    'Set item = folder.ParseName(fso.GetFileName(link))' + #13#10 +
    'item.InvokeVerb "taskbarpin"' + #13#10;
  try
    if SaveStringToFile(ScriptPath, AnsiString(ScriptText), False) then
      if ExecAsOriginalUser(ExpandConstant('{sys}\cscript.exe'),
        '//B //NoLogo ' + AddQuotes(ScriptPath) + ' ' +
        AddQuotes(ExpandConstant('{group}\手柄转发工具.lnk')), '',
        SW_HIDE, ewWaitUntilTerminated, ExitCode) then
        Log(Format('Taskbar pin request exited with code %d; Windows may ignore this request.', [ExitCode]));
  except
    Log('Taskbar pin request unavailable: ' + GetExceptionMessage);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    SetRunAsAdmin(WizardIsTaskSelected('runasadmin'));
    if WizardIsTaskSelected('taskbaricon') then TryPinToTaskbar;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then SetRunAsAdmin(False);
end;

function CreateProbeFile(FileName: String; DesiredAccess, ShareMode: Cardinal;
  SecurityAttributes: THandle; CreationDisposition, FlagsAndAttributes: Cardinal;
  TemplateFile: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';

function CloseProbeFile(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function CreateProbeDirectories(const Directory: String; Created: TStringList): Boolean;
var
  Parent: String;
begin
  Result := DirExists(Directory);
  if Result then Exit;
  Parent := ExtractFileDir(Directory);
  if (Parent = '') or (CompareText(Parent, Directory) = 0) then Exit;
  if not CreateProbeDirectories(Parent, Created) then Exit;
  Result := CreateDir(Directory);
  if Result then
    Created.Add(Directory)
  else
    Result := DirExists(Directory); { Another process may have created it. }
end;

function CanWriteInstallDirectory(const Directory: String): Boolean;
var
  Created: TStringList;
  Target, Probe: String;
  Handle: THandle;
  I: Integer;
begin
  Result := False;
  Created := TStringList.Create;
  try
    try
      Target := RemoveBackslashUnlessRoot(ExpandFileName(Directory));
      if not CreateProbeDirectories(Target, Created) then
      begin
        Log('Cannot create installation directory: ' + Target);
        Exit;
      end;

      Probe := GenerateUniqueName(AddBackslash(Target), '.tmp');
      { CREATE_NEW never overwrites existing files. Request write and delete access;
        DELETE_ON_CLOSE removes only our probe, including if Setup is interrupted. }
      Handle := CreateProbeFile(Probe, $40010000, 0, 0, 1, $04000100, 0);
      if Handle = THandle(-1) then
      begin
        Log(Format('Installation directory write check failed (%d): %s', [DLLGetLastError, Target]));
        Exit;
      end;
      Result := CloseProbeFile(Handle);
    except
      Log('Installation directory write check failed: ' + GetExceptionMessage);
      Result := False;
    end;
  finally
    { Remove only empty directories created by this check, never existing data. }
    for I := Created.Count - 1 downto 0 do
      if not RemoveDir(Created[I]) then
        Log('Could not remove empty write-check directory: ' + Created[I]);
    Created.Free;
  end;
end;

function InstallDirectoryError(const Directory: String): String;
begin
  Result := '';
  if CanWriteInstallDirectory(Directory) then Exit;
  Result := FmtMessage(CustomMessage('InstallDirNotWritable'), [Directory]);
  if not IsAdminInstallMode then
    Result := Result + #13#10#13#10 + CustomMessage('InstallDirCurrentUserHint');
  Log(Result);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Error: String;
begin
  Result := True;
  if CurPageID = wpSelectTasks then
  begin
    Error := AdditionalTasksError;
    Result := Error = '';
    if not Result and not WizardSilent then MsgBox(Error, mbError, MB_OK);
    Exit;
  end;
  if CurPageID <> wpSelectDir then Exit;
  Error := InstallDirectoryError(WizardDirValue);
  Result := Error = '';
  if not Result and not WizardSilent then
    MsgBox(Error, mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  { Recheck immediately before installing, including silent/skipped-page installs. }
  Result := InstallDirectoryError(WizardDirValue);
  if Result = '' then Result := AdditionalTasksError;
end;
