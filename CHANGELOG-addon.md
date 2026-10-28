# Mount Sheet Sync — addon changelog

The addon and the Windows program are released separately; the program's changes are in CHANGELOG.md on GitHub.

## 2.9.0
- Attempts from before the addon: at login the addon reads the game's kill statistics (Achievements → Statistics) of the
  bosses whose mounts the sheet counts; the program 2.9.0 adds them up over all characters and raises “Attempts” in
  the sheet. Log in once with each character to bring in its kills.

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
