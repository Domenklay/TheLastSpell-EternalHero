using System;
using System.Reflection;
using System.Collections;
using BepInEx.Logging;
using HarmonyLib;

namespace EternalHero.Plugin
{
    // Does not change game definitions, race ids, stat bases or companions.
    // Racial negative modifiers are masked at their READ point only, so changing
    // race in the original editor continues to remove and apply correct values.
    internal static class RacialPenaltyGuard
    {
        private static bool IsOurHero(object hero)
        {
            return hero != null && Plugin.Instance != null &&
                Plugin.Instance.IgnoreRacialPenalties &&
                ReferenceEquals(hero, Plugin.Instance.CurrentHero);
        }

        internal static void Install(Harmony harmony, ManualLogSource log)
        {
            PatchGetter(harmony, log,
                "TheLastStand.Model.Unit.Perk.PerkEffect.StatModifierEffect",
                nameof(NullifyNegativeRacialPerk));
            PatchGetter(harmony, log,
                "TheLastStand.Model.Unit.Perk.PerkEffect.DynamicStatsModifierEffect",
                nameof(NullifyNegativeRacialPerk));
            try
            {
                var stat = AccessTools.TypeByName("TheLastStand.Model.Unit.Stat.PlayableUnitStat");
                var getter = stat?.GetProperty("Final",
                    BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
                var postfix = AccessTools.Method(typeof(RacialPenaltyGuard), nameof(NullifyRaceStatFinal));
                if (getter != null && postfix != null)
                {
                    harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
                    log.LogInfo("Eternal Hero: negative static race stats are compensated.");
                }
            }
            catch (Exception e) { log.LogWarning("Race final stat patch: " + e.Message); }
        }

        private static void PatchGetter(Harmony harmony, ManualLogSource log,
            string typeName, string handler)
        {
            try
            {
                var type = AccessTools.TypeByName(typeName);
                var getter = type?.GetProperty("Value",
                    BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod();
                var callback = AccessTools.Method(typeof(RacialPenaltyGuard), handler);
                if (getter == null || callback == null)
                    log.LogWarning("Racial perk modifier getter unavailable: " + typeName);
                else
                {
                    harmony.Patch(getter, postfix: new HarmonyMethod(callback));
                    log.LogInfo("Eternal Hero: masked negative racial perk values in " + typeName);
                }
            }
            catch (Exception e) { log.LogWarning("Racial modifier patch: " + e.Message); }
        }

        private static void NullifyNegativeRacialPerk(object __instance, ref float __result)
        {
            if (__result >= 0f || float.IsNaN(__result) || float.IsInfinity(__result)) return;
            var module = GameBridge.Get(__instance, "APerkModule");
            var perk = GameBridge.Get(module, "Perk");
            if (perk == null || !(GameBridge.Get(perk, "IsFromRace") is bool racial) || !racial)
                return;
            if (IsOurHero(GameBridge.Get(perk, "Owner")))
                __result = 0f;
        }

        internal static string RaceSummary(object hero)
        {
            if(hero==null) return "нет героя";
            try
            {
                var controller=GameBridge.Get(hero,"PlayableUnitStatsController");
                var stats=GameBridge.Get(controller,"UnitStats");
                var values=GameBridge.Get(stats,"Stats") as IDictionary;
                if(values==null) return "данные характеристик пока недоступны";
                int negative=0;
                foreach(DictionaryEntry item in values)
                {
                    object raw=GameBridge.Get(item.Value,"Race");
                    if(raw==null) continue;
                    float value=Convert.ToSingle(raw);
                    if(value<0f && !float.IsNaN(value)) negative++;
                }
                return negative+" отрицательных статовых модификаторов "+
                    (IsOurHero(hero)?"(компенсируются в расчёте)":"(не изменены)");
            }
            catch(Exception e) { return "диагностика недоступна: "+e.GetBaseException().Message; }
        }

        private static void NullifyRaceStatFinal(object __instance, ref float __result)
        {
            var raceValue = GameBridge.Get(__instance, "Race");
            if (raceValue == null) return;
            float negative;
            try { negative = Convert.ToSingle(raceValue); }
            catch { return; }
            if (negative >= 0f || float.IsNaN(negative)) return;
            // PlayableUnitStat inherits UnitStatsController from UnitStat. There is
            // NO PlayableUnitStatsController property on the stat itself: the old
            // lookup was always null and racial-stat suppression never ran.
            var controller = GameBridge.Get(__instance, "UnitStatsController");
            var stats = GameBridge.Get(controller, "UnitStats");
            var unit = GameBridge.Get(stats, "Unit");
            if (IsOurHero(unit)) __result -= negative;
        }
    }
}
