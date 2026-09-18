using NUnit.Framework;
using StressTraining.Adaptation;
using StressTraining.Data;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// T_INSUFFICIENT_DATA_HOLD stops a task level from rising when the cost of the
    /// current level cannot be judged. The pressure table needed the same guard: with
    /// heart rate or NASA-TLX absent every hold rule passes on sentinel values, and
    /// P_ALL_STABLE_INCREASE would then raise the global level on accuracy alone.
    /// These tests hold that door shut while leaving the fully-informed increase open.
    /// </summary>
    public sealed class PressureInsufficientDataTests
    {
        private static readonly AdaptationConfig Cfg = new AdaptationConfig();

        /// <summary>
        /// A session that would otherwise reach P_ALL_STABLE_INCREASE: three tasks ran,
        /// all well above accuracyHigh, nothing struggling, recovery fast and measured.
        /// Individual tests then remove exactly one kind of cost evidence.
        /// </summary>
        private static AdaptationInput HighPerformanceSession()
        {
            var input = new AdaptationInput
            {
                sessionId = "pressure-guard",
                validityStatus = ValidityStatus.Valid,
                condition = SessionCondition.Pressure,
                currentPressureLevel = 1,

                baselineBpm = 70f,
                sessionAvgBpmDelta = 6f,
                elevatedOrHighSeconds = 60,
                totalActiveSeconds = 600,

                recoveryMeasured = true,
                recoveryReachedZone = true,
                recoverySeconds = 30f,

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
                m.currentLevel = 1;
                m.wasSelectedThisSession = t != TaskType.NBack;   // 3-of-4
                m.accuracy = 0.95f;                               // >= accuracyHigh
                m.blockAccuracyStd = 0.02f;                       // stable
                m.avgBpmDelta = 8f;
            }
            return input;
        }

        private static AdaptationDecisionData Decide(AdaptationInput input) =>
            new AdaptationScheduler(Cfg).Decide(input);

        private static void AssertPressureHeld(AdaptationDecisionData d, string because)
        {
            Assert.AreEqual(AdaptationDirective.Hold, d.pressure.directive, because);
            Assert.AreEqual(d.pressure.previousLevel, d.pressure.newLevel,
                "nivo pritiska se ne smije pomjeriti: " + because);
            CollectionAssert.Contains(d.firedRules, "P_INSUFFICIENT_DATA_HOLD");
        }

        // ── the guard ────────────────────────────────────────────────────

        [Test]
        public void HighPerformanceWithNoHeartRateAndNoTlx_HoldsPressure()
        {
            var input = HighPerformanceSession();
            input.sessionAvgBpmDelta = -999f;
            input.elevatedOrHighSeconds = 0;
            input.recoveryMeasured = false;
            input.recoveryReachedZone = false;
            input.recoverySeconds = -1f;
            input.tlxTotal = -1f;
            input.tlxFrustration = -1f;
            foreach (var t in new[] { TaskType.GoNoGo, TaskType.Flanker, TaskType.CorsiSequence })
                input.Metrics(t).avgBpmDelta = -999f;

            AssertPressureHeld(Decide(input),
                "bez HR-a i bez NASA-TLX cijena sesije se ne može procijeniti");
        }

        [Test]
        public void HighPerformanceWithMissingHeartRate_HoldsPressure()
        {
            var input = HighPerformanceSession();
            input.sessionAvgBpmDelta = -999f;
            input.elevatedOrHighSeconds = 0;
            foreach (var t in new[] { TaskType.GoNoGo, TaskType.Flanker, TaskType.CorsiSequence })
                input.Metrics(t).avgBpmDelta = -999f;

            AssertPressureHeld(Decide(input), "fiziološka cijena nije poznata");
        }

        [Test]
        public void HighPerformanceWithMissingNasaTlx_HoldsPressure()
        {
            var input = HighPerformanceSession();
            input.tlxTotal = -1f;
            input.tlxMental = -1f;
            input.tlxPhysical = -1f;
            input.tlxTemporal = -1f;
            input.tlxPerformance = -1f;
            input.tlxEffort = -1f;
            input.tlxFrustration = -1f;

            AssertPressureHeld(Decide(input), "subjektivna cijena nije poznata");
        }

        // ── the guard must not close the legitimate path ─────────────────

        [Test]
        public void HighPerformanceWithCompleteData_StillIncreasesPressure()
        {
            // P_ALL_STABLE_INCREASE is only reachable when the tasks themselves do not
            // go up, otherwise P_TASK_INCREASED_HOLD stops it first. A dominant
            // Temporal Demand produces exactly that: accuracy is high, every task
            // holds because levelling up would shorten the windows further, and the
            // cost of the session is fully known.
            var input = HighPerformanceSession();
            input.tlxTemporal = 75f;
            input.tlxMental = 40f;
            input.tlxPhysical = 40f;
            input.tlxPerformance = 40f;
            input.tlxEffort = 40f;
            input.tlxFrustration = 40f;
            input.tlxTotal = 45f;

            var d = Decide(input);

            Assert.AreEqual(AdaptationDirective.Increase, d.pressure.directive,
                "sa potpunim podacima P_ALL_STABLE_INCREASE mora i dalje raditi");
            Assert.AreEqual(d.pressure.previousLevel + 1, d.pressure.newLevel);
            CollectionAssert.Contains(d.firedRules, "P_ALL_STABLE_INCREASE");
            CollectionAssert.DoesNotContain(d.firedRules, "P_INSUFFICIENT_DATA_HOLD");
        }

        /// <summary>
        /// The original defect, stated as a test: the task rules correctly refused to
        /// raise difficulty, and the pressure level rose anyway in the same decision.
        /// </summary>
        [Test]
        public void TasksHeldForMissingData_DoNotLetPressureRise()
        {
            var input = HighPerformanceSession();
            input.sessionAvgBpmDelta = -999f;
            input.elevatedOrHighSeconds = 0;
            input.recoveryMeasured = false;
            input.tlxTotal = -1f;
            input.tlxFrustration = -1f;
            foreach (var t in new[] { TaskType.GoNoGo, TaskType.Flanker, TaskType.CorsiSequence })
                input.Metrics(t).avgBpmDelta = -999f;

            var d = Decide(input);

            CollectionAssert.Contains(d.firedRules, "GoNoGo:T_INSUFFICIENT_DATA_HOLD");
            Assert.AreNotEqual(AdaptationDirective.Increase, d.pressure.directive,
                "zadaci zadržani zbog nedostajućih podataka ne smiju otvoriti put pritisku");
            Assert.AreEqual(d.pressure.previousLevel, d.pressure.newLevel);
        }
    }
}
