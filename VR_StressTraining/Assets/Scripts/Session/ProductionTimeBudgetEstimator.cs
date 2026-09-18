using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Tasks;

namespace StressTraining.Session
{
    /// <summary>
    /// Pure deterministic estimator for active challenge time. It uses the
    /// concrete generated trials of every block and excludes every UI/setup
    /// phase outside active blocks. All timing constants are PROJECT_HEURISTIC.
    /// </summary>
    public static class ProductionTimeBudgetEstimator
    {
        public static float MultiplierForLevel(SessionConfig config, int globalDifficultyLevel)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            switch (TaskDifficultyConfig.Clamp(globalDifficultyLevel))
            {
                case 1: return Math.Max(0.01f, config.globalDifficultyTimeMultiplierLevel1);
                case 2: return Math.Max(0.01f, config.globalDifficultyTimeMultiplierLevel2);
                default: return Math.Max(0.01f, config.globalDifficultyTimeMultiplierLevel3);
            }
        }

        public static float EstimateNominalPlanSeconds(IReadOnlyList<SessionBlockPlan> blocks)
        {
            if (blocks == null) throw new ArgumentNullException(nameof(blocks));
            double total = 0;
            for (int i = 0; i < blocks.Count; i++)
                total += EstimateBlockSeconds(blocks[i]);
            return (float)total;
        }

        public static float EstimateBlockSeconds(SessionBlockPlan block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            TaskLevelParameters p = TaskDifficultyConfig.Get(block.taskType, block.difficultyLevel);
            List<TaskTrialDefinition> trials = GenerateTrials(block);
            double total = 0;

            foreach (TaskTrialDefinition trial in trials)
            {
                if (block.taskType == TaskType.CorsiSequence)
                {
                    int length = CorsiTaskDefinition.DecodeSequence(trial.stimulus).Count;
                    double expectedResponse = TaskDifficultyConfig.CorsiExpectedResponseBaseSeconds +
                                              length * TaskDifficultyConfig.CorsiExpectedResponsePerItemSeconds;
                    total += trial.interTrialIntervalSeconds +
                             trial.stimulusDurationSeconds +
                             p.corsiRetentionSeconds +
                             expectedResponse +
                             TaskDifficultyConfig.CorsiFeedbackSeconds;
                }
                else
                {
                    // The stimulus is displayed inside the response window, so it
                    // is not added twice. Max makes that relationship explicit.
                    total += trial.interTrialIntervalSeconds +
                             Math.Max(trial.stimulusDurationSeconds, trial.responseWindowSeconds) +
                             TaskDifficultyConfig.StandardFeedbackSeconds;
                }
            }
            return (float)total;
        }

        private static List<TaskTrialDefinition> GenerateTrials(SessionBlockPlan block)
        {
            switch (block.taskType)
            {
                case TaskType.NBack:
                    return new NBackTaskDefinition().GenerateTrials(block.difficultyLevel, block.blockSeed);
                case TaskType.GoNoGo:
                    return new GoNoGoTaskDefinition().GenerateTrials(block.difficultyLevel, block.blockSeed);
                case TaskType.Flanker:
                    return new FlankerTaskDefinition().GenerateTrials(block.difficultyLevel, block.blockSeed);
                case TaskType.CorsiSequence:
                    return new CorsiTaskDefinition().GenerateTrials(block.difficultyLevel, block.blockSeed);
                default:
                    throw new ArgumentOutOfRangeException(nameof(block.taskType));
            }
        }
    }
}
