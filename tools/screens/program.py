"""Pictures of the tray program and of the spreadsheet for the README.

- program-menu.png / program-settings.png: the program's own texts and menu order (TrayApp.cs, SettingsForm.cs),
  drawn in a Windows 11 style instead of a WinForms screenshot. Sample account, times and counts.
- sheet.png: the top of a character sheet exactly as SheetBuilder creates it (BuilderHost: values, header and band
  colours, "Yes"/"No" colours, the "Farming" ticks), a few sample marks set.

  python3 tools/screens/program.py      (needs tools/cs_host/BuilderHost.exe: tools/cs_host/build.sh)
"""
import html
import json
import os
import subprocess

from playwright.sync_api import sync_playwright

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "docs", "screens")

BASE_CSS = """
:root { --fg:#f3f3f3; --muted:#a7a7a7; --accent:#f2c45a; --menu:#2b2b2b; --line:rgba(255,255,255,.08); }
* { box-sizing: border-box; }
html, body { margin: 0; }
body { font-family: "Inter", sans-serif; -webkit-font-smoothing: antialiased; width: var(--w); height: var(--h);
  position: relative; overflow: hidden;
  background:
    radial-gradient(900px 600px at 20% 25%, rgba(70,110,190,.55), transparent 60%),
    radial-gradient(800px 600px at 85% 75%, rgba(150,80,170,.45), transparent 60%),
    linear-gradient(135deg, #10172c, #1d1830); }
"""

MENU_CSS = """
.taskbar { position:absolute; left:0; right:0; bottom:0; height:48px; background:rgba(28,28,30,.86);
  border-top:1px solid var(--line); backdrop-filter: blur(20px); }
.tray { position:absolute; right:14px; bottom:0; height:48px; display:flex; align-items:center; gap:14px; color:var(--fg); font-size:12px; }
.tray img { width:18px; height:18px; border-radius:4px; outline:2px solid rgba(255,255,255,.18); outline-offset:3px; }
.clock { text-align:right; line-height:1.3; }
.menu { position:absolute; background:rgba(43,43,43,.96); border:1px solid rgba(255,255,255,.1); border-radius:8px;
  padding:4px; box-shadow:0 16px 40px rgba(0,0,0,.5); color:var(--fg); font-size:12.5px; min-width:250px; }
.item { height:30px; display:flex; align-items:center; padding:0 12px 0 34px; border-radius:5px; position:relative; white-space:nowrap; }
.item.dis { color:var(--muted); }
.item.title { color:var(--accent); font-weight:600; }
.item.hot { background:rgba(255,255,255,.08); }
.item .chk { position:absolute; left:12px; font-size:12px; color:var(--fg); }
.item .arr { margin-left:auto; padding-left:24px; color:var(--muted); font-size:11px; }
.sep { height:1px; background:rgba(255,255,255,.09); margin:4px 6px; }
"""

SETTINGS_CSS = """
.win { position:absolute; left:40px; top:36px; width:620px; background:#202020; border:1px solid rgba(255,255,255,.1);
  border-radius:9px; box-shadow:0 24px 60px rgba(0,0,0,.55); color:var(--fg); font-size:12.5px; overflow:hidden; }
.bar { height:36px; display:flex; align-items:center; gap:10px; padding:0 0 0 14px; background:#1c1c1c; }
.bar img { width:16px; height:16px; border-radius:3px; }
.bar .t { font-size:12px; }
.bar .x { margin-left:auto; width:46px; height:36px; display:flex; align-items:center; justify-content:center; color:#ddd; font-size:15px; }
.body { padding:16px 22px 18px; }
.b { font-weight:600; margin-bottom:12px; }
.row { display:flex; align-items:center; gap:9px; margin:7px 0; }
.radio, .cbox { width:18px; height:18px; flex:none; border-radius:50%; border:1.5px solid #9a9a9a; display:flex; align-items:center; justify-content:center; }
.radio.on { border-color:var(--accent); } .radio.on::after { content:""; width:8px; height:8px; border-radius:50%; background:var(--accent); }
.cbox { border-radius:4px; } .cbox.on { background:var(--accent); border-color:var(--accent); color:#1b1406; font-size:12px; font-weight:700; }
.group { margin:8px 0 6px 27px; padding:12px 14px; border-radius:8px; background:#2b2b2b; border:1px solid rgba(255,255,255,.06); }
.ok { color:#6ccb5f; } .gray { color:var(--muted); font-size:11.5px; line-height:1.45; }
.btn { display:inline-flex; align-items:center; height:30px; padding:0 14px; border-radius:5px; background:#2d2d2d;
  border:1px solid rgba(255,255,255,.1); color:var(--fg); font-size:12px; margin-right:6px; }
.btn.primary { background:var(--accent); color:#1b1406; border-color:transparent; font-weight:600; }
.input { display:flex; align-items:center; height:30px; padding:0 10px; border-radius:5px; background:#2d2d2d;
  border:1px solid rgba(255,255,255,.08); border-bottom:1.5px solid var(--accent); flex:1; color:var(--fg); }
.foot { display:flex; justify-content:flex-end; margin-top:16px; }
.lbl { margin:14px 0 6px; }
"""


def e(s):
    return html.escape(s, quote=False)


def page(css, body, w, h):
    return ("<!doctype html><html lang='ru'><head><meta charset='utf-8'><style>%s%s</style></head>"
            "<body style='--w:%dpx;--h:%dpx'>%s</body></html>") % (BASE_CSS, css, w, h, body)


def icon_url():
    import base64
    return "data:image/png;base64," + base64.b64encode(open(os.path.join(ROOT, "docs", "screens", "icon-64.png"), "rb").read()).decode()


def menu_html(w, h):
    ic = icon_url()
    main = [
        ("title", "Mount Sheet Sync 2.5.0"),
        ("dis", "Таблица «Маунты WoW» — связь есть"),
        ("dis", "Последняя отправка: сегодня в 18:42 — Отмечено в таблице: 2"),
        ("sep", None),
        ("", "Отправить сейчас"), ("sub", "Последние события"), ("hotsub", "Осталось на неделе"), ("", "Открыть таблицу"),
        ("", "Персонажи в таблице…"), ("", "Что фармить…"), ("", "Сверить с коллекцией"),
        ("sep", None),
        ("", "Настройки…"), ("", "Журнал работы"), ("chk", "Запускать вместе с Windows"), ("chk", "Показывать уведомления"),
        ("sub", "Язык / Language"),
        ("sep", None),
        ("", "Выход"),
    ]
    sub = [("sub", "Эйра — 23 из 36"), ("sub", "Мирна — 12 из 30"), ("dis", "Каэлия — всё отмечено"), ("sep", None),
           ("dis", "Из таблицы сегодня в 18:42")]

    def render(items):
        out = []
        for kind, text in items:
            if kind == "sep":
                out.append("<div class='sep'></div>")
                continue
            cls = "item" + (" dis" if kind == "dis" else "") + (" title" if kind == "title" else "") + (" hot" if kind == "hotsub" else "")
            chk = "<span class='chk'>✓</span>" if kind == "chk" else ""
            arr = "<span class='arr'>❯</span>" if kind in ("sub", "hotsub") else ""
            out.append("<div class='%s'>%s%s%s</div>" % (cls, chk, e(text), arr))
        return "".join(out)
    # main menu sits above the tray icon; the "Remaining" submenu opens to its left
    menu_w, item_h = 470, 30
    n_items = sum(1 for k, _ in main if k != "sep")
    n_sep = sum(1 for k, _ in main if k == "sep")
    menu_h = n_items * item_h + n_sep * 9 + 10
    menu_left = w - menu_w - 70
    menu_top = h - 48 - 8 - menu_h
    hot_index = [i for i, (k, _) in enumerate(main) if k == "hotsub"][0]
    hot_top = menu_top + 5 + sum(item_h if k != "sep" else 9 for k, _ in main[:hot_index])
    return page(MENU_CSS, (
        "<div class='menu' style='left:%dpx;top:%dpx;width:%dpx'>%s</div>" % (menu_left, menu_top, menu_w, render(main)) +
        "<div class='menu' style='left:%dpx;top:%dpx;width:250px'>%s</div>" % (menu_left - 254, hot_top - 5, render(sub)) +
        "<div class='taskbar'><div class='tray'><span style='color:#ccc'>⌃</span><img src='%s'><span>ENG</span>"
        "<div class='clock'>18:45<br>23.10.2026</div></div></div>" % ic), w, h)


def settings_html(w, h):
    ic = icon_url()
    body = """
<div class='win'>
 <div class='bar'><img src='{ic}'><span class='t'>Mount Sheet Sync — настройки</span><span class='x'>✕</span></div>
 <div class='body'>
  <div>Где таблица:</div>
  <div class='row'><span class='radio on'></span>Таблица, которую создаёт программа (вход через Google) — рекомендуется</div>
  <div class='group'>
   <div class='ok'>Аккаунт Google: player@example.com</div>
   <div style='margin:8px 0 12px'><span class='btn'>Выйти</span></div>
   <div class='ok'>Таблица: «Маунты WoW»</div>
   <div style='margin:8px 0 12px'><span class='btn'>Создать новую…</span><span class='btn'>Персонажи…</span><span class='btn'>Открыть</span></div>
   <div class='gray'>Программа видит только таблицы, которые создала сама. Остальные файлы на вашем Google Диске ей недоступны.</div>
  </div>
  <div class='row'><span class='radio'></span>Своя таблица со скриптом Code.gs (как в версиях 1.x)</div>
  <div class='lbl'>Папка World of Warcraft (если пусто — ищется сама):</div>
  <div class='row'><span class='input'>C:\\Program Files (x86)\\World of Warcraft</span><span class='btn' style='margin:0'>Обзор…</span></div>
  <div class='ok' style='font-size:11.5px'>Ок, файлы аддона ищутся в C:\\Program Files (x86)\\World of Warcraft\\_retail_\\WTF.</div>
  <div class='row' style='margin-top:14px'><span class='cbox on'>✓</span>Запускать вместе с Windows (значок будет в трее)</div>
  <div class='ok' style='margin-top:12px'>Связь есть: таблица «Маунты WoW». Листы найдены: Эйра, Мирна, Каэлия.</div>
  <div class='foot'><span class='btn'>Проверить связь</span><span class='btn primary'>Сохранить</span><span class='btn' style='margin-right:0'>Отмена</span></div>
 </div>
</div>""".format(ic=ic)
    return page(SETTINGS_CSS, body, w, h)


def color(c, default):
    if not c:
        return default
    return "rgb(%d,%d,%d)" % tuple(round(255 * c.get(k, 0)) for k in ("red", "green", "blue"))


def sheet_html(w, h):
    job = {"title": "Маунты WoW", "chars": [{"name": "Эйра", "raids": True, "dungeons": True}], "collected": [], "defaultSheetId": 0}
    host = os.path.join(ROOT, "tools", "cs_host", "BuilderHost.exe")
    out = json.loads(subprocess.run(["mono", host], input=json.dumps(job, ensure_ascii=False).encode(), capture_output=True,
                                    check=True).stdout.decode("utf-8"))
    reqs = out["requests"]
    sheet_id = [r["addSheet"]["properties"]["sheetId"] for r in reqs
                if "addSheet" in r and r["addSheet"]["properties"]["title"] == "Рейды Эйра"][0]
    rows = [r["updateCells"]["rows"] for r in reqs if "updateCells" in r and r["updateCells"]["start"]["sheetId"] == sheet_id][0]
    band = [r["addBanding"]["bandedRange"]["rowProperties"] for r in reqs
            if "addBanding" in r and r["addBanding"]["bandedRange"]["range"]["sheetId"] == sheet_id][0]
    rules = [r["addConditionalFormatRule"]["rule"] for r in reqs if "addConditionalFormatRule" in r
             and r["addConditionalFormatRule"]["rule"]["ranges"][0]["sheetId"] == sheet_id]

    def rule_for(text):
        for ru in rules:
            cond = ru["booleanRule"]["condition"]
            if cond["type"] == "TEXT_EQ" and cond["values"][0]["userEnteredValue"] == text:
                return ru["booleanRule"]["format"]
    yes, no = rule_for("Да"), rule_for("Нет")
    grey = [ru["booleanRule"]["format"] for ru in rules if ru["booleanRule"]["condition"]["type"] == "CUSTOM_FORMULA"
            and "FALSE" in ru["booleanRule"]["condition"]["values"][0]["userEnteredValue"]][0]

    def val(c):
        v = c.get("userEnteredValue", {})
        return v.get("stringValue", v.get("boolValue", v.get("numberValue", "")))
    header = [val(c) for c in rows[0]["values"]]
    data = [[val(c) for c in r["values"]] for r in rows[1:10]]
    # sample marks: two kills this week, one row not farmed on this character
    data[1][0] = "Да"
    data[4][0] = "Да"
    data[6][7] = False
    cols = [118, 150, 150, 170, 150, 140, 82, 72]
    th = "".join("<th style='width:%dpx'>%s</th>" % (cw, e(t)) for cw, t in zip(cols, header))
    body = []
    for i, r in enumerate(data):
        farmed = r[7] is not False
        bg = color(band["firstBandColor"] if i % 2 == 0 else band["secondBandColor"], "#fff")
        cells = []
        for j, v in enumerate(r):
            style = "background:%s;" % (color(grey["backgroundColor"], bg) if not farmed else bg)
            if not farmed:
                style += "color:%s;" % color(grey["textFormat"]["foregroundColor"], "#999")
            if j == 0:
                f = yes if v == "Да" else no
                if farmed:
                    style += "background:%s;color:%s;" % (color(f["backgroundColor"], bg), color(f["textFormat"]["foregroundColor"], "#000"))
                style += "text-align:center;font-weight:600;"
                cells.append("<td style='%s'>%s <span class='dd'>▾</span></td>" % (style, e(v)))
            elif j == 7:
                cells.append("<td style='%s;text-align:center'><span class='tick%s'>%s</span></td>" % (style, "" if v else " off", "✓" if v else ""))
            else:
                cells.append("<td style='%s'>%s</td>" % (style, e(str(v))))
        body.append("<tr><td class='rn'>%d</td>%s</tr>" % (i + 2, "".join(cells)))
    head_bg = color(band["headerColor"], "#1f4e79")
    css = """
.card { position:absolute; left:34px; top:30px; right:34px; bottom:30px; background:#fff; border-radius:12px; overflow:hidden;
  box-shadow:0 24px 60px rgba(0,0,0,.5); font-family:"Carlito","Inter",sans-serif; }
.top { height:44px; display:flex; align-items:center; gap:10px; padding:0 16px; border-bottom:1px solid #e3e3e3;
  font-family:"Inter",sans-serif; font-size:14px; color:#202124; }
.top .doc { width:22px; height:22px; border-radius:4px; background:#1e8e3e; }
table { border-collapse:collapse; font-size:13px; color:#202124; table-layout:fixed; }
th { background:%s; color:#fff; font-weight:700; padding:6px 7px; text-align:center; vertical-align:top; border:1px solid #c9d7e2; line-height:1.2; }
td { padding:5px 7px; border:1px solid #d7e3ec; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; height:24px; }
td.rn, th.rn { background:#f8f9fa; color:#5f6368; width:34px; text-align:center; font-family:"Inter",sans-serif; font-size:11px; border-color:#e3e3e3; }
.dd { font-size:9px; opacity:.55; }
.tick { display:inline-flex; width:15px; height:15px; border-radius:3px; background:#1a73e8; color:#fff; font-size:11px;
  align-items:center; justify-content:center; font-weight:700; }
.tick.off { background:#fff; border:1.5px solid #9e9e9e; }
.tabs { position:absolute; left:0; right:0; bottom:0; height:36px; background:#f8f9fa; border-top:1px solid #e3e3e3;
  display:flex; align-items:center; gap:2px; padding-left:44px; font-family:"Inter",sans-serif; font-size:12.5px; color:#444; }
.tabs span { padding:8px 14px; } .tabs .on { background:#e1ebf6; color:#0b57d0; font-weight:600; border-radius:6px 6px 0 0; }
""" % head_bg
    tabs = "".join("<span%s>%s</span>" % (" class='on'" if t == "Рейды Эйра" else "", e(t))
                   for t in ["Рейды", "Рейды Эйра", "Подземелья", "Подземелья Эйра", "Мировые боссы", "Сводка"])
    body_html = ("<div class='card'><div class='top'><span class='doc'></span>Маунты WoW</div>"
                 "<table><tr><th class='rn'></th>%s</tr>%s</table><div class='tabs'>%s</div></div>") % (th, "".join(body), tabs)
    return page(css, body_html, w, h)


def main():
    from PIL import Image
    Image.open(os.path.join(ROOT, "program", "app.ico")).convert("RGBA").resize((64, 64)).save(
        os.path.join(OUT, "icon-64.png"))
    jobs = [("program-menu.png", menu_html, 1100, 760), ("program-settings.png", settings_html, 700, 700),
            ("sheet.png", sheet_html, 1180, 420)]
    with sync_playwright() as p:
        browser = p.chromium.launch()
        for name, fn, w, h in jobs:
            pg = browser.new_page(viewport={"width": w, "height": h}, device_scale_factor=2)
            pg.set_content(fn(w, h))
            pg.wait_for_timeout(300)
            if name == "program-menu.png":
                box = pg.evaluate("(() => { const m = [...document.querySelectorAll('.menu')].map(x => x.getBoundingClientRect()); return [Math.min(...m.map(r => r.x)), Math.min(...m.map(r => r.y))]; })()")
                pg.screenshot(path=os.path.join(OUT, name), clip={"x": box[0] - 40, "y": box[1] - 30, "width": w - box[0] + 40, "height": h - box[1] + 30})
            elif name == "program-settings.png":
                box = pg.evaluate("(() => { const r = document.querySelector('.win').getBoundingClientRect(); return [r.x, r.y, r.width, r.height]; })()")
                clip = {"x": box[0] - 36, "y": box[1] - 30, "width": box[2] + 72, "height": box[3] + 60}
                pg.screenshot(path=os.path.join(OUT, name), clip=clip)
            else:
                pg.screenshot(path=os.path.join(OUT, name))
            print(name)
            pg.close()
        browser.close()
    os.remove(os.path.join(OUT, "icon-64.png"))


if __name__ == "__main__":
    main()
