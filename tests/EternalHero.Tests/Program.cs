using System;
using System.IO;
using System.Linq;
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

            Check(UpgradeCatalog.Find("legacy.trait").MaxRank == 11,
                "up to twelve independent selected permanent traits are supported");
            Check(UpgradeCatalog.Find("legacy.perk").MaxRank == 8,
                "eight purchasable permanent legacy perk-point ranks");
            Check(UpgradeCatalog.All.Count == 96, "all 96 upgrades registered");
            Check(UpgradeCatalog.Branches.Count == 6, "six specialization branches");
            Check(UpgradeCatalog.All.Select(u => u.Id).Distinct().Count() == 96, "upgrade IDs unique");
            Check(UpgradeCatalog.All.All(u => u.RequiresId == null ||
                UpgradeCatalog.Find(u.RequiresId) != null), "all prerequisites refer to valid nodes");
            Check(recovered.MetaRank == 1, "fresh meta rank determined from ranks");
            Check(!recovered.PurchaseAscension("asc.health", out _),
                "ascension locked until full main-tree completion");
            recovered.Profile.AvailableEssence = 1000000; // test-only budget, not written to user's profile
            bool changed;
            int passes = 0;
            do {
                changed = false;
                foreach (var node in UpgradeCatalog.All)
                    if (recovered.Purchase(node.Id, out _)) changed = true;
                passes++;
                if (passes > 100) throw new Exception("Upgrade prerequisite cycle");
            } while (changed);
            Check(recovered.MainTreeCompleted, "entire six-branch tree is achievable");
            Check(recovered.PurchasedUpgradeRanks == recovered.TotalUpgradeRanks,
                "every node reaches maximum rank");
            Check(recovered.MetaRank == 100, "completed tree reports legendary rank 100");
            Check(recovered.PurchaseAscension("asc.health", out _),
                "ascension opens after fully completing development");
            Check(recovered.Profile.AscensionRanks["asc.health"] == 1, "ascension records permanent rank");
            Check(recovered.CalculatedStatBonuses()["Health"] >= 15f,
                "ascension contributes real health bonus");
            store.Save(recovered.Profile);
            var reloaded = new ProgressionService(store.Load());
            recovered.Profile.SelectedPerks.Add("SomeVanillaPerk");
            store.Save(recovered.Profile);
            Check(store.Load().SelectedPerks.Contains("SomeVanillaPerk"),
                "permanent perk loadout survives reload");
            Check(reloaded.MainTreeCompleted, "completion survives save reload");
            Check(reloaded.Profile.AscensionRanks["asc.health"] == 1,
                "ascension survives save reload");
            var career = new HeroProfile();
            Check(CareerHistory.Merge(career, "Kills", 80), "first career snapshot recorded");
            Check(!CareerHistory.Merge(career, "Kills", 80), "duplicate career snapshot ignored");
            Check(!CareerHistory.Merge(career, "Kills", 20), "older saved game cannot decrease achievements");
            Check(CareerHistory.Merge(career, "Kills", 112), "new run increases career totals");
            Check(CareerHistory.MergeDetail(career, "UsesPerWeapon", "Crossbow", 28),
                "weapon usage recorded");
            Check(!CareerHistory.MergeDetail(career, "UsesPerWeapon", "Crossbow", 5),
                "old weapon use statistics do not replace career values");
            Check(CareerHistory.MergeDetail(career, "DamagesInflictedToEnemies", "Boss", 7.5),
                "favourite enemy scores recorded");
            career.SelectedPerks.Add("SamplePerk");
            career.UnlockAllPerkTiers = true;
            store.Save(career);
            var longTerm = store.Load();
            Check(longTerm.CareerLifetimeStats["Kills"] == 112,
                "lifetime achievements survive profile serialization");
            Check(longTerm.CareerLifetimeDetails["UsesPerWeapon"]["Crossbow"] == 28,
                "weapon history survives profile serialization");
            Check(longTerm.UnlockAllPerkTiers && longTerm.SelectedPerks.Contains("SamplePerk"),
                "unrestricted perk choices survive serialization");
            Check(TraitBrowser.CategoriesFor(new[] { "PhysicalDamage" }).Contains("УРОН"),
                "physical attack traits belong to damage category");
            Check(TraitBrowser.CategoriesFor(new[] { "HealthTotal", "ArmorTotal" })
                .Contains("ЗДОРОВЬЕ") && TraitBrowser.CategoriesFor(new[] { "HealthTotal", "ArmorTotal" })
                .Contains("ЗАЩИТА"), "mixed traits appear in multiple relevant categories");
            Check(TraitBrowser.CategoriesFor(new[] { "ManaRegen" }).Contains("МАНА"),
                "mana regeneration traits categorized correctly");
            Check(TraitBrowser.CategoriesFor(new string[0]).Contains("ОСОБЫЕ"),
                "traits without numeric modifiers remain discoverable");
            Check(TraitBrowser.MatchesSearch("Ядовитая кожа", "Trait.Poison",
                "PoisonDamage 20", "УРОН", "яд кожа"), "multiword Russian search works");
            Check(!TraitBrowser.MatchesSearch("Ядовитая кожа", "Trait.Poison",
                "PoisonDamage 20", "УРОН", "яд мана"), "search excludes wrong category");
            Check(TraitBrowser.MatchesCategory(new [] { "ЗАЩИТА" }, "ВСЕ") &&
                !TraitBrowser.MatchesCategory(new [] { "ЗАЩИТА" }, "МАНА"),
                "category selection filters entries without losing them");
            var mixed = TraitBrowser.CategoriesFor(new[]{"CriticalDamage"});
            Check(mixed.Contains("УРОН") && mixed.Contains("ТОЧНОСТЬ"),
                "critical damage may appear in both relevant categories");
            Console.WriteLine("ALL PROGRESSION TESTS PASSED");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }
}
