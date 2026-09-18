using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Feedback;
using StressTraining.Core;
using UnityEngine;

namespace StressTraining.Console
{
    /// <summary>
    /// Base for a semantic console control. Direct Quest interaction is supplied
    /// by Meta Interaction SDK's PokeInteractable; Unity EventSystem ray clicks
    /// deliberately do not activate physical console controls.
    /// </summary>
    public abstract class ConsoleControlBase : MonoBehaviour
    {
        [SerializeField] private string controlId;
        [SerializeField] private bool isDistractor;
        [SerializeField] private bool interactable = true;
        [SerializeField] private PokeInteractable pokeInteractable;
        [SerializeField] protected Renderer visualRenderer;

        private readonly HashSet<int> _hoveringPointers = new HashSet<int>();
        private readonly HashSet<int> _selectingPointers = new HashSet<int>();
        private Color _baseColor = Color.white;
        private float _flashUntilRealtime;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        protected float DebounceSeconds = 0.25f;
        private double _lastAcceptedRealtime = -10;

        public string ControlId => controlId;
        public bool IsDistractor => isDistractor;
        public PokeInteractable PokeInteractable => pokeInteractable;
        public bool HasActivePokePress => _selectingPointers.Count > 0;
        public abstract ConsoleControlType ControlType { get; }

        /// <summary>
        /// True only when the package FeedbackManager prefab is present. Poke
        /// remains functional without it, but the application must not claim
        /// that controller haptics are active.
        /// </summary>
        public static bool MetaHapticsAvailable => FeedbackManager.Instance != null;
        public static string MetaHapticsStatus => MetaHapticsAvailable
            ? "Meta FeedbackManager present - physical haptics require Quest verification"
            : "Meta FeedbackManager missing - direct poke works without haptics";

        public bool Interactable
        {
            get => interactable;
            set
            {
                if (interactable == value) return;
                interactable = value;
                if (pokeInteractable != null) pokeInteractable.enabled = value;
                if (!value) ResetPointerState();
                ApplyInteractionVisual();
            }
        }

        /// <summary>Raised once for every accepted physical press.</summary>
        public event Action<ConsoleControlBase, ConsoleControlEvent> Activated;

        // Optional project feedback hooks. Meta's FeedbackManager handles
        // controller haptics directly from the PokeInteractable event stream.
        public Action<ConsoleControlBase> HapticFeedback;
        public Action<ConsoleControlBase> AudioFeedback;

        public void Configure(string id, bool distractor, Renderer visual)
        {
            controlId = id;
            isDistractor = distractor;
            visualRenderer = visual;
            if (TryGetVisualColor(out Color color)) _baseColor = color;
            ApplyInteractionVisual();
        }

        /// <summary>Connects the control to a Meta Interaction SDK poke target.</summary>
        public void ConfigurePoke(PokeInteractable target)
        {
            if (pokeInteractable != null)
                pokeInteractable.WhenPointerEventRaised -= HandlePokePointerEvent;

            pokeInteractable = target;
            if (pokeInteractable != null)
            {
                // Idempotent reconfiguration after a domain reload or serialized
                // runtime root: remove any stale duplicate before subscribing.
                pokeInteractable.WhenPointerEventRaised -= HandlePokePointerEvent;
                pokeInteractable.WhenPointerEventRaised += HandlePokePointerEvent;
                pokeInteractable.enabled = interactable;
            }
        }

        /// <summary>
        /// Meta pointer bridge. A pointer identifier is latched on Select and is
        /// released only by Unselect/Cancel, so duplicate Select notifications
        /// during one physical press cannot emit duplicate semantic actions.
        /// </summary>
        public void HandlePokePointerEvent(PointerEvent pointerEvent)
        {
            switch (pointerEvent.Type)
            {
                case PointerEventType.Hover:
                    if (interactable) _hoveringPointers.Add(pointerEvent.Identifier);
                    ApplyInteractionVisual();
                    break;

                case PointerEventType.Unhover:
                    _hoveringPointers.Remove(pointerEvent.Identifier);
                    ApplyInteractionVisual();
                    break;

                case PointerEventType.Select:
                    if (!interactable || !_selectingPointers.Add(pointerEvent.Identifier)) return;
                    _hoveringPointers.Add(pointerEvent.Identifier);
                    bool firstSelectingPointer = _selectingPointers.Count == 1;
                    if (firstSelectingPointer) OnPokePressStarted();
                    ApplyInteractionVisual();
                    // The latch is control-wide, not merely per pointer ID. If a
                    // second controller/hand touches an already held control it
                    // joins the pressed state without emitting another answer.
                    if (firstSelectingPointer) Activate(enforceTimeDebounce: false);
                    break;

                case PointerEventType.Unselect:
                    ReleasePointer(pointerEvent.Identifier, removeHover: false);
                    break;

                case PointerEventType.Cancel:
                    ReleasePointer(pointerEvent.Identifier, removeHover: true);
                    break;
            }
        }

        /// <summary>
        /// Programmatic activation retained for tests and non-pointer adapters.
        /// Direct poke uses the per-pointer latch above as its debounce.
        /// </summary>
        public void PhysicalActivate() => Activate(enforceTimeDebounce: true);

        private void Activate(bool enforceTimeDebounce)
        {
            if (!interactable) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (enforceTimeDebounce && now - _lastAcceptedRealtime < DebounceSeconds) return;
            _lastAcceptedRealtime = now;

            float value = OnActivated();
            var evt = new ConsoleControlEvent
            {
                ControlId = controlId,
                ControlType = ControlType,
                Value = value,
                TimestampUtcIso = UtcTime.NowIso(),
                MonotonicSeconds = now,
                IsDistractor = isDistractor
            };

            FlashVisual();
            HapticFeedback?.Invoke(this);
            AudioFeedback?.Invoke(this);
            Activated?.Invoke(this, evt);
        }

        protected abstract float OnActivated();
        protected virtual void OnPokePressStarted() { }
        protected virtual void OnPokePressEnded() { }

        protected void FlashVisual()
        {
            if (visualRenderer == null) return;
            SetVisualColor(Color.Lerp(_baseColor, Color.white, 0.70f));
            _flashUntilRealtime = Time.realtimeSinceStartup + 0.12f;
        }

        protected virtual void Update()
        {
            if (_flashUntilRealtime > 0 && Time.realtimeSinceStartup >= _flashUntilRealtime)
            {
                _flashUntilRealtime = 0;
                ApplyInteractionVisual();
            }
        }

        protected virtual void OnDestroy()
        {
            if (pokeInteractable != null)
                pokeInteractable.WhenPointerEventRaised -= HandlePokePointerEvent;
        }

        private void ReleasePointer(int identifier, bool removeHover)
        {
            bool wasPressed = _selectingPointers.Count > 0;
            _selectingPointers.Remove(identifier);
            if (removeHover) _hoveringPointers.Remove(identifier);
            if (wasPressed && _selectingPointers.Count == 0)
            {
                // A completed release is the debounce boundary. The next Select
                // represents a new physical press even if it happens quickly.
                _lastAcceptedRealtime = -10;
                OnPokePressEnded();
            }
            ApplyInteractionVisual();
        }

        private void ResetPointerState()
        {
            bool wasPressed = _selectingPointers.Count > 0;
            _selectingPointers.Clear();
            _hoveringPointers.Clear();
            _lastAcceptedRealtime = -10;
            if (wasPressed) OnPokePressEnded();
        }

        private void ApplyInteractionVisual()
        {
            if (visualRenderer == null) return;
            Color color;
            if (!interactable)
                color = Color.Lerp(_baseColor, Color.black, 0.72f);
            else if (_selectingPointers.Count > 0)
                color = Color.Lerp(_baseColor, Color.white, 0.62f);
            else if (_hoveringPointers.Count > 0)
                color = Color.Lerp(_baseColor, Color.white, 0.28f);
            else
                color = _baseColor;
            SetVisualColor(color);
        }

        private bool TryGetVisualColor(out Color color)
        {
            color = Color.white;
            if (visualRenderer == null) return false;
            // READ-ONLY, so sharedMaterial is correct. Using .material here made
            // Unity instantiate a material on every Configure — which leaks a material
            // per control in edit mode (the installer and every EditMode test that
            // builds the console logged an error for it).
            Material material = visualRenderer.sharedMaterial;
            if (material == null) return false;
            if (material.HasProperty(BaseColorId))
            {
                color = material.GetColor(BaseColorId);
                return true;
            }
            if (material.HasProperty(ColorId))
            {
                color = material.GetColor(ColorId);
                return true;
            }
            return false;
        }

        private void SetVisualColor(Color color)
        {
            if (visualRenderer == null) return;
            Material material = visualRenderer.material;
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
            else if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
        }
    }
}
