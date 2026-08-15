# Jobbklikk menü — architektúra és korlátok (F4)

A Pilaster saját jobbklikk-menüje **teljes egészében a mi designunk**
(lekerekítés, téma-tokenek, ikonok, animáció), de megjeleníti a telepített
Windows shell-bővítmények bejegyzéseit is, és azok ugyanazt csinálják, mint az
Intézőben.

## Hogyan működik

1. `ShellItem.Open` a kijelölt elemekre, majd
   `IShellFolder::GetUIObjectOf` → `IContextMenu`
   (a `Vanara.Windows.Shell.ShellContextMenu`-n keresztül). Mappa üres
   területénél `IShellFolder::CreateViewObject(IID_IContextMenu)`.
2. `QueryContextMenu` egy **rejtett** `HMENU`-be
   (`CMF_NORMAL`, Shift lenyomásakor `| CMF_EXTENDEDVERBS`).
3. A menüfa kiolvasása `GetMenuItemInfo`-val: szöveg, típus, állapot
   (letiltott/pipált), `HBITMAP` ikon, almenü rekurzívan (max. 5 szint).
4. Dinamikusan feltöltődő almenük (7-Zip, Virtual CloneDrive):
   `IContextMenu3::HandleMenuMsg2(WM_INITMENUPOPUP, …)`, ennek hiányában
   `IContextMenu2::HandleMenuMsg`.
5. Ikonok: `HBITMAP` → `Bgra32` `BitmapSource`, **alfa megtartásával**. A
   beépített `CreateBitmapSourceFromHBitmap` eldobja az alfát, ezért kézzel
   olvassuk ki a bitteket, és visszaosztjuk az előszorzott alfát. A végig
   nulla alfájú (hibásan kitöltött) bitmapeket átlátszatlanként kezeljük,
   különben láthatatlanok lennének.
6. Kattintáskor `IContextMenu::InvokeCommand` `CMINVOKECOMMANDINFOEX`-szel,
   a menü azonosítójából levont eltolással, kitöltött `hwnd`-del és
   `nShow`-val.

A beolvasás után a menüfa **sima adatobjektum** (`ShellMenuNode`) — a
megjelenítés tisztán WPF. A natív oldalról csak a parancsazonosító marad meg,
amivel a kattintás visszahívható.

## Robusztusság

- **Aszinkron, időkorláttal.** A saját elemek azonnal megjelennek; a
  shell-elemek utólag csúsznak be. Időtúllépésnél a menü egyszerűen a saját
  elemekkel marad.
- **EGY közös, hosszú életű STA szál** (`StaWorker`). A shell-bővítmények
  COM-példányai apartment-kötöttek, és a lekérdezés és a végrehajtás két külön,
  időben távoli művelet — ezért a szálnak élnie kell a menü bezárásáig.
  Korábban minden lekérdezés SAJÁT szálat kapott (menünként új szál + új COM
  apartment); most egy közös szál sorosítja a munkát. Ha egy bővítmény
  beragad rajta, a szálat eldobjuk (`RetireShared`), és a következő lekérdezés
  frisset kap — e nélkül egyetlen rossz bővítmény az összes további menüt
  megbénítaná.
- **Minden hívás védve.** `COMException`, `Win32Exception`, `ArgumentException`,
  `InvalidOperationException` mind elnyelve; egy hibás bővítmény nem ejti el az
  appot, és a beolvasás a többi elemmel folytatódik.
- **Mélységkorlát (5).** Egy hibás bővítmény önmagára mutató almenüt is
  visszaadhat; e nélkül végtelen rekurzió lenne.
- **Feketelista.** A Beállítások → Jobbklikk menü szakaszban név vagy CLSID
  alapján kizárhatók az egyes bővítmények.

## Ismert korlát: nincs folyamat-izoláció

A spec javasolta, hogy a shell-lekérdezés **külön segédfolyamatban** fusson, és
JSON-ben adja vissza a menüfát. A jelenlegi megvalósítás **egy folyamaton
belül**, dedikált STA szálon dolgozik.

Mit jelent ez a gyakorlatban:

| Hiba a bővítményben | Izolált? |
|---|---|
| Kivételt dob (COM hiba, érvénytelen argumentum) | **Igen** — elnyelve |
| Lassú / beragad | **Igen** — időkorlát, a menü nélküle nyílik meg |
| Hibás menüfát ad (önhivatkozás, üres szöveg) | **Igen** — mélységkorlát és szűrés |
| Hozzáférési hiba (AV) a natív kódjában | **Nem** — ez a folyamatot viszi |

A spec szövege szerint a helper-folyamat elsősorban a nem natív (Electron/Tauri)
platformoknak szól; a Pilaster natív Win32, tehát az in-process hívás
támogatott. A hard crash elleni védelem azonban így hiányzik — ez tudatos,
elhalasztott döntés, nem feledékenység. A helper-folyamat bevezetése
visszafelé kompatibilis: a `ShellMenuSession` publikus felülete
(`QueryItemsAsync` / `QueryBackgroundAsync` / `InvokeAsync`) változatlanul
maradhat, csak a megvalósítása kerülne IPC mögé.

### Ez nem elméleti: MÉRVE összeomlik (v1.1-be halasztva)

A T2-höz készült terheléses mérés közben a folyamat rendszeresen elszállt
**`0xC0000374` (STATUS_HEAP_CORRUPTION)** hibával. Amit a mérés mutat:

| Eset | Összeomlás |
|---|---|
| **Fájl**-menü, 8 lekérdezés/futás, 150 ms szünet | 5 / 10 futás |
| **Fájl**-menü, 8 lekérdezés/futás, 1500 ms szünet | 5 / 6 futás |
| **Mappa**-menü, 8 lekérdezés/futás | **0 / 6 futás** |
| Rendes indulás (1 előmelegítő lekérdezés), harness nélkül | **0 / 8 futás** |

Amit kizártunk méréssel:

- **Nem a közös STA szál okozza.** A régi, menünként új szálas változat
  ugyanígy omlik (3/6), a közös szálas 2/6 — a különbség a zajban van.
- **Nem az ikon-átalakítás.** Kikapcsolt `TryConvertBitmap` mellett 7/10.
- **Nem (csak) a felszabadítás sorrendje.** Mindent szándékosan MEGSZIVÁROGTATVA
  is 5/6 — sőt így rosszabb, mert a véglegesítők (finalizer) az MTA
  finalizer-szálon engednék el az apartment-kötött COM-objektumokat.

Két valódi hibát viszont ez a nyomozás talált meg és javított:

1. A `keepAlive` objektumot a `ShellContextMenu` UTÁN kell elengedni (a Vanara
   dokumentációja szerint túl kell élnie) — a lista fordítva ürült, tehát
   előbb szabadult fel.
2. A `ShellItem`-eket NEM mi szabadítjuk fel: azok a `keepAlive` tulajdonai.
   A dupla felszabadítás önmagában 5/6-ról 1/6-ra vitte le a rátát.

**Következtetés:** a maradék korrupció a betöltött, harmadik féltől származó
bővítmény-DLL-ekben keletkezik (ezen a gépen 21 db: NVIDIA, AVG, Nextcloud,
Google DriveFS, OneDrive), és in-process architektúrában **nem javítható** —
pontosan ezért van a specben a folyamat-izoláció. Ez a **v1.1 első feladata**.

Ami a v1.0-t addig is használhatóvá teszi: a normál használat mintája (egy menü,
emberi tempóban) a mérésekben **nem** omlott össze, és a `ShellCrashGuard`
(lásd lent) a következő indulásnál kimenti a felhasználót. Aki gyakran
jobbklikkel fájlokon, annak a **Beállítások → Jobbklikk menü → shell-bővítmények
kikapcsolása** ad azonnali, teljes védelmet.

## Előmelegítés és az időkorlát alapértéke

### Mért számok

Fejlesztői gép, `C:\Windows\notepad.exe` (28 menüelem), 6 egymás utáni
lekérdezés futásonként:

| Eset | Első lekérdezés | Többi mediánja |
|---|---|---|
| Hidegen, előmelegítés nélkül | **2186 ms** | 898 ms |
| Csak mappa-előmelegítés után | 1825 ms | 869 ms |
| Mappa + fájl előmelegítés után | **1132 ms** | **777 ms** |

Mappára (`C:\Windows\System32`, 29 elem), előmelegítés után: első **1566 ms**,
többi mediánja **324 ms**.

### Amit ebből megtanultunk

1. Az első lekérdezés költsége EGYSZERI: COM apartment indulás +
   bővítmény-DLL-ek betöltése. Ez előmelegítéssel a felhasználó elől
   elrejthető — ezért fut a `ShellMenuSession.WarmUp()` induláskor,
   alacsony prioritású STA háttérszálon.
2. A **mappa** és a **fájl** menü KÜLÖN DLL-készletet használ. Csak a mappát
   melegítve az első fájl-lekérdezés alig gyorsult (2186 → 1825 ms); mindkettőt
   melegítve viszont 1132 ms-ra esett. Ezért melegít a `WarmUp()` mind a
   felhasználói profilra (mappa-menü), mind a saját futtatható fájlunkra
   (fájl-menü).
3. Az állandósult költség fájlokon **777 ms** — ez nem COM-indulás, hanem a
   telepített kezelők (tömörítők, víruskereső, szerkesztők) tényleges munkája
   fájlonként.

### Mi teszi ki a 777 ms-ot? (T1)

A rendes lekérdezés EGYETLEN, összefogott `IContextMenu`-t kap a shelltől,
amiből nem látszik a bontás. A `ShellHandlerProbe` ezért megkerüli a shellt: a
registryből maga szedi össze a kezelőket, egyenként példányosítja őket, és
külön méri a `CoCreateInstance` és a `QueryContextMenu` idejét.

Fejlesztői gép, `C:\Windows\notepad.exe`, **21 kezelő**, két-két hideg és meleg
kör:

| Kezelő | DLL | Létrehozás | Lekérdezés | Összesen |
|---|---|---|---|---|
| **NvAppShExt Class** (NVIDIA) | `nv3dappshext.dll` | 2–16 ms | **632–783 ms** | **648–786 ms** |
| Nextcloud context menu handler | `NCContextMenu.dll` | 3–11 ms | 62–119 ms | 67–130 ms |
| AVG | `ashShell.dll` | 8–60 ms | 5–69 ms | 13–125 ms |
| Pin To Start Screen verb handler | `appresolver.dll` | 8–9 ms | 61–73 ms | 69–82 ms |
| DriveFS ContextMenu Handler | `drivefsext.dll` | 11–19 ms | 13–19 ms | 25–36 ms |

Mappára (`C:\Users\<név>`, 21 kezelő) a leglassabb: Nextcloud 52–129 ms,
`Open With` (`shell32.dll`) 45–140 ms, DriveFS 29–54 ms.

**A tanulság:** a fájlmenü ~780 ms-ának a döntő részét EGYETLEN kezelő, az
NVIDIA `NvAppShExt` adja — és ez az egyetlen, ami **nem melegszik be**: hidegen
és melegen egyaránt 650–790 ms. A többi kezelő együtt sem éri el a felét.

Ezért a `WarmUp()` a fájlmenün keveset tud javítani: nem betöltési költséget
mérünk, hanem egy kezelő tényleges munkáját minden egyes lekérdezésnél.

### Diagnosztika és javaslat

A mérés a kiadott kódban is elérhető, de **csak `Debug` naplószinten**
(Beállítások → Speciális → Naplózás szintje), mert minden bővítményt betölt.
Ilyenkor a napló felsorolja az 5 leglassabb kezelőt, és a **400 ms** fölöttieket
külön figyelmeztetéssel megnevezi.

A program **nem tilt le magától semmit**: egy kezelő kikapcsolása funkciót vesz
el (a Nextcloud menüje valódi munkafolyamat), ezt nem dönthetjük el a
felhasználó helyett. A napló megnevezi a vétkest, a döntés a
**Beállítások → Jobbklikk menü → Kikapcsolt bővítmények** mezőé.

Ezen a gépen a javaslat: `NvAppShExt` felvétele a feketelistára — ez egymaga
kb. **650–780 ms-ot** venne le a fájlmenü idejéből.

### A választott alapérték: 2000 ms

A spec 400 ms-ot javasolt, és 800 ms-ra való visszatérést, HA az előmelegítés
utáni első lekérdezés stabilan 800 ms alatt marad. **Nem marad: 1132 ms.** Sőt,
a fájlokra vonatkozó állandósult 777 ms is épphogy a küszöb alatt van, tehát
800 ms mellett a menük jelentős részéről lemaradnának a bővítmények.

Az alapérték ezért az előmelegítés utáni legrosszabb mért értékből számol:
**1132 ms × 1,75 ≈ 1980 → 2000 ms.** A szorzó a lassabb gépek tartaléka.

Ez nem kerül semmibe, mert a menü nem várja meg a lekérdezést — lásd alább.

## A menü nem blokkol (mérve)

A `PilasterContextMenu.Show()` szinkron felépíti a saját elemeket, megnyitja a
menüt, és csak UTÁNA indítja a shell-lekérdezést.

**Helyesbítés (Q1).** A korábban itt szereplő **96 ms** egy *csonka* mérésből
származott: a harness egy kézzel összerakott, 3 elemű listát adott a menünek,
nem a valódi menüt. Újramérve az ÉLES `BuildFileMenuEntries` kimenetével
(**22 elem**, ikonokkal):

| Build | Első menü | Többi (medián) |
|---|---|---|
| Debug | 132 ms | **99 ms** |
| Release | 215 ms | **78 ms** |

Fázisbontás (Debug): leíró-építés 0–1 ms, menüelem-építés 3–10 ms, a
megnyitás (`IsOpen = true`) **60–136 ms** — vagyis a költség szinte teljesen a
WPF menü-megnyitásé. Az üvegeffektus (`ApplyToContextMenu` → DWM Acrylic)
külön mérve **1–2 ms**, tehát nem az.

Release buildben tehát a menü a **100 ms-os küszöb alatt** van; egyedül a
munkamenet ELSŐ menüje lép fölé (215 ms), ami JIT és a WPF-UI ikonfont egyszeri
betöltése.

Amíg a bővítmények töltődnek, egy szeparátor alatt alacsony kontrasztú
„Bővítmények betöltése…" sor foglalja a helyet, hogy a menü ne ugráljon,
amikor az elemek megérkeznek.

## Összeomlás-felismerés (P3)

A lekérdezés és a parancsvégrehajtás köré a `ShellCrashGuard` jelzőfájlt ír
(`%LOCALAPPDATA%\Pilaster\shell.inflight`), és sikeres befejezéskor törli. Ha a
következő indulás beragadt jelzőt talál, az azt jelenti, hogy a folyamat egy
shell-hívás közben halt meg:

- a bővítmények betöltése **kimarad** (a warmup sem indul),
- a jobbklikk-menü tetején egy sor jelzi ezt, a bűnös **útvonalával**,
- a Beállítások → Jobbklikk menü szakaszban megjelenik a „Bővítmények újra
  bekapcsolása" gomb.

Ez pontosan azt az esetet menti ki, amire a folyamat-izoláció kellett volna.

## Mért eredmény (fejlesztői gép)

`C:\Windows\notepad.exe`-re, üres feketelistával:

- 25 felső szintű elem, 60 elem összesen (almenükkel együtt)
- **7-Zip**: 13 elemű almenü
- Ikonos elemek: Futtatás rendszergazdaként, Edit with Notepad++,
  AVG adatmegsemmisítő, File Locksmith, PowerRename, Törlés
- „Küldés" almenü, „Korábbi verziók visszaállítása", „Megosztás",
  „Másolás elérési útként" — mind jelen
- Minden nem-elválasztó, almenü nélküli elemnek van érvényes parancsazonosítója
