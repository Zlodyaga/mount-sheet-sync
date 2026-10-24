# Changelog

## 2.6.0
- The program is also published on its own, as `MountSheetSync.exe` (the home page links to the latest one), and
  installs itself: started from Downloads, the desktop or an archive, it offers to copy itself to
  `%LOCALAPPDATA%\Programs\MountSheetSync` (no administrator rights), adds a Start menu shortcut and registers in
  Windows' “Installed apps”, where it can be uninstalled (with or without its settings). “No” runs it where it is and
  is remembered. `--install`, `--uninstall [--yes]` on the command line.
- The addon says where to get the program when it has nothing to show yet: in the empty “Remaining” tab (with the
  address in a box to copy) and in the `/mss left` reply.

## 2.5.0
- The program updates itself: once a day it looks for a new release on GitHub, says so in a notification and in
  the icon menu (“Update to version…”), and one click downloads it, checks its checksum, replaces the program and
  restarts it. An addon installed from the release archive is updated with it (from 2.5.0 the archive marks such an
  addon); an addon from CurseForge is left to the CurseForge app. “Check for updates” in the icon menu turns the check
  off; `--check-update` and `--update` on the command line.

## 2.4.0
- A new spreadsheet can be in any of the six languages: “Spreadsheet language” in “Create spreadsheet…”
  (`--sheet-lang` on the command line); by default the game's language, otherwise the program's. Sheet names,
  headers, “Yes”/“No”, statuses, drop-down lists, the summary and the formulas are in that language. Mount, boss
  and instance names come from the game when it runs in that language (the addon now also notes the Encounter
  Journal), English otherwise; farming tips and skip notes are in English for German, French, Spanish and
  Portuguese spreadsheets. Existing spreadsheets stay as they are.
- Kills are recognised by the encounter ID as well as by the boss name: a spreadsheet in one language works with a
  game client in another, and bosses the game names a little differently are found too.
- The addon's icon (minimap button, addon list, Data Broker) is now the horseshoe of the tray program.

## 2.3.1
- Google sign-in no longer stalls (and sometimes fails the first time) when the browser opens spare
  connections to the program: every connection is now read on its own.

## 2.3.0
- Addon in six languages (English, Russian, German, French, Spanish, Portuguese): the game language when known,
  English otherwise. Settings window (right-click the minimap/DataBroker/compartment icon, `/mss settings`):
  language, chat messages, minimap button. `/mss lang`.
- Tray program in the same six languages: the Windows language when known, English otherwise; switch in the
  icon menu → “Language”.
- The program says clearly when the addon is not installed, and shows a message instead of failing silently when
  something goes wrong in a window.
- Test hooks are no longer compiled into release builds.

## 2.2.0
- “Yes”/“No” in colour; existing spreadsheets are upgraded automatically.
- “Farming” column on character sheets, “What to farm…” window in the program, and right-click
  “Not farming on this character” in the addon's “Remaining” list.
- “Characters in the spreadsheet…”: add, change or remove characters and their sheets.

## 2.1.0
- Mounts are matched by their in-game ID; names as in the game.
- “Check against collection”: marks “Collected?” for mounts you already own and removes them from character sheets.
- More reliable Google sign-in (“Try again” page when file access was not granted).

## 2.0.0
- The program creates the spreadsheet itself (sign in with Google, `drive.file` access only).
- The addon remembers characters and the mount collection; new mounts are marked automatically.

## 1.x
- Addon + tray program + your own spreadsheet with the Apps Script `Code.gs`.
