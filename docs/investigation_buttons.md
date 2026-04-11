# Investigation: Silent Button Failures + Kick to Dashboard
**Date:** 2026-03-22

---

## Summary of Finding

There are **two complete competing SPAs** running on the same page simultaneously. This is the
root cause of all button failures and the "kick to dashboard" behaviour.

---

## What the Page Actually Contains

Fetching `http://localhost:8080/` reveals the full rendered HTML. Key structure:

```
1. <head> — Bootstrap CSS, body padding-top:56px
2. <nav class="navbar ... fixed-top">  ← game's OWN nav with .tab-btn buttons
3. TB.levers / TB.adapters injected as <script> tags
4. Our CSS hide blocks (BuildBody) — injected TWICE (double-registration bug)
5. <main class="container-fluid"> with #tab-dashboard, #tab-levers etc.
6. FULL inline JavaScript SPA — "TimberAutoControl" — several hundred lines
7. <div id="errAlert">
8. <script src="/automation.js?v=6"></script>  ← our JS, first copy
9. <script src="/automation.js?v=6"></script>  ← our JS, SECOND copy (bug)
```

The backend agent wrote an **entirely new frontend SPA** injected as an inline `<script>` directly
into the page. Our `index-levers-footer.hbs` is ALSO loaded on top of it as `/automation.js`.

---

## The Two Competing SPAs

### SPA 1 — Game's new "TimberAutoControl" (inline script, runs first)
- Defines its own `const S = { tab: 'dashboard', ... }` (closure, unreachable from outside)
- Defines its own `function render()`, `function poll()`, `function renderDashboard()` etc.
- Defines its own `window.TAC` with `leverOn`, `leverOff`, `leverRed`, `leverGreen`, `addRule`
- Sets up tab listeners on `.tab-btn` elements (the game's nav buttons)
- Calls `render(); poll(); setInterval(poll, 3000);` at boot
- Renders dashboard content into `#tab-dashboard` etc.

### SPA 2 — Our `index-levers-footer.hbs` (loaded twice as /automation.js, runs second)
- Defines its own `var S = { tab: 'dashboard', ... }` (closure, separate from SPA 1's S)
- Our `takeover()` runs:
  1. Removes the game's nav (`nav.navbar.fixed-top`)
  2. Removes the game's main content area (`main.container-fluid`) ← **CRITICAL PROBLEM**
  3. Appends our `#tac-root` with nav + `#tac-content`
- Defines its own `window.TAC` — **overwrites SPA 1's TAC** — now `TAC.on/off/newRule` etc.
- Starts its own poll and setInterval

---

## Why Buttons Fail: The TypeError Crash Loop

After our `takeover()` removes `main.container-fluid`, the game's `#tab-dashboard`,
`#tab-levers` etc. no longer exist in the DOM.

SPA 1's `setInterval(poll, 3000)` keeps running. Every 3 seconds:
1. Fetches `/api/levers`, `/api/adapters`, etc.
2. Calls `render()` → `renderDashboard()`
3. `renderDashboard()` does `document.getElementById('tab-dashboard').innerHTML = ...`
4. `getElementById('tab-dashboard')` returns **null**
5. `.innerHTML = ...` on null → **TypeError: Cannot set property 'innerHTML' of null**
6. This TypeError is an **unhandled Promise rejection** (thrown inside `.then()`)

In the in-game Chromium/CEF browser, unhandled Promise rejections in `.then()` blocks
may trigger a **page reload or navigation**, depending on the CEF version and flags.
This would explain the "kick to dashboard" — the page reloads, both SPAs reinitialise,
and the app boots to Dashboard.

Even if CEF doesn't reload, the TypeError every 3 seconds produces console errors and
may interfere with the browser's event queue in subtle ways.

---

## The `window.TAC` Collision

SPA 1 sets `window.TAC = { leverOn, leverOff, leverRed, leverGreen, addRule, ... }`.
SPA 2 then overwrites it: `window.TAC = { on, off, red, green, newRule, ... }`.

Our `renderLevers()` generates `onclick="TAC.on('NAME')"` — which calls OUR TAC.on. ✓
Our `renderRuleList()` generates `onclick="TAC.newRule()"` — which calls OUR TAC.newRule. ✓

So the TAC collision is NOT causing the button failures directly. Our TAC is in control.

BUT — SPA 1's `runAutomation()` is ALSO running every 3 seconds on SPA 1's lever state
data. If SPA 1's automation rules match, it POSTs to lever URLs, which can undo or
conflict with actions the user just took via our UI.

---

## The Double Load Bug

`/automation.js?v=6` appears TWICE in the HTML:
```html
<script src="/automation.js?v=6"></script>
<script src="/automation.js?v=6"></script>
```

This means `AutomationUiSection.BuildFooter()` is being called twice — the static
`_registered` guard in `Configurator.cs` has failed again, allowing two
`IHttpApiPageSection` instances to register. The `window.__TAC__` guard in our JS
prevents the second execution from running takeover() again, but the double `<script>`
tag is a symptom that the DI guard is broken.

Similarly, our CSS `BuildBody()` block appears twice (same cause).

---

## The Lever switchOnUrl Format

The actual lever URLs from the game (confirmed from page source):
```
/api/switch-on/HTTP%20Lever%201
/api/switch-off/HTTP%20Lever%201
/api/color/HTTP%20Lever%201/ff0000
/api/color/HTTP%20Lever%201/00ff00
```

These are relative paths. `TAC.on()` calls `post(l.switchOnUrl)` which fetches these.
The endpoints exist in the stock game — so the actual lever toggle DOES reach the game.
The lever is probably changing state. The "without activating" perception is because the
page reloads before the user sees the new state reflected.

---

## Root Causes — In Order of Severity

| # | Cause | Effect |
|---|-------|--------|
| 1 | `main.container-fluid` removed from DOM | SPA 1's `render()` throws TypeError on every poll |
| 2 | Two SPAs running simultaneously | Competing poll loops, competing window.TAC, automation conflicts |
| 3 | Unhandled TypeError → possible CEF page reload | User is kicked to dashboard on every button click |
| 4 | `_registered` guard broken → double `<script>` load | Wasteful, signals deeper DI issue |

---

## What Needs Fixing

1. **Don't remove the game's `main.container-fluid` — hide it instead**
   (`el.style.display='none'` rather than `el.remove()`)
   This stops the TypeError crash loop. SPA 1's render() will silently write to invisible
   elements instead of throwing.

2. **Remove SPA 1 entirely** — the backend agent's inline SPA should be removed from the
   page. Our `index-levers-footer.hbs` is the intended full frontend. Having two SPAs is
   inherently broken.

3. **Fix the `_registered` guard** in `Configurator.cs` to stop the double BuildFooter call.

4. **Suppress SPA 1's poll loop** — `clearInterval` on the game's poll interval, or ensure
   the game's SPA is removed before our SPA runs. Until SPA 1 is removed from the backend,
   we can try to intercept by overriding the game's `render` function to a no-op before
   our takeover (but it's inside a closure, unreachable directly).
