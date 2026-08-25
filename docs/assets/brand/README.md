# Pilaster — márkajelek

| Fájl | Méret | Mire való |
|---|---|---|
| `icon-1024.png` | 1024×1024 | Mesterpéldány. Minden más ebből készül. |
| `app.ico` | 16–256 | Az alkalmazás ikonja. 10 méret ágyazva: 16, 20, 24, 32, 40, 48, 64, 96, 128, 256. |
| `png/icon-*.png` | 16–256 | Az egyes méretek külön, README-hez és dokumentációhoz. |
| `wordmark.png` | 932×245 | Csak a szókép, átlátszó háttérrel. |
| `lockup.png` | 680×256 | Ikon + szókép egymás mellett. |
| `lockup-512.png` | 1364×512 | Ugyanaz nagyobb felbontásban. |

## Használati szabályok

**Az ikonba soha ne kerüljön szöveg.** A „Pilaster" felirat 16 és 32 képpontos méretben — tehát a tálcán, a fájllistában és a címsorban — olvashatatlan elkenődéssé válik, és koszfoltnak látszik a mappán. Ezért van a szókép külön fájlban.

**A szóképet ott használd, ahol vízszintes hely van:** telepítő fejléce, Névjegy párbeszéd, README, splash képernyő, weboldal. Ilyenkor a `lockup.png` a helyes választás, mert a két elem arányát és térközét már beállítottuk.

**Biztonsági zóna:** az ikon rajza a vászon középső 86%-át tölti ki, körben ~72 képpont levegővel. Ezt ne vágd le — enélkül a Windows tálcán és a Start menüben a rajz széle levágódik.

**Háttér:** minden fájl valódi alfa-csatornás PNG. Az amber szín világos és sötét háttéren egyaránt megáll; ne tegyél mögé kitöltést.

## Színek

| Szerep | Hex |
|---|---|
| Mappa (alap) | `#E9B843` |
| Mappa (mély árnyék) | `#E2A61F` |
| Dokumentum | `#DCE0EC` |
| Szövegsáv a dokumentumon | `#6B7280` |
| Szókép | `#C9844E` |

## Újragenerálás

Ha a mesterpéldány (`icon-1024.png`) változik, az `app.ico` és a `png/` tartalma abból származtatott — újra kell képezni őket, hogy ne csússzanak szét a verziók.
