using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.Tasks
{
    /// <summary>
    /// Arrow flanker (spec §14). The tablet shows five characters; the user answers
    /// Left/Right for the CENTER arrow on the console.
    ///
    /// - Exact congruent/incongruent/neutral counts derived from proportions,
    ///   seeded-shuffled; center direction seeded 50/50.
    /// - Stimulus encoding: "arr:&lt;&lt;&lt;&lt;&lt;" (congruent), "arr:&gt;&gt;&lt;&gt;&gt;" (incongruent),
    ///   "arr:--&lt;--" (neutral). congruency field carries the classification for
    ///   the congruency-effect summary.
    /// </summary>
    public sealed class FlankerTaskDefinition : ITaskDefinition
    {
        public TaskType TaskType => TaskType.Flanker;
        public string DisplayName => "Flanker";

        public List<TaskTrialDefinition> GenerateTrials(int difficultyLevel, int seed)
        {
            var p = TaskDifficultyConfig.Get(TaskType.Flanker, difficultyLevel);
            return Generate(p, seed);
        }

        public static List<TaskTrialDefinition> Generate(TaskLevelParameters p, int seed)
        {
            var rng = new Random(seed);
            int total = p.trialCount;
            int incongruent = (int)Math.Round(p.incongruentProportion * total);
            int neutral = (int)Math.Round(p.neutralProportion * total);
            int congruent = Math.Max(0, total - incongruent - neutral);

            var kinds = new List<string>(total);
            for (int i = 0; i < congruent; i++) kinds.Add("congruent");
            for (int i = 0; i < incongruent; i++) kinds.Add("incongruent");
            for (int i = 0; i < neutral; i++) kinds.Add("neutral");
            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }

            var trials = new List<TaskTrialDefinition>(total);
            for (int i = 0; i < total; i++)
            {
                bool centerLeft = rng.Next(2) == 0;
                char center = centerLeft ? '<' : '>';
                char flank;
                switch (kinds[i])
                {
                    case "incongruent": flank = centerLeft ? '>' : '<'; break;
                    case "neutral": flank = '-'; break;
                    default: flank = center; break;
                }
                string arrows = $"{flank}{flank}{center}{flank}{flank}";
                string encoded = "arr:" + arrows;

                trials.Add(new TaskTrialDefinition
                {
                    index = i,
                    isWarmup = false,
                    stimulus = encoded,
                    expectedAction = CentralDirection(encoded),
                    stimulusDurationSeconds = p.stimulusDurationSeconds,
                    responseWindowSeconds = p.responseWindowSeconds,
                    interTrialIntervalSeconds = p.interTrialIntervalSeconds,
                    congruency = kinds[i]
                });
            }
            return trials;
        }

        /// <summary>
        /// Returns the response dictated only by the central arrow. Keeping this
        /// rule explicit prevents the flanker characters from influencing the
        /// expected action and makes the tutorial examples directly testable.
        /// </summary>
        public static SemanticAction CentralDirection(string encodedStimulus)
        {
            if (string.IsNullOrEmpty(encodedStimulus)) return SemanticAction.None;
            string arrows = encodedStimulus.StartsWith("arr:", StringComparison.Ordinal)
                ? encodedStimulus.Substring(4)
                : encodedStimulus;
            if (arrows.Length == 0) return SemanticAction.None;
            char center = arrows[arrows.Length / 2];
            if (center == '<') return SemanticAction.Left;
            if (center == '>') return SemanticAction.Right;
            return SemanticAction.None;
        }

        /// <summary>Congruency effect: mean RT(incongruent) − mean RT(congruent), correct trials only.</summary>
        public static float CongruencyEffectMs(List<TaskTrialResult> results)
        {
            double cSum = 0, iSum = 0;
            int cN = 0, iN = 0;
            foreach (var r in results)
            {
                if (!r.Correct || r.ReactionTimeMs < 0) continue;
                if (r.Definition.congruency == "congruent") { cSum += r.ReactionTimeMs; cN++; }
                else if (r.Definition.congruency == "incongruent") { iSum += r.ReactionTimeMs; iN++; }
            }
            if (cN == 0 || iN == 0) return 0f;
            return (float)(iSum / iN - cSum / cN);
        }
    }

    public sealed class FlankerTaskRuntime : TaskRuntimeBase
    {
        public override TaskType TaskType => TaskType.Flanker;

        public FlankerTaskRuntime(List<TaskTrialDefinition> trials) : base(trials) { }

        protected override bool IsRelevantAction(SemanticAction action) =>
            action == SemanticAction.Left || action == SemanticAction.Right;

        protected override void Evaluate(TaskTrialResult r)
        {
            var def = r.Definition;
            bool responded = r.ActualAction != SemanticAction.None;
            r.Correct = responded && r.ActualAction == def.expectedAction;
            r.Missed = !responded;
            r.FalsePositive = false; // flanker uses incorrect/missed taxonomy only
        }
    }
}
