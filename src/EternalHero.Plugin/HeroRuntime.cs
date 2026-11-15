using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;

namespace EternalHero.Plugin
{
    // All game-facing reflection is isolated here. Fail closed on missing members.
    internal static class HeroRuntime
    {
        private static readonly BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly ConditionalWeakTable<object, Dictionary<string, float>> Patched =
            new ConditionalWeakTable<object, Dictionary<string, float>>();

        private static object Member(object instance, string name)
        {
            try
            {
                var type = instance.GetType();
                var property = type.GetProperty(name, Flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(instance, null);
                var field = type.GetField(name, Flags);
                return field?.GetValue(instance);
            }
            catch { return null; }
        }

        private static bool IsPlayableUnit(object value) =>
            value != null && value.GetType().Name.IndexOf("PlayableUnit", StringComparison.OrdinalIgnoreCase) >= 0
            && value.GetType().Name.IndexOf("Manager", StringComparison.OrdinalIgnoreCase) < 0;

        private static object FirstFrom(object value)
        {
            if (value == null || value is string) return null;
            if (IsPlayableUnit(value)) return value;
            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                    if (IsPlayableUnit(entry.Value)) return entry.Value;
            }
            if (value is IEnumerable sequence)
                foreach (object element in sequence)
                {
                    if (IsPlayableUnit(element)) return element;
                    var nested = element == null ? null : Member(element, "Value");
                    if (IsPlayableUnit(nested)) return nested;
                }
            return null;
        }

        internal static object TryGetFirstHero(object manager)
        {
            // Prefer explicitly named playable-unit roster collections, not arbitrary units/enemies.
            var names = new[] { "PlayableUnits", "PlayableUnitsById", "PlayableUnitsByTag",
                "StartingPlayableUnits", "AllPlayableUnits", "PlayableUnitControllers" };
            foreach (var name in names)
            {
                var unit = FirstFrom(Member(manager, name));
                if (unit != null) return unit;
            }
            // Conservative fallback: only inspect members with "playable" in their name.
            var type = manager.GetType();
            foreach (var member in type.GetMembers(Flags))
            {
                if (member.Name.IndexOf("playable", StringComparison.OrdinalIgnoreCase) < 0) continue;
                var unit = FirstFrom(Member(manager, member.Name));
                if (unit != null) return unit;
            }
            return null;
        }

        internal static string DiagnosticMembers(object manager)
        {
            try
            {
                return string.Join(", ", manager.GetType().GetMembers(Flags)
                    .Where(m => m.Name.IndexOf("unit", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(m => m.Name).Distinct().Take(35).ToArray());
            }
            catch { return "<unavailable>"; }
        }

        internal static bool TrySetHeroName(object hero, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (var propertyName in new[] { "Name", "UnitName", "CharacterName" })
            {
                try
                {
                    var property = hero.GetType().GetProperty(propertyName, Flags);
                    if (property != null && property.CanWrite && property.PropertyType == typeof(string))
                    {
                        property.SetValue(hero, name, null);
                        return true;
                    }
                }
                catch { /* Try next candidate without impacting the game. */ }
            }
            return false;
        }

        internal static string DiagnosticStatSurface(object hero)
        {
            if (hero == null) return "hero not found";
            try
            {
                var sources = StatSources(hero).ToArray();
                return string.Join(" | ", sources.Select(s =>
                {
                    var methods = s.GetType().GetMethods(Flags)
                        .Where(m => m.Name.IndexOf("Stat", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                    (m.Name.IndexOf("Increase", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     m.Name.IndexOf("Add", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     m.Name.IndexOf("Modify", StringComparison.OrdinalIgnoreCase) >= 0))
                        .Select(m => m.Name + "(" + string.Join(",",
                            m.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")")
                        .Distinct().Take(16);
                    return s.GetType().Name + ": " + string.Join("; ", methods.ToArray());
                }).ToArray());
            }
            catch (Exception e) { return "Diagnostic failed: " + e.Message; }
        }

        private static IEnumerable<object> StatSources(object hero)
        {
            string[] names = { "UnitStatsController", "StatsController", "Stats", "UnitStats",
                "UnitStatsData", "PlayableUnitStats", "Unit", "Controller" };
            var seen = new HashSet<object>(ReferenceComparer.Instance);
            foreach (var name in names)
            {
                object nested = Member(hero, name);
                if (nested != null && seen.Add(nested)) yield return nested;
            }
            if (seen.Add(hero)) yield return hero;
        }

        private static string GameStatName(string id)
        {
            // Use game XML stat IDs wherever they are known, fall back to original ID.
            switch (id)
            {
                case "Health": return "HealthTotal";
                case "Armor": return "ArmorTotal";
                case "MagicDamage": return "MagicalDamage";
                case "MovePoints": return "MovePointsTotal";
                case "ActionPoints": return "ActionPointsTotal";
                case "SkillRange": return "SkillRangeModifier";
                default: return id;
            }
        }

        // Vanilla save already includes the stat changes made in a previous session.
        // Prime delta tracking when resuming it: a subsequent new purchase applies ONLY its delta.
        internal static void PrimeFromLoadedSave(object hero, IReadOnlyDictionary<string, float> previous)
        {
            if (hero == null || previous == null) return;
            if (Patched.TryGetValue(hero, out _)) return;
            Patched.Add(hero, previous.ToDictionary(pair => pair.Key, pair => pair.Value));
        }

        // Conservative bridge. Refuses unknown stat API signatures and changes only named stats.
        // Per-hero delta tracking protects against double applying in the same runtime instance.
        internal static int TryApplyBonuses(object hero, IReadOnlyDictionary<string, float> bonuses, ManualLogSource log)
        {
            if (hero == null || bonuses == null || bonuses.Count == 0) return 0;
            if (!Patched.TryGetValue(hero, out var already))
            {
                already = new Dictionary<string, float>();
                Patched.Add(hero, already);
            }
            foreach (var controller in StatSources(hero))
            {
                var candidates = controller.GetType().GetMethods(Flags)
                    .Where(m => m.Name == "IncreaseBaseStat" || m.Name == "AddBaseStat")
                    .Where(m => m.GetParameters().Length == 4 &&
                         m.GetParameters()[0].ParameterType.IsEnum &&
                         (m.GetParameters()[1].ParameterType == typeof(float) ||
                          m.GetParameters()[1].ParameterType == typeof(int)) &&
                         m.GetParameters()[2].ParameterType == typeof(bool) &&
                         m.GetParameters()[3].ParameterType == typeof(bool))
                    .ToArray();
                if (candidates.Length != 1) continue; // no guesses on ambiguous overloads
                var method = candidates[0];
                var args = method.GetParameters();
                var enumType = args[0].ParameterType;
                int changed = 0;
                foreach (var kv in bonuses)
                {
                    try
                    {
                        string gameName = GameStatName(kv.Key);
                        if (!Enum.IsDefined(enumType, gameName))
                        {
                            log.LogWarning("Eternal Hero: game stat does not exist: " + gameName);
                            continue;
                        }
                        already.TryGetValue(kv.Key, out var applied);
                        float diff = kv.Value - applied;
                        if (diff <= .001f) continue;
                        object numeric = args[1].ParameterType == typeof(int) ?
                            (object)Convert.ToInt32(diff) : diff;
                        method.Invoke(controller, new object[] {
                            Enum.Parse(enumType, gameName), numeric, false, true
                        });
                        already[kv.Key] = kv.Value;
                        changed++;
                    }
                    catch (Exception e)
                    {
                        log.LogWarning("Eternal Hero: stat " + kv.Key +
                            " could not be applied: " + e.GetBaseException().Message);
                    }
                }
                log.LogInfo("Stat bridge: " + controller.GetType().FullName +
                    "." + method.Name + " / changed=" + changed);
                return changed;
            }
            log.LogWarning("Eternal Hero: compatible stat mutation API was not found: " +
                DiagnosticStatSurface(hero));
            return 0;
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
