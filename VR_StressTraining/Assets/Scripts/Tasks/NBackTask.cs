using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.Tasks
{
    /// <summary>
    /// N-back (spec §12). The tablet shows a stream of stimuli; the user answers
    /// Match / NoMatch on the console for every scoreable trial.
    ///
    /// - First n trials are warm-up: presented, never scoreable, excluded from stats.
    /// - Match count is exact (round(targetMatchProportion * trialCount)) and
    ///   positions are seeded-shuffled → identical sequence for identical seed.
    /// - Stimulus encoding: "sym:K" / "col:RED" / "pos:4" depending on StimulusMode.
    /// </summary>
    public sealed class NBackTaskDefinition : ITaskDefinition
    {
        public TaskType TaskType => TaskType.NBack;
        public string DisplayName => "N-Back";

        public static readonly string[] Symbols = { "B", "K", "M", "R", "S", "T", "H", "P" };
        public static readonly string[] Colors = { "RED", "BLUE", "GREEN", "YELLOW", "PURPLE", "ORANGE", "CYAN", "WHITE" };
        public const int PositionGridSize = 9; // 3x3 tablet grid

        public List<TaskTrialDefinition> GenerateTrials(int difficultyLevel, int seed)
        {
            var p = TaskDifficultyConfig.Get(TaskType.NBack, difficultyLevel);
            return Generate(p, seed);
        }

        public static List<TaskTrialDefinition> Generate(TaskLevelParameters p, int seed)
        {
            var rng = new Random(seed);
            int n = p.nBackN;
            int total = p.trialCount + n; // + warmups

            // Choose exact match positions among scoreable trials.
            int matchCount = (int)Math.Round(p.targetMatchProportion * p.trialCount);
            var isMatch = new bool[total];
            var scoreableIndices = new List<int>();
            for (int i = n; i < total; i++) scoreableIndices.Add(i);
            Shuffle(scoreableIndices, rng);
            for (int m = 0; m < matchCount && m < scoreableIndices.Count; m++)
                isMatch[scoreableIndices[m]] = true;

            var tokens = new string[total];
            for (int i = 0; i < total; i++)
            {
                if (i >= n && isMatch[i])
                {
                    tokens[i] = tokens[i - n];
                }
                else
                {
                    // pick a token different from the n-back token (avoids accidental matches)
                    string forbidden = i >= n ? tokens[i - n] : null;
                    string tok;
                    do { tok = Token(p, rng.Next(p.stimulusSetSize)); }
                    while (tok == forbidden);
                    tokens[i] = tok;
                }
            }

            var trials = new List<TaskTrialDefinition>(total);
            for (int i = 0; i < total; i++)
            {
                trials.Add(new TaskTrialDefinition
                {
                    index = i,
                    isWarmup = i < n,
                    stimulus = tokens[i],
                    expectedAction = i < n
                        ? SemanticAction.None
                        : (isMatch[i] ? SemanticAction.Match : SemanticAction.NoMatch),
                    stimulusDurationSeconds = p.stimulusDurationSeconds,
                    responseWindowSeconds = p.responseWindowSeconds,
                    interTrialIntervalSeconds = p.interTrialIntervalSeconds
                });
            }
            return trials;
        }

        private static string Token(TaskLevelParameters p, int idx)
        {
            switch (p.stimulusMode)
            {
                case StimulusMode.Color: return "col:" + Colors[idx % Colors.Length];
                case StimulusMode.Position: return "pos:" + (idx % PositionGridSize);
                default: return "sym:" + Symbols[idx % Symbols.Length];
            }
        }

        private static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    public sealed class NBackTaskRuntime : TaskRuntimeBase
    {
        public override TaskType TaskType => TaskType.NBack;

        public NBackTaskRuntime(List<TaskTrialDefinition> trials) : base(trials) { }

        protected override bool IsRelevantAction(SemanticAction action) =>
            action == SemanticAction.Match || action == SemanticAction.NoMatch;

        protected override void Evaluate(TaskTrialResult r)
        {
            var def = r.Definition;
            if (def.isWarmup)
            {
                // Warm-up: any response is recorded but nothing is scored.
                r.Correct = false; r.Missed = false; r.FalsePositive = false;
                return;
            }
            bool responded = r.ActualAction != SemanticAction.None;
            r.Correct = responded && r.ActualAction == def.expectedAction;
            r.Missed = !responded;
            // Commission-style error: claiming a match when there is none.
            r.FalsePositive = responded &&
                              r.ActualAction == SemanticAction.Match &&
                              def.expectedAction == SemanticAction.NoMatch;
        }
    }
}
