using System;
using System.IO;
using EternalHero.Core;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("TEST FAILED: " + message);
        Console.WriteLine("PASS " + message);
    }

    public static void Main()
    {
        var temp = Path.Combine(Path.GetTempPath(), "eternal-hero-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new ProfileStore(Path.Combine(temp, "hero.json"));
            var service = new ProgressionService(store.Load());

            Check(service.Profile.AvailableEssence == 0, "empty profile starts with no currency");
            Check(service.GrantExperience("run-1:night-1", 250), "first event awards XP");
            Check(!service.GrantExperience("run-1:night-1", 250), "duplicate event cannot be farmed");
            Check(service.Profile.LifetimeExperience == 250, "XP total is correct");
            Check(service.Profile.AvailableEssence == 2, "100 XP grants one essence point");
            Check(service.Purchase("body.vitality", out _), "first vitality upgrade purchased");
            Check(!service.Purchase("forbidden.ghostshot", out _), "special ability requires prerequisite");
            Check(service.CalculatedStatBonuses()["Health"] == 10f, "health bonus calculated");
            store.Save(service.Profile);
            var loaded = new ProgressionService(store.Load());
            Check(loaded.Rank("body.vitality") == 1, "upgrade survived save and reload");
            Check(loaded.Profile.LifetimeExperience == 250, "XP survived save and reload");
            Check(!loaded.GrantExperience("run-1:night-1", 250), "reward IDs survive save and reload");
            loaded.GrantExperience("run-1:night-2", 100);
            store.Save(loaded.Profile);
            Check(File.Exists(store.BackupPath), "backup created on subsequent save");
            var rewards = new RunRewards(loaded);
            Check(rewards.Begin("saveA/zone1/run1"), "stable first run started");
            Check(!rewards.Begin("saveA/zone1/run1"), "loading same run doesn't restart rewards");
            Check(rewards.NightSurvived(1), "completed first night");
            Check(!rewards.NightSurvived(1), "night can't be rewarded twice");
            Check(!rewards.NightSurvived(3), "skipped night not incorrectly rewarded");
            Check(rewards.BossDefeated("boss-1"), "boss awarded once");
            Check(!rewards.BossDefeated("boss-1"), "same boss cannot be farmed");
            Check(rewards.Finish(true), "run victory rewarded");
            Check(!rewards.Finish(true), "victory cannot be farmed");
            Check(!rewards.NightSurvived(2), "no rewards after run ends");
            store.Save(loaded.Profile);
            var next = new ProgressionService(store.Load());
            var nextRewards = new RunRewards(next);
            Check(!nextRewards.Begin("saveA/zone1/run1"), "run identity survives game reload");
            Check(nextRewards.Begin("saveA/zone1/run2"), "new run creates new reward scope");
            Check(nextRewards.NightSurvived(1), "same night in another run can grant XP");
            Check(next.Profile.RunsStarted == 2, "run counter persisted");
            Check(next.Profile.Victories == 1, "victory counter persisted");
            Check(next.Profile.BossesDefeated == 1, "boss counter persisted");
            next.Profile.Appearance["self/Gender"] = "Female";
            next.Profile.HeroName = "Альтаир";
            store.Save(next.Profile);
            var appearance = store.Load();
            Check(appearance.Appearance["self/Gender"] == "Female", "appearance markers survive save and load");
            Check(appearance.HeroName == "Альтаир", "hero name survives save and load");
            Check(appearance.RunsStarted == 2, "run identity survives appearance save");
            appearance.HeroName = "Кайден";
            appearance.HeroRaceId = "Dwarf";
            appearance.HeroGender = "Female";
            appearance.HeroFaceId = "FaceA";
            appearance.PortraitCode = "TEST-CODE";
            appearance.CreationConfirmed = true;
            appearance.StarterEssenceGranted = true;
            appearance.SelectedTraits.Add("Test-Existing-Trait");
            store.Save(appearance);
            var recovered = new ProgressionService(store.Load());
            Check(recovered.Profile.HeroName == "Кайден", "creator saves the chosen name");
            Check(recovered.Profile.HeroRaceId == "Dwarf", "creator saves race selection");
            Check(recovered.Profile.HeroGender == "Female", "creator saves gender selection");
            Check(recovered.Profile.HeroFaceId == "FaceA", "creator saves face ID");
            Check(recovered.Profile.PortraitCode == "TEST-CODE", "creator saves game portrait code");
            Check(recovered.Profile.CreationConfirmed, "creator save completed flag persists");
            Check(recovered.Profile.StarterEssenceGranted, "free starter essence flag persists");
            Check(recovered.Profile.SelectedTraits.Contains("Test-Existing-Trait"), "trait loadout survives reload");

            Console.WriteLine("ALL PROGRESSION TESTS PASSED");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }
}
