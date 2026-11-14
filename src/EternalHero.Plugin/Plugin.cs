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
        internal const string Version = "0.3.0";
        internal static Plugin Instance;
        private Harmony harmony;
        private ProfileStore store;
        private ProgressionService progression;
        private RunRewards runRewards;
        private EternalHeroSkin skin;
        private object currentHero;
        private int activeTab;
        private bool showDiagnostics;
        private int appearanceFields;
        private string identityStatus = "Герой ещё не определён";
        private readonly string[] branchNames = {
            "Стальное тело", "Мастер оружия", "Тактический гений", "Наследие", "Запретные техники"
        };
        private readonly string[] branchLabels = {
            "Тело", "Оружие", "Тактика", "Наследие", "Техники"
        };
        private ConfigEntry<bool> enableTestRewards;
        private ConfigEntry<bool> experimentalStatInjection;
        private ConfigEntry<bool> openMenuOnStartup;
        private ConfigEntry<bool> showLauncherButton;
        private ConfigEntry<bool> useGameUISprites;
        private bool visible;
        private bool guiSeen;
        private bool guiErrorLogged;
        private int lastToggleFrame = -1;
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
            openMenuOnStartup = Config.Bind("Interface", "OpenMenuOnStartup", true,
                "Open Eternal Hero progression menu when the game launches (useful if hotkeys fail).");
            showLauncherButton = Config.Bind("Interface", "ShowLauncherButton", true,
                "Show an always-visible top-right button to open and close the progression menu.");
            useGameUISprites = Config.Bind("Interface", "UseGameUISprites", true,
                "Try reusing already-loaded game panel sprites (with custom styling fallback).");
            visible = openMenuOnStartup.Value;
            skin = new EternalHeroSkin(Logger, useGameUISprites.Value);
            try
            {
                store = new ProfileStore(Path.Combine(Paths.ConfigPath, "EternalHero", "profile.json"));
                progression = new ProgressionService(store.Load());
                runRewards = new RunRewards(progression);
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
                Logger.LogInfo("Eternal Hero v" + Version +
                    " loaded. UI starts open=" + visible +
                    "; hotkeys F8/F9/Home, top-right launcher enabled=" + showLauncherButton.Value);
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
                currentHero = hero;
                bool captured;
                appearanceFields = HeroIdentity.CaptureOrRestore(progression.Profile, hero, out captured);
                identityStatus = captured
                    ? "Запомнены параметры внешности: " + appearanceFields
                    : "Восстановлены параметры внешности: " + appearanceFields;
                if (captured)
                    store.Save(progression.Profile);
                Logger.LogInfo("Eternal Hero appearance: " + identityStatus);
                var stableId = HeroRunIdentity.TryGetStableHeroId(hero);
                if (!string.IsNullOrWhiteSpace(stableId))
                {
                    bool isNewRun = runRewards.Begin(stableId);
                    if (isNewRun) store.Save(progression.Profile);
                    Logger.LogInfo("Eternal Hero provisional run key: " + stableId +
                        "; new run=" + isNewRun + "; requires reload/new-run verification");
                    rosterStatus += "; provisional run key found";
                }
                else
                {
                    Logger.LogWarning("Eternal Hero could not resolve stable game run ID. " +
                        "Automatic per-run XP disabled until game API verified.");
                    rosterStatus += "; no stable run key";
                }
                if (!progression.Profile.FirstRunGiftClaimed)
                {
                    // One-time onboarding grant; never repeat on game restarts.
                    if (progression.GrantExperience("career:welcome-v1", 100))
                    {
                        progression.Profile.FirstRunGiftClaimed = true;
                        store.Save(progression.Profile);
                        lastMessage = "Первый дар наследия: +100 опыта (1 эссенция)";
                        Logger.LogInfo("Eternal Hero: first-run legacy gift awarded once");
                    }
                }

                var named = HeroRuntime.TrySetHeroName(hero, progression.Profile.HeroName);
                Logger.LogInfo("Identified starting hero; fixed name applied: " + named +
                    "; type=" + hero.GetType().FullName);
                Logger.LogInfo("Hero stat bridge: " + HeroRuntime.DiagnosticStatSurface(hero));
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

        private void ToggleMenu(string source)
        {
            // IMGUI and Input.GetKeyDown can both observe the same key in one frame.
            if (lastToggleFrame == Time.frameCount) return;
            lastToggleFrame = Time.frameCount;
            visible = !visible;
            Logger.LogInfo("Eternal Hero menu " + (visible ? "OPEN" : "CLOSED") + " via " + source);
        }

        private void Update()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F8) ||
                    Input.GetKeyDown(KeyCode.F9) ||
                    Input.GetKeyDown(KeyCode.Home))
                    ToggleMenu("Unity Input");
            }
            catch (Exception e)
            {
                if (!guiErrorLogged)
                {
                    guiErrorLogged = true;
                    Logger.LogError("Eternal Hero keyboard check failed: " + e);
                }
            }
        }

        private void OnGUI()
        {
            try
            {
                if (!guiSeen)
                {
                    guiSeen = true;
                    Logger.LogInfo("Eternal Hero OnGUI reached. Resolution " + Screen.width + "x" + Screen.height);
                }

                // Fallback for games which consume Input.GetKeyDown before our Update.
                var current = Event.current;
                if (current != null && current.type == EventType.KeyDown &&
                    (current.keyCode == KeyCode.F8 ||
                     current.keyCode == KeyCode.F9 ||
                     current.keyCode == KeyCode.Home))
                {
                    ToggleMenu("IMGUI KeyDown");
                    current.Use();
                }

                // Always-rendered mouse control, independent of the progression window.
                if (showLauncherButton != null && showLauncherButton.Value)
                {
                    var launcher = new Rect(
                        Mathf.Max(4f, Screen.width - 206f),
                        8f, 198f, 36f);
                    if (GUI.Button(launcher, visible ? "Eternal Hero - CLOSE" : "Eternal Hero - OPEN"))
                        ToggleMenu("mouse launcher");
                }

                if (!visible || progression == null) return;
                bounds.width = Mathf.Min(640f, Mathf.Max(280f, Screen.width - 16f));
                bounds.height = Mathf.Min(640f, Mathf.Max(180f, Screen.height - 56f));
                bounds.x = Mathf.Clamp(bounds.x, 0, Mathf.Max(0, Screen.width - bounds.width));
                bounds.y = Mathf.Clamp(bounds.y, 0, Mathf.Max(0, Screen.height - bounds.height));
                bounds = GUI.Window(187113, bounds, DrawWindow, "ВЕЧНЫЙ ЗАЩИТНИК — НАСЛЕДИЕ", skin.Window);
            }
            catch (Exception ex)
            {
                if (!guiErrorLogged)
                {
                    guiErrorLogged = true;
                    Logger.LogError("Eternal Hero OnGUI error: " + ex);
                }
            }
        }

        private void DrawWindow(int id)
        {
            // Rich backing uses game-loaded UI sprites when identifiable, never exported as assets.
            skin.DrawGamePanel(new Rect(3, 24, bounds.width - 6, bounds.height - 28));
            GUILayout.BeginVertical();
            try
            {
                GUILayout.Label(progression.Profile.HeroName, skin.Title);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Постоянный опыт: " + progression.Profile.LifetimeExperience, skin.Label);
                GUILayout.Label("Эссенция: " + progression.Profile.AvailableEssence, skin.Header);
                GUILayout.EndHorizontal();
                long remainder = progression.Profile.LifetimeExperience % ProgressionService.ExperiencePerEssence;
                skin.Progress((float)remainder / ProgressionService.ExperiencePerEssence);
                GUILayout.Label("До следующего очка наследия: " +
                    (ProgressionService.ExperiencePerEssence - remainder) + " опыта", skin.Tiny);
                GUILayout.Label("Забеги: " + progression.Profile.RunsStarted +
                    "   |   Победы: " + progression.Profile.Victories +
                    "   |   Пережито ночей: " + progression.Profile.TotalNightsSurvived,
                    skin.Tiny);

                skin.DrawDivider(bounds.width);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < branchLabels.Length; i++)
                {
                    int ix = i;
                    var style = activeTab == ix ? skin.Selected : skin.Button;
                    if (GUILayout.Button(branchLabels[ix], style, GUILayout.MinHeight(34)))
                    {
                        activeTab = ix;
                        scroll = Vector2.zero;
                    }
                }
                GUILayout.EndHorizontal();
                skin.DrawDivider(bounds.width);
                GUILayout.Label(branchNames[activeTab].ToUpperInvariant(), skin.Header);

                scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
                try
                {
                    foreach (var upgrade in UpgradeCatalog.All.Where(u => u.Branch == branchNames[activeTab]))
                    {
                        int rank = progression.Rank(upgrade.Id);
                        GUILayout.BeginVertical(skin.Card);
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(upgrade.Name, skin.Header);
                        GUILayout.FlexibleSpace();
                        GUILayout.Label(rank + " / " + upgrade.MaxRank, skin.Label, GUILayout.Width(65));
                        GUILayout.EndHorizontal();
                        GUILayout.Label(upgrade.Description, skin.Label);
                        if (upgrade.RequiresGameIntegration)
                            GUILayout.Label("Особая механика: требуется игровая интеграция", skin.Tiny);
                        if (rank < upgrade.MaxRank)
                        {
                            var canBuy = progression.CanPurchase(upgrade.Id, out var reason);
                            bool enabled = GUI.enabled;
                            GUI.enabled = canBuy;
                            if (GUILayout.Button("Изучить • " +
                                upgrade.CostForNextRank(rank) + " эссенции", skin.Button,
                                GUILayout.MinHeight(30)))
                            {
                                try
                                {
                                    if (progression.Purchase(upgrade.Id, out reason))
                                    {
                                        store.Save(progression.Profile);
                                        lastMessage = "Открыто: " + upgrade.Name;
                                        Logger.LogInfo("Purchased " + upgrade.Id + " rank " + progression.Rank(upgrade.Id));
                                        if (currentHero != null && experimentalStatInjection.Value)
                                            ApplyCurrentBonuses();
                                    }
                                }
                                catch (Exception ex) { lastMessage = ex.Message; Logger.LogError(ex); }
                            }
                            GUI.enabled = enabled;
                            if (!canBuy)
                                GUILayout.Label(ReasonInRussian(reason), skin.Tiny);
                        }
                        else GUILayout.Label("ИЗУЧЕНО", skin.Header);
                        GUILayout.EndVertical();
                        GUILayout.Space(5);
                    }
                }
                finally { GUILayout.EndScrollView(); }

                if (GUILayout.Button(showDiagnostics ? "Скрыть диагностику" : "Информация и диагностика",
                    skin.Button, GUILayout.Height(29))) showDiagnostics = !showDiagnostics;
                if (showDiagnostics)
                {
                    GUILayout.Label(rosterStatus, skin.Tiny);
                    GUILayout.Label(identityStatus, skin.Tiny);
                    GUILayout.Label("Привязка забега: " +
                        (string.IsNullOrWhiteSpace(progression.Profile.CurrentRunId) ? "не обнаружена" :
                         "обнаружен предварительный идентификатор"), skin.Tiny);
                    GUILayout.Label("Игровой статус навыков: определения XML есть; привязка к ГГ ещё не сделана", skin.Tiny);
                    GUILayout.Label("Текстура из игры: " + (skin.UsingGameSprite ? "найдена" : "не найдена, используется запасная"), skin.Tiny);
                    GUILayout.Label("Применение статов: " + (experimentalStatInjection.Value ? "тестовое" : "отключено"), skin.Tiny);
                    if (currentHero != null)
                    {
                        if (GUILayout.Button("Попробовать применить купленные бонусы к герою", skin.Button))
                            ApplyCurrentBonuses();
                    }
                    else GUILayout.Label("Сначала начни новый забег: герой ещё не обнаружен", skin.Tiny);
                    if (enableTestRewards.Value)
                    {
                        if (GUILayout.Button("DEV: +100 опыта", skin.Button))
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
                    }
                    GUILayout.Label("Особые навыки пока не прикреплены к боевой панели. Установленные ранги сохраняются.", skin.Tiny);
                }
                if (!string.IsNullOrWhiteSpace(lastMessage))
                    GUILayout.Label(lastMessage, skin.Header);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Свернуть", skin.Button, GUILayout.MinHeight(29)))
                    visible = false;
                GUILayout.EndHorizontal();
            }
            catch (Exception e)
            {
                Logger.LogError("Eternal Hero DrawWindow failed: " + e);
                GUILayout.Label("Ошибка интерфейса: проверь BepInEx/LogOutput.log", skin.Warning);
            }
            finally { GUILayout.EndVertical(); }
            GUI.DragWindow(new Rect(0, 0, bounds.width, 24));
        }

        private static string ReasonInRussian(string reason)
        {
            switch (reason)
            {
                case "Not enough essence": return "Недостаточно эссенции";
                case "Prerequisite not met": return "Сначала изучи предыдущий талант";
                case "Already max rank": return "Максимальный ранг";
                default: return reason ?? "";
            }
        }

        private void ApplyCurrentBonuses()
        {
            if (!experimentalStatInjection.Value)
            {
                lastMessage = "В настройках включи Experimental > ApplyStatBonuses = true, если согласен тестировать на отдельном сохранении";
                return;
            }
            try
            {
                int applied = HeroRuntime.TryApplyBonuses(
                    currentHero, progression.CalculatedStatBonuses(), Logger);
                lastMessage = "Изменено характеристик: " + applied +
                    ". Проверь характеристики ГГ и BepInEx/LogOutput.log";
                Logger.LogInfo(lastMessage);
            }
            catch (Exception ex) { lastMessage = ex.Message; Logger.LogError(ex); }
        }

        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
    }
}
