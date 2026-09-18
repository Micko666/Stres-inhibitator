using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Tasks;

namespace StressTraining.Session
{
    /// <summary>
    /// Builds the deterministic block plan for one session (spec §15).
    ///
    /// Compatibility three-task plan: 9 mini-blocks — three rounds of
    /// NBack, GoNoGo and Flanker. Production uses ProductionSessionPlanGenerator
    /// for the locked seeded 3-of-4 plan; demo plan remains one block per task.
    /// Block order is either the fixed interleaved default or a seeded shuffle;
    /// randomization without a recorded seed is impossible by construction
    /// because the seed lives inside the returned plan.
    /// </summary>
    public static class SessionPlanGenerator
    {
        public static readonly TaskType[] InterleavedOrder =
            { TaskType.NBack, TaskType.GoNoGo, TaskType.Flanker };

        public static SessionPlan Generate(
            int masterSeed,
            SessionCondition condition,
            int pressureLevel,
            int nBackLevel, int goNoGoLevel, int flankerLevel,
            bool demoPlan,
            SessionConfig config,
            bool shuffleBlockOrder = false)
        {
            var plan = new SessionPlan
            {
                masterSeed = masterSeed,
                condition = condition,
                pressureLevel = pressureLevel,
                isDemoPlan = demoPlan
            };

            int rounds = demoPlan ? config.blocksPerTaskDemo : config.blocksPerTaskFull;
            var order = new List<TaskType>(rounds * InterleavedOrder.Length);
            for (int r = 0; r < rounds; r++)
                order.AddRange(InterleavedOrder);

            if (shuffleBlockOrder)
            {
                var rng = new Random(masterSeed);
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }
            }

            for (int i = 0; i < order.Count; i++)
            {
                TaskType type = order[i];
                int level = type == TaskType.NBack ? nBackLevel
                          : type == TaskType.GoNoGo ? goNoGoLevel
                          : flankerLevel;
                level = TaskDifficultyConfig.Clamp(level);
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
            if (!demoPlan)
            {
                plan.nominalPlanSeconds = ProductionTimeBudgetEstimator.EstimateNominalPlanSeconds(plan.blocks);
                plan.globalDifficultyTimeMultiplier =
                    ProductionTimeBudgetEstimator.MultiplierForLevel(config, pressureLevel);
                plan.globalDurationSeconds = plan.nominalPlanSeconds * plan.globalDifficultyTimeMultiplier;
            }
            return plan;
        }

        /// <summary>Creates the matching runtime for a planned block (trials generated from the block seed).</summary>
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
                default:
                    throw new ArgumentOutOfRangeException(nameof(block.taskType));
            }
        }
    }
}
