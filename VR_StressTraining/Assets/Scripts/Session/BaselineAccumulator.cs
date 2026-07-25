using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;

namespace StressTraining.Session
{
    /// <summary>
    /// Pure baseline statistics accumulator (spec §18). Fed by HeartRateService
    /// during the Baseline state; produces BaselineSummaryData at the end.
    /// Invalid samples are counted but excluded from statistics.
    /// </summary>
    public sealed class BaselineAccumulator
    {
        private readonly BaselineConfig _cfg;
        private readonly List<int> _validBpm = new List<int>(256);
        private int _totalSamples;
        private double _lastValidAtSeconds = -1;
        private int _gapCount;

        public BaselineAccumulator(BaselineConfig cfg)
        {
            _cfg = cfg;
        }

        public void AddSample(int bpm, bool isValid, double monotonicSeconds)
        {
            _totalSamples++;
            if (!isValid) return;

            if (_lastValidAtSeconds >= 0 &&
                monotonicSeconds - _lastValidAtSeconds > _cfg.maxSignalGapSeconds)
                _gapCount++;
            _lastValidAtSeconds = monotonicSeconds;
            _validBpm.Add(bpm);
        }

        public BaselineSummaryData ComputeSummary(float measuredDurationSeconds)
        {
            var s = new BaselineSummaryData
            {
                sampleCount = _totalSamples,
                validSampleCount = _validBpm.Count,
                validSampleRatio = _totalSamples > 0 ? (float)_validBpm.Count / _totalSamples : 0f,
                signalGapCount = _gapCount,
                durationSeconds = measuredDurationSeconds
            };

            if (_validBpm.Count == 0)
            {
                s.quality = BaselineQuality.Missing;
                return s;
            }

            long sum = 0;
            int min = int.MaxValue, max = int.MinValue;
            foreach (var v in _validBpm)
            {
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            s.averageBpm = (float)sum / _validBpm.Count;
            s.minBpm = min;
            s.maxBpm = max;

            var sorted = new List<int>(_validBpm);
            sorted.Sort();
            s.medianBpm = sorted.Count % 2 == 1
                ? sorted[sorted.Count / 2]
                : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) * 0.5f;

            bool good = _validBpm.Count >= _cfg.minValidSamples &&
                        s.validSampleRatio >= _cfg.minValidSampleRatio;
            s.quality = good ? BaselineQuality.Good : BaselineQuality.Low;
            return s;
        }
    }

    /// <summary>
    /// Pure recovery statistics (spec §18). Tracks the quiet post-session
    /// measurement and time-to-working-zone. The working zone threshold
    /// (baseline + recoveryZoneDeltaBpm) is PROJECT_HEURISTIC.
    /// </summary>
    public sealed class RecoveryEvaluator
    {
        private readonly RecoveryConfig _cfg;
        private readonly float _baselineAvgBpm;
        private readonly float _sessionPeakBpm;
        private readonly List<int> _bpm = new List<int>(128);
        private float _firstZoneEntrySeconds = -1;

        public RecoveryEvaluator(RecoveryConfig cfg, float baselineAvgBpm, float sessionPeakBpm)
        {
            _cfg = cfg;
            _baselineAvgBpm = baselineAvgBpm;
            _sessionPeakBpm = sessionPeakBpm;
        }

        public float WorkingZoneCeilingBpm =>
            _baselineAvgBpm > 0 ? _baselineAvgBpm + _cfg.recoveryZoneDeltaBpm : -1;

        public void AddSample(int bpm, double secondsSinceRecoveryStart)
        {
            _bpm.Add(bpm);
            if (_firstZoneEntrySeconds < 0 && WorkingZoneCeilingBpm > 0 && bpm <= WorkingZoneCeilingBpm)
                _firstZoneEntrySeconds = (float)secondsSinceRecoveryStart;
        }

        public RecoverySummaryData ComputeSummary(float measuredDurationSeconds)
        {
            var s = new RecoverySummaryData { durationSeconds = measuredDurationSeconds };
            if (_bpm.Count == 0) return s;

            long sum = 0;
            foreach (var v in _bpm) sum += v;
            s.averageBpm = (float)sum / _bpm.Count;

            if (_sessionPeakBpm > 0) s.deltaFromSessionPeakBpm = _sessionPeakBpm - s.averageBpm;
            if (_baselineAvgBpm > 0) s.deltaFromBaselineBpm = s.averageBpm - _baselineAvgBpm;
            s.secondsToWorkingZone = _firstZoneEntrySeconds;
            s.reachedWorkingZone = _firstZoneEntrySeconds >= 0;
            return s;
        }
    }
}
