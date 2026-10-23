# Mount Sheet Sync

Keep track of mount farming across all your characters: which bosses each character has already killed this week, which mounts you already own, and what is still left to farm.

## What the addon does

- **Records boss kills** of raids, dungeons and world bosses for every character, with difficulty, as they happen. Bosses you killed before installing the addon can be picked up from your instance lockouts with one click.
- **Remembers your characters and your mount collection**, and notices new mounts the moment you learn them.
- **"Remaining" tab** (with the companion program, see below): what is still left this week for the current character — bosses not killed yet whose mount you do not own. Right-click a row to say you are not farming it on this character.
- **Window, chat commands and a minimap / Data Broker button** (for Data Broker displays such as Titan Panel); `/mss` opens the window, `/mss help` lists the commands.
- **Six languages:** English, Russian, German, French, Spanish and Portuguese — follows your game language, switchable in the settings (right-click the button).

## Google Sheets (optional companion program)

The addon is built to work with a free, open-source Windows tray program from the same project. The program reads what the addon recorded, creates a spreadsheet in **your own** Google Drive and marks kills and collected mounts there automatically — and sends the "Remaining" list back into the game.

- The spreadsheet can be in English, Russian, German, French, Spanish or Portuguese; mount, boss and instance names come from your game when it runs in that language.
- The program asks Google only for access to the files it creates itself (`drive.file`).
- It runs on your computer and talks to Google directly: no server in between, no tracking.
- The addon itself never sends anything anywhere — WoW addons cannot use the network.

Without the program, the addon still records kills, characters and mounts in its log in the game; the "Remaining" list and the spreadsheet need the program.

## Getting started

1. Install the addon and log in. Kill a boss, or press "Find in lockouts" in the `/mss` window.
2. For the spreadsheet: get the Windows program from the project website (link below), sign in with Google and press "Create spreadsheet…".

## Privacy and source

The full source code (addon and program) is public under the MIT license. Privacy policy and the program: see the project website.

---

Project website: https://zlodyaga.github.io/ · Source code: https://github.com/Zlodyaga/mount-sheet-sync · Bugs and ideas: GitHub Issues or the comments here.
