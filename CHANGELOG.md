# Changelog

Follows [Semantic Versioning](https://semver.org/).

## v1.1.1 — 2026-09-05

### UI — tab strip only when it's useful

- **The single-pane tab strip now stays hidden until a 2nd tab is open.**
  With only one tab, it used to occupy a full row for nothing to switch
  between. It reappears automatically the moment a second tab opens
  (Ctrl+T, or the overflow menu's new "New tab" entry, which only shows up
  while the strip itself is hidden — the shortcut always works regardless).
- **Tighter padding across the toolbar, breadcrumb pill, and quick-filter
  box** for a slightly more compact single-pane top area.

### Fixes

- **Fixed: the "…" overflow menu's "New tab" entry also showed up in
  dual-pane view**, where it was a pointless duplicate of the "New tab"
  button each pane already draws for itself.
- **Fixed: the README's top banner image pointed at a path that doesn't
  exist** (`assets/brand/lockup.png` instead of `docs/assets/brand/lockup.png`),
  so it rendered as a broken image on GitHub.
- **discord-bot: fixed a race where two near-simultaneous clicks on the same
  bug report's "Kész" button could archive it twice**, if both reached
  Discord before the button was actually removed from the original message.

## v1.1.0 — 2026-08-26

### New — cloud drives (NextCloud, ownCloud, WebDAV)

- **"Cloud drives" section in the sidebar.** Right-click the header → "Add
  cloud drive…" — connect any WebDAV server (NextCloud, ownCloud, etc.) by
  entering a server URL, username, and password (or app password). The
  connection goes through Windows' own, built-in WebDAV redirector (the
  "WebClient" service) — the server then behaves like a plain network path,
  so copying, tags, favorites, and everything else works on it unchanged.
  The password is NOT stored in a Pilaster file — Windows' own Credential
  Manager handles it. Removal: right-click a cloud drive row.

### UI — airier design

- **Real Windows 11 icons in Quick Access** — instead of the previous
  simple, single-color Fluent glyph, folders now show their own badged
  shell icon straight from `desktop.ini` (Documents, Downloads, Pictures,
  Music, Videos), exactly as in Explorer. The Recycle Bin gets the same
  treatment — requested via Windows' well-known namespace CLSID, since it
  has no real file-system path — and reflects its actual empty/full state.
  The Home tab intentionally keeps its own house glyph (user feedback: the
  real "This PC" icon wasn't obviously "Home" at a glance) — the same house
  icon now also appears on its tab label, and the Recycle Bin's tab label
  gets the trash-can glyph instead of the generic folder icon.
  Two side-effect fixes: (1) `IShellItemImageFactory.GetImage` would
  occasionally return `E_PENDING` for these custom-icon folders (the shell
  loads the icon asynchronously) — the call now waits it out with a few
  short retries instead of giving up on the first failed attempt; (2)
  requesting a 32px image instead of 20px, combined with WPF's smoother
  downscaling, fixes the earlier request sometimes returning a pixelated,
  upscaled icon.
- **Colored icons for the default Quick Access folders** (Desktop,
  Documents, Downloads, Pictures, Music, Videos) and the Recycle Bin —
  following the Windows 11 reference design pattern, where each folder type
  gets its own accent color instead of all of them inheriting the same
  neutral text color. Applies retroactively to existing Quick Access files
  too, if the user hasn't already set a custom color.
- **Unpin needle on pinned Quick Access folders.** Hovering or selecting
  shows a small pin icon at the right edge of the row — unpin with one
  click, no right-click menu needed, matching the reference design.
- **Section header arrows now point up when open, down when closed**
  (previously right/down) — matching the reference design and the
  direction convention used by Windows 11 Explorer's own grouped sidebar.
- **In dual-pane view, the top back/forward/up/refresh bar and path row
  disappear.** They were redundant there: both panes already draw their
  own navigation buttons and breadcrumb (see FilePaneView.xaml) — the top,
  whole-window-bound copy was just duplication. Search, the "…" menu, the
  view switcher, and the theme/Settings buttons remain available in a thin
  strip at the top right. Single-pane view is unchanged.
- **Collapsible sidebar sections.** Click a section header (Quick Access,
  Recent, Drives, and the rest) to collapse or expand it (an arrow shows
  the state, with an animated rotation) — following the pattern used by
  Windows 11 Explorer's grouped sidebar.
- **Bigger, airier layout throughout.** Larger sidebar icons and spacing, a
  bigger and rounder search box, a rounder/wider path pill (in both
  single- and dual-pane view), bigger corner rounding on the main panels
  (sidebar, file area, top bar) — altogether a roomier, less cramped look
  compared to the earlier, tighter layout.

### Website (docs/index.html)

- **Dark/light theme.** Follows the browser's system setting by default,
  but can also be switched manually (sun/moon icon in the nav) — the
  choice is remembered.
- **English/Hungarian language switch.** A toggle next to the GitHub
  button, top right; default language is English, and the whole page
  (text, title, meta description) fades smoothly between English and
  Hungarian — the choice is remembered.

### Fixes — Recycle Bin

- **The Recycle Bin no longer opens in a separate window — it navigates
  like a real folder.** Instead of the previous `RecycleBinWindow`
  (a standalone OS window), clicking the Recycle Bin makes the tab
  navigate there exactly like Documents or Downloads would — the same
  Details/Grid/Columns view, breadcrumb, and tab title as any real folder.
  Right-clicking an item offers Restore/Delete Permanently (instead of the
  usual Open/Cut/Copy), the background right-click menu offers Empty
  Recycle Bin, and the Delete key — just like Explorer — immediately
  deletes PERMANENTLY after confirmation.
- **A toolbar button to empty the Recycle Bin.** Only shown in Recycle Bin
  view, next to the Refresh button — no need to right-click the background
  for it.
- **Modern, Fluent-style confirmation dialogs.** The Recycle Bin's Delete
  Permanently/Empty dialogs now use WPF-UI's own `MessageBox` instead of
  the native, old-fashioned `System.Windows.MessageBox` — the same
  rounded, light/dark-theme-aware design as the rest of the UI (user
  feedback: the old native dialog looked like a style break).
- **Fixed: emptying the Recycle Bin threw an error if it was already
  empty** (`E_UNEXPECTED` from `SHEmptyRecycleBin` — undocumented, but
  observed Shell behavior). It now silently does nothing if it's already
  empty.
- **Fixed: navigating to a drive root (e.g. "C:\") showed a row with a
  blank label in Quick Access's "Recent" section** — `GetFileName` returns
  an empty string for a drive root, which made the row show up nameless
  and "shifted." The full path is now used as the label in that case.
- **The Activity Center (the copy/move progress panel) is no longer
  transparent.** It now always gets an opaque card background instead of
  the shared "liquid glass" background, regardless of the Color Themes
  liquid-glass toggle — floating over a constantly changing file list, the
  transparency made it hard to read and distracting.

## v1.0.3 — 2026-08-25

### New — right-click menu

- **Selectable native Windows menu (default).** New option under Settings →
  Right-click menu: **Windows** (the default, and existing users are
  switched to it on update too) or **Pilaster** (the app's own,
  Fluent-styled menu). Windows mode shows the REAL system menu
  (`TrackPopupMenuEx`, on the same raw, non-Vanara `HMENU` the
  `ShellMenuSession` already built) — behaving exactly like Explorer,
  including installed extensions' (7-Zip, Send To ▸, etc.) REAL,
  dynamically populated submenus. The eight custom commands with no native
  equivalent (Open in New Tab, Open in Other Pane, Edit with Pilaster
  Editor, Copy Path/Name, Open Terminal Here, Pin to Quick Access, Tags)
  are inserted ahead of the shell's own items, with their own icons — the
  app doesn't duplicate them in the native list. The existing
  Open/Cut/Copy/Paste/Delete/Rename/Properties, etc. come straight from the
  REAL shell menu, with no duplication. The A2 preload (v1.0.2) works in
  both modes — the query is the same, only the display differs. Confirmed
  with a REAL, human test: 7-Zip's submenu populates dynamically, with the
  selected file's name (e.g. "Add to »filename.7z«"), with no errors.

### Fixes/technical notes — right-click menu

- **Dead, dangerous code removed.** `NativeContextMenuService` used to
  render FILE items' native menu through Vanara's
  `ShellContextMenu.CreateFromItems` too — this call was the proven cause
  of a documented, five-round 0xC0000374 heap corruption, and it stayed in
  the code afterward, alive but unused (`ShowAsync`/`ShowItemsCore`) — a
  silent trap for some future change. Removed; the Vanara comment in
  `Pilaster.Shell.csproj` that previously (and incorrectly) called this
  same call "tested, safe" has also been corrected.
- **New native display layer** (`ShellMenuSession.ShowNativeAsync`,
  `NativeMenuOwnerWindow`, extensions to `NativeMenuInterop`): calls
  `TrackPopupMenuEx` on the SHARED STA thread, the same thread the
  `IContextMenu` was created on (mandatory — otherwise dynamic submenus
  would come back empty). A freshly created, invisible helper window
  (`NativeMenuOwnerWindow`) for each display forwards
  `WM_INITMENUPOPUP`/`WM_DRAWITEM`/`WM_MEASUREITEM`/`WM_MENUCHAR` messages
  to `IContextMenu3::HandleMenuMsg2`. The menu does NOT freeze the WPF
  window while it's open — MEASURED (headless self-test): the UI
  Dispatcher, pinged every ~30 ms, kept responding throughout a menu held
  open for ~500 ms.
- **A real bug, only found by manual testing.** The `GetDC` P/Invoke
  declaration incorrectly pointed at `gdi32.dll` — it's actually exported
  by `user32.dll` (a classic Win32 trap). This threw an
  `EntryPointNotFoundException` in icon rendering on every native menu
  open, taking down the entire right-click menu (and the app with it). The
  headless self-test didn't catch this, because it used a `nint.Zero`
  (iconless) test command — only a REAL right-click exposed the bug.
  Fixed, and the self-test now runs with a REAL rendered icon, so this
  class of bug can't slip through unnoticed again. A deliberately broad
  `catch (Exception)` was also added around icon rendering: a failure at a
  P/Invoke boundary (GDI calls) now causes at most a missing icon, never an
  app crash.

### Known limitations

- **Automated testing of submenu navigation isn't reliable.** A headless
  self-test attempt that used `PostMessage` (not global input) to navigate
  into a REAL, slow extension's (7-Zip) submenu once "froze" the test
  process for over 90 seconds — likely because `WM_CANCELMODE` arrived
  WHILE a slow, synchronous `HandleMenuMsg2` call was in progress. It
  eventually closed on its own, with nothing left hanging, but because of
  this, final confirmation of submenu population was done by manual
  testing, not automated.
- The existing limitations (process isolation, slow shell extensions, no
  code signing, etc., see v1.0.2) are unchanged.

## v1.0.2

### New — right-click menu

- **Opening animation brought back, safely (A1).** In v1.0.1, fixing the
  black border (see below) took the opening animation down with the
  `EffectThicknessDecorator` it was attached to — the menu appeared
  instantly afterward, with no animation. Now the menu (and every submenu)
  runs a subtle fade-up on its ALREADY FINAL, fully-sized content
  (`Opacity` 0→1 + `RenderTransform.Y` −6→0 px, `CubicEase`/`EaseOut`, 130 ms
  — 70 ms at the reduced animation level), via code-behind
  `BeginAnimation`, NEVER through `Style`/`Storyboard`+`DynamicResource`.
  This is DELIBERATE: an earlier experiment (row/tile hover highlighting)
  crashed with exactly this pattern ("Cannot freeze this Storyboard
  timeline tree"), because a `Storyboard` containing a `DynamicResource`
  can't be frozen. The new animation doesn't modify the Popup's size or
  `Margin` afterward either, so it does NOT bring back the black-border bug.
  Controlled by the existing Appearance category's "Animations" toggle
  (Full/Reduced/Off); at Off, the menu appears instantly, with no
  animation. MEASURED: the synchronous cost of opening didn't increase
  (`BeginAnimation` doesn't block), the ~90 ms median open time for the
  app's own items is unchanged.
- **Preload on selection (A2).** On selection, after a short (200 ms)
  delay, the app preloads the selected item(s)' shell-menu query in the
  background — if the user then right-clicks the SAME selection, the menu
  opens immediately with its FULL content (both the app's own items and
  the shell's), with none of the usual "Loading extensions…" lag. If the
  selection changes in the meantime, a preload that hasn't started yet
  cancels itself quickly, so the expensive COM call doesn't run, and
  whatever query is waiting behind it (e.g. from an actual right-click) can
  run almost immediately — an ALREADY STARTED call, as before, can't be
  safely interrupted. At most ONE preloaded session is alive at a time; the
  old one always releases on the shared STA thread before a new one
  starts, and a preload left unused for more than ~30s also releases
  itself automatically. MEASURED (dev machine, Release build,
  `notepad.exe`): time until shell items arrive, with a ready preload,
  ~1081 ms → ~0 ms (file), ~240 ms → ~0 ms (folder as an item); that number
  is small because the expensive COM call already ran at selection time.
  Can be turned off under Settings → Right-click menu (ON by default); if
  it ever causes trouble, turning it off falls back to the v1.0.1 path
  (no preload).

### Fixes — right-click menu

- **Black border/dark strip around the menu.** Root cause: WPF-UI's
  `ContextMenu` template wraps the menu in an `EffectThicknessDecorator`,
  which AFTER OPENING dynamically adds a 30px margin to the menu to make
  room for blurring the `DropShadowEffect`-based shadow. When the menu
  grows afterward, once the shell items arrive, this margin's newly
  exposed strips of the Popup don't get an actual (transparent) repaint —
  a stale, solid gray/dark strip is left there. MEASURED: reproducible in
  both themes, independent of the native Acrylic background (Liquid
  Glass). The template now uses WPF's built-in, native `HasDropShadow`
  mechanism instead of the decorator+effect — this doesn't dynamically
  resize the Popup afterward, so it doesn't leave a stale strip. As a side
  effect, the menu's open time MEASURED not just held steady but got
  faster (Release, after warmup: ~130 ms → ~90 ms median), since the
  earlier GPU effect and opening animation were dropped.

## v1.0.1

Pure UI fixes from real-world use after the v1.0.0 release — **no new
features**, only the right-click menu's and dual-pane view's appearance
changed.

### Fixes — right-click menu

- **Empty gray box at the top of the menu.** The built-in search box was a
  plain, undecorated native `TextBox` — with no placeholder or icon, it
  read as an empty box. Now it uses WPF-UI's `TextBox` with
  `PlaceholderText`/`Icon`, the same pattern as the Settings search box.
- **The menu got clipped at the bottom of the screen.** The `ContextMenu`
  didn't constrain itself to the work area. On open, it now queries the
  CORRECT (not necessarily primary) monitor's work area via native
  `MonitorFromWindow`/`GetMonitorInfo`, converts it to WPF units using that
  monitor's own DPI, and constrains `MaxHeight` to it — this activates the
  native `ContextMenu` template's built-in scrolling (up/down arrows), just
  like Explorer. MEASURED: a 68-item menu (natural height ~1900+ px) was
  constrained to 972px on a 1032px work area — scrolling correctly kicked
  in.
- **Duplicated "Open".** Alongside the app's own "Open," a shell-sourced
  "Open" also showed up in the "Other apps" section. Filtering is now
  based on the shell item's LANGUAGE-INDEPENDENT verb
  (`IContextMenu.GetCommandString(GCS_VERBW)`); if an extension doesn't
  provide a verb, the fallback signal is the `MFS_DEFAULT` state. The
  filtered item is logged at `Debug` level.
- **Own items disabled without reason.** "Open in New Tab," "Open in
  Other Pane," and "Pin to Quick Access" showed up grayed out on a file (a
  non-navigable item) instead of disappearing — a disabled item of the
  app's own implies something is broken. `PilasterMenuEntry` now
  distinguishes between `IsVisible`/`IsEnabled`: whatever is conceptually
  never applicable to that item type (the three above on a file, or "Open
  in Other Pane" without dual-pane view) doesn't make it into the menu AT
  ALL.
- **The "Other apps" label blended in.** It had an empty icon — occupying
  the same icon column as a real item — which visually lined it up with
  the left edge of every other row's text, and it got even vertical
  spacing.

### Fixes — dual-pane view

- **No column headers.** The panes' file list now uses a `GridView` with
  Name/Size/Type/Modified columns — sortable by click (the same
  `TabViewModel.ApplySort` as single-pane Details view), resizable by
  dragging, and the Size/Type/Modified widths are saved **per pane**
  separately (`AppSettings.LeftPane…`/`RightPane…`). The Name column fills
  the remaining space — `GridViewColumn` doesn't support "*" sizing, so
  code-behind recalculates it whenever the pane or its columns are
  resized. Also shown under Settings → Panes (adjustable by dragging, and
  the category's "Restore Defaults" button resets it from there too).
- **The column layout was cramped.** Size right-aligned, Type and Modified
  at a fixed width, Name fills the rest with `…` for long names.
- **Rows were hard to track.** Selection/hover now starts 4px in on both
  sides, rounded. New alternating-row striping, on by default
  (`AppSettings.DualPaneRowStriping`), with a toggle under Settings →
  Panes, applied instantly.
- **Four rows of controls before the content.** The toolbar
  (back/forward/up/refresh) and the path bar are now combined into one
  row — a full row's worth of extra room for files.
- **The active-pane indicator was too heavy.** Instead of a 1.5px
  accent-colored border on all four sides, the border is now always
  neutral, with only the TOP edge thickening and turning accent-colored
  while the pane is active.
- **A rendering glitch in the left pane's top-right corner.** The
  sync/swap button between panes used to float, with
  `VerticalAlignment="Top"`, at exactly the same height as the panes' own
  tab strip — at a narrow window width or DPI, this overlapped the "+"
  new-tab button. Centered vertically on the splitter instead, where it
  floats independent of width and DPI, and never overlaps either pane's
  header.
- **No status bar.** One shared row below the two panes, showing the
  ACTIVE pane's item count, selection, and the volume's free space.

### Fixes — Quick Access

- **Rows were too thin.** `SidebarItemStyle` previously had no explicit
  height — `TokenRowPadding` gives 0 vertical padding at all three
  densities, so row height just shrank to fit the content, regardless of
  the density setting. Now bound to `TokenRowHeight`, giving 32px at
  "comfortable" density (the low end of the requested 32–36px range), and
  keeping the previous, narrower 24px at "compact" density. Selection now
  starts 4px in, with 4px rounding. The section header's left margin lines
  up with the list items, with more even spacing.

### Fixes — other

- **The screenshot attached to bug reports came out blank in dark theme.**
  Root cause: `RenderTargetBitmap`-based capture renders the WPF visual
  tree WITHOUT DWM compositing — pixel-perfect on most surfaces, but on
  the semi-transparent, Mica-backed "liquid glass" sidebar
  (`GlassPanelBrush`), in dark theme, it produced a dim, nearly blank
  result, because without the real Mica base color, alpha blending falls
  back to resolving against nothing. Capture now primarily calls the
  native `PrintWindow` with the `PW_RENDERFULLCONTENT` flag — this copies
  the ACTUALLY composited, DWM-rendered surface, Mica included, and still
  works even if another window is covering it at the time. Falls back to
  the original `RenderTargetBitmap` path on failure.
- **The dual-pane view's Home tab stayed blank.** The panes' file list
  (`FilePaneView`) doesn't know about the Home dashboard (cards, drives) —
  that stays a v1.1 task. Instead, NEW tabs in dual-pane view now open a
  real folder (the configured start folder, or the user profile if none is
  set) instead of the virtual Home. If a tab does end up in Home state
  anyway (e.g. from an older saved session), it at least shows the Quick
  Access list there, not a blank surface.
- Duplicated bullets in the "Known limitations" section (process
  isolation, code signing, custom keymap editor) merged.
- The K1 (column-width persistence) and K3 (row striping) settings are now
  also available under Settings → Panes, not just in the config file —
  see below.

### Fixes — Settings

- **The "Horizontal layout" toggle under the Panes category showed up but
  had no effect.** The XAML bound to a property named `DualPaneVertical`,
  which never existed on `SettingsViewModel` — the toggle sat there as a
  silent, error-free dead binding. It now binds to a real property, which
  immediately flips the visible layout on both panes
  (`MainWindowViewModel` picks it up through the settings-change event),
  and the category's "Restore Defaults" button reaches it too.
- **New row-striping and column-width toggle** under the Panes category —
  see the K1/K3 note above.
- **Protection against silent binding errors.** WPF binding errors don't
  show up anywhere by default — which is exactly what caused the
  `DualPaneVertical` bug above. A built-in scanner now walks every
  window's visual tree (`BindingErrorScanner`), finding unresolved
  bindings via `BindingExpression.Status`; in Debug builds, found errors
  also go into the diagnostic log (`CrashDiagnostics`). A new automated
  test (`BindingErrorTests`) opens every window and every Settings
  category, and fails if even a single binding error occurs — this test
  would have caught the bug above automatically.

## v1.0.0

The first release that isn't a milestone, but a **complete product**: the
dual-pane view rewritten from scratch, a right-click menu with its own
design but real shell integration, a built-in text editor, reorganized
settings, and a full theme audit.

### New features

**Pilaster Editor — built-in text editor**
- `F4` (Pilaster Classic keymap) or `Ctrl+E` (both keymaps) on a selected
  file; also from the right-click menu.
- Multiple tabs, one file per tab. A dot marks a modified tab, and it asks
  to save on close and on exit.
- Syntax highlighting: `txt, md, json, xml, yaml, yml, ini, cfg, conf,
  properties, log, js, ts, py, java, cs, c, cpp, html, css, sh, bat, ps1,
  sql, sk`. **`.sk` and `.yml`** got their own definitions, written for
  this release.
- Line numbers, word wrap, current-line highlighting, box selection.
- `Ctrl+F`/`Ctrl+H` find and replace with regex + case + whole-word
  options; `Ctrl+G` go to line; `Ctrl+D` duplicate line;
  `Ctrl+Shift+K` delete line; `Alt+↑/↓` move line.
- Encoding: auto-detection (BOM + heuristics), manual switching between
  UTF-8 / UTF-8 BOM / CP1250 / CP852 / UTF-16LE / UTF-16BE. "Reopen with
  this encoding" and "Save with this encoding" are separate commands.
- Line-ending detection (CRLF/LF/CR/mixed) and conversion.
- Status bar: line:column, selected characters, encoding, line ending,
  language, INS/OVR.
- **Atomic save**: writes to a temp file, then swaps it in — no truncated
  file even on a power loss.
- Read-only files open with a banner; over 50 MB, read-only only; binary
  content doesn't open at all, falling back to the `F3` hexdump preview.
- Detects external changes on disk and offers to reload.

**Its own right-click menu, with shell integration**
- The menu is entirely Pilaster's own design, **but** shows installed
  shell extensions' (7-Zip, Notepad++, TortoiseGit, PowerToys, …) items
  with submenus and icons, and they do exactly what they do in Explorer.
- No "Show more options" two-tier split — everything's on one level.
- Asynchronous loading with a timeout: the app's own items appear
  instantly.
- Extension blocklist by name or CLSID.
- Optional search in the open menu, with keyboard navigation.

**Quick Access: editable and persistent**
- Its own, versioned `quickaccess.json`; every change saves instantly.
- An editor window: drag-and-drop reordering, add, rename, icon and
  color, group, separator, remove, defaults, import/export.
- Right-click on the header and on rows; **Pinned** and **Recent**
  sections.
- A missing path doesn't disappear on its own: grayed out, with a warning
  icon, fixable from the right-click menu.
- Network paths get an asynchronous, timeout-bound reachability check.

**Reorganized Settings**
- A left-side category list (11 categories) + a search box that matches
  the name, the description, **and hidden keywords**, in both Hungarian
  and English.
- Deep links: every setting has an ID, so other places can jump straight
  to it.
- Per-category "Restore Defaults" + full export/import.
- About 30 new settings, each with a short help text underneath.

**Modern installer**
- Inno Setup 6, `WizardStyle=modern`, per-user by default (no UAC).
- Install type: Normal / Custom / **Portable**.
- Options: desktop shortcut, Start menu, Explorer right-click (file,
  folder, and folder-background verbs), file associations, launch with
  Windows, default file manager.
- Silent install: `/VERYSILENT /NORESTART /DIR="…" /PORTABLE=1 /TASKS="…"`.
- The uninstaller **asks** whether to delete settings, and keeps them by
  default.

**Other**
- Per-pane tabs (`Ctrl+T`, `Ctrl+W`, `Ctrl+Tab`).
- `Ctrl+U` swap panes, `Ctrl+L`/`Ctrl+R` pass path across, `Alt+F5` refresh
  both panes.
- `Alt`+drag: create a shortcut.
- Session save and restore, with both panes' full tab set.
- Portable mode: settings go into the app's own folder.

### Changes

- **Dual-pane view was rewritten from scratch.** Through v0.9, the two
  panes were each a single, lone tab, with the tab system living
  independently as global state — which meant no per-pane tabs, and their
  state got mixed together. Now **every file-list state lives per pane**
  (tabs, active tab, and per-tab path, history, selection, focused item,
  sort, view mode, scroll, filter); only the active pane and the layout
  remain global.
- **The keymap got new names.** "Total Commander keymap" is now
  **Pilaster Classic (dual-pane)** and **Pilaster Modern (Explorer-like)**.
  Behavior is unchanged, only the label. No third-party product name
  appears anywhere in the user-visible UI.
- **The shell menu warms up on startup**, on a low-priority background
  thread. MEASURED: the first right-click takes 2186 ms without this,
  1132 ms with it — the difference is the COM apartment starting up and
  extension DLLs loading, a one-time cost.
- The right-click menu's shell-item **timeout is 2000 ms** (instead of the
  400 ms suggested in the spec). The value comes from measurement: the
  worst-case first query after warmup is 1132 ms, ×1.75 safety margin.
  This doesn't slow anything down: the menu's own open time MEASURED at
  96 ms, with shell items sliding in afterward. Detailed numbers used to
  live in `docs/CONTEXT-MENU.md`.
- **The installer always runs on the per-user branch.** MEASURED: with the
  previous `PrivilegesRequiredOverridesAllowed=dialog`, a silent install
  used to go down the per-machine branch (writing to HKLM, putting
  shortcuts on the Public Desktop); with `commandline dialog`, the
  mode-picker dialog popped up even under `/VERYSILENT`. Now only
  `commandline` remains — per-machine install can be requested with the
  `/ALLUSERS` switch.
- The editor's default font is Consolas (not Cascadia Mono: the latter
  ships with Windows Terminal, not Windows itself).

#### Breaking change: `Ctrl+R` — **Pilaster Classic keymap only**

| Preset | `Ctrl+R` | `Ctrl+Shift+R` | `F5` | `Alt+F5` |
|---|---|---|---|---|
| **Pilaster Classic** | copy right pane's path to left *(changed)* | refresh | copy to other pane | refresh both panes |
| **Pilaster Modern** | refresh *(unchanged)* | – | refresh *(unchanged)* | refresh both panes |
| Custom | per the user's own bindings | | | |

Pilaster Modern follows the Explorer/browser convention: both `Ctrl+R` and
`F5` refresh, exactly as before. **Anyone using the Modern keymap sees no
change at all.**

In the Classic keymap, `Ctrl+R` used to refresh; it now follows the
classic dual-pane convention instead. Refresh moved to `Ctrl+Shift+R`
there.

*Migration:* nothing to do — the keymap isn't stored config data. The full
list is available anytime from Settings → Keyboard → "View keymap."

#### Migrations (automatic, no data loss)

- The v0.9 `totalcommander` / `tc` / `total_commander` config values and
  the old boolean toggle map to **Pilaster Classic**.
- Quick Access stored in `settings.json` moves to `quickaccess.json` —
  once, on the first v1.0 startup, and only if the new file is still
  empty.
- Tags' `metadata.json` still loads unchanged; the palette grew from 7 to
  12 colors, the old color names untouched.

### Fixes

- **Surfaces stayed dark in light mode.** Root cause: `GlassPanelBrush`
  was copied once from WPF-UI's dictionary, and on theme change the old
  (dark) brush object stayed stuck in it — this is what kept the sidebar,
  top bar, and Settings panel stuck dark. The entire UI moved to a
  23-token theme token set (`ThemeTokenService`), every hardcoded hex
  color disappeared, and every text token now meets WCAG AA's 4.5:1
  contrast requirement, measured and verified.
- **A tag's color wasn't visible in Settings.** The dot was replaced with
  the spec's 14×14, rounded, **always-bordered** color swatch (without
  which a light-colored tag would blend into a light background). The
  swatch also appears in the file list, in the filter, and in the panes,
  with a color-picker popup: 12 predefined colors + a custom hex, with a
  live preview.
- **Wrong taskbar icon.** The app now sets its `AppUserModelID`
  (`Obsidix.Pilaster`) at process start, every window gets an explicit,
  multi-resolution `.ico` icon, and the installer writes the same ID into
  the Start menu and desktop shortcut properties.
- If a pane's path disappears (an unplugged flash drive), the pane now
  moves to the nearest available parent with an error message, instead of
  silently going blank.
- The top tab strip's `ListBox` was writing `null` back to the active-tab
  slot on pane switch; the active pane would briefly have no active tab as
  a result.
- AvalonEdit's `TextDocument` is thread-bound; the editor threw a
  `NullReferenceException` in the measurement phase on every open.
- **Opening a large file froze the UI.** MEASURED: for a 122.7 MB log
  file, a 50 ms clock only got 17 of the expected 97 ticks during
  loading — a 4.6-second silent freeze. Reading, decoding, and building
  the document moved to a background thread (with AvalonEdit's proper
  `SetOwnerThread` ownership transfer, since the document is thread-bound),
  opening became cancelable, and got a proportional progress indicator.
  Re-measured: the tick ratio returned to the IDLE baseline (74–77%
  instead of 78%), with a single 196–1343 ms pause left — handing the
  document to AvalonEdit's view, which has to run on the UI thread. No
  half-loaded tab is left behind after "Cancel."
- **The right-click menu crashed the app on the second open.** MEASURED
  (Release, real menu path): the process crashed with `0xC0000374`
  (heap corruption), in rounds 1–2, across all four scenarios. The bug was
  caused by `Vanara.Windows.Shell`'s `ShellContextMenu.CreateFromItems`
  call. The proof started from the WORKING side, moving one variable at a
  time (`tools/ShellCrashRepro/`): a minimal, raw P/Invoke harness runs
  4×10/10 cleanly; 3×10/10 even without message pumping; 3×10/10 even with
  the `ShellItem` lifecycle included — but with `CreateFromItems`, it
  crashes 3 out of 3 times, even on Vanara 5.0.6. So the file menu now
  calls the shell API directly (`SHParseDisplayName` → `SHBindToParent` →
  `GetUIObjectOf`); the menu reader, icon converter, and folder-background
  path are unchanged. Afterward: all four scenarios 10/10, and zero
  crashes out of 200 menu opens.
- **Shutting down the shell thread killed the process.** `StaWorker.Dispose()`
  disposed the work queue while the pump thread was still inside the
  `GetConsumingEnumerable()` loop; the resulting
  `ObjectDisposedException` escaped outside the `foreach`, so it took the
  process down unhandled (`0xE0434352`). Typically happened when a shared
  thread got disposed due to a timeout. MEASURED: 3 out of 200 menu opens
  died this way; zero after the fix. The queue is now released by the
  thread that reads it.
- **Double-free in the right-click menu.** The shell menu's `ShellItem`s
  used to be released twice in one place, and not at all in another — the
  latter left them to the GC's finalizer thread (MTA), which is also
  memory corruption for an apartment-bound COM object. The correct release
  order is documented in the code, with a table.
- **Silent uninstall deleted the user's settings.** MEASURED: a
  `/VERYSILENT` uninstall removed the `%APPDATA%\Pilaster` folder, even
  though the default is to keep it. The silent branch no longer depends on
  the confirmation dialog's default: it only deletes on the explicit
  `/DELETESETTINGS=1` switch. An interactive uninstall still asks, with
  "No" as the default.
- **Three "dead" settings are now six**: extension display, the system
  files toggle (independent of hidden items), and density (row height and
  margin in the file list, Quick Access, and Settings). All three apply
  instantly, with no restart needed, on both panes and every open tab.
  Rename still edits the FULL name, so the extension is never lost even
  when it's not displayed.

### Known limitations

These are **not bugs** — they're deliberately deferred to v1.1.

- **Process isolation for the shell menu.** We're protected against
  exceptions, hangs, and a broken menu tree, and the heap corruption that
  blocked v1.0 is gone too (see Fixes). A native access violation in an
  extension's code, though, can still take down the process — only a
  separate PROCESS would protect against that. As a mitigation, v1.0
  writes a crash flag around the query, and around warmup too: if the next
  startup finds a stuck flag, extensions are LEFT OUT, a line at the top
  of the menu names the culprit path, and there's a "Re-enable extensions"
  button under Settings → Right-click menu. `ShellMenuSession`'s interface
  stays IPC-compatible, so a helper-process approach can be introduced
  backward-compatibly later — that's a **v1.1** task.
- **Slow shell extensions.** MEASURED: of the file menu's ~780 ms steady
  state, 650–790 ms belongs to a SINGLE handler (NVIDIA's `NvAppShExt`,
  `nv3dappshext.dll`), and it's the only one that doesn't warm up. At
  `Debug` log level, the app lists the 5 slowest handlers and names
  anything over 400 ms, but **doesn't disable anything on its own** — that
  decision belongs to Settings → Right-click menu → Disabled extensions.
- **Code signing.** The `signtool` hook has a place in the build script,
  but there's no certificate yet.
- **Custom keymap editor UI.** The `Custom` preset and its storage field
  (`CustomKeyBindings`) exist; bindings can currently only be set by hand,
  in `settings.json`. The preset picker and the "View keymap" table work.
- **Large files' memory footprint in the editor.** MEASURED: opening a
  122.7 MB log file takes 4.6s and holds 701 MB of managed memory (871 MB
  working set). The file correctly opens read-only, scrolling and search
  are fast (807 ms and 161 ms respectively), but memory use is ~5.7× the
  file size — v1.1 may reduce this with memory-mapped loading. v1.0
  eliminated the freeze during loading (see Fixes), but handing the
  document to the view is still a one-time 196–1343 ms pause.
- **The taskbar icon hasn't been verified on a clean profile.** The
  `AppUserModelID`, the multi-resolution `.ico`, and the shortcut property
  are correct on the dev machine, but verification on a FRESH Windows
  user profile with no icon cache is still pending — that needs a new
  profile to be created. This should be checked manually before release.
- **Some new settings don't affect everything yet.** Density, system
  files, and the extension-display toggle save correctly, but the file
  list's rendering doesn't read them yet.
