# The Last Spell — Eternal Hero / Вечный защитник

[![CI](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml/badge.svg)](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml)

## v0.4 — Native character creator and permanent-protagonist prototype

The progression UI has been **replaced** by a real Unity UGUI Canvas (not Unity IMGUI/OnGUI). BepInEx 5 only loads the plugin. Uses game sprites/icons when available and locally styled frames as fallback.

### Current features

- Create a permanent protagonist profile: **name, unlocked race, gender, portrait code, and a loadout of existing game traits**.
- Creator panel in **Russian**, native Unity buttons, text inputs, scrollable trait picker, original game race icons and game portrait preview.
- **Native game appearance editor** for faces, artwork, hairstyles, colors, and portrait. It can be opened **after beginning a run**, when a real hero exists.
- A **single free essence point** granted immediately to existing/new profiles for testing, once only.
- Harmony patch on the **static** `PlayableUnitManager.CreateStartUnits` (previous versions incorrectly assumed an instance). First generated hero is customized and the selected existing traits are granted through the game's own `AddTrait`.
- Race change uses the game's race definitions and race stat handlers.
- Persistent upgrades are applied to game stats through the verified `IncreaseBaseStat` signature on newly generated heroes, with per-object delta protection.
- Three **weapon-independent special skills** registered through [TLS.ModKit](https://github.com/stunl0ck/tls-modkit) and automatically granted via `SkillController` / `NativeSkills` if unlocked.
- Stable run ID and automatic night meta-XP on `DistributeDailyExperience`, anti-duplication ledger per day; recognize hero when loading an existing save without stacking stats.
- Save of character creation choices and purchased upgrades, including backups.

### Limitations and test status

CI **compiles** the plugin, checks persistence tests and validates skill XML syntax. We do **not yet claim** that all behavior works in-game on your build: portrait restoration, hero assignment, race changes, special skills, automatic night XP and stat changes need an actual game smoke test.

Existing perks (other than selected game traits), richer illustrations and win/loss/boss automatic rewards are not yet connected; run reward engine already supports these events.

**Install:** see [Russian instructions](docs/INSTALL.md). **Progress:** [roadmap](docs/ROADMAP.md).

## Download

[Latest successful GitHub Actions build](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml) → Artifacts → EternalHero-prototype.

The repository does not include original game DLLs, copied artwork or fonts.
