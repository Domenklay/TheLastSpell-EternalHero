using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace EternalHero.Core
{
    // Game-independent persistent progression. No references to proprietary game assemblies.
    [DataContract]
    public sealed class HeroProfile
    {
        [DataMember] public int SchemaVersion { get; set; } = 1;
        [DataMember] public string HeroId { get; set; } = "eternal-hero";
        [DataMember] public string HeroName { get; set; } = "Вечный защитник";
        [DataMember] public long LifetimeExperience { get; set; }
        [DataMember] public long EarnedEssence { get; set; }
        [DataMember] public int AvailableEssence { get; set; }
        [DataMember] public int RunsStarted { get; set; }
        [DataMember] public int Victories { get; set; }
        [DataMember] public int Defeats { get; set; }
        [DataMember] public Dictionary<string, int> Ranks { get; set; } = new Dictionary<string, int>();
        [DataMember] public HashSet<string> ClaimedRewardIds { get; set; } = new HashSet<string>();
    }

    public sealed class Upgrade
    {
        public string Id { get; }
        public string Branch { get; }
        public string Name { get; }
        public string Description { get; }
        public int MaxRank { get; }
        public int BaseCost { get; }
        public string StatId { get; }
        public float BonusPerRank { get; }
        public string RequiresId { get; }
        public int RequiresRank { get; }
        public bool RequiresGameIntegration { get; }

        public Upgrade(string id, string branch, string name, string description,
            int maxRank, int baseCost, string statId = null, float bonusPerRank = 0,
            string requiresId = null, int requiresRank = 0, bool requiresGameIntegration = false)
        {
            Id = id; Branch = branch; Name = name; Description = description;
            MaxRank = maxRank; BaseCost = baseCost; StatId = statId;
            BonusPerRank = bonusPerRank; RequiresId = requiresId; RequiresRank = requiresRank;
            RequiresGameIntegration = requiresGameIntegration;
        }

        public int CostForNextRank(int currentRank) => BaseCost + currentRank;
    }

    public static class UpgradeCatalog
    {
        // Stat identifiers are intentionally independent of game enum names.
        public static readonly IReadOnlyList<Upgrade> All = new List<Upgrade>
        {
            new Upgrade("body.vitality", "Стальное тело", "Живучесть", "+10 максимального здоровья за ранг", 10, 1, "Health", 10),
            new Upgrade("body.armor", "Стальное тело", "Несокрушимость", "+4 брони за ранг", 10, 1, "Armor", 4),
            new Upgrade("body.block", "Стальное тело", "Стальная воля", "+1 блок за ранг", 8, 2, "Block", 1),
            new Upgrade("body.resistance", "Стальное тело", "Невозмутимость", "+2% сопротивления за ранг", 5, 3, "Resistance", 2, "body.armor", 2),
            new Upgrade("weapon.physical", "Мастер оружия", "Клинок", "+3% физического урона за ранг", 10, 1, "PhysicalDamage", 3),
            new Upgrade("weapon.ranged", "Мастер оружия", "Меткость", "+3% дальнего урона за ранг", 10, 1, "RangedDamage", 3),
            new Upgrade("weapon.magic", "Мастер оружия", "Знание магии", "+3% магического урона за ранг", 10, 1, "MagicDamage", 3),
            new Upgrade("weapon.critical", "Мастер оружия", "Слабые места", "+1% шанса критического удара за ранг", 8, 2, "Critical", 1),
            new Upgrade("tactics.mobility", "Тактический гений", "Манёвр", "+1 очко передвижения за ранг", 3, 4, "MovePoints", 1),
            new Upgrade("tactics.range", "Тактический гений", "Дальний взгляд", "+1 к дальности за ранг", 2, 5, "SkillRange", 1),
            new Upgrade("tactics.action", "Тактический гений", "Дополнительное действие", "+1 очко действий за ранг", 2, 10, "ActionPoints", 1,
                "tactics.mobility", 2),
            new Upgrade("legacy.trait", "Наследие", "Закалённый ветеран", "Открывает дополнительную существующую черту", 3, 6,
                requiresGameIntegration: true),
            new Upgrade("legacy.perk", "Наследие", "Наследник знаний", "Открывает постоянный существующий перк", 3, 7,
                "legacy.trait", 1, true),
            new Upgrade("legacy.mastery", "Наследие", "Универсал", "Усиление эффектов каждого типа оружия", 5, 5,
                "legacy.perk", 1, true),
            new Upgrade("forbidden.ghostshot", "Запретные техники", "Призрачный выстрел",
                "Особая атака дальнего боя без винтовки", 1, 12, requiresId: "tactics.range", requiresRank: 2, requiresGameIntegration: true),
            new Upgrade("forbidden.etherslash", "Запретные техники", "Эфирный клинок",
                "Круговой удар без оружия ближнего боя", 1, 12, requiresId: "weapon.physical", requiresRank: 5, requiresGameIntegration: true),
            new Upgrade("forbidden.arcaneburst", "Запретные техники", "Пульс арканы",
                "Магический взрыв без посоха", 1, 12, requiresId: "weapon.magic", requiresRank: 5, requiresGameIntegration: true),
        };

        public static Upgrade Find(string id) => All.FirstOrDefault(x => x.Id == id);
    }

    public sealed class ProgressionService
    {
        public HeroProfile Profile { get; }
        public const long ExperiencePerEssence = 100;

        public ProgressionService(HeroProfile profile)
        {
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            if (profile.SchemaVersion != 1) throw new NotSupportedException("Unsupported profile format");
            if (profile.Ranks == null) profile.Ranks = new Dictionary<string, int>();
            if (profile.ClaimedRewardIds == null) profile.ClaimedRewardIds = new HashSet<string>();
        }

        public int Rank(string upgradeId) =>
            Profile.Ranks.TryGetValue(upgradeId, out var rank) ? rank : 0;

        public bool CanPurchase(string upgradeId, out string reason)
        {
            var upgrade = UpgradeCatalog.Find(upgradeId);
            if (upgrade == null) { reason = "Unknown upgrade"; return false; }
            var rank = Rank(upgradeId);
            if (rank >= upgrade.MaxRank) { reason = "Already max rank"; return false; }
            if (upgrade.RequiresId != null && Rank(upgrade.RequiresId) < upgrade.RequiresRank)
            {
                reason = "Prerequisite not met"; return false;
            }
            if (Profile.AvailableEssence < upgrade.CostForNextRank(rank))
            {
                reason = "Not enough essence"; return false;
            }
            reason = null;
            return true;
        }

        public bool Purchase(string id, out string reason)
        {
            if (!CanPurchase(id, out reason)) return false;
            var upgrade = UpgradeCatalog.Find(id);
            Profile.AvailableEssence -= upgrade.CostForNextRank(Rank(id));
            Profile.Ranks[id] = Rank(id) + 1;
            return true;
        }

        // rewardId must uniquely identify the event in the actual game; duplicates give no rewards.
        public bool GrantExperience(string rewardId, int amount)
        {
            if (string.IsNullOrWhiteSpace(rewardId)) throw new ArgumentException("A unique reward ID is required", nameof(rewardId));
            if (amount <= 0 || amount > 1000000) throw new ArgumentOutOfRangeException(nameof(amount));
            if (!Profile.ClaimedRewardIds.Add(rewardId)) return false;
            Profile.LifetimeExperience = checked(Profile.LifetimeExperience + amount);
            var totalEarned = Profile.LifetimeExperience / ExperiencePerEssence;
            var gain = totalEarned - Profile.EarnedEssence;
            if (gain > 0)
            {
                Profile.AvailableEssence = checked(Profile.AvailableEssence + checked((int)gain));
                Profile.EarnedEssence = totalEarned;
            }
            return true;
        }

        public IReadOnlyDictionary<string, float> CalculatedStatBonuses()
        {
            var result = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var u in UpgradeCatalog.All)
            {
                if (u.StatId == null || u.BonusPerRank == 0f) continue;
                var amount = u.BonusPerRank * Rank(u.Id);
                if (amount <= 0f) continue;
                result[u.StatId] = (result.TryGetValue(u.StatId, out var old) ? old : 0) + amount;
            }
            return result;
        }
    }
}
