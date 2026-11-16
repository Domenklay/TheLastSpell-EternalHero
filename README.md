# Eternal Hero — Вечный защитник для The Last Spell

[![Build](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml/badge.svg)](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml)

## v0.12 — боевые навыки и увеличенные награды

Обновление сохраняет профиль героя и улучшения из v0.10.

### Особые навыки

- Призрачный выстрел (без винтовки), Эфирный клинок (без ближнего оружия) и Пульс арканы (без посоха) теперь улучшаются при 3/5 рангах мастерства. Высшие версии заменяют младшие в NativeSkills.
- Второй призрачный выстрел, Эхо охотника и Эхо мага получают усиленные варианты II от соответствующих талантов. Каждое эхо бесплатно по AP и мане, раз за ход.
- «Эфирный шаг» открывается талантом Мгновенный переход: 1 AP, 4 маны, дальность до 6 клеток, один раз за ход. Использует штатный эффект Maneuver — перемещение на доступную клетку, а не сквозь любые стены.
- «Последний бастион» должен спасти от смертельного удара раз в игровую ночь и восстановить 30% HP. Использует отдельный журнал по ID забега и номеру ночи.
- Всего 16 SkillDefinition XML; все боевые навыки требуют установленный TLS.ModKit. В «Сведениях» добавлено состояние выданных навыков.
- Наведение курсора на черты теперь показывает описания без клика.

### Эссенция

Оставлено соотношение 100 XP = 1 эссенция, но увеличены награды и подключено окончание карты через GameController.TriggerGameOver:

| Событие | Опыт |
|---|---:|
| Ночь | 175 + min(125, 15 × номер ночи) XP |
| Победа на карте | 700 XP |
| Первая победа за карьеру | +400 XP |
| Победа над боссовой фазой | 400 XP |
| Поражение | 200 XP |
| Ручной отказ | 0 XP |

Для старых профилей при первом запуске компенсируется разница по подтверждённым прежним ночам. Профилю с уже пережитыми ночами, ранее зарегистрированными забегами и нулём зафиксированных побед начисляется дополнительно 600 XP как единовременная поддержка. Награды защищены от повторной выдачи. Успех и неудача учитываются раздельно.

**Статус:** сборка DLL, тесты экономики/Last Stand и 16 XML прошли через GitHub Actions. Реальную работу боевых умений и их анимаций нужно проверить в самой игре.

[Установка и проверка](docs/INSTALL.md) · [Roadmap](docs/ROADMAP.md) · [Сборки](https://github.com/Domenklay/TheLastSpell-EternalHero/actions/workflows/build.yml)


## v0.12 — доступная прокачка и один ZIP

- В Eternal Hero по умолчанию `FreeUpgradeTree = true`: **все 96 узлов и техники доступны для покупки в любом порядке**, но стоимость в эссенции и предел рангов сохраняются. Параметр `false` возвращает строгие цепочки талантов.
- «Дополнительное действие»: 4/5 эссенций вместо 10/11; «Второй темп»: 6/7. Первые техники без оружия: по 5 эссенций. Эхо и второй выстрел: 7. Эфирный шаг: 7.
- Если улучшение недоступно, кнопка показывает стоимость и остаток или конкретное требование; нажатие выводит подробную причину. Окно сразу обновляется после покупки и не сбрасывает прокрутку.
- При покупке максимума ОД/ОП мод дополнительно увеличивает текущие очки в ходе и записывает фактически разрешённое игровым API количество.
- **Больше нет ZIP внутри ZIP**: артефакт GitHub Actions напрямую содержит папку `BepInEx/` и `INSTALL.md`. Распаковывать только один раз.

Это исправление исходников и автоматической сборки; работу новых очков действий необходимо подтвердить внутри The Last Spell.


## v0.13 — weapon-independent skills shown on the game's skill bar

The Last Spell already supports **native hero skills** (such as Punch) alongside equipment skills. They are displayed on its standard `PlayableSkillBar` without using any item, consumable or inventory slot.

The previous integration placed custom skills in `PlayableUnit.NativeSkills` but **never invalidated the bar's cached buttons**, so the new skills could remain invisible after purchase. It also never retried if TLS.ModKit loaded SkillDefinitions later than the new roster.

Version 0.13 fixes both:
- Calls the original `PlayableSkillBar.Refresh(true)` and the character-sheet `RefreshSkills(hero)` after skill grants and upgrades.
- Every 6 seconds checks learned skills for a missing/obsolete rank, reattaches them once the TLS.ModKit database is ready, and refreshes the bar once the hero is selected.
- Uses the same game-native `SkillController(SkillDefinition, hero)` path as built-in native skills. **Never creates scroll items or uses inventory/equipment slots.**
- Shows in **Техники** the number of skills registered by TLS.ModKit, purchased by the hero, and successfully placed in `NativeSkills`.
- Adds a **ВЫДАТЬ УМЕНИЯ** button for immediate manual retry, without resetting ranks, spending essence or opening the inventory.
- Keeps upgrading skill tiers and restoring them on loaded saves.

**Important:** GitHub CI checks DLL compilation and XML structure, but cannot run the commercial game. Skill registrations and their action effects need an in-game test. If the counter shows `0/7` definitions, TLS.ModKit is missing, isn't loading the patch directory, or rejected the XML; inspect `BepInEx/LogOutput.log` for `[ModKit][Skills]`.
