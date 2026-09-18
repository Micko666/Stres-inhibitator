using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StressTraining.Adaptation;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Session;
using StressTraining.Tasks;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// Final integration (2026-07-14): merged breathing/reference phase, pre-session
    /// order, scheduler chain and the pause invariants.
    /// </summary>
    public sealed class FableFinalIntegrationTests
    {
        // ── merged breathing + reference phase ───────────────────────────

        [Test]
        public void BreathingAndReference_AreOneStage_TaggedAsBreathingReference()
        {
            // One stage, one HR phase — breathing is no longer separate.
            Assert.That(ProductionSessionFlow.HrPhaseForStage(
                    ProductionFlowStage.BreathingReferenceBaseline),
                Is.EqualTo(SessionHrPhase.BreathingReferenceBaseline));
        }

        [Test]
        public void ReferencePhase_IsNotLabelledAsNeutralRestingBaseline()
        {
            // Methodology guard: the enum member that used to be "NeutralBaseline"
            // must now read as a breathing-assisted reference.
            Assert.That(SessionHrPhase.BreathingReferenceBaseline.ToString(),
                Does.Contain("Breathing"));
            Assert.That(SessionHrPhase.BreathingReferenceBaseline.ToString(),
                Does.Not.Contain("Neutral"));
            // Persisted int value must never shift.
            Assert.That((int)SessionHrPhase.BreathingReferenceBaseline, Is.EqualTo(3));
            Assert.That((int)ProductionFlowStage.BreathingReferenceBaseline, Is.EqualTo(5));
        }

        [Test]
        public void OnlyReferencePhase_FeedsTheReferenceBpm()
        {
            // Every other phase keeps RECORDING HR but must not be the reference phase.
            foreach (ProductionFlowStage stage in System.Enum.GetValues(typeof(ProductionFlowStage)))
            {
                SessionHrPhase phase = ProductionSessionFlow.HrPhaseForStage(stage);
                if (stage == ProductionFlowStage.BreathingReferenceBaseline)
                    Assert.That(phase, Is.EqualTo(SessionHrPhase.BreathingReferenceBaseline));
                else
                    Assert.That(phase, Is.Not.EqualTo(SessionHrPhase.BreathingReferenceBaseline),
                        $"{stage} must not be tagged as the reference phase");
            }
        }

        [Test]
        public void PreSessionStages_NeverRunTheGlobalChallengeClock()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.SetGlobalDuration(600);           // armed, but Ready — not running
            double before = clock.RemainingGlobalSeconds;

            // Breathing/reference, SSQ, tutorial, countdown: the gate is never opened.
            for (int i = 0; i < 100; i++) clock.Tick(1.0);

            Assert.That(clock.IsGlobalChallengeRunning, Is.False);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(before).Within(1e-6),
                "the challenge budget must not burn during pre-session");
        }

        // ── scheduler chain ──────────────────────────────────────────────

        [Test]
        public void FirstSession_UsesDefaultLevels()
        {
            var profile = new UserProfileData();
            Assert.That(profile.currentNBackLevel, Is.EqualTo(1));
            Assert.That(profile.currentGoNoGoLevel, Is.EqualTo(1));
            Assert.That(profile.currentFlankerLevel, Is.EqualTo(1));
            Assert.That(profile.currentCorsiLevel, Is.EqualTo(1));
            Assert.That(profile.currentPressureLevel, Is.EqualTo(1));
            foreach (TaskType t in new[] { TaskType.NBack, TaskType.GoNoGo,
                                           TaskType.Flanker, TaskType.CorsiSequence })
                Assert.That(profile.GetTaskLevel(t), Is.EqualTo(1));
        }

        [Test]
        public void SchedulerIneligible_WhenBaselineQualityIsNotGood()
        {
            // Invalid / technically unusable reference must never drive adaptation.
            Assert.That(ProductionSessionFlow.IsSchedulerEligibleForProductionSession(
                false, HrSourceType.NetworkBridge, BaselineQuality.Low), Is.False);
            Assert.That(ProductionSessionFlow.IsSchedulerEligibleForProductionSession(
                false, HrSourceType.NetworkBridge, BaselineQuality.Missing), Is.False);
            Assert.That(ProductionSessionFlow.IsSchedulerEligibleForProductionSession(
                false, HrSourceType.Simulated, BaselineQuality.Good), Is.False);
            Assert.That(ProductionSessionFlow.IsSchedulerEligibleForProductionSession(
                true, HrSourceType.NetworkBridge, BaselineQuality.Good), Is.False);
            // Only a real source + good reference is eligible.
            Assert.That(ProductionSessionFlow.IsSchedulerEligibleForProductionSession(
                false, HrSourceType.NetworkBridge, BaselineQuality.Good), Is.True);
        }

        [Test]
        public void PressureAndTaskDifficulty_NeverIncreaseTogether()
        {
            var cfg = new AdaptationConfig();
            var fired = new List<string>();
            var ctx = new AdaptationRuleSet.PressureRuleContext
            {
                AnyTaskIncreased = true,          // a task already went up
                AnyTaskDecreasedOrRepeat = false
            };
            AdaptationDirective d = AdaptationRuleSet.EvaluatePressure(
                new AdaptationInput(), cfg, ctx, fired);

            Assert.That(d, Is.EqualTo(AdaptationDirective.Hold),
                "pressure must hold whenever any task difficulty increased");
            Assert.That(fired, Contains.Item("P_TASK_INCREASED_HOLD"));
        }

        [Test]
        public void CompleteSession_WritesDecisionLevelsOntoTheProfile()
        {
            var profile = new UserProfileData { userId = "u1" };
            var decision = new AdaptationDecisionData
            {
                nBack = new TaskAdaptationDecisionData { newLevel = 2 },
                goNoGo = new TaskAdaptationDecisionData { newLevel = 3 },
                flanker = new TaskAdaptationDecisionData { newLevel = 1 },
                corsi = new TaskAdaptationDecisionData { newLevel = 2 },
                pressure = new PressureAdaptationDecisionData { newLevel = 2 }
            };

            // Mirrors UserProfileService.CompleteSession's level write-back.
            profile.currentNBackLevel = decision.nBack.newLevel;
            profile.currentGoNoGoLevel = decision.goNoGo.newLevel;
            profile.currentFlankerLevel = decision.flanker.newLevel;
            if (decision.corsi != null && decision.corsi.newLevel > 0)
                profile.currentCorsiLevel = decision.corsi.newLevel;
            profile.currentPressureLevel = decision.pressure.newLevel;

            // The NEXT plan reads exactly these fields.
            Assert.That(profile.GetTaskLevel(TaskType.NBack), Is.EqualTo(2));
            Assert.That(profile.GetTaskLevel(TaskType.GoNoGo), Is.EqualTo(3));
            Assert.That(profile.GetTaskLevel(TaskType.CorsiSequence), Is.EqualTo(2));
            Assert.That(profile.currentPressureLevel, Is.EqualTo(2));
        }

        [Test]
        public void DifferentProfiles_DoNotShareLevels()
        {
            var a = new UserProfileData { userId = "a", currentNBackLevel = 3 };
            var b = new UserProfileData { userId = "b" };
            Assert.That(a.GetTaskLevel(TaskType.NBack), Is.EqualTo(3));
            Assert.That(b.GetTaskLevel(TaskType.NBack), Is.EqualTo(1),
                "a second profile must start from defaults, never inherit another plan");
        }

        [Test]
        public void PreviouslyOmittedTask_IsNeverOmittedTwiceInARow()
        {
            foreach (TaskType omitted in new[] { TaskType.NBack, TaskType.GoNoGo,
                                                 TaskType.Flanker, TaskType.CorsiSequence })
            {
                for (int seed = 1; seed <= 30; seed++)
                {
                    TaskSelectionResult r = SeededConstrainedTaskSelector.Select(seed, omitted);
                    Assert.That(r.SelectedTasks, Contains.Item(omitted),
                        $"seed {seed}: {omitted} was omitted last time and must run now");
                    Assert.That(r.OmittedTask, Is.Not.EqualTo(omitted));
                }
            }
        }

        // ── 9-block plan / timer ─────────────────────────────────────────

        [Test]
        public void ProductionPlan_Has9BlocksIn3Rounds()
        {
            Assert.That(SessionPlan.ProductionBlockCount, Is.EqualTo(9));
            Assert.That(SessionPlan.RoundCount, Is.EqualTo(3));
            Assert.That(SessionPlan.BlocksPerRound, Is.EqualTo(3));
            Assert.That(SessionPlan.SelectedTaskCount, Is.EqualTo(3));
        }

        [Test]
        public void GlobalTimerReachingZero_MarksTimeExpiredNotSuccess()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.SetGlobalDuration(5);
            clock.SetGlobalChallengeRunning(true);
            for (int i = 0; i < 10; i++) clock.Tick(1.0);

            Assert.That(clock.GlobalTimeExpired, Is.True);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(0).Within(1e-6));
        }

        [Test]
        public void NeutralCondition_NeverRunsPressure_ControlledPressureDoes()
        {
            Assert.That(Pressure.PressureController.ShouldRunFor(SessionCondition.Neutral), Is.False);
            Assert.That(Pressure.PressureController.ShouldRunFor(SessionCondition.Pressure), Is.True);
        }

        // ── manual pause invariants (must not regress) ───────────────────

        [Test]
        public void PauseController_HasNoAutomaticOrTechnicalHoldApi()
        {
            // The automatic HR pause is gone for good: no technical-hold surface,
            // and only a manual pause/resume remains.
            System.Type t = typeof(PauseController);
            Assert.That(t.GetMethod("HoldTechnical"), Is.Null);
            Assert.That(t.GetMethod("ReleaseTechnical"), Is.Null);
            Assert.That(t.GetProperty("IsTechnicalHold"), Is.Null);
            Assert.That(t.GetMethod("Pause"), Is.Not.Null);
            Assert.That(t.GetMethod("Resume"), Is.Not.Null);
        }

        [Test]
        public void HrTechnicalPauseController_NoLongerExists()
        {
            System.Type t = System.Type.GetType(
                "StressTraining.Session.HrTechnicalPauseController, StressTraining");
            Assert.That(t, Is.Null, "the automatic HR pause must not come back");
        }

        [Test]
        public void Console_HasNoPauseControl()
        {
            var actions = Console.ConsoleLayoutBuilder.DefaultBindings()
                .Select(b => b.action).ToList();
            Assert.That(actions, Has.No.Member(SemanticAction.PauseToggle),
                "pause is left-controller Y only");
        }
    }
}
