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
        private sealed class MetaPointsLedger { internal int RankApplied; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, MetaPointsLedger>
            MetaPoints = new System.Runtime.CompilerServices.ConditionalWeakTable<object, MetaPointsLedger>();

        private static int BonusRank(HeroProfile profile) =>
            profile?.Ranks != null && profile.Ranks.TryGetValue("legacy.perk", out int rank)
                ? Math.Max(0, Math.Min(8, rank)) : 0;

        internal static void PrimeLoadedPerkPoints(object hero, HeroProfile profile)
        {
            if (hero == null) return;
            var ledger = MetaPoints.GetValue(hero, _ => new MetaPointsLedger());
            // The game save already has the perk-point bonus from earlier ranks.
            ledger.RankApplied = BonusRank(profile);
        }

        internal static int ApplyMetaPerkPoints(object hero, HeroProfile profile, ManualLogSource log)
        {
            if (hero == null || profile == null) return 0;
            var ledger = MetaPoints.GetValue(hero, _ => new MetaPointsLedger());
            int rank = BonusRank(profile);
            int delta = Math.Max(0, rank - ledger.RankApplied);
            if (delta == 0) return 0;
            int before = AvailablePerkPoints(hero);
            if (!GameBridge.Set(hero, "PerksPoints", before + delta))
            {
                log.LogWarning("Eternal Hero: meta perk-point injection unavailable");
                return 0;
            }
            ledger.RankApplied = rank;
            log.LogInfo("Eternal Hero: granted "+delta+
                " additional original perk points from legacy.perk");
            return delta;
        }

        private const string Database = "TheLastStand.Database.Unit.PlayableUnitDatabase";
        private static readonly Dictionary<string,string> LastGrantFailures =
            new Dictionary<string,string>(StringComparer.Ordinal);
        internal static string LastFailure(string id) =>
            id != null && LastGrantFailures.TryGetValue(id,out string message) ? message : "";
        private static void NoteGrantFailure(string id,string error,ManualLogSource log)
        {
            LastGrantFailures[id]=error;
            log.LogWarning("Eternal Hero permanent perk "+id+": "+error);
        }
        public sealed class Option
        {
            public string Id;
            public string Name;
            public string Description;
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
                string description=null;
                try
                {
                    var method=entry.Value.GetType().GetMethods(
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public)
                        .FirstOrDefault(m=>m.Name=="GetDescription"&&
                            m.GetParameters().Length==1);
                    if(method!=null)
                        description=method.Invoke(entry.Value,new object[]{null})?.ToString();
                }
                catch { }
                if (!string.IsNullOrWhiteSpace(description))
                    description=System.Text.RegularExpressions.Regex.Replace(
                        description,"<[^>]*>","").Trim();
                var option = new Option {
                    Id = id, Name = string.IsNullOrWhiteSpace(name) ? id : name,
                    Description = string.IsNullOrWhiteSpace(description)
                        ? "Подробности доступны в стандартном описании перка игры."
                        : description
                };
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

        internal static UnityEngine.Sprite PerkIcon(string perkId)
        {
            if (string.IsNullOrWhiteSpace(perkId)) return null;
            try
            {
                var defs = GameBridge.StaticGet(Database, "PerkDefinitions") as IDictionary;
                if (defs != null && defs.Contains(perkId))
                    return GameBridge.Get(defs[perkId], "PerkSprite") as UnityEngine.Sprite;
            }
            catch { }
            return null;
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
            // Item-granted buffs are not a purchased perk. Count only a perk
            // explicitly unlocked by the playable character, not by equipment.
            var perk = dictionary[id];
            return GameBridge.Get(perk,"Unlocked") is bool unlocked && unlocked &&
                GameBridge.Get(perk, "IsUnlockedFromPlayableUnit") is bool value && value;
        }

        // The randomized six-column vanilla skill tree is NOT an authoritative
        // record of all learned perks. Native/off-tree perks live in hero.Perks and
        // have no PerkTier (and therefore no icon in that randomized tree).
        // A learned perk's effects are installed by PerkController.Unlock, which
        // calls Hook and its native perk modules.
        internal static string RuntimeState(object hero,string id)
        {
            if(hero==null) return "НЕТ ГЕРОЯ";
            var perks=GameBridge.Get(hero,"Perks") as IDictionary;
            if(perks==null || !perks.Contains(id)) return "НЕ ВЫДАН"+
                (string.IsNullOrWhiteSpace(LastFailure(id))?"":": "+LastFailure(id));
            var perk=perks[id];
            if(!IsLearned(hero,id)) return "НЕ АКТИВИРОВАН"+
                (string.IsNullOrWhiteSpace(LastFailure(id))?"":": "+LastFailure(id));
            return GameBridge.Get(perk,"PerkTier")==null
                ? "ВЫДАН • ВНЕ СЛУЧАЙНОГО ДЕРЕВА"
                : "ВЫДАН • В ДЕРЕВЕ";
        }

        internal static bool NeedsPermanentSync(object hero,HeroProfile profile)
        {
            return hero!=null && profile?.SelectedPerks!=null &&
                profile.SelectedPerks.Any(id=>!IsLearned(hero,id));
        }

        internal static string PermanentDiagnostic(object hero,HeroProfile profile)
        {
            var chosen=profile?.SelectedPerks?.Distinct().ToArray()??new string[0];
            int active=chosen.Count(id=>IsLearned(hero,id));
            return "Постоянные перки: выдано "+active+"/"+chosen.Length+
                (hero==null?" • начни забег":
                 active<chosen.Length?" • нажми «Применить», если нет эффекта":" • эффект зависит от условий");
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
                bool wasExisting = perks.Contains(id);
                if (wasExisting)
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
                    // Roll back only our playable-unit unlocker; never remove an
                    // existing item-granted perk or the vanilla tree's perk object.
                    var lockMethod=controller.GetType().GetMethods().FirstOrDefault(m =>
                        m.Name == "Lock" && m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType.IsInstanceOfType(hero));
                    try {
                        lockMethod?.Invoke(controller, new[] { hero });
                        if (!wasExisting && perks.Contains(id)) perks.Remove(id);
                    }
                    catch (Exception rollbackError) {
                        log.LogWarning("Eternal Hero perk rollback failed: " +
                            rollbackError.GetBaseException().Message);
                    }
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
                    if (existing != null && existing.Contains(id))
                    {
                        // A tree perk can already be instantiated but LOCKED.
                        // Previously the mere presence of its ID was mistaken for a
                        // successful permanent grant.
                        if (!IsLearned(hero, id))
                        {
                            var currentController = GameBridge.Get(existing[id], "PerkController");
                            var unlockExisting = currentController?.GetType().GetMethods()
                                .FirstOrDefault(m => m.Name == "Unlock" &&
                                    m.GetParameters().Length == 1 &&
                                    m.GetParameters()[0].ParameterType.IsInstanceOfType(hero));
                            if (unlockExisting != null)
                                unlockExisting.Invoke(currentController, new[] { hero });
                        }
                        if (IsLearned(hero, id)) total++;
                        else problems.Add(id + ": existing perk remained locked");
                        log.LogInfo("Eternal Hero permanent perk "+id+
                            ": "+RuntimeState(hero,id));
                        continue;
                    }
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
                    if (IsLearned(hero, id)) total++;
                    else problems.Add(id + ": unlocked state not confirmed");
                    if(IsLearned(hero,id)) LastGrantFailures.Remove(id);
                    else NoteGrantFailure(id,"Unlock completed but no owner unlocker. "+
                        "Perk definition or game UI rejected activation.",log);
                    log.LogInfo("Eternal Hero permanent perk "+id+
                        ": "+RuntimeState(hero,id));
                }
                catch (Exception e)
                {
                    string why=e.GetBaseException().Message;
                    problems.Add(id + ": " + why);
                    NoteGrantFailure(id,why,log);
                }
            }
            var summary = "perks active/already available=" + total + "/" + slots;
            if (problems.Count > 0) summary += "; errors=" + string.Join(" | ",problems.Take(5).ToArray());
            return summary;
        }
    }
}
