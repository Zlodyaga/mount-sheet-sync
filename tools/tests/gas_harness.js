// Runs Code.gs (Apps Script) in Node with mocked SpreadsheetApp / ContentService / LockService / PropertiesService.
// CLI: node gas_harness.js <workbook.json> <request-body-file>  -> prints the JSON reply,
// and saves the changed workbook back to <workbook.json>.
'use strict';
const fs = require('fs');
const vm = require('vm');
const path = require('path');

const CODE = process.env.MSS_CODE || path.join(__dirname, '..', '..', 'script', 'Code.gs');

function rectangular(values) {
  const width = Math.max(...values.map(r => r.length));
  return values.map(r => r.concat(Array(width - r.length).fill('')));
}

// MSS_ENGINE=cs: the same workbook and request go to the C# port (SheetLogic.cs) through cs_host/EngineHost.exe.
const CS = process.env.MSS_ENGINE === 'cs';
const HOST = require('path').join(__dirname, '..', 'cs_host', 'EngineHost.exe');

function runCs(workbook, body, opts) {
  if (opts.get || opts.unbound || opts.locked) throw new Error('Apps Script only: ' + JSON.stringify(opts));
  const { execFileSync } = require('child_process');
  const job = JSON.stringify({ workbook, body: body === undefined ? '' : body, opts: { noProps: !!opts.noProps } });
  const out = JSON.parse(execFileSync('mono', [HOST], { input: job, maxBuffer: 64 << 20 }).toString('utf8'));
  out.writes.forEach(w => {
    const s = workbook.sheets.find(x => x.name === w.sheet);
    while (s.values.length < w.row) s.values.push([]);
    const row = s.values[w.row - 1];
    while (row.length < w.col) row.push('');
    row[w.col - 1] = w.value;
  });
  if (!opts.noProps && Object.keys(out.properties).length) workbook.properties = out.properties;
  return { reply: out.reply, writes: out.writes, calls: out.calls };
}

function run(workbook, body, opts = {}) {
  if (CS) return runCs(workbook, body, opts);
  const writes = [];
  const calls = [];
  const sheets = workbook.sheets.map(s => ({
    getName: () => s.name,
    getDataRange: () => ({
      getValues: () => JSON.parse(JSON.stringify(rectangular(s.values))),
      // formulas: { "row,col": "=..." } (1-based) in the workbook JSON
      getFormulas: () => rectangular(s.values).map((row, r) => row.map((_, c) => (s.formulas && s.formulas[(r + 1) + ',' + (c + 1)]) || '')),
    }),
    getRange: (r, c, nr, nc) => {
      const put = (rr, cc, v) => {
        while (s.values.length < rr) s.values.push([]);
        const row = s.values[rr - 1];
        while (row.length < cc) row.push('');
        row[cc - 1] = v;
        writes.push({ sheet: s.name, row: rr, col: cc, value: v });
      };
      return {
        setValue: v => { calls.push(1); put(r, c, v); },
        setValues: vals => {
          calls.push(vals.length);
          if (vals.length !== (nr || 1) || vals.some(row => row.length !== (nc || 1))) throw new Error('setValues size mismatch');
          vals.forEach((row, i) => row.forEach((v, j) => put(r + i, c + j, v)));
        },
      };
    },
  }));
  const ss = { getName: () => workbook.name, getSheets: () => sheets };
  const ctx = {
    SpreadsheetApp: { getActiveSpreadsheet: () => (opts.unbound ? null : ss), flush: () => {} },
    ContentService: {
      MimeType: { JSON: 'application/json' },
      createTextOutput: text => ({ text, mime: null, setMimeType(m) { this.mime = m; return this; } }),
    },
    LockService: { getScriptLock: () => ({ tryLock: () => !opts.locked, releaseLock: () => {} }) },
    // script properties live in the workbook JSON (workbook.properties), so they survive between requests
    PropertiesService: {
      getScriptProperties: () => {
        if (opts.noProps) throw new Error('Service unavailable: Properties');
        const props = workbook.properties || (workbook.properties = {});
        const store = {
          getProperty: k => (Object.prototype.hasOwnProperty.call(props, k) ? String(props[k]) : null),
          setProperty: (k, v) => { props[k] = String(v); return store; },
        };
        return store;
      },
    },
  };
  vm.createContext(ctx);
  vm.runInContext(fs.readFileSync(CODE, 'utf8'), ctx, { filename: 'Code.gs' });
  const out = opts.get ? ctx.doGet({}) : ctx.doPost(body === undefined ? undefined : { postData: { contents: body, type: 'application/json' } });
  if (out.mime !== 'application/json') throw new Error('reply is not JSON mime');
  return { reply: JSON.parse(out.text), writes, calls };
}

module.exports = { run, CS };

if (require.main === module) {
  const wbPath = process.argv[2];
  const body = fs.readFileSync(process.argv[3], 'utf8');
  const wb = JSON.parse(fs.readFileSync(wbPath, 'utf8'));
  const { reply } = run(wb, body);
  fs.writeFileSync(wbPath, JSON.stringify(wb, null, 1));
  process.stdout.write(JSON.stringify(reply));
}
