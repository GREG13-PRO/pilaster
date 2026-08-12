# Fordítás / Translating

*Magyar lentebb — [ugrás](#magyar)*

## English

Pilaster ships with a runtime-switchable localization system. Adding a language needs **no code changes** — one resource file is enough.

### Adding a language

1. Copy [`src/Pilaster.App/Resources/Strings.resx`](../src/Pilaster.App/Resources/Strings.resx) to `Strings.<code>.resx`, where `<code>` is the culture code (`de`, `fr`, `pt-BR`, `zh-Hans`…).
2. Translate every `<value>`. Leave the `name` attributes untouched — those are the lookup keys.
3. Add the code to `SatelliteResourceLanguages` in [`src/Pilaster.App/Pilaster.App.csproj`](../src/Pilaster.App/Pilaster.App.csproj) and to `SupportedLanguages` in [`App.xaml.cs`](../src/Pilaster.App/App.xaml.cs).
4. Build and switch languages from the view menu — no restart needed.

### Rules that matter

- **Placeholders stay put.** `{0}`, `{1}` are substituted at runtime. `"{0} items"` → `"{0} elem"`. Never translate or reorder them unless your language genuinely requires a different order — in which case reorder the *indices*, not the text around them.
- **Keep it short.** Toolbar and column labels sit in tight space. If your translation is much longer than the English, look for a shorter phrasing before letting it truncate.
- **Match the platform.** Use the same wording Windows itself uses in your language for concepts like *folder*, *drive*, *properties*. Users read the OS far more than they read us.
- **Right-to-left** (Arabic, Hebrew) is supported by the layout, but please flag anything that reads wrong.

---

## Magyar

A Pilaster futásidőben váltható lokalizációs rendszert használ. Új nyelv hozzáadásához **nem kell kódot módosítani** — egyetlen erőforrásfájl elég.

### Új nyelv hozzáadása

1. Másold a [`src/Pilaster.App/Resources/Strings.resx`](../src/Pilaster.App/Resources/Strings.resx) fájlt `Strings.<kód>.resx` néven, ahol a `<kód>` a kultúrakód (`de`, `fr`, `pt-BR`, `zh-Hans`…).
2. Fordítsd le az összes `<value>` elemet. A `name` attribútumokhoz **ne nyúlj** — azok a keresési kulcsok.
3. Vedd fel a kódot a [`Pilaster.App.csproj`](../src/Pilaster.App/Pilaster.App.csproj) `SatelliteResourceLanguages` listájába és az [`App.xaml.cs`](../src/Pilaster.App/App.xaml.cs) `SupportedLanguages` tömbjébe.
4. Fordítsd le a projektet, és válts nyelvet a nézetmenüből — újraindítás nem kell.

### Amire figyelj

- **A helyőrzők maradjanak.** A `{0}`, `{1}` futásidőben töltődik ki. `"{0} items"` → `"{0} elem"`. Ne fordítsd és ne told el őket, hacsak a nyelved nyelvtana tényleg más sorrendet nem kíván — olyankor a *sorszámokat* cseréld, ne a körülöttük lévő szöveget.
- **Legyen rövid.** Az eszköztár és az oszlopfejlécek szűk helyen ülnek. Ha a fordításod jóval hosszabb az angolnál, keress tömörebb megfogalmazást, mielőtt levágódna.
- **Kövesd a rendszert.** Használd ugyanazokat a szavakat, amiket a Windows használ az adott nyelven a *mappa*, *meghajtó*, *tulajdonságok* fogalmakra. A felhasználó sokkal többet olvassa az operációs rendszert, mint minket.
- **Jobbról balra író nyelveknél** (arab, héber) az elrendezés felkészült, de jelezd, ha valami rosszul olvasható.

### Jelenlegi nyelvek

| Kód | Nyelv | Állapot |
|---|---|---|
| `hu` | Magyar | teljes |
| `en` | English | teljes (semleges alap) |
