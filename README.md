# Eternal Hero / Вечный защитник — The Last Spell

[![Build](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml/badge.svg)](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml)

**v0.5.0 — six-branch metaprogression**. The goal is a persistent protagonist who can eventually do the work of three ordinary heroes in one run.

## What's built

- **96 upgrade nodes** across six branches: Стальное тело (16), Мастер оружия (16), Тактический гений (16), Наследие (16), Запретные техники (16), Триединство (16).
- The rank 1–100 indicator is based on completion of all available upgrade ranks (not game XP level). Main-tree completion unlocks **Ascension**, with six repeatable increases after maxing every node.
- A native Unity UGUI screen now offers category switching and displays only 16 upgrades per branch, their prerequisites, ranks and essence prices. Ascension has its own sixth tab.
- **Six special skill definitions**, requiring [TLS.ModKit](https://github.com/stunl0ck/tls-modkit): Ghost Shot, Ether Slash, Arcane Pulse, Hunter Echo, Mage Echo, and Spectral Follow-up.
- Hunter/Mage Echo and follow-up are defined as **0 AP / 0 mana, once-per-turn native skills**. They do NOT spawn separate monsters/characters or automatically fire; user selects the extra skill. Hero skill list is updated through the game's SkillController if available. Real gameplay needs confirmation.
- Existing game traits and **permanent vanilla perks**, with selection slots unlocked through meta upgrades, saved to JSON and applied via native AddTrait and PerkController to the protagonist only.
- Meta bonuses calculated across purchased ordinary + ascension ranks. Stat delta handling prevents reapplication of old stat bonuses after loading a game save.
- Original v0.4 profile data is kept. Existing node IDs are unchanged; existing trait slots and XP carry over.
- Existing night reward/event ledger and separate character-creator profile are retained.

## Not yet finished
Real-game QA is required to confirm skill XML parsing, native-perk activation, individual spell cooldowns/weapon-independent targeting, UI at different resolutions, safe portrait restoration and full 3-hero solo performance. Automatic boss/victory rewards, an actual one-hit resurrection mechanic (Последний бастион), and a dedicated teleport skill are **not implemented** yet. Echo attacks currently do not have separate spectral clones or custom VFX; they reuse available game effects.

## Download
[GitHub Actions — successful builds](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml): open the newest green run and download **EternalHero-v0.5** under Artifacts. The artifact contains `EternalHero-v0.5.zip`, to extract into the game directory.

## Documents
- [Installation and test checklist (RU)](docs/INSTALL.md)
- [Progression mechanics (RU)](docs/META_PROGRESSION.md)
- [Roadmap and known limitations (RU)](docs/ROADMAP.md)

Requires BepInEx 5 x64, The Last Spell, and TLS.ModKit for custom skill definitions. No game assets, DLLs or fonts are published in this repository.
