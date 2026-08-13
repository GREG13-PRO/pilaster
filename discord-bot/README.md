# Pilaster hibabejelentő bot

A Pilaster asztali alkalmazás [Hibabejelentés](../docs/BUG_REPORTS.md) szakasza ide küldi a
jelentéseket. Egy sima Discord webhook erre már nem elég: a jelentés alá kerülő **„Kész"** gomb
kattintása egy *interakció*, amit csak egy ténylegesen futó, a Discord Gateway-hez kapcsolódó bot
tud fogadni — ezért kell ez a külön, folyamatosan futó szolgáltatás.

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

## Telepítés felhős hosztra

A `Dockerfile` bármelyik Dockerfile-alapú hoszttal (Railway, Fly.io, Render stb.) működik.

- **Railway**: *New Project → Deploy from GitHub repo*, add meg a `discord-bot` mappát root
  directoryként, majd a *Variables* fülön töltsd ki a `.env.example` szerinti változókat. A
  Railway automatikusan érzékeli a Dockerfile-t.
- **Fly.io**: `fly launch` a `discord-bot` mappában (Dockerfile-t érzékeli), majd
  `fly secrets set DISCORD_BOT_TOKEN=... REPORT_CHANNEL_ID=... ARCHIVE_CHANNEL_ID=... API_KEY=...`.

A `/healthz` végpont egyszerű "ok" választ ad — ezt add meg health checkként, ha a hoszt kéri.

## A Pilaster oldali beállítás

A Pilaster asztali alkalmazás a bot URL-jét és API-kulcsát ugyanúgy olvassa, mint korábban a
webhookot — környezeti változóból, vagy egy helyi fájlból, ami SOHA nem kerül a repóba. Lásd
[docs/BUG_REPORTS.md](../docs/BUG_REPORTS.md).
