# Frontend Plan — v4 (2026-03-23)
### Priority-ordered implementation plan for all requested features

---

## BUGS (fix first)

---

### BUG 1 — Weather not updating on season change

**Observed:** Dashboard shows Temperate even during confirmed Drought and Badtide.

**Diagnosis:** The backend reads `HazardousWeatherService.CurrentCycleHazardousWeather` at
request time. This is a singleton property that should update at cycle start. The frontend
displays `S.gameState.isDrought` / `S.gameState.isBadtide` which come from the poll.
The frontend display logic is correct. This is a **backend issue** — either:
- The property name is wrong or unavailable in the current game version
- The singleton reference is stale (captured at Load() time, service updated a new internal
  instance at season transition)
- The `hazard.Id` string comparison uses the wrong casing or value

**Frontend action:** None needed until backend confirms fix.
**Backend action needed:** Probe `HazardousWeatherService` in the live game.
See bot_chat.md for the specific question.

---

### BUG 2 — Disabled/deleted automation rules still firing

**Observed:** Levers turn ON every 3 seconds after user disables all automation. ui_log shows
TWO RULE entries per poll cycle for the same lever. User manually sets lever OFF, it goes ON
after ~1 second.

**Root causes (multiple):**

**A. Direct mode fires every poll cycle, not on rising edge.**
`runAutomation()` with `functionType:'direct'` calls `applyActions()` on EVERY cycle while
conditions are true. The guard `if(lever.state===wantOn)return` should prevent repeated POSTs,
BUT the poll merge overwrites `lever.state` from the API response each cycle. If the API
reports the lever as OFF (spring-return, game state mismatch, or API lag), the guard fails
every single cycle and the lever keeps being toggled.

**Frontend fix (immediate):** After `applyActions` optimistically sets `lever.state=true`,
also set a `lever._lastSetState` flag. The poll merge should NOT overwrite `lever.state` if
`lever._lastSetState` was set within the last 10 seconds. This prevents the guard from being
defeated by API latency.

**B. Dual-SPA automation conflict (still unresolved).**
The inline TimberAutoControl SPA (still injected by backend) runs its own `runAutomation()`
every 3 seconds using `localStorage['tac_rules']`. If the user ever created rules through the
inline SPA's "Add Rule" form, those rules persist independently and fire in parallel. Our
SPA's uiLog won't show SPA1's actions, but SPA1 could be turning levers ON/OFF anyway.

**Frontend fix:** When our `TAC.delRule()` or `TAC.toggleRule()` is called, also clear
`localStorage.removeItem('tac_rules')` as cleanup. This prevents SPA1 from having its own
rule copy.

**C. Two RULE entries per cycle = two `runAutomation()` calls.**
Two entries at the same second in the log means two executions. Most likely: the rule has
TWO actions, OR there are TWO rules matching (user created duplicates). Confirm by checking
the rule count in the log (SYNC entries showed "3 rules" at one point → could be 2 rules
both targeting the same lever).

**Frontend fix:** Add deduplication to `applyActions` — if the same lever was POSTed within
the last 5 seconds, skip. Use a `S.leverLastAction = {}` timestamp map.

---

## FEATURE 1 — Automation rules: fix "direct" mode behavior

**Problem:** Direct mode fires every 3 seconds while true. User expects "set ON when
condition becomes true, leave it alone otherwise."

**Plan:** Add a mode called **"enforce"** (current direct behavior — keep trying every cycle)
vs **"trigger"** (only fire on RISING EDGE of condition becoming true — i.e., when previously
false and now true). Rename "Direct" to "Trigger" in the UI. Keep "Enforce" as an opt-in.

This also solves the "lever fights back" loop: with Trigger mode, automation fires once, sets
the lever, and doesn't touch it again until the condition goes false → true again.

**Reset latch — make automatic:**
Current latch requires a manual "Reset Latch" button click. Add an optional second condition
group labelled "RESET when" that resets the latch automatically when true. In the Simple
editor, show a second condition row below the existing ones labeled "Reset condition (optional)".

---

## FEATURE 2 — FBD improvements

### 2a. Larger, easier hit targets for connection pins

Current pin radius: `FBD_PR = 6`. Increase to `FBD_PR = 10` for hit detection while keeping
the visual circle at 6px. This gives a 10px invisible hit area around each pin without
changing how the diagram looks.

Additionally: when hovering within 15px of a pin, show a highlight ring (strokeStyle cyan,
lineWidth 2) as affordance feedback. Track mouse position in `FBD.mouse` (already done) and
check in `fbdDrawCanvas()` if cursor is within range of any pin — draw the highlight.

### 2b. Inline node configuration (click directly on node)

Instead of a sidebar, parameters appear as an overlay below the selected node directly on
the canvas. When `FBD.selected` is set, draw a parameter panel as part of the canvas using
`fillRect` + `fillText` + click-area overlays. No HTML sidebar needed.

The sidebar div `#tac-fbd-sidebar` becomes a small "node library" panel only (add nodes).
Parameter editing happens on-canvas.

Implementation: track a `FBD.paramInputs = []` array of `{x, y, w, h, nodeId, key, type,
value}` hit rects drawn below the selected node. On mousedown, check paramInputs first before
node/pin detection. On hit: prompt user via a small inline `<input>` element positioned
absolutely over the canvas, pre-filled with current value, confirmed on Enter/blur.

### 2c. Lever OUTPUT node: auto spring-return, single input

Current lever output requires two nodes (one for ON, one for OFF). Replace with a single
`OUTPUT_LEVER` that:
- Sets lever ON when its `IN` input is HIGH
- Sets lever OFF when its `IN` input is LOW (automatic inversion)
- Has a **"Spring return"** param (default: ON) — when true, posts switchOff after 200ms
  delay when IN goes LOW, mimicking spring-return behavior without relying on the in-game
  spring-return system

This makes the common case (one condition controls one lever) need only one output node.

### 2d. New constant input nodes

- **ALWAYS HIGH** — output permanently TRUE. Use case: force a latch SET input.
- **ALWAYS LOW** — output permanently FALSE. Use case: test wiring, debug.

### 2e. New logic blocks

| Block | Behavior |
|-------|----------|
| NAND | NOT(A AND B) — correct Boolean NAND |
| NOR | NOT(A OR B) — correct Boolean NOR |
| On Delay (TON) | Already exists as LOGIC_TIMER — rename to "On Delay" for clarity |
| Off Delay (TOF) | Output goes HIGH immediately on IN=HIGH; stays HIGH for T seconds after IN goes LOW |
| Wiping Relay (TP) | Fires a pulse of length T when IN goes LOW→HIGH (one-shot) |
| Async Generator | Free-running oscillator — output HIGH for T_on, LOW for T_off, repeat |
| Up-Down Counter | Counts UP pulses on CU input, DOWN on CD input; output HIGH when count >= preset |
| Real-Time Clock | Uses wall-clock time (browser Date), fires HIGH during configured time window |

**Note:** All of these are pure JS state machines — no backend changes needed. Each adds a new
entry to FBD_DEFS and a case in `runFbdRule()`'s evaluation switch.

---

## FEATURE 3 — Code editor: state snapshot + AI-friendly clipboard button

### Current state sidebar
Show a read-only panel to the left of the code editor listing all current input/output names
and their live values, updated from `S.levers` and `S.adapters` without a full re-render:

```
ADAPTERS (inputs)
  HTTP Adapter 1  OFF
  HTTP Adapter 2  ON

LEVERS (outputs)
  HTTP Lever 1  OFF
  HTTP Lever 2  ON
```

### AI agent clipboard button
Add a button "📋 Copy for AI" next to the Save button. It copies a structured prompt to the
clipboard containing:

```
# HTTPAutomation Code Rule Context

## Available Adapters (inputs, read-only)
adapters = [
  { name: "HTTP Adapter 1", state: false },
  { name: "HTTP Adapter 2", state: true }
]

## Available Levers (outputs)
levers = [
  { name: "HTTP Lever 1", state: false },
  { name: "HTTP Lever 2", state: false }
]

## Game State (if DLL loaded)
gameState = { cycleNumber: 1, dayNumber: 5, isDay: true, isDrought: false, ... }

## Population (if DLL loaded)
population = { totalBeavers: 29, adults: 20, children: 9, unemployed: 0, ... }

## API available in script
setLever(name, 'on'|'off')  — controls a lever
log(msg)                    — appears in console below editor

## Your current script
[current script content here]

## Task
[blank for user to fill in]
```

---

## FEATURE 4 — Simple rule layout redesign

**Current:** vertical stacked sections (Conditions top, Function Type middle, Actions bottom)
**New:** horizontal left-to-right flow matching how logic naturally reads

Layout:
```
[INPUT CONDITIONS]  →  [LOGIC BLOCK]  →  [OUTPUT ACTIONS]
   Adapter is ON         Direct              Set Lever ON
   isDay                 Timer: 10s
                         Latch ↺ reset when:
                           Adapter is OFF
```

Each column is a card. Conditions and Actions support multiple rows with + buttons.
The Logic Block column shows the function type selector inline.

**More operators for conditions:**
Currently: adapter state (on/off), gamestate (is/isn't), time range, population (>, <, =, ≥, ≤)

Add:
- **Adapter was ON for N seconds** (duration-based)
- **Population rate of change** (growing/shrinking)
- **Day of cycle** (numeric, for cycle-based rules)

---

## FEATURE 5 — In-game sensor data passthrough

This is a large backend feature. The frontend plan is documented here; backend questions are
in bot_chat.md.

**What the sensors expose (from wiki research):**

| Sensor | Data | On/Off signal |
|--------|------|---------------|
| Flow Sensor | Flow rate (m³/s), comparison operator, threshold | Fires when condition met |
| Depth Sensor | Fluid depth (m), comparison operator, threshold | Fires when condition met |
| Contamination Sensor | Contamination % (0-100), comparison operator, threshold | Fires when condition met |
| Chronometer | Mode (time range/work time/leisure time), time window | Fires during configured time |
| Weather Station | Season selection (any/drought/badtide/temperate) | Fires during selected season |
| Power Meter | Supply (HP), demand (HP), surplus (HP), battery charge % | Fires when condition met |
| Population Counter | Count target (all/adults/children/idle), comparison | Fires when condition met |
| Resource Counter | Resource type, stock level or fill rate, comparison | Fires when condition met |
| Science Counter | Science point count, comparison | Fires when condition met |

**Output buildings:**

| Building | What we can control |
|----------|---------------------|
| Indicator | ON/OFF + color (hex) via automation rules |
| Speaker | ON/OFF + sound selection (need backend to expose sound list) |
| Firework Launcher | ON/OFF + animation type (need backend to expose options) |
| Detonator | ON/OFF (arms/triggers dynamite) |

**Frontend additions needed when backend exposes `/api/sensors`:**

1. A new "Sensors" section in the dashboard showing all named sensors with their current
   values and ON/OFF state
2. Sensors as condition inputs in Simple rules:
   `[Flow Sensor "Inlet"] [>] [0.5 m³/s]`
3. Sensor input nodes in FBD (`INPUT_SENSOR` — generic, parameterized by sensor name)
4. Output buildings in Simple rule actions:
   `[Set Indicator "Status Light"] [color: #00ff00]`
5. Output nodes in FBD (`OUTPUT_INDICATOR`, `OUTPUT_SPEAKER`, `OUTPUT_FIREWORK`, `OUTPUT_DETONATOR`)

**Spring return for outputs (all output buildings):**
Indicator, Speaker, Firework Launcher, Detonator should all support spring-return in our rules
— send ON when condition true, send OFF when condition false. The Detonator is one-shot by
nature (sending ON fires it, no "off" needed), but for web-side spring-return we'd just track
state and not re-fire.

---

## FEATURE 6 — Welcome popup (welcome.json)

On startup, `takeover()` checks for `GET /api/welcome`. If the endpoint returns data, display
a modal before switching to the dashboard.

**welcome.json** in mod folder:
```json
{
  "_comment": "Delete this file to remove welcome text",
  "title": "Welcome to HTTPAutomation",
  "text": "Your custom message here.\nSupports newlines."
}
```

Backend serves `GET /api/welcome` — reads the file, returns `{"title":"...","text":"..."}` or
404 if file not found. Frontend shows modal only once per page load.

Modal HTML (inline styles, no Bootstrap dependency):
- Dark overlay
- Card with title, text (pre-wrap for newlines), and an OK button
- Dismissed by clicking OK or clicking overlay

---

## FEATURE 7 — Skills library

The user uploaded skills packages. These should be installed to the skills directory for use
in future sessions. This is a workspace setup task — unzip and read SKILL.md files.

---

## Implementation order

1. ✅ **Bug 2 fix** — deduplication guard + rising-edge trigger mode + old localStorage cleanup
2. ⚠️ **Bug 1** — backend root cause pending (weatherId returns "Temperate" during drought); frontend shows "Hazardous" as workaround
3. ✅ **Feature 1** — Trigger mode, Enforce mode, auto-reset latch with resetConditions[]
4. ✅ **Feature 6** — welcome.json popup (frontend done; backend /api/welcome endpoint pending)
5. ✅ **Feature 3** — code editor: live state sidebar + AI clipboard button
6. ✅ **Feature 2c** — OUTPUT_LEVER spring-return (auto-OFF when IN goes LOW)
7. ✅ **INPUT_LEVER** — FBD input node + Simple rule lever condition
8. ✅ **Feature 2a** — larger FBD pin hit targets (12px) + cyan hover highlight
9. ✅ **Feature 2d** — ALWAYS_HIGH / ALWAYS_LOW constant input nodes
10. ✅ **Feature 2e** — 7 new logic blocks: NAND, NOR, TOF, TP, Generator, Counter, Real-Time Clock
11. ✅ **Feature 2b** — inline on-canvas node parameter editing (click node to configure, no sidebar)
12. ✅ **Feature 4** — simple rule layout redesign (horizontal left-to-right) + 3 new condition types (duration, popchange, daycycle)
13. **NEXT → Feature 5** — sensor passthrough (blocked on backend /api/sensors)
