using System;
using System.Collections.Generic;
using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Tasks;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// Levels 2–3 crowd the flanker display with irrelevant rows above and below.
    /// The point of these tests is that the crowding is visual ONLY: the expected
    /// response, the congruent/incongruent classification and the scoring must be
    /// decided by the centre arrow of the middle row and by nothing else.
    /// </summary>
    public sealed class FlankerDistractorRowTests
    {
        private const int Seed = 20260912;

        private static List<TaskTrialDefinition> Trials(int level, int seed = Seed) =>
            new FlankerTaskDefinition().GenerateTrials(level, seed);

        // ── row structure ────────────────────────────────────────────────

        [Test]
        public void Level1_ProducesASingleRelevantRow()
        {
            foreach (var t in Trials(1))
            {
                Assert.That(FlankerTaskDefinition.Rows(t.stimulus).Length, Is.EqualTo(1),
                    "L1 mora ostati klasičan jedan red: " + t.stimulus);
                Assert.That(t.stimulus.IndexOf(FlankerTaskDefinition.RowSeparator), Is.LessThan(0));
            }
        }

        [TestCase(2)]
        [TestCase(3)]
        public void Levels2And3_AddOneRowAboveAndOneBelow(int level)
        {
            foreach (var t in Trials(level))
            {
                var rows = FlankerTaskDefinition.Rows(t.stimulus);
                Assert.That(rows.Length, Is.EqualTo(3),
                    "L" + level + " mora imati tačno tri reda: " + t.stimulus);
                foreach (var row in rows)
                    Assert.That(row.Length, Is.EqualTo(FlankerTaskDefinition.RowLength));
            }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void RelevantRowIsAlwaysTheMiddleOne(int level)
        {
            foreach (var t in Trials(level))
            {
                var rows = FlankerTaskDefinition.Rows(t.stimulus);
                Assert.That(FlankerTaskDefinition.RelevantRow(t.stimulus),
                    Is.EqualTo(rows[rows.Length / 2]));
            }
        }

        // ── the distractors never decide anything ────────────────────────

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ExpectedResponseComesFromTheCentreOfTheMiddleRow(int level)
        {
            foreach (var t in Trials(level))
            {
                string middle = FlankerTaskDefinition.RelevantRow(t.stimulus);
                char centre = middle[middle.Length / 2];
                var expected = centre == '<' ? SemanticAction.Left : SemanticAction.Right;

                Assert.That(t.expectedAction, Is.EqualTo(expected), t.stimulus);
                Assert.That(FlankerTaskDefinition.CentralDirection(t.stimulus),
                    Is.EqualTo(expected), t.stimulus);
            }
        }

        [TestCase(2)]
        [TestCase(3)]
        public void RewritingTheDistractorRowsChangesNothing(int level)
        {
            foreach (var t in Trials(level))
            {
                string middle = FlankerTaskDefinition.RelevantRow(t.stimulus);

                // The most misleading content available: both irrelevant rows filled
                // with arrows pointing the opposite way to the real answer.
                char opposite = t.expectedAction == SemanticAction.Left ? '>' : '<';
                string hostile = new string(opposite, FlankerTaskDefinition.RowLength);
                string sep = FlankerTaskDefinition.RowSeparator.ToString();
                string tampered = "arr:" + hostile + sep + middle + sep + hostile;

                Assert.That(FlankerTaskDefinition.CentralDirection(tampered),
                    Is.EqualTo(t.expectedAction),
                    "Distraktori ne smiju pomjeriti očekivani odgovor.");
            }
        }

        [TestCase(2)]
        [TestCase(3)]
        public void ScoringIgnoresTheDistractorRows(int level)
        {
            foreach (var t in Trials(level))
            {
                var opposite = t.expectedAction == SemanticAction.Left
                    ? SemanticAction.Right : SemanticAction.Left;

                Assert.That(Run(t, t.expectedAction).Correct, Is.True,
                    "Odgovor po srednjem redu mora biti tačan: " + t.stimulus);
                Assert.That(Run(t, opposite).Correct, Is.False,
                    "Odgovor suprotan srednjem redu mora biti netačan: " + t.stimulus);
            }
        }

        /// <summary>
        /// Drives the real runtime rather than a test-only shortcut: responds as soon
        /// as the trial accepts input and ticks until the trial actually closes, so
        /// the result comes out of the production state machine.
        /// </summary>
        private static TaskTrialResult Run(TaskTrialDefinition trial, SemanticAction action)
        {
            var runtime = new FlankerTaskRuntime(new List<TaskTrialDefinition> { trial });
            runtime.Begin();

            bool submitted = false;
            for (int i = 0; i < 500 && runtime.Results.Count == 0; i++)
            {
                if (!submitted &&
                    (runtime.Phase == TrialPhase.Stimulus || runtime.Phase == TrialPhase.ResponseOpen))
                {
                    runtime.SubmitAction(action);
                    submitted = true;
                }
                runtime.Tick(0.02);
            }

            Assert.That(runtime.Results.Count, Is.GreaterThan(0),
                "Pokušaj se nije zatvorio: " + trial.stimulus);
            return runtime.Results[0];
        }

        [TestCase(2)]
        [TestCase(3)]
        public void CongruencyDescribesTheRelevantRowOnly(int level)
        {
            foreach (var t in Trials(level))
            {
                string middle = FlankerTaskDefinition.RelevantRow(t.stimulus);
                char centre = middle[middle.Length / 2];
                char flank = middle[0];

                string expected = flank == '-' ? "neutral"
                    : flank == centre ? "congruent" : "incongruent";
                Assert.That(t.congruency, Is.EqualTo(expected), t.stimulus);
            }
        }

        // ── the numbers that must not have moved ─────────────────────────

        [TestCase(1, 16, 0.35, 0.15)]
        [TestCase(2, 20, 0.50, 0.10)]
        [TestCase(3, 24, 0.60, 0.00)]
        public void ProportionsAreUnchanged(int level, int count, double incongruent, double neutral)
        {
            var p = TaskDifficultyConfig.Get(TaskType.Flanker, level);
            Assert.That(p.trialCount, Is.EqualTo(count));
            Assert.That(p.incongruentProportion, Is.EqualTo(incongruent).Within(1e-9));
            Assert.That(p.neutralProportion, Is.EqualTo(neutral).Within(1e-9));

            var trials = Trials(level);
            Assert.That(trials.Count, Is.EqualTo(count));
            Assert.That(trials.FindAll(t => t.congruency == "incongruent").Count,
                Is.EqualTo((int)Math.Round(incongruent * count)));
            Assert.That(trials.FindAll(t => t.congruency == "neutral").Count,
                Is.EqualTo((int)Math.Round(neutral * count)));
        }

        [TestCase(1, 1.0, 1.6, 1.0)]
        [TestCase(2, 0.8, 1.3, 0.9)]
        [TestCase(3, 0.7, 1.0, 0.75)]
        public void TimingsAreUnchanged(int level, double stimulus, double window, double iti)
        {
            var p = TaskDifficultyConfig.Get(TaskType.Flanker, level);
            Assert.That(p.stimulusDurationSeconds, Is.EqualTo(stimulus).Within(1e-9));
            Assert.That(p.responseWindowSeconds, Is.EqualTo(window).Within(1e-9));
            Assert.That(p.interTrialIntervalSeconds, Is.EqualTo(iti).Within(1e-9));
        }

        [Test]
        public void DistractorsAreDenserAtLevel3ThanAtLevel2()
        {
            var l2 = TaskDifficultyConfig.Get(TaskType.Flanker, 2);
            var l3 = TaskDifficultyConfig.Get(TaskType.Flanker, 3);
            Assert.That(l2.flankerDistractorRows, Is.EqualTo(2));
            Assert.That(l3.flankerDistractorRows, Is.EqualTo(2));
            Assert.That(l3.flankerDistractorDensity, Is.GreaterThan(l2.flankerDistractorDensity));

            // Per-trial rate, since the two levels do not run the same trial count.
            Assert.That(ArrowsPerTrial(3), Is.GreaterThan(ArrowsPerTrial(2)),
                "L3 mora imati izraženiju distrakciju od L2.");
        }

        private static double ArrowsPerTrial(int level)
        {
            var trials = Trials(level);
            int n = 0;
            foreach (var t in trials)
            {
                var rows = FlankerTaskDefinition.Rows(t.stimulus);
                foreach (char c in rows[0]) if (c == '<' || c == '>') n++;
                foreach (char c in rows[2]) if (c == '<' || c == '>') n++;
            }
            return n / (double)trials.Count;
        }

        // ── determinism ──────────────────────────────────────────────────

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SameSeedProducesIdenticalStimuliIncludingDistractors(int level)
        {
            var a = Trials(level);
            var b = Trials(level);
            Assert.That(a.Count, Is.EqualTo(b.Count));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b[i].stimulus, Is.EqualTo(a[i].stimulus));
                Assert.That(b[i].congruency, Is.EqualTo(a[i].congruency));
                Assert.That(b[i].expectedAction, Is.EqualTo(a[i].expectedAction));
            }
        }

        [TestCase(2)]
        [TestCase(3)]
        public void DifferentSeedsProduceDifferentDistractors(int level)
        {
            var a = Trials(level, 11);
            var b = Trials(level, 12);
            bool anyDifferent = false;
            for (int i = 0; i < a.Count && !anyDifferent; i++)
                if (a[i].stimulus != b[i].stimulus) anyDifferent = true;
            Assert.That(anyDifferent, Is.True, "Seed mora stvarno mijenjati stimuluse.");
        }

        // ── tutorial ─────────────────────────────────────────────────────

        [Test]
        public void Level2TutorialExplainsTheMiddleRowRule()
        {
            string page = TaskInstructions.Page(TaskType.Flanker, 2);
            Assert.That(page, Does.Contain("TRI reda"));
            Assert.That(page, Does.Contain("srednjem redu"));
            Assert.That(page, Does.Contain("ignorisati"));
        }

        [Test]
        public void Level3TutorialStillNamesTheMiddleRow()
        {
            string page = TaskInstructions.Page(TaskType.Flanker, 3);
            Assert.That(page, Does.Contain("srednjem redu"));
        }
    }
}
