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
        }

        internal static List<Option> Available()
        {
            var list = new List<Option>();
            var defs = GameBridge.StaticGet(Database, "PerkDefinitions") as IDictionary;
            if (defs == null) return list;
            foreach (DictionaryEntry entry in defs)
            {
                if (!(entry.Key is string id)) continue;
                string name = GameBridge.Get(entry.Value, "Name")?.ToString();
                list.Add(new Option { Id = id, Name = string.IsNullOrWhiteSpace(name) ? id : name });
            }
            return list.OrderBy(x => x.Name).ToList();
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
