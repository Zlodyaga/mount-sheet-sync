# Mount Sheet Sync — addon changelog

The addon and the Windows program are released separately; the program's changes are in CHANGELOG.md on GitHub.

## 3.0.0
- Boss tooltip in the world: the name is now also taken from the tooltip's own data when the game does not give it
  directly, and a hidden “is it a player” no longer stops the hint (it did not show on Ultraxion). `/mss tip` writes
  to chat what the addon sees when you hover a boss — to check it in the game.
- Inside raids and dungeons the game hides NPC names even before the pull, so the hint could not tell which boss it
  is: hovering a boss there now shows the mounts of all bosses of that raid or dungeon, by boss.
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
