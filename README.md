# The Last Spell — Eternal Hero / Вечный защитник

[![CI](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml/badge.svg)](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml)

## v0.6 — compact UI + first-slot protagonist restoration

Compared with v0.5:

- The character editor renders at 78% of its previous size, centered in the game with a lighter backdrop.
- The floating launcher moves **out of the top-right 3-hero portraits**, becoming a small **✦** tab on the **right edge at half-screen height**. F8/F9/Home still work.
- No automatic menu takeover on launch (new configuration setting `OpenCreatorOnStartupV06=false`).
- Important gameplay fix: the first character in an **existing loaded save** now receives the permanent protagonist's identity, portrait, chosen race and traits, instead of the previous behavior that only recognized the hero without applying these settings.
- The CreateStartUnits hook always attempts to apply the protagonist, including profiles without the older `CreationConfirmed` flag.
- Refresh calls for the original in-game portrait, character model and HUD names are attempted after changing the character.
- Existing-save stat bonuses are **not re-added**, to avoid doubling them; new upgrade deltas still work where supported.
- Missed save-load hooks can recover by scanning the roster periodically.
- Persistent meta XP, 96 upgrades, 6 branches, skill XML and perk slots are unchanged.

**What this does not do:** spawn a fourth ally. It **replaces the first starting/loaded character** as originally requested, keeping the starting roster at three. It also does not yet guarantee the exact same avatar on every version of the game. Game version-specific tests are required.

## Installation and download

[Latest GitHub Actions builds](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml) → successful run → artifact **EternalHero-v0.6**. Extract the internal ZIP into the root of The Last Spell, replacing both DLLs.

See [Russian install/test instructions](docs/INSTALL.md), [progression design](docs/META_PROGRESSION.md), and [roadmap](docs/ROADMAP.md). Backup saves first. Mod requires BepInEx 5 x64. TLS.ModKit is required for the custom combat skill definitions.
