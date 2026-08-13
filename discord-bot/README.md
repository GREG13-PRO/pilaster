# Pilaster hibabejelentő bot

A Pilaster asztali alkalmazás [Hibabejelentés](../docs/BUG_REPORTS.md) szakasza ide küldi a
jelentéseket. Egy sima Discord webhook erre már nem elég: a jelentés alá kerülő **„Kész"** gomb
kattintása egy *interakció*, amit csak egy ténylegesen futó, a Discord Gateway-hez kapcsolódó bot
tud fogadni — ezért kell ez a külön, folyamatosan futó szolgáltatás.

> **Jelenlegi működés:** a bot most a fejlesztő saját gépén fut helyben (lásd „Helyi futtatás"
> lent), **nem** felhős hoszton. Ez azt jelenti, hogy a hibabejelentés csak akkor működik, ha az a
> gép be van kapcsolva és a bot fut rajta — ez a korai fejlesztési szakaszban (ahol még nincsenek
> külső felhasználók) elfogadható kompromisszum. Amint lesznek valódi felhasználók, érdemes lesz
> visszatérni a felhős hosztolásra (lásd lent) vagy egy alagút-szolgáltatásra.

## Mit csinál

1. HTTP szervert futtat (`POST /report`), amit a Pilaster hív meg egy megosztott API-kulccsal.
2. A jelentést a megadott csatornára posztolja, „Kész" gombbal.
3. Gombkattintásra a jelentést átmásolja az archívum-csatornára, az eredetit lezártnak jelöli.
4. Naponta egyszer törli az archívum-csatorna 30 napnál régebbi üzeneteit.

## Beállítás

1. **Discord alkalmazás létrehozása**: [discord.com/developers/applications](https://discord.com/developers/applications) → *New Application*.
2. **Bot létrehozása**: *Bot* fül → *Reset Token* → másold ki (`DISCORD_BOT_TOKEN`). Kapcsold ki a *Public Bot*-ot, ha nem szeretnéd, hogy bárki meghívhassa.
3. **Meghívás a szerverre**: *OAuth2 → URL Generator* → scope: `bot`, jogosultságok: *Send Messages*, *Embed Links*, *Attach Files*, *Manage Messages* (a lezáráshoz és a régi üzenetek törléséhez), *Read Message History*. A generált linket nyisd meg, válaszd ki a szervert.
4. **Csatorna ID-k**: kapcsold be a Discord *Fejlesztői mód*ot (Beállítások → Speciális), majd jobbklikk a jelentés- és az archívum-csatornán → *ID másolása*.
5. **API-kulcs**: bármilyen hosszú, véletlenszerű string, pl. `openssl rand -hex 32`.
6. Másold `.env.example`-t `.env` névre, töltsd ki.

```powershell
npm install
npm start
```

## Helyi futtatás (saját gépen, mindig bekapcsolva)

Ha nem szeretnél fizetni felhős hosztért, a bot futhat a saját gépeden is — ehhez kell a
[Node.js](https://nodejs.org) (LTS verzió; Windows-on `winget install OpenJS.NodeJS.LTS`).

Az **automatikus indításhoz** (hogy bejelentkezéskor magától elinduljon, látható ablak nélkül):

1. `npm install` egyszer, a `discord-bot` mappában.
2. Hozz létre egy parancsikont a `start-bot.vbs` fájlhoz (ez indítja csendben, háttérben, a
   kimenetet `bot.log`-ba írva) a Windows **Indítópult** mappájában
   (`Win+R` → `shell:startup` → ide másold a parancsikont).
3. Bejelentkezéskor a bot magától elindul. Ha most rögtön ki akarod próbálni újraindítás nélkül,
   futtasd kézzel: dupla katt a `start-bot.vbs`-en, vagy `npm start` a terminálban.

**Korlát:** a bot csak akkor érhető el, amíg ez a gép be van kapcsolva és be vagy jelentkezve. Ha
a gép alszik/ki van kapcsolva, a hibabejelentés a Pilasterben "nincs internetkapcsolat"-szerű
hibát fog mutatni — ez ilyenkor várható, nem hiba.

## Telepítés felhős hosztra

A `Dockerfile` bármelyik Dockerfile-alapú hoszttal működik, ha később mégis emellett döntenél.

- **Railway**: *New Project → Deploy from GitHub repo*, add meg a `discord-bot` mappát root
  directoryként, majd a *Variables* fülön töltsd ki a `.env.example` szerinti változókat. A
  Railway automatikusan érzékeli a Dockerfile-t. **Fontos:** a Networking fülön a generált domain
  "target port"-ja egyezzen azzal a porttal, amit a futási napló ténylegesen mutat (Railway
  gyakran saját portot rendel a szolgáltatáshoz, ami eltérhet a `PORT` változóban megadottól).
  Új fiók 30 napos / 5$ próbaidőt kap, utána fizetős csomagra kell váltani.
- **Fly.io**: 2024 óta **nincs tartósan ingyenes szintje** — bankkártya és fizetés kell hozzá már a
  próbaidő után is, ezért jelenleg nem ajánlott ehhez a projekthez.

A `/healthz` végpont egyszerű "ok" választ ad — ezt add meg health checkként, ha a hoszt kéri.

## A Pilaster oldali beállítás

A Pilaster asztali alkalmazás a bot URL-jét és API-kulcsát ugyanúgy olvassa, mint korábban a
webhookot — környezeti változóból, vagy egy helyi fájlból, ami SOHA nem kerül a repóba. Lásd
[docs/BUG_REPORTS.md](../docs/BUG_REPORTS.md).
