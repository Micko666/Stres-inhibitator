using StressTraining.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StressTraining.UI
{
    /// <summary>
    /// Configures Meta's standard OVRInputModule/OVRRaycaster pipeline. This
    /// class does not synthesize clicks; QuestUiRayVisual is presentation only,
    /// so a trigger press can enter the EventSystem exactly once.
    /// </summary>
    public sealed class QuestRayUiSystem : MonoBehaviour
    {
        public OVRInputModule InputModule { get; private set; }
        public OVRRaycaster CanvasRaycaster { get; private set; }
        public QuestUiRayVisual Visual { get; private set; }

        public void Initialize(OVRCameraRig rig, Transform rightControllerRay,
            UIManager uiManager)
        {
            if (rig == null || rightControllerRay == null || uiManager?.MainCanvas == null)
                throw new System.ArgumentException("Quest ray UI requires rig, right controller and MainCanvas.");

            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                var eventGo = new GameObject("QuestUIEventSystem");
                eventGo.transform.SetParent(transform, false);
                eventSystem = eventGo.AddComponent<EventSystem>();
            }
            eventSystem.sendNavigationEvents = false;

            InputModule = eventSystem.GetComponent<OVRInputModule>();
            if (InputModule == null) InputModule = eventSystem.gameObject.AddComponent<OVRInputModule>();
            foreach (var module in eventSystem.GetComponents<BaseInputModule>())
                if (module != InputModule) module.enabled = false;
            InputModule.enabled = true;
            InputModule.rayTransform = rightControllerRay;
            InputModule.joyPadClickButton = OVRInput.Button.SecondaryIndexTrigger;
            InputModule.gazeClickKey = KeyCode.None;
            InputModule.useRightStickScroll = false;
            InputModule.useSwipeScroll = false;
            InputModule.allowActivationOnMobileDevice = true;

            var canvas = uiManager.MainCanvas;
            canvas.worldCamera = rig.centerEyeAnchor.GetComponent<Camera>();
            var oldGraphic = canvas.GetComponent<GraphicRaycaster>();
            if (oldGraphic != null && !(oldGraphic is OVRRaycaster)) oldGraphic.enabled = false;
            CanvasRaycaster = canvas.GetComponent<OVRRaycaster>();
            if (CanvasRaycaster == null) CanvasRaycaster = canvas.gameObject.AddComponent<OVRRaycaster>();
            CanvasRaycaster.pointer = rightControllerRay.gameObject;
            CanvasRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.ThreeD;

            // Physical console colliders use Unity's built-in Ignore Raycast
            // layer. Exclude it both as a UI occluder and as an OVR physics
            // target so a menu trigger cannot become a second activation path.
            int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
            int rayMask = ignoreRaycastLayer >= 0
                ? ~(1 << ignoreRaycastLayer)
                : ~0;
            CanvasRaycaster.blockingMask = rayMask;

            var physicsRaycaster = rig.GetComponent<OVRPhysicsRaycaster>();
            if (physicsRaycaster == null) physicsRaycaster = rig.gameObject.AddComponent<OVRPhysicsRaycaster>();
            physicsRaycaster.eventMask = rayMask;

            Visual = rightControllerRay.GetComponent<QuestUiRayVisual>();
            if (Visual == null) Visual = rightControllerRay.gameObject.AddComponent<QuestUiRayVisual>();
            Visual.Initialize(uiManager, InputModule, CanvasRaycaster, rightControllerRay);
        }
    }

    /// <summary>Visible ray/cursor only; all hover/click events come from OVRInputModule.</summary>
    public sealed class QuestUiRayVisual : MonoBehaviour
    {
        private UIManager _ui;
        private OVRInputModule _module;
        private OVRRaycaster _raycaster;
        private Transform _origin;
        private LineRenderer _line;
        private GameObject _cursor;
        private Material _lineMaterial;

        public void Initialize(UIManager ui, OVRInputModule module,
            OVRRaycaster raycaster, Transform origin)
        {
            _ui = ui;
            _module = module;
            _raycaster = raycaster;
            _origin = origin;

            _line = GetComponent<LineRenderer>();
            if (_line == null) _line = gameObject.AddComponent<LineRenderer>();
            _line.useWorldSpace = true;
            _line.positionCount = 2;
            _line.startWidth = 0.004f;
            _line.endWidth = 0.002f;
            _line.numCapVertices = 4;
            _lineMaterial = RuntimeVisualUtil.Unlit(new Color(0.25f, 0.75f, 1f, 0.9f));
            _line.sharedMaterial = _lineMaterial;

            var cursorMaterial = RuntimeVisualUtil.Unlit(new Color(0.25f, 0.75f, 1f));
            _cursor = RuntimeVisualUtil.Primitive(PrimitiveType.Sphere, "QuestUICursor", transform,
                Vector3.zero, Vector3.one * 0.014f, cursorMaterial);
        }

        private void LateUpdate()
        {
            bool visible = _ui != null && _ui.HasActivePanel && _origin != null &&
                           _ui.MainCanvas != null && _ui.MainCanvas.gameObject.activeInHierarchy;
            if (_line != null) _line.enabled = visible;
            if (_cursor != null) _cursor.SetActive(visible);
            if (!visible) return;

            Ray ray = new Ray(_origin.position, _origin.forward);
            float distance = 4f;
            Plane plane = new Plane(_ui.MainCanvas.transform.forward, _ui.MainCanvas.transform.position);
            if (plane.Raycast(ray, out float hit) && hit > 0.05f && hit < 8f) distance = hit;
            Vector3 end = ray.GetPoint(distance);
            _line.SetPosition(0, ray.origin);
            _line.SetPosition(1, end);
            _cursor.transform.position = end;
            _cursor.transform.localScale = Vector3.one * (_raycaster != null && _raycaster.IsFocussed() ? 0.019f : 0.014f);

            Color color = _raycaster != null && _raycaster.IsFocussed()
                ? new Color(0.2f, 1f, 0.55f, 0.95f)
                : new Color(0.25f, 0.75f, 1f, 0.85f);
            if (_lineMaterial != null && _lineMaterial.HasProperty("_BaseColor"))
                _lineMaterial.SetColor("_BaseColor", color);
        }
    }
}
