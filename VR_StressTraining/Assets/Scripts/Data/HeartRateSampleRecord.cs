using System;

namespace StressTraining.Data
{
    /// <summary>
    /// One heart-rate sample, appended as a single JSON line to hr.jsonl.
    /// Written for every accepted sample during an active app session.
    /// </summary>
    [Serializable]
    public sealed class HeartRateSampleRecord
    {
        public int schemaVersion = 1;

        public long sampleId;                   // monotonically increasing per session
        public int bpm;
        public string receivedAtUtcIso;
        public string sourceTimestampUtcIso = "";   // "" when the source did not provide one
        public double monotonicReceiveSeconds;      // SessionClock.RealElapsedSeconds at receive
        public double signalAgeMs = -1;             // receivedAt - sourceTimestamp when both known

        public HrQualityStatus qualityStatus;
        public HrSourceType sourceType;
        public int sequence = -1;               // bridge sequence number; -1 = legacy packet

        public bool isPaused;                   // paused samples never enter task HR averages
        public string sessionState = "";        // AppState name at receive time
        public TaskType taskType;
        public string blockId = "";
        public string trialId = "";
    }
}
