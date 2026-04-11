# Timberborn_HTTPAutomation

> ⚠️ **Very Early Alpha** — Everything is subject to change. APIs, rule formats, file structures, and features may change without notice between updates. Do not build on top of this mod's interfaces in production.

A Timberborn mod that replaces the stock HTTP API dashboard with a full automation frontend — letting you monitor your settlement and build automation logic directly in the browser.

---

## What It Does

The stock Timberborn HTTP API exposes levers, adapters, and game state over a local web server. This mod intercepts that page and replaces it with a custom single-page app that adds:

- **Live dashboard** — game state, weather, population, and resource overview
- **Lever control** — toggle HTTP Levers directly from the browser
- **Sensor monitoring** — view all automation sensor buildings and their signals
- **Automation rules** — three editing modes:
  - **Simple** — condition → logic → action, no code required
  - **FBD (Function Block Diagram)** — visual wiring editor, PLC-style logic blocks
  - **Code** — JavaScript sandbox with access to all live game data
- **Population tab** — live beaver list with need bars and job info
- **Logs tab** — tail of the backend log, plus a frontend activity log

---

## Requirements

- Timberborn (current release branch)
- The [HTTP API](https://www.timberborn.com) feature enabled in game settings
- A browser pointed at the game's HTTP API port (shown in game settings)

---

## Installation

1. Download or clone this repository
2. Copy the `HTTPAutomation` folder into your Timberborn `Mods` directory:
   ```
   Documents/Timberborn/Mods/HTTPAutomation/
   ```
3. The `Scripts/HTTPAutomation.dll` must be present — either use the prebuilt one or build from source (see below)
4. Launch Timberborn, enable the mod, load a save, and open the HTTP API URL in a browser

---

## Building From Source

Requires [.NET 4.7.2 SDK](https://dotnet.microsoft.com/en-us/download) and a Timberborn installation at the default Steam path.

```powershell
cd build
dotnet build HTTPAutomation.csproj -o ../Scripts -c Release
```

The game DLL path is hardcoded to `B:\Steam\steamapps\common\Timberborn\Timberborn_Data\Managed\` — update `<GameManaged>` in `build/HTTPAutomation.csproj` if yours differs.

---

## Project Structure

```
HTTPAutomation/
├── manifest.json          ← Timberborn mod manifest (required at root)
├── src/                   ← C# backend source
├── build/                 ← .csproj and build intermediates
├── Scripts/               ← Compiled DLL (loaded by the game)
├── HttpApi/               ← index.hbs — the JS single-page app
├── automation_saves/      ← Per-save automation rule JSON (runtime)
├── docs/                  ← Internal documentation and planning
├── tools/                 ← Dev utilities (check_syntax.js)
└── logs/                  ← Saved debug log snapshots
```

---

## Automation Modes

### Simple
Pick conditions (adapter state, sensor signal, game state, time of day, population), choose a logic function (trigger on rising edge, enforce continuously, timer, latch), and set lever actions.

### FBD (Function Block Diagram)
Wire nodes together on a canvas. Input nodes read from adapters, levers, sensors, game state, population, or time. Logic nodes implement AND/OR/NAND/NOR/NOT, timers (TON/TOF/TP), a free-running generator, up-counter, and real-time clock. Output nodes control levers with optional spring-return.

### Code
A JavaScript sandbox running every ~3 seconds with access to:
```js
adapters   // [{ name, state }]
levers     // [{ name, state }]
sensors    // [{ id, name, type, unit, isOn, value }]
gameState  // { cycleNumber, dayNumber, timeOfDayHours, isDay, isDrought, ... }
population // { totalBeavers, adults, children, unemployed, homeless, ... }

setLever(name, 'on' | 'off')  // control a lever
log(msg)                       // write to the rule's console panel
```

---

## HTTP API Endpoints Added

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/gamestate` | Cycle, day, time, weather |
| GET | `/api/population` | Population counts and averages |
| GET | `/api/beavers` | Live beaver list with needs |
| GET | `/api/sensors` | All automation sensor buildings |
| GET/POST | `/api/automation?save=N` | Per-save rule persistence |
| GET | `/api/log?lines=N` | Backend log tail |
| POST | `/api/log` | Save frontend log to disk |
| GET | `/api/welcome` | Optional startup message from `welcome.json` |
| GET | `/automation.js` | Serves the frontend SPA |

---

## Known Limitations (Alpha)

- Sensor numeric values (`value` field) are always `null` — the game's internal measurement components are not yet accessible via reflection
- The in-game HTTP API port changes each session — check game settings each time
- Two Bindito containers per session both attempt to register the mod, resulting in duplicate script tags in the page HTML; the `window.__TAC__` guard handles this but the underlying double-registration is a known issue
- No authentication on any endpoint — the API is localhost-only by default, but be aware if you expose Timberborn's port on your network

---

## Contributors

- [naab007](https://github.com/naab007)
- [Claude](https://claude.ai) (Anthropic) — AI pair programmer

---

## License

This project is licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) (Creative Commons Attribution 4.0 International). You are free to share and adapt this work for any purpose, including commercially, as long as you give appropriate credit.

---

## Disclaimer

This is a hobbyist mod in active early development. It directly patches the stock HTTP API page and uses reflection to access game internals. It may break on game updates. Use at your own risk.
