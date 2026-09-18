using NUnit.Framework;
using StressTraining.Adaptation;
using StressTraining.Data;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// Guards the methodological invariants of the between-session scheduler:
    ///  • heart rate alone never MOVES a level (it may block, never decide);
    ///  • an increase requires evidence of the cost of that performance;
    ///  • an unmeasured recovery is not evidence of a slow recovery;
    ///  • task difficulty and pressure are never raised in the same decision.
    /// Pure data-in/decision-out — no Unity runtime involved.
    /// </summary>
    public class SchedulerConsistencyTests
    {
        private static readonly AdaptationConfig Cfg = new AdaptationConfig();

        /// <summary>
        /// A session with complete, unremarkable data: mid-band accuracy, benign
        /// TLX, low arousal, measured and fast recovery. Every test starts here and
        /// perturbs exactly the field under test.
        /// </summary>
        private static AdaptationInput Baseline()
        {
            var input = new AdaptationInput
            {
                sessionId = "test-session",
                validityStatus = ValidityStatus.Valid,
                condition = SessionCondition.Pressure,
                currentPressureLevel = 2,

                baselineBpm = 70f,
                sessionAvgBpmDelta = 6f,          // below sessionDeltaBpmHigh (18)
                elevatedOrHighSeconds = 60,
                totalActiveSeconds = 600,         // ratio 0.10, below 0.5

                recoveryMeasured = true,
                recoveryReachedZone = true,
                recoverySeconds = 30f,            // below recoverySlowSeconds (60)

                tlxTotal = 40f,
                tlxMental = 40f,
                tlxPhysical = 40f,
                tlxTemporal = 40f,
                tlxPerformance = 40f,
                tlxEffort = 40f,
                tlxFrustration = 40f
            };

            foreach (var t in new[] { TaskType.NBack, TaskType.GoNoGo,
                                      TaskType.Flanker, TaskType.CorsiSequence })
            {
                var m = input.Metrics(t);
                m.currentLevel = 2;
                m.wasSelectedThisSession = t != TaskType.CorsiSequence;   // 3-of-4
                m.accuracy = 0.70f;               // mid band: >= 0.55, < 0.85
                m.blockAccuracyStd = 0.05f;       // stable
                m.avgBpmDelta = 8f;               // known, below 18
            }
            return input;
        }

        private static void ForEachSelected(AdaptationInput input, System.Action<TaskMetricsInput> mutate)
        {
            foreach (var t in new[] { TaskType.NBack, TaskType.GoNoGo, TaskType.Flanker })
                mutate(input.Metrics(t));
        }

        private static AdaptationDecisionData Decide(AdaptationInput input) =>
            new AdaptationScheduler(Cfg).Decide(input);

        // ── 1. HR-only high cost must not move the pressure level ────────────

        [Test]
        public void HrOnlyHighCostAndSlowRecovery_HoldsPressure_DoesNotDecrease()
        {
            var input = Baseline();
            input.elevatedOrHighSeconds = 360;      // ratio 0.60 -> highCostGlobal (HR)
            input.recoveryMeasured = true;
            input.recoveryReachedZone = false;      // slow recovery (HR)
            // No non-HR signal: TLX benign, no task went down.

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Hold, d.pressure.directive,
                "HR-derived cost alone must never decrease the pressure level.");
            Assert.AreEqual(input.currentPressureLevel, d.pressure.newLevel);
            CollectionAssert.Contains(d.firedRules, "P_HIGH_COST_HR_ONLY_HOLD");
            CollectionAssert.DoesNotContain(d.firedRules, "P_HIGH_COST_DECREASE");
        }

        [Test]
        public void HrOnlyHighCost_NeverIncreasesPressureEither()
        {
            var input = Baseline();
            input.elevatedOrHighSeconds = 360;
            input.recoveryReachedZone = false;

            var d = Decide(input);

            Assert.AreNotEqual(AdaptationDirective.Increase, d.pressure.directive);
            Assert.AreNotEqual(AdaptationDirective.Decrease, d.pressure.directive);
        }

        // ── 2. Corroborated high cost may still decrease ─────────────────────

        [Test]
        public void HighCostCorroboratedByFrustration_DecreasesPressure()
        {
            var input = Baseline();
            input.elevatedOrHighSeconds = 360;      // HR cost
            input.recoveryReachedZone = false;      // slow recovery
            input.tlxFrustration = 75f;             // non-HR corroboration

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Decrease, d.pressure.directive);
            Assert.AreEqual(1, d.pressure.newLevel, "pressure 2 -> 1");
            CollectionAssert.Contains(d.firedRules, "P_HIGH_COST_DECREASE");
        }

        [Test]
        public void HighCostCorroboratedByTaskDifficulty_DecreasesPressure()
        {
            var input = Baseline();
            input.elevatedOrHighSeconds = 360;
            input.recoveryReachedZone = false;
            ForEachSelected(input, m => m.accuracy = 0.40f);   // tasks actually struggling

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Decrease, d.pressure.directive);
            CollectionAssert.Contains(d.firedRules, "P_HIGH_COST_DECREASE");
        }

        // ── 3-4. Data-completeness gate before a task increase ───────────────

        [Test]
        public void HighAccuracyWithMissingTaskHeartRate_HoldsTaskLevel()
        {
            var input = Baseline();
            ForEachSelected(input, m =>
            {
                m.accuracy = 0.90f;
                m.avgBpmDelta = -999f;            // HR aggregate unknown
            });

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Hold, d.nBack.directive);
            Assert.AreEqual(2, d.nBack.newLevel, "level must not move without cost evidence");
            CollectionAssert.Contains(d.nBack.reasonCodes, "T_INSUFFICIENT_DATA_HOLD");
        }

        [Test]
        public void HighAccuracyWithMissingNasaTlx_HoldsTaskLevel()
        {
            var input = Baseline();
            input.tlxTotal = -1f;
            input.tlxMental = -1f;
            input.tlxPhysical = -1f;
            input.tlxTemporal = -1f;
            input.tlxPerformance = -1f;
            input.tlxEffort = -1f;
            input.tlxFrustration = -1f;
            ForEachSelected(input, m => m.accuracy = 0.90f);

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Hold, d.flanker.directive);
            Assert.AreEqual(2, d.flanker.newLevel);
            CollectionAssert.Contains(d.flanker.reasonCodes, "T_INSUFFICIENT_DATA_HOLD");
        }

        // ── 5. Complete data still allows a normal increase ──────────────────

        [Test]
        public void HighAccuracyWithCompleteLowCostData_StillIncreases()
        {
            var input = Baseline();
            ForEachSelected(input, m => m.accuracy = 0.90f);   // HR + TLX already present

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Increase, d.nBack.directive);
            Assert.AreEqual(3, d.nBack.newLevel, "level 2 -> 3");
            CollectionAssert.Contains(d.nBack.reasonCodes, "T_HIGH_ACC_INCREASE");
        }

        [Test]
        public void NotSelectedTask_KeepsLevelRegardlessOfData()
        {
            var input = Baseline();
            ForEachSelected(input, m => m.accuracy = 0.95f);

            var d = Decide(input);

            Assert.AreEqual(2, d.corsi.newLevel);
            CollectionAssert.Contains(d.corsi.reasonCodes, "NOT_SELECTED_THIS_SESSION");
        }

        // ── 6. Unmeasured recovery is not slow recovery ──────────────────────

        [Test]
        public void UnmeasuredRecovery_IsNotTreatedAsSlowRecovery()
        {
            var input = Baseline();
            input.elevatedOrHighSeconds = 360;      // HR cost present
            input.tlxFrustration = 75f;             // non-HR corroboration present
            input.recoveryMeasured = false;         // recovery never captured
            input.recoveryReachedZone = false;      // the default that used to imply "slow"
            input.recoverySeconds = -1f;

            var d = Decide(input);

            Assert.AreNotEqual(AdaptationDirective.Decrease, d.pressure.directive,
                "A recovery that was never measured must not act as evidence of slow recovery.");
        }

        [Test]
        public void MeasuredSlowRecovery_StillCounts()
        {
            var input = Baseline();
            input.elevatedOrHighSeconds = 360;
            input.tlxFrustration = 75f;
            input.recoveryMeasured = true;
            input.recoveryReachedZone = false;      // genuinely did not return to zone

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Decrease, d.pressure.directive);
        }

        [Test]
        public void UnmeasuredRecovery_DoesNotBlockAnIncreaseOnItsOwn()
        {
            var input = Baseline();
            input.recoveryMeasured = false;
            input.recoveryReachedZone = false;
            ForEachSelected(input, m => m.accuracy = 0.90f);

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Increase, d.goNoGo.directive);
        }

        // ── 7. sessionAvgBpmDelta is a live input ────────────────────────────

        // NOTE on isolating the HR terms: tlxFrustration cannot be used as the
        // non-HR corroboration here, because frustration >= frustrationHigh is ALSO
        // the third term of highCostGlobal — it would make the session high-cost by
        // itself and mask the HR input under test. tlxTotal only feeds
        // nonHrCorroboration, so it isolates the HR term cleanly.
        [Test]
        public void SessionAvgBpmDelta_DrivesGlobalHighCost()
        {
            var input = Baseline();
            input.sessionAvgBpmDelta = 25f;         // >= sessionDeltaBpmHigh (18)
            input.recoveryReachedZone = false;      // slow recovery, measured
            input.tlxTotal = 70f;                   // corroboration only (>= tlxTotalHigh 65)
            input.tlxFrustration = 40f;             // stays below frustrationHigh

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Decrease, d.pressure.directive,
                "sessionAvgBpmDelta must be read by the pressure rules.");

            // Same session with the delta back in the normal band stays put.
            var calm = Baseline();
            calm.sessionAvgBpmDelta = 6f;
            calm.recoveryReachedZone = false;
            calm.tlxTotal = 70f;
            calm.tlxFrustration = 40f;
            Assert.AreEqual(AdaptationDirective.Hold, Decide(calm).pressure.directive);
        }

        [Test]
        public void UnknownSessionAvgBpmDelta_DoesNotCountAsHighCost()
        {
            var input = Baseline();
            input.sessionAvgBpmDelta = -999f;       // sentinel
            input.recoveryReachedZone = false;
            input.tlxFrustration = 40f;             // no corroboration either

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Hold, d.pressure.directive);
            CollectionAssert.DoesNotContain(d.firedRules, "P_HIGH_COST_DECREASE");
        }

        // ── 8. Elevated-zone cost uses the session ratio, whose denominator exists ──

        [Test]
        public void SessionElevatedRatio_DrivesGlobalHighCost()
        {
            // tlxTotal (not tlxFrustration) supplies the corroboration — see the note
            // above SessionAvgBpmDelta_DrivesGlobalHighCost.
            var below = Baseline();
            below.elevatedOrHighSeconds = 299;      // 299/600 = 0.498 < 0.5
            below.recoveryReachedZone = false;
            below.tlxTotal = 70f;
            below.tlxFrustration = 40f;
            Assert.AreEqual(AdaptationDirective.Hold, Decide(below).pressure.directive);

            var above = Baseline();
            above.elevatedOrHighSeconds = 301;      // 301/600 = 0.502 >= 0.5
            above.recoveryReachedZone = false;
            above.tlxTotal = 70f;
            above.tlxFrustration = 40f;
            Assert.AreEqual(AdaptationDirective.Decrease, Decide(above).pressure.directive);
        }

        [Test]
        public void ZeroActiveSeconds_DoesNotDivideOrTriggerHighCost()
        {
            var input = Baseline();
            input.totalActiveSeconds = 0;
            input.elevatedOrHighSeconds = 500;
            input.recoveryReachedZone = false;
            input.tlxFrustration = 40f;

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Hold, d.pressure.directive);
        }

        // ── 9. Task and pressure never rise together ─────────────────────────

        [Test]
        public void TaskIncrease_ForcesPressureHold()
        {
            var input = Baseline();
            ForEachSelected(input, m => m.accuracy = 0.95f);   // all stable -> would raise pressure

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Increase, d.nBack.directive);
            Assert.AreEqual(AdaptationDirective.Hold, d.pressure.directive);
            Assert.AreEqual(input.currentPressureLevel, d.pressure.newLevel);
            CollectionAssert.Contains(d.firedRules, "P_TASK_INCREASED_HOLD");
        }

        [Test]
        public void PressureIncrease_OnlyWhenNoTaskIncreased()
        {
            // All selected tasks are at max level, so "high accuracy" cannot raise them;
            // the pressure rule may then rise on its own.
            var input = Baseline();
            ForEachSelected(input, m => { m.accuracy = 0.95f; m.currentLevel = 3; });

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Hold, d.nBack.directive);
            CollectionAssert.Contains(d.nBack.reasonCodes, "AT_MAX_LEVEL");
            Assert.AreEqual(AdaptationDirective.Increase, d.pressure.directive);
            Assert.AreEqual(3, d.pressure.newLevel);
        }

        // ── Preserved invariants ─────────────────────────────────────────────

        [Test]
        public void InvalidSession_FreezesEveryLevel()
        {
            var input = Baseline();
            input.validityStatus = ValidityStatus.IncompleteTimeExpired;
            ForEachSelected(input, m => m.accuracy = 0.95f);

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.NoDecisionInvalidSession, d.nBack.directive);
            Assert.AreEqual(AdaptationDirective.NoDecisionInvalidSession, d.pressure.directive);
            Assert.AreEqual(2, d.nBack.newLevel);
            Assert.AreEqual(2, d.pressure.newLevel);
            CollectionAssert.Contains(d.firedRules, "G_SESSION_INVALID");
        }

        [Test]
        public void DecisionStaysTraceable()
        {
            var input = Baseline();
            ForEachSelected(input, m => m.accuracy = 0.90f);

            var d = Decide(input);

            Assert.IsNotEmpty(d.firedRules);
            Assert.IsNotEmpty(d.humanExplanation);
            Assert.IsNotNull(d.inputSnapshotJson);
            Assert.IsNotEmpty(d.inputSnapshotJson);
            Assert.AreEqual("test-session", d.sessionId);
        }

        [Test]
        public void LevelsStayWithinOneToThree()
        {
            var low = Baseline();
            ForEachSelected(low, m => { m.accuracy = 0.20f; m.currentLevel = 1; });
            low.currentPressureLevel = 1;
            var dLow = Decide(low);
            Assert.AreEqual(1, dLow.nBack.newLevel);
            Assert.AreEqual(1, dLow.pressure.newLevel);

            var high = Baseline();
            ForEachSelected(high, m => { m.accuracy = 0.95f; m.currentLevel = 3; });
            high.currentPressureLevel = 3;
            var dHigh = Decide(high);
            Assert.AreEqual(3, dHigh.nBack.newLevel);
            Assert.AreEqual(3, dHigh.pressure.newLevel);
        }

        [Test]
        public void DecisionIsDeterministic()
        {
            var a = Decide(Baseline());
            var b = Decide(Baseline());
            Assert.AreEqual(a.nBack.directive, b.nBack.directive);
            Assert.AreEqual(a.pressure.directive, b.pressure.directive);
            CollectionAssert.AreEqual(a.firedRules, b.firedRules);
        }
    }
}
