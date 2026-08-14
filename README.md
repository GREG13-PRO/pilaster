<div align="center">

<img src="assets/brand/lockup.png" alt="Pilaster" width="340">

**Modern fájlkezelő Windows 11-re — oszlopos nézettel, valódi aktivitás-központtal, és sok mindennel, ami az Explorerből hiányzik.**

*A modern file manager for Windows 11 — with Finder-style column view, a real activity center, and a lot of things Explorer never had.*

[![build](https://github.com/GREG13-PRO/pilaster/actions/workflows/build.yml/badge.svg)](https://github.com/GREG13-PRO/pilaster/actions/workflows/build.yml)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

</div>

---

## Miért?

A Windows 11 Explorer lassú, és hiányzik belőle a macOS Finder legjobb ötlete: az **oszlopos nézet**, amiben a mappaszerkezet vízszintesen bomlik ki, és egy pillantással látod, hol vagy a fában. A Pilaster ezt hozza el a Windowsra — és mellé még sok mindent.

## Állapot

**v0.8.0 — korai fejlesztés.** Ami már működik:

- **Kezdőlap fül**: „Ez a gép"-stílusú áttekintés — gyorsmappák (Asztal, Dokumentumok, Letöltések, Képek, Zene, Videók) és a meghajtók csempézve, kihasználtság-sávval, a felület saját liquid glass dizájnjában
- **Szerkeszthető gyorselérés**: saját mappák rögzítése jobbklikkel vagy húzással a panelre, alapértelmezett elemek eltávolítása, sorrend húzással átrendezve — minden mentve, újraindítás után is megmarad
- **Kuka a gyorselérésben**: tartalom megtekintése, elemek visszaállítása vagy végleges törlése, „Kuka ürítése", üres állapot jelzése
- **Azonnali átnevezés** új mappa/fájl létrehozásakor — pontosan az Intézőhöz hasonlóan: az alapnév kijelölve, szerkeszthető állapotban, Enter menti, Esc visszaáll, ütköző névnél automatikus sorszámozás
- **Kiadás ikon** a cserélhető és optikai meghajtók sorának végén az oldalsávban — egy kattintásra azonnali biztonságos leválasztás, rendszermeghajtónál sosem jelenik meg
- **Új alkalmazás ikon és arculat** — friss, éles arculat minden méretben (ablak, tálca, telepítő, exe-erőforrás)
- **Natív Windows 11 jobbklikk-menü** fájlokon ÉS mappák üres területén egyaránt — a menü hívása külön szálon fut, hogy soha ne fagyassza le a felületet
- **Liquid glass felület**: áttetsző oldalsáv, felső sáv, helyi menük (natív DWM Acrylic háttérrel) és Beállítások panel a Mica háttér felett — kapcsolható a Beállításokban, gyengébb gépekre
- **Címkék** (macOS Tags mintára): 7 előre definiált szín, saját nevekkel, Beállításokban létrehozva/átnevezve/törölve; a fájlsoron megjelenő címke-ikonnal rendelhetők egy elemhez, az oldalsáv Címkék szekciója pedig szűr rájuk
- **Kedvencek**: szív ikon hoverre a fájlsoron, oldalsáv Kedvencek szekció, törölt célnál halvány jelzéssel és egykattintásos eltávolítással
- **Oszlopos (Miller) nézet** macOS Finder módra: mappára kattintva jobbra nyílik az újabb oszlop, fájlnál jobb oldalon részletek panel (típus, méret, módosítás dátuma) — a nézetmód (Lista/Rács/Oszlopok) fülenként megjegyzett
- **Breadcrumb**: útvonal másolása egy kattintással, vagy kattintásra szerkeszthető szövegmezővé vált (mint az Intézőben) — Enter navigál, Esc/fókuszvesztés visszavált
- **Mappák mérete** háttérszálon kiszámolva és gyorsítótárazva, amíg számol „…" jelzéssel
- **Kiadás** (biztonságos leválasztás) cserélhető és optikai meghajtóknál, „használatban van"-jelzéssel hiba esetén
- **Optikai meghajtó saját ikonja és neve** a behelyezett lemez kötetcímkéje/autorun.inf ikonja alapján, lemezcserére automatikusan frissülve
- Fluent felület Mica háttérrel, lekerekített sarkokkal, **keretek/háttér nélküli eszköztár-gombokkal** (csak hoverre finom kiemelés, a téma szövegszínét követve)
- **Automatikus frissítés** a GitHub Release-ekből: induláskor csendben ellenőriz, nem tolakodó sávban jelzi, egy kattintásra letölti, ellenőrzőösszeggel hitelesíti és — újraindítás megerősítése után — telepíti
- **Téma**: világos / sötét / rendszerkövető, egykattintásos kapcsolóval, átúsztatva, **mentve**
- Oldalsáv gyorseléréssel, meghajtókkal (kihasználtság-sáv, szabad hely), Kedvencekkel és Címkékkel — a mappalánc **minden szintje** kiemelve, nem csak a pontos találat
- Fülek, vissza/előre/fel/frissítés, csúszó átmenet mappaváltáskor
- Részletes lista és ikonrács — mindkettő **teljesen virtualizálva**, kijelöléssel, jobbklikk-menüvel (elemen ÉS üres területen egyaránt: új mappa/fájl, beillesztés, frissítés, rendezés) és **húzásos (marquee) kijelöléssel**
- Fájlok **beillesztése a vágólapról** — az Intézővel kompatibilis formátumban, másolással és kivágással is
- **Oszlopfejléces rendezés** iránynyíllal, az Explorer természetes sorrendjével
- Natív Windows ikonok és bélyegképek, lemezre gyorsítótárazva
- **Két testreszabható gyorsgomb** — mappa vagy fájl, saját névsablonnal (`{date}`, `{time}`, `{n}`) és célmappával
- **Beállítások panel**: téma, áttetsző hatás, animációk, nyelv, gyorsgombok, címkék, frissítések — minden azonnal mentődik
- **Hibabejelentő**: a felhasználók egy publikus e-mail-címet látnak (`pilaster-explorer@proton.me`); a fejlesztői panel (közvetlen küldés egy Discord botnak, „Kész" gombbal és automatikus archiválással, képernyőkép-/naplócsatolással) rejtve marad, amíg a szekciófejlécre 10-szer nem kattintanak
- Magyar és angol felület, **futásidejű nyelvváltással**, a rendszernyelv automatikus felismerésével

Amit a következő mérföldkövek hoznak, azt lásd az [ütemezésben](#ütemezés).

## Teljesítmény

A fájlkezelőnél a sebesség nem extra, hanem alapkövetelmény. Ezért:

| Terület | Megoldás |
|---|---|
| Mappalistázás | `FileSystemEnumerable` a `DirectoryInfo` helyett — elemenként egy allokációval kevesebb |
| Megjelenítés | A bejárás háttérszálon egy `Channel`-be termel; az első 200 elem azonnal kirajzolódik, az adagméret onnan négyszereződik |
| Lista-értesítések | Saját `RangeObservableCollection`: egy adag = egy értesítés, nem elemenként egy |
| Rácsnézet | Saját virtualizáló sortördelő panel — a WPF-ben nincs ilyen beépítve |
| Ikonok | Csak a képernyőn lévő sorokra indul COM-hívás; a típusikonok kiterjesztés szerint gyorsítótárazódnak |
| Rendezés | A natív `StrCmpLogicalW`, hogy a sorrend pontosan egyezzen az Explorerével (`kép9` a `kép10` előtt) |

## Telepítés

Töltsd le a [legutóbbi kiadásból](https://github.com/GREG13-PRO/pilaster/releases/latest):

- **`Pilaster-<verzió>-x64-setup.exe`** — telepítő. Nem kér rendszergazdai jogot, a felhasználói profilba telepít.
- **`...-portable.zip`** — hordozható változat: kicsomagolod és futtatod, semmit nem ír a rendszerbe.

ARM64-es gépre (Snapdragon X, Surface Pro) az `arm64` változat való.

> **A víruskeresőd bejelez?** Aláíró tanúsítvány nélkül ez sajnos előfordul.
> A [docs/ANTIVIRUS.md](docs/ANTIVIRUS.md) leírja, miért, és hogyan tudod
> ellenőrzőösszeggel vagy saját fordítással meggyőződni róla, hogy a fájl az,
> aminek mondja magát.

## Fordítás

Kell hozzá a [.NET 10 SDK](https://dotnet.microsoft.com/download). Semmi más.

```powershell
git clone https://github.com/GREG13-PRO/pilaster.git
cd pilaster
dotnet build Pilaster.slnx
dotnet run --project src/Pilaster.App
```

Önálló, egyfájlos kiadás készítése:

```powershell
dotnet publish src/Pilaster.App -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:SelfContained=true
```

## Felépítés

```
src/
├─ Pilaster.Core/       Domain: elemek, provider-interfész, rendezés, formázás
├─ Pilaster.Providers/  Helyi fájlrendszer (később: archívum, FTP/SFTP, S3, WebDAV)
├─ Pilaster.Shell/      Win32/COM interop: ikonok, bélyegképek, meghajtó-kiadás, natív jobbklikk-menü
└─ Pilaster.App/        WPF felület, nézetmodellek, lokalizáció

discord-bot/            Node.js — hibabejelentő Discord bot (lásd docs/BUG_REPORTS.md)
```

A `IFileSystemProvider` absztrakció az első naptól megvan: a helyi lemez csak *egy* implementáció. Ezért fog később az archívum, az FTP és az S3 ugyanúgy „mappaként" viselkedni, a felület módosítása nélkül.

## Ütemezés

| Verzió | Tartalom |
|---|---|
| **v0.1** ✅ | Váz, oldalsáv, fülek, részletes + rács nézet, lokalizáció |
| **v0.1.1** ✅ | Oszlopfejléces rendezés, telepítő, mappás kiadás |
| **v0.2** ✅ | Témaváltás perzisztenciával, animációk, Beállítások, két testreszabható gyorsgomb |
| **v0.3** ✅ | Hibabejelentő (Discord webhook), aktív mappa kiemelése, tisztább eszköztár |
| **v0.4** ✅ | Fájlkijelölés/jobbklikk javítás, ős-lánc kiemelés, egységes gombstílus, csúszó átmenet, ötlet/hiba megkülönböztetés |
| **v0.5** ✅ | Automatikus frissítés, keretek nélküli gombok, jobbklikk üres területen, húzásos kijelölés, vágólap-beillesztés |
| **v0.6** ✅ | Oszlopos (Miller) nézet, natív jobbklikk-menü, mappaméret-számítás, meghajtó-kiadás, optikai lemez ikonja, Discord bot |
| **v0.6.1** ✅ | Publikus hibabejelentő e-mail + rejtett fejlesztői panel, bot-biztonsági frissítés (multer 2.x) |
| **v0.7.0** ✅ | Liquid glass felület, natív jobbklikk-fagyás javítása, üres terület natív menüje, címkék, kedvencek, breadcrumb-szerkesztés |
| **v0.8.0** ✅ | Új alkalmazás ikon, azonnali átnevezés létrehozáskor, Kuka a gyorselérésben, Kezdőlap „Ez a gép" nézet, szerkeszthető gyorselérés, meghajtó-kiadás ikon az oldalsávban |
| v0.9 | **Előnézeti panel bővítése + Quick Look** (kép/kód/PDF előnézet, Space-re lebegő nézet) — lásd [docs/ROADMAP-columns.md](docs/ROADMAP-columns.md) |
| v1.0 | Másolómotor + **aktivitás-központ** (szüneteltetés, ütközéskezelés, visszavonás) |
| v1.1 | Azonnali keresés (NTFS MFT-index), parancspaletta |
| v1.2 | Osztott panelek, munkaterek, polc, tömeges átnevezés |
| v1.3 | Terminál, Git-integráció, archívum mappaként, szabálymotor, alapértelmezett fájlkezelő |
| v1.4 | Csiszolás, dokumentáció, 30+ nyelv |

## Fordítás más nyelvre

A feliratok a [`src/Pilaster.App/Resources/`](src/Pilaster.App/Resources/) mappában vannak. Új nyelvhez elég egy `Strings.<kód>.resx` fájl — kódmódosítás nem kell. Részletek: [`docs/TRANSLATING.md`](docs/TRANSLATING.md).

## Licenc

[MIT](LICENSE)
