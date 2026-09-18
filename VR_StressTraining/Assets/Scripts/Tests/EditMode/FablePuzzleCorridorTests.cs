#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StressTraining.Data;
using StressTraining.Pressure;
using UnityEngine;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// Modular puzzle corridor: deterministic segment discovery, the fixed collapse
    /// schedule, the safe segment, stage-driven motion, pause/reset, and the
    /// PressureController hand-off from procedural slabs to the puzzle model.
    /// </summary>
    public sealed class FablePuzzleCorridorTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        // ── id parsing + schedule (pure C#) ──────────────────────────────

        [Test]
        public void ParseId_ExtractsSegmentIdFromRealFbxNodeNames()
        {
            Assert.That(PuzzleCollapseSequence.ParseId("Segment_S3"), Is.EqualTo("S3"));
            Assert.That(PuzzleCollapseSequence.ParseId("Segment_S3_Wall_L"), Is.EqualTo("S3"));
            Assert.That(PuzzleCollapseSequence.ParseId("Segment_Safe"), Is.EqualTo("Safe"));
            Assert.That(PuzzleCollapseSequence.ParseId("Segment_Safe_Floor_Mesh"), Is.EqualTo("Safe"));
            Assert.That(PuzzleCollapseSequence.ParseId("Corridor_Modular_Puzzle_ROOT"), Is.Null);
        }

        [Test]
        public void Schedule_IsDeterministic_AndOrderedFarToNear()
        {
            var a = PuzzleCollapseSequence.Build(PuzzleCollapseSequence.AllIds);
            var b = PuzzleCollapseSequence.Build(PuzzleCollapseSequence.AllIds.Reverse().ToList());

            // Same segment set ⇒ identical schedule regardless of discovery order.
            CollectionAssert.AreEqual(a.Select(p => p.SegmentId), b.Select(p => p.SegmentId));

            var collapse = a.Where(p => !p.IsSafe).ToList();
            CollectionAssert.AreEqual(new[] { "S1", "S2", "S3", "S4", "S5", "S6" },
                collapse.Select(p => p.SegmentId), "S1 (farthest) collapses first");
            for (int i = 1; i < collapse.Count; i++)
                Assert.That(collapse[i].CollapseOrder, Is.GreaterThan(collapse[i - 1].CollapseOrder));
        }

        [Test]
        public void Safe_IsAlwaysLast_NeverFalls_AndKeepsItsFloor()
        {
            var plans = PuzzleCollapseSequence.Build(PuzzleCollapseSequence.AllIds);
            var safe = plans.Single(p => p.IsSafe);

            Assert.That(plans.Last().IsSafe, Is.True, "safe segment sorts last");
            Assert.That(safe.CanPhysicallyFall, Is.False);
            Assert.That(safe.DetachStage, Is.EqualTo(PressureStage.Stable), "never detaches");
            Assert.That(safe.VanishStage, Is.EqualTo(PressureStage.Stable), "never vanishes");

            // The safe floor is not a movable part — the ground can never be removed.
            var movable = PuzzleCollapseSequence.MovableParts(isSafe: true).ToList();
            Assert.That(movable, Has.No.Member(SegmentPart.Floor));
            Assert.That(PuzzleCollapseSequence.MovableParts(isSafe: false).ToList(),
                Has.Member(SegmentPart.Floor));
        }

        [Test]
        public void Thresholds_TravelTowardTheUser_AcrossTheFiveStages()
        {
            var plans = PuzzleCollapseSequence.Build(PuzzleCollapseSequence.AllIds)
                .Where(p => !p.IsSafe).ToList();

            // Nothing falls at 70% (Early) — only shudder on the far pair.
            Assert.That(plans.Where(p => p.DetachStage == PressureStage.Early), Is.Empty,
                "no segment may fall as early as 70 %");
            Assert.That(plans.First(p => p.SegmentId == "S1").DamageStage,
                Is.EqualTo(PressureStage.Early), "the farthest segment shudders first, at 70 %");

            // The far pair falls by Late (30 %); the near pair only at Expired (0 %).
            Assert.That(plans.First(p => p.SegmentId == "S1").DetachStage, Is.EqualTo(PressureStage.Late));
            Assert.That(plans.First(p => p.SegmentId == "S6").DetachStage, Is.EqualTo(PressureStage.Expired));

            foreach (var p in plans)
            {
                Assert.That((int)p.DamageStage, Is.LessThanOrEqualTo((int)p.DetachStage),
                    p.SegmentId + ": must shudder no later than it detaches");
                Assert.That((int)p.DetachStage, Is.LessThanOrEqualTo((int)p.VanishStage),
                    p.SegmentId + ": must detach no later than it vanishes");
            }
        }

        [Test]
        public void Build_IgnoresUnknownIds_AndToleratesMissingSegments()
        {
            var plans = PuzzleCollapseSequence.Build(new[] { "S1", "S2", "Safe", "BOGUS" });
            CollectionAssert.AreEqual(new[] { "S1", "S2", "Safe" }, plans.Select(p => p.SegmentId));
        }

        // ── discovery + runtime on a synthetic hierarchy ─────────────────

        private Transform BuildFakeCorridor()
        {
            // Mirrors the real FBX: ROOT → Segment_<id> → part nodes (+ mesh child).
            // Segments are spaced along Z and given REAL cube meshes so renderer
            // bounds (used by the ground/footprint check) are non-degenerate and
            // do not all collapse onto the origin.
            var root = new GameObject(PuzzleCorridorController.RootNodeName);
            _created.Add(root);

            string[] ids = PuzzleCollapseSequence.AllIds;
            for (int s = 0; s < ids.Length; s++)
            {
                string id = ids[s];
                var seg = new GameObject("Segment_" + id);
                seg.transform.SetParent(root.transform, false);
                seg.transform.localPosition = new Vector3(0f, 0f, s * 2f);   // distinct Z
                foreach (string part in new[] { "Floor", "Ceiling", "Wall_L", "Wall_R" })
                {
                    var partNode = new GameObject("Segment_" + id + "_" + part);
                    partNode.transform.SetParent(seg.transform, false);
                    partNode.transform.localPosition = new Vector3(0f, part == "Ceiling" ? 2.8f : 0f, 0f);

                    var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    mesh.name = "Segment_" + id + "_" + part + "_Mesh";
                    var col = mesh.GetComponent<Collider>();
                    if (col != null) Object.DestroyImmediate(col);
                    mesh.transform.SetParent(partNode.transform, false);
                    mesh.transform.localScale = new Vector3(2.8f, 0.2f, 1.6f);
                }
                // decoy anchors that must NOT be treated as segment roots
                foreach (string anchor in new[] { "CollapsePivot", "FXAnchor", "LightAnchor" })
                {
                    var a = new GameObject("Segment_" + id + "_" + anchor);
                    a.transform.SetParent(seg.transform, false);
                }
            }
            return root.transform;
        }

        [Test]
        public void Controller_DiscoversAllSevenSegments_AndTheSafeSegment()
        {
            var root = BuildFakeCorridor();
            var controller = root.gameObject.AddComponent<PuzzleCorridorController>();

            Assert.That(controller.Bind(root), Is.True);
            Assert.That(controller.SegmentCount, Is.EqualTo(7));
            Assert.That(controller.HasSafeSegment, Is.True);
        }

        [Test]
        public void Bind_ReturnsFalse_WhenNoSegmentsExist_ForCleanFallback()
        {
            var empty = new GameObject("EmptyRoot");
            _created.Add(empty);
            var controller = empty.AddComponent<PuzzleCorridorController>();
            Assert.That(controller.Bind(empty.transform), Is.False);
            Assert.That(controller.IsBound, Is.False);
        }

        [Test]
        public void Neutral_DoesNotMoveAnySegment()
        {
            var root = BuildFakeCorridor();
            var controller = root.gameObject.AddComponent<PuzzleCorridorController>();
            controller.Bind(root);

            var s1Ceiling = FindDeep(root, "Segment_S1_Ceiling");
            Vector3 rest = s1Ceiling.localPosition;

            // Stable stage (neutral) + plenty of ticks must not move anything.
            controller.ApplyStage(PressureStage.Stable);
            for (int i = 0; i < 30; i++) controller.TickActive(0.1f);

            Assert.That(s1Ceiling.localPosition, Is.EqualTo(rest));
        }

        [Test]
        public void ControlledPressure_MovesFarSegmentsFirst_ThenNear()
        {
            var root = BuildFakeCorridor();
            var controller = root.gameObject.AddComponent<PuzzleCorridorController>();
            controller.Bind(root);

            var s1Ceiling = FindDeep(root, "Segment_S1_Ceiling");
            var s6Ceiling = FindDeep(root, "Segment_S6_Ceiling");
            Vector3 s1Rest = s1Ceiling.localPosition;
            Vector3 s6Rest = s6Ceiling.localPosition;

            // At Late (30 %) S1 has fallen; S6 has not.
            controller.ApplyStage(PressureStage.Late);
            for (int i = 0; i < 30; i++) controller.TickActive(0.1f);
            Assert.That(s1Ceiling.localPosition, Is.Not.EqualTo(s1Rest), "far segment falls by 30 %");

            // S6 only moves at Expired (0 %).
            controller.ApplyStage(PressureStage.Expired);
            for (int i = 0; i < 30; i++) controller.TickActive(0.1f);
            Assert.That(s6Ceiling.localPosition, Is.Not.EqualTo(s6Rest), "near segment falls at 0 %");
        }

        [Test]
        public void SafeSegment_FloorNeverMoves_EvenAtExpired()
        {
            var root = BuildFakeCorridor();
            var controller = root.gameObject.AddComponent<PuzzleCorridorController>();
            controller.Bind(root);

            var safeFloor = FindDeep(root, "Segment_Safe_Floor");
            Vector3 rest = safeFloor.localPosition;

            controller.ApplyStage(PressureStage.Expired);
            for (int i = 0; i < 40; i++) controller.TickActive(0.1f);

            Assert.That(safeFloor.localPosition, Is.EqualTo(rest),
                "the participant must never lose the floor under the XR origin");
        }

        [Test]
        public void Pause_FreezesAnimation_AndResumeContinues()
        {
            var root = BuildFakeCorridor();
            var controller = root.gameObject.AddComponent<PuzzleCorridorController>();
            controller.Bind(root);
            var s1Ceiling = FindDeep(root, "Segment_S1_Ceiling");

            controller.ApplyStage(PressureStage.Late);
            controller.TickActive(0.2f);
            Vector3 mid = s1Ceiling.localPosition;

            // "Pause" = the caller simply stops feeding time. Position must hold.
            for (int i = 0; i < 10; i++) { /* no TickActive */ }
            Assert.That(s1Ceiling.localPosition, Is.EqualTo(mid), "frozen while no active time is fed");

            controller.TickActive(0.2f);
            Assert.That(s1Ceiling.localPosition, Is.Not.EqualTo(mid), "resumes where it froze");
        }

        [Test]
        public void Reset_RestoresEverySegmentAndRenderer()
        {
            var root = BuildFakeCorridor();
            var controller = root.gameObject.AddComponent<PuzzleCorridorController>();
            controller.Bind(root);

            var restById = new Dictionary<string, Vector3>();
            foreach (string id in PuzzleCollapseSequence.AllIds)
                restById[id] = FindDeep(root, "Segment_" + id + "_Ceiling").localPosition;

            controller.ApplyStage(PressureStage.Expired);
            for (int i = 0; i < 50; i++) controller.TickActive(0.1f);

            controller.ResetCorridor();

            foreach (string id in PuzzleCollapseSequence.AllIds)
                Assert.That(FindDeep(root, "Segment_" + id + "_Ceiling").localPosition,
                    Is.EqualTo(restById[id]), id + " must return to rest for the next session");

            // Hidden renderers must be re-enabled by reset.
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                Assert.That(r.enabled, Is.True, "reset re-enables every vanished mesh");
        }

        [Test]
        public void Segment_DoesNotRetriggerOnceCollapsed()
        {
            var root = BuildFakeCorridor();
            var controller = root.gameObject.AddComponent<PuzzleCorridorController>();
            controller.Bind(root);
            var s1Ceiling = FindDeep(root, "Segment_S1_Ceiling");

            controller.ApplyStage(PressureStage.Expired);
            for (int i = 0; i < 40; i++) controller.TickActive(0.1f);
            Vector3 settled = s1Ceiling.localPosition;

            // Further ticks at the same stage must not move it again.
            for (int i = 0; i < 20; i++) controller.TickActive(0.1f);
            Assert.That(s1Ceiling.localPosition, Is.EqualTo(settled),
                "a fully collapsed segment does not animate a second time");
        }

        // ── PressureController hand-off ──────────────────────────────────

        [Test]
        public void PressureController_PrefersPuzzle_OverProceduralSlabs()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var root = BuildFakeCorridor();
            var puzzle = root.gameObject.AddComponent<PuzzleCorridorController>();
            puzzle.Bind(root);

            var corridor = new GameObject("Corridor_ForPressure");
            _created.Add(corridor);
            var eye = new GameObject("Eye_ForPressure");
            _created.Add(eye);
            var pressure = new GameObject("Systems_ForPressure").AddComponent<PressureController>();
            _created.Add(pressure.gameObject);

            pressure.AttachPuzzleCorridor(puzzle);
            pressure.Initialize(corridor.transform, eye.transform);

            Assert.That(pressure.PuzzleActive, Is.True);
            Assert.That(pressure.PuzzleSegmentCount, Is.EqualTo(7));
            Assert.That(pressure.SegmentPairCount, Is.Zero,
                "procedural collapse slabs must not be built when the puzzle is present");
            Assert.That(pressure.ProceduralBlockoutActive, Is.False);
        }

        [Test]
        public void PressureController_FallsBackToProcedural_WhenNoPuzzle()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var corridor = new GameObject("Corridor_NoPuzzle");
            _created.Add(corridor);
            var eye = new GameObject("Eye_NoPuzzle");
            _created.Add(eye);
            var pressure = new GameObject("Systems_NoPuzzle").AddComponent<PressureController>();
            _created.Add(pressure.gameObject);

            pressure.Initialize(corridor.transform, eye.transform);

            Assert.That(pressure.PuzzleActive, Is.False);
            Assert.That(pressure.SegmentPairCount, Is.EqualTo(PressureHeuristics.SegmentPairCount),
                "the procedural blockout is the development fallback");
        }

        // ── developer collapse preview (5 min) ───────────────────────────

        [Test]
        public void Preview_IsTwoMinutes()
        {
            Assert.That(StressTraining.Session.SessionCoordinator.PreviewSeconds,
                Is.EqualTo(120.0), "the collapse preview budget is 2 minutes");
        }

        [Test]
        public void Preview_DrivesTheSameThresholdsAsTheRealSession()
        {
            // The preview only swaps the BUDGET; the remaining-fraction it feeds the
            // pressure system must cross 70/50/30/10/0 at the same fractions, so what
            // it shows is what a production ControlledPressure session does.
            double total = StressTraining.Session.SessionCoordinator.PreviewSeconds;
            var f = new System.Func<double, double>(remaining =>
                StressTraining.Session.SessionCoordinator.PreviewRemainingFraction(remaining, total));

            Assert.That(PressureTimeline.StageFor(f(120)), Is.EqualTo(PressureStage.Stable));
            Assert.That(PressureTimeline.StageFor(f(83)), Is.EqualTo(PressureStage.Early));    // <70 % (84 s)
            Assert.That(PressureTimeline.StageFor(f(59)), Is.EqualTo(PressureStage.Mid));      // <50 % (60 s)
            Assert.That(PressureTimeline.StageFor(f(35)), Is.EqualTo(PressureStage.Late));     // <30 % (36 s)
            Assert.That(PressureTimeline.StageFor(f(11)), Is.EqualTo(PressureStage.Critical)); // <10 % (12 s)
            Assert.That(PressureTimeline.StageFor(f(0)), Is.EqualTo(PressureStage.Expired));
        }

        [Test]
        public void Preview_RemainingFraction_IsClampedAndSafe()
        {
            Assert.That(StressTraining.Session.SessionCoordinator.PreviewRemainingFraction(600, 300),
                Is.EqualTo(1.0), "never above 1");
            Assert.That(StressTraining.Session.SessionCoordinator.PreviewRemainingFraction(-5, 300),
                Is.EqualTo(0.0), "never below 0");
            Assert.That(StressTraining.Session.SessionCoordinator.PreviewRemainingFraction(10, 0),
                Is.EqualTo(0.0), "a zero budget must not divide by zero");
        }

        // ── fall / loss ──────────────────────────────────────────────────

        [Test]
        public void Fall_DropStep_NeverExceedsTheHardCap()
        {
            // Accelerating fall, but the total drop must never pass maxDistance —
            // "don't fall forever". Simulate frames and accumulate.
            const float max = 3f;
            float dropped = 0f;
            for (float t = 0f; t < 10f; t += 0.05f)
            {
                float step = StressTraining.Session.FallDecision.DropStep(t, 0.05f, dropped, max);
                Assert.That(step, Is.GreaterThanOrEqualTo(0f));
                dropped += step;
                Assert.That(dropped, Is.LessThanOrEqualTo(max + 1e-4f),
                    "the fall must stop at the cap, never continue past it");
            }
            Assert.That(dropped, Is.EqualTo(max).Within(1e-2f), "it reaches the cap");
        }

        [Test]
        public void Fall_DropStep_Accelerates()
        {
            // v = g·t, so a later frame of equal dt moves further (until the cap).
            float early = StressTraining.Session.FallDecision.DropStep(0.1f, 0.02f, 0f, 100f);
            float later = StressTraining.Session.FallDecision.DropStep(0.5f, 0.02f, 0f, 100f);
            Assert.That(later, Is.GreaterThan(early), "gravity accelerates the drop");
        }

        [Test]
        public void Fall_ComfortFade_IsShort_AndDebounced()
        {
            Assert.That(StressTraining.Session.FallDecision.FadeSeconds, Is.LessThanOrEqualTo(0.5f),
                "the comfort fade is brief");
            Assert.That(StressTraining.Session.FallDecision.MinLossSeconds, Is.GreaterThan(0f),
                "a loss can never be committed instantly (debounce)");
        }

        [Test]
        public void Arm_HasSettleGrace_SoTheDeferredTeleportCanLand()
        {
            // The Meta locomotor teleport is applied a frame late; without a grace the
            // floor check runs while the rig is still outside every corridor footprint
            // and ejects the player the instant they enter. The grace must comfortably
            // cover at least a frame or two.
            Assert.That(StressTraining.Session.FallDecision.ArmSettleSeconds,
                Is.GreaterThanOrEqualTo(0.1f), "arming must tolerate a deferred teleport");
        }

        [Test]
        public void Loss_HoldsBeforeReturningToSafe()
        {
            // The whole point of the delay: a loss must NOT snap away instantly. The
            // consequence lingers for a configurable hold before the return teleport.
            var dev = new StressTraining.Core.DeveloperConfig();
            Assert.That(dev.lossConsequenceHoldSeconds, Is.GreaterThan(0.5f),
                "the consequence must be held long enough to be felt");
        }

        [Test]
        public void Devirt_FlickersEarly_ThenSettlesToSolidWhiteout()
        {
            // Early on it blinks (digital breakup); by the end it is a steady white-out.
            bool sawOff = false;
            for (float t = 0f; t < StressTraining.Session.FallDecision.DevirtSeconds * 0.5f; t += 0.01f)
                if (!StressTraining.Session.FallDecision.DevirtVisible(t)) sawOff = true;
            Assert.That(sawOff, Is.True, "the de-render flickers on/off early");

            Assert.That(StressTraining.Session.FallDecision.DevirtVisible(
                    StressTraining.Session.FallDecision.DevirtSeconds), Is.True,
                "it is solid at the end");
            Assert.That(StressTraining.Session.FallDecision.DevirtWhiteout01(0f),
                Is.EqualTo(0f).Within(1e-4f), "starts cyan, not white");
            Assert.That(StressTraining.Session.FallDecision.DevirtWhiteout01(
                    StressTraining.Session.FallDecision.DevirtSeconds),
                Is.EqualTo(1f).Within(1e-4f), "ends fully white");
        }

        [Test]
        public void Station_DropsAtExpired_HoldsBeforeThen_RestoresOnReset()
        {
            var parent = new GameObject("StationParent");
            _created.Add(parent);
            var floor = new GameObject("Segment_Safe_Floor").transform;
            floor.SetParent(parent.transform, false);
            floor.localPosition = new Vector3(0f, 1f, 0f);
            Vector3 rest = floor.localPosition;

            var station = parent.AddComponent<StationCollapse>();
            station.AddTarget(floor, 0f, 8f, 0f);
            Assert.That(station.PieceCount, Is.EqualTo(1));

            // Before Expired: no movement even if ticked (the corridor still stands).
            station.ApplyStage(PressureStage.Late);
            station.TickActive(0.5f);
            Assert.That(floor.localPosition, Is.EqualTo(rest), "station holds until the finale");

            // At Expired it drops straight down; after the fall it is well below rest.
            station.ApplyStage(PressureStage.Expired);
            for (int i = 0; i < 60; i++) station.TickActive(0.05f);   // 3 s
            Assert.That(floor.localPosition.y, Is.LessThan(rest.y - 7f), "the platform drops away");
            Assert.That(floor.localPosition.x, Is.EqualTo(rest.x).Within(1e-3f), "straight down only");

            // Reset restores the exact rest pose.
            station.ResetStation();
            Assert.That(floor.localPosition, Is.EqualTo(rest));
        }

        [Test]
        public void HasFloorAt_SafeStandsSolid_CollapseBecomesHole()
        {
            var root = BuildFakeCorridor();
            var controller = root.gameObject.AddComponent<PuzzleCorridorController>();
            controller.Bind(root);

            // Position over the Safe segment floor and over an S1 floor.
            var safeFloor = FindDeep(root, "Segment_Safe_Floor");
            var s1Floor = FindDeep(root, "Segment_S1_Floor");
            Vector3 overSafe = safeFloor.position;
            Vector3 overS1 = s1Floor.position;

            Assert.That(controller.HasFloorAt(overSafe), Is.True, "safe floor is always solid");
            Assert.That(controller.HasFloorAt(overS1), Is.True, "S1 floor solid before collapse");

            // Collapse everything; S1 floor detaches → a hole; Safe stays solid.
            controller.ApplyStage(PressureStage.Expired);
            for (int i = 0; i < 80; i++) controller.TickActive(0.1f);

            Assert.That(controller.HasFloorAt(overSafe), Is.True,
                "the participant is never dropped on the safe segment");
            Assert.That(controller.HasFloorAt(overS1), Is.False,
                "standing where S1 collapsed is now over a hole");
        }

        [Test]
        public void HasFloorAt_ReturnsTrueWhenUnbound_SoNothingFallsOutsidePressure()
        {
            var empty = new GameObject("UnboundCorridor");
            _created.Add(empty);
            var controller = empty.AddComponent<PuzzleCorridorController>();
            Assert.That(controller.HasFloorAt(Vector3.zero), Is.True);
        }

        // ── console-back breakable wall ──────────────────────────────────

        private ConsoleBackWall NewBackWall()
        {
            var parent = new GameObject("BackWallParent");
            _created.Add(parent);
            var wall = parent.AddComponent<ConsoleBackWall>();
            wall.Build(parent.transform, new Vector3(0, 2.8f, -5f), 2.8f, 5.6f, 0.12f, Quaternion.identity);
            return wall;
        }

        [Test]
        public void BackWall_BuildsAGridOfPanels()
        {
            var wall = NewBackWall();
            Assert.That(wall.PanelCount, Is.EqualTo(9), "3×3 breakable panels");
            Assert.That(wall.FallenPanelCount, Is.Zero, "solid at rest");
        }

        [Test]
        public void BackWall_StaysSolidInNeutral_BreaksUnderPressure()
        {
            var wall = NewBackWall();
            // No stage advance / no ticks fed (Neutral): nothing falls.
            wall.ApplyStage(PressureStage.Stable);
            for (int i = 0; i < 20; i++) wall.TickActive(0.1f);
            Assert.That(wall.FallenPanelCount, Is.Zero, "Neutral keeps the wall solid");

            // Expired: every panel breaks away and clears.
            wall.ApplyStage(PressureStage.Expired);
            for (int i = 0; i < 40; i++) wall.TickActive(0.1f);
            Assert.That(wall.FallenPanelCount, Is.EqualTo(9), "all panels clear at 0 %");
        }

        [Test]
        public void BackWall_Reset_RestoresEveryPanel()
        {
            var wall = NewBackWall();
            var parent = GameObject.Find("BackWallParent");

            wall.ApplyStage(PressureStage.Expired);
            for (int i = 0; i < 40; i++) wall.TickActive(0.1f);

            wall.ResetWall();
            Assert.That(wall.FallenPanelCount, Is.Zero, "reset makes the wall solid again");
            foreach (var r in parent.GetComponentsInChildren<Renderer>(true))
                Assert.That(r.enabled, Is.True, "every panel renderer re-enabled");
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var f = FindDeep(child, name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
#endif
