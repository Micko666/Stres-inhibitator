using System;
using System.Collections.Generic;
using StressTraining.Data;
using UnityEngine;

namespace StressTraining.Console
{
    /// <summary>
    /// Registry of all console controls, found ONCE at initialization —
    /// never per-frame (architecture rule 16).
    /// </summary>
    public sealed class ConsoleControlRegistry
    {
        private readonly Dictionary<string, ConsoleControlBase> _byId =
            new Dictionary<string, ConsoleControlBase>(32);

        public IReadOnlyDictionary<string, ConsoleControlBase> All => _byId;

        public void ScanChildren(Transform root)
        {
            _byId.Clear();
            foreach (var c in root.GetComponentsInChildren<ConsoleControlBase>(true))
            {
                if (string.IsNullOrEmpty(c.ControlId)) continue;
                _byId[c.ControlId] = c;
            }
        }

        public ConsoleControlBase Get(string controlId) =>
            _byId.TryGetValue(controlId, out var c) ? c : null;
    }

    /// <summary>
    /// The single boundary between physical controls and gameplay (spec §10):
    /// controls raise Activated → router maps controlId→SemanticAction via
    /// bindings → ActionTriggered. Non-physical adapters (OVRInput buttons,
    /// dev keyboard) inject through InjectAction and are labelled as such in
    /// the event payload. Gameplay input is blocked while paused; wrong-element
    /// (distractor/unbound) presses are still reported for logging.
    /// </summary>
    public sealed class ConsoleInputRouter : MonoBehaviour
    {
        private static readonly SemanticAction[] NoTaskActions = Array.Empty<SemanticAction>();
        private static readonly SemanticAction[] NBackActions =
            { SemanticAction.Match, SemanticAction.NoMatch };
        private static readonly SemanticAction[] GoNoGoActions =
            { SemanticAction.Go };
        private static readonly SemanticAction[] FlankerActions =
            { SemanticAction.Left, SemanticAction.Right };
        private static readonly SemanticAction[] CorsiActions =
        {
            SemanticAction.Corsi0, SemanticAction.Corsi1, SemanticAction.Corsi2,
            SemanticAction.Corsi3, SemanticAction.Corsi4, SemanticAction.Corsi5,
            SemanticAction.Corsi6, SemanticAction.Corsi7, SemanticAction.Corsi8
        };

        public ConsoleControlRegistry Registry { get; } = new ConsoleControlRegistry();

        private readonly Dictionary<string, SemanticAction> _bindings =
            new Dictionary<string, SemanticAction>(16);
        private readonly HashSet<SemanticAction> _activeTaskActions =
            new HashSet<SemanticAction>();

        public bool GameplayInputEnabled { get; private set; } = true;
        public TaskType ActiveTask { get; private set; } = TaskType.None;

        public static IReadOnlyList<SemanticAction> ActionsForTask(TaskType taskType)
        {
            switch (taskType)
            {
                case TaskType.NBack: return NBackActions;
                case TaskType.GoNoGo: return GoNoGoActions;
                case TaskType.Flanker: return FlankerActions;
                case TaskType.CorsiSequence: return CorsiActions;
                default: return NoTaskActions;
            }
        }

        public static bool IsActionActiveForTask(TaskType taskType, SemanticAction action)
        {
            IReadOnlyList<SemanticAction> actions = ActionsForTask(taskType);
            for (int i = 0; i < actions.Count; i++)
                if (actions[i] == action) return true;
            return false;
        }

        /// <summary>(action, sourceDescription). Fired for bound, enabled interactions only.</summary>
        public event Action<SemanticAction, string> ActionTriggered;
        /// <summary>Every physical interaction (incl. distractors/unbound) — for the event log.</summary>
        public event Action<ConsoleControlEvent, SemanticAction> ControlInteracted;

        public void Initialize(Transform controlsRoot, IEnumerable<ConsoleBindingDefinition> bindings)
        {
            Registry.ScanChildren(controlsRoot);
            _bindings.Clear();
            foreach (var b in bindings)
                _bindings[b.controlId] = b.action;

            foreach (var kv in Registry.All)
            {
                kv.Value.Activated -= OnControlActivated;
                kv.Value.Activated += OnControlActivated;
            }

            // Profile/tutorial screens have no active task. The session flow
            // explicitly selects the task when a practice or block begins.
            SetActiveTask(TaskType.None);
        }

        public void SetGameplayInputEnabled(bool enabled) => GameplayInputEnabled = enabled;

        /// <summary>
        /// Enables only controls relevant to the current task. Pause is available
        /// only while a task is running; tutorial/countdown/paused screens use
        /// their normal ray UI. Unbound legacy controls remain non-interactive.
        /// </summary>
        public void SetActiveTask(TaskType taskType)
        {
            ActiveTask = taskType;
            _activeTaskActions.Clear();
            IReadOnlyList<SemanticAction> active = ActionsForTask(taskType);
            for (int i = 0; i < active.Count; i++) _activeTaskActions.Add(active[i]);

            foreach (var pair in Registry.All)
            {
                ConsoleControlBase control = pair.Value;
                bool isBound = _bindings.TryGetValue(pair.Key, out SemanticAction action);
                control.Interactable = isBound &&
                    (action == SemanticAction.PauseToggle
                        ? taskType != TaskType.None
                        : _activeTaskActions.Contains(action));
            }
        }

        private void OnControlActivated(ConsoleControlBase control, ConsoleControlEvent evt)
        {
            _bindings.TryGetValue(evt.ControlId, out var action); // None when unbound
            ControlInteracted?.Invoke(evt, action);

            if (action == SemanticAction.None) return;           // distractor / unbound
            if (!GameplayInputEnabled && action != SemanticAction.PauseToggle) return;
            if (action != SemanticAction.PauseToggle && !_activeTaskActions.Contains(action)) return;
            ActionTriggered?.Invoke(action, "console:" + evt.ControlId);
        }

        /// <summary>Adapter path (OVRInput buttons / dev keyboard). Same pause gate.</summary>
        public void InjectAction(SemanticAction action, string source)
        {
            if (action == SemanticAction.None) return;
            if (!GameplayInputEnabled && action != SemanticAction.PauseToggle) return;
            if (action != SemanticAction.PauseToggle && !_activeTaskActions.Contains(action)) return;
            ActionTriggered?.Invoke(action, source);
        }


    }
}
