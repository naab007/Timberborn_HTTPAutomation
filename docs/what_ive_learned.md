# HTTPAutomation — What I've Learned
### A complete technical handoff document for any AI continuing this project

---

## 1. Project Identity

| Key | Value |
|-----|-------|
| Mod ID | `HTTPAutomisation` (note: intentional typo in original name, keep it) |
| Assembly name | `HTTPAutomation` |
| Manifest version | `2.0.0` (not updated — can be bumped when publishing) |
| Game | Timberborn 1.0.12.6, Unity 6000.3.6f1, .NET Framework 4.7.2 |
| Location | `C:\Users\Naabin\Documents\Timberborn\Mods\HTTPAutomation\` |
| HTTP port | Varies per session — check game settings or Player.log (`Starting HttpApi at http://localhost:{port}/`) |

---

## 2. What the Mod Does

Replaces Timberborn's stock HTTP API web UI with a custom SPA dashboard. The stock UI shows
raw lever/adapter tables. Ours adds:

- **Dashboard** — game state (cycle, day, time, weather) + population summary + lever/adapter/automation overview
- **Levers** — toggle HTTP Levers on/off/red/green
- **Adapters** — live state of all HTTP Adapters  
- **Population** — aggregate counts (total, adults, children, bots, idle, homeless, contaminated)
- **Automation** — IF [adapter state] THEN [set lever state] rules, persisted in `localStorage`

The DLL adds custom HTTP endpoints and a page section. The web UI is served from `HttpApi/index.hbs` as plain JS via `AutomationJsEndpoint`.

---

## 3. How Timberborn Mods Work (Critical Knowledge)

### 3.1 Mod entry point
- Implement `IModStarter` (in `Timberborn.ModManagerScene.dll`, namespace `Timberborn.ModManagerScene`)
- `IModEnvironment.ModPath` (string property) gives the mod's directory on disk
- `StartMod()` is called at mod-manager scene load time (main menu) — NOT at game scene load
- There is **no** `Binder` or `AddDecorator` on `IModEnvironment`. It only has `ModPath` and `OriginPath`

### 3.2 Bindito DI system (the game's IoC container)
- Timberborn uses **Bindito** (custom IoC, like Guice for .NET)
- `IConfigurator` implementations are discovered by `InstallAll(contextName)` scanning loaded assemblies
- **Must use `[Context("Game")]` attribute** on your `IConfigurator` class — without it, `Configure()` is never called
- `[Context("Game")]` runs in **multiple containers per session** (root + several child containers). This causes:
  - Duplicate `MultiBind<>` registrations → double UI sections → double dashboard render
  - **Fix**: use a `private static bool _registered` guard in `Configure()` — first call sets it, rest return immediately
- Correct registration pattern to avoid CTDs:
  ```csharp
  containerDefinition.Bind<MyClass>().AsSingleton();
  containerDefinition.MultiBind<IMyInterface>().ToExisting<MyClass>();
  ```
  Do NOT inject the class directly via `MultiBind<IMyInterface>().To<MyClass>()` when the class has
  dependencies not available in child containers (see section 3.3).

### 3.3 The child container CTD trap (MOST IMPORTANT)
This caused 4+ CTDs during development. **Read carefully.**

`IHttpApiEndpoint` and `IHttpApiPageSection` are resolved in **child containers**, not the root Game container.
Bindito validates ALL transitive deps when creating a child container. If any dep in the chain is not exported
to child containers, you get:
```
BinditoException: X isn't instantiable due to missing dependency: BeaverCollection.
Dependency chain: ... => IEnumerable<IHttpApiEndpoint> => BeaverCollection.
```

**Types that are NOT available in child containers:**
- `BeaverCollection` — not in DI at all (registered nowhere, built by event listeners)
- `WeatherService` — registered only as `ILoadableSingleton`, not as its concrete type
- `HazardousWeatherService` — same
- `GameCycleService` — same
- `IDayNightCycle` / `DayNightCycle` — same

**The solution: `GameServicesInitializer` pattern**
1. Register `GameServicesInitializer` as `Bind<>().AsSingleton()` (has only `ISingletonRepository` dep — safe everywhere)
2. Register it as `MultiBind<ILoadableSingleton>().ToExisting<GameServicesInitializer>()` so `Load()` is called at save-load
3. `Load()` calls `_repo.GetSingletons<object>()` to get ALL singletons, then assigns them to `static` fields in `GameServices`
4. All endpoints have ZERO constructor deps and read `GameServices.*` statics at request time

### 3.4 ISingletonRepository — how to use it
- `ISingletonRepository.GetSingletons<T>()` is the ONLY method — it's generic
- `GetSingletons<object>()` returns ALL registered singletons (confirmed via `/api/debug` endpoint)
- `GetSingletons<WeatherService>()` returns NOTHING — services are registered by their interface, not concrete class
- Always use `GetSingletons<object>()` with a type-name switch to find what you need
- The list includes ~400 singletons from the game + all mods

### 3.5 IHttpApiPageSection interface
The correct method names (confirmed from compiler errors):
```csharp
int    Order        { get; }   // higher = later. Use 1000 to run after stock sections
string BuildBody();             // returns HTML injected into {{#each bodySections}}
string BuildFooter();           // returns HTML injected into {{#each footerSections}}
```
NOT `GetBodySection()` / `GetFooterSection()` — those are wrong.

### 3.6 How the stock HTTP API works
- `IndexHtmlEndpoint` reads `index.hbs` from **StreamingAssets only** — mod `HttpApi/index.hbs` is IGNORED for page template purposes
- The mod's `HttpApi/index.hbs` is our SPA JavaScript file, served exclusively by `AutomationJsEndpoint` as `/automation.js`
- `{{#each bodySections}}` and `{{#each footerSections}}` in the stock template render `IHttpApiPageSection` content
- `index-levers.hbs` and `index-adapters.hbs` are picked up by the game as body sections and inject `window.TB.levers` and `window.TB.adapters` as `<script>` data blobs
- Our `AutomationUiSection.BuildBody()` hides the stock header/main via CSS
- Our `AutomationUiSection.BuildFooter()` loads `/automation.js` which is served by `AutomationJsEndpoint`

**Naming note:** The SPA JS file was originally named `index-levers-footer.hbs` because it was injected as a Handlebars footer section. It has since been renamed to `index.hbs` (v4.9.0) to match the intended documentation. The old dead page-template `index.hbs` was deleted at the same time. The `<script>` / `</script>` wrapper is kept in the file and stripped at serve time.

### 3.7 IHttpApiEndpoint interface
```csharp
Task<bool> TryHandle(HttpListenerContext ctx)
// Return true = handled (even on error). Return false = not my route, try next endpoint.
```

---

## 4. DLL Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `GET /api/ping` | GET | Health check. Returns `{"ok":true,"mod":"HTTPAutomation","version":"2.9","ready":bool}` |
| `GET /api/debug` | GET | Returns JSON array of all singleton type names (diagnostic tool) |
| `GET /api/gamestate` | GET | Cycle, day, time, weather. 503 if game not loaded. |
| `GET /api/population` | GET | Population counts via `PopulationService.GlobalPopulationData`. |
| `GET /api/beavers` | GET | Returns `[]` — BeaverCollection not in DI (see section 5.1) |
| `GET /automation.js` | GET | Serves `HttpApi/index.hbs` as raw JS (strips `<script>` tags) |

### /api/gamestate response
```json
{
  "cycleNumber": 3, "dayNumber": 199, "dayProgress": 0.85,
  "timeOfDayHours": 19.96, "dayStage": "Nighttime",
  "isDay": false, "isNight": true,
  "weather": "Temperate",   // "Temperate" | "Drought" | "Badtide"
  "isDrought": false, "isBadtide": false, "isHazardous": false
}
```
Weather is "Temperate" when `HazardousWeatherService.CurrentCycleHazardousWeather` is null — this is CORRECT.

### /api/population response
```json
{
  "totalPopulation": 20, "totalBeavers": 18, "adults": 16, "children": 2,
  "bots": 2, "unemployed": 0, "homeless": 0, "injured": 0, "contaminated": 0
}
```
Uses `PopulationService.GlobalPopulationData`:
- `TotalPopulation` = beavers + bots
- `BeaverWorkforceData.Unemployable` = idle beavers
- `BedData.Homeless` = homeless
- `ContaminationData.ContaminatedTotal` = contaminated

---

## 5. Key Technical Discoveries

### 5.1 BeaverCollection is NOT in any DI container
`BeaverCollection` has zero interfaces and is not registered anywhere in Bindito.
It's built up by `CharacterCreatedEvent` / `CharacterKilledEvent` listeners.
`Beaver` extends `BaseComponent` (plain C# object) — not a `MonoBehaviour`, not findable via `FindObjectsOfType`.
**Per-beaver data (individual need bars, dismiss) is currently not implemented** because there's no safe way to enumerate live beavers without `BeaverCollection`.

Possible future approaches:
- Register a custom `ILoadableSingleton` that listens for `CharacterCreatedEvent` and builds its own beaver list
- Check if `Timberborn.AutomationBuildings.SamplingPopulationService` exposes per-entity data

### 5.2 Reflection probing Timberborn DLLs
The `dll_info` MCP tool's PowerShell reflection fails for Timberborn assemblies with:
`"Cannot find an overload for GetCustomAttributes"`.
**Use run_process with a compiled .NET 4.7.2 probe exe instead.** Template:
```csharp
var managed = @"B:\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed";
foreach (var dll in Directory.GetFiles(managed, "*.dll"))
    try { Assembly.LoadFrom(dll); } catch {}
// Now use reflection safely
```

### 5.3 GetSingletons<T>() key = registered type, not concrete type
If a service is bound as:
```csharp
containerDefinition.MultiBind<ILoadableSingleton>().To<WeatherService>().AsSingleton();
```
Then `GetSingletons<WeatherService>()` returns nothing.
`GetSingletons<ILoadableSingleton>()` returns it, but returns it as the interface.
`GetSingletons<object>()` returns everything — use this plus type-name switch matching.

### 5.4 Double-dashboard root cause and fix
`[Context("Game")]` fires for multiple Bindito containers in one session.
`MultiBind<IHttpApiPageSection>().To<AutomationUiSection>()` creates a NEW instance per container.
Two instances → two `BuildFooter()` calls → two `<script src="/automation.js">` tags → two `takeover()` calls.

**Fix**: Static `bool _registered` guard in `Configurator.Configure()` + JS `window.__TAC__` mutex.

### 5.5 Port is not fixed
The HttpApi port changes between sessions. It's shown in Player.log: `Starting HttpApi at http://localhost:PORT/`.
The game settings also show it. The JS uses relative paths (`/api/...`) so the port doesn't matter for the web UI
as long as you navigate to the right port.

### 5.6 `IModEnvironment` has no binder
Do NOT try to call `modEnvironment.Binder.AddDecorator<IConfigurator>()`.
`IModEnvironment` only has `ModPath` and `OriginPath`. IConfigurator is auto-discovered via `[Context("Game")]`.

---

## 6. File Structure

```
HTTPAutomation/
├── manifest.json                 — Id: "HTTPAutomisation", do not change Id
├── HTTPAutomation.csproj         — net472 build, all DLL references listed here
├── Scripts/
│   └── HTTPAutomation.dll        — compiled output, deployed here directly
├── HttpApi/
│   ├── index.hbs   — THE main JS file (served as /automation.js)
│   ├── index-levers.hbs          — injects window.TB.levers as <script> (game picks this up)
│   ├── index-adapters.hbs        — injects window.TB.adapters as <script>
│   └── index.hbs                 — IGNORED by game (IndexHtmlEndpoint reads StreamingAssets only)
├── src/
│   ├── Plugin.cs                 — IModStarter, sets Plugin.ModDirectory
│   ├── Configurator.cs           — [Context("Game")] IConfigurator with static _registered guard
│   ├── GameServices.cs           — static cache + GameServicesInitializer : ILoadableSingleton
│   ├── AutomationUiSection.cs    — IHttpApiPageSection: hides stock UI, loads /automation.js
│   ├── AutomationJsEndpoint.cs   — serves GET /automation.js from HttpApi/index.hbs
│   ├── GameStateEndpoint.cs      — GET /api/gamestate, /api/ping, /api/debug
│   ├── PopulationEndpoint.cs     — GET /api/population, /api/beavers (stubs)
│   └── HttpResponseHelper.cs     — CORS, JSON write helpers
├── services.log                  — written by GameServicesInitializer.Load() on each save load
├── debug.log                     — written by old debug builds, may be stale
├── PROJECT.md
├── CHANGELOG.md
└── what_ive_learned.md           — this file
```

---

## 7. Build & Deploy

```powershell
cd C:\Users\Naabin\Documents\Timberborn\Mods\HTTPAutomation\build
dotnet build HTTPAutomation.csproj -o ../Scripts -c Release
# DLL deploys directly to Scripts/ — no copy needed
```

All DLL references are in `HTTPAutomation.csproj`. Key ones:
- `Timberborn.HttpApiSystem.dll` — `IHttpApiEndpoint`, `IHttpApiPageSection`
- `Timberborn.SingletonSystem.dll` — `ILoadableSingleton`, `ISingletonRepository`
- `Timberborn.Population.dll` — `PopulationService`, `PopulationData`
- `Timberborn.TimeSystem.dll` — `IDayNightCycle`
- `Timberborn.GameCycleSystem.dll` — `GameCycleService`
- `Timberborn.WeatherSystem.dll` — `WeatherService`
- `Timberborn.HazardousWeatherSystem.dll` — `HazardousWeatherService`
- `Timberborn.Beavers.dll` — `BeaverPopulation`, `Child` (note: `Child` is in `Timberborn.Beavers` namespace, not `Timberborn.Characters`)
- `Bindito.Core.dll` — `IConfigurator`, `IContainerDefinition`, `[Context]`
- `Timberborn.ModManagerScene.dll` — `IModStarter`, `IModEnvironment`

---

## 8. JS Architecture (index.hbs)

The file is raw JS wrapped in `<script>...</script>` tags (for legacy Handlebars injection compatibility).
`AutomationJsEndpoint` strips those tags and serves the inner JS as `application/javascript`.

Key patterns:
- `window.__TAC__ = true` guard at boot — prevents double execution if `<script>` injected twice
- `window.TB.levers` and `window.TB.adapters` — injected by the game's own `index-levers.hbs` and `index-adapters.hbs`
- `DLL_AVAILABLE` flag — set to `false` if `/api/gamestate` returns 404, enabling graceful degradation
- All lever/adapter API calls use relative paths — works regardless of port
- `localStorage` key `tac_rules_v2` — stores automation rules between browser sessions
- Polls every 3 seconds; spinner shown during poll

---

## 9. What's Not Working / Future Work

| Feature | Status | Notes |
|---------|--------|-------|
| Per-beaver detail (need bars, dismiss) | ❌ Not implemented | `BeaverCollection` not in DI. Need event-listener approach. |
| Weather badge | ✅ Working | "Temperate" IS correct when no hazardous weather active |
| Idle/Homeless/Contaminated counts | ✅ Working | Via `PopulationService.GlobalPopulationData` |
| Bots in population | ✅ Working | `TotalPopulation` includes bots |
| FBD (Function Block Diagram) automation editor | ❌ Not started | User requested this — complex JS feature |
| Automation rules persist across game sessions | ✅ Working | `localStorage` |
| Lever On/Off/Red/Green control | ✅ Working | Via stock `/api/levers/*` endpoints |

### FBD Automation Editor (requested feature)
User wants a visual FBD-style programming interface for automation rules — more expressive than
the current "IF adapter ON THEN set lever ON" flat rules. This is a pure JS addition to
`index.hbs` (no DLL changes needed). It would replace/extend the current Automation tab.

---

## 10. Game Service API Reference (confirmed working)

### IDayNightCycle (from Timberborn.TimeSystem)
```csharp
int   DayNumber          // current day total
float DayProgress        // 0.0–1.0, fraction of day elapsed
float HoursPassedToday   // hours since midnight
bool  IsDaytime          // true during daytime
bool  IsNighttime        // true at night
```

### GameCycleService (from Timberborn.GameCycleSystem)
```csharp
int Cycle     // current cycle number (seasons completed)
int CycleDay  // day within current cycle
```

### WeatherService (from Timberborn.WeatherSystem)
```csharp
bool IsHazardousWeather   // true during drought or badtide
```

### HazardousWeatherService (from Timberborn.HazardousWeatherSystem)
```csharp
IHazardousWeather CurrentCycleHazardousWeather  // null = temperate; .Id = "Drought" or "Badtide"
```

### PopulationService (from Timberborn.Population)
```csharp
PopulationData GlobalPopulationData
// PopulationData has:
//   int TotalPopulation, NumberOfBeavers, NumberOfAdults, NumberOfChildren, NumberOfBots
//   WorkforceData BeaverWorkforceData  → .Unemployable = idle beavers
//   BedData BedData                   → .Homeless = homeless beavers
//   ContaminationData ContaminationData → .ContaminatedTotal
```

### BeaverPopulation (from Timberborn.Beavers) — fallback only
```csharp
int NumberOfBeavers, NumberOfAdults, NumberOfChildren  // no bots, no homeless/idle data
```

---

## 11. Probing Unknown APIs

When you need to discover what's available in a Timberborn DLL, use this probe pattern
(compile and run as a .NET 4.7.2 exe from the `Managed` directory):

```csharp
var managed = @"B:\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed";
foreach (var dll in Directory.GetFiles(managed, "*.dll"))
    try { Assembly.LoadFrom(dll); } catch {}
// Pre-loading all siblings is REQUIRED before reflecting game types

var a = Assembly.LoadFrom(managed + @"\Timberborn.SomeDll.dll");
var t = a.GetTypes().Where(x => x.Name == "SomeType").First();
// Now reflect on t — GetProperties, GetMethods, GetConstructors, GetInterfaces
```

Or use `/api/debug` in the browser while the game is running — it returns all ~400 singleton
type names and you can see exactly what's available in `ISingletonRepository`.

---

## 12. MCP Server Tools (modding-tools)

Available for direct use in this project:
- `patch_file` — surgical string replace (preferred for edits)
- `read_lines` / `replace_lines` / `insert_lines` / `delete_lines` — line-based editing
- `append_file` — fast append to CHANGELOG etc.
- `dotnet_compile` — build directly to `Scripts/` with `output_dir`
- `run_process` — run probe exes, PowerShell commands
- `write_file` — full file overwrite when needed

Game data paths:
- Game DLLs: `B:\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed\`
- Mod source: `C:\Users\Naabin\Documents\Timberborn\Mods\HTTPAutomation\`
- Temp workspace: `B:\-AI-Stuff-\Modding_MCP\AI-TEMP\`
- Player.log: `C:\Users\Naabin\AppData\LocalLow\Mechanistry\Timberborn\Player.log`
- Error reports: `C:\Users\Naabin\Documents\Timberborn\Error reports\*.zip`

---

## 13. The Silent Button/Tab Failure Pattern

**Symptom:** Clicking any tab or button silently fails and the UI snaps back to the starting tab (Dashboard).

**Root cause:** Inline `onclick="TAC.something()"` handlers are registered in HTML strings
built by `render()`. If `window.TAC` is undefined at click time — because the IIFE threw
a JS error during boot before the `window.TAC = {...}` assignment — the browser swallows
`ReferenceError: TAC is not defined` silently (inline handlers don't surface to `console.error`
in the in-game Chromium unless DevTools is open).

**How to diagnose:**
1. Open the game's web UI in a real browser (Chrome/Edge at `http://localhost:<port>/`)
2. Open DevTools → Console
3. Any red errors on page load = the IIFE failed partway through
4. If `window.TAC` is `undefined` in the console → confirm the error came before the TAC assignment

**Common causes of IIFE partial failure:**
- A patch left mismatched braces `{` / `}` or parentheses
- An `old_str` patch target appeared in the wrong place and duplicated/corrupted logic
- A regex special character in an `old_str` or `new_str` that the patch tool misinterpreted

**Prevention:**
- After every significant patch, verify the file has balanced braces:
  `grep -c '{' file` vs `grep -c '}' file` (should be equal)
- The `window.TAC` block is near the END of the IIFE — any error anywhere before it = silent UI death
- The `window.__TAC__ = true` guard at the very bottom is past TAC — if that's missing, the IIFE ran fine

---

## 14. Browser Caching in the In-Game Chromium

**Symptom:** JS changes have no effect even after DLL rebuild; the old broken version keeps loading.

**Root cause:** The in-game Chromium caches `/automation.js?v=N` aggressively with no expiry.
The browser stores the response keyed on the full URL including query string. If `?v=2` was
cached when the JS was broken, it will keep serving the broken version forever even after fixes.

**Fix already applied (v3.6.1):**
- `AutomationJsEndpoint.cs` now sends `Cache-Control: no-store` on `/automation.js`
- `AutomationUiSection.cs` bumped `?v=4` to force an immediate one-time cache bust

**Rule going forward:**
- ANY endpoint served to the browser should have `Cache-Control: no-store`
- Add it to `HttpResponseHelper.AddCorsHeaders()` so it applies to everything automatically
- Never rely on `?v=N` as the sole cache-busting strategy — it only works once

---

## 15. Poll Loop Architecture — Why Selective Re-Render Matters

**Old broken pattern:**
```js
// In poll() .then() handler:
render(); // ALWAYS — every 3 seconds, full DOM rebuild
```
This destroyed any open editor, form, tab, or scroll position every 3 seconds.
Clicking a button would fire, open a form, then 3 seconds later `render()` would wipe it.

**Fixed pattern (v3.6.0):**
```js
if (S.tab === 'automation') {
    if (S.automationView === 'edit') {
        // only update code log or FBD canvas in-place
    }
    // list view: no re-render from poll at all
} else if (S.tab === 'logs') {
    // uiLog() already patches #tac-log-panel directly; no full render needed
} else {
    render(); // dashboard / levers / adapters / population — these need live data
}
```

**Rule:** Only call `render()` from poll for tabs that display live game data that changes
every few seconds (levers state, adapter state, population). For tabs that the user actively
edits (automation editor), or that self-patch via DOM id (logs), never call `render()` from poll.

---

## 16. MCP Agent Crash Pattern

When the MCP agent (AI) crashes mid-session, the file being edited may be left in a partially
patched state. Before resuming, always:
1. `read_file` the file that was being edited
2. Check for obviously doubled or corrupted code blocks
3. If the file is corrupted, restore from the last known-good version or rewrite the affected section

Files most at risk (large, frequently patched):
- `HttpApi/index.hbs` — ~85 KB, single file, any corruption = silent JS failure
- `src/GameServices.cs` — DI wiring, corruption = CTD on game load

---

## 17. IUnloadableSingleton — Resetting State Between Game Sessions

**Problem:** Static fields in `GameServices` persist for the entire application lifetime. When the player loads a second save without quitting, Timberborn destroys all game containers and creates new ones, but `_registered == true` in `Configurator`, so `Configure()` returns immediately. `GameServicesInitializer` is never registered, `Load()` is never called, and the new session runs on stale service references from the old scene.

**Fix:** Implement `IUnloadableSingleton` alongside `ILoadableSingleton`.

```csharp
// In Configurator.Configure():
containerDefinition.MultiBind<IUnloadableSingleton>().ToExisting<GameServicesInitializer>();

// In GameServicesInitializer:
public void Unload()
{
    ResetState();                       // unregister EventBus, clear beavers, null all refs
    Configurator._registered = false;   // allow next session's Configure() to run normally
}
```

`Unload()` is called by Timberborn when the game scene tears down — before the new scene's
`Configure()` is called. This guarantees a clean slate for the next session.

**`Configurator._registered` must be `internal static`**, not `private`, so `GameServicesInitializer.Unload()` can reset it.

**Key sequence on save reload:**
1. Game scene A tears down → `IUnloadableSingleton.Unload()` called on session A's instance
2. `ResetState()` clears beavers, nulls services, sets `Ready = false`
3. `Configurator._registered = false`
4. New game scene B's containers initialise → `Configure()` runs (because `_registered == false`)
5. New `GameServicesInitializer` instance registered → `Load()` called → fresh service resolution

---

## 18. GameLoader Is NOT a Singleton — Use SettlementReferenceService Instead

**Problem:** `GameLoader` does not appear in `ISingletonRepository.GetSingletons<object>()`. It is constructed internally during load and not retained as a registered singleton. Any code expecting it via the singleton repo will find nothing.

**What we wanted:** The currently loaded save/settlement name, to use as the key for per-save automation rule storage.

**Working alternative:** `SettlementReferenceService` (from `Timberborn.SettlementNameSystem.dll`) IS in the singleton list. It exposes:
```csharp
SettlementReference SettlementReference  // { SettlementName, SaveDirectory }
```
`SettlementName` is the human-readable settlement name (e.g. "Beaver Valley"). This is the correct key to use for automation rule files — it's per-settlement, which matches the user's mental model better than a save-file timestamp anyway.

**DLL reference needed:** `Timberborn.SettlementNameSystem.dll`

**In GameServices.cs:**
```csharp
public static SettlementReferenceService SettlementRef;
public static string SettlementName =>
    SettlementRef?.SettlementReference?.SettlementName ?? "";
```

Note: `SettlementName` may be empty during `Load()` if the service hasn't populated yet — it is set later by the game. At `/api/gamestate` request time it is always populated. Log a warning if empty at `Load()` time.

---

## 19. volatile Is Required for Static Ready Flag

**Problem:** `GameServices.Ready` was `public static bool`. Two Bindito containers initialise concurrently on separate threads. Thread 2 reads a stale cached `false` before thread 1's `Ready = true` write becomes visible. Both threads run the full `Load()` body, register with EventBus twice, and every beaver gets added twice.

**Fix:**
```csharp
public static volatile bool Ready;
```
The `volatile` keyword enforces read/write ordering guarantees across threads — a write on one thread is immediately visible to reads on other threads with no caching.

**Symptom in debug.log:** Two complete "starting service resolution" blocks milliseconds apart, and every beaver logged twice ("total: 1" then "total: 1" again for the same beaver).

---

## 20. EventBus — The [OnEvent] Pattern for Live Game Data

`BeaverCollection` is not in DI at all. The correct way to maintain a live list of game entities is Timberborn's own event bus pattern, confirmed by inspecting how the game's own `BeaverPopulation` class works.

**Pattern:**
1. In `Load()`, get `EventBus` from `GetSingletons<object>()` and call `EventBus.Register(this)`
2. Add `[OnEvent]` methods on the same class — Timberborn's `EventBus` discovers them by reflection
3. `[OnEvent]` attribute is `Timberborn.SingletonSystem.OnEventAttribute`

```csharp
[OnEvent]
public void OnCharacterCreated(CharacterCreatedEvent e)
{
    var beaver = e.Character.GetComponent<Beaver>();
    if (beaver == null) return;
    lock (GameServices.BeaversLock)
        GameServices.Beavers.Add(beaver);
}
```

**Thread safety:** EventBus fires events on the Unity main thread. HTTP handlers run on .NET thread-pool threads. Always lock when reading or writing the shared list.

**Save load timing:** `ILoadableSingleton.Load()` is called BEFORE entity deserialisation. `Register(this)` in `Load()` is therefore in place before the game fires `CharacterCreatedEvent` for each saved beaver. This means the live list is populated correctly on every save load — confirmed by the game's own `BeaverPopulation` using the exact same timing.

**Unregister in Unload:** Always call `EventBus.Unregister(oldInstance)` in `ResetState()` before nulling the EventBus reference. Wrap in try/catch — by the time `Unload()` is called the EventBus object may already be in a partially-torn-down state.

---

## 21. ModLog — Thread-Safe Logging Utility

All subsystems write to `debug.log` in the mod directory via `ModLog` (in `src/ModLog.cs`).

**API:**
```csharp
ModLog.Info("message");
ModLog.Warn("message");
ModLog.Error("message");
ModLog.Error("message", exception);  // appends type + message + full stack trace
```

**Design:**
- Single lock around all file writes — safe for concurrent Unity main thread + .NET thread-pool access
- Timestamps to millisecond: `HH:mm:ss.fff [LEVEL] message`
- Rolling at 500 KB: when the file exceeds the cap, the oldest half is discarded (split at a newline boundary) and a `--- log rolled ---` marker is written. Recent entries are always preserved.
- All failure paths are wrapped in try/catch — logging itself never throws into caller code.
- `File.AppendAllBytes` is .NET 6+ only. Use `FileStream(path, FileMode.Append)` for .NET 4.7.2 compatibility.

**Log file location:** `Path.Combine(Plugin.ModDirectory, "debug.log")`

---

## 22. Cache-Control on All Endpoints Is Non-Negotiable

The in-game Chromium browser caches HTTP responses aggressively. If an endpoint response is cached without `Cache-Control: no-store`, stale data (including broken JS) will be served indefinitely — even after the DLL is rebuilt and the file on disk is fixed.

**Fix:** `HttpResponseHelper.AddCorsHeaders()` is called by every endpoint before writing a response. Adding `Cache-Control: no-store, no-cache, must-revalidate` and `Pragma: no-cache` there means every endpoint gets it automatically without any per-endpoint changes.

```csharp
public static void AddCorsHeaders(HttpListenerResponse r)
{
    r.Headers.Set("Access-Control-Allow-Origin",  "*");
    r.Headers.Set("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
    r.Headers.Set("Access-Control-Allow-Headers", "Content-Type");
    r.Headers.Set("Cache-Control", "no-store, no-cache, must-revalidate");
    r.Headers.Set("Pragma", "no-cache");
}
```

`?v=N` query string versioning on `/automation.js` is NOT a reliable cache-busting strategy — it only works for the first load after the version changes. `no-store` is the only correct fix.

---

## 23. Updated File Structure (v4.5)

```
HTTPAutomation/
├── manifest.json
├── HTTPAutomation.csproj         — added Timberborn.SettlementNameSystem.dll ref
├── Scripts/
│   └── HTTPAutomation.dll        — compiled output
├── HttpApi/
│   ├── index.hbs   — main JS SPA (~85 KB)
│   ├── index-levers.hbs
│   ├── index-adapters.hbs
│   └── index.hbs                 — IGNORED by game
├── src/
│   ├── Plugin.cs
│   ├── Configurator.cs           — _registered is now internal static (reset by Unload)
│   ├── ModLog.cs                 — NEW: thread-safe logger → debug.log (500 KB rolling)
│   ├── GameServices.cs           — ILoadableSingleton + IUnloadableSingleton; volatile Ready;
│   │                               SettlementReferenceService replaces GameLoader
│   ├── AutomationUiSection.cs
│   ├── AutomationJsEndpoint.cs
│   ├── GameStateEndpoint.cs      — saveName from SettlementName; /api/ping has beaverCount
│   ├── PopulationEndpoint.cs     — /api/beavers now live (GameServices.Beavers)
│   ├── AutomationStorageEndpoint.cs — GET+POST /api/automation?save=<n>
│   ├── LogEndpoint.cs            — GET /api/log?lines=N (serves debug.log tail)
│   └── HttpResponseHelper.cs     — Cache-Control: no-store added to AddCorsHeaders
├── automation_saves/             — per-settlement rule JSON files
├── debug.log                     — rolling log, all backend events
├── services.log                  — last save load summary
├── plans_backend.md
├── plans_frontend.md
├── frontend_needs.md
├── hooks.md
├── PROJECT.md
├── CHANGELOG.md
└── what_ive_learned.md
```

---

## 24. Updated Endpoint Table (v4.5)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/ping` | GET | `{ok, mod, version, ready, saveName, beaverCount, eventBus}` |
| `/api/debug` | GET | JSON array of all singleton type names |
| `/api/gamestate` | GET | Cycle, day, time, weather, saveName |
| `/api/population` | GET | Aggregate counts via `PopulationService.GlobalPopulationData` |
| `/api/beavers` | GET | Live beaver list from `GameServices.Beavers` (EventBus-maintained) |
| `/api/beavers/<id>` | GET | Single beaver by runtime hash ID |
| `/api/beavers/<id>/dismiss` | POST | Unassign beaver from workplace via reflection |
| `/api/automation?save=<n>` | GET | Load rule JSON for settlement name |
| `/api/automation?save=<n>` | POST | Save rule JSON for settlement name |
| `/api/log?lines=N` | GET | Last N lines of debug.log (default 200) |
| `/automation.js` | GET | Serves `HttpApi/index.hbs` as raw JS |

---

## 25. Interlocked.CompareExchange — The Correct Guard for Concurrent Load/Unload

**Problem:** `volatile bool Ready` does not prevent two threads from both reading `false` before either writes `true`. Two Bindito containers call `ILoadableSingleton.Load()` per session — if they start within a millisecond of each other both will see `Ready == false`, both run the full body, both call `EventBus.Register(this)`, and every game event is received twice. Every beaver gets added twice.

Same problem with `IUnloadableSingleton.Unload()` — both containers fire it on scene teardown.

**Fix: `Interlocked.CompareExchange` atomic claim flags**

```csharp
// In GameServices (static):
internal static int LoadClaimed   = 0;   // 0 = available, 1 = claimed
internal static int UnloadClaimed = 0;

// In Load():
if (Interlocked.CompareExchange(ref GameServices.LoadClaimed, 1, 0) != 0)
{
    ModLog.Info("Load() — skipped (claimed by concurrent container)");
    return;
}
// Winner runs the full body...

// In Unload() — winning instance resets flags at end:
if (Interlocked.CompareExchange(ref GameServices.UnloadClaimed, 1, 0) != 0)
{
    ModLog.Info("Unload() — skipped (claimed by concurrent container)");
    return;
}
// ... reset state ...
Interlocked.Exchange(ref GameServices.LoadClaimed,   0);
Interlocked.Exchange(ref GameServices.UnloadClaimed, 0);
```

`CompareExchange` is a single atomic hardware instruction (CMPXCHG on x86). No thread can observe a state between the read and the write. Exactly one thread gets `0` back (the "comparand"), all others get `1`.

**Why not lock?** A lock would work too, but `Interlocked` is cheaper and the semantics are exactly "only one gets through" — which is what we want.

**Why not `volatile`?** `volatile` guarantees memory ordering for individual reads and writes, but it does NOT make a read-modify-write operation atomic. Two threads can still both read `0` before either writes `1`.

**Flag reset:** The winning `Unload()` must reset both flags to `0` at the end, so the next session's `Load()` can claim again. If this is omitted, the second session will always skip.

---

## 17. The Dual-SPA Collision (Critical — Root Cause of All Button Failures)

**Discovered:** 2026-03-22 by fetching the raw page HTML from `http://localhost:8080/`

When a backend agent writes a new frontend, it may inject a **full inline SPA** directly
into the page as a `<script>` block inside the game's `index.hbs` template rendering pipeline.
Our `/automation.js` is then loaded on top of it. Both execute. Both fight over the same DOM.

### What the page actually contained

```
1. <nav class="navbar fixed-top">  ← game's nav with .tab-btn buttons (SPA 1 owns these)
2. window.TB.levers + window.TB.adapters injected as <script> tags
3. Our BuildBody() CSS hide blocks — injected TWICE (double-registration)
4. <main class="container-fluid"> with #tab-dashboard, #tab-levers etc. (SPA 1 renders here)
5. FULL inline SPA ("TimberAutoControl") — its own S, render(), poll(), window.TAC
6. <script src="/automation.js?v=6">  ← our JS
7. <script src="/automation.js?v=6">  ← our JS again (double-registration)
```

### Why buttons fail: the TypeError crash loop

Our `takeover()` calls `el.remove()` on `main.container-fluid`. That element is gone from
the DOM. SPA 1's `setInterval(poll, 3000)` keeps running and calls its `renderDashboard()`,
which does `document.getElementById('tab-dashboard').innerHTML = ...`. That element no longer
exists → **TypeError on every poll cycle**. In the in-game Chromium/CEF browser, unhandled
Promise-chain TypeErrors can trigger a page reload, which is why every button click appeared
to "kick the user back to Dashboard" — the page was literally reloading.

**Fix:** Never `remove()` the game's container elements. Use `style.display='none'` instead.
Hidden elements still exist in the DOM, so other scripts can safely write to them.

### The window.TAC collision

Both SPAs assign `window.TAC`. Ours runs second and wins. Our button `onclick="TAC.on(...)"` 
calls our handler correctly. The collision is not the direct cause of failure, but SPA 1's 
`runAutomation()` still runs on its own lever state copy, potentially undoing lever changes.

### The double-registration symptom

`/automation.js?v=6` appears twice in the page. `BuildBody()` CSS block appears twice.
This means `Configurator.cs`'s `static bool _registered` guard failed — two
`IHttpApiPageSection` instances are active. The `window.__TAC__` guard in our JS prevents
double-`takeover()`, but the double `<script>` tag is a reliable indicator the DI guard
is broken again. Always check for this when the game reloads the mod.

### How to diagnose this in future

Fetch the raw page and look for signs of a competing SPA:
```powershell
Invoke-WebRequest -Uri 'http://localhost:8080/' -UseBasicParsing | Select-Object -ExpandProperty Content
```
Red flags:
- Multiple `<script src="/automation.js">` tags
- An inline `<script>` block with its own `function render()` or `window.TAC =`
- `<script src="/automation.js">` appearing AFTER a large inline script block
- Our CSS `BuildBody()` block appearing more than once

### Rule for backend agents

The backend agent must **not** write any frontend HTML or JavaScript into the page.
The only correct outputs from the backend are:
1. C# endpoint classes (`IHttpApiEndpoint`) that serve JSON at `/api/*`
2. `IHttpApiPageSection.BuildBody()` — CSS only, to hide the stock UI elements
3. `IHttpApiPageSection.BuildFooter()` — a single `<script src="/automation.js?v=N">` tag

All frontend logic lives exclusively in `HttpApi/index.hbs`.

---

## 18. The Double-Load TAC Overwrite Bug

**Symptom:** All buttons silently "kick back to dashboard" with no errors in any log.

**Cause:** `/automation.js` loading twice overwrites `window.TAC` with a stale object.

When the script loads twice and the `__TAC__` guard is positioned AFTER `window.TAC = {...}`:

```
Copy 1: var S={tab:'dashboard'} → ... → window.TAC={on,off,...} → guard=false → __TAC__=true → takeover() → S.tab='levers'
Copy 2: var S={tab:'dashboard'} → ... → window.TAC={on,off,...}  ← OVERWRITES with new S
                                  → guard=true → return
```

After boot, `window.TAC` points to Copy 2's `S` where `S.tab` is permanently `'dashboard'`
(takeover() never ran for Copy 2). Any `render()` call via that TAC renders the dashboard.
No errors because the code executes correctly — just against the wrong state object.

**Fix:** Move `if(window.__TAC__)return; window.__TAC__=true;` to BEFORE `window.TAC={...}`.
Copy 2 then exits before it can overwrite TAC.

**Rule:** The `__TAC__` guard must be the VERY FIRST executable statement in the IIFE that
has any global side effect. Specifically it must precede: `window.TAC`, `window.onerror`,
any `document.addEventListener`, and `takeover()`. Only pure declarations (`var`, `function`)
are safe to run before the guard since they're scoped to the IIFE closure anyway.

## v5.0.0 — GET /api/levers colorUrl template, what was learned about internal types

### HttpApiIntermediary and HttpApiUrlGenerator are internal to Timberborn.HttpApiSystem
Both types are `internal` — they exist in the DLL and show in reflection probes, but cannot be
referenced by type name from any other assembly. Attempting to use them as field types or to call
`new HttpApiUrlGenerator()` produces `CS0122: inaccessible due to its protection level`.

**Pattern used:**
- Store the singleton as `object` in `GameServices.Intermediary`
- Assign in the `"HttpApiIntermediary"` switch case with no cast: `GameServices.Intermediary = s`
- Access via reflection at call time using `GetMethod("GetLevers")` and `Invoke`
- Iterate the `ImmutableArray<HttpLeverSnapshot>` result via `IEnumerable` (boxing works fine)
- Read each snapshot's properties via `GetProperty("Name").GetValue(snap)` etc.
- Return data as a plain public struct (`LeverInfo`) so callers stay type-safe

**URL construction without HttpApiUrlGenerator:**
The game's lever URL format is clearly documented in `HttpApi/index-levers.hbs`:
```
/api/switch-on/<urlEncodedName>
/api/switch-off/<urlEncodedName>
/api/color/<urlEncodedName>/<hexColor>
```
`Uri.EscapeDataString(name)` produces the same encoding the game uses internally (space → `%20` etc.).
`HttpApiUrlGenerator` is therefore not needed — the format can be inferred from the HBS template
and constructed manually without touching any internal type.

### colorUrl template design
Instead of two fixed color URLs (`redUrl` = `ff0000`, `greenUrl` = `00ff00`), expose a single
`colorUrl` with `{color}` placeholder:
```
/api/color/HTTP%20Lever%201/{color}
```
Frontend resolves it as: `lever.colorUrl.replace('{color}', hexString)` then POSTs to that URL.
The game's own color endpoint handles execution — any 6-char hex is accepted, driving real
in-game model recoloring (not just the red/green indicator states the HBS template targeted).

---

## 19. Color API Contract

**`colorUrl` template format:** `/api/color/<encoded-lever-name>/{color}`

The `{color}` placeholder is **stable and will not change** — confirmed by backend agent.
The frontend uses `.replace('{color}', hex)` where `hex` is a 6-char lowercase string
with no `#` prefix (e.g. `'3a7bff'`).

```js
// Correct usage
post(lever.colorUrl.replace('{color}', 'ff0000'))  // red
post(lever.colorUrl.replace('{color}', '00ff00'))  // green
post(lever.colorUrl.replace('{color}', '3a7bff'))  // custom blue
```

**Why `{color}` was chosen:** it cannot appear in a valid hex string or URL path segment,
is trivial to `.replace()` in JS, and is visually obvious in JSON responses.

**The lever name segment CAN change** if the lever is renamed in-game, but this is handled
automatically because the frontend re-fetches `/api/levers` on every poll cycle and always
reads `colorUrl` fresh from the response.

**Automation action schema for color:**
```json
{ "leverName": "HTTP Lever 1", "leverState": "color", "leverColor": "3a7bff" }
```
`leverColor` is 6-char lowercase hex, no `#`. The engine calls
`lever.colorUrl.replace('{color}', action.leverColor)`. No backend changes needed —
it maps directly onto the existing `POST /api/color/<name>/<hex>` stock endpoint.

---

## 26. The CAS Self-Reset Bug — Why Interlocked Alone Isn't Enough

When two Bindito containers call `Unload()` sequentially (not concurrently), a CAS-based guard can still allow both to run if the winner resets its own flag.

**The pattern that fails:**
```csharp
public void Unload()
{
    if (Interlocked.CompareExchange(ref UnloadClaimed, 1, 0) != 0) return; // wins
    // ... do work ...
    Interlocked.Exchange(ref UnloadClaimed, 0); // ← SELF-RESET: allows second call to win
}
```

Unload #1 wins, sets `UnloadClaimed=1`, runs, resets to 0. Unload #2 arrives, sees 0, wins again.

**The correct cross-session reset pattern:**

Each guard flag is reset by the OTHER lifecycle event, not by its own winner:

```csharp
// Load() resets UnloadClaimed (enabling THIS session's Unload to run):
public void Load()
{
    Interlocked.Exchange(ref UnloadClaimed, 0); // reset at start, before own CAS
    if (Interlocked.CompareExchange(ref LoadClaimed, 1, 0) != 0) return;
    // ... run ...
    // LoadClaimed stays at 1 — Unload() will reset it
}

// Unload() resets LoadClaimed (enabling NEXT session's Load to run):
public void Unload()
{
    if (Interlocked.CompareExchange(ref UnloadClaimed, 1, 0) != 0) return;
    // ... run ...
    Interlocked.Exchange(ref LoadClaimed, 0);
    // UnloadClaimed stays at 1 — next Load() will reset it
    // _registered also reset here to allow next session's Configure() to run
}
```

**Full correct state trace across sessions:**
```
Session N running:  LoadClaimed=1, UnloadClaimed=0, _registered=1
Unload #1 wins:     LoadClaimed=0, UnloadClaimed=1, _registered=0
Unload #2:          skipped (UnloadClaimed=1) ✓
Configure #1 wins:  LoadClaimed=0, UnloadClaimed=1, _registered=1
Configure #2:       skipped (_registered=1) ✓
Load #1:            resets UnloadClaimed=0, LoadClaimed=1, _registered=1
```

**Why this matters:** If Unload resets its own flag AND `_registered`, the second Unload fires during the new session's Configure sequence, resetting `_registered=0` a second time. Configure #2 then also wins, creating a second GSI instance, a second EventBus registration, double event firings, and double `<script>` tags.

---

## 26. CAS Guard — The Reset-Before-Check Failure Mode

**Confirmed from debug.log:** Both `Load()` calls still ran to completion after v5.1.0.

The bug: placing `Interlocked.Exchange(ref UnloadClaimed, 0)` BEFORE the `LoadClaimed` CAS
means BOTH Load() callers reset `UnloadClaimed` before either checks `LoadClaimed`. Timeline:

```
Load #1: UnloadClaimed = 0  ← resets it
Load #2: UnloadClaimed = 0  ← also resets it (no-op, but dangerous timing below)
Load #1: CAS(LoadClaimed, 0→1) — wins
Load #2: CAS(LoadClaimed, 1→skip) — skips ✓

...session runs...

Unload #1: CAS(UnloadClaimed, 0→1) — wins  ✓
Unload #1: resets LoadClaimed=0, leaves UnloadClaimed=1

BUT: both Load() callers already reset UnloadClaimed=0 at the start.
So if Unload #2 from the PREVIOUS session arrives AFTER Load #1 resets UnloadClaimed:
  Previous Unload #2: CAS(UnloadClaimed, 0→1) — wins (should have been blocked!)
  → Resets _registered=0 again mid-transition
  → Configure #2 now wins, registers second GSI
  → Two Load() calls next session
```

**Correct fix:** Only the CAS winner should reset `UnloadClaimed`:
```csharp
// WRONG — both callers reset before CAS:
Interlocked.Exchange(ref UnloadClaimed, 0);
if (CAS(LoadClaimed, 0→1) != 0) return;

// CORRECT — only winner resets:
if (CAS(LoadClaimed, 0→1) != 0) { Log("skipped"); return; }
Interlocked.Exchange(ref UnloadClaimed, 0);  // winner only
```

**Rule:** In a cross-session flag protocol where Load() must reset the Unload flag,
the reset must happen INSIDE the winner's exclusive path, never before the guard.

---

## 27. patch_file Danger — Always Include the Anchor String in new_str

When using `patch_file` to INSERT code before an existing line, the `old_str` is the
existing line that marks the insertion point. The `new_str` must INCLUDE that original
line at the end — otherwise the original line gets deleted.

**Pattern that causes data loss:**
```python
patch_file(
    old_str="    _removeCondition:function(id,idx){",
    new_str="    _addResetCond:function(...){...},\n    _removeCondition:function..."
    # ↑ forgot to include _removeCondition here → method header deleted
)
```

**Safe pattern:**
```python
patch_file(
    old_str="    _removeCondition:function(id,idx){",
    new_str="    _addResetCond:function(...){...},\n    _removeCondition:function(id,idx){"
    # ↑ original line preserved at the end of new_str
)
```

**Rule:** When patching to INSERT before something, always end your `new_str` with the
exact text from `old_str`. When patching to REPLACE something, double-check that the
full context (opening + body + closing) of the thing you're replacing is in `new_str`.

## 28. Always Run check_syntax.js After JS Patches

A JScript-based syntax checker lives at:
`C:\Users\Naabin\Documents\Timberborn\Mods\HTTPAutomation\check_syntax.js`

Run it with:
```
cscript //nologo //e:jscript check_syntax.js
```

This catches object literal corruption, missing method headers, mismatched braces, and
other syntax errors BEFORE a build — before the user has to report a black screen.

The checker tests the IIFE body (skipping the `window.onerror` block at the top which
uses `line` as a parameter name, which confuses JScript's ES3 parser).

The two black-screen bugs in v5.3.0 (missing `if(rule.mode==='code'){` and missing
`_removeCondition:function(id,idx){`) would both have been caught by this checker
immediately after patching, before any build.

---

## 29. CEF Browser Disk Cache Survives Game Restarts

The Timberborn in-game Chromium/CEF browser has a persistent disk cache that is NOT cleared
when the game is restarted. This means:

- A script URL like `/automation.js?v=19` that was cached before `Cache-Control: no-store`
  was added will continue to be served from cache in future sessions
- The stock `IndexHtmlEndpoint` (which serves the page HTML) does NOT have our
  `Cache-Control: no-store` headers, so the page HTML itself can be cached with old script
  references in it
- Even a full game restart does not clear this cache

**Rule:** Never use a static `?v=N` cache-buster for scripts served by this mod. Use a
Unix timestamp instead:
```csharp
var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
return $"<script src=\"/automation.js?t={ts}\"></script>";
```
This guarantees the URL is unique every session. The browser has never seen it before,
so it always fetches fresh. Old cached entries (v=19, v=20, etc.) remain in the cache
but are never requested again.
