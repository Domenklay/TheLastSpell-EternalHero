# Eternal Hero — Вечный защитник (The Last Spell)

[![Build](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml/badge.svg)](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml)

## v0.7 — persistent protagonist, selected traits and career statistics

Changes over v0.6:

- **Portrait persistence:** the native game's appearance-editor confirmation now captures the *first playable unit* even when the previous CreateStartUnits callback was missed. The saved portrait code's presence is displayed in the creator, and a redundant click on the already selected gender no longer deletes the stored code. Portrait restoration uses CodeGenerator + PortraitAPIManager and refreshes body parts, color palette, snapshot, original game unit portraits and character panel.
- **Permanent traits:** selected traits are added using AddTrait, including the explicitly chosen override when default randomly generated traits conflict. Selected traits are brought to the front of the native character's list because the native character sheet shows only its normal number of icons.
- **All perk specializations:** the protagonist can select any existing perk from the game's catalogue without the former 8-perk cap and without restrictions to the initially generated weapon specializations. The mod also attempts to unlock all five native perk tiers for this hero, without changing any other character. Standard perk points are still needed for purchases directly in the original game UI; the mod's specifically selected persistent perks are granted through the existing PerkController.
- **Career/personal achievements:** existing game's `PlayableUnit.LifetimeStats` counters and weapon/enemy dictionaries are captured into a separate persistent profile and restored to each NEW starting hero. Capturing the highest cumulative values prevents repeated save reloads from duplicating totals. The vanilla `Личные достижения` section should now show carried totals on subsequent runs.
- **Existing v0.6 meta progression and 96 upgrade nodes** remain unchanged. Automatic career checkpoints every 15 seconds, on quit, after nights, and before switching heroes.

These fixes require testing in the installed game. GitHub CI validates compilation, independent core save/reload, progression tests and XML syntax only. A game update or third-party mod can change the integration behavior.

## Downloads
[GitHub Actions Builds](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml) → latest successful run → artifact EternalHero-v0.7.

[Russian installation instructions](docs/INSTALL.md) · [Progression](docs/META_PROGRESSION.md)

No copyrighted game assets are shipped with the mod.
