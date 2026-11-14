using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using EternalHero.Core;
using HarmonyLib;
using UnityEngine;

namespace EternalHero.Plugin
{
    [BepInPlugin(Id, "Eternal Hero / Вечный защитник", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "domenklay.tls.eternalhero";
        internal const string Version = "0.1.0";
        internal static Plugin Instance;
        private Harmony harmony;
        private ProfileStore store;
        private ProgressionService progression;
        private ConfigEntry<bool> enableTestRewards;
        private ConfigEntry<bool> experimentalStatInjection;
        private bool visible;
        private Vector2 scroll;
        private Rect bounds = new Rect(60, 60, 640, 640);
        private string lastMessage = "";
        private string rosterStatus = "The start-roster hook has not fired yet.";

        private void Awake()
        {
            Instance = this;
            enableTestRewards = Config.Bind("Development", "EnableTestRewards", false,
                "Enable manual debug XP in the F8 panel (NOT intended for normal gameplay).");
            experimentalStatInjection = Config.Bind("Experimental", "ApplyStatBonuses", false,
                "Danger: try applying bonuses via reflection. Leave off until verified against your game version.");
            try
            {
                store = new ProfileStore(Path.Combine(Paths.ConfigPath, "EternalHero", "profile.json"));
                progression = new ProgressionService(store.Load());
                harmony = new Harmony(Id);
                // This method has been verified in the public Omen of Specialists mod.
                var target = AccessTools.Method("TheLastStand.Manager.Unit.PlayableUnitManager:CreateStartUnits");
                if (target == null)
                {
                    Logger.LogWarning("CreateStartUnits was not found. UI/profile will still work.");
                    rosterStatus = "Start-roster hook not found (game version?)";
                }
                else
                {
                    var post = AccessTools.Method(typeof(Plugin), nameof(AfterStartUnits));
                    harmony.Patch(target, postfix: new HarmonyMethod(post));
                    Logger.LogInfo("Patched PlayableUnitManager.CreateStartUnits");
                    rosterStatus = "Start-roster hook installed; awaiting run creation.";
                }
                Logger.LogInfo("Eternal Hero v" + Version + " loaded. F8 opens progression.");
            }
            catch (Exception e) { Logger.LogError(e); }
        }

        private static void AfterStartUnits(object __instance)
        {
            if (Instance != null && __instance != null)
                Instance.StartCoroutine(Instance.InspectAfterUiReady(__instance));
        }

        private IEnumerator InspectAfterUiReady(object manager)
        {
            yield return null;
            yield return null;
            try
            {
                var hero = HeroRuntime.TryGetFirstHero(manager);
                if (hero == null)
                {
                    rosterStatus = "Starting units created, but first hero not resolved. See log.";
                    Logger.LogWarning("Could not resolve hero from manager " + manager.GetType().FullName
                        + ". Candidate members: " + HeroRuntime.DiagnosticMembers(manager));
                    yield break;
                }

                rosterStatus = "Candidate first hero: " + hero.GetType().FullName;
                var named = HeroRuntime.TrySetHeroName(hero, progression.Profile.HeroName);
                Logger.LogInfo("Identified starting hero; fixed name applied: " + named);
                if (experimentalStatInjection.Value)
                {
                    int applied = HeroRuntime.TryApplyBonuses(hero, progression.CalculatedStatBonuses(), Logger);
                    Logger.LogWarning("EXPERIMENTAL stat adjustments attempted: " + applied);
                    rosterStatus += "; applied stat calls: " + applied;
                }
                else rosterStatus += "; stat bridge disabled for safety";
            }
            catch (Exception ex)
            {
                rosterStatus = "Roster inspection failed; see log";
                Logger.LogError(ex);
            }
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F8)) visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible || progression == null) return;
            bounds.x = Mathf.Clamp(bounds.x, 0, Mathf.Max(0, Screen.width - 120));
            bounds.y = Mathf.Clamp(bounds.y, 0, Mathf.Max(0, Screen.height - 45));
            bounds = GUI.Window(187113, bounds, DrawWindow, "Eternal Hero — Наследие");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Персонаж: " + progression.Profile.HeroName);
            GUILayout.Label("Общий опыт: " + progression.Profile.LifetimeExperience
                + "   |   Эссенция: " + progression.Profile.AvailableEssence);
            GUILayout.Label("Следующее очко за каждые " + ProgressionService.ExperiencePerEssence + " опыта");
            GUILayout.Label("Статус: " + rosterStatus);
            GUILayout.Label("Версия 0.1: профиль и панель работают; автоматические награды и специальные боевые умения ещё не подключены.");
            if (enableTestRewards.Value && GUILayout.Button("DEV: +100 опыта"))
            {
                try
                {
                    if (progression.GrantExperience("dev:" + Guid.NewGuid().ToString("N"), 100))
                    {
                        store.Save(progression.Profile);
                        lastMessage = "Добавлено 100 тестового опыта";
                    }
                }
                catch (Exception ex) { lastMessage = ex.Message; Logger.LogError(ex); }
            }
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            foreach (var branch in UpgradeCatalog.All.Select(u => u.Branch).Distinct())
            {
                GUILayout.Space(7);
                GUILayout.Label("══ " + branch + " ══");
                foreach (var upgrade in UpgradeCatalog.All.Where(u => u.Branch == branch))
                {
                    int rank = progression.Rank(upgrade.Id);
                    string label = upgrade.Name + " [" + rank + "/" + upgrade.MaxRank + "] — " + upgrade.Description;
                    GUILayout.Label(label);
                    if (upgrade.RequiresGameIntegration)
                        GUILayout.Label("Реализация игрового эффекта: в разработке. Покупка уже сохраняется.");
                    if (rank >= upgrade.MaxRank) continue;
                    bool canBuy = progression.CanPurchase(upgrade.Id, out var reason);
                    GUI.enabled = canBuy;
                    if (GUILayout.Button("Изучить за " + upgrade.CostForNextRank(rank) + " эссенции"))
                    {
                        try
                        {
                            if (progression.Purchase(upgrade.Id, out reason))
                            {
                                store.Save(progression.Profile);
                                lastMessage = "Изучено: " + upgrade.Name;
                            }
                        }
                        catch (Exception ex) { lastMessage = ex.Message; Logger.LogError(ex); }
                    }
                    GUI.enabled = true;
                    if (!canBuy) GUILayout.Label("Недоступно: " + reason);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.Label(lastMessage);
            if (GUILayout.Button("Закрыть (F8)")) visible = false;
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, bounds.width, 24));
        }

        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
    }
}
