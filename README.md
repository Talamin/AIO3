# AIO3 — All-in-One Combat Class for WRobot (WotLK 3.3.5a)

AIO3 is a ground-up rework of the [Talamin/AIO-Public](https://github.com/Talamin/AIO-Public)
WRobot fightclass. It keeps the proven **Action Priority List (APL)** model from the original
but rebuilds the foundation to be **layered, testable, and configurable in-game**.

> **Status — functional and in active use.** **Warrior** (Fury / Arms / Protection), **Paladin**
> (Retribution / Protection), **Hunter** (Beast Mastery / Marksmanship / Survival), **Mage**
> (Frost / Fire / Arcane) and **Warlock** (Affliction / Demonology / Destruction) run end-to-end
> in-game as solo leveling APLs (10–80). **Rogue** (Combat / Assassination) and **Druid** (Feral / Balance)
> are built and unit-tested, with in-game verification ongoing. Each class is a self-contained **module**
> (`IClassModule`), so the entry point is class-agnostic and adding the next class is dropping in a module.
> Remaining classes (Priest, Death Knight, Shaman) are not implemented yet — the foundation is built to add them.

## Why AIO3 (vs. the original AIO-Public)

Same APL idea, a rebuilt foundation. The original is a single ~18k-line project that grew organically;
AIO3 fixes the structural problems that made it hard to extend and trust:

| Area | Old AIO-Public | AIO3 |
|------|----------------|------|
| **Architecture** | the WRobot API is called throughout the codebase | one **compiler-enforced boundary** — every WRobot call lives in a single adapter; everything above it is game-agnostic |
| **Tests** | none | **500+ offline unit tests** against a fake game client (no WoW needed to run them) |
| **Per-tick cache** | a static dictionary, **not thread-safe** — only worked by luck of the frame lock | one **immutable snapshot per tick**, so the data races are gone by construction |
| **Idle CPU** | an out-of-combat spin-loop pegged a core while resting / dead | the loop **always sleeps**, and **idles entirely** while dead or running back to a corpse |
| **Drift** | cross-cutting logic was copy-pasted per spec (some specs ended up richer than others) | a **shared step library** every spec composes — interrupts, defensives, racials, pet control, the caster baseline |
| **Interrupts** | trusted the API's "interruptible" flag (unreliable on private servers) | **learns** which casts are really (non-)interruptible from the combat log, persisted per character |
| **In-game config** | a static GUI | **typed settings → auto-generated overlay** (`/aio3`), edited live, persisted per character |
| **Updates / shipping** | a risky auto-updater (a committed 2 MB DLL, overwrote the running assembly, no hash) | a single **self-contained, hot-swappable** DLL |
| **Product coexistence** | — | every behaviour a WRobot product might also do (targeting, interrupts) is a **toggle**, so they never fight |

## Design goals

- **Keep the APL model** — a rotation is a priority-sorted list of steps; the first one whose condition
  holds and whose target is valid casts. This matches how WoW rotations are reasoned about.
- **One hard boundary** — only a single adapter touches the WRobot API; everything above it is
  WRobot-agnostic and unit-testable offline. The boundary is *compiler-enforced* (see [Architecture](#architecture)).
- **No drift** — cross-cutting behaviour lives in a shared library that every spec composes, never copy-pasted.
- **Configure in-game** — settings are typed objects rendered as a clickable WoW UI panel (`/aio3`), edited live, persisted per character.
- **Coexist with WRobot products** — anything a product might also do is disableable via a setting.
- **Trust measurement over assumptions** — on a private server the API can lie; where it matters, AIO3 *learns from the combat log*.

## Classes

Every class auto-assigns talents per spec out of combat, gates each ability on being *learned* (so a
rotation runs cleanly from level 10 and fills in as you level), and composes the [shared systems](#shared-systems)
below (interrupts, racials, target switching, performance).

### Warrior — Fury / Arms / Protection

*Melee, rage. The reference implementation — most reusable patterns were established here.*

- **Stances:** Fury → Berserker, Arms → Battle, Protection → Defensive (falls back to whatever's learned).
- **Gap-closers (stance-dance):** **Charge** out of combat switches to Battle Stance, charges, then restores the home stance; **Intercept** covers the in-combat gap. *Default off* — they move the character, which a product may own.
- **Rotation:** rage-gated pooling vs. dumping · **Heroic Strike / Cleave** on-next-swing dump (guarded so the 50 ms tick doesn't re-queue it) · **Rend** (refreshed; skips bleed-immune creatures + bosses) · **Execute** < 20% · Victory Rush · Bloodrage · **AoE** (Thunder Clap / Whirlwind / Cleave at 2+ enemies in range).
- **Cooldowns / defensives:** Recklessness burst (boss / elite / pack) · Last Stand / Shield Wall / Shield Block · Enraged Regeneration · Berserker Rage (break fear).
- **Utility:** Pummel / Shield Bash interrupt · Hamstring / Piercing Howl slows · emergency Healthstone / potion.

### Paladin — Retribution / Protection

*Plate melee. Holy is intentionally absent — a healer, not a solo leveling spec.*

- **Upkeep (resolved live):** seal / aura / blessing / judgement — `Auto` picks a spec-appropriate default and falls back as you learn better options.
- **Retribution:** Judgement → Exorcism (on an Art-of-War proc) → Divine Storm → Crusader Strike · Hammer of Wrath < 20% · Consecration / Holy Wrath on packs.
- **Protection:** Righteous Fury + Holy Shield upkeep · Shield of Righteousness → Hammer of the Righteous · Avenger's Shield gated so it never pulls (the product owns the opener).
- **Survival:** self-sustained while leveling (Art-of-War Flash of Light / Lay on Hands / Divine Plea).

### Hunter — Beast Mastery / Marksmanship / Survival

*Ranged + pet. Source of the shared **pet controller** (`PetControl`).*

- **Pet:** keep summoned / revived / healed · send it to the target · **peel** adds off you (lowest-HP mob attacking you, then a mob attacking the pet) · **taunt** (re-taunts when a mob switches back) · pet **specials** (Bite/Claw, Dash/Dive, Call of the Wild, Furious Howl, Rabid) when they're on the bar.
- **Petless-safe:** everything keys on the pet *actually existing* (never on level) — a petless hunter plays clean ranged DPS, and abilities a pet lacks are skipped automatically.
- **Aspect:** Viper ↔ Hawk / Dragonhawk, managed by mana with hysteresis.

### Mage — Frost / Fire / Arcane

*First pure caster — the shared **caster baseline** (`MageCommon`) later casters reuse.*

- **Baseline:** armor upkeep · Arcane Intellect · mana (Evocation / conjured Mana Gem / wand) · survival (Ice Block / Ice Barrier / Mana Shield).
- **Cast handling:** cast-time nukes gate on standing still; instants and procs (Brain Freeze, Fingers-of-Frost shatter, Hot Streak, Missile Barrage) fire on the move.
- **Interrupt:** Counterspell (a Blood Elf's Arcane Torrent silence backs it up).
- **Kiting:** Frost Nova roots a mob as it enters the nova radius, then **Blink** away (with a landing-safety check) or a cliff-safe step back · holds for a Polymorphed add · skips a mob about to die or a *grey* trivial mob · suppressed while swimming.
- **Self-sufficient:** conjures its own food / water / mana gem and eats the best conjured food (clearing stale vendor food a plugin left) · summons and directs a **Water Elemental** (Frost) · optional Polymorph of an extra add, and finishes its own sheep after the kill.

### Warlock — Affliction / Demonology / Destruction

*Caster + permanent demon + DoTs (`WarlockCommon`). The demon is a real, managed tank.*

- **DoTs + filler:** keep Corruption / Immolate / Unstable Affliction / chosen Curse / Haunt up, fill with Shadow Bolt — **Conflagrate + Incinerate** (Destruction), **Soul Fire** on proc + **Demonic Empowerment** (Demonology).
- **Let the DoTs finish:** holds the filler on a low, normal mob already covered by enough DoTs (saves mana / Life-Tap pressure; bosses and elites exempt; tunable floor).
- **Pet handling:**
  - Summoned **before the pull**, *pinning* the character (cancelling the bot's travel re-pathing) so the ~10 s summon cast completes.
  - **Imp fallback** when out of Soul Shards (every demon but the Imp costs one), and **swaps back** to the wanted demon once a shard is harvested — never downgrading a healthy one.
  - **Voidwalker tanking:** **proactive** Torment (taunts the instant a mob leaves the pet, not after it reaches you) · **Suffering** AoE taunt when surrounded · **Consume Shadows** out-of-combat self-heal.
  - **Felhunter Spell Lock** (the warlock's *only* interrupt); **Imp** keeps Firebolt / Blood Pact / Fire Shield / Phase Shift on autocast.
- **Survival:** emergency **Fear / Howl of Terror** to break melee when low and surrounded.
- **Soul Shard economy:** **Drain Soul** harvests a shard off a dying mob (a 4× execute under 25 % HP — costs no DPS) · **Create Healthstone** restocks the emergency-heal item out of combat, so the supply never runs dry.

### Rogue — Combat / Assassination

*First melee combo-point class.*

- **Combat:** Slice and Dice uptime · Sinister Strike builder · Eviscerate finisher · Rupture (opt-in) · **Blade Flurry** on packs · Adrenaline Rush / Killing Spree cooldowns.
- **Assassination:** Mutilate builder · **Envenom** finisher (consumes Deadly Poison stacks) · Rupture + Hunger for Blood upkeep · Cold Blood · Fan of Knives on packs.
- **Weapon poisons:** keeps **Instant Poison** on the main hand and **Deadly Poison** on the off hand out of combat, picking the best rank for your level (so Envenom is the default Assassination finisher).
- **Stealth opener (opt-in):** picks by position — **Garrote** from behind (the shared behind-detection), else **Cheap Shot** from the front.
- **Utility / survival:** Kick interrupt · Evasion (panics at critical HP) · Cloak of Shadows (vs. magic) · Sprint · emergency item.

### Druid — Feral / Balance

*Hybrid: a melee combo-point form, a rage tank form, and an eclipse caster — one spec list that shifts between them. Restoration is intentionally absent (a healer).*

- **Feral (cat + bear, with form management):** **Cat** for single targets (Mangle / Claw builders · Rake bleed · Rip / Ferocious Bite finishers · Tiger's Fury), shifting to **Bear** when surrounded (Mangle / Lacerate / Swipe / Maul · Demoralizing Roar · Frenzied Regeneration). A **positional stealth opener** — **Ravage** from behind (the same behind-detection the rogue's Garrote uses), else **Pounce** from the front — and a caster fallback (Wrath / Moonfire) before the forms are learned.
- **Balance (eclipse caster):** Moonkin Form · Insect Swarm / Moonfire DoTs · **Eclipse-aware** nukes (Starfire under a Lunar proc, Wrath under Solar) · Starfall / Hurricane / Typhoon AoE · Force of Nature.
- **Survival (the hybrid's edge):** in-combat self-heal — an instant Regrowth / Healing Touch off a **Predator's Swiftness** proc, else a mana-gated shift out to heal · Barkskin · Innervate · Mark of the Wild / Thorns.

## Shared systems

Cross-cutting behaviour every class composes — built once, not per spec.

- **Interrupts** — `Smart` / `Always` / `Never`. `Smart` empirically **learns** which casts are actually (non-)interruptible from the combat log and persists that per character (the API flag is unreliable on private servers).
- **Racials** — one shared, class-agnostic bundle, each racial gated by *known-by-this-character* so it fires only for the right race on any class: **Blood Fury** (Orc), **Berserking** (Troll), **Arcane Torrent** (Blood Elf — AoE silence + resource), **War Stomp** (Tauren), **Gift of the Naaru** (Draenei), plus the defensive/utility racials — **Will of the Forsaken** & **Every Man for Himself** (break fear/charm/sleep), **Escape Artist** (break a root), **Stoneform** (cleanse poison/disease), **Shadowmeld** (last-ditch vanish), **Cannibalize** (out-of-combat corpse heal). CC-breaks / cleanse / panic take priority over offensive racials (which are held while feared, so a 2-minute cooldown isn't wasted).
- **Auto target switching** — *toggle per class.* Among enemies already attacking you (it **never pulls** — the product owns the opener), it focuses the lowest estimated **time-to-kill** (low health wins, minus the run-up cost of distant targets), with hysteresis to avoid thrashing. When a hostile **caster/hunter fights through its pet**, it redirects to the **owner** (kill the owner, the pet follows) instead of chasing the pet. Turn it off if a product owns targeting.
- **Cliff-safe backpedal** — when a mob is inside melee range (on the pet), the character steps back to restore ranged distance, refusing to move over a ledge (a downward trace guards the destination). The hop runs *on WRobot's own fight-loop thread* and briefly cancels its move-to-range, so the single continuous keypress is one smooth motion, not a stutter (default on, 7 yd).
- **Performance** — cooldowns / GCD read in one memory pass per tick (not an API call per spell) · enemy/party lists rebuilt on the object-manager pulse, not per tick · the WRobot frame lock held only around the unit snapshot · per-tick Lua reads (stance, auto-attack, usability, pet bar) cached · the rotation idles entirely while dead / corpse-running. A toggle logs per-tick / per-step timing (and unit positions) to disk.
- **Damage learning** *(in progress)* — a combat-log-fed `DamageTracker` learns per-ability damage. It *measures* for every class and is *advisory* for the Warrior (the `BestDamage` block picks the highest learned-damage strike among interchangeable options, behind a toggle). Still to do: feed it into the target-selection time-to-kill estimate.
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

- **Compiler-enforced boundary.** `AIO3.Core` has *no reference* to the WRobot assemblies, so nothing above the adapter can accidentally call the game API. The adapter (`WRobotGameClient`) implements the `IGameClient` seam; tests and an offline `FakeGameClient` implement the same seam.
- **Per-tick snapshot.** Each tick builds one immutable `CombatContext` (me, target, enemies, party, resources) — created once, read many — which removes the data races the old static cache had.
- **Single self-contained DLL.** WRobot loads the fightclass DLL from a byte array (so it isn't file-locked and hot-swaps while WRobot runs). To keep that, the Core sources are compiled *into* `AIO3.dll` rather than referenced as a second assembly; the WRobot libraries are referenced but **not** bundled (`Private=false`) and resolved from the WRobot `Bin` folder at runtime.

### In-game settings + spec selection

- Settings are typed (`ToggleSetting`, `IntSetting`, `ChoiceSetting`) and exposed by each rotation.
- `SettingsOverlay` auto-generates a dark, movable, tabbed WoW UI panel from that list (sliders, checkboxes, cycle buttons) — adding a setting needs no UI code. Open it from a draggable **minimap button** or with **`/aio3`**. Built with Blizzard's own UI, so no addon libraries.
- Edits flow Lua → C# through a small `AIO3Bridge` table and take effect live; values are saved per character under `<WRobot>\Settings\AIO3\<Character>.conf`.
- A **native over-the-game overlay** (a transparent WPF window that tracks the WoW client in borderless / windowed mode) renders the same auto-generated controls and binds them **directly** to the setting objects — so edits apply live with no Lua bridge. It follows the WoW window (DPI-aware), **remembers its position + state** per character, has a **filter box** and tabbed, hover-highlighted rows, and shows a **live status line** (spec · target HP% → current cast). It is **minimizable in-game** to a small status pill that keeps showing that line. The Lua panel above is the fallback (and the option under exclusive-fullscreen). See [docs/overlay-design.md](docs/overlay-design.md).
- **Spec selection** combines talent auto-detection with a manual override (the `Spec` dropdown): `Auto` picks the spec from the highest talent tree; below level 10 it falls back to a sensible default. A `Mode` selector (Solo / Group) chooses the rotation set — only Solo exists today. The active rotation is swapped at runtime when the spec or mode changes.
- **Class modules.** Each class is one `IClassModule` that owns its settings, resolves spec + mode → rotation, and supplies the talent build. The entry point looks the module up by the player's class, wires its settings into the overlay/persistence, ticks the resolved rotation, and applies talents. Adding a class is writing a module and registering it in one factory.

## Project layout

| Project        | Output               | WRobot reference      | Purpose                                            |
|----------------|----------------------|-----------------------|----------------------------------------------------|
| `AIO3.Core`    | (compiled into AIO3) | none                  | domain model, engine, rotations, settings, fakes   |
| `AIO3`         | `AIO3.dll`           | yes (`Private=false`) | fightclass entry + adapter + overlay + persistence |
| `AIO3.Tests`   | (not shipped)        | none                  | offline xUnit tests against `FakeGameClient`       |

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

See [ROADMAP.md](ROADMAP.md) for the class order (Warrior ✓ → Paladin ✓ → Hunter ✓ → Mage ✓ → Warlock ✓ →
**Rogue** (built) → Priest → Death Knight → Shaman → **Druid** (built)) and the cross-cutting systems built alongside them.
