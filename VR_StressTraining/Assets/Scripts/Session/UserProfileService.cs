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

        /// <summary>
        /// Starts a fresh cycle when none is active or the previous one is complete, and
        /// repairs an active cycle whose summary record is missing.
        ///
        /// ProfileRepository.CreateProfile assigns an activeCycleId but never wrote the
        /// matching TrainingCycleSummaryData, and this method used to return early
        /// whenever an id was present — so every profile ran its first cycle with no
        /// record to write into. The visible effect was silent: the cycle-level STAI-6
        /// scores (ProductionSessionFlow, questionnaire result handling) and
        /// completedSessionCount are only stored `if (cycle != null)`, so they were
        /// dropped. The raw questionnaire answers were never lost — they stay in
        /// questionnaires.json and the session summary — but the cycle aggregate was.
        /// </summary>
        public void EnsureActiveCycle(UserProfileData profile)
        {
            bool needNew = string.IsNullOrEmpty(profile.activeCycleId) ||
                           profile.currentCycleSessionIndex >= profile.plannedCycleSessionCount;
            if (!needNew)
            {
                if (!profile.cycleSummaries.Exists(c => c.cycleId == profile.activeCycleId))
                    RepairMissingCycleSummary(profile);
                return;
            }

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

        /// <summary>
        /// Writes the record for an active cycle that never got one, rebuilding
        /// completedSessionCount from the sessions already filed under that cycle so a
        /// profile repaired mid-cycle keeps its place instead of restarting at zero.
        /// </summary>
        private void RepairMissingCycleSummary(UserProfileData profile)
        {
            int usable = 0;
            string earliest = null;
            foreach (var s in profile.sessionSummaries)
            {
                if (s.cycleId != profile.activeCycleId) continue;
                if (CountsTowardCycle(s.validityStatus)) usable++;
                if (!string.IsNullOrEmpty(s.startedAtUtcIso) &&
                    (earliest == null || string.CompareOrdinal(s.startedAtUtcIso, earliest) < 0))
                    earliest = s.startedAtUtcIso;
            }

            profile.cycleSummaries.Add(new TrainingCycleSummaryData
            {
                cycleId = profile.activeCycleId,
                startedAtUtcIso = earliest ??
                                  (string.IsNullOrEmpty(profile.createdAtUtcIso)
                                      ? UtcTime.NowIso() : profile.createdAtUtcIso),
                plannedSessionCount = profile.plannedCycleSessionCount,
                completedSessionCount = usable
            });
            _repository.SaveProfile(profile);
        }

        // ── Session completion ───────────────────────────────────────────

        /// <summary>
        /// Whether a finished session consumes one of the cycle's planned places.
        /// Only a session the scheduler could actually use does: the same two statuses
        /// AdaptationScheduler treats as usable, so "five sessions in a cycle" and
        /// "five sessions that informed adaptation" cannot drift apart.
        /// </summary>
        public static bool CountsTowardCycle(ValidityStatus status) =>
            status == ValidityStatus.Valid || status == ValidityStatus.ValidWithWarnings;

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

            // A training cycle is five USABLE sessions, not five attempts. An aborted,
            // timed-out or otherwise invalid session is kept in full history but must
            // not consume a place in the cycle, or a participant who feels unwell once
            // would end the cycle with four usable sessions instead of five.
            if (CountsTowardCycle(summary.validityStatus))
            {
                var cycle = profile.cycleSummaries.Find(c => c.cycleId == profile.activeCycleId);
                if (cycle != null) cycle.completedSessionCount++;
                profile.currentCycleSessionIndex++;
            }

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
