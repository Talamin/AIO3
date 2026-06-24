# AIO3 — All-in-One Combat Class for WRobot (WotLK 3.3.5a)

AIO3 is a ground-up rework of the [Talamin/AIO-Public](https://github.com/Talamin/AIO-Public)
WRobot fightclass. It keeps the proven **Action Priority List (APL)** model from the original
but rebuilds the foundation to be **layered, testable, and configurable in-game**.

> Status: functional and in active use. **Warrior** (Fury / Arms / Protection), **Paladin**
> (Retribution / Protection), **Hunter** (Beast Mastery / Marksmanship / Survival) and **Mage**
> (Frost / Fire / Arcane) run end-to-end in-game as solo leveling APLs (10–80), with talent auto-assign,
> an in-game settings overlay, per-character persistence, runtime spec selection, empirical interrupt
> learning, and a tuned performance path. Each class is a self-contained **module** (`IClassModule`) so the
> entry point is class-agnostic — adding the next class is dropping in a module. The Hunter brought a shared
> **pet controller** (`PetControl`); the Mage brought the **caster baseline** (`MageCommon`: mana, kiting,
> conjuring). The **Warlock** brings all three solo specs (**Affliction / Demonology / Destruction**) on a
> `WarlockCommon` caster baseline + the shared pet controller — built and unit-tested, **not yet in-game
> verified**, including pet specials (Torment tank-taunt, Spell Lock interrupt, Firebolt) and emergency Fear /
> Howl of Terror, with multi-target DoT spreading (`SpreadDot`) still to follow. The remaining
> classes are not implemented yet (the foundation is built to add them).

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

- **Warrior — Fury / Arms / Protection**, melee rage. Each spec manages its **stance** (Fury → Berserker,
  Arms → Battle, Protection → Defensive, falling back to whatever's learned) and works the **gap-closers** as a
  *stance-dance*: to **Charge** out of combat it switches to Battle Stance, charges, then restores the home
  stance; **Intercept** covers the in-combat gap (gap-closers default off — they move the character, which a
  WRobot product may own). On top of that: rage-gated ability choice (pool vs. dump), **Heroic Strike / Cleave**
  as an on-next-swing rage dump (guarded so the 50 ms tick doesn't re-queue it every frame), **Rend** (refreshed,
  skipped on bleed-immune creatures and bosses), **Execute** under 20%, **Victory Rush**, **Bloodrage**,
  **Hamstring / Piercing Howl** slows, **AoE** (Thunder Clap / Whirlwind / Cleave at 2+ enemies in range), a
  **burst** cooldown (Recklessness on a boss / elite / pack), **defensives** (Last Stand / Shield Wall / Shield
  Block, Enraged Regeneration, Berserker Rage to break fear), an emergency Healthstone / potion, racials, and a
  **Pummel / Shield Bash** interrupt. Talent points auto-assign per spec out of combat.
- **Paladin — Retribution / Protection** (Holy is intentionally absent — a healer, not a solo leveling spec).
  Brings the shared **seal / aura / blessing / judgement** upkeep (`PaladinCommon`), each resolved **live** so an
  `Auto` choice picks a spec-appropriate default and falls back as you learn better options. **Retribution**:
  Judgement → Exorcism (on an Art-of-War proc) → Divine Storm → Crusader Strike, Hammer of Wrath under 20%,
  Consecration / Holy Wrath on packs. **Protection**: Righteous Fury + Holy Shield upkeep, Shield of
  Righteousness → Hammer of the Righteous, Avenger's Shield gated so it never pulls (the product owns the opener).
  Self-sustained while leveling (Art-of-War Flash of Light / Lay on Hands / Divine Plea). Talents auto-assign.
- **Hunter — Beast Mastery / Marksmanship / Survival**, ranged + pet. Brings the shared, class-agnostic
  **pet controller** (`PetControl`): keep the pet summoned / revived / healed, send it to the target,
  **peel** adds off you (it redirects to the lowest-HP mob attacking *you*, then a mob attacking the pet),
  **taunt** (re-taunts whenever a mob switches back to you), and fire the pet's **special abilities**
  (Bite/Claw, Dash/Dive, Call of the Wild, Furious Howl, Rabid) when they're on its bar. Everything
  pet-related keys on the pet *actually existing* (never on level), so a petless hunter (below the taming
  level / untamed / a product owning the pet) plays as a clean ranged DPS with the pet steps skipped — and
  abilities a given pet lacks are skipped automatically. The aspect (Viper ↔ Hawk/Dragonhawk) is managed by
  mana with hysteresis.
- **Mage — Frost / Fire / Arcane**, the first pure caster and the shared **caster baseline** (`MageCommon`)
  the later casters reuse: armor upkeep, Arcane Intellect, mana management (Evocation / conjured Mana Gem /
  wand) and survival (Ice Block / Ice Barrier / Mana Shield). Cast-time nukes gate on standing still while
  instants and procs (Brain Freeze, Fingers-of-Frost shatter, Hot Streak, Missile Barrage) fire on the move.
  **Interrupts** — Counterspell (a Blood Elf's Arcane Torrent silence backs it up via the shared racial bundle).
  **Kiting** — Frost Nova roots a mob as it enters the nova radius, then Blink away (with a landing-safety
  check) or a cliff-safe step back; it holds for a Polymorphed add, skips a mob about to die or a *grey*
  trivial mob several levels below you (just nuke those), and is suppressed while swimming (the product wins
  the position fight in water, so the mage just stands and nukes).
  **Self-sufficient** — conjures its own food / water / mana gem and points WRobot at the best conjured food
  in the bags to eat/drink (clearing any stale named vendor food/drink a plugin left behind, once on start, so
  it doesn't override the conjured food); summons and directs a **Water Elemental** (Frost). Polymorphs an extra attacker
  (off by default; only sheepable creature types, resolved live) and, after the kill, retargets its own sheeped
  add so it gets finished instead of waking up untended.
- **Warlock — Affliction / Demonology / Destruction**, caster + permanent pet + DoTs on the `WarlockCommon`
  caster baseline (Fel / Demon Armor, **Life Tap** for mana, Drain Life self-heal, wand). Each spec keeps its
  DoTs up (Corruption / Immolate / Unstable Affliction / the chosen Curse / Haunt) and fills with its signature
  damage — Shadow Bolt, **Conflagrate + Incinerate** (Destruction), Soul-Fire-on-proc + **Demonic Empowerment**
  (Demonology). When a low, normal mob is already covered by enough DoTs to finish it, the filler nuke is
  **held so the DoTs kill it** instead of overkilling — saving mana / Life-Tap pressure while leveling (tunable
  HP% floor; bosses and elites are never affected). A **per-spec Auto pet** (Affliction → Voidwalker, Demonology
  → Felguard, Destruction → Imp) is **summoned out of combat *before* the pull** — it PINS the character (cancelling
  the bot's travel re-pathing) so the long summon cast completes — and **falls back to the free Imp when out of Soul
  Shards**, then **swaps back** to the wanted demon once a shard is harvested (never downgrading a healthy one). It's
  commanded through the shared pet controller, including its **special abilities**: the Voidwalker **proactively
  taunts** (Torment as soon as a mob leaves the pet, not after it reaches you) and **AoE-taunts** (Suffering when
  surrounded), self-heals out of combat (**Consume Shadows**), and tanks mobs off the cloth caster; the Felhunter
  **Spell Lock** is the warlock's *only* interrupt; and the Imp keeps **Firebolt / Blood Pact / Fire Shield / Phase
  Shift** on autocast. **Emergency Fear / Howl of Terror**
  break melee when you're low and surrounded so you can heal. A self-sustaining **Soul Shard economy** keeps the
  reagent stocked: **Drain Soul** harvests a shard off a dying mob when shards run low (it's a 4× execute under
  25% HP, so it costs no damage), and **Create Healthstone** restocks the emergency-heal item out of combat — so
  the Healthstone supply never runs dry. *(Rotations, shard economy and pet handling in-game-verified.)*
- **Cliff-safe backpedal** — when a mob is in the hunter's face (on the pet, inside melee range), the
  hunter steps back to restore ranged distance, refusing to move over a ledge (a downward trace guards the
  destination). The hop runs *on WRobot's own fight-loop thread* and briefly cancels its move-to-range for
  that iteration, so the one continuous keypress isn't fought by the product's path correction — it is a
  single smooth motion, not a stutter. Toggle + distance are settings (default on, 7 yd).
- **Interrupts** — `Smart` / `Always` / `Never`. `Smart` empirically learns which casts are actually
  (non-)interruptible from the combat log and persists that per character (the API flag is unreliable).
- **Racials** — one shared, class-agnostic bundle (the equivalent of the old project's RacialManager) that
  *every* class composes. Each racial is gated by *known-by-this-character*, so it fires only for the right
  race on any class that race can be: **Blood Fury** (Orc), **Berserking** (Troll), **Arcane Torrent** (Blood
  Elf — 8-yd AoE silence + resource), **War Stomp** (Tauren), **Gift of the Naaru** (Draenei), and the
  defensive/utility racials — **Will of the Forsaken** & **Every Man for Himself** (break fear/charm/sleep),
  **Escape Artist** (break a root), **Stoneform** (cleanse poison/disease), **Shadowmeld** (last-ditch vanish),
  and **Cannibalize** (out-of-combat corpse heal). One *Use racials* toggle per class; the CC-breaks / cleanse /
  panic racials take priority over the offensive ones (and the offensive racials are held while you're feared,
  so a 2-minute cooldown isn't wasted on a cast you can't make).
- **Auto target switching** — **on by default** (toggle per class). Among enemies already attacking you
  (it never pulls — the product still owns the opener, and it only acts with several attackers), it
  focuses the one with the lowest estimated *time-to-kill* (low health wins, minus the run-up cost of
  distant targets), with hysteresis to avoid thrashing. Turn it off if a WRobot product owns targeting
  and the two start fighting over the target. Casters are handled by the interrupt system.
- **Performance** — cooldowns/GCD are read in one memory pass per tick (not a slow API call per spell);
  the enemy/party lists are rebuilt on the object-manager pulse, not per tick; the WRobot frame lock is
  held only around the unit snapshot; and the handful of per-tick Lua reads (stance, auto-attack,
  usability) are cached. The rotation idles entirely while dead or running back to a corpse (the product
  owns the corpse run). A toggle logs per-tick / per-step timing (and, for diagnosis, unit positions) to disk.
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
  default. A `Mode` selector (Solo / Group) chooses the rotation set — only Solo exists today; Group is
  a placeholder for later. The active rotation is swapped at runtime when the spec or mode changes.
- **Class modules.** Each class is one `IClassModule` (e.g. `WarriorModule`, `PaladinModule`) that owns
  its settings (spec/mode selectors included), resolves spec + mode → rotation, and supplies the talent
  build. The WRobot entry point is class-agnostic: it looks the module up by the player's class, wires
  its settings into the overlay/persistence, ticks the resolved rotation, and applies the talents. Adding
  a class is writing a module and registering it in one factory.

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

- **Empirical damage learning** — a combat-log-fed `DamageTracker` learns per-ability damage. It both
  *measures* (records + logs) and is now *advisory*: the `BestDamage` block picks the highest learned-
  damage strike among interchangeable options (behind the "Use damage learning" toggle, hand order as the
  fallback). Still to do: feed the learned damage into the target-selection time-to-kill estimate.
