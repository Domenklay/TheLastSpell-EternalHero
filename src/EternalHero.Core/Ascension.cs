using System.Collections.Generic;

namespace EternalHero.Core
{
    public sealed class AscensionUpgrade
    {
        public string Id { get; }
        public string Name { get; }
        public string StatId { get; }
        public float BonusPerRank { get; }
        public string Description { get; }
        public AscensionUpgrade(string id, string name, string stat, float bonus, string description)
        {
            Id = id; Name = name; StatId = stat; BonusPerRank = bonus; Description = description;
        }
    }

    public static class AscensionCatalog
    {
        // Infinite post-max progression (up to technical limit of 100,000 in each stat).
        public static readonly IReadOnlyList<AscensionUpgrade> All = new[]
        {
            new AscensionUpgrade("asc.health", "Вечная кровь", "Health", 15f, "+15 HP за возвышение"),
            new AscensionUpgrade("asc.armor", "Бессмертный панцирь", "Armor", 5f, "+5 брони за возвышение"),
            new AscensionUpgrade("asc.physical", "Нескончаемый бой", "PhysicalDamage", 3f, "+3 физического урона за возвышение"),
            new AscensionUpgrade("asc.ranged", "Дальнее наследие", "RangedDamage", 3f, "+3 дальнего урона за возвышение"),
            new AscensionUpgrade("asc.magic", "Бесконечная магия", "MagicDamage", 3f, "+3 магического урона за возвышение"),
            new AscensionUpgrade("asc.echoes", "Бессмертные эха", "OverallDamage", 4f, "+4 общего урона за возвышение")
        };
    }
}
