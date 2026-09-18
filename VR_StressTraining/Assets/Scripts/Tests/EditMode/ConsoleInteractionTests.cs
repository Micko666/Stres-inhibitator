using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Oculus.Interaction;
using StressTraining.Console;
using StressTraining.Data;
using StressTraining.Tasks;
using UnityEngine;

namespace StressTraining.Tests.EditMode
{
    public sealed class ConsoleInteractionTests
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

        [Test]
        public void DefaultBindings_ContainFiveTaskButtonsAndNineCorsiButtons_NoPause()
        {
            Dictionary<string, SemanticAction> map = ConsoleLayoutBuilder.DefaultBindings()
                .ToDictionary(binding => binding.controlId, binding => binding.action);

            Assert.That(map, Has.Count.EqualTo(14));
            Assert.That(map[ConsoleLayoutBuilder.LeftId], Is.EqualTo(SemanticAction.Left));
            Assert.That(map[ConsoleLayoutBuilder.MatchId], Is.EqualTo(SemanticAction.Match));
            Assert.That(map[ConsoleLayoutBuilder.GoId], Is.EqualTo(SemanticAction.Go));
            Assert.That(map[ConsoleLayoutBuilder.NoMatchId], Is.EqualTo(SemanticAction.NoMatch));
            Assert.That(map[ConsoleLayoutBuilder.RightId], Is.EqualTo(SemanticAction.Right));
            Assert.That(map.Values, Has.No.Member(SemanticAction.PauseToggle),
                "pause is left-controller Y only — the console has no PAUSE button");

            for (int i = 0; i < CorsiLayout.PositionCount; i++)
            {
                string controlId = CorsiLayout.ControlId(i);
                Assert.That(map.ContainsKey(controlId), Is.True,
                    $"Default bindings must contain {controlId}.");
                Assert.That(map[controlId], Is.EqualTo(CorsiLayout.ActionFor(i)));
            }
        }


        [Test]
        public void ProductionBuild_ContainsOnlyFiveTaskAndNineCorsiControls()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            GameObject shell = NewObject("ProductionConsoleShell");
            Transform root = ConsoleLayoutBuilder.Build(shell.transform, new ConsoleLayoutConfig());

            Assert.That(root.GetComponentsInChildren<ConsoleLeverControl>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<ConsoleKnobControl>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<ConsoleToggleControl>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<ConsoleIndicatorLight>(true), Is.Empty);

            ConsoleButtonControl[] buttons = root.GetComponentsInChildren<ConsoleButtonControl>(true);
            Assert.That(buttons, Has.Length.EqualTo(14));
            Assert.That(buttons.Count(b => b.ControlId.StartsWith("CORSI_")), Is.EqualTo(9));
            Assert.That(root.Find(ConsoleLayoutBuilder.ModularLeftAnchorName), Is.Null);
            Assert.That(root.Find(ConsoleLayoutBuilder.ModularRightAnchorName), Is.Null);
        }

        // Pause is exclusively the left-controller Y button; the console must not
        // carry a PAUSE control at all (and a stale one must be rebuilt away).
        [Test]
        public void ProductionBuild_HasNoPauseButtonOnConsole()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            GameObject shell = NewObject("NoPauseConsoleShell");
            Transform root = ConsoleLayoutBuilder.Build(shell.transform, new ConsoleLayoutConfig());

            Assert.That(root.Find("btn_pause"), Is.Null,
                "the console must not contain a PAUSE button");
            foreach (ConsoleButtonControl b in root.GetComponentsInChildren<ConsoleButtonControl>(true))
                Assert.That(b.ControlId, Is.Not.EqualTo("btn_pause"));
        }

        [Test]
        public void RepeatedBuild_IsIdempotent_AndDoesNotDuplicateControls()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            GameObject shell = NewObject("IdempotentConsoleShell");
            var config = new ConsoleLayoutConfig();
            Transform first = ConsoleLayoutBuilder.Build(shell.transform, config);
            Transform second = ConsoleLayoutBuilder.Build(shell.transform, config);

            Assert.That(second, Is.SameAs(first));
            Assert.That(shell.transform.Cast<Transform>().Count(t => t.name == ConsoleLayoutBuilder.AnchorName),
                Is.EqualTo(1));
            Assert.That(first.GetComponentsInChildren<ConsoleButtonControl>(true), Has.Length.EqualTo(14));
            for (int i = 0; i < CorsiLayout.PositionCount; i++)
            {
                string id = CorsiLayout.ControlId(i);
                Assert.That(first.GetComponentsInChildren<ConsoleButtonControl>(true)
                    .Count(b => b.ControlId == id), Is.EqualTo(1), id);
            }
        }

        [Test]
        public void ActiveTaskMap_IsExact()
        {
            CollectionAssert.AreEqual(
                new[] { SemanticAction.Match, SemanticAction.NoMatch },
                ConsoleInputRouter.ActionsForTask(TaskType.NBack));
            CollectionAssert.AreEqual(
                new[] { SemanticAction.Go },
                ConsoleInputRouter.ActionsForTask(TaskType.GoNoGo));
            CollectionAssert.AreEqual(
                new[] { SemanticAction.Left, SemanticAction.Right },
                ConsoleInputRouter.ActionsForTask(TaskType.Flanker));
            CollectionAssert.IsEmpty(ConsoleInputRouter.ActionsForTask(TaskType.None));
        }

        [Test]
        public void InactiveControl_SelectEmitsNothing()
        {
            ConsoleInputRouter router = CreateRouterWithButtons(out ConsoleButtonControl match,
                out _);
            router.SetActiveTask(TaskType.GoNoGo);
            int actions = 0;
            int physicalEvents = 0;
            router.ActionTriggered += (_, __) => actions++;
            router.ControlInteracted += (_, __) => physicalEvents++;

            match.HandlePokePointerEvent(Event(7, PointerEventType.Select));

            Assert.That(match.Interactable, Is.False);
            Assert.That(actions, Is.Zero);
            Assert.That(physicalEvents, Is.Zero);
        }

        [Test]
        public void Select_IsLatchedUntilUnselect()
        {
            ConsoleInputRouter router = CreateRouterWithButtons(out ConsoleButtonControl match,
                out _);
            router.SetActiveTask(TaskType.NBack);
            int actions = 0;
            router.ActionTriggered += (action, _) =>
            {
                if (action == SemanticAction.Match) actions++;
            };

            match.HandlePokePointerEvent(Event(42, PointerEventType.Select));
            match.HandlePokePointerEvent(Event(42, PointerEventType.Select));
            Assert.That(actions, Is.EqualTo(1), "Duplicate Select must not repeat one press.");

            match.HandlePokePointerEvent(Event(42, PointerEventType.Unselect));
            match.HandlePokePointerEvent(Event(42, PointerEventType.Select));
            Assert.That(actions, Is.EqualTo(2), "A new Select after release must be accepted.");
        }

        [Test]
        public void Select_IsControlWideLatchedAcrossDifferentPointers()
        {
            ConsoleInputRouter router = CreateRouterWithButtons(out ConsoleButtonControl match,
                out _);
            router.SetActiveTask(TaskType.NBack);
            int actions = 0;
            router.ActionTriggered += (action, _) =>
            {
                if (action == SemanticAction.Match) actions++;
            };

            match.HandlePokePointerEvent(Event(10, PointerEventType.Select));
            match.HandlePokePointerEvent(Event(11, PointerEventType.Select));
            Assert.That(actions, Is.EqualTo(1),
                "A second hand/controller joining one held press must not emit again.");

            match.HandlePokePointerEvent(Event(10, PointerEventType.Unselect));
            match.HandlePokePointerEvent(Event(10, PointerEventType.Select));
            Assert.That(actions, Is.EqualTo(1),
                "The control remains latched while any selecting pointer is held.");

            match.HandlePokePointerEvent(Event(10, PointerEventType.Unselect));
            match.HandlePokePointerEvent(Event(11, PointerEventType.Unselect));
            match.HandlePokePointerEvent(Event(12, PointerEventType.Select));
            Assert.That(actions, Is.EqualTo(2),
                "A press after all pointers release is a new physical input.");
        }

        private ConsoleInputRouter CreateRouterWithButtons(out ConsoleButtonControl match,
            out ConsoleButtonControl go)
        {
            GameObject root = NewObject("ConsoleTestRoot");
            match = NewButton(root.transform, ConsoleLayoutBuilder.MatchId);
            go = NewButton(root.transform, ConsoleLayoutBuilder.GoId);
            ConsoleInputRouter router = root.AddComponent<ConsoleInputRouter>();
            router.Initialize(root.transform, ConsoleLayoutBuilder.DefaultBindings());
            return router;
        }

        private ConsoleButtonControl NewButton(Transform parent, string id)
        {
            GameObject button = NewObject(id);
            button.transform.SetParent(parent, false);
            ConsoleButtonControl control = button.AddComponent<ConsoleButtonControl>();
            control.Configure(id, false, null);
            return control;
        }

        private GameObject NewObject(string name)
        {
            var go = new GameObject(name);
            _created.Add(go);
            return go;
        }

        private static PointerEvent Event(int identifier, PointerEventType type) =>
            new PointerEvent(identifier, type, Pose.identity);
    }
}
