using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using EternalHero.Core;
using HarmonyLib;

namespace EternalHero.Plugin
{
    internal static class GameBridge
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string DatabaseType = "TheLastStand.Database.Unit.PlayableUnitDatabase";

        public static object Get(object obj, string name)
        {
            if (obj == null) return null;
            try
            {
                var p = obj.GetType().GetProperty(name, Instance);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(obj, null);
                return obj.GetType().GetField(name, Instance)?.GetValue(obj);
            }
            catch { return null; }
        }

        public static bool Set(object obj, string name, object value)
        {
            if (obj == null) return false;
            try
            {
                var p = obj.GetType().GetProperty(name, Instance);
                if (p != null && p.CanWrite && p.GetIndexParameters().Length == 0)
                { p.SetValue(obj, value, null); return true; }
                var f = obj.GetType().GetField(name, Instance);
                if (f != null && !f.IsInitOnly) { f.SetValue(obj, value); return true; }
            }
            catch { }
            return false;
        }

        public static object StaticGet(string typeName, string name)
        {
            try
            {
                var t = AccessTools.TypeByName(typeName);
                return t?.GetProperty(name, Static)?.GetValue(null, null) ??
                    t?.GetField(name, Static)?.GetValue(null);
            }
            catch { return null; }
        }

        public static object Singleton(string typeName)
        {
            try
            {
                var t = AccessTools.TypeByName(typeName);
                var generic = AccessTools.TypeByName("TPLib.TPSingleton" + ((char)96) + "1");
                if (t != null && generic != null)
                    return generic.MakeGenericType(t).GetProperty("Instance", Static)?.GetValue(null, null);
                return t?.GetProperty("Instance", Static | BindingFlags.FlattenHierarchy)?.GetValue(null, null);
            }
            catch { return null; }
        }

        public static object FirstHero()
        {
            var mgr = Singleton("TheLastStand.Manager.Unit.PlayableUnitManager");
            var units = Get(mgr, "PlayableUnits") as IEnumerable;
            if (units != null) foreach (object hero in units) if (hero != null) return hero;
            return null;
        }

        public static List<string> Races()
        {
            var result = new List<string>();
            try
            {
                var manager = AccessTools.TypeByName("TheLastStand.Manager.Unit.PlayableUnitManager");
                var allowed = manager?.GetMethod("GetAvailableRacesIds", Static)
                    ?.Invoke(null, new object[] { false }) as IEnumerable;
                if (allowed != null)
                    foreach (object race in allowed) if (race is string s) result.Add(s);
            }
            catch { }
            if (result.Count > 0) return result.Distinct().ToList();
            var defs = StaticGet(DatabaseType, "RaceDefinitions") as IDictionary;
            if (defs != null) foreach (DictionaryEntry e in defs) if (e.Key is string s) result.Add(s);
            if (result.Count == 0) result.Add("Human");
            return result;
        }

        public sealed class TraitOption
        {
            public string Id;
            public string Name;
            public int Cost;
            public string Description = "";
            public List<string> ForbiddenRaces = new List<string>();
        }

        public static string RaceName(string id)
        {
            var definitions = StaticGet(DatabaseType, "RaceDefinitions") as IDictionary;
            if (id != null && definitions != null && definitions.Contains(id))
            {
                var localized = Get(definitions[id], "Name")?.ToString();
                if (!string.IsNullOrWhiteSpace(localized)) return localized;
            }
            return id ?? "";
        }

        public static List<TraitOption> Traits()
        {
            var list = new List<TraitOption>();
            var defs = StaticGet(DatabaseType, "UnitTraitDefinitions") as IDictionary;
            if (defs == null) return list;
            foreach (DictionaryEntry e in defs)
            {
                if (!(e.Key is string id)) continue;
                string localizedName = Get(e.Value, "Name")?.ToString();
                var trait = new TraitOption { Id = id,
                    Name = string.IsNullOrWhiteSpace(localizedName) ? id : localizedName };
                try { trait.Cost = Convert.ToInt32(Get(e.Value, "Cost") ?? 0); } catch { }
                var modifiers = Get(e.Value, "StatModifiers") as IEnumerable;
                if (modifiers != null)
                {
                    var statDescriptions = new List<string>();
                    foreach (object modifier in modifiers)
                    {
                        var stat = Get(modifier, "Stat")?.ToString();
                        var value = Get(modifier, "Value");
                        if (stat != null && value != null)
                            statDescriptions.Add(stat + " " + value);
                    }
                    trait.Description = string.Join(", ", statDescriptions.Take(3).ToArray());
                }
                var forbidden = Get(e.Value, "RaceIncompatibilities") as IEnumerable;
                if (forbidden != null)
                    foreach (object race in forbidden) if (race != null) trait.ForbiddenRaces.Add(race.ToString());
                list.Add(trait);
            }
            return list.OrderBy(t => t.Cost).ThenBy(t => t.Id).ToList();
        }

        private static object Call(object obj, string methodName, params object[] args)
        {
            if (obj == null) return null;
            foreach (var m in obj.GetType().GetMethods(Instance).Where(x => x.Name == methodName
                && x.GetParameters().Length == args.Length))
            {
                var parameters = m.GetParameters();
                if (parameters.Where((p, i) => args[i] != null &&
                    !p.ParameterType.IsInstanceOfType(args[i])).Any()) continue;
                return m.Invoke(obj, args);
            }
            return null;
        }

        public static string Apply(object hero, HeroProfile profile, ManualLogSource log)
        {
            if (hero == null) return "Hero not found";
            var status = new List<string>();
            if (Set(hero, "PlayableUnitName", profile.HeroName)) status.Add("имя");
            var races = StaticGet(DatabaseType, "RaceDefinitions") as IDictionary;
            if (!string.IsNullOrWhiteSpace(profile.HeroRaceId) && races != null &&
                races.Contains(profile.HeroRaceId) && Races().Contains(profile.HeroRaceId))
            {
                object previous = Get(hero, "RaceDefinition");
                object next = races[profile.HeroRaceId];
                if (Get(previous, "Id")?.ToString() != profile.HeroRaceId)
                {
                    var stats = Get(hero, "PlayableUnitStatsController");
                    try
                    {
                        if (previous != null) Call(stats, "OnRaceRemoved", previous);
                        Set(hero, "RaceDefinition", next);
                        Call(stats, "OnRaceGenerated", next);
                        Call(Get(hero, "PlayableUnitView"), "InitVisuals", false);
                        status.Add("раса: " + profile.HeroRaceId);
                    }
                    catch (Exception e) { log.LogWarning("Eternal Hero race: " + e.GetBaseException().Message); }
                }
            }

            if (profile.HeroGender == "Male" || profile.HeroGender == "Female")
            {
                if (Set(hero, "Gender", profile.HeroGender))
                    status.Add("пол: " + profile.HeroGender);
            }

            if (!string.IsNullOrWhiteSpace(profile.PortraitCode))
            {
                if (HeroPortrait.TryApply(hero, profile, log)) status.Add("портрет восстановлен");
                else status.Add("портрет пока не применён");
            }

            var existing = Get(hero, "UnitTraitDefinitions") as IEnumerable;
            int max = Math.Min(4, 1 + (profile.Ranks != null &&
                profile.Ranks.TryGetValue("legacy.trait", out int ranks) ? ranks : 0));
            int added = 0;
            foreach (var id in (profile.SelectedTraits ?? new List<string>()).Distinct().Take(max))
            {
                if (!Traits().Any(x => x.Id == id &&
                    !x.ForbiddenRaces.Contains(profile.HeroRaceId))) continue;
                bool exists = false;
                if (existing != null) foreach (object trait in existing)
                    if (Get(trait, "Id")?.ToString() == id) exists = true;
                if (exists) { added++; continue; }
                try
                {
                    var result = Call(Get(hero, "PlayableUnitController"), "AddTrait", id, false);
                    if (result is bool ok && ok) added++;
                }
                catch (Exception e) { log.LogWarning("Eternal Hero AddTrait(" + id + "): " + e.GetBaseException().Message); }
            }
            status.Add("черты: " + added);
            return string.Join("; ", status.ToArray());
        }

        public static bool OpenNativeEditor(object hero, ManualLogSource log)
        {
            if (hero == null) return false;
            try
            {
                var editor = Singleton("TheLastStand.View.PlayableUnitCustomisation.PlayableUnitCustomisationPanel");
                if (editor == null) return false;
                Call(editor, "Open", hero);
                return true;
            }
            catch (Exception e)
            { log.LogWarning("Native customization editor error: " + e); return false; }
        }

        public static void CaptureEditorResult(object hero, HeroProfile profile)
        {
            if (hero == null) return;
            var value = Get(hero, "PortraitCodeData");
            if (value != null) profile.PortraitCode = value.ToString();
            profile.HeroFaceId = Get(hero, "FaceId")?.ToString() ?? profile.HeroFaceId;
            profile.HeroGender = Get(hero, "Gender")?.ToString() ?? profile.HeroGender;
            profile.HeroName = Get(hero, "PlayableUnitName")?.ToString() ?? profile.HeroName;
        }
    }
}
