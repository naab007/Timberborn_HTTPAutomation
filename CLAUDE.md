# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build

Requires .NET 4.7.2 SDK and a Timberborn installation.

```powershell
cd build
dotnet build HTTPAutomation.csproj -o ../Scripts -c Release
```

If the game is not at the default Steam path (`B:\Steam\steamapps\common\Timberborn\...`), update `<GameManaged>` in `build/HTTPAutomation.csproj`.

The frontend (`HttpApi/index.hbs`) does not require a rebuild — browser refresh picks up changes immediately.

## Architecture

This is a Timberborn mod (C# .NET 4.7.2) that injects a JavaScript SPA into the game's built-in HTTP API server. The backend registers HTTP endpoints; the frontend (`HttpApi/index.hbs`) is a self-contained ~128KB minified JS SPA served at `/automation.js`.

### Backend (C#)

**Entry flow:** `Plugin.cs` (IModStarter) → `Configurator.cs` (Bindito DI, `[Context("Game")]`) → `GameServicesInitializer.cs` (ILoadableSingleton)

**Key problem:** Two Bindito containers run per game session. The entire codebase uses `Interlocked.CompareExchange` atomic claims (in `Configurator.cs` and `GameServices.cs`) to ensure exactly one registration fires.

**`GameServices.cs`** is a static cache holding all resolved game singletons (DayNightCycle, GameCycle, Weather, Population, etc.). It populates via reflection in `GameServicesInitializer.Load()` by iterating `ISingletonRepository` and matching types by name. Optional features (ModdableWeathers, AutomatorRegistry) are stored as `object` and accessed via `MethodInfo`/`PropertyInfo`. Beaver tracking uses EventBus subscriptions with a lock-guarded list. `GameServicesInitializer.Unload()` resets all state between saves.

**Endpoints** (all implement `IHttpApiEndpoint`):
- `GameStateEndpoint.cs` — `/api/gamestate`, `/api/ping`, `/api/debug`
- `PopulationEndpoint.cs` — `/api/population`, `/api/beavers`, `/api/beavers/{id}`, `/api/beavers/{id}/dismiss`
- `SensorEndpoint.cs` — `/api/sensors` (reads AutomatorRegistry via reflection)
- `LeverEndpoint.cs` — `/api/levers`
- `AutomationStorageEndpoint.cs` — `/api/automation?save={name}` (GET/POST, persists to `automation_saves/`)
- `AutomationJsEndpoint.cs` — `/automation.js` (serves `HttpApi/index.hbs`)
- `LogEndpoint.cs` — `/api/log`
- `WelcomeEndpoint.cs` — `/api/welcome`

`AutomationUiSection.cs` injects a `<script src="/automation.js">` tag into the game's HTTP dashboard page.

`HttpResponseHelper.cs` handles CORS headers, cache control, and JSON serialization. `ModLog.cs` is a thread-safe rolling logger (500KB cap) writing to `debug.log`.

### Frontend (`HttpApi/index.hbs`)

Single-file minified JS SPA. Global state lives in the `S` object. Polls every ~3 seconds (`/api/gamestate`, `/api/population`, `/api/sensors`, `/api/beavers`). Detects save changes via `saveName` to load/persist rules via `/api/automation`.

Three automation modes:
- **Simple** — condition → logic → action UI
- **FBD** — visual node graph (22 node types: inputs, AND/OR/timers/counters/latches, lever outputs) with Bézier wire canvas
- **Code** — `eval()` sandbox with `adapters`, `levers`, `sensors`, `gameState`, `population`, `setLever()`, `log()`

## Key Notes

- Sensor `value` field is always `null` — numeric measurement internals are not yet accessible via reflection
- The HTTP API port changes each session — check game settings
- `window.__TAC__` guards against the double-script-injection caused by two Bindito containers
- Rule persistence: `automation_saves/{sanitized-save-name}.json`
- Docs in `/docs/` include `PROJECT.md`, `CHANGELOG.md`, `hooks.md`, and planning notes
