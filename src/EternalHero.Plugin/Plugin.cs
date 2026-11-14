using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EternalHero.Core;
using HarmonyLib;
using UnityEngine;

namespace EternalHero.Plugin
{
    [BepInPlugin(Id, "Eternal Hero / Вечный защитник", Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal const string Id = "domenklay.tls.eternalhero";
        internal const string Version = "0.4.0";

        private Harmony harmony;
        private ProfileStore store;
        private ProgressionService progression;
        private HeroScreen screen;
        private RunRewards runRewards;
        private ConfigEntry<bool> applyBonuses;
        private ConfigEntry<bool> debugRewards;
        private ConfigEntry<bool> openOnStartup;
        private object currentHero;
        private string rosterStatus = "Герой не обнаружен; начни новый забег";

        internal static Plugin Instance { get; private set; }
        internal HeroProfile Profile => progression?.Profile;
        internal ProgressionService Progression => progression;
        internal object CurrentHero => currentHero;
        internal string RosterStatus => rosterStatus;
        internal bool EnableDebugRewards => debugRewards != null && debugRewards.Value;

        private void Awake()
        {
            Instance = this;
            try
            {
                debugRewards = Config.Bind("Development", "EnableTestRewards", false,
                    "Enable the manual debug XP button.");
                applyBonuses = Config.Bind("Eternal Hero", "ApplyVerifiedStatBonuses", true,
                    "Automatically apply purchased stat upgrades to the protagonist on a NEW run.");
                openOnStartup = Config.Bind("Interface", "OpenCreatorOnStartup", true,
                    "Open the native Unity character creator on game startup.");
                store = new ProfileStore(Path.Combine(Paths.ConfigPath, "EternalHero", "profile.json"));
                progression = new ProgressionService(store.Load());
                runRewards = new RunRewards(progression);
                // One free point for initial testing, once per profile. Old v0.3 gift won't be doubled.
                if (!Profile.StarterEssenceGranted)
                {
                    if (!Profile.FirstRunGiftClaimed)
                    {
                        if (progression.GrantExperience("career:starter-v0.4", 100))
                            Logger.LogInfo("Eternal Hero: granted one starter essence for testing.");
                        Profile.FirstRunGiftClaimed = true;
                    }
                    Profile.StarterEssenceGranted = true;
                    store.Save(Profile);
                }

                harmony = new Harmony(Id);
                var start = AccessTools.Method("TheLastStand.Manager.Unit.PlayableUnitManager:CreateStartUnits");
                if (start != null)
                {
                    // IMPORTANT: CreateStartUnits is STATIC. No __instance argument can be injected.
                    harmony.Patch(start, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(Plugin), nameof(AfterStartUnits))));
                    Logger.LogInfo("Eternal Hero static CreateStartUnits hook installed.");
                }
                else Logger.LogWarning("CreateStartUnits missing on this game version.");
                var deserialize = AccessTools.Method("TheLastStand.Manager.Unit.PlayableUnitManager:Deserialize");
                if (deserialize != null)
                {
                    harmony.Patch(deserialize, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(Plugin), nameof(AfterRosterLoad))));
                    Logger.LogInfo("Eternal Hero existing-save roster detection hook installed.");
                }


                var nightXp = AccessTools.Method("TheLastStand.Manager.Unit.PlayableUnitManager:DistributeDailyExperience");
                if (nightXp != null)
                {
                    harmony.Patch(nightXp, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(Plugin), nameof(AfterNightExperience))));
                    Logger.LogInfo("Eternal Hero night XP hook installed.");
                }

                var customization = AccessTools.Method(
                    "TheLastStand.View.PlayableUnitCustomisation.PlayableUnitCustomisationPanel:OnConfirmButtonClicked");
                if (customization != null)
                {
                    harmony.Patch(customization, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(Plugin), nameof(AfterNativeAppearanceSaved))));
                    Logger.LogInfo("Eternal Hero native appearance save hook installed.");
                }
                Logger.LogInfo("Eternal Hero v" + Version + " initialized (Unity Canvas).");
            }
            catch (Exception e) { Logger.LogError("Eternal Hero boot error: " + e); }
        }

        private void Start()
        {
            try
            {
                screen = new HeroScreen(this, Logger);
                screen.Init();
                if (openOnStartup.Value) screen.Open();
                Logger.LogInfo("Eternal Hero native creator ready; click upper-right launcher.");
            }
            catch (Exception e) { Logger.LogError("Eternal Hero canvas init error: " + e); }
        }

        // Called after the game's STATIC CreateStartUnits(), not on an instance.
        private static void AfterStartUnits()
        {
            if (Instance != null) Instance.StartCoroutine(Instance.ConfigureStartingHero());
        }

        private static void AfterRosterLoad()
        {
            if (Instance != null) Instance.StartCoroutine(Instance.RecognizeLoadedHero());
        }

        private IEnumerator RecognizeLoadedHero()
        {
            yield return null;
            yield return null;
            var found = GameBridge.FirstHero();
            if (found == null || ReferenceEquals(currentHero, found)) yield break;
            currentHero = found;
            rosterStatus = "Герой найден в загруженном забеге. Сохранённые характеристики не начисляются повторно.";
            Logger.LogInfo("Eternal Hero: loaded first hero from existing save; skipping stat reapplication.");
            if (Profile != null && Profile.CreationConfirmed)
                Logger.LogInfo("Eternal Hero resumed active skills: " + GameSkills.Grant(found, Profile, Logger));
            screen?.NotifyHero();
        }

        private IEnumerator ConfigureStartingHero()
        {
            yield return null;
            yield return null;
            currentHero = GameBridge.FirstHero();
            if (currentHero == null)
            {
                rosterStatus = "Стартовая команда создана, но список героев не найден";
                Logger.LogWarning("Eternal Hero: PlayableUnits not resolved. API probe required.");
                screen?.Notify(rosterStatus);
                yield break;
            }
            Logger.LogInfo("Eternal Hero: first unit discovered " + currentHero.GetType().FullName);
            // Called only when the game generates the starting roster, not during ordinary saved-game reloads.
            // A new independent GUID cannot collide with the previous run or depend on the hero name.
            string newRunId = Guid.NewGuid().ToString("N");
            if (runRewards.Begin(newRunId))
            {
                Logger.LogInfo("Eternal Hero: created persistent run " + newRunId);
                SaveProfile();
            }
            ApplyCurrentHero();
            screen?.NotifyHero();
        }

        private static void AfterNightExperience()
        {
            if (Instance != null) Instance.RewardCompletedNight();
        }

        private void RewardCompletedNight()
        {
            try
            {
                if (runRewards == null || string.IsNullOrWhiteSpace(Profile?.CurrentRunId) ||
                    Profile.RunFinished) return;
                object game = GameBridge.Singleton("TheLastStand.Manager.GameManager");
                object dayObj = GameBridge.Get(game, "DayNumber");
                if (dayObj == null)
                {
                    Logger.LogWarning("Eternal Hero: cannot read DayNumber for night reward");
                    return;
                }
                int day = Convert.ToInt32(dayObj);
                string token = "run:" + Profile.CurrentRunId + ":game-day:" + day;
                if (Profile.ClaimedRewardIds.Contains(token)) return;
                int nextNight = Profile.LastCompletedNight + 1;
                if (runRewards.NightSurvived(nextNight))
                {
                    Profile.ClaimedRewardIds.Add(token);
                    SaveProfile();
                    Logger.LogInfo("Eternal Hero: rewarded survived night " + nextNight +
                        ", game day " + day + "; total essence=" + Profile.AvailableEssence);
                    screen?.Notify("Пережита ночь " + nextNight + ": получен постоянный опыт");
                }
            }
            catch (Exception ex) { Logger.LogError("Eternal Hero night reward: " + ex); }
        }

        private static void AfterNativeAppearanceSaved(object __instance)
        {
            if (Instance == null || Instance.currentHero == null) return;
            object edited = GameBridge.Get(__instance, "playableUnit");
            if (!ReferenceEquals(edited, Instance.currentHero)) return;
            GameBridge.CaptureEditorResult(Instance.currentHero, Instance.Profile);
            Instance.SaveProfile();
            Instance.Logger.LogInfo("Eternal Hero: native portrait, gender and name saved to profile.");
            Instance.screen?.Open();
            Instance.screen?.Notify("Облик героя сохранён и будет восстановлен в новых забегах");
        }

        internal void SaveProfile()
        {
            try { if (Profile != null) store.Save(Profile); }
            catch (Exception e) { Logger.LogError("Eternal Hero save failed: " + e); }
        }

        internal void ApplyCurrentHero()
        {
            if (currentHero == null || Profile == null) return;
            try
            {
                if (Profile.CreationConfirmed)
                {
                    string applied = GameBridge.Apply(currentHero, Profile, Logger);
                    rosterStatus = applied;
                    Logger.LogInfo("Eternal Hero applied profile: " + applied);
                }
                if (applyBonuses.Value)
                {
                    int n = HeroRuntime.TryApplyBonuses(currentHero,
                        progression.CalculatedStatBonuses(), Logger);
                    Logger.LogInfo("Eternal Hero stat upgrades applied: " + n);
                }
                string skills = GameSkills.Grant(currentHero, Profile, Logger);
                Logger.LogInfo("Eternal Hero active skills: " + skills);
                Logger.LogInfo("Eternal Hero stat API: " + HeroRuntime.DiagnosticStatSurface(currentHero));
                Logger.LogInfo("Eternal Hero ability API: " + GameApiProbe.HeroAbilityMethods(currentHero));
                Logger.LogInfo("Eternal Hero skill definitions: " + GameApiProbe.SkillDefinitions());
            }
            catch (Exception e) { rosterStatus = "Ошибка применения профиля: " + e.Message; Logger.LogError(e); }
            screen?.NotifyHero();
        }

        internal void OpenAppearanceEditor()
        {
            if (currentHero == null)
            {
                screen?.Notify("Сначала начни новый забег: редактор игры работает с существующим героем.");
                return;
            }
            screen?.Hide();
            if (!GameBridge.OpenNativeEditor(currentHero, Logger))
            {
                screen?.Open();
                screen?.Notify("Оригинальный редактор игры недоступен в текущей сцене; проверь лог.");
            }
        }

        internal void GrantDebugExperience()
        {
            if (!EnableDebugRewards) return;
            try
            {
                if (progression.GrantExperience("developer:" + Guid.NewGuid().ToString("N"), 100))
                    SaveProfile();
            }
            catch (Exception e) { Logger.LogError(e); }
        }

        private void Update()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F8) || Input.GetKeyDown(KeyCode.F9) ||
                    Input.GetKeyDown(KeyCode.Home))
                    screen?.Toggle();
            }
            catch (Exception e) { Logger.LogWarning("Creator hotkey: " + e.Message); }
        }

        private void OnDestroy()
        {
            if (harmony != null) harmony.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
    }
}
