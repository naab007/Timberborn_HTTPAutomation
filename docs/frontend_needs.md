# Frontend API Contract — v3 (2026-03-22)
### What the frontend actually needs from each endpoint

---

## GET /api/levers

**Status: RESOLVED in v5.0.0 ✅**

Now returns full data including URL fields:

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

`colorUrl` is a template. Replace `{color}` with any 6-char hex to recolor the in-game model:
```js
fetch(lever.colorUrl.replace('{color}', 'ff0000'), { method: 'POST' }) // red
fetch(lever.colorUrl.replace('{color}', '3a7bff'), { method: 'POST' }) // blue
fetch(lever.colorUrl.replace('{color}', 'ffffff'), { method: 'POST' }) // white
```

---

## GET /api/adapters

No issues. Returns `[{"name":"...","state":false}]` — frontend only needs name and state.

---

## GET /api/gamestate

No issues. Returns full object including `saveName`. Working correctly.

---

## GET /api/population

No issues. Working correctly.

---

## GET /api/automation?save=NAME / POST /api/automation?save=NAME

No issues. Working correctly.

---

## GET /api/log?lines=N / POST /api/log

No issues. Working correctly.

---

## CRITICAL: Remove the inline SPA

The page still contains the full `TimberAutoControl` inline SPA. It is being suppressed
by the `__TAC__` guard workaround but it still executes partially (its own `render()` and
`poll()` run). This causes:
- SPA1's `setInterval(poll, 3000)` fires unnecessary API calls in parallel with ours
- SPA1's `runAutomation()` runs its own (simpler) rule engine in parallel with ours
- SPA1's `window.TAC` gets assigned (then immediately overwritten by ours — harmless but wasteful)

The inline SPA should be removed from the page entirely. The page should contain only:
1. `window.TB.levers` and `window.TB.adapters` injections (game's own — do not touch)
2. Our `BuildBody()` CSS block — exactly ONCE
3. Our `BuildFooter()` `<script src="/automation.js?v=10">` — exactly ONCE

---

## CRITICAL: Fix double `<script src="/automation.js">` tag

`/automation.js` still loads twice per page. The `__TAC__` guard workaround prevents the
second copy from overwriting `window.TAC`, but it's wasteful and the guard position is fragile.
Fix the `_registered` guard in `Configurator.cs` so `AutomationUiSection` only registers once.

---

## Summary

| Issue | Severity | Workaround in place? |
|-------|----------|----------------------|
| `/api/levers` missing URLs | HIGH | Yes — merge-only poll |
| Inline SPA still present | MEDIUM | Yes — __TAC__ guard |
| Double script tag | LOW | Yes — __TAC__ guard |
