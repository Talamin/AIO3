# AIO3 Roadmap

## Classes (in priority order)

The order deliberately builds the big shared systems in a sensible sequence — buffs → pets →
casters → DoT-spreading — and then fills in the remaining classes.

- [x] **Warrior** — melee, rage, stances. *(Fury / Arms / Protection solo leveling specs, 10–80.)*
- [x] **Paladin** — hybrid melee. *(Retribution / Protection solo leveling specs, 10–80; Holy is not a
      solo leveling spec here.)* Brought the seal / aura / blessing / judgement buff system (`PaladinCommon`)
      and the class-module abstraction (`IClassModule`) that makes Main class-agnostic.
- [x] **Hunter** — first pet class. *(Beast Mastery / Marksmanship / Survival solo leveling specs.)* Built
      the shared, class-agnostic **pet controller** (`PetControl`: summon / revive / heal / attack / **taunt**) —
      keyed on the pet actually existing (never on level), so a petless hunter plays ranged-only and any pet
      without a given ability (e.g. a taunt) is handled automatically. The pet peels adds off the owner, and
      a cliff-safe backpedal regains ranged distance.
- [x] **Mage** — first pure caster: mana, cast-while-stationary, kiting (the caster baseline). *(Frost /
      Fire / Arcane solo leveling specs; `MageCommon` caster baseline — armor, mana, survival, kiting
      (Frost Nova → Blink / step-back, suppressed while swimming), Polymorph adds, Water Elemental,
      conjure + eat/drink best bag food. Skips the tick while dead/ghost.)*
- [ ] **Warlock** — caster + permanent pet + DoTs: reuses the pet controller and builds the
      **`SpreadDot`** shared block.
- [ ] **Rogue** — melee, energy / combo points.
- [ ] **Priest** (Shadow) — caster DoTs (+ healing later).
- [ ] **Death Knight** — melee, runes / runic power + temporary pet.
- [ ] **Shaman** — hybrid (Enhancement / Elemental / Restoration) + totems.
- [ ] **Druid** — shapeshifting forms (Feral / Balance / Restoration); most complex, last.

## Cross-cutting systems (built alongside)

- [ ] **DamageTracker** — learn per-ability damage from the combat log. *Measure-only first* (record +
      log), then *advisory* (re-orders the damage filler and feeds the target-selection time-to-kill
      estimate). Class-agnostic; pet damage tracked in a separate notebook by the pet's GUID.
- [x] **Pet controller v1** (with Hunter) — `PetControl`: keep the pet summoned / revived / healed, send it
      to the target, and **taunt** (cast a named pet ability off its bar, auto-skipping pets that lack it).
      Class-agnostic (Warlock / DK reuse it), keyed on the pet existing (never level), behind a setting.
      *v2 still to do:* the pet as a full second rotation (its own ability priority + pet-bar cooldowns),
      pet-aware add selection (`IsTargetingMeOrMyPet`), and multi-target taunt redirect.
- [ ] **`SpreadDot`** shared block (with Warlock) — apply / maintain a DoT across several enemies.
- [ ] Content port — important debuffs / dispels from the old project.
- [ ] CI (build + tests) and DLL releases.
