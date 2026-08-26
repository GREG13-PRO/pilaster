# Pilaster — brand assets

| File | Size | Used for |
|---|---|---|
| `icon-1024.png` | 1024×1024 | Master source. Everything else is derived from this. |
| `app.ico` | 16–256 | The app icon. 10 sizes embedded: 16, 20, 24, 32, 40, 48, 64, 96, 128, 256. |
| `png/icon-*.png` | 16–256 | Individual sizes, for READMEs and documentation. |
| `wordmark.png` | 932×245 | Wordmark only, transparent background. |
| `lockup.png` | 680×256 | Icon + wordmark side by side. |
| `lockup-512.png` | 1364×512 | Same, at higher resolution. |

## Usage rules

**Never put text inside the icon.** At 16 and 32 pixels — i.e. in the taskbar, file list, and title bar — the "Pilaster" lettering turns into unreadable mush and just looks like a smudge on the folder. That's why the wordmark lives in a separate file.

**Use the wordmark wherever there's horizontal space:** installer header, About dialog, README, splash screen, website. The `lockup.png` is the right choice there, since the proportions and spacing between the two elements are already tuned.

**Safe zone:** the icon artwork fills the middle 86% of the canvas, with ~72px of breathing room around it. Don't crop that away — without it, the artwork gets clipped at the edges in the Windows taskbar and Start menu.

**Background:** every file is a real alpha-channel PNG. The amber color holds up on both light and dark backgrounds; don't put a fill behind it.

## Colors

| Role | Hex |
|---|---|
| Folder (base) | `#E9B843` |
| Folder (deep shadow) | `#E2A61F` |
| Document | `#DCE0EC` |
| Text bar on document | `#6B7280` |
| Wordmark | `#C9844E` |

## Regenerating

If the master source (`icon-1024.png`) changes, `app.ico` and the contents of `png/` are derived from it — regenerate them so the versions don't drift apart.
