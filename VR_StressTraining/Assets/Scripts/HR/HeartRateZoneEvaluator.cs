using StressTraining.Core;
using StressTraining.Data;

namespace StressTraining.HR
{
    /// <summary>
    /// Baseline-relative zone classification (spec §19). During tasks the user
    /// only ever sees the zone (Stable / Elevated / High / SignalLost) — raw BPM
    /// is reserved for the developer panel, post-session summary and research
    /// exports. Thresholds are PROJECT_HEURISTIC via HrZoneConfig.
    /// </summary>
    public sealed class HeartRateZoneEvaluator
    {
        private readonly HrZoneConfig _cfg;

        public float BaselineAvgBpm { get; private set; } = -1f;

        public HeartRateZoneEvaluator(HrZoneConfig cfg)
        {
            _cfg = cfg;
        }

        public void SetBaseline(float baselineAvgBpm) => BaselineAvgBpm = baselineAvgBpm;

        public HrZone Classify(int bpm, bool signalLost)
        {
            if (signalLost || bpm <= 0) return HrZone.SignalLost;
            if (BaselineAvgBpm <= 0)
            {
                // No baseline yet (pre-baseline phases / Missing quality):
                // relative zones are undefined — report Stable, documented limitation.
                return HrZone.Stable;
            }
            float delta = bpm - BaselineAvgBpm;
            if (delta >= _cfg.highDeltaBpm) return HrZone.High;
            if (delta >= _cfg.elevatedDeltaBpm) return HrZone.Elevated;
            return HrZone.Stable;
        }
    }
}
