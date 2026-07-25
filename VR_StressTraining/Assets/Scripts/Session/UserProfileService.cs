using System;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Persistence;

namespace StressTraining.Session
{
    public enum SessionEligibility
    {
        ReadyFirstSession = 0,   // no session yet in this cycle
        Ready = 1,               // recommended time reached
        EarlyWithWarning = 2,    // before recommended time — allowed, user is warned
        NewCycleReady = 3        // previous cycle complete; a new cycle starts
    }

    /// <summary>
    /// Profile lifecycle + 48h scheduling (spec §5). Pure logic where possible;
    /// persistence is delegated to ProfileRepository.
    /// The system never hard-blocks a researcher: early starts are allowed with a
    /// warning, and developer bypass is logged by the caller (spec §5).
    /// </summary>
    public sealed class UserProfileService
    {
        private readonly ProfileRepository _repository;
        private readonly SessionConfig _sessionConfig;

        public UserProfileData ActiveProfile { get; private set; }

        public UserProfileService(ProfileRepository repository, SessionConfig sessionConfig)
        {
            _repository = repository;
            _sessionConfig = sessionConfig;
        }

        public UserProfileData CreateProfile(string username)
        {
            var profile = _repository.CreateProfile(
                username, _sessionConfig.defaultSessionIntervalHours,
                _sessionConfig.plannedCycleSessionCount);
            ActiveProfile = profile;
            return profile;
        }

        public UserProfileData SelectProfile(string userId)
        {
            ActiveProfile = _repository.LoadProfile(userId);
            return ActiveProfile;
        }

        public void ClearActiveProfile() => ActiveProfile = null;

        // ── Scheduling ───────────────────────────────────────────────────

        /// <summary>Pure next-session calculation — unit-testable.</summary>
        public static DateTime CalculateNextRecommended(DateTime sessionEndUtc, int intervalHours) =>
            sessionEndUtc.AddHours(intervalHours);

        public SessionEligibility CheckEligibility(UserProfileData profile, DateTime nowUtc, out TimeSpan waitRemaining)
        {
            waitRemaining = TimeSpan.Zero;
            if (string.IsNullOrEmpty(profile.lastSessionAtUtcIso))
                return SessionEligibility.ReadyFirstSession;

            if (profile.currentCycleSessionIndex >= profile.plannedCycleSessionCount)
                return SessionEligibility.NewCycleReady;

            if (UtcTime.TryParseIso(profile.nextRecommendedSessionAtUtcIso, out var next) && nowUtc < next)
            {
                waitRemaining = next - nowUtc;
                return SessionEligibility.EarlyWithWarning;
            }
            return SessionEligibility.Ready;
        }

        public static bool IsFirstSessionOfCycle(UserProfileData profile) =>
            profile.currentCycleSessionIndex == 0;

        public static bool ShouldAdministerCycleStartStai(UserProfileData profile)
        {
            if (profile == null || !IsFirstSessionOfCycle(profile)) return false;
            var cycle = profile.cycleSummaries.Find(c => c.cycleId == profile.activeCycleId);
            return cycle == null || cycle.staiScoreCycleStart < 0f;
        }

        public static bool ShouldAdministerCycleEndStai(UserProfileData profile)
        {
            if (profile == null || !IsLastSessionOfCycle(profile)) return false;
            var cycle = profile.cycleSummaries.Find(c => c.cycleId == profile.activeCycleId);
            return cycle == null || cycle.staiScoreCycleEnd < 0f;
        }

        /// <summary>True when the session ABOUT TO RUN (or just run) is the last of the cycle.</summary>
        public static bool IsLastSessionOfCycle(UserProfileData profile) =>
            profile.currentCycleSessionIndex >= profile.plannedCycleSessionCount - 1;

        /// <summary>Starts a fresh cycle when none is active or the previous one is complete.</summary>
        public void EnsureActiveCycle(UserProfileData profile)
        {
            bool needNew = string.IsNullOrEmpty(profile.activeCycleId) ||
                           profile.currentCycleSessionIndex >= profile.plannedCycleSessionCount;
            if (!needNew) return;

            if (!string.IsNullOrEmpty(profile.activeCycleId))
            {
                // close out previous cycle summary if it exists
                var prev = profile.cycleSummaries.Find(c => c.cycleId == profile.activeCycleId);
                if (prev != null && string.IsNullOrEmpty(prev.endedAtUtcIso))
                    prev.endedAtUtcIso = UtcTime.NowIso();
            }
            profile.activeCycleId = Guid.NewGuid().ToString("N");
            profile.currentCycleSessionIndex = 0;
            profile.cycleSummaries.Add(new TrainingCycleSummaryData
            {
                cycleId = profile.activeCycleId,
                startedAtUtcIso = UtcTime.NowIso(),
                plannedSessionCount = profile.plannedCycleSessionCount
            });
            _repository.SaveProfile(profile);
        }

        // ── Session completion ───────────────────────────────────────────

        /// <summary>
        /// Persists the finished session into the profile: history, cycle index,
        /// next recommended time, and — ONLY between sessions — new difficulty levels
        /// from the scheduler decision (architecture rule 18).
        /// </summary>
        public void CompleteSession(UserProfileData profile, SessionSummaryData summary,
            AdaptationDecisionData decision)
        {
            if (!profile.sessionSummaries.Exists(x => x.sessionId == summary.sessionId))
                profile.sessionSummaries.Add(summary);
            else
                return; // idempotent repeated save/event protection
            profile.lastSessionAtUtcIso = summary.endedAtUtcIso;

            DateTime endUtc = UtcTime.TryParseIso(summary.endedAtUtcIso, out var e) ? e : UtcTime.Now();
            profile.nextRecommendedSessionAtUtcIso =
                UtcTime.ToIso(CalculateNextRecommended(endUtc, profile.sessionIntervalHours));

            var cycle = profile.cycleSummaries.Find(c => c.cycleId == profile.activeCycleId);
            if (cycle != null) cycle.completedSessionCount++;
            profile.currentCycleSessionIndex++;

            if (decision != null)
            {
                profile.currentNBackLevel = decision.nBack.newLevel;
                profile.currentGoNoGoLevel = decision.goNoGo.newLevel;
                profile.currentFlankerLevel = decision.flanker.newLevel;
                if (decision.corsi != null && decision.corsi.newLevel > 0)
                    profile.currentCorsiLevel = decision.corsi.newLevel;
                profile.currentPressureLevel = decision.pressure.newLevel;
            }
            _repository.SaveProfile(profile);
        }
    }
}
