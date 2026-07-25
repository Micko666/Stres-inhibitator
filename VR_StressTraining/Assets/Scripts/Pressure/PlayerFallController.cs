using System;
using StressTraining.Core;
using StressTraining.Pressure;
using UnityEngine;

namespace StressTraining.Session
{
    /// <summary>
    /// Handles losing footing: the participant standing where a puzzle floor has
    /// collapsed, or the global timer expiring. The loss is registered the instant it
    /// starts; the mode (<see cref="FallMode"/>) only chooses how the consequence
    /// looks. Every mode runs a short CONSEQUENCE phase and then HOLDS the fully
    /// applied effect for <c>lossConsequenceHoldSeconds</c> so it is felt, before the
    /// coordinator returns the participant to the Safe Space:
    ///
    ///   • ComfortFade  — the view fades to black in place. No camera translation, so
    ///     no vestibular conflict. The ONLY mode safe during a measured session (a
    ///     scripted camera drop is a strong simulator-sickness trigger and would
    ///     contaminate the SSQ/HR the study records).
    ///   • RealFall     — the rig drops straight down, capped at fallMaxDistanceMeters
    ///     so it never falls forever, covers with black, then holds. Demo/preview.
    ///   • Devirtualize — a digital "de-render": a glitch flicker resolving into a
    ///     white-out, no camera translation. Reads as a real loss while staying
    ///     measurement-safe; the intended long-term consequence once an avatar exists.
    ///
    /// This is always a VISUAL consequence, never a locomotion mechanism: the fade and
    /// devirtualize paths never move the rig, and RealFall snaps back after the capped
    /// drop. Pure timing/geometry lives in <see cref="FallDecision"/> for unit tests.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFallController : MonoBehaviour
    {
        public event Action PlayerLost;   // raised once when a loss is committed

        private enum LossPhase { None, Consequence, Hold }

        private Transform _rigRoot;
        private Transform _head;
        private Transform _physicsCharacter;
        private PuzzleCorridorController _corridor;
        private DeveloperConfig _config;
        private GameObject _fadeSphere;      // black cover (ComfortFade / RealFall snap)
        private GameObject _devirtSphere;    // digital cover (Devirtualize)
        private Renderer _devirtRenderer;

        private bool _armed;
        private float _armGraceRemaining;   // suppress the floor check until the teleport settles
        private bool _falling;
        private LossPhase _phase;
        private float _phaseElapsed;
        private float _holdElapsed;
        private float _fallStartY;
        private FallMode _activeMode;

        public bool IsArmed => _armed;
        public bool IsFalling => _falling;
        public FallMode CurrentMode => _config != null ? _config.fallMode : FallMode.ComfortFade;

        public void Initialize(Transform rigRoot, Transform head, Transform physicsCharacter,
            PuzzleCorridorController corridor, DeveloperConfig config)
        {
            _rigRoot = rigRoot;
            _head = head != null ? head : rigRoot;
            _physicsCharacter = physicsCharacter;
            _corridor = corridor;
            _config = config ?? new DeveloperConfig();
            BuildCoverSpheres();
        }

        /// <summary>Enable ground checks (corridor entry). Disable in Safe Space.</summary>
        public void SetArmed(bool armed)
        {
            _armed = armed;
            // On arming, wait for the corridor teleport to actually land before we
            // start checking the floor. The Meta locomotor teleport is deferred a
            // frame, so the rig is briefly still at the previous (Safe Space) position,
            // which is outside every corridor footprint — checking there would read
            // "no floor" and eject the player the instant they enter the corridor.
            if (armed) _armGraceRemaining = FallDecision.ArmSettleSeconds;
            else if (!_falling) ResetCovers();
        }

        private void Update()
        {
            if (_falling) { TickLoss(Time.unscaledDeltaTime); return; }
            if (!_armed || _config == null || !_config.fallThroughMissingFloorEnabled) return;
            if (_armGraceRemaining > 0f)
            {
                _armGraceRemaining -= Time.unscaledDeltaTime;
                return;   // let the teleport settle before the floor check begins
            }
            if (_corridor == null || !_corridor.IsBound) return;

            // Sample under the head so the check follows where the participant stands.
            Vector3 ground = _head != null ? _head.position : _rigRoot.position;
            if (!_corridor.HasFloorAt(ground)) BeginLoss();
        }

        /// <summary>External trigger (e.g. TimeExpired). Idempotent while falling.</summary>
        public void TriggerLoss() => BeginLoss();

        private void BeginLoss()
        {
            if (_falling) return;
            _falling = true;
            _phase = LossPhase.Consequence;
            _phaseElapsed = 0f;
            _holdElapsed = 0f;
            _activeMode = _config != null ? _config.fallMode : FallMode.ComfortFade;
            _fallStartY = _rigRoot != null ? _rigRoot.position.y : 0f;

            // Fade covers immediately; devirt builds up over the consequence phase;
            // RealFall shows nothing until the drop completes.
            if (_activeMode == FallMode.ComfortFade) ShowFade(true);
        }

        private void TickLoss(float dt)
        {
            if (dt <= 0f) return;

            if (_phase == LossPhase.Consequence)
            {
                _phaseElapsed += dt;
                if (TickConsequence(dt)) _phase = LossPhase.Hold;
                return;
            }

            // Hold: the consequence stays fully applied so the loss is felt, then commit.
            _holdElapsed += dt;
            float hold = _config != null ? Mathf.Max(0f, _config.lossConsequenceHoldSeconds) : 1.6f;
            if (_holdElapsed >= hold && _phaseElapsed + _holdElapsed >= FallDecision.MinLossSeconds)
                CommitLoss();
        }

        /// <summary>Runs the active consequence; returns true when it is fully applied.</summary>
        private bool TickConsequence(float dt)
        {
            switch (_activeMode)
            {
                case FallMode.RealFall:
                {
                    if (_rigRoot == null) { ShowFade(true); return true; }
                    float maxDist = _config != null ? _config.fallMaxDistanceMeters : 3f;
                    float dropped = _fallStartY - _rigRoot.position.y;
                    MoveRigDown(FallDecision.DropStep(_phaseElapsed, dt, dropped, maxDist));
                    if ((_fallStartY - _rigRoot.position.y) >= maxDist - 1e-3f)
                    {
                        ShowFade(true);   // cover the snap back
                        return true;
                    }
                    return false;
                }

                case FallMode.Devirtualize:
                {
                    // Glitch flicker resolving into a solid white-out — no rig movement.
                    ShowDevirt(FallDecision.DevirtVisible(_phaseElapsed),
                        FallDecision.DevirtWhiteout01(_phaseElapsed));
                    if (_phaseElapsed >= FallDecision.DevirtSeconds)
                    {
                        ShowDevirt(true, 1f);
                        return true;
                    }
                    return false;
                }

                default: // ComfortFade — already black; just wait out the short fade.
                    return _phaseElapsed >= FallDecision.FadeSeconds;
            }
        }

        private void MoveRigDown(float distance)
        {
            if (_rigRoot != null)
                _rigRoot.position += Vector3.down * distance;
            if (_physicsCharacter != null)
                _physicsCharacter.position += Vector3.down * distance;
        }

        private void CommitLoss()
        {
            _falling = false;
            _phase = LossPhase.None;
            _phaseElapsed = 0f;
            _holdElapsed = 0f;
            PlayerLost?.Invoke();   // the coordinator teleports to Safe Space
            ResetCovers();
        }

        // ── head-locked cover spheres ────────────────────────────────────

        private void BuildCoverSpheres()
        {
            if (_head == null) return;
            if (_fadeSphere == null)
            {
                _fadeSphere = BuildInwardSphere("FallFadeSphere", Color.black, out _);
                _fadeSphere.SetActive(false);
            }
            if (_devirtSphere == null)
            {
                _devirtSphere = BuildInwardSphere("DevirtSphere",
                    new Color(0.55f, 0.85f, 1f), out _devirtRenderer);   // digital cyan
                _devirtSphere.SetActive(false);
            }
        }

        private GameObject BuildInwardSphere(string name, Color color, out Renderer renderer)
        {
            // Negative X scale makes the sphere face inward (we sit inside it).
            var go = RuntimeVisualUtil.Primitive(PrimitiveType.Sphere, name,
                _head, Vector3.zero, new Vector3(-0.6f, 0.6f, 0.6f),
                RuntimeVisualUtil.Unlit(color));
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            renderer = go.GetComponent<Renderer>();
            return go;
        }

        private void ShowFade(bool visible)
        {
            if (_fadeSphere != null) _fadeSphere.SetActive(visible);
        }

        private void ShowDevirt(bool visible, float whiteout01)
        {
            if (_devirtSphere == null) return;
            _devirtSphere.SetActive(visible);
            if (visible && _devirtRenderer != null && _devirtRenderer.sharedMaterial != null)
            {
                // Lerp the digital cyan toward white as the de-render completes.
                Color c = Color.Lerp(new Color(0.55f, 0.85f, 1f), Color.white,
                    Mathf.Clamp01(whiteout01));
                var mat = _devirtRenderer.sharedMaterial;
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
            }
        }

        private void ResetCovers()
        {
            ShowFade(false);
            if (_devirtSphere != null) _devirtSphere.SetActive(false);
        }

        public void ResetState()
        {
            _falling = false;
            _phase = LossPhase.None;
            _phaseElapsed = 0f;
            _holdElapsed = 0f;
            ResetCovers();
        }
    }

    /// <summary>Pure, unit-testable timing/geometry decisions for the loss consequence.</summary>
    public static class FallDecision
    {
        public const float FadeSeconds = 0.30f;      // comfort fade-out
        public const float MinLossSeconds = 0.20f;   // debounce so a loss is never instant
        public const float ArmSettleSeconds = 0.5f;  // grace after arming for the teleport to land
        public const float DevirtSeconds = 0.90f;    // glitch → white-out build-up
        private const float Gravity = 9.81f;
        private const float DevirtFlickerHz = 14f;   // digital breakup rate

        /// <summary>
        /// Distance to move the rig DOWN this frame for a capped accelerating fall.
        /// Never overshoots maxDistance (the hard "don't fall forever" stop).
        /// </summary>
        public static float DropStep(float elapsed, float dt, float alreadyDropped, float maxDistance)
        {
            float velocity = Gravity * Mathf.Max(0f, elapsed);   // v = g·t
            float step = velocity * dt;
            float remaining = Mathf.Max(0f, maxDistance - alreadyDropped);
            return Mathf.Min(step, remaining);
        }

        /// <summary>
        /// Devirtualize flicker: the cover blinks on/off early (digital breakup) and
        /// becomes steadily lit as the white-out takes over near the end.
        /// </summary>
        public static bool DevirtVisible(float elapsed)
        {
            if (elapsed >= DevirtSeconds * 0.7f) return true;    // settle into solid white-out
            return (Mathf.FloorToInt(Mathf.Max(0f, elapsed) * DevirtFlickerHz) & 1) == 0;
        }

        /// <summary>0 at the start of the de-render, 1 once fully white.</summary>
        public static float DevirtWhiteout01(float elapsed)
        {
            return Mathf.Clamp01(elapsed / DevirtSeconds);
        }
    }
}
