using System;
using StressTraining.Data;

namespace StressTraining.Tasks
{
    /// <summary>
    /// Level parameters for one task at one difficulty level.
    /// ALL numeric values in this file are PROJECT_HEURISTIC — working values for
    /// this MVP prototype, centralized per spec §2.11/§12–14, tunable without
    /// touching task logic, and NOT claimed to be validated difficulty norms.
    /// </summary>
    [Serializable]
    public sealed class TaskLevelParameters
    {
        public TaskType taskType;
        public int level;

        public int trialCount;                      // scoreable trials
        public double stimulusDurationSeconds;
        public double responseWindowSeconds;        // from stimulus onset
        public double interTrialIntervalSeconds;

        // n-back
        public int nBackN;
        public int stimulusSetSize;
        public double targetMatchProportion;
        public StimulusMode stimulusMode = StimulusMode.Symbol;

        // go/no-go
        public double noGoProportion;
        public int stimulusSimilarityTier;          // 1 = clearly distinct … 3 = visually similar

        // flanker
        public double incongruentProportion;
        public double neutralProportion;

        // Corsi-inspired sequential spatial reproduction (spec §18.3).
        // All values PROJECT_HEURISTIC; scientific anchor: Brunetti, Del Gatto &
        // Delogu (2014) eCorsi — referenced as thesis reference 34.
        public int corsiMinSequenceLength;
        public int corsiMaxSequenceLength;
        public double corsiPresentationStepSeconds;   // one position lit
        public double corsiInterStepSeconds;          // gap between lit positions
        public double corsiRetentionSeconds;          // blank retention interval
        public double corsiResponseBaseSeconds;       // response window base…
        public double corsiResponsePerItemSeconds;    // …+ this per sequence item
        public bool corsiBackward;                    // level 3 rule change
        public double corsiInactivitySafetyTimeoutSeconds; // abandoned/inactivity, not a scoring timeout
    }

    /// <summary>
    /// Central difficulty table (PROJECT_HEURISTIC). Levels are 1..3.
    /// Difficulty changes ONLY between sessions (architecture rule 17/18).
    /// </summary>
    public static class TaskDifficultyConfig
    {
        public const int MinLevel = 1;
        public const int MaxLevel = 3;

        // PROJECT_HEURISTIC timing values shared by runtime and plan estimator.
        public const double StandardFeedbackSeconds = 0.35;
        public const double CorsiFeedbackSeconds = 0.45;
        public const double CorsiExpectedResponseBaseSeconds = 1.50;
        public const double CorsiExpectedResponsePerItemSeconds = 0.80;
        public const double CorsiInactivitySafetyTimeoutSeconds = 30.0;

        public static int Clamp(int level) => level < MinLevel ? MinLevel : (level > MaxLevel ? MaxLevel : level);

        public static TaskLevelParameters Get(TaskType type, int level)
        {
            level = Clamp(level);
            switch (type)
            {
                case TaskType.NBack: return NBack(level);
                case TaskType.GoNoGo: return GoNoGo(level);
                case TaskType.Flanker: return Flanker(level);
                case TaskType.CorsiSequence: return Corsi(level);
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "No difficulty table");
            }
        }

        private static TaskLevelParameters Corsi(int level)
        {
            // PROJECT_HEURISTIC values; forward L1/L2, backward L3 (spec §18.3).
            var p = new TaskLevelParameters
            {
                taskType = TaskType.CorsiSequence,
                level = level,
                corsiInactivitySafetyTimeoutSeconds = CorsiInactivitySafetyTimeoutSeconds
            };
            switch (level)
            {
                case 1: // forward, shorter sequences, slower presentation
                    p.trialCount = 6;
                    p.corsiMinSequenceLength = 3; p.corsiMaxSequenceLength = 5;
                    p.corsiPresentationStepSeconds = 0.9; p.corsiInterStepSeconds = 0.35;
                    p.corsiRetentionSeconds = 1.0;
                    p.corsiResponseBaseSeconds = 2.5; p.corsiResponsePerItemSeconds = 1.2;
                    p.corsiBackward = false;
                    p.interTrialIntervalSeconds = 1.2;
                    break;
                case 2: // forward, longer sequences, faster presentation
                    p.trialCount = 7;
                    p.corsiMinSequenceLength = 4; p.corsiMaxSequenceLength = 6;
                    p.corsiPresentationStepSeconds = 0.65; p.corsiInterStepSeconds = 0.25;
                    p.corsiRetentionSeconds = 1.0;
                    p.corsiResponseBaseSeconds = 2.0; p.corsiResponsePerItemSeconds = 1.0;
                    p.corsiBackward = false;
                    p.interTrialIntervalSeconds = 1.0;
                    break;
                default: // 3: BACKWARD reproduction (rule change → tutorial repeats)
                    p.trialCount = 6;
                    p.corsiMinSequenceLength = 3; p.corsiMaxSequenceLength = 5;
                    p.corsiPresentationStepSeconds = 0.8; p.corsiInterStepSeconds = 0.3;
                    p.corsiRetentionSeconds = 1.2;
                    p.corsiResponseBaseSeconds = 2.5; p.corsiResponsePerItemSeconds = 1.3;
                    p.corsiBackward = true;
                    p.interTrialIntervalSeconds = 1.2;
                    break;
            }
            return p;
        }

        private static TaskLevelParameters NBack(int level)
        {
            var p = new TaskLevelParameters { taskType = TaskType.NBack, level = level };
            switch (level)
            {
                case 1: // 1-back, small set, generous window
                    p.nBackN = 1; p.stimulusSetSize = 4; p.trialCount = 12;
                    p.stimulusDurationSeconds = 1.5; p.responseWindowSeconds = 2.6;
                    p.interTrialIntervalSeconds = 1.0; p.targetMatchProportion = 0.35;
                    break;
                case 2: // 2-back, moderate set/pace
                    p.nBackN = 2; p.stimulusSetSize = 6; p.trialCount = 14;
                    p.stimulusDurationSeconds = 1.2; p.responseWindowSeconds = 2.2;
                    p.interTrialIntervalSeconds = 0.9; p.targetMatchProportion = 0.30;
                    break;
                default: // 3: 2-back, larger set, faster rhythm, shorter window
                    p.nBackN = 2; p.stimulusSetSize = 8; p.trialCount = 16;
                    p.stimulusDurationSeconds = 1.0; p.responseWindowSeconds = 1.8;
                    p.interTrialIntervalSeconds = 0.7; p.targetMatchProportion = 0.30;
                    break;
            }
            return p;
        }

        private static TaskLevelParameters GoNoGo(int level)
        {
            var p = new TaskLevelParameters { taskType = TaskType.GoNoGo, level = level };
            switch (level)
            {
                case 1:
                    p.trialCount = 20; p.noGoProportion = 0.20; p.stimulusSimilarityTier = 1;
                    p.stimulusDurationSeconds = 0.8; p.responseWindowSeconds = 1.2;
                    p.interTrialIntervalSeconds = 1.2;
                    break;
                case 2:
                    p.trialCount = 24; p.noGoProportion = 0.30; p.stimulusSimilarityTier = 2;
                    p.stimulusDurationSeconds = 0.6; p.responseWindowSeconds = 1.0;
                    p.interTrialIntervalSeconds = 1.0;
                    break;
                default:
                    p.trialCount = 28; p.noGoProportion = 0.40; p.stimulusSimilarityTier = 3;
                    p.stimulusDurationSeconds = 0.5; p.responseWindowSeconds = 0.8;
                    p.interTrialIntervalSeconds = 0.8;
                    break;
            }
            return p;
        }

        private static TaskLevelParameters Flanker(int level)
        {
            var p = new TaskLevelParameters { taskType = TaskType.Flanker, level = level };
            switch (level)
            {
                case 1:
                    p.trialCount = 16; p.incongruentProportion = 0.35; p.neutralProportion = 0.15;
                    p.stimulusDurationSeconds = 1.0; p.responseWindowSeconds = 1.6;
                    p.interTrialIntervalSeconds = 1.0;
                    break;
                case 2:
                    p.trialCount = 20; p.incongruentProportion = 0.50; p.neutralProportion = 0.10;
                    p.stimulusDurationSeconds = 0.8; p.responseWindowSeconds = 1.3;
                    p.interTrialIntervalSeconds = 0.9;
                    break;
                default:
                    p.trialCount = 24; p.incongruentProportion = 0.60; p.neutralProportion = 0.0;
                    p.stimulusDurationSeconds = 0.7; p.responseWindowSeconds = 1.0;
                    p.interTrialIntervalSeconds = 0.75;
                    break;
            }
            return p;
        }
    }
}
