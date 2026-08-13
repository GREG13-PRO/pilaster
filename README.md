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

**v0.2 — korai fejlesztés.** Ami már működik:

- Fluent felület Mica háttérrel, lekerekített sarkokkal
- **Téma**: világos / sötét / rendszerkövető, egykattintásos kapcsolóval, átúsztatva, **mentve**
- Oldalsáv gyorseléréssel és meghajtókkal (kihasználtság-sáv, szabad hely)
- Fülek, útvonalsáv (breadcrumb), vissza/előre/fel/frissítés
- Részletes lista és ikonrács — mindkettő **teljesen virtualizálva**
- **Oszlopfejléces rendezés** iránynyíllal, az Explorer természetes sorrendjével
- Natív Windows ikonok és bélyegképek, lemezre gyorsítótárazva
- **Két testreszabható gyorsgomb** — mappa vagy fájl, saját névsablonnal (`{date}`, `{time}`, `{n}`) és célmappával
- **Beállítások panel**: téma, animációk, nyelv, gyorsgombok — minden azonnal mentődik
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
├─ Pilaster.Shell/      Win32/COM interop: ikonok, bélyegképek
└─ Pilaster.App/        WPF felület, nézetmodellek, lokalizáció
```

A `IFileSystemProvider` absztrakció az első naptól megvan: a helyi lemez csak *egy* implementáció. Ezért fog később az archívum, az FTP és az S3 ugyanúgy „mappaként" viselkedni, a felület módosítása nélkül.

## Ütemezés

| Verzió | Tartalom |
|---|---|
| **v0.1** ✅ | Váz, oldalsáv, fülek, részletes + rács nézet, lokalizáció |
| **v0.1.1** ✅ | Oszlopfejléces rendezés, telepítő, mappás kiadás |
| **v0.2** ✅ | Témaváltás perzisztenciával, animációk, Beállítások, két testreszabható gyorsgomb |
| v0.3 | **Oszlopos (Miller) nézet**, előnézeti panel, Quick Look |
| v0.4 | Másolómotor + **aktivitás-központ** (szüneteltetés, ütközéskezelés, visszavonás) |
| v0.5 | Azonnali keresés (NTFS MFT-index), parancspaletta |
| v0.6 | Osztott panelek, munkaterek, címkék, polc, tömeges átnevezés |
| v0.7 | Terminál, Git-integráció, archívum mappaként, szabálymotor |
| v0.8 | Alapértelmezett fájlkezelő, automatikus frissítés |
| v0.9 | Lemeztérkép, duplikátumkereső, plugin SDK, teljesítmény-hangolás |
| v1.0 | Csiszolás, dokumentáció, 30+ nyelv |

## Fordítás más nyelvre

A feliratok a [`src/Pilaster.App/Resources/`](src/Pilaster.App/Resources/) mappában vannak. Új nyelvhez elég egy `Strings.<kód>.resx` fájl — kódmódosítás nem kell. Részletek: [`docs/TRANSLATING.md`](docs/TRANSLATING.md).

## Licenc

[MIT](LICENSE)
