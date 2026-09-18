using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Tasks;

namespace StressTraining.Tests.EditMode
{
    public sealed class FlankerTaskTests
    {
        [TestCase("arr:>>>>>", SemanticAction.Right)]
        [TestCase("arr:<<<<<", SemanticAction.Left)]
        [TestCase("arr:<<><<", SemanticAction.Right)]
        [TestCase("arr:>><>>", SemanticAction.Left)]
        public void CentralDirection_UsesOnlyMiddleArrow(string stimulus, SemanticAction expected)
        {
            Assert.That(FlankerTaskDefinition.CentralDirection(stimulus), Is.EqualTo(expected));
        }

        [Test]
        public void PracticeContainsCongruentAndIncongruentTrials()
        {
            var trials = DemoTaskFactory.CreatePracticeTrials(TaskType.Flanker);
            Assert.That(trials.Exists(t => t.congruency == "congruent"), Is.True);
            Assert.That(trials.Exists(t => t.congruency == "incongruent"), Is.True);
            foreach (var trial in trials)
                Assert.That(trial.expectedAction,
                    Is.EqualTo(FlankerTaskDefinition.CentralDirection(trial.stimulus)));
        }
    }
}
