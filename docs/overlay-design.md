# AIO3 — Native Live-Overlay (design, for review before any code)

**Goal.** Replace the in-game Lua settings panel ("fake addon") with a **real native overlay**: a transparent,
always-on-top window drawn *over* the WoW client (borderless/windowed mode), whose controls edit the rotation's
settings **live** — and which can be **minimized in-game** (collapse to a small on-screen token, no alt-tab).

This is purely additive UI in the `AIO3` adapter project. It does **not** touch the Core rotations or the engine.

---

## 1. What stays, what changes

| Layer | Today | After |
|-------|-------|-------|
| `Setting` model (`Toggle/Int/Choice`, `Category`, `Spec`, `Key`, `Label`, `Value`) | unchanged | **unchanged** — the single source of truth the rotation reads each tick |
| Persistence (`SettingsStore`, per character) | unchanged | **unchanged** |
| `IClassModule` wiring, the rotation read path | unchanged | **unchanged** |
| **View** | `SettingsOverlay` builds a WoW Lua frame; `Poll()` reads back via the `AIO3Bridge` Lua table every tick | a **WPF overlay window** renders the same setting list and **binds two-way directly to the `Setting` objects** — no Lua, no per-tick polling |

The overlay is just a second *view* over the existing model. The current Lua panel **stays in place as a fallback**
at first (a setting picks which view is active); we only remove it once the native overlay is proven.

**Why this is a clean win:** edits write straight into `setting.Value`, which the rotation already reads live, so a
change takes effect on the next tick — *more* direct than the Lua bridge, and immune to the `/reload` wipe.

---

## 2. The window

- **Plain WPF `Window`** (not MahApps): `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`,
  `ShowInTaskbar=False`, `ResizeMode=NoResize`, semi-transparent dark background, custom title bar.
  *(MahApps.Metro is shipped in the WRobot Bin, but its `MetroWindow` is a chromed normal window and fights the
  transparent-overlay use case; plain WPF + a small custom style is simpler here.)*
- **Layout:** a draggable title bar (`AIO3` + minimize `─` + close/hide `×`), a row of **Category tabs**
  (Spec / Rotation / Survival / …), and the active tab's controls. Same grouping the Lua panel uses.
- **Controls auto-generated from the `Setting` list** (mirrors `SettingsOverlay.BuildFrameLua`):
  - `ToggleSetting` → `CheckBox`
  - `IntSetting` → `Slider` (min/max/step) + value label
  - `ChoiceSetting` → `ComboBox` (or a cycle button)
- **Spec filter:** controls whose `Setting.Spec` doesn't match the active spec are hidden; the window **rebuilds on
  spec change** (same trigger the Lua overlay uses — `activeSpec` callback).

---

## 3. Tracking the WoW window (Win32 interop)

A `DispatcherTimer` (~15 Hz) on the UI thread keeps the overlay glued to the game:

1. **Find the WoW HWND** — preferred: read it from WRobot (it's already attached to the process; verify the exact
   member during build, e.g. the WoW process/main-window handle WRobot exposes). Fallback: `FindWindow`/enumerate
   by the WoW process + `MainWindowHandle`.
2. `GetWindowRect(hwnd)` → position/size the overlay relative to that rect (e.g. anchored top-left with an offset,
   or remembered position clamped into the rect).
3. **Foreground/visibility:** show the overlay only while WoW (or the overlay itself) is the foreground window
   (`GetForegroundWindow`); hide it when WoW is minimized or another app is focused, so it never floats over the
   desktop. Re-assert `Topmost` so it stays above the game.
4. **Multi-monitor / DPI:** position from the real `RECT`; handle per-monitor DPI (WPF `PresentationSource` /
   `Matrix` transform) so the overlay lands pixel-correct. *(Open item — see §9.)*

No D3D hook, no injection — this is ordinary Windows desktop compositing, which is why it needs
borderless/windowed (a separate top-level window cannot cover an **exclusive-fullscreen** D3D surface).

---

## 4. Threading

A FightClass runs on WRobot's threads, not a UI thread. So:

- Start **one dedicated STA thread** that creates the `Window`, shows it, and runs `Dispatcher.Run()`.
- The bot thread talks to the UI via `Dispatcher.BeginInvoke` (e.g. "active spec changed → rebuild").
- UI edits write `setting.Value` (plain `bool`/`int`/`string` fields — atomic in .NET); the rotation reads them on
  the bot thread. Simple value types need **no lock**; a change also schedules a **debounced save** through the
  existing `SettingsStore` (e.g. 1 s after the last edit).
- Lifecycle: created when the module's settings are wired (like the Lua overlay today), torn down on dispose
  (close the window, end the Dispatcher, join the thread).

---

## 5. Minimize-in-game (explicit requirement)

Three states, all **on-screen over the game** (never an OS-minimize to the taskbar):

- **Expanded** — the full panel.
- **Minimized** — the panel collapses to a small **draggable token** (an `AIO3` pill / gear icon) that stays over
  the game where the panel was (or a screen corner). **Click/double-click → expand.** This is the in-game minimize
  Daniel asked for: you stay in the game view, the panel shrinks to a dot, one click brings it back.
- **Hidden** — fully off, toggled by a **global hotkey** (and/or a tray/`/aio3`-style command). For an over-the-game
  hotkey we register a Win32 hotkey or a low-level keyboard hook (open item — §9).

The `─` button goes Expanded → Minimized; the `×` goes → Hidden. State + positions **persist per character**
(window position, minimized-or-not, last tab) alongside the settings file.

This mirrors today's minimap-button-opens-panel idea, but as a native token instead of a WoW frame.

---

## 6. Coexistence & rollout

1. **Phase 1 (prototype):** the WPF overlay runs **in parallel** with the Lua panel for the *current* class, read-
   only tracking + a couple of live controls, to validate window-tracking + live binding + minimize on Daniel's
   borderless setup.
2. **Phase 2:** full control generation (all setting types, tabs, spec filter), persistence of window/minimize
   state, the hotkey, polish.
3. **Phase 3:** a setting **"Settings UI: Native overlay / In-game panel"** picks the view; default flips to native
   once it's solid. The Lua panel stays as the fallback (and the only option in exclusive-fullscreen).

---

## 7. Persistence

- **Settings values:** unchanged (`SettingsStore`, per character), now written on live edit (debounced).
- **Overlay chrome:** window position, minimized state, last active tab → a small per-character UI-state file (or a
  section in the existing settings file). Restored on load.

---

## 8. Effort (rough)

- Window + transparent styling + control generation: **small–medium** (the control mapping mirrors existing logic).
- Win32 window-tracking + foreground/DPI handling: **medium** (the genuinely new part).
- STA threading + dispatcher plumbing: **small** (standard pattern).
- Minimize token + hotkey + persistence: **small–medium**.
- Total: a **medium**, self-contained subsystem, isolated from the combat code.

---

## 9. Open questions / to confirm during build

1. **WoW HWND from WRobot** — confirm the exact member WRobot exposes for the WoW window handle (else fall back to
   process `MainWindowHandle`). *(scout)*
2. **Global hotkey over the game** — `RegisterHotKey` (needs a message loop; the STA thread has one) vs a low-level
   keyboard hook. Pick the lighter one that works while WoW has focus.
3. **Per-monitor DPI** — verify the overlay lands correctly on a HiDPI/secondary monitor; add the DPI transform if
   needed.
4. **Focus** — clicking the overlay briefly takes focus from WoW. Acceptable for config; if it ever matters, a
   "click-through unless a modifier is held" mode is possible (`WS_EX_TRANSPARENT` toggling).
5. **MahApps?** — default is plain WPF; revisit only if we want the Metro look and it doesn't fight transparency.

---

*Status: design for review. No code written. Awaiting sign-off (and the answers to §9.1–9.2 don't block the
prototype — they have working fallbacks).*
