#ifndef AppVersion
  #define AppVersion "2.6.5-hdr.1"
#endif
#ifndef PayloadDir
  #error PayloadDir must point to the installer payload
#endif
#ifndef OutputDir
  #error OutputDir must point to the artifact directory
#endif

[Setup]
AppId={{5534ED6B-6291-41CD-9124-5B764BB19312}
AppName=Starshot HDR Fork
AppVersion={#AppVersion}
AppPublisher=Starshot HDR Fork contributors
AppPublisherURL=https://github.com/ItIsMF/Starshot
DefaultDirName={localappdata}\Programs\Starshot HDR Fork
DefaultGroupName=Starshot HDR Fork
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Starshot.exe
SetupIconFile=..\src\logo.ico
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=Starshot-HDR-Fork-{#AppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Starshot HDR Fork"; Filename: "{app}\Starshot.exe"; AppUserModelID: "Starshot.HDRFork"
Name: "{autodesktop}\Starshot HDR Fork"; Filename: "{app}\Starshot.exe"; AppUserModelID: "Starshot.HDRFork"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Starshot.HDRFork.Autorun"; Flags: uninsdeletevalue

[UninstallRun]
Filename: "{app}\app\Starshot.exe"; Parameters: "--manage-task delete"; Flags: runhidden; RunOnceId: "RemoveHDRForkAutorunTask"

; User settings and screenshots are intentionally retained on uninstall.
