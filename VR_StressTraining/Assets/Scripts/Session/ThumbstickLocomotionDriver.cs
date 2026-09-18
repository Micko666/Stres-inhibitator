using Oculus.Interaction.Locomotion;
using UnityEngine;
using OvrCharacterController = Oculus.Interaction.Locomotion.CharacterController;

namespace StressTraining.Session
{
    /// <summary>
    /// Self-contained smooth thumbstick locomotion for the Meta rig. The scene's
    /// <see cref="FirstPersonLocomotor"/> is present but its LocomotionEventsConnection
    /// handler is unset, so the built-in thumbstick input never reaches it and the
    /// player cannot move at all (dead over Link and on standalone alike).
    ///
    /// Movement is fed to the locomotor as a per-frame RELATIVE translation event, NOT
    /// a direct capsule Move: the locomotor only carries the rig along for movement IT
    /// applied (its accumulated frame delta), so a direct CharacterController.Move shifts
    /// the capsule while the camera stays put — i.e. "can't move". A Relative event is
    /// collide-and-slid AND accounted, so the rig follows. Snap turn is a rotation event.
    /// Null-safe: no-ops without a locomotor (tests).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThumbstickLocomotionDriver : MonoBehaviour
    {
        [SerializeField] private float moveSpeed = 2.5f;        // metres/second at full stick
        [SerializeField] private float deadzone = 0.15f;
        [SerializeField] private float snapTurnDegrees = 30f;
        [SerializeField] private float snapTurnThreshold = 0.7f;

        [Header("Standalone startup recovery")]
        [SerializeField, Min(0.1f)] private float recoveryGroundDistance = 2f;
        [SerializeField, Min(0.1f)] private float recoveryTimeoutSeconds = 5f;
        [SerializeField, Min(0.02f)] private float recoveryRetrySeconds = 0.2f;

        private FirstPersonLocomotor _locomotor;
        private OvrCharacterController _character;
        private Transform _head;
        private bool _snapArmed = true;
        private bool _startupRecoveryComplete;
        private bool _startupRecoveryWarningLogged;
        private float _recoveryStartedAt;
        private float _nextRecoveryAttemptAt;

        public bool HasLocomotor => _locomotor != null;
        public bool HasCharacter => _character != null;

        public void Initialize(Transform head)
        {
            _head = head;
            _locomotor = Object.FindAnyObjectByType<FirstPersonLocomotor>();
            if (_locomotor != null) _character = _locomotor.GetComponent<OvrCharacterController>();
            _startupRecoveryComplete = false;
            _startupRecoveryWarningLogged = false;
            _recoveryStartedAt = Time.unscaledTime;
            _nextRecoveryAttemptAt = _recoveryStartedAt;
        }

        private void Update()
        {
            if (_head == null || _locomotor == null) return;

            RecoverVelocityAfterRuntimeFloorSpawn();

            // Smooth move — left stick, head-relative on the ground plane. Fed as a
            // Relative translation so the locomotor moves the capsule AND carries the rig.
            Vector2 move = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
            if (move.sqrMagnitude > deadzone * deadzone)
            {
                Vector3 fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
                Vector3 right = Vector3.ProjectOnPlane(_head.right, Vector3.up).normalized;
                Vector3 delta = (right * move.x + fwd * move.y) * (moveSpeed * Time.deltaTime);
                if (delta.sqrMagnitude > 1e-8f)
                    _locomotor.HandleLocomotionEvent(new LocomotionEvent(
                        0, new Pose(delta, Quaternion.identity),
                        LocomotionEvent.TranslationType.Relative,
                        LocomotionEvent.RotationType.None));
            }

            // Snap turn — right stick, one step per flick (re-arm near centre).
            {
                float turn = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).x;
                if (_snapArmed && Mathf.Abs(turn) >= snapTurnThreshold)
                {
                    _snapArmed = false;
                    float deg = Mathf.Sign(turn) * snapTurnDegrees;
                    _locomotor.HandleLocomotionEvent(new LocomotionEvent(
                        0, new Pose(Vector3.zero, Quaternion.Euler(0f, deg, 0f)),
                        LocomotionEvent.TranslationType.None,
                        LocomotionEvent.RotationType.Relative));
                }
                else if (Mathf.Abs(turn) < snapTurnThreshold * 0.6f)
                {
                    _snapArmed = true;
                }
            }
        }

        /// <summary>
        /// Meta disables velocity permanently when its Start-time ground probe runs
        /// before our procedural SafeSpace floor and deferred teleport are ready.
        /// Never re-enable blindly: first prove that the capsule has reachable ground.
        /// This preserves Meta's fail-safe when the scene genuinely has no floor.
        /// </summary>
        private void RecoverVelocityAfterRuntimeFloorSpawn()
        {
            if (_startupRecoveryComplete) return;

            // Normal path: Meta found ground itself and never disabled velocity.
            if (!_locomotor.IgnoringVelocity)
            {
                _startupRecoveryComplete = true;
                return;
            }

            float now = Time.unscaledTime;
            if (_character != null && now >= _nextRecoveryAttemptAt)
            {
                _nextRecoveryAttemptAt = now + Mathf.Max(0.02f, recoveryRetrySeconds);
                if (_character.TryGround(Mathf.Max(0.1f, recoveryGroundDistance)))
                {
                    _locomotor.EnableMovement();
                    _startupRecoveryComplete = true;
                    Debug.Log("[ThumbstickLocomotion] Startup velocity recovered after ground became available.");
                    return;
                }
            }

            if (!_startupRecoveryWarningLogged &&
                now - _recoveryStartedAt >= Mathf.Max(0.1f, recoveryTimeoutSeconds))
            {
                _startupRecoveryWarningLogged = true;
                _startupRecoveryComplete = true;
                Debug.LogError("[ThumbstickLocomotion] Startup velocity remains disabled: " +
                    "no reachable ground was found. Gravity was not forced on to avoid an endless fall.");
            }
        }
    }
}
