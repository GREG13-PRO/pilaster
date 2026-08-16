# Változásnapló

A jelölés [Semantic Versioning](https://semver.org/lang/hu/) szerinti.

## v1.0.1

Tisztán UI-javítások a v1.0.0 kiadás utáni valós használatból — **nincs benne
új funkció**, a jobbklikk-menü és a kétpaneles nézet kizárólag a
megjelenítése változott.

### Javítások — jobbklikk menü

- **Üres szürke doboz a menü tetején.** A beépített keresőmező dísztelen,
  natív `TextBox` volt — placeholder és ikon nélkül üres dobozként ütött el.
  Most a WPF-UI `TextBox`-ának `PlaceholderText`/`Icon` tulajdonságaival
  ugyanaz a minta, mint a Beállítások keresőjén.
- **A menü levágódott a képernyő alján.** A `ContextMenu` nem korlátozta
  magát a munkaterülethez. Megnyíláskor mostantól lekérdezi a natív
  `MonitorFromWindow`/`GetMonitorInfo`-val a MEGFELELŐ (nem feltétlenül
  elsődleges) monitor munkaterületét, a monitor saját DPI-jével átváltva
  WPF-egységre, és ehhez korlátozza a `MaxHeight`-ot — ez aktiválja a natív
  `ContextMenu`-sablon beépített görgetését (fel/le nyilak), mint az
  Intézőben. MÉRVE: egy 68 elemes menü (természetes magassága ~1900+ px)
  972 px-re korlátozódott egy 1032 px munkaterületen — a görgetés helyesen
  bekapcsolt.
- **Duplikált „Megnyitás".** A saját „Megnyitás" mellett az „Egyéb
  alkalmazások" szekcióban egy shell-eredetű „Megnyitás" is megjelent. A
  szűrés a shell-elem NYELVFÜGGETLEN verbje alapján történik
  (`IContextMenu.GetCommandString(GCS_VERBW)`); ha egy bővítmény nem ad
  verbet, a másodlagos jelző az `MFS_DEFAULT` állapot. A szűrt elemet
  `Debug` szinten naplózzuk.
- **Indokolatlanul letiltott saját elemek.** A „Megnyitás új fülön",
  „Megnyitás a másik panelen" és „Rögzítés a gyorseléréshez" fájlon (nem
  navigálható elemen) szürkén jelent meg, ahelyett hogy eltűnt volna — egy
  letiltott saját elem azt sugallja, hogy elromlott valami. A
  `PilasterMenuEntry` mostantól `IsVisible`/`IsEnabled` között
  különböztet: ami elvileg sosem értelmezhető az adott elemtípuson (fájlon a
  fenti három, vagy „Megnyitás a másik panelen" kétpaneles nézet nélkül),
  az EGYÁLTALÁN NEM kerül a menübe.
- **Az „Egyéb alkalmazások" felirat beleolvadt.** Üres ikont kapott — ugyanazt
  az ikon-oszlopot foglalja le, mint egy valódi elem —, így pixelre egybeesik
  a többi sor szövegének bal szélével, és egyenletes függőleges térközt kapott.

### Javítások — kétpaneles nézet

- **Nem voltak oszlopfejlécek.** A panelek fájllistája mostantól `GridView`-t
  használ Név/Méret/Típus/Módosítva oszlopokkal — kattintással rendezhető
  (ugyanaz a `TabViewModel.ApplySort`, mint az egypaneles Részletes nézeten),
  húzással átméretezhető, és a Méret/Típus/Módosítva szélessége **panelenként
  külön** mentődik (`AppSettings.LeftPane…`/`RightPane…`). A Név oszlop
  tölti ki a maradék helyet — a `GridViewColumn` nem támogat „*" méretezést,
  ezért ezt a code-behind számolja újra a panel és az oszlopok
  átméretezésekor.
- **Az oszlopelrendezés zsúfolt volt.** Méret jobbra igazítva, Típus és
  Módosítva fix szélességgel, a Név a maradékot tölti ki hosszú névnél `…`-vel.
- **A sorok nehezen követhetők.** A kijelölés/hover mostantól 4 px-rel
  beljebb kezdődik mindkét oldalon, lekerekítve. Új, alapból bekapcsolt
  páros/páratlan csíkozás (`AppSettings.DualPaneRowStriping`).
- **Négy sornyi vezérlő a tartalom előtt.** Az eszköztár (vissza/előre/fel/
  frissítés) és az útvonalsáv egy sorba vonva — egy teljes sornyi hellyel
  több jut a fájloknak.
- **Az aktív panel jelzése túl hangsúlyos volt.** A mind a négy oldalán
  1,5 px-es akcentusszínű keret helyett a keret mindig semleges, csak a
  FELSŐ éle vastagodik és színeződik akcentusra, amíg a panel aktív.
- **Rendereléshiba a bal panel jobb felső sarkában.** A panelek közötti
  szinkronizálás/csere gomb korábban `VerticalAlignment="Top"` mellett
  pontosan a panelek saját fülsávjával azonos magasságban lebegett — keskeny
  ablaknál vagy DPI-nél ez egymásra csúszott az „+" új fül gombbal.
  Függőlegesen középre helyezve a választóra float-ol, ami szélességtől és
  DPI-től függetlenül sosem esik egybe egyik panel fejlécével sem.
- **Nem volt állapotsor.** Egy közös sor a két panel alatt, az AKTÍV panel
  elemszámával, kijelölésével és a kötet szabad helyével.

### Javítások — gyorselérés

- **Túl vékony sorok.** A `SidebarItemStyle`-nak korábban nem volt explicit
  magassága — a `TokenRowPadding` mindhárom sűrűségnél 0 függőleges paddingot
  ad, tehát a sormagasság pusztán a tartalomra zsugorodott, a
  sűrűség-beállítástól függetlenül. Most a `TokenRowHeight`-re kötve
  „kényelmes" sűrűségben 32 px (a kért 32–36 px tartomány alján), „tömör"
  sűrűségben a korábbi szűkebb 24 px marad. A kijelölés 4 px-rel beljebb
  kezdődik, 4 px lekerekítéssel. A szekciócím bal margója igazodik a
  listaelemekhez, egyenletesebb térközzel.

### Javítások — egyéb

- **A hibabejelentőhöz csatolt képernyőmentés üresen jelent meg sötét
  témában.** Gyökérok: a `RenderTargetBitmap`-alapú rögzítés a WPF vizuális
  fát DWM-kompozitálás NÉLKÜL rendereli — ez a legtöbb felületen
  pixelpontos, de a félig áttetsző, Mica-rétegre épülő „liquid glass"
  oldalsávon (`GlassPanelBrush`) sötét témában derengő, majdnem üres
  eredményt adott, mert a valódi Mica-alapszín hiányában az alfa-keverés a
  háttér nélküli feloldásra esik vissza. A képrögzítés mostantól elsődlegesen
  a natív `PrintWindow`-t hívja `PW_RENDERFULLCONTENT` jelzővel — ez a
  TÉNYLEGESEN összeállított, DWM-kompozitált felületet másolja, Mica-val
  együtt, és akkor is működik, ha az ablakot közben más takarja ki. Hiba
  esetén visszaesik az eredeti `RenderTargetBitmap`-útra.
- **A kétpaneles nézet Kezdőlap füle üres maradt.** A panelek fájllistája
  (`FilePaneView`) nem ismeri a Kezdőlap-irányítópultot (kártyák, meghajtók)
  — ez a v1.1 feladata marad. Ehelyett az ÚJ fülek kétpaneles nézetben
  mostantól valódi mappát nyitnak (a beállított kezdőmappát, vagy ennek
  híján a felhasználói profilt) a virtuális Kezdőlap helyett. Ha egy fül
  mégis Kezdőlap-állapotba kerülne (pl. régebbi mentett munkamenetből), ott
  legalább a gyorselérés-lista jelenik meg, nem üres felület.
- A „Ismert korlátok" szakasz duplikált pontjai (folyamat-izoláció,
  kódaláírás, egyedi kiosztás-szerkesztő) összevonva.

## v1.0.0

Az első kiadás, ami nem egy mérföldkő, hanem egy **kerek termék**: a
kétpaneles nézet a nulláról újraírva, a jobbklikk-menü saját designnal de
valódi shell-integrációval, beépített szövegszerkesztő, átszervezett
beállítások, és teljes téma-audit.

### Új funkciók

**Pilaster Editor — beépített szövegszerkesztő**
- `F4` (Pilaster Classic kiosztás) vagy `Ctrl+E` (mindkét kiosztásban) a
  kijelölt fájlon; jobbklikk-menüből is.
- Több fül, fülönként külön fájl. A módosított fület pötty jelöli, bezáráskor
  és kilépéskor rákérdez a mentésre.
- Szintaxiskiemelés: `txt, md, json, xml, yaml, yml, ini, cfg, conf,
  properties, log, js, ts, py, java, cs, c, cpp, html, css, sh, bat, ps1,
  sql, sk`. A **`.sk` és `.yml`** saját, e kiadásban írt definíciót kapott.
- Sorszámozás, sortörés, aktuális sor kiemelése, téglalap-kijelölés.
- `Ctrl+F`/`Ctrl+H` keresés és csere regex + kis/nagybetű + egész szó
  opciókkal; `Ctrl+G` ugrás sorra; `Ctrl+D` sor duplikálás;
  `Ctrl+Shift+K` sor törlés; `Alt+↑/↓` sor mozgatás.
- Kódolás: automatikus felismerés (BOM + heurisztika), kézi váltás UTF-8 /
  UTF-8 BOM / CP1250 / CP852 / UTF-16LE / UTF-16BE között. Az „Újranyitás
  ezzel a kódolással" és a „Mentés ezzel a kódolással" külön parancs.
- Sorvég felismerés (CRLF/LF/CR/vegyes) és konvertálás.
- Státuszsor: sor:oszlop, kijelölt karakterek, kódolás, sorvég, nyelv, INS/OVR.
- **Atomi mentés**: ideiglenes fájlba írás, majd csere — áramszünetnél sem
  csonkul a fájl.
- Írásvédett fájl bannerrel nyílik; 50 MB fölött csak olvasható; bináris
  tartalom meg sem nyílik, hanem az `F3` hexdump-előnézetre esik vissza.
- A lemezen történt külső változást jelzi, és felajánlja az újratöltést.

**Saját jobbklikk-menü, shell-integrációval**
- A menü teljes egészében a Pilaster designja, **de** megjeleníti a telepített
  shell-bővítmények (7-Zip, Notepad++, TortoiseGit, PowerToys, …) elemeit
  almenükkel és ikonokkal, és azok ugyanazt csinálják, mint az Intézőben.
- Nincs „További lehetőségek megjelenítése" kétszintűség — minden egy szinten.
- Aszinkron betöltés időkorláttal: a saját elemek azonnal megjelennek.
- Bővítmény-feketelista név vagy CLSID alapján.
- Opcionális kereső a nyitott menüben, billentyűzetes navigációval.

**Gyorselérés: szerkeszthető és perzisztens**
- Saját, verziózott `quickaccess.json`; minden módosítás azonnal mentődik.
- Szerkesztő ablak: drag & drop sorrendezés, hozzáadás, átnevezés, ikon és
  szín, csoport, elválasztó, eltávolítás, alapértelmezettek, import/export.
- Jobbklikk a fejlécen és a sorokon; **Rögzített** és **Legutóbbi** szekció.
- Nem létező útvonal nem tűnik el magától: szürkítve, figyelmeztető ikonnal,
  jobbklikkből javíthatóan.
- Hálózati útvonalak aszinkron, időkorlátos elérhetőség-ellenőrzéssel.

**Beállítások átszervezése**
- Bal oldali kategórialista (11 kategória) + kereső, ami a névre, a leírásra
  **és rejtett kulcsszavakra** is illeszkedik, magyarul és angolul.
- Mélyhivatkozás: minden beállításnak van azonosítója, más helyről közvetlenül
  odaugorhatunk.
- Kategóriánkénti „Alapértelmezettek visszaállítása" + teljes export/import.
- Kb. 30 új beállítás, mindegyik alatt rövid segédszöveg.

**Modern telepítő**
- Inno Setup 6, `WizardStyle=modern`, per-user alapértelmezéssel (nincs UAC).
- Telepítés típusa: Normál / Egyedi / **Hordozható**.
- Opciók: asztali parancsikon, Start menü, Explorer jobbklikk (fájl, mappa és
  mappa-háttér verb), fájltársítások, indítás Windowsszal, alapértelmezett
  fájlkezelő.
- Csendes telepítés: `/VERYSILENT /NORESTART /DIR="…" /PORTABLE=1 /TASKS="…"`.
- Az eltávolító **rákérdez** a beállítások törlésére, és alapból megtartja őket.

**Egyéb**
- Panelenkénti fülek (`Ctrl+T`, `Ctrl+W`, `Ctrl+Tab`).
- `Ctrl+U` panelcsere, `Ctrl+L`/`Ctrl+R` útvonal átadása, `Alt+F5` mindkét
  panel frissítése.
- `Alt`+húzás: parancsikon készítése.
- Munkamenet mentése és visszaállítása mindkét panel összes fülével.
- Hordozható mód: a beállítások a program mappájába kerülnek.

### Változások

- **A kétpaneles nézet a nulláról újraíródott.** A v0.9-ig a két panel egy-egy
  magányos fül volt, a fülrendszer pedig ezektől függetlenül, globális
  állapotként élt — emiatt nem lehetett panelenként füle, és az állapotuk
  összemosódott. Most **minden fájllista-állapot panelenként él** (fülek,
  aktív fül, és fülönként útvonal, előzmény, kijelölés, fókuszált elem,
  rendezés, nézetmód, görgetés, szűrő); globálisan csak az aktív panel és az
  elrendezés marad.
- **A billentyűkiosztás új nevet kapott.** A „Total Commander billentyűkiosztás"
  helyett **Pilaster Classic (kétpaneles)** és **Pilaster Modern
  (Explorer-szerű)**. A viselkedés változatlan, csak a felirat más. A
  felhasználónak látható felületen sehol nem szerepel idegen terméknév.
- **A shell-menü előmelegítése induláskor**, alacsony prioritású háttérszálon.
  MÉRVE: az első jobbklikk enélkül 2186 ms, vele 1132 ms — a különbség a COM
  apartment indulása és a bővítmény-DLL-ek betöltése, ami egyszeri költség.
- A jobbklikk-menü shell-elemeinek **időkorlátja 2000 ms** (a specifikációban
  javasolt 400 ms helyett). Az érték mérésből származik: az előmelegítés utáni
  legrosszabb első lekérdezés 1132 ms, ×1,75 biztonsági szorzóval. Ez nem
  lassít semmit: a menü megnyitása MÉRVE 96 ms, a shell elemek utólag
  csúsznak be. Részletes számok: `docs/CONTEXT-MENU.md`.
- **A telepítő mindig per-user ágon fut.** MÉRVE: a korábbi
  `PrivilegesRequiredOverridesAllowed=dialog` mellett a csendes telepítés
  per-machine ágra ment (HKLM-be írt, a parancsikonokat a Public Desktopra
  tette); `commandline dialog` mellett pedig a mód-választó ablak `/VERYSILENT`
  mellett is felugrott. Most csak `commandline` marad — per-machine telepítés
  az `/ALLUSERS` kapcsolóval kérhető.
- A szerkesztő alapértelmezett betűkészlete Consolas (nem Cascadia Mono: az
  utóbbi a Windows Terminallal érkezik, nem magával a Windowsszal).

#### Breaking változás: `Ctrl+R` — **csak a Pilaster Classic kiosztásban**

| Preset | `Ctrl+R` | `Ctrl+Shift+R` | `F5` | `Alt+F5` |
|---|---|---|---|---|
| **Pilaster Classic** | jobb panel útvonala a balra *(változás)* | frissítés | másolás a másik panelbe | mindkét panel frissítése |
| **Pilaster Modern** | frissítés *(változatlan)* | – | frissítés *(változatlan)* | mindkét panel frissítése |
| Egyedi | a felhasználó kiosztása szerint | | | |

A Pilaster Modern kiosztás az Explorer/böngésző konvencióját követi: a `Ctrl+R`
és az `F5` is frissít, pontosan úgy, mint eddig. **Aki a Modern kiosztást
használja, nem tapasztal semmilyen változást.**

A Classic kiosztásban a `Ctrl+R` eddig frissítés volt; mostantól a klasszikus
kétpaneles konvenciót követi. A frissítés ott `Ctrl+Shift+R`-re került.

*Migráció:* nincs teendő — a kiosztás nem konfigurációs adat. A teljes lista a
Beállítások → Billentyűzet → „Kiosztás megtekintése" gombjával bármikor
előhívható.

#### Migrációk (automatikusak, adatvesztés nélkül)

- A v0.9-es `totalcommander` / `tc` / `total_commander` konfigurációs értékek
  és a régi logikai kapcsoló **Pilaster Classic**-ra képződnek.
- A `settings.json`-ben tárolt gyorselérés átkerül a `quickaccess.json`-be —
  egyszer, az első v1.0-s indításkor, és csak ha az új fájl még üres.
- A címkék `metadata.json`-je változatlanul betöltődik; a paletta 7-ről 12
  színre bővült, a régi színnevek érintetlenek.

### Javítások

- **Világos módban sötéten maradt felületek.** Gyökérok: a `GlassPanelBrush`
  egyszer másolódott a WPF-UI szótárából, és témaváltáskor a régi (sötét)
  ecsetobjektum maradt benne — ettől ragadt sötétben az oldalsáv, a felső sáv
  és a Beállítások panel. A teljes felület átállt egy 23 elemű
  téma-tokenkészletre (`ThemeTokenService`), minden beégetett hex eltűnt, és
  minden szövegtoken teljesíti a WCAG AA 4,5:1 kontrasztot. Ellenőrző lista:
  `docs/THEME-CHECKLIST.md`.
- **A címke színe nem látszott a Beállításokban.** A pötty helyére a
  specifikált 14×14-es, lekerekített, **mindig szegélyezett** színminta került
  (enélkül egy világos címke beleolvadna a világos háttérbe). A minta a
  fájllistában, a szűrőben és a panelekben is megjelenik, és színválasztó
  popup tartozik hozzá: 12 előre definiált szín + egyedi hex, élő előnézettel.
- **Rossz ikon a tálcán.** Az alkalmazás mostantól a folyamat elején beállítja
  az `AppUserModelID`-t (`Obsidix.Pilaster`), minden ablak explicit,
  multi-resolution `.ico` ikont kap, és a telepítő ugyanezt az azonosítót
  írja a Start menü és az asztali parancsikon tulajdonságába.
- Egy panel útvonalának megszűnésekor (kihúzott pendrive) a panel a
  legközelebbi elérhető szülőre lép hibaüzenettel, nem ürül ki némán.
- A felső fülsáv `ListBox`-a panelváltáskor `null`-t írt vissza az aktív fül
  helyére; az aktív panelnek így egy pillanatra nem volt aktív füle.
- Az AvalonEdit `TextDocument` szálhoz kötött; a szerkesztő minden
  megnyitáskor `NullReferenceException`-t dobott a mérési fázisban.
- **A nagy fájl megnyitása befagyasztotta a felületet.** MÉRVE: egy 122,7 MB-os
  naplófájlnál a betöltés alatt egy 50 ms-os órajel a várt 97 ütésből csak
  17-et kapott meg — 4,6 másodperc néma fagyás. A beolvasás, a dekódolás és a
  dokumentum felépítése átkerült háttérszálra (az AvalonEdit szabályos
  `SetOwnerThread` tulajdonjog-átadásával, mert a dokumentum szálhoz kötött), a
  megnyitás megszakítható lett, és arányt mutató folyamatjelzőt kapott.
  Újramérve: az ütések aránya az ÜRESJÁRATI alapvonalra állt vissza (78% helyett
  74–77%), és egyetlen, 196–1343 ms-os szünet maradt — a dokumentum átadása az
  AvalonEdit nézetének, ami kötelezően a UI-szálon fut. „Mégse" után nem marad
  félig betöltött fül.
- **A jobbklikk-menü összeomlasztotta a programot a második megnyitásnál.**
  MÉRVE (Release, éles menü-út): a folyamat `0xC0000374`
  (heap-korrupció) hibával elszállt, mind a négy forgatókönyvben az 1–2.
  körben. A hibát a `Vanara.Windows.Shell` `ShellContextMenu.CreateFromItems`
  hívása okozta. A bizonyítás a MŰKÖDŐ oldalról indult, egyszerre egy változót
  mozgatva (`tools/ShellCrashRepro/`): a minimál, nyers P/Invoke harness
  4×10/10 tisztán fut; pumpálás nélkül is 3×10/10; csak a `ShellItem`
  életciklusával is 3×10/10; a `CreateFromItems`-szel viszont 3-ból 3-szor
  elszáll — a Vanara 5.0.6-tal is. Ezért a fájlmenü mostantól közvetlenül a
  shell API-ját hívja (`SHParseDisplayName` → `SHBindToParent` →
  `GetUIObjectOf`); a menüolvasó, az ikonkonverter és a mappa-háttér ág
  változatlan. Utána: mind a négy forgatókönyv 10/10, és 200 menünyitásból
  nulla összeomlás. Részletes bisect-táblázat: `docs/CONTEXT-MENU.md`.
- **A shell-szál lezárása megölte a folyamatot.** A `StaWorker.Dispose()`
  eldobta a munkasort, miközben a szivattyú szál még benne állt a
  `GetConsumingEnumerable()` ciklusban; a keletkező `ObjectDisposedException`
  a `foreach`-en kívül csapódott ki, tehát kezeletlenül vitte a folyamatot
  (`0xE0434352`). Jellemzően akkor, amikor egy időtúllépés miatt eldobtuk a
  közös szálat. MÉRVE: 200 menünyitásból 3 futás halt így meg; a javítás után
  nulla. A sort mostantól az a szál szabadítja fel, amelyik olvassa.
- **Kettős felszabadítás a jobbklikk-menüben.** A shell-menü `ShellItem`-jeit
  előbb kétszer engedtük el, majd egyáltalán nem — utóbbitól a GC véglegesítő
  szálára (MTA) kerültek, ami apartment-kötött COM-objektumnál szintén
  memóriasérülés. A helyes felszabadítási sorrend a kód kommentjében,
  táblázattal rögzítve.
- **A csendes eltávolítás törölte a felhasználó beállításait.** MÉRVE: a
  `/VERYSILENT` eltávolítás a `%APPDATA%\Pilaster` mappát elvitte, pedig az
  alapértelmezés a megtartás. A csendes ág többé nem a megerősítő párbeszéd
  alapértelmezésétől függ: csak a kifejezett `/DELETESETTINGS=1` kapcsolóra
  töröl. Interaktív eltávolításnál marad a kérdés, „Nem" alapértelmezéssel.
- **A három „halott" beállítás mostantól hat**: a kiterjesztés-megjelenítés, a
  rendszerfájlok kapcsolója (a rejtett elemektől függetlenül) és a sűrűség
  (sormagasság és margó a fájllistában, a gyorselérésben és a Beállításokban).
  Mindhárom azonnal érvényesül, újraindítás nélkül, mindkét panelen és minden
  nyitott fülön. Az átnevezés továbbra is a TELJES nevet szerkeszti, tehát a
  kiterjesztés akkor sem veszik el, ha nincs megjelenítve.

### Ismert korlátok

Ezek **nem hibák**, hanem tudatosan a v1.1-re halasztott munkák.

- **Folyamat-izoláció a shell-menühöz.** A kivétel, a beragadás és a hibás
  menüfa ellen védve vagyunk, és a v1.0-t blokkoló heap-korrupció is elhárult
  (lásd a Javításokat). Egy natív hozzáférési hiba egy bővítmény kódjában
  viszont továbbra is viheti a folyamatot — ez ellen csak külön FOLYAMAT
  védene. Enyhítésként a v1.0 összeomlás-jelzőt ír a lekérdezés köré, és az
  előmelegítés köré is: ha a következő indulás beragadt jelzőt talál, a
  bővítmények KIMARADNAK, a menü tetején egy sorban jelezzük ezt a bűnös
  útvonalával, és a Beállítások → Jobbklikk menü szakaszban van „Bővítmények
  újra bekapcsolása" gomb. A `ShellMenuSession` felülete IPC-kompatibilis
  marad, tehát a helper-folyamat visszafelé kompatibilisen bevezethető — ez a
  **v1.1** feladata.
- **Lassú shell-bővítmények.** MÉRVE: a fájlmenü ~780 ms-os állandósult
  idejéből 650–790 ms **egyetlen** kezelőé (NVIDIA `NvAppShExt`,
  `nv3dappshext.dll`), és ez az egyetlen, ami nem melegszik be. `Debug`
  naplószinten a program felsorolja az 5 leglassabb kezelőt és megnevezi a
  400 ms fölöttieket, de **magától nem tilt le semmit** — a döntés a
  Beállítások → Jobbklikk menü → Kikapcsolt bővítmények mezőé.
- **Kódaláírás.** A `signtool` hook helye megvan a build scriptben,
  tanúsítvány viszont nincs.
- **Egyedi kiosztás-szerkesztő UI.** A `Custom` preset és a tároló mező
  (`CustomKeyBindings`) megvan; a hozzárendelések ma még csak kézzel, a
  `settings.json`-ben adhatók meg. A preset-választó és a „Kiosztás
  megtekintése" táblázat működik.
- **Nagy fájl memóriaigénye a szerkesztőben.** MÉRVE: egy 122,7 MB-os
  naplófájl megnyitása 4,6 mp, és 701 MB felügyelt memóriát köt le (871 MB
  working set). A fájl helyesen csak olvasható módban nyílik, a görgetés és a
  keresés gyors (807 ms, illetve 161 ms), de a memóriaigény a fájlméret
  ~5,7-szerese — ezt a v1.1 memóriaképezett betöltéssel csökkentheti. A
  betöltés alatti fagyást a v1.0 megszüntette (lásd a Javításokat), de a
  dokumentum átadása a nézetnek így is 196–1343 ms egyszeri szünet marad.
- **A tálcaikon tiszta profilon nincs ellenőrizve.** Az `AppUserModelID`, a
  multi-resolution `.ico` és a parancsikon-tulajdonság a fejlesztői gépen
  helyes, de az ikongyorsítótár nélküli, FRISS Windows-felhasználói profilon
  végzett ellenőrzés kimaradt — ahhoz új profilt kell létrehozni. Ezt a
  kiadás előtti kézi körben kell megnézni.
- **Néhány új beállítás még nem hat mindenre.** A sűrűség, a rendszerfájlok
  és a kiterjesztés-megjelenítés kapcsolója mentődik, de a fájllista
  megjelenítése még nem olvassa őket.
