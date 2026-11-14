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
            Console.WriteLine("ALL PROGRESSION TESTS PASSED");
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, true);
        }
    }
}
