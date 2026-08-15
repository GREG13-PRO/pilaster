# Kétpaneles nézet — mit fed le a teszt, és mit kell kézzel (F7 / P2)

A v1.0 legsúlyosabb bejelentett hibája a kétpaneles nézet volt, ezért itt
tételesen elkülönítjük, mi bizonyított automatán, és mi marad kézi
ellenőrzésre. Ez a lista **nem** a fejlesztő önigazolása — a nyitott pontokat
végig kell kattintani a kiadás előtt.

## Automata lefedettség

`tests/Pilaster.Tests/PaneStateTests.cs` — 11 teszt, a rendes tesztkészletben
fut (`dotnet test`), WPF nélkül:

| Teszt | Mit igazol |
|---|---|
| `KetPanelFuljeiTeljesenKulonallnak` | A két panel fülgyűjteménye külön objektum, nincs átfedés |
| `KulonKijeloles` | A kijelölés fülönként tárolódik, a másik panelé nem változik |
| `KulonElozmeny` | A bal panel `GoBack`-je nem mozgatja a jobb panel előzményét, és nem hoz létre benne „előre" ágat |
| `KulonRendezes` | A két panel egyszerre rendezhető eltérő oszlop és irány szerint |
| `KulonNezetmodEsGorgetes` | Nézetmód és görgetési pozíció fülönként külön él |
| `FulvaltasMegorziAzElhagyottFulAllapotat` | Fülváltás után visszatérve a kijelölés, görgetés és rendezés megvan |
| `AzUtolsoFulNemZarhatoBe` | Nem lehet üres panelt előállítani |
| `FulBezarasaUtanASzomszedLeszAzAktiv` | Nem marad aktív fül nélküli panel |
| `KorbeforgoFulvaltasAPanelenBelulMarad` | `Ctrl+Tab` nem lép át a másik panelbe |
| `MindketPanelnekVanSajatAktivFulJeloltje` | Az aktív fül jelölése a modellen él, nem a fülsáv kijelölésén |
| `AktivFulIndexeAMunkamenetMentesehez` | A munkamenet a helyes fülindexet menti |

`tests/Pilaster.Tests/DualPaneLayoutTests.cs` — 3 teszt. Ez a kettő korábban
kézi pont volt, pedig egyikhez sem kell vizuális fa (spec T4):

| Teszt | Mit igazol |
|---|---|
| `ElrendezesTulEliAzUjrainditast` | A splitter aránya, az elrendezés iránya, a séma verziója és a panelenkénti fülek VALÓDI fájlon át írva-olvasva ugyanazok |
| `AzElvalasztoAlapertekeFelezes` | Üres beállításfájlból is 0,5 az arány és vízszintes az elrendezés |
| `EltuntUtvonalCsakAzEgyikPaneltErinti` | Egy TÉNYLEGESEN törölt mappára navigálva a másik panel útvonala, előzménye, kijelölése és elemszáma bitre ugyanaz marad |

Ezen felül egy futásidejű önteszt (a fejlesztés közben futott, nem része a
kiadott kódnak) végigmérte a panelváltást, a `Ctrl+U` panelcserét, a
`Ctrl+L` útvonal-átadást, az elrendezésváltást, az egypaneles módra váltás és
vissza forgatókönyvét és a munkamenet mentését — mind zölden.

`tests/Pilaster.Tests/PaneDragDropTests.cs` — 8 teszt (spec A2). A panelek
közötti húzás-ejtés DÖNTÉSI MÁTRIXA (`FilePaneView.ResolveDropEffect` /
`IsSameVolume`, most `internal`, `InternalsVisibleTo` a tesztekhez):

| Teszt | Mit igazol |
|---|---|
| `AltMindigParancsikon` / `CtrlMindigMasolas` / `ShiftMindigAthelyezes` | A három módosító felülírja az alapértelmezett hatást |
| `ModositoNelkulAzonosKotetenAthelyezes` / `...ElteroKotetreMasolas` | Módosító nélkül: azonos kötet = áthelyezés, eltérő = másolás |
| `VegyesForrasKotetNelMasolasAzAlapertelmezes` | Vegyes forráskötetnél a biztonságosabb másolás az alapértelmezés |
| `UresForrasnalNincsAzonosKotet` | Üres forráslista nem "azonos kötet" |
| `AltEsCtrlEgyuttAzAltNyer` | A sorrend Alt > Ctrl > Shift > alapértelmezés |

## Talált eltérés a dokumentáció és a kód között

**A „Backspace / Alt+←" gyorsbillentyű NINCS bekötve.** A
`KeymapCatalog.cs`-ben szerepel a felhasználónak mutatott „Kiosztás
megtekintése" táblázatban (`Alt+←` → `Cmd_Back`), de az egyetlen billentyű-
kezelő (`MainWindow.OnMainPreviewKeyDown`) sehol nem kezeli sem a
`Key.Back`-et, sem az `Alt+Left`-et — a `GoBackCommand` kizárólag a
toolbar-gombhoz van kötve. Ez tehát nem egy hiányzó teszt, hanem egy hiányzó
IMPLEMENTÁCIÓ: vagy a `KeymapCatalog` bejegyzés téves (a funkció sosem
készült el), vagy a billentyű-bekötés maradt ki. Ezt vagy javítani kell, vagy
törölni a táblázatból — jelenleg a felhasználónak hazudik.

## Amit a tesztek NEM fednek le — kézi ellenőrzés

- [ ] **Kijelölés vizuális megmaradása.** Jelölj ki 3 fájlt a bal panelben,
      majd kattints a jobb panelbe. A bal oldali kijelölés maradjon LÁTHATÓ
      (halványabb, „inaktív kijelölés" színnel), ne tűnjön el.
      *Miért kézi: a `FilePaneView.List` valódi virtualizált `ListView`,
      konténer-generáláshoz és stílus-kiértékeléshez kell a tényleges
      layout — ez önmagában megoldható lenne STA szálon, de az „inaktív
      kijelölés" SZÍNÉNEK helyessége (nem csak hogy van-e kijelölés) képi
      ellenőrzést kíván.*
- [ ] **Görgetési pozíció visszaállása.** Görgess le egy nagy mappában, válts
      másik fülre, majd vissza — ugyanoda kell visszatérni.
      *A mögöttes kód (`FilePaneView.CaptureViewState`/`RestoreViewState`)
      egyszerű és megismerhető — a visszaállítás egy Background-prioritású
      `Dispatcher.BeginInvoke`-ra vár, ami tesztből `DispatcherFrame`-mel
      pumpálható lenne. Ez a jövőben automatizálható, de valódi virtualizált
      `ListView`-t és `ScrollViewer`-t igényel, amit ebben a körben nem
      építettünk meg.*
- [ ] **Kijelölés visszaállása fülváltás után.** Jelölj ki elemeket, válts
      fület, válts vissza — a kijelölésnek vissza kell állnia. *Ugyanaz a
      kód útja és ugyanaz a korlát, mint fent.*
- [ ] **Backspace / Alt+← csak az aktív panelt lépteti.** **NE ellenőrizd
      kézzel, amíg a fenti eltérés nincs tisztázva** — a gyorsbillentyű ma
      valószínűleg semmit nem csinál.
- [ ] **Splitter húzása egérrel.** Húzd el az elválasztót, és dupla kattints
      rajta (felezzen). *Az arány MENTÉSÉT és visszaolvasását már teszt fedi —
      itt csak az egérinterakció marad kézi.*
- [ ] **Elrendezésváltás.** Váltsd függőleges ↔ vízszintes elrendezésre: a
      két panel útvonala, füljei és a splitter aránya maradjon meg.
- [ ] **Drag & drop a panelek között — csak a HÚZÁS gesztusa.** Azonos
      meghajtón belül alapból áthelyezés, másik meghajtóra másolás; `Shift` =
      áthelyezés, `Ctrl` = másolás, `Alt` = parancsikon. *A döntési mátrixot
      (melyik módosító melyik hatást adja) már teszt fedi
      (`PaneDragDropTests`) — itt csak azt kell látni, hogy a kurzor
      visszajelzése és a tényleges fájlművelet a döntésnek megfelelően
      történik-e, EGY esetben, nem mind a hatnál.*
- [ ] **`F5`/`F6` mindkét irányban.** A célmappa mindig a MÁSIK panel legyen.
      *A cél kiválasztásának logikája (`MainWindowViewModel.InactivePane`)
      egy háromsoros, tiszta számított tulajdonság — de a
      `MainWindowViewModel` megkonstruálása teszthez 9 szolgáltatást
      igényelne, ami ehhez a mértékű logikához aránytalan; ezért maradt
      kézi.*

## Billentyűk preset szerint (K2)

| Preset | `Ctrl+R` | `Ctrl+Shift+R` | `F5` | `Alt+F5` |
|---|---|---|---|---|
| Pilaster Classic | jobb panel útvonala a balra | frissítés | másolás a másik panelbe | mindkét panel frissítése |
| Pilaster Modern | **frissítés** | – | **frissítés** | mindkét panel frissítése |
| Egyedi | a felhasználó kiosztása szerint | | | |
