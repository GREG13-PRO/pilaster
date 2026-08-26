# Bug reports — setting up the bot

The Settings → Bug Report panel lets users send in a report with one click, optionally with a
screenshot and a log snippet attached. Sending POSTs to the HTTP API of the
**[Discord bot](../discord-bot/) in the `discord-bot/` folder**.

## Why a bot, not a plain webhook

A simple inbound webhook was enough through v0.5. As of v0.6, each report gets a
**"Done" button** that marks it as handled — but that's a Discord *interaction* (a button click),
which only an actually-running bot connected to the Discord Gateway can receive. A plain inbound
webhook can't do that, so sending moved to the bot too. See
[`discord-bot/README.md`](../discord-bot/README.md) for installing and running the bot.

## Why the bot URL and key aren't hardcoded

Anyone with access to the source could use a hardcoded key to send anything to your Discord
channel on your behalf. So Pilaster **never stores these in the repo** — it reads them at
runtime, in this order:

1. **`PILASTER_BUG_REPORT_API_URL`** and **`PILASTER_BUG_REPORT_API_KEY`** environment variables —
   convenient for development and testing.
2. **`%APPDATA%\Pilaster\bugreport-api.txt`** — for the final, installed build. Two lines: the
   first is the URL, the second is the key. This file lives in the user profile and never goes
   into git.

If neither is set, the Send button stays disabled, and the panel shows where the file should go.

## Setting up your own bot

See [`discord-bot/README.md`](../discord-bot/README.md) for the full details — in short:

1. Create a Discord application + bot in the Developer Portal, and invite it to your server.
2. Run the bot somewhere — right now it runs **locally, on the developer's own machine** (see the
   "Running locally" section of `discord-bot/README.md`, with automatic startup on login), but the
   `Dockerfile` makes it easy to move to a cloud host (Railway, etc.) once there are external users.
3. Set up the bot's `.env` (token, two channel IDs, an API key you make up yourself).
4. Create the config file on the Pilaster side:

   ```
   %APPDATA%\Pilaster\bugreport-api.txt
   ```

   First line is the bot's URL (`http://localhost:3000` for a local run, or something like
   `https://pilaster-bot.up.railway.app` for a cloud host), second line is the API key.

   Or, for development, environment variables are simpler:

   ```powershell
   $env:PILASTER_BUG_REPORT_API_URL = "http://localhost:3000"
   $env:PILASTER_BUG_REPORT_API_KEY = "..."
   ```

## What ends up in Discord

An embed with the description, plus version/platform/.NET fields, with a "Done" button below it.
If the user checked the boxes for it:

- **Screenshot** — a PNG render of the main window from WPF itself (not a screen capture, so it
  shows the real UI state even if another window is covering the screen at the time).
- **Log** — the last ~200 KB of the most recently modified log file
  (`%LOCALAPPDATA%\Pilaster\logs\`), since the full log is too large and only the most recent
  events matter.

Clicking "Done" makes the bot copy the report to the archive channel and mark the original as
closed (no button, with a "✅ Closed" label instead). The bot deletes messages older than 30 days
from the archive channel once a day.

## Limits

- Discord embed description: 4096 characters — past 3800 the app truncates it and adds "…".
- File attachments: Discord's limit is typically 25 MB; the log snippet is capped at 200 KB, so
  this is never an issue.
