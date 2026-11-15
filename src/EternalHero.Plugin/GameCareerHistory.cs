using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using EternalHero.Core;

namespace EternalHero.Plugin
{
    // The Last Spell uses LifetimeStats on PlayableUnit for the "Personal Achievements" page.
    // Accumulate the *maximum observed cumulative value* to avoid save/reload duplication.
    internal static class GameCareerHistory
    {
        private static readonly string[] ScalarNames = {
            "BestBlow", "CriticalHits", "DamagesBlocked", "DamagesInflicted",
            "DamagesMitigatedByResistance", "DamagesTakenOnArmor", "Dodges",
            "HealthLost", "JumpsOverWallUsed", "Kills", "IsolatedKills",
            "ManaSpent", "MostUnitsKilledInOneBlow", "PunchesUsed",
            "StunnedEnemies", "TilesCrossed"
        };
        private static readonly string[] MapNames = {
            "DamagesInflictedToEnemies", "DamagesTakenByEnemyType", "UsesPerWeapon"
        };

        private static object Stats(object hero) => GameBridge.Get(hero, "LifetimeStats");

        private static double ReadDouble(object value)
        {
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch { return 0; }
        }

        private static object CastNumber(double value, Type type)
        {
            if (type == typeof(int)) return Convert.ToInt32(Math.Min(int.MaxValue, Math.Max(0, value)));
            if (type == typeof(long)) return Convert.ToInt64(Math.Min(long.MaxValue, Math.Max(0, value)));
            if (type == typeof(float)) return Convert.ToSingle(Math.Min(float.MaxValue, Math.Max(0, value)));
            if (type == typeof(double)) return value;
            return null;
        }

        private static Type ScalarType(object obj, string name)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;
            return t.GetProperty(name, flags)?.PropertyType ?? t.GetField(name, flags)?.FieldType;
        }

        internal static bool Capture(object hero, HeroProfile profile)
        {
            object stats = Stats(hero);
            if (stats == null || profile == null) return false;
            bool changed = false;
            foreach (string name in ScalarNames)
            {
                object raw = GameBridge.Get(stats, name);
                if (raw == null) continue;
                changed |= CareerHistory.Merge(profile, name, ReadDouble(raw));
            }
            foreach (string name in MapNames)
            {
                if (!(GameBridge.Get(stats, name) is IDictionary dict)) continue;
                foreach (DictionaryEntry e in dict)
                {
                    if (e.Key is string id && e.Value != null)
                        changed |= CareerHistory.MergeDetail(profile, name, id, ReadDouble(e.Value));
                }
            }
            return changed;
        }

        internal static int ApplyToFreshHero(object hero, HeroProfile profile, ManualLogSource log)
        {
            object stats = Stats(hero);
            if (stats == null || profile == null) return 0;
            int changed = 0;
            foreach (string name in ScalarNames)
            {
                if (profile.CareerLifetimeStats == null ||
                    !profile.CareerLifetimeStats.TryGetValue(name, out double stored)) continue;
                object old = GameBridge.Get(stats, name);
                var type = ScalarType(stats, name);
                if (type == null || old == null || ReadDouble(old) >= stored) continue;
                object val = CastNumber(stored, type);
                if (val != null && GameBridge.Set(stats, name, val)) changed++;
            }
            foreach (string name in MapNames)
            {
                if (profile.CareerLifetimeDetails == null ||
                    !profile.CareerLifetimeDetails.TryGetValue(name, out var saved)) continue;
                if (!(GameBridge.Get(stats, name) is IDictionary dict)) continue;
                foreach (var kv in saved)
                {
                    try
                    {
                        object current = dict.Contains(kv.Key) ? dict[kv.Key] : null;
                        if (current != null && ReadDouble(current) >= kv.Value) continue;
                        // All lifetime dictionaries have string keys and int/float values.
                        Type numberType = name == "UsesPerWeapon" ? typeof(int) : typeof(float);
                        var cast = CastNumber(kv.Value, numberType);
                        if (cast != null) { dict[kv.Key] = cast; changed++; }
                    }
                    catch (Exception e) { log.LogWarning("Career map field " + name + "/" + kv.Key + ": " + e.Message); }
                }
            }
            log.LogInfo("Eternal Hero: restored " + changed + " permanent personal achievement fields");
            return changed;
        }

        internal static string Summary(HeroProfile profile)
        {
            if (profile?.CareerLifetimeStats == null) return "Статистика ещё не собрана";
            var dict = profile.CareerLifetimeStats;
            string[] keys = { "Kills", "DamagesInflicted", "CriticalHits", "Dodges", "BestBlow", "TilesCrossed" };
            var names = new[] { "Убийств", "Урона нанесено", "Критических ударов", "Уклонений", "Сильнейший удар", "Пройдено клеток" };
            var parts = new List<string>();
            for (int i = 0; i < keys.Length; i++)
                if (dict.TryGetValue(keys[i], out double value))
                    parts.Add(names[i] + ": " + value.ToString("0", CultureInfo.InvariantCulture));
            return parts.Count == 0 ? "Личных достижений пока нет" : string.Join("   •   ", parts.ToArray());
        }
    }
}
