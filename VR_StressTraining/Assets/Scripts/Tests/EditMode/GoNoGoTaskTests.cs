using System.Collections.Generic;
using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Tasks;

namespace StressTraining.Tests.EditMode
{
    public sealed class GoNoGoTaskTests
    {
        [Test]
        public void GoPressedOnNoGo_IsCommissionError()
        {
            var result = Run(SemanticAction.None, SemanticAction.Go);
            Assert.That(result.Correct, Is.False);
            Assert.That(result.FalsePositive, Is.True);
            Assert.That(result.Missed, Is.False);
        }

        [Test]
        public void NoResponseOnGo_IsOmissionError()
        {
            var result = Run(SemanticAction.Go, SemanticAction.None);
            Assert.That(result.Correct, Is.False);
            Assert.That(result.Missed, Is.True);
            Assert.That(result.FalsePositive, Is.False);
        }

        [Test]
        public void NoResponseOnNoGo_IsCorrectInhibition()
        {
            var result = Run(SemanticAction.None, SemanticAction.None);
            Assert.That(result.Correct, Is.True);
            Assert.That(result.Missed, Is.False);
            Assert.That(result.FalsePositive, Is.False);
        }

        private static TaskTrialResult Run(SemanticAction expected, SemanticAction actual)
        {
            var trial = new TaskTrialDefinition
            {
                index = 0,
                stimulus = expected == SemanticAction.Go ? "go:T1" : "nogo:T1",
                expectedAction = expected,
                interTrialIntervalSeconds = 0,
                stimulusDurationSeconds = 0.1,
                responseWindowSeconds = 0.2
            };
            var runtime = new GoNoGoTaskRuntime(new List<TaskTrialDefinition> { trial });
            runtime.Begin();
            runtime.Tick(0.01);
            if (actual != SemanticAction.None)
            {
                runtime.SubmitAction(actual);
                runtime.Tick(0.01);
            }
            else
            {
                runtime.Tick(0.25);
            }
            return runtime.Results[0];
        }
    }
}
