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

## Miért NYERS a fájlmenü beszerzése — ne tedd vissza a Vanara réteget

A fájlmenü a shell API-ját közvetlenül hívja
(`SHParseDisplayName` → `SHBindToParent` → `GetUIObjectOf`), nem a
`Vanara.Windows.Shell` objektummodelljét. Ez **nem stíluskérdés**: a
`ShellContextMenu.CreateFromItems` **heap-korrupcióval** (`0xC0000374`) vitte
a folyamatot, és a v1.0-t hetekig blokkolta.

A bizonyítás a `tools/ShellCrashRepro/` harnessben reprodukálható. A bisect a
MŰKÖDŐ oldalról indult, egyszerre egy változót mozgatva —
`C:\Windows\notepad.exe`, 10 kör, Release:

| Lépés | Mit változtat | Eredmény |
|---|---|---|
| **H2** | minimál harness, nyers P/Invoke | 4×10/10 tiszta → **nem a bővítmények** |
| **B1** (`nopump`) | üzenethurok helyett `BlockingCollection` munkasor | 3×10/10 tiszta → **nem a pumpálás hiánya** |
| **B2a** (`b2a`) | csak a `ShellItem` életciklusa Vanarából | 3×10/10 tiszta → **nem a `ShellItem`** |
| **B2** (`vanara`) | `ShellItem` + `CreateFromItems` | 3× `0xC0000374` → **EZ a vétkes** |
| B2 Vanara 5.0.6-tal | ugyanaz, frissebb csomaggal | 3× `0xC0000374` → a verziófrissítés nem old meg |

Korábban méréssel kizártuk a menüolvasót és az ikonkonvertert is (kikapcsolva
ugyanúgy elszállt), és a felszabadítás időzítését (mindent szándékosan
megszivárogtatva szintén elszállt).

Két buktató, amit a nyers út magával hoz, és amit könnyű elrontani:

1. A `SHBindToParent` **utolsó PIDL-je BELSŐ MUTATÓ** egy nagyobb allokáción
   belülre. Tilos felszabadítani; csak addig érvényes, amíg a szülő
   (abszolút) PIDL él.
2. A felszabadítás sorrendje: **COM-menü előbb, PIDL-ek utána** — a menü a
   PIDL-ekre hivatkozik. A `_keepAlive` lista fordítva ürül, ezért a felvétel
   sorrendje ennek a fordítottja.

### A reprodukciós eszköz

`tools/ShellCrashRepro/` — külön konzolalkalmazás, semmi Pilaster-kód. Módok:

| Kapcsoló | Mit futtat |
|---|---|
| *(nincs)* | nyers P/Invoke, pumpáló STA szálon — a zöld referencia |
| `nopump` | ugyanaz, munkasorral, üzenethurok nélkül |
| `vanara` | `ShellItem` + `ShellContextMenu.CreateFromItems` |
| `b2a` | csak a `ShellItem` életciklusa, a menü nyersen |

Használat: `ShellCrashRepro.exe <útvonal> <körök> [mód]`. Kilépési kód 0, ha
minden kör lefutott.

*(A „nyers PIDL + `CreateFromItems`" változat nem építhető meg: a
`CreateFromItems` szignatúrája `IEnumerable<ShellItem>`-et vár.)*

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
kb. **650–780 ms-ot** venne le a fájlmenü idejéből. A program ezt **nem teszi
meg magától**: egy kezelő kikapcsolása funkciót vesz el, és ez a felhasználó
döntése.

## Több mappát átfogó kijelölés (Polc / Shelf)

A `GetUIObjectOf` **azonos szülőmappát** vár: egyetlen munkamenet csak egy
mappa elemeire tud menüt adni. Ma ez nem korlát, mert a kijelölés mindig egy
listázásból jön.

A **Polc (Shelf)** funkcióval viszont ez meg fog változni: az több helyről
gyűjt fájlokat. Amikor az készül, a menü **mappánként külön munkamenetet**
kell hogy nyisson, és a kapott menüfákat össze kell fésülni (az azonos verb-ű
elemeket egyesítve). A `ShellMenuNode.Verb` már megvan ehhez — szándékosan
NYELVFÜGGETLEN azonosító, a felirat nem az.

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
