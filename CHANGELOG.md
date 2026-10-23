# Changelog

## 2.3.1
- Google sign-in no longer stalls (and sometimes fails the first time) when the browser opens spare
  connections to the program: every connection is now read on its own.
- The addon's icon (minimap button, addon list, Data Broker) is now the horseshoe of the tray program.

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
