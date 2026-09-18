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
    /// - Levels 2–3 add purely visual distractor rows above and below, encoded as
    ///   "arr:&lt;top&gt;|&lt;relevant&gt;|&lt;bottom&gt;". The relevant row is always the MIDDLE
    ///   one, so the instruction "the centre arrow of the middle row" is exact. The
    ///   extra rows are generated from the same seeded RNG and never touch
    ///   expectedAction, congruency or scoring. Level 1 emits a bare single row, so
    ///   its stimuli are byte-identical to before this was added.
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

                // Distractor rows are drawn AFTER the relevant row so that a level
                // without them consumes no extra random draws and keeps producing
                // exactly the sequence it produced before this feature existed.
                string encoded = p.flankerDistractorRows > 0
                    ? "arr:" + DistractorRow(rng, p.flankerDistractorDensity) + RowSeparator +
                      arrows + RowSeparator + DistractorRow(rng, p.flankerDistractorDensity)
                    : "arr:" + arrows;

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

        /// <summary>Separates stimulus rows inside one encoded flanker stimulus.</summary>
        public const char RowSeparator = '|';

        /// <summary>Characters per row — the classic five-mark flanker array.</summary>
        public const int RowLength = 5;

        /// <summary>
        /// One irrelevant row: each cell is a random direction with probability
        /// <paramref name="density"/>, otherwise a neutral dash. Direction is drawn
        /// independently per cell, so nothing about these rows can correlate with
        /// the relevant row's answer.
        /// </summary>
        private static string DistractorRow(Random rng, double density)
        {
            var row = new char[RowLength];
            for (int i = 0; i < RowLength; i++)
                row[i] = rng.NextDouble() < density
                    ? (rng.Next(2) == 0 ? '<' : '>')
                    : '-';
            return new string(row);
        }

        /// <summary>Rows of an encoded stimulus, in top-to-bottom order.</summary>
        public static string[] Rows(string encodedStimulus)
        {
            if (string.IsNullOrEmpty(encodedStimulus)) return Array.Empty<string>();
            string body = encodedStimulus.StartsWith("arr:", StringComparison.Ordinal)
                ? encodedStimulus.Substring(4)
                : encodedStimulus;
            return body.Length == 0 ? Array.Empty<string>() : body.Split(RowSeparator);
        }

        /// <summary>
        /// The single row that carries the answer: the middle one when distractor
        /// rows are present, the only one otherwise.
        /// </summary>
        public static string RelevantRow(string encodedStimulus)
        {
            var rows = Rows(encodedStimulus);
            return rows.Length == 0 ? string.Empty : rows[rows.Length / 2];
        }

        /// <summary>
        /// Returns the response dictated only by the central arrow of the relevant
        /// row. Keeping this rule explicit prevents the flanker characters — and the
        /// distractor rows added at levels 2–3 — from influencing the expected
        /// action, and makes the tutorial examples directly testable.
        /// </summary>
        public static SemanticAction CentralDirection(string encodedStimulus)
        {
            string arrows = RelevantRow(encodedStimulus);
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
