; ===========================================================================
;  BC Container Creator — Inno Setup Installer
;  Bauen via build/build-installer.ps1 (ruft ISCC.exe).
; ===========================================================================

#define MyAppName        "BC Container Creator"
; Version kommt normalerweise von build-installer.ps1 via /DMyAppVersion
; (gelesen aus der App-csproj). Der #ifndef-Guard ist Pflicht — ein
; unbedingtes #define würde den CLI-Wert überschreiben. Der Fallback greift
; nur bei direktem ISCC-Aufruf ohne Build-Skript.
#ifndef MyAppVersion
  #define MyAppVersion   "0.0.0"
#endif
#define MyAppPublisher   "Thomas Scharf"
#define MyAppURL         "https://github.com/kaminarixo/BcContainerCreator"
#define MyAppExeName     "BcContainerCreator.exe"
#define MyAppDirName     "BcContainerCreator"

; Erwarteter Publish-Output-Ordner — relativ zur .iss-Datei.
#define PublishDir       "..\dist\publish"

[Setup]
AppId={{8E0F7A12-BFB3-4FE8-B9A5-48FD50A15A9A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppDirName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=no
LicenseFile=
OutputDir=..\dist
OutputBaseFilename=BcContainerCreator-Setup-{#MyAppVersion}
Compression=lzma2/ultra
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
MinVersion=10.0
ShowLanguageDialog=auto
SetupLogging=yes
; Setup.exe selbst trägt das Brand-Icon. Wird auch als Programm-Icon im
; Apps-&amp;-Features-Eintrag verwendet (zusammen mit UninstallDisplayIcon).
SetupIconFile=..\src\BcContainerCreator.App\Assets\icon.ico

[Languages]
Name: "german";  MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Alle publish-Output-Files (EXE + Modules + runtimes + native libs) ins {app}.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Logs öffnen"; Filename: "{cmd}"; Parameters: "/c explorer ""%ProgramData%\BcContainerCreator\Logs"""; IconFilename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

; ===========================================================================
;  Pre-Install: .NET 10 Desktop Runtime Check
;  Wenn fehlt: Hinweis + optional Download-Seite öffnen + Setup abbrechen.
; ===========================================================================
[Code]
const
  RequiredRuntimeMarker = 'Microsoft.WindowsDesktop.App 10.';
  DotNetDownloadUrl     = 'https://dotnet.microsoft.com/download/dotnet/10.0';

function IsDotNet10DesktopInstalled(): Boolean;
var
  ResultCode: Integer;
  Lines: TArrayOfString;
  i: Integer;
  TempFile: String;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\dotnet-list.txt');

  if not Exec(ExpandConstant('{cmd}'),
              '/c dotnet --list-runtimes > "' + TempFile + '" 2>&1',
              '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    // dotnet selbst nicht im PATH — also keine Runtime.
    Exit;
  end;

  if ResultCode <> 0 then Exit;

  if LoadStringsFromFile(TempFile, Lines) then
  begin
    for i := 0 to GetArrayLength(Lines) - 1 do
    begin
      if (Pos(RequiredRuntimeMarker, Lines[i]) > 0) then
      begin
        Result := True;
        Break;
      end;
    end;
  end;

  DeleteFile(TempFile);
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Result := True;

  if IsDotNet10DesktopInstalled() then Exit;

  case MsgBox(
    '.NET 10 Desktop Runtime (x64) wurde nicht gefunden.' + #13#10 + #13#10 +
    'Diese ist Voraussetzung für BC Container Creator.' + #13#10 + #13#10 +
    'Möchten Sie die offizielle Download-Seite öffnen?' + #13#10 +
    'Setup wird danach abgebrochen, damit Sie die Runtime installieren können.',
    mbConfirmation, MB_YESNOCANCEL) of
    IDYES:
      begin
        ShellExec('open', DotNetDownloadUrl, '', '', SW_SHOW, ewNoWait, ResultCode);
        Result := False;
      end;
    IDNO:
      begin
        // User will trotzdem installieren (z. B. zum Aufbewahren / Test).
        Result := True;
      end;
    IDCANCEL:
      begin
        Result := False;
      end;
  end;
end;
