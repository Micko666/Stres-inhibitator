using System;

namespace StressTraining.Adaptation
{
    /// <summary>
    /// Every threshold used by the between-session scheduler.
    /// ALL values are PROJECT_HEURISTIC — working values for this MVP, chosen to
    /// be conservative, centralized so a researcher can tune them, and NOT
    /// claimed to be clinically validated (spec §24). Rationale per value in
    /// docs/ADAPTATION_RULES.md.
    /// </summary>
    [Serializable]
    public sealed class AdaptationConfig
    {
        // Accuracy bands
        public float accuracyHigh = 0.85f;              // PROJECT_HEURISTIC
        public float accuracyLow = 0.55f;               // PROJECT_HEURISTIC

        // Subjective load (Raw NASA-TLX, each dimension and total normalized 0..100)
        public float frustrationHigh = 60f;             // PROJECT_HEURISTIC
        public float tlxTotalHigh = 65f;                // PROJECT_HEURISTIC
        public float tlxTotalLow = 30f;                 // PROJECT_HEURISTIC — low engagement candidate
        public float dominantDimensionMin = 60f;        // PROJECT_HEURISTIC — dimension must reach this…
        public float dominantDimensionMargin = 10f;     // …and exceed every other dimension by this

        // Physiological cost (relative to baseline)
        public float sessionDeltaBpmHigh = 18f;         // PROJECT_HEURISTIC — avg task BPM − baseline
        public float sessionDeltaBpmLow = 5f;           // PROJECT_HEURISTIC — below = low arousal
        public float elevatedZoneRatioHigh = 0.5f;      // PROJECT_HEURISTIC — fraction of task time above Stable

        // Recovery
        public float recoverySlowSeconds = 60f;         // PROJECT_HEURISTIC — slower than this = slow recovery

        // Stability across blocks (std of block accuracies)
        public float blockAccuracyStdHigh = 0.18f;      // PROJECT_HEURISTIC — unstable → RepeatForStability

        // Level bounds
        public int minLevel = 1;
        public int maxLevel = 3;
        public int minPressureLevel = 1;
        public int maxPressureLevel = 3;
    }
}
