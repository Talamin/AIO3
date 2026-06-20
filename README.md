# AIO3 — All-in-One Combat Class for WRobot (WotLK 3.3.5a)

AIO3 is a ground-up rework of the [Talamin/AIO-Public](https://github.com/Talamin/AIO-Public)
WRobot fightclass. It keeps the proven **Action Priority List (APL)** model from the original
but rebuilds the foundation to be **layered, testable, and configurable in-game**.

> Status: functional and in active use. All three **Warrior** solo specs — **Fury, Arms, Protection** —
> run end-to-end in-game as full leveling APLs (10–80), with talent auto-assign, an in-game settings
> overlay, per-character persistence, runtime spec selection, empirical interrupt learning, and a tuned
> performance path. Other classes are not implemented yet (the foundation is built to add them).

## Design goals

- **Keep the APL model** — a rotation is a priority-sorted list of steps; the first one whose
  condition holds and whose target is valid casts. This matches how WoW rotations are reasoned about.
- **One hard boundary** — only a single adapter touches the WRobot API; everything above it is
  WRobot-agnostic and unit-testable offline. The boundary is *compiler-enforced* (see below).
- **No drift** — cross-cutting behaviour (interrupts, defensives, buffs, auto-attack, gap-closers,
  Demoralizing Shout) lives in a shared library that every spec composes, instead of being copy-pasted.
- **Configure in-game** — settings are typed objects rendered as a clickable WoW UI panel (`/aio3`),
  edited live, and persisted per character.
- **Coexist with WRobot products** — any behaviour a product might also do (target selection, interrupts)
  is disableable via a setting so the fightclass and the product never fight each other.
- **Trust measurement over assumptions** — on a private server the API can lie (e.g. the "interruptible"
  flag). Where it matters, AIO3 *learns from the combat log* instead of trusting the flag.

## What's implemented

- **Warrior — Fury / Arms / Protection**, each a single solo leveling APL that fills in as you level
  (unknown spells auto-skip). Talent points are auto-assigned per spec out of combat.
- **Interrupts** — `Smart` / `Always` / `Never`. `Smart` empirically learns which casts are actually
  (non-)interruptible from the combat log and persists that per character (the API flag is unreliable).
- **Auto target switching** — optional, **off by default**. Among enemies already attacking you (it
  never pulls — the product owns the opener, and it only acts with several attackers), it focuses the
  one with the lowest estimated *time-to-kill* (low health wins, minus the run-up cost of distant
  targets), with hysteresis to avoid thrashing. Casters are handled by the interrupt system.
- **Performance** — cooldowns/GCD are read in one memory pass per tick (not a slow API call per spell);
  the enemy/party lists are rebuilt on the object-manager pulse, not per tick; the WRobot frame lock is
  held only around the unit snapshot; and the handful of per-tick Lua reads (stance, auto-attack,
  usability) are cached. A toggle logs per-tick / per-step timing for development.
- **Content** — the curated boss list is ported from the old project.

## Architecture

```
┌───────────────────────────────────────────────────────────────────────┐
│ Rotations (Layer 4)   thin specs: a baseline + class-specific filler    │  AIO3.Core
│ Shared library (L3)   Interrupt / Defensive / AutoAttack / SelfBuff …    │  (no WRobot
│ Engine (Layer 2)      priority runner + exclusive tokens + GCD gating    │   reference)
│ CombatContext (L1)    one immutable world snapshot per tick              │
│ IGameClient (seam)    the only abstraction the layers above talk to      │
├───────────────────────────────────────────────────────────────────────┤
│ WRobotGameClient      Layer 0 adapter — the ONLY code that uses wManager │  AIO3 (fightclass)
└───────────────────────────────────────────────────────────────────────┘
```

**Compiler-enforced boundary.** `AIO3.Core` has *no reference* to the WRobot assemblies, so nothing
above the adapter can accidentally call the game API. The adapter (`WRobotGameClient`) implements the
`IGameClient` seam; tests and an offline `FakeGameClient` implement the same seam.

**Per-tick snapshot.** Each tick builds one immutable `CombatContext` (me, target, enemies, party,
resources). It is the cache — created once, read many — which removes the data races the old static
cache had.

**Single self-contained DLL.** WRobot loads the fightclass DLL from a byte array (so it is not
file-locked and hot-swaps while WRobot runs). To keep that property, the Core sources are compiled
*into* `AIO3.dll` rather than referenced as a second assembly; the WRobot libraries are referenced
but **not** bundled (`Private=false`) and are resolved from the WRobot `Bin` folder at runtime.

### In-game settings + spec selection

- Settings are typed (`ToggleSetting`, `IntSetting`, `ChoiceSetting`) and exposed by each rotation.
- `SettingsOverlay` auto-generates a dark, movable, tabbed WoW UI panel from that list (sliders,
  checkboxes, cycle buttons) — adding a setting needs no UI code. It starts minimized; open it from a
  draggable **minimap button** or with **`/aio3`**. Built with Blizzard's own UI, so no addon libraries.
- Edits flow Lua → C# through a small `AIO3Bridge` table and take effect live; values are saved
  per character under `<WRobot>\Settings\AIO3\<Character>.conf`.
- **Spec selection** combines talent auto-detection with a manual override (the `Spec` dropdown):
  `Auto` picks the spec from the highest talent tree; below level 10 it falls back to a sensible
  default. The active rotation is swapped at runtime when the spec changes.

## Project layout

| Project        | Output            | WRobot reference     | Purpose                                  |
|----------------|-------------------|----------------------|------------------------------------------|
| `AIO3.Core`    | (compiled into AIO3) | none              | domain model, engine, rotations, settings, fakes |
| `AIO3`         | `AIO3.dll`        | yes (`Private=false`) | fightclass entry + adapter + overlay + persistence |
| `AIO3.Tests`   | (not shipped)     | none                 | offline xUnit tests against `FakeGameClient` |

## Building

Requires the .NET SDK (builds `net472`) or Visual Studio Build Tools, and a local WRobot install.

```powershell
# WRobotBin defaults to the path in Directory.Build.props; override if needed:
dotnet build AIO3.sln -c Release -p:WRobotBin="C:\path\to\WRobot\Bin"
dotnet test  AIO3.sln
```

A successful build copies `AIO3.dll` into the WRobot `FightClass` folder (override with
`-p:WRobotFightClass=...`). Restart the WRobot product to reload it — no need to close WRobot.

## Roadmap

See [ROADMAP.md](ROADMAP.md) for the class order (Warrior ✓ → Paladin → Hunter → Mage → Warlock → …)
and the cross-cutting systems built alongside them. In progress:

- **Empirical damage learning** — a combat-log-fed `DamageTracker` that learns per-ability damage. It is
  in *measure-only* mode now (records + logs); next it becomes *advisory* — re-ordering the damage filler
  and refining the target-selection time-to-kill estimate. The hand-tuned APL stays as the prior/fallback.
