# HTTPAutomation — Hooks Reference

All callable surfaces in the mod, current as of v5.4.0.

---

## 1. HTTP API Endpoints

Served by the game's built-in HTTP API. Base URL: `http://localhost:{port}/`
The port changes each session — check the game settings or `Player.log`.
All endpoints return JSON with CORS headers (`Access-Control-Allow-Origin: *`).

---

### GET /api/ping
Health check. Always responds even before a save is loaded.

**Response**
```json
{ "ok": true, "mod": "HTTPAutomation", "version": "2.5", "ready": true }
```
`ready` is `false` until `GameServicesInitializer.Load()` has run (i.e. a save has been loaded).

---

### GET /api/gamestate
Current game time, weather, and active save name.
Returns `503` if no save is loaded yet.

**Response**
```json
{
  "cycleNumber": 3,
  "dayNumber": 199,
  "dayProgress": 0.8512,
  "timeOfDayHours": 19.96,
  "dayStage": "Nighttime",
  "isDay": false,
  "isNight": true,
  "weather": "Temperate",
  "isDrought": false,
  "isBadtide": false,
  "isHazardous": false,
  "saveName": "MySave"
}
```

| Field | Type | Notes |
|---|---|---|
| `cycleNumber` | int | Seasons completed |
| `dayNumber` | int | Absolute day count |
| `dayProgress` | float 0–1 | Fraction of current day elapsed |
| `timeOfDayHours` | float | Hours since midnight (0–24) |
| `dayStage` | string | `"Sunrise"` / `"Daytime"` / `"Sunset"` / `"Nighttime"` |
| `isDay` / `isNight` | bool | |
| `weather` | string | `"Temperate"` / `"Drought"` / `"Badtide"` |
| `isDrought` / `isBadtide` / `isHazardous` | bool | |
| `weatherId` | string | Actual stage ID — matches vanilla or custom weather (e.g. `"Monsoon"`, `"Rain"`) |
| `weatherIsHazardous` | bool | True for any hazardous stage (not just Drought/Badtide) |
| `weatherDaysInStage` | int | Total days this weather stage lasts (0 without ModdableWeathers) |
| `weatherDaysSinceStart` | int | Days elapsed since this stage began |
| `weatherDaysRemaining` | int | Days left in this stage |
| `nextWeatherId` | string | Next stage's weather ID (empty without ModdableWeathers) |
| `moddableWeather` | bool | True when the ModdableWeathers workshop mod is active |
| `saveName` | string | File name of the loaded save; empty string if no save is loaded |

---

### GET /api/population
Aggregate population counts via `PopulationService.GlobalPopulationData`.
Returns `503` if no save is loaded yet.

**Response**
```json
{
  "totalPopulation": 20,
  "totalBeavers": 18,
  "adults": 16,
  "children": 2,
  "bots": 2,
  "unemployed": 0,
  "homeless": 0,
  "injured": 0,
  "contaminated": 0,
  "averageHunger": 0.82,
  "averageThirst": 0.71,
  "averageSleep": 0.90,
  "averageWellbeing": 9,
  "maxWellbeing": 14
}
```

| Field | Type | Notes |
|---|---|---|
| `averageHunger/Thirst/Sleep` | float | Averaged across live adult beaver list (need points scale) |
| `averageWellbeing` | int | `WellbeingService.AverageGlobalWellbeing` — game pre-computed |
| `maxWellbeing` | int | `WellbeingLimitService.MaxBeaverWellbeing` — colony-wide cap |

---

### GET /api/beavers
Returns all beavers currently alive in the settlement.
Returns `503` if no save is loaded yet.

**Response** — array of beaver objects:
```json
[
  {
    "id": "12345678",
    "name": "Timber",
    "ageInDays": 42,
    "isAdult": true,
    "hunger": 0.85,
    "thirst": 0.60,
    "sleep": 0.90,
    "injury": 0.00,
    "contamination": 0.00,
    "isInjured": false,
    "isContaminated": false,
    "isHungry": false,
    "isThirsty": false,
    "hasJob": true,
    "hasHome": true,
    "workplace": "Lumberjack Flag",
    "wellbeing": 8,
    "maxWellbeing": 14,
    "needs": [
      {
        "id": "Hunger",
        "name": "Need.Hunger",
        "wellbeingNow": 2,
        "wellbeingMax": 2,
        "wellbeingBad": 0,
        "isFavorable": true,
        "points": 0.85
      },
      {
        "id": "Coffee",
        "name": "Need.Coffee",
        "wellbeingNow": 3,
        "wellbeingMax": 3,
        "wellbeingBad": 0,
        "isFavorable": true,
        "points": 0.95
      }
    ]
  }
]
```

| Field | Type | Notes |
|---|---|---|
| `wellbeing` | int | Current wellbeing score from `WellbeingTracker.Wellbeing` |
| `maxWellbeing` | int | Per-beaver max from `WellbeingLimitService.GetMaxWellbeing()` |
| `needs` | array | All needs where `AffectsWellbeing=true` AND `NeedIsActive=true` for this beaver |
| `needs[].id` | string | Need spec ID, e.g. `"Hunger"`, `"Coffee"` |
| `needs[].name` | string | Localization key, e.g. `"Need.Hunger"` — strip `"Need."` prefix for display |
| `needs[].wellbeingNow` | int | Current contribution — `NeedManager.GetNeedWellbeing(id)` |
| `needs[].wellbeingMax` | int | Max contribution when favorable — `NeedSpec.GetFavorableWellbeing()` |
| `needs[].wellbeingBad` | int | Contribution when unfavorable (≤0) — `NeedSpec.GetUnfavorableWellbeing()` |
| `needs[].isFavorable` | bool | Whether this need is currently satisfied |
| `needs[].points` | float | Raw need points from `NeedManager.GetNeedPoints(id)` |

**Dynamic max wellbeing:** Sum `needs[].wellbeingMax` across all entries for the per-beaver actual maximum (varies by mod, species, available buildings). The top-level `maxWellbeing` is the game's own calculation.

**Need name display:** `n.name.replace(/^Need\./, '')` strips the localization prefix.

The list is maintained by `[OnEvent]` handlers on `CharacterCreatedEvent` / `CharacterKilledEvent`. `id` is a runtime hash — stable per session, not persisted.

---

### GET /api/automation?save=\<saveName\>
Load the stored automation rule set for a given save name.
Returns `{"rules":[]}` if no rules have been saved yet for that name.
Returns `400` if `save` param is missing.
Returns `503` if no save is loaded.

**Query params**
| Param | Required | Notes |
|---|---|---|
| `save` | yes | The save file name (matches `saveName` from `/api/gamestate`) |

**Response** — whatever was last POSTed, or the default:
```json
{ "rules": [] }
```

Files are stored at `<ModDirectory>/automation_saves/<saveName>.json`.
The save name is sanitised (invalid filename characters replaced with `_`) before use as a filename.

---

### POST /api/automation?save=\<saveName\>
Persist an automation rule set for a given save name.
The request body should be the JSON you want stored (typically `{"rules": [...]}`).
Returns `400` if `save` param is missing or body is empty.
Returns `503` if no save is loaded.

**Request body**: raw JSON string (Content-Type not strictly required but good practice to set `application/json`).

**Response**
```json
{ "ok": true }
```

The `automation_saves/` directory is created automatically on first write.

---

### GET /api/levers
Returns all HTTP Lever buildings. Enriched by our `LeverEndpoint` (intercepts the stock route).

**Response** — array of lever objects:
```json
[
  {
    "name": "HTTP Lever 1",
    "state": false,
    "isSpringReturn": false,
    "switchOnUrl":  "/api/switch-on/HTTP%20Lever%201",
    "switchOffUrl": "/api/switch-off/HTTP%20Lever%201",
    "colorUrl":     "/api/color/HTTP%20Lever%201/{color}"
  }
]
```

`colorUrl` is a URL template — replace `{color}` with any 6-char hex string before POSTing:
```js
// Turn lever red:   lever.colorUrl.replace('{color}', 'ff0000')
// Turn lever blue:  lever.colorUrl.replace('{color}', '3a7bff')
// Custom purple:    lever.colorUrl.replace('{color}', '7b2cf8')
```
Any valid 6-char HTML hex is accepted. The game's own `POST /api/color/<n>/<hex>` endpoint
executes the actual in-game model recoloring — our endpoint only exposes the URL template.

---

### GET /api/adapters
Served by the stock HTTP API. Returns all HTTP Adapter buildings.

**Response** — array of adapter objects:
```json
[
  { "name": "MyAdapter", "state": true }
]
```

---

### POST /api/levers/\<name\>/on
### POST /api/levers/\<name\>/off
### POST /api/levers/\<name\>/red
### POST /api/levers/\<name\>/green
Stock endpoints. Turn a named lever on, off, red (disabled), or green (enabled).

---

### GET /api/welcome
Returns a welcome message to display on first load. Frontend calls this once at startup.

- Returns `200` + `{"title":"...","text":"..."}` if `welcome.json` exists in mod folder.
- Returns `404` if the file does not exist (frontend ignores silently — no popup shown).

**welcome.json** — place in `<ModDirectory>/`:
```json
{
  "_comment": "Delete this file to remove the welcome popup",
  "title": "Welcome to HTTPAutomation",
  "text": "Your message here.\nSupports \\n newlines."
}
```

`\n` in the `text` field is decoded to real newlines — frontend can render with `white-space: pre-wrap`.

---

### GET /api/sensors
Returns all automation transmitter buildings (sensors) currently placed in the map.
Returns `503` if no save is loaded.

**Response**
```json
{
  "sensors": [
    {
      "id":     "a1b2c3d4-...",
      "name":   "Inlet Flow",
      "type":   "FlowSensor",
      "unit":   "m³/s",
      "isOn":   true,
      "value":  null
    },
    {
      "id":     "...",
      "name":   "Reservoir",
      "type":   "DepthSensor",
      "unit":   "m",
      "isOn":   false,
      "value":  null
    }
  ]
}
```

| Field | Type | Notes |
|---|---|---|
| `id` | string | `Automator.AutomatorId` — stable GUID string per session |
| `name` | string | User-assigned building name from `Automator.AutomatorName` |
| `type` | string | Detected from Spec component on the building's GameObject |
| `unit` | string | Hardcoded per sensor type — `"m³/s"`, `"m"`, `"%"`, `"HP"`, etc. |
| `isOn` | bool | Current output signal from `Automator.State` (the game's automation signal) |
| `value` | null | Numeric measurement value — not yet accessible (internal game type) |

**Sensor types and units:**

| `type` | `unit` | Building |
|---|---|---|
| `FlowSensor` | `m³/s` | Flow Sensor |
| `DepthSensor` | `m` | Depth Sensor |
| `ContaminationSensor` | `%` | Contamination Sensor |
| `Chronometer` | `` | Chronometer |
| `WeatherStation` | `` | Weather Station |
| `PowerMeter` | `HP` | Power Meter |
| `PopulationCounter` | `beavers` | Population Counter |
| `ResourceCounter` | `goods` | Resource Counter |
| `ScienceCounter` | `pts` | Science Counter |
| `Memory` | `` | Memory |
| `Relay` | `` | Relay |
| `Unknown` | `` | Unrecognised building |

**Note:** `value` is always `null` in v5.5.0. Numeric measurement values live in internal component types not yet accessible via reflection. The `isOn` boolean is the same signal the game's own automation wiring uses — it is ON when the sensor's threshold condition is currently met.

---

### GET /api/debug
Diagnostic endpoint. Returns a JSON array of every singleton type name currently registered in Timberborn's DI container (`ISingletonRepository.GetSingletons<object>()`). Useful for probing what services are available.

```json
["DayNightCycle", "GameCycleService", "WeatherService", ...]
```

---

### GET /api/log?lines=N
Returns the tail of the backend `debug.log` file as JSON. Useful for reading backend events from the UI without needing filesystem access.

**Query params**

| Param | Default | Max | Notes |
|---|---|---|---|
| `lines` | 200 | 2000 | Number of lines to return from the end of the file |

**Response**
```json
{
  "lines": [
    "12:34:56.789 [INFO ] GameServicesInitializer.Load() — complete",
    "12:34:57.001 [INFO ] Beaver added: \"Timber\" — total: 1"
  ],
  "totalLines": 1450,
  "file": "C:\\...\\HTTPAutomation\\debug.log"
}
```

Returns `{"lines":[],"totalLines":0,"file":"..."}` if `debug.log` doesn't exist yet.
Reads the file under `ModLog.FileLock` to avoid torn reads during concurrent writes.

---

### POST /api/log
Saves a frontend UI log snapshot to `ui_log.txt` in the mod directory. Called by the frontend "Save log" button.

**Request body:** plain text or JSON string  
**Response:** `{"ok": true}`

---

### GET /automation.js
Serves `HttpApi/index.hbs` as raw JavaScript (`application/javascript`).
The `<script>` / `</script>` wrapper is stripped before serving.
Editing `index.hbs` and refreshing the browser takes effect immediately — no DLL recompile needed.

---

## 2. JavaScript Frontend API (`window.TAC`)

All public callbacks are exposed on `window.TAC`. They can be called from the browser console while the UI is open.

---

### Lever control

```js
TAC.on(name)      // Turn lever ON
TAC.off(name)     // Turn lever OFF
TAC.red(name)     // Set lever to Red (disabled state)
TAC.green(name)   // Set lever to Green (enabled state)
```

`name` — string matching the lever's in-game building name.
Each call POSTs to the appropriate stock lever endpoint and re-renders the UI on success.

---

### Population

```js
TAC.dismiss(id)   // Dismiss a beaver from their job
```

`id` — string, the runtime hash-based ID returned by `/api/beavers` (currently always empty, reserved for future use).

---

### Rule list

```js
TAC.toggleRule(id)        // Enable / disable a rule by id
TAC.delRule(id)           // Delete a rule by id
TAC.editRule(id)          // Open the rule editor for an existing rule
TAC.newRule()             // Create a blank Simple rule and open the editor
TAC.moveRule(id, dir)     // Reorder — dir: -1 (up) or +1 (down)
TAC.exportRules()         // Copy all rules as JSON to clipboard
TAC.toggleImport()        // Show / hide the import panel
TAC.importRules()         // Parse textarea content and replace all rules
```

---

### Rule editor

```js
TAC.cancelEdit()          // Discard changes and return to rule list
TAC.saveEdit(id)          // Commit name + mode changes and return to list
TAC._setMode(id, mode)    // Switch rule mode: 'simple' | 'fbd' | 'code'
```

---

### Simple mode editor

All of these save immediately and re-render the editor.

```js
TAC._addCondition(id, type)
// Append a condition. type: 'adapter' | 'gamestate' | 'time' | 'population'

TAC._removeCondition(id, index)
// Remove condition at zero-based index

TAC._updateCondition(id, index, key, value)
// Update a single field on a condition.
// Keys vary by type:
//   adapter:    adapterName (string), adapterState ('on'|'off')
//   gamestate:  field ('isDay'|'isNight'|'isDrought'|'isBadtide'|'isHazardous')
//   time:       fromHour (number), toHour (number)
//   population: field, op ('gt'|'gte'|'lt'|'lte'|'eq'), threshold (number)

TAC._setCondMode(id, mode)
// Set AND/OR combining logic. mode: 'and' | 'or'

TAC._setFuncType(id, ft)
// Set function type. ft: 'direct' | 'timer' | 'latch'

TAC._setTimerSec(id, seconds)
// Set timer duration in seconds (only relevant when functionType is 'timer')

TAC.resetLatch(id)
// Reset SR-Latch so the rule can fire again (only relevant for 'latch' mode)

TAC._addAction(id)
// Append a lever action (defaults to first available lever, ON)

TAC._removeAction(id, index)
// Remove action at zero-based index

TAC._updateAction(id, index, key, value)
// Update a field on an action. Keys: leverName (string), leverState ('on'|'off')
```

---

### Code mode editor

```js
TAC._saveCode(id)
// Read tac-code-textarea and persist its value to the rule (called on every keystroke)

TAC._clearLog(id)
// Clear the code console log for a rule
```

---

### FBD editor

```js
TAC._fbdAddNode(ruleId, type)
// Add a node of the given type to the FBD canvas.
// Types: INPUT_ADAPTER, INPUT_GAMESTATE, INPUT_TIME, INPUT_POPULATION,
//        LOGIC_AND, LOGIC_OR, LOGIC_NOT, LOGIC_TIMER, LOGIC_SRLATCH, LOGIC_COMPARE,
//        OUTPUT_LEVER

TAC._fbdDeleteNode(ruleId, nodeId)
// Remove a node and all its connected edges by node id

TAC._fbdUpdateParam(ruleId, nodeId, key, value)
// Update a parameter on a node. Valid keys depend on node type:
//   INPUT_ADAPTER:    adapterName, adapterState ('on'|'off')
//   INPUT_GAMESTATE:  field ('isDay'|'isNight'|'isDrought'|'isBadtide'|'isHazardous')
//   INPUT_TIME:       fromHour, toHour (numbers)
//   INPUT_POPULATION: field, op, threshold
//   LOGIC_TIMER:      preset (seconds, number)
//   LOGIC_COMPARE:    field, op, threshold
//   OUTPUT_LEVER:     leverName, leverState ('on'|'off')
```

---

## 3. Rule Data Schema (v3)

Rules are stored in `localStorage` under `tac_rules_v3` and synced to `automation_saves/<saveName>.json` on the server.

```js
{
  id:      "r_<timestamp>_<rand>",  // unique, assigned at creation
  name:    "My Rule",               // user-editable display name
  enabled: true,                    // whether the rule runs each poll cycle
  mode:    "simple",                // "simple" | "fbd" | "code"

  simple: {
    conditionMode: "and",           // "and" | "or"
    functionType:  "direct",        // "direct" | "timer" | "latch"
    timerSeconds:  10,              // used when functionType is "timer"
    conditions: [
      // adapter:
      { type: "adapter",    adapterName: "MyAdapter", adapterState: "on" },
      // gamestate:
      { type: "gamestate",  field: "isDay" },
      // time range:
      { type: "time",       fromHour: 6, toHour: 20 },
      // population:
      { type: "population", field: "homeless", op: "gt", threshold: 5 }
    ],
    actions: [
      { leverName: "MyLever", leverState: "on" }
    ]
  },

  fbd: {
    nodes: [
      {
        id:     "n_<timestamp>_<rand>",
        type:   "INPUT_ADAPTER",          // see node types above
        x:      120,                      // canvas position
        y:      80,
        params: { adapterName: "MyAdapter", adapterState: "on" }
      }
    ],
    edges: [
      {
        id:         "e_<timestamp>_<rand>",
        fromNodeId: "n_...",
        fromPin:    "Q",
        toNodeId:   "n_...",
        toPin:      "IN"
      }
    ]
  },

  code: {
    script: "// write JS here\nsetLever('MyLever', adapters[0].state ? 'on' : 'off');"
  }
}
```

Runtime-only fields (not persisted, stripped on export/POST):
- `_timerStart` — timestamp when the Simple timer started
- `_latchFired` — whether the SR-Latch has already fired
- `_latchQ` — FBD SR-Latch output state
- `_timerStart` on FBD timer nodes — elapsed time tracking
- `_lastError` — last error thrown by the rule engine

---

## 4. Code Rule Sandbox

When `mode` is `"code"`, the script runs inside a `new Function(...)` sandbox every poll cycle (~3 s).

**Read-only inputs:**

| Variable | Type | Description |
|---|---|---|
| `adapters` | `Array<{name, state}>` | Shallow copy of current adapter states |
| `levers` | `Array<{name, state}>` | Shallow copy of current lever states |
| `gameState` | `object \| null` | Current `/api/gamestate` response, or `null` if DLL not loaded |
| `population` | `object \| null` | Current `/api/population` response, or `null` if DLL not loaded |

**Write functions:**

| Function | Description |
|---|---|
| `setLever(name, state)` | Set a lever. `state`: `'on'`, `'off'`, or `true`/`false` |
| `log(msg)` | Append a message to the rule's console panel |

Uncaught exceptions are caught, written to the console panel in red, and set as `rule._lastError` (shown as a `!` badge on the rule list row).
