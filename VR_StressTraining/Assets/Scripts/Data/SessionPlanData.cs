using System;
using System.Collections.Generic;

namespace StressTraining.Data
{
    /// <summary>
    /// Deterministic plan for one session, generated before the session starts.
    /// Neutral and Pressure sessions can share identical seeds and therefore
    /// identical stimulus sequences (spec §20).
    /// </summary>
    [Serializable]
    public sealed class SessionPlan
    {
        public const int CurrentSchemaVersion = 4;

        // v4 adds deterministic plan-dependent active-time budget metadata.
        public const int SelectedTaskCount = 3;
        public const int RoundCount = 3;
        public const int BlocksPerRound = 3;
        public const int ProductionBlockCount = RoundCount * BlocksPerRound;

        public int schemaVersion = CurrentSchemaVersion;
        public int masterSeed;
        public SessionCondition condition;
        public int pressureLevel;
        public bool isDemoPlan;
        public List<SessionBlockPlan> blocks = new List<SessionBlockPlan>();

        // ── v2: production plan identity ─────────────────────────────────
        public int taskSelectionSeed;
        public int blockOrderSeed;
        public int pressureSeed;
        public int consoleLayoutSeed;
        public List<TaskType> selectedTaskTypes = new List<TaskType>();
        public TaskType omittedTaskType = TaskType.None;
        public List<string> selectionReasonCodes = new List<string>();
        public string selectionAlgorithmVersion = "";
        public float nominalPlanSeconds;
        public float globalDifficultyTimeMultiplier = 1f;
        public float globalDurationSeconds;
    }

    [Serializable]
    public sealed class SessionBlockPlan
    {
        public string blockId;              // "B01_NBack" etc.
        public int blockIndex;              // 0-based position in session
        public int roundIndex;              // 0-based production round (0..2)
        public TaskType taskType;
        public int difficultyLevel;         // local level 1..3 fixed for the whole session
        public int trialCount;              // scoreable trials (excludes n-back warmup)
        public int blockSeed;               // derived deterministically from masterSeed
        public int RoundNumber => roundIndex + 1;
    }
}
