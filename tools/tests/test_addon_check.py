"""Mount Sheet Sync 3.0.8: the in-game self-check (/mss check, Check.lua) — every part reports a line, events seen this
session are counted (a boss fight with its id and difficulty), secret values are not printed, a missing game API is
reported with [!!] instead of an error, and the window shows the text with the bug report under it.

    python3 test_addon_check.py
"""
from addon_env import start

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check

L.execute("""
MountSheetSyncDB.journal = { instances = { { id = 758, name = "Цитадель Ледяной Короны", raid = true, map = 631 } },
                             encounters = { { id = 1106, j = 1636, name = "Король-лич", i = 758 } } }
MOCK.inst = { name = "Цитадель Ледяной Короны", type = "raid", diff = 6, max = 25, id = 631 }
""")

text = str(ns.SelfCheck())
lines = text.split("\n")
check("the self-check starts with its title", lines[0] == "Mount Sheet Sync self-check (/mss check)", lines[0])
for name in ("mount journal", "place", "journal", "runs", "lockouts", "statistics", "holidays", "rarity", "chat", "quests",
             "tooltips", "world map", "mapzeroth", "hide", "event ENCOUNTER_END"):
    check("a line for «%s»" % name, any(("] %s:" % name) in l for l in lines), [l for l in lines if name in l][:2])
check("the place: the instance from the journal by its map", any(l.startswith("[OK] place:") and "journal instance 758" in l for l in lines),
      [l for l in lines if "place" in l])
check("no fight yet: «not seen this session»", any("event ENCOUNTER_END: not seen this session" in l for l in lines))
check("the bug report goes under it", "Mount Sheet Sync bug report" in text)

# a boss fight: counted, with its id and difficulty
g.Fire("ENCOUNTER_END", 1106, "Король-лич", 6, 25, 1)
text = str(ns.SelfCheck())
check("a fight: counted with its id and difficulty", "event ENCOUNTER_END: 1 times" in text and "id 1106, difficulty 6" in text,
      [l for l in text.split("\n") if "ENCOUNTER_END" in l])

# secret values are not printed
L.execute("MOCK.inst = { name = SECRET, type = SECRET, diff = SECRET, max = 0, id = SECRET }")
g.Fire("ENCOUNTER_END", SECRET := L.eval("SECRET"), "x", SECRET, 25, SECRET)
text = str(ns.SelfCheck())
place = [l for l in text.split("\n") if "] place:" in l]
check("secret place: «secret», not the value", place and "secret, type secret, difficulty secret" in place[0], place)
check("secret fight: «secret»", "id secret, difficulty secret, size 25, success secret" in text,
      [l for l in text.split("\n") if "ENCOUNTER_END" in l])
L.execute('MOCK.inst = { name = "Даларан", type = "none", diff = 0, max = 0, id = 1 }')

# a game API missing: [!!], not an error
L.execute("MSS_SAVED_STAT = GetStatistic; GetStatistic = nil")
text = str(ns.SelfCheck())
check("no GetStatistic: [!!] with what breaks", "[!!] statistics: GetStatistic missing" in text)
L.execute("GetStatistic = MSS_SAVED_STAT")
L.execute("MSS_SAVED_RW = MSS_NS.RunsWhere; MSS_NS.RunsWhere = function() error('boom') end")
text = str(ns.SelfCheck())
check("a part that fails: [!!] error, the rest still reported", "[!!] place: error" in text and "] lockouts:" in text)
L.execute("MSS_NS.RunsWhere = MSS_SAVED_RW")

# /mss check: the window with the text selected; the title says what it is; /mss help lists it
g.SlashCmdList["MOUNTSHEETSYNC"]("check")
g.RunTimers()
rep = g.MountSheetSyncReportFrame
boxes = [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1)]
boxes = [r for r in boxes if r._type == "EditBox" and r._multiline and r._highlighted]
shown = str(boxes[0]._text) if boxes else ""
check("/mss check: the window, text selected for Ctrl+C", rep is not None and rep._shown and boxes and boxes[0]._highlighted
      and shown.startswith("Mount Sheet Sync self-check"), shown[:80])
title = str(rep.titleText.GetText(rep.titleText))
check("…its own title", title == str(ns.L.check_title), title)
rep.Hide(rep)
g.SlashCmdList["MOUNTSHEETSYNC"]("help")
check("/mss help lists /mss check", "/mss check" in env.chat())
env.finish()
