"""«Не фармлю» from the «Осталось» tab (right click on a row): the menu, the «plan» record, the row hidden at once, the whole
place, per character, dropped when a newer list comes, shown in the journal. Moved out of test_addon_left.py (16.11.2026).

    python3 test_addon_plan_menu.py
"""
from addon_env import start
from window_helpers import Window, ROW

env = start()
L, g, ns, check = env.L, env.g, env.ns, env.check
w = Window(env)
w.tab(0)
NOW = g.MOCK.now


def remaining():
    return ns.GetRemaining()


def row_buttons():
    """Rows of the main list that have the right-click menu (in list order)."""
    return [f for f in w.pool(ROW) if f.elem.entry is not None]


# another character's list before anything is hidden here
g.MOCK.name = "Элтара"; g.MOCK.guid = "Player-1602-0E1715"
eltara_total = remaining().total
g.MOCK.name = "Эйра"; g.MOCK.guid = "Player-1602-0ABCDEF1"
rb = row_buttons()
texts = [w.row_text(b) for b in rb]
check("right-click buttons only on raid/dungeon rows that are left (not headers, world bosses or ticked rows)",
      len(rb) > 10 and all("убит" not in t for t in texts)
      and not any("Галлеон" in t or "Налак" in t for t in texts), texts[:5])
left0 = remaining().left
first = None
for i in range(1, len(remaining()["items"]) + 1):
    it = remaining()["items"][i]
    if not it.done and str(it.item.kind) in ("raid", "dungeon") and not it.item.sep:
        first = it.item
        break
boss0, place0 = str(first.boss), str(first.place)
btn = [b for b in rb if w.row_text(b).startswith(boss0)][0]
n_kills = len(g.MountSheetSyncDB.kills)
btn.MockClick(btn, "RightButton")
menu = g.MENUS[len(g.MENUS)]
items = [(str(menu["items"][i].kind), str(menu["items"][i].text)) for i in range(1, len(menu["items"]) + 1)]
check("menu: title + «не фармлю эту строку» + «весь …»", items == [("title", boss0),
      ("button", "Не фармлю эту строку на этом персонаже"), ("button", "Не фармлю весь «%s» на этом персонаже" % place0)], items)
L.eval("function(b) b.cb() end")(menu["items"][2])
rec = dict(g.MountSheetSyncDB.kills[n_kills + 1].items())
check("record «plan»: row scope, the row's boss, mount, difficulty, place, list", rec["kind"] == "plan" and rec["scope"] == "row"
      and rec["boss"] == boss0 and rec["list"] == str(first.kind) and rec.get("mount") == (str(first.mount) or None)
      and rec["place"] == place0 and rec["farm"] is False and rec["char"] == "Эйра"
      and rec["id"].startswith("P-Player-1602-0ABCDEF1-") and "weekEnd" not in rec, rec)
r = remaining()
check("the row is gone from «Осталось» right away", r.left == left0 - 1
      and not any(str(r["items"][i].item.boss) == boss0 and str(r["items"][i].item.mount) == str(first.mount)
                  for i in range(1, len(r["items"]) + 1)), r.left)
check("chat explains and says how to undo", "не фармлю на Эйра" in env.chat() and "«Что фармить…»" in env.chat(), env.chat().split("\n")[-1])
g.RunTimers()
check("window refreshed, tab title updated", not any(t.startswith(boss0) for t in w.shown_rows())
      and w.tabs[0]._text == "Осталось (%d)" % (left0 - 1), w.tabs[0]._text)
# whole place
rb = row_buttons()
target_place = None
for i in range(1, len(remaining()["items"]) + 1):
    it = remaining()["items"][i]
    if not it.done and str(it.item.kind) == "dungeon" and str(it.item.place) != place0:
        target_place = str(it.item.place)
        break
dbtn = [b for b in rb if target_place and target_place in w.row_text(b)][0]
dbtn.MockClick(dbtn, "RightButton")
menu = g.MENUS[len(g.MENUS)]
L.eval("function(b) b.cb() end")(menu["items"][3])
r = remaining()
check("«весь …» hides every row of that dungeon", not any(str(r["items"][i].item.place) == target_place for i in range(1, len(r["items"]) + 1))
      and dict(g.MountSheetSyncDB.kills[len(g.MountSheetSyncDB.kills)].items())["scope"] == "place", target_place)
hidden = g.MountSheetSyncDB.notFarmed["Player-1602-0ABCDEF1"]
check("pending «не фармлю» kept per character until the sheet's list is newer", len(hidden) == 2)
g.MOCK.name = "Элтара"; g.MOCK.guid = "Player-1602-0E1715"
check("another character is not affected", remaining().total == eltara_total, (remaining().total, eltara_total))
g.MOCK.name = "Эйра"; g.MOCK.guid = "Player-1602-0ABCDEF1"
saved_generated = g.MountSheetSyncSheet.generated
g.MountSheetSyncSheet.generated = NOW + 3600   # the program sent it and brought a newer list
r = remaining()
check("newer list from the sheet: local hiding dropped (the sheet decides now)", g.MountSheetSyncDB.notFarmed["Player-1602-0ABCDEF1"] is None
      and any(str(r["items"][i].item.boss) == boss0 for i in range(1, len(r["items"]) + 1)))
g.MountSheetSyncSheet.generated = saved_generated
w.tab(3)
check("journal shows «Не фармлю» in grey", any("|cff9d9d9dНе фармлю: " + boss0 in t for t in w.shown_rows()), [t for t in w.shown_rows() if "фармлю" in t])
check("world rows have no «не фармлю» menu", ns.AddPlan(L.table_from({"kind": "world", "boss": "Галлеон"}), "row") is False)
env.finish()
