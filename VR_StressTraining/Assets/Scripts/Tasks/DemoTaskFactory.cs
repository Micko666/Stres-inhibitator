using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.Tasks
{
    /// <summary>
    /// Deterministic content for the short three-task demo. Practice sequences
    /// are intentionally explicit so the tutorial examples and expected
    /// actions never drift with difficulty configuration. Scored demo blocks
    /// reuse the production seeded generators with a bounded trial count.
    /// </summary>
    public static class DemoTaskFactory
    {
        public const int DefaultNBackTrials = 8;
        public const int DefaultGoNoGoTrials = 10;
        public const int DefaultFlankerTrials = 10;
        public const int DefaultCorsiTrials = 5;

        public static List<TaskTrialDefinition> CreatePracticeTrials(TaskType taskType)
        {
            switch (taskType)
            {
                case TaskType.NBack: return NBackPractice();
                case TaskType.GoNoGo: return GoNoGoPractice();
                case TaskType.Flanker: return FlankerPractice();
                case TaskType.CorsiSequence: return CorsiPractice();
                default: throw new ArgumentOutOfRangeException(nameof(taskType), taskType, "No practice sequence");
            }
        }

        public static List<TaskTrialDefinition> CreateShortScoredTrials(
            TaskType taskType, int difficultyLevel, int seed, int requestedScoreableCount = 0)
        {
            var parameters = TaskDifficultyConfig.Get(taskType, difficultyLevel);
            parameters.trialCount = ClampDemoCount(taskType, requestedScoreableCount);

            switch (taskType)
            {
                case TaskType.NBack: return NBackTaskDefinition.Generate(parameters, seed);
                case TaskType.GoNoGo: return GoNoGoTaskDefinition.Generate(parameters, seed);
                case TaskType.Flanker: return FlankerTaskDefinition.Generate(parameters, seed);
                case TaskType.CorsiSequence: return CorsiTaskDefinition.Generate(parameters, seed);
                default: throw new ArgumentOutOfRangeException(nameof(taskType), taskType, "No demo generator");
            }
        }

        public static ITaskRuntime CreateRuntime(TaskType taskType, List<TaskTrialDefinition> trials,
            int difficultyLevel = 1)
        {
            switch (taskType)
            {
                case TaskType.NBack: return new NBackTaskRuntime(trials);
                case TaskType.GoNoGo: return new GoNoGoTaskRuntime(trials);
                case TaskType.Flanker: return new FlankerTaskRuntime(trials);
                case TaskType.CorsiSequence:
                    return new CorsiTaskRuntime(trials,
                        TaskDifficultyConfig.Get(TaskType.CorsiSequence, difficultyLevel));
                default: throw new ArgumentOutOfRangeException(nameof(taskType), taskType, "No task runtime");
            }
        }

        public static int ScoreableCount(IReadOnlyList<TaskTrialDefinition> trials)
        {
            if (trials == null) return 0;
            int count = 0;
            for (int i = 0; i < trials.Count; i++)
                if (!trials[i].isWarmup) count++;
            return count;
        }

        public static int WarmupCount(IReadOnlyList<TaskTrialDefinition> trials)
        {
            if (trials == null) return 0;
            int count = 0;
            for (int i = 0; i < trials.Count; i++)
                if (trials[i].isWarmup) count++;
            return count;
        }

        public static bool IsActionForTask(TaskType taskType, SemanticAction action)
        {
            switch (taskType)
            {
                case TaskType.NBack:
                    return action == SemanticAction.Match || action == SemanticAction.NoMatch;
                case TaskType.GoNoGo:
                    return action == SemanticAction.Go;
                case TaskType.Flanker:
                    return action == SemanticAction.Left || action == SemanticAction.Right;
                case TaskType.CorsiSequence:
                    return CorsiLayout.IndexOf(action) >= 0;
                default:
                    return false;
            }
        }

        public static string DisplayName(TaskType taskType)
        {
            switch (taskType)
            {
                case TaskType.NBack: return "N-back";
                case TaskType.GoNoGo: return "Go/No-Go";
                case TaskType.Flanker: return "Flanker";
                // Participant-facing name; technical/code name stays CorsiSequence.
                case TaskType.CorsiSequence: return "Sekvencijalna memorija";
                default: return "Task";
            }
        }

        private static int ClampDemoCount(TaskType taskType, int requested)
        {
            switch (taskType)
            {
                case TaskType.NBack:
                    return Math.Max(6, Math.Min(10, requested > 0 ? requested : DefaultNBackTrials));
                case TaskType.GoNoGo:
                    return Math.Max(8, Math.Min(12, requested > 0 ? requested : DefaultGoNoGoTrials));
                case TaskType.Flanker:
                    return Math.Max(8, Math.Min(12, requested > 0 ? requested : DefaultFlankerTrials));
                case TaskType.CorsiSequence:
                    return Math.Max(4, Math.Min(6, requested > 0 ? requested : DefaultCorsiTrials));
                default:
                    throw new ArgumentOutOfRangeException(nameof(taskType), taskType, "No demo range");
            }
        }

        private static List<TaskTrialDefinition> NBackPractice() => new List<TaskTrialDefinition>
        {
            Trial(0, "sym:A", SemanticAction.None, true, 1.4, 2.8, 0.6,
                "Zapamti uvodni simbol. Ne pritiskaj ništa."),
            Trial(1, "sym:A", SemanticAction.Match, false, 1.4, 2.8, 0.6,
                "Isti je kao prethodni: pritisni MATCH."),
            Trial(2, "sym:B", SemanticAction.NoMatch, false, 1.4, 2.8, 0.6,
                "Različit je: pritisni NO MATCH."),
            Trial(3, "sym:B", SemanticAction.Match, false, 1.4, 2.8, 0.6,
                "Isti je kao prethodni: pritisni MATCH.")
        };

        private static List<TaskTrialDefinition> GoNoGoPractice() => new List<TaskTrialDefinition>
        {
            Trial(0, "go:T1", SemanticAction.Go, false, 0.9, 2.0, 0.7,
                "Pritisni GO da vidiš tačan GO odgovor."),
            Trial(1, "nogo:T1", SemanticAction.None, false, 0.9, 2.0, 0.7,
                "Ne pritiskaj ništa: vježbaj tačno suzdržavanje."),
            Trial(2, "go:T1", SemanticAction.Go, false, 0.9, 2.0, 0.7,
                "Za ovu vježbu ne pritiskaj GO: vidi kako izgleda propušten GO."),
            Trial(3, "nogo:T1", SemanticAction.None, false, 0.9, 2.0, 0.7,
                "Za ovu vježbu pritisni GO: vidi commission grešku na NO GO.")
        };

        private static List<TaskTrialDefinition> CorsiPractice()
        {
            // Fixed, explicit sequences — presentation is on the tablet map,
            // reproduction on the physical Corsi buttons (forward order).
            TaskTrialDefinition Corsi(int index, string seq, int len, string cue) =>
                new TaskTrialDefinition
                {
                    index = index,
                    stimulus = seq,
                    expectedAction = Data.SemanticAction.None,
                    isWarmup = false,
                    stimulusDurationSeconds = len * 0.9,     // PROJECT_HEURISTIC practice pace
                    responseWindowSeconds = 2.5 + len * 1.4,
                    interTrialIntervalSeconds = 1.2,
                    practiceCue = cue
                };

            return new List<TaskTrialDefinition>
            {
                Corsi(0, "corsi:4-7", 2,
                    "Gledaj koje se pozicije pale na tabletu, pa ih pritisni ISTIM redom."),
                Corsi(1, "corsi:1-5-8", 3,
                    "Sada tri pozicije — isti redosljed kao na tabletu."),
                Corsi(2, "corsi:6-2-0", 3,
                    "Prva pogrešna pozicija završava pokušaj — polako i redom.")
            };
        }

        private static List<TaskTrialDefinition> FlankerPractice() => new List<TaskTrialDefinition>
        {
            FlankerTrial(0, "arr:>>>>>", "congruent"),
            FlankerTrial(1, "arr:<<<<<", "congruent"),
            FlankerTrial(2, "arr:<<><<", "incongruent"),
            FlankerTrial(3, "arr:>><>>", "incongruent")
        };

        private static TaskTrialDefinition FlankerTrial(int index, string stimulus, string congruency)
        {
            var trial = Trial(index, stimulus, FlankerTaskDefinition.CentralDirection(stimulus),
                false, 1.2, 2.4, 0.7, "Odgovori prema pravcu CENTRALNE strelice.");
            trial.congruency = congruency;
            return trial;
        }

        private static TaskTrialDefinition Trial(int index, string stimulus, SemanticAction expected,
            bool warmup, double stimulusSeconds, double responseSeconds, double itiSeconds,
            string practiceCue = "")
        {
            return new TaskTrialDefinition
            {
                index = index,
                stimulus = stimulus,
                expectedAction = expected,
                isWarmup = warmup,
                stimulusDurationSeconds = stimulusSeconds,
                responseWindowSeconds = responseSeconds,
                interTrialIntervalSeconds = itiSeconds,
                practiceCue = practiceCue
            };
        }
    }
}
