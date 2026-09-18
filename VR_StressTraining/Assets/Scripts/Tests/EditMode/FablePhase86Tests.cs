using System.Collections.Generic;
using NUnit.Framework;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Pressure;
using StressTraining.Session;
using StressTraining.Tasks;   // TaskSeedService lives in StressTraining.Tasks

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// PHASE 8.6 non-HR contract tests: plan-dependent challenge budget,
    /// condition wiring, questionnaire-cycle decisions and flow phase tags.
    /// Unity execution is still required before these tests can be marked run.
    /// </summary>
    public sealed class FablePhase86Tests
    {
        [Test]
        public void ConcretePlanBudget_IsDeterministicForSameSeedAndPlan()
        {
            SessionPlan a = MakePlan(8128, 2);
            SessionPlan b = MakePlan(8128, 2);

            Assert.That(a.nominalPlanSeconds, Is.GreaterThan(0f));
            Assert.That(a.nominalPlanSeconds, Is.EqualTo(b.nominalPlanSeconds).Within(0.0001));
            Assert.That(a.globalDurationSeconds, Is.EqualTo(b.globalDurationSeconds).Within(0.0001));
            Assert.That(a.globalDifficultyTimeMultiplier, Is.EqualTo(1.0f).Within(0.0001));
        }

        [Test]
        public void CorsiBlock_HasDifferentNominalBudgetFromFasterGoNoGoBlock()
        {
            var corsi = new SessionBlockPlan
            {
                blockId = "C",
                blockIndex = 0,
                roundIndex = 0,
                taskType = TaskType.CorsiSequence,
                difficultyLevel = 1,
                blockSeed = 77
            };
            var goNoGo = new SessionBlockPlan
            {
                blockId = "G",
                blockIndex = 0,
                roundIndex = 0,
                taskType = TaskType.GoNoGo,
                difficultyLevel = 1,
                blockSeed = 77
            };

            float corsiSeconds = ProductionTimeBudgetEstimator.EstimateBlockSeconds(corsi);
            float goNoGoSeconds = ProductionTimeBudgetEstimator.EstimateBlockSeconds(goNoGo);
            Assert.That(System.Math.Abs(corsiSeconds - goNoGoSeconds), Is.GreaterThan(0.0001f));
            Assert.That(corsiSeconds, Is.GreaterThan(goNoGoSeconds));
        }

        [Test]
        public void GlobalDifficultyMultipliers_AreCentralizedAndApplied()
        {
            var config = new SessionConfig();
            Assert.That(ProductionTimeBudgetEstimator.MultiplierForLevel(config, 1),
                Is.EqualTo(1.20f).Within(0.0001));
            Assert.That(ProductionTimeBudgetEstimator.MultiplierForLevel(config, 2),
                Is.EqualTo(1.00f).Within(0.0001));
            Assert.That(ProductionTimeBudgetEstimator.MultiplierForLevel(config, 3),
                Is.EqualTo(0.85f).Within(0.0001));
        }

        [Test]
        public void NeutralAndPressureConditions_ControlExistingPressureSystem()
        {
            Assert.That(PressureController.ShouldRunFor(SessionCondition.Neutral), Is.False);
            Assert.That(PressureController.ShouldRunFor(SessionCondition.Pressure), Is.True);
        }

        [Test]
        public void FlowStages_MapToContinuousHrRecordingPhases()
        {
            Assert.That(ProductionSessionFlow.HrPhaseForStage(ProductionFlowStage.PreCycleStai),
                Is.EqualTo(SessionHrPhase.PreSessionQuestionnaire));
            Assert.That(ProductionSessionFlow.HrPhaseForStage(ProductionFlowStage.PreSessionSSQ),
                Is.EqualTo(SessionHrPhase.PreSessionQuestionnaire));
            // Breathing + reference measurement are ONE phase (2026-07-14).
            Assert.That(ProductionSessionFlow.HrPhaseForStage(
                    ProductionFlowStage.BreathingReferenceBaseline),
                Is.EqualTo(SessionHrPhase.BreathingReferenceBaseline));
            Assert.That(ProductionSessionFlow.HrPhaseForStage(ProductionFlowStage.TaskTutorialDecision),
                Is.EqualTo(SessionHrPhase.Tutorial));
            Assert.That(ProductionSessionFlow.HrPhaseForStage(ProductionFlowStage.ActiveBlock),
                Is.EqualTo(SessionHrPhase.ActiveTask));
            Assert.That(ProductionSessionFlow.HrPhaseForStage(ProductionFlowStage.Recovery),
                Is.EqualTo(SessionHrPhase.Recovery));
        }

        [Test]
        public void QuestionnaireAndTutorialPhases_AreNeverTaggedAsBreathingReference()
        {
            Assert.That(ProductionSessionFlow.HrPhaseForStage(ProductionFlowStage.PreSessionSSQ),
                Is.Not.EqualTo(SessionHrPhase.BreathingReferenceBaseline));
            Assert.That(ProductionSessionFlow.HrPhaseForStage(ProductionFlowStage.PreCycleStai),
                Is.Not.EqualTo(SessionHrPhase.BreathingReferenceBaseline));
            Assert.That(ProductionSessionFlow.HrPhaseForStage(ProductionFlowStage.CopingPreparation),
                Is.Not.EqualTo(SessionHrPhase.BreathingReferenceBaseline));
            Assert.That(ProductionSessionFlow.HrPhaseForStage(ProductionFlowStage.TaskTutorialDecision),
                Is.Not.EqualTo(SessionHrPhase.BreathingReferenceBaseline));
        }

        [Test]
        public void StaiCycleRules_ShowOnlyAtCycleStartAndCycleEnd()
        {
            var profile = new UserProfileData
            {
                activeCycleId = "cycle",
                currentCycleSessionIndex = 0,
                plannedCycleSessionCount = 5
            };
            var cycle = new TrainingCycleSummaryData
            {
                cycleId = "cycle",
                plannedSessionCount = 5,
                staiScoreCycleStart = -1f,
                staiScoreCycleEnd = -1f
            };
            profile.cycleSummaries.Add(cycle);

            Assert.That(UserProfileService.ShouldAdministerCycleStartStai(profile), Is.True);
            cycle.staiScoreCycleStart = 12f;
            Assert.That(UserProfileService.ShouldAdministerCycleStartStai(profile), Is.False,
                "re-entering the first cycle session must not duplicate completed STAI-6");

            profile.currentCycleSessionIndex = 1;
            Assert.That(UserProfileService.ShouldAdministerCycleStartStai(profile), Is.False);
            Assert.That(UserProfileService.ShouldAdministerCycleEndStai(profile), Is.False);

            profile.currentCycleSessionIndex = 4;
            Assert.That(UserProfileService.ShouldAdministerCycleEndStai(profile), Is.True);
            cycle.staiScoreCycleEnd = 10f;
            Assert.That(UserProfileService.ShouldAdministerCycleEndStai(profile), Is.False);
        }

        [Test]
        public void PlanStoresNominalMultiplierAndFinalBudget()
        {
            SessionPlan plan = MakePlan(404, 3);
            Assert.That(plan.blocks, Has.Count.EqualTo(9));
            Assert.That(plan.nominalPlanSeconds, Is.GreaterThan(0f));
            Assert.That(plan.globalDifficultyTimeMultiplier, Is.EqualTo(0.85f).Within(0.0001));
            Assert.That(plan.globalDurationSeconds,
                Is.EqualTo(plan.nominalPlanSeconds * 0.85f).Within(0.001));
        }

        private static SessionPlan MakePlan(int masterSeed, int globalLevel)
        {
            TaskSelectionResult selection = SeededConstrainedTaskSelector.Select(
                TaskSeedService.DeriveNamedSeed(masterSeed, TaskSeedService.SaltTaskSelection),
                TaskType.None);
            return ProductionSessionPlanGenerator.Generate(
                masterSeed,
                SessionCondition.Pressure,
                globalLevel,
                selection,
                _ => globalLevel,
                new SessionConfig(),
                3);
        }
    }
}
