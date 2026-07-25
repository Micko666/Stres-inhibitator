using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.Adaptation
{
    /// <summary>One named, ordered rule. First rule returning a directive wins.</summary>
    public sealed class AdaptationRule
    {
        public string Id;
        public string Description;
        public Func<AdaptationInput, TaskMetricsInput, AdaptationConfig, AdaptationDirective?> Evaluate;
    }

    /// <summary>
    /// Ordered, transparent rule tables (spec §24). Machine-readable rule ids are
    /// recorded per decision; human text is produced by AdaptationExplanationBuilder.
    ///
    /// Hard constraints encoded here:
    ///  1. Local task and global pressure are never increased simultaneously.
    ///  4. High accuracy at high physiological/subjective cost does NOT increase.
    ///  5. Low accuracy + low arousal + low load → RepeatForStability (unclear task).
    ///  6. High frustration holds or decreases.
    ///  7. Dominant Temporal Demand blocks further time shortening (all levels shorten time).
    ///  8. Dominant Mental Demand slows n-back increases.
    ///  9. Invalid SSQ/session blocks normal increase (handled before rules run).
    /// </summary>
    public static class AdaptationRuleSet
    {
        // ── helpers ──────────────────────────────────────────────────────
        private static bool HighCost(AdaptationInput s, TaskMetricsInput m, AdaptationConfig c)
        {
            bool physio =
                (m.avgBpmDelta > -900 && m.avgBpmDelta >= c.sessionDeltaBpmHigh) ||
                (m.elevatedZoneRatio >= 0 && m.elevatedZoneRatio >= c.elevatedZoneRatioHigh);
            bool subjective = s.tlxFrustration >= 0 && s.tlxFrustration >= c.frustrationHigh ||
                              s.tlxTotal >= 0 && s.tlxTotal >= c.tlxTotalHigh;
            bool slowRecovery = !s.recoveryReachedZone ||
                                (s.recoverySeconds >= 0 && s.recoverySeconds > c.recoverySlowSeconds);
            return physio || subjective && slowRecovery;
        }

        private static bool LowArousal(AdaptationInput s, TaskMetricsInput m, AdaptationConfig c)
        {
            float delta = m.avgBpmDelta > -900 ? m.avgBpmDelta : s.sessionAvgBpmDelta;
            return delta > -900 && delta <= c.sessionDeltaBpmLow;
        }

        private static bool DominantDimension(AdaptationInput s, float dim, AdaptationConfig c)
        {
            if (dim < c.dominantDimensionMin) return false;
            float[] others = { s.tlxMental, s.tlxPhysical, s.tlxTemporal,
                               s.tlxPerformance, s.tlxEffort, s.tlxFrustration };
            int atLeast = 0;
            foreach (var o in others)
                if (o >= 0 && dim >= o + c.dominantDimensionMargin) atLeast++;
            // dominant = exceeds at least 4 of the other 5 dimensions by the margin
            return atLeast >= 4;
        }

        // ── task rules (ordered) ─────────────────────────────────────────
        public static readonly List<AdaptationRule> TaskRules = new List<AdaptationRule>
        {
            new AdaptationRule
            {
                Id = "T_STABILITY_REPEAT",
                Description = "Unstable accuracy across blocks → repeat level for stability",
                Evaluate = (s, m, c) =>
                    m.blockAccuracyStd > c.blockAccuracyStdHigh
                        ? AdaptationDirective.RepeatForStability : (AdaptationDirective?)null
            },
            new AdaptationRule
            {
                Id = "T_LOW_ACC_LOW_AROUSAL_REPEAT",
                Description = "Low accuracy with low arousal and low load → likely unclear task/low engagement; repeat, do not punish",
                Evaluate = (s, m, c) =>
                    m.accuracy >= 0 && m.accuracy < c.accuracyLow &&
                    LowArousal(s, m, c) &&
                    (s.tlxTotal < 0 || s.tlxTotal <= c.tlxTotalLow)
                        ? AdaptationDirective.RepeatForStability : (AdaptationDirective?)null
            },
            new AdaptationRule
            {
                Id = "T_LOW_ACC_DECREASE",
                Description = "Low accuracy → decrease",
                Evaluate = (s, m, c) =>
                    m.accuracy >= 0 && m.accuracy < c.accuracyLow
                        ? AdaptationDirective.Decrease : (AdaptationDirective?)null
            },
            new AdaptationRule
            {
                Id = "T_FRUSTRATION_HOLD",
                Description = "High frustration → hold (never increase under high frustration)",
                Evaluate = (s, m, c) =>
                    s.tlxFrustration >= 0 && s.tlxFrustration >= c.frustrationHigh
                        ? AdaptationDirective.Hold : (AdaptationDirective?)null
            },
            new AdaptationRule
            {
                Id = "T_HIGH_ACC_HIGH_COST_HOLD",
                Description = "High accuracy but high physiological/subjective cost → hold",
                Evaluate = (s, m, c) =>
                    m.accuracy >= c.accuracyHigh && HighCost(s, m, c)
                        ? AdaptationDirective.Hold : (AdaptationDirective?)null
            },
            new AdaptationRule
            {
                Id = "T_TEMPORAL_DOMINANT_HOLD",
                Description = "Dominant Temporal Demand → no further time shortening (every level-up shortens windows)",
                Evaluate = (s, m, c) =>
                    m.accuracy >= c.accuracyHigh && DominantDimension(s, s.tlxTemporal, c)
                        ? AdaptationDirective.Hold : (AdaptationDirective?)null
            },
            new AdaptationRule
            {
                Id = "T_MENTAL_DOMINANT_MEMORY_HOLD",
                Description = "Dominant Mental Demand slows MEMORY task increases (n-back, Corsi)",
                Evaluate = (s, m, c) =>
                    (m.taskType == TaskType.NBack || m.taskType == TaskType.CorsiSequence) &&
                    m.accuracy >= c.accuracyHigh && DominantDimension(s, s.tlxMental, c)
                        ? AdaptationDirective.Hold : (AdaptationDirective?)null
            },
            new AdaptationRule
            {
                Id = "T_HIGH_ACC_INCREASE",
                Description = "High accuracy at acceptable cost → increase",
                Evaluate = (s, m, c) =>
                    m.accuracy >= c.accuracyHigh
                        ? AdaptationDirective.Increase : (AdaptationDirective?)null
            },
            new AdaptationRule
            {
                Id = "T_DEFAULT_HOLD",
                Description = "Mid-band performance → stay in the zone",
                Evaluate = (s, m, c) => AdaptationDirective.Hold
            }
        };

        // ── pressure rules (evaluated AFTER task decisions) ──────────────
        public sealed class PressureRuleContext
        {
            public bool AnyTaskIncreased;
            public bool AnyTaskDecreasedOrRepeat;
        }

        public static AdaptationDirective EvaluatePressure(
            AdaptationInput s, AdaptationConfig c, PressureRuleContext ctx, List<string> firedRules)
        {
            // Rule 1: never raise task and pressure in the same decision.
            if (ctx.AnyTaskIncreased)
            {
                firedRules.Add("P_TASK_INCREASED_HOLD");
                return AdaptationDirective.Hold;
            }

            bool highCostGlobal =
                (s.sessionAvgBpmDelta > -900 && s.sessionAvgBpmDelta >= c.sessionDeltaBpmHigh) ||
                (s.totalActiveSeconds > 0 &&
                 s.elevatedOrHighSeconds / s.totalActiveSeconds >= c.elevatedZoneRatioHigh) ||
                (s.tlxFrustration >= 0 && s.tlxFrustration >= c.frustrationHigh);
            bool slowRecovery = !s.recoveryReachedZone ||
                                (s.recoverySeconds >= 0 && s.recoverySeconds > c.recoverySlowSeconds);

            if (highCostGlobal && slowRecovery)
            {
                firedRules.Add("P_HIGH_COST_DECREASE");
                return AdaptationDirective.Decrease;
            }
            if (highCostGlobal)
            {
                firedRules.Add("P_HIGH_COST_HOLD");
                return AdaptationDirective.Hold;
            }
            if (ctx.AnyTaskDecreasedOrRepeat)
            {
                firedRules.Add("P_TASKS_STRUGGLING_HOLD");
                return AdaptationDirective.Hold;
            }

            // "All stable" is judged over the tasks that actually RAN this session.
            bool allGood = true;
            foreach (var m in new[] { s.nBack, s.goNoGo, s.flanker, s.corsi })
                if (m.wasSelectedThisSession && m.accuracy < c.accuracyHigh) allGood = false;
            if (allGood)
            {
                firedRules.Add("P_ALL_STABLE_INCREASE");
                return AdaptationDirective.Increase;
            }

            firedRules.Add("P_DEFAULT_HOLD");
            return AdaptationDirective.Hold;
        }
    }
}
