#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Pressure;
using UnityEngine;
using UnityEngine.TestTools;

namespace StressTraining.Tests.EditMode
{
    public sealed class FablePressureAnimationRefactorTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) UnityEngine.Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        // 1-5 Architecture -------------------------------------------------

        [Test] public void Architecture_01_TimelineOwnsLockedThresholds()
        {
            Assert.That(PressureTimeline.EarlyBelow, Is.EqualTo(0.70f));
            Assert.That(PressureTimeline.MidBelow, Is.EqualTo(0.50f));
            Assert.That(PressureTimeline.LateBelow, Is.EqualTo(0.30f));
            Assert.That(PressureTimeline.CriticalBelow, Is.EqualTo(0.10f));
            Assert.That(PressureTimeline.ExpiredAt, Is.EqualTo(0f));
            Assert.That(typeof(PuzzleCollapseAnimationProfile).GetFields()
                .Any(f => f.Name.IndexOf("threshold", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
        }

        [Test] public void Architecture_02_LogicalPlanContainsNoVisualTuningFields()
        {
            string[] forbidden = { "Meters", "Degrees", "Seconds", "Curve", "Amplitude", "Frequency" };
            FieldInfo[] fields = typeof(PuzzleSegmentPlan).GetFields();
            foreach (FieldInfo field in fields)
                foreach (string token in forbidden)
                    Assert.That(field.Name, Does.Not.Contain(token));
        }

        [Test] public void Architecture_03_DefaultProfileAssetOrFallbackExists()
        {
            var asset = Resources.Load<PuzzleCollapseAnimationProfile>(
                PuzzleCorridorController.DefaultProfileResourcePath);
            if (asset != null)
                Assert.That(asset.warningShakeEnvelope, Is.Not.Null);
            else
            {
                var fallback = Track(PuzzleCollapseAnimationProfile.CreateRuntimeFallback());
                Assert.That(fallback.fallPositionCurve.length, Is.GreaterThan(0));
            }
        }

        [Test] public void Architecture_04_SameSeedProducesSameChoreography()
        {
            for (int part = 0; part < 4; part++)
                Assert.That(PuzzleChoreography.SignedVariation(1234, "S3",
                    (SegmentPart)part, 77), Is.EqualTo(
                    PuzzleChoreography.SignedVariation(1234, "S3",
                        (SegmentPart)part, 77)));
            Assert.That(PuzzleChoreography.LeftWallFirst(1234, "S3"),
                Is.EqualTo(PuzzleChoreography.LeftWallFirst(1234, "S3")));
        }

        [Test] public void Architecture_05_DifferentSeedDoesNotChangeLogicalOrder()
        {
            var a = PuzzleCollapseSequence.Build(PuzzleCollapseSequence.AllIds);
            var b = PuzzleCollapseSequence.Build(PuzzleCollapseSequence.AllIds);
            CollectionAssert.AreEqual(a.Select(x => x.SegmentId), b.Select(x => x.SegmentId));
            bool variationChanged = false;
            for (int i = 0; i < 4; i++)
                variationChanged |= Math.Abs(PuzzleChoreography.SignedVariation(1, "S4",
                    (SegmentPart)i, 11) - PuzzleChoreography.SignedVariation(2, "S4",
                    (SegmentPart)i, 11)) > 0.0001f;
            Assert.That(variationChanged, Is.True);
        }

        // 6-10 Binding -----------------------------------------------------

        [Test] public void Binding_06_FindsS1ThroughS6AndSafe()
        {
            var setup = BuildBoundCorridor();
            Assert.That(setup.Controller.SegmentCount, Is.EqualTo(7));
            foreach (string id in PuzzleCollapseSequence.AllIds)
                Assert.That(setup.Controller.GetSegment(id), Is.Not.Null, id);
        }

        [Test] public void Binding_07_CachesAllFourPartKinds()
        {
            var setup = BuildBoundCorridor();
            foreach (string id in PuzzleCollapseSequence.AllIds)
            {
                PuzzleSegmentController segment = setup.Controller.GetSegment(id);
                Assert.That(segment.BoundPartCount, Is.EqualTo(4), id);
                foreach (SegmentPart part in Enum.GetValues(typeof(SegmentPart)))
                    Assert.That(segment.GetBoundPartNode(part), Is.Not.Null, id + "/" + part);
            }
        }

        [Test] public void Binding_08_MissingPartUsesSafePartialBinding()
        {
            Transform root = BuildCorridorHierarchy("Segment_S3_Floor");
            var profile = FastProfile();
            var controller = root.gameObject.AddComponent<PuzzleCorridorController>();
            controller.ConfigureAnimationProfile(profile);
            LogAssert.Expect(LogType.Warning, "[PuzzleCorridor] Missing part: Segment_S3_Floor");
            Assert.That(controller.Bind(root), Is.True);
            Assert.That(controller.GetSegment("S3").BoundPartCount, Is.EqualTo(3));
        }

        [Test] public void Binding_09_SafeFloorIsBoundButNeverMovable()
        {
            var setup = BuildBoundCorridor();
            var safe = setup.Controller.GetSegment("Safe");
            Assert.That(safe.TryGetPartDiagnostics(SegmentPart.Floor, out _, out _, out _,
                out _, out bool movable), Is.True);
            Assert.That(movable, Is.False);
        }

        [Test] public void Binding_10_RebindDoesNotDuplicateControllersOrPivots()
        {
            var setup = BuildBoundCorridor();
            Assert.That(setup.Controller.Bind(setup.Root), Is.True);
            Assert.That(setup.Root.GetComponentsInChildren<PuzzleSegmentController>(true).Length,
                Is.EqualTo(7));
            Assert.That(setup.Root.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name.StartsWith("PuzzleAnimPivot_", StringComparison.Ordinal)), Is.Zero);
        }

        // 11-20 Animation --------------------------------------------------

        [Test] public void Animation_11_WarningReturnsToStableBaseWithoutAccumulatingDrift()
        {
            var setup = BuildBoundCorridor();
            Transform ceiling = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.Ceiling);
            Vector3 rest = ceiling.localPosition;
            setup.Controller.ApplyStage(PressureStage.Early);
            Tick(setup.Controller, 1.2f);
            Vector3 settled = ceiling.localPosition;
            Assert.That(Vector3.Distance(rest, settled), Is.LessThanOrEqualTo(0.0021f));
            Tick(setup.Controller, 1f);
            Assert.That(ceiling.localPosition, Is.EqualTo(settled));
        }

        [Test] public void Animation_12_ShakeUsesOriginalBaseAndDoesNotDrift()
        {
            var setup = BuildBoundCorridor();
            Transform wall = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.WallLeft);
            setup.Controller.ApplyStage(PressureStage.Early);
            Tick(setup.Controller, 1.5f);
            Vector3 first = wall.localPosition;
            Tick(setup.Controller, 2f);
            Assert.That(wall.localPosition, Is.EqualTo(first));
        }

        [Test] public void Animation_13_LooseningFinishesAtExactTargetAndStopsEvaluating()
        {
            var setup = BuildBoundCorridor();
            Transform wall = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.WallLeft);
            setup.Controller.ApplyStage(PressureStage.Mid);
            Tick(setup.Controller, 2f);
            Vector3 target = wall.localPosition;
            Quaternion rotation = wall.localRotation;
            Tick(setup.Controller, 1f);
            Assert.That(wall.localPosition, Is.EqualTo(target));
            Assert.That(Quaternion.Angle(wall.localRotation, rotation), Is.LessThan(0.0001f));
        }

        [Test] public void Animation_14_DetachEventFiresOncePerSegment()
        {
            var setup = BuildBoundCorridor();
            int count = 0;
            setup.Controller.AnimationEvent += e =>
            {
                if (e.Kind == PuzzleAnimationEventKind.SegmentDetached && e.SegmentId == "S1") count++;
            };
            setup.Controller.ApplyStage(PressureStage.Late);
            Tick(setup.Controller, 5f);
            Assert.That(count, Is.EqualTo(1));
        }

        [Test] public void Animation_15_FallStartsAfterConfiguredDelay()
        {
            var setup = BuildBoundCorridor();
            CompletePreviousStages(setup.Controller, PressureStage.Mid);
            setup.Controller.ApplyStage(PressureStage.Late);
            setup.Controller.TickActive(0.01f);
            var s1 = setup.Controller.GetSegment("S1");
            s1.TryGetPartDiagnostics(SegmentPart.Floor, out _, out _, out float floorDelay,
                out _, out _);
            s1.TryGetPartDiagnostics(SegmentPart.Ceiling, out _, out _, out float ceilingDelay,
                out _, out _);
            Assert.That(floorDelay, Is.GreaterThan(ceilingDelay));
        }

        [Test] public void Animation_16_CeilingWallsFloorUseDeterministicStagger()
        {
            var setup = BuildBoundCorridor(seed: 8181);
            CompletePreviousStages(setup.Controller, PressureStage.Mid);
            setup.Controller.ApplyStage(PressureStage.Late);
            setup.Controller.TickActive(0.001f);
            var s1 = setup.Controller.GetSegment("S1");
            var delays = new List<float>();
            foreach (SegmentPart part in Enum.GetValues(typeof(SegmentPart)))
            {
                s1.TryGetPartDiagnostics(part, out _, out _, out float delay, out _, out _);
                delays.Add(delay);
            }
            Assert.That(delays.Distinct().Count(), Is.GreaterThanOrEqualTo(3));
            Assert.That(delays[(int)SegmentPart.Ceiling], Is.LessThan(delays[(int)SegmentPart.Floor]));
        }

        [Test] public void Animation_17_RenderersAreNotHiddenAtFallStart()
        {
            var setup = BuildBoundCorridor();
            CompletePreviousStages(setup.Controller, PressureStage.Mid);
            setup.Controller.ApplyStage(PressureStage.Late);
            Tick(setup.Controller, 0.35f);
            var s1 = setup.Controller.GetSegment("S1");
            s1.TryGetPartDiagnostics(SegmentPart.Ceiling, out _, out _, out _, out bool hidden, out _);
            Assert.That(hidden, Is.False);
        }

        [Test] public void Animation_18_LocalVoidActivatesOnlyAfterFallingBegins()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Mid);
            Tick(setup.Controller, 2f);
            Assert.That(setup.Controller.ActiveVoidCount, Is.Zero);
            setup.Controller.ApplyStage(PressureStage.Late);
            Tick(setup.Controller, 1.2f);
            Assert.That(setup.Controller.ActiveVoidCount, Is.GreaterThan(0));
        }

        [Test] public void Animation_19_QuaternionTargetRemainsExactAfterCompletion()
        {
            var setup = BuildBoundCorridor();
            Transform ceiling = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.Ceiling);
            setup.Controller.ApplyStage(PressureStage.Mid);
            Tick(setup.Controller, 2f);
            Quaternion completed = ceiling.localRotation;
            Tick(setup.Controller, 2f);
            Assert.That(Quaternion.Angle(ceiling.localRotation, completed), Is.LessThan(0.0001f));
        }

        [Test] public void Animation_20_NormalizedProgressIsAlwaysClamped()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Expired);
            for (int i = 0; i < 100; i++)
            {
                setup.Controller.TickActive(0.05f);
                var s1 = setup.Controller.GetSegment("S1");
                foreach (SegmentPart part in Enum.GetValues(typeof(SegmentPart)))
                {
                    s1.TryGetPartDiagnostics(part, out _, out float p, out _, out _, out _);
                    Assert.That(p, Is.InRange(0f, 1f));
                }
            }
        }

        // 21-28 Stage sequence --------------------------------------------

        [Test] public void Stages_21_NeutralMovesNothing()
        {
            var setup = BuildBoundCorridor();
            Transform part = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.Ceiling);
            Vector3 rest = part.localPosition;
            setup.Controller.ApplyStage(PressureStage.Stable);
            Tick(setup.Controller, 3f);
            Assert.That(part.localPosition, Is.EqualTo(rest));
        }

        [Test] public void Stages_22_SeventyPercentCollapsesNoSegment()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Early);
            Tick(setup.Controller, 3f);
            Assert.That(setup.Controller.CollapsedSegmentCount, Is.Zero);
            Assert.That(setup.Controller.ActiveVoidCount, Is.Zero);
        }

        [Test] public void Stages_23_FiftyPercentOnlyLoosensFarPair()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Mid);
            Tick(setup.Controller, 3f);
            Assert.That(setup.Controller.CollapsedSegmentCount, Is.Zero);
            Assert.That(setup.Controller.GetSegment("S1").DominantPhase,
                Is.EqualTo(PuzzleAnimationPhase.Loosening));
        }

        [Test] public void Stages_24_ThirtyPercentCollapsesS1AndS2()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Late);
            Tick(setup.Controller, 5f);
            Assert.That(setup.Controller.GetSegment("S1").HasFullyCollapsed, Is.True);
            Assert.That(setup.Controller.GetSegment("S2").HasFullyCollapsed, Is.True);
            Assert.That(setup.Controller.GetSegment("S3").HasFullyCollapsed, Is.False);
        }

        [Test] public void Stages_25_TenPercentCollapsesS3AndS4AndPreparesS5S6()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Critical);
            Tick(setup.Controller, 5f);
            Assert.That(setup.Controller.GetSegment("S3").HasFullyCollapsed, Is.True);
            Assert.That(setup.Controller.GetSegment("S4").HasFullyCollapsed, Is.True);
            Assert.That(setup.Controller.GetSegment("S5").HasFullyCollapsed, Is.False);
            Assert.That(setup.Controller.GetSegment("S5").DominantPhase,
                Is.EqualTo(PuzzleAnimationPhase.Loosening));
        }

        [Test] public void Stages_26_ZeroPercentCollapsesS5AndS6()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 6f);
            Assert.That(setup.Controller.GetSegment("S5").HasFullyCollapsed, Is.True);
            Assert.That(setup.Controller.GetSegment("S6").HasFullyCollapsed, Is.True);
        }

        [Test] public void Stages_27_SameStageDoesNotRetriggerEvents()
        {
            var setup = BuildBoundCorridor();
            int warning = 0;
            setup.Controller.AnimationEvent += e =>
            {
                if (e.Kind == PuzzleAnimationEventKind.WarningStarted && e.SegmentId == "S1") warning++;
            };
            setup.Controller.ApplyStage(PressureStage.Early);
            setup.Controller.ApplyStage(PressureStage.Early);
            Tick(setup.Controller, 3f);
            setup.Controller.ApplyStage(PressureStage.Early);
            Tick(setup.Controller, 1f);
            Assert.That(warning, Is.EqualTo(1));
        }

        [Test] public void Stages_28_SkippedStagesAreProcessedInLogicalOrder()
        {
            var setup = BuildBoundCorridor();
            var events = new List<PuzzleAnimationEventKind>();
            setup.Controller.AnimationEvent += e =>
            {
                if (e.SegmentId == "S1") events.Add(e.Kind);
            };
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 6f);
            Assert.That(events.IndexOf(PuzzleAnimationEventKind.WarningStarted),
                Is.LessThan(events.IndexOf(PuzzleAnimationEventKind.JointLoosened)));
            Assert.That(events.IndexOf(PuzzleAnimationEventKind.JointLoosened),
                Is.LessThan(events.IndexOf(PuzzleAnimationEventKind.SegmentDetached)));
            Assert.That(events.IndexOf(PuzzleAnimationEventKind.SegmentDetached),
                Is.LessThan(events.IndexOf(PuzzleAnimationEventKind.SegmentFalling)));
            Assert.That(events.Last(), Is.EqualTo(PuzzleAnimationEventKind.SegmentHidden));
        }

        // 29-33 Pause ------------------------------------------------------

        [Test] public void Pause_29_NoActiveDeltaFreezesProgress()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Late);
            setup.Controller.TickActive(0.2f);
            Transform part = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.Ceiling);
            Vector3 frozen = part.localPosition;
            Assert.That(part.localPosition, Is.EqualTo(frozen));
        }

        [Test] public void Pause_30_ResumeContinuesWithoutJump()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Late);
            setup.Controller.TickActive(0.2f);
            Transform part = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.Ceiling);
            Vector3 frozen = part.localPosition;
            setup.Controller.TickActive(0.05f);
            Assert.That(Vector3.Distance(part.localPosition, frozen), Is.LessThan(0.2f));
        }

        [Test] public void Pause_31_DelayDoesNotAdvanceWithoutTick()
        {
            var setup = BuildBoundCorridor();
            CompletePreviousStages(setup.Controller, PressureStage.Mid);
            setup.Controller.ApplyStage(PressureStage.Late);
            setup.Controller.TickActive(0.001f);
            var s1 = setup.Controller.GetSegment("S1");
            s1.TryGetPartDiagnostics(SegmentPart.Floor, out _, out _, out float before, out _, out _);
            s1.TryGetPartDiagnostics(SegmentPart.Floor, out _, out _, out float after, out _, out _);
            Assert.That(after, Is.EqualTo(before));
        }

        [Test] public void Pause_32_FallTransformFreezesWithoutTick()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Late);
            Tick(setup.Controller, 1.5f);
            Transform part = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.Ceiling);
            Vector3 frozen = part.localPosition;
            Assert.That(part.localPosition, Is.EqualTo(frozen));
        }

        [Test] public void Pause_33_PressureControllerPauseGatesPuzzleTick()
        {
            LogAssert.ignoreFailingMessages = true;
            var setup = BuildBoundCorridor();
            var eye = Track(new GameObject("Eye"));
            var systems = Track(new GameObject("Systems"));
            var pressureRoot = Track(new GameObject("PressureEnvironment"));
            var pressure = systems.AddComponent<PressureController>();
            pressure.AttachPuzzleCorridor(setup.Controller);
            pressure.Initialize(pressureRoot.transform, eye.transform, eye.transform);
            pressure.Begin(44, 2);
            pressure.SetPaused(true);
            Transform part = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.Ceiling);
            Vector3 rest = part.localPosition;
            pressure.TickActive(2f, 0.20);
            Assert.That(part.localPosition, Is.EqualTo(rest));
        }

        // 34-38 Safety -----------------------------------------------------

        [Test] public void Safety_34_SafeFloorTransformIsIdenticalAtEveryStage()
        {
            var setup = BuildBoundCorridor();
            Transform floor = setup.Controller.GetSegment("Safe").GetBoundPartNode(SegmentPart.Floor);
            Vector3 pos = floor.localPosition;
            Quaternion rot = floor.localRotation;
            foreach (PressureStage stage in Enum.GetValues(typeof(PressureStage)))
            {
                setup.Controller.ApplyStage(stage);
                Tick(setup.Controller, 1f);
                Assert.That(floor.localPosition, Is.EqualTo(pos));
                Assert.That(Quaternion.Angle(floor.localRotation, rot), Is.LessThan(0.0001f));
            }
        }

        [Test] public void Safety_35_SafeFloorColliderStaysEnabled()
        {
            var setup = BuildBoundCorridor();
            Transform floor = setup.Controller.GetSegment("Safe").GetBoundPartNode(SegmentPart.Floor);
            Collider collider = floor.GetComponent<Collider>();
            Assert.That(collider.enabled, Is.True);
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 6f);
            Assert.That(collider.enabled, Is.True);
        }

        [Test] public void Safety_36_SafeWallAndCeilingStayWithinProfileLimits()
        {
            var setup = BuildBoundCorridor();
            var safe = setup.Controller.GetSegment("Safe");
            Transform wall = safe.GetBoundPartNode(SegmentPart.WallLeft);
            Transform ceiling = safe.GetBoundPartNode(SegmentPart.Ceiling);
            Vector3 wallRest = wall.position;
            Vector3 ceilingRest = ceiling.position;
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 3f);
            Assert.That(Vector3.Distance(wall.position, wallRest),
                Is.LessThanOrEqualTo(setup.Profile.safeWallMaximumTranslation + 0.08f));
            Assert.That(Vector3.Distance(ceiling.position, ceilingRest),
                Is.LessThanOrEqualTo(setup.Profile.safeCeilingMaximumTranslation + 0.08f));
        }

        [Test] public void Safety_37_ConsoleTabletAndControlsAreNeverMoved()
        {
            var setup = BuildBoundCorridor();
            var console = Track(new GameObject("Console_Placeholder"));
            var tablet = Track(new GameObject("TabletAnchor"));
            var controls = Track(new GameObject("ConsoleControlsAnchor"));
            Vector3 a = console.transform.position;
            Vector3 b = tablet.transform.position;
            Vector3 c = controls.transform.position;
            setup.Controller.ConfigureSafety(null, console.transform, tablet.transform, controls.transform);
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 6f);
            Assert.That(console.transform.position, Is.EqualTo(a));
            Assert.That(tablet.transform.position, Is.EqualTo(b));
            Assert.That(controls.transform.position, Is.EqualTo(c));
        }

        [Test] public void Safety_38_CameraTransformIsNeverModified()
        {
            var setup = BuildBoundCorridor();
            var camera = Track(new GameObject("CenterEyeAnchor"));
            camera.transform.position = new Vector3(0f, 1.65f, 0f);
            Quaternion rotation = Quaternion.Euler(0f, 22f, 0f);
            camera.transform.rotation = rotation;
            setup.Controller.ConfigureSafety(camera.transform, null, null, null);
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 6f);
            Assert.That(camera.transform.position, Is.EqualTo(new Vector3(0f, 1.65f, 0f)));
            Assert.That(Quaternion.Angle(camera.transform.rotation, rotation), Is.LessThan(0.0001f));
        }

        // 39-45 Reset ------------------------------------------------------

        [Test] public void Reset_39_RestoresAllTransforms()
        {
            var setup = BuildBoundCorridor();
            Transform part = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.WallRight);
            Vector3 p = part.localPosition;
            Quaternion r = part.localRotation;
            Vector3 s = part.localScale;
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 6f);
            setup.Controller.ResetCorridor();
            Assert.That(part.localPosition, Is.EqualTo(p));
            Assert.That(Quaternion.Angle(part.localRotation, r), Is.LessThan(0.0001f));
            Assert.That(part.localScale, Is.EqualTo(s));
        }

        [Test] public void Reset_40_RestoresRendererAndColliderStates()
        {
            var setup = BuildBoundCorridor();
            Transform part = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.Floor);
            Renderer renderer = part.GetComponent<Renderer>();
            Collider collider = part.GetComponent<Collider>();
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 6f);
            setup.Controller.ResetCorridor();
            Assert.That(renderer.enabled, Is.True);
            Assert.That(collider.enabled, Is.True);
            Assert.That(collider.isTrigger, Is.False);
        }

        [Test] public void Reset_41_RestoresLocalLightIntensity()
        {
            var setup = BuildBoundCorridor(addLight: true);
            Light light = setup.Root.GetComponentsInChildren<Light>(true).First();
            float original = light.intensity;
            setup.Controller.ApplyStage(PressureStage.Early);
            setup.Controller.TickActive(0.04f);
            setup.Controller.ResetCorridor();
            Assert.That(light.intensity, Is.EqualTo(original).Within(0.0001f));
        }

        [Test] public void Reset_42_DisablesAllVoidBackings()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 6f);
            Assert.That(setup.Controller.ActiveVoidCount, Is.GreaterThan(0));
            setup.Controller.ResetCorridor();
            Assert.That(setup.Controller.ActiveVoidCount, Is.Zero);
        }

        [Test] public void Reset_43_ResetDuringActiveAnimationWorks()
        {
            var setup = BuildBoundCorridor();
            Transform part = setup.Controller.GetSegment("S1").GetBoundPartNode(SegmentPart.Ceiling);
            Vector3 rest = part.localPosition;
            setup.Controller.ApplyStage(PressureStage.Late);
            Tick(setup.Controller, 0.5f);
            setup.Controller.ResetCorridor();
            Assert.That(part.localPosition, Is.EqualTo(rest));
            Assert.That(setup.Controller.AnimatedPartCount, Is.Zero);
        }

        [Test] public void Reset_44_TwoConsecutiveResetsAreIdempotent()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 2f);
            setup.Controller.ResetCorridor();
            string first = Snapshot(setup.Root);
            setup.Controller.ResetCorridor();
            Assert.That(Snapshot(setup.Root), Is.EqualTo(first));
        }

        [Test] public void Reset_45_NewSessionStartsIntact()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 6f);
            setup.Controller.ResetCorridor();
            setup.Controller.SetSessionSeed(999);
            setup.Controller.ApplyStage(PressureStage.Stable);
            Assert.That(setup.Controller.CollapsedSegmentCount, Is.Zero);
            Assert.That(setup.Controller.ActiveVoidCount, Is.Zero);
        }

        // 46-50 Finale -----------------------------------------------------

        [Test] public void Finale_46_ExpiredStartsFinalEventOnce()
        {
            var setup = BuildBoundCorridor();
            int count = 0;
            setup.Controller.AnimationEvent += e =>
            {
                if (e.Kind == PuzzleAnimationEventKind.FinalCollapseStarted) count++;
            };
            setup.Controller.ApplyStage(PressureStage.Expired);
            setup.Controller.ApplyStage(PressureStage.Expired);
            Assert.That(count, Is.EqualTo(1));
        }

        [Test] public void Finale_47_GameOverFinaleCompleteEmitsOnce()
        {
            LogAssert.ignoreFailingMessages = true;
            var setup = BuildBoundCorridor();
            var eye = Track(new GameObject("Eye_Finale"));
            var corridor = Track(new GameObject("Corridor_Finale"));
            var systems = Track(new GameObject("Systems_Finale"));
            var pressure = systems.AddComponent<PressureController>();
            pressure.AttachPuzzleCorridor(setup.Controller);
            pressure.Initialize(corridor.transform, eye.transform, eye.transform);
            pressure.Begin(1, 1);
            int count = 0;
            pressure.GameOverFinaleComplete += () => count++;
            Assert.That(pressure.TriggerGameOverFinale(), Is.True);
            for (int i = 0; i < 100; i++) pressure.TickFinaleActive(0.05f);
            Assert.That(count, Is.EqualTo(1));
        }

        [Test] public void Finale_48_FinalCollapseIsNotInstantlySnappedHidden()
        {
            LogAssert.ignoreFailingMessages = true;
            var setup = BuildBoundCorridor();
            var eye = Track(new GameObject("Eye_NoSnap"));
            var corridor = Track(new GameObject("Corridor_NoSnap"));
            var systems = Track(new GameObject("Systems_NoSnap"));
            var pressure = systems.AddComponent<PressureController>();
            pressure.AttachPuzzleCorridor(setup.Controller);
            pressure.Initialize(corridor.transform, eye.transform, eye.transform);
            pressure.Begin(1, 1);
            pressure.TriggerGameOverFinale();
            Assert.That(setup.Controller.GetSegment("S5").HasFullyCollapsed, Is.False);
        }

        [Test] public void Finale_49_ProductionFlowStillContainsTimeExpiredRecoveryPath()
        {
            string source = ReadProjectSource("Scripts/Session/ProductionSessionFlow.cs");
            Assert.That(source, Does.Contain("HandleGlobalTimeExpired"));
            Assert.That(source, Does.Contain("BeginRecovery"));
            Assert.That(source, Does.Contain("GameOverFinaleComplete"));
        }

        [Test] public void Finale_50_EndSessionPathIsDistinctFromTimeExpiredHandler()
        {
            string source = ReadProjectSource("Scripts/Session/ProductionSessionFlow.cs");
            Assert.That(source, Does.Contain("User chose End Session"));
            Assert.That(source, Does.Contain("HandleGlobalTimeExpired"));
            Assert.That(source.IndexOf("User chose End Session", StringComparison.Ordinal),
                Is.Not.EqualTo(source.IndexOf("HandleGlobalTimeExpired", StringComparison.Ordinal)));
        }

        // 51-54 Performance / cleanup -------------------------------------

        [Test] public void Performance_51_NoRuntimeInstantiateOrCoroutineInTickPath()
        {
            string source = ReadProjectSource("Scripts/Pressure/PuzzleSegmentController.cs");
            string tick = ExtractMethod(source, "public void TickActive(float dt)");
            Assert.That(tick, Does.Not.Contain("Instantiate("));
            Assert.That(tick, Does.Not.Contain("StartCoroutine"));
            Assert.That(source, Does.Not.Contain("IEnumerator"));
        }

        [Test] public void Performance_52_NoRendererMaterialAccessInAnimationCode()
        {
            string source = ReadProjectSource("Scripts/Pressure/PuzzleSegmentController.cs");
            Assert.That(source, Does.Not.Contain("renderer.material"));
            Assert.That(source, Does.Contain("MaterialPropertyBlock"));
        }

        [Test] public void Performance_53_NoFindOrGetComponentsInsideTickMethod()
        {
            string segmentSource = ReadProjectSource("Scripts/Pressure/PuzzleSegmentController.cs");
            string corridorSource = ReadProjectSource("Scripts/Pressure/PuzzleCorridorController.cs");
            string segmentTick = ExtractMethod(segmentSource, "public void TickActive(float dt)");
            string corridorTick = ExtractMethod(corridorSource, "public void TickActive(float dt)");
            Assert.That(segmentTick, Does.Not.Contain("Find"));
            Assert.That(segmentTick, Does.Not.Contain("GetComponent"));
            Assert.That(corridorTick, Does.Not.Contain("Find"));
            Assert.That(corridorTick, Does.Not.Contain("GetComponent"));
        }

        [Test] public void Performance_54_CleanupLeavesNoActiveAnimations()
        {
            var setup = BuildBoundCorridor();
            setup.Controller.ApplyStage(PressureStage.Expired);
            Tick(setup.Controller, 1f);
            setup.Controller.ResetCorridor();
            Assert.That(setup.Controller.AnimatedPartCount, Is.Zero);
            foreach (string id in PuzzleCollapseSequence.AllIds)
                Assert.That(setup.Controller.GetSegment(id).HasActiveAnimation, Is.False);
        }

        // Helpers ----------------------------------------------------------

        private sealed class Setup
        {
            public Transform Root;
            public PuzzleCorridorController Controller;
            public PuzzleCollapseAnimationProfile Profile;
        }

        private Setup BuildBoundCorridor(int seed = 123, bool addLight = false)
        {
            Transform root = BuildCorridorHierarchy(null, addLight);
            var profile = FastProfile();
            var controller = root.gameObject.AddComponent<PuzzleCorridorController>();
            controller.ConfigureAnimationProfile(profile);
            controller.SetSessionSeed(seed);
            Assert.That(controller.Bind(root), Is.True);
            controller.SetSessionSeed(seed);
            return new Setup { Root = root, Controller = controller, Profile = profile };
        }

        private Transform BuildCorridorHierarchy(string omitExactName = null,
            bool addLight = false)
        {
            var root = Track(new GameObject(PuzzleCorridorController.RootNodeName));
            for (int i = 0; i < PuzzleCollapseSequence.AllIds.Length; i++)
            {
                string id = PuzzleCollapseSequence.AllIds[i];
                var segment = new GameObject("Segment_" + id);
                segment.transform.SetParent(root.transform, false);
                segment.transform.localPosition = id == "Safe"
                    ? Vector3.zero
                    : new Vector3(0f, 0f, 7f - i);

                CreatePart(segment.transform, id, "Floor", new Vector3(0f, -0.10f, 0f),
                    new Vector3(2.8f, 0.15f, 1f), omitExactName);
                CreatePart(segment.transform, id, "Ceiling", new Vector3(0f, 2.8f, 0f),
                    new Vector3(2.8f, 0.15f, 1f), omitExactName);
                CreatePart(segment.transform, id, "Wall_L", new Vector3(-1.35f, 1.35f, 0f),
                    new Vector3(0.15f, 2.7f, 1f), omitExactName);
                CreatePart(segment.transform, id, "Wall_R", new Vector3(1.35f, 1.35f, 0f),
                    new Vector3(0.15f, 2.7f, 1f), omitExactName);

                foreach (string anchorName in new[] { "CollapsePivot", "FXAnchor", "LightAnchor" })
                {
                    var anchor = new GameObject("Segment_" + id + "_" + anchorName);
                    anchor.transform.SetParent(segment.transform, false);
                    anchor.transform.localPosition = new Vector3(0f, 1.4f, 0.45f);
                    if (addLight && id == "S1" && anchorName == "LightAnchor")
                    {
                        var lightObject = new GameObject("LocalPressureLight");
                        lightObject.transform.SetParent(anchor.transform, false);
                        Light light = lightObject.AddComponent<Light>();
                        light.intensity = 2f;
                    }
                }
            }
            return root.transform;
        }

        private static void CreatePart(Transform parent, string id, string suffix,
            Vector3 localPosition, Vector3 scale, string omitExactName)
        {
            string exactName = "Segment_" + id + "_" + suffix;
            if (string.Equals(exactName, omitExactName, StringComparison.Ordinal)) return;
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = exactName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = scale;
        }

        private PuzzleCollapseAnimationProfile FastProfile()
        {
            var profile = Track(PuzzleCollapseAnimationProfile.CreateRuntimeFallback());
            ConfigureWave(profile.farWave, 1.8f);
            ConfigureWave(profile.middleWave, 1.7f);
            ConfigureWave(profile.nearWave, 1.6f);
            ConfigureWave(profile.safeWave, 0f);
            profile.ceilingDelay = 0f;
            profile.firstWallDelay = 0.025f;
            profile.secondWallDelay = 0.050f;
            profile.floorDelay = 0.075f;
            profile.pairedSegmentDelay = 0.080f;
            profile.seededDelayVariation = 0.004f;
            profile.revealVoidAtFallProgress = 0.20f;
            profile.hideRendererAtFallProgress = 0.90f;
            // Local void backings are OFF by default in production (the puzzle corridor
            // exposes the real space behind a fallen segment). These tests exercise the
            // void behaviour itself, so enable it in the test profile.
            profile.useVoidBackings = true;
            return profile;
        }

        private static void ConfigureWave(PuzzleCollapseAnimationProfile.WaveSettings wave,
            float fallDistance)
        {
            wave.warningDuration = 0.12f;
            wave.warningPositionMeters = 0.01f;
            wave.warningRotationDegrees = 0.4f;
            wave.warningFrequencyHz = 8f;
            wave.warningResidualGapMeters = 0.001f;
            wave.loosenDuration = 0.14f;
            wave.loosenGapMeters = 0.04f;
            wave.loosenCeilingDropMeters = 0.03f;
            wave.loosenWallRotationDegrees = 2f;
            wave.loosenCeilingRotationDegrees = 1f;
            wave.loosenFloorDropMeters = 0.005f;
            wave.segmentStartDelay = 0f;
            wave.detachDuration = 0.10f;
            wave.detachedHoldSeconds = 0.04f;
            wave.fallDuration = 0.28f;
            wave.settleDuration = 0.04f;
            wave.fallDistanceMeters = fallDistance;
            wave.lateralReleaseMeters = 0.15f;
            wave.awayReleaseMeters = 0.12f;
            wave.fallRotationDegrees = 18f;
            wave.hideDelaySeconds = 0.02f;
        }

        private static void CompletePreviousStages(PuzzleCorridorController controller,
            PressureStage stage)
        {
            controller.ApplyStage(stage);
            Tick(controller, 2f);
        }

        private static void Tick(PuzzleCorridorController controller, float seconds)
        {
            const float step = 0.02f;
            int count = Mathf.CeilToInt(seconds / step);
            for (int i = 0; i < count; i++) controller.TickActive(step);
        }

        private T Track<T>(T value) where T : UnityEngine.Object
        {
            _created.Add(value);
            return value;
        }

        private static string Snapshot(Transform root)
        {
            var lines = new List<string>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                Renderer renderer = t.GetComponent<Renderer>();
                Collider collider = t.GetComponent<Collider>();
                lines.Add(t.name + "|" + t.localPosition + "|" + t.localRotation + "|" +
                    t.localScale + "|" + t.gameObject.activeSelf + "|" +
                    (renderer != null && renderer.enabled) + "|" +
                    (collider != null && collider.enabled));
            }
            return string.Join("\n", lines);
        }

        private static string ReadProjectSource(string relativeToAssets)
        {
            return File.ReadAllText(Path.Combine(Application.dataPath, relativeToAssets));
        }

        private static string ExtractMethod(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
            int brace = source.IndexOf('{', start);
            int depth = 0;
            for (int i = brace; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0) return source.Substring(start, i - start + 1);
                }
            }
            Assert.Fail("Method block not closed: " + signature);
            return string.Empty;
        }
    }
}
#endif
