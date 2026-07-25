using StressTraining.Data;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StressTraining.Console
{
    /// <summary>
    /// Quest controller path. Task answers deliberately come only from the physical
    /// Meta poke controls, avoiding a second ambiguous answer path.
    ///
    /// PAUSE IS EXCLUSIVELY THE LEFT-CONTROLLER **Y** BUTTON, and this is the ONLY
    /// place a pause can originate on Quest. There is no console PAUSE button and
    /// no automatic pause anywhere in the app.
    /// </summary>
    public sealed class OVRControllerInputAdapter : MonoBehaviour
    {
        private ConsoleInputRouter _router;
        public void Initialize(ConsoleInputRouter router) => _router = router;

        private void Update()
        {
            if (_router == null) return;

            // RawButton.Y == Y on the LEFT Touch controller (unambiguous).
            if (OVRInput.GetDown(OVRInput.RawButton.Y)) Inject(SemanticAction.PauseToggle);
        }

        private void Inject(SemanticAction action) => _router.InjectAction(action, "ovr_controller_Y");
    }

    /// <summary>
    /// Developer keyboard path (Editor / Link testing without controllers).
    ///   M → Match, N → NoMatch, ←/→ → Left/Right, Space → Go, P → PauseToggle.
    /// Uses the Input System package (the project runs with the new input backend).
    /// </summary>
    public sealed class KeyboardInputAdapter : MonoBehaviour
    {
        private ConsoleInputRouter _router;
        public void Initialize(ConsoleInputRouter router) => _router = router;

        private void Update()
        {
#if UNITY_EDITOR
            var kb = Keyboard.current;
            if (_router == null || kb == null) return;

            if (kb.mKey.wasPressedThisFrame) Inject(SemanticAction.Match);
            if (kb.nKey.wasPressedThisFrame) Inject(SemanticAction.NoMatch);
            if (kb.leftArrowKey.wasPressedThisFrame) Inject(SemanticAction.Left);
            if (kb.rightArrowKey.wasPressedThisFrame) Inject(SemanticAction.Right);
            if (kb.spaceKey.wasPressedThisFrame) Inject(SemanticAction.Go);
            if (kb.pKey.wasPressedThisFrame) Inject(SemanticAction.PauseToggle);
#endif
        }

        private void Inject(SemanticAction action) => _router.InjectAction(action, "dev_keyboard");
    }
}
