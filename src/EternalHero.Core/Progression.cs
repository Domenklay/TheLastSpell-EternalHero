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
        [DataMember] public string HeroRaceId { get; set; } = "Human";
        [DataMember] public string HeroGender { get; set; } = "Male";
        [DataMember] public string HeroFaceId { get; set; } = "";
        [DataMember] public string PortraitCode { get; set; } = "";
        [DataMember] public bool CreationConfirmed { get; set; }
        [DataMember] public List<string> SelectedTraits { get; set; } = new List<string>();
        [DataMember] public List<string> SelectedPerks { get; set; } = new List<string>();
        // The separate personal-achievement history is monotonic across runs.
        [DataMember] public Dictionary<string, double> CareerLifetimeStats { get; set; } =
            new Dictionary<string, double>();
        [DataMember] public Dictionary<string, Dictionary<string, double>> CareerLifetimeDetails { get; set; } =
            new Dictionary<string, Dictionary<string, double>>();
        [DataMember] public bool UnlockAllPerkTiers { get; set; } = true;
        [DataMember] public bool StarterEssenceGranted { get; set; }
        [DataMember] public long LifetimeExperience { get; set; }
        [DataMember] public long EarnedEssence { get; set; }
        [DataMember] public int AvailableEssence { get; set; }
        [DataMember] public int RunsStarted { get; set; }
        [DataMember] public int Victories { get; set; }
        [DataMember] public int Defeats { get; set; }
        [DataMember] public string CurrentRunId { get; set; } = "";
        [DataMember] public int LastCompletedNight { get; set; }
        [DataMember] public int TotalNightsSurvived { get; set; }
        [DataMember] public int BossesDefeated { get; set; }
        [DataMember] public bool RunFinished { get; set; }
        [DataMember] public string PortraitHint { get; set; } = "";
        [DataMember] public Dictionary<string, string> Appearance { get; set; } =
            new Dictionary<string, string>();
        [DataMember] public bool FirstRunGiftClaimed { get; set; }
        [DataMember] public Dictionary<string, int> Ranks { get; set; } = new Dictionary<string, int>();
        [DataMember] public Dictionary<string, int> AscensionRanks { get; set; } = new Dictionary<string, int>();
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
            new Upgrade("legacy.trait", "Наследие", "Закалённый ветеран", "Открывает дополнительную существующую черту", 5, 6,
                requiresGameIntegration: true),
            new Upgrade("legacy.perk", "Наследие", "Наследник знаний", "Открывает постоянный существующий перк", 8, 7,
                requiresId: "legacy.trait", requiresRank: 1, requiresGameIntegration: true),
            new Upgrade("legacy.mastery", "Наследие", "Универсал", "Усиление эффектов каждого типа оружия", 5, 5,
                requiresId: "legacy.perk", requiresRank: 1, requiresGameIntegration: true),
            new Upgrade("forbidden.ghostshot", "Запретные техники", "Призрачный выстрел",
                "Особая атака дальнего боя без винтовки", 1, 12, requiresId: "tactics.range", requiresRank: 2, requiresGameIntegration: true),
            new Upgrade("forbidden.etherslash", "Запретные техники", "Эфирный клинок",
                "Круговой удар без оружия ближнего боя", 1, 12, requiresId: "weapon.physical", requiresRank: 5, requiresGameIntegration: true),
            new Upgrade("forbidden.arcaneburst", "Запретные техники", "Пульс арканы",
                "Магический взрыв без посоха", 1, 12, requiresId: "weapon.magic", requiresRank: 5, requiresGameIntegration: true),
            new Upgrade("body.regeneration", "Стальное тело", "Регенерация", "+2 регенерации HP за ранг", 6, 3, "HealthRegen", 2, null, 0, false),
            new Upgrade("body.manawell", "Стальное тело", "Запас маны", "+4 маны за ранг", 8, 2, "ManaTotal", 4, null, 0, false),
            new Upgrade("body.manaregen", "Стальное тело", "Восстановление маны", "+1 регенерации маны за ранг", 5, 3, "ManaRegen", 1, "body.manawell", 2, false),
            new Upgrade("body.dodge", "Стальное тело", "Ускользание", "+2 уклонения за ранг", 7, 3, "Dodge", 2, null, 0, false),
            new Upgrade("body.shell", "Стальное тело", "Костяная броня", "+7 брони за ранг", 5, 5, "Armor", 7, "body.armor", 3, false),
            new Upgrade("body.life", "Стальное тело", "Крепкая кровь", "+18 HP за ранг", 5, 6, "Health", 18, "body.vitality", 3, false),
            new Upgrade("body.will", "Стальное тело", "Стойкость разума", "+3 сопротивления за ранг", 5, 5, "Resistance", 3, "body.resistance", 2, false),
            new Upgrade("body.stamina", "Стальное тело", "Неутомимость", "+2 регенерации HP за ранг", 6, 5, "HealthRegen", 2, "body.regeneration", 3, false),
            new Upgrade("body.aegis", "Стальное тело", "Прочная защита", "+2 блока за ранг", 5, 6, "Block", 2, "body.block", 3, false),
            new Upgrade("body.evasion", "Стальное тело", "Лёгкие шаги", "+3 уклонения за ранг", 5, 6, "Dodge", 3, "body.dodge", 3, false),
            new Upgrade("body.citadel", "Стальное тело", "Живая крепость", "+25 HP за ранг", 3, 12, "Health", 25, "body.life", 2, false),
            new Upgrade("body.laststand", "Стальное тело", "Последний бастион", "Сейчас даёт +80 HP; спасение от смерти пока ожидает боевого хука", 1, 24, "Health", 80, "body.citadel", 3, true),
            new Upgrade("weapon.power", "Мастер оружия", "Сила трёх школ", "+3 общего урона за ранг", 8, 3, "OverallDamage", 3, null, 0, false),
            new Upgrade("weapon.critpower", "Мастер оружия", "Завершающий удар", "+5 крит. урона за ранг", 7, 3, "CriticalPower", 5, "weapon.critical", 2, false),
            new Upgrade("weapon.accuracy", "Мастер оружия", "Выверенный удар", "+4 точности за ранг", 6, 3, "Accuracy", 4, null, 0, false),
            new Upgrade("weapon.propagation", "Мастер оружия", "Проводник", "+5 PropagationDamage за ранг", 6, 4, "PropagationDamage", 5, "weapon.magic", 2, false),
            new Upgrade("weapon.armorbreak", "Мастер оружия", "Разлом брони", "+4 ArmorShreddingAttacks за ранг", 6, 4, "ArmorShreddingAttacks", 4, "weapon.physical", 2, false),
            new Upgrade("weapon.poison", "Мастер оружия", "Ядовитая рука", "+5 PoisonDamageModifier за ранг", 5, 4, "PoisonDamageModifier", 5, null, 0, false),
            new Upgrade("weapon.opportunity", "Мастер оружия", "Контратака", "+4 OpportunisticAttacks за ранг", 5, 5, "OpportunisticAttacks", 4, "weapon.physical", 3, false),
            new Upgrade("weapon.momentum", "Мастер оружия", "Инерция", "+4 MomentumAttacks за ранг", 5, 5, "MomentumAttacks", 4, null, 0, false),
            new Upgrade("weapon.lonely", "Мастер оружия", "Один против всех", "+5 IsolatedAttacks за ранг", 5, 5, "IsolatedAttacks", 5, null, 0, false),
            new Upgrade("weapon.reliability", "Мастер оружия", "Надёжность", "+4 Reliability за ранг", 6, 4, "Reliability", 4, null, 0, false),
            new Upgrade("weapon.multihit", "Мастер оружия", "Серия ударов", "+1 MultiHitsCountModifier за ранг", 2, 12, "MultiHitsCountModifier", 1, "weapon.critical", 4, false),
            new Upgrade("weapon.trinity", "Мастер оружия", "Абсолютный мастер", "+10 общего урона за ранг", 3, 15, "OverallDamage", 10, "weapon.power", 6, false),
            new Upgrade("tactics.march", "Тактический гений", "Быстрый марш", "+1 передвижения за ранг", 5, 4, "MovePoints", 1, null, 0, false),
            new Upgrade("tactics.command", "Тактический гений", "Второй темп", "+1 очков действий за ранг", 2, 15, "ActionPoints", 1, "tactics.action", 1, false),
            new Upgrade("tactics.longshot", "Тактический гений", "Снайперская позиция", "+1 дальности способностей за ранг", 3, 7, "SkillRange", 1, "tactics.range", 2, false),
            new Upgrade("tactics.precision", "Тактический гений", "Хладнокровие", "+5 точности за ранг", 6, 3, "Accuracy", 5, null, 0, false),
            new Upgrade("tactics.stun", "Тактический гений", "Сдерживание", "+4 StunChanceModifier за ранг", 5, 6, "StunChanceModifier", 4, null, 0, false),
            new Upgrade("tactics.experience", "Тактический гений", "Учёба боем", "+5 ExperienceGain за ранг", 4, 5, "ExperienceGain", 5, null, 0, false),
            new Upgrade("tactics.potion", "Тактический гений", "Медик", "+1 PotionRangeModifier за ранг", 3, 5, "PotionRangeModifier", 1, null, 0, false),
            new Upgrade("tactics.crit", "Тактический гений", "Окно возможности", "+2 крит. шанса за ранг", 5, 4, "Critical", 2, null, 0, false),
            new Upgrade("tactics.survival", "Тактический гений", "Позиционная защита", "+3 сопротивления за ранг", 4, 4, "Resistance", 3, null, 0, false),
            new Upgrade("tactics.endurance", "Тактический гений", "Марафонец", "+1 регенерации маны за ранг", 5, 4, "ManaRegen", 1, null, 0, false),
            new Upgrade("tactics.spread", "Тактический гений", "Длинная цепь", "+1 PropagationBouncesModifier за ранг", 3, 8, "PropagationBouncesModifier", 1, "tactics.longshot", 1, false),
            new Upgrade("tactics.storm", "Тактический гений", "Удержать фронт", "+7 общего урона за ранг", 3, 12, "OverallDamage", 7, "tactics.command", 2, false),
            new Upgrade("tactics.teleport", "Тактический гений", "Мгновенный переход", "Сейчас даёт +3 передвижения; активная телепортация ещё не готова", 1, 18, "MovePoints", 3, "tactics.longshot", 2, true),
            new Upgrade("legacy.study", "Наследие", "Книга войны", "+5 ExperienceGain за ранг", 6, 4, "ExperienceGain", 5, null, 0, false),
            new Upgrade("legacy.blood", "Наследие", "Наследственная сила", "+10 HP за ранг", 5, 4, "Health", 10, null, 0, false),
            new Upgrade("legacy.focus", "Наследие", "Искусство точности", "+3 крит. шанса за ранг", 5, 5, "Critical", 3, null, 0, false),
            new Upgrade("legacy.magic", "Наследие", "Память чародея", "+5 магического урона за ранг", 6, 5, "MagicDamage", 5, null, 0, false),
            new Upgrade("legacy.archery", "Наследие", "Память охотника", "+5 дальнего урона за ранг", 6, 5, "RangedDamage", 5, null, 0, false),
            new Upgrade("legacy.blade", "Наследие", "Память воина", "+5 физического урона за ранг", 6, 5, "PhysicalDamage", 5, null, 0, false),
            new Upgrade("legacy.guarantor", "Наследие", "Охранитель", "+7 брони за ранг", 5, 7, "Armor", 7, "legacy.blood", 3, false),
            new Upgrade("legacy.chain", "Наследие", "Сеть знаний", "+4 PropagationDamage за ранг", 5, 6, "PropagationDamage", 4, null, 0, false),
            new Upgrade("legacy.cunning", "Наследие", "Скрытая удача", "+2 уклонения за ранг", 5, 6, "Dodge", 2, null, 0, false),
            new Upgrade("legacy.recovery", "Наследие", "Опыт поколений", "+1 регенерации HP за ранг", 5, 6, "HealthRegen", 1, null, 0, false),
            new Upgrade("legacy.endurance", "Наследие", "Старинная дисциплина", "+1 регенерации маны за ранг", 5, 6, "ManaRegen", 1, null, 0, false),
            new Upgrade("legacy.champion", "Наследие", "Прославленный герой", "+9 общего урона за ранг", 3, 12, "OverallDamage", 9, "legacy.mastery", 3, false),
            new Upgrade("legacy.sage", "Наследие", "Мудрость веков", "+10 HP за ранг", 5, 11, "Health", 10, "legacy.study", 4, false),
            new Upgrade("forbidden.ghostmastery", "Запретные техники", "Мастерство выстрела", "+4 дальнего урона за ранг", 5, 5, "RangedDamage", 4, "forbidden.ghostshot", 1, false),
            new Upgrade("forbidden.ethermastery", "Запретные техники", "Мастерство клинка", "+4 физического урона за ранг", 5, 5, "PhysicalDamage", 4, "forbidden.etherslash", 1, false),
            new Upgrade("forbidden.arcanemastery", "Запретные техники", "Мастерство арканы", "+4 магического урона за ранг", 5, 5, "MagicDamage", 4, "forbidden.arcaneburst", 1, false),
            new Upgrade("forbidden.piercing", "Запретные техники", "Пронзающая аура", "+4 ArmorShreddingAttacks за ранг", 5, 6, "ArmorShreddingAttacks", 4, "forbidden.ghostmastery", 2, false),
            new Upgrade("forbidden.etherguard", "Запретные техники", "Защита эфира", "+3 блока за ранг", 5, 6, "Block", 3, "forbidden.ethermastery", 2, false),
            new Upgrade("forbidden.arcanaregen", "Запретные техники", "Эфирный источник", "+2 регенерации маны за ранг", 5, 6, "ManaRegen", 2, "forbidden.arcanemastery", 2, false),
            new Upgrade("forbidden.riflecrit", "Запретные техники", "Спектральная точность", "+4 крит. шанса за ранг", 5, 6, "Critical", 4, "forbidden.ghostmastery", 3, false),
            new Upgrade("forbidden.bleed", "Запретные техники", "Кровавый разрез", "+5 физического урона за ранг", 5, 7, "PhysicalDamage", 5, "forbidden.ethermastery", 3, false),
            new Upgrade("forbidden.storm", "Запретные техники", "Магический шторм", "+6 магического урона за ранг", 5, 7, "MagicDamage", 6, "forbidden.arcanemastery", 3, false),
            new Upgrade("forbidden.focus", "Запретные техники", "Сосредоточение", "+2 дальности способностей за ранг", 2, 10, "SkillRange", 2, "forbidden.ghostmastery", 4, false),
            new Upgrade("forbidden.efficiency", "Запретные техники", "Экономия сил", "+3 регенерации маны за ранг", 3, 10, "ManaRegen", 3, "forbidden.arcanemastery", 4, false),
            new Upgrade("forbidden.secondshot", "Запретные техники", "Второй призрачный выстрел", "Открывает дополнительный выстрел без AP (TLS.ModKit)", 1, 22, null, 0, "forbidden.ghostmastery", 5, true),
            new Upgrade("forbidden.triple", "Запретные техники", "Три школы", "+10 общего урона за ранг", 3, 18, "OverallDamage", 10, "forbidden.arcanemastery", 5, false),
            new Upgrade("trinity.hunter", "Триединство", "Эхо охотника", "Бесплатное дальнее эхо раз за раунд (TLS.ModKit)", 1, 18, null, 0, "forbidden.ghostshot", 1, true),
            new Upgrade("trinity.mage", "Триединство", "Эхо мага", "Бесплатное магическое эхо раз за раунд (TLS.ModKit)", 1, 18, null, 0, "forbidden.arcaneburst", 1, true),
            new Upgrade("trinity.coherence", "Триединство", "Связь душ", "+5 общего урона за ранг", 5, 7, "OverallDamage", 5, "trinity.hunter", 1, false),
            new Upgrade("trinity.precision", "Триединство", "Узор охотника", "+4 дальнего урона за ранг", 5, 6, "RangedDamage", 4, "trinity.hunter", 1, false),
            new Upgrade("trinity.arcana", "Триединство", "Узор чародея", "+4 магического урона за ранг", 5, 6, "MagicDamage", 4, "trinity.mage", 1, false),
            new Upgrade("trinity.blood", "Триединство", "Общая кровь", "+12 HP за ранг", 5, 5, "Health", 12, "trinity.coherence", 2, false),
            new Upgrade("trinity.reflex", "Триединство", "Вспышка сознания", "+2 крит. шанса за ранг", 5, 6, "Critical", 2, "trinity.hunter", 1, false),
            new Upgrade("trinity.mana", "Триединство", "Третий поток", "+2 регенерации маны за ранг", 5, 7, "ManaRegen", 2, "trinity.mage", 1, false),
            new Upgrade("trinity.focus", "Триединство", "Два фронта", "+1 дальности способностей за ранг", 3, 8, "SkillRange", 1, "trinity.precision", 2, false),
            new Upgrade("trinity.march", "Триединство", "Три шага", "+1 передвижения за ранг", 3, 8, "MovePoints", 1, "trinity.coherence", 2, false),
            new Upgrade("trinity.calm", "Триединство", "Неподвижная душа", "+4 блока за ранг", 4, 8, "Block", 4, "trinity.arcana", 2, false),
            new Upgrade("trinity.chain", "Триединство", "Цепная реакция", "+1 PropagationBouncesModifier за ранг", 3, 10, "PropagationBouncesModifier", 1, "trinity.arcana", 3, false),
            new Upgrade("trinity.brilliance", "Триединство", "Гений объединения", "+10 общего урона за ранг", 3, 13, "OverallDamage", 10, "trinity.coherence", 5, false),
            new Upgrade("trinity.blade", "Триединство", "Третье лицо", "+6 физического урона за ранг", 5, 8, "PhysicalDamage", 6, "trinity.march", 2, false),
            new Upgrade("trinity.harmony", "Триединство", "Полная синхронизация", "+5 магического урона за ранг", 5, 9, "MagicDamage", 5, "trinity.mage", 1, false),
            new Upgrade("trinity.apotheosis", "Триединство", "Триединство", "+15 общего урона за ранг", 3, 24, "OverallDamage", 15, "trinity.brilliance", 3, false),
        };

        public static readonly IReadOnlyList<string> Branches = new[] {
            "Стальное тело", "Мастер оружия", "Тактический гений", "Наследие", "Запретные техники", "Триединство"
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
            if (profile.AscensionRanks == null) profile.AscensionRanks = new Dictionary<string, int>();
            if (profile.ClaimedRewardIds == null) profile.ClaimedRewardIds = new HashSet<string>();
            if (profile.SelectedTraits == null) profile.SelectedTraits = new List<string>();
            if (profile.SelectedPerks == null) profile.SelectedPerks = new List<string>();
            if (profile.CareerLifetimeStats == null) profile.CareerLifetimeStats = new Dictionary<string, double>();
            if (profile.CareerLifetimeDetails == null)
                profile.CareerLifetimeDetails = new Dictionary<string, Dictionary<string, double>>();
            if (profile.Appearance == null) profile.Appearance = new Dictionary<string, string>();
        }

        public int TotalUpgradeRanks => UpgradeCatalog.All.Sum(x => x.MaxRank);
        public int PurchasedUpgradeRanks => UpgradeCatalog.All.Sum(x => Math.Min(x.MaxRank, Rank(x.Id)));
        public bool MainTreeCompleted => PurchasedUpgradeRanks == TotalUpgradeRanks;
        public int MetaRank => 1 + (int)(99L * PurchasedUpgradeRanks / TotalUpgradeRanks);
        public float Completion => TotalUpgradeRanks == 0 ? 0 : (float)PurchasedUpgradeRanks / TotalUpgradeRanks;

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
            foreach (var a in AscensionCatalog.All)
            {
                if (!Profile.AscensionRanks.TryGetValue(a.Id, out int rank) || rank <= 0) continue;
                float amount = rank * a.BonusPerRank;
                result[a.StatId] = (result.TryGetValue(a.StatId, out float previous) ? previous : 0) + amount;
            }
            return result;
        }

        public bool PurchaseAscension(string id, out string reason)
        {
            var upgrade = AscensionCatalog.All.FirstOrDefault(a => a.Id == id);
            if (upgrade == null) { reason = "Unknown ascension"; return false; }
            if (!MainTreeCompleted) { reason = "Complete all six branches first"; return false; }
            Profile.AscensionRanks.TryGetValue(id, out int rank);
            if (rank >= 100000) { reason = "Ascension safety limit"; return false; }
            int cost = checked(5 + 2 * rank);
            if (Profile.AvailableEssence < cost) { reason = "Not enough essence"; return false; }
            Profile.AvailableEssence -= cost;
            Profile.AscensionRanks[id] = rank + 1;
            reason = null;
            return true;
        }
    }
}
