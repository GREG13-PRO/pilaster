# Pilaster bug-report bot

The Pilaster desktop app's [Bug Report](../docs/BUG_REPORTS.md) section sends reports here. A
plain Discord webhook isn't enough anymore: clicking the **"Done"** button under a report is an
*interaction*, which only an actually-running bot connected to the Discord Gateway can receive —
hence this separate, always-on service.

> **Current setup:** the bot currently runs locally on the developer's own machine (see "Running
> locally" below), **not** on a cloud host. That means bug reporting only works while that machine
> is on and the bot is running — an acceptable tradeoff during early development, before there are
> external users. Once there are real users, it's worth moving to cloud hosting (see below) or a
> tunneling service.

## What it does

1. Runs an HTTP server (`POST /report`), which Pilaster calls with a shared API key.
2. Posts the report to the configured channel, with a "Done" button.
3. On button click, copies the report to the archive channel and marks the original as closed.
4. Once a day, deletes messages older than 30 days from the archive channel.

## Setup

1. **Create a Discord application**: [discord.com/developers/applications](https://discord.com/developers/applications) → *New Application*.
2. **Create a bot**: *Bot* tab → *Reset Token* → copy it (`DISCORD_BOT_TOKEN`). Turn off *Public Bot* if you don't want anyone to be able to invite it.
3. **Invite it to your server**: *OAuth2 → URL Generator* → scope: `bot`, permissions: *Send Messages*, *Embed Links*, *Attach Files*, *Manage Messages* (for closing and deleting old messages), *Read Message History*. Open the generated link and pick your server.
4. **Channel IDs**: enable Discord *Developer Mode* (Settings → Advanced), then right-click the report and archive channels → *Copy ID*.
5. **API key**: any sufficiently long random string, e.g. `openssl rand -hex 32`.
6. Copy `.env.example` to `.env` and fill it in.

```powershell
npm install
npm start
```

## Running locally (on your own machine, always on)

If you'd rather not pay for cloud hosting, the bot can run on your own machine too — you'll need
[Node.js](https://nodejs.org) (LTS version; on Windows, `winget install OpenJS.NodeJS.LTS`).

For **automatic startup** (so it launches on login, with no visible window):

1. Run `npm install` once, in the `discord-bot` folder.
2. Create a shortcut to `start-bot.vbs` (this launches it silently in the background, writing
   output to `bot.log`) in the Windows **Startup** folder
   (`Win+R` → `shell:startup` → paste the shortcut there).
3. The bot starts automatically on login. To try it right away without restarting, run it
   manually: double-click `start-bot.vbs`, or run `npm start` in a terminal.

**Limitation:** the bot is only reachable while this machine is on and you're logged in. If the
machine is asleep or off, bug reporting in Pilaster will show something like a "no internet
connection" error — that's expected in that case, not a bug.

## Deploying to a cloud host

The `Dockerfile` works with any Dockerfile-based host, if you decide to move to one later.

- **Railway**: *New Project → Deploy from GitHub repo*, set `discord-bot` as the root directory,
  then fill in the variables from `.env.example` under the *Variables* tab. Railway detects the
  Dockerfile automatically. **Important:** on the Networking tab, make sure the generated domain's
  "target port" matches the port the runtime log actually shows (Railway often assigns its own
  port to the service, which can differ from what's set in the `PORT` variable). New accounts get
  a 30-day / $5 trial, after which you need to switch to a paid plan.
- **Fly.io**: as of 2024 there's **no permanently free tier** — a card and payment are required
  even after the trial, so it's not recommended for this project right now.

The `/healthz` endpoint returns a simple "ok" response — use it as the health check if your host
asks for one.

## Setup on the Pilaster side

The Pilaster desktop app reads the bot's URL and API key the same way it used to read the
webhook — from an environment variable, or from a local file that never goes into the repo. See
[docs/BUG_REPORTS.md](../docs/BUG_REPORTS.md).
