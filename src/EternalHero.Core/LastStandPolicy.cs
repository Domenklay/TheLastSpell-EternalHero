using System;
using System.Collections.Generic;

namespace EternalHero.Core
{
    public static class LastStandPolicy
    {
        // This is a per-run, per-in-game-day entitlement. The ledger is persisted in the
        // mod profile so reloading an old save cannot grant another resurrection.
        public static bool TryConsume(HeroProfile profile, int gameDay, float health,
            float incomingDamage, bool isNight)
        {
            if(profile==null||!isNight||gameDay<0||
                string.IsNullOrWhiteSpace(profile.CurrentRunId) ||
                health<=0 || incomingDamage<health ||
                float.IsNaN(incomingDamage) || float.IsInfinity(incomingDamage) ||
                profile.Ranks==null ||
                !profile.Ranks.TryGetValue("body.laststand",out int rank) ||
                rank<1) return false;
            if(profile.ClaimedRewardIds==null)
                profile.ClaimedRewardIds=new HashSet<string>();
            string token="laststand:"+profile.CurrentRunId+":"+gameDay;
            return profile.ClaimedRewardIds.Add(token);
        }

        public static float MinimumRecovery(float totalHealth) =>
            Math.Max(1f, totalHealth * 0.30f);
    }
}
