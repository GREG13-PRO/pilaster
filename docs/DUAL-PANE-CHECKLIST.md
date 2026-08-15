# Kétpaneles nézet — mit fed le a teszt, és mit kell kézzel (F7 / P2)

A v1.0 legsúlyosabb bejelentett hibája a kétpaneles nézet volt, ezért itt
tételesen elkülönítjük, mi bizonyított automatán, és mi marad kézi
ellenőrzésre. Ez a lista **nem** a fejlesztő önigazolása — a nyitott pontokat
végig kell kattintani a kiadás előtt.

## Automata lefedettség

`tests/Pilaster.Tests/PaneStateTests.cs` — 11 teszt, a rendes tesztkészletben
fut (`dotnet test`), WPF nélkül:

| Teszt | Mit igazol |
|---|---|
| `KetPanelFuljeiTeljesenKulonallnak` | A két panel fülgyűjteménye külön objektum, nincs átfedés |
| `KulonKijeloles` | A kijelölés fülönként tárolódik, a másik panelé nem változik |
| `KulonElozmeny` | A bal panel `GoBack`-je nem mozgatja a jobb panel előzményét, és nem hoz létre benne „előre" ágat |
| `KulonRendezes` | A két panel egyszerre rendezhető eltérő oszlop és irány szerint |
| `KulonNezetmodEsGorgetes` | Nézetmód és görgetési pozíció fülönként külön él |
| `FulvaltasMegorziAzElhagyottFulAllapotat` | Fülváltás után visszatérve a kijelölés, görgetés és rendezés megvan |
| `AzUtolsoFulNemZarhatoBe` | Nem lehet üres panelt előállítani |
| `FulBezarasaUtanASzomszedLeszAzAktiv` | Nem marad aktív fül nélküli panel |
| `KorbeforgoFulvaltasAPanelenBelulMarad` | `Ctrl+Tab` nem lép át a másik panelbe |
| `MindketPanelnekVanSajatAktivFulJeloltje` | Az aktív fül jelölése a modellen él, nem a fülsáv kijelölésén |
| `AktivFulIndexeAMunkamenetMentesehez` | A munkamenet a helyes fülindexet menti |

Ezen felül egy futásidejű önteszt (a fejlesztés közben futott, nem része a
kiadott kódnak) végigmérte a panelváltást, a `Ctrl+U` panelcserét, a
`Ctrl+L` útvonal-átadást, az elrendezésváltást, a splitter-arány mentését, az
egypaneles módra váltás és vissza forgatókönyvét, az eltűnt útvonal
kezelését és a munkamenet mentését — mind zölden.

## Amit a tesztek NEM fednek le — kézi ellenőrzés

Ezek mind valódi vizuális fát és egeret igényelnek, ezért nem
egységtesztelhetők.

- [ ] **Kijelölés vizuális megmaradása.** Jelölj ki 3 fájlt a bal panelben,
      majd kattints a jobb panelbe. A bal oldali kijelölés maradjon LÁTHATÓ
      (halványabb, „inaktív kijelölés" színnel), ne tűnjön el.
- [ ] **Görgetési pozíció visszaállása.** Görgess le egy nagy mappában, válts
      másik fülre, majd vissza — ugyanoda kell visszatérni.
- [ ] **Kijelölés visszaállása fülváltás után.** Jelölj ki elemeket, válts
      fület, válts vissza — a kijelölésnek vissza kell állnia.
- [ ] **Backspace / Alt+← csak az aktív panelt lépteti.** Navigálj mélyre
      mindkét panelben, majd lépj vissza — csak az aktív panel mozduljon.
- [ ] **Splitter húzása és dupla kattintás.** Húzd el az elválasztót, indítsd
      újra az appot: az arány álljon vissza. Dupla kattintás felezze.
- [ ] **Elrendezésváltás.** Váltsd függőleges ↔ vízszintes elrendezésre: a
      két panel útvonala, füljei és a splitter aránya maradjon meg.
- [ ] **Újraindítás utáni visszaállás.** Nyiss több fület mindkét panelben,
      lépj ki, indítsd újra — minden fül és az aktív fül álljon vissza.
- [ ] **Eltűnő útvonal.** Nyiss meg egy pendrive-ot az EGYIK panelben, húzd
      ki: csak az a panel lépjen a legközelebbi elérhető szülőre,
      hibaüzenettel; a másik panel maradjon érintetlen.
- [ ] **Drag & drop a panelek között.** Azonos meghajtón belül alapból
      áthelyezés, másik meghajtóra másolás; `Shift` = áthelyezés,
      `Ctrl` = másolás, `Alt` = parancsikon.
- [ ] **`F5`/`F6` mindkét irányban.** A célmappa mindig a MÁSIK panel legyen.

## Billentyűk preset szerint (K2)

| Preset | `Ctrl+R` | `Ctrl+Shift+R` | `F5` | `Alt+F5` |
|---|---|---|---|---|
| Pilaster Classic | jobb panel útvonala a balra | frissítés | másolás a másik panelbe | mindkét panel frissítése |
| Pilaster Modern | **frissítés** | – | **frissítés** | mindkét panel frissítése |
| Egyedi | a felhasználó kiosztása szerint | | | |
