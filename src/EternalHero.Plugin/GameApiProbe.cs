using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace EternalHero.Plugin
{
    internal static class GameApiProbe
    {
        private const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        public static string SkillDefinitions()
        {
            try
            {
                var db = AccessTools.TypeByName("TheLastStand.Database.Unit.SkillDatabase");
                if (db == null) return "SkillDatabase class unavailable";
                var property = db.GetProperty("SkillDefinitions", Static);
                var field = db.GetField("SkillDefinitions", Static);
                object value = property?.GetValue(null, null) ?? field?.GetValue(null);
                if (!(value is IDictionary dictionary)) return "Skill definitions are not loaded or dictionary unavailable";
                string[] expected = { "EternalGhostShot", "EternalEtherSlash", "EternalArcanePulse" };
                return "Custom skills loaded from ModKit: " +
                    string.Join(", ", expected.Select(id => id + "=" + dictionary.Contains(id)).ToArray());
            }
            catch (Exception e) { return "SkillDatabase inspection failed: " + e.GetBaseException().Message; }
        }

        public static string HeroAbilityMethods(object hero)
        {
            if (hero == null) return "hero is missing";
            try
            {
                var candidates = new[] {
                    hero,
                    Read(hero, "PlayableUnitController"), Read(hero, "SkillController"),
                    Read(hero, "Skills"), Read(hero, "UnitSkillController"),
                    Read(hero, "PerkController"), Read(hero, "TraitController"),
                    Read(hero, "UnitData"), Read(hero, "Controller")
                }.Where(x => x != null).Distinct().Take(9);
                return string.Join(" | ", candidates.Select(x => {
                    string[] names = { "Skill", "Trait", "Perk" };
                    var methods = x.GetType().GetMethods(Instance)
                        .Where(m => names.Any(n => m.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
                        .Where(m => m.Name.StartsWith("Add", StringComparison.OrdinalIgnoreCase) ||
                                    m.Name.StartsWith("Learn", StringComparison.OrdinalIgnoreCase) ||
                                    m.Name.StartsWith("Unlock", StringComparison.OrdinalIgnoreCase) ||
                                    m.Name.StartsWith("Give", StringComparison.OrdinalIgnoreCase) ||
                                    m.Name.StartsWith("Grant", StringComparison.OrdinalIgnoreCase))
                        .Select(m => m.Name + "(" +
                            string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")")
                        .Distinct().Take(12).ToArray();
                    return x.GetType().Name + "=" + string.Join("; ", methods);
                }).ToArray());
            }
            catch (Exception e) { return "Hero abilities probe failed: " + e.GetBaseException().Message; }
        }

        private static object Read(object target, string name)
        {
            try
            {
                var p = target.GetType().GetProperty(name, Instance);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(target, null);
                return target.GetType().GetField(name, Instance)?.GetValue(target);
            }
            catch { return null; }
        }
    }
}
