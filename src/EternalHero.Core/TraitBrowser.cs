using System;
using System.Collections.Generic;
using System.Linq;

namespace EternalHero.Core
{
    /// <summary>
    /// Pure catalogue classification/filtering, deliberately independent from Unity and
    /// the game's assemblies, so the enormous traits database stays navigable.
    /// A mixed trait can belong to more than one category.
    /// </summary>
    public static class TraitBrowser
    {
        public static readonly string[] Categories =
        {
            "ВСЕ", "УРОН", "ЗАЩИТА", "ЗДОРОВЬЕ",
            "МАНА", "ПОДВИЖНОСТЬ", "ТОЧНОСТЬ", "ОСОБЫЕ"
        };

        public static string[] CategoriesFor(IEnumerable<string> statNames)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            if (statNames != null)
            {
                foreach (string stat in statNames)
                {
                    if (string.IsNullOrWhiteSpace(stat)) continue;
                    string s = stat.ToLowerInvariant();
                    int before=set.Count;
                    if (Has(s, "critical", "crit", "accuracy", "reliability", "opportunistic"))
                        set.Add("ТОЧНОСТЬ");
                    if (Has(s, "armor", "armour", "block", "resist", "dodge",
                                   "shield", "protection"))
                        set.Add("ЗАЩИТА");
                    if (Has(s, "manaregen", "mana", "magicpoint"))
                        set.Add("МАНА");
                    if (Has(s, "health", "hitpoint", "healing", "heal", "life"))
                        set.Add("ЗДОРОВЬЕ");
                    if (Has(s, "movepoint", "movement", "actionpoint", "skillrange",
                                   "range", "jump", "momentum"))
                        set.Add("ПОДВИЖНОСТЬ");
                    if (Has(s, "damage", "attack", "physical", "magical", "magic",
                                   "poison", "propagation", "multihit", "stun"))
                        set.Add("УРОН");
                    if(before==set.Count) set.Add("ОСОБЫЕ");
                }
            }
            if (set.Count == 0) set.Add("ОСОБЫЕ");
            return Categories.Skip(1).Where(set.Contains).ToArray();
        }

        public static bool MatchesCategory(IEnumerable<string> categories, string filter)
        {
            return string.IsNullOrWhiteSpace(filter) || filter == "ВСЕ" ||
                categories != null && categories.Contains(filter);
        }

        public static bool MatchesSearch(string name, string id, string description,
            string category, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            string text = string.Join(" ", new[] { name, id, description, category }
                .Where(s => !string.IsNullOrEmpty(s)));
            // Each word must match, allowing "яд шанс", "стрел крит", etc.
            foreach (string token in query.Trim().Split(new[] { ' ', '\t', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }
            return true;
        }

        private static bool Has(string value, params string[] terms) =>
            terms.Any(value.Contains);
    }
}
