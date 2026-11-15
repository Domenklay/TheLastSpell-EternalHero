using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using EternalHero.Core;
using HarmonyLib;

namespace EternalHero.Plugin
{
    internal static class GamePerks
    {
        private const string Database = "TheLastStand.Database.Unit.PlayableUnitDatabase";
        public sealed class Option
        {
            public string Id;
            public string Name;
            public string Collection = "Общие";
            public int Tier;
        }

        internal static List<Option> Available()
        {
            var list = new List<Option>();
            var defs = GameBridge.StaticGet(Database, "PerkDefinitions") as IDictionary;
            if (defs == null) return list;
            var byId = new Dictionary<string, Option>();
            foreach (DictionaryEntry entry in defs)
            {
                if (!(entry.Key is string id)) continue;
                string name = GameBridge.Get(entry.Value, "Name")?.ToString();
                var option = new Option { Id = id, Name = string.IsNullOrWhiteSpace(name) ? id : name };
                list.Add(option);
                byId[id] = option;
            }
            // Every vanilla perk can belong to one or more collections. Display the
            // first matching specialization and its actual tier, for search and clarity.
            var collections = GameBridge.StaticGet(Database, "UnitPerkCollectionDefinitions") as IDictionary;
            if (collections != null)
            {
                foreach (DictionaryEntry col in collections)
                {
                    string category = col.Key?.ToString() ?? "Общие";
                    var tiers = GameBridge.Get(col.Value, "PerksFromTier") as IDictionary;
                    if (tiers == null) continue;
                    foreach (DictionaryEntry tier in tiers)
                    {
                        int level = 0;
                        try { level = Convert.ToInt32(tier.Key); } catch { }
                        if (!(tier.Value is IEnumerable options)) continue;
                        foreach (object pair in options)
                        {
                            string id = GameBridge.Get(GameBridge.Get(pair, "Item1"), "Id")?.ToString();
                            if (id == null || !byId.TryGetValue(id, out Option option)) continue;
                            if (option.Tier == 0)
                            {
                                option.Collection = category;
                                option.Tier = level;
                            }
                        }
                    }
                }
            }
            return list.OrderBy(x => x.Collection).ThenBy(x => x.Tier).ThenBy(x => x.Name).ToList();
        }

        internal static void Install(Harmony harmony, ManualLogSource log)
        {
            try
            {
                // The original game only sets UnitPerkTier.Available after previous
                // tier purchases. For this one hero, expose every tier as accessible.
                var type=AccessTools.TypeByName("TheLastStand.Model.Unit.Perk.UnitPerkTier");
                var getter=type?.GetProperty("Available")?.GetGetMethod();
                var postfix=AccessTools.Method(typeof(GamePerks),nameof(AvailableForProtagonist));
                if(getter!=null && postfix!=null)
                {
                    harmony.Patch(getter,postfix:new HarmonyMethod(postfix));
                    log.LogInfo("Eternal Hero: all vanilla perk tiers accessible to protagonist");
                }
                else log.LogWarning("Eternal Hero: UnitPerkTier.Available getter not found");
            }
            catch(Exception ex){ log.LogWarning("Eternal Hero perk tier Harmony patch: "+ex); }
        }

        private static void AvailableForProtagonist(object __instance, ref bool __result)
        {
            if (__result || Plugin.Instance?.CurrentHero == null ||
                Plugin.Instance.Profile?.UnlockAllPerkTiers != true) return;
            var tree=GameBridge.Get(__instance,"UnitPerkTree");
            var owner=GameBridge.Get(tree,"PlayableUnit");
            if(ReferenceEquals(owner,Plugin.Instance.CurrentHero)) __result=true;
        }

        internal static int AvailablePerkPoints(object hero)
        {
            try { return Convert.ToInt32(GameBridge.Get(hero, "PerksPoints") ?? 0); }
            catch { return 0; }
        }

        internal static bool IsLearned(object hero, string id)
        {
            if (hero == null || string.IsNullOrWhiteSpace(id)) return false;
            var dictionary = GameBridge.Get(hero, "Perks") as IDictionary;
            if (dictionary == null || !dictionary.Contains(id)) return false;
            return GameBridge.Get(dictionary[id], "Unlocked") is bool value && value;
        }

        // This works with the game's original PerkController, but does not add new columns
        // to the vanilla six-column view. It purchases any *existing* PerkDefinition
        // for one ordinary perk point and saves it as a dynamic perk in the game's save.
        internal static bool PurchaseAnyPerk(object hero, string id, ManualLogSource log,
            out string error)
        {
            error = null;
            if (hero == null || Plugin.Instance == null ||
                !ReferenceEquals(hero, Plugin.Instance.CurrentHero))
            {
                error = "Главный герой не найден в текущем забеге";
                return false;
            }
            if (string.IsNullOrWhiteSpace(id)) { error = "Перк не выбран"; return false; }
            var db = GameBridge.StaticGet(Database, "PerkDefinitions") as IDictionary;
            if (db == null || !db.Contains(id))
            { error = "Перка нет в базе данных игры"; return false; }
            int points = AvailablePerkPoints(hero);
            if (points <= 0) { error = "Недостаточно очков особых черт"; return false; }
            var perks = GameBridge.Get(hero, "Perks") as IDictionary;
            if (perks == null) { error = "Не обнаружен игровой список перков"; return false; }
            if (IsLearned(hero, id)) { error = "Перк уже изучен"; return false; }

            try
            {
                object controller;
                if (perks.Contains(id))
                {
                    controller = GameBridge.Get(perks[id], "PerkController");
                    if (controller == null) { error = "Нет контроллера существующего перка"; return false; }
                }
                else
                {
                    var type = AccessTools.TypeByName("TheLastStand.Controller.Unit.Perk.PerkController");
                    object definition = db[id];
                    var constructor = type?.GetConstructors().FirstOrDefault(c =>
                    {
                        var parameters = c.GetParameters();
                        return parameters.Length == 7 &&
                            parameters[0].ParameterType.IsInstanceOfType(definition) &&
                            parameters[2].ParameterType.IsInstanceOfType(hero) &&
                            parameters[4].ParameterType == typeof(string) &&
                            parameters[5].ParameterType == typeof(bool) &&
                            parameters[6].ParameterType == typeof(bool);
                    });
                    if (constructor == null)
                    { error = "Неподдерживаемая версия игрового PerkController"; return false; }
                    // isNative = false and isFromRace = false: the perk is serialized by
                    // PlayableUnit.Serialize() as a dynamic perk, not as an original tier.
                    controller = constructor.Invoke(new object[] {
                        definition, null, hero, null, string.Empty, false, false
                    });
                    if (GameBridge.Get(controller, "Perk") == null)
                    { error = "Игра не создала перк"; return false; }
                }

                var method = controller.GetType().GetMethods().FirstOrDefault(m => {
                    var args = m.GetParameters();
                    return m.Name == "Unlock" && args.Length == 1 &&
                        args[0].ParameterType.IsInstanceOfType(hero);
                });
                if (method == null) { error = "Метод разблокировки недоступен"; return false; }
                method.Invoke(controller, new[] { hero });

                // Charge the point only after the game's own controller has confirmed
                // the perk is unlocked, so failed attempts do not spend currency.
                if (!IsLearned(hero, id))
                { error = "Игра отказалась активировать перк"; return false; }
                if (!GameBridge.Set(hero, "PerksPoints", points - 1))
                {
                    // Fail closed when point storage changed: reverse the unlock we made.
                    var lockWithoutArgs=controller.GetType().GetMethods().FirstOrDefault(m =>
                        m.Name == "LockAndClearUnlockers" && m.GetParameters().Length == 1);
                    try { lockWithoutArgs?.Invoke(controller, new object[] { true }); }
                    catch { }
                    error = "Не удалось списать очко — покупка отменена";
                    return false;
                }
                log.LogInfo("Eternal Hero: purchased off-tree perk " + id +
                    " for 1 original perk point; remaining " + (points - 1));
                GameBridge.RefreshHeroPresentation(hero, log);
                return true;
            }
            catch (Exception e)
            {
                error = "Ошибка игрового API: " + e.GetBaseException().Message;
                log.LogWarning("Eternal Hero off-tree perk " + id + ": " + e);
                return false;
            }
        }

        // Uses the same PerkController/native-perk path as the game's existing Glyph perks.
        // Never unlocks the perks of companions.
        internal static bool UnlockAllTiers(object hero, HeroProfile profile, ManualLogSource log)
        {
            if (hero == null || profile == null || !profile.UnlockAllPerkTiers) return false;
            try
            {
                // The Last Spell's controller already provides a tier-unlock operation.
                object tree = GameBridge.Get(hero, "PerkTree");
                object controller = GameBridge.Get(tree, "UnitPerkTreeController");
                if (controller == null) return false;
                var method = controller.GetType().GetMethod("DebugUpdateAllTiersAvailability",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (method == null) return false;
                method.Invoke(controller, null);
                return true;
            }
            catch (Exception e)
            {
                log.LogWarning("Eternal Hero unlock all perk tiers failed: " + e.GetBaseException().Message);
                return false;
            }
        }

        internal static string Grant(object hero, HeroProfile profile, ManualLogSource log)
        {
            if (hero == null || profile == null) return "no hero";
            UnlockAllTiers(hero, profile, log);
            var selected = profile.SelectedPerks;
            if (selected == null || selected.Count == 0) return "no permanent perks selected";
            var db = GameBridge.StaticGet(Database, "PerkDefinitions") as IDictionary;
            if (db == null) return "perk database unavailable";
            var type = AccessTools.TypeByName("TheLastStand.Controller.Unit.Perk.PerkController");
            if (type == null) return "PerkController unavailable";
            // The user explicitly chose an unrestricted permanent protagonist.
            // All vanilla perk definitions may be selected, not merely the usual three
            // weapon-focused choices. Selection still requires user action.
            int slots = selected.Count;
            var existing = GameBridge.Get(hero, "Perks") as IDictionary;
            int total = 0;
            var problems = new List<string>();
            foreach (string id in selected.Distinct())
            {
                if (!db.Contains(id)) { problems.Add(id + ": definition missing"); continue; }
                try
                {
                    if (existing != null && existing.Contains(id)) { total++; continue; }
                    object definition = db[id];
                    var constructor = type.GetConstructors().FirstOrDefault(c =>
                    {
                        var args = c.GetParameters();
                        return args.Length == 7 &&
                            args[0].ParameterType.IsInstanceOfType(definition) &&
                            args[2].ParameterType.IsInstanceOfType(hero) &&
                            args[4].ParameterType == typeof(string) &&
                            args[5].ParameterType == typeof(bool) &&
                            args[6].ParameterType == typeof(bool);
                    });
                    if (constructor == null) { problems.Add(id + ": constructor unavailable"); continue; }
                    var controller = constructor.Invoke(new object[] {
                        definition, null, hero, null, string.Empty, true, false
                    });
                    var perk = GameBridge.Get(controller, "Perk");
                    if (perk == null) { problems.Add(id + ": new perk missing"); continue; }
                    var unlock = type.GetMethods().FirstOrDefault(m =>
                    {
                        var args = m.GetParameters();
                        return m.Name == "Unlock" && args.Length == 1 &&
                            args[0].ParameterType.IsInstanceOfType(hero);
                    });
                    if (unlock == null) { problems.Add(id + ": unlock unavailable"); continue; }
                    unlock.Invoke(controller, new[] { hero });
                    total++;
                }
                catch (Exception e)
                {
                    problems.Add(id + ": " + e.GetBaseException().Message);
                    log.LogWarning("Eternal Hero permanent perk " + id + ": " + e.GetBaseException().Message);
                }
            }
            var summary = "perks active/already available=" + total + "/" + slots;
            if (problems.Count > 0) summary += "; errors=" + string.Join(" | ",problems.Take(5).ToArray());
            return summary;
        }
    }
}
