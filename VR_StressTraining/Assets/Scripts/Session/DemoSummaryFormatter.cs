using System;
using System.Collections.Generic;
using System.Text;
using StressTraining.Data;

namespace StressTraining.Session
{
    /// <summary>
    /// Participant-facing formatter for the bounded three-task demo. It exposes
    /// performance/session facts only; physiological and adaptive interpretation
    /// deliberately stays out of this view.
    /// </summary>
    public static class DemoSummaryFormatter
    {
        public static string Format(SessionSummaryData summary, HrSourceType sourceType)
        {
            if (summary == null) return "Nema dostupnog sažetka sesije.";

            // Source is explicit at the boundary so callers cannot accidentally
            // infer a real measurement from simulated records. This demo summary
            // currently omits HR for every source; in particular, no simulated
            // BPM value can enter participant-facing text.
            _ = sourceType;

            TaskSessionSummaryData nBack = Find(summary.taskSummaries, TaskType.NBack);
            TaskSessionSummaryData goNoGo = Find(summary.taskSummaries, TaskType.GoNoGo);
            TaskSessionSummaryData flanker = Find(summary.taskSummaries, TaskType.Flanker);

            var text = new StringBuilder(320);
            text.AppendLine("N-back tačnost: " + Accuracy(nBack));
            text.AppendLine("Go/No-Go tačnost: " + Accuracy(goNoGo));
            text.AppendLine("Commission greške: " + Count(goNoGo, falsePositive: true));
            text.AppendLine("Omission greške: " + Count(goNoGo, falsePositive: false));
            text.AppendLine("Flanker tačnost: " + Accuracy(flanker));
            text.AppendLine("Prosječno vrijeme reakcije: " + MeanReactionTime(summary.taskSummaries));
            text.AppendLine("Ukupno trajanje: " + Duration(summary.totalActiveSeconds + summary.totalPausedSeconds));
            text.AppendLine("Broj pauza: " + summary.pauseCount);
            text.Append("Status završetka: " + summary.completionStatus);
            return text.ToString();
        }

        private static TaskSessionSummaryData Find(List<TaskSessionSummaryData> summaries, TaskType type)
        {
            if (summaries == null) return null;
            for (int i = 0; i < summaries.Count; i++)
                if (summaries[i] != null && summaries[i].taskType == type) return summaries[i];
            return null;
        }

        private static string Accuracy(TaskSessionSummaryData value) =>
            value == null ? "nema podataka" : $"{value.accuracy * 100f:0}%";

        private static int Count(TaskSessionSummaryData value, bool falsePositive) =>
            value == null ? 0 : (falsePositive ? value.falsePositiveCount : value.missCount);

        private static string MeanReactionTime(List<TaskSessionSummaryData> summaries)
        {
            if (summaries == null) return "nema podataka";
            double weightedSum = 0;
            int weight = 0;
            for (int i = 0; i < summaries.Count; i++)
            {
                TaskSessionSummaryData task = summaries[i];
                if (task == null || task.meanReactionTimeMs < 0 || task.correctCount <= 0) continue;
                weightedSum += task.meanReactionTimeMs * task.correctCount;
                weight += task.correctCount;
            }
            return weight == 0 ? "nema podataka" : $"{weightedSum / weight:0} ms";
        }

        private static string Duration(double seconds)
        {
            int rounded = Math.Max(0, (int)Math.Round(seconds));
            return $"{rounded / 60:0}:{rounded % 60:00}";
        }
    }
}
