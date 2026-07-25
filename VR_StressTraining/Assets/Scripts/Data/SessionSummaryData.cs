using System;
using System.Collections.Generic;

namespace StressTraining.Data
{
    /// <summary>
    /// One completed (or interrupted) session. Written to
    /// sessions/&lt;userId&gt;/&lt;sessionId&gt;/session.json and appended to the profile.
    /// </summary>
    [Serializable]
    public sealed class SessionSummaryData
    {
        public const int CurrentSchemaVersion = 4;

        // v4 adds active challenge budget/elapsed metadata while preserving
        // the existing JSON persistence shape. Old records load
        // with JsonUtility defaults and are migrated by SchemaMigrationService.
        public int schemaVersion = CurrentSchemaVersion;

        public string sessionId;
        public string userId;
        public string cycleId;
        public int sessionNumberInCycle;            // 1-based

        public string startedAtUtcIso;
        public string endedAtUtcIso = "";

        public CompletionStatus completionStatus;
        public UserTerminationReason userTerminationReason;
        public List<SystemDetectedIssue> systemDetectedIssues = new List<SystemDetectedIssue>();
        public ValidityStatus validityStatus;
        public List<string> validityNotes = new List<string>();

        public SessionCondition condition;          // Neutral / Pressure
        public int pressureLevel;                   // global level used this session (1..3)
        public int seed;                            // master session seed

        // ── v2: production session identity ─────────────────────────────
        public bool isProductionSession;            // false = demo/tutorial record
        public int taskSelectionSeed;               // seed of the 3-of-4 selector
        public int blockOrderSeed;                  // seed of the 9-block / 3-round ordering
        public int pressureSeed;                    // seed of the pressure event timeline
        public int consoleLayoutSeed;               // retained provenance field; production layout is fixed in Phase 8.5
        public List<TaskType> selectedTaskTypes = new List<TaskType>();
        public TaskType omittedTaskType = TaskType.None;
        public List<string> selectionReasonCodes = new List<string>();
        public string selectionAlgorithmVersion = "";
        public List<string> blockOrder = new List<string>();   // blockIds in execution order
        public float nominalPlanSeconds;            // deterministic estimate for the concrete plan
        public float globalDifficultyTimeMultiplier = 1f;
        public float globalDurationSeconds;         // final active challenge budget
        public float globalChallengeElapsedSeconds;
        public float remainingGlobalSecondsAtEnd = -1f;
        public bool scheduleOverride;               // started before recommended time
        public string scheduleOverrideReason = "";
        public string pressureStageAtEnd = "";      // e.g. "70", "30", "expired"
        public int plannedRoundCount;
        public bool developerTestSession;          // explicit non-research developer execution
        public bool schedulerEligible = false;     // unlocked only after real HR + good baseline

        public string appVersion = "";
        public string configVersion = "";

        public double totalPausedSeconds;
        public int pauseCount;
        public double totalActiveSeconds;

        public BaselineSummaryData baseline = new BaselineSummaryData();
        public RecoverySummaryData recovery = new RecoverySummaryData();
        public List<TaskSessionSummaryData> taskSummaries = new List<TaskSessionSummaryData>();
        public List<QuestionnaireResultData> questionnaireResults = new List<QuestionnaireResultData>();
        public AdaptationDecisionData schedulerDecision;    // null until scheduler runs
        public List<string> technicalWarnings = new List<string>();
    }

    [Serializable]
    public sealed class BaselineSummaryData
    {
        public BaselineQuality quality = BaselineQuality.Unknown;
        public float averageBpm = -1f;
        public float medianBpm = -1f;
        public int minBpm = -1;
        public int maxBpm = -1;
        public int sampleCount;
        public int validSampleCount;
        public float validSampleRatio;
        public int signalGapCount;
        public float durationSeconds;
    }

    [Serializable]
    public sealed class RecoverySummaryData
    {
        public float averageBpm = -1f;
        public float deltaFromSessionPeakBpm;       // sessionPeak - recoveryAvg
        public float deltaFromBaselineBpm;          // recoveryAvg - baselineAvg
        public float secondsToWorkingZone = -1f;    // -1 = never reached within measurement window
        public float durationSeconds;
        public bool reachedWorkingZone;
    }

    /// <summary>Aggregated per-task performance for one session (all blocks of that task type).</summary>
    [Serializable]
    public sealed class TaskSessionSummaryData
    {
        public TaskType taskType;
        public int difficultyLevel;
        public int blockCount;
        public int trialCount;
        public int correctCount;
        public int missCount;                       // omission
        public int falsePositiveCount;              // commission
        public float accuracy;                      // correct / scoreable trials
        public float meanReactionTimeMs = -1f;      // correct responses only
        public float medianReactionTimeMs = -1f;
        public float accuracyStdAcrossBlocks;       // block stability metric
        public float maxBpmDuringTask = -1f;
        public float avgBpmDuringTask = -1f;
        public double elevatedOrHighZoneSeconds;    // time spent above Stable during this task

        // ── v2: flanker-specific (computed from congruency-tagged trials) ──
        public float congruentAccuracy = -1f;
        public float incongruentAccuracy = -1f;
        public float congruentRtMs = -1f;
        public float incongruentRtMs = -1f;
        public float congruencyEffectMs;

        // ── v2: Corsi-specific ───────────────────────────────────────────
        public int corsiMaxCorrectSequenceLength = -1;   // longest fully reproduced sequence
        public float corsiMeanCorrectPrefix = -1f;       // average correctly reproduced prefix
    }
}
