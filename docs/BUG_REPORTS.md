# Hibabejelentés — Discord webhook beállítása

A Beállítások → Hibabejelentés panelen keresztül a felhasználók egy gombnyomással
küldhetnek hibajegyet, opcionálisan képernyőképpel és naplórészlettel. A küldés
egy Discord **bejövő webhookra** (incoming webhook) POST-ol — nincs hozzá bot,
csak egy URL.

## Miért nincs beégetve a webhook URL

Aki hozzáfér a forráskódhoz, az egy beégetett webhookkal küldhetne bárkinek
bármit a te Discord-csatornádba. Ezért a Pilaster a webhook URL-t **soha nem
tárolja a repóban** — futásidőben olvassa be, ebben a sorrendben:

1. **`PILASTER_BUG_REPORT_WEBHOOK` környezeti változó** — fejlesztéshez,
   teszteléshez kényelmes.
2. **`%APPDATA%\Pilaster\webhook.txt`** — a végleges, telepített változathoz.
   Ez a fájl a felhasználói profilban él, sosem kerül git alá, és a
   `.gitignore` sem szabályozza, mert eleve a repón kívül van.

Ha egyik sincs beállítva, a Küldés gomb inaktív marad, és a panel megmutatja,
hova kellene tenni a fájlt.

## Saját webhook létrehozása

1. Discordban: **Szerverbeállítások → Integrációk → Webhookok → Új webhook**.
2. Válaszd ki, melyik csatornára érkezzenek a hibajegyek.
3. **Webhook URL másolása**.
4. Hozz létre egy `webhook.txt` fájlt ezen az útvonalon, és illeszd be az URL-t
   (semmi más ne legyen a fájlban, a program körülötte lévő üres helyet
   automatikusan levágja):

   ```
   %APPDATA%\Pilaster\webhook.txt
   ```

   Vagy fejlesztéskor egyszerűbb egy környezeti változóval:

   ```powershell
   $env:PILASTER_BUG_REPORT_WEBHOOK = "https://discord.com/api/webhooks/..."
   ```

## Mi kerül a Discordba

Egy beágyazás (embed) a leírással, valamint verzió/platform/.NET mezőkkel.
Ha a felhasználó bepipálta:

- **Képernyőkép** — a főablak WPF-renderelése PNG-ként (nem képernyőfotó, tehát
  akkor is a valós felületi állapotot mutatja, ha épp más ablak takarja ki a
  képernyőn).
- **Napló** — a legutóbb módosított naplófájl utolsó ~200 KB-ja
  (`%LOCALAPPDATA%\Pilaster\logs\`), mert a teljes napló túl nagy és a legutóbbi
  események számítanak.

## Korlátok

- Discord beágyazás-leírás: 4096 karakter — 3800 fölött a program levágja, és
  „…"-tal jelzi.
- Fájlmelléklet: a Discord webhookoknál jellemzően 25 MB a felső korlát; a
  naplórészlet 200 KB-ra van korlátozva, így ez sosem probléma.
