# Változásnapló

A jelölés [Semantic Versioning](https://semver.org/lang/hu/) szerinti.

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
- A jobbklikk-menü shell-elemeinek **időkorlátja 2500 ms** (a specifikációban
  javasolt 400 ms helyett). Mérés szerint az első lekérdezés 2263 ms — 400 ms
  mellett a shell elemek első használatkor sosem jelennének meg. Részletes
  indoklás: `docs/CONTEXT-MENU.md`.
- A szerkesztő alapértelmezett betűkészlete Consolas (nem Cascadia Mono: az
  utóbbi a Windows Terminallal érkezik, nem magával a Windowsszal).

#### Breaking változás: `Ctrl+R`

A Pilaster Classic kiosztásban a `Ctrl+R` **eddig frissítés volt**; mostantól
a klasszikus kétpaneles konvenciót követi, és a jobb panel útvonalát viszi a
balra. A frissítés `Ctrl+Shift+R`-re került, a `Alt+F5` pedig mindkét panelt
frissíti. A Pilaster Modern kiosztásban az `F5` továbbra is frissítés.

*Migráció:* nincs teendő — a kiosztás nem konfigurációs adat, csak a
megszokást kell átállítani. A teljes lista a Beállítások → Billentyűzet →
„Kiosztás megtekintése" gombjával bármikor előhívható.

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

### Ismert korlátok

- **A jobbklikk-menü shell-lekérdezése nem külön folyamatban fut.** A kivétel,
  a beragadás és a hibás menüfa ellen védve vagyunk, egy natív hozzáférési
  hiba (AV) ellen nem. Tudatos, elhalasztott döntés; a helper-folyamat
  visszafelé kompatibilisen bevezethető. Részletek: `docs/CONTEXT-MENU.md`.
- **A kódaláírás még nincs bekötve.** A telepítő `signtool` hookja hiányzik,
  mert tanúsítvány sincs.
- **Az egyedi billentyű-kiosztás szerkesztője nem készült el.** A `Custom`
  preset és a tároló mező (`CustomKeyBindings`) megvan, de a hozzárendeléseket
  ma még csak kézzel, a `settings.json`-ben lehet megadni; a preset-választó
  és a „Kiosztás megtekintése" táblázat működik.
- **Néhány új beállítás még nem hat mindenre.** A sűrűség, a rendszerfájlok
  és a kiterjesztés-megjelenítés kapcsolója mentődik, de a fájllista
  megjelenítése még nem olvassa őket.
