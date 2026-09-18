using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Pressure;
using StressTraining.Session;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// The sandbox exists so a researcher can try anything without consequences. These
    /// tests hold the "without consequences" half: a sandbox run must stay invisible to
    /// a participant's profile, their training cycle and the adaptation, no matter what
    /// is clicked inside it.
    /// </summary>
    public sealed class DeveloperSandboxIsolationTests
    {
        [Test]
        public void SandboxRunsAreWrittenAsDemoOnly()
        {
            Assert.That(DeveloperSandbox.SandboxValidity, Is.EqualTo(ValidityStatus.DemoOnly));
        }

        [Test]
        public void SandboxValidityNeverCountsTowardATrainingCycle()
        {
            Assert.That(UserProfileService.CountsTowardCycle(DeveloperSandbox.SandboxValidity),
                Is.False, "Sandbox sesija ne smije trošiti mjesto u ciklusu.");
        }

        [Test]
        public void SandboxValidityIsNotUsableBySchedulerEither()
        {
            // Same two statuses gate the scheduler, so a status that cannot count
            // toward a cycle also cannot move a level.
            bool usable = DeveloperSandbox.SandboxValidity == ValidityStatus.Valid ||
                          DeveloperSandbox.SandboxValidity == ValidityStatus.ValidWithWarnings;
            Assert.That(usable, Is.False, "Sandbox sesija ne smije hraniti adaptaciju.");
        }

        // ── the menu's offers must mean what they say ────────────────────

        [Test]
        public void EveryStageJumpLandsInTheStageItIsLabelledWith()
        {
            foreach (var jump in DeveloperSandbox.StageJumps)
            {
                PressureStage actual = PressureTimeline.StageFor(jump.Fraction);
                Assert.That(actual.ToString(), Is.EqualTo(jump.Label),
                    "Skok „" + jump.Label + "\" (preostalo " + jump.Fraction +
                    ") stvarno vodi u " + actual);
            }
        }

        [Test]
        public void StageJumpsCoverEveryPressureStageExactlyOnce()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var jump in DeveloperSandbox.StageJumps)
                Assert.That(seen.Add(jump.Label), Is.True, "Duplikat faze: " + jump.Label);

            foreach (PressureStage stage in System.Enum.GetValues(typeof(PressureStage)))
                Assert.That(seen.Contains(stage.ToString()), Is.True,
                    "Faza " + stage + " se ne može dohvatiti iz sandboxa.");
        }

        [Test]
        public void DurationChoicesAreAscendingAndUsable()
        {
            var d = DeveloperSandbox.DurationChoices;
            Assert.That(d.Length, Is.GreaterThan(0));
            for (int i = 0; i < d.Length; i++)
            {
                Assert.That(d[i], Is.GreaterThan(0), "Trajanje mora biti pozitivno.");
                if (i > 0) Assert.That(d[i], Is.GreaterThan(d[i - 1]), "Trajanja moraju rasti.");
            }
        }
    }
}
