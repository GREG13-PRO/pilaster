# Hibabejelentés — a bot beállítása

A Beállítások → Hibabejelentés panelen keresztül a felhasználók egy gombnyomással
küldhetnek hibajegyet, opcionálisan képernyőképpel és naplórészlettel. A küldés a
**[`discord-bot/`](../discord-bot/) mappában lévő Discord bot** HTTP API-jára POST-ol.

## Miért bot, nem sima webhook

A v0.5-ig egy egyszerű bejövő webhook is elég volt. A v0.6-tól a jelentés alá egy
**„Kész" gomb** kerül, amivel a jelentést fel lehet dolgozottnak jelölni — ez viszont már egy
Discord *interakció* (gombkattintás), amit csak egy ténylegesen futó, a Discord Gateway-hez
kapcsolódó bot tud fogadni. Egy puszta bejövő webhook erre nem képes, ezért a küldés is a botra
került át. A bot telepítéséhez és futtatásához lásd [`discord-bot/README.md`](../discord-bot/README.md).

## Miért nincs beégetve a bot URL-je és kulcsa

Aki hozzáfér a forráskódhoz, az egy beégetett kulccsal küldhetne bárkinek bármit a te
Discord-csatornádba. Ezért a Pilaster ezeket **soha nem tárolja a repóban** — futásidőben olvassa
be, ebben a sorrendben:

1. **`PILASTER_BUG_REPORT_API_URL`** és **`PILASTER_BUG_REPORT_API_KEY`** környezeti változók —
   fejlesztéshez, teszteléshez kényelmes.
2. **`%APPDATA%\Pilaster\bugreport-api.txt`** — a végleges, telepített változathoz. Két sor: az
   első az URL, a második a kulcs. Ez a fájl a felhasználói profilban él, sosem kerül git alá.

Ha egyik sincs beállítva, a Küldés gomb inaktív marad, és a panel megmutatja, hova kellene tenni
a fájlt.

## Saját bot beállítása

Lásd részletesen [`discord-bot/README.md`](../discord-bot/README.md) — dióhéjban:

1. Hozz létre egy Discord alkalmazást + botot a Developer Portalon, hívd meg a szerveredre.
2. Töltsd fel valahova (Railway, Fly.io, saját gép) a `discord-bot/` mappát — van hozzá Dockerfile.
3. Állítsd be a bot `.env`-jét (token, két csatorna ID, egy általad kitalált API-kulcs).
4. A Pilaster oldalán hozd létre a config fájlt:

   ```
   %APPDATA%\Pilaster\bugreport-api.txt
   ```

   Első sor a bot URL-je (pl. `https://pilaster-bot.up.railway.app`), második sor az API-kulcs.

   Vagy fejlesztéskor egyszerűbb környezeti változókkal:

   ```powershell
   $env:PILASTER_BUG_REPORT_API_URL = "https://pilaster-bot.up.railway.app"
   $env:PILASTER_BUG_REPORT_API_KEY = "..."
   ```

## Mi kerül a Discordba

Egy beágyazás (embed) a leírással, valamint verzió/platform/.NET mezőkkel, alatta egy „Kész"
gombbal. Ha a felhasználó bepipálta:

- **Képernyőkép** — a főablak WPF-renderelése PNG-ként (nem képernyőfotó, tehát akkor is a valós
  felületi állapotot mutatja, ha épp más ablak takarja ki a képernyőn).
- **Napló** — a legutóbb módosított naplófájl utolsó ~200 KB-ja
  (`%LOCALAPPDATA%\Pilaster\logs\`), mert a teljes napló túl nagy és a legutóbbi események
  számítanak.

A „Kész" gombra kattintva a bot átmásolja a jelentést az archívum-csatornára, az eredetit
lezártként jelöli meg (gomb nélkül, „✅ Lezárva" jelzéssel). Az archívum-csatornából a bot naponta
törli a 30 napnál régebbi üzeneteket.

## Korlátok

- Discord beágyazás-leírás: 4096 karakter — 3800 fölött a program levágja, és „…"-tal jelzi.
- Fájlmelléklet: a Discordnál jellemzően 25 MB a felső korlát; a naplórészlet 200 KB-ra van
  korlátozva, így ez sosem probléma.
