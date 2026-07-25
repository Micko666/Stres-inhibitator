using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.Adaptation
{
    /// <summary>Per-task metrics extracted from the finished session.</summary>
    [Serializable]
    public sealed class TaskMetricsInput
    {
        public TaskType taskType;
        public int currentLevel;
        /// <summary>False when the 3-of-4 selector omitted this task this session.</summary>
        public bool wasSelectedThisSession = true;
        public float accuracy = -1f;
        public float meanReactionTimeMs = -1f;
        public int missCount;
        public int falsePositiveCount;
        public float blockAccuracyStd;          // stability across the task's blocks
        public float avgBpmDelta = -999f;       // avg task BPM − baseline; -999 = unknown
        public float elevatedZoneRatio = -1f;   // elevated/high seconds ÷ task active seconds

        // Corsi-specific (spec §32): sequence span and first-error profile.
        public int corsiMaxCorrectSequenceLength = -1;
        public float corsiMeanCorrectPrefix = -1f;
    }

    /// <summary>
    /// Full snapshot the scheduler decides from (spec §24). Serialized into the
    /// decision record (inputSnapshotJson) so every decision is reconstructable.
    /// </summary>
    [Serializable]
    public sealed class AdaptationInput
    {
        public string sessionId;
        public ValidityStatus validityStatus;
        public SessionCondition condition;
        public int currentPressureLevel;

        public TaskMetricsInput nBack = new TaskMetricsInput { taskType = TaskType.NBack };
        public TaskMetricsInput goNoGo = new TaskMetricsInput { taskType = TaskType.GoNoGo };
        public TaskMetricsInput flanker = new TaskMetricsInput { taskType = TaskType.Flanker };
        public TaskMetricsInput corsi = new TaskMetricsInput { taskType = TaskType.CorsiSequence };

        // Production context (spec §32 rules 9/12)
        public bool timeExpired;
        public bool scheduleOverride;

        public float baselineBpm = -1f;
        public float sessionAvgBpmDelta = -999f;    // session-wide average delta vs baseline
        public double elevatedOrHighSeconds;
        public double totalActiveSeconds;
        public bool recoveryReachedZone;
        public float recoverySeconds = -1f;

        // Raw NASA-TLX (0..100 each)
        public float tlxTotal = -1f;
        public float tlxMental = -1f;
        public float tlxPhysical = -1f;
        public float tlxTemporal = -1f;
        public float tlxPerformance = -1f;
        public float tlxEffort = -1f;
        public float tlxFrustration = -1f;

        // History (previous sessions of the same cycle, oldest→newest)
        public List<float> previousAccuraciesNBack = new List<float>();
        public List<float> previousAccuraciesGoNoGo = new List<float>();
        public List<float> previousAccuraciesFlanker = new List<float>();
        public int previousSessionCount;

        public TaskMetricsInput Metrics(TaskType t) =>
            t == TaskType.NBack ? nBack
            : t == TaskType.GoNoGo ? goNoGo
            : t == TaskType.Flanker ? flanker
            : corsi;
    }
}
