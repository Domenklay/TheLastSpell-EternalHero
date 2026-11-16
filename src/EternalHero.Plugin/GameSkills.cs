using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using EternalHero.Core;
using HarmonyLib;

namespace EternalHero.Plugin
{
    // New skill definitions are registered by TLS.ModKit. This bridge only equips
    // the correct tier onto the first hero and never edits other playable units.
    internal static class GameSkills
    {
        private sealed class SkillTrack
        {
            internal readonly string Unlock, BaseId, Mastery;
            internal readonly int TierThree, TierFive;
            internal SkillTrack(string unlock, string baseId, string mastery=null,
                int tierThree=0, int tierFive=0)
            {
                Unlock=unlock; BaseId=baseId; Mastery=mastery;
                TierThree=tierThree; TierFive=tierFive;
            }
            internal string Choose(HeroProfile p)
            {
                int master=Rank(p,Mastery);
                if(TierFive>0 && master>=TierFive) return BaseId+"V";
                if(TierThree>0 && master>=TierThree) return BaseId+"III";
                if(TierThree==4 && master>=TierThree) return BaseId+"II";
                return BaseId;
            }
            internal bool Belongs(string id) =>
                id==BaseId || id==BaseId+"III" || id==BaseId+"V" || id==BaseId+"II";
        }

        private static readonly SkillTrack[] Tracks = {
            new SkillTrack("forbidden.ghostshot","EternalGhostShot","forbidden.ghostmastery",3,5),
            new SkillTrack("forbidden.etherslash","EternalEtherSlash","forbidden.ethermastery",3,5),
            new SkillTrack("forbidden.arcaneburst","EternalArcanePulse","forbidden.arcanemastery",3,5),
            new SkillTrack("forbidden.secondshot","EternalSecondShot","forbidden.ghostmastery",4),
            new SkillTrack("trinity.hunter","EternalEchoHunter","trinity.precision",4),
            new SkillTrack("trinity.mage","EternalEchoMage","trinity.arcana",4),
            new SkillTrack("tactics.teleport","EternalTeleport")
        };
        private static int Rank(HeroProfile p,string id)
        {
            if(string.IsNullOrWhiteSpace(id)||p?.Ranks==null) return 0;
            return p.Ranks.TryGetValue(id,out int rank)?rank:0;
        }

        internal static string Grant(object hero, HeroProfile profile, ManualLogSource log)
        {
            if(hero==null||profile==null) return "hero missing";
            var db=GameBridge.StaticGet("TheLastStand.Database.Unit.SkillDatabase","SkillDefinitions")
                as IDictionary;
            if(db==null) return "ModKit skill database not ready";
            var native=GameBridge.Get(hero,"NativeSkills") as IList;
            if(native==null) return "NativeSkills collection missing";
            var controllerType=AccessTools.TypeByName("TheLastStand.Controller.Skill.SkillController");
            if(controllerType==null) return "SkillController missing";
            int equipped=0,removed=0,missing=0;
            foreach(var track in Tracks)
            {
                bool unlocked=Rank(profile,track.Unlock)>0;
                string id=track.Choose(profile);
                // If a higher-rank XML definition is absent, preserve the basic
                // ability rather than dropping the skill entirely.
                if(unlocked && !db.Contains(id) && db.Contains(track.BaseId))
                    id=track.BaseId;
                if(unlocked && !db.Contains(id))
                {
                    log.LogWarning("Eternal Hero: ModKit definition missing "+id+
                        ", check BepInEx/LogOutput.log for [ModKit] errors");
                    missing++;
                    continue;
                }
                bool present=false;
                for(int i=native.Count-1;i>=0;i--)
                {
                    string oldId=GameBridge.Get(GameBridge.Get(native[i],"SkillDefinition"),"Id")?.ToString();
                    if(!track.Belongs(oldId)) continue;
                    if(unlocked && oldId==id) {present=true; continue;}
                    native.RemoveAt(i);
                    removed++;
                }
                if(!unlocked||present) continue;
                try
                {
                    object def=db[id];
                    var constructor=controllerType.GetConstructors().FirstOrDefault(c=>{
                        var a=c.GetParameters();
                        return a.Length==4 && a[0].ParameterType.IsInstanceOfType(def) &&
                            a[1].ParameterType.IsInstanceOfType(hero);
                    });
                    if(constructor==null)
                    {
                        log.LogWarning("Eternal Hero: compatible SkillController constructor unavailable");
                        missing++;
                        continue;
                    }
                    object controller=constructor.Invoke(new object[]{def,hero,-1,-1});
                    object skill=GameBridge.Get(controller,"Skill");
                    if(skill==null) { missing++; continue; }
                    native.Add(skill);
                    equipped++;
                    log.LogInfo("Eternal Hero native combat skill equipped: "+id+
                        " (unlock="+track.Unlock+")");
                }
                catch(Exception ex)
                {
                    log.LogWarning("Eternal Hero combat skill "+id+": "+ex.GetBaseException().Message);
                    missing++;
                }
            }
            return "equipped="+equipped+", upgraded/removed="+removed+
                ", missing definitions="+missing+", native total="+native.Count;
        }

        internal static string Status(object hero, HeroProfile profile)
        {
            var native=GameBridge.Get(hero,"NativeSkills") as IList;
            var lines=new List<string>();
            foreach(var track in Tracks)
            {
                if(Rank(profile,track.Unlock)<1) continue;
                string best=track.Choose(profile);
                bool found=false;
                if(native!=null) foreach(var skill in native)
                {
                    string id=GameBridge.Get(GameBridge.Get(skill,"SkillDefinition"),"Id")?.ToString();
                    if(track.Belongs(id)) {best=id;found=true;break;}
                }
                lines.Add(best+(found?" ✓":" (не найден в игре)"));
            }
            return lines.Count==0?"Особые боевые навыки ещё не изучены":
                string.Join("  •  ",lines.ToArray());
        }
    }
}
