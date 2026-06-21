# AIO3 Roadmap

## Classes (in priority order)

The order deliberately builds the big shared systems in a sensible sequence — buffs → pets →
casters → DoT-spreading — and then fills in the remaining classes.

- [x] **Warrior** — melee, rage, stances. *(Fury / Arms / Protection solo leveling specs, 10–80.)*
- [x] **Paladin** — hybrid melee. *(Retribution / Protection solo leveling specs, 10–80; Holy is not a
      solo leveling spec here.)* Brought the seal / aura / blessing / judgement buff system (`PaladinCommon`)
      and the class-module abstraction (`IClassModule`) that makes Main class-agnostic.
- [ ] **Hunter** — first pet class: builds the **pet controller** + ranged + focus.
- [ ] **Mage** — first pure caster: mana, cast-while-stationary, kiting (the caster baseline).
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
- [ ] **Pet controller** (with Hunter) — the pet as its own actor: its own target selection, ability
      priority list, and cooldowns (read from the pet action bar — we share no cooldowns with the pet).
      Pet-aware add selection (enemies attacking the pet). Behind a setting (product coexistence).
- [ ] **`SpreadDot`** shared block (with Warlock) — apply / maintain a DoT across several enemies.
- [ ] Content port — important debuffs / dispels from the old project.
- [ ] CI (build + tests) and DLL releases.
