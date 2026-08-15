; Pilaster telepítő — Inno Setup 6
;
; Fordítás:
;   iscc /DAppVersion=1.0.0 /DArch=x64 /DSourceDir=..\publish\win-x64 Pilaster.iss
;
; Miért Inno Setup 6 (és nem MSIX vagy NSIS)?
;   A jelenlegi build pipeline (.github/workflows/release.yml) egy sima
;   self-contained `dotnet publish` mappát ad, amit egyetlen `iscc` hívás
;   csomagol — az MSIX aláírási tanúsítványt és csomagidentitást követelne
;   (amivel ma nem rendelkezünk), az NSIS pedig ugyanezért a funkcionalitásért
;   lényegesen több kézzel írt szkriptet igényelne. Az Inno `WizardStyle=modern`
;   varázslója ma is korszerűen néz ki, és teljes körű Pascal-szkriptet ad a
;   testreszabáshoz.
;
; Miért per-user az alapértelmezés (PrivilegesRequired=lowest):
;   1. Nem kér UAC-jóváhagyást, tehát a telepítés súrlódásmentes.
;   2. Az alapértelmezett fájlkezelővé állítás úgyis a HKCU kulcs alá ír,
;      tehát rendszerszintű jogosultságra sehol nincs szükség.
;   3. Rendszergazdai telepítőt a víruskeresők eleve gyanakvóbban néznek.
;   Per-machine telepítés az /ALLUSERS parancssori kapcsolóval kérhető — az
;   kéri az admin jogot. Lásd a PrivilegesRequiredOverridesAllowed melletti
;   mérési jegyzetet, hogy miért nem a varázsló-párbeszéd adja ezt.

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
#define AppTagline "Modern kétpaneles fájlkezelő Windowsra"

; Ennek EGYEZNIE kell az App.AppUserModelId konstanssal (src/Pilaster.App/App.xaml.cs).
; Enélkül a tálcára rögzített parancsikon más ikont és külön csoportot kapna,
; mint a futó folyamat — ez volt a v0.9-es „rossz ikon a tálcán" hiba egyik fele.
#define AppUserModelId "Obsidix.Pilaster"

[Setup]
; Ez az azonosító köti össze a frissítéseket az eltávolítóval — SOHA ne változzon.
; Enélkül minden verzió külön bejegyzést hozna létre a Programok listájában.
AppId={{8F3A6C21-5B4E-4D9A-9E27-1C0B7A5D3E64}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases

; Az exe erőforrásába kerülő verzióinformáció.
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright=Copyright © {#AppPublisher}
VersionInfoDescription={#AppName} telepítő

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=no
DisableReadyPage=no
AllowNoIcons=yes

PrivilegesRequired=lowest

; MÉRVE, két lépésben:
;   1. "dialog" override mellett a CSENDES telepítés (/VERYSILENT) per-machine
;      ágra ment: HKLM-be írt, a parancsikonokat a Public Desktopra tette.
;   2. "commandline dialog" mellett a "Telepítési mód kiválasztása" ablak
;      /VERYSILENT mellett is FELUGROTT, és megakasztotta a telepítést.
; Mindkettő ellentétes a tervezett viselkedéssel (per-user alapértelmezés,
; UAC és párbeszéd nélkül). Csak a "commandline" marad: a telepítés mindig a
; PrivilegesRequired értékét (lowest) követi, a per-machine telepítés pedig
; kifejezetten kérhető az /ALLUSERS kapcsolóval.
PrivilegesRequiredOverridesAllowed=commandline

OutputDir=..\dist
OutputBaseFilename=Pilaster-{#AppVersion}-{#Arch}-setup
SetupIconFile=..\assets\brand\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

WizardStyle=modern
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
WizardImageStretch=no
WizardSizePercent=110

; A licencet a Ready lapon jelöltnégyzettel fogadtatjuk el, nem rádiógombokkal.
LicenseFile=..\LICENSE

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

; Futó példány felismerése: a telepítő megkéri a felhasználót a bezárásra,
; NEM öl folyamatot figyelmeztetés nélkül.
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no

[Languages]
Name: "hu"; MessagesFile: "compiler:Languages\Hungarian.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
hu.Tagline={#AppTagline}
hu.TypeNormal=Normál telepítés
hu.TypeNormalDesc=A szokásos összetevők, asztali parancsikonnal.
hu.TypeCustom=Egyedi telepítés
hu.TypeCustomDesc=Minden összetevő és integráció külön kiválasztható.
hu.TypePortable=Hordozható (portable)
hu.TypePortableDesc=Nincs registry-írás; a beállítások a program mappájába kerülnek.
hu.CreateDesktopIcon=Parancsikon az asztalra
hu.CreateStartMenu=Bejegyzés a Start menübe
hu.ContextMenu=„Megnyitás Pilasterben" a jobbklikk-menübe
hu.FileAssoc=Fájltársítások
hu.StartWithWindows=Indítás a Windowsszal
hu.DefaultFileManager=Legyen az alapértelmezett fájlkezelő
hu.LaunchApp=A {#AppName} indítása
hu.ViewChangelog=Változásnapló megnyitása
hu.AdditionalTasks=További feladatok
hu.Integrations=Rendszerintegráció
hu.RemoveSettings=Törli a Pilaster felhasználói beállításait és címkéit is?%n%nHa a Nem gombot választja, a beállításai megmaradnak egy későbbi újratelepítéshez.
hu.RemoveSettingsTitle=Beállítások eltávolítása

en.Tagline=A modern dual-pane file manager for Windows
en.TypeNormal=Normal installation
en.TypeNormalDesc=The usual components, with a desktop shortcut.
en.TypeCustom=Custom installation
en.TypeCustomDesc=Pick every component and integration individually.
en.TypePortable=Portable
en.TypePortableDesc=No registry writes; settings live in the program folder.
en.CreateDesktopIcon=Create a desktop shortcut
en.CreateStartMenu=Add a Start menu entry
en.ContextMenu="Open in Pilaster" in the context menu
en.FileAssoc=File associations
en.StartWithWindows=Start with Windows
en.DefaultFileManager=Make the default file manager
en.LaunchApp=Launch {#AppName}
en.ViewChangelog=Open the changelog
en.AdditionalTasks=Additional tasks
en.Integrations=System integration
en.RemoveSettings=Also delete Pilaster's user settings and tags?%n%nChoose No to keep your settings for a future reinstall.
en.RemoveSettingsTitle=Remove settings

[Types]
Name: "normal"; Description: "{cm:TypeNormal}"
Name: "custom"; Description: "{cm:TypeCustom}"; Flags: iscustom
Name: "portable"; Description: "{cm:TypePortable}"

[Components]
; Egyetlen kötelező összetevő: maga a program. A típusok között a KÜLÖNBSÉG a
; [Tasks] alapértékeiben és a portable jelzőfájlban van.
Name: "app"; Description: "{#AppName}"; Types: normal custom portable; Flags: fixed

; A [Tasks] szakasz NEM ismeri a Types paramétert (az a [Components]/[Files]
; sajátja). A telepítési típushoz igazodást ezért két eszköz adja:
;   - Check: not IsPortable — hordozható módban a registry-t író feladatok meg
;     sem jelennek, hiszen a portable telepítés definíció szerint nem ír oda;
;   - a [Code] CurPageChanged eljárása, ami a típus kiválasztásakor beállítja
;     az alapértelmezett pipákat (Normál: parancsikonok; Egyedi: a felhasználóra
;     bízva).
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalTasks}"
Name: "startmenu"; Description: "{cm:CreateStartMenu}"; GroupDescription: "{cm:AdditionalTasks}"
Name: "contextmenu"; Description: "{cm:ContextMenu}"; GroupDescription: "{cm:Integrations}"; Check: not IsPortable
Name: "fileassoc"; Description: "{cm:FileAssoc}"; GroupDescription: "{cm:Integrations}"; Flags: unchecked; Check: not IsPortable
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:Integrations}"; Flags: unchecked; Check: not IsPortable
Name: "defaultfm"; Description: "{cm:DefaultFileManager}"; GroupDescription: "{cm:Integrations}"; Flags: unchecked; Check: not IsPortable

[Files]
; A teljes publish mappa. Szándékosan NEM egyfájlos csomag: az önkicsomagoló
; viselkedés indításonként lassít, és a víruskeresők heurisztikái pont erre
; a mintára ugranak rá.
Source: "{#SourceDir}\*"; DestDir: "{app}"; Components: app; Flags: ignoreversion recursesubdirs createallsubdirs

; Hordozható módban egy jelzőfájl kerül a program mappájába — az alkalmazás
; ebből tudja, hogy a beállításait a saját mappájába kell írnia, nem az
; %APPDATA%-ba (lásd src/Pilaster.App/Services/AppDataLocator.cs).
; A fájlt a [Code] szakasz CurStepChanged eljárása írja ki, nem innen másoljuk:
; egy nulla bájtos jelzőfájlhoz nem kell forrásfájl.

[Icons]
; A System.AppUserModel.ID EGYEZIK a folyamatéval — enélkül a tálcára
; rögzítés más ikont és külön csoportot kapna.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: startmenu; AppUserModelID: "{#AppUserModelId}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; AppUserModelID: "{#AppUserModelId}"

[Registry]
; --- Jobbklikk-menü: fájl, mappa ÉS mappa-háttér verb ---
Root: HKA; Subkey: "Software\Classes\Directory\shell\PilasterOpen"; ValueType: string; ValueName: ""; ValueData: "Megnyitás Pilasterben"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\PilasterOpen"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\PilasterOpen\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: contextmenu; Flags: uninsdeletekey

Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\PilasterOpen"; ValueType: string; ValueName: ""; ValueData: "Megnyitás Pilasterben"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\PilasterOpen"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"; Tasks: contextmenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\PilasterOpen\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%V"""; Tasks: contextmenu; Flags: uninsdeletekey

Root: HKA; Subkey: "Software\Classes\Drive\shell\PilasterOpen"; ValueType: string; ValueName: ""; ValueData: "Megnyitás Pilasterben"; Tasks: contextmenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Drive\shell\PilasterOpen\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: contextmenu; Flags: uninsdeletekey

; --- ProgID a fájltársításokhoz ---
Root: HKA; Subkey: "Software\Classes\Pilaster.Folder"; ValueType: string; ValueName: ""; ValueData: "{#AppName} mappa"; Tasks: fileassoc; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Pilaster.Folder\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#AppExe},0"; Tasks: fileassoc
Root: HKA; Subkey: "Software\Classes\Pilaster.Folder\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: fileassoc

; --- Alapértelmezett fájlkezelő (per-user, HKCU) ---
; A bekapcsolás előtti állapotot az ALKALMAZÁS menti el (lásd
; ShellIntegrationService/AppSettings.ShellIntegration) — a telepítő csak
; beállítja, az eltávolító pedig törli a saját bejegyzését.
Root: HKCU; Subkey: "Software\Classes\Directory\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: defaultfm; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Drive\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" ""%1"""; Tasks: defaultfm; Flags: uninsdeletekey

; --- Indítás a Windowsszal ---
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Pilaster"; ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
Filename: "{#AppUrl}/releases"; Description: "{cm:ViewChangelog}"; Flags: postinstall shellexec skipifsilent unchecked

[UninstallDelete]
; Az alkalmazás futás közben létrehozott gyorsítótára — eltávolításkor menjen.
Type: filesandordirs; Name: "{localappdata}\Pilaster\cache"
Type: filesandordirs; Name: "{localappdata}\Pilaster\logs"
; Hordozható telepítésnél a beállítások is a program mappájában vannak.
Type: filesandordirs; Name: "{app}\config"
Type: files; Name: "{app}\portable.marker"

[Code]
{
  Csendes telepítés támogatás:
    Pilaster-setup.exe /VERYSILENT /NORESTART /DIR="C:\Pilaster" /PORTABLE=1 /TASKS="desktopicon,contextmenu"
  A /PORTABLE=1 kapcsoló az egyetlen egyedi paraméter; a /DIR és a /TASKS az
  Inno beépített kapcsolói.
}

var
  PortableRequested: Boolean;

function IsPortable: Boolean;
begin
  Result := PortableRequested or (WizardSetupType(False) = 'portable');
end;

function InitializeSetup: Boolean;
begin
  PortableRequested := ExpandConstant('{param:PORTABLE|0}') = '1';
  Result := True;
end;

{
  A licenc-elfogadás. Az Inno alapból két RÁDIÓGOMBOT tesz a licenc lapra
  ("Elfogadom" / "Nem fogadom el"); a spec egyetlen jelölőnégyzetet kér, ezért
  a rádiógombokat elrejtjük, és helyettük egy checkbox vezérli őket. A
  rádiógombok maradnak az igazság forrása — az Inno azok alapján engedélyezi a
  Tovább gombot —, csak nem látszanak.

  A Pascal Script felülről lefelé fordít: a kezelőt a rá hivatkozó
  InitializeWizard ELŐTT kell deklarálni.
}
procedure LicenseAcceptClick(Sender: TObject);
begin
  WizardForm.LicenseAcceptedRadio.Checked := TNewCheckBox(Sender).Checked;
  WizardForm.LicenseNotAcceptedRadio.Checked := not TNewCheckBox(Sender).Checked;
end;

procedure InitializeWizard;
var
  AcceptCheck: TNewCheckBox;
begin
  WizardForm.LicenseAcceptedRadio.Visible := False;
  WizardForm.LicenseNotAcceptedRadio.Visible := False;

  AcceptCheck := TNewCheckBox.Create(WizardForm);
  AcceptCheck.Parent := WizardForm.LicenseAcceptedRadio.Parent;
  AcceptCheck.Left := WizardForm.LicenseAcceptedRadio.Left;
  AcceptCheck.Top := WizardForm.LicenseAcceptedRadio.Top;
  AcceptCheck.Width := WizardForm.LicenseAcceptedRadio.Width;
  AcceptCheck.Height := WizardForm.LicenseAcceptedRadio.Height;
  AcceptCheck.Caption := SetupMessage(msgLicenseAccepted);
  AcceptCheck.OnClick := @LicenseAcceptClick;
end;

{
  A hordozható mód jelzőfájlja. Nulla bájtos, a puszta LÉTEZÉSE a jel — lásd
  AppDataLocator: a beállításokat magukat is ez alapján kell megtalálni, tehát
  nem lehet bennük tárolni a módot.
}
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and IsPortable then
    SaveStringToFile(ExpandConstant('{app}\portable.marker'), '', False);
end;

{
  Eltávolításkor a felhasználói beállítások SOSEM tűnnek el magától: külön
  rákérdezünk, és az alapértelmezés a megtartás (spec F3).
}
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ConfigDir: String;
  Remove: Boolean;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  ConfigDir := ExpandConstant('{userappdata}\Pilaster');

  if not DirExists(ConfigDir) then
    Exit;

  {
    MÉRVE: csendes eltávolításnál (/VERYSILENT) a MsgBox NEM az MB_DEFBUTTON2
    alapértelmezésével tért vissza, hanem a beállítások TÖRLŐDTEK. Ez a
    legrosszabb fajta hiba: a felhasználó adatait veszi el, méghozzá némán.

    Ezért a csendes ág többé nem a MsgBox-tól függ: alapból MEGTARTJA a
    beállításokat, és csak akkor törli, ha ezt a /DELETESETTINGS=1 kapcsolóval
    kifejezetten kérték. Interaktív eltávolításnál marad a kérdés, Nem
    alapértelmezéssel.
  }
  if UninstallSilent then
    Remove := ExpandConstant('{param:DELETESETTINGS|0}') = '1'
  else
    Remove := MsgBox(CustomMessage('RemoveSettings'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;

  if Remove then
    DelTree(ConfigDir, True, True, True);
end;
