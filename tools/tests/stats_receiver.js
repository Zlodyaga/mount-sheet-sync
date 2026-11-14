// Runs tools/stats/Code.gs (the Apps Script receiver of anonymous statistics) against imitations of the Google
// services it uses and prints what happened as JSON. Called by test_stats_receiver.py.
const fs = require('fs');
const path = require('path');
const vm = require('vm');

const sheets = {};
let triggers = [];
function makeSheet(name) {
  const rows = [];
  return {
    name: name, rows: rows, frozen: 0,
    getLastRow: function () { return rows.length; },
    appendRow: function (row) {
      // like Sheets: a leading apostrophe makes the value text and is not shown
      rows.push(row.map(function (v) { return typeof v === 'string' && v.charAt(0) === "'" ? { text: v.slice(1) } : { raw: v }; }));
    },
    setFrozenRows: function (n) { this.frozen = n; },
    getRange: function (r, c, n, m) {
      return { getDisplayValues: function () {
        return rows.slice(r - 1, r - 1 + n).map(function (row) {
          return row.slice(c - 1, c - 1 + m).map(function (v) { return String(v.text !== undefined ? v.text : v.raw); });
        });
      } };
    },
    deleteRows: function (from, count) { rows.splice(from - 1, count); }
  };
}
const sandbox = {
  SpreadsheetApp: { getActiveSpreadsheet: function () {
    return {
      getSheetByName: function (n) { return sheets[n] || null; },
      insertSheet: function (n) { sheets[n] = makeSheet(n); return sheets[n]; }
    };
  } },
  ContentService: {
    MimeType: { JSON: 'json' },
    createTextOutput: function (t) { return { text: t, setMimeType: function () { return this; } }; }
  },
  Utilities: { formatDate: function (d) { return d.toISOString().slice(0, 10); } },
  ScriptApp: {
    getProjectTriggers: function () { return triggers.map(function (t) { return { getHandlerFunction: function () { return t; }, id: t }; }); },
    deleteTrigger: function (t) { triggers = triggers.filter(function (x) { return x !== t.id; }); },
    newTrigger: function (fn) {
      const b = { timeBased: function () { return b; }, everyDays: function () { return b; }, atHour: function () { return b; },
        create: function () { triggers.push(fn); } };
      return b;
    }
  },
  JSON: JSON, Date: Date, Math: Math, String: String, Number: Number
};
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(__dirname, '..', 'stats', 'Code.gs'), 'utf8'), sandbox);

function post(body) {
  const out = sandbox.doPost(body === null ? {} : { postData: { contents: typeof body === 'string' ? body : JSON.stringify(body) } });
  return JSON.parse(out.text).ok;
}
function cells(i) {
  return sheets.data.rows[i].map(function (v) { return v.text !== undefined ? { text: v.text } : { raw: v.raw }; });
}
const good = { v: 1, id: '0123456789abcdef0123456789abcdef', program: '2.10.2', addon: '3.0.7', windows: '11', lang: 'ru', game: 'ruRU',
  mode: 'local', chars: '2-5', mapzeroth: true, day: '2026-11-13', use: { open: 2, tab_left: 5, hacked: 9, route: -1, manual: 1e9, tab_log: 1.7 } };
const r = {};
r.goodOk = post(good);
r.header = cells(0).map(function (c) { return c.raw; });
r.goodRow = cells(1);
r.frozen = sheets.data.frozen;
r.badJson = post('{not json');
r.empty = post(null);
r.noId = post(Object.assign({}, good, { id: 'short' }));
r.wrongVersion = post(Object.assign({}, good, { v: 2 }));
r.tooLong = post(Object.assign({}, good, { use: { open: 1 }, pad: new Array(3000).join('x') }));
r.rowsAfterBad = sheets.data.rows.length;
r.dirtyOk = post({ v: 1, id: good.id, program: '=HYPERLINK("x")', addon: '3.0.7<script>', windows: 'eleven', lang: 'RU', game: 'ru',
  mode: 'cloud', chars: '100', mapzeroth: 'yes', day: 'yesterday', use: 'many', name: 'Тайное Имя' });
r.dirtyRow = cells(2);
// prune: rows older than a year go, the header and fresh rows stay
const oldDay = new Date(Date.now() - 400 * 86400000).toISOString().slice(0, 10);
sheets.data.rows.splice(1, 0, [{ text: oldDay }, { text: 'old1' }], [{ text: oldDay }, { text: 'old2' }]);
r.beforePrune = sheets.data.rows.length;
sandbox.prune();
r.afterPrune = sheets.data.rows.length;
r.headerKept = cells(0)[0].raw === r.header[0];
r.oldGone = !sheets.data.rows.some(function (row) { return row[1] && (row[1].text === 'old1' || row[1].text === 'old2'); });
sandbox.setup(); sandbox.setup();
r.triggers = triggers.slice();
console.log(JSON.stringify(r));
