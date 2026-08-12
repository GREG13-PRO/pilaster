# „A víruskeresőm bejelez a Pilasterre"

Rövid válasz: **téves riasztás, de jogos a gyanakvásod** — és van, amit tehetsz, hogy ne kelljen a szavunkat adnod rá.

## Miért jelez be

Négy dolog jön össze, és mind a négy önmagában is gyanús egy heurisztikának:

1. **Nincs kódaláírás.** A tanúsítvány évi 200–600 dollár. Enélkül a Windows SmartScreen és a legtöbb víruskereső „ismeretlen kiadó"-ként kezeli a programot.
2. **Nulla reputáció.** Egy vadonatúj binárist, amit még senki nem futtatott, a felhőalapú reputációs rendszerek alapból gyanúsnak vesznek. Ez a letöltésszámmal magától javul.
3. **.NET, önálló csomagolással.** A .NET-es programok gyakori célpontjai az általánosító heurisztikáknak, mert sok zsarolóprogram is .NET-ben készül.
4. **Régebben egyfájlos csomagot adtunk ki.** Az önkicsomagoló exe indításkor natív könyvtárakat ír ki lemezre — ez viselkedésében megegyezik azzal, ahogy a kártevő-letöltők működnek.

## Amit már megléptünk

A **v0.1.1-től nincs többé egyfájlos csomag.** A telepítő rendes mappába pakolja ki a fájlokat, futásidőben semmit nem csomagol ki. Ez a 4. pontot megszünteti, és mellékesen a hidegindulást is gyorsítja.

Amit **nem** tudunk megtenni: aláírni a binárist. Amíg nincs kódaláíró tanúsítvány, a téves riasztások időnként vissza fognak térni. Ezt nem szépítjük.

## Hogyan győződj meg róla magad

Ne bízz bennünk — ellenőrizd:

**1. Ellenőrzőösszeg.** Minden kiadáshoz tartozik `.sha256` fájl. Letöltés után:

```powershell
Get-FileHash .\Pilaster-0.1.1-x64-setup.exe -Algorithm SHA256
```

Ha az érték egyezik a release-ben lévővel, a fájl bitre pontosan az, amit a GitHub buildelt.

**2. Nézd meg, hol készült.** A binárisokat nem egy fejlesztői gépről töltjük fel: a
[release workflow](../.github/workflows/release.yml) építi őket GitHub Actions futtatón, nyilvános naplóval. A [futás naplója](https://github.com/GREG13-PRO/pilaster/actions) bárki számára megnyitható, és látszik benne minden parancs.

**3. Fordítsd le magad.** A forrás teljes egészében itt van, és semmilyen bináris függősége nincs a NuGet-en kívül:

```powershell
git clone https://github.com/GREG13-PRO/pilaster.git
cd pilaster
dotnet publish src/Pilaster.App -c Release -r win-x64 --self-contained
```

**4. VirusTotal.** Töltsd fel a fájlt a [virustotal.com](https://www.virustotal.com) oldalra. Jellemzően 1–3 motor jelez a 70-ből, mindegyik általánosító névvel (`Win32:Malware-gen`, `ML.Attribute.HighConfidence` és hasonlók) — ezek gépi tanuláson alapuló találgatások, nem konkrét kártevő-azonosítások.

## Ha az AVG karanténba tette

1. **AVG → Menü → Karantén** — ott találod a fájlt, és onnan visszaállítható.
2. **Kivétel felvétele:** AVG → Menü → Beállítások → Általános → Kivételek → Kivétel hozzáadása, és add meg a telepítési mappát (`%LOCALAPPDATA%\Programs\Pilaster`).
3. **Jelentsd téves riasztásként** — ez segít mindenki másnak is:
   [avg.com/false-positive-file-form](https://www.avg.com/en-ww/false-positive-file-form)

Az AVG és az Avast ugyanazt a motort használja, tehát elég az egyikhez bejelenteni.

## Mikor NE hidd el, hogy téves riasztás

Akkor gyanakodj, ha:

- **Nem a hivatalos release oldalról** töltötted le (`github.com/GREG13-PRO/pilaster/releases`).
- **Az ellenőrzőösszeg nem egyezik.**
- Több tucat motor jelez, nem egy-kettő.
- A riasztás konkrét kártevőcsaládot nevez meg, nem általános `-gen` vagy `ML.` nevet.

Ilyenkor a fájl tényleg módosított lehet — töröld, és nyiss egy [hibajegyet](https://github.com/GREG13-PRO/pilaster/issues).
