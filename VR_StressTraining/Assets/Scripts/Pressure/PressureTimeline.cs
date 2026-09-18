using System;
using System.Collections.Generic;

namespace StressTraining.Pressure
{
    /// <summary>Pressure phases keyed to the remaining fraction of the global timer.</summary>
    public enum PressureStage
    {
        Stable = 0,
        Early = 1,
        Mid = 2,
        Late = 3,
        Critical = 4,
        Expired = 5
    }

    public enum PressureEventKind
    {
        Rumble = 0,
        DustBurst = 1,
        LightFlicker = 2,
        CeilingCreak = 3
    }

    /// <summary>
    /// Centralized PROJECT_HEURISTIC values for the pressure prototype. These are
    /// design values, not scientifically validated thresholds.
    /// </summary>
    public static class PressureHeuristics
    {
        public const float EarlyThreshold = 0.70f;
        public const float MidThreshold = 0.50f;
        public const float LateThreshold = 0.30f;
        public const float CriticalThreshold = 0.10f;
        public const float ExpiredThreshold = 0.00f;

        public const float FirstEventMaximumFraction = 0.68f;
        public const float LastEventMinimumFraction = 0.04f;
        public const float EventMinimumMagnitude = 0.40f;
        public const float EventMagnitudeRange = 0.60f;

        public const float Level1DurationMultiplier = 1.00f;
        public const float Level2DurationMultiplier = 1.00f;
        public const float Level3DurationMultiplier = 1.00f;

        public const float Level1IntensityCap = 0.50f;
        public const float Level2IntensityCap = 0.75f;
        public const float Level3IntensityCap = 1.00f;

        public const int Level1EventCount = 3;
        public const int Level2EventCount = 5;
        public const int Level3EventCount = 8;

        public const float Level1ShakeMeters = 0.004f;
        public const float Level2ShakeMeters = 0.008f;
        public const float Level3ShakeMeters = 0.012f;

        public const float Level1AudioCap = 0.40f;
        public const float Level2AudioCap = 0.70f;
        public const float Level3AudioCap = 1.00f;

        public const float Level1CrackAlpha = 0.35f;
        public const float Level2CrackAlpha = 0.60f;
        public const float Level3CrackAlpha = 0.85f;

        public const float CoreEarlyEventMinimumFraction = 0.52f;
        public const float CoreMidEventMaximumFraction = 0.49f;
        public const float CoreMidEventMinimumFraction = 0.32f;

        public const int SegmentPairCount = 4;
        public const float SegmentSpanMeters = 3.60f;
        public const float SegmentWidthMeters = 3.10f;
        public const float SegmentThicknessMeters = 0.03f;
        public const float SegmentGapMeters = 0.05f;
        public const float FloorOffsetMeters = 0.015f;
        public const float CeilingOffsetMeters = 2.98f;
        public const float SegmentCloseDistanceMeters = 1.40f;
        public const float SegmentCloseSeconds = 6.0f;
        public const float EventPulseDecayPerSecond = 0.80f;

        public const int DustMoteCount = 12;
        public const float DustDurationSeconds = 1.35f;
        public const float DustForwardDistanceMeters = 1.20f;
        public const float CeilingEventDurationSeconds = 1.25f;
        public const float CeilingEventTravelMeters = 0.10f;
        public const float CeilingEventForwardDistanceMeters = 1.45f;
        public const float CriticalVoidBehindDistanceMeters = 2.80f;

        public const float FinaleFadeSeconds = 2.50f;
        public const float FinaleSafetyFallbackSeconds = 4.00f;

        public const float RumbleSourceVolumeCap = 0.22f;
        public const float CeilingSourceVolumeCap = 0.28f;
        public const float DustSourceVolumeCap = 0.20f;
        public const float CriticalSourceVolumeCap = 0.25f;
    }

    /// <summary>One deterministic scheduled event, derived only from the pressure seed and level.</summary>
    [Serializable]
    public sealed class PressureEventDefinition
    {
        public float atRemainingFraction;
        public PressureEventKind kind;
        public float magnitude;
    }

    /// <summary>
    /// Per-level pressure tuning. Every numeric value is a PROJECT_HEURISTIC.
    /// No current BPM value is read by this type.
    /// </summary>
    [Serializable]
    public sealed class PressureLevelConfig
    {
        public int level = 1;
        public float globalDurationMultiplier = 1f;
        public float intensityCap = 0.5f;
        public int scheduledEventCount = 3;
        public float shakeAmplitudeMeters = 0.004f;
        public float audioRampCap = 0.4f;
        public float crackMaxAlpha = 0.35f;

        public static int ClampLevel(int level) => Math.Max(1, Math.Min(3, level));

        public static PressureLevelConfig Get(int requestedLevel)
        {
            int level = ClampLevel(requestedLevel);
            switch (level)
            {
                case 1:
                    return new PressureLevelConfig
                    {
                        level = 1,
                        globalDurationMultiplier = PressureHeuristics.Level1DurationMultiplier,
                        intensityCap = PressureHeuristics.Level1IntensityCap,
                        scheduledEventCount = PressureHeuristics.Level1EventCount,
                        shakeAmplitudeMeters = PressureHeuristics.Level1ShakeMeters,
                        audioRampCap = PressureHeuristics.Level1AudioCap,
                        crackMaxAlpha = PressureHeuristics.Level1CrackAlpha
                    };
                case 2:
                    return new PressureLevelConfig
                    {
                        level = 2,
                        globalDurationMultiplier = PressureHeuristics.Level2DurationMultiplier,
                        intensityCap = PressureHeuristics.Level2IntensityCap,
                        scheduledEventCount = PressureHeuristics.Level2EventCount,
                        shakeAmplitudeMeters = PressureHeuristics.Level2ShakeMeters,
                        audioRampCap = PressureHeuristics.Level2AudioCap,
                        crackMaxAlpha = PressureHeuristics.Level2CrackAlpha
                    };
                default:
                    return new PressureLevelConfig
                    {
                        level = 3,
                        globalDurationMultiplier = PressureHeuristics.Level3DurationMultiplier,
                        intensityCap = PressureHeuristics.Level3IntensityCap,
                        scheduledEventCount = PressureHeuristics.Level3EventCount,
                        shakeAmplitudeMeters = PressureHeuristics.Level3ShakeMeters,
                        audioRampCap = PressureHeuristics.Level3AudioCap,
                        crackMaxAlpha = PressureHeuristics.Level3CrackAlpha
                    };
            }
        }
    }

    /// <summary>
    /// Pure deterministic pressure timeline. Time moves from remaining fraction 1
    /// toward 0, so scheduled events are sorted in descending fraction order.
    /// </summary>
    public sealed class PressureTimeline
    {
        public const float EarlyBelow = PressureHeuristics.EarlyThreshold;
        public const float MidBelow = PressureHeuristics.MidThreshold;
        public const float LateBelow = PressureHeuristics.LateThreshold;
        public const float CriticalBelow = PressureHeuristics.CriticalThreshold;
        public const float ExpiredAt = PressureHeuristics.ExpiredThreshold;

        public PressureLevelConfig Level { get; }
        public IReadOnlyList<PressureEventDefinition> Events => _events;
        public int NextEventIndex => _nextEventIndex;

        private readonly List<PressureEventDefinition> _events;
        private int _nextEventIndex;

        public PressureTimeline(int pressureSeed, int pressureLevel)
        {
            Level = PressureLevelConfig.Get(pressureLevel);
            var rng = new Random(pressureSeed);
            _events = new List<PressureEventDefinition>(Level.scheduledEventCount);

            // Every level includes the three locked prototype cues: an Early
            // rumble and both Mid transient effects. Their order is seeded, and
            // all event times/magnitudes remain reproducible from seed + level.
            var coreKinds = new[]
            {
                PressureEventKind.Rumble,
                PressureEventKind.DustBurst,
                PressureEventKind.CeilingCreak
            };
            for (int i = coreKinds.Length - 1; i > 0; i--)
            {
                int swapIndex = rng.Next(0, i + 1);
                var temporary = coreKinds[i];
                coreKinds[i] = coreKinds[swapIndex];
                coreKinds[swapIndex] = temporary;
            }

            for (int i = 0; i < Level.scheduledEventCount; i++)
            {
                bool isCore = i < coreKinds.Length;
                var kind = isCore
                    ? coreKinds[i]
                    : (PressureEventKind)rng.Next(0, 4);

                float minimumFraction;
                float maximumFraction;
                if (isCore && kind == PressureEventKind.Rumble)
                {
                    minimumFraction = PressureHeuristics.CoreEarlyEventMinimumFraction;
                    maximumFraction = PressureHeuristics.FirstEventMaximumFraction;
                }
                else if (isCore && (kind == PressureEventKind.DustBurst ||
                                    kind == PressureEventKind.CeilingCreak))
                {
                    minimumFraction = PressureHeuristics.CoreMidEventMinimumFraction;
                    maximumFraction = PressureHeuristics.CoreMidEventMaximumFraction;
                }
                else
                {
                    minimumFraction = PressureHeuristics.LastEventMinimumFraction;
                    maximumFraction = kind == PressureEventKind.DustBurst ||
                                      kind == PressureEventKind.CeilingCreak
                        ? PressureHeuristics.CoreMidEventMaximumFraction
                        : PressureHeuristics.FirstEventMaximumFraction;
                }

                float fraction = minimumFraction +
                                 (float)rng.NextDouble() *
                                 (maximumFraction - minimumFraction);
                _events.Add(new PressureEventDefinition
                {
                    atRemainingFraction = fraction,
                    kind = kind,
                    magnitude = PressureHeuristics.EventMinimumMagnitude +
                                (float)rng.NextDouble() * PressureHeuristics.EventMagnitudeRange
                });
            }

            _events.Sort((a, b) =>
            {
                int byFraction = b.atRemainingFraction.CompareTo(a.atRemainingFraction);
                if (byFraction != 0) return byFraction;
                int byKind = a.kind.CompareTo(b.kind);
                if (byKind != 0) return byKind;
                return b.magnitude.CompareTo(a.magnitude);
            });
        }

        /// <summary>
        /// Exact boundary policy: 0.70 is Stable, 0.50 is Early, 0.30 is Mid,
        /// 0.10 is Late, and 0 is Expired. Each new stage starts strictly below
        /// its named threshold, except Expired which starts at zero.
        /// </summary>
        public static PressureStage StageFor(double remainingFraction)
        {
            // Compare double input against exact double literals. Using the public
            // float constants here would widen values such as 0.30f to roughly
            // 0.3000000119, incorrectly classifying an exact 0.30 input as Late.
            if (remainingFraction <= 0.00d) return PressureStage.Expired;
            if (remainingFraction < 0.10d) return PressureStage.Critical;
            if (remainingFraction < 0.30d) return PressureStage.Late;
            if (remainingFraction < 0.50d) return PressureStage.Mid;
            if (remainingFraction < 0.70d) return PressureStage.Early;
            return PressureStage.Stable;
        }

        /// <summary>
        /// Smooth monotonic intensity ramp. It is zero in Stable and reaches the
        /// selected level cap at expiration. Input outside 0..1 is safely clamped.
        /// </summary>
        public float Intensity(double remainingFraction)
        {
            double clamped = Math.Max(0.0, Math.Min(1.0, remainingFraction));
            if (clamped >= EarlyBelow) return 0f;

            double t = 1.0 - clamped / EarlyBelow;
            double smooth = t * t * (3.0 - 2.0 * t);
            return (float)Math.Max(0.0, Math.Min(Level.intensityCap,
                smooth * Level.intensityCap));
        }

        /// <summary>Consumes one due event. Kept for small callers and tests.</summary>
        public bool TryConsumeEvent(double remainingFraction, out PressureEventDefinition evt)
        {
            if (_nextEventIndex < _events.Count &&
                remainingFraction <= _events[_nextEventIndex].atRemainingFraction)
            {
                evt = _events[_nextEventIndex++];
                return true;
            }

            evt = null;
            return false;
        }

        /// <summary>
        /// Consumes every event made due by the current fraction. A large frame or
        /// penalty jump therefore cannot skip intermediate events.
        /// </summary>
        public int ConsumeDueEvents(double remainingFraction,
            ICollection<PressureEventDefinition> destination)
        {
            int consumed = 0;
            while (TryConsumeEvent(remainingFraction, out var evt))
            {
                destination?.Add(evt);
                consumed++;
            }
            return consumed;
        }

        public void ResetEventCursor() => _nextEventIndex = 0;
    }
}
