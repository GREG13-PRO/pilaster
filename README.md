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

**v0.1 — korai fejlesztés.** Ami már működik:

- Fluent felület Mica háttérrel, lekerekített sarkokkal, világos/sötét/rendszer témával
- Oldalsáv gyorseléréssel és meghajtókkal (kihasználtság-sáv, szabad hely)
- Fülek, útvonalsáv (breadcrumb), vissza/előre/fel/frissítés
- Részletes lista és ikonrács — mindkettő **teljesen virtualizálva**
- Natív Windows ikonok és bélyegképek, lemezre gyorsítótárazva
- Rejtett elemek kapcsolható megjelenítése
- Magyar és angol felület, **futásidejű nyelvváltással**

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
| v0.2 | **Oszlopos (Miller) nézet**, előnézeti panel, Quick Look |
| v0.3 | Másolómotor + **aktivitás-központ** (szüneteltetés, ütközéskezelés, visszavonás) |
| v0.4 | Azonnali keresés (NTFS MFT-index), parancspaletta |
| v0.5 | Osztott panelek, munkaterek, címkék, polc, tömeges átnevezés |
| v0.6 | Terminál, Git-integráció, archívum mappaként, szabálymotor |
| v0.7 | Alapértelmezett fájlkezelő, telepítő, automatikus frissítés |
| v0.8 | Lemeztérkép, duplikátumkereső, szinkronizálás, távoli providerek |
| v0.9 | Plugin SDK, témák, teljesítmény-hangolás |
| v1.0 | Csiszolás, dokumentáció, 30+ nyelv |

## Fordítás más nyelvre

A feliratok a [`src/Pilaster.App/Resources/`](src/Pilaster.App/Resources/) mappában vannak. Új nyelvhez elég egy `Strings.<kód>.resx` fájl — kódmódosítás nem kell. Részletek: [`docs/TRANSLATING.md`](docs/TRANSLATING.md).

## Licenc

[MIT](LICENSE)
