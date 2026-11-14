# Eternal Hero — Вечный защитник

[![Build](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml/badge.svg)](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml)

## Version 0.3 — playable UI and metaprogression test build

This is a BepInEx 5 mod prototype for The Last Spell. It is not yet the complete permanent-protagonist gameplay feature.

**Implemented:** Five tabs with 17 purchasable permanent upgrades, separate JSON profile with a backup, game-styled menu, loaded-game UI sprite lookup (with its own fallback skin), an initial 100 XP / one essence gift after the first hero is found, best-effort capture and restore of character appearance properties, tested run reward ledger, debug diagnostics for game stats and ability APIs. An experimental stat bridge is opt-in only.

**Skill definitions:** Three custom XML skills (EternalGhostShot, EternalEtherSlash, EternalArcanePulse) with Russian names. Requires [TLS.ModKit](https://github.com/stunl0ck/tls-modkit) to load the XML. They are NOT yet granted to the hero automatically.

**Still under development:** Guaranteed same portrait and body across runs, automatic real-night rewards, safe automatic game-stat bonuses, actual grants of existing traits/perks, and weapon-independent active skills on the combat hotbar.

[Download the latest successful artifact](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml)

See [Install instructions](docs/INSTALL.md) and [Roadmap](docs/ROADMAP.md).

No copyrighted game binaries, portraits or textures are distributed. The game itself supplies UI sprites when available at runtime.
