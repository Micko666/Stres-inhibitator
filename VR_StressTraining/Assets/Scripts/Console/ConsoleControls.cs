using UnityEngine;

namespace StressTraining.Console
{
    /// <summary>Momentary push button — the primary task input.</summary>
    public sealed class ConsoleButtonControl : ConsoleControlBase
    {
        public override ConsoleControlType ControlType => ConsoleControlType.Button;

        [SerializeField] private Transform cap;             // visual that dips on press
        [SerializeField] private float pressDepth = 0.008f;
        private Vector3 _capRest;
        private float _releaseAtRealtime = -1;

        public void ConfigureCap(Transform capTransform)
        {
            cap = capTransform;
            _capRest = cap.localPosition;
        }

        protected override float OnActivated()
        {
            // Meta poke keeps the cap depressed until Unselect/Cancel. A short
            // pulse remains for explicit programmatic activation in EditMode.
            if (cap != null && !HasActivePokePress)
            {
                SetPressed(true);
                _releaseAtRealtime = Time.realtimeSinceStartup + 0.12f;
            }
            return 1f;
        }

        protected override void OnPokePressStarted()
        {
            _releaseAtRealtime = -1;
            SetPressed(true);
        }

        protected override void OnPokePressEnded()
        {
            _releaseAtRealtime = -1;
            SetPressed(false);
        }

        protected override void Update()
        {
            base.Update();
            if (_releaseAtRealtime > 0 && Time.realtimeSinceStartup >= _releaseAtRealtime)
            {
                _releaseAtRealtime = -1;
                SetPressed(false);
            }
        }

        private void SetPressed(bool pressed)
        {
            if (cap != null)
                cap.localPosition = pressed
                    ? _capRest - new Vector3(0, pressDepth, 0)
                    : _capRest;
        }
    }

    /// <summary>Two-state toggle (modularity demonstration; distractor in MVP tasks).</summary>
    public sealed class ConsoleToggleControl : ConsoleControlBase
    {
        public override ConsoleControlType ControlType => ConsoleControlType.Toggle;

        [SerializeField] private Transform head;
        public bool IsOn { get; private set; }

        public void ConfigureHead(Transform h) => head = h;

        protected override float OnActivated()
        {
            IsOn = !IsOn;
            if (head != null)
                head.localRotation = Quaternion.Euler(IsOn ? -25f : 25f, 0, 0);
            return IsOn ? 1f : 0f;
        }
    }

    /// <summary>Two-position lever. Poking flips it (MVP physical metaphor).</summary>
    public sealed class ConsoleLeverControl : ConsoleControlBase
    {
        public override ConsoleControlType ControlType => ConsoleControlType.Lever;

        [SerializeField] private Transform arm;
        public bool IsUp { get; private set; } = true;

        public void ConfigureArm(Transform a)
        {
            arm = a;
            if (arm != null) arm.localRotation = Quaternion.Euler(-35f, 0, 0);
        }

        protected override float OnActivated()
        {
            IsUp = !IsUp;
            if (arm != null)
                arm.localRotation = Quaternion.Euler(IsUp ? -35f : 35f, 0, 0);
            return IsUp ? 1f : 0f;
        }
    }

    /// <summary>Stepped rotary knob. Each poke advances one step (MVP demonstration).</summary>
    public sealed class ConsoleKnobControl : ConsoleControlBase
    {
        public override ConsoleControlType ControlType => ConsoleControlType.Knob;

        [SerializeField] private Transform dial;
        [SerializeField] private int stepCount = 8;
        public int Step { get; private set; }

        public void ConfigureDial(Transform d) => dial = d;

        protected override float OnActivated()
        {
            Step = (Step + 1) % stepCount;
            if (dial != null)
                dial.localRotation = Quaternion.Euler(0, Step * (360f / stepCount), 0);
            return Step;
        }
    }

    /// <summary>Non-interactive indicator light (state feedback only).</summary>
    public sealed class ConsoleIndicatorLight : ConsoleControlBase
    {
        public override ConsoleControlType ControlType => ConsoleControlType.IndicatorLight;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        [SerializeField] private Color onColor = new Color(0.2f, 0.9f, 0.3f);
        public bool IsOn { get; private set; }

        private void Awake() => Interactable = false; // never poke-able

        public void SetOn(bool on)
        {
            IsOn = on;
            if (visualRenderer == null) return;
            var mat = visualRenderer.material;
            Color c = on ? onColor : new Color(0.12f, 0.12f, 0.12f);
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, c);
            if (mat.HasProperty(EmissionColorId))
            {
                if (on) mat.EnableKeyword("_EMISSION"); else mat.DisableKeyword("_EMISSION");
                mat.SetColor(EmissionColorId, on ? onColor * 1.6f : Color.black);
            }
        }

        public void SetColor(Color c)
        {
            onColor = c;
            if (IsOn) SetOn(true);
        }

        protected override float OnActivated() => IsOn ? 1f : 0f;
    }

    /// <summary>
    /// Legacy marker retained for old scenes. Standard console input now uses
    /// Meta Interaction SDK PokeInteractor/PokeInteractable directly.
    /// </summary>
    [System.Obsolete("Use Meta Interaction SDK PokeInteractor instead.")]
    public sealed class ConsolePokeTip : MonoBehaviour { }
}
