<div align="center">

<img src="docs/assets/brand/lockup.png" alt="Pilaster" width="340">

**A modern file manager for Windows 11 — with Finder-style column view, a real activity center, and a lot of things Explorer never had.**

[![build](https://github.com/GREG13-PRO/pilaster/actions/workflows/build.yml/badge.svg)](https://github.com/GREG13-PRO/pilaster/actions/workflows/build.yml)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)

</div>

---

## Why?

Windows 11's Explorer is slow, and it's missing macOS Finder's best idea: **column view**, where the folder structure unfolds horizontally so you can see exactly where you are in the tree at a glance. Pilaster brings that to Windows — plus a lot more.

## Status

**v1.1.3 — actively developed, daily-driver ready.** What already works:

- **Cloud drives (NextCloud, ownCloud, WebDAV)**: connect any WebDAV server from the sidebar's "Cloud drives" section — through Windows' own built-in WebDAV redirector, so once connected it behaves like any other network path (copy, tags, favorites, everything just works). Credentials are never stored in a Pilaster file; Windows' own Credential Manager handles them.
- **Real Windows 11 icons in Quick Access**: Documents, Downloads, Pictures, Music, and Videos show their actual badged shell icon straight from `desktop.ini`, exactly as in Explorer — not a generic glyph.
- **Recycle Bin navigates in place**, like any real folder — same Details/Grid/Columns view, breadcrumb, and tab title as Documents or Downloads, with Restore/Delete permanently on right-click and an Empty Recycle Bin toolbar button.
- **Collapsible sidebar sections**: click a section header (Quick Access, Recent, Drives, …) to collapse or expand it, with an animated arrow — following Windows 11 Explorer's own grouped-sidebar convention.
- **Color themes**: a ready palette (blue, purple, green, red, orange, pink, teal, graphite) or a custom hex accent color, or automatically follow your own Windows accent color — applies to selections, the active sidebar item, buttons, and focus rings, in both light and dark themes, with contrast correction
- **Animation levels**: Full / Reduced / Off in Settings, following Windows' own "reduce motion" system setting by default
- **System integration** (optional, all OFF by default): open folders/drives in Pilaster, redirect Win+E, an "Open in Pilaster" right-click entry — each toggled individually, and turning it off restores Explorer's original behavior exactly (not just by deleting a registry key)
- **Its own copy/move/delete engine**: not Explorer's green bar — pausable, resumable, cancelable operations, transfer speed and time remaining, multiple operations at once in the Activity Center panel, conflict handling (overwrite/skip/keep both/apply to all), and if one file fails, the rest keep copying
- **Dual-pane view** (two-panel layout, modern styling): two fully independent panels — each with its own history, selection, and view mode; a "Sync" button, a draggable splitter, horizontal/vertical layout
- **Pilaster Classic keymap** (opt-in; Explorer-like shortcuts stay the default): F3 View, F4 Edit, F5 Copy, F6 Move, F7 New Folder, F8/Delete, Tab to switch panes, Insert/Space/Ctrl+A/Ctrl+D/Num*/Num- for selection, Alt+F7 quick filter
- **Fixed sort order**: folders always sort before files regardless of sort key, natural ordering (`file2` before `file10`), accent- and case-insensitive, size/date sorts by the real value (not the displayed text)
- **Home tab**: a "This PC"-style overview — Quick Access folders (Desktop, Documents, Downloads, Pictures, Music, Videos) and drives as tiles with a usage bar, in the app's own liquid-glass design
- **Editable Quick Access**: pin your own folders by right-click or drag onto the panel, remove default entries, reorder by dragging — everything saved, and still there after a restart
- **Recycle Bin in Quick Access**: view contents, restore or permanently delete items, "Empty Recycle Bin," an empty-state indicator
- **Instant rename** when creating a new folder/file — just like Explorer: the base name comes pre-selected and editable, Enter saves, Esc reverts, and name conflicts get an automatic number suffix
- **Eject icon** at the end of the row for removable and optical drives in the sidebar — one click for instant safe removal, never shown for the system drive
- **New app icon and branding** — a fresh, crisp look at every size (window, taskbar, installer, exe resource)
- **Native Windows 11 right-click menu**, on files AND on empty folder space alike — the menu is invoked on its own thread, so it never freezes the UI
- **Liquid-glass surface**: a translucent sidebar, top bar, context menus (with a native DWM Acrylic background), and Settings panel over the Mica backdrop — toggleable in Settings for weaker machines
- **Tags** (macOS Tags-style): 7 predefined colors with your own names, created/renamed/deleted in Settings; assigned to an item via the tag icon on its row, and the sidebar's Tags section filters by them
- **Favorites**: a heart icon on hover on a file row, a Favorites section in the sidebar, with a faint marker and one-click removal for deleted targets
- **Column (Miller) view** in the macOS Finder style: click a folder to open the next column to the right, click a file to see a details panel on the right (type, size, modified date) — the view mode (List/Grid/Columns) is remembered per tab
- **Breadcrumb**: copy the path with one click, or click to turn it into an editable text field (just like Explorer) — Enter navigates, Esc/losing focus reverts
- **Folder sizes** computed on a background thread and cached, with a "…" indicator while calculating
- **Eject** (safe removal) for removable and optical drives, with an "in use" error indicator on failure
- **Optical drive's own icon and name**, taken from the inserted disc's volume label/autorun.inf icon, refreshed automatically on disc swap
- Fluent UI with a Mica backdrop, rounded corners, **frameless/backgroundless toolbar buttons** (a subtle highlight only on hover, following the theme's text color)
- **Automatic updates** from GitHub Releases: checks quietly on startup, shows a non-intrusive banner, downloads with one click, verifies with a checksum, and installs after a restart confirmation
- **Theme**: light / dark / follow system, one-click toggle, animated, **saved**
- Sidebar with Quick Access, drives (usage bar, free space), Favorites, and Tags — **every level** of the folder chain highlighted, not just the exact match
- Tabs, back/forward/up/refresh, a sliding transition on folder change
- Details list and icon grid — both **fully virtualized**, with selection, a right-click menu (on an item AND on empty space alike: new folder/file, paste, refresh, sort) and **marquee (drag) selection**
- **Paste from clipboard** — in an Explorer-compatible format, for both copy and cut
- **Column-header sorting** with a direction arrow, matching Explorer's natural sort order
- Native Windows icons and thumbnails, cached to disk
- **Two customizable quick-action buttons** — folder or file, with your own name template (`{date}`, `{time}`, `{n}`) and target folder
- **Settings panel**: theme, transparency effect, animations, language, shortcuts, tags, updates — everything saves instantly
- **Bug reporter**: users see a public email address (`pilaster-explorer@proton.me`); the developer panel (sends straight to a Discord bot, with a "Done" button and automatic archiving, screenshot/log attachment) stays hidden until the section header is clicked 10 times
- Hungarian and English UI, with **runtime language switching** and automatic system-language detection

See the [roadmap](#roadmap) for what's coming in the next milestones.

## Performance

For a file manager, speed isn't a nice-to-have, it's table stakes. So:

| Area | Approach |
|---|---|
| Folder listing | `FileSystemEnumerable` instead of `DirectoryInfo` — one fewer allocation per item |
| Rendering | Background-thread traversal feeds a `Channel`; the first 200 items render immediately, and the batch size quadruples from there |
| List notifications | A custom `RangeObservableCollection`: one batch = one notification, not one per item |
| Grid view | A custom virtualizing wrap panel — WPF doesn't ship one |
| Icons | COM calls only fire for rows actually on screen; type icons are cached by extension |
| Sorting | Native `StrCmpLogicalW`, so ordering matches Explorer exactly (`image9` before `image10`) |

## Installation

Download from the [latest release](https://github.com/GREG13-PRO/pilaster/releases/latest):

- **`Pilaster-<version>-x64-setup.exe`** — the installer. No admin rights needed, installs into your user profile.
- **`...-portable.zip`** — the portable build: unzip and run, writes nothing to the system.

On ARM64 hardware (Snapdragon X, Surface Pro), use the `arm64` build.

> **Antivirus flagging it?** Without a signing certificate, that happens sometimes.
> [docs/ANTIVIRUS.md](docs/ANTIVIRUS.md) explains why, and how to verify with a
> checksum or your own build that the file is exactly what it claims to be.

## Building

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Nothing else.

```powershell
git clone https://github.com/GREG13-PRO/pilaster.git
cd pilaster
dotnet build Pilaster.slnx
dotnet run --project src/Pilaster.App
```

Building a standalone, single-file release:

```powershell
dotnet publish src/Pilaster.App -c Release -r win-x64 `
  -p:PublishSingleFile=true -p:SelfContained=true
```

## Structure

```
src/
├─ Pilaster.Core/       Domain: items, provider interface, sorting, formatting
├─ Pilaster.Providers/  Local file system (later: archives, FTP/SFTP, S3, WebDAV)
├─ Pilaster.Shell/      Win32/COM interop: icons, thumbnails, drive ejection, native right-click menu
└─ Pilaster.App/        WPF UI, view models, localization

discord-bot/            Node.js — bug-report Discord bot (see docs/BUG_REPORTS.md)
```

The `IFileSystemProvider` abstraction has been there from day one: the local disk is just *one* implementation. That's why archives, FTP, and S3 will eventually behave just like "folders" too, without any changes to the UI.

## Roadmap

| Version | Contents |
|---|---|
| **v0.1** ✅ | Skeleton, sidebar, tabs, details + grid view, localization |
| **v0.1.1** ✅ | Column-header sorting, installer, folder-based release |
| **v0.2** ✅ | Persistent theme switching, animations, Settings, two customizable quick-action buttons |
| **v0.3** ✅ | Bug reporter (Discord webhook), active-folder highlighting, cleaner toolbar |
| **v0.4** ✅ | File selection/right-click fixes, ancestor-chain highlighting, unified button style, sliding transition, idea/bug distinction |
| **v0.5** ✅ | Automatic updates, frameless buttons, right-click on empty space, marquee selection, clipboard paste |
| **v0.6** ✅ | Column (Miller) view, native right-click menu, folder size calculation, drive ejection, optical disc icon, Discord bot |
| **v0.6.1** ✅ | Public bug-report email + hidden developer panel, bot security update (multer 2.x) |
| **v0.7.0** ✅ | Liquid-glass UI, native right-click freeze fix, native menu on empty space, tags, favorites, breadcrumb editing |
| **v0.8.0** ✅ | New app icon, instant rename on creation, Recycle Bin in Quick Access, Home tab "This PC" view, editable Quick Access, drive-eject icon in the sidebar |
| **v0.9.0** ✅ | Color themes (accent color), animation levels, its own copy/move engine + Activity Center, dual-pane view, Pilaster Classic keymap + F3 preview, sort-order fix, optional system integration (Explorer replacement), download website |
| **v1.0.0** ✅ | Pilaster Editor (built-in text editor), native right-click menu with real shell integration, editable/persistent Quick Access, reorganized Settings, modern installer |
| **v1.0.1–v1.0.3** ✅ | Dual-pane column headers and status bar, selectable native/Pilaster right-click menu, context-menu preload and crash fixes, dozens of UI/reliability fixes from real-world use |
| **v1.1.0** ✅ | Cloud drives (NextCloud/ownCloud/WebDAV) via Windows' built-in WebDAV client, in-place Recycle Bin navigation, real Windows 11 Quick Access icons, collapsible sidebar sections, airier redesign, bilingual (EN/HU) website with light/dark theme |
| **v1.1.1** ✅ | Single-pane tab strip only shows with 2+ tabs open, tighter toolbar padding, dual-pane overflow-menu fix, discord-bot duplicate-archive fix |
| **v1.1.2** ✅ | Fixed: the window could open with its title bar above the visible screen on smaller/DPI-scaled displays |
| **v1.1.3** ✅ | Fixed: misaligned installer wizard logos, plus a developer-only test-suite side effect |
| v1.2 | Instant search (NTFS MFT index), command palette, quick jump |
| v1.3 | Workspaces, Shelf, bulk rename, duplicate finder |
| v1.4 | Terminal, Git integration, archives as folders, rule engine |
| v1.5 | Set as default file manager (full), disk map, folder sync |
| v1.6 | Remote providers (FTP/SFTP/S3/WebDAV client-side), plugin SDK |
| v1.7 | Polish, documentation, 30+ languages |

## Translating to another language

Strings live in [`src/Pilaster.App/Resources/`](src/Pilaster.App/Resources/). Adding a new language just needs a `Strings.<code>.resx` file — no code changes required. Details: [`docs/TRANSLATING.md`](docs/TRANSLATING.md).

## License

[MIT](LICENSE)
