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
    // This version deliberately reuses ORIGINAL skills listed on weapons and
    // trinkets in ItemDatabase. No TLS.ModKit definitions and no inventory items.
    internal static class GameSkills
    {
        private const string SkillDb="TheLastStand.Database.Unit.SkillDatabase";
        private const string ItemDb="TheLastStand.Database.ItemDatabase";
        private const string SelectionManager="TheLastStand.Manager.TileObjectSelectionManager";

        internal sealed class StockSkill
        {
            internal string Id, Name, Source, Kind, Description;
        }

        private static object lastBar,lastHero;
        private static bool refreshPending;
        private static readonly string[] Groups={
            "БЛИЖНИЙ БОЙ","СТРЕЛЬБА","МАГИЯ","УКРАШЕНИЯ"
        };

        private static string Localized(string id)
        {
            try
            {
                var localizer=AccessTools.TypeByName("TPLib.Localization.Localizer");
                var get=localizer?.GetMethod("Get",BindingFlags.Public|BindingFlags.Static,
                    null,new[]{typeof(string)},null);
                return get?.Invoke(null,new object[]{id})?.ToString()??id;
            }
            catch {return id;}
        }

        internal static List<StockSkill> Available()
        {
            var result=new Dictionary<string,StockSkill>(StringComparer.Ordinal);
            var items=(GameBridge.StaticGet(ItemDb,"AllItemsDefinitions") ??
                GameBridge.StaticGet(ItemDb,"ItemDefinitions")) as IDictionary;
            var definitions=GameBridge.StaticGet(SkillDb,"SkillDefinitions") as IDictionary;
            if(items==null||definitions==null) return new List<StockSkill>();
            foreach(DictionaryEntry item in items)
            {
                object definition=item.Value;
                string category=GameBridge.Get(definition,"Category")?.ToString();
                string group=category=="Trinket"?"УКРАШЕНИЯ":
                    category=="MagicWeapon"?"МАГИЯ":
                    category=="RangeWeapon"?"СТРЕЛЬБА":
                    category=="MeleeWeapon"?"БЛИЖНИЙ БОЙ":null;
                if(group==null)continue;
                string source=GameBridge.Get(definition,"BaseName")?.ToString();
                if(string.IsNullOrWhiteSpace(source)) source=item.Key?.ToString();
                var byLevel=GameBridge.Get(definition,"SkillsByLevel") as IDictionary;
                if(byLevel==null)continue;
                foreach(DictionaryEntry level in byLevel)
                {
                    var skills=level.Value as IDictionary;
                    if(skills==null)continue;
                    foreach(DictionaryEntry entry in skills)
                    {
                        string id=entry.Key?.ToString();
                        if(string.IsNullOrWhiteSpace(id)||!definitions.Contains(id))continue;
                        object skill=definitions[id];
                        if(GameBridge.Get(skill,"IsContextual") is bool contextual && contextual)continue;
                        if(GameBridge.Get(skill,"IsBrazierSpecific") is bool brazier && brazier)continue;
                        if(result.ContainsKey(id))continue; // one skill, not every item variant
                        string localization=GameBridge.Get(skill,"LocalizationId")?.ToString();
                        if(string.IsNullOrEmpty(localization))localization=id;
                        string name=Localized("SkillName_"+localization);
                        if(string.IsNullOrWhiteSpace(name) || name=="SkillName_"+localization)
                            name=id;
                        string description=Localized("SkillDescription_"+localization);
                        if(description=="SkillDescription_"+localization)description="";
                        result.Add(id,new StockSkill{Id=id,Name=name,Source=source,
                            Kind=group,Description=description});
                    }
                }
            }
            return result.Values.OrderBy(x=>Array.IndexOf(Groups,x.Kind))
                .ThenBy(x=>x.Name,StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        internal static string Purchase(Plugin plugin,string id,ManualLogSource log)
        {
            var skill=Available().FirstOrDefault(s=>s.Id==id);
            if(skill==null)return "Умение недоступно в базе оружия и украшений";
            if(!plugin.Progression.PurchaseVanillaSkill(id,out string reason))
                return reason;
            plugin.SaveProfile();
            if(plugin.CurrentHero!=null)Grant(plugin.CurrentHero,plugin.Profile,log);
            return "Куплено: "+skill.Name+" (постоянно, без предмета)";
        }

        private static bool IsGranted(IList native,string id)
        {
            if(native==null)return false;
            foreach(object skill in native)
                if(GameBridge.Get(GameBridge.Get(skill,"SkillDefinition"),"Id")?.ToString()==id)
                    return true;
            return false;
        }

        internal static bool NeedsSync(object hero,HeroProfile profile)
        {
            if(hero==null||profile==null)return false;
            var native=GameBridge.Get(hero,"NativeSkills") as IList;
            if(native==null)return false;
            if(native.Cast<object>().Any(x=>
                (GameBridge.Get(GameBridge.Get(x,"SkillDefinition"),"Id")?.ToString()??"")
                    .StartsWith("Eternal",StringComparison.Ordinal)))return true;
            return (profile.PermanentVanillaSkills??new List<string>())
                .Any(id=>!IsGranted(native,id));
        }

        private static void RefreshBar(object hero,ManualLogSource log)
        {
            try
            {
                object selected=GameBridge.StaticGet(SelectionManager,"SelectedPlayableUnit");
                if(!ReferenceEquals(hero,selected))return;
                var panel=GameBridge.Singleton("TheLastStand.View.HUD.UnitManagement.PlayableUnitManagementView");
                var bar=GameBridge.Get(panel,"PlayableSkillBar");
                var refresh=bar?.GetType().GetMethod("Refresh",new[]{typeof(bool)});
                if(refresh==null){refreshPending=true;return;}
                refresh.Invoke(bar,new object[]{true});
                lastHero=hero;lastBar=bar;refreshPending=false;
                log.LogInfo("Eternal Hero: stock hero skill bar refreshed");
            }
            catch(Exception ex)
            {
                refreshPending=true;
                log.LogWarning("Stock skill UI refresh: "+ex.GetBaseException().Message);
            }
        }

        internal static string Grant(object hero,HeroProfile profile,ManualLogSource log)
        {
            if(hero==null||profile==null)return "Нет героя";
            var native=GameBridge.Get(hero,"NativeSkills") as IList;
            var db=GameBridge.StaticGet(SkillDb,"SkillDefinitions") as IDictionary;
            if(native==null||db==null)return "Список навыков игры пока недоступен";
            int count=0,unavailable=0,removed=0;
            // Old experimental XML abilities failed to register. Remove their
            // stale runtime entries, never touch actual vanilla native abilities.
            for(int i=native.Count-1;i>=0;i--)
            {
                string id=GameBridge.Get(GameBridge.Get(native[i],"SkillDefinition"),"Id")?.ToString();
                if(id!=null&&id.StartsWith("Eternal",StringComparison.Ordinal))
                { native.RemoveAt(i);removed++; }
            }
            var controllerType=AccessTools.TypeByName("TheLastStand.Controller.Skill.SkillController");
            if(controllerType==null)return "Невозможно найти игровой SkillController";
            var allowed=new HashSet<string>(Available().Select(x=>x.Id),StringComparer.Ordinal);
            foreach(string id in (profile.PermanentVanillaSkills??new List<string>()).Distinct())
            {
                if(IsGranted(native,id))continue;
                if(!db.Contains(id)||!allowed.Contains(id))
                {unavailable++;log.LogWarning("Eternal Hero stock skill not found: "+id);continue;}
                try
                {
                    var definition=db[id];
                    var ctor=controllerType.GetConstructors().FirstOrDefault(c=>{
                        var a=c.GetParameters();
                        return a.Length==4 && a[0].ParameterType.IsInstanceOfType(definition)
                            && a[1].ParameterType.IsInstanceOfType(hero);
                    });
                    if(ctor==null){unavailable++;continue;}
                    object controller=ctor.Invoke(new object[]{definition,hero,-1,-1});
                    object skill=GameBridge.Get(controller,"Skill");
                    if(skill==null){unavailable++;continue;}
                    native.Add(skill);
                    count++;
                    log.LogInfo("Eternal Hero stock native skill granted: "+id);
                }
                catch(Exception ex)
                {
                    unavailable++;
                    log.LogWarning("Stock skill "+id+": "+ex.GetBaseException().Message);
                }
            }
            if(count>0||removed>0)refreshPending=true;
            if(refreshPending)RefreshBar(hero,log);
            return "выдано "+count+", отсутствует в базе "+unavailable+
                ", заменено прежних XML-умений "+removed;
        }

        internal static void Synchronize(object hero,HeroProfile profile,ManualLogSource log)
        {
            if(hero==null||profile==null)return;
            if(NeedsSync(hero,profile))Grant(hero,profile,log);
            var selected=GameBridge.StaticGet(SelectionManager,"SelectedPlayableUnit");
            if(!ReferenceEquals(selected,hero))return;
            object panel=GameBridge.Singleton("TheLastStand.View.HUD.UnitManagement.PlayableUnitManagementView");
            object bar=GameBridge.Get(panel,"PlayableSkillBar");
            if(bar!=null && (refreshPending || !ReferenceEquals(lastHero,hero)||
                             !ReferenceEquals(lastBar,bar)))RefreshBar(hero,log);
        }

        internal static string Diagnostic(object hero,HeroProfile profile)
        {
            int purchased=profile?.PermanentVanillaSkills?.Count??0;
            var native=GameBridge.Get(hero,"NativeSkills") as IList;
            int actual=(profile?.PermanentVanillaSkills??new List<string>())
                .Count(id=>IsGranted(native,id));
            return "Штатные навыки: "+Available().Count+
                " • куплено "+purchased+" • выдано ГГ "+actual;
        }

        internal static string Status(object hero,HeroProfile profile)
        {
            return Diagnostic(hero,profile)+
                "\nОружие/украшения: собственные навыки, без предметов и TLS.ModKit";
        }
    }
}
