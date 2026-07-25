using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;

namespace StressTraining.Session
{
    /// <summary>Phase tag persisted with each accepted HR sample via sessionState.</summary>
    public enum SessionHrPhase
    {
        None = 0,
        ConnectionSetup = 1,
        PreSessionQuestionnaire = 2,
        /// <summary>
        /// PROJECT-DEFINED BREATHING-ASSISTED REFERENCE (2026-07-14 methodology change).
        /// Guided breathing and the reference HR measurement run as ONE phase, so this
        /// is NOT a standard neutral/resting baseline — the participant is deliberately
        /// breathing to a guided rhythm. Only samples from this phase feed the session
        /// reference BPM. Numeric value kept at 3 (never reorder — persisted as int).
        /// </summary>
        BreathingReferenceBaseline = 3,
        /// <summary>Legacy: breathing used to be a separate stage; kept for old records.</summary>
        CopingBreathing = 4,
        Tutorial = 5,
        TaskTransition = 6,
        ActiveTask = 7,
        Recovery = 8,
        PostSessionQuestionnaire = 9,
        SessionEnding = 10
    }

    /// <summary>
    /// Mutable runtime state of the single active session (spec §4).
    /// Only one session can be active at a time; SessionCoordinator owns the
    /// instance and is the only writer of most fields.
    /// </summary>
    public sealed class ActiveSessionContext
    {
        public string SessionId;
        public string UserId;
        public string CycleId;
        public int SessionNumberInCycle;        // 1-based
        public string StartedAtUtcIso;

        public SessionCondition Condition;
        public int MasterSeed;
        public SessionPlan Plan;

        // Local difficulty levels — FIXED for the whole session (rule 17/18).
        public int NBackLevel;
        public int GoNoGoLevel;
        public int FlankerLevel;
        public int CorsiLevel = 1;              // Corsi-inspired task level (v2)
        public int PressureLevel;               // global pressure level 1..3

        // Live block/task state
        public int CurrentBlockIndex = -1;
        public SessionBlockPlan CurrentBlock =>
            Plan != null && CurrentBlockIndex >= 0 && CurrentBlockIndex < Plan.blocks.Count
                ? Plan.blocks[CurrentBlockIndex] : null;
        public TaskType ActiveTaskType => CurrentBlock?.taskType ?? TaskType.None;
        public string ActiveTrialId = "";

        // HR status snapshot (updated by HeartRateService)
        public bool HrConnected;
        public HrZone CurrentHrZone = HrZone.SignalLost;
        public int LatestBpm = -1;
        public float SessionPeakBpm = -1;
        public SessionHrPhase HrPhase = SessionHrPhase.None;

        // Termination
        public CompletionStatus CompletionStatus = CompletionStatus.Unknown;
        public UserTerminationReason UserTerminationReason = UserTerminationReason.None;
        public readonly List<SystemDetectedIssue> SystemIssues = new List<SystemDetectedIssue>();
        public ValidityStatus ValidityStatus = ValidityStatus.Unknown;

        public bool IsDemoPlan => Plan != null && Plan.isDemoPlan;
        public bool IntervalBypassUsed;         // dev bypass of the 48h rule — always logged

        public readonly List<string> TechnicalWarnings = new List<string>();

        public SessionClock Clock { get; }
        public PauseController Pause { get; }

        public ActiveSessionContext() : this(new SessionClock(), null) { }

        public ActiveSessionContext(SessionClock clock, PauseController pause)
        {
            Clock = clock ?? new SessionClock();
            Pause = pause ?? new PauseController(Clock);
        }
    }
}
