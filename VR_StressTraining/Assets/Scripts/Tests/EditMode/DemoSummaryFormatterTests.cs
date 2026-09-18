using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Session;

namespace StressTraining.Tests.EditMode
{
    public sealed class DemoSummaryFormatterTests
    {
        [Test]
        public void SimulatedSource_ShowsThreeTaskMetricsAndNoHeartRate()
        {
            var summary = new SessionSummaryData
            {
                completionStatus = CompletionStatus.Completed,
                totalActiveSeconds = 101,
                totalPausedSeconds = 20,
                pauseCount = 2,
                baseline = new BaselineSummaryData { averageBpm = 211.3f }
            };
            summary.taskSummaries.Add(new TaskSessionSummaryData
            {
                taskType = TaskType.NBack, accuracy = 0.8f, correctCount = 4,
                meanReactionTimeMs = 500, avgBpmDuringTask = 199
            });
            summary.taskSummaries.Add(new TaskSessionSummaryData
            {
                taskType = TaskType.GoNoGo, accuracy = 0.75f, correctCount = 8,
                missCount = 2, falsePositiveCount = 1, meanReactionTimeMs = 350,
                avgBpmDuringTask = 201
            });
            summary.taskSummaries.Add(new TaskSessionSummaryData
            {
                taskType = TaskType.Flanker, accuracy = 0.9f, correctCount = 6,
                meanReactionTimeMs = 450, avgBpmDuringTask = 205
            });

            string text = DemoSummaryFormatter.Format(summary, HrSourceType.Simulated);

            Assert.That(text, Does.Contain("N-back tačnost: 80%"));
            Assert.That(text, Does.Contain("Go/No-Go tačnost: 75%"));
            Assert.That(text, Does.Contain("Commission greške: 1"));
            Assert.That(text, Does.Contain("Omission greške: 2"));
            Assert.That(text, Does.Contain("Flanker tačnost: 90%"));
            Assert.That(text, Does.Contain("Prosječno vrijeme reakcije: 417 ms"));
            Assert.That(text, Does.Contain("Ukupno trajanje: 2:01"));
            Assert.That(text, Does.Contain("Broj pauza: 2"));
            Assert.That(text, Does.Contain("Status završetka: Completed"));
            Assert.That(text, Does.Not.Contain("BPM"));
            Assert.That(text, Does.Not.Contain("211"));
            Assert.That(text, Does.Not.Contain("199"));
            Assert.That(text, Does.Not.Contain("SIMULATED HR"));
        }
    }
}
