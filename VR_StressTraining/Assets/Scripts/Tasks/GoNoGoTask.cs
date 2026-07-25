using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.Tasks
{
    /// <summary>
    /// Go/No-Go (spec §13). The tablet shows go or no-go stimuli; the user presses
    /// the central Go control on go stimuli and withholds on no-go stimuli.
    ///
    /// - Exact no-go count = round(noGoProportion * trialCount), seeded-shuffled.
    /// - Commission error (falsePositive): Go pressed on a no-go trial.
    /// - Omission error (missed): no press on a go trial.
    /// - Difficulty varies rhythm, window, no-go proportion and the visual
    ///   similarity tier the tablet uses to render the two stimuli.
    /// - Stimulus encoding: "go:T{tier}" / "nogo:T{tier}".
    /// - Results are session performance, not a stable psychometric trait.
    /// </summary>
    public sealed class GoNoGoTaskDefinition : ITaskDefinition
    {
        public TaskType TaskType => TaskType.GoNoGo;
        public string DisplayName => "Go / No-Go";

        public List<TaskTrialDefinition> GenerateTrials(int difficultyLevel, int seed)
        {
            var p = TaskDifficultyConfig.Get(TaskType.GoNoGo, difficultyLevel);
            return Generate(p, seed);
        }

        public static List<TaskTrialDefinition> Generate(TaskLevelParameters p, int seed)
        {
            var rng = new Random(seed);
            int total = p.trialCount;
            int noGoCount = (int)Math.Round(p.noGoProportion * total);

            var isNoGo = new List<bool>(total);
            for (int i = 0; i < total; i++) isNoGo.Add(i < noGoCount);
            for (int i = isNoGo.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (isNoGo[i], isNoGo[j]) = (isNoGo[j], isNoGo[i]);
            }

            var trials = new List<TaskTrialDefinition>(total);
            for (int i = 0; i < total; i++)
            {
                trials.Add(new TaskTrialDefinition
                {
                    index = i,
                    isWarmup = false,
                    stimulus = (isNoGo[i] ? "nogo:T" : "go:T") + p.stimulusSimilarityTier,
                    expectedAction = isNoGo[i] ? SemanticAction.None : SemanticAction.Go,
                    stimulusDurationSeconds = p.stimulusDurationSeconds,
                    responseWindowSeconds = p.responseWindowSeconds,
                    interTrialIntervalSeconds = p.interTrialIntervalSeconds
                });
            }
            return trials;
        }
    }

    public sealed class GoNoGoTaskRuntime : TaskRuntimeBase
    {
        public override TaskType TaskType => TaskType.GoNoGo;

        public GoNoGoTaskRuntime(List<TaskTrialDefinition> trials) : base(trials) { }

        protected override bool IsRelevantAction(SemanticAction action) =>
            action == SemanticAction.Go;

        protected override void Evaluate(TaskTrialResult r)
        {
            var def = r.Definition;
            bool responded = r.ActualAction == SemanticAction.Go;

            if (def.expectedAction == SemanticAction.Go)
            {
                r.Correct = responded;
                r.Missed = !responded;          // omission
                r.FalsePositive = false;
            }
            else // no-go: correct = successful inhibition
            {
                r.Correct = !responded;
                r.Missed = false;
                r.FalsePositive = responded;    // commission
            }
        }
    }
}
