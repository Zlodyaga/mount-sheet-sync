# Mount Sheet Sync — addon changelog

The addon and the Windows program are released separately; the program's changes are in CHANGELOG.md on GitHub.

## 3.0.5
- Two new groups in “Remaining”: rare monsters with a guaranteed drop and common mob farming. They are not counted in what is left this week and are not in the “All characters” grid.
- Entrances closed by a story chain or a zone phase (Antorus on Argus, Operation: Mechagon, Ny'alotha, Amirdrassil, Tazavesh, Sepulcher of the First Ones, Manaforge Omega): Ctrl + click first says what to do and leads to the chain's next quest (tracks it when it is in your log, otherwise pins where it is given); if the chain is not done on this character there is no pin to the entrance, and a second Ctrl + click pins it anyway. Access the whole account shares (the Emerald Dream) is remembered once any character has it.
- Ctrl + click on a rare monster's row pins where it appears; for a rare with several spawn points the map of its zone opens.
- A rare is also recognised by the creature's number: each of the three Zandalari Warbringers counts for its own direhorn, and any rare of a Midnight zone counts for that zone's mounts, even when its name is unknown. Needs the program 2.10.1.
- Ctrl + click pins the entrance of every raid and dungeon in the list on the map when the game shows no entrance icon (a character who has never been there, newer raids), not only of a few places.

## 3.0.4
- Achievement requirements, rare monsters, chests and zones follow the addon's language when it differs from the game's.
- Ctrl + click now also works where the game shows no entrance icon or has no map for the boss: Freehold, Operation: Mechagon, Return to Karazhan, the Stratholme service entrance and Dunegorger Kraulok get a map pin at the known spot.
- Achievements and rewards have a tab of their own instead of a group in “Remaining”, in four tables: raids, dungeons, Mythic+ and other. Every achievement with a mount you can still get is there, the raid “Glory of…” ones too; past Mythic+ seasons' mounts appear as the Timelost Saddle with this season's requirement as the game words it: the full name, the requirement as the game words it and the mount; what is earned turns grey and goes to the bottom. Left-click the achievement to open it in the game's achievements window, the mount to open the Mount Journal. Needs the program 2.10.0.
- Mount, boss and place names follow the addon's language in German, French, Spanish and Portuguese too, not only English and Russian.
- Ctrl + click on a raid whose entrance is inside another zone (Dragon Soul in the Caverns of Time) leads to the Caverns of Time in Tanaris, and once you are inside, on to the entrance by itself. If you pick another target meanwhile, the addon leaves it alone. Entrances are looked for on every map, and by name too, so more dungeons are found.
- The offline line at the bottom of the window stands out: a warning icon and bold orange text. The window sits on whole screen pixels, buttons, tabs and headings use the same plain font as the rest (the decorative one blurred in game), and the bottom line is a size larger, so the letters are crisper.
- All characters: a character who doesn't farm a row (no tick in “Farming”, or no such group) now gets a faint cross instead of the same dash as a kill this week. The row tooltip names who killed it this week and who doesn't farm it.
- Mount, boss and place names follow the addon's language, not the sheet's. In the game's language they come from the game itself; in another one from translations the program sends (English and Russian; other languages get English). Needs the program 2.10.0.
- Without a Google sheet the addon talks about the list: the button is “Save marks”, and no text mentions a sheet. With a sheet everything stays as it was, including “Send to sheet”.
- Achievements and rewards (Glory of the Hero, Keystone Master and the like) have a group of their own at the end of the table and no longer count as left to kill, because they aren't boss kills. Needs the program 2.10.0.
- Clicks on a row, all named in its tooltip. Left-click the boss: the raid or dungeon in the Adventure Guide. Left-click the mount: the game's Mount Journal. Ctrl + left-click: the way to the entrance, or a map pin at the entrance when the game can't track it; for a world boss, a pin where the game shows it. The window steps aside and comes back when the game's window closes.
- Mount icons on every row of “Remaining”, including achievements and guaranteed drops. Needs the program 2.10.0; with an older one those rows stay without an icon.
- With one character there is no “All characters” row: it opens straight on that character. The all-characters table shows rows and attempts side by side.
- The row tooltip stands beside the table, so it covers no rows. The boss column keeps just the boss; what follows it is in the tooltip.
- The difficulty column is short and readable: “N10”, “Torg.”, “Ach.” and the like, with the full text in the tooltip. No word in it is cut off in any language.
- The tooltip about the program no longer says that a Google Sheet is required.

## 3.0.3
- Raids with separate lockouts (Normal/Heroic/Mythic rows): each difficulty now counts on its own. The letters N H M show what is still left, in "Remaining" and in the all-characters table. Needs the program 2.9.2.
- A new look: the windows have their own dark style with gold accents, fonts (PT Sans, Philosopher) and round selectors.
- The window is wider (1060x600) and its lists are real tables: mount icons, columns for boss, difficulty, mount, attempts
  and chance, full names in the tooltip. Numbers no longer get cut off by a long name.
- "Attempts": click a column title to sort, filter by raids, dungeons, world bosses and rares, hide mounts without
  attempts, and see the chance as a bar.
- "All characters" is now the first row of the list on the left: a table with a column per character. With many
  characters the columns scroll; "All columns at once" in the settings shows them all without scrolling.
- "Mark by hand" and "Search saved instances" moved to the "Log" tab.
- “Rare monsters” is a group of its own in “Remaining” and “All characters”. A rare killed by this character shows when it can
  be tried again (Ishak — after the daily reset), and comes back by itself. Needs the program 2.9.2 for the new sheet.
- “Weekly reset” moved to the settings: the program 2.9.2 does it by itself.
- Rares (Ishak of the Four Winds and the like): the attempt no longer depends on what is targeted when the loot window
  opens. The addon remembers rares you targeted, hovered or saw a nameplate of, and a corpse whose name it did not know
  still counts if you open it again.

## 3.0.2
- Kills are now recognised by the game's encounter ID from the sheet's list, not only by the boss name: the game
  calls Galleon «Галеон» while the sheet said «Галлеон», so in the Russian client the attempt line in chat did not show
  (the sheet counted it). Needs the program 2.9.1 — the window says so while an older one runs.

## 3.0.1
- The separator dot “·” and the arrow “→” showed as a box in the Russian game client (its font has no such glyphs):
  they are now a plain “-” and “>”.

## 3.0.0
- The mount hint when you hover a unit is now only for rares (Ishak of the Four Winds and the like). Inside raids and
  dungeons the game hides NPC names from addons even before the pull, so on bosses it could not work; raid and
  dungeon bosses have the Adventure Guide icon instead. `/mss tip` writes to chat what the addon sees when you hover
  a unit — to check it in the game.
- Adventure Guide: the horseshoe icon is there already on the raid or dungeon page, before you pick a boss, with the
  mounts of all its bosses; on a raid without such mounts it goes away instead of keeping the previous raid's hint.
- New window. On the left — every character from the sheet in class colour with how much is left this week (a tick
  when everything is done); click one to see its “Remaining” list. The window now opens on “Remaining”.
- “All characters” tab: what is still to kill this week across all characters, one line per boss, with who still
  needs it (“Eira, Mirna, Kaelia and 12 more”; every name in the tooltip).
- “Attempts” tab: every uncollected mount with a drop chance — attempts and chance so far, most expected drops first;
  the tooltip says on how many characters the boss is still alive this week.
- Lighter with many characters: the lists only create frames for the rows you see and are rebuilt only when their
  data changes, so opening the window or switching tabs does not stutter even with 20+ characters and a full log.
- “No chat messages” and “Clear log” moved to the settings (the gear button in the window or right-click on the icon).

## 2.9.1
- Lighter: the boss tooltip remembers its answer per boss until something changes (a kill, a new mount, the weekly
  reset), so hovering a boss no longer re-reads the whole kill journal on every tooltip refresh (about 150× less work
  in a test with real data). The mount journal read after login no longer stores the source text of every mount —
  the program never used it, and it was half of the saved file — so that read takes about 5× less time.

## 2.9.0
- Attempts from before the addon: at login the addon reads the game's kill statistics (Achievements → Statistics) of the
  bosses whose mounts the sheet counts; the program 2.9.0 adds them up over all characters and raises “Attempts” in
  the sheet. Log in once with each character to bring in its kills.
- Boss hints are shorter and no longer cover the Adventure Guide: the boss page gets a small horseshoe icon under the
  tabs on the right (hidden on the Model tab), and its tooltip has the lines — “Not killed this week on 6 characters”, then per mount
  “12 attempts, chance 11%” (“no attempts yet” at 0). The boss tooltip in the world uses the same lines.

## 2.8.0
- Attempt counter (with the program's spreadsheet): after a kill that could drop a mount you still need, the chat says
  “<mount> could drop: attempt 37 · chance over all attempts 31%” (kills not sent yet are counted too).
- Boss tooltip and the boss page of the Adventure Guide: attempts, chance and on how many characters the boss is still
  alive this week.
- Rares count too (Ishak of the Four Winds): recorded when you loot the corpse, if its mount is on the World Bosses
  sheet and not collected yet. A kill whose name the game hides is found in the lockouts a few seconds later and also
  gets its attempt line.
- Settings (right click) → “Bug report info…”: versions and state as text to copy into a bug report (no character
  names, realm or spreadsheet).
- The program and the addon are now released separately: a grey line in the window says when the program needs a
  newer addon (or the addon a newer program).
