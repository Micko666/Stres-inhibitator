using UnityEngine;
using StressTraining.UI;
using Oculus.Interaction.Locomotion;

namespace StressTraining.Session
{
    /// <summary>
    /// Moves the player rig between the SafeSpace and the corridor and toggles
    /// zone roots (spec §7). The rig ROOT is teleported (position + yaw) — the
    /// camera itself is never touched. All references are serialized/injected;
    /// name lookups happen once at initialization, never per frame.
    /// </summary>
    public sealed class SceneZoneController : MonoBehaviour
    {
        [SerializeField] private Transform cameraRigRoot;
        [SerializeField] private GameObject safeSpaceRoot;
        [SerializeField] private GameObject corridorRoot;
        [SerializeField] private Transform safeSpaceSpawn;
        [SerializeField] private Transform corridorSpawn;
        [SerializeField] private Transform safeSpaceUiAnchor;
        [SerializeField] private Transform corridorUiAnchor;
        [SerializeField] private SafeSpaceUiLayoutConfig safeSpaceUiLayout =
            new SafeSpaceUiLayoutConfig();

        /// <summary>
        /// The physics capsule driven by the locomotion system (PlayerCharacter).
        /// A zone switch moves the rig directly, which bypasses locomotion entirely —
        /// so the capsule MUST be moved with it. Otherwise it stays behind in the
        /// other zone, that zone's root gets deactivated, the capsule has no floor
        /// left and falls forever, dragging the player down with it.
        /// Optional: when absent (no locomotion in the scene) teleport still works.
        /// </summary>
        [SerializeField] private Transform physicsCharacter;

        public const string PhysicsCharacterName = "PlayerCharacter";

        /// <summary>
        /// Meta first-person locomotor. When present it is the ONLY reliable way to
        /// teleport: writing the rig transform directly does not stick because the
        /// locomotor re-locks the rig (its _playerOrigin) onto the CharacterController
        /// capsule every frame (CatchUpPlayerToCharacter in LastUpdate), and its
        /// roomscale catch-up moves the capsule only over short, collision-limited
        /// distances — so a direct write over any real distance is reverted within a
        /// frame. An Absolute LocomotionEvent moves the capsule via SetPosition and the
        /// rig follows, which is what actually holds. Discovered once; null in scenes
        /// without locomotion (e.g. tests), where the direct-write fallback is used.
        /// </summary>
        private FirstPersonLocomotor _locomotor;

        public enum Zone { None, SafeSpace, Corridor }
        public Zone CurrentZone { get; private set; } = Zone.None;

        /// <summary>Raised after every zone switch (robot arm presenter, ambience…).</summary>
        public event System.Action<Zone> ZoneChanged;

        public Transform SafeSpaceUiAnchor => safeSpaceUiAnchor;
        public Transform CorridorUiAnchor => corridorUiAnchor;
        public Transform SafeSpaceSpawn => safeSpaceSpawn;
        public Transform CorridorSpawn => corridorSpawn;
        public SafeSpaceUiLayoutConfig SafeSpaceUiLayout => safeSpaceUiLayout;

        public void Initialize(Transform rig, GameObject safeSpace, GameObject corridor,
            Transform safeSpawn, Transform corrSpawn, Transform safeUi, Transform corrUi,
            SafeSpaceUiLayoutConfig uiLayout = null)
        {
            cameraRigRoot = rig;
            safeSpaceRoot = safeSpace;
            corridorRoot = corridor;
            safeSpaceSpawn = safeSpawn;
            corridorSpawn = corrSpawn;
            safeSpaceUiAnchor = safeUi;
            corridorUiAnchor = corrUi;
            // One-time discovery (never per frame), matching this class's contract.
            // The Meta locomotor is the authoritative teleport path; the physics
            // capsule is the object it lives on (the scene names it "PlayerController",
            // not "PlayerCharacter"), used by the direct-write fallback.
            if (_locomotor == null)
                _locomotor = Object.FindAnyObjectByType<FirstPersonLocomotor>();
            if (physicsCharacter == null)
            {
                var character = GameObject.Find(PhysicsCharacterName);
                if (character == null && _locomotor != null) character = _locomotor.gameObject;
                if (character != null) physicsCharacter = character.transform;
            }
            if (uiLayout != null) safeSpaceUiLayout = uiLayout;
            safeSpaceUiLayout ??= new SafeSpaceUiLayoutConfig();
            WorldSpaceUiOrientation.PlaceFromSpawn(safeSpaceUiAnchor, safeSpaceSpawn,
                safeSpaceUiLayout.distanceMeters, safeSpaceUiLayout.eyeHeightMeters);
            if (safeSpaceUiAnchor != null &&
                safeSpaceUiAnchor.GetComponent<SafeSpaceUiAnchorGizmo>() == null)
                safeSpaceUiAnchor.gameObject.AddComponent<SafeSpaceUiAnchorGizmo>();
            WorldSpaceUiOrientation.PlaceFromSpawn(corridorUiAnchor, corridorSpawn, 1.0f, 1.55f);
        }

        public void EnterSafeSpace()
        {
            if (safeSpaceRoot != null) safeSpaceRoot.SetActive(true);
            if (corridorRoot != null) corridorRoot.SetActive(false);
            Teleport(safeSpaceSpawn);
            CurrentZone = Zone.SafeSpace;
            ZoneChanged?.Invoke(CurrentZone);
        }

        public void EnterCorridor()
        {
            if (corridorRoot != null) corridorRoot.SetActive(true);
            if (safeSpaceRoot != null) safeSpaceRoot.SetActive(false);
            Teleport(corridorSpawn);
            CurrentZone = Zone.Corridor;
            ZoneChanged?.Invoke(CurrentZone);
        }

        // ── Safe Space recenter (comfort) ────────────────────────────────
        // In the Safe Space the participant can wander (roomscale / locomotion) off
        // the platform with no obvious way back. Pressing B (right controller) drops
        // them just above the platform spawn so it is always recoverable. Gated to the
        // Safe Space so it can never move the player during a corridor session; the
        // height offset drops the participant in ABOVE the platform (never clipping
        // into it) and lets the capsule settle down onto the collider. B is also the
        // UI "Back", which is harmless here (the Safe Space menu Back is a no-op at
        // its root).
        // A metre above, so the capsule drops in ABOVE the platform and settles onto
        // its collider (never clipping in). Bigger values = a longer, more nauseating
        // drop, since the locomotor teleport places the feet exactly here and gravity
        // does the rest; 0 would land exactly on the surface with no drop at all.
        private const float RecenterHeightOffsetMeters = 1.0f;

        private void Update()
        {
            if (CurrentZone != Zone.SafeSpace) return;
            if (OVRInput.GetDown(OVRInput.RawButton.B))
                RecenterToSafeSpaceSpawn();
        }

        /// <summary>Returns the rig (and physics capsule) to just above the Safe spawn.</summary>
        public void RecenterToSafeSpaceSpawn()
        {
            if (safeSpaceSpawn != null) Teleport(safeSpaceSpawn, RecenterHeightOffsetMeters);
        }

        private void Teleport(Transform target, float heightOffset = 0f)
        {
            if (target == null) return;
            Vector3 position = target.position + Vector3.up * heightOffset;
            Quaternion yaw = Quaternion.Euler(0, target.eulerAngles.y, 0);

            // Authoritative path: fire the locomotor's own absolute teleport. Absolute
            // moves the capsule's FEET to the target (via SetPosition), then the rig is
            // synced to follow. This is the only teleport that holds — see _locomotor.
            if (_locomotor != null)
            {
                _locomotor.HandleLocomotionEvent(new LocomotionEvent(
                    0, new Pose(position, yaw),
                    LocomotionEvent.TranslationType.Absolute,
                    LocomotionEvent.RotationType.Absolute));
                return;
            }

            // Fallback (no locomotion in the scene, e.g. tests): move rig + capsule
            // directly. Correct only when nothing re-locks the rig to the capsule.
            if (cameraRigRoot != null)
                cameraRigRoot.SetPositionAndRotation(position, yaw);
            if (physicsCharacter != null)
                physicsCharacter.SetPositionAndRotation(position, yaw);
        }
    }
}
