'use strict';
// node dump_status.js <workbook.json> <out.json> — reply of action "status" for that workbook.
// Uses the C# port of Code.gs (SheetLogic.cs) through tools/cs_host/EngineHost.exe.
const fs = require('fs');
const { execFileSync } = require('child_process');
const HOST = require('path').join(__dirname, '..', 'cs_host', 'EngineHost.exe');
const wb = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const out = JSON.parse(execFileSync('mono', [HOST], { input: JSON.stringify({ workbook: wb, body: JSON.stringify({ action: 'status' }) }), maxBuffer: 64 << 20 }).toString('utf8'));
const reply = out.reply;
fs.writeFileSync(process.argv[3], JSON.stringify(reply, null, 1));
console.log('status:', reply.chars.map(c => c.name + ' ' + c.items.length).join(', '));
