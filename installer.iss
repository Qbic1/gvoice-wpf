; Voice Room — Inno Setup Script
; Build: dotnet publish first, then compile this script.
#define AppVersion "1.0.0"

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

[Icons]
Name: "{group}\Voice Room"; Filename: "{app}\VoiceRoom.exe"
Name: "{commondesktop}\Voice Room"; Filename: "{app}\VoiceRoom.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VoiceRoom"; ValueData: """{app}\VoiceRoom.exe"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\VoiceRoom.exe"; Description: "Launch Voice Room"; Flags: nowait postinstall skipifsilent