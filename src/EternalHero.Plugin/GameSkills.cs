using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using EternalHero.Core;
using HarmonyLib;

namespace EternalHero.Plugin
{
    internal static class GameSkills
    {
        private static readonly Dictionary<string,string> Ids = new Dictionary<string,string> {
            { "forbidden.ghostshot", "EternalGhostShot" },
            { "forbidden.etherslash", "EternalEtherSlash" },
            { "forbidden.arcaneburst", "EternalArcanePulse" }
        };

        public static string Grant(object hero, HeroProfile profile, ManualLogSource log)
        {
            if (hero == null || profile == null) return "hero missing";
            var database = GameBridge.StaticGet("TheLastStand.Database.Unit.SkillDatabase", "SkillDefinitions")
                as IDictionary;
            if (database == null) return "ModKit skill database not ready";
            var native = GameBridge.Get(hero, "NativeSkills") as IList;
            if (native == null) return "NativeSkills collection missing";
            var controllerType = AccessTools.TypeByName("TheLastStand.Controller.Skill.SkillController");
            if (controllerType == null) return "SkillController missing";
            var added = new List<string>();
            foreach (var pair in Ids)
            {
                if (profile.Ranks == null || !profile.Ranks.TryGetValue(pair.Key, out int rank) ||
                    rank == 0 || !database.Contains(pair.Value)) continue;
                bool already = false;
                foreach (object entry in native)
                    if (GameBridge.Get(GameBridge.Get(entry, "SkillDefinition"), "Id")?.ToString() == pair.Value)
                    { already = true; break; }
                if (already) continue;
                try
                {
                    object definition = database[pair.Value];
                    var constructor = controllerType.GetConstructors().FirstOrDefault(c => {
                        var args = c.GetParameters();
                        return args.Length == 4 && args[0].ParameterType.IsInstanceOfType(definition) &&
                            args[1].ParameterType.IsInstanceOfType(hero);
                    });
                    if (constructor == null) { log.LogWarning("Eternal Hero: skill constructor unavailable"); continue; }
                    var ctrl = constructor.Invoke(new object[] { definition, hero, -1, -1 });
                    object skill = GameBridge.Get(ctrl, "Skill");
                    if (skill == null) continue;
                    native.Add(skill);
                    added.Add(pair.Value);
                }
                catch (Exception ex) { log.LogWarning("Eternal Hero skill " + pair.Value + " error: " + ex.GetBaseException().Message); }
            }
            return added.Count > 0 ? "Granted skills: " + string.Join(", ",added) : "No new skills to add (check ModKit and purchased upgrades)";
        }
    }
}
