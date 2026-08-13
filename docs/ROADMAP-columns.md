# v0.3 — oszlopos nézet és a hozzá tartozó felület

> Ez a specifikáció eredetileg a v0.2-nek készült. A v0.2 végül más irányt kapott
> (téma, animációk, Beállítások, gyorsgombok), ezért az itt leírt munka a
> **v0.3-ba** csúszott. A tartalma változatlanul érvényes.

A referencia a [Files](https://files.community) felülete. **Nem kódot veszünk át**, hanem
elrendezési és interakciós mintákat — a megvalósítás saját, a WPF/Fluent
alapokon, amit a v0.1 lerakott.

---

## 1. Oszlopos nézet (Miller columns) — a kiadás fő funkciója

Ez az, amiért a projekt elindult.

- Vízszintesen egymás mellett álló oszlopok; minden oszlop egy mappaszint.
- A navigálható mappák sorának végén **chevron (›)** jelzi, hogy tovább lehet lépni.
- Kijelölés egy oszlopban → jobbra megnyílik a következő oszlop.
- Az aktív útvonal minden oszlopban kiemelve marad, így egy pillantással látszik a teljes lánc.
- **← / →** oszlopváltás, **↑ / ↓** mozgás az oszlopon belül.
- Oszlopszélesség húzható, és mappánként megjegyződik.
- Ha az utolsó kijelölt elem fájl, a záró oszlop helyén **előnézet** jelenik meg.
- Vízszintes görgetés automatikusan követi a kijelölést.

**Technikai megkötés:** minden oszlop önálló virtualizált lista. Egy 4600 elemű
mappa (a referenciaképen is ekkora) nem akaszthatja meg a szomszéd oszlopokat,
ezért oszloponként külön betöltés és külön megszakítási jelző kell.

## 2. Előnézeti panel

- Jobb oldalt, húzható szélességgel, **Részletek / Előnézet** fülekkel.
- *Részletek*: méret, dátumok, típus, attribútumok, kép esetén EXIF.
- *Előnézet*: kép, Markdown (renderelve), kód (szintaxiskiemeléssel), PDF első oldal, szöveg, videó-poszter.
- Nagy fájlnál ne blokkoljon: küszöb felett csak fejlécet olvasunk.
- **Space** = Quick Look: lebegő, teljes méretű előnézet a Mac mintájára.

## 3. Eszköztár

Bal oldalon:
- **Új ▾** — mappa, majd sablonból fájl (`.txt`, `.md`, …)
- Kivágás · Másolás · Beillesztés · Átnevezés · Megosztás · Törlés · Tulajdonságok

Jobb oldalon (legördülők):
- **Szűrés**
- **Címkézés**
- **Rendezés ▾** — szempont + irány
- **Csoportosítás ▾**
- **Elrendezés ▾** — részletek / csempe / rács / **oszlopok** / galéria
- **Előnézeti panel** be/ki

A gombok a kijelöléstől függően engedélyezettek (üres kijelölésnél a másolás inaktív).

## 4. Útvonalsáv

- Bal szélen **Kezdőlap ikon**, utána chevronokkal tagolt szegmensek.
- Kattintás a sávra → szerkeszthető szövegmezővé alakul (útvonal beillesztés).
- Jobb szélen: útvonal másolása, majd **kereső ikon**.

## 5. Oldalsáv

Csoportok, mind összecsukható:
- **Kezdőlap**
- **Rögzített** — pin ikonnal, húzással átrendezhető
- **Meghajtók** — kihasználtság-sávval *(v0.1-ben kész)*
- **Felhő** — OneDrive, Google Drive, Dropbox
- **Hálózat**
- **Címkék** — színes pöttyel
- Alul külön: **Beállítások**

## 6. Címkerendszer

- Színes, elnevezett címkék; egy elem többet is kaphat.
- Külön **Címke** oszlop a részletes nézetben.
- Kereshető: `tag:Fontos` → találati lista útvonal-oszloppal.
- Tárolás: NTFS alternatív adatfolyam **és** SQLite — az ADS a fájllal együtt mozog, a SQLite gyorsan kereshető. A kettő közül a SQLite az elsődleges, az ADS a hordozhatóságért van.

## 7. Kijelölés jelölőnégyzettel

- Az ikonra víve **jelölőnégyzet** jelenik meg (a referencián is így van).
- Egérrel is használható többszörös kijelölés, `Ctrl` nyomva tartása nélkül.

## 8. Testreszabható gyorsbillentyűk („Műveletek")

- Kereshető parancslista, mellette a hozzárendelt billentyű.
- Átírható, új parancs felvehető, **Alapértelmezések visszaállítása** gomb.
- A parancsdefiníciók egy központi regiszterből jönnek — ugyanabból, amit a v0.4 parancspalettája is használ majd.

## 9. Megjelenés

- **Egyedi háttérkép / áttetszőség** — a referencián a háttér átüt az ablakon.
- Az áttetszőség mértéke állítható; Mica és Acrylic között váltható.
- Az oldalsáv és a fájlterület továbbra is lekerekített kártyákban ül.

## 10. Osztott panel

- Két panel egymás mellett, külön nézetmóddal (pl. bal rács, jobb részletes).
- Az aktív panel kerete kiemelve; **Tab** vált köztük.

## 11. Állapotsor

- Bal: elemszám, kijelölés darabszáma és összmérete.
- Jobb: **Git-ág és eltérés** (`main`, `0/0`), ha a mappa repóban van.

---

## Sorrend a megvalósításban

1. Oszlopos nézet *(a kiadás lényege — ez megy először)*
2. Előnézeti panel + Quick Look
3. Eszköztár és a hozzá tartozó fájlműveletek
4. Kijelölés jelölőnégyzettel, szűrés, rendezés, csoportosítás
5. Címkék
6. Osztott panel
7. Gyorsbillentyű-szerkesztő
8. Egyedi háttér és áttetszőség
9. Git-jelzés az állapotsorban
