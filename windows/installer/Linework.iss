; Per-user install, so no administrator rights are needed. The library is the user's own folder, elsewhere, and uninstalling leaves it alone.
#ifndef AppVersion
  #define AppVersion "0.0"
#endif

[Setup]
AppId={{6E3C2B55-7A44-4C7E-9B7E-3F5A1D9C2E10}
AppName=Linework
AppVersion={#AppVersion}
AppPublisher=hyprsea solutions
DefaultDirName={localappdata}\Programs\Linework
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=Linework-Setup
SetupIconFile=..\Linework.App\Resources\AppIcon.ico
UninstallDisplayIcon={app}\Linework.exe
UninstallDisplayName=Linework
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Linework"; Filename: "{app}\Linework.exe"

[Run]
Filename: "{app}\Linework.exe"; Description: "Open Linework"; Flags: nowait postinstall skipifsilent
