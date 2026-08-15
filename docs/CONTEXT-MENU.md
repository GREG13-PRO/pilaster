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
- **Dedikált STA szál** munkamenetenként (`StaWorker`). A shell-bővítmények
  COM-példányai apartment-kötöttek, és a lekérdezés és a végrehajtás két külön,
  időben távoli művelet — ezért a szálnak élnie kell a menü bezárásáig.
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
menüt, és csak UTÁNA indítja a shell-lekérdezést. Mérve:

- `Show()` **96 ms** alatt visszatér, a menü nyitva, mind a 3 saját elem látszik;
- 2,5 másodperccel később az elemszám 3 → 24, vagyis a shell elemek utólag
  csúsztak be.

Amíg a bővítmények töltődnek, egy szeparátor alatt alacsony kontrasztú
„Bővítmények betöltése…" sor foglalja a helyet, hogy a menü ne ugráljon,
amikor az elemek megérkeznek.

*A mérés korlátja:* a harness futásonként egy mintát adott, mert egy WPF
`ContextMenu` programozott bezárása és azonnali újranyitása ugyanabban a
futásban megbízhatatlannak bizonyult. A 96 ms egyetlen, de egyértelmű minta —
és a 3 saját elem jelenléte önmagában bizonyítja, hogy nincs várakozás.

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
