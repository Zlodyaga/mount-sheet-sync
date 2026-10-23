"""Mock of the Google endpoints Mount Sheet Sync 2.0 uses: OAuth (loopback + PKCE), Sheets API v4, Drive files.

Strict on purpose: unknown batchUpdate request types, bad field masks, missing bearer tokens, wrong PKCE,
formulas with ',' in a ru_RU sheet (as Google does) -> errors. State is kept in memory.

  python3 mock_google.py <port> <state.json out> [<log file>]

Control (test only):
  POST /__control  {"fail": [503, 429], "expireAccess": true, "revokeRefresh": true, "consent": "deny"|"noscope"|"ok"}
  GET  /__state    -> whole model as JSON
"""
import base64
import hashlib
import json
import re
import sys
import threading
import time
import urllib.parse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

PORT = int(sys.argv[1])
STATE_OUT = sys.argv[2]
LOG = open(sys.argv[3], "a", encoding="utf-8") if len(sys.argv) > 3 else None

CLIENT_ID = "676267272691-cq183fvmictehrh4u6p305i39rckt71q.apps.googleusercontent.com"
CLIENT_SECRET = "GOCSPX" + "-2P-Snr0_C" + "qlSNjhRhBxe" + "XWUXmJzQ"
SCOPE_FULL = "openid https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/drive.file"
EMAIL = "tester@example.com"

LOCK = threading.RLock()
STATE = {
    "codes": {},        # code -> {challenge, redirect, scope}
    "refresh": {},      # refresh -> True (valid)
    "access": {},       # access -> expires (epoch)
    "fail": [],         # queue of HTTP codes for the next API requests
    "consent": "ok",
    "spreadsheets": {},
    "requests": [],     # log of (method, path) for assertions
    "revoked": [],
    "trashed": [],
}
SEQ = [0]

KNOWN_REQUESTS = {
    "addSheet", "deleteSheet", "updateCells", "repeatCell", "updateDimensionProperties", "addBanding",
    "setDataValidation", "addConditionalFormatRule", "setBasicFilter", "mergeCells", "updateSpreadsheetProperties",
    "createDeveloperMetadata", "updateDeveloperMetadata", "appendDimension", "deleteDimension",
}


def log(msg):
    if LOG:
        LOG.write(time.strftime("%H:%M:%S ") + msg + "\n")
        LOG.flush()


def save():
    with open(STATE_OUT, "w", encoding="utf-8") as f:
        json.dump(STATE, f, ensure_ascii=False, indent=1, default=str)


def new_id(prefix):
    SEQ[0] += 1
    return "%s%04d" % (prefix, SEQ[0])


def b64url(data):
    return base64.urlsafe_b64encode(data).rstrip(b"=").decode()


class ApiError(Exception):
    def __init__(self, code, status, message):
        super().__init__(message)
        self.code, self.status, self.message = code, status, message


def col_index(letters):
    n = 0
    for ch in letters:
        n = n * 26 + (ord(ch) - 64)
    return n - 1


def parse_range(rng):
    """'Sheet'!A2:B5 / 'Sheet' / Sheet!A1 -> (title, r0, c0, r1, c1) (inclusive, None = open)."""
    m = re.match(r"^(?:'((?:[^']|'')*)'|([^!]+))(?:!(.*))?$", rng)
    if not m:
        raise ApiError(400, "INVALID_ARGUMENT", "Unable to parse range: " + rng)
    title = m.group(1).replace("''", "'") if m.group(1) is not None else m.group(2)
    part = m.group(3)
    if not part:
        return title, 0, 0, None, None
    cells = part.split(":")

    def one(s):
        mm = re.match(r"^([A-Z]+)?(\d+)?$", s)
        if not mm:
            raise ApiError(400, "INVALID_ARGUMENT", "Unable to parse range: " + rng)
        c = col_index(mm.group(1)) if mm.group(1) else None
        r = int(mm.group(2)) - 1 if mm.group(2) else None
        return r, c
    r0, c0 = one(cells[0])
    r1, c1 = one(cells[1]) if len(cells) > 1 else (r0, c0)
    return title, r0 or 0, c0 or 0, r1, c1


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, fmt, *args):
        log(fmt % args)

    # ---- plumbing
    def reply(self, code, obj=None, headers=None):
        body = b"" if obj is None else json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        for k, v in (headers or {}).items():
            self.send_header(k, v)
        if obj is not None:
            self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def body(self):
        n = int(self.headers.get("Content-Length") or 0)
        return self.rfile.read(n) if n else b""

    def api_error(self, e):
        self.reply(e.code, {"error": {"code": e.code, "status": e.status, "message": e.message}})

    def authed(self):
        auth = self.headers.get("Authorization") or ""
        if not auth.startswith("Bearer "):
            raise ApiError(401, "UNAUTHENTICATED", "Request is missing required authentication credential.")
        tok = auth[7:]
        exp = STATE["access"].get(tok)
        if exp is None or exp < time.time():
            raise ApiError(401, "UNAUTHENTICATED", "Request had invalid authentication credentials.")

    def do_GET(self):
        self.route("GET")

    def do_POST(self):
        self.route("POST")

    def do_PATCH(self):
        self.route("PATCH")

    def route(self, method):
        u = urllib.parse.urlsplit(self.path)
        path = u.path
        q = urllib.parse.parse_qs(u.query, keep_blank_values=True)
        with LOCK:
            STATE["requests"].append([method, path])
            try:
                if path == "/__state":
                    return self.reply(200, STATE)
                if path == "/__control":
                    c = json.loads(self.body() or b"{}")
                    if "fail" in c:
                        STATE["fail"] = list(c["fail"])
                    if c.get("expireAccess"):
                        for k in STATE["access"]:
                            STATE["access"][k] = 0
                    if c.get("revokeRefresh"):
                        for k in STATE["refresh"]:
                            STATE["refresh"][k] = False
                    if "consent" in c:
                        STATE["consent"] = c["consent"]
                    return self.reply(200, {"ok": True})
                if path == "/o/oauth2/v2/auth":
                    return self.auth(q)
                if path == "/token":
                    return self.token(urllib.parse.parse_qs(self.body().decode()))
                if path == "/revoke":
                    tok = urllib.parse.parse_qs(self.body().decode()).get("token", [""])[0]
                    STATE["revoked"].append(tok)
                    STATE["refresh"][tok] = False
                    return self.reply(200, {})
                # ---- APIs with a bearer token
                self.authed()
                if STATE["fail"]:
                    code = STATE["fail"].pop(0)
                    raise ApiError(code, "UNAVAILABLE" if code >= 500 else "RESOURCE_EXHAUSTED", "injected failure")
                m = re.match(r"^/drive/v3/files/([^/]+)$", path)
                if m and method == "PATCH":
                    fid = urllib.parse.unquote(m.group(1))
                    if fid not in STATE["spreadsheets"]:
                        raise ApiError(404, "NOT_FOUND", "File not found")
                    if json.loads(self.body() or b"{}").get("trashed"):
                        STATE["trashed"].append(fid)
                    return self.reply(200, {"id": fid})
                if path == "/v4/spreadsheets" and method == "POST":
                    return self.reply(200, self.create(json.loads(self.body())))
                m = re.match(r"^/v4/spreadsheets/([^/:]+)(?::batchUpdate|/values:batchGet|/values:batchUpdate)?$", path)
                if not m:
                    raise ApiError(404, "NOT_FOUND", "no such endpoint " + path)
                sid = urllib.parse.unquote(m.group(1))
                book = STATE["spreadsheets"].get(sid)
                if book is None:
                    raise ApiError(404, "NOT_FOUND", "Requested entity was not found.")
                if path.endswith(":batchUpdate") and "/values" not in path:
                    return self.reply(200, self.batch_update(book, json.loads(self.body())))
                if path.endswith("/values:batchGet"):
                    return self.reply(200, self.batch_get(book, q))
                if path.endswith("/values:batchUpdate"):
                    return self.reply(200, self.values_update(book, json.loads(self.body())))
                return self.reply(200, self.get(book, q))
            except ApiError as e:
                log("ERROR %s %s %d %s" % (method, path, e.code, e.message))
                return self.api_error(e)
            finally:
                save()

    # ---- OAuth
    def auth(self, q):
        def p(k):
            return q.get(k, [""])[0]
        if p("client_id") != CLIENT_ID:
            return self.reply(400, {"error": "invalid_client"})
        redirect = p("redirect_uri")
        if not re.match(r"^http://127\.0\.0\.1:\d+/$", redirect):
            return self.reply(400, {"error": "redirect_uri_mismatch"})
        if p("code_challenge_method") != "S256" or len(p("code_challenge")) != 43 or not p("state"):
            return self.reply(400, {"error": "invalid_request", "error_description": "PKCE S256 and state required"})
        if p("access_type") != "offline":
            return self.reply(400, {"error": "invalid_request", "error_description": "offline access expected"})
        state = urllib.parse.quote(p("state"))
        if STATE["consent"] == "deny":
            loc = redirect + "?error=access_denied&state=" + state
        else:
            scope = SCOPE_FULL if STATE["consent"] != "noscope" else "openid https://www.googleapis.com/auth/userinfo.email"
            code = new_id("4/code-")
            STATE["codes"][code] = {"challenge": p("code_challenge"), "redirect": redirect, "scope": scope}
            loc = redirect + "?state=" + state + "&code=" + urllib.parse.quote(code) + "&scope=" + urllib.parse.quote(scope)
        self.send_response(302)
        self.send_header("Location", loc)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def token(self, f):
        def p(k):
            return f.get(k, [""])[0]
        if p("client_id") != CLIENT_ID or p("client_secret") != CLIENT_SECRET:
            return self.reply(401, {"error": "invalid_client", "error_description": "Unauthorized"})
        if p("grant_type") == "authorization_code":
            c = STATE["codes"].pop(p("code"), None)
            if c is None or c["redirect"] != p("redirect_uri"):
                return self.reply(400, {"error": "invalid_grant", "error_description": "Bad Request"})
            if b64url(hashlib.sha256(p("code_verifier").encode()).digest()) != c["challenge"]:
                return self.reply(400, {"error": "invalid_grant", "error_description": "Invalid code verifier."})
            refresh = new_id("1//refresh-")
            STATE["refresh"][refresh] = True
            access = new_id("ya29.access-")
            STATE["access"][access] = time.time() + 3599
            idt = ".".join([b64url(b'{"alg":"RS256"}'), b64url(json.dumps({"email": EMAIL, "email_verified": True}).encode()), "sig"])
            return self.reply(200, {"access_token": access, "expires_in": 3599, "refresh_token": refresh,
                                    "scope": c["scope"], "token_type": "Bearer", "id_token": idt})
        if p("grant_type") == "refresh_token":
            if not STATE["refresh"].get(p("refresh_token")):
                return self.reply(400, {"error": "invalid_grant", "error_description": "Token has been expired or revoked."})
            access = new_id("ya29.access-")
            STATE["access"][access] = time.time() + 3599
            return self.reply(200, {"access_token": access, "expires_in": 3599, "scope": SCOPE_FULL, "token_type": "Bearer"})
        return self.reply(400, {"error": "unsupported_grant_type"})

    # ---- Sheets
    def create(self, body):
        props = body.get("properties") or {}
        sid = new_id("SHEET-")
        book = {"id": sid, "title": props.get("title", "Новая таблица"), "locale": props.get("locale", "ru_RU"),
                "sheets": [], "devmeta": [], "metaSeq": 100, "validations": [], "formats": [], "bandings": [], "filters": []}
        book["sheets"].append({"sheetId": 0, "title": "Лист1", "rows": 1000, "cols": 26, "cells": {}})
        STATE["spreadsheets"][sid] = book
        return {"spreadsheetId": sid, "spreadsheetUrl": "https://docs.google.com/spreadsheets/d/%s/edit" % sid,
                "properties": {"title": book["title"], "locale": book["locale"]},
                "sheets": [{"properties": {"sheetId": 0, "title": "Лист1", "index": 0}}]}

    def sheet_by_id(self, book, sid):
        for s in book["sheets"]:
            if s["sheetId"] == sid:
                return s
        raise ApiError(400, "INVALID_ARGUMENT", "No grid with id: %s" % sid)

    def sheet_by_title(self, book, title):
        for s in book["sheets"]:
            if s["title"] == title:
                return s
        raise ApiError(400, "INVALID_ARGUMENT", "Unable to parse range: " + title)

    def check_formula(self, book, formula):
        # with a decimal comma (ru_RU, de_DE, fr_FR, es_ES, pt_BR) formulas use ';' — a ',' outside quotes is an
        # error, like in the real Sheets
        if book["locale"] in ("ru_RU", "de_DE", "fr_FR", "es_ES", "pt_BR"):
            outside = re.sub(r'"[^"]*"', "", formula)
            outside = re.sub(r"'[^']*'", "", outside)
            if "," in outside:
                raise ApiError(400, "INVALID_ARGUMENT", "Invalid formula for %s locale: %s" % (book["locale"], formula))

    def batch_update(self, book, body):
        reqs = body.get("requests") or []
        locale_at_start = book["locale"]
        replies = []
        import copy
        snapshot = copy.deepcopy(book)
        try:
            for r in reqs:
                if len(r) != 1:
                    raise ApiError(400, "INVALID_ARGUMENT", "exactly one request kind per entry")
                kind, x = next(iter(r.items()))
                if kind not in KNOWN_REQUESTS:
                    raise ApiError(400, "INVALID_ARGUMENT", "Unknown request: " + kind)
                replies.append(self.apply(book, kind, x, locale_at_start))
        except ApiError:
            STATE["spreadsheets"][book["id"]] = snapshot   # atomic like Google
            raise
        return {"spreadsheetId": book["id"], "replies": replies}

    def apply(self, book, kind, x, locale_at_start):
        if kind == "addSheet":
            p = x.get("properties") or {}
            sid = p.get("sheetId", len(book["sheets"]) + 1000)
            if any(s["sheetId"] == sid for s in book["sheets"]):
                raise ApiError(400, "INVALID_ARGUMENT", "duplicate sheet id %s" % sid)
            if any(s["title"] == p["title"] for s in book["sheets"]):
                raise ApiError(400, "INVALID_ARGUMENT", 'A sheet with the name "%s" already exists.' % p["title"])
            g = p.get("gridProperties") or {}
            s = {"sheetId": sid, "title": p["title"], "rows": g.get("rowCount", 1000), "cols": g.get("columnCount", 26),
                 "frozen": g.get("frozenRowCount", 0), "cells": {}}
            idx = p.get("index", len(book["sheets"]))
            if idx > len(book["sheets"]):
                raise ApiError(400, "INVALID_ARGUMENT", "index out of range")
            book["sheets"].insert(idx, s)
            return {"addSheet": {"properties": {"sheetId": sid, "title": p["title"], "index": idx}}}
        if kind == "deleteSheet":
            s = self.sheet_by_id(book, x["sheetId"])
            if len(book["sheets"]) == 1:
                raise ApiError(400, "INVALID_ARGUMENT", "You can't remove all the sheets in a document.")
            book["sheets"].remove(s)
            return {}
        if kind == "updateCells":
            st = x["start"]
            s = self.sheet_by_id(book, st["sheetId"])
            if x.get("fields") != "userEnteredValue":
                raise ApiError(400, "INVALID_ARGUMENT", "unexpected fields " + str(x.get("fields")))
            for i, row in enumerate(x["rows"]):
                for j, cell in enumerate(row.get("values", [])):
                    r, c = st["rowIndex"] + i, st["columnIndex"] + j
                    if r >= s["rows"] or c >= s["cols"]:
                        raise ApiError(400, "INVALID_ARGUMENT", "Range (%s!R%dC%d) exceeds grid limits" % (s["title"], r + 1, c + 1))
                    v = cell.get("userEnteredValue")
                    if v and "formulaValue" in v:
                        self.check_formula(book if locale_at_start == book["locale"] else {"locale": locale_at_start}, v["formulaValue"])
                    key = "%d,%d" % (r, c)
                    if v:
                        s["cells"][key] = v
                    else:
                        s["cells"].pop(key, None)
            return {}
        if kind == "appendDimension":
            s = self.sheet_by_id(book, x["sheetId"])
            if x["dimension"] == "COLUMNS":
                s["cols"] += x["length"]
            else:
                s["rows"] += x["length"]
            return {}
        if kind == "repeatCell":
            self.sheet_by_id(book, x["range"]["sheetId"])
            for f in x["fields"].split(","):
                if not f.startswith("userEnteredFormat.") or "(" in f:
                    raise ApiError(400, "INVALID_ARGUMENT", "Invalid field: " + f)
            book["formats"].append(x)
            return {}
        if kind in ("updateDimensionProperties", "mergeCells"):
            s = self.sheet_by_id(book, x["range"]["sheetId"])
            if kind == "updateDimensionProperties" and "hiddenByUser" in x.get("fields", ""):
                rg = x["range"]
                hidden = s.setdefault("hidden", {"COLUMNS": [], "ROWS": []})
                for i in range(rg["startIndex"], rg["endIndex"]):
                    if x["properties"].get("hiddenByUser") and i not in hidden[rg["dimension"]]:
                        hidden[rg["dimension"]].append(i)
            return {}
        if kind == "deleteDimension":
            rg = x["range"]
            s = self.sheet_by_id(book, rg["sheetId"])
            a, b = rg["startIndex"], rg["endIndex"]
            if rg["dimension"] == "COLUMNS":
                if b > s["cols"] or a >= b:
                    raise ApiError(400, "INVALID_ARGUMENT", "Invalid dimension range")
                cells = {}
                for k, v in s["cells"].items():
                    r, c = map(int, k.split(","))
                    if a <= c < b:
                        continue
                    cells["%d,%d" % (r, c - (b - a) if c >= b else c)] = v
                s["cells"] = cells
                s["cols"] -= b - a
                for band in book["bandings"]:
                    o = band["range"]
                    if o["sheetId"] != s["sheetId"]:
                        continue
                    lo, hi = o["startColumnIndex"], o["endColumnIndex"]
                    cut = max(0, min(hi, b) - max(lo, a))
                    if lo >= b:
                        o["startColumnIndex"], o["endColumnIndex"] = lo - (b - a), hi - (b - a)
                    else:
                        o["endColumnIndex"] = hi - cut
                book.setdefault("deletedCols", []).append([s["title"], a, b])
                return {}
            if b > s["rows"] or a >= b:
                raise ApiError(400, "INVALID_ARGUMENT", "Invalid dimension range")
            if s["rows"] - (b - a) <= s.get("frozen", 0):
                raise ApiError(400, "INVALID_ARGUMENT", "You can't delete all the rows on the sheet.")
            cells = {}
            for k, v in s["cells"].items():
                r, c = map(int, k.split(","))
                if a <= r < b:
                    continue
                cells["%d,%d" % (r - (b - a) if r >= b else r, c)] = v
            s["cells"] = cells
            s["rows"] -= b - a
            book.setdefault("deleted", []).append([s["title"], a, b])
            return {}
        if kind == "addBanding":
            rng = x["bandedRange"]["range"]
            self.sheet_by_id(book, rng["sheetId"])
            for b in book["bandings"]:
                o = b["range"]
                if o["sheetId"] == rng["sheetId"] and not (rng["endColumnIndex"] <= o["startColumnIndex"] or rng["startColumnIndex"] >= o["endColumnIndex"]
                                                           or rng["endRowIndex"] <= o["startRowIndex"] or rng["startRowIndex"] >= o["endRowIndex"]):
                    raise ApiError(400, "INVALID_ARGUMENT", "You cannot add alternating background colors to a range that already has alternating background colors.")
            book["bandings"].append(x["bandedRange"])
            return {"addBanding": {"bandedRange": dict(x["bandedRange"], bandedRangeId=len(book["bandings"]))}}
        if kind == "setDataValidation":
            self.sheet_by_id(book, x["range"]["sheetId"])
            book["validations"].append(x)
            return {}
        if kind == "addConditionalFormatRule":
            for rg in x["rule"]["ranges"]:
                self.sheet_by_id(book, rg["sheetId"])
            cond = x["rule"]["booleanRule"]["condition"]
            if cond["type"] == "CUSTOM_FORMULA":
                self.check_formula({"locale": locale_at_start}, cond["values"][0]["userEnteredValue"])
            return {}
        if kind == "setBasicFilter":
            self.sheet_by_id(book, x["filter"]["range"]["sheetId"])
            book["filters"].append(x)
            return {}
        if kind == "updateSpreadsheetProperties":
            props = x["properties"]
            for f in x["fields"].split(","):
                if f == "locale":
                    book["locale"] = props["locale"]
                elif f == "title":
                    book["title"] = props["title"]
                else:
                    raise ApiError(400, "INVALID_ARGUMENT", "field " + f)
            return {}
        if kind == "createDeveloperMetadata":
            m = dict(x["developerMetadata"])
            if m.get("location") != {"spreadsheet": True} or m.get("visibility") not in ("DOCUMENT", "PROJECT"):
                raise ApiError(400, "INVALID_ARGUMENT", "bad metadata location/visibility")
            book["metaSeq"] += 1
            m["metadataId"] = book["metaSeq"]
            m["location"] = {"locationType": "SPREADSHEET", "spreadsheet": True}
            book["devmeta"].append(m)
            return {"createDeveloperMetadata": {"developerMetadata": m}}
        if kind == "updateDeveloperMetadata":
            mid = x["dataFilters"][0]["developerMetadataLookup"]["metadataId"]
            if x["fields"] != "metadataValue":
                raise ApiError(400, "INVALID_ARGUMENT", "fields")
            hit = [m for m in book["devmeta"] if m["metadataId"] == mid]
            if not hit:
                raise ApiError(400, "INVALID_ARGUMENT", "No developer metadata with id %s" % mid)
            hit[0]["metadataValue"] = x["developerMetadata"]["metadataValue"]
            return {"updateDeveloperMetadata": {"developerMetadata": hit}}
        raise ApiError(400, "INVALID_ARGUMENT", kind)

    def get(self, book, q):
        fields = q.get("fields", [""])[0]
        if "sheets.properties" not in fields:
            raise ApiError(400, "INVALID_ARGUMENT", "test mock expects a field mask with sheets.properties")
        out = {"spreadsheetId": book["id"], "properties": {"title": book["title"], "locale": book["locale"]},
               "spreadsheetUrl": "https://docs.google.com/spreadsheets/d/%s/edit" % book["id"],
               "sheets": [{"properties": {"sheetId": s["sheetId"], "title": s["title"], "index": i, "sheetType": "GRID",
                                          "gridProperties": {"rowCount": s["rows"], "columnCount": s["cols"]}}}
                          for i, s in enumerate(book["sheets"])]}
        if "developerMetadata" in fields and book["devmeta"]:
            out["developerMetadata"] = book["devmeta"]
        return out

    @staticmethod
    def cell_value(v, render):
        if v is None:
            return ""
        if "formulaValue" in v:
            return v["formulaValue"] if render == "FORMULA" else 0
        for k in ("stringValue", "numberValue", "boolValue"):
            if k in v:
                return v[k]
        return ""

    def batch_get(self, book, q):
        render = q.get("valueRenderOption", ["FORMATTED_VALUE"])[0]
        if render not in ("UNFORMATTED_VALUE", "FORMULA"):
            raise ApiError(400, "INVALID_ARGUMENT", "render " + render)
        out = []
        for rng in q.get("ranges", []):
            title, r0, c0, r1, c1 = parse_range(rng)
            s = self.sheet_by_title(book, title)
            cells = {tuple(map(int, k.split(","))): v for k, v in s["cells"].items()}
            if not cells:
                out.append({"range": rng, "majorDimension": "ROWS"})
                continue
            maxr = max(r for r, _ in cells)
            rows = []
            for r in range(r0, (maxr if r1 is None else min(r1, maxr)) + 1):
                row = [self.cell_value(cells.get((r, c)), render) for c in range(c0, s["cols"] if c1 is None else c1 + 1)]
                while row and row[-1] == "":
                    row.pop()
                rows.append(row)
            while rows and not rows[-1]:
                rows.pop()
            out.append({"range": rng, "majorDimension": "ROWS", "values": rows})
        return {"spreadsheetId": book["id"], "valueRanges": out}

    def values_update(self, book, body):
        if body.get("valueInputOption") != "RAW":
            raise ApiError(400, "INVALID_ARGUMENT", "test mock expects RAW")
        n = 0
        for d in body["data"]:
            title, r0, c0, r1, c1 = parse_range(d["range"])
            s = self.sheet_by_title(book, title)
            for i, row in enumerate(d["values"]):
                for j, v in enumerate(row):
                    r, c = r0 + i, c0 + j
                    if (r1 is not None and r > r1) or (c1 is not None and c > c1):
                        raise ApiError(400, "INVALID_ARGUMENT", "values outside the range " + d["range"])
                    key = "%d,%d" % (r, c)
                    if isinstance(v, bool):
                        s["cells"][key] = {"boolValue": v}
                    elif isinstance(v, (int, float)):
                        s["cells"][key] = {"numberValue": v}
                    elif v == "":
                        s["cells"].pop(key, None)
                    else:
                        s["cells"][key] = {"stringValue": str(v)}
                    n += 1
        return {"spreadsheetId": book["id"], "totalUpdatedCells": n}


if __name__ == "__main__":
    save()
    srv = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    log("mock google on %d" % PORT)
    srv.serve_forever()
