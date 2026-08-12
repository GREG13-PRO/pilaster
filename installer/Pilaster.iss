; Pilaster telepítő — Inno Setup 6
;
; Fordítás:
;   iscc /DAppVersion=0.1.1 /DArch=x64 /DSourceDir=..\publish\win-x64 Pilaster.iss
;
; Miért per-user telepítés (PrivilegesRequired=lowest):
;   1. Nem kér UAC-jóváhagyást, tehát a telepítés súrlódásmentes.
;   2. Az alapértelmezett fájlkezelővé állítás úgyis a HKCU kulcs alá ír
;      (v0.7), tehát rendszerszintű jogosultságra sehol nincs szükség.
;   3. Rendszergazdai telepítőt a víruskeresők eleve gyanakvóbban néznek.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#ifndef Arch
  #define Arch "x64"
#endif

#ifndef SourceDir
  #define SourceDir "..\publish\win-" + Arch
#endif

#define AppName "Pilaster"
#define AppPublisher "Pilaster contributors"
#define AppUrl "https://github.com/GREG13-PRO/pilaster"
#define AppExe "Pilaster.exe"

[Setup]
; Ez az azonosító köti össze a frissítéseket az eltávolítóval — soha ne változzon.
AppId={{8F3A6C21-5B4E-4D9A-9E27-1C0B7A5D3E64}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
AllowNoIcons=yes

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

OutputDir=..\dist
OutputBaseFilename=Pilaster-{#AppVersion}-{#Arch}-setup
SetupIconFile=..\assets\brand\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

WizardStyle=modern
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
WizardImageStretch=no

Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=4

; A telepítő ne fusson 32 biten, ha a csomag 64 bites.
#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

[Languages]
Name: "hu"; MessagesFile: "compiler:Languages\Hungarian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
hu.CreateDesktopIcon=Parancsikon az asztalra
hu.LaunchApp=A {#AppName} indítása
hu.AdditionalTasks=További feladatok
en.CreateDesktopIcon=Create a desktop shortcut
en.LaunchApp=Launch {#AppName}
en.AdditionalTasks=Additional tasks

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[Files]
; A teljes publish mappa. Szándékosan NEM egyfájlos csomag: az önkicsomagoló
; viselkedés indításonként lassít, és a víruskeresők heurisztikái pont erre
; a mintára ugranak rá.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Az alkalmazás futás közben létrehozott gyorsítótára — eltávolításkor menjen.
Type: filesandordirs; Name: "{localappdata}\Pilaster\cache"
