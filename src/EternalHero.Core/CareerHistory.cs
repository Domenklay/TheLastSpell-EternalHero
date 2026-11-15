using System;
using System.Collections.Generic;

namespace EternalHero.Core
{
    public static class CareerHistory
    {
        // LifetimeStats values in the game's current character are cumulative.
        // Keep the larger observation, NOT their sum. This is replay-safe even when
        // a save is loaded again, and ensures a new run inherits the old totals.
        public static bool Merge(HeroProfile profile, string stat, double observed)
        {
            if (profile == null || string.IsNullOrWhiteSpace(stat) ||
                double.IsNaN(observed) || double.IsInfinity(observed) || observed < 0) return false;
            if (profile.CareerLifetimeStats == null)
                profile.CareerLifetimeStats = new Dictionary<string, double>();
            profile.CareerLifetimeStats.TryGetValue(stat, out double previous);
            if (observed <= previous) return false;
            profile.CareerLifetimeStats[stat] = observed;
            return true;
        }

        public static bool MergeDetail(HeroProfile profile, string category, string id, double observed)
        {
            if (profile == null || string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(id) ||
                double.IsNaN(observed) || double.IsInfinity(observed) || observed < 0) return false;
            if (profile.CareerLifetimeDetails == null)
                profile.CareerLifetimeDetails = new Dictionary<string, Dictionary<string, double>>();
            if (!profile.CareerLifetimeDetails.TryGetValue(category, out var values))
            {
                values = new Dictionary<string, double>();
                profile.CareerLifetimeDetails[category] = values;
            }
            values.TryGetValue(id, out double previous);
            if (observed <= previous) return false;
            values[id] = observed;
            return true;
        }
    }
}
