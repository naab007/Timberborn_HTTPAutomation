# HTTPAutomation — Project Overview

## What this is
A Timberborn mod (no BepInEx) that replaces the built-in HTTP API web UI with a full automation dashboard.

### How the UI takeover works
`AutomationUiSection` injects CSS that hides the stock UI and `<script src="/automation.js?v=8">`. The JS is served from `HttpApi/index.hbs` via `AutomationJsEndpoint`. Editing `index.hbs` takes effect on next browser refresh — no DLL recompile needed.

### DLL endpoints
| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/gamestate` | GET | Cycle, day, time, weather, saveName (settlement name) |
| `/api/population` | GET | Aggregate population counts |
| `/api/beavers` | GET | Live beaver list (EventBus-maintained) |
| `/api/beavers/<id>` | GET | Single beaver by runtime hash ID |
| `/api/beavers/<id>/dismiss` | POST | Unassign beaver from workplace |
| `/api/ping` | GET | Health: version, ready, saveName, beaverCount, eventBus |
| `/api/welcome` | GET | Returns `{title,text}` from `welcome.json` or 404 if not present |
| `/api/automation?save=<n>` | GET | Fetch stored rules for settlement name |
| `/api/automation?save=<n>` | POST | Store rules for settlement name |
| `/api/log?lines=N` | GET | Last N lines of debug.log (default 200, max 2000) |
| `/api/log` | POST | Save frontend UI log to ui_log.txt |
| `/automation.js` | GET | Serves HttpApi/index.hbs as raw JS |

### Web UI tabs
- **Dashboard** — game state + population + levers/adapters/automation overview (per-mode counts)
- **Levers** — toggle any HTTP Lever on/off/red/green
- **Adapters** — live state of all HTTP Adapters
- **Population** — aggregate counts + per-beaver cards + dismiss button (requires DLL)
- **Automation** — rule list + Simple/Code/FBD editors, reorder/export/import, per-rule errors
- **Logs** — filterable frontend activity log (ok/rule/sync/warn/err)

### Save-game rule persistence
`saveName` in `/api/gamestate` returns the settlement name from `SettlementReferenceService`.
On each poll, if saveName changes:
1. `loadRulesFromServer(saveName)` fetches `GET /api/automation?save=<n>`
2. Server rules found → load and cache in localStorage
3. No server rules + local rules exist → upload them (first-run migration)
4. `saveRules()` debounces a `POST /api/automation?save=<n>` 500ms after any rule change
5. 💾 saveName shown in navbar

### Backend source files
| File | Purpose |
|------|---------|
| `ModLog.cs` | Thread-safe logger → `debug.log` (500 KB rolling) |
| `Plugin.cs` | `IModStarter` — sets `Plugin.ModDirectory` |
| `Configurator.cs` | `[Context("Game")]` — atomic Interlocked claim, registers all once |
| `GameServices.cs` | Static cache + `GameServicesInitializer : ILoadable/IUnloadableSingleton` |
| `GameStateEndpoint.cs` | `/api/gamestate`, `/api/ping`, `/api/debug` |
| `PopulationEndpoint.cs` | `/api/population`, `/api/beavers` |
| `AutomationStorageEndpoint.cs` | `/api/automation` GET+POST per settlement |
| `AutomationJsEndpoint.cs` | `/automation.js` — serves `HttpApi/index.hbs` |
| `LogEndpoint.cs` | `/api/log` GET+POST |
| `AutomationUiSection.cs` | `IHttpApiPageSection` — CSS hide + script tag inject |
| `HttpResponseHelper.cs` | CORS + Cache-Control: no-store on all responses |

### Key concurrency guards (all use Interlocked.CompareExchange)
- `Configurator._registered` — only one of the two Bindito containers registers anything
- `GameServices.LoadClaimed` — only one container runs `Load()` fully
- `GameServices.UnloadClaimed` — only one container runs `Unload()` fully; winning instance resets all three flags

### Key DI constraint
`BeaverCollection` not in Bindito child containers. `GameServicesInitializer` uses
`ISingletonRepository.GetSingletons<object>()` + EventBus `[OnEvent]` handlers for live beaver tracking.
See `what_ive_learned.md` §3.3 and §20.

## What was done last
**v5.5.0 — GET /api/sensors**

`AutomatorRegistry.Transmitters` accessed via reflection gives all placed sensor/transmitter buildings. Each returns `id` (GUID), `name`, `type` (detected from Spec component names on the GameObject), `unit`, `isOn` (from `Automator.State` enum → bool). `value` is `null` — numeric measurements are in internal component types not yet probed.

Also in this session: docs updated (hooks.md at v5.5.0), PROJECT.md updated.

DLL built clean, 59 KB.

## Next action
Load a save with automation buildings placed. Call `GET /api/sensors` and verify:
- Sensors appear with correct `name` and `type`
- `isOn` toggles when the sensor's condition is met/unmet in-game
- `id` is a non-empty GUID string

If `type` shows `"Unknown"`, check `debug.log` for which component names are on that building — add a new case to `TryGetSensorType()` in `SensorEndpoint.cs`.

1. **weatherId normalization:** ModdableWeathers spec IDs end in "Weather" (e.g. "DroughtWeather"). Stripped the suffix so `weatherId` returns "Drought"/"Badtide"/"Temperate". Frontend's `gs.weatherId.toLowerCase() === 'drought'` now works correctly.
2. **GET /api/welcome:** New `WelcomeEndpoint.cs`. Returns `{title,text}` if `welcome.json` exists in mod folder, 404 if not. Frontend welcome popup is now fully unblocked.
3. **Wellbeing + population averages:** `WellbeingService` + `WellbeingLimitService` added to `GameServices`. Per-beaver `/api/beavers` now includes `wellbeing`, `maxWellbeing`, and a full `needs[]` array covering every active wellbeing-affecting need. `/api/population` adds `averageHunger/Thirst/Sleep/Wellbeing/maxWellbeing`.

DLL built clean, 54 KB.

## Next action
Probe sensor buildings for the `/api/sensors` endpoint (Feature 5, currently blocked on backend). Look for `AutomatorRegistry`, `AutomationRunner`, signal source interfaces in the DI singleton list. Start with `/api/debug` output — look for "Sensor", "Signal", "Automation" type names.

Player.log confirmed ModdableWeathers weather spec IDs are `"DroughtWeather"` / `"BadtideWeather"`, not `"Drought"` / `"Badtide"`. v5.3.0's backward-compat check was comparing against the wrong strings, so `isDrought` and `isBadtide` were always false when ModdableWeathers was active. Fixed to accept both forms.

Also bumped `manifest.json` from 2.0.0 → 5.3.0 (was never updated from the initial version).

Two crashes visible in the player logs — both from third-party mods, not ours:
- **SluiceIsBack**: removed between sessions (now using "Restore My Sluice Gate" instead)
- **TimberCommons v1.15.1**: crashes on hover over any manufactory (`UnitFormatter.FormatHours` removed from game). Author needs to update.

## Next action
Load a save during Drought with ModdableWeathers active. Call `/api/gamestate` and verify:
- `weatherId: "DroughtWeather"`
- `isDrought: true`
- `moddableWeather: true`
- `weatherDaysRemaining` is a positive number

`WeatherCycleService` from the ModdableWeathers workshop mod (ID 3630523180) is now stored in `GameServices.WeatherCycle` and read via reflection in `GameStateEndpoint`. New fields in `/api/gamestate`:

- `weatherId` — actual stage ID ("Drought", "Badtide", "Monsoon", "Rain", custom...)
- `weatherIsHazardous` — true for hazardous stages
- `weatherDaysInStage` / `weatherDaysSinceStart` / `weatherDaysRemaining`
- `nextWeatherId` — the next stage's ID
- `moddableWeather` — true when the mod is active

All existing fields (`weather`, `isDrought`, `isBadtide`, `isHazardous`) kept and derived from `weatherId` when ModdableWeathers is active. All reflection wrapped in try/catch — graceful fallback to vanilla logic if mod absent.

DLL built clean, 50 KB.

## Next action
Load a save with ModdableWeathers active. Call `/api/gamestate` and confirm:
- `moddableWeather: true`
- `weatherId` matches the current in-game weather stage name
- `weatherDaysRemaining` is a plausible positive number
- `nextWeatherId` is non-empty

The game renamed its weather singleton registrations: `WeatherService` → `ModdableWeatherService`, `HazardousWeatherService` → `ModdableHazardousWeatherService`. The concrete types and their APIs are unchanged (confirmed by probe), only the DI registration names changed. Fix: added both old and new names as alias cases in `Assign()`. Mod now handles both pre-update and post-update Timberborn.

Also confirmed from this log: `WellbeingService` is available with `AverageGlobalWellbeing` (int) — candidate for future `/api/population` wellbeing field.

DLL built clean, 48 KB.

## Next action
Restart the game and load a save. `services.log` / `debug.log` should now show:
- `Weather: OK` and `Hazardous: OK`
- Single Load() run (no duplicate) — v5.1.0 CAS fix confirmed if working
- `/api/gamestate` should return correct `weather`, `isDrought`, `isBadtide` values

Root cause: `Unload()` was resetting `UnloadClaimed=0` at the end of its own body. Since the two Unload() calls are sequential (not concurrent), Unload #2 arrived after Unload #1 had already reset the flag — so Unload #2 also won, also reset `_registered=0`, which let Configure #2 of the new session also register a second GSI instance. Two GSI = two Load() = two EventBus registers = every event fires twice = every rule fires twice per poll = two `<script>` tags.

Fix: `Unload()` no longer resets `UnloadClaimed`. It stays at 1 until the NEXT session's `Load()` resets it at its very start. This breaks the self-reset cycle and ensures exactly ONE Unload, ONE Configure, ONE GSI, ONE Load per session.

DLL built clean, 48 KB.

## Next action
Restart the game, load a save. `debug.log` should show:
- ONE "starting service resolution" line (no duplicate)
- ONE "Unload() — skipped" line, ONE "Unload() — complete" line
- Each beaver added exactly once (no paired duplicates)
- `/automation.js` served once per page load (not twice)

`LeverEndpoint.cs` (new) intercepts `GET /api/levers` and returns the full enriched response:
- `switchOnUrl`, `switchOffUrl` — computed from `Uri.EscapeDataString(name)`
- `colorUrl` — template: `/api/color/<encoded>/{color}` — frontend replaces `{color}` with any
  6-char hex to recolor the in-game lever model. The game's own `/api/color/<n>/<hex>` endpoint handles it.

`HttpApiIntermediary` is internal to `Timberborn.HttpApiSystem` — stored as `object`, accessed
via reflection (`GetLevers()` → ImmutableArray iterated as IEnumerable, properties via GetProperty).
`HttpApiUrlGenerator` is also internal — dropped entirely, URLs built with `Uri.EscapeDataString`.

DLL built clean, 48 KB.

## Next action
Load a save and call `GET /api/levers` — confirm each lever has `switchOnUrl`, `switchOffUrl`,
and `colorUrl` fields. Test a color change by POSTing to a resolved `colorUrl` with a hex like `ff0000`.

- Old dead `HttpApi/index.hbs` (game page template stub, never read per §3.6) deleted
- `HttpApi/index-levers-footer.hbs` renamed to `HttpApi/index.hbs`
- `AutomationJsEndpoint.cs` and `Plugin.cs` path references updated
- `<script>` wrapper left in place per bot_chat.md A2
- DLL built clean, 44 KB

**v4.8.0 — Fix double `<script>` registration**

- `Configurator._registered` changed from `bool` to `int`, guarded with `Interlocked.CompareExchange`
- Prevents both concurrent Bindito containers from registering `AutomationUiSection` and injecting `/automation.js` twice
- `?v=8` bumped to force cache bust
- Confirmed `index.hbs` (formerly the page template) contained no inline SPA — already cleaned up in a prior session

**v4.7.0 — Fix double Load()/Unload()**

- `GameServices.LoadClaimed` / `UnloadClaimed` int flags with `Interlocked.CompareExchange`
- Exactly one container wins each; winning `Unload()` resets both flags
- Each beaver now added/removed exactly once per session

## Next action
**Restart the game** to pick up v4.7.0–v4.9.0 DLLs.

After restart, `debug.log` should show:
- ONE "starting service resolution" line (not two)
- ONE "skipped" line from the losing container
- Each beaver logged exactly once
- `AutomationJs: served ... chars from ...HttpApi\index.hbs` (renamed path)
- ONE Unload + ONE skipped on scene teardown
