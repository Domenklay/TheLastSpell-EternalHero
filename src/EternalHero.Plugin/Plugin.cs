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
        internal const string Version = "0.21.0";

        private Harmony harmony;
        private ProfileStore store;
        private ProgressionService progression;
        private HeroScreen screen;
        private RunRewards runRewards;
        private ConfigEntry<bool> applyBonuses;
        private ConfigEntry<bool> freeUpgradeTree;
        private ConfigEntry<bool> debugRewards;
        private ConfigEntry<bool> openOnStartup;
        private ConfigEntry<float> editorScale;
        private ConfigEntry<bool> removeRacePenalties;
        private object currentHero;
        private string rosterStatus = "Герой не обнаружен; начни новый забег";
        private float nextRosterProbeTime;
        private object lastPerkTree;
        private float nextCareerSaveTime;
        private float nextSkillSynchronization;
        private float nextPerkSynchronization;

        internal static Plugin Instance { get; private set; }
        internal HeroProfile Profile => progression?.Profile;
        internal ProgressionService Progression => progression;
        internal object CurrentHero => currentHero;
        internal string RosterStatus => rosterStatus;
        internal bool EnableDebugRewards => debugRewards != null && debugRewards.Value;
        internal float MenuScale => Mathf.Clamp(editorScale?.Value ?? .78f, .58f, 1f);
        internal bool IgnoreRacialPenalties => removeRacePenalties == null || removeRacePenalties.Value;

        private void Awake()
        {
            Instance = this;
            try
            {
                debugRewards = Config.Bind("Development", "EnableTestRewards", false,
                    "Enable the manual debug XP button.");
                applyBonuses = Config.Bind("Eternal Hero", "ApplyVerifiedStatBonuses", true,
                    "Automatically apply purchased stat upgrades to the protagonist on a NEW run.");
                freeUpgradeTree = Config.Bind("Eternal Hero", "FreeUpgradeTree", true,
                    "Buy any meta upgrade/technique regardless of prerequisites. Essence and rank costs remain.");
                openOnStartup = Config.Bind("Interface", "OpenCreatorOnStartupV06", false,
                    "Open the native Unity character creator on game startup.");
                removeRacePenalties = Config.Bind("Eternal Hero", "RemoveNegativeRaceEffects", true,
                    "Keep positive race bonuses, suppress negative racial stat and racial-perk effects on the Eternal Hero only.");
                editorScale = Config.Bind("Interface", "MenuScale", .78f,
                    "Scale of the character creator window. Allowed range: 0.58 to 1.00.");
                store = new ProfileStore(Path.Combine(Paths.ConfigPath, "EternalHero", "profile.json"));
                progression = new ProgressionService(store.Load(),freeUpgradeTree.Value);
                Logger.LogInfo("Eternal Hero free meta progression: "+progression.IgnorePrerequisites);
                int refunded=progression.RefundOldUnusableTechniques();
                if(refunded>0)
                    Logger.LogInfo("Eternal Hero: refunded "+refunded+
                        " essence for old nonworking XML technique unlocks.");
                runRewards = new RunRewards(progression);
                int legacyCompensation = runRewards.CompensateEarlyEconomy();
                if (legacyCompensation>0)
                    Logger.LogInfo("Eternal Hero: economy v0.11 compensation awarded "
                        + legacyCompensation + " essence to previous run profile.");
                store.Save(Profile);
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
                RacialPenaltyGuard.Install(harmony, Logger);
                GamePerks.Install(harmony, Logger);
                GameSkills.InstallDamageAdapter(harmony, Logger);
                LastStandGuard.Install(harmony, Logger);
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

                var over = AccessTools.Method("TheLastStand.Controller.GameController:TriggerGameOver");
                if(over!=null)
                {
                    harmony.Patch(over,postfix:new HarmonyMethod(AccessTools.Method(
                        typeof(Plugin),nameof(AfterGameOver))));
                    Logger.LogInfo("Eternal Hero: final map completion/reward hook installed");
                }
                else Logger.LogWarning("Eternal Hero: game-over reward hook unavailable.");

                var boss = AccessTools.Method("TheLastStand.Manager.Unit.BossManager:HandleBossPhaseActorDeath");
                if(boss!=null)
                {
                    harmony.Patch(boss,postfix:new HarmonyMethod(AccessTools.Method(
                        typeof(Plugin),nameof(AfterBossPhaseDeath))));
                    Logger.LogInfo("Eternal Hero: boss victory reward hook installed");
                }
                var customization = AccessTools.Method(
                    "TheLastStand.View.PlayableUnitCustomisation.PlayableUnitCustomisationPanel:OnConfirmButtonClicked");
                if (customization != null)
                {
                    harmony.Patch(customization, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(Plugin), nameof(AfterNativeAppearanceSaved))));
                    Logger.LogInfo("Eternal Hero native appearance save hook installed.");
                }
                var buyPerk = AccessTools.Method("TheLastStand.Controller.Unit.Perk.UnitPerkTreeController:BuyPerk");
                if (buyPerk != null)
                {
                    harmony.Patch(buyPerk, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(Plugin), nameof(AfterPerkPurchase))));
                    Logger.LogInfo("Eternal Hero vanilla perk tier unlock hook installed.");
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
                Logger.LogInfo("Eternal Hero compact creator ready; F8/F9/Home or small right-center launcher.");
            }
            catch (Exception e) { Logger.LogError("Eternal Hero canvas init error: " + e); }
        }

        // Called after the game's STATIC CreateStartUnits(), not on an instance.
        private static void AfterStartUnits()
        {
            if (Instance != null) Instance.StartCoroutine(Instance.ConfigureStartingHero());
        }

        private static void AfterPerkPurchase(object __instance)
        {
            if (Instance == null || Instance.currentHero == null) return;
            var tree = GameBridge.Get(__instance, "UnitPerkTree");
            var owner = GameBridge.Get(tree, "PlayableUnit");
            if (ReferenceEquals(owner, Instance.currentHero))
                GamePerks.UnlockAllTiers(owner, Instance.Profile, Instance.Logger);
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
            if (currentHero != null && !ReferenceEquals(currentHero, found))
                SnapshotCurrentCareer();
            currentHero = found;
            SnapshotCurrentCareer();
            // On a loaded save, do not apply old base-stat bonuses twice.
            // Identity, portrait and selected perks are independent and MUST be synchronized.
            if (progression != null)
            {
                HeroRuntime.PrimeFromLoadedSave(found, progression.CalculatedStatBonuses());
                GamePerks.PrimeLoadedPerkPoints(found, Profile);
            }
            try
            {
                if (Profile != null)
                {
                    string identity = GameBridge.Apply(found, Profile, Logger);
                    rosterStatus = "ГГ восстановлен в первом слоте: " + identity;
                    Logger.LogInfo("Eternal Hero loaded-save identity synchronized: " + identity);
                    Logger.LogInfo("Eternal Hero resumed active skills: " + GameSkills.Grant(found, Profile, Logger));
                    Logger.LogInfo("Eternal Hero resumed permanent perks: " + GamePerks.Grant(found, Profile, Logger));
                }
            }
            catch (Exception e)
            {
                rosterStatus = "Ошибка восстановления ГГ в сохранении; см. журнал";
                Logger.LogError("Eternal Hero loaded-save identity failed: " + e);
            }
            screen?.NotifyHero();
        }

        private IEnumerator ConfigureStartingHero()
        {
            yield return null;
            yield return null;
            var previousHero = currentHero;
            var firstUnit = GameBridge.FirstHero();
            if (previousHero != null && !ReferenceEquals(previousHero, firstUnit))
                SnapshotCurrentCareer();
            currentHero = firstUnit;
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
            // The new run inherits all recorded personal achievements from the career profile.
            GameCareerHistory.ApplyToFreshHero(currentHero, Profile, Logger);
            ApplyCurrentHero();
            screen?.NotifyHero();
        }

        private static void AfterNightExperience()
        {
            if (Instance != null) Instance.RewardCompletedNight();
        }

        private static void AfterGameOver(object __0)
        {
            var instance=Instance;
            if(instance==null || instance.Profile==null) return;
            string cause=__0?.ToString()??"";
            if(cause=="Abandon") return; // abandoning a run is not a defeat to farm
            bool victory=cause=="MagicSealsCompleted";
            instance.RewardRunEnd(victory,cause);
        }

        private void RewardRunEnd(bool victory,string cause)
        {
            try
            {
                if(runRewards==null||Profile==null||Profile.RunFinished) return;
                SnapshotCurrentCareer();
                long before=Profile.EarnedEssence;
                if(!runRewards.Finish(victory)) return;
                int bonus=(int)(Profile.EarnedEssence-before);
                SaveProfile();
                Logger.LogInfo("Eternal Hero: finished map; cause="+cause+
                    " awarded +"+bonus+" essence; total="+Profile.AvailableEssence);
                screen?.Notify((victory?"ПОБЕДА":"ЗАВЕРШЕНИЕ ЗАБЕГА")+
                    " • +"+bonus+" эссенции");
            }
            catch(Exception e){Logger.LogError("Eternal Hero finish reward: "+e);}
        }

        private static void AfterBossPhaseDeath(object __instance)
        {
            var instance=Instance;
            if(instance==null||instance.Profile==null||instance.Profile.RunFinished)return;
            try
            {
                if(!(GameBridge.Get(__instance,"IsBossVanquished") is bool killed)||!killed)return;
                string phase=GameBridge.Get(__instance,"CurrentBossPhaseId")?.ToString();
                var gameManager=GameBridge.Singleton("TheLastStand.Manager.GameManager");
                int day=Convert.ToInt32(GameBridge.Get(GameBridge.Get(gameManager,"Game"),"DayNumber")??0);
                if(string.IsNullOrWhiteSpace(phase)) phase="boss";
                string id=day+":"+phase;
                long before=instance.Profile.EarnedEssence;
                if(instance.runRewards.BossDefeated(id))
                {
                    int awarded=(int)(instance.Profile.EarnedEssence-before);
                    instance.SaveProfile();
                    instance.Logger.LogInfo("Eternal Hero boss phase cleared "+id+
                        ", essence +"+awarded);
                    instance.screen?.Notify("БОСС ПОВЕРЖЕН • +"+awarded+" эссенции");
                }
            }
            catch(Exception e){instance.Logger.LogWarning("Eternal Hero boss reward: "+e.Message);}
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
                    SnapshotCurrentCareer();
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
            if (Instance == null || Instance.Profile == null) return;
            object edited = GameBridge.Get(__instance, "playableUnit");
            if (edited == null) return;
            object first = GameBridge.FirstHero();
            // Match against the first unit even when the manager's post-start hook missed.
            if (!ReferenceEquals(edited, Instance.currentHero) &&
                !ReferenceEquals(edited, first))
            {
                Instance.Logger.LogInfo("Eternal Hero: customization belongs to another character, skipped");
                return;
            }
            Instance.currentHero = edited;
            GameBridge.CaptureEditorResult(Instance.currentHero, Instance.Profile);
            Instance.SaveProfile();
            Instance.Logger.LogInfo("Eternal Hero: native portrait saved, code length=" +
                (Instance.Profile.PortraitCode?.Length ?? 0) + ", face=" +
                Instance.Profile.HeroFaceId + ", gender=" + Instance.Profile.HeroGender);
            Instance.screen?.Open();
            Instance.screen?.Notify("Облик героя сохранён и будет применён в новых забегах");
        }

        private void SnapshotCurrentCareer()
        {
            try
            {
                if (currentHero != null && Profile != null &&
                    GameCareerHistory.Capture(currentHero, Profile))
                    SaveProfile();
            }
            catch (Exception e) { Logger.LogWarning("Eternal Hero personal statistics snapshot: " + e.Message); }
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
                // Always replace the first starting unit with the saved protagonist.
                // A missing CreationConfirmed flag from older profiles must not block restoration.
                string applied = GameBridge.Apply(currentHero, Profile, Logger);
                rosterStatus = "Главный герой: " + applied;
                Logger.LogInfo("Eternal Hero applied profile to FIRST roster unit: " + applied);
                if (applyBonuses.Value)
                {
                    int n = HeroRuntime.TryApplyBonuses(currentHero,
                        progression.CalculatedStatBonuses(), Logger);
                    Logger.LogInfo("Eternal Hero stat upgrades applied: " + n);
                }
                string skills = GameSkills.Grant(currentHero, Profile, Logger);
                Logger.LogInfo("Eternal Hero active skills: " + skills);
                Logger.LogInfo("Eternal Hero permanent perks: " + GamePerks.Grant(currentHero, Profile, Logger));
                Logger.LogInfo("Eternal Hero granted additional vanilla perk points: " +
                    GamePerks.ApplyMetaPerkPoints(currentHero, Profile, Logger));
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
                // Recovery if scene changes or save loads before the standard roster hook.
                if (Time.unscaledTime >= nextRosterProbeTime)
                {
                    nextRosterProbeTime = Time.unscaledTime + 5f;
                    object found = GameBridge.FirstHero();
                    if (found != null && !ReferenceEquals(currentHero, found))
                        StartCoroutine(RecognizeLoadedHero());
                    else if(currentHero != null && Profile != null)
                    {
                        var tree=GameBridge.Get(currentHero,"PerkTree");
                        if(tree != null && !ReferenceEquals(tree,lastPerkTree))
                        {
                            lastPerkTree=tree;
                            GamePerks.UnlockAllTiers(currentHero,Profile,Logger);
                        }
                    }
                }
                // TLS.ModKit applies custom SkillDefinitions as databases deserialize.
                // The character roster and combat bar are often initialized earlier.
                // Repair missing techniques and rebuild the actual game's combat bar.
                if (Time.unscaledTime >= nextSkillSynchronization)
                {
                    nextSkillSynchronization = Time.unscaledTime + 6f;
                    if (currentHero!=null && Profile!=null)
                        GameSkills.Synchronize(currentHero,Profile,Logger);
                }
                if(Time.unscaledTime>=nextPerkSynchronization)
                {
                    nextPerkSynchronization=Time.unscaledTime+10f;
                    if(currentHero!=null && Profile!=null &&
                        GamePerks.NeedsPermanentSync(currentHero,Profile))
                    {
                        Logger.LogWarning("Eternal Hero: permanent perks missing after load; restoring");
                        Logger.LogInfo("Eternal Hero restored perks: "+
                            GamePerks.Grant(currentHero,Profile,Logger));
                    }
                }
                if (Time.unscaledTime >= nextCareerSaveTime)
                {
                    nextCareerSaveTime = Time.unscaledTime + 15f;
                    SnapshotCurrentCareer();
                }
            }
            catch (Exception e) { Logger.LogWarning("Creator hotkey / roster probe: " + e.Message); }
        }

        private void OnApplicationQuit()
        {
            SnapshotCurrentCareer();
        }

        private void OnDestroy()
        {
            SnapshotCurrentCareer();
            if (harmony != null) harmony.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
    }
}
