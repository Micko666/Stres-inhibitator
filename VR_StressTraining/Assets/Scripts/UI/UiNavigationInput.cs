using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StressTraining.UI
{
    public enum NavEvent
    {
        Up, Down, Left, Right, Confirm, Back
    }

    /// <summary>
    /// Non-ray menu fallback, alongside the standard right-controller ray. The ray
    /// remains the primary path; this adds the thumbstick and A/B so a panel is
    /// never a dead end (the profile keyboard advertised A/B while this was switched
    /// off, which is why none of its keys responded).
    ///
    /// Thumbstick: crossing ±0.6 fires once, auto-repeats every 0.35 s while held.
    /// RawButton.A (RIGHT controller) → Confirm, RawButton.B (RIGHT) → Back. Raw
    /// buttons are used deliberately: OVRInput.Button.One/Two also match X/Y on the
    /// LEFT controller, and Y is the pause button — it must never reach a menu.
    ///
    /// The ray click (OVRInputModule, right index trigger) and A are separate
    /// physical presses, and EventSystem.sendNavigationEvents is off, so one press
    /// can never be delivered through both paths.
    ///
    /// This class NEVER triggers a pause. Pause has exactly one owner
    /// (OVRControllerInputAdapter → left-controller Y).
    /// </summary>
    public sealed class UiNavigationInput : MonoBehaviour
    {
        public event Action<NavEvent> Nav;

        private const float Threshold = 0.6f;
        private const float RepeatSeconds = 0.35f;

        private float _xHeldSince = -1, _yHeldSince = -1;
        private float _nextXRepeat, _nextYRepeat;
        private bool _controllerNavigationEnabled;

        public void ConfigureControllerNavigation(bool enabled) => _controllerNavigationEnabled = enabled;

        private void Update()
        {
            if (_controllerNavigationEnabled)
            {
                PollSticks();
                PollButtons();
            }
            PollKeyboard();
        }

        private void PollSticks()
        {
            Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick) +
                            OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);

            // horizontal
            if (Mathf.Abs(stick.x) >= Threshold)
            {
                float now = Time.unscaledTime;
                if (_xHeldSince < 0) { _xHeldSince = now; _nextXRepeat = now + RepeatSeconds; Fire(stick.x > 0 ? NavEvent.Right : NavEvent.Left); }
                else if (now >= _nextXRepeat) { _nextXRepeat = now + RepeatSeconds; Fire(stick.x > 0 ? NavEvent.Right : NavEvent.Left); }
            }
            else _xHeldSince = -1;

            // vertical
            if (Mathf.Abs(stick.y) >= Threshold)
            {
                float now = Time.unscaledTime;
                if (_yHeldSince < 0) { _yHeldSince = now; _nextYRepeat = now + RepeatSeconds; Fire(stick.y > 0 ? NavEvent.Up : NavEvent.Down); }
                else if (now >= _nextYRepeat) { _nextYRepeat = now + RepeatSeconds; Fire(stick.y > 0 ? NavEvent.Up : NavEvent.Down); }
            }
            else _yHeldSince = -1;
        }

        private void PollButtons()
        {
            if (OVRInput.GetDown(OVRInput.RawButton.A)) Fire(NavEvent.Confirm);
            if (OVRInput.GetDown(OVRInput.RawButton.B)) Fire(NavEvent.Back);
        }

        private void PollKeyboard()
        {
#if UNITY_EDITOR
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.upArrowKey.wasPressedThisFrame) Fire(NavEvent.Up);
            if (kb.downArrowKey.wasPressedThisFrame) Fire(NavEvent.Down);
            if (kb.leftArrowKey.wasPressedThisFrame) Fire(NavEvent.Left);
            if (kb.rightArrowKey.wasPressedThisFrame) Fire(NavEvent.Right);
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Fire(NavEvent.Confirm);
            if (kb.escapeKey.wasPressedThisFrame) Fire(NavEvent.Back);
#endif
        }

        private void Fire(NavEvent e) => Nav?.Invoke(e);
    }
}
