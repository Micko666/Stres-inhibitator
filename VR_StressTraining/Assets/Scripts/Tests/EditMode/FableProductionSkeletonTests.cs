using System;
using System.Collections.Generic;
using NUnit.Framework;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Persistence;
using StressTraining.Session;
using StressTraining.Tasks;
using UnityEngine;

namespace StressTraining.Tests.EditMode
{
    /// <summary>PHASE 8.5: schema migration, 3-of-4 selection, 9-block/3-round plan, global timer.</summary>
    public sealed class FableProductionSkeletonTests
    {
        // ── schema migration ─────────────────────────────────────────────

        [Test]
        public void ProfileMigration_V1_GetsCorsiLevelOneAndSchemaTwo()
        {
            // Minimal authentic v1 profile payload (no corsi/tutorial fields).
            const string v1Json = "{\"schemaVersion\":1,\"userId\":\"abc\",\"username\":\"Test\"," +
                "\"createdAtUtcIso\":\"2026-01-01T00:00:00Z\",\"currentNBackLevel\":2," +
                "\"currentGoNoGoLevel\":1,\"currentFlankerLevel\":3,\"currentPressureLevel\":2}";

            var migration = new SchemaMigrationService();
            string migrated = migration.MigrateProfileJson(v1Json);
            Assert.That(migrated, Is.Not.Null);

            var profile = JsonUtility.FromJson<UserProfileData>(migrated);
            Assert.That(profile.schemaVersion, Is.EqualTo(UserProfileData.CurrentSchemaVersion));
            Assert.That(profile.currentCorsiLevel, Is.EqualTo(1), "v1 profiles default to Corsi level 1");
            Assert.That(profile.currentNBackLevel, Is.EqualTo(2), "existing levels must survive");
            Assert.That(profile.currentFlankerLevel, Is.EqualTo(3));
            Assert.That(profile.tutorialStates, Is.Not.Null.And.Empty);
        }

        [Test]
        public void ProfileMigration_NewerSchema_IsNeverDowngraded()
        {
            string newer = "{\"schemaVersion\":" + (UserProfileData.CurrentSchemaVersion + 1) +
                           ",\"userId\":\"x\"}";
            Assert.That(new SchemaMigrationService().MigrateProfileJson(newer), Is.Null);
        }

        [Test]
        public void SessionMigration_V1_LoadsWithProductionDefaults()
        {
            const string v1 = "{\"schemaVersion\":1,\"sessionId\":\"s1\",\"userId\":\"u\"," +
                              "\"validityStatus\":8}";
            string migrated = new SchemaMigrationService().MigrateSessionJson(v1);
            var s = JsonUtility.FromJson<SessionSummaryData>(migrated);
            Assert.That(s.schemaVersion, Is.EqualTo(SessionSummaryData.CurrentSchemaVersion));
            Assert.That(s.isProductionSession, Is.False, "old records are demo/legacy, never production");
            Assert.That(s.validityStatus, Is.EqualTo(ValidityStatus.DemoOnly));
            Assert.That(s.omittedTaskType, Is.EqualTo(TaskType.None));
            Assert.That(s.plannedRoundCount, Is.Zero,
                "historical pre-Phase-8.5 records must not be mislabeled as three-round plans");
        }

        // ── 3-of-4 selection ─────────────────────────────────────────────

        [Test]
        public void Selection_IsDeterministic_ThreeDistinct_OmittedExcluded()
        {
            var a = SeededConstrainedTaskSelector.Select(4242, TaskType.None);
            var b = SeededConstrainedTaskSelector.Select(4242, TaskType.None);

            Assert.That(a.SelectedTasks, Is.EqualTo(b.SelectedTasks), "same seed → same selection");
            Assert.That(a.OmittedTask, Is.EqualTo(b.OmittedTask));
            Assert.That(a.SelectedTasks, Has.Count.EqualTo(3));
            Assert.That(new HashSet<TaskType>(a.SelectedTasks), Has.Count.EqualTo(3), "distinct");
            Assert.That(a.SelectedTasks, Has.No.Member(a.OmittedTask));
            Assert.That(a.OmittedTask, Is.Not.EqualTo(TaskType.None));
        }

        [Test]
        public void Selection_PreviouslyOmittedTask_IsAlwaysIncluded()
        {
            // No seed may omit the same task twice in a row (spec §7 rule 6).
            foreach (TaskType prev in SeededConstrainedTaskSelector.FullPool)
            {
                for (int seed = 1; seed <= 25; seed++)
                {
                    var result = SeededConstrainedTaskSelector.Select(seed, prev);
                    Assert.That(result.SelectedTasks, Does.Contain(prev),
                        $"seed {seed}: task omitted twice consecutively: {prev}");
                    Assert.That(result.ReasonCodes, Has.Some.Contains("IncludedBecausePreviouslyOmitted"));
                }
            }
        }

        [Test]
        public void Selection_DisabledTask_IsOmittedByConfig()
        {
            var pool = new List<TaskType> { TaskType.NBack, TaskType.GoNoGo, TaskType.Flanker };
            var result = SeededConstrainedTaskSelector.Select(7, TaskType.None, pool);
            Assert.That(result.OmittedTask, Is.EqualTo(TaskType.CorsiSequence));
            Assert.That(result.ReasonCodes, Has.Some.Contains("OmittedByConfigDisable"));
            Assert.That(result.SelectedTasks, Is.EquivalentTo(pool));
        }

        [Test]
        public void ResolvePreviousOmitted_IgnoresDemoAndInvalidSessions()
        {
            var history = new List<SessionSummaryData>
            {
                Production(TaskType.Flanker, ValidityStatus.Valid),
                Demo(TaskType.NBack),
                Production(TaskType.CorsiSequence, ValidityStatus.InvalidTechnicalFailure),
                Production(TaskType.GoNoGo, ValidityStatus.IncompleteUserTerminated)
            };
            Assert.That(SeededConstrainedTaskSelector.ResolvePreviousOmitted(history),
                Is.EqualTo(TaskType.Flanker),
                "only the last VALID production session counts");
        }

        private static SessionSummaryData Production(TaskType omitted, ValidityStatus v) =>
            new SessionSummaryData { isProductionSession = true, omittedTaskType = omitted, validityStatus = v };
        private static SessionSummaryData Demo(TaskType omitted) =>
            new SessionSummaryData { isProductionSession = false, omittedTaskType = omitted, validityStatus = ValidityStatus.DemoOnly };

        // ── 9-block / 3-round plan ────────────────────────────────────────────────

        private static SessionPlan MakePlan(int seed, SessionCondition condition)
        {
            var selection = SeededConstrainedTaskSelector.Select(
                TaskSeedService.DeriveNamedSeed(seed, TaskSeedService.SaltTaskSelection), TaskType.None);
            return ProductionSessionPlanGenerator.Generate(seed, condition, 1, selection,
                _ => 1, new SessionConfig(), 3);
        }

        [Test]
        public void ProductionPlan_Has9Blocks_3PerSelectedTask()
        {
            var plan = MakePlan(123, SessionCondition.Neutral);
            Assert.That(plan.blocks, Has.Count.EqualTo(9));
            foreach (var task in plan.selectedTaskTypes)
                Assert.That(plan.blocks.FindAll(b => b.taskType == task), Has.Count.EqualTo(3),
                    $"{task} must appear in exactly 3 blocks");
            Assert.That(plan.blocks.FindAll(b => b.taskType == plan.omittedTaskType), Is.Empty);
            Assert.That(plan.nominalPlanSeconds, Is.GreaterThan(0f));
            Assert.That(plan.globalDifficultyTimeMultiplier, Is.EqualTo(1.20f).Within(1e-6));
            Assert.That(plan.globalDurationSeconds,
                Is.EqualTo(plan.nominalPlanSeconds * plan.globalDifficultyTimeMultiplier).Within(1e-4));
            Assert.That(plan.blocks.ConvertAll(b => b.roundIndex),
                Is.EqualTo(new List<int> { 0, 0, 0, 1, 1, 1, 2, 2, 2 }));
        }

        [Test]
        public void ProductionPlan_IsDeterministic_AndBalanced()
        {
            var a = MakePlan(555, SessionCondition.Neutral);
            var b = MakePlan(555, SessionCondition.Neutral);
            for (int i = 0; i < a.blocks.Count; i++)
            {
                Assert.That(a.blocks[i].taskType, Is.EqualTo(b.blocks[i].taskType));
                Assert.That(a.blocks[i].blockSeed, Is.EqualTo(b.blocks[i].blockSeed));
            }

            // No immediate repetition (stronger than the ≤2-in-a-row rule) and
            // every round of three contains all three tasks (early/mid/late).
            for (int i = 1; i < a.blocks.Count; i++)
                Assert.That(a.blocks[i].taskType, Is.Not.EqualTo(a.blocks[i - 1].taskType),
                    $"immediate repeat at block {i}");
            for (int r = 0; r < 3; r++)
            {
                var round = new HashSet<TaskType>
                {
                    a.blocks[r * 3].taskType, a.blocks[r * 3 + 1].taskType, a.blocks[r * 3 + 2].taskType
                };
                Assert.That(round, Has.Count.EqualTo(3), $"round {r} must contain all three tasks");
            }
        }

        [Test]
        public void ProductionPlan_SnapshotsOneStableLevelPerTaskForWholeSession()
        {
            const int seed = 909;
            var selection = SeededConstrainedTaskSelector.Select(
                TaskSeedService.DeriveNamedSeed(seed, TaskSeedService.SaltTaskSelection), TaskType.None);
            int levelLookups = 0;
            SessionPlan plan = ProductionSessionPlanGenerator.Generate(
                seed, SessionCondition.Neutral, 1, selection,
                _ => ++levelLookups, new SessionConfig(), 3);

            Assert.That(levelLookups, Is.EqualTo(3),
                "each selected task level must be captured exactly once");
            foreach (TaskType task in plan.selectedTaskTypes)
            {
                List<SessionBlockPlan> blocks = plan.blocks.FindAll(b => b.taskType == task);
                Assert.That(blocks, Has.Count.EqualTo(3));
                Assert.That(blocks.TrueForAll(b =>
                    b.difficultyLevel == blocks[0].difficultyLevel), Is.True,
                    $"{task} must keep one level across all three rounds");
            }
        }

        [Test]
        public void ProductionPlan_NeutralAndPressure_ShareIdenticalTaskContent()
        {
            var neutral = MakePlan(777, SessionCondition.Neutral);
            var pressure = MakePlan(777, SessionCondition.Pressure);
            Assert.That(neutral.selectedTaskTypes, Is.EqualTo(pressure.selectedTaskTypes));
            for (int i = 0; i < neutral.blocks.Count; i++)
            {
                Assert.That(neutral.blocks[i].taskType, Is.EqualTo(pressure.blocks[i].taskType));
                Assert.That(neutral.blocks[i].blockSeed, Is.EqualTo(pressure.blocks[i].blockSeed),
                    "identical trial seeds are required for the neutral/pressure comparison");
            }
            Assert.That(neutral.condition, Is.Not.EqualTo(pressure.condition));
        }

        [Test]
        public void PlanBudgetMultipliers_AreCentralizedProjectHeuristics()
        {
            var config = new StressTrainingConfig();
            Assert.That(config.session.globalDifficultyTimeMultiplierLevel1, Is.EqualTo(1.20f));
            Assert.That(config.session.globalDifficultyTimeMultiplierLevel2, Is.EqualTo(1.00f));
            Assert.That(config.session.globalDifficultyTimeMultiplierLevel3, Is.EqualTo(0.85f));
            Assert.That(config.session.productionBlocksPerSelectedTask, Is.EqualTo(3));
            Assert.That(config.baseline.durationSeconds, Is.EqualTo(300f));
            Assert.That(SessionPlan.ProductionBlockCount, Is.EqualTo(9));
            Assert.That(SessionPlan.RoundCount, Is.EqualTo(3));

            // Pressure visual intensity does not multiply the budget. The only
            // budget multiplier is the centralized global difficulty value above.
            for (int level = 1; level <= 3; level++)
                Assert.That(StressTraining.Pressure.PressureLevelConfig.Get(level).globalDurationMultiplier,
                    Is.EqualTo(1f));
        }

        // ── global timer ─────────────────────────────────────────────────

        [Test]
        public void GlobalTimer_CountsActiveTimeOnly_AndFreezesOnPause()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.SetGlobalDuration(100);
            clock.SetGlobalChallengeRunning(true);

            clock.Tick(10);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(90).Within(1e-6));

            clock.SetPaused(true);
            clock.Tick(50);   // paused time must not consume the global timer
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(90).Within(1e-6));
            Assert.That(clock.PausedElapsedSeconds, Is.EqualTo(50).Within(1e-6));

            clock.SetPaused(false);
            clock.Tick(90);
            Assert.That(clock.GlobalTimeExpired, Is.True);
            Assert.That(clock.RemainingGlobalSeconds, Is.Zero);
        }

        [Test]
        public void GlobalTimer_PenaltySeconds_ReduceRemaining()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.SetGlobalDuration(100);
            clock.SetGlobalChallengeRunning(true);
            clock.Tick(10);
            clock.AddPenaltySeconds(15);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(75).Within(1e-6));
            Assert.That(clock.PenaltySeconds, Is.EqualTo(15).Within(1e-6));
        }

        [Test]
        public void GlobalTimer_WithoutDuration_ReportsNoTimer()
        {
            var clock = new SessionClock();
            clock.StartSession();
            Assert.That(clock.HasGlobalTimer, Is.False);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(-1));
            Assert.That(clock.GlobalTimeExpired, Is.False);
        }

        // ── validity: time expired + schedule override ───────────────────

        [Test]
        public void Validity_TimeExpired_YieldsIncompleteTimeExpired()
        {
            var evaluator = new SessionValidityEvaluator();
            var status = evaluator.Evaluate(new SessionValidityEvaluator.Input
            {
                CompletionStatus = CompletionStatus.SystemTerminated,
                TimeExpired = true
            });
            Assert.That(status, Is.EqualTo(ValidityStatus.IncompleteTimeExpired));
        }

        [Test]
        public void Validity_ScheduleOverride_CompletedSession_IsValidWithWarnings()
        {
            var evaluator = new SessionValidityEvaluator();
            var status = evaluator.Evaluate(new SessionValidityEvaluator.Input
            {
                CompletionStatus = CompletionStatus.Completed,
                ScheduleOverride = true
            });
            Assert.That(status, Is.EqualTo(ValidityStatus.ValidWithWarnings));
        }
    }
}
