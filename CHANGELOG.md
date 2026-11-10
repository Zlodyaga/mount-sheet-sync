# Changelog

The tray program (GitHub releases). The addon has its own list from 2.8.0 on: [CHANGELOG-addon.md](CHANGELOG-addon.md).

## 2.10.1
- 82 more rare-monster mounts on the “Rare monsters” sheet, from Wrath of the Lich King to Midnight (13 of them a sure drop): drop chance, loot cooldown, and the zone and map point in the farming tip; names in all six languages. Sheets made earlier get the new rows at the end of that sheet by themselves.
- Rares the addon can't tell by name are told by the creature's number: the three Zandalari Warbringers (№1–№3, each with its own direhorn) and “Any rare: <zone>” rows of Midnight. Rows with several rares have “No cooldown”. Needs the addon 3.0.5.
- The drop chance on the rare sheet shows tenths (0.3%, not 0%).
- Raid drops with the status “Current content” and “Farmable / needs content grouping” now go to the character's raid sheet and to “Remaining” too; sheets made earlier get these rows by themselves.
- Two more groups on the rare sheet: rares with a guaranteed drop (the Time-Lost Proto-Drake, Aeonaxx, Nok-Karosh, Gorok, Poundfist, Alani) and common mob farming in Battle for Azeroth zones (about 1 in 3000, every kill counts); plus any Forbidden Reach rare and any Theater of Pain champion.

## 2.10.0
- Achievement requirements, rares, chests, zones and general rows now come in all six languages too.
- The list for the addon carries names in all six languages, taken from the game's own tables, and every reward row of the sheet with its achievement, for the addon's “Achievements” tab.
- A character removed and added back gets this week's kills marked again on the next pass, without counting attempts twice. Before, their rows stayed “killed, waiting for an update” in the addon.
- Choosing “On this computer” with a spreadsheet already in place now asks whether to move its data over, so marks and “not farming” aren't lost. “Move from the spreadsheet…” also works over data already created here: the old data is kept next to it, and kills marked only here are marked in the copy again.
- The list for the addon carries English and Russian names of its mounts and bosses, so the addon can show them in its own language (needs the addon 3.0.4).
- The program no longer needs Google: new players start with the data in a file on this computer, with no sign-in and nothing to set up. It appears by itself as soon as the addon knows your characters, and the addon's window shows the list.
- A Google Sheet is still there as an option in the settings, and players who already have one stay on it. “Move from the spreadsheet…” copies an existing sheet to this computer without counting kills twice; the sheet stays in Google as it is.
- “Characters…” and “What to farm…” work with the data on this computer too. The menu, notifications and the “Characters…” window mention the spreadsheet only if there is one.
- With the data on this computer, new characters join by themselves when you log in on them, and their kills count from the start. A character you remove in “Characters…” stays removed until you tick them again.
- The list tells the addon which rows are achievements and rewards rather than boss kills, and where each row is in the Adventure Guide (needs the addon 3.0.4). The tray menu doesn't count achievements and rewards as left to kill.
- A spreadsheet with one character no longer gets a phantom character named after its “Attempts left” column.
- Every row sent to the addon carries its mount's ID, so achievements and guaranteed drops get an icon too (needs the addon 3.0.4).

## 2.9.2
- Raids with separate lockouts: on a Normal/Heroic/Mythic row a kill on one difficulty no longer closes the row. It becomes “Yes” when all three are killed; the killed ones are kept in a hidden “N/H/M” column.
- World boss mounts now reach the addon with their game ID: their icons show in its window, and a collected one leaves its lists.
- The weekly reset happens by itself: when a new game week has started and the sheet has not been reset yet, the program
  sets “No” for every character and marks this week's kills again — at the first update of the “Remaining” list, for
  example when you start the game. The “Weekly reset” button in the addon still works.
- Fewer requests to Google: one pass (send the kills, check the collection, update the list) loads the spreadsheet once
  instead of once per step, so sending kills with the list takes 3 reads instead of 6.
- A smaller list for the addon: SheetData.lua no longer carries the sheet and row of every line (about 15% of the file).
- Rare monsters have their own sheet: Ishak of the Four Winds moves from “World Bosses” to “Rare Monsters” by itself, with
  “Collected?” and “Attempts”; the sheet shows the last kill. A rare has its own cooldown from “Reset / frequency” (Ishak —
  until the daily reset), not the weekly reset. Needs the addon 3.0.3.

## 2.9.1
- The "Remaining" list and the attempt rows the program writes for the addon now carry the encounter IDs of each
  boss, so the addon 3.0.2 recognises a kill even when the game spells the boss differently from the sheet.
- The catalog spells Galleon as the Russian client does: «Галеон» (sheets made before keep «Галлеон»; both work).

## 2.9.0
- Attempts made before you installed the addon now count too. The game keeps kill statistics for every character
  (Achievements → Statistics); the addon 2.9.0 reads them for the bosses of the sheet's random-drop mounts each time a
  character logs in, and “Check against collection” (it also runs by itself after a login) raises “Attempts” to the
  sum over all characters — only upwards, so a bigger number typed by hand stays and kills counted already are not
  counted twice. Mounts you already have get it once, while their “Attempts” is still 0, so you can see what
  Invincible cost you (kills after it dropped are in that number too); after that their number stays. Each character adds its kills once it has logged in with the new
  addon. About 50 mounts have such a statistic; bosses without one of their own (Attumen, Anzu, Skadi, rares such as
  Ishak, trash in Ahn'Qiraj, Torghast…) keep counting from the addon's kills only.
- “Chance over all attempts” showed a fraction (0.0394) instead of 3.9% in rows the sheet's filter hid when 2.8 added
  the counter (collected mounts): the sheet gets the format again, cell by cell, on its own.
- The program needs the addon 2.9.0 for this and says so in the icon menu while an older addon is installed.

## 2.8.0
- Attempt counter: “Attempts” and “Chance over all attempts” next to “Drop chance” on the Raids, Dungeons and World
  Bosses sheets (existing spreadsheets get them on their own, counting from 0; a number typed by hand is continued).
  Every kill with a fitting difficulty of a boss whose mount is not collected yet adds one attempt, all characters
  together; mounts without a random chance (achievements, purchases, guaranteed) have no counter.
- In the game: after such a kill the chat says “<mount> could drop: attempt 37 · chance over all attempts 31%”
  (kills the program has not sent yet are counted too), and the boss tooltip — and the boss page of the Adventure
  Guide — shows the attempts and on how many characters the boss is still alive this week.
- The addon settings (right click) have “Bug report info…”: versions and state of the addon and the program as text to
  copy into a bug report, without character names, realm or spreadsheet.
- The program refreshes the “Remaining” list on its 30-minute schedule only while the game is running (and right when
  it starts), so it asks Google much less often; after sending kills it refreshes as before.
- Rares count too: killing a rare is not an encounter and leaves no lockout (Ishak of the Four Winds), so the addon
  records it when you loot its corpse (if its mount is on the World Bosses sheet and not collected yet) — with the
  attempt line in chat. A kill whose name the game hides is found in the lockouts a few seconds later and also gets
  its attempt line.
- When the CurseForge app updates the addon, it puts back an empty “Remaining” list (`SheetData.lua`); the
  program now notices that within seconds and writes the list again (before, it took up to 10 minutes).
- Test versions: icon menu → “Update to test versions (alpha, beta)” — the program then also offers GitHub
  pre-releases (tags like v2.8.0-alpha1) and updates to them like to a release; untick it to get only releases again.
  A test build shows its full number (2.8.0-alpha1) and moves on to the release with the same number when it is out.
- From now on the program and the addon are released separately, each with its own version: a program update no
  longer means an addon update in CurseForge, and the other way round. If one of them needs a newer other half, it
  says so: the icon menu (and one notification) “update the addon in CurseForge”, or a grey line in the addon window.

## 2.7.0
- The addon says, quietly, when the tray program is not running: a grey line at the bottom of its window (details on
  hover), one grey line in the Data Broker tooltip, and after the first kill of the session “the kill is remembered and
  goes to the sheet once the program starts” instead of the usual hint. Kills are recorded as always. While it runs,
  the program writes `Program.lua` into the addon folder once a minute; the game reads it at login and `/reload`.

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
