; Voice Room — Inno Setup Script
; Build: dotnet publish first, then compile this script.
#define AppVersion "1.1.0"

[Setup]
AppName=Voice Room
AppVersion={#AppVersion}
AppPublisher=Your Company
DefaultDirName={autopf}\VoiceRoom
DefaultGroupName=Voice Room
OutputDir=installer-output
OutputBaseFilename=VoiceRoom-Setup-{#AppVersion}
SetupIconFile=VoiceRoom\Resources\app-icon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: english; MessagesFile: compiler:Default.isl

[Tasks]
Name: startup; Description: "Start Voice Room with Windows"; GroupDescription: Options; Flags: unchecked

[Files]
Source: "publish\win-x64\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "publish\win-x64\Resources\app-icon.ico"; DestDir: "{app}\Resources"; Flags: ignoreversion
Source: "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\Voice Room"; Filename: "{app}\VoiceRoom.exe"
Name: "{commondesktop}\Voice Room"; Filename: "{app}\VoiceRoom.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VoiceRoom"; ValueData: """{app}\VoiceRoom.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Code]
function WebView2Installed: Boolean;
var
  version: String;
begin
  Result := RegQueryStringValue(HKLM,
    'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
    'pv', version) and (version <> '') and (version <> '0.0.0.0');

  if not Result then
    Result := RegQueryStringValue(HKCU,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', version) and (version <> '') and (version <> '0.0.0.0');
end;

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2RuntimeInstallerX64.exe"; Parameters: "/silent /install"; StatusMsg: "Installing WebView2 runtime..."; Flags: waituntilterminated; Check: not WebView2Installed
Filename: "{app}\VoiceRoom.exe"; Description: "Launch Voice Room"; Flags: nowait postinstall skipifsilent