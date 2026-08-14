# Téma-ellenőrző lista (B1)

Kézi végigkattintós lista minden kiadás előtt. **Mindkét témában** végig kell
menni rajta: Beállítások → Megjelenés → Téma (Világos / Sötét / Rendszer).

## A tokenkészlet

Minden saját szín a `ThemeTokenService`-ből származik (`src/Pilaster.App/Services/ThemeTokenService.cs`).
XAML-ben `{DynamicResource TokenXxx}` alakban hivatkozunk rájuk; beégetett
hex/rgb/névvel megadott szín a komponensekben **tilos**.

| Token | Világos | Sötét | Kontraszt* |
|---|---|---|---|
| `TokenBgApp` | `#F3F3F3` | `#202020` | — |
| `TokenBgSurface` | `#FFFFFF` | `#2B2B2B` | — |
| `TokenBgElevated` | `#FFFFFF` | `#323232` | — |
| `TokenBgInput` | `#FBFBFB` | `#2D2D2D` | — |
| `TokenTextPrimary` | `#1A1A1A` | `#FFFFFF` | 17,4:1 / 14,1:1 |
| `TokenTextSecondary` | `#5D5D5D` | `#C8C8C8` | 7,0:1 / 9,4:1 |
| `TokenTextMuted` | `#6B6B6B` | `#A0A0A0` | 5,7:1 / 5,6:1 |
| `TokenTextInverse` | `#FFFFFF` | `#1A1A1A` | — |
| `TokenBorder` | `#E5E5E5` | `#3D3D3D` | — |
| `TokenBorderStrong` | `#9E9E9E` | `#767676` | — |
| `TokenAccent` / `TokenAccentHover` / `TokenAccentText` | rendszer- vagy egyedi akcentusból | ugyanaz | — |
| `TokenHover` | `#0A000000` | `#14FFFFFF` | — |
| `TokenSelected` | akcentus α=46 | akcentus α=72 | — |
| `TokenSelectedInactive` | `#14000000` | `#1AFFFFFF` | — |
| `TokenFocusRing` | `#1A1A1A` | `#FFFFFF` | — |
| `TokenDanger` | `#C42B1C` | `#FF99A4` | 5,9:1 / 6,6:1 |
| `TokenWarning` | `#9D5D00` | `#FFC83D` | 5,0:1 / 8,8:1 |
| `TokenSuccess` | `#0F7B32` | `#6CCB5F` | 4,8:1 / 7,5:1 |
| `TokenScrollbarThumb` | `#8A8A8A` | `#9A9A9A` | — |
| `TokenOverlay` | `#66000000` | `#99000000` | — |
| `TokenShadow` | `#33000000` | `#66000000` | — |

\* A szövegtokenek kontrasztja a saját témájuk `TokenBgSurface` hátterén.
Mindegyik teljesíti a WCAG AA 4,5:1 küszöböt.

## Végigkattintós lista

- [ ] **Jobbklikk menü** fájlon, mappán, üres területen — és a benne lévő **almenük**
- [ ] **Címke-választó** menü (a fájlsor címke ikonja)
- [ ] **Modális ablakok**: Beállítások, Lomtár, Előnézet (F3), Átvitel megerősítése (F5/F6)
- [ ] **Megerősítő és hibaüzenet-dobozok**: kiadás eredménye, frissítés-újraindítás, lomtár ürítése
- [ ] **Tooltipek** (eszköztár gombok) és **popoverek** (címke-színválasztó)
- [ ] **Görgetősávok**: oldalsáv, fájllista, Beállítások, oszlopos nézet
- [ ] **Beállítások** minden vezérlője: kapcsoló, jelölőnégyzet, rádiógomb, legördülő, szövegmező, gomb
- [ ] **Állapotsor**, **útvonalsáv**, **breadcrumb**, és a breadcrumb szerkesztő módja
- [ ] **Fájllista fejléc**, hover, kijelölés, **inaktív kijelölés** (másik panelre kattintva)
- [ ] **Üres állapotok**: üres mappa, hozzáférés megtagadva, nem található útvonal
- [ ] **Letiltott (disabled) állapotok**: Vissza/Előre gomb a lista elején/végén
- [ ] **Drag & drop overlay** és a **húzásos kijelölő téglalap** (marquee)
- [ ] **Fókusz keret** Tab-bal végigjárva
- [ ] **Monokróm ikonok** — mind a `TokenTextPrimary`-t vegyék fel, ne legyen fix fehér
- [ ] **Betöltésjelzők** (ProgressRing) a fájllistán és az oszlopokban
- [ ] **Címke chipek** a fájlsorokon és az oldalsáv Címkék szekciójában
- [ ] **Aktivitás-központ**: folyamatsáv, ütközéskezelő, hibaszöveg
- [ ] **Funkcióbillentyű-sáv** a kétpaneles nézet alján

## Váltás közbeni ellenőrzés

- [ ] A téma azonnal vált, **újraindítás nélkül**, MINDEN nyitva lévő ablakban
  (a Beállítások nyitva hagyásával váltva is)
- [ ] „Rendszer" módban a Windows Gépház → Színek váltása **élőben** követődik
- [ ] Az akcentusszín váltása (paletta és egyedi hex) azonnal átfesti a
  kijelölést, a marquee-t és a kiemelt gombokat

## Regressziós fogódzó

A hiba gyökéroka az volt, hogy a `GlassPanelBrush` **egyszer** másolódott a
WPF-UI szótárából, és témaváltáskor a régi (sötét) ecsetobjektum maradt benne —
ettől ragadt sötéten az oldalsáv, a felső sáv és a Beállítások panel. A
`GlassEffectService` ezért ma feliratkozik az `ApplicationThemeManager.Changed`
eseményre. **Bármilyen új, WPF-UI szótárból másoló erőforrásnál ugyanezt kell
tenni.**
