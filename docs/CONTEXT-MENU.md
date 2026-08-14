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

## Az időkorlát alapértéke

A spec 400 ms-ot javasolt. **Mérés szerint** az első lekérdezés ennél
lényegesen tovább tart — a fejlesztői gépen `C:\Windows\notepad.exe`-re
**2263 ms** —, mert ilyenkor indul a COM apartment, és töltődnek be a
bővítmény-DLL-ek; a további lekérdezések már ezredmásodpercesek.

400 ms mellett tehát a shell elemek az **első** használatkor sosem jelennének
meg, ami pontosan az a hiba, amit ez a funkció orvosolni hivatott. Ezért az
alapérték **2500 ms**. Hosszabb időkorlát semmibe nem kerül, ha a lekérdezés
gyors: a menü akkor is azonnal megnyílik. A Beállításokban szabadon állítható.

## Mért eredmény (fejlesztői gép)

`C:\Windows\notepad.exe`-re, üres feketelistával:

- 25 felső szintű elem, 60 elem összesen (almenükkel együtt)
- **7-Zip**: 13 elemű almenü
- Ikonos elemek: Futtatás rendszergazdaként, Edit with Notepad++,
  AVG adatmegsemmisítő, File Locksmith, PowerRename, Törlés
- „Küldés" almenü, „Korábbi verziók visszaállítása", „Megosztás",
  „Másolás elérési útként" — mind jelen
- Minden nem-elválasztó, almenü nélküli elemnek van érvényes parancsazonosítója
