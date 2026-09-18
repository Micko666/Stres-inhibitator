using StressTraining.Core;
using UnityEngine;

namespace StressTraining.Pressure
{
    /// <summary>
    /// Asset-free transient pressure effects. All objects are built once and reused.
    /// No Rigidbody, physics debris, scene searches or per-frame allocations.
    /// </summary>
    public sealed class PressureTransientFxController : MonoBehaviour
    {
        private Transform _fxRoot;
        private Transform _dustRoot;
        private readonly Transform[] _dustMotes =
            new Transform[PressureHeuristics.DustMoteCount];
        private readonly Vector3[] _dustRest =
            new Vector3[PressureHeuristics.DustMoteCount];
        private readonly Vector3[] _dustVelocity =
            new Vector3[PressureHeuristics.DustMoteCount];
        private GameObject _ceilingSignal;
        private Vector3 _dustDefaultLocalPosition;
        private Quaternion _dustDefaultLocalRotation;
        private Vector3 _ceilingDefaultLocalPosition;
        private Quaternion _ceilingDefaultLocalRotation;
        private Vector3 _ceilingActiveLocalPosition;
        private bool _initialized;
        private float _dustElapsed = -1f;
        private float _dustMagnitude;
        private float _ceilingElapsed = -1f;
        private float _ceilingMagnitude;

        public bool IsInitialized => _initialized;

        // REMOVED: the "critical void" — a near-black 3.25 x 3.1 m cube parked behind
        // the user and switched on at the Critical stage. It dated from before the
        // modular puzzle corridor, when nothing else could suggest a void. Now the
        // collapsing segments expose the real space behind them, so the cube only read
        // as a black square floating in the room. A proper sky backdrop replaces it.
        public bool CriticalVoidVisible => false;

        public void Initialize(Transform pressureRoot, Vector3 userLocalPosition,
            Vector3 userForwardLocal)
        {
            if (_initialized || pressureRoot == null) return;

            userForwardLocal.y = 0f;
            if (userForwardLocal.sqrMagnitude < 0.0001f)
                userForwardLocal = Vector3.back;
            else
                userForwardLocal.Normalize();

            _fxRoot = new GameObject("PressureTransientFx").transform;
            _fxRoot.SetParent(pressureRoot, false);

            BuildDust(userLocalPosition, userForwardLocal);
            // REMOVED: BuildCeilingSignal — a 2.5 x 0.72 m emissive brown slab that
            // flashed overhead on CeilingCreak events. It was a stand-in from before
            // the modular puzzle corridor existed; the real ceiling segments now carry
            // that signal, so the slab only read as an unexplained brown square.
            ResetEffects();
            _initialized = true;
        }

        private void BuildDust(Vector3 userLocalPosition,
            Vector3 userForwardLocal)
        {
            _dustRoot = new GameObject("DustBurst").transform;
            _dustRoot.SetParent(_fxRoot, false);
            Vector3 center = userLocalPosition + userForwardLocal *
                PressureHeuristics.DustForwardDistanceMeters;
            _dustRoot.localPosition = new Vector3(center.x,
                userLocalPosition.y + 0.12f, center.z);
            _dustRoot.localRotation = Quaternion.LookRotation(userForwardLocal,
                Vector3.up);
            _dustDefaultLocalPosition = _dustRoot.localPosition;
            _dustDefaultLocalRotation = _dustRoot.localRotation;

            var material = RuntimeVisualUtil.Unlit(new Color(0.47f, 0.44f, 0.40f, 1f));
            for (int i = 0; i < PressureHeuristics.DustMoteCount; i++)
            {
                float x = ((i % 4) - 1.5f) * 0.22f;
                float y = (i / 4) * 0.08f;
                float z = ((i * 5) % 7 - 3) * 0.045f;
                float scale = 0.025f + (i % 3) * 0.008f;
                var mote = RuntimeVisualUtil.Primitive(PrimitiveType.Sphere, "DustMote" + i,
                    _dustRoot, new Vector3(x, y, z), Vector3.one * scale, material);
                _dustMotes[i] = mote.transform;
                _dustRest[i] = mote.transform.localPosition;
                _dustVelocity[i] = new Vector3(
                    ((i % 5) - 2f) * 0.075f,
                    0.18f + (i % 4) * 0.035f,
                    (((i * 3) % 5) - 2f) * 0.04f);
            }
        }

        private void BuildCeilingSignal(Vector3 userLocalPosition,
            Vector3 userForwardLocal)
        {
            Vector3 center = userLocalPosition + userForwardLocal *
                PressureHeuristics.CeilingEventForwardDistanceMeters;
            _ceilingSignal = RuntimeVisualUtil.Primitive(PrimitiveType.Cube,
                "ControlledCeilingSignal", _fxRoot,
                new Vector3(center.x, userLocalPosition.y + 2.88f, center.z),
                new Vector3(2.5f, 0.035f, 0.72f),
                RuntimeVisualUtil.Emissive(new Color(0.15f, 0.14f, 0.13f),
                    new Color(0.35f, 0.16f, 0.08f), 0.35f));
            _ceilingSignal.transform.localRotation =
                Quaternion.LookRotation(userForwardLocal, Vector3.up);
            _ceilingDefaultLocalPosition = _ceilingSignal.transform.localPosition;
            _ceilingDefaultLocalRotation = _ceilingSignal.transform.localRotation;
            _ceilingActiveLocalPosition = _ceilingDefaultLocalPosition;
        }

        public void TriggerDustBurst(float magnitude)
        {
            if (!_initialized) return;
            _dustMagnitude = Mathf.Clamp01(magnitude);
            _dustElapsed = 0f;
            _dustRoot.localPosition = _dustDefaultLocalPosition;
            _dustRoot.localRotation = _dustDefaultLocalRotation;
            _dustRoot.gameObject.SetActive(true);
            for (int i = 0; i < _dustMotes.Length; i++)
            {
                _dustMotes[i].localPosition = _dustRest[i];
                _dustMotes[i].localScale = Vector3.one *
                    (0.025f + (i % 3) * 0.008f);
            }
        }


        public void TriggerDustBurstAt(float magnitude, Vector3 worldPosition)
        {
            TriggerDustBurst(magnitude);
            if (!_initialized || _dustRoot == null) return;
            _dustRoot.position = worldPosition;
        }

        /// <summary>
        /// No-op while the ceiling slab is not built (see Initialize). PressureController
        /// still fires CeilingCreak events; they now only drive audio and the real
        /// puzzle ceiling segments.
        /// </summary>
        public void TriggerCeilingSignal(float magnitude)
        {
            if (!_initialized || _ceilingSignal == null) return;
            _ceilingMagnitude = Mathf.Clamp01(magnitude);
            _ceilingElapsed = 0f;
            _ceilingActiveLocalPosition = _ceilingDefaultLocalPosition;
            _ceilingSignal.transform.localPosition = _ceilingActiveLocalPosition;
            _ceilingSignal.transform.localRotation = _ceilingDefaultLocalRotation;
            _ceilingSignal.SetActive(true);
        }

        public void TriggerCeilingSignalAt(float magnitude, Vector3 worldPosition)
        {
            TriggerCeilingSignal(magnitude);
            if (!_initialized || _ceilingSignal == null) return;
            _ceilingSignal.transform.position = worldPosition;
            _ceilingActiveLocalPosition = _ceilingSignal.transform.localPosition;
        }

        /// <summary>
        /// No-op. Kept so PressureController's stage/finale calls stay unchanged;
        /// see <see cref="CriticalVoidVisible"/> for why the cube is gone.
        /// </summary>
        public void SetCriticalVoidVisible(bool visible) { _ = visible; }

        /// <summary>Advanced only from PressureController with active session delta time.</summary>
        public void TickActive(float dt)
        {
            if (!_initialized || dt <= 0f) return;
            TickDust(dt);
            TickCeiling(dt);
        }

        private void TickDust(float dt)
        {
            if (_dustElapsed < 0f) return;
            _dustElapsed += dt;
            float normalized = Mathf.Clamp01(_dustElapsed /
                PressureHeuristics.DustDurationSeconds);
            float strength = Mathf.Lerp(0.45f, 1f, _dustMagnitude);
            for (int i = 0; i < _dustMotes.Length; i++)
            {
                Vector3 position = _dustRest[i] + _dustVelocity[i] *
                    (_dustElapsed * strength);
                position.y -= 0.16f * _dustElapsed * _dustElapsed;
                _dustMotes[i].localPosition = position;
                _dustMotes[i].localScale = Vector3.one *
                    (0.025f + (i % 3) * 0.008f) * (1f - normalized * 0.75f);
            }

            if (_dustElapsed >= PressureHeuristics.DustDurationSeconds)
            {
                _dustElapsed = -1f;
                _dustRoot.localPosition = _dustDefaultLocalPosition;
                _dustRoot.localRotation = _dustDefaultLocalRotation;
                _dustRoot.gameObject.SetActive(false);
            }
        }

        private void TickCeiling(float dt)
        {
            if (_ceilingElapsed < 0f) return;
            _ceilingElapsed += dt;
            float normalized = Mathf.Clamp01(_ceilingElapsed /
                PressureHeuristics.CeilingEventDurationSeconds);
            float pulse = Mathf.Sin(normalized * Mathf.PI);
            _ceilingSignal.transform.localPosition = _ceilingActiveLocalPosition +
                Vector3.down * (PressureHeuristics.CeilingEventTravelMeters *
                    _ceilingMagnitude * pulse);

            if (_ceilingElapsed >= PressureHeuristics.CeilingEventDurationSeconds)
            {
                _ceilingElapsed = -1f;
                _ceilingSignal.transform.localPosition = _ceilingActiveLocalPosition;
                _ceilingSignal.SetActive(false);
            }
        }

        public void ResetEffects()
        {
            _dustElapsed = -1f;
            _dustMagnitude = 0f;
            if (_dustRoot != null)
            {
                for (int i = 0; i < _dustMotes.Length; i++)
                {
                    if (_dustMotes[i] == null) continue;
                    _dustMotes[i].localPosition = _dustRest[i];
                    _dustMotes[i].localScale = Vector3.one *
                        (0.025f + (i % 3) * 0.008f);
                }
                _dustRoot.localPosition = _dustDefaultLocalPosition;
                _dustRoot.localRotation = _dustDefaultLocalRotation;
                _dustRoot.gameObject.SetActive(false);
            }

            _ceilingElapsed = -1f;
            _ceilingMagnitude = 0f;
            if (_ceilingSignal != null)
            {
                _ceilingActiveLocalPosition = _ceilingDefaultLocalPosition;
                _ceilingSignal.transform.localPosition = _ceilingDefaultLocalPosition;
                _ceilingSignal.transform.localRotation = _ceilingDefaultLocalRotation;
                _ceilingSignal.SetActive(false);
            }
        }
    }
}
