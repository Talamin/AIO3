# AIO3 — All-in-One Combat Class for WRobot (WotLK 3.3.5a)

AIO3 is a ground-up rework of the [Talamin/AIO-Public](https://github.com/Talamin/AIO-Public)
WRobot fightclass. It keeps the proven **Action Priority List (APL)** model from the original
but rebuilds the foundation to be **layered, testable, and configurable in-game**.

> Status: early but functional. The Warrior **Solo Fury** rotation runs end-to-end in-game,
> with an in-game settings overlay, per-character persistence, and runtime spec selection.
> Other classes/specs are not implemented yet.

## Design goals

- **Keep the APL model** — a rotation is a priority-sorted list of steps; the first one whose
  condition holds and whose target is valid casts. This matches how WoW rotations are reasoned about.
- **One hard boundary** — only a single adapter touches the WRobot API; everything above it is
  WRobot-agnostic and unit-testable offline. The boundary is *compiler-enforced* (see below).
- **No drift** — cross-cutting behaviour (interrupts, defensives, buffs, auto-attack, gap-closers)
  lives in a shared library that every spec composes, instead of being copy-pasted per spec.
- **Configure in-game** — settings are typed objects rendered as a clickable WoW UI panel (`/aio3`),
  edited live, and persisted per character.

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
- `SettingsOverlay` auto-generates a movable WoW UI panel from that list (checkboxes, `[-]/[+]`,
  cycle buttons) — adding a setting needs no UI code. Toggle the panel with **`/aio3`**.
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

- Warrior: Arms and Protection specs; settings-gated utilities (Hamstring, Piercing Howl).
- Port curated content from the old project (boss list, talent builds, important debuffs).
- More classes/specs; richer shared library (cooldowns, time-to-die, incoming damage).
- CI (build + tests) and distributing the compiled DLL via GitHub Releases.
