# Eternal Hero: Development Roadmap

## Requirements
- A recurring protagonist takes the first starting roster slot.
- Normal levels reset between runs; permanent upgrades do not.
- Any vanilla weapon is usable.
- Permanently stronger than normal recruits via extra stats, vanilla traits/perks and weapon-independent skills.

## Implemented and unit-tested
- [x] Profile saving, backups, existing save compatibility
- [x] Five metaprogression branches with 17 ranked upgrades
- [x] Dark game-like UI, tabs, RPG progress bar, optional in-game sprite reuse
- [x] Starting roster hook, best-effort capture/restore of identity properties
- [x] First-character one-time gift: +100 XP
- [x] Run reward ledger for night/boss/win/loss, duplicate protection (standalone tests)
- [x] Three XML skill definitions and Russian localization for optional TLS.ModKit
- [x] Diagnostics for game stat/trait/perk/skill methods, plus build CI

## Not completed, requires real game API validation
- [ ] Guarantee first hero identity/portrait on new runs and save reloads
- [ ] Bind earned meta-XP to actual night events, bosses and win/loss
- [ ] Apply meta stats safely without duplication
- [ ] Grant existing game traits and perks to protagonist only
- [ ] Bind three special skills to hotbar without weapon requirements
- [ ] True native Unity UI prefab integration; current UI is styled IMGUI with game sprite lookup
- [ ] Verify custom skill definitions parse in the installed ModKit
- [ ] Resolution and localization QA, compatibility with game updates

## What to provide for next integration
After loading a NEW RUN, submit BepInEx/LogOutput.log and a screenshot showing hero stats/portrait. Also note whether TLS.ModKit is installed. Do not upload proprietary game DLLs to the public repository.
