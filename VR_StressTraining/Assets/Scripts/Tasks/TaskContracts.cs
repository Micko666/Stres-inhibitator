using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.Tasks
{
    /// <summary>
    /// Practice runs exercise the same task runtime and feedback path as scored
    /// runs, but are deliberately excluded from trial persistence, task HR
    /// aggregation and the session score.
    /// </summary>
    public enum TaskRunKind
    {
        Practice = 0,
        Scored = 1
    }

    /// <summary>Single source of truth for practice/scored data separation.</summary>
    public static class TaskRunPolicy
    {
        public static bool PersistsTrials(TaskRunKind kind) => kind == TaskRunKind.Scored;
        public static bool AggregatesTaskHeartRate(TaskRunKind kind) => kind == TaskRunKind.Scored;
        public static bool ContributesToSessionScore(TaskRunKind kind) => kind == TaskRunKind.Scored;
    }

    /// <summary>Stimulus modality for n-back (spec §12).</summary>
    public enum StimulusMode
    {
        Symbol = 0,
        Color = 1,
        Position = 2
    }

    public enum TrialPhase
    {
        Idle = 0,
        InterTrialInterval = 1,
        Stimulus = 2,       // stimulus visible, response window open
        ResponseOpen = 3,   // stimulus hidden, response window still open
        Feedback = 4,
        Finished = 5,
        Aborted = 6
    }

    /// <summary>One planned trial. Produced deterministically by a seeded generator.</summary>
    [Serializable]
    public sealed class TaskTrialDefinition
    {
        public int index;                       // 0-based within block (warmups included)
        public bool isWarmup;                   // shown but not scoreable (n-back first n trials)
        public string stimulus;                 // encoded, e.g. "sym:K", "go:TARGET", "arr:<<><<"
        public SemanticAction expectedAction;   // None = correct response is NO response (no-go)
        public double stimulusDurationSeconds;
        public double responseWindowSeconds;    // from stimulus onset
        public double interTrialIntervalSeconds;

        // Flanker-specific classification, empty for other tasks: "congruent"/"incongruent"/"neutral"
        public string congruency = "";

        // Practice-only coaching cue. Empty for scored trials and never persisted
        // as a response; the runner shows it before the practice outcome.
        public string practiceCue = "";
    }

    /// <summary>Outcome of one executed trial (runtime object; persisted as TrialRecord).</summary>
    public sealed class TaskTrialResult
    {
        public TaskTrialDefinition Definition;
        public SemanticAction ActualAction = SemanticAction.None;
        public double ReactionTimeMs = -1;
        public bool Correct;
        public bool Missed;                     // omission
        public bool FalsePositive;              // commission
        public bool WasInterruptedByPause;      // trial was restarted after a pause
        public string StimulusPresentedAtUtcIso = "";
        public string ResponseAtUtcIso = "";
        public double StimulusOnsetActiveSeconds;

        /// <summary>Corsi-only reproduction detail; null for every other task.</summary>
        public CorsiTrialDetail Corsi;
    }

    /// <summary>Full reproducibility record of one Corsi trial (spec §18.4).</summary>
    public sealed class CorsiTrialDetail
    {
        public List<int> PresentedSequence = new List<int>();
        public List<int> RequiredResponseOrder = new List<int>();  // forward = same, backward = reversed
        public List<int> ResponsePrefix = new List<int>();         // actual presses in order
        public int SequenceLength;
        public int CorrectlyReproducedCount;
        public int FirstErrorIndex = -1;                           // -1 = no wrong press
        public string FirstWrongButtonId = "";
        public double StartResponseMs = -1;                        // response-phase start → first press
        public List<double> InterPressIntervalsMs = new List<double>();
        public double TotalResponseMs = -1;
        public bool Backward;
        public bool TimedOut;
        public bool AbandonedByInactivity;
    }

    /// <summary>Aggregate over the scoreable trials of one block.</summary>
    public sealed class TaskBlockResult
    {
        public string BlockId;
        public TaskType TaskType;
        public int DifficultyLevel;
        public int BlockSeed;
        public List<TaskTrialResult> Trials = new List<TaskTrialResult>();

        public int ScoreableCount;
        public int CorrectCount;
        public int MissCount;
        public int FalsePositiveCount;
        public float Accuracy;
        public float MeanReactionTimeMs = -1;
        public float MedianReactionTimeMs = -1;

        public static TaskBlockResult Aggregate(string blockId, TaskType type, int level, int seed,
            List<TaskTrialResult> trials)
        {
            var r = new TaskBlockResult
            {
                BlockId = blockId, TaskType = type, DifficultyLevel = level, BlockSeed = seed,
                Trials = trials
            };
            var rts = new List<double>();
            foreach (var t in trials)
            {
                if (t.Definition.isWarmup) continue;
                r.ScoreableCount++;
                if (t.Correct) r.CorrectCount++;
                if (t.Missed) r.MissCount++;
                if (t.FalsePositive) r.FalsePositiveCount++;
                if (t.Correct && t.ReactionTimeMs >= 0) rts.Add(t.ReactionTimeMs);
            }
            r.Accuracy = r.ScoreableCount > 0 ? (float)r.CorrectCount / r.ScoreableCount : 0f;
            if (rts.Count > 0)
            {
                double sum = 0;
                foreach (var v in rts) sum += v;
                r.MeanReactionTimeMs = (float)(sum / rts.Count);
                rts.Sort();
                r.MedianReactionTimeMs = (float)(rts.Count % 2 == 1
                    ? rts[rts.Count / 2]
                    : (rts[rts.Count / 2 - 1] + rts[rts.Count / 2]) * 0.5);
            }
            return r;
        }
    }

    /// <summary>Definition side of a task: produces seeded trial lists for a level.</summary>
    public interface ITaskDefinition
    {
        TaskType TaskType { get; }
        string DisplayName { get; }
        List<TaskTrialDefinition> GenerateTrials(int difficultyLevel, int seed);
    }

    /// <summary>Runtime side: a tickable, pause-aware state machine over a trial list.</summary>
    public interface ITaskRuntime
    {
        TaskType TaskType { get; }
        TrialPhase Phase { get; }
        int CurrentTrialIndex { get; }
        int TrialCount { get; }
        TaskTrialDefinition CurrentTrial { get; }

        event Action<TaskTrialDefinition> StimulusShown;
        event Action StimulusHidden;
        event Action<TaskTrialResult> TrialCompleted;
        event Action BlockFinished;
        event Action BlockAborted;

        void Begin();
        /// <summary>Advance with ACTIVE (unpaused) delta seconds. Caller must not tick while paused.</summary>
        void Tick(double activeDeltaSeconds);
        /// <summary>Submit a semantic action. Ignored while paused / irrelevant actions filtered.</summary>
        void SubmitAction(SemanticAction action);
        /// <summary>Called when a pause interrupted the block; mid-trial state is safely restarted.</summary>
        void OnPauseInterrupt();
        void Abort();
        void Reset();
        List<TaskTrialResult> Results { get; }
    }
}
