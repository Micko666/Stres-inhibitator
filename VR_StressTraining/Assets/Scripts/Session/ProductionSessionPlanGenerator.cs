using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Tasks;

namespace StressTraining.Session
{
    /// <summary>
    /// Deterministic 9-block production plan (PHASE 8.5):
    /// - exactly 3 selected tasks × 3 scored blocks;
    /// - seeded, balanced interleaving: every round contains all three tasks, so
    ///   each task appears early, mid and late in the session;
    /// - no immediate repetition across block boundaries (round permutations of
    ///   three DISTINCT tasks + deterministic boundary rotation);
    /// - the same master seed reproduces selection, order, per-block trial
    ///   seeds, pressure timeline seed and console layout seed;
    /// - task difficulty is fixed for the whole session (one level per task).
    /// </summary>
    public static class ProductionSessionPlanGenerator
    {
        public static SessionPlan Generate(
            int masterSeed,
            SessionCondition condition,
            int pressureLevel,
            TaskSelectionResult selection,
            Func<TaskType, int> levelForTask,
            SessionConfig sessionConfig,
            int blocksPerTask = SessionPlan.RoundCount)
        {
            if (selection == null || selection.SelectedTasks.Count != SessionPlan.SelectedTaskCount ||
                new HashSet<TaskType>(selection.SelectedTasks).Count != SessionPlan.SelectedTaskCount)
                throw new ArgumentException("Production plan requires exactly 3 distinct selected tasks.");
            if (blocksPerTask != SessionPlan.RoundCount)
                throw new ArgumentOutOfRangeException(nameof(blocksPerTask),
                    "Production plan is locked to exactly 3 rounds / 3 blocks per selected task.");

            var plan = new SessionPlan
            {
                masterSeed = masterSeed,
                condition = condition,
                pressureLevel = pressureLevel,
                isDemoPlan = false,
                taskSelectionSeed = selection.SelectionSeed,
                blockOrderSeed = TaskSeedService.DeriveNamedSeed(masterSeed, TaskSeedService.SaltBlockOrder),
                pressureSeed = TaskSeedService.DeriveNamedSeed(masterSeed, TaskSeedService.SaltPressure),
                consoleLayoutSeed = TaskSeedService.DeriveNamedSeed(masterSeed, TaskSeedService.SaltConsoleLayout),
                omittedTaskType = selection.OmittedTask,
                selectionAlgorithmVersion = TaskSelectionResult.AlgorithmVersion
            };
            plan.selectedTaskTypes.AddRange(selection.SelectedTasks);
            plan.selectionReasonCodes.AddRange(selection.ReasonCodes);

            List<TaskType> order = BuildBalancedOrder(
                selection.SelectedTasks, plan.blockOrderSeed, blocksPerTask);

            // Snapshot each selected task level exactly once. This guarantees one
            // stable level per task for the whole active session even if a caller's
            // profile/delegate changes after plan generation begins.
            var sessionLevels = new Dictionary<TaskType, int>(SessionPlan.SelectedTaskCount);
            foreach (TaskType task in selection.SelectedTasks)
                sessionLevels[task] = TaskDifficultyConfig.Clamp(levelForTask?.Invoke(task) ?? 1);

            for (int i = 0; i < order.Count; i++)
            {
                TaskType type = order[i];
                int level = sessionLevels[type];
                plan.blocks.Add(new SessionBlockPlan
                {
                    blockId = $"B{i + 1:00}_{type}",
                    blockIndex = i,
                    roundIndex = i / SessionPlan.BlocksPerRound,
                    taskType = type,
                    difficultyLevel = level,
                    trialCount = TaskDifficultyConfig.Get(type, level).trialCount,
                    blockSeed = TaskSeedService.DeriveBlockSeed(masterSeed, i, type)
                });
            }

            sessionConfig = sessionConfig ?? new SessionConfig();
            plan.nominalPlanSeconds = ProductionTimeBudgetEstimator.EstimateNominalPlanSeconds(plan.blocks);
            plan.globalDifficultyTimeMultiplier =
                ProductionTimeBudgetEstimator.MultiplierForLevel(sessionConfig, pressureLevel);
            plan.globalDurationSeconds = plan.nominalPlanSeconds * plan.globalDifficultyTimeMultiplier;
            return plan;
        }

        /// <summary>
        /// Exactly three rounds; each round is a seeded permutation of the three
        /// tasks. If a round would start with the task that ended the previous
        /// round, the permutation is rotated left once — deterministic, and it
        /// guarantees no immediate repetition (max streak = 1 across boundaries,
        /// well under the "never more than two in a row" rule).
        /// </summary>
        public static List<TaskType> BuildBalancedOrder(
            IReadOnlyList<TaskType> selectedTasks, int blockOrderSeed, int rounds)
        {
            if (selectedTasks == null || selectedTasks.Count != SessionPlan.SelectedTaskCount ||
                new HashSet<TaskType>(selectedTasks).Count != SessionPlan.SelectedTaskCount)
                throw new ArgumentException("Balanced order requires exactly 3 distinct tasks.");
            if (rounds != SessionPlan.RoundCount)
                throw new ArgumentOutOfRangeException(nameof(rounds),
                    "Production order is locked to exactly 3 rounds.");

            var rng = new Random(blockOrderSeed);
            var order = new List<TaskType>(rounds * 3);
            TaskType previousLast = TaskType.None;

            for (int r = 0; r < rounds; r++)
            {
                var round = new List<TaskType>(selectedTasks);
                for (int i = round.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (round[i], round[j]) = (round[j], round[i]);
                }
                if (round[0] == previousLast)
                {
                    // rotate left by one — deterministic repeat-avoidance
                    TaskType first = round[0];
                    round.RemoveAt(0);
                    round.Add(first);
                }
                order.AddRange(round);
                previousLast = round[round.Count - 1];
            }
            return order;
        }

        /// <summary>Creates the runtime for one planned block (all four task types).</summary>
        public static ITaskRuntime CreateRuntime(SessionBlockPlan block)
        {
            switch (block.taskType)
            {
                case TaskType.NBack:
                    return new NBackTaskRuntime(
                        new NBackTaskDefinition().GenerateTrials(block.difficultyLevel, block.blockSeed));
                case TaskType.GoNoGo:
                    return new GoNoGoTaskRuntime(
                        new GoNoGoTaskDefinition().GenerateTrials(block.difficultyLevel, block.blockSeed));
                case TaskType.Flanker:
                    return new FlankerTaskRuntime(
                        new FlankerTaskDefinition().GenerateTrials(block.difficultyLevel, block.blockSeed));
                case TaskType.CorsiSequence:
                    return new CorsiTaskRuntime(
                        new CorsiTaskDefinition().GenerateTrials(block.difficultyLevel, block.blockSeed),
                        TaskDifficultyConfig.Get(TaskType.CorsiSequence, block.difficultyLevel));
                default:
                    throw new ArgumentOutOfRangeException(nameof(block.taskType));
            }
        }
    }
}
