using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using EternalHero.Core;
using HarmonyLib;
using UnityEngine;

namespace EternalHero.Plugin
{
    // This version deliberately reuses ORIGINAL skills listed on weapons and
    // trinkets in ItemDatabase. No external skill definitions and no inventory items.
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
        private static string lastWeaponProbe="";
        private static int weaponProbeLogCount;
        private static ManualLogSource combatLog;
        private static bool itemDamageHookInstalled,rangeDamageHookInstalled;
        private static readonly HashSet<string> LoggedDamageFallback=
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> LoggedDamageSources=
            new HashSet<string>(StringComparer.Ordinal);

        // The original game's AttackSkillAction.BaseDamage uses
        // Skill.SkillContainer as an Item to read Item.BaseDamages.
        // Our purchased abilities live directly on the PlayableUnit. Feed
        // them the equipped primary-hand weapon's damage instead.
        internal static void InstallDamageAdapter(Harmony harmony,ManualLogSource log)
        {
            combatLog=log;
            // Install each layer independently: a missing BaseDamage getter must
            // NEVER disable the combat/tooltip fallback (or abort plugin startup).
            try
            {
                var action=AccessTools.TypeByName(
                    "TheLastStand.Model.Skill.SkillAction.AttackSkillAction");
                var getter=action==null?null:AccessTools.PropertyGetter(action,"BaseDamage");
                var patch=AccessTools.Method(typeof(GameSkills),nameof(ResolveOwnedSkillDamage));
                if(getter!=null && patch!=null)
                {
                    harmony.Patch(getter,postfix:new HarmonyMethod(patch));
                    itemDamageHookInstalled=true;
                    log.LogInfo("Eternal Hero: native item skill base damage adapter installed");
                }
                else log.LogWarning("Eternal Hero: AttackSkillAction.BaseDamage adapter unavailable");
            }
            catch(Exception ex)
            {
                log.LogWarning("Eternal Hero: BaseDamage hook failed: "+
                    ex.GetBaseException().Message);
            }

            // Second layer: tooltip preview and combat both request this range.
            // A failure in either layer must not prevent the other being installed.
            try
            {
                var controller=AccessTools.TypeByName(
                    "TheLastStand.Controller.Skill.SkillAction.AttackSkillActionController");
                var calculate=controller==null?null:AccessTools.Method(
                    controller,"ComputeBaseDamageRange");
                var fixRange=AccessTools.Method(typeof(GameSkills),nameof(EnforcePurchasedAttackDamageFloor));
                if(calculate!=null&&fixRange!=null)
                {
                    harmony.Patch(calculate,postfix:new HarmonyMethod(fixRange));
                    rangeDamageHookInstalled=true;
                    log.LogInfo("Eternal Hero: combat + tooltip purchased-attack damage floor installed");
                }
                else log.LogWarning("Eternal Hero: secondary damage range hook unavailable");
            }
            catch(Exception ex)
            {
                log.LogWarning("Eternal Hero: secondary damage range hook failed: "+
                    ex.GetBaseException().Message);
            }
            log.LogInfo("Eternal Hero: damage hooks: BaseDamage="+
                itemDamageHookInstalled+", ComputeBaseDamageRange="+rangeDamageHookInstalled);
        }

        private static bool IsOurActiveNativeSkill(object action,out string id)
        {
            id=null;
            var profile=Plugin.Instance?.Profile;
            var hero=Plugin.Instance?.CurrentHero;
            if(hero==null||profile?.PermanentVanillaSkills==null||action==null)
                return false;
            var skill=GameBridge.Get(action,"Skill");
            if(skill==null || !ReferenceEquals(GameBridge.Get(skill,"SkillContainer"),hero))
                return false;
            id=GameBridge.Get(GameBridge.Get(skill,"SkillDefinition"),"Id")?.ToString();
            return id!=null&&profile.PermanentVanillaSkills.Contains(id)&&
                (profile.DisabledVanillaSkills==null ||
                 !profile.DisabledVanillaSkills.Contains(id));
        }

        // The game's equipment model is a Dictionary<E_ItemSlotId,
        // List<EquipmentSlot>>, but work across versions that expose a
        // different IDictionary / generic IEnumerable of key-value pairs.
        // RightHand=1, LeftHand=2 in the game's E_ItemSlotId enum.
        private static IEnumerable<KeyValuePair<object,object>> EquipmentPairs(object slots)
        {
            if(slots is IDictionary dict)
            {
                foreach(DictionaryEntry pair in dict)
                    yield return new KeyValuePair<object,object>(pair.Key,pair.Value);
            }
            else if(slots is IEnumerable sequence)
            {
                foreach(object pair in sequence)
                {
                    object key=GameBridge.Get(pair,"Key");
                    if(key!=null)
                        yield return new KeyValuePair<object,object>(
                            key,GameBridge.Get(pair,"Value"));
                }
            }
        }

        private static bool IsHandSlot(object key,string expected,int number)
        {
            if(key==null)return false;
            if(string.Equals(key.ToString(),expected,StringComparison.OrdinalIgnoreCase))
                return true;
            try{return (key is Enum || key is int || key is byte) &&
                Convert.ToInt64(key)==number;}
            catch{return false;}
        }

        private static List<object> ReadSlots(object value)
        {
            if(value is IList list)
                return list.Cast<object>().ToList();
            if(value is IEnumerable enumerable)
                return enumerable.Cast<object>().Take(16).ToList();
            return new List<object>();
        }

        // Unlike GameBridge.Get, this exposes reflection exceptions in the log.
        // The Last Spell's Item.BaseDamages getter indexes
        // ItemDefinition.BaseDamageByLevel[Level] and can throw for missing
        // levels; silently treating that as an unequipped weapon hid our bug.
        private static object ReadDamageMember(object owner,string name,
            out string detail)
        {
            detail="owner=NULL";
            if(owner==null)return null;
            var type=owner.GetType();
            const BindingFlags flags=BindingFlags.Instance|
                BindingFlags.Public|BindingFlags.NonPublic;
            try
            {
                var property=type.GetProperty(name,flags);
                if(property!=null && property.GetIndexParameters().Length==0)
                {
                    object value=property.GetValue(owner,null);
                    detail="property "+type.Name+"."+name+
                        "="+(value==null?"NULL":value.GetType().Name);
                    return value;
                }
                var field=type.GetField(name,flags);
                if(field!=null)
                {
                    object value=field.GetValue(owner);
                    detail="field "+type.Name+"."+name+
                        "="+(value==null?"NULL":value.GetType().Name);
                    return value;
                }
                detail="missing member "+type.FullName+"."+name;
            }
            catch(Exception ex)
            {
                detail="error "+type.FullName+"."+name+": "+
                    ex.GetBaseException().GetType().Name+": "+
                    ex.GetBaseException().Message;
            }
            return null;
        }

        private static bool TryItemDamage(object equipped,out Vector2 result,
            out string diagnostic)
        {
            result=Vector2.zero;
            if(equipped==null){diagnostic="item=NULL";return false;}
            string baseStatus;
            object raw=ReadDamageMember(equipped,"BaseDamages",out baseStatus);
            diagnostic="item="+equipped.GetType().Name+
                ", BaseDamages="+(raw==null?"NULL":raw.ToString())+
                " ("+baseStatus+")";
            if(raw is Vector2 baseDamage && baseDamage.y>0f)
            {result=baseDamage;return true;}

            string definitionStatus,tableStatus,levelStatus;
            object definition=ReadDamageMember(equipped,"ItemDefinition",
                out definitionStatus);
            object byLevel=ReadDamageMember(definition,"BaseDamageByLevel",
                out tableStatus);
            object level=ReadDamageMember(equipped,"Level",out levelStatus);
            diagnostic+=", definition="+definitionStatus+
                ", damageTable="+tableStatus+
                ", level="+(level?.ToString()??"NULL")+
                " ("+levelStatus+")";
            if(byLevel is IDictionary levels)
            {
                // Always print the available levels on first failure so the
                // actual game version can be diagnosed without guesswork.
                var keys=new List<string>();
                foreach(object key in levels.Keys)
                {
                    if(keys.Count>=16)break;
                    keys.Add(key?.ToString()??"NULL");
                }
                diagnostic+=", availableLevels=["+string.Join(",",keys.ToArray())+"]";
                if(level!=null)
                {
                    try
                    {
                        if(levels.Contains(level))
                        {
                            object alternative=levels[level];
                            if(alternative is Vector2 range && range.y>0f)
                            {
                                result=range;
                                diagnostic+=", definition-level="+level+" -> "+range;
                                return true;
                            }
                            diagnostic+=", table value="+(alternative?.ToString()??"NULL");
                        }
                        else diagnostic+=", requested level absent";
                    }
                    catch(Exception ex)
                    {diagnostic+=", level lookup error="+
                        ex.GetBaseException().Message;}
                }
            }
            return false;
        }

        private static void RecordWeaponProbe(string id,string diagnosis)
        {
            string record=id+": "+diagnosis;
            if(record==lastWeaponProbe)return;
            lastWeaponProbe=record;
            if(weaponProbeLogCount++<96)
                combatLog?.LogInfo("Eternal Hero equipment probe: "+record);
        }

        // Read the weapon anew every time: changes to equipment or the
        // active set must affect damage without caching or reloading.
        private static Vector2 GetSourceDamage(string id)
        {
            object hero=Plugin.Instance?.CurrentHero;
            object source=GameBridge.Get(hero,"EquipmentSlots");
            object active=GameBridge.Get(hero,"EquippedWeaponSetIndex");
            int setIndex=0;
            try{setIndex=Convert.ToInt32(active??0);}
            catch{ }

            string header="hero="+(hero?.GetType().Name??"NULL")+
                ", EquipmentSlots="+(source?.GetType().Name??"NULL")+
                ", activeSet="+setIndex;
            var allPairs=EquipmentPairs(source).Take(32).ToList();
            string names=string.Join(",",
                allPairs.Select(pair=>pair.Key?.ToString()??"NULL").ToArray());
            header+=", keys=["+names+"]";

            // Prefer the primary hand. If genuinely empty, permit a weapon
            // in the other active hand, but never a weapon in an inactive set
            // or another hero's inventory.
            foreach(var hand in new[]{new{Key="RightHand",Code=1},
                                       new{Key="LeftHand",Code=2}})
            {
                var pair=allPairs.FirstOrDefault(p=>
                    IsHandSlot(p.Key,hand.Key,hand.Code));
                if(pair.Key==null)continue;
                var handSlots=ReadSlots(pair.Value);
                string slotInfo=hand.Key+" count="+handSlots.Count;
                if(setIndex<0||setIndex>=handSlots.Count)
                {
                    header+="; "+slotInfo+" active index out of range";
                    continue;
                }
                object slot=handSlots[setIndex];
                object equipped=GameBridge.Get(slot,"Item");
                if(TryItemDamage(equipped,out Vector2 baseDamage,out string itemDiag))
                {
                    RecordWeaponProbe(id,header+"; "+slotInfo+
                        "; "+itemDiag+"; RESOLVED="+baseDamage);
                    string key=id+":"+setIndex+":"+hand.Key+":"+baseDamage.x+":"+baseDamage.y;
                    if(LoggedDamageSources.Count<256&&LoggedDamageSources.Add(key))
                        combatLog?.LogInfo("Eternal Hero first weapon slot: "+
                            id+" from "+hand.Key+" active weapon set "+setIndex+
                            ", base damage "+baseDamage.x+"–"+baseDamage.y);
                    return baseDamage;
                }
                header+="; "+slotInfo+"; slotType="+(slot?.GetType().Name??"NULL")+
                    "; "+itemDiag;
            }
            float fallbackMin=Plugin.Instance?.FallbackAttackBaseDamage??150f;
            float fallbackMax=Plugin.Instance?.FallbackAttackMaxDamage??200f;
            // A nonzero span is essential: v0.24 used (150,150), which
            // collapsed every purchased skill tooltip to one damage value.
            fallbackMax=Mathf.Max(fallbackMin+1f,fallbackMax);
            RecordWeaponProbe(id,header+"; fallback=range "+
                fallbackMin+"–"+fallbackMax+" (real weapon damage unavailable)");
            return new Vector2(fallbackMin,fallbackMax);
        }

        // Called after the game's own skill-base range calculation. The
        // original multiplier may be as small as 0.1 even for a powerful gun:
        // unlike v0.21, enforce a minimum for ALL purchased attacks, not only
        // for zero-damage failures. Vanilla high-damage skills retain their
        // larger original range. The result is used by preview and combat.
        private static void EnforcePurchasedAttackDamageFloor(object __instance,
            ref Vector2Int __result)
        {
            var action=GameBridge.Get(__instance,"SkillAction");
            if(!IsOurActiveNativeSkill(action,out string id))return;
            var definition=GameBridge.Get(action,"AttackSkillActionDefinition");
            var rawMultiplier=GameBridge.Get(definition,"DamageMultiplier");
            if(rawMultiplier!=null)
            {
                try{if(Convert.ToSingle(rawMultiplier)<=0f)return;}
                catch { }
            }
            Vector2 source=GetSourceDamage(id);
            int lower=__result.x,upper=__result.y;
            int oldLower=lower,oldUpper=upper;
            float weaponFactor=Plugin.Instance?.PurchasedAttackDamageFloor??1.25f;
            if(!PurchasedAttackDamageFloor.Apply(ref lower,ref upper,
                source.x,source.y,weaponFactor))return;
            __result=new Vector2Int(lower,upper);
            string key=id+":"+source.x+":"+source.y+":"+
                oldLower+":"+oldUpper+":"+lower+":"+upper;
            // Only log distinct corrections and cap memory usage during
            // extended runs with repeated stat/weapon changes.
            if(LoggedDamageFallback.Count<512 && LoggedDamageFallback.Add(key))
                combatLog?.LogInfo("Eternal Hero: purchased attack floor applied to "+
                    id+"; original "+oldLower+"–"+oldUpper+
                    ", source "+source.x+"–"+source.y+
                    ", minimum="+(weaponFactor*100f)+"%, output "+
                    lower+"–"+upper+" (tooltip and combat)");
        }

        private static void ResolveOwnedSkillDamage(object __instance,ref Vector2 __result)
        {
            if(!IsOurActiveNativeSkill(__instance,out string id))return;
            // The purchased attack always uses the equipped primary-hand weapon,
            // even when its original item-independent getter returned nonzero.
            // Support-only actions with an explicit zero multiplier stay untouched.
            var def=GameBridge.Get(__instance,"AttackSkillActionDefinition");
            var multiplier=GameBridge.Get(def,"DamageMultiplier");
            if(multiplier!=null)
            {
                try{if(Convert.ToSingle(multiplier)<=0f)return;}
                catch { }
            }
            __result=GetSourceDamage(id); // vanilla damage modifiers apply afterward
        }

        private static readonly Dictionary<string,Sprite> IconCache=
            new Dictionary<string,Sprite>(StringComparer.Ordinal);
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

        // Reuse exactly the icon requested by the game's SkillDisplay.RefreshIcon:
        // SkillView.GetIconSprite(skillDefinition.ArtId).
        internal static Sprite Icon(string id)
        {
            if(string.IsNullOrWhiteSpace(id))return null;
            try
            {
                var db=GameBridge.StaticGet(SkillDb,"SkillDefinitions") as IDictionary;
                if(db==null || !db.Contains(id))return null;
                string art=GameBridge.Get(db[id],"ArtId")?.ToString();
                if(string.IsNullOrWhiteSpace(art))art=id;
                if(IconCache.TryGetValue(art,out Sprite cached))return cached;
                var view=AccessTools.TypeByName("TheLastStand.View.Skill.SkillView");
                var method=view?.GetMethod("GetIconSprite",
                    BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(string)},null);
                Sprite sprite=method?.Invoke(null,new object[]{art}) as Sprite;
                IconCache[art]=sprite;
                return sprite;
            }
            catch{return null;}
        }

        internal static string SetEnabled(Plugin plugin,string id,bool enabled,ManualLogSource log)
        {
            if(!plugin.Progression.SetVanillaSkillEnabled(id,enabled,out string error))
                return error;
            plugin.SaveProfile();
            if(plugin.CurrentHero!=null)
            {
                string result=Grant(plugin.CurrentHero,plugin.Profile,log);
                Synchronize(plugin.CurrentHero,plugin.Profile,log);
                log.LogInfo("Eternal Hero skill "+id+" enabled="+enabled+": "+result);
            }
            return enabled?"Включено: навык снова доступен герою":
                "Отключено: покупка сохранена, навык скрыт с боевой панели";
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
            var disabled=new HashSet<string>(
                profile.DisabledVanillaSkills??new List<string>(),StringComparer.Ordinal);
            return (profile.PermanentVanillaSkills??new List<string>())
                .Any(id=>disabled.Contains(id)?IsGranted(native,id):!IsGranted(native,id));
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
            // Only purchased skills can be toggled; never mutate unrelated inventory,
            // equipment, scrolls or the built-in roster of the other characters.
            var disabled=new HashSet<string>(
                profile.DisabledVanillaSkills??new List<string>(),StringComparer.Ordinal);
            foreach(string id in disabled)
            {
                if(profile.PermanentVanillaSkills==null ||
                    !profile.PermanentVanillaSkills.Contains(id))continue;
                for(int i=native.Count-1;i>=0;i--)
                {
                    string existingId=GameBridge.Get(
                        GameBridge.Get(native[i],"SkillDefinition"),"Id")?.ToString();
                    if(existingId!=id)continue;
                    native.RemoveAt(i);
                    removed++;
                    log.LogInfo("Eternal Hero: skill disabled from NativeSkills "+id);
                }
            }
            var controllerType=AccessTools.TypeByName("TheLastStand.Controller.Skill.SkillController");
            if(controllerType==null)return "Невозможно найти игровой SkillController";
            var allowed=new HashSet<string>(Available().Select(x=>x.Id),StringComparer.Ordinal);
            foreach(string id in (profile.PermanentVanillaSkills??new List<string>()).Distinct())
            {
                if(disabled.Contains(id) || IsGranted(native,id))continue;
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
            int enabled=(profile?.PermanentVanillaSkills??new List<string>())
                .Count(id=>profile?.DisabledVanillaSkills==null||
                    !profile.DisabledVanillaSkills.Contains(id));
            int actual=(profile?.PermanentVanillaSkills??new List<string>())
                .Count(id=>IsGranted(native,id));
            return "Штатные навыки: "+Available().Count+
                " • куплено "+purchased+" • включено "+enabled+
                " • выдано ГГ "+actual;
        }

        internal static string Status(object hero,HeroProfile profile)
        {
            return Diagnostic(hero,profile)+
                "\nОружие/украшения: собственные навыки, без предметов и TLS.ModKit"+
                "\nБоевые обработчики: BaseDamage="+(itemDamageHookInstalled?"OK":"НЕТ")+
                ", диапазон="+(rangeDamageHookInstalled?"OK":"НЕТ")+
                "\nМинимальный базовый урон купленных атак: "+
                Mathf.RoundToInt((Plugin.Instance?.PurchasedAttackDamageFloor??1.25f)*100f)+"% оружия"+
                "\nЗапасная база при ошибке чтения: "+
                Mathf.RoundToInt(Plugin.Instance?.FallbackAttackBaseDamage??150f)+"–"+
                Mathf.RoundToInt(Plugin.Instance?.FallbackAttackMaxDamage??200f);
        }
    }
}
