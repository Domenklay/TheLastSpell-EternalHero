using System;

namespace EternalHero.Core
{
    // Events require a stable identifier originating from a game's save/run.
    // Never derive a run key from wall clock time or each game reload: that would permit replay rewards.
    public sealed class RunRewards
    {
        private readonly ProgressionService progression;
        public HeroProfile Profile => progression.Profile;
        public RunRewards(ProgressionService progression)
        {
            this.progression = progression ?? throw new ArgumentNullException(nameof(progression));
        }
        public bool Begin(string stableRunId)
        {
            if (string.IsNullOrWhiteSpace(stableRunId) || stableRunId.Length > 200)
                throw new ArgumentException("A stable game-run ID is required", nameof(stableRunId));
            if (Profile.CurrentRunId == stableRunId) return false; // resume, never reset the ledger
            Profile.CurrentRunId = stableRunId;
            Profile.RunFinished = false;
            Profile.LastCompletedNight = 0;
            Profile.RunsStarted = checked(Profile.RunsStarted + 1);
            return true;
        }
        public bool NightSurvived(int night)
        {
            if (string.IsNullOrWhiteSpace(Profile.CurrentRunId) || Profile.RunFinished) return false;
            if (night < 1 || night > 1000 || night <= Profile.LastCompletedNight) return false;
            // Only one night per event. Do not award skipped nights without verified game events.
            if (night != Profile.LastCompletedNight + 1) return false;
            string rewardId = "run:" + Profile.CurrentRunId + ":night:" + night;
            if (!progression.GrantExperience(rewardId, 175 + Math.Min(125, night * 15))) return false;
            Profile.LastCompletedNight = night;
            Profile.TotalNightsSurvived = checked(Profile.TotalNightsSurvived + 1);
            return true;
        }
        public bool BossDefeated(string bossEventId)
        {
            if (string.IsNullOrWhiteSpace(Profile.CurrentRunId) || Profile.RunFinished ||
                string.IsNullOrWhiteSpace(bossEventId)) return false;
            if (bossEventId.Length > 100) throw new ArgumentOutOfRangeException(nameof(bossEventId));
            var claimed = progression.GrantExperience("run:" + Profile.CurrentRunId + ":boss:" + bossEventId, 400);
            if (claimed) Profile.BossesDefeated = checked(Profile.BossesDefeated + 1);
            return claimed;
        }
        // Previous releases did not hook actual victory and gave 35–100 XP/night.
        // Reimburse only proven previously claimed night tokens. An optional one-time
        // early-player grant compensates pre-v0.11 runs with missing victory hooks.
        // Existing purchased upgrades and experience are never reduced.
        public int CompensateEarlyEconomy()
        {
            const string token = "career:legacy-economy-v0.11";
            if (Profile.ClaimedRewardIds.Contains(token)) return 0;
            int backpay = 0;
            foreach (string id in Profile.ClaimedRewardIds)
            {
                int at = id.LastIndexOf(":night:", StringComparison.Ordinal);
                if(at < 0 || !id.StartsWith("run:",StringComparison.Ordinal)) continue;
                if (!int.TryParse(id.Substring(at+7),out int night) || night<1 || night>1000)
                    continue;
                int old = 30 + Math.Min(70, night*5);
                int adjusted = 175 + Math.Min(125, night*15);
                backpay=checked(backpay + adjusted-old);
            }
            if(Profile.RunsStarted>0 && Profile.TotalNightsSurvived>0 &&
                Profile.Victories==0)
                backpay=checked(backpay+600); // early player, not a claimed victory
            if (backpay<=0)
            {
                Profile.ClaimedRewardIds.Add(token); // new profiles cannot retroactively claim later
                return 0;
            }
            long before=Profile.EarnedEssence;
            if (!progression.GrantExperience(token,backpay)) return 0;
            return checked((int)(Profile.EarnedEssence-before));
        }

        public bool Finish(bool victory)
        {
            if (string.IsNullOrWhiteSpace(Profile.CurrentRunId) || Profile.RunFinished) return false;
            string id = "run:" + Profile.CurrentRunId + ":finish";
            if (!progression.GrantExperience(id, victory ? 700 + (Profile.Victories == 0 ? 400 : 0) : 200)) return false;
            Profile.RunFinished = true;
            if (victory) Profile.Victories = checked(Profile.Victories + 1);
            else Profile.Defeats = checked(Profile.Defeats + 1);
            return true;
        }
    }
}
