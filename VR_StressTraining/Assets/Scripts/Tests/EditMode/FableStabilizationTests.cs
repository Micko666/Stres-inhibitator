using System;
using System.Collections.Generic;
using NUnit.Framework;
using StressTraining.Console;
using StressTraining.Tablet;
using StressTraining.Tasks;
using StressTraining.UI;
using UnityEngine;
using UnityEngine.UI;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// FAZA 1 regression lock: physical LEFT/RIGHT sides, black-on-white
    /// Go/No-Go stimulus and tutorial/footer layout separation.
    /// </summary>
    public sealed class FableStabilizationTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            for (int i = _created.Count - 1; i >= 0; i--)
                if (_created[i] != null) UnityEngine.Object.DestroyImmediate(_created[i]);
            _created.Clear();
        }

        // ── LEFT/RIGHT user-perspective mapping ──────────────────────────

        [Test]
        public void UserPerspectiveX_PlayerLeftIsPositiveLocalX()
        {
            const float spacing = 0.15f;
            // The player faces -Z; their left hand side is local +X.
            Assert.That(ConsoleLayoutBuilder.UserPerspectiveX(-2, spacing),
                Is.EqualTo(0.30f).Within(1e-5), "LEFT must sit at +2·spacing");
            Assert.That(ConsoleLayoutBuilder.UserPerspectiveX(2, spacing),
                Is.EqualTo(-0.30f).Within(1e-5), "RIGHT must sit at -2·spacing");
            Assert.That(ConsoleLayoutBuilder.UserPerspectiveX(0, spacing), Is.EqualTo(0f));
        }

        [Test]
        public void BuiltRow_ReadsLeftMatchGoNoMatchRight_FromUserPerspective()
        {
            // ConsoleControlBase intentionally uses per-instance renderer.material
            // (runtime feature); in edit mode Unity logs a leak warning-as-error.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var shell = NewObject("Console_TestShell");
            Transform root = ConsoleLayoutBuilder.Build(shell.transform, new ConsoleLayoutConfig());
            _created.Add(root.gameObject);

            float leftX = root.Find(ConsoleLayoutBuilder.LeftId).localPosition.x;
            float matchX = root.Find(ConsoleLayoutBuilder.MatchId).localPosition.x;
            float goX = root.Find(ConsoleLayoutBuilder.GoId).localPosition.x;
            float noMatchX = root.Find(ConsoleLayoutBuilder.NoMatchId).localPosition.x;
            float rightX = root.Find(ConsoleLayoutBuilder.RightId).localPosition.x;

            // Player's left = +X: strictly descending local X from LEFT to RIGHT.
            Assert.That(leftX, Is.GreaterThan(matchX));
            Assert.That(matchX, Is.GreaterThan(goX));
            Assert.That(goX, Is.GreaterThan(noMatchX));
            Assert.That(noMatchX, Is.GreaterThan(rightX));
            Assert.That(goX, Is.EqualTo(0f).Within(1e-5), "GO stays centered");
        }

        // ── Go/No-Go black-on-white stimulus ─────────────────────────────

        [Test]
        public void GoNoGoStimulus_IsWordOnWhite_NoColorMeaning()
        {
            StimulusVisual go = TabletViewModel.Decode("go:T1");
            Assert.That(go.Kind, Is.EqualTo(StimulusVisualKind.GoNoGo));
            Assert.That(go.IsNoGo, Is.False);
            Assert.That(go.Text, Is.EqualTo("GO"));
            Assert.That(go.Color, Is.EqualTo(Color.white), "background must be white");

            StimulusVisual noGo = TabletViewModel.Decode("nogo:T3");
            Assert.That(noGo.IsNoGo, Is.True);
            Assert.That(noGo.Text, Is.EqualTo("NO GO"));
            Assert.That(noGo.Color, Is.EqualTo(Color.white),
                "no-go must not be distinguishable by color at any tier");
        }

        [Test]
        public void GoNoGoStimulus_AllTiersShareIdenticalVisuals()
        {
            // Difficulty tiers change timing/proportion only — never the visual.
            for (int tier = 1; tier <= 3; tier++)
            {
                StimulusVisual v = TabletViewModel.Decode("nogo:T" + tier);
                Assert.That(v.Color, Is.EqualTo(Color.white));
                Assert.That(v.Text, Is.EqualTo("NO GO"));
            }
        }

        // ── Tutorial/footer layout separation ────────────────────────────

        [Test]
        public void MenuPanel_BodyIsClippedAboveFooter_NeverUnderButtons()
        {
            var host = NewObject("PanelHost");
            var canvasRoot = NewObject("CanvasRoot");
            canvasRoot.AddComponent<RectTransform>();

            var panel = host.AddComponent<InfoPanel>();
            panel.Build(canvasRoot.transform, "TestPanel");

            panel.Configure("Naslov",
                new string('x', 4000), // pathologically long tutorial text
                new List<(string, Action)>
                {
                    ("Ponovi vježbu", () => { }),
                    ("Nastavi", () => { })
                });

            // Footer reserves two rows; the clipped body must end above it.
            Assert.That(panel.FooterHeight, Is.GreaterThan(80f));
            Assert.That(panel.BodyBottomOffset,
                Is.GreaterThanOrEqualTo(52f + panel.FooterHeight),
                "body bottom must sit above the reserved footer");

            // The body text object must live under a RectMask2D so overflow is
            // clipped instead of painting across the action buttons.
            var mask = panel.PanelRoot.GetComponentInChildren<UnityEngine.UI.RectMask2D>(true);
            Assert.That(mask, Is.Not.Null);
        }

        [Test]
        public void MenuPanel_FooterGrowsWithOptionCount()
        {
            var host = NewObject("PanelHost2");
            var canvasRoot = NewObject("CanvasRoot2");
            canvasRoot.AddComponent<RectTransform>();

            var panel = host.AddComponent<InfoPanel>();
            panel.Build(canvasRoot.transform, "TestPanel2");

            panel.Configure("t", "b", new List<(string, Action)> { ("A", null) });
            float one = panel.FooterHeight;
            panel.Configure("t", "b", new List<(string, Action)>
                { ("A", null), ("B", null), ("C", null) });
            float three = panel.FooterHeight;

            Assert.That(three, Is.GreaterThan(one));
            Assert.That(panel.BodyBottomOffset, Is.GreaterThan(three),
                "body must always clear the footer plus the info line");
        }


        // ── PHASE 8.5 tablet progress layout ─────────────────────────────

        [Test]
        public void TabletProgress_UsesSeparateRowsAndStableNineBlockThreeRoundLabels()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            TabletDisplayController tablet = TabletDisplayController.CreateOrFind(null);
            _created.Add(tablet.gameObject);
            tablet.SetSessionProgress(9, 9, 3, 3, "Sekvencijalna memorija", 3);
            tablet.SetTimer(367);

            Transform header = FindDeep(tablet.transform, "SessionHeader");
            Transform progress = FindDeep(header, "ProgressRow");
            Transform taskRow = FindDeep(header, "TaskRow");
            Text block = FindDeep(progress, "BlockText").GetComponent<Text>();
            Text round = FindDeep(progress, "RoundText").GetComponent<Text>();
            Text time = FindDeep(progress, "GlobalTimeText").GetComponent<Text>();
            Text task = FindDeep(taskRow, "TaskAndLevelText").GetComponent<Text>();

            Assert.That(block.text, Is.EqualTo("Blok 9 od 9"));
            Assert.That(round.text, Is.EqualTo("Runda 3 od 3"));
            Assert.That(time.text, Is.EqualTo("6:07"));
            Assert.That(task.text, Is.EqualTo("Sekvencijalna memorija · nivo 3"));
            Assert.That(task.transform.parent, Is.Not.SameAs(progress),
                "task/level must occupy its own row below progress/time");
            Assert.That(task.GetComponent<LayoutElement>(), Is.Not.Null);

            Transform stimulusArea = FindDeep(tablet.transform, "StimulusArea");
            Transform localProgress = FindDeep(tablet.transform, "LocalProgressArea");
            Transform feedback = FindDeep(tablet.transform, "FeedbackArea");
            float preferred = header.GetComponent<LayoutElement>().preferredHeight +
                              stimulusArea.GetComponent<LayoutElement>().preferredHeight +
                              localProgress.GetComponent<LayoutElement>().preferredHeight +
                              feedback.GetComponent<LayoutElement>().preferredHeight + 15f;
            Assert.That(preferred, Is.LessThanOrEqualTo(364f),
                "fixed rows plus layout spacing must fit inside TabletRoot");
        }

        [Test]
        public void TabletCorsiMap_LivesInsideStimulusArea_NotHeader()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            TabletDisplayController tablet = TabletDisplayController.CreateOrFind(null);
            _created.Add(tablet.gameObject);
            tablet.ShowStimulus("corsi:0-1-2");

            Transform header = FindDeep(tablet.transform, "SessionHeader");
            Transform stimulusArea = FindDeep(tablet.transform, "StimulusArea");
            for (int i = 0; i < CorsiLayout.PositionCount; i++)
            {
                Transform cell = FindDeep(stimulusArea, "Corsi" + i);
                Assert.That(cell, Is.Not.Null);
                Assert.That(cell.IsChildOf(header), Is.False);
            }
        }

        // ── PHASE 8.5 static robot arm ──────────────────────────────────

        [Test]
        public void RobotArm_InitializeAppliesOneFixedPose_AndCompatibilityCallsDoNotMoveIt()
        {
            GameObject arm = NewObject("RobotArm");
            Transform basePivot = NewChild(arm.transform, "BasePivot");
            Transform shoulder = NewChild(basePivot, "ShoulderPivot");
            Transform elbow = NewChild(shoulder, "ElbowPivot");
            Transform wrist = NewChild(elbow, "WristPivot");
            RobotArmTabletPresenter presenter = arm.AddComponent<RobotArmTabletPresenter>();

            presenter.Initialize(arm.transform);
            Assert.That(presenter.StaticPoseApplied, Is.True);
            Assert.That(presenter.HasActiveTransition, Is.False);
            Quaternion[] fixedPose =
            {
                basePivot.localRotation, shoulder.localRotation,
                elbow.localRotation, wrist.localRotation
            };

            presenter.MoveToPresent();
            presenter.MoveToRest();
            presenter.SetPaused(true);
            presenter.SetPaused(false);

            AssertRotation(fixedPose[0], basePivot.localRotation);
            AssertRotation(fixedPose[1], shoulder.localRotation);
            AssertRotation(fixedPose[2], elbow.localRotation);
            AssertRotation(fixedPose[3], wrist.localRotation);
            Assert.That(typeof(RobotArmTabletPresenter).GetMethod("Update",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public), Is.Null,
                "static presenter must not retain a runtime Update tween");
        }


        private static Transform NewChild(Transform parent, string name)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                if (item.name == name) return item;
            return null;
        }

        private static void AssertRotation(Quaternion expected, Quaternion actual) =>
            Assert.That(Quaternion.Angle(expected, actual), Is.LessThan(0.001f));

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }
    }
}
