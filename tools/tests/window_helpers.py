"""Looking at the addon's window inside the WoW imitation: its regions, the two ScrollBox lists, the rows as text.
Shared by the addon tests that start from addon_env.start() (test_addon_left.py has its own copy of the same helpers).

    from window_helpers import Window
    w = Window(env)          # opens the window
    w.tab(1); w.shown_rows()
"""
import re

ROW, CHAR = 22, 24


def plain(text):
    return re.sub(r"\|c[0-9a-fA-F]{8}|\|r", "", str(text))


class Window(object):
    def __init__(self, env):
        self.env, self.L, self.g = env, env.L, env.g
        if not (self.g.MountSheetSyncFrame and self.g.MountSheetSyncFrame._shown):
            self.L.execute("MSS_NS.UI.Toggle()")
            self.g.RunTimers()
        self.win = self.g.MountSheetSyncFrame
        self.tabs = [x for x in self.regions("Button") if x._isTab]

    def regions(self, kind=None):
        g = self.g
        return [g.ALL_REGIONS[i] for i in range(1, len(g.ALL_REGIONS) + 1) if kind is None or g.ALL_REGIONS[i]._type == kind]

    def under_window(self, x):
        p = x._parent
        while p is not None:
            if p._uid == self.win._uid:
                return True
            p = p._parent
        return False

    def boxes(self, extent):
        return [x for x in self.regions("Frame") if x._template == "WowScrollBoxList" and self.under_window(x) and x._view._extent == extent]

    def pool(self, extent):
        box = self.boxes(extent)[0]
        return [box._pool[i] for i in range(1, len(box._pool) + 1) if box._pool[i]._shown]

    def row_text(self, f):
        if f.elem.cols is None:
            return str(f.text._text)
        cells = [f.cells[i] for i in range(1, len(f.cells) + 1)]
        return " | ".join(str(c._text) for c in cells if c._shown)

    def shown_rows(self):
        win = self.win
        head = [str(win.summaryText._text)] if win.summaryText._shown and win.summaryText._text else []
        return head + [self.row_text(f) for f in self.pool(ROW)]

    def char_rows(self):
        return [(plain(f.name._text), str(f.count._text), bool(f.sel._shown)) for f in self.pool(CHAR)]

    def click_all(self):
        row = self.pool(CHAR)[0]
        row.MockClick(row)
        self.g.RunTimers()

    def tab(self, i):
        t = self.tabs[i]
        t.MockClick(t)
        self.g.RunTimers()
