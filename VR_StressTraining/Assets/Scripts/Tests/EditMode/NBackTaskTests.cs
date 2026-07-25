using System.Collections.Generic;
using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Tasks;

namespace StressTraining.Tests.EditMode
{
    public sealed class NBackTaskTests
    {
        [Test]
        public void SameSeed_ProducesSameSequenceAndExpectedActions()
        {
            var definition = new NBackTaskDefinition();
            var a = definition.GenerateTrials(1, 42);
            var b = definition.GenerateTrials(1, 42);
            Assert.That(a.Count, Is.EqualTo(b.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].stimulus, Is.EqualTo(b[i].stimulus));
                Assert.That(a[i].expectedAction, Is.EqualTo(b[i].expectedAction));
            }
        }

        [Test]
        public void WarmupCount_EqualsN_AndScoreableExpectedActionsAreDefined()
        {
            var p = TaskDifficultyConfig.Get(TaskType.NBack, 1);
            var trials = NBackTaskDefinition.Generate(p, 7);
            Assert.That(trials[0].isWarmup, Is.True);
            Assert.That(trials[0].expectedAction, Is.EqualTo(SemanticAction.None));
            for (int i = p.nBackN; i < trials.Count; i++)
                Assert.That(trials[i].expectedAction == SemanticAction.Match ||
                            trials[i].expectedAction == SemanticAction.NoMatch, Is.True);
        }

        [Test]
        public void NoInput_ProducesMissedScoreableTrial()
        {
            var trial = new TaskTrialDefinition
            {
                index = 0, isWarmup = false, stimulus = "sym:A",
                expectedAction = SemanticAction.NoMatch,
                interTrialIntervalSeconds = 0, stimulusDurationSeconds = 0.1,
                responseWindowSeconds = 0.2
            };
            var runtime = new NBackTaskRuntime(new List<TaskTrialDefinition> { trial });
            runtime.Begin();
            runtime.Tick(0.01);
            runtime.Tick(0.25);

            Assert.That(runtime.Results.Count, Is.EqualTo(1));
            Assert.That(runtime.Results[0].Missed, Is.True);
        }

        [Test]
        public void AbortMidTrial_DoesNotEmitCompletionOrBlockFinished()
        {
            var trial = new TaskTrialDefinition
            {
                index = 0, stimulus = "sym:A", expectedAction = SemanticAction.Match,
                interTrialIntervalSeconds = 0, stimulusDurationSeconds = 1,
                responseWindowSeconds = 2
            };
            var runtime = new NBackTaskRuntime(new List<TaskTrialDefinition> { trial });
            bool finished = false;
            runtime.BlockFinished += () => finished = true;
            runtime.Begin();
            runtime.Tick(0.01);
            runtime.Abort();
            runtime.Tick(10);

            Assert.That(runtime.Phase, Is.EqualTo(TrialPhase.Aborted));
            Assert.That(runtime.Results, Is.Empty);
            Assert.That(finished, Is.False);
        }

        [Test]
        public void WarmupIntro_IgnoresInputAndCannotCompleteEarly()
        {
            var trial = new TaskTrialDefinition
            {
                index = 0, isWarmup = true, stimulus = "sym:A",
                expectedAction = SemanticAction.None,
                interTrialIntervalSeconds = 0, stimulusDurationSeconds = 0.5,
                responseWindowSeconds = 1.0
            };
            var runtime = new NBackTaskRuntime(new List<TaskTrialDefinition> { trial });
            runtime.Begin();
            runtime.Tick(0.01); // enter stimulus
            runtime.SubmitAction(SemanticAction.Match);
            runtime.Tick(0.01);

            Assert.That(runtime.Results, Is.Empty, "Intro must remain visible after an accidental press.");
            Assert.That(runtime.Phase, Is.EqualTo(TrialPhase.Stimulus));

            runtime.Tick(1.1);
            Assert.That(runtime.Results.Count, Is.EqualTo(1));
            Assert.That(runtime.Results[0].ActualAction, Is.EqualTo(SemanticAction.None));
            Assert.That(runtime.Results[0].Missed, Is.False);
        }
    }
}
