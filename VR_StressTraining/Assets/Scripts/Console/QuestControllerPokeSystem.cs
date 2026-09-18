using System;
using System.Linq;
using Oculus.Interaction;
using Oculus.Interaction.Input;
using UnityEngine;

namespace StressTraining.Console
{
    /// <summary>
    /// Small adapter that exposes the already tracked OVR controller anchors to
    /// Meta Interaction SDK's official ControllerPokeInteractor prefab.
    /// It does not perform hit testing or synthesize poke events; PokeInteractor
    /// remains the sole direct-interaction implementation.
    /// </summary>
    public sealed class OVRControllerPokeSource : MonoBehaviour, IController
    {
        [SerializeField] private Transform trackedAnchor;
        [SerializeField] private bool leftHand;
        private OVRCameraRig _rig;

        private ControllerInput _input;
        private OVRInput.Controller OvrController => leftHand
            ? OVRInput.Controller.LTouch
            : OVRInput.Controller.RTouch;

        public Handedness Handedness => leftHand ? Handedness.Left : Handedness.Right;
        public float Scale => trackedAnchor != null ? trackedAnchor.lossyScale.x : 1f;
        public bool IsConnected => trackedAnchor != null &&
            (OVRInput.GetConnectedControllers() & OvrController) != 0;
        public bool IsPoseValid => IsConnected && trackedAnchor.gameObject.activeInHierarchy;
        public ControllerInput ControllerInput => _input;

        public event Action WhenUpdated = delegate { };

        public void Configure(OVRCameraRig rig, Transform anchor, bool isLeft)
        {
            if (isActiveAndEnabled && _rig != null)
                _rig.UpdatedAnchors -= HandleUpdatedAnchors;
            _rig = rig;
            trackedAnchor = anchor;
            leftHand = isLeft;
            if (isActiveAndEnabled && _rig != null)
                _rig.UpdatedAnchors += HandleUpdatedAnchors;
        }

        private void OnEnable()
        {
            if (_rig != null) _rig.UpdatedAnchors += HandleUpdatedAnchors;
        }

        private void OnDisable()
        {
            if (_rig != null) _rig.UpdatedAnchors -= HandleUpdatedAnchors;
        }

        private void HandleUpdatedAnchors(OVRCameraRig _)
        {
            RefreshInput();
            WhenUpdated.Invoke();
        }

        public bool TryGetPose(out Pose pose) => TryGetTrackedPose(out pose);
        public bool TryGetPointerPose(out Pose pose) => TryGetTrackedPose(out pose);

        private bool TryGetTrackedPose(out Pose pose)
        {
            if (!IsPoseValid)
            {
                pose = Pose.identity;
                return false;
            }

            pose = new Pose(trackedAnchor.position, trackedAnchor.rotation);
            return true;
        }

        public bool IsButtonUsageAnyActive(ControllerButtonUsage buttonUsage) =>
            (_input.ButtonUsageMask & buttonUsage) != 0;

        public bool IsButtonUsageAllActive(ControllerButtonUsage buttonUsage) =>
            (_input.ButtonUsageMask & buttonUsage) == buttonUsage;

        private void RefreshInput()
        {
            _input.Clear();
            if (!IsConnected) return;

            float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OvrController);
            float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OvrController);
            _input.SetAxis1D(ControllerAxis1DUsage.Trigger, trigger);
            _input.SetAxis1D(ControllerAxis1DUsage.Grip, grip);
            _input.SetAxis2D(ControllerAxis2DUsage.Primary2DAxis,
                OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OvrController));
            _input.SetButton(ControllerButtonUsage.TriggerButton, trigger > 0.5f);
            _input.SetButton(ControllerButtonUsage.GripButton, grip > 0.5f);
            _input.SetButton(ControllerButtonUsage.PrimaryButton,
                OVRInput.Get(OVRInput.Button.One, OvrController));
            _input.SetButton(ControllerButtonUsage.SecondaryButton,
                OVRInput.Get(OVRInput.Button.Two, OvrController));
            _input.SetButton(ControllerButtonUsage.MenuButton,
                OVRInput.Get(OVRInput.Button.Start, OvrController));
            _input.SetButton(ControllerButtonUsage.Primary2DAxisClick,
                OVRInput.Get(OVRInput.Button.PrimaryThumbstick, OvrController));
        }
    }

    /// <summary>
    /// Instantiates one official Meta ControllerPokeInteractor for each tracked
    /// Touch controller and injects the OVR pose adapter before Start executes.
    /// </summary>
    public sealed class QuestControllerPokeSystem : MonoBehaviour
    {
        private const string ResourcePath = "QuestPokePrefabReference";
        private const string LeftName = "QuestPokeInteractor_Left";
        private const string RightName = "QuestPokeInteractor_Right";

        public bool IsInitialized { get; private set; }
        public string Status { get; private set; } = "Not initialized";

        public void Initialize(OVRCameraRig rig)
        {
            if (IsInitialized) return;
            if (rig == null || rig.leftControllerAnchor == null || rig.rightControllerAnchor == null)
            {
                Status = "OVRCameraRig controller anchors missing";
                Debug.LogError("[QuestControllerPokeSystem] " + Status);
                return;
            }

            // MainScene already contains Meta's OVRInteractionComprehensive
            // building block. Reuse its controller poke interactors instead of
            // creating a second pair (which would double Select and haptics).
            PokeInteractor[] sceneControllerPokes = rig
                .GetComponentsInChildren<PokeInteractor>(true)
                .Where(poke => poke != null &&
                    (poke.GetComponent<ControllerRef>() != null ||
                     poke.GetComponentInParent<ControllerRef>() != null))
                .ToArray();
            if (sceneControllerPokes.Length > 0)
            {
                IsInitialized = sceneControllerPokes.Length >= 2;
                Status = IsInitialized
                    ? "Existing Meta OVRInteractionComprehensive controller poke interactors ready; physical behavior requires Quest verification"
                    : "Existing Meta controller poke setup is incomplete";
                if (IsInitialized) Debug.Log("[QuestControllerPokeSystem] " + Status);
                else Debug.LogError("[QuestControllerPokeSystem] " + Status);
                return;
            }

            QuestPokePrefabReference reference =
                Resources.Load<QuestPokePrefabReference>(ResourcePath);
            GameObject prefab = reference != null ? reference.ControllerPokeInteractor : null;
            if (prefab == null)
            {
                Status = "Meta ControllerPokeInteractor resource missing";
                Debug.LogError("[QuestControllerPokeSystem] " + Status);
                return;
            }

            bool leftReady = EnsureInteractor(prefab, rig, rig.leftControllerAnchor, true, LeftName);
            bool rightReady = EnsureInteractor(prefab, rig, rig.rightControllerAnchor, false, RightName);
            IsInitialized = leftReady && rightReady;
            Status = IsInitialized
                ? "Meta controller poke interactors ready; physical behavior requires Quest verification"
                : "One or more controller poke interactors failed to initialize";
            if (IsInitialized) Debug.Log("[QuestControllerPokeSystem] " + Status);
            else Debug.LogError("[QuestControllerPokeSystem] " + Status);
        }

        private static bool EnsureInteractor(GameObject prefab, OVRCameraRig rig, Transform anchor,
            bool left, string objectName)
        {
            Transform existing = anchor.Find(objectName);
            GameObject instance = existing != null
                ? existing.gameObject
                : Instantiate(prefab, anchor, false);
            instance.name = objectName;

            var source = instance.GetComponent<OVRControllerPokeSource>() ??
                         instance.AddComponent<OVRControllerPokeSource>();
            source.Configure(rig, anchor, left);

            var controllerRef = instance.GetComponent<ControllerRef>();
            var pointerPose = instance.GetComponentInChildren<ControllerPointerPose>(true);
            var pokeInteractor = instance.GetComponent<PokeInteractor>();
            if (controllerRef == null || pointerPose == null || pokeInteractor == null)
            {
                Debug.LogError("[QuestControllerPokeSystem] Invalid Meta poke prefab: " + objectName);
                return false;
            }

            controllerRef.InjectController(source);
            pointerPose.InjectController(controllerRef);
            return true;
        }
    }
}
