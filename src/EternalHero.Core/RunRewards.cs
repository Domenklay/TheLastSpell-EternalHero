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
            if (!progression.GrantExperience(rewardId, 30 + Math.Min(70, night * 5))) return false;
            Profile.LastCompletedNight = night;
            Profile.TotalNightsSurvived = checked(Profile.TotalNightsSurvived + 1);
            return true;
        }
        public bool BossDefeated(string bossEventId)
        {
            if (string.IsNullOrWhiteSpace(Profile.CurrentRunId) || Profile.RunFinished ||
                string.IsNullOrWhiteSpace(bossEventId)) return false;
            if (bossEventId.Length > 100) throw new ArgumentOutOfRangeException(nameof(bossEventId));
            var claimed = progression.GrantExperience("run:" + Profile.CurrentRunId + ":boss:" + bossEventId, 150);
            if (claimed) Profile.BossesDefeated = checked(Profile.BossesDefeated + 1);
            return claimed;
        }
        public bool Finish(bool victory)
        {
            if (string.IsNullOrWhiteSpace(Profile.CurrentRunId) || Profile.RunFinished) return false;
            string id = "run:" + Profile.CurrentRunId + ":finish";
            if (!progression.GrantExperience(id, victory ? 500 : 50)) return false;
            Profile.RunFinished = true;
            if (victory) Profile.Victories = checked(Profile.Victories + 1);
            else Profile.Defeats = checked(Profile.Defeats + 1);
            return true;
        }
    }
}
