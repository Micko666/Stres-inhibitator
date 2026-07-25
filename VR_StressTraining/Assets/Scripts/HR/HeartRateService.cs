using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Persistence;
using UnityEngine;

namespace StressTraining.HR
{
    public struct ProductionHrReadiness
    {
        public bool IsReady;
        public bool IsRealExternalSource;
        public bool HasFreshValidSample;
        public string Reason;
    }

    /// <summary>
    /// Central HR hub. Owns the active source, enriches raw samples with session
    /// context, performs stale detection, classifies zones and streams records to
    /// hr.jsonl. The receiver never interprets stress; this service only labels
    /// zones relative to baseline — interpretation happens in the scheduler.
    ///
    /// Must be ticked once per frame with UNSCALED real delta time by the
    /// bootstrapper. Paused samples are tagged isPaused=true and excluded from
    /// task aggregation (spec §16/§17).
    /// </summary>
    public sealed class HeartRateService
    {
        private readonly HrZoneConfig _cfg;
        private readonly AppErrorService _errors;
        public HeartRateZoneEvaluator ZoneEvaluator { get; }

        public IHeartRateSource ActiveSource { get; private set; }

        public int CurrentBpm { get; private set; } = -1;
        public HrZone CurrentZone { get; private set; } = HrZone.SignalLost;
        public bool IsReceiving { get; private set; }
        public float SessionPeakBpm { get; private set; } = -1;

        public event Action<HeartRateSampleRecord> SampleAccepted;
        public event Action<HrZone> ZoneChanged;
        public event Action SignalLost;
        public event Action SignalRecovered;

        // Session context (set by SessionCoordinator; null outside sessions)
        private HeartRateLogWriter _writer;
        private string _sessionId = "";
        private Func<(bool isPaused, string appState, TaskType taskType, string blockId, string trialId)> _contextProvider;

        private readonly List<RawHrSample> _drainBuffer = new List<RawHrSample>(16);
        private double _lastSampleRealtime = -1;
        private double _lastGoodSampleRealtime = -1;
        private HrQualityStatus _lastAcceptedQuality = HrQualityStatus.Unknown;
        private double _realtimeNow;
        private double _sessionStartRealtime;
        private bool _wasReceiving;
        private int _sessionSampleCount;
        private long _sessionSampleSum;
        private int _sessionMaxBpm = -1;

        // Per-block aggregation
        private bool _aggregating;
        private int _aggCount;
        private long _aggSum;
        private int _aggMax = -1;
        private double _aggElevatedSeconds;

        // Totals for validity evaluation
        public int TotalSamples { get; private set; }
        public int ValidSamples { get; private set; }

        public HeartRateService(HrZoneConfig cfg, AppErrorService errors)
        {
            _cfg = cfg;
            _errors = errors;
            ZoneEvaluator = new HeartRateZoneEvaluator(cfg);
        }

        // ── HR mode switching (spec §35.1) ───────────────────────────────
        // The service holds one source per mode; SetMode swaps the active one.
        // Disconnected = no source at all (honest "Sat nije povezan" display).
        public HeartRateMode Mode { get; private set; } = HeartRateMode.Disconnected;
        public event Action<HeartRateMode> ModeChanged;

        private SimulatedHeartRateSource _simulatedSource;
        private NetworkHeartRateSource _networkSource;
        private NativeAdbHeartRateSource _nativeAdbSource;

        public void ConfigureSources(SimulatedHeartRateSource simulated,
            NetworkHeartRateSource network, NativeAdbHeartRateSource nativeAdb)
        {
            _simulatedSource = simulated;
            _networkSource = network;
            _nativeAdbSource = nativeAdb;
        }

        public void SetMode(HeartRateMode mode)
        {
            if (Mode == mode && ActiveSource != null) { return; }
            Mode = mode;
            switch (mode)
            {
                case HeartRateMode.Simulated: SetSource(_simulatedSource); break;
                case HeartRateMode.Network: SetSource(_networkSource); break;
                case HeartRateMode.NativeAdbPlaceholder: SetSource(_nativeAdbSource); break;
                default: SetSource(null); break;   // Disconnected
            }
            ModeChanged?.Invoke(mode);
        }

        public void SetSource(IHeartRateSource source)
        {
            if (ActiveSource == source) return;
            ActiveSource?.StopSource();
            ActiveSource = source;
            CurrentBpm = -1;
            IsReceiving = false;
            _lastSampleRealtime = -1;
            _lastGoodSampleRealtime = -1;
            _lastAcceptedQuality = HrQualityStatus.Unknown;
            _wasReceiving = false;
            UpdateZone(HrZone.SignalLost);
            ActiveSource?.StartSource();
        }

        public void AttachSession(string sessionId, HeartRateLogWriter writer,
            Func<(bool, string, TaskType, string, string)> contextProvider)
        {
            _sessionId = sessionId ?? "";
            _writer = writer;
            _contextProvider = contextProvider;
            SessionPeakBpm = -1;
            TotalSamples = 0;
            ValidSamples = 0;
            _sessionSampleCount = 0;
            _sessionSampleSum = 0;
            _sessionMaxBpm = -1;
            _sessionStartRealtime = _realtimeNow;
        }

        public void DetachSession()
        {
            _writer = null;
            _sessionId = "";
            _contextProvider = null;
        }

        /// <summary>
        /// Teardown: stops the active AND every configured source so no background
        /// thread or UDP socket outlives the bootstrapper (a leaked Network thread
        /// would keep the port bound between editor Play Mode runs). Safe to call
        /// twice; raises no events — listeners are being disposed at this point.
        /// </summary>
        public void Shutdown()
        {
            DetachSession();
            ActiveSource?.StopSource();
            ActiveSource = null;
            _simulatedSource?.StopSource();
            _networkSource?.StopSource();
            _nativeAdbSource?.StopSource();
            Mode = HeartRateMode.Disconnected;
            CurrentBpm = -1;
            IsReceiving = false;
            _wasReceiving = false;
            CurrentZone = HrZone.SignalLost;
        }

        /// <summary>Tick once per frame with unscaled real dt.</summary>
        public void Update(double realDeltaSeconds)
        {
            if (realDeltaSeconds < 0) return;
            _realtimeNow += realDeltaSeconds;
            (ActiveSource as SimulatedHeartRateSource)?.Advance(realDeltaSeconds);

            _drainBuffer.Clear();
            if (ActiveSource != null && ActiveSource.DrainSamples(_drainBuffer) > 0)
            {
                foreach (var raw in _drainBuffer) Accept(raw);
            }

            // Stale detection
            bool receiving = _lastSampleRealtime >= 0 &&
                             _realtimeNow - _lastSampleRealtime <= _cfg.staleSignalSeconds;
            if (receiving != _wasReceiving)
            {
                _wasReceiving = receiving;
                IsReceiving = receiving;
                if (!receiving)
                {
                    CurrentBpm = -1;
                    UpdateZone(HrZone.SignalLost);
                    SignalLost?.Invoke();
                }
                else
                {
                    SignalRecovered?.Invoke();
                }
            }

            if (_aggregating && CurrentZone >= HrZone.Elevated && !IsPausedNow())
                _aggElevatedSeconds += realDeltaSeconds;
        }

        private bool IsPausedNow()
        {
            var ctx = _contextProvider?.Invoke();
            return ctx?.Item1 ?? false;
        }

        private void Accept(RawHrSample raw)
        {
            TotalSamples++;
            bool plausible = raw.Bpm >= _cfg.minPlausibleBpm && raw.Bpm <= _cfg.maxPlausibleBpm;
            if (!plausible) return; // counted, not used
            ValidSamples++;

            CurrentBpm = raw.Bpm;
            _lastSampleRealtime = _realtimeNow;
            if (raw.Bpm > SessionPeakBpm) SessionPeakBpm = raw.Bpm;

            var ctx = _contextProvider?.Invoke() ?? (false, "", TaskType.None, "", "");
            bool isPaused = ctx.Item1;

            var quality = HrQualityStatus.Good;
            if (raw.SignalAgeMs > _cfg.staleSignalSeconds * 1000.0)
                quality = HrQualityStatus.Stale;
            _lastAcceptedQuality = quality;
            if (quality == HrQualityStatus.Good)
                _lastGoodSampleRealtime = _realtimeNow;

            UpdateZone(ZoneEvaluator.Classify(raw.Bpm, signalLost: false));

            var record = new HeartRateSampleRecord
            {
                bpm = raw.Bpm,
                receivedAtUtcIso = raw.ReceivedAtUtcIso,
                sourceTimestampUtcIso = raw.SourceTimestampUtcIso ?? "",
                monotonicReceiveSeconds = Math.Max(0, _realtimeNow - _sessionStartRealtime),
                signalAgeMs = raw.SignalAgeMs,
                qualityStatus = quality,
                sourceType = ActiveSource?.SourceType ?? HrSourceType.None,
                sequence = raw.Sequence,
                isPaused = isPaused,
                sessionState = ctx.Item2 ?? "",
                taskType = ctx.Item3,
                blockId = ctx.Item4 ?? "",
                trialId = ctx.Item5 ?? ""
            };

            _writer?.Log(record);
            SampleAccepted?.Invoke(record);

            if (!isPaused)
            {
                _sessionSampleCount++;
                _sessionSampleSum += raw.Bpm;
                if (raw.Bpm > _sessionMaxBpm) _sessionMaxBpm = raw.Bpm;
            }

            // Paused samples never enter the task HR aggregate (spec §16).
            if (_aggregating && !isPaused)
            {
                _aggCount++;
                _aggSum += raw.Bpm;
                if (raw.Bpm > _aggMax) _aggMax = raw.Bpm;
            }
        }

        private void UpdateZone(HrZone zone)
        {
            if (zone == CurrentZone) return;
            CurrentZone = zone;
            ZoneChanged?.Invoke(zone);
        }

        /// <summary>
        /// Production gate: only a running NetworkBridge with a fresh, plausible,
        /// quality-Good sample may unlock baseline. This is an engineering gate,
        /// not a psychological-stress assessment.
        /// </summary>
        public ProductionHrReadiness GetProductionReadiness()
        {
            var result = new ProductionHrReadiness
            {
                IsReady = false,
                IsRealExternalSource = false,
                HasFreshValidSample = false,
                Reason = "Poveži uređaj za mjerenje pulsa."
            };

            if (Mode == HeartRateMode.Simulated ||
                ActiveSource?.SourceType == HrSourceType.Simulated)
            {
                result.Reason = "Simulirani puls nije dozvoljen u produkcijskoj sesiji.";
                return result;
            }
            if (Mode == HeartRateMode.NativeAdbPlaceholder ||
                ActiveSource?.SourceType == HrSourceType.NativeAdbPlaceholder)
            {
                result.Reason = "Native ADB izvor je samo placeholder i nije stvarna veza.";
                return result;
            }
            if (ActiveSource == null || ActiveSource.SourceType != HrSourceType.NetworkBridge)
            {
                result.Reason = "Poveži uređaj za mjerenje pulsa.";
                return result;
            }

            result.IsRealExternalSource = true;
            if (!ActiveSource.IsRunning)
            {
                result.Reason = "HR mrežni prijemnik nije pokrenut.";
                return result;
            }
            if (!IsReceiving || CurrentBpm < _cfg.minPlausibleBpm ||
                CurrentBpm > _cfg.maxPlausibleBpm)
            {
                result.Reason = "Čeka se validan uzorak pulsa sa povezanog uređaja.";
                return result;
            }

            double goodAge = _lastGoodSampleRealtime < 0
                ? double.PositiveInfinity : _realtimeNow - _lastGoodSampleRealtime;
            result.HasFreshValidSample = _lastAcceptedQuality == HrQualityStatus.Good &&
                                         goodAge <= _cfg.staleSignalSeconds;
            if (!result.HasFreshValidSample)
            {
                result.Reason = "Posljednji HR uzorak nije svjež ili nije odgovarajućeg kvaliteta.";
                return result;
            }

            result.IsReady = true;
            result.Reason = "Mjerenje pulsa je povezano i prima svježe uzorke.";
            return result;
        }

        public bool CanUnlockProductionBaseline => GetProductionReadiness().IsReady;
        public double LastGoodSampleAgeSeconds => _lastGoodSampleRealtime < 0
            ? double.PositiveInfinity : Math.Max(0, _realtimeNow - _lastGoodSampleRealtime);

        // ── Per-task aggregation (used per block by the coordinator) ─────

        public void BeginTaskAggregation()
        {
            _aggregating = true;
            _aggCount = 0;
            _aggSum = 0;
            _aggMax = -1;
            _aggElevatedSeconds = 0;
        }

        public (float avgBpm, int maxBpm, double elevatedSeconds, int sampleCount) EndTaskAggregation()
        {
            _aggregating = false;
            float avg = _aggCount > 0 ? (float)_aggSum / _aggCount : -1f;
            return (avg, _aggMax, _aggElevatedSeconds, _aggCount);
        }

        public float ValidSampleRatio => TotalSamples > 0 ? (float)ValidSamples / TotalSamples : -1f;
        public int SessionActiveSampleCount => _sessionSampleCount;
        public float SessionAverageBpm => _sessionSampleCount > 0
            ? (float)_sessionSampleSum / _sessionSampleCount : -1f;
        public int SessionActiveMaxBpm => _sessionMaxBpm;
    }
}
