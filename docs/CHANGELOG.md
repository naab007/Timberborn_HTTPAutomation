# Changelog — HTTPAutomation

## v5.9.0 — 2026-03-29
**Feature: ComfyUI-style FBD editor redesign (`docs/fbd.md`)**

All changes in `HttpApi/index.hbs`.

### Zoom support
- Scroll wheel now zooms in/out (mouse-centred, range 0.1×–4×) instead of panning
- Grid dots are world-aligned and scale with zoom so the grid stays visually consistent
- `fbdScreenToWorld` now divides by `FBD.zoom` so all coordinate math remains in world space
- Inline parameter overlay (`fbdShowInlineInput`) scales its size and font with zoom

### ComfyUI-style context menu (replaces HTML sidebar)
- `renderFbdEditor` no longer renders the `#tac-fbd-sidebar` panel; canvas is now full-width
- Right-clicking empty canvas opens `fbdShowContextMenu`: a `position:fixed` floating panel with a search input and categorised node buttons (Input / Logic / Output)
- Node buttons place the new node at the exact canvas world-coordinate of the right-click
- Menu auto-closes on outside click via a capture-phase `mousedown` listener
- Right-clicking an existing node still deletes it; right-clicking an edge still deletes it

### Hotkeys
- `Delete` / `Backspace` — delete the currently selected node (when focus is not in a text input)
- `F` — fit all nodes to the visible canvas (`fbdFitToScreen`: calculates bbox, sets zoom and pan)
- `Ctrl+Z` / `Cmd+Z` — undo last structural change (`fbdUndo`)

### Undo stack
- `FBD_UNDO` — up to 20 snapshots of `{nodes, edges}` serialised as JSON strings
- `fbdPushUndo(rule)` called before: add node (both sidebar and context menu), delete node (right-click, keyboard), add wire, delete edge
- `fbdUndo(rule)` — pops last snapshot, replaces `rule.fbd.nodes/edges`, redraws

### Thumbnail generation and full-screen plotter
- `fbdDrawCanvas` captures `canvas.toDataURL('image/jpeg', 0.55)` into `rule._fbdThumb` after every draw (transient — stripped from saved JSON by the `_`-key replacer in `saveRules`)
- `ruleDescription` for FBD rules shows the thumbnail as a small `<img>` (38px tall) in the rule list; clicking it opens the full-screen plotter via `TAC._fbdOpenFullscreen`
- `TAC._fbdOpenFullscreen(ruleId)` — creates a `position:fixed` full-viewport overlay with its own canvas element; calls `fbdFitToScreen()` if the rule has nodes; `Escape` or the × button closes it and calls `fbdDetach()`
- FBD editor inline view also has a "⬆ Fullscreen" button in the toolbar

### Misc
- `attachFbdCanvas`: resets `FBD.zoom = 1.0` when switching to a new rule
- `fbdDetach` and `fbdOnMouseDown` both call `fbdHideContextMenu()` to clean up stray menus
- Canvas height increased from 520 → 560 px and width uses full parent width (no 236 px sidebar deduction)
- Updated help text to describe new interactions

## v2.4.0 — 2026-03-22
**Fixed: double dashboard + all-zeros data**

### Root cause: double dashboard
`[Context("Game")]` causes Bindito to discover and run our `Configurator` once per
container that calls `InstallAll("Game")`. Timberborn calls this in both the Game
scene container AND what appears to be a MapEditor/child container. Result: two
`AutomationUiSection` instances → two `BuildFooter()` calls → two `<script>` tags →
`takeover()` ran twice → two `#tac-root` divs.

**Fix:** Added `if (document.getElementById('tac-root')) return;` guard at the top
of `takeover()` in `index-levers-footer.hbs`. The JS file is served at runtime so no
DLL recompile needed for JS-only changes.

### Root cause: all-zeros data
`FindObjectsOfType<MonoBehaviour>()` doesn't find Timberborn DI singletons —
`DayNightCycle`, `GameCycleService`, `WeatherService`, `HazardousWeatherService`, and
`BeaverCollection` are plain C# objects managed by Bindito, not Unity MonoBehaviours.

**Fix:** Restored proper constructor injection:
- `GameStateEndpoint(IDayNightCycle, GameCycleService, WeatherService, HazardousWeatherService)`
- `PopulationEndpoint(BeaverCollection, IDayNightCycle)`

These are correctly resolved by Bindito now that `[Context("Game")]` is working.
Worker/Workplace remain reflection-based since those are per-entity components,
not DI singletons.

### Note: port change
The game changed from port 8080 to 8072 between sessions. The HttpApi port is shown
in the Timberborn game settings. `http://localhost:{port}/` is where the UI lives.

---

## v2.3.0 — 2026-03-21  Fixed [Context("Game")] missing attribute — Configurator was never discovered.
## v2.2.0 — 2026-03-21  Zero-dep endpoints with FindObjectsOfType (wrong approach).
## v2.1.0 — 2026-03-21  Fixed UI takeover via IHttpApiPageSection.
## v2.0.0 — 2026-03-21  Initial implementation.

## v2.5.0 — 2026-03-22
**Fixed CTD on save load — BeaverCollection not in child container**

### Root cause
Bindito validates all transitive deps when creating a child container. `BeaverCollection`
is registered in the root Game container but NOT exported to child containers.
`IHttpApiEndpoint` is resolved in a child container (`ConstructionSitePanelDescriptionUpdater
→ IEntityPanel → HttpApi → IEnumerable<IHttpApiEndpoint>`). Injecting `BeaverCollection`
directly in `PopulationEndpoint` caused: `BinditoException: BeaverCollection isn't
instantiable due to missing dependency`.

### Fix: GameServicesInitializer + static cache
- Added `GameServices` static class — holds refs to all game singletons
- Added `GameServicesInitializer : ILoadableSingleton` — constructor-injected by the
  root [Context("Game")] container (where all deps ARE available), populates `GameServices`
  statics in `Load()` which runs at save-load time
- `GameStateEndpoint` and `PopulationEndpoint` now have zero constructor deps, read
  `GameServices.*` at request time
- Both endpoints return 503 `"Game not loaded yet"` if called before `Load()` runs

### Also fixed
- `Child` component is in `Timberborn.Beavers` namespace (not `Timberborn.Characters`)
  — added missing `using Timberborn.Beavers;` to `PopulationEndpoint.cs`
- `Timberborn.SingletonSystem.dll` added to `HTTPAutomation.csproj`

## v2.6.0 — 2026-03-22
**Fixed second CTD — ISingletonRepository pattern**

### Root cause
`GameServicesInitializer : ILoadableSingleton` had `BeaverCollection` in its constructor.
`MultiBind<ILoadableSingleton>` is also validated in child containers (same as
`IHttpApiEndpoint`), and `BeaverCollection` is not exported to child containers.
Error: `ILoadableSingleton isn't instantiable due to missing dependency: BeaverCollection`.

### Fix
`GameServicesInitializer` now injects only **`ISingletonRepository`** — the one dep that
IS available in all container scopes. At `Load()` time it calls
`_repo.GetSingletons<T>().FirstOrDefault()` for each needed type (`BeaverCollection`,
`IDayNightCycle`, `GameCycleService`, `WeatherService`, `HazardousWeatherService`).

### Rule confirmed
**Nothing that touches `BeaverCollection` (or other non-exported singletons) can appear
in any constructor dep chain registered via `MultiBind<>` in a `[Context("Game")]`
configurator.** Only `ISingletonRepository` is universally safe.

## v2.7.0 — 2026-03-22
**Fixed weather/population data — all services now resolved correctly**

### Root cause (confirmed via /api/debug)
`GetSingletons<ILoadableSingleton>()` did NOT return `WeatherService` etc. because
Bindito keys the cache by the *exact registered type*, not by interface. The `/api/debug`
endpoint confirmed `GetSingletons<object>()` returns all singletons regardless of how
they were registered — including `WeatherService`, `HazardousWeatherService`,
`BeaverPopulation`, `GameCycleService`, and `DayNightCycle`.

### Fixes
- `GameServices.Load()` now uses `GetSingletons<object>()` (confirmed working)
- Added `if (GameServices.Ready) return;` guard to prevent double-population
- `GameServicesInitializer` registration changed: `Bind<>().AsSingleton()` +
  `MultiBind<ILoadableSingleton>().ToExisting<>()` — prevents duplicate instantiation
  when `[Context("Game")]` configurator runs in multiple Bindito containers
- `PopulationEndpoint`: uses `BeaverPopulation` for aggregate counts
  (BeaverCollection is not in DI — counts only, no per-beaver detail for now)

## v2.8.0 — 2026-03-22
**Fixed double dashboard rendering**

### Root cause
`[Context("Game")]` configurator runs in multiple Bindito containers per session.
`Bind<>().AsSingleton()` only prevents duplicates within a single container — each
container still creates its own instance. Result: two `AutomationUiSection` registrations
→ two `BuildFooter()` calls → two `<script src="/automation.js">` tags in the page
→ `takeover()` race condition → two dashboards rendered.

### Fix: static configurator guard
Added `private static bool _registered` to `Configurator` — the first container to
call `Configure()` sets it and subsequent calls return immediately. This is the only
reliable way to enforce one-time registration across multiple Bindito container scopes.

### Fix: JS window mutex
Replaced `if (document.getElementById('tac-root'))` with `if (window.__TAC__) return`
set synchronously before any async work. Prevents the race condition where two script
executions both pass the DOM check before either creates the element.

### Note on weather
"Temperate" weather display IS correct — `HazardousWeatherService.CurrentCycleHazardousWeather`
returns null during temperate periods. Drought/Badtide badges will appear when those
weather events are active.

## v2.9.0 — 2026-03-22
**Fixed population counts — now includes bots, homeless, contaminated**

### Root cause
`BeaverPopulation` only counts beavers, not bots. A settlement with 18 beavers + 2 bots
showed 18 total instead of 20. `BeaverPopulation` also had no homeless/contaminated data.

### Fix
Added `PopulationService` to `GameServices`. `PopulationService.GlobalPopulationData`
is a `PopulationData` struct with:
- `TotalPopulation` — beavers + bots
- `NumberOfBeavers`, `NumberOfAdults`, `NumberOfChildren`, `NumberOfBots`
- `BeaverWorkforceData.Unemployable` — idle beavers
- `BedData.Homeless` — homeless beavers
- `ContaminationData.ContaminatedTotal` — contaminated beavers

`PopulationEndpoint.BuildPopJson` now uses this as primary source with `BeaverPopulation`
as fallback. Added `Timberborn.Population.dll` to csproj.

## v3.0.0 — 2026-03-22
**Step 1: Rule data model v3 — no DLL changes**

### What changed
- New rule schema (v3) with `id`, `name`, `mode`, `simple`, `fbd`, `code` fields
- `makeRule(name, mode)` factory function generates a properly-structured rule object
- `loadRules()` tries `tac_rules_v3` first; if missing, loads and migrates `tac_rules_v2` automatically
  then persists the migrated result under `tac_rules_v3`
- `saveRules()` now writes to `tac_rules_v3`
- `runAutomation()` rewritten to read from `rule.simple.conditions[]` and `rule.simple.actions[]`
  — supports AND/OR condition combining, Direct/Timer/SR-Latch function types
- `evalCondition()` added — evaluates a single condition (adapter, gamestate, time, population)
- `applyActions()` added — applies a list of lever-set actions
- `TAC.addRule()` now creates a v3 simple rule from the quick-add form inputs
- `TAC.resetLatch(id)` added for future SR-Latch reset button
- Automation tab list updated: shows Name column + mode badge + description summary
  (reads from `rule.simple.conditions[0]` and `rule.simple.actions[0]`)
- `ruleDescription(r)` helper generates human-readable one-liner for the rule list
- FBD and Code modes are placeholders in the engine (no-op for now)
- Timer and latch runtime state (`_timerStart`, `_latchFired`) is transient (not persisted)

### Why
Data model must be in place before any UI work on steps 2–6 can begin.
All existing v2 rules survive via the migration path.

## v3.1.0 — 2026-03-22
**Step 2: Automation tab rule list view — no DLL changes**

### What changed
- Added `automationView` ('list'|'edit') and `editingRuleId` to state `S`
- `switchTab()` resets edit state when leaving the automation tab, so returning always shows the list
- `poll()` skips `render()` when the automation editor is open to avoid clobbering it mid-edit
- `renderAutomation()` dispatches to `renderRuleList()` or `renderRuleEditor()`
- `renderRuleList()` — new full list view:
  - Header bar with "⚡ Automation Rules" title and "+ Add Rule" button
  - Empty state card with call-to-action when no rules exist
  - Table rows: enabled toggle / mode badge / name / description summary / Edit (✏) / Delete (✕)
  - Mode badge colours: Simple = grey, FBD = cyan, Code = yellow
  - Timer/Latch function type shown as a secondary grey badge on the row
- `renderRuleEditor()` — editor shell (Step 3 structure, content stubs for steps 4–6):
  - "← Back" breadcrumb button + title
  - Rule name text input
  - Mode selector: three clickable card-radios (Simple / FBD / Code) with descriptions
  - Mode-specific area (placeholder panels for steps 4, 5, 6)
  - Save Rule / Cancel buttons at the bottom
- `modeBadgeHtml(mode)` — shared helper for consistent badge rendering
- `ruleDescription(r)` — updated to produce richer HTML (bold lever name, italic placeholders)
- Removed the old quick-add form from the bottom of the automation tab
- `TAC` updated:
  - `toggleRule(id)` — now id-based (was index-based)
  - `delRule(id)` — now id-based; also exits edit view if deleting the currently-edited rule
  - `editRule(id)` — opens editor for an existing rule
  - `newRule()` — creates a blank Simple rule, pushes it, opens editor
  - `cancelEdit()` — returns to list view without saving name changes
  - `saveEdit(id)` — writes name field back to rule, saves, returns to list
  - `_setMode(id, mode)` — internal; called by mode radio cards, saves immediately

### Why
The list view is the persistent home for all rules. The editor shell gives a working
Add Rule → Edit → Save → Back flow before the mode-specific editors (steps 4–6) are built.
Switching to id-based lookups makes toggle/delete safe regardless of render order.

## v3.2.0 — 2026-03-22
**Steps 3+4: Rule editor shell complete + Simple mode editor — no DLL changes**

### Note on step numbering
The editor shell (plans_frontend.md Step 3) was already completed as part of v3.1.0.
This release fills in Step 4: the full Simple mode editor inside that shell.

### What changed
- `renderSimpleEditor(rule)` — new function replacing the Simple mode placeholder:
  - **Conditions section**: dropdown to add a condition by type (Adapter / Game state / Time of day / Population).
    Each type renders its own controls inline: adapter gets a name select + ON/OFF; game state gets a field picker;
    time gets from/to hour number inputs (0–23, 0.5 step); population gets field + operator + threshold.
    When 2+ conditions exist, AND/OR toggle buttons appear.
    Each condition row has a ✕ remove button.
  - **Function type section**: Direct / Timer / SR-Latch button group.
    Timer shows a "Fire after N seconds" number input. Latch shows a Reset Latch button.
    Each type shows a one-line description of its behaviour.
  - **Actions section**: "+ Add Action" button adds a lever name + ON/OFF row.
    Each action row has a ✕ remove button.
  - Empty states shown for both conditions and actions when the lists are empty.
- New TAC methods (all mutate rule.simple and call saveRules() + render()):
  - `_addCondition(id, type)` — pushes a default condition of the given type
  - `_removeCondition(id, idx)` — removes condition at index
  - `_updateCondition(id, idx, key, value)` — updates a single field on a condition (fires on onchange/blur)
  - `_setCondMode(id, mode)` — sets AND/OR combining logic
  - `_setFuncType(id, ft)` — switches Direct/Timer/Latch and re-renders the function type UI
  - `_setTimerSec(id, val)` — saves timer seconds on input blur (no re-render, field already unfocused)
  - `_addAction(id)` — pushes a default action (first available lever, ON)
  - `_removeAction(id, idx)` — removes action at index
  - `_updateAction(id, idx, key, value)` — updates leverName or leverState on an action
- FBD and Code mode areas remain as styled placeholder cards in the editor

### Why
onchange (not oninput) is used on all inputs so re-renders only happen on blur/commit,
not on every keystroke — preventing focus loss while typing.

## v3.3.0 — 2026-03-22
**Step 5: Code mode editor — no DLL changes**

### What changed
- `S.codeLogs` — new state: `{ [ruleId]: [{time, msg, isErr}] }`, capped at 80 lines per rule
- `codeLog(ruleId, msg, isErr)` — pushes a timestamped entry, trims oldest when over cap
- `runCodeRule(rule)` — builds `new Function('adapters','levers','gameState','population','setLever','log', script)`
  and executes it each poll cycle. Errors caught and written to the rule's log buffer.
  Sandbox exposes: read-only `adapters`/`levers`/`gameState`/`population` (shallow copies),
  `setLever(name, state)` (routes through `applyActions`), `log(msg)`.
- `runAutomation()` Code branch — calls `runCodeRule(rule)` (was a no-op placeholder)
- `updateCodeLogPanel()` — after `runAutomation()` in `poll()`, patches `#tac-code-log` innerHTML
  directly without a full re-render, keeping the textarea focused and cursor position intact.
  Auto-scrolls the log panel to the bottom.
- `renderCodeLogLines(ruleId)` — generates log HTML from `S.codeLogs[ruleId]`
- `renderCodeEditor(rule)`:
  - Reference card: table of available variables and helpers with types
  - `<textarea id="tac-code-textarea">` with monospace dark theme, `oninput` calls `TAC._saveCode`
  - Console panel (`#tac-code-log`) with Clear button, monospace, scrollable, 180px max height
  - Error lines shown in red with ❌ prefix; timestamps dimmed
- `TAC._saveCode(id)` — reads textarea.value on every keystroke, saves to `rule.code.script`
  and `localStorage` without re-rendering (cursor stays put)
- `TAC._clearLog(id)` — clears `S.codeLogs[id]` and patches log panel DOM directly
- `saveEdit()` and `_setMode()` — both flush the textarea to `rule.code.script` before
  saving/switching, in case `oninput` didn't catch the last keystroke
- `ruleDescription()` for Code mode — now shows "N lines of code" / "Empty script" instead of
  a generic italic placeholder
- CSS additions: `.tac-code-area`, `.tac-log-panel`, `.tac-log-line`, `.tac-log-err`, `.tac-log-time`

### Key design decisions
- `oninput` (not `onchange`) on the textarea so every keystroke persists — avoids data loss
  if the user clicks Save without blurring the textarea first, covered by the `saveEdit` flush
- `new Function(...)` is rebuilt each poll cycle — this is intentional so live edits to the
  script take effect on the next cycle without needing to save first
- Log panel is patched via `updateCodeLogPanel()` using the stable DOM id `tac-code-log`,
  not via a full `render()` — full re-render would destroy the textarea and lose cursor position
- Shallow copies of adapters/levers passed to sandbox — prevents scripts from accidentally
  mutating S state; writes must go through `setLever()` which uses the real lever objects

## v3.4.0 — 2026-03-22
**Step 6: FBD canvas editor + engine — no DLL changes**

### Node types (11 total)
Inputs: INPUT_ADAPTER, INPUT_GAMESTATE, INPUT_TIME, INPUT_POPULATION
Logic:  LOGIC_AND, LOGIC_OR, LOGIC_NOT, LOGIC_TIMER (TON), LOGIC_SRLATCH, LOGIC_COMPARE
Output: OUTPUT_LEVER

### Canvas interaction
- Left-click output pin → left-click input pin: draws a bezier wire (one edge per input pin max)
- Left-click node body: selects node, starts drag; saves on mouseup
- Right-click node: deletes node + all attached edges
- Right-click wire: deletes that edge (bezier proximity hit-test, 15 sample points)
- Middle-mouse drag or Space+drag: pan the canvas
- Scroll (wheel): pan X/Y

### Rendering
- Dark grid (24px dot spacing), dark node boxes with rounded corners, colour-coded by category
- Input category: dark blue; Logic: dark green/amber; Output: dark red
- Pin circles: green=true, dark=false/unknown, blue=unknown
- Wires: bezier, green=true, dark grey=false, medium grey=unknown
- In-progress wire: dashed white bezier following mouse
- Selected node: bright blue border
- Node body shows type label + compact param summary (e.g. "MyAdapter ON", "TON PT=10s")

### Sidebar
- Node library: grouped by Input / Logic / Output, compact Add buttons
- Selected node params: type-appropriate form controls (selects + number inputs)
  using `onchange` → `TAC._fbdUpdateParam` which saves + redraws without full render
- Delete Node button at bottom of params panel

### DOM persistence strategy
Same approach as code editor:
- `poll()` when FBD editor open: calls `fbdDrawCanvas()` only (no full render)
- param changes: patch `#tac-fbd-sidebar` innerHTML + redraw canvas (no full render)
- `render()` automation case: after `innerHTML =`, calls `attachFbdCanvas(rule)` if FBD mode
- `attachFbdCanvas(rule)`: removes old listeners, sizes canvas to fill available width at 520px height, re-attaches listeners, initial draw
- `fbdDetach()`: removes all listeners, clears FBD state; called on cancel/save/mode switch/tab leave

### FBD engine (runFbdRule)
1. Kahn's algorithm topological sort on node graph
2. Evaluate each node in dependency order, reading input signal values from `signals` map
3. Results written to `S.fbdSignals` keyed `nodeId_pinName` (outputs) and `nodeId_in_pinName` (inputs)
4. Timer nodes: runtime `_timerStart` stored on node object (in-memory, not persisted)
5. SR-Latch nodes: runtime `_latchQ` stored on node object
6. OUTPUT_LEVER: calls `applyActions` when input IN is true

### Rule list
- FBD description now shows "N nodes" / "Empty diagram" instead of generic italic
- Mode card description updated (removed "Coming in Step 6")

### New module-level state
- `FBD` object: canvas, ctx, ruleId, pan, drag, wire, selected, mouse, spaceDown, panning, panStart
- `S.fbdSignals`: shared signal map updated by engine, read by canvas draw
- `FBD_DEFS`: node type definitions (label, cat, color, inputs[], outputs[])
- Constants: FBD_W=150, FBD_ROW=22, FBD_HEAD=26, FBD_PAD=8, FBD_PR=6

### New functions
fbdNodeH, fbdPinPos, fbdMakeNodeId/EdgeId, fbdScreenToWorld, fbdNodeAt, fbdPinAt
fbdBezierDist, fbdHitEdge, fbdDefaultParams, fbdNodeParamSummary
fbdRoundRect, fbdDrawWire, fbdDrawPin, fbdDrawNode, fbdDrawCanvas
fbdOnMouseDown/Move/Up, fbdOnContextMenu, fbdOnWheel, fbdOnKeyDown/Up
attachFbdCanvas, fbdDetach, fbdRefreshSidebar
renderFbdEditor, renderFbdSidebar, renderFbdNodeParams
runFbdRule
TAC._fbdAddNode, TAC._fbdDeleteNode, TAC._fbdUpdateParam

## v3.5.0 — 2026-03-22
**Steps 7/8/9: Polish + DLL fallback — no DLL changes**

### Note on step numbering
Step 7 (engine wiring) was already complete — all three modes were wired during steps 3–6.
This release covers Steps 8 (DLL fallback improvements) and 9 (Polish).

### Dashboard automation mini-card
Per-mode active rule counts now shown as coloured badges alongside the total.
S: Simple, F: FBD, C: Code — colour-matched to mode badge colours.
Only non-zero counts are shown.

### Rule list — reordering
↑ / ↓ arrow buttons on each row. Swaps adjacent items in S.rules and saves.
Top row has no ↑, bottom row has no ↓ (replaced with invisible spacer to keep column width stable).

### Rule list — Export / Import
Export button: serialises S.rules to JSON (private `_*` runtime fields stripped via JSON.stringify replacer),
copies to clipboard via navigator.clipboard with fallback to execCommand('copy').
Shows a green success toast with rule count, or error on failure.
Import button: toggles `S.showImport` which renders a textarea + "Replace all rules" + Cancel.
On confirm: parses JSON, validates array structure, fills in missing sub-objects via makeRule(),
replaces S.rules, saves, shows success toast.
`S.showImport` state added to S; reset when leaving automation tab.

### Per-rule error badges
`rule._lastError` (not persisted — set at runtime by engine) shown as a red "!" badge on the rule row.
Code rules also show "!" if `S.codeLogs[id]` has any isErr=true in the last 5 entries.
Error text shown as tooltip on the badge.
In the rule editor, a red alert bar shows the error message above the mode-specific editor area.

### Step 8 — DLL fallback improvements
`FBD_DEFS` entries now have a `needsDll` flag.
FBD node library buttons for DLL-dependent types (INPUT_GAMESTATE, INPUT_TIME, INPUT_POPULATION,
LOGIC_COMPARE) show a ⚠ icon when DLL_AVAILABLE === false.
Automation rule list shows a grey notice: "DLL not loaded — game state / population conditions
evaluate to false. Adapter rules work normally."
`runFbdRule()` wrapped in try/catch — errors written to `rule._lastError`.
`runCodeRule()` already had try/catch — `rule._lastError` now set on catch (was already logged).

### Other
`showOk(msg)` helper added — green toast using same DOM element as showErr, auto-reverts to red.
Import panel styled with `.tac-import-box` class.
Export JSON replacer strips `_timerStart`, `_latchFired`, `_latchQ`, `_lastError` from serialisation.

## v4.0.0 — 2026-03-22
**Step 10 (backend): Save-game-aware automation storage**

### What changed
- Probed all Timberborn managed DLLs for save-name APIs.
  Found `GameLoader` in `Timberborn.GameSaveRuntimeSystem` with `SaveReference LoadedSave` property.
  `SaveReference.SaveName` (string) is the current save file name.
- Added two new DLL references to `HTTPAutomation.csproj`:
  - `Timberborn.GameSaveRuntimeSystem` — for `GameLoader`
  - `Timberborn.GameSaveRepositorySystem` — for `SaveReference` type
- `GameServices.cs`:
  - Added `using Timberborn.GameSaveRuntimeSystem;`
  - Added `public static GameLoader SaveLoader;` field
  - `Assign()` switch: case `"GameLoader"` populates `SaveLoader`
  - `services.log` now includes `SaveLoader` status line
- `GameStateEndpoint.cs`:
  - Added `"saveName"` field to `/api/gamestate` JSON response
  - Value: `GameServices.SaveLoader?.LoadedSave?.SaveName ?? ""`  (empty string if not loaded yet)
- New file `src/AutomationStorageEndpoint.cs`:
  - `GET /api/automation?save=<saveName>` — reads `<ModDir>/automation_saves/<saveName>.json`
  - Returns `{"rules":[]}` if the file does not exist yet (first time for that save)
  - Returns 400 if `save` param is missing or empty
  - `POST /api/automation?save=<saveName>` — writes request body verbatim to the same path
  - Creates `automation_saves/` directory on first write if it doesn't exist
  - Returns `{"ok":true}` on success; 500 on I/O error; 503 if game not loaded
  - `SanitiseFileName()` strips all `Path.GetInvalidFileNameChars()` from the save name
  - `ExtractQueryParam()` manually parses the query string with `Uri.UnescapeDataString`
- `Configurator.cs`: registered `AutomationStorageEndpoint` as singleton + `IHttpApiEndpoint`
- DLL built successfully — 0 warnings, 0 errors, 30 KB

## v4.1.0 — 2026-03-22
**Step 10 (frontend): Save-game-aware rule loading and saving**

### What changed
- `S.currentSaveName: ''` added to state object — tracks the save name last loaded from the backend
- `saveRules()` rewritten:
  - Still writes to `tac_rules_v3` in localStorage immediately (unchanged)
  - When `S.currentSaveName` is set, schedules a 500 ms debounced POST to `/api/automation?save=<n>`
  - POST body: `{rules: S.rules}` with all `_*` runtime fields stripped via JSON replacer
- New `loadRulesFromServer(saveName)` function:
  - Sets `S.currentSaveName = saveName` immediately (prevents repeat calls on subsequent polls)
  - Updates `#tac-savename` span in navbar with 💾 + save name
  - Fetches `GET /api/automation?save=<n>` — handles both `{rules:[...]}` and raw array responses
  - If server returns non-empty rules: replaces `S.rules`, writes to localStorage cache
  - If server returns empty and we have rules in memory: calls `saveRules()` to migrate them to server (first-run / upgrade path)
  - Re-renders automation tab if it's open
  - On fetch error: silently stays with current in-memory rules
- `poll()` `.then()` updated: after setting `S.gameState`, extracts `saveName` field; if it differs from `S.currentSaveName`, calls `loadRulesFromServer`
- `takeover()` navbar: added `<span id="tac-savename">` between the spinner and the clock
- No DLL recompile needed — all changes are in `index-levers-footer.hbs`

## v4.2.0 — 2026-03-22
**Step G: live beaver tracker — `/api/beavers` returns real data**

### Root cause of the stub
`BeaverCollection` is not registered in Timberborn's Bindito DI container at all.
It is built at runtime by event listeners, not by constructor injection. Any attempt
to inject it via a constructor dep chain crashes the game (see `what_ive_learned.md` §3.3).

### Fix: event-listener pattern on `GameServicesInitializer`
`GameServicesInitializer` is already a registered `ILoadableSingleton` with access to
`ISingletonRepository`. The same `[OnEvent]` attribute pattern used by the game's own
`BeaverPopulation` class is used here to maintain a live `List<Beaver>`.

### Changes

**`GameServices.cs`**
- Added `using Timberborn.Characters;`
- New static fields:
  - `public static EventBus EventBus` — stored from `GetSingletons<object>()` in `Assign()`
  - `public static readonly List<Beaver> Beavers` — live list of all beavers currently in the game
  - `public static readonly object BeaversLock` — lock object protecting `Beavers` from cross-thread race conditions (HTTP thread-pool vs Unity main thread)
- `Assign()` switch: added `"EventBus"` case
- `services.log` output: added `EventBus` status line
- `Load()`: calls `GameServices.EventBus.Register(this)` at end of `Load()` body (protected by the existing `if (GameServices.Ready) return;` guard — runs exactly once per session)
- New `[OnEvent] void OnCharacterCreated(CharacterCreatedEvent e)`:
  - Gets the `Beaver` component from the event's `Character`
  - If non-null, adds it to `GameServices.Beavers` under lock (duplicate check included)
- New `[OnEvent] void OnCharacterKilled(CharacterKilledEvent e)`:
  - Gets the `Beaver` component, removes from `GameServices.Beavers` under lock

**`PopulationEndpoint.cs`**
- `Dispatch()` — all three beaver routes rewritten to use `GameServices.Beavers`:
  - `GET /api/beavers` — takes a lock snapshot, calls `BuildListJson(snapshot)`
  - `GET /api/beavers/<id>` — takes a lock snapshot, calls `FindById(snapshot, id)`
  - `POST /api/beavers/<id>/dismiss` — takes a lock snapshot, calls `TryDismiss(snapshot, id)`
- `BuildListJson(List<Beaver>)` — signature changed from `BeaverCollection`
- `FindById(List<Beaver>, string)` — signature changed from `BeaverCollection`
- `TryDismiss(List<Beaver>, string)` — signature changed from `BeaverCollection`

### Why lock is needed
`EventBus` fires events on the Unity main thread. `TryHandle()` is called on .NET
thread-pool threads (HttpListener). Without a lock, list reads and writes could race,
causing `InvalidOperationException: Collection was modified` or worse.

### Event timing — existing beavers on save load
The game fires `CharacterCreatedEvent` for each entity it deserialises when loading a
save. Since `ILoadableSingleton.Load()` is called *before* entity deserialisation,
`Register(this)` is in place in time to catch all of them. This is confirmed by
`BeaverPopulation` in `Timberborn.Beavers` using the exact same pattern.

## v4.3.0 — 2026-03-22
**Detailed debug logging across all subsystems**

### New: `src/ModLog.cs`
- Thread-safe (single lock around all file writes, safe for Unity main thread + .NET thread-pool concurrent access)
- Timestamped to millisecond precision: `HH:mm:ss.fff [LEVEL] message`
- Three levels: `Info`, `Warn`, `Error` (with optional `Exception` overload appending type + message + full stack trace + inner exception chain)
- Rolling at 500 KB: when the file exceeds the cap, the oldest half is discarded and a `--- log rolled ---` marker is inserted; recent entries are always preserved
- Writes to `debug.log` in the mod directory (`Documents/Timberborn/Mods/HTTPAutomation/debug.log`)
- All failure paths are double-wrapped — logging itself never throws into caller code

### `GameServices.cs`
- `Load()` logs each of the 8 services individually as `OK` or `MISSING`
- Logs the loaded save name from `SaveLoader.LoadedSave.SaveName`
- Logs `EventBus.Register` outcome with exception detail on failure
- `OnCharacterCreated`: logs beaver name and total list count; exception caught and logged
- `OnCharacterKilled`: logs beaver name and remaining count; warns if beaver wasn't in the list

### `GameStateEndpoint.cs`
- `/api/ping` response upgraded: now includes `version: "4.2"`, `saveName`, `beaverCount`, `eventBus` — useful as a one-call health check
- `/api/gamestate` exception handler now calls `ModLog.Error` with full stack trace before returning 500

### `AutomationStorageEndpoint.cs`
- Every `GET` logs: save name, whether file existed, chars served, file path
- Every `POST` logs: save name, chars written, file path
- Directory creation logged
- Exceptions logged with full stack trace

### `PopulationEndpoint.cs`
- `EnsureWorkerReflection`: logs once which of the four reflection targets (`Employed`, `Workplace`, `WorkplaceTemplateName`/`TemplateName`, `UnassignWorker`) were found or missing — immediately reveals if a game update broke the reflection
- `WorkerEmployed`: exception logged with stack trace instead of silently returning false
- `DismissWorker`: each failure branch (no Worker component, not employed, null Workplace, no UnassignWorker method) logged as Warn; success logged as Info
- `BuildListJson`: wraps each `Snapshot()` call — exceptions logged with beaver ID, beaver skipped rather than aborting the whole list
- Dismiss result (success/failure reason) logged

### `AutomationJsEndpoint.cs`
- Logs file path and byte count on successful serve
- Warns on missing file with full path so it's obvious what to fix

---

## v4.4.0 — 2026-03-22
**Step H: fix save-reload bug in GameServicesInitializer**

### Root cause
`GameServices` uses static fields that persist for the entire Unity application lifetime.
`GameServices.Ready = true` was set on the first save load and never reset. If the player
loaded a second save mid-session, `ILoadableSingleton.Load()` was called on a brand-new
`GameServicesInitializer` instance (new DI container for the new save), but immediately
returned because `Ready == true`. Results: beaver list retained dead beavers from the old
scene, service references pointed to destroyed Unity objects, new session's events were
never received.

### Fix: three-way entry guard using instance identity
New static field `GameServices.RegisteredInitializer` (type `GameServicesInitializer`)
stores the instance that last ran `Load()` successfully.

`Load()` entry logic:
1. `Ready == false` → first load, run normally
2. `Ready == true` AND `this == RegisteredInitializer` → same-save second Bindito container
   call, skip (original behaviour preserved)
3. `Ready == true` AND `this != RegisteredInitializer` → new save loaded mid-session:
   - Unregister old initializer from `EventBus` (try/catch — EventBus may be destroyed)
   - Clear `GameServices.Beavers` under lock, logging the count discarded
   - Null out all 8 service references
   - Set `Ready = false`
   - Fall through to full re-initialization as if it were a first load

`RegisteredInitializer` is set only after `EventBus.Register(this)` succeeds, so it is
never set to an initializer that didn't complete cleanly.

## v3.6.0 — 2026-03-22
**Logging: frontend activity log + save-sync integration — no DLL changes**

### What changed

**Frontend activity log (uiLog)**
- `S.uiLog` ring buffer (300 entries, newest first), `S.logFilter` for display filtering
- `uiLog(level, msg)` — levels: ok / info / warn / err / rule / sync
- Log panel patches itself live via `#tac-log-panel` ID when the Logs tab is open — no full re-render

**Logs tab (6th navbar tab)**
- `renderLogs()` — filterable panel (All / OK / Rule / Sync / Warn / Error) with entry count and Clear button
- `renderLogLines()` — colour-coded level labels, monospace font, newest entry at top
- Cross-reference note pointing to `debug.log` for backend events
- Panel height fills viewport minus header
- `TAC._setLogFilter(f)` / `TAC._clearUiLog()` added to TAC object

**Instrumented call sites**
- `takeover()` — boot entry: rule/lever/adapter counts loaded from localStorage
- `loadRulesFromServer()` — save detected, server fetch result, migration, error (sync level)
- `saveRules()` server POST — success (sync) and failure (warn) instead of silent swallow
- `poll()` — replaced noisy every-3s INFO log with change-only events:
  DLL going offline (warn), DLL coming back online (ok). Save changes logged in `loadRulesFromServer`.
- `applyActions(actions, _ruleName)` — new optional `_ruleName` param. Logs each lever state
  change: `Lever "X" → ON [Rule Name]` (rule level). Logs HTTP failure (err level).
- `runAutomation()` — passes `rule.name` to `applyActions`; logs Timer elapsed and Latch set events
  (rule level); logs new Code/FBD errors on first occurrence only (err level, compares to `_prevErr`
  to avoid repeating the same error every 3 s)

**Bug fix: `loadRulesFromServer`**
Rules loaded from server now go through `Object.assign({}, makeRule(r.name, r.mode), r)` so
any missing sub-objects (simple/fbd/code) are filled in with defaults. Previously, old save files
with incomplete schemas could cause the engine to crash on missing `.conditions`.

### Log level guide
| Level | Colour | When used |
|-------|--------|-----------|
| ok    | green  | Successful operations (DLL online, rules loaded, sync OK) |
| info  | grey   | Informational (currently unused in steady state to reduce noise) |
| warn  | amber  | Non-fatal problems (DLL offline, sync failed, unexpected format) |
| err   | red    | Errors (lever toggle failed, rule engine exception) |
| rule  | blue   | Automation actions (lever changes, timer/latch fires) |
| sync  | purple | Save-game sync events (save detected, upload/download) |

## v4.5.0 — 2026-03-22
**Fix: loading a game whilst in a game — three-bug root cause analysis and full fix**

### Bug report
"Loading a game whilst being in a game makes the web-ui not register any game at all."

### Root causes (three separate bugs found via debug.log + services.log)

**Bug 1: `GameLoader` not a singleton — `saveName` always empty**
`GameLoader` has zero entries in `GetSingletons<object>()`. It is not registered as a
singleton in Timberborn's DI. `SaveLoader: MISSING` appeared in every `services.log`.
`/api/gamestate` always returned `"saveName": ""`.

**Bug 2: Two full `Load()` runs per session — `GameServices.Ready` not volatile**
The log showed two `Load()` calls 11ms apart, both logging "starting service resolution"
(i.e., both seeing `Ready == false`). The Configurator `_registered` guard correctly
prevented a second registration, but the two concurrent container initialisations ran
on separate threads. `GameServices.Ready` was not `volatile`, so the second thread read
a stale `false` value and ran the full initialisation again — registering with EventBus
twice, causing every beaver to be logged twice.

**Bug 3: `Configurator._registered` survived game reloads — second session never initialised**
When the user loaded a second save, Timberborn destroyed all containers and created new ones.
`Configure()` was called on a new Configurator instance, but `_registered == true` from the
first session (static field). Configure() returned immediately. `GameServicesInitializer` was
never registered in the new containers. `Load()` was never called. `GameServices.Ready` stayed
`true` but all service references pointed to destroyed Unity objects from the old session.
The result: every API call either threw a NullReferenceException or returned stale data.

### Fixes

**Bug 1: Replace `GameLoader` with `SettlementReferenceService`**
- Dropped `GameServices.SaveLoader` (type `GameLoader`)
- Added `GameServices.SettlementRef` (type `SettlementReferenceService`) — confirmed in singletons list
- Added `Timberborn.SettlementNameSystem` DLL reference to csproj
- Added `"SettlementReferenceService"` case in `Assign()` switch
- `GameServices.SettlementName` computed property: `SettlementRef?.SettlementReference?.SettlementName ?? ""`
- `/api/gamestate` `saveName` field now returns the settlement name (e.g. "Beaver Valley")
- `/api/ping` `saveName` field updated the same way
- Note: the key is now per-settlement rather than per-save-file. This is intentional —
  automation rules logically belong to a settlement, not to a specific autosave slot.

**Bug 2: `volatile` on `GameServices.Ready`**
- `public static volatile bool Ready;`
- Ensures writes from one thread are immediately visible to all other threads.

**Bug 3: `IUnloadableSingleton` on `GameServicesInitializer` + reset `Configurator._registered`**
- `GameServicesInitializer` now implements `IUnloadableSingleton` in addition to `ILoadableSingleton`
- `Configurator` registers `MultiBind<IUnloadableSingleton>().ToExisting<GameServicesInitializer>()`
- `Configurator._registered` changed from `private` to `internal static` so `Unload()` can reset it
- `Unload()` (called by Timberborn when the game scene tears down):
  - Logs the unload
  - Calls `ResetState()` (unregisters from EventBus, clears beaver list, nulls all service refs, sets Ready=false)
  - Sets `Configurator._registered = false` so the next session's `Configure()` registers fresh
- `ResetState()` extracted as a private helper, also used by the defensive fallback in `Load()`
- The three-way guard in `Load()` simplified: Ready=false (normal path); Ready=true + same instance
  (skip, second call on same instance); Ready=true + different instance (warn + force reset as
  safety net — should not happen with IUnloadableSingleton but guards against edge cases)

## v3.6.1 — 2026-03-22
**Fix: browser cached old JS, blocking all UI interaction — DLL rebuild required**

### Root cause
`AutomationUiSection.BuildFooter()` serves the script as `/automation.js?v=2`.
The in-game Chromium browser cached this URL the first time it was fetched and
never re-fetched it — so all JS changes since v=2 was set were invisible.
The selective-render poll fix (v3.6.0) was correct but never executed by the browser.

### Changes

**`AutomationUiSection.cs`**
- Bumped query param from `?v=2` to `?v=4`. This forces the browser to fetch
  a fresh copy immediately on next page load, bypassing any cached version.
  (v3 was skipped to leave a gap for any intermediate cached states.)

**`AutomationJsEndpoint.cs`**
- Added `Cache-Control: no-store, no-cache, must-revalidate` and `Pragma: no-cache`
  response headers. The browser will never cache `/automation.js` again, so future
  JS edits take effect on the next page reload without needing a version bump.

**DLL rebuilt**: `Scripts/HTTPAutomation.dll` updated.

### What this unblocks
All fixes from v3.5.0 and v3.6.0 are now live:
- Poll no longer re-renders automation or logs tabs (user can navigate freely)
- Logs tab persists and self-patches via DOM id
- Rule editor stays open while poll fires
- All TAC.* click handlers work correctly

## v4.6.0 — 2026-03-22
**frontend_needs.md: Cache-Control everywhere + GET /api/log + consolidate log endpoints**

### Cache-Control: no-store on all endpoints
`HttpResponseHelper.AddCorsHeaders()` is called by every endpoint before any response.
Added `Cache-Control: no-store, no-cache, must-revalidate` and `Pragma: no-cache` there.
This covers all endpoints in one place with no per-endpoint changes needed.
Prevents the in-game Chromium from serving stale API data or JS between sessions.

### GET /api/log?lines=N — new, serves debug.log tail
`LogEndpoint` (merged, handles both GET and POST on `/api/log`).
- `GET /api/log?lines=N` — reads `debug.log` under `ModLog.FileLock` to avoid torn reads
  while the logger is mid-write. Returns last N lines (default 200, max 2000) as:
  `{"lines":[...], "totalLines": N, "file": "path/to/debug.log"}`
  Returns `{"lines":[], "totalLines":0, "file":"..."}` if the file doesn't exist yet.
- `POST /api/log` — saves frontend UI log to `ui_log.txt` (unchanged from FrontendLogEndpoint)

### FrontendLogEndpoint removed (route conflict)
`FrontendLogEndpoint` handled `POST /api/log` but its route check used `path.StartsWith`
and returned `true` even for GET requests (with a 405), blocking `LogEndpoint` from ever
seeing GETs. Merged both into `LogEndpoint.cs` and deleted `FrontendLogEndpoint.cs`.

### ModLog.FileLock exposed
`ModLog._lock` exposed as `internal static object FileLock` (via a property) so
`LogEndpoint` can share the same lock when reading the log file.

### DLL built clean, 44 KB

## v4.7.0 — 2026-03-22
**Fix: double Load()/Unload() from two concurrent Bindito containers**

### Root cause (confirmed from debug.log)
Two Bindito containers call `Load()` per session within ~1ms of each other.
Both threads checked `GameServices.Ready == false` before either wrote `true` — a classic
TOCTOU race. `volatile` prevents stale reads *after* a write but cannot help when both
threads read before either writes. Result: both ran full `Load()`, both called
`EventBus.Register(this)`, and every `CharacterCreatedEvent` was received by two
listeners — every beaver added/removed twice.

Same pattern for `Unload()`: two containers, two teardown calls, second one tried to
unregister from an already-nulled EventBus.

### Fix: Interlocked.CompareExchange atomic claim flags
Two `static int` fields added to `GameServices`:
- `LoadClaimed` — 0 = available, 1 = claimed
- `UnloadClaimed` — same

In `Load()`:
```csharp
if (Interlocked.CompareExchange(ref GameServices.LoadClaimed, 1, 0) != 0)
{
    ModLog.Info("skipped (claimed by concurrent container)");
    return;
}
```
`CompareExchange` is a single atomic hardware instruction — no thread can observe
a state between the read and the write. Exactly one instance wins; all others skip.

In `Unload()`:
```csharp
if (Interlocked.CompareExchange(ref GameServices.UnloadClaimed, 1, 0) != 0)
{
    ModLog.Info("skipped (claimed by concurrent container)");
    return;
}
// ... reset state, reset Configurator._registered ...
Interlocked.Exchange(ref GameServices.LoadClaimed, 0);
Interlocked.Exchange(ref GameServices.UnloadClaimed, 0);
```
The winning `Unload()` resets both flags at the end so the next session's
`Load()` and `Unload()` can claim them again.

### Also removed
- `volatile` qualifier on `GameServices.Ready` (no longer needed — only one thread writes it now)
- The `Ready == true && this != RegisteredInitializer` "new save" fallback path in `Load()`
  (replaced entirely by the CAS approach + `IUnloadableSingleton`)
- `GameServices.RegisteredInitializer` kept (still used by `ResetState()` for EventBus unregister)

### Expected log output after this fix
```
GameServicesInitializer.Load() — starting service resolution  (one line only)
...
GameServicesInitializer.Load() — skipped (claimed by concurrent container)
```
And on game exit:
```
GameServicesInitializer.Unload() — game scene tearing down, resetting all state
GameServicesInitializer.Unload() — skipped (claimed by concurrent container)
```
No duplicate beaver adds. Each beaver logged exactly once.

## v3.6.2 — 2026-03-22
**Fix: navbar invisible + body padding gap (JS-only fix + DLL rebuild for ?v=6)**

### Root cause
The navbar used Bootstrap class `bg-dark` which resolves to `#212529` — identical to our
`body{background-color:#212529}` injection. The navbar existed in the DOM but was completely
invisible against the page background. Tab buttons appeared missing.

Additionally, the game's Bootstrap layout leaves residual `padding-top` on `<body>` after we
remove its own navbar, creating a large empty gap above our content.

### Changes

**`HttpApi/index-levers-footer.hbs`**
- Replaced Bootstrap navbar (`nav.navbar.navbar-dark.bg-dark.fixed-top`) with a fully
  inline-styled nav (`#tac-nav`): background `#1a1f2e`, `position:sticky;top:0`, visible border
- Removed `padding-top:58px` from `#tac-content` (no longer needed with sticky nav)
- Added `TAC_BTN_STYLE` constant — all tab buttons use inline styles, no Bootstrap dependency
  for their core appearance
- Added body/html CSS reset: `padding:0!important; margin:0!important; background:#0d1117`
- Added `@keyframes tac-spin` for the spinner (was using Bootstrap `.spinner-border` before)
- Spinner updated to use `style.display` instead of classList `.d-none`
- Active tab highlight: blue tint (`rgba(79,140,255,.35)`)

**`src/AutomationUiSection.cs`**
- Bumped `?v=6` to force browser cache bust

**DLL rebuilt**: `Scripts/HTTPAutomation.dll` updated.

### Why sticky instead of fixed
`position:sticky;top:0` keeps the navbar in document flow, so content naturally sits below it
without needing an explicit `padding-top` offset. `position:fixed` required knowing the exact
navbar height and was fragile if the game's body had existing padding.

## v3.7.0 — 2026-03-22
**Fix: el.remove() → el.style.display='none' in takeover() — stops CEF page reload loop**

### Root cause (documented in investigation_buttons.md)
The game's inline SPA (injected by the backend agent) holds live references to DOM elements
like `#tab-dashboard` inside `main.container-fluid` and writes `.innerHTML` to them every
3 seconds from its own poll loop.

Our `takeover()` was calling `el.remove()` on `main.container-fluid`, which deleted those
elements from the DOM. On the next poll cycle, `document.getElementById('tab-dashboard')`
returned null, and `.innerHTML = ...` on null threw a TypeError. In the in-game Chromium/CEF
browser, an unhandled TypeError inside a Promise `.then()` chain triggers a page reload —
which is why every button click appeared to kick the user back to Dashboard.

### Fix
Changed `el.remove()` to `el.style.display='none'` for all stock game elements:
```
nav.navbar.fixed-top, main.container-fluid, header.container, main.container, #errAlert
```
These elements now remain in the DOM but are invisible. SPA1's `render()` can still write
to `#tab-dashboard` etc. without crashing. Our `#tac-content` is a separate element we
control. Both SPAs write to different parts of the DOM without conflict.

### Remaining backend work required
See `frontend_needs.md`:
1. Remove the inline SPA from the page entirely
2. Fix double `<script src="/automation.js">` registration

**DLL rebuilt**: `Scripts/HTTPAutomation.dll` updated (?v=7).

## v4.8.0 — 2026-03-22
**frontend_needs.md: fix double `<script>` registration + confirm inline SPA already removed**

### Investigation findings (from re-reading all documentation)

**Inline SPA status: already removed**
`index.hbs` was audited. The file currently contains only: head CSS, the game navbar
structure, `{{#each bodySections}}`, the `<main>` tab divs, errAlert div, and
`{{#each footerSections}}`. There is NO inline JavaScript SPA anywhere in the file.
The SPA was removed in a prior session (likely v3.6.2→v3.7.0 timeframe when `index.hbs`
was restructured). The `frontend_needs.md` requirement is satisfied.

**Double `<script>` root cause: `_registered` was a plain `bool`, not atomic**
`Configurator._registered` was `internal static bool`. Two Bindito containers call
`Configure()` per session. If they run concurrently (or even if one reads before the
other writes), both can see `false` and both register `AutomationUiSection` — resulting
in two `BuildBody()` CSS blocks and two `BuildFooter()` `<script>` tags in the page.
This is the same TOCTOU race confirmed earlier for `Load()`/`Unload()` (fixed in v4.7.0).

### Fix: `_registered` now uses Interlocked.CompareExchange
Changed `internal static bool _registered` → `internal static int _registered = 0`.

```csharp
// In Configure():
if (Interlocked.CompareExchange(ref _registered, 1, 0) != 0) return;

// In GameServicesInitializer.Unload():
Interlocked.Exchange(ref Configurator._registered, 0);
```

Added `using System.Threading;` to `Configurator.cs`.
Updated `Unload()` reset from `Configurator._registered = false` to
`Interlocked.Exchange(ref Configurator._registered, 0)` — consistent with the atomic
write pattern used throughout.

### Version bump: `?v=7` → `?v=8`
`AutomationUiSection.BuildFooter()` now returns `/automation.js?v=8` to force a cache
bust in the in-game Chromium on first load after this DLL is deployed.

### DLL built clean, 44 KB

### Expected page structure after this fix
```html
<script>window.TB.levers = [...]</script>        ← game injects ONCE
<script>window.TB.adapters = [...]</script>      ← game injects ONCE
<style>nav.navbar.fixed-top{display:none}...</style>  ← our BuildBody, ONCE
<main class="container-fluid">...</main>         ← hidden, kept in DOM (v3.7.0)
<script src="/automation.js?v=8"></script>       ← our BuildFooter, ONCE
```
No duplicate CSS blocks. No duplicate script tags. No inline SPA.

## v4.9.0 — 2026-03-22
**Rename: index-levers-footer.hbs → index.hbs**

### What changed
- `HttpApi/index-levers-footer.hbs` renamed to `HttpApi/index.hbs`
  - The old `HttpApi/index.hbs` (the dead page-template file) was deleted first.
    Per `what_ive_learned.md` §3.6, `IndexHtmlEndpoint` reads `index.hbs` from
    StreamingAssets only — our mod's copy was never read by the game.
- `AutomationJsEndpoint.cs`: path updated from `index-levers-footer.hbs` → `index.hbs`
- `Plugin.cs`: both fallback path checks updated from `index-levers-footer.hbs` → `index.hbs`
- DLL built clean, 44 KB

### Questions pending in bot_chat.md
1. Whether the `<script>`/`</script>` wrapper should be removed from `index.hbs`
   now that it's no longer a Handlebars body section (frontend change — awaiting confirmation).

## v4.9.0 — 2026-03-22
**Rename: index-levers-footer.hbs → index.hbs**

- Old dead `HttpApi/index.hbs` (game page-template stub, confirmed ignored by game per §3.6) deleted
- `HttpApi/index-levers-footer.hbs` renamed to `HttpApi/index.hbs` — aligns with documented intent
- `AutomationJsEndpoint.cs`: path updated `"index-levers-footer.hbs"` → `"index.hbs"`; summary comment updated
- `Plugin.cs`: both fallback path scan checks updated to `"index.hbs"`
- `<script>` / `</script>` wrapper left in place per bot_chat.md A2 (stripped at serve time, harmless)
- `what_ive_learned.md` §3.6 updated with naming history note
- All 10 occurrences of `index-levers-footer.hbs` in `what_ive_learned.md` replaced with `index.hbs`
- DLL built clean, 44 KB
- `frontend_needs.md` marked fully resolved
- `PROJECT.md` rewritten: removed stale stacked "Next action" blocks, accurate current state

## v3.7.1 — 2026-03-22
**Fix: window.__TAC__ guard moved before window.TAC assignment — stops copy 2 overwriting TAC with stale S**

### Root cause
`/automation.js` is still loaded twice per page (double-registration backend bug not yet fixed).
The old guard position was:

```js
window.TAC = { on, off, newRule, ... };  // ← ran on BOTH copies
...
if(window.__TAC__)return;                // ← too late for copy 2
window.__TAC__=true;
takeover();
```

Copy 1: sets TAC → guard is false → sets __TAC__=true → takeover() → switchTab() sets S.tab
Copy 2: creates NEW S (tab:'dashboard') → **overwrites window.TAC** with one pointing to new S → hits guard → returns

After boot, window.TAC pointed to copy 2's fresh S where S.tab === 'dashboard' forever.
Every button click called render() via that TAC, which rendered the dashboard. Silent because
no errors — the code ran fine, just against the wrong state object.

### Fix
Moved guard to immediately before window.TAC:
```js
if(window.__TAC__)return;   // ← copy 2 exits HERE
window.__TAC__=true;
window.TAC = { ... };       // ← only copy 1 reaches this
takeover();
```

Also removed the now-redundant second guard at the bottom of the IIFE.

**File changed:** `HttpApi/index.hbs` (JS-only, no DLL logic change)
**DLL rebuilt:** `Scripts/HTTPAutomation.dll` updated (?v=9 for cache bust)

## v3.7.2 — 2026-03-22
**Fix: lever actions silent no-op — /api/levers no longer returns URLs, merge state instead of replacing**

### Root cause
The backend agent rewrote `/api/levers` to return a minimal format:
```json
[{"name":"HTTP Lever 1","state":false,"springReturn":false}]
```
The previous format included `switchOnUrl`, `switchOffUrl`, `redUrl`, `greenUrl`.

Our poll loop did `if(res[0]) S.levers = res[0]` — replacing S.levers entirely with the
stripped data. `S.levers` is seeded at boot from `window.TB.levers` (injected by the game's
own HBS template) which contains all the action URLs. After the first poll cycle, those URLs
were gone. `TAC.on(name)` called `post(l.switchOnUrl)` where `l.switchOnUrl === undefined`,
which silently fetched nothing.

Also noticed: field name changed from `isSpringReturn` → `springReturn`.

### Fix
Poll now merges state into existing S.levers rather than replacing:
```js
res[0].forEach(function(fresh) {
    var ex = S.levers.find(l => l.name === fresh.name);
    if(ex) {
        ex.state = fresh.state;
        ex.isSpringReturn = fresh.springReturn || fresh.isSpringReturn || false;
    }
});
```
URLs from `window.TB.levers` are preserved. Both field name variants handled.

**DLL rebuilt:** ?v=10

## v5.0.0 — 2026-03-22
**New: GET /api/levers — enriched lever list with switchOnUrl, switchOffUrl, colorUrl template**

### What changed

**`src/GameServices.cs`**
- Added `public static object Intermediary` — stores `HttpApiIntermediary` as `object` since the
  type is `internal` to `Timberborn.HttpApiSystem` and inaccessible from our assembly.
- Removed typed `HttpApiIntermediary` and `HttpApiUrlGenerator` fields (both internal — caused
  `CS0122` build failures when referenced as types in another assembly).
- Added `public struct LeverInfo { Name, State, IsSpringReturn }` — plain data carrier so
  `LeverEndpoint` never needs to reference the internal `HttpLeverSnapshot` type.
- Added `public static LeverInfo[] GetLevers()` — calls `Intermediary.GetLevers()` via reflection,
  iterates the returned `ImmutableArray<HttpLeverSnapshot>` as `IEnumerable`, reads each property
  by name via reflection, returns a plain `LeverInfo[]`. All exception paths return empty array.
- `Assign()` switch: `"HttpApiIntermediary"` case stores singleton as `object` (no cast needed).
- `ResetState()`: nulls `Intermediary`.
- `services.log`: includes `Intermediary` status line.

**`src/LeverEndpoint.cs`** (new)
- Handles `GET /api/levers` (exact match only — subpaths like `/api/levers/...` fall through to stock).
- Returns 503 if not ready or Intermediary is null.
- Calls `GameServices.GetLevers()`, builds JSON array for each lever.
- URL construction — all manual with `Uri.EscapeDataString(name)`, no `HttpApiUrlGenerator`:
  - `switchOnUrl`  = `/api/switch-on/<encoded>`
  - `switchOffUrl` = `/api/switch-off/<encoded>`
  - `colorUrl`     = `/api/color/<encoded>/{color}`
- `{color}` is a template placeholder the frontend replaces with any 6-char hex string.
  The game's own `POST /api/color/<n>/<hex>` endpoint executes the actual recolor.
  Any valid HTML hex works: `ff0000`, `00ff00`, `3a7bff`, `7b2cf8`, `ffffff`, etc.
  This drives real in-game model recoloring on the lever building, not just the indicator.

**`src/Configurator.cs`**
- Registered `LeverEndpoint` as singleton + `IHttpApiEndpoint`.

### Why `colorUrl` as a template instead of `redUrl`/`greenUrl`
The stock game only injected two hardcoded color URLs (red = `ff0000`, green = `00ff00`) in the
HBS template. The user's intent is to set any arbitrary hex color — using levers as a mechanism
to recolor in-game models. A template with `{color}` placeholder gives the frontend full control:
it can present a color picker and resolve the URL on the fly as `colorUrl.replace('{color}', hex)`.

### DLL built clean, 48 KB

## v3.8.0 — 2026-03-23
**Feature: Color wheel on levers tab + color action in automation rules**

### Changes

**`HttpApi/index.hbs`**

Poll merge updated — now also stores `colorUrl`, `switchOnUrl`, `switchOffUrl` from API
response when present, and handles new levers added mid-session.

`renderLevers()` rewritten:
- Red/Green buttons removed
- Native `<input type="color">` color wheel + 6-char hex text input added per lever
- Both controls are synced (wheel updates text, 6-char text updates wheel)
- "Apply" button calls `TAC.color(name, hex)`
- Color picker only shown when `lever.colorUrl` is present (graceful degradation)

`TAC.color(name, hex)` added:
- Validates hex is 6 chars `[0-9a-f]`
- Calls `POST lever.colorUrl.replace('{color}', hex)`
- `TAC.red()` and `TAC.green()` converted to aliases for backward compat with any
  saved automation rules that used those action names

`applyActions()` extended:
- New branch for `leverState === 'color'`
- Reads `action.leverColor` (6-char hex), POSTs to colorUrl template
- Logs to uiLog as 'rule' level

Automation simple editor action row updated:
- Lever state dropdown now has ON / OFF / COLOR options
- When COLOR selected, a color wheel + hex input appear inline in the action row
- Both controls call `TAC._updateAction(..., 'leverColor', hex)` on change

**DLL rebuilt:** ?v=11

## v3.9.0 — 2026-03-23
**Planning: comprehensive feature roadmap + bug documentation + skills install**

### Plans documented
- `plans_frontend.md` — full priority-ordered implementation plan for all requested features
- `bot_chat.md` — questions sent to backend agent on weather bug, inline SPA removal, sensor API, welcome.json

### Bug 1: Weather not updating (backend investigation required)
- Frontend display is correct — reads `S.gameState.isDrought` / `isBadtide`
- Backend reads `HazardousWeatherService.CurrentCycleHazardousWeather.Id`
- Property name or access pattern may be wrong for current game version
- Backend agent to probe and confirm

### Bug 2: Inactive/deleted rules still firing
Three root causes identified:
- A: `direct` mode fires every poll cycle, defeated by poll merge resetting lever.state
- B: Dual-SPA (inline TimberAutoControl still running its own automation)
- C: Two rules both targeting the same lever (shows as double-log-entries per cycle)
Frontend fix planned: lever._lastSetState guard + rising-edge trigger mode

### Skills installed
~600+ skills extracted from uploaded skill zips to `/mnt/skills/user/`.
Notable skills available: docx, xlsx, pdf, pptx, frontend-design, algorithmic-art,
artifacts-builder, systematic-debugging, brainstorming, aesthetic, webapp-testing.

## v5.1.0 — 2026-03-23
**Fix: double Load()/Unload()/Configure() — self-reset CAS bug**

### Root cause (confirmed from debug.log + bot_chat.md analysis)

The previous CAS approach had a critical flaw in `Unload()`:

```csharp
// OLD (broken):
Interlocked.Exchange(ref GameServices.LoadClaimed,   0);
Interlocked.Exchange(ref GameServices.UnloadClaimed, 0); // ← self-reset
```

The winning `Unload() #1` reset `UnloadClaimed = 0` at the END of its own body.
Since the two Unload() calls are sequential (not concurrent), `Unload() #2` arrived
AFTER `Unload() #1` had already reset the flag. It saw `UnloadClaimed = 0`, won the CAS,
and ran the full Unload body — including resetting `Configurator._registered = 0` a second time.

This second `_registered = 0` reset interleaved with the NEW session's two `Configure()` calls:
- Configure #1 wins CAS(_registered, 0→1), registers GameServicesInitializer
- Old-session Unload #2 fires, resets `_registered = 0`
- Configure #2 now also wins CAS(_registered, 0→1), registers ANOTHER GameServicesInitializer
- Two GSI instances → two Load() calls → two EventBus.Register() calls → every event fires twice
- Two `AutomationUiSection` registrations → two `<script src="/automation.js">` tags → two SPAs
- Every automation rule fires twice per poll cycle (confirmed in ui_log.txt: paired identical entries)

### Fix: cross-session reset pattern

The key insight: **`Unload()` must NOT reset `UnloadClaimed`**. It must stay at 1 after the
winning Unload runs. The NEXT session's `Load()` resets it at its very START.

```csharp
// In Load() — at the very top, BEFORE the LoadClaimed CAS:
Interlocked.Exchange(ref GameServices.UnloadClaimed, 0);
// Purpose: allow THIS session's Unload() to run (previous session left it at 1)

if (Interlocked.CompareExchange(ref GameServices.LoadClaimed, 1, 0) != 0)
{
    // skipped — another container already won
    return;
}

// In Unload():
Interlocked.Exchange(ref Configurator._registered, 0);
Interlocked.Exchange(ref GameServices.LoadClaimed, 0);
// UnloadClaimed intentionally NOT reset — stays at 1 until next Load() clears it
```

### Full session transition trace (correct behavior):

Initial state after first session: `LoadClaimed=1, UnloadClaimed=0, _registered=1`

1. Unload #1: CAS(UnloadClaimed, 0→1) — WINS. Resets `LoadClaimed=0`, `_registered=0`.
   Does NOT reset UnloadClaimed. State: `LoadClaimed=0, UnloadClaimed=1, _registered=0`
2. Unload #2: CAS(UnloadClaimed, 1→skip) — SKIPS ✓
3. Configure #1: CAS(_registered, 0→1) — WINS. Registers ONE GSI. State: `_registered=1`
4. Configure #2: CAS(_registered, 1→skip) — SKIPS ✓ (only ONE GSI registered!)
5. Load #1 (only one call, from the one registered GSI): resets UnloadClaimed=0.
   CAS(LoadClaimed, 0→1) — WINS. Full load runs once. State: `LoadClaimed=1, UnloadClaimed=0`

Result: ONE Load(), ONE Unload(), ONE GSI, ONE `<script>` tag, ONE automation loop per session.

### Expected log output after this fix:
```
Unload() — game scene tearing down...
Unload() — complete, ready for next session
Unload() — skipped (claimed by concurrent container)   ← only one runs
Load() — starting service resolution                   ← only one runs
Load() — complete
```
NO "Load() — skipped" line — because with only one GSI registered, Load() is only called once.

### DLL built clean, 48 KB

## v5.2.0 — 2026-03-23
**Fix: Weather/Hazardous MISSING after game update renamed singleton types**

### Finding from logs
`services.log` from session at 23:37:19 shows:
```
Weather:          MISSING
Hazardous:        MISSING
```
The singleton list no longer contains `WeatherService` or `HazardousWeatherService`.
Instead the game now registers them as `ModdableWeatherService` and
`ModdableHazardousWeatherService` — the concrete types are unchanged (probe confirmed
`WeatherService.IsHazardousWeather` and `HazardousWeatherService.CurrentCycleHazardousWeather`
still exist with identical signatures), only the DI registration name changed.

The IHazardousWeather probe confirmed:
- `HazardousWeatherService.CurrentCycleHazardousWeather` → `IHazardousWeather` with `Id` property
- `IHazardousWeather.Id` is still a string — "Drought" / "Badtide" values expected to be unchanged

`WeatherService.IsHazardousWeather` is still the correct `bool` property for `isHazardous`.
`GameStateEndpoint.cs` logic using `hazard.Id == "Drought"` etc. should still work once the
services are resolved.

### Fix
Added alias cases in `GameServices.Assign()` for both old and new names — the mod now
handles both pre-update and post-update Timberborn versions gracefully:

```csharp
case "WeatherService":
case "ModdableWeatherService":          // game update renamed the singleton
    if (GameServices.Weather == null)
        GameServices.Weather = s as WeatherService; break;

case "HazardousWeatherService":
case "ModdableHazardousWeatherService": // game update renamed the singleton
    if (GameServices.Hazardous == null)
        GameServices.Hazardous = s as HazardousWeatherService; break;
```

The `as` casts will return null if the runtime type is no longer assignable, which would
surface as MISSING in the next log — but since the probe confirmed the concrete types are
identical, the cast should succeed.

### Also noted from logs
- `WellbeingService` is in the singleton list with `AverageGlobalWellbeing` (int) — useful for
  a future `/api/population` `averageWellbeing` field. Add `"WellbeingService"` to Assign().
- Settlement name "8946541" — this is a NEW save with a different settlement from earlier sessions

### DLL built clean, 48 KB

## v5.3.0 — 2026-03-23
**ModdableWeathers mod support — rich weather API fields**

### What changed

**`GameServices.cs`**
- Added `public static object WeatherCycle` — stores `WeatherCycleService` from the
  ModdableWeathers mod (Steam workshop ID 3630523180) when it is installed.
  Stored as `object` — the type is internal to `ModdableWeathers.dll`.
- `Assign()` switch: `"WeatherCycleService"` case stores singleton as `object`.
- `ResetState()`: nulls `WeatherCycle`.
- `Load()` log: reports `WeatherCycle: OK (ModdableWeathers)` or `not present`.

**`GameStateEndpoint.cs` — new weather fields in `/api/gamestate`**

When ModdableWeathers is installed, `WeatherCycleService.CurrentStage.Stage` exposes:
- `WeatherId` (string) — the actual weather ID for the current stage, e.g. "Drought",
  "Badtide", "Monsoon", "Rain", "SurprisinglyRefreshing", or any mod-defined weather.
- `IsBenign` (bool) — whether this is a "safe" (non-hazardous) weather stage.
- `DaysSinceCurrentStage`, `CurrentStageTotalDays` on `WeatherCycleService` itself.
- Next stage `WeatherId` derived from `CurrentStage.Cycle.Stages[stageIndex + 1]`.

New fields in `/api/gamestate` response:
```json
{
  "weatherId":            "Monsoon",   // actual weather stage ID (replaces "Temperate"/"Drought"/"Badtide")
  "weatherIsHazardous":   false,       // !IsBenign
  "weatherDaysInStage":   8,           // total days this stage lasts
  "weatherDaysSinceStart": 3,          // days elapsed since this stage began
  "weatherDaysRemaining": 5,           // daysInStage - daysSinceStart
  "nextWeatherId":        "Drought",   // next stage's weather ID
  "moddableWeather":      true,        // true when ModdableWeathers mod is active
}
```

Backward-compatible fields kept and updated when ModdableWeathers is active:
- `weather`: still "Drought" / "Badtide" / "Temperate" (derived from `weatherId`)
- `isDrought`: true when `weatherId == "Drought"`
- `isBadtide`:  true when `weatherId == "Badtide"`
- `isHazardous`: true when `!weatherIsHazardous`

When ModdableWeathers is NOT installed, all new fields are present but with zero/empty
values and `moddableWeather: false`. Existing frontends reading `isDrought`/`isBadtide`/
`isHazardous`/`weather` continue working without changes.

### Reflection access path
```
WeatherCycleService (object)
  .CurrentStage      → DetailedWeatherStageReference
    .Stage           → WeatherCycleStage
      .WeatherId     → string   (e.g. "Monsoon")
      .IsBenign      → bool
    .StageIndex      → int
    .Cycle           → DetailedWeatherCycle
      .Stages        → ImmutableArray<WeatherCycleStage>
        [stageIndex+1].WeatherId → next weather
  .CurrentStageTotalDays  → int
  .DaysSinceCurrentStage  → int
```

All reflection calls are wrapped in a try/catch — any failure falls back to the
existing vanilla weather logic and logs a WARN. The endpoint never throws due to
ModdableWeathers being unavailable or a future API change.

### DLL built clean, 50 KB

## v5.3.1 — 2026-03-28
**Fix: isDrought/isBadtide detection wrong with ModdableWeathers + manifest version bump**

### Finding from Player.log
ModdableWeathers logs its stage transitions as:
```
Starting new weather stage: Cycle 9, stage 1, lasting 2 days
| - Weather: DroughtWeather (Drought)
```
The `WeatherId` field on the stage is the spec ID string — `"DroughtWeather"` and
`"BadtideWeather"` — not the shorthand `"Drought"` / `"Badtide"` that vanilla
`HazardousWeatherService.CurrentCycleHazardousWeather.Id` returns. v5.3.0's
backward-compat derivation `isDrought = weatherId == "Drought"` was therefore always
false when ModdableWeathers was active.

**Fix:** Accept both forms:
```csharp
isDrought = weatherId == "DroughtWeather" || weatherId == "Drought";
isBadtide = weatherId == "BadtideWeather" || weatherId == "Badtide";
```

### manifest.json updated
`manifest.json` was still at v2.0.0 — updated to v5.3.0 to match actual DLL version.
The mod manager now shows the correct version.

### DLL built clean, 50 KB

## v5.2.0 — 2026-03-28
**Fix: CAS guard bug (double Load/Unload) + weather display using new weatherId field**

### CAS bug fix (GameServices.cs)

**Root cause confirmed from debug.log:**
Both `Load()` calls still ran to completion. The previous fix (v5.1.0) put
`Interlocked.Exchange(ref UnloadClaimed, 0)` BEFORE the LoadClaimed CAS, meaning both
containers reset `UnloadClaimed` to 0 before either checked `LoadClaimed`. Then the
previous session's second `Unload()` arrived after both resets, saw `UnloadClaimed=0`,
won the CAS, and ran a second full teardown — resetting `Configurator._registered=0`
mid-transition, letting `Configure #2` also win, registering a second GSI, causing two
`Load()` calls in the new session.

**Fix:** Move `Interlocked.Exchange(ref UnloadClaimed, 0)` to AFTER the `LoadClaimed` CAS,
inside the winner's path only. Losing `Load()` callers never touch `UnloadClaimed`.

Expected log after fix:
```
Load() — starting service resolution    ← exactly once
Load() — skipped (claimed by ...)       ← exactly once
```
No double beavers. No double automation engine.

### Weather display (index.hbs + DLL rebuild for ?v=12)

**Root cause:** The backend's `isDrought`/`isBadtide` booleans are computed from
`HazardousWeatherService.CurrentCycleHazardousWeather.Id == "Drought"` which doesn't
work correctly (confirmed broken in earlier sessions). The backend now provides `weatherId`
(the actual stage ID string) and `weatherIsHazardous` (bool) as more reliable fields.
The frontend was still reading only the broken `isDrought`/`isBadtide` booleans.

**Fixes across index.hbs:**

1. **Dashboard weather badge** — now reads `weatherId` first, falls back to `isDrought`/
   `isBadtide` for older DLL versions. Shows custom weather names (e.g. "Monsoon") when
   `weatherId` is something other than Drought/Badtide/Temperate. Shows `weatherDaysRemaining`
   in parentheses when available.

2. **evalCondition (Simple rules)** — `isDrought`/`isBadtide`/`isHazardous` conditions now
   check `weatherId` as primary source, with old boolean fields as fallback.

3. **runFbdRule INPUT_GAMESTATE** — same weatherId-first logic applied.

4. **FBD INPUT_GAMESTATE node params** — added `weatherIsHazardous` ("Any Hazardous Weather")
   as a field option alongside the existing legacy options.

5. **Simple rule gamestate condition dropdown** — added `weatherIsHazardous` option;
   renamed `isHazardous` to `isHazardous (legacy)` to make the distinction clear.

6. **Code editor reference card** — updated gameState type description to include
   `weatherId`, `weatherIsHazardous`, `weatherDaysRemaining`.

**DLL rebuilt:** ?v=12

## v5.2.1 — 2026-03-28
**Fix: "⚠ Temperate" during drought — show "Hazardous" when type is unknown**

### What the live API returns during drought
```json
"weatherId": "Temperate", "isDrought": false, "isBadtide": false,
"isHazardous": true, "weatherIsHazardous": true, "weatherDaysRemaining": 1
```
`weatherId` is "Temperate" because `CurrentCycleHazardousWeather` returns null on the
backend, so the fallback "Temperate" string is used. But `isHazardous`/`weatherIsHazardous`
are correctly set to true by `WeatherService.IsHazardousWeather`.

### Frontend fix
Added `unknownHazard` flag: `isHazardous && !isDrought && !isBadtide`.
When true, displays "⚠ Hazardous" (amber) instead of "⚠ Temperate".
Temperate (green ☀) only shown when `!isHazardous`.

Priority order: Drought → Badtide → Hazardous (unknown type) → Temperate.

This is a workaround. Root cause is in the backend — `weatherId` should return "Drought"
during drought. Backend investigation documented in bot_chat.md.

**DLL rebuilt:** ?v=13 (JS-only change needed the bump for cache bust)

## v5.3.0 — 2026-03-28
**Bug fixes + Feature 1 (Trigger mode) + Feature 3 (AI clipboard) + Feature 6 (welcome popup)**

### Bug 2 fix — applyActions deduplication guard
Added `_leverLastAction` timestamp map and `DEDUP_MS = 4000`.
Before posting to the lever API, checks: same lever+state was sent within 4 seconds → skip.
This stops `direct` mode from fighting back when the poll merge resets `lever.state` before
the HTTP response arrives. Also clears old `localStorage['tac_rules']` on `toggleRule` and
`delRule` to prevent any legacy SPA from resurrecting deleted rules.

### Feature 1 — Trigger mode (rising-edge) + auto-reset latch

**New function type: Trigger**
Fires actions exactly once when conditions transition from false→true. Does nothing while
conditions remain true. Waits for conditions to go false, then fires again on the next
true transition. This is the correct behaviour for "set lever ON when drought starts" —
not "keep hammering the lever ON every 3 seconds during drought".

Previous "Direct" renamed to "Enforce" in descriptions (still stored as `'direct'`
internally for backward compatibility). UI shows all four types:
Trigger · Enforce · Timer · SR-Latch

`_prevTriggered` runtime field tracks previous state per rule (not persisted).

**Auto-reset latch — `resetConditions[]`**
SR-Latch now shows an "AUTO-RESET WHEN" section. Add any number of conditions —
when they become true the latch resets automatically (no button click needed).
Reset conditions use same types as main conditions: Adapter, Lever, Game state, Time, Population.
`resetCondMode` ('or'|'and') controls combining. `resetConditions` stored in rule.simple.

New TAC methods: `_addResetCond(id, type)`, `_removeResetCond(id, idx)`, `_updateResetCond(id, idx, key, value)`.
"Reset Latch now" manual button still present for immediate override.

### Feature 3 — Code editor: live state sidebar + 📋 Copy for AI

**Live state sidebar** — narrow panel left of the textarea showing all adapters and levers
with live ON/OFF values. Updates on every render without re-rendering the textarea.

**📋 Copy for AI button** — copies structured AI-ready context to clipboard:
- All adapter names + live states
- All lever names + live states
- Full gameState object (if DLL loaded)
- Full population object (if DLL loaded)
- Current script content
- Blank "Task" section for the user to describe what they want

Uses `navigator.clipboard.writeText` with `execCommand('copy')` fallback.

### Feature 6 — Welcome popup (welcome.json)

`takeover()` now calls `GET /api/welcome` on boot. If the backend returns `{title, text}`,
a modal overlay appears before the dashboard loads. Dismissed by clicking OK or the overlay.
Backend must implement `GET /api/welcome` — returns 200+JSON if `welcome.json` exists in
mod folder, 404 if not. Frontend silently ignores 404.

`showWelcomeModal(title, text)` — standalone function, no Bootstrap dependency, inline styles.

### DLL rebuilt: ?v=15

## v5.3.1 — 2026-03-28
**Hotfix: black screen — missing `if(rule.mode==='code'){` guard in runAutomation**

### Root cause
The `replace_lines` operation that rewrote `runAutomation()` (v5.3.0) replaced lines
547–577. The replacement content ended with the closing `}` of the `if(rule.mode==='simple')`
block. The line immediately following in the original file — `if(rule.mode==='code'){` —
was NOT included in the replacement range, and was consequently deleted.

This produced the structure:
```js
        } // closes if(simple)
        // MISSING: if(rule.mode==='code'){
            var _prevCodeErr=rule._lastError;
            runCodeRule(rule);
            return;      // ← exits forEach callback for ALL non-simple rules
        }                // ← now closes the forEach callback, not a code block
        if(rule.mode==='fbd'){  // ← dead code; 'rule' undefined here
            runFbdRule(rule);   // ← throws ReferenceError
        }
    });
```

The JavaScript engine accepted this as syntactically valid because the braces balanced
(the forEach callback closed early, and the fbd block was loose code inside runAutomation
but outside the forEach). However, at runtime `rule` was undefined in the fbd block,
causing a ReferenceError that was thrown inside the `.then()` handler of `poll()`.

Effect: `runAutomation()` threw, `render()` was never called after the first poll,
the DOM stayed at the initial `switchTab('dashboard')` output for ~500ms (the network
round-trip time), then the poll's `.finally()` ran (clearing the spinner) but `render()`
never fired — leaving the page at the initial unpolled state which appeared black.

### Fix
Restored `if(rule.mode==='code'){` immediately after the `if(simple)` closing brace.

**DLL rebuilt:** ?v=16

## v5.3.2 — 2026-03-28
**Hotfix: second syntax error — `_removeCondition:function(id,idx){` deleted by patch**

### Root cause
The `patch_file` call that inserted `_addResetCond`, `_removeResetCond`, `_updateResetCond`
used `old_str = "    _removeCondition:function(id,idx){"` to locate the insertion point.
The patch replaced that string with the three new methods, but did NOT include
`_removeCondition:function(id,idx){` in the `new_str`. The method header was deleted.

The body of `_removeCondition` (`var r=S.rules.find(...)...splice(idx,1)...`) was left
orphaned inside the TAC object literal, producing invalid syntax:

```js
_updateResetCond:function(...){...},
var r=S.rules.find(...);...splice(idx,1);...},  // ← INVALID: var in object literal
_updateCondition:function(...){...},
```

This is a JavaScript SyntaxError. The script fails to parse — nothing runs at all.
The "half second" the UI showed was the game's own stock HTTP API page briefly visible
before the script error left `takeover()` uncalled.

### Fix
Restored `_removeCondition:function(id,idx){` immediately after `_updateResetCond`.

### Prevention: check_syntax.js added
A JScript-based syntax checker (`check_syntax.js`) is now in the mod root. Run with:
```
cscript //nologo //e:jscript check_syntax.js
```
Prints `SYNTAX OK` and exits 0 if no errors, or prints the error line region and exits 1.
Run this after every batch of JS patches before rebuilding the DLL.

**DLL rebuilt:** ?v=17

## v5.4.0 — 2026-03-28
**Feature 2a: FBD pin usability + Feature 2d/2e: new FBD nodes**

### Feature 2a — Bigger pin hit targets + hover highlight
- `FBD_PR = 6` — visual pin circle size unchanged
- `FBD_PR_HIT = 12` — invisible hit radius for click/drag detection (was `FBD_PR + 5 = 11`, now explicit and larger)
- `fbdDrawPin()` — draws a cyan glow ring (`rgba(0,220,255,.75)`, radius `FBD_PR+4`) around any pin the mouse is within `FBD_PR_HIT` pixels of
- `fbdOnMouseMove()` — now always calls `fbdDrawCanvas()` (was only when dragging/wiring), so hover highlight redraws on every mouse move

### Feature 2d — Constant input nodes: ALWAYS_HIGH / ALWAYS_LOW
Two new input nodes with no parameters:
- **ALWAYS HIGH** — output permanently `true`. Use to force a latch SET input or test wiring.
- **ALWAYS LOW** — output permanently `false`. Use to disable branches without disconnecting.

### Feature 2e — New logic blocks (7 new node types)
All pure JS state machines, no backend needed:

| Node | Label | Behaviour |
|------|-------|-----------|
| `LOGIC_NAND` | NAND | `!(A AND B)` — correct boolean NAND |
| `LOGIC_NOR` | NOR | `!(A OR B)` — correct boolean NOR |
| `LOGIC_TOF` | TOF (Off Delay) | Output HIGH immediately on IN=true; stays HIGH for `preset` seconds after IN goes false |
| `LOGIC_TP` | TP (Pulse) | Fires a fixed-length pulse (`preset` s) on each rising edge of IN |
| `LOGIC_GEN` | Generator | Free-running oscillator: HIGH for `onTime` s, LOW for `offTime` s, repeat |
| `LOGIC_CTU` | Counter (CTU) | Counts rising edges on CU input; resets on R; output HIGH when count ≥ preset |
| `LOGIC_RTC` | Real-Time Clock | Fires HIGH during a wall-clock time window (`fromHour`–`toHour`, browser time) |

Runtime state fields per node (not persisted, reset on page load):
- TON/TOF: `_timerStart`
- TP: `_pulseStart`, `_prevIn`
- GEN: `_genStart`
- CTU: `_count`, `_prevCU`

### check_syntax.js rewritten as structural checker
Replaced the fragile JScript `eval()` syntax checker with a targeted string-presence checker
that verifies 29 critical structural invariants (guard ordering, method presence, engine cases,
node type registrations). Runs in ~1 second, no false positives from browser globals.
All 29 checks pass on this build.

**DLL rebuilt:** ?v=18

## v5.5.0 — 2026-03-28
**Feature: FBD wire snap-to-closest-input-pin within 300px**

### How it works
When dragging a wire from an output pin, the system continuously scans all input pins on all
other nodes. If any input pin is within `FBD_SNAP_RADIUS = 300` world-units of the mouse
cursor, the wire tip snaps to the closest one automatically.

**Visual feedback:**
- The wire draws to the snapped pin position instead of the raw mouse cursor
- A bright cyan ring (`rgba(0,220,255,0.9)`, radius `FBD_PR+7`) pulses on the snap target
- A softer outer ring (`rgba(0,220,255,0.25)`, radius `FBD_PR+14`) shows the snap zone boundary
- Both rings disappear when no target is within range

**On mouse release:**
- If `FBD.snapTarget` exists, it's used directly — no need to click precisely on the pin
- Falls back to `fbdPinAt` exact hit-test if no snap target (preserves existing behaviour)
- Any existing edge on the target input pin is replaced (one edge per input, as before)

### New additions
- `fbdSnapTarget(wx, wy, nodes, fromNodeId)` — iterates all input pins, returns the closest
  within `FBD_SNAP_RADIUS` as `{node, pin, pos, dist}` or null
- `FBD.snapTarget` — stored on `FBD` state during wire drag, cleared on release/detach
- `FBD_SNAP_RADIUS = 300` — constant, easily tunable
- `fbdDetach()` now clears `FBD.snapTarget`

**DLL rebuilt:** ?v=19

## v5.4.0 — 2026-03-28
**Three new backend features for frontend agent**

### Fix 1 — weatherId normalization (Priority 1)
`WeatherCycleService.CurrentStage.Stage.WeatherId` returns the ModdableWeathers spec ID
string which ends with "Weather" — e.g. "DroughtWeather", "BadtideWeather",
"TemperateWeather". The frontend checks `gs.weatherId.toLowerCase() === 'drought'`
which was failing against "droughtweather".

Fix: strip trailing "Weather" suffix in `GameStateEndpoint.cs` when ModdableWeathers is
active. "DroughtWeather" → "Drought", "BadtideWeather" → "Badtide", etc. Custom modded
weather IDs that don't end in "Weather" (e.g. "Monsoon", "Rain") pass through unchanged.

### Feature 2 — GET /api/welcome (Priority 2)
New `WelcomeEndpoint.cs` registered in `Configurator.cs`.
- `GET /api/welcome` → 200 `{title, text}` if `welcome.json` exists in mod folder
- `GET /api/welcome` → 404 if file not found (frontend ignores silently)
- Custom minimal JSON string parser — no extra library dependency
- welcome.json format: `{"_comment":"Delete this file...","title":"...","text":"..."}`
- `\n` in `text` field parsed to real newlines before returning (frontend uses pre-wrap)

### Feature 3 — Beaver wellbeing + population averages (Priority 3)
**New DLL references:** `Timberborn.NeedSpecs`, `Timberborn.Wellbeing`,
`System.Collections.Immutable`, `Timberborn.BlueprintSystem`

**`GameServices.cs`:**
- Added `WellbeingService WellbeingSvc` → `AverageGlobalWellbeing` (int, colony average)
- Added `WellbeingLimitService WellbeingLimit` → `GetMaxWellbeing(tracker)`, `MaxBeaverWellbeing`
- Both resolved in `Assign()` switch by type name, both nulled in `ResetState()`

**`PopulationEndpoint.cs` — /api/beavers response** — new fields per beaver:
```json
{
  "wellbeing": 8,         // int — current score from WellbeingTracker.Wellbeing
  "maxWellbeing": 14,     // int — per-beaver max from WellbeingLimitService.GetMaxWellbeing
  "needs": [              // all needs where AffectsWellbeing=true and NeedIsActive=true
    {
      "id": "Hunger",
      "name": "Need.Hunger",         // DisplayNameLocKey (localization key)
      "wellbeingNow": 2,             // current contribution: NeedManager.GetNeedWellbeing()
      "wellbeingMax": 2,             // max contribution when favorable: NeedSpec.GetFavorableWellbeing()
      "wellbeingBad": 0,             // contribution when unfavorable: NeedSpec.GetUnfavorableWellbeing()
      "isFavorable": true,           // NeedManager.NeedIsFavorable()
      "points": 0.85                 // raw need points: NeedManager.GetNeedPoints()
    },
    { "id": "Coffee", "name": "...", "wellbeingNow": 3, "wellbeingMax": 3, ... }
  ]
}
```

The `needs` array covers ALL needs (Hunger, Thirst, Sleep, Coffee, different foods, etc.)
— not just the hardcoded 5. Frontend can sum `wellbeingMax` across all needs to get the
dynamic per-beaver maximum, and use it for color-coded progress bars.

**`PopulationEndpoint.cs` — /api/population response** — new aggregate fields:
```json
{
  "averageHunger":    0.82,   // float — averaged across live beaver list
  "averageThirst":    0.71,
  "averageSleep":     0.90,
  "averageWellbeing": 9,      // int — WellbeingService.AverageGlobalWellbeing (game-computed)
  "maxWellbeing":     14      // int — WellbeingLimitService.MaxBeaverWellbeing (global cap)
}
```

### DLL built clean, 54 KB

## v5.5.0 — 2026-03-28
**GET /api/sensors — automation transmitter building enumeration**

### What this does
Exposes all automation transmitter buildings (sensors) placed in the map via
`AutomatorRegistry.Transmitters`. Each entry gives:
- `id` — `Automator.AutomatorId` (stable GUID string for the session)
- `name` — `Automator.AutomatorName` (user-assigned building name)
- `type` — detected from Spec component names on the same GameObject
- `unit` — hardcoded per sensor type (m³/s, m, %, HP, etc.)
- `isOn` — derived from `Automator.State` (AutomatorState enum → bool)
- `value: null` — numeric measurement value not yet accessible (internal types)

### Sensor type detection
The `Automator.AllComponents` list is iterated to find component type names matching
known Spec patterns:
`FlowSensorSpec` → FlowSensor (m³/s), `DepthSensorSpec` → DepthSensor (m),
`ContaminationSensorSpec` → ContaminationSensor (%), `Chronometer*` → Chronometer,
`WeatherStation*` → WeatherStation, `PowerMeterSpec` → PowerMeter (HP),
`PopulationCounter*` → PopulationCounter (beavers), `ResourceCounter*` → ResourceCounter (goods),
`ScienceCounter*` → ScienceCounter (pts), `Memory*` → Memory, `Relay*` → Relay

### Architecture note
`AutomatorRegistry` is a DI singleton now captured in `GameServices.AutomatorReg`.
All access is via reflection (internal types). `AutomatorState` enum maps to bool by
checking string value != "Off"/"Undetermined"/"Unknown".

Numeric measurement values are in internal component types not yet probed. The ON/OFF
signal from `Automator.State` is sufficient for the frontend to use sensors as boolean
automation inputs — matching how the game's own automation system uses them.

### DLL built clean, 59 KB

## v5.6.0 — 2026-03-28
**Sensors: full frontend integration of /api/sensors**

### New: Sensors tab
A dedicated **Sensors** tab sits between Adapters and Population in the navbar.
Shows a table of all automation sensor buildings placed in the settlement:
- Icon (per sensor type), user-assigned Name, Type (human-readable), Value (shows `—` while `null`), Signal ON/OFF badge
- Empty state with instructions if no sensors are placed
- Note that numeric `value` is always null until the backend can expose internal measurement components
- Polls every 3 seconds alongside adapters/levers/gamestate

### Sensor types mapped: FlowSensor, DepthSensor, ContaminationSensor, Chronometer, WeatherStation, PowerMeter, PopulationCounter, ResourceCounter, ScienceCounter, Memory, Relay, Unknown

### Dashboard: Sensors mini-card
When sensors are present, a `📱 Sensors (N)` mini-card appears in the summary column showing ON/OFF counts, matching the Levers and Adapters cards.

### Simple rules: sensor condition type
New condition type `sensor` — pick any sensor by name, check whether its signal is ON or OFF.
```
[Sensor "Inlet Flow"] signal is ON  →  fire rule
```

### FBD: INPUT_SENSOR node
New input node in the Input category. Outputs HIGH when the selected sensor's `isOn` matches the configured state (ON or OFF). Requires DLL — shows ⚠ when unavailable.

### Code sandbox: sensors variable
`sensors` is now exposed alongside `adapters`, `levers`, `gameState`, `population`:
```js
var flowSensor = sensors.find(s => s.name === 'Inlet Flow');
if (flowSensor && flowSensor.isOn) setLever('Pump', 'on');
```

### 📋 Copy for AI: includes sensors
The clipboard context dump now includes the full sensors array.

### check_syntax.js: 7 new checks (36 total)
All sensor integration points verified on every run.

**DLL rebuilt:** ?v=20

## v5.6.1 — 2026-03-28
**Fix: black screen after sensor poll — defensive array guards + poll try-catch**

### Root cause
`S.sensors` was assigned directly from `res[5].sensors` without checking if it was
actually an array. If the backend returns `{"sensors": null}`, or an unexpected object
shape, `S.sensors` would be set to a non-array value. The next `render()` call from
inside the poll `.then()` would invoke `S.sensors.filter(...)` in `renderDashboard()`,
which throws `TypeError: S.sensors.filter is not a function` — silently swallowed by the
Promise chain, killing the render cycle. The user sees black after ~0.5s (the network fetch
time for the first poll).

### Fixes

**Poll sensor assignment** — now guarded with `Array.isArray`:
```js
if(res[5]&&Array.isArray(res[5].sensors)) S.sensors=res[5].sensors;
else if(res[5]&&Array.isArray(res[5])) S.sensors=res[5]; // flat array fallback
```

**renderDashboard** — uses `_sensors=Array.isArray(S.sensors)?S.sensors:[]` for all
sensor-related references. Will never throw even if S.sensors is garbage.

**renderSensors** — uses `slist` local variable from `Array.isArray` check. Also guards
`s.type` with `(s.type||'Unknown')` before calling `.replace()`.

**poll().then() try-catch** — wrapped the entire `.then()` body in try/catch. Any
runtime error now calls `uiLog('err', 'Poll render error: ...')` instead of silently
killing the render loop. This is the error surfacing mechanism that was missing.

**DLL rebuilt:** ?v=21

## v5.6.2 — 2026-03-28
**Fix: black screen — replace static ?v=N with Unix timestamp URL for automation.js**

### Root cause (definitive)
The Timberborn in-game CEF browser has a PERSISTENT DISK CACHE that survives game restarts.
`/automation.js?v=19` was cached before `Cache-Control: no-store` was added to the endpoint
(or the stock `IndexHtmlEndpoint` that serves the page HTML doesn't send no-store, so the
page itself was cached with v=19 in it).

On each page load, the browser served `/automation.js?v=19` from disk cache using the OLD
broken code (no sensor guards, missing try-catch). Because scripts execute in DOM order, and
the v=19 tag sometimes appeared first (from the cached page), v=19 won the `__TAC__` guard
race and ran the old code — which crashed on missing `S.sensors`, producing a black screen.

### Fix: timestamp-based URL
`AutomationUiSection.BuildFooter()` now returns:
```
<script src="/automation.js?t=1742000000000">
```
where `t` is the current Unix millisecond timestamp (via `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`).

Every game session generates a unique URL. The browser has never seen it before, so it always
fetches fresh. No old cached version can match the URL. The v=19 (or any old version) remains
in the browser cache under its own URL but is ignored because it's not referenced by the page.

`AutomationJsEndpoint` already used `path.StartsWith("/automation.js")` so `?t=...` queries
work without any endpoint changes.

### What this resolves permanently
- No more static `?v=N` management — no version number to maintain or forget to bump
- Any version cached in the CEF browser from before this fix becomes permanently irrelevant
- The `__TAC__` guard remains as secondary protection, but is no longer needed for this case

## v5.6.3 — 2026-03-28
**Fix: definitive black screen — extra closing braces in fbdOnMouseUp broke IIFE**

### Root cause (exact)
The snap-to-pin patch (`patch_file` replacing the inner `if(FBD.wire&&e.button===0)` block)
replaced the target string but left behind the OLD tail of `fbdOnMouseUp`:

```js
// Old tail left in file after the new code was inserted:
        FBD.wire=null;fbdDrawCanvas();return;   // dead code
    }                                            // EXTRA closing brace
    FBD.wire=null;                               // dead code
```

The extra `}` at what was line 284 closed `fbdOnMouseUp` prematurely. The next `}` (at
line 286) then closed the IIFE function body. `function fbdOnContextMenu(e)` at line 287
appeared OUTSIDE the IIFE, making it a top-level declaration after an expression statement.
The JS parser expected an operator but saw `function` → "Unexpected token 'function'".

Because the IIFE threw a SyntaxError, NOTHING ran. `takeover()` was never called. The page
went black after the network request time (~0.5s). No `window.onerror` either because the
error was a compile-time SyntaxError, not a runtime throw.

### Fix
Removed the 4 orphaned lines. `fbdOnMouseUp` now correctly ends with:
```js
    FBD.wire=null;FBD.snapTarget=null;
}
```

### check_syntax.js rewritten — now uses Node.js (vm.Script)
The JScript-based checker gave too many false positives (`'use strict'`, `.padStart()`,
`window.onerror` parameter named `line`). Replaced entirely with a Node.js script using
`vm.Script` which gives exact file line numbers and correctly parses all modern JS.

New invocation: `node tools\check_syntax.js`
Checks: 39 structural invariants + full syntax parse.

This checker would have caught the extra brace immediately: it would have reported
"Unexpected token 'function'" at line 287 and shown the context.

## v5.7.0 — 2026-03-29
**Feature 2b: Inline on-canvas FBD node parameter editing**

### What changed
Removed parameter editing from the HTML sidebar. Parameters are now edited directly on the
FBD canvas via a panel that appears below the selected node.

### How it works
**`fbdParamDefs(node)`** — new function that returns a typed array of parameter definitions
for every node type. Each entry has `{key, label, type, opts/min/max/step, val}`. Types are
`'select'`, `'number'`, or `'boolean'`.

**`fbdDrawParamPanel(ctx, node)`** — called at the end of `fbdDrawNode()` when the node is
selected. Draws a dark rounded-rect panel below the node with one row per parameter. Each
value box is registered into `FBD.paramInputs` as a world-space hit rect
`{nodeId, key, type, opts, val, x, y, w, h}`.

**`fbdShowInlineInput(pi)`** — fires when a param hit rect is clicked in `fbdOnMouseDown`.
Converts world coords to viewport coords (`world + pan + canvasRect`) and creates a
`position:fixed` `<select>` or `<input type="number">` overlaid on the canvas, pre-filled
with the current value. Confirms on `Enter`/`blur` (calls `TAC._fbdUpdateParam`), cancels
on `Escape`. Boolean params (`springReturn`) toggle immediately with no input element.

**`FBD.paramInputs`** is reset to `[]` at the top of every `fbdDrawCanvas()` and repopulated
during node draw, so hit rects always reflect the current pan offset.

**`FBD.inlineInput`** holds a reference to the active overlay element. It is cleaned up
(`.remove()`) in: `fbdOnMouseDown` (deselect or drag-start on different node), `fbdDetach`,
`_fbdDeleteNode`, and `attachFbdCanvas` (rule switch).

**`renderFbdSidebar`** now returns only the node library buttons — the `if(FBD.selected)`
param block and the entire `renderFbdNodeParams` function are no longer called from the
sidebar. `renderFbdNodeParams` is kept in the file as dead code (still used nowhere) in case
it's needed for reference; it can be removed in a future cleanup pass.

### Files changed
- `HttpApi/index.hbs` — all changes

---

## v5.8.0 — 2026-03-29
**Feature 4: Simple rule editor — horizontal three-column layout + 3 new condition types**

### Layout redesign
`renderSimpleEditor` return value changed from a single `.card` with three vertical sections
separated by `<hr>` to a flex row of three separate cards:

```
[ IF — Conditions ]  →  [ THEN — Function Type ]  →  [ DO — Actions ]
```

- Left card (`flex:1; min-width:190px`): conditions with blue **IF** badge
- Centre card (`flex:0 0 220px`): function type + timer/latch controls with grey **THEN** badge
- Right card (`flex:1; min-width:190px`): actions with green **DO** badge
- `→` separators between cards (`.text-muted`, non-interactive)
- `flex-wrap:wrap` on the outer container so it degrades to vertical on narrow viewports

### New condition types
Three new entries added to the `condTypes` dropdown (main conditions only — not reset conditions):

| Key | Label | Fields | Eval logic |
|-----|-------|--------|------------|
| `duration` | Adapter ON for N sec | `adapterName`, `durationSec` | Tracks `cond._startTime`; returns true once adapter has been ON continuously for `durationSec` seconds; resets `_startTime` when adapter goes OFF |
| `popchange` | Population rate | `field`, `direction` (growing/shrinking) | Compares `S.population[field]` against `cond._prevValue` set the previous poll cycle; returns true when value moved in the specified direction |
| `daycycle` | Day of cycle | `op`, `threshold` | Numeric comparison of `S.gameState.dayNumber` using the same gt/gte/lt/lte/eq operators as the population condition |

`_startTime` and `_prevValue` are prefixed with `_` so `saveRules()` strips them from the
persisted JSON (the replacer already skips keys starting with `_`).

Defaults added to `TAC._addCondition` for all three new types.

### Files changed
- `HttpApi/index.hbs` — all changes
