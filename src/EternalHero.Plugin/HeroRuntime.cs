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
        private static readonly ConditionalWeakTable<object, object> Patched = new ConditionalWeakTable<object, object>();

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

        internal static int TryApplyBonuses(object hero, IReadOnlyDictionary<string, float> bonuses, ManualLogSource log)
        {
            if (hero == null || bonuses == null || bonuses.Count == 0) return 0;
            if (Patched.TryGetValue(hero, out _)) return 0; // avoid double-applying on same object
            int applied = 0;
            var controller = Member(hero, "UnitStatsController") ??
                             Member(hero, "StatsController") ??
                             Member(hero, "Stats") ?? hero;
            var method = controller.GetType().GetMethods(Flags)
                .FirstOrDefault(m => m.Name == "IncreaseBaseStat" &&
                    m.GetParameters().Length == 4 && m.GetParameters()[0].ParameterType.IsEnum
                    && m.GetParameters()[1].ParameterType == typeof(float) &&
                    m.GetParameters()[2].ParameterType == typeof(bool) &&
                    m.GetParameters()[3].ParameterType == typeof(bool));
            if (method == null)
            {
                log.LogWarning("No compatible IncreaseBaseStat on " + controller.GetType().FullName);
                return 0;
            }
            foreach (var kv in bonuses)
            {
                try
                {
                    var enumType = method.GetParameters()[0].ParameterType;
                    if (!Enum.IsDefined(enumType, kv.Key))
                    {
                        log.LogWarning("Missing game stat enum: " + kv.Key);
                        continue;
                    }
                    var gameStat = Enum.Parse(enumType, kv.Key);
                    method.Invoke(controller, new object[] { gameStat, kv.Value, false, true });
                    applied++;
                }
                catch (Exception e) { log.LogWarning("Stat " + kv.Key + " not applied: " + e.Message); }
            }
            if (applied > 0) Patched.Add(hero, new object());
            return applied;
        }
    }
}
