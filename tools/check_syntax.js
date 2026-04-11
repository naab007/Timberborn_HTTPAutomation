// HTTPAutomation JS syntax checker — run with: node check_syntax.js
// Uses Node's vm.Script for exact line/column error reporting.
// Exit 0 = clean. Exit 1 = syntax error (line number + context printed).

const fs  = require('fs');
const vm  = require('vm');
const path = require('path');

const hbsPath = path.join(__dirname, '..', 'HttpApi', 'index.hbs');
let src = fs.readFileSync(hbsPath, 'utf8');
src = src.replace(/^\s*<script[^>]*>\s*/i, '').replace(/\s*<\/script>\s*$/i, '');

// ── Syntax check ──────────────────────────────────────────────────────────────
let syntaxOk = true;
try {
  new vm.Script(src, { filename: 'index.hbs' });
} catch (e) {
  syntaxOk = false;
  const m = e.stack && e.stack.match(/index\.hbs:(\d+)/);
  const errLine = m ? parseInt(m[1]) : null;
  console.error('SYNTAX ERROR: ' + e.message);
  if (errLine) {
    const lines = src.split('\n');
    console.error('At line ' + errLine + ':');
    for (let i = Math.max(0, errLine - 4); i < Math.min(lines.length, errLine + 2); i++) {
      const marker = i === errLine - 1 ? '>>>' : '   ';
      console.error(marker + ' ' + (i + 1) + ': ' + lines[i].substring(0, 160));
    }
  }
}

if (!syntaxOk) process.exit(1);

// ── Structural checks ─────────────────────────────────────────────────────────
const checks = [
  // Core guards
  ["__TAC__ guard present",            src.includes("if(window.__TAC__)return;")],
  ["window.TAC = { present",           src.includes("window.TAC = {")],
  ["__TAC__ guard BEFORE window.TAC",  src.indexOf("if(window.__TAC__)return;") < src.indexOf("window.TAC = {")],
  // Critical methods
  ["_removeCondition:function",        src.includes("_removeCondition:function")],
  ["_addResetCond:function",           src.includes("_addResetCond:function")],
  ["_copyForAI:function",              src.includes("_copyForAI:function")],
  // Mode guards in runAutomation
  ["runAutomation simple guard",       src.includes("if(rule.mode==='simple')")],
  ["runAutomation code guard",         src.includes("if(rule.mode==='code')")],
  ["runAutomation fbd guard",          src.includes("if(rule.mode==='fbd')")],
  // FBD node types
  ["ALWAYS_HIGH in FBD_DEFS",          src.includes("ALWAYS_HIGH:")],
  ["ALWAYS_LOW in FBD_DEFS",           src.includes("ALWAYS_LOW:")],
  ["LOGIC_NAND in FBD_DEFS",           src.includes("LOGIC_NAND:")],
  ["LOGIC_NOR in FBD_DEFS",            src.includes("LOGIC_NOR:")],
  ["LOGIC_TOF in FBD_DEFS",            src.includes("LOGIC_TOF:")],
  ["LOGIC_TP in FBD_DEFS",             src.includes("LOGIC_TP:")],
  ["LOGIC_GEN in FBD_DEFS",            src.includes("LOGIC_GEN:")],
  ["LOGIC_CTU in FBD_DEFS",            src.includes("LOGIC_CTU:")],
  ["LOGIC_RTC in FBD_DEFS",            src.includes("LOGIC_RTC:")],
  ["INPUT_SENSOR in FBD_DEFS",         src.includes("INPUT_SENSOR:")],
  // Engine cases
  ["case ALWAYS_HIGH in engine",       src.includes("case'ALWAYS_HIGH'")],
  ["case LOGIC_NAND in engine",        src.includes("case'LOGIC_NAND'")],
  ["case LOGIC_TOF in engine",         src.includes("case'LOGIC_TOF'")],
  ["case LOGIC_RTC in engine",         src.includes("case'LOGIC_RTC'")],
  ["case INPUT_SENSOR in engine",      src.includes("case'INPUT_SENSOR'")],
  // FBD hit radius
  ["FBD_PR_HIT defined",               src.includes("FBD_PR_HIT")],
  ["FBD_PR_HIT used in fbdPinAt",      src.includes("<FBD_PR_HIT)return{")],
  // Snap to pin
  ["fbdSnapTarget function",           src.includes("function fbdSnapTarget")],
  ["FBD_SNAP_RADIUS constant",         src.includes("FBD_SNAP_RADIUS")],
  // Dedup
  ["_leverLastAction dedup map",       src.includes("var _leverLastAction")],
  ["DEDUP_MS constant",                src.includes("var DEDUP_MS")],
  // Trigger mode
  ["ft==='trigger' branch",            src.includes("ft==='trigger'")],
  // Welcome modal
  ["showWelcomeModal function",        src.includes("function showWelcomeModal")],
  ["get('/api/welcome')",              src.includes("get('/api/welcome')")],
  // Sensors
  ["S.sensors in state",               src.includes("sensors:        []")],
  ["get('/api/sensors') in poll",      src.includes("get('/api/sensors')")],
  ["sensor condition type",            src.includes("cond.type==='sensor'")],
  ["sensors in code sandbox",          src.includes("'sensors',")],
  ["renderSensors function",           src.includes("function renderSensors")],
  // Poll safety
  ["poll try-catch present",           src.includes("} catch(pollErr) {")],
];

let passed = 0, failed = 0;
console.log('\n=== HTTPAutomation Structure Check ===');
for (const [name, result] of checks) {
  if (result) { console.log('OK  ' + name); passed++; }
  else         { console.error('FAIL ' + name); failed++; }
}

console.log(`\n${failed ? '!!! ' + failed + ' FAILURES !!!' : 'All ' + passed + ' checks passed.'}`);
process.exit(failed > 0 ? 1 : 0);
