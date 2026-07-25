using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.Session
{
    /// <summary>Machine-readable reason codes for task inclusion/omission (spec §33).</summary>
    public static class TaskSelectionReason
    {
        public const string IncludedBecausePreviouslyOmitted = "IncludedBecausePreviouslyOmitted";
        public const string IncludedByBalancedSeededRotation = "IncludedByBalancedSeededRotation";
        public const string OmittedByBalancedSeededRotation = "OmittedByBalancedSeededRotation";
        public const string OmittedByConfigDisable = "OmittedByConfigDisable";
        public const string IncludedAllRemainingEnabled = "IncludedAllRemainingEnabled";
    }

    public sealed class TaskSelectionResult
    {
        public const string AlgorithmVersion = "sel-v1";

        public List<TaskType> SelectedTasks = new List<TaskType>(3);
        public TaskType OmittedTask = TaskType.None;
        public List<string> ReasonCodes = new List<string>();
        public int SelectionSeed;
    }

    /// <summary>
    /// Deterministic 3-of-4 task selection (spec §7):
    /// 1. same seed + same history + same config version → same selection;
    /// 2. exactly three DISTINCT tasks are selected, one omitted;
    /// 3. a task omitted in the previous VALID production session is guaranteed
    ///    to be included now — no task can be omitted twice in a row (unless
    ///    disabled by configuration);
    /// 4. tie-breaking is seeded and reproducible;
    /// 5. invalid/aborted/demo sessions must not be passed in as history —
    ///    the caller resolves "previous omitted" from the last VALID production
    ///    session only (see ResolvePreviousOmitted).
    /// Pure static — fully unit-testable.
    /// </summary>
    public static class SeededConstrainedTaskSelector
    {
        public static readonly TaskType[] FullPool =
        {
            TaskType.NBack, TaskType.GoNoGo, TaskType.Flanker, TaskType.CorsiSequence
        };

        public static TaskSelectionResult Select(
            int selectionSeed,
            TaskType previousOmitted,
            IReadOnlyList<TaskType> enabledPool = null)
        {
            var pool = new List<TaskType>(enabledPool ?? FullPool);
            pool.RemoveAll(t => t == TaskType.None);
            if (pool.Count < 3)
                throw new ArgumentException(
                    $"Task pool must contain at least 3 enabled tasks (has {pool.Count}).");

            var result = new TaskSelectionResult { SelectionSeed = selectionSeed };
            var rng = new Random(selectionSeed);

            // Config-disabled tasks are recorded as omitted-by-config when the
            // pool is exactly 3 (all enabled are then included).
            if (pool.Count == 3)
            {
                foreach (var t in FullPool)
                    if (!pool.Contains(t)) { result.OmittedTask = t; break; }
                result.SelectedTasks.AddRange(pool);
                result.ReasonCodes.Add(TaskSelectionReason.OmittedByConfigDisable + ":" + result.OmittedTask);
                foreach (var t in pool)
                    result.ReasonCodes.Add(TaskSelectionReason.IncludedAllRemainingEnabled + ":" + t);
                Canonicalize(result);
                return result;
            }

            // Rule: previously omitted (valid production) task is forced in.
            var mustInclude = new List<TaskType>();
            if (previousOmitted != TaskType.None && pool.Contains(previousOmitted))
            {
                mustInclude.Add(previousOmitted);
                result.ReasonCodes.Add(
                    TaskSelectionReason.IncludedBecausePreviouslyOmitted + ":" + previousOmitted);
            }

            // Seeded omission among candidates that are NOT protected.
            var omissionCandidates = new List<TaskType>(pool);
            omissionCandidates.RemoveAll(t => mustInclude.Contains(t));
            TaskType omitted = omissionCandidates[rng.Next(omissionCandidates.Count)];
            result.OmittedTask = omitted;
            result.ReasonCodes.Add(TaskSelectionReason.OmittedByBalancedSeededRotation + ":" + omitted);

            foreach (var t in pool)
            {
                if (t == omitted) continue;
                result.SelectedTasks.Add(t);
                if (!mustInclude.Contains(t))
                    result.ReasonCodes.Add(TaskSelectionReason.IncludedByBalancedSeededRotation + ":" + t);
            }
            Canonicalize(result);
            return result;
        }

        /// <summary>Canonical enum order for the selected set (block order is decided separately).</summary>
        private static void Canonicalize(TaskSelectionResult result) =>
            result.SelectedTasks.Sort((a, b) => ((int)a).CompareTo((int)b));

        /// <summary>
        /// Finds the omitted task of the most recent VALID production session.
        /// Demo, invalid and incomplete sessions never influence the balance
        /// (spec §7 rule 7). Returns TaskType.None for a fresh history.
        /// </summary>
        public static TaskType ResolvePreviousOmitted(IReadOnlyList<SessionSummaryData> history)
        {
            if (history == null) return TaskType.None;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var s = history[i];
                if (s == null || !s.isProductionSession) continue;
                bool usable = s.validityStatus == ValidityStatus.Valid ||
                              s.validityStatus == ValidityStatus.ValidWithWarnings;
                if (!usable) continue;
                return s.omittedTaskType;
            }
            return TaskType.None;
        }
    }
}
