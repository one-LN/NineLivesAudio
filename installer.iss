; Inno Setup Script for Nine Lives Audio
; Download Inno Setup 6 from https://jrsoftware.org/isinfo.php
; Compile: Run ISCC.exe installer.iss (or open in Inno Setup GUI)

#define MyAppName "Nine Lives Audio"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "Static Hum Studio"
#define MyAppURL "https://github.com/StaticHumStudio/NineLivesAudio"
#define MyAppExeName "NineLivesAudio.exe"
#define MyAppSourceDir "binpublish"

[Setup]
AppId={{E7A3F2B1-9C4D-4E5F-8A6B-3D2C1F0E9D8A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=installer_output
OutputBaseFilename=NineLivesAudioSetup
SetupIconFile=Assets\app-icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; Upgrades leave the previous version's files in place, so an older build's language
; folders (cs/ de/ fr/ ja/ ...) survived on top of the trimmed package. Remove them
; before copying, keeping the two cultures the app ships (en*, zh*).
[InstallDelete]
Type: filesandordirs; Name: "{app}\cs"
Type: filesandordirs; Name: "{app}\da"
Type: filesandordirs; Name: "{app}\de"
Type: filesandordirs; Name: "{app}\el"
Type: filesandordirs; Name: "{app}\es"
Type: filesandordirs; Name: "{app}\fi"
Type: filesandordirs; Name: "{app}\fr"
Type: filesandordirs; Name: "{app}\hu"
Type: filesandordirs; Name: "{app}\it"
Type: filesandordirs; Name: "{app}\ja"
Type: filesandordirs; Name: "{app}\ko"
Type: filesandordirs; Name: "{app}\nb"
Type: filesandordirs; Name: "{app}\nl"
Type: filesandordirs; Name: "{app}\pl"
Type: filesandordirs; Name: "{app}\pt-BR"
Type: filesandordirs; Name: "{app}\pt-PT"
Type: filesandordirs; Name: "{app}\ro"
Type: filesandordirs; Name: "{app}\ru"
Type: filesandordirs; Name: "{app}\sv"
Type: filesandordirs; Name: "{app}\tr"
Type: filesandordirs; Name: "{app}\uk"

[Files]
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
