# Mount Sheet Sync

[Русский](README.ru.md) · [Home page](https://zlodyaga.github.io/) · [Privacy policy](https://zlodyaga.github.io/privacy.html)

A World of Warcraft addon and a small Windows tray program that keep a Google Sheet of mount farming up to date for you:
which bosses each character has killed this week, which mounts you already own, and what is still left to farm.

- **The addon** (WoW Retail, Midnight 12.x) records boss kills, your characters and your mount collection, and shows
  the “Remaining” list of every character from the sheet right in the game. After a kill that could drop a mount you still
  need it says which attempt that was and the chance over all attempts; the boss tooltip says the same. Attempts from
  before the addon come from the game's own kill statistics.
- **The program** (Windows 10/11, one file, no administrator rights) watches the addon's saved data, creates the spreadsheet in your
  own Google Drive and marks kills and collected mounts there. It also brings the “Remaining” list back into the game.

```
WoW addon ──(SavedVariables file)──► tray program ──(Google Sheets API)──► your spreadsheet
     ▲                                     │
     └──────────(SheetData.lua: what is left this week)──┘
```

The addon and the program speak English, Russian, German, French, Spanish and Portuguese (the addon follows the game
language, the program follows Windows; both can be switched). The spreadsheet can be in any of these languages
too — you choose when you create it (the game's language by default). Mount, boss and instance names then come
from your game when it runs in that language; farming tips are in English for German, French, Spanish and
Portuguese spreadsheets.

## Screenshots

| “Remaining” this week | Log | Settings |
| --- | --- | --- |
| ![Remaining](docs/screens/addon-remaining.png) | ![Log](docs/screens/addon-log.png) | ![Settings](docs/screens/addon-settings.png) |

The addon window (Russian game client, sample characters). Drawn from the addon's real layout — the same frames,
sizes and texts it creates in the game — with `tools/screens/render.py`; the in-game look follows your UI.

| The tray program | Its settings |
| --- | --- |
| ![Tray menu](docs/screens/program-menu.png) | ![Settings](docs/screens/program-settings.png) |

The spreadsheet — the top of a character's raid sheet:

![Spreadsheet](docs/screens/sheet.png)

The program's own texts and menu, drawn in a Windows 11 style, and the sheet as the program creates it, with a few
sample marks (`tools/screens/program.py`).

## Install

1. **Addon:** install *Mount Sheet Sync* from CurseForge, or copy the `MountSheetSync` folder from the full release
   archive (`MountSheetSync-<version>.zip` in [Releases](../../releases)) to `World of Warcraft\_retail_\Interface\AddOns`.
2. **Program:** download
   [MountSheetSync.exe](https://github.com/Zlodyaga/mount-sheet-sync/releases/latest/download/MountSheetSync.exe) and
   run it. Windows SmartScreen may warn about an unknown publisher: “More info” → “Run anyway” (the program is not
   code-signed; its full source is in this repository). On the first start it offers to install itself: it copies
   itself to `%LOCALAPPDATA%\Programs\MountSheetSync` (no administrator rights), adds a Start menu shortcut and appears
   in Windows' “Installed apps”, where it can be uninstalled. “No” runs it from where it is. The program from the full
   archive (`2-program\MountSheetSync.exe`) works the same way.
3. In the settings window: **Sign in with Google…** → **Create spreadsheet…** → pick the spreadsheet language and tick
   your characters. Done — log in to
   the game, kill a boss, go back to character selection, and the row turns “Yes”.

The full step-by-step guide (in Russian) is in [docs/README.ru.txt](docs/README.ru.txt); it is also `README.txt` in the
release archive.

## Privacy

Full text: [privacy policy](https://zlodyaga.github.io/privacy.html).


- The program asks Google only for `drive.file` (plus your e-mail address to show which account is signed in).
  With `drive.file` it can open **only the spreadsheets it created itself** — nothing else on your Google Drive.
- Everything runs on your computer. There is no server of ours in between: the program talks to Google directly.
- Once a day it asks GitHub whether a new version is out (icon menu → “Check for updates” turns that off).
- The sign-in key is stored in `%APPDATA%\MountSheetSync\google-token.dat`, encrypted by Windows (DPAPI) for your
  Windows user only. “Sign out” in the settings revokes it.
- The addon never sends anything anywhere: WoW addons cannot use the network. It only writes its SavedVariables file.

## Older way: your own sheet with Apps Script

Versions 1.x worked with a spreadsheet of your own plus the Apps Script in [script/Code.gs](script/Code.gs).
That still works (“Your own spreadsheet with the Code.gs script” in the settings), see the Russian guide.

## Building

How everything works inside (addon, program, sheet, releases, tests, decisions), in Russian: [docs/reference](docs/reference/README.md).


- **Program:** on Windows run `program\build.cmd`. It uses the C# compiler that ships with Windows (.NET Framework 4.8),
  nothing to install. The code is C# 5 for that reason.
- **Addon:** no build step; the repository root *is* the addon (`MountSheetSync.toc`).
- **Release archive:** `tools/package.sh <MountSheetSync.exe>` → `dist/MountSheetSync-<version>.zip`.
- **Translations of the program:** texts in the code are the Russian originals and serve as keys
  (`Tr._("…")`). Translations live in `tools/i18n/ui*.txt` (all languages) and `tools/i18n/log.txt`
  (English for the activity log); `python3 tools/i18n/build.py` checks them and writes `program/i18n.json`.
- **Translations of the spreadsheet:** `tools/i18n/sheet_words.txt` (sheet names, headers, list values, summary —
  all languages), `sheet_content.txt` (catalog texts → English) and `mounts_en.txt` (English mount names, from
  SimpleArmory); `python3 tools/i18n/build_sheet.py` checks them and writes `program/sheet-i18n.json`. The
  catalog's boss and instance IDs come from the BigWigs / LittleWigs boss modules: `tools/catalog/encounter_ids.py`.

## Tests

`tools/run_tests.sh` (Linux) runs everything: the addon in a mock WoW API (Lua 5.1 via `lupa`), the program under
mono against a mock Google server, `Code.gs` in Node, and UI walk-throughs under Xvfb. The test build is compiled with
`-define:TESTS`; the test hooks (mock Google address, browser log, forced language) are not in release builds.

Needs: `mono-devel`, `python3` with `lupa`, `nodejs`, and for the UI tests `xvfb xdotool stalonetray imagemagick`.

## License

[MIT](LICENSE). Bundled libraries: LibStub and LibDataBroker-1.1 (public domain), CallbackHandler-1.0 (BSD-style Ace3
license, see `Libs/CallbackHandler-1.0/LICENSE.txt`).

World of Warcraft is a trademark of Blizzard Entertainment. Google Sheets and Google Drive are trademarks of Google LLC.
This project is not affiliated with either.
