# Backend Plan — HTTPAutomation

## What this plan covers

The frontend (Step 10) wants save-game-aware rule storage.
That means automation rules should be linked to the specific save file that's loaded,
not stored globally in localStorage. This needs backend support.

Everything below requires a DLL recompile and deploy when implemented.

---

## Step A — Find the save name API in Timberborn's DLLs

Before writing any code, we need to know which game service exposes the
currently loaded save file name.

The approach:
- Use the probe pattern (see `what_ive_learned.md` §11) to scan Timberborn's
  managed DLLs for likely class names: `SaveLoadService`, `SaveManager`,
  `GameSaveService`, `CurrentSaveInfo`, or similar.
- Look for a property that returns a string — probably something like
  `CurrentSaveName`, `SaveId`, or the save file path.
- Confirm it's accessible via `ISingletonRepository.GetSingletons<object>()`
  by adding it temporarily to `/api/debug` output.

---

## Step B — Expose save name in `GameServices` and `/api/gamestate`

Once we know the right service:

1. Add a static field for it in `GameServices.cs` (e.g. `GameServices.SaveName`).
2. Populate it in `GameServicesInitializer.Load()` using the same type-name switch
   pattern already used for WeatherService etc.
3. Add `"saveName": "..."` to the JSON response of `/api/gamestate`.
   - Return an empty string if the save name service wasn't found.
   - The frontend will treat an empty `saveName` as "no save loaded yet" and
     fall back to localStorage only.

---

## Step C — Add a file storage folder

Automation rule sets will be saved as JSON files on disk, one file per save name.

- Storage folder: `<ModDirectory>/automation_saves/`
- File naming: `<saveName>.json` (sanitise the name — strip characters that
  are illegal in file names)
- Create the folder on first write if it doesn't exist.

---

## Step D — New endpoint: `AutomationStorageEndpoint`

A new class that handles two routes:

- `GET /api/automation?save=<saveName>`
  — reads `automation_saves/<saveName>.json`, returns its contents as-is.
  — returns `{"rules":[]}` if the file doesn't exist yet (first time for that save).
  — returns 400 if `save` param is missing or empty.

- `POST /api/automation?save=<saveName>`
  — reads the request body (JSON string sent by the frontend).
  — writes it to `automation_saves/<saveName>.json`.
  — returns `{"ok":true}` on success.
  — returns 400/500 on bad input or write error.

Both routes return 503 if `GameServices.Ready` is false.

---

## Step E — Register the new endpoint

In `Configurator.cs`, add the same `Bind<>().AsSingleton()` +
`MultiBind<IHttpApiEndpoint>().ToExisting<>()` pair for `AutomationStorageEndpoint`.

No new DLL references needed — file I/O uses `System.IO` which is already available.

---

## Step F — Build and deploy

```
dotnet build HTTPAutomation.csproj -o Scripts -c Release
```

The compiled DLL goes to `Scripts/HTTPAutomation.dll` — same as always.
Reload the save in-game to pick up the new endpoints.

---

## Notes

- The frontend does **not** need to know about the file path or folder — it only
  sends save names it gets from `/api/gamestate`.
- localStorage stays as a session-level cache; the backend is the durable store.
- If the DLL probe in Step A comes up empty (game doesn't expose a save name at all),
  the fallback is to skip Step B and have the frontend stay on localStorage. The
  `GET /api/automation` and `POST /api/automation` endpoints would still work as a
  general-purpose rule store, just without per-save isolation.

---

## Step G — Fix `/api/beavers` with a live beaver tracker

### Problem
`/api/beavers` currently returns `[]` always. The Population tab's per-beaver
cards and the Dismiss button are both dead as a result. The root cause is that
`BeaverCollection` is not in Timberborn's DI container — it's built up by
event listeners at runtime and can't be constructor-injected into any endpoint.

### Solution: event-listener pattern
Timberborn's own `BeaverPopulation` class uses `[OnEvent]` methods on an
`ILoadableSingleton` to track beaver counts. We do the same thing to maintain
our own live list of `Beaver` objects.

The approach is to extend `GameServicesInitializer` (already registered as
`ILoadableSingleton`) rather than add a new class, since it already has the
`ISingletonRepository` constructor dep and the `Load()` guard:

1. In `Load()`: get `EventBus` from `GetSingletons<object>()` and store it in
   `GameServices.EventBus`. At the end of `Load()`, call `EventBus.Register(this)`
   so our `[OnEvent]` methods start receiving events.

2. Add `[OnEvent] void OnCharacterCreated(CharacterCreatedEvent e)` — if the
   character has a `Beaver` component, add it to `GameServices.Beavers`.

3. Add `[OnEvent] void OnCharacterKilled(CharacterKilledEvent e)` — if the
   character has a `Beaver` component, remove it from `GameServices.Beavers`.

4. `GameServices.Beavers` is a `List<Beaver>` protected by a lock object
   (`GameServices.BeaversLock`) because HTTP handlers run on thread-pool
   threads while game events fire on the Unity main thread.

5. Update `PopulationEndpoint.Dispatch()` to read `GameServices.Beavers` for
   the `/api/beavers` route instead of requiring `BeaverCollection`.

### DLL references needed
- `Timberborn.Characters.dll` — already referenced (has `CharacterCreatedEvent`,
  `CharacterKilledEvent`, `Character`)
- `Timberborn.SingletonSystem.dll` — already referenced (has `OnEventAttribute`,
  `EventBus`)
- `Timberborn.Beavers.dll` — already referenced (has `Beaver`)

No new references needed.

### Registration trap to avoid
`EventBus.Register(this)` must only be called once. The
`if (GameServices.Ready) return;` guard in `Load()` already ensures the body
only runs once, so this is handled automatically.

### Initial beaver population on save load
When a save loads, the game fires `CharacterCreatedEvent` for each entity it
deserialises, including existing beavers. Since `Load()` is called before entity
deserialisation, our `Register()` call will be in place in time to catch them.
This is confirmed by the game's own `BeaverPopulation` using the same pattern.

---

## Step H — Fix save-reload bug in GameServicesInitializer

### Problem
`GameServices` uses static fields that persist for the entire application lifetime.
`GameServices.Ready` is set to `true` after the first save loads and never reset.

When the player loads a **second save** mid-session:
- Timberborn destroys the old game scene and creates a new one
- `GameServicesInitializer` is a brand-new C# object (new DI container, new instance)
- `ILoadableSingleton.Load()` is called on this new instance
- But `GameServices.Ready == true` → the guard fires → `return` immediately
- Result: beaver list keeps old beavers from the dead scene, service references
  (`DayNightCycle`, `GameCycle`, etc.) point to destroyed Unity objects, and the new
  session's `EventBus` is never registered — so `OnCharacterCreated/Killed` events
  for the new save are never received

### Root cause
The guard `if (GameServices.Ready) return;` was designed to prevent double-execution
within a single save (because `[Context("Game")]` fires in multiple Bindito containers
per session). It correctly suppresses second-container calls but incorrectly suppresses
new-save calls, because it can't distinguish the two cases.

### Fix
Track which `GameServicesInitializer` instance last successfully ran `Load()` in a
static field (`GameServices.RegisteredInitializer`). On entry to `Load()`:

- If `Ready == false` → first-ever load, run normally.
- If `Ready == true` AND `this == RegisteredInitializer` → same-save second-container
  call, skip (existing behaviour).
- If `Ready == true` AND `this != RegisteredInitializer` → new save loaded mid-session.
  Reset all static state and run the full initialization again.

Reset means:
1. Unregister the old initializer from EventBus (if EventBus is still alive — wrapped in try/catch)
2. Clear `GameServices.Beavers` under the lock
3. Null out all service references so they get re-populated
4. Set `Ready = false` so the rest of `Load()` runs normally

No new DLL references needed. No new endpoints needed.
