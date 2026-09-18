#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using StressTraining.Adaptation;
using StressTraining.Console;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Pressure;
using StressTraining.Tablet;
using StressTraining.Tasks;
using StressTraining.UI;
using UnityEngine;
using UnityEngine.UI;

namespace StressTraining.Tests.EditMode
{
    /// <summary>
    /// UX corrections: the profile keyboard, the manual pause contract, the Flanker
    /// stimulus frame, the level-specific instructions and the pressure chain.
    /// </summary>
    public sealed class FableUxCorrectionTests
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

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        // ── profile UI ───────────────────────────────────────────────────

        private ProfileSelectionPanel NewProfilePanel()
        {
            var root = NewObject("ProfilePanelRoot");
            root.AddComponent<RectTransform>();
            var panel = root.AddComponent<ProfileSelectionPanel>();
            panel.Build(root.transform, "ProfileSelectionPanel");
            panel.ShowProfiles(new List<ProfileIndexEntry>(), false);
            return panel;
        }

        private static Button FindButton(ProfileSelectionPanel panel, string name)
        {
            foreach (var button in panel.PanelRoot.GetComponentsInChildren<Button>(true))
                if (button.name == name) return button;
            return null;
        }

        private static void OpenCreateMode(ProfileSelectionPanel panel)
        {
            // "+ Novi profil" is the last option row in list mode.
            var rows = panel.PanelRoot.GetComponentsInChildren<Button>(true)
                .Where(b => b.name.StartsWith("Row")).ToList();
            rows[rows.Count - 1].onClick.Invoke();
        }

        [Test]
        public void ProfileCreate_EveryKeyAndActionIsARealClickableButton()
        {
            var panel = NewProfilePanel();
            OpenCreateMode(panel);
            Assert.That(panel.IsCreateMode, Is.True);

            // 39 keys (3×13) + 4 actions + NAZAD must exist as uGUI Buttons, because
            // the Quest ray can only click Selectables — the old text strip could not
            // be clicked at all.
            for (int i = 0; i < 26; i++)
                Assert.That(FindButton(panel, "Key_" + (char)('A' + i)), Is.Not.Null,
                    "letter key must be a Button");
            for (int i = 0; i < 10; i++)
                Assert.That(FindButton(panel, "Key_" + i), Is.Not.Null, "digit key must be a Button");
            foreach (string action in new[] { "RAZMAK", "OBRIŠI", "SAČUVAJ", "OTKAŽI" })
                Assert.That(FindButton(panel, "Action_" + action), Is.Not.Null);
            Assert.That(FindButton(panel, "BackButton"), Is.Not.Null, "NAZAD must be visible");
        }

        [Test]
        public void ProfileCreate_OnePress_AddsExactlyOneCharacter()
        {
            var panel = NewProfilePanel();
            OpenCreateMode(panel);

            FindButton(panel, "Key_M").onClick.Invoke();
            Assert.That(panel.CurrentNameBuffer, Is.EqualTo("M"));

            FindButton(panel, "Key_I").onClick.Invoke();
            Assert.That(panel.CurrentNameBuffer, Is.EqualTo("MI"),
                "one press = one character; no listener may be attached twice");
        }

        [Test]
        public void ProfileCreate_Delete_RemovesOneCharacter()
        {
            var panel = NewProfilePanel();
            OpenCreateMode(panel);
            FindButton(panel, "Key_A").onClick.Invoke();
            FindButton(panel, "Key_B").onClick.Invoke();

            FindButton(panel, "Action_OBRIŠI").onClick.Invoke();
            Assert.That(panel.CurrentNameBuffer, Is.EqualTo("A"));
        }

        [Test]
        public void ProfileCreate_Space_IsRejectedWhenNotAllowed()
        {
            var panel = NewProfilePanel();
            OpenCreateMode(panel);

            var space = FindButton(panel, "Action_RAZMAK");
            Assert.That(space.interactable, Is.False, "no leading space on an empty name");
            space.onClick.Invoke();
            Assert.That(panel.CurrentNameBuffer, Is.Empty);

            FindButton(panel, "Key_A").onClick.Invoke();
            space.onClick.Invoke();
            Assert.That(panel.CurrentNameBuffer, Is.EqualTo("A "));

            space.onClick.Invoke();
            Assert.That(panel.CurrentNameBuffer, Is.EqualTo("A "), "no doubled space");
        }

        [Test]
        public void ProfileCreate_Save_DoesNothingForAnEmptyOrTooShortName()
        {
            var panel = NewProfilePanel();
            int raised = 0;
            panel.ProfileCreateRequested += _ => raised++;
            OpenCreateMode(panel);

            var save = FindButton(panel, "Action_SAČUVAJ");
            Assert.That(save.interactable, Is.False);
            save.onClick.Invoke();
            Assert.That(raised, Is.Zero, "an empty name must never be saved");

            FindButton(panel, "Key_A").onClick.Invoke();          // 1 char < MinLength (2)
            save.onClick.Invoke();
            Assert.That(raised, Is.Zero, "a too-short name must never be saved");
        }

        [Test]
        public void ProfileCreate_Save_EmitsTheTrimmedNameOnce()
        {
            var panel = NewProfilePanel();
            var names = new List<string>();
            panel.ProfileCreateRequested += names.Add;
            OpenCreateMode(panel);

            FindButton(panel, "Key_A").onClick.Invoke();
            FindButton(panel, "Key_N").onClick.Invoke();
            FindButton(panel, "Key_A").onClick.Invoke();
            FindButton(panel, "Action_SAČUVAJ").onClick.Invoke();

            Assert.That(names, Is.EqualTo(new List<string> { "ANA" }));
        }

        [Test]
        public void ProfileCreate_CancelAndBack_BothReturnToTheProfileList()
        {
            var panel = NewProfilePanel();
            int backCount = 0;
            panel.OnBack = () => backCount++;

            OpenCreateMode(panel);
            FindButton(panel, "Action_OTKAŽI").onClick.Invoke();
            Assert.That(panel.IsCreateMode, Is.False);
            Assert.That(backCount, Is.EqualTo(1), "OTKAŽI must leave create mode");

            OpenCreateMode(panel);
            FindButton(panel, "BackButton").onClick.Invoke();
            Assert.That(panel.IsCreateMode, Is.False);
            Assert.That(backCount, Is.EqualTo(2), "NAZAD must leave create mode");

            OpenCreateMode(panel);
            panel.OnNav(NavEvent.Back);                            // B on the right controller
            Assert.That(panel.IsCreateMode, Is.False);
            Assert.That(backCount, Is.EqualTo(3), "B must leave create mode");
        }

        [Test]
        public void ProfileCreate_ReEntry_DoesNotDuplicateListeners()
        {
            var panel = NewProfilePanel();
            int raised = 0;
            panel.ProfileCreateRequested += _ => raised++;

            for (int i = 0; i < 3; i++)
            {
                OpenCreateMode(panel);
                Assert.That(panel.CurrentNameBuffer, Is.Empty, "re-entry starts from an empty name");
                FindButton(panel, "Key_A").onClick.Invoke();
                FindButton(panel, "Key_B").onClick.Invoke();
                Assert.That(panel.CurrentNameBuffer, Is.EqualTo("AB"),
                    "keys must still add exactly one character after re-entry");
                FindButton(panel, "Action_OTKAŽI").onClick.Invoke();
            }

            OpenCreateMode(panel);
            FindButton(panel, "Key_A").onClick.Invoke();
            FindButton(panel, "Key_B").onClick.Invoke();
            FindButton(panel, "Action_SAČUVAJ").onClick.Invoke();
            Assert.That(raised, Is.EqualTo(1), "save must fire once, not once per entry");
        }

        [Test]
        public void ProfileCreate_ConfirmNav_ActivatesTheFocusedKey()
        {
            var panel = NewProfilePanel();
            OpenCreateMode(panel);

            panel.OnNav(NavEvent.Confirm);                 // focus starts on 'A'
            Assert.That(panel.CurrentNameBuffer, Is.EqualTo("A"));

            panel.OnNav(NavEvent.Right);
            panel.OnNav(NavEvent.Confirm);                 // 'B'
            Assert.That(panel.CurrentNameBuffer, Is.EqualTo("AB"));
        }

        // ── pause ────────────────────────────────────────────────────────

        [Test]
        public void Pause_IsManualOnly_AndIdempotent()
        {
            var clock = new SessionClock();
            clock.StartSession();
            var pause = new PauseController(clock);
            int changes = 0;
            pause.PauseChanged += (_, __) => changes++;

            pause.Pause("user_pause");
            pause.Pause("user_pause");                     // repeated Y while already paused
            Assert.That(pause.IsPaused, Is.True);
            Assert.That(changes, Is.EqualTo(1), "one pause open, not two");

            pause.Resume("countdown_complete");
            pause.Resume("countdown_complete");
            Assert.That(pause.IsPaused, Is.False);
            Assert.That(changes, Is.EqualTo(2), "one resume, so exactly one countdown");
            Assert.That(pause.Records, Has.Count.EqualTo(1));
        }

        [Test]
        public void Pause_BlocksTaskInputAndKeepsTheSameAttempt()
        {
            var trials = FlankerTaskDefinition.Generate(
                TaskDifficultyConfig.Get(TaskType.Flanker, 1), 7);
            var runtime = new FlankerTaskRuntime(trials);
            runtime.Begin();
            for (int i = 0; i < 200 && runtime.Phase != TrialPhase.ResponseOpen; i++)
                runtime.Tick(0.05);
            Assert.That(runtime.Phase, Is.EqualTo(TrialPhase.ResponseOpen));

            int before = runtime.CurrentTrialIndex;
            runtime.OnPauseInterrupt();
            runtime.SubmitAction(SemanticAction.Left);     // input while paused
            Assert.That(runtime.Results, Is.Empty, "no trial may be scored while paused");
            Assert.That(runtime.CurrentTrialIndex, Is.EqualTo(before),
                "resume continues the SAME attempt, it does not skip ahead");
        }

        [Test]
        public void Console_HasNoPauseControl_PauseStaysOnLeftControllerY()
        {
            Assert.That(ConsoleLayoutBuilder.DefaultBindings()
                    .Any(b => b.action == SemanticAction.PauseToggle), Is.False,
                "the console must never regain a PAUSE button");
        }

        // ── Flanker stimulus frame ───────────────────────────────────────

        private TabletDisplayController NewTablet()
        {
            // Awake does not run in edit mode, so go through the same entry point the
            // bootstrapper uses — it builds the hierarchy explicitly.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var parent = NewObject("TabletTestParent");
            var tablet = TabletDisplayController.CreateOrFind(parent.transform);
            _created.Add(tablet.gameObject);
            return tablet;
        }

        [Test]
        public void Flanker_UsesTheFullStimulusArea_WithNoCard()
        {
            var tablet = NewTablet();
            tablet.ShowStimulus("arr:>><>>");

            Assert.That(tablet.StimulusPanelRect.sizeDelta,
                Is.EqualTo(TabletDisplayController.StimulusAreaSize),
                "the arrows must claim the whole central StimulusArea");
            Assert.That(tablet.StimulusPanelColor.a, Is.EqualTo(0f).Within(1e-4),
                "no card/frame may be drawn behind the flanker arrows");
        }

        [Test]
        public void Flanker_ChevronRowWidth_IsWithinSeventyToEightyFivePercent()
        {
            var tablet = NewTablet();
            tablet.ShowStimulus("arr:<<<<<");

            float ratio = tablet.FlankerRowRect.sizeDelta.x /
                          TabletDisplayController.StimulusAreaSize.x;
            Assert.That(ratio, Is.InRange(0.70f, 0.85f));
            Assert.That(tablet.FlankerRowRect.anchoredPosition.x, Is.EqualTo(0f).Within(1e-4),
                "the chevron row must be horizontally centred");
            Assert.That(tablet.FlankerRowRect.gameObject.activeInHierarchy, Is.True,
                "the chevron row is shown for a flanker stimulus");
        }

        [Test]
        public void Flanker_ChevronRow_StaysInsideTheStimulusArea()
        {
            var tablet = NewTablet();
            tablet.ShowStimulus("arr:>><>>");

            Assert.That(tablet.FlankerRowRect.sizeDelta.x,
                Is.LessThanOrEqualTo(TabletDisplayController.StimulusAreaSize.x));
            Assert.That(tablet.FlankerRowRect.sizeDelta.y,
                Is.LessThanOrEqualTo(TabletDisplayController.StimulusAreaSize.y));
        }

        [Test]
        public void Flanker_ChevronRow_HiddenForOtherStimuli()
        {
            var tablet = NewTablet();
            tablet.ShowStimulus("arr:>><>>");
            Assert.That(tablet.FlankerRowRect.gameObject.activeInHierarchy, Is.True);

            tablet.ShowStimulus("sym:A");
            Assert.That(tablet.FlankerRowRect.gameObject.activeInHierarchy, Is.False,
                "the chevron row must not linger behind other tasks");
        }

        [Test]
        public void OtherStimuli_KeepTheirCard_FlankerChangeIsIsolated()
        {
            var tablet = NewTablet();
            tablet.ShowStimulus("sym:A");
            Assert.That(tablet.StimulusPanelColor.a, Is.GreaterThan(0.5f),
                "the n-back symbol keeps its card");

            tablet.ShowStimulus("go:T1");
            Assert.That(tablet.StimulusPanelColor.a, Is.GreaterThan(0.5f),
                "Go/No-Go keeps its white card");
        }

        [Test]
        public void Flanker_ScoringAndResponse_AreUnchanged()
        {
            Assert.That(FlankerTaskDefinition.CentralDirection("arr:>><>>"),
                Is.EqualTo(SemanticAction.Left), "the CENTRE arrow still decides the answer");
            Assert.That(FlankerTaskDefinition.CentralDirection("arr:<<><<"),
                Is.EqualTo(SemanticAction.Right));
        }

        // ── level-specific instructions ──────────────────────────────────

        [Test]
        public void Instructions_DifferPerLevel_ForEveryTask()
        {
            foreach (TaskType task in new[]
                     {
                         TaskType.NBack, TaskType.GoNoGo, TaskType.Flanker, TaskType.CorsiSequence
                     })
            {
                string l1 = TaskInstructions.Page(task, 1);
                string l2 = TaskInstructions.Page(task, 2);
                string l3 = TaskInstructions.Page(task, 3);

                Assert.That(l1, Is.Not.Empty);
                Assert.That(l1, Is.Not.EqualTo(l2), task + ": level 1 and 2 must not read the same");
                Assert.That(l2, Is.Not.EqualTo(l3), task + ": level 2 and 3 must not read the same");
                Assert.That(TaskInstructions.Rule(task), Is.Not.Empty,
                    task + ": the rule line must always be present");
            }
        }

        [Test]
        public void Instructions_MatchTheRealDifficultyTable()
        {
            // Flanker level 2 really is 1.3 s / 50 % incongruent in TaskDifficultyConfig.
            var flanker2 = TaskDifficultyConfig.Get(TaskType.Flanker, 2);
            Assert.That(flanker2.responseWindowSeconds, Is.EqualTo(1.3).Within(1e-6));
            Assert.That(flanker2.incongruentProportion, Is.EqualTo(0.50).Within(1e-6));

            string page = TaskInstructions.Page(TaskType.Flanker, 2);
            Assert.That(page, Does.Contain("1.3 s"), "the stated window must be the configured one");
            Assert.That(page, Does.Contain("50 %"), "the stated incongruent share must be configured");
            Assert.That(page, Does.Contain("SREDNJE"), "the flanker rule names the central arrow");
        }

        [Test]
        public void Instructions_CorsiLevel3_AnnouncesTheBackwardRuleChange()
        {
            Assert.That(TaskDifficultyConfig.Get(TaskType.CorsiSequence, 3).corsiBackward, Is.True);
            Assert.That(TaskInstructions.Page(TaskType.CorsiSequence, 3), Does.Contain("UNAZAD"));
            Assert.That(TaskInstructions.Page(TaskType.CorsiSequence, 1), Does.Not.Contain("UNAZAD"));
        }

        [Test]
        public void Instructions_NBackLevel2_AnnouncesTheNChange()
        {
            Assert.That(TaskDifficultyConfig.Get(TaskType.NBack, 1).nBackN, Is.EqualTo(1));
            Assert.That(TaskDifficultyConfig.Get(TaskType.NBack, 2).nBackN, Is.EqualTo(2));
            Assert.That(TaskInstructions.Page(TaskType.NBack, 2), Does.Contain("2 koraka ranije"));
        }

        // ── scheduler summary formatting (presentation only) ──────────────

        [Test]
        public void AdaptationSummary_IsOneShortLinePerTask_WithBeforeAndAfterLevels()
        {
            // NOTE: AdaptationDirective's default value is NoDecisionInvalidSession (0),
            // so every directive is set explicitly here — exactly as the real scheduler
            // does. Leaving one unset would describe a valid session as an invalid one.
            var decision = new AdaptationDecisionData();
            decision.nBack.previousLevel = 1; decision.nBack.newLevel = 1;
            decision.nBack.directive = AdaptationDirective.Hold;
            decision.nBack.reasonCodes.Add("NOT_SELECTED_THIS_SESSION");
            decision.goNoGo.previousLevel = 1; decision.goNoGo.newLevel = 2;
            decision.goNoGo.directive = AdaptationDirective.Increase;
            decision.flanker.previousLevel = 1; decision.flanker.newLevel = 1;
            decision.flanker.directive = AdaptationDirective.Hold;
            decision.corsi.previousLevel = 1; decision.corsi.newLevel = 1;
            decision.corsi.directive = AdaptationDirective.Hold;
            decision.pressure.previousLevel = 1; decision.pressure.newLevel = 1;
            decision.pressure.directive = AdaptationDirective.Hold;
            decision.pressure.reasonCodes.Add("P_TASK_INCREASED_HOLD");

            string text = AdaptationExplanationBuilder.BuildCompact(decision);

            Assert.That(text, Does.Contain("N-back: nivo 1 → nivo 1"));
            Assert.That(text, Does.Contain("Nije korišćen u ovoj sesiji."));
            Assert.That(text, Does.Contain("Go/No-Go: nivo 1 → nivo 2"));
            Assert.That(text, Does.Contain("Globalni pritisak: nivo 1 → nivo 1"));
            Assert.That(text, Does.Contain("Task i pritisak se nikad ne povećavaju istovremeno."));

            foreach (string line in text.Split('\n'))
                Assert.That(line.Length, Is.LessThanOrEqualTo(70),
                    "no line may be long enough to wrap into a wall of text: " + line);
        }

        // ── pressure chain ───────────────────────────────────────────────

        [Test]
        public void NeutralCondition_NeverArmsPressure()
        {
            Assert.That(PressureController.ShouldRunFor(SessionCondition.Neutral), Is.False);
            Assert.That(PressureController.ShouldRunFor(SessionCondition.Pressure), Is.True);
        }

        [Test]
        public void PressureThresholds_ChangeStageAt70_50_30_10_0()
        {
            Assert.That(PressureTimeline.StageFor(0.71), Is.EqualTo(PressureStage.Stable));
            Assert.That(PressureTimeline.StageFor(0.69), Is.EqualTo(PressureStage.Early));
            Assert.That(PressureTimeline.StageFor(0.49), Is.EqualTo(PressureStage.Mid));
            Assert.That(PressureTimeline.StageFor(0.29), Is.EqualTo(PressureStage.Late));
            Assert.That(PressureTimeline.StageFor(0.09), Is.EqualTo(PressureStage.Critical));
            Assert.That(PressureTimeline.StageFor(0.00), Is.EqualTo(PressureStage.Expired));
        }

        [Test]
        public void PressureRun_BuildsSceneSegmentsAndMovesThem()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var corridor = NewObject("Corridor_TestRoot");
            var eye = NewObject("CenterEye_Test");
            var systems = NewObject("Systems_Test");
            var pressure = systems.AddComponent<PressureController>();
            pressure.Initialize(corridor.transform, eye.transform);

            Assert.That(pressure.IsInitialized, Is.True);
            Assert.That(pressure.SegmentPairCount, Is.EqualTo(PressureHeuristics.SegmentPairCount),
                "the collapse segments must exist — a missing binding is not silently skipped");
            Assert.That(pressure.CrackCount, Is.GreaterThan(0));

            pressure.Begin(1234, 3);
            pressure.SetPaused(false);

            pressure.TickActive(0.1f, 0.60);                 // Early
            Assert.That(pressure.CurrentStage, Is.EqualTo(PressureStage.Early));
            Assert.That(pressure.CurrentIntensity, Is.GreaterThan(0f), "lights/shake must ramp");

            pressure.TickActive(0.1f, 0.20);                 // Late → segments begin closing
            Assert.That(pressure.CurrentStage, Is.EqualTo(PressureStage.Late));
            Assert.That(pressure.ClosedSegmentPairCount, Is.GreaterThan(0),
                "corridor segments must actually start collapsing by 30 %");
            Assert.That(pressure.LastAppliedRemainingFraction, Is.EqualTo(0.20f).Within(1e-4));

            pressure.TickActive(0.1f, 0.05);                 // Critical
            Assert.That(pressure.CurrentStage, Is.EqualTo(PressureStage.Critical));

            Assert.That(pressure.TriggerGameOverFinale(), Is.True, "TimeExpired must run a finale");
            Assert.That(pressure.CurrentStage, Is.EqualTo(PressureStage.Expired));

            pressure.ResetPressure();
            Assert.That(pressure.ClosedSegmentPairCount, Is.Zero, "reset restores the corridor");
        }

        [Test]
        public void PressureRun_NeverClosesTheLastSafePairOverTheUser()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            var corridor = NewObject("Corridor_SafePairRoot");
            var eye = NewObject("CenterEye_SafePair");
            var pressure = NewObject("Systems_SafePair").AddComponent<PressureController>();
            pressure.Initialize(corridor.transform, eye.transform);

            pressure.Begin(99, 3);
            pressure.SetPaused(false);
            pressure.TickActive(0.1f, 0.0001);               // effectively fully expired
            pressure.TriggerGameOverFinale();

            Assert.That(pressure.ClosedSegmentPairCount,
                Is.LessThanOrEqualTo(pressure.SegmentPairCount - 1),
                "the nearest pair stays open — the corridor never crushes the participant");
        }
    }
}
#endif
