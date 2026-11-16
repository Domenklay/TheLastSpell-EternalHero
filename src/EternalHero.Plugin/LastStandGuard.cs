using System;
using System.Reflection;
using BepInEx.Logging;
using EternalHero.Core;
using HarmonyLib;
using UnityEngine;

namespace EternalHero.Plugin
{
    internal static class LastStandGuard
    {
        private static ManualLogSource logger;

        internal static void Install(Harmony harmony, ManualLogSource log)
        {
            logger=log;
            var type=AccessTools.TypeByName("TheLastStand.Controller.Unit.PlayableUnitController");
            var method=AccessTools.Method(type,"LoseHealth",new[]{
                typeof(float),AccessTools.TypeByName("TheLastStand.Model.ISkillCaster"),typeof(bool)});
            // Resolve by signature when the interface namespace differs across game builds.
            if(method==null && type!=null)
                foreach(var m in type.GetMethods(BindingFlags.Public|BindingFlags.Instance))
                    if(m.Name=="LoseHealth"&&m.GetParameters().Length==3 &&
                        m.GetParameters()[0].ParameterType==typeof(float))
                    { method=m;break; }
            if(method==null)
            {
                log.LogWarning("Eternal Hero Last Stand: LoseHealth hook not found");
                return;
            }
            harmony.Patch(method,
                prefix:new HarmonyMethod(AccessTools.Method(typeof(LastStandGuard),nameof(BeforeLoseHealth))),
                postfix:new HarmonyMethod(AccessTools.Method(typeof(LastStandGuard),nameof(AfterLoseHealth))));
            log.LogInfo("Eternal Hero: Last Stand death-prevention combat hook installed.");
        }

        private static void BeforeLoseHealth(object __instance, ref float amount, out float __state)
        {
            __state=0f;
            var plugin=Plugin.Instance;
            if(plugin==null||plugin.Profile==null||plugin.CurrentHero==null) return;
            object hero=GameBridge.Get(__instance,"PlayableUnit");
            if(!ReferenceEquals(hero,plugin.CurrentHero)) return;
            try
            {
                var gameManager=GameBridge.Singleton("TheLastStand.Manager.GameManager");
                var game=GameBridge.Get(gameManager,"Game");
                bool night=GameBridge.Get(game,"Cycle")?.ToString()=="Night";
                object dayObj=GameBridge.Get(game,"DayNumber") ??
                    GameBridge.Get(gameManager,"DayNumber");
                if(dayObj==null)return;
                int day=Convert.ToInt32(dayObj);
                float hp=Convert.ToSingle(GameBridge.Get(hero,"Health")??0f);
                if(!LastStandPolicy.TryConsume(plugin.Profile,day,hp,amount,night))return;

                // Preserve the game's regular damage processing and all feedback,
                // but reduce the lethal amount so the hero remains on 1 HP.
                amount=Mathf.Max(0,hp-1f);
                __state=Convert.ToSingle(GameBridge.Get(hero,"HealthTotal")??hp);
                plugin.SaveProfile();
                logger?.LogInfo("Eternal Hero Last Stand triggered: day="+day+
                    ", prevented lethal damage; hero will recover to 30% HP");
            }
            catch(Exception ex)
            {
                __state=0f;
                logger?.LogWarning("Last Stand prefix: "+ex.GetBaseException().Message);
            }
        }

        private static void AfterLoseHealth(object __instance, float __state)
        {
            if(__state<=0f)return;
            try
            {
                object hero=GameBridge.Get(__instance,"PlayableUnit");
                if(hero==null)return;
                float hp=Convert.ToSingle(GameBridge.Get(hero,"Health")??0f);
                float max=Convert.ToSingle(GameBridge.Get(hero,"HealthTotal")??__state);
                float target=Mathf.Min(max,LastStandPolicy.MinimumRecovery(max));
                if(hp<target) GameBridge.Set(hero,"Health",target);
                var view=GameBridge.Get(hero,"PlayableUnitView");
                var refresh=view?.GetType().GetMethod("RefreshHealth",
                    BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
                if(refresh!=null && refresh.GetParameters().Length==0)
                    refresh.Invoke(view,null);
            }
            catch(Exception ex){logger?.LogWarning("Last Stand recovery: "+ex.GetBaseException().Message);}
        }
    }
}
