using System.Linq;
using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Tasks;

namespace StressTraining.Tests.EditMode
{
    public sealed class DemoTaskFactoryTests
    {
        [Test]
        public void NBackPractice_IsFixedAndSeparatedFromScoredData()
        {
            var practice = DemoTaskFactory.CreatePracticeTrials(TaskType.NBack);

            Assert.That(practice.Count, Is.EqualTo(4));
            Assert.That(practice[0].stimulus, Is.EqualTo("sym:A"));
            Assert.That(practice[0].isWarmup, Is.True);
            Assert.That(practice[0].expectedAction, Is.EqualTo(SemanticAction.None));
            Assert.That(practice[1].expectedAction, Is.EqualTo(SemanticAction.Match));
            Assert.That(practice[2].expectedAction, Is.EqualTo(SemanticAction.NoMatch));
            Assert.That(practice[3].expectedAction, Is.EqualTo(SemanticAction.Match));
            Assert.That(DemoTaskFactory.ScoreableCount(practice), Is.EqualTo(3));

            Assert.That(TaskRunPolicy.PersistsTrials(TaskRunKind.Practice), Is.False);
            Assert.That(TaskRunPolicy.AggregatesTaskHeartRate(TaskRunKind.Practice), Is.False);
            Assert.That(TaskRunPolicy.ContributesToSessionScore(TaskRunKind.Practice), Is.False);
            Assert.That(TaskRunPolicy.PersistsTrials(TaskRunKind.Scored), Is.True);
        }

        [TestCase(TaskType.NBack, 100, 10)]
        [TestCase(TaskType.GoNoGo, 1, 8)]
        [TestCase(TaskType.Flanker, 0, 10)]
        public void ShortScoredFactory_ClampsToRequestedDemoRange(
            TaskType taskType, int requested, int expectedScoreable)
        {
            var trials = DemoTaskFactory.CreateShortScoredTrials(taskType, 1, 1234, requested);
            Assert.That(DemoTaskFactory.ScoreableCount(trials), Is.EqualTo(expectedScoreable));
        }

        [Test]
        public void GoNoGoPractice_ExplicitlyCoachesAllFourRequiredOutcomes()
        {
            var practice = DemoTaskFactory.CreatePracticeTrials(TaskType.GoNoGo);
            Assert.That(practice.Count, Is.EqualTo(4));
            Assert.That(practice[0].practiceCue, Does.Contain("tačan GO"));
            Assert.That(practice[1].practiceCue, Does.Contain("suzdržavanje"));
            Assert.That(practice[2].practiceCue, Does.Contain("propušten GO"));
            Assert.That(practice[3].practiceCue, Does.Contain("commission"));
            Assert.That(practice[0].expectedAction, Is.EqualTo(SemanticAction.Go));
            Assert.That(practice[1].expectedAction, Is.EqualTo(SemanticAction.None));
            Assert.That(practice[2].expectedAction, Is.EqualTo(SemanticAction.Go));
            Assert.That(practice[3].expectedAction, Is.EqualTo(SemanticAction.None));
        }

        [TestCase(TaskType.NBack)]
        [TestCase(TaskType.GoNoGo)]
        [TestCase(TaskType.Flanker)]
        public void ShortScoredFactory_SameSeedProducesSamePlan(TaskType taskType)
        {
            var a = DemoTaskFactory.CreateShortScoredTrials(taskType, 1, 8128);
            var b = DemoTaskFactory.CreateShortScoredTrials(taskType, 1, 8128);

            Assert.That(a.Select(t => t.stimulus), Is.EqualTo(b.Select(t => t.stimulus)));
            Assert.That(a.Select(t => t.expectedAction), Is.EqualTo(b.Select(t => t.expectedAction)));
        }
    }
}
