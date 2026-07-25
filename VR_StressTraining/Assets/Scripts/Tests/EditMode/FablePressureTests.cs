using System.Collections.Generic;
using NUnit.Framework;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Pressure;
using UnityEngine;

namespace StressTraining.Tests.EditMode
{
    /// <summary>FAZA 7: deterministic timeline, reset safety and finale signaling.</summary>
    public sealed class FablePressureTests
    {
        [Test]
        public void StageFor_Above70_IsStable()
        {
            Assert.That(PressureTimeline.StageFor(0.71), Is.EqualTo(PressureStage.Stable));
            Assert.That(PressureTimeline.StageFor(0.70), Is.EqualTo(PressureStage.Stable));
        }

        [Test]
        public void StageFor_Below70_IsEarly()
        {
            Assert.That(PressureTimeline.StageFor(0.69), Is.EqualTo(PressureStage.Early));
            Assert.That(PressureTimeline.StageFor(0.50), Is.EqualTo(PressureStage.Early));
        }

        [Test]
        public void StageFor_Below50_IsMid()
        {
            Assert.That(PressureTimeline.StageFor(0.49), Is.EqualTo(PressureStage.Mid));
            Assert.That(PressureTimeline.StageFor(0.30), Is.EqualTo(PressureStage.Mid));
        }

        [Test]
        public void StageFor_Below30_IsLate()
        {
            Assert.That(PressureTimeline.StageFor(0.29), Is.EqualTo(PressureStage.Late));
            Assert.That(PressureTimeline.StageFor(0.10), Is.EqualTo(PressureStage.Late));
        }

        [Test]
        public void StageFor_Below10_IsCritical()
        {
            Assert.That(PressureTimeline.StageFor(0.09), Is.EqualTo(PressureStage.Critical));
        }

        [Test]
        public void StageFor_Zero_IsExpired()
        {
            Assert.That(PressureTimeline.StageFor(0.0), Is.EqualTo(PressureStage.Expired));
            Assert.That(PressureTimeline.StageFor(-0.1), Is.EqualTo(PressureStage.Expired));
        }

        [Test]
        public void PressureThresholds_ScaleWithAnyPlanDependentBudget()
        {
            const double duration = 600.0;
            Assert.That(duration * (1.0 - PressureTimeline.EarlyBelow), Is.EqualTo(180.0).Within(0.001));
            Assert.That(duration * (1.0 - PressureTimeline.MidBelow), Is.EqualTo(300.0).Within(0.001));
            Assert.That(duration * (1.0 - PressureTimeline.LateBelow), Is.EqualTo(420.0).Within(0.001));
            Assert.That(duration * (1.0 - PressureTimeline.CriticalBelow), Is.EqualTo(540.0).Within(0.001));
        }

        [Test]
        public void SameSeedAndLevel_ProduceSameEvents()
        {
            var a = new PressureTimeline(123456, 2);
            var b = new PressureTimeline(123456, 2);
            Assert.That(a.Events.Count, Is.EqualTo(b.Events.Count));
            for (int i = 0; i < a.Events.Count; i++)
            {
                Assert.That(a.Events[i].atRemainingFraction,
                    Is.EqualTo(b.Events[i].atRemainingFraction).Within(1e-7));
                Assert.That(a.Events[i].kind, Is.EqualTo(b.Events[i].kind));
                Assert.That(a.Events[i].magnitude,
                    Is.EqualTo(b.Events[i].magnitude).Within(1e-7));
            }
        }

        [Test]
        public void DifferentSeed_ChangesAtLeastOneScheduledEvent()
        {
            var a = new PressureTimeline(111, 2);
            var b = new PressureTimeline(222, 2);
            bool differs = a.Events.Count != b.Events.Count;
            for (int i = 0; !differs && i < a.Events.Count; i++)
            {
                differs = a.Events[i].kind != b.Events[i].kind ||
                          Mathf.Abs(a.Events[i].atRemainingFraction -
                                    b.Events[i].atRemainingFraction) > 1e-6f ||
                          Mathf.Abs(a.Events[i].magnitude - b.Events[i].magnitude) > 1e-6f;
            }
            Assert.That(differs, Is.True);
        }

        [Test]
        public void Events_AreSortedByDescendingRemainingFraction()
        {
            var timeline = new PressureTimeline(42, 3);
            bool hasEarlyRumble = false;
            bool hasMidDust = false;
            bool hasMidCeiling = false;
            for (int i = 0; i < timeline.Events.Count; i++)
            {
                var evt = timeline.Events[i];
                if (i > 0)
                    Assert.That(timeline.Events[i - 1].atRemainingFraction,
                        Is.GreaterThanOrEqualTo(evt.atRemainingFraction));
                if (evt.kind == PressureEventKind.Rumble &&
                    evt.atRemainingFraction > PressureTimeline.MidBelow &&
                    evt.atRemainingFraction < PressureTimeline.EarlyBelow)
                    hasEarlyRumble = true;
                if (evt.kind == PressureEventKind.DustBurst &&
                    evt.atRemainingFraction >= PressureTimeline.LateBelow &&
                    evt.atRemainingFraction < PressureTimeline.MidBelow)
                    hasMidDust = true;
                if (evt.kind == PressureEventKind.CeilingCreak &&
                    evt.atRemainingFraction >= PressureTimeline.LateBelow &&
                    evt.atRemainingFraction < PressureTimeline.MidBelow)
                    hasMidCeiling = true;
            }
            Assert.That(hasEarlyRumble, Is.True);
            Assert.That(hasMidDust, Is.True);
            Assert.That(hasMidCeiling, Is.True);
        }

        [Test]
        public void ResetEventCursor_ReplaysSameEvents()
        {
            var timeline = new PressureTimeline(99, 3);
            var first = new List<PressureEventDefinition>();
            var second = new List<PressureEventDefinition>();
            timeline.ConsumeDueEvents(0.0, first);
            timeline.ResetEventCursor();
            timeline.ConsumeDueEvents(0.0, second);

            Assert.That(second.Count, Is.EqualTo(first.Count));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(second[i].kind, Is.EqualTo(first[i].kind));
                Assert.That(second[i].atRemainingFraction,
                    Is.EqualTo(first[i].atRemainingFraction).Within(1e-7));
                Assert.That(second[i].magnitude,
                    Is.EqualTo(first[i].magnitude).Within(1e-7));
            }
        }

        [Test]
        public void LargeFractionJump_DoesNotLoseDueEvents()
        {
            var timeline = new PressureTimeline(777, 3);
            var due = new List<PressureEventDefinition>();
            int count = timeline.ConsumeDueEvents(0.0, due);
            Assert.That(count, Is.EqualTo(timeline.Events.Count));
            Assert.That(due, Has.Count.EqualTo(timeline.Events.Count));
            Assert.That(timeline.NextEventIndex, Is.EqualTo(timeline.Events.Count));
        }

        [Test]
        public void Intensity_IsMonotonicAsTimeRunsOut()
        {
            var timeline = new PressureTimeline(1, 3);
            float previous = timeline.Intensity(1.0);
            for (int i = 99; i >= 0; i--)
            {
                float current = timeline.Intensity(i / 100.0);
                Assert.That(current, Is.GreaterThanOrEqualTo(previous - 1e-6f),
                    "intensity decreased at remaining fraction " + (i / 100.0));
                previous = current;
            }
        }

        [Test]
        public void Intensity_DoesNotExceedLevelCap()
        {
            for (int level = 1; level <= 3; level++)
            {
                var timeline = new PressureTimeline(10, level);
                for (int i = 100; i >= 0; i--)
                    Assert.That(timeline.Intensity(i / 100.0),
                        Is.LessThanOrEqualTo(timeline.Level.intensityCap + 1e-6f));
            }
        }

        [Test]
        public void GlobalTimer_ArmsAtCorridorEntry_NotAtPreSessionStart()
        {
            var clock = new SessionClock();
            clock.StartSession();
            clock.Tick(25.0); // pre-session active time
            clock.SetGlobalDuration(100.0);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(100.0).Within(1e-6));
            clock.Tick(10.0);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(100.0).Within(1e-6),
                "arming at corridor entry must not consume countdown/transition time");
            clock.SetGlobalChallengeRunning(true);
            clock.Tick(10.0);
            Assert.That(clock.RemainingGlobalSeconds, Is.EqualTo(90.0).Within(1e-6));
        }

        [Test]
        public void PressureLevel_IsClampedToOneThroughThree()
        {
            Assert.That(PressureLevelConfig.Get(-8).level, Is.EqualTo(1));
            Assert.That(PressureLevelConfig.Get(0).level, Is.EqualTo(1));
            Assert.That(PressureLevelConfig.Get(2).level, Is.EqualTo(2));
            Assert.That(PressureLevelConfig.Get(4).level, Is.EqualTo(3));
            Assert.That(PressureLevelConfig.Get(99).level, Is.EqualTo(3));
        }

        [Test]
        public void SegmentReset_RestoresOriginalTransform()
        {
            var go = new GameObject("segment_test");
            try
            {
                go.transform.localPosition = new Vector3(1f, 2f, 3f);
                go.transform.localRotation = Quaternion.Euler(5f, 10f, 15f);
                go.transform.localScale = new Vector3(2f, 3f, 4f);
                var segment = go.AddComponent<PressureSegmentController>();
                segment.CloseSeconds = 0.5f;
                segment.CacheRest();
                segment.BeginClose();
                segment.TickActive(1f);
                Assert.That(go.transform.localPosition.y, Is.Not.EqualTo(2f));

                segment.ResetSegment();
                Assert.That(go.transform.localPosition, Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(Quaternion.Angle(go.transform.localRotation,
                    Quaternion.Euler(5f, 10f, 15f)), Is.LessThan(0.001f));
                Assert.That(go.transform.localScale, Is.EqualTo(new Vector3(2f, 3f, 4f)));
                Assert.That(segment.IsClosing, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CollapseReset_RestoresAllSegments()
        {
            var root = new GameObject("collapse_test");
            var originals = new List<Vector3>();
            var transforms = new List<Transform>();
            try
            {
                var collapse = root.AddComponent<CorridorCollapseController>();
                for (int i = 0; i < 4; i++)
                {
                    var floor = MakeSegment(root.transform, "floor" + i,
                        new Vector3(0f, 0f, i), -1f);
                    var ceiling = MakeSegment(root.transform, "ceiling" + i,
                        new Vector3(0f, 3f, i), 1f);
                    collapse.RegisterPair(floor, ceiling);
                    transforms.Add(floor.transform);
                    transforms.Add(ceiling.transform);
                    originals.Add(floor.transform.localPosition);
                    originals.Add(ceiling.transform.localPosition);
                }

                collapse.SetCollapseProgress(1f);
                collapse.TickActive(10f);
                Assert.That(collapse.ClosedPairCount, Is.EqualTo(3),
                    "one nearest pair must remain safe");
                collapse.ResetAll();

                Assert.That(collapse.ClosedPairCount, Is.Zero);
                for (int i = 0; i < transforms.Count; i++)
                    Assert.That(transforms[i].localPosition, Is.EqualTo(originals[i]));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PressureReset_IsIdempotent()
        {
            var corridor = new GameObject("corridor_test");
            var systems = new GameObject("systems_test");
            try
            {
                var pressure = systems.AddComponent<PressureController>();
                pressure.Initialize(corridor.transform, null, corridor.transform);
                pressure.Begin(123, 2);
                pressure.TickActive(0.1f, 0.25);
                pressure.ResetPressure();
                pressure.ResetPressure();

                Assert.That(pressure.IsRunning, Is.False);
                Assert.That(pressure.CurrentStage, Is.EqualTo(PressureStage.Stable));
                Assert.That(pressure.CurrentIntensity, Is.Zero);
                Assert.That(corridor.transform.Find("PressureRoot").gameObject.activeSelf,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(systems);
                Object.DestroyImmediate(corridor);
            }
        }

        [Test]
        public void NeutralCondition_DoesNotStartPressure()
        {
            Assert.That(PressureController.ShouldRunFor(SessionCondition.Neutral), Is.False);
            Assert.That(PressureController.ShouldRunFor(SessionCondition.Pressure), Is.True);
        }

        [Test]
        public void GameOverCompletion_CannotFinishTwice()
        {
            var root = new GameObject("finale_test");
            var head = new GameObject("head_test");
            try
            {
                var finale = root.AddComponent<GameOverController>();
                finale.FadeSeconds = 0.5f;
                finale.Initialize(head.transform, root.transform);
                int completionCount = 0;
                finale.FinaleComplete += () => completionCount++;

                Assert.That(finale.BeginFinale(), Is.True);
                finale.TickActive(1f);
                finale.TickActive(1f);
                Assert.That(finale.BeginFinale(), Is.False);
                Assert.That(completionCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(head);
            }
        }

        private static PressureSegmentController MakeSegment(Transform parent,
            string name, Vector3 position, float direction)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var segment = go.AddComponent<PressureSegmentController>();
            segment.CloseDirection = direction;
            segment.CloseSeconds = 0.5f;
            segment.CacheRest();
            return segment;
        }
    }
}
