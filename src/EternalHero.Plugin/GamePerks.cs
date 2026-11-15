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

        // Uses the same PerkController/native-perk path as the game's existing Glyph perks.
        // Never unlocks the perks of companions.
        internal static string Grant(object hero, HeroProfile profile, ManualLogSource log)
        {
            if (hero == null || profile == null) return "no hero";
            var selected = profile.SelectedPerks;
            if (selected == null || selected.Count == 0) return "no permanent perks selected";
            var db = GameBridge.StaticGet(Database, "PerkDefinitions") as IDictionary;
            if (db == null) return "perk database unavailable";
            var type = AccessTools.TypeByName("TheLastStand.Controller.Unit.Perk.PerkController");
            if (type == null) return "PerkController unavailable";
            int slots = profile.Ranks != null && profile.Ranks.TryGetValue("legacy.perk", out int value) ? value : 0;
            if (slots <= 0) return "no unlocked permanent perk slots";
            var existing = GameBridge.Get(hero, "Perks") as IDictionary;
            int total = 0;
            var problems = new List<string>();
            foreach (string id in selected.Distinct().Take(Math.Min(8, slots)))
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
            var summary = "perks active/already available=" + total + "/" + Math.Min(slots,selected.Count);
            if (problems.Count > 0) summary += "; errors=" + string.Join(" | ",problems.Take(5).ToArray());
            return summary;
        }
    }
}
