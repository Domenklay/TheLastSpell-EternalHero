# The Last Spell — Eternal Hero / Вечный защитник

[![Build](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml/badge.svg)](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml)

## v0.8.0 — racial debuffs, expanded traits and 4-column UI

Changes since v0.7:

- **12 selectable permanent traits (plus randomly generated vanilla traits)**: the "Закалённый ветеран" node now has 11 ranks, each unlocking one additional selection. This preserves the same upgrade ID in older profiles. Original game's character sheet has a limited number of visible trait icons. All trait definitions reside on the hero regardless of how many it displays.
- **Compact 4-column trait/perk grid**: 20 entries per page, thinner rows, search by name/ID/effect and 4 filters — All, Without Penalties, With Penalties, Selected. Filters use actual trait stat and slot modifiers.
- **Negative race modifiers are neutralized only for Eternal Hero**: native racial stats with a negative Race component are compensated on reading the final stat. Negative `StatModifierEffect.Value` and `DynamicStatsModifierEffect.Value` belonging to racial perks are masked; **positive racial bonuses remain**. This includes conditional penalties like the red damage penalty in racial-perk descriptions. Original tooltips may still display the original negative text, because localization is not altered.
- **All five vanilla perk tiers accessible** to the protagonist irrespective of preceding perk purchases through a Harmony override of the native tier availability getter. The game's random selection of six perk columns is unchanged; permanent perks from other columns can be selected in the mod's perk catalogue. Vanilla perk-point costs for native tree purchases remain unchanged.
- All changes affect **the protagonist only**. Other party members keep their vanilla races, perks and traits. The UI shows whether racial penalty suppression is enabled.
- Metaprogression, permanent stats and achievements, save profile, portrait/race/trait persistence are retained.

**Status:** code compiles in GitHub Actions and persistence tests pass. Actual game runtime behavior, especially the damage suppression inside conditional racial perks, still requires a new-run smoke test.

## Downloads
[GitHub Actions](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml) → latest **green** run → artifact **EternalHero-v0.8**.

[Installation and verification](docs/INSTALL.md) · [Progression](docs/META_PROGRESSION.md) · [Roadmap](docs/ROADMAP.md)

This repository distributes no copyrighted game textures, original game binaries or fonts.
