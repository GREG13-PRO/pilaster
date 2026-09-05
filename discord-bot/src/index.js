import 'dotenv/config';
import crypto from 'node:crypto';
import express from 'express';
import multer from 'multer';
import {
  ActionRowBuilder,
  AttachmentBuilder,
  ButtonBuilder,
  ButtonStyle,
  Client,
  EmbedBuilder,
  Events,
  GatewayIntentBits,
} from 'discord.js';

// A Pilaster asztali alkalmazás (DiscordBugReportService) ezt a customId-t
// várja el ismerni sem kell — csak a saját gombjára figyel, az összes többi
// interakciót figyelmen kívül hagyja.
const DONE_BUTTON_ID = 'bugreport_done';
const ARCHIVE_MAX_AGE_MS = 30 * 24 * 60 * 60 * 1000;
const SWEEP_INTERVAL_MS = 24 * 60 * 60 * 1000;

// Azoknak a jelentés-üzeneteknek az ID-ja, amelyeken éppen fut a "Kész"
// archiválás — két majdnem egyidejű kattintás (pl. két ügyeletes egyszerre)
// enélkül mindkettő végigfutna, mielőtt a gomb ténylegesen eltűnik az eredeti
// üzenetről, és duplán archiválná ugyanazt a jelentést.
const closingReports = new Set();

const requiredEnv = ['DISCORD_BOT_TOKEN', 'REPORT_CHANNEL_ID', 'ARCHIVE_CHANNEL_ID', 'API_KEY'];
const missingEnv = requiredEnv.filter((name) => !process.env[name]);

if (missingEnv.length > 0) {
  console.error(`Hiányzó környezeti változó(k): ${missingEnv.join(', ')}. Lásd .env.example.`);
  process.exit(1);
}

const client = new Client({ intents: [GatewayIntentBits.Guilds] });

client.once(Events.ClientReady, (readyClient) => {
  console.log(`Bejelentkezve mint ${readyClient.user.tag}.`);
  sweepArchiveChannel().catch((error) => console.error('Archívum-tisztítás sikertelen:', error));
  setInterval(() => {
    sweepArchiveChannel().catch((error) => console.error('Archívum-tisztítás sikertelen:', error));
  }, SWEEP_INTERVAL_MS);
});

client.on(Events.InteractionCreate, async (interaction) => {
  if (!interaction.isButton() || interaction.customId !== DONE_BUTTON_ID) {
    return;
  }

  try {
    await handleDoneButton(interaction);
  } catch (error) {
    console.error('A "Kész" gomb kezelése sikertelen:', error);

    if (!interaction.replied && !interaction.deferred) {
      await interaction.reply({ content: 'Hiba történt az archiválás közben.', ephemeral: true }).catch(() => {});
    }
  }
});

/**
 * Lekéri a megadott azonosítójú csatornát, és ellenőrzi, hogy szöveges-e.
 * Hiba esetén (nem található / nem szöveges) null-t ad vissza — a hívó dönti
 * el, ez nála mit jelent (kivétel, csendes kilépés, vagy HTTP hibaválasz).
 */
async function fetchTextChannel(channelId) {
  const channel = await client.channels.fetch(channelId);
  return channel?.isTextBased() ? channel : null;
}

/**
 * A jelentés archiválása: az eredeti üzenet tartalmát átmásolja az
 * archívum-csatornára (a mellékletekkel együtt, a Discord CDN URL-jükről
 * újratöltve), majd az eredeti üzenetről leveszi a gombot és lezártnak
 * jelöli — nem törli, hogy a jelentés-csatornán megmaradjon a nyoma.
 */
async function handleDoneButton(interaction) {
  const messageId = interaction.message.id;

  if (closingReports.has(messageId)) {
    return;
  }

  closingReports.add(messageId);

  try {
    await interaction.deferUpdate();

    const original = interaction.message;

    const archiveChannel = await fetchTextChannel(process.env.ARCHIVE_CHANNEL_ID);

    if (!archiveChannel) {
      throw new Error(`Az ARCHIVE_CHANNEL_ID (${process.env.ARCHIVE_CHANNEL_ID}) nem szöveges csatorna.`);
    }

    const archivedEmbeds = original.embeds.map((embed) => EmbedBuilder.from(embed));

    if (archivedEmbeds.length > 0) {
      archivedEmbeds[0].setFooter({
        text: `Lezárta: ${interaction.user.tag} · ${new Date().toLocaleString('hu-HU')}`,
      });
    }

    const attachments = [...original.attachments.values()].map(
      (attachment) => new AttachmentBuilder(attachment.url, { name: attachment.name ?? 'attachment' }),
    );

    await archiveChannel.send({ embeds: archivedEmbeds, files: attachments });

    const closedEmbeds = original.embeds.map((embed) => EmbedBuilder.from(embed).setColor(0x71717a));

    await original.edit({
      content: `✅ Lezárva — ${interaction.user.tag}`,
      embeds: closedEmbeds,
      components: [],
    });
  } finally {
    closingReports.delete(messageId);
  }
}

/**
 * Az archívum-csatorna 30 napnál régebbi üzeneteinek törlése.
 *
 * A Discord bulkDelete csak 14 napnál fiatalabb üzeneteknél működik, ezért
 * egyenként töröl — igaz, lassabb, de egy alacsony forgalmú
 * hibajelentés-archívumnál ez nem számít, és minden korosztályra helyesen
 * működik, plusz állapotot sem kell sehol tárolni (a Discord üzenet-ID-ja
 * már önmagában időbélyeg).
 */
async function sweepArchiveChannel() {
  const archiveChannel = await fetchTextChannel(process.env.ARCHIVE_CHANNEL_ID);

  if (!archiveChannel) {
    return;
  }

  const cutoff = Date.now() - ARCHIVE_MAX_AGE_MS;
  let before;
  let deleted = 0;

  for (;;) {
    const batch = await archiveChannel.messages.fetch({ limit: 100, ...(before ? { before } : {}) });

    if (batch.size === 0) {
      break;
    }

    for (const message of batch.values()) {
      if (message.createdTimestamp < cutoff) {
        await message.delete().catch((error) => console.error(`Nem sikerült törölni #${message.id}:`, error));
        deleted += 1;
      }
    }

    before = batch.last().id;

    // Az üzenetek időrendben csökkennek, tehát ha az adag legrégebbi tagja
    // is a küszöbön belül van, a nála korábbiak sem lehetnek régebbiek —
    // nincs értelme tovább lapozni.
    if (batch.last().createdTimestamp < cutoff && batch.size < 100) {
      break;
    }
  }

  if (deleted > 0) {
    console.log(`Archívum-tisztítás: ${deleted} üzenet törölve (30 napnál régebbi).`);
  }
}

const app = express();

app.get('/healthz', (_req, res) => res.status(200).send('ok'));

const upload = multer({ storage: multer.memoryStorage(), limits: { fileSize: 25 * 1024 * 1024 } });

app.post(
  '/report',
  (req, res, next) => {
    const provided = req.header('X-Api-Key') ?? '';
    const expected = process.env.API_KEY;

    const providedBuffer = Buffer.from(provided);
    const expectedBuffer = Buffer.from(expected);

    const isValid =
      providedBuffer.length === expectedBuffer.length && crypto.timingSafeEqual(providedBuffer, expectedBuffer);

    if (!isValid) {
      res.status(401).send('Invalid API key');
      return;
    }

    next();
  },
  upload.fields([
    { name: 'files[0]', maxCount: 1 },
    { name: 'files[1]', maxCount: 1 },
  ]),
  async (req, res) => {
    try {
      const payload = JSON.parse(req.body.payload_json ?? '{}');
      const embeds = Array.isArray(payload.embeds) ? payload.embeds : [];

      if (embeds.length === 0) {
        res.status(400).send('Missing embeds in payload_json');
        return;
      }

      const reportChannel = await fetchTextChannel(process.env.REPORT_CHANNEL_ID);

      if (!reportChannel) {
        res.status(500).send('Report channel unavailable');
        return;
      }

      const files = [];
      const screenshotFile = req.files?.['files[0]']?.[0];
      const logFile = req.files?.['files[1]']?.[0];

      if (screenshotFile) {
        files.push(new AttachmentBuilder(screenshotFile.buffer, { name: screenshotFile.originalname || 'screenshot.png' }));
      }

      if (logFile) {
        files.push(new AttachmentBuilder(logFile.buffer, { name: logFile.originalname || 'log.txt' }));
      }

      const doneButton = new ActionRowBuilder().addComponents(
        new ButtonBuilder().setCustomId(DONE_BUTTON_ID).setLabel('Kész').setStyle(ButtonStyle.Success).setEmoji('✅'),
      );

      await reportChannel.send({ embeds, files, components: [doneButton] });

      res.status(204).send();
    } catch (error) {
      console.error('A jelentés feldolgozása sikertelen:', error);
      res.status(500).send('Internal error');
    }
  },
);

const port = Number(process.env.PORT ?? 3000);

app.listen(port, () => {
  console.log(`HTTP szerver figyel a ${port} porton.`);
});

client.login(process.env.DISCORD_BOT_TOKEN);
