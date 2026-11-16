"""Mount Sheet Sync 3.0.7: the line in chat on login (how much this character has left this week and where to start, a
setting turns it off) and «до сброса …» in the head of the «Осталось» tab.

    python3 test_addon_login.py
"""
from addon_env import start
from window_helpers import Window

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check

before = len(g.CHAT)
ns.LoginNote()
line = str(g.CHAT[len(g.CHAT)]) if len(g.CHAT) > before else ""
r = ns.GetRemaining()
check("on login: «<character>: осталось N из M на этой неделе - например, <place>. Список - /mss»",
      ("Эйра: осталось %d из %d на этой неделе - например, " % (r.left, r.total)) in line and line.endswith("Список - /mss"), line)
g.MountSheetSyncDB.loginNote = False
before = len(g.CHAT)
ns.LoginNote()
check("…the setting is off: no line", len(g.CHAT) == before)
g.MountSheetSyncDB.loginNote = None
g.MountSheetSyncDB.quiet = True
ns.LoginNote()
check("…«no chat messages»: no line either", len(g.CHAT) == before)
g.MountSheetSyncDB.quiet = None

w = Window(env)
w.tab(0)
head = w.shown_rows()[0]
check("the head of «Осталось» says how long until the weekly reset", " - до сброса 3 д " in head, head)
env.finish()
