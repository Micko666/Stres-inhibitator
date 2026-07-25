using System;
using System.Collections.Generic;
using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Tasks;

namespace StressTraining.Tests.EditMode
{
    /// <summary>FAZA 3: Corsi-inspired task — generator, runtime, first-error rule.</summary>
    public sealed class FableCorsiTests
    {
        private static TaskLevelParameters L(int level) =>
            TaskDifficultyConfig.Get(TaskType.CorsiSequence, level);

        // ── generator ────────────────────────────────────────────────────

        [Test]
        public void Generator_IsDeterministic_WithNineValidDistinctPositions()
        {
            var a = CorsiTaskDefinition.Generate(L(1), 999);
            var b = CorsiTaskDefinition.Generate(L(1), 999);
            Assert.That(a.Count, Is.EqualTo(b.Count).And.GreaterThan(0));

            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].stimulus, Is.EqualTo(b[i].stimulus), "same seed → same sequences");
                var seq = CorsiTaskDefinition.DecodeSequence(a[i].stimulus);
                Assert.That(seq, Has.Count.GreaterThanOrEqualTo(L(1).corsiMinSequenceLength));
                Assert.That(seq, Has.Count.LessThanOrEqualTo(L(1).corsiMaxSequenceLength));
                foreach (int p in seq)
                    Assert.That(p, Is.InRange(0, CorsiLayout.PositionCount - 1));
                Assert.That(new HashSet<int>(seq), Has.Count.EqualTo(seq.Count),
                    "positions never repeat within one sequence");
            }
        }

        [Test]
        public void Layout_IsVersionedFixedSevenByThree_WithNineUniqueActiveSlots()
        {
            Assert.That(CorsiLayout.GridColumns, Is.EqualTo(7));
            Assert.That(CorsiLayout.GridRows, Is.EqualTo(3));
            Assert.That(CorsiLayout.GridSlotCount, Is.EqualTo(21));
            Assert.That(CorsiLayout.CorsiLayoutVersion, Is.EqualTo(1));
            Assert.That(CorsiLayout.ActiveSlots, Has.Count.EqualTo(9));
            Assert.That(new HashSet<int>(CorsiLayout.ActiveSlots), Has.Count.EqualTo(9));
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 13, 8, 10, 7, 17, 12 },
                CorsiLayout.ActiveSlots, "layout version 1 must remain byte-for-byte fixed");

            foreach (int slot in CorsiLayout.ActiveSlots)
                Assert.That(slot, Is.InRange(0, CorsiLayout.GridSlotCount - 1));
        }

        [Test]
        public void Layout_IsSeedIndependent_AndConsoleGridIsRotated180FromTablet()
        {
            var before = new List<int>(CorsiLayout.ActiveSlots);
            CorsiTaskDefinition.Generate(L(1), 11);
            CorsiTaskDefinition.Generate(L(3), 987654);
            CollectionAssert.AreEqual(before, CorsiLayout.ActiveSlots,
                "task seed must never alter the physical/tablet mapping");

            // The PHYSICAL Corsi grid is rotated 180° about Y relative to the tablet
            // map, because the console's local +X is the participant's LEFT (spawn
            // faces −Z, console is at identity rotation) while the tablet canvas
            // faces the participant so tablet +X is their RIGHT. A 180° rotation
            // negates BOTH axes:
            //     console.x = −normalized.x   and   console.z = −normalized.y
            // Negating only ONE axis is a MIRROR, not a rotation — it silently swaps
            // left/right (or near/far). That regression is what this test locks out.
            for (int i = 0; i < CorsiLayout.PositionCount; i++)
            {
                int slot = CorsiLayout.GetSlotIdForCorsiIndex(i);
                Assert.That(CorsiLayout.GetGridColumn(i), Is.EqualTo(slot % 7));
                Assert.That(CorsiLayout.GetGridRow(i), Is.EqualTo(slot / 7));

                var normalized = CorsiLayout.GetNormalizedPosition(i);
                var tablet = CorsiLayout.GetTabletPosition(i, 100f, 60f);
                var console = CorsiLayout.GetConsoleLocalPosition(i, 0.5f, 0.3f);

                Assert.That(tablet.x / 50f, Is.EqualTo(normalized.x).Within(1e-6));
                Assert.That(tablet.y / 30f, Is.EqualTo(normalized.y).Within(1e-6));

                Assert.That(console.x / 0.5f, Is.EqualTo(-normalized.x).Within(1e-6),
                    $"CORSI_{i}: console X must be the 180°-rotated (negated) tablet X");
                Assert.That(console.z / 0.3f, Is.EqualTo(-normalized.y).Within(1e-6),
                    $"CORSI_{i}: console Z must be the 180°-rotated (negated) tablet Y");

                // Both axes must be rotated the SAME way — never one alone.
                Assert.That(console.y, Is.EqualTo(0f).Within(1e-6),
                    $"CORSI_{i} must stay on the console surface plane");
            }
        }

        /// <summary>
        /// The full nine-position table, expressed in PARTICIPANT-FACING terms.
        /// Signs: tablet +x = participant right, tablet +y = top of the map.
        /// Console +x = participant LEFT, console −z = FAR from the participant.
        /// So tablet-left/top must land on console +x/−z, for every position.
        /// </summary>
        [Test]
        public void Layout_AllNinePositions_MatchTabletAndConsoleFromParticipantView()
        {
            for (int i = 0; i < CorsiLayout.PositionCount; i++)
            {
                var tablet = CorsiLayout.GetTabletPosition(i, 400f, 130f);
                var console = CorsiLayout.GetConsoleLocalPosition(i, 0.28f, 0.10f);

                // participant-left on the tablet (x<0) ⇒ participant-left button (console x>0)
                Assert.That(Math.Sign(console.x), Is.EqualTo(-Math.Sign(tablet.x)),
                    $"CORSI_{i}: left/right side must agree from the participant's view");
                // top of the tablet (y>0) ⇒ far edge of the console (z<0)
                Assert.That(Math.Sign(console.z), Is.EqualTo(-Math.Sign(tablet.y)),
                    $"CORSI_{i}: near/far must agree from the participant's view");
            }
        }

        [Test]
        public void Layout_CentralPosition_StaysCentralAndIsCorsi5()
        {
            // The rotation's fixed point: grid column 3 of 7, middle row.
            var normalized = CorsiLayout.GetNormalizedPosition(5);
            Assert.That(normalized.x, Is.EqualTo(0f).Within(1e-6));
            Assert.That(normalized.y, Is.EqualTo(0f).Within(1e-6));

            var console = CorsiLayout.GetConsoleLocalPosition(5, 0.28f, 0.10f);
            var tablet = CorsiLayout.GetTabletPosition(5, 400f, 130f);
            Assert.That(console, Is.EqualTo(UnityEngine.Vector3.zero));
            Assert.That(tablet, Is.EqualTo(UnityEngine.Vector2.zero));

            for (int i = 0; i < CorsiLayout.PositionCount; i++)
                if (i != 5)
                    Assert.That(CorsiLayout.GetNormalizedPosition(i),
                        Is.Not.EqualTo(UnityEngine.Vector2.zero),
                        $"CORSI_{i} must not also sit at the centre");
        }

        [Test]
        public void Layout_RotationIsAppliedExactlyOnce_NotTwice()
        {
            // Applying the rotation twice would return to the identity mapping
            // (console.x == tablet.x). Locking the single application prevents a
            // future "fix" from stacking a second flip on top of this one.
            for (int i = 0; i < CorsiLayout.PositionCount; i++)
            {
                var normalized = CorsiLayout.GetNormalizedPosition(i);
                var console = CorsiLayout.GetConsoleLocalPosition(i, 1f, 1f);
                if (Math.Abs(normalized.x) > 1e-6)
                    Assert.That(console.x, Is.Not.EqualTo(normalized.x).Within(1e-6),
                        $"CORSI_{i}: X looks un-rotated (or rotated twice)");
                if (Math.Abs(normalized.y) > 1e-6)
                    Assert.That(console.z, Is.Not.EqualTo(normalized.y).Within(1e-6),
                        $"CORSI_{i}: Z looks un-rotated (or rotated twice)");
            }
        }

        [Test]
        public void Layout_SemanticIndices_AreUnchangedByTheRotation()
        {
            // The fix must be geometric only — CORSI_n on the tablet stays CORSI_n
            // on the console, and the action/slot mapping is untouched.
            CollectionAssert.AreEqual(new[] { 1, 2, 4, 13, 8, 10, 7, 17, 12 },
                CorsiLayout.ActiveSlots);
            for (int i = 0; i < CorsiLayout.PositionCount; i++)
            {
                Assert.That(CorsiLayout.ControlId(i), Is.EqualTo("CORSI_" + i));
                Assert.That(CorsiLayout.IndexOf(CorsiLayout.ActionFor(i)), Is.EqualTo(i));
            }
        }

        [Test]
        public void DifficultyLevels_KeepLockedForwardForwardBackwardModel()
        {
            TaskLevelParameters level1 = L(1);
            TaskLevelParameters level2 = L(2);
            TaskLevelParameters level3 = L(3);

            Assert.That(level1.corsiBackward, Is.False);
            Assert.That(level2.corsiBackward, Is.False);
            Assert.That(level3.corsiBackward, Is.True);
            Assert.That(level2.corsiMinSequenceLength, Is.GreaterThan(level1.corsiMinSequenceLength));
            Assert.That(level2.corsiPresentationStepSeconds,
                Is.LessThan(level1.corsiPresentationStepSeconds));
        }

        [Test]
        public void Layout_ActionMapping_RoundTrips()
        {
            for (int i = 0; i < 9; i++)
            {
                var action = CorsiLayout.ActionFor(i);
                Assert.That(CorsiLayout.IndexOf(action), Is.EqualTo(i));
                Assert.That(CorsiLayout.ControlId(i), Is.EqualTo("CORSI_" + i));
            }
            Assert.That(CorsiLayout.IndexOf(SemanticAction.Go), Is.EqualTo(-1));
        }

        // ── runtime helpers ──────────────────────────────────────────────

        private static CorsiTaskRuntime NewRuntime(out List<int> firstSequence,
            bool backward = false, int seed = 42)
        {
            var timing = L(backward ? 3 : 1);
            var trials = CorsiTaskDefinition.Generate(timing, seed);
            firstSequence = CorsiTaskDefinition.DecodeSequence(trials[0].stimulus);
            return new CorsiTaskRuntime(trials, timing);
        }

        private static void TickUntil(CorsiTaskRuntime runtime, Func<bool> condition,
            double maxSeconds = 60)
        {
            double elapsed = 0;
            while (!condition() && elapsed < maxSeconds)
            {
                runtime.Tick(0.05);
                elapsed += 0.05;
            }
            Assert.That(condition(), Is.True, $"condition not reached within {maxSeconds}s of ticking");
        }

        private static void TickToResponsePhase(CorsiTaskRuntime runtime)
        {
            bool open = false;
            runtime.ResponsePhaseStarted += () => open = true;
            TickUntil(runtime, () => open || runtime.Phase == TrialPhase.ResponseOpen);
        }

        // ── runtime behavior ─────────────────────────────────────────────

        [Test]
        public void ForwardReproduction_CompletesTrialAsCorrect()
        {
            var runtime = NewRuntime(out var sequence);
            runtime.Begin();
            TickToResponsePhase(runtime);

            foreach (int pos in sequence)
                runtime.SubmitAction(CorsiLayout.ActionFor(pos));

            Assert.That(runtime.Results, Has.Count.EqualTo(1));
            var r = runtime.Results[0];
            Assert.That(r.Correct, Is.True);
            Assert.That(r.Corsi.CorrectlyReproducedCount, Is.EqualTo(sequence.Count));
            Assert.That(r.Corsi.FirstErrorIndex, Is.EqualTo(-1));
            Assert.That(r.Corsi.RequiredResponseOrder, Is.EqualTo(sequence), "forward = presented order");
        }

        [Test]
        public void BackwardLevel_RequiresReversedOrder()
        {
            var runtime = NewRuntime(out var sequence, backward: true);
            Assert.That(runtime.Backward, Is.True, "level 3 is backward reproduction");
            runtime.Begin();
            TickToResponsePhase(runtime);

            var reversed = new List<int>(sequence);
            reversed.Reverse();
            foreach (int pos in reversed)
                runtime.SubmitAction(CorsiLayout.ActionFor(pos));

            var r = runtime.Results[0];
            Assert.That(r.Correct, Is.True);
            Assert.That(r.Corsi.RequiredResponseOrder, Is.EqualTo(reversed));
            Assert.That(r.Corsi.Backward, Is.True);
        }

        [Test]
        public void FirstWrongPosition_EndsAttemptImmediately()
        {
            var runtime = NewRuntime(out var sequence);
            runtime.Begin();
            TickToResponsePhase(runtime);

            runtime.SubmitAction(CorsiLayout.ActionFor(sequence[0]));       // correct
            int wrong = Wrong(sequence, 1);                                 // any position ≠ expected
            runtime.SubmitAction(CorsiLayout.ActionFor(wrong));             // wrong → ends

            Assert.That(runtime.Results, Has.Count.EqualTo(1), "attempt ends on first error");
            var r = runtime.Results[0];
            Assert.That(r.Correct, Is.False);
            Assert.That(r.Corsi.CorrectlyReproducedCount, Is.EqualTo(1));
            Assert.That(r.Corsi.FirstErrorIndex, Is.EqualTo(1));
            Assert.That(r.Corsi.FirstWrongButtonId, Is.EqualTo(CorsiLayout.ControlId(wrong)));
            Assert.That(r.Corsi.ResponsePrefix, Has.Count.EqualTo(2),
                "no further presses are recorded after the first error");
        }

        private static int Wrong(List<int> sequence, int step)
        {
            for (int candidate = 0; candidate < 9; candidate++)
                if (candidate != sequence[step]) return candidate;
            throw new InvalidOperationException();
        }

        [Test]
        public void Input_DuringPresentation_IsRejected()
        {
            var runtime = NewRuntime(out var sequence);
            runtime.Begin();
            // advance into presentation (Stimulus phase) but NOT into response
            TickUntil(runtime, () => runtime.Phase == TrialPhase.Stimulus, 10);

            runtime.SubmitAction(CorsiLayout.ActionFor(sequence[0]));
            Assert.That(runtime.Results, Is.Empty, "presses during presentation must be ignored");

            TickToResponsePhase(runtime);
            foreach (int pos in sequence) runtime.SubmitAction(CorsiLayout.ActionFor(pos));
            Assert.That(runtime.Results[0].Correct, Is.True,
                "ignored presentation press must not count as first response");
            Assert.That(runtime.Results[0].Corsi.ResponsePrefix, Has.Count.EqualTo(sequence.Count));
        }

        [Test]
        public void SlowResponse_BeyondLegacyWindow_CanStillBeCorrect()
        {
            var timing = L(1);
            var trials = CorsiTaskDefinition.Generate(timing, 42);
            var sequence = CorsiTaskDefinition.DecodeSequence(trials[0].stimulus);
            var runtime = new CorsiTaskRuntime(trials, timing);
            runtime.Begin();
            TickToResponsePhase(runtime);

            // Wait beyond the former short scoring response window but remain
            // below the independent 30-second inactivity safety guard.
            runtime.Tick(trials[0].responseWindowSeconds + 1.0);
            foreach (int pos in sequence)
                runtime.SubmitAction(CorsiLayout.ActionFor(pos));

            Assert.That(runtime.Results, Has.Count.EqualTo(1));
            Assert.That(runtime.Results[0].Correct, Is.True);
            Assert.That(runtime.Results[0].Corsi.TimedOut, Is.False);
            Assert.That(runtime.Results[0].Corsi.AbandonedByInactivity, Is.False);
        }

        [Test]
        public void InactivitySafetyTimeout_IsAbandoned_NotScoringMiss()
        {
            var runtime = NewRuntime(out _);
            runtime.Begin();
            TickToResponsePhase(runtime);
            TickUntil(runtime, () => runtime.Results.Count == 1, 120);

            var r = runtime.Results[0];
            Assert.That(r.Correct, Is.False);
            Assert.That(r.Missed, Is.False);
            Assert.That(r.Corsi.TimedOut, Is.False);
            Assert.That(r.Corsi.AbandonedByInactivity, Is.True);
        }

        [Test]
        public void PauseDuringPresentation_RestartsSameTrial()
        {
            var runtime = NewRuntime(out var sequence);
            runtime.Begin();
            TickUntil(runtime, () => runtime.Phase == TrialPhase.Stimulus, 10);

            runtime.OnPauseInterrupt();
            Assert.That(runtime.Phase, Is.EqualTo(TrialPhase.InterTrialInterval),
                "pause discards the partial presentation");
            Assert.That(runtime.CurrentTrialIndex, Is.Zero, "the SAME trial repeats");

            TickToResponsePhase(runtime);
            foreach (int pos in sequence) runtime.SubmitAction(CorsiLayout.ActionFor(pos));
            Assert.That(runtime.Results[0].Correct, Is.True);
            Assert.That(runtime.Results[0].WasInterruptedByPause, Is.True);
        }

        [Test]
        public void PauseDuringResponse_DiscardsPartialAnswer()
        {
            var runtime = NewRuntime(out var sequence);
            runtime.Begin();
            TickToResponsePhase(runtime);
            runtime.SubmitAction(CorsiLayout.ActionFor(sequence[0]));

            runtime.OnPauseInterrupt();
            Assert.That(runtime.Results, Is.Empty, "partial response is discarded, not scored");

            TickToResponsePhase(runtime);
            foreach (int pos in sequence) runtime.SubmitAction(CorsiLayout.ActionFor(pos));
            Assert.That(runtime.Results[0].Correct, Is.True,
                "no double registration after the pause restart");
        }


        [Test]
        public void Reset_RemovesPreviousResponsePrefix()
        {
            var runtime = NewRuntime(out var sequence);
            runtime.Begin();
            TickToResponsePhase(runtime);
            runtime.SubmitAction(CorsiLayout.ActionFor(sequence[0]));

            runtime.Reset();
            runtime.Begin();
            TickToResponsePhase(runtime);
            foreach (int pos in sequence)
                runtime.SubmitAction(CorsiLayout.ActionFor(pos));

            Assert.That(runtime.Results, Has.Count.EqualTo(1));
            Assert.That(runtime.Results[0].Correct, Is.True);
            Assert.That(runtime.Results[0].Corsi.ResponsePrefix, Is.EqualTo(sequence),
                "reset must not retain the old response prefix");
        }

        [Test]
        public void AbortAndReset_LeaveCleanState()
        {
            var runtime = NewRuntime(out _);
            bool aborted = false;
            runtime.BlockAborted += () => aborted = true;
            runtime.Begin();
            TickUntil(runtime, () => runtime.Phase == TrialPhase.Stimulus, 10);

            runtime.Abort();
            Assert.That(aborted, Is.True);
            Assert.That(runtime.Phase, Is.EqualTo(TrialPhase.Aborted));

            runtime.Reset();
            Assert.That(runtime.Phase, Is.EqualTo(TrialPhase.Idle));
            Assert.That(runtime.Results, Is.Empty);
        }

        [Test]
        public void Block_CompletesAllTrials_AndRaisesBlockFinished()
        {
            var timing = L(1);
            var trials = CorsiTaskDefinition.Generate(timing, 7);
            var runtime = new CorsiTaskRuntime(trials, timing);
            bool finished = false;
            runtime.BlockFinished += () => finished = true;
            runtime.Begin();

            // answer every trial correctly as its response phase opens
            runtime.ResponsePhaseStarted += () =>
            {
                var seq = CorsiTaskDefinition.DecodeSequence(runtime.CurrentTrial.stimulus);
                foreach (int pos in seq) runtime.SubmitAction(CorsiLayout.ActionFor(pos));
            };
            TickUntil(runtime, () => finished, 300);

            Assert.That(runtime.Results, Has.Count.EqualTo(trials.Count));
            foreach (var r in runtime.Results) Assert.That(r.Correct, Is.True);
        }
    }
}
