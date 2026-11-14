# The Last Spell — Eternal Hero / Вечный защитник

[![Build](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml/badge.svg)](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml)

**v0.1.1 technical prototype** for BepInEx 5 x64. The goal is one recurring protagonist with an independent, persistent metaprogression tree.

## Current working components
- Game-independent progression engine: five branches, 17 upgrades, rank limits and prerequisites.
- Purchases, experience → essence conversion, duplicate-reward prevention.
- Local JSON profile + backup on subsequent saves.
- In-game progression panel opens automatically on startup; top-right OPEN/CLOSE button, F8/F9/Home shortcuts; optional debug XP button.
- Harmony hook for `PlayableUnitManager.CreateStartUnits` and diagnostic attempt to find the first starting character.
- Automated GitHub Actions tests and compilation of `EternalHero.Core.dll` and `EternalHero.Plugin.dll`.

**Not yet implemented/verified:** automatic experience from the game, reliable fixed identity across runs, safe automatic application of stat bonuses, persistent perks/traits and weapon-independent active combat abilities. The runtime bridge is experimental and stat injection is OFF by default.

## Download / install
[Latest successful build](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml) → open run → **Artifacts → EternalHero-prototype**. The downloaded artifact includes an installable ZIP with both DLLs. GitHub may require signing in to download workflow artifacts.

See [Russian installation instructions](docs/INSTALL.md) and [feature roadmap](docs/ROADMAP.md).

## Building
- .NET SDK 8, NuGet restore (the BepInEx feed is configured in `NuGet.Config`).
- `dotnet run --project tests/EternalHero.Tests/EternalHero.Tests.csproj -c Release`
- `dotnet build src/EternalHero.Plugin/EternalHero.Plugin.csproj -c Release`

The project does not publish proprietary game DLLs or redistribute third-party mod assets.
