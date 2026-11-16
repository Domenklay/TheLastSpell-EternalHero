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
    internal static class GamePerks
    {
        private sealed class MetaPointsLedger { internal int RankApplied; }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, MetaPointsLedger>
            MetaPoints = new System.Runtime.CompilerServices.ConditionalWeakTable<object, MetaPointsLedger>();

        private static int BonusRank(HeroProfile profile) =>
            profile?.Ranks != null && profile.Ranks.TryGetValue("legacy.perk", out int rank)
                ? Math.Max(0, Math.Min(8, rank)) : 0;

        internal static void PrimeLoadedPerkPoints(object hero, HeroProfile profile)
        {
            if (hero == null) return;
            var ledger = MetaPoints.GetValue(hero, _ => new MetaPointsLedger());
            // The game save already has the perk-point bonus from earlier ranks.
            ledger.RankApplied = BonusRank(profile);
        }

        internal static int ApplyMetaPerkPoints(object hero, HeroProfile profile, ManualLogSource log)
        {
            if (hero == null || profile == null) return 0;
            var ledger = MetaPoints.GetValue(hero, _ => new MetaPointsLedger());
            int rank = BonusRank(profile);
            int delta = Math.Max(0, rank - ledger.RankApplied);
            if (delta == 0) return 0;
            int before = AvailablePerkPoints(hero);
            if (!GameBridge.Set(hero, "PerksPoints", before + delta))
            {
                log.LogWarning("Eternal Hero: meta perk-point injection unavailable");
                return 0;
            }
            ledger.RankApplied = rank;
            log.LogInfo("Eternal Hero: granted "+delta+
                " additional original perk points from legacy.perk");
            return delta;
        }

        private const string Database = "TheLastStand.Database.Unit.PlayableUnitDatabase";
        private static readonly Dictionary<string,string> LastGrantFailures =
            new Dictionary<string,string>(StringComparer.Ordinal);
        internal static string LastFailure(string id) =>
            id != null && LastGrantFailures.TryGetValue(id,out string message) ? message : "";
        private static void NoteGrantFailure(string id,string error,ManualLogSource log)
        {
            LastGrantFailures[id]=error;
            log.LogWarning("Eternal Hero permanent perk "+id+": "+error);
        }
        public sealed class Option
        {
            public string Id;
            public string Name;
            public string Description;
            public string Collection = "Общие";
            public int Tier;
        }

        internal static List<Option> Available()
        {
            var list = new List<Option>();
            var defs = GameBridge.StaticGet(Database, "PerkDefinitions") as IDictionary;
            if (defs == null) return list;
            var byId = new Dictionary<string, Option>();
            foreach (DictionaryEntry entry in defs)
            {
                if (!(entry.Key is string id)) continue;
                string name = GameBridge.Get(entry.Value, "Name")?.ToString();
                string description=null;
                try
                {
                    var method=entry.Value.GetType().GetMethods(
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public)
                        .FirstOrDefault(m=>m.Name=="GetDescription"&&
                            m.GetParameters().Length==1);
                    if(method!=null)
                        description=method.Invoke(entry.Value,new object[]{null})?.ToString();
                }
                catch { }
                if (!string.IsNullOrWhiteSpace(description))
                    description=System.Text.RegularExpressions.Regex.Replace(
                        description,"<[^>]*>","").Trim();
                var option = new Option {
                    Id = id, Name = string.IsNullOrWhiteSpace(name) ? id : name,
                    Description = string.IsNullOrWhiteSpace(description)
                        ? "Подробности доступны в стандартном описании перка игры."
                        : description
                };
                list.Add(option);
                byId[id] = option;
            }
            // Every vanilla perk can belong to one or more collections. Display the
            // first matching specialization and its actual tier, for search and clarity.
            var collections = GameBridge.StaticGet(Database, "UnitPerkCollectionDefinitions") as IDictionary;
            if (collections != null)
            {
                foreach (DictionaryEntry col in collections)
                {
                    string category = col.Key?.ToString() ?? "Общие";
                    var tiers = GameBridge.Get(col.Value, "PerksFromTier") as IDictionary;
                    if (tiers == null) continue;
                    foreach (DictionaryEntry tier in tiers)
                    {
                        int level = 0;
                        try { level = Convert.ToInt32(tier.Key); } catch { }
                        if (!(tier.Value is IEnumerable options)) continue;
                        foreach (object pair in options)
                        {
                            string id = GameBridge.Get(GameBridge.Get(pair, "Item1"), "Id")?.ToString();
                            if (id == null || !byId.TryGetValue(id, out Option option)) continue;
                            if (option.Tier == 0)
                            {
                                option.Collection = category;
                                option.Tier = level;
                            }
                        }
                    }
                }
            }
            return list.OrderBy(x => x.Collection).ThenBy(x => x.Tier).ThenBy(x => x.Name).ToList();
        }

        internal static void Install(Harmony harmony, ManualLogSource log)
        {
            try
            {
                // The original game only sets UnitPerkTier.Available after previous
                // tier purchases. For this one hero, expose every tier as accessible.
                var type=AccessTools.TypeByName("TheLastStand.Model.Unit.Perk.UnitPerkTier");
                var getter=type?.GetProperty("Available")?.GetGetMethod();
                var postfix=AccessTools.Method(typeof(GamePerks),nameof(AvailableForProtagonist));
                if(getter!=null && postfix!=null)
                {
                    harmony.Patch(getter,postfix:new HarmonyMethod(postfix));
                    log.LogInfo("Eternal Hero: all vanilla perk tiers accessible to protagonist");
                }
                else log.LogWarning("Eternal Hero: UnitPerkTier.Available getter not found");
            }
            catch(Exception ex){ log.LogWarning("Eternal Hero perk tier Harmony patch: "+ex); }
        }

        private static void AvailableForProtagonist(object __instance, ref bool __result)
        {
            if (__result || Plugin.Instance?.CurrentHero == null ||
                Plugin.Instance.Profile?.UnlockAllPerkTiers != true) return;
            var tree=GameBridge.Get(__instance,"UnitPerkTree");
            var owner=GameBridge.Get(tree,"PlayableUnit");
            if(ReferenceEquals(owner,Plugin.Instance.CurrentHero)) __result=true;
        }

        internal static UnityEngine.Sprite PerkIcon(string perkId)
        {
            if (string.IsNullOrWhiteSpace(perkId)) return null;
            try
            {
                var defs = GameBridge.StaticGet(Database, "PerkDefinitions") as IDictionary;
                if (defs != null && defs.Contains(perkId))
                    return GameBridge.Get(defs[perkId], "PerkSprite") as UnityEngine.Sprite;
            }
            catch { }
            return null;
        }

        internal static int AvailablePerkPoints(object hero)
        {
            try { return Convert.ToInt32(GameBridge.Get(hero, "PerksPoints") ?? 0); }
            catch { return 0; }
        }

        internal static bool IsLearned(object hero, string id)
        {
            if (hero == null || string.IsNullOrWhiteSpace(id)) return false;
            var dictionary = GameBridge.Get(hero, "Perks") as IDictionary;
            if (dictionary == null || !dictionary.Contains(id)) return false;
            // Item-granted buffs are not a purchased perk. Count only a perk
            // explicitly unlocked by the playable character, not by equipment.
            var perk = dictionary[id];
            return GameBridge.Get(perk,"Unlocked") is bool unlocked && unlocked &&
                GameBridge.Get(perk, "IsUnlockedFromPlayableUnit") is bool value && value;
        }

        // The randomized six-column vanilla skill tree is NOT an authoritative
        // record of all learned perks. Native/off-tree perks live in hero.Perks and
        // have no PerkTier (and therefore no icon in that randomized tree).
        // A learned perk's effects are installed by PerkController.Unlock, which
        // calls Hook and its native perk modules.
        internal static string RuntimeState(object hero,string id)
        {
            if(hero==null) return "НЕТ ГЕРОЯ";
            var perks=GameBridge.Get(hero,"Perks") as IDictionary;
            if(perks==null || !perks.Contains(id)) return "НЕ ВЫДАН"+
                (string.IsNullOrWhiteSpace(LastFailure(id))?"":": "+LastFailure(id));
            var perk=perks[id];
            if(!IsLearned(hero,id)) return "НЕ АКТИВИРОВАН"+
                (string.IsNullOrWhiteSpace(LastFailure(id))?"":": "+LastFailure(id));
            return GameBridge.Get(perk,"PerkTier")==null
                ? "ВЫДАН • ВНЕ СЛУЧАЙНОГО ДЕРЕВА"
                : "ВЫДАН • В ДЕРЕВЕ";
        }

        internal static bool NeedsPermanentSync(object hero,HeroProfile profile)
        {
            return hero!=null && profile?.SelectedPerks!=null &&
                profile.SelectedPerks.Any(id=>!IsLearned(hero,id));
        }

        internal static string PermanentDiagnostic(object hero,HeroProfile profile)
        {
            var chosen=profile?.SelectedPerks?.Distinct().ToArray()??new string[0];
            int active=chosen.Count(id=>IsLearned(hero,id));
            return "Постоянные перки: выдано "+active+"/"+chosen.Length+
                (hero==null?" • начни забег":
                 active<chosen.Length?" • нажми «Применить», если нет эффекта":" • эффект зависит от условий");
        }

        // Prefer the type of a controller which this *running game* actually
        // created, not just a type guessed by name. The constructor signature
        // varies across The Last Spell builds, including visibility and booleans.
        internal static object CreateOffTreeController(object definition,object hero,
            IDictionary existing,ManualLogSource log,out string reason)
        {
            reason=null;
            if(definition==null||hero==null)
            { reason="Отсутствует игровое определение перка или герой";return null; }
            var candidates=new List<Type>();
            if(existing!=null)foreach(DictionaryEntry item in existing)
            {
                var current=GameBridge.Get(item.Value,"PerkController");
                if(current!=null && !candidates.Contains(current.GetType()))
                    candidates.Add(current.GetType());
            }
            var byName=AccessTools.TypeByName("TheLastStand.Controller.Unit.Perk.PerkController");
            if(byName!=null&&!candidates.Contains(byName))candidates.Add(byName);
            var descriptions=new List<string>();
            foreach(var type in candidates)
            {
                foreach(var ctor in type.GetConstructors(BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    var p=ctor.GetParameters();
                    descriptions.Add(type.Name+"("+string.Join(",",
                        p.Select(x=>x.ParameterType.Name+" "+x.Name).ToArray())+")");
                    // Serialized-perk constructor must never be used to create
                    // fresh permanent perks. Only bind a real PerkDefinition.
                    if(p.Length<5 || !p[0].ParameterType.IsInstanceOfType(definition))
                        continue;
                    if(!p.Any(x=>x.ParameterType.IsInstanceOfType(hero)))
                        continue;
                    if(p.Any(x=>x.ParameterType.Name.Contains("SerializedPerk")))
                        continue;
                    var values=new object[p.Length];
                    bool okay=true,ownerSet=false,definitionSet=false;
                    int booleanIndex=0;
                    for(int i=0;i<p.Length;i++)
                    {
                        var v=p[i];
                        if(!definitionSet && v.ParameterType.IsInstanceOfType(definition))
                        {values[i]=definition;definitionSet=true;continue;}
                        if(!ownerSet && v.ParameterType.IsInstanceOfType(hero))
                        {values[i]=hero;ownerSet=true;continue;}
                        if(v.ParameterType==typeof(string))
                        {values[i]=string.Empty;continue;}
                        if(v.ParameterType==typeof(bool))
                        {
                            string n=(v.Name??"").ToLowerInvariant();
                            values[i]=n.Contains("native") || (!n.Contains("race")&&booleanIndex==0);
                            booleanIndex++;
                            continue;
                        }
                        if(v.HasDefaultValue){values[i]=v.DefaultValue;continue;}
                        if(!v.ParameterType.IsValueType){values[i]=null;continue;}
                        okay=false;break;
                    }
                    if(!okay||!ownerSet||!definitionSet)continue;
                    try
                    {
                        var controller=ctor.Invoke(values);
                        if(GameBridge.Get(controller,"Perk")!=null)
                        {
                            log.LogInfo("Eternal Hero native off-tree perk factory: "+
                                type.FullName+" constructor params="+p.Length);
                            return controller;
                        }
                    }
                    catch(Exception ex)
                    {
                        log.LogWarning("Off-tree perk constructor "+type.Name+
                            " rejected: "+ex.GetBaseException().Message);
                    }
                }
            }
            reason="Не удалось создать штатный PerkController. "+
                "Definition="+definition.GetType().FullName+
                ", hero="+hero.GetType().FullName+
                ", сигнатуры="+string.Join(" | ",descriptions.Take(5).ToArray());
            log.LogWarning("Eternal Hero "+reason);
            return null;
        }

        // This works with the game's original PerkController, but does not add new columns
        // to the vanilla six-column view. It purchases any *existing* PerkDefinition
        // for one ordinary perk point and saves it as a dynamic perk in the game's save.
        internal static bool PurchaseAnyPerk(object hero, string id, ManualLogSource log,
            out string error)
        {
            error = null;
            if (hero == null || Plugin.Instance == null ||
                !ReferenceEquals(hero, Plugin.Instance.CurrentHero))
            {
                error = "Главный герой не найден в текущем забеге";
                return false;
            }
            if (string.IsNullOrWhiteSpace(id)) { error = "Перк не выбран"; return false; }
            var db = GameBridge.StaticGet(Database, "PerkDefinitions") as IDictionary;
            if (db == null || !db.Contains(id))
            { error = "Перка нет в базе данных игры"; return false; }
            int points = AvailablePerkPoints(hero);
            if (points <= 0) { error = "Недостаточно очков особых черт"; return false; }
            var perks = GameBridge.Get(hero, "Perks") as IDictionary;
            if (perks == null) { error = "Не обнаружен игровой список перков"; return false; }
            if (IsLearned(hero, id)) { error = "Перк уже изучен"; return false; }

            try
            {
                object controller;
                bool wasExisting = perks.Contains(id);
                if (wasExisting)
                {
                    controller = GameBridge.Get(perks[id], "PerkController");
                    if (controller == null) { error = "Нет контроллера существующего перка"; return false; }
                }
                else
                {
                    object definition = db[id];
                    controller = CreateOffTreeController(definition,hero,perks,log,
                        out string constructReason);
                    if(controller==null)
                    { error=constructReason;return false; }
                    if (GameBridge.Get(controller, "Perk") == null)
                    { error = "Игра не создала перк"; return false; }
                }

                var method = controller.GetType().GetMethods().FirstOrDefault(m => {
                    var args = m.GetParameters();
                    return m.Name == "Unlock" && args.Length == 1 &&
                        args[0].ParameterType.IsInstanceOfType(hero);
                });
                if (method == null) { error = "Метод разблокировки недоступен"; return false; }
                method.Invoke(controller, new[] { hero });

                // Charge the point only after the game's own controller has confirmed
                // the perk is unlocked, so failed attempts do not spend currency.
                if (!IsLearned(hero, id))
                { error = "Игра отказалась активировать перк"; return false; }
                if (!GameBridge.Set(hero, "PerksPoints", points - 1))
                {
                    // Roll back only our playable-unit unlocker; never remove an
                    // existing item-granted perk or the vanilla tree's perk object.
                    var lockMethod=controller.GetType().GetMethods().FirstOrDefault(m =>
                        m.Name == "Lock" && m.GetParameters().Length == 1 &&
                        m.GetParameters()[0].ParameterType.IsInstanceOfType(hero));
                    try {
                        lockMethod?.Invoke(controller, new[] { hero });
                        if (!wasExisting && perks.Contains(id)) perks.Remove(id);
                    }
                    catch (Exception rollbackError) {
                        log.LogWarning("Eternal Hero perk rollback failed: " +
                            rollbackError.GetBaseException().Message);
                    }
                    error = "Не удалось списать очко — покупка отменена";
                    return false;
                }
                log.LogInfo("Eternal Hero: purchased off-tree perk " + id +
                    " for 1 original perk point; remaining " + (points - 1));
                GameBridge.RefreshHeroPresentation(hero, log);
                return true;
            }
            catch (Exception e)
            {
                error = "Ошибка игрового API: " + e.GetBaseException().Message;
                log.LogWarning("Eternal Hero off-tree perk " + id + ": " + e);
                return false;
            }
        }

        // Uses the same PerkController/native-perk path as the game's existing Glyph perks.
        // Never unlocks the perks of companions.
        internal static bool UnlockAllTiers(object hero, HeroProfile profile, ManualLogSource log)
        {
            if (hero == null || profile == null || !profile.UnlockAllPerkTiers) return false;
            try
            {
                // The Last Spell's controller already provides a tier-unlock operation.
                object tree = GameBridge.Get(hero, "PerkTree");
                object controller = GameBridge.Get(tree, "UnitPerkTreeController");
                if (controller == null) return false;
                var method = controller.GetType().GetMethod("DebugUpdateAllTiersAvailability",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (method == null) return false;
                method.Invoke(controller, null);
                return true;
            }
            catch (Exception e)
            {
                log.LogWarning("Eternal Hero unlock all perk tiers failed: " + e.GetBaseException().Message);
                return false;
            }
        }

        // PerkController.Unlock normally calls TryAddPerk itself. However, in
        // loaded/procedurally generated trees a perk which is not in the rolled
        // columns can lack the registration made by UnitPerkTreeController.
        // Register the *real game Perk object* in the protagonist's own
        // PlayableUnitPerksController before unlocking it. No perk points,
        // inventory slots or columns are required for a permanent perk.
        private static bool RegisterRealPerk(object hero, object perk, string id,
            ManualLogSource log, out string reason)
        {
            reason=null;
            var controller=GameBridge.Get(hero,"PlayableUnitPerksController");
            if(controller==null)
            {
                reason="Герой ещё не инициализировал PlayableUnitPerksController";
                return false;
            }
            var method=controller.GetType().GetMethods().FirstOrDefault(m=>
                m.Name=="TryAddPerk"&&m.GetParameters().Length==1&&
                m.GetParameters()[0].ParameterType.IsInstanceOfType(perk));
            if(method==null)
            {
                reason="Не найден штатный PlayableUnitPerksController.TryAddPerk";
                return false;
            }
            try
            {
                method.Invoke(controller,new[]{perk});
                var dictionary=GameBridge.Get(hero,"Perks") as IDictionary;
                if(dictionary==null||!dictionary.Contains(id))
                {
                    string actual=GameBridge.Get(GameBridge.Get(perk,"PerkDefinition"),"Id")?.ToString();
                    reason="Игровой реестр не содержит "+id+" после TryAddPerk"+
                        (actual==id?"":" (ID созданного перка: "+(actual??"?")+")");
                    return false;
                }
                return true;
            }
            catch(Exception ex)
            {
                reason="Ошибка регистрации перка: "+ex.GetBaseException().Message;
                log.LogWarning("Eternal Hero perk registry "+id+": "+ex);
                return false;
            }
        }

        private static string ExplainPerkState(object hero,string id)
        {
            var dictionary=GameBridge.Get(hero,"Perks") as IDictionary;
            if(dictionary==null) return "Игровой словарь Perks недоступен";
            if(!dictionary.Contains(id)) return "Перка нет в реестре героя";
            var perk=dictionary[id];
            bool unlocked=GameBridge.Get(perk,"Unlocked") is bool b && b;
            bool heroUnlocker=GameBridge.Get(perk,"IsUnlockedFromPlayableUnit") is bool a && a;
            bool itemUnlocker=GameBridge.Get(perk,"IsUnlockedFromItem") is bool c && c;
            bool isNative=GameBridge.Get(perk,"IsNative") is bool n && n;
            return "Запись есть; активен="+unlocked+"; владелец="+heroUnlocker+
                "; от предмета="+itemUnlocker+"; собственный="+isNative;
        }

        private static bool TryUnlockForHero(object controller,object hero,string id,
            ManualLogSource log,out string error)
        {
            error=null;
            var method=controller?.GetType().GetMethods().FirstOrDefault(m=>
                m.Name=="Unlock"&&m.GetParameters().Length==1&&
                m.GetParameters()[0].ParameterType.IsInstanceOfType(hero));
            if(method==null)
            {
                error="PerkController.Unlock(IPerkUnlocker) не найден";
                return false;
            }
            try
            {
                method.Invoke(controller,new[]{hero});
                if(IsLearned(hero,id))return true;
                error="Unlock вызван, но перк не активировался: "+
                    ExplainPerkState(hero,id);
                return false;
            }
            catch(Exception ex)
            {
                // Some special perks throw in their UI refresh after the actual
                // stat/event hooks have been applied; verify the effect state
                // instead of blindly reporting a successful or failed unlock.
                if(IsLearned(hero,id))
                {
                    log.LogWarning("Eternal Hero perk "+id+
                        ": unlocked, but UI follow-up threw "+ex.GetBaseException().Message);
                    return true;
                }
                error="Ошибка PerkController.Unlock: "+ex.GetBaseException().Message+
                    "; "+ExplainPerkState(hero,id);
                return false;
            }
        }

        internal static string Grant(object hero, HeroProfile profile, ManualLogSource log)
        {
            if(hero==null||profile==null) return "Герой ещё не загружен";
            UnlockAllTiers(hero,profile,log);
            var selected=profile.SelectedPerks;
            if(selected==null || selected.Count==0)
                return "Постоянные перки не выбраны";
            var db=GameBridge.StaticGet(Database,"PerkDefinitions") as IDictionary;
            if(db==null)return "База PerkDefinitions пока не загружена";
            var type=AccessTools.TypeByName("TheLastStand.Controller.Unit.Perk.PerkController");
            if(type==null)return "PerkController отсутствует";
            var existing=GameBridge.Get(hero,"Perks") as IDictionary;
            if(existing==null)return "Игровой список Perks героя не готов";
            int active=0;
            var problems=new List<string>();
            foreach(var id in selected.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct())
            {
                if(!db.Contains(id))
                {
                    const string missing="ID отсутствует в текущей игровой базе";
                    NoteGrantFailure(id,missing,log);
                    problems.Add(id+": "+missing);
                    continue;
                }
                if(IsLearned(hero,id))
                {
                    LastGrantFailures.Remove(id);
                    active++;
                    continue;
                }
                try
                {
                    object controller=null;
                    if(existing.Contains(id))
                    {
                        var perk=existing[id];
                        controller=GameBridge.Get(perk,"PerkController");
                        if(controller==null)
                        {
                            var errorNote="Существующий Perk не содержит PerkController";
                            NoteGrantFailure(id,errorNote,log);
                            problems.Add(id+": "+errorNote);
                            continue;
                        }
                    }
                    else
                    {
                        object definition=db[id];
                        controller=CreateOffTreeController(definition,hero,existing,log,
                            out string constructReason);
                        if(controller==null)
                        {
                            NoteGrantFailure(id,constructReason,log);
                            problems.Add(id+": "+constructReason);
                            continue;
                        }
                        object perk=GameBridge.Get(controller,"Perk");
                        if(perk==null)
                        {
                            var errorNote="Игра не создала объект Perk";
                            NoteGrantFailure(id,errorNote,log);
                            problems.Add(id+": "+errorNote);
                            continue;
                        }
                        // Explicit registration handles all perk definitions,
                        // including those absent from randomized tier columns.
                        if(!RegisterRealPerk(hero,perk,id,log,out string registrationError))
                        {
                            NoteGrantFailure(id,registrationError,log);
                            problems.Add(id+": "+registrationError);
                            continue;
                        }
                    }
                    if(TryUnlockForHero(controller,hero,id,log,out string error))
                    {
                        active++;
                        LastGrantFailures.Remove(id);
                        log.LogInfo("Eternal Hero permanent perk ACTIVE: "+id+"; "+
                            ExplainPerkState(hero,id));
                    }
                    else
                    {
                        NoteGrantFailure(id,error,log);
                        problems.Add(id+": "+error);
                    }
                }
                catch(Exception ex)
                {
                    string why=ex.GetBaseException().Message+
                        "; "+ExplainPerkState(hero,id);
                    NoteGrantFailure(id,why,log);
                    problems.Add(id+": "+why);
                    log.LogWarning("Eternal Hero failed perk "+id+" full trace: "+ex);
                }
            }
            string result="Активно "+active+"/"+selected.Distinct().Count();
            if(problems.Count>0)result+=" • Ошибки: "+
                string.Join(" | ",problems.Take(3).ToArray());
            return result;
        }

    }
}
