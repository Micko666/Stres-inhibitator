using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;

namespace StressTraining.HR
{
    public enum SimulatedHrScenario
    {
        StableBaseline = 0,
        SlowRise = 1,
        SlowFall = 2,
        Spike = 3,
        SignalLoss = 4,
        Recovery = 5
    }

    /// <summary>
    /// Deterministic-enough mock HR source for development and demos (spec §17).
    /// Emits one sample per second. Scenario is switchable at runtime from the
    /// developer panel. Advance(dt) must be called each frame by HeartRateService
    /// with UNSCALED real delta time.
    /// </summary>
    public sealed class SimulatedHeartRateSource : IHeartRateSource
    {
        public HrSourceType SourceType => HrSourceType.Simulated;
        public bool IsRunning { get; private set; }

        public SimulatedHrScenario Scenario { get; private set; } = SimulatedHrScenario.StableBaseline;
        public float BaseBpm = 72f;                 // PROJECT_HEURISTIC — plausible resting default
        public float NoiseAmplitude = 2f;

        private readonly Random _rng = new Random(12345);
        private readonly List<RawHrSample> _pending = new List<RawHrSample>(8);
        private double _emitTimer;
        private double _scenarioTime;
        private float _currentBpm;
        private double _realtime;
        private int _sequence;

        private const double EmitIntervalSeconds = 1.0;

        public void StartSource()
        {
            IsRunning = true;
            _currentBpm = BaseBpm;
            _emitTimer = 0;
            _scenarioTime = 0;
            _realtime = 0;
            _sequence = 0;
            _pending.Clear();
        }

        public void StopSource() => IsRunning = false;

        public void SetScenario(SimulatedHrScenario scenario)
        {
            Scenario = scenario;
            _scenarioTime = 0;
        }

        /// <summary>Call once per frame with unscaled real dt before DrainSamples.</summary>
        public void Advance(double realDeltaSeconds)
        {
            if (!IsRunning || realDeltaSeconds <= 0) return;
            _realtime += realDeltaSeconds;
            _scenarioTime += realDeltaSeconds;
            _emitTimer += realDeltaSeconds;

            while (_emitTimer >= EmitIntervalSeconds)
            {
                _emitTimer -= EmitIntervalSeconds;
                if (Scenario == SimulatedHrScenario.SignalLoss) continue; // emit nothing

                _currentBpm = TargetBpm();
                float noise = (float)(_rng.NextDouble() * 2 - 1) * NoiseAmplitude;
                int bpm = UnityEngine.Mathf.Clamp((int)Math.Round(_currentBpm + noise), 35, 210);

                _pending.Add(new RawHrSample
                {
                    Bpm = bpm,
                    ReceivedAtUtcIso = UtcTime.NowIso(),
                    RealtimeAtReceive = _realtime,
                    SourceTimestampUtcIso = UtcTime.NowIso(),
                    Sequence = _sequence++,
                    IsLegacyPacket = false,
                    SignalAgeMs = 0
                });
            }
        }

        private float TargetBpm()
        {
            switch (Scenario)
            {
                case SimulatedHrScenario.SlowRise:
                    return Math.Min(BaseBpm + (float)_scenarioTime * 0.5f, BaseBpm + 45f);
                case SimulatedHrScenario.SlowFall:
                    return Math.Max(_currentBpm - (float)(0.4 * EmitIntervalSeconds), BaseBpm);
                case SimulatedHrScenario.Spike:
                    // sharp rise for 15 s, then decay back
                    return _scenarioTime < 15
                        ? BaseBpm + 40f
                        : Math.Max(BaseBpm, BaseBpm + 40f - (float)(_scenarioTime - 15) * 1.5f);
                case SimulatedHrScenario.Recovery:
                    return Math.Max(BaseBpm, BaseBpm + 30f - (float)_scenarioTime * 0.8f);
                default:
                    return BaseBpm;
            }
        }

        public int DrainSamples(List<RawHrSample> buffer)
        {
            int n = _pending.Count;
            if (n > 0)
            {
                buffer.AddRange(_pending);
                _pending.Clear();
            }
            return n;
        }
    }
}
