using System;

namespace StressTraining.Data
{
    /// <summary>
    /// One task trial, appended as a single JSON line to trials.jsonl.
    /// Flat structure by design — every field the analysis needs is denormalized
    /// so a single line is self-describing (spec §11).
    /// </summary>
    [Serializable]
    public sealed class TrialRecord
    {
        public int schemaVersion = 1;

        public string trialId;
        public string sessionId;
        public string blockId;
        public TaskType taskType;
        public int trialIndexInBlock;
        public bool isWarmup;                   // warm-up n-back trials: shown but not scoreable

        public string stimulus;                 // task-specific encoding (see DATA_SCHEMA.md)
        public string expectedResponse;         // semantic action name or "None"
        public string actualResponse;           // semantic action name or "None"

        public string stimulusPresentedAtUtcIso;
        public double stimulusPresentedAtActiveSeconds;   // SessionClock.ActiveElapsedSeconds
        public string responseAtUtcIso = "";
        public double reactionTimeMs = -1;      // -1 = no response

        public bool correct;
        public bool missed;                     // omission (expected response, none given)
        public bool falsePositive;              // commission (response when none expected)

        public int difficultyLevel;
        public int pressureLevel;
        public SessionCondition condition;

        public int currentBpm = -1;             // latest HR at stimulus onset; -1 = none
        public HrZone hrZone;
        public bool wasPausedDuringTrial;
        public double remainingBlockTimeSeconds;
        public double remainingGlobalTimeSeconds = -1;   // v2 — production global timer

        public int sessionSeed;
        public int blockSeed;

        // ── v2: Corsi-inspired task detail (empty/-1 for other tasks) ─────
        // Full reproducibility of one Corsi trial (spec §18.4).
        public string corsiPresentedSequence = "";     // e.g. "2-7-4"
        public string corsiRequiredResponseOrder = ""; // forward = same, backward = reversed
        public string corsiResponsePrefix = "";        // what the user actually pressed
        public int corsiSequenceLength = -1;
        public int corsiCorrectlyReproducedCount = -1;
        public int corsiFirstErrorIndex = -1;          // -1 = no wrong press
        public string corsiFirstWrongButtonId = "";
        public double corsiStartResponseMs = -1;       // stimulus end → first press
        public string corsiInterPressIntervalsMs = ""; // "312;280;301"
        public double corsiTotalResponseMs = -1;
        public bool corsiBackward;
        public bool corsiAbandonedByInactivity;
    }
}
