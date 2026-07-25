using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;
using UnityEngine;

namespace StressTraining.Pressure
{
    /// <summary>
    /// One asset-free overlay segment. Only the overlay transform moves; original
    /// corridor geometry and the XR rig are never modified.
    /// </summary>
    public sealed class PressureSegmentController : MonoBehaviour
    {
        private Vector3 _restLocalPosition;
        private Quaternion _restLocalRotation;
        private Vector3 _restLocalScale;
        private bool _cached;
        private float _progress;

        public bool IsClosing { get; private set; }
        public float CloseDirection = -1f;
        public float CloseDistance = PressureHeuristics.SegmentCloseDistanceMeters;
        public float CloseSeconds = PressureHeuristics.SegmentCloseSeconds;

        public void CacheRest()
        {
            if (_cached) return;
            _restLocalPosition = transform.localPosition;
            _restLocalRotation = transform.localRotation;
            _restLocalScale = transform.localScale;
            _cached = true;
        }

        public void BeginClose()
        {
            CacheRest();
            IsClosing = true;
        }

        /// <summary>Advanced only with active session delta time.</summary>
        public void TickActive(float dt)
        {
            if (!IsClosing || _progress >= 1f || dt <= 0f) return;
            _progress = Mathf.Min(1f,
                _progress + dt / Mathf.Max(0.5f, CloseSeconds));
            float smooth = _progress * _progress * (3f - 2f * _progress);
            transform.localPosition = _restLocalPosition +
                new Vector3(0f, CloseDirection * CloseDistance * smooth, 0f);
        }

        public void ResetSegment()
        {
            if (_cached)
            {
                transform.localPosition = _restLocalPosition;
                transform.localRotation = _restLocalRotation;
                transform.localScale = _restLocalScale;
            }
            IsClosing = false;
            _progress = 0f;
        }
    }

    /// <summary>
    /// Closes paired floor/ceiling overlays from the far end. The nearest pair is
    /// deliberately retained as the last safe segment.
    /// </summary>
    public sealed class CorridorCollapseController : MonoBehaviour
    {
        private sealed class SegmentPair
        {
            public PressureSegmentController Floor;
            public PressureSegmentController Ceiling;
        }

        private readonly List<SegmentPair> _pairs = new List<SegmentPair>();
        private int _closedPairCount;

        public int PairCount => _pairs.Count;
        public int ClosedPairCount => _closedPairCount;
        public int ClosablePairCount => Mathf.Max(0, _pairs.Count - 1);

        public void RegisterPair(PressureSegmentController floor,
            PressureSegmentController ceiling)
        {
            if (floor == null && ceiling == null) return;
            _pairs.Add(new SegmentPair { Floor = floor, Ceiling = ceiling });
        }

        /// <summary>Progress 0..1 across the Late and Critical span.</summary>
        public void SetCollapseProgress(float progress01)
        {
            int closable = ClosablePairCount;
            int shouldBeClosed = Mathf.Clamp(
                Mathf.CeilToInt(Mathf.Clamp01(progress01) * closable - 0.0001f),
                0, closable);

            for (int i = _closedPairCount; i < shouldBeClosed; i++)
            {
                _pairs[i].Floor?.BeginClose();
                _pairs[i].Ceiling?.BeginClose();
            }

            if (shouldBeClosed > _closedPairCount)
                _closedPairCount = shouldBeClosed;
        }

        public void TickActive(float dt)
        {
            for (int i = 0; i < _pairs.Count; i++)
            {
                _pairs[i].Floor?.TickActive(dt);
                _pairs[i].Ceiling?.TickActive(dt);
            }
        }

        public void ResetAll()
        {
            for (int i = 0; i < _pairs.Count; i++)
            {
                _pairs[i].Floor?.ResetSegment();
                _pairs[i].Ceiling?.ResetSegment();
            }
            _closedPairCount = 0;
        }
    }

    /// <summary>
    /// Controlled finale using only a cached fade sphere (or corridor fallback).
    /// The center-eye transform is an anchor only and is never moved.
    /// </summary>
    public sealed class GameOverController : MonoBehaviour
    {
        private Transform _centerEye;
        private Transform _fallbackAnchor;
        private GameObject _headFadeSphere;
        private GameObject _fallbackBlackout;
        private float _elapsed;
        private bool _completionSent;

        public event Action FinaleComplete;
        public float FadeSeconds = PressureHeuristics.FinaleFadeSeconds;
        public bool IsRunning { get; private set; }

        public void Initialize(Transform centerEye, Transform fallbackAnchor)
        {
            _centerEye = centerEye;
            _fallbackAnchor = fallbackAnchor;
            BuildCachedVisuals();
            ResetFinale();
        }

        private void BuildCachedVisuals()
        {
            if (_headFadeSphere == null && _centerEye != null)
            {
                _headFadeSphere = RuntimeVisualUtil.Primitive(PrimitiveType.Sphere,
                    "GameOverHeadFade", _centerEye, Vector3.zero,
                    new Vector3(-8f, 8f, 8f),
                    RuntimeVisualUtil.Unlit(Color.black));
            }

            if (_fallbackBlackout == null && _fallbackAnchor != null)
            {
                _fallbackBlackout = RuntimeVisualUtil.Primitive(PrimitiveType.Cube,
                    "GameOverFallbackBlackout", _fallbackAnchor,
                    new Vector3(0f, 1.45f, -3.55f),
                    new Vector3(3.4f, 3.2f, 0.04f),
                    RuntimeVisualUtil.Unlit(Color.black));
            }
        }

        public bool BeginFinale()
        {
            if (IsRunning || _completionSent) return false;
            BuildCachedVisuals();
            IsRunning = true;
            _elapsed = 0f;
            if (_headFadeSphere != null)
            {
                _headFadeSphere.transform.localPosition = Vector3.zero;
                _headFadeSphere.transform.localScale = new Vector3(-8f, 8f, 8f);
                _headFadeSphere.SetActive(true);
            }
            else if (_fallbackBlackout != null)
            {
                _fallbackBlackout.transform.localScale =
                    new Vector3(0.2f, 0.2f, 0.04f);
                _fallbackBlackout.SetActive(true);
            }
            return true;
        }

        /// <summary>Pause-aware because the production flow supplies active delta time.</summary>
        public void TickActive(float dt)
        {
            if (!IsRunning || dt <= 0f) return;
            _elapsed = Mathf.Min(FadeSeconds,
                _elapsed + dt);
            float normalized = Mathf.Clamp01(_elapsed / Mathf.Max(0.5f, FadeSeconds));
            float smooth = normalized * normalized;

            if (_headFadeSphere != null && _headFadeSphere.activeSelf)
            {
                float scale = Mathf.Lerp(8f, 0.35f, smooth);
                _headFadeSphere.transform.localScale =
                    new Vector3(-scale, scale, scale);
            }
            else if (_fallbackBlackout != null && _fallbackBlackout.activeSelf)
            {
                float scale = Mathf.Lerp(0.2f, 1f, smooth);
                _fallbackBlackout.transform.localScale =
                    new Vector3(3.4f * scale, 3.2f * scale, 0.04f);
            }

            if (normalized >= 1f) CompleteOnce();
        }

        private void CompleteOnce()
        {
            if (_completionSent) return;
            IsRunning = false;
            _completionSent = true;
            FinaleComplete?.Invoke();
        }

        public void ResetFinale()
        {
            IsRunning = false;
            _elapsed = 0f;
            _completionSent = false;
            if (_headFadeSphere != null)
            {
                _headFadeSphere.transform.localPosition = Vector3.zero;
                _headFadeSphere.transform.localScale = new Vector3(-8f, 8f, 8f);
                _headFadeSphere.SetActive(false);
            }
            if (_fallbackBlackout != null)
            {
                _fallbackBlackout.transform.localScale =
                    new Vector3(0.2f, 0.2f, 0.04f);
                _fallbackBlackout.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Deterministic production pressure layer subordinate to ProductionSessionFlow.
    /// No live BPM adaptation, Rigidbody collapse, scene searches per frame or XR
    /// camera movement. Runtime overlays are built once and fully reset between sessions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PressureController : MonoBehaviour
    {
        private sealed class LightSnapshot
        {
            public Light Light;
            public Color Color;
            public float Intensity;
        }

        public PressureStage CurrentStage { get; private set; } = PressureStage.Stable;
        public float CurrentIntensity { get; private set; }
        public bool IsRunning { get; private set; }
        public bool IsInitialized { get; private set; }
        public bool IsFinaleRunning => _gameOver != null && _gameOver.IsRunning;
        public int ActivePressureLevel => _timeline?.Level.level ?? 1;

        // ── developer diagnostics (never shown to a participant) ──────────
        /// <summary>Corridor collapse pairs found/built; 0 means nothing can close.</summary>
        public int SegmentPairCount => _collapse != null ? _collapse.PairCount : 0;
        /// <summary>Pairs already closing; the nearest pair is deliberately never closed.</summary>
        public int ClosedSegmentPairCount => _collapse != null ? _collapse.ClosedPairCount : 0;
        /// <summary>Crack overlays built once at initialization.</summary>
        public int CrackCount => _crackQuads.Count;
        /// <summary>Corridor lights captured for the pressure ramp.</summary>
        public int BoundLightCount => _lightSnapshots.Count;
        /// <summary>Remaining-fraction of the last applied TickActive, or -1.</summary>
        public float LastAppliedRemainingFraction { get; private set; } = -1f;
        public bool IsPaused => _isPaused;

        // Puzzle-corridor diagnostics surfaced to ProductionSessionFlow (dev only).
        public bool PuzzleActive => UsePuzzle;
        public int PuzzleSegmentCount => _puzzle != null ? _puzzle.SegmentCount : 0;
        public string PuzzleDiagnostics => _puzzle != null ? _puzzle.Diagnostics() : "PuzzleCorridor: nema";
        public bool ProceduralBlockoutActive => !UsePuzzle && _collapse != null;

        public event Action<PressureStage> StageChanged;
        public event Action<PressureEventDefinition> PressureEventFired;
        public event Action GameOverFinaleComplete;

        // When an imported puzzle corridor is attached it becomes the ONLY visible
        // collapse model: the procedural floor/ceiling slabs and crack quads are not
        // built, and stage/tick are routed to the puzzle segments instead. The
        // procedural path stays as a development fallback when no puzzle is present.
        private PuzzleCorridorController _puzzle;
        private bool UsePuzzle => _puzzle != null && _puzzle.IsBound;

        private PressureTimeline _timeline;
        private Transform _pressureRoot;
        private Vector3 _rootRestPosition;
        private Quaternion _rootRestRotation;
        private Vector3 _rootRestScale;
        private CorridorCollapseController _collapse;
        private GameOverController _gameOver;
        private PressureTransientFxController _transientFx;
        private PressureAudioController _audio;

        private readonly List<LightSnapshot> _lightSnapshots =
            new List<LightSnapshot>();
        private Light _cageLight;
        private Light _consoleLight;
        private Color _cageColor;
        private Color _consoleColor;
        private float _cageIntensity;
        private float _consoleIntensity;

        private readonly List<Renderer> _crackQuads = new List<Renderer>();
        private readonly List<PressureEventDefinition> _dueEventBuffer =
            new List<PressureEventDefinition>(8);
        private float _shakePhase;
        private float _eventPulse;
        private bool _criticalWarningPlayed;
        private bool _finaleTriggered;
        private bool _isPaused;

        public static bool ShouldRunFor(SessionCondition condition) =>
            condition == SessionCondition.Pressure;

        /// <summary>
        /// Makes the imported modular puzzle corridor the visible collapse model.
        /// Call BEFORE <see cref="Initialize"/> so the procedural slabs/cracks are
        /// never built. When the puzzle is missing (dev), Initialize falls back to
        /// the procedural blockout and logs a clear warning.
        /// </summary>
        public void AttachPuzzleCorridor(PuzzleCorridorController puzzle)
        {
            if (_puzzle != null)
                _puzzle.AnimationEvent -= OnPuzzleAnimationEvent;
            _puzzle = puzzle;
            if (_puzzle != null)
                _puzzle.AnimationEvent += OnPuzzleAnimationEvent;
            if (UsePuzzle)
                Debug.Log("[PressureController] Puzzle corridor attached (" +
                          _puzzle.SegmentCount + " segments) — procedural slabs disabled.");
        }

        public void Initialize(Transform corridorRoot, Transform centerEye,
            Transform fallbackViewAnchor = null)
        {
            if (IsInitialized)
            {
                return;
            }
            if (corridorRoot == null)
            {
                Debug.LogWarning("[PressureController] Corridor root is missing; pressure remains disabled.");
                return;
            }
            if (centerEye == null)
                Debug.LogWarning("[PressureController] center-eye anchor is missing; using corridor blackout fallback.");

            _pressureRoot = new GameObject("PressureRoot").transform;
            _pressureRoot.SetParent(corridorRoot, false);
            _rootRestPosition = _pressureRoot.localPosition;
            _rootRestRotation = _pressureRoot.localRotation;
            _rootRestScale = _pressureRoot.localScale;

            Transform layoutAnchor = fallbackViewAnchor != null
                ? fallbackViewAnchor
                : centerEye;
            Vector3 userLocalPosition = layoutAnchor != null
                ? _pressureRoot.InverseTransformPoint(layoutAnchor.position)
                : new Vector3(0f, 0f, -3.2f);
            Vector3 userForwardLocal = layoutAnchor != null
                ? _pressureRoot.InverseTransformDirection(layoutAnchor.forward)
                : Vector3.back;
            userForwardLocal.y = 0f;
            if (userForwardLocal.sqrMagnitude < 0.0001f)
                userForwardLocal = Vector3.back;
            else
                userForwardLocal.Normalize();

            CacheLights(corridorRoot);
            if (UsePuzzle)
            {
                // One-time scene discovery only. The safety set stores references and
                // recomputes AABBs when a new target pose is authored; no scene search
                // occurs in TickActive. Missing anchors remain valid/NEPROVJERENO.
                _puzzle.ConfigureSafety(centerEye,
                    FindSceneTransformByName("Console_Placeholder"),
                    FindSceneTransformByName("TabletAnchor"),
                    FindSceneTransformByName("ConsoleControlsAnchor"));
                // Puzzle corridor is the visible model — do not build the procedural
                // floor/ceiling slabs or crack quads that would sit on top of it.
                Debug.Log("[PressureController] Using imported puzzle corridor; procedural collapse geometry skipped.");
            }
            else
            {
                if (_puzzle != null)
                    Debug.LogWarning("[PressureController] Puzzle corridor present but no segments bound — " +
                                     "falling back to the procedural blockout.");
                BuildCracks();
                BuildSegments(userLocalPosition, userForwardLocal);
            }

            _transientFx = _pressureRoot.gameObject
                .AddComponent<PressureTransientFxController>();
            _transientFx.Initialize(_pressureRoot, userLocalPosition,
                userForwardLocal);

            _audio = gameObject.GetComponent<PressureAudioController>();
            if (_audio == null) _audio = gameObject.AddComponent<PressureAudioController>();
            _audio.Initialize();

            _gameOver = _pressureRoot.gameObject.AddComponent<GameOverController>();
            _gameOver.Initialize(centerEye, fallbackViewAnchor ?? _pressureRoot);
            _gameOver.FinaleComplete += OnFinaleComplete;

            BuildSkyBackdrop(corridorRoot);
            BuildConsoleBackWall(corridorRoot);
            BuildStationCollapse(corridorRoot);

            _pressureRoot.gameObject.SetActive(false);
            IsInitialized = true;
        }

        // ── sky backdrop + breakable console-back wall ───────────────────

        private CorridorSkyBackdrop _skyBackdrop;
        private ConsoleBackWall _backWall;

        private void BuildSkyBackdrop(Transform corridorRoot)
        {
            // Always built (both conditions): a large inward sky sphere behind the
            // corridor, seen only through gaps that open as segments fall.
            _skyBackdrop = corridorRoot.gameObject.AddComponent<CorridorSkyBackdrop>();
            _skyBackdrop.Build(corridorRoot, new Vector3(0f, 2.8f, 0f), 30f);
        }

        private void BuildConsoleBackWall(Transform corridorRoot)
        {
            // Solid wall behind the console (far −Z end the participant faces). It
            // matches the interior console-end footprint; under pressure it breaks so
            // the collapse is visible even while looking at the tablet.
            Transform end = FindDeep(corridorRoot, "Wall_ConsoleEnd");
            Vector3 centerLocal = end != null
                ? new Vector3(0f, 2.8f, end.localPosition.z + 0.12f)  // just inside the end
                : new Vector3(0f, 2.8f, -4.98f);
            _backWall = corridorRoot.gameObject.AddComponent<ConsoleBackWall>();
            // Faces +Z toward the player; interior is 2.8 wide × 5.6 tall.
            _backWall.Build(corridorRoot, centerLocal, 2.8f, 5.6f, 0.12f, Quaternion.identity);
        }

        private StationCollapse _station;

        private void BuildStationCollapse(Transform corridorRoot)
        {
            // The final loss beat: at Expired the Safe platform, console and robot arm
            // drop away too. Scene-wide name lookup (the safe floor lives under the
            // puzzle root, the console/arm are independent) so this is robust to which
            // corridor root is bound. Missing targets are simply skipped.
            _station = corridorRoot.gameObject.AddComponent<StationCollapse>();
            _station.AddTarget(FindSceneTransformByName("Segment_Safe_Floor"), 0.35f, 8f, 7f);
            _station.AddTarget(FindSceneTransformByName("Console_BlenderPrototype"), 0.50f, 8f, 9f);
            _station.AddTarget(FindSceneTransformByName("RobotArm_Scene"), 0.65f, 8f, 0f);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var f = FindDeep(child, name);
                if (f != null) return f;
            }
            return null;
        }

        private void CacheLights(Transform corridorRoot)
        {
            var lights = corridorRoot.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null) continue;
                _lightSnapshots.Add(new LightSnapshot
                {
                    Light = light,
                    Color = light.color,
                    Intensity = light.intensity
                });
                if (light.name == "Light_CageZone")
                {
                    _cageLight = light;
                    _cageColor = light.color;
                    _cageIntensity = light.intensity;
                }
                else if (light.name == "Light_ConsoleZone")
                {
                    _consoleLight = light;
                    _consoleColor = light.color;
                    _consoleIntensity = light.intensity;
                }
            }
        }

        private void BuildCracks()
        {
            for (int i = 0; i < 4; i++)
            {
                float x = i % 2 == 0 ? -1.58f : 1.58f;
                float z = -4.6f + (i / 2) * 2.2f;
                var quad = RuntimeVisualUtil.Primitive(PrimitiveType.Quad,
                    "PressureCrack" + i, _pressureRoot,
                    new Vector3(x, 1.4f + (i % 3) * 0.4f, z),
                    new Vector3(0.06f, 1.6f, 1f),
                    RuntimeVisualUtil.Emissive(
                        new Color(0.05f, 0.05f, 0.05f), Color.black, 0f));
                quad.transform.localRotation = Quaternion.Euler(
                    0f, x < 0 ? 90f : -90f, 12f * (i - 1.5f));
                var renderer = quad.GetComponent<Renderer>();
                renderer.enabled = false;
                _crackQuads.Add(renderer);
            }
        }

        private void BuildSegments(Vector3 userLocalPosition,
            Vector3 userForwardLocal)
        {
            _collapse = _pressureRoot.gameObject
                .AddComponent<CorridorCollapseController>();

            Vector3 behindDirection = -userForwardLocal;
            float segmentLength = PressureHeuristics.SegmentSpanMeters /
                                  PressureHeuristics.SegmentPairCount;
            Quaternion segmentRotation = Quaternion.LookRotation(behindDirection,
                Vector3.up);

            // Registration order is farthest-behind to nearest. The final pair,
            // centered immediately behind the start pose, is never closed.
            for (int order = 0; order < PressureHeuristics.SegmentPairCount; order++)
            {
                int distanceIndex = PressureHeuristics.SegmentPairCount - 1 - order;
                float distance = segmentLength * (distanceIndex + 0.5f);
                Vector3 horizontalCenter = userLocalPosition +
                                           behindDirection * distance;
                Vector3 floorPosition = new Vector3(horizontalCenter.x,
                    userLocalPosition.y + PressureHeuristics.FloorOffsetMeters,
                    horizontalCenter.z);
                Vector3 ceilingPosition = new Vector3(horizontalCenter.x,
                    userLocalPosition.y + PressureHeuristics.CeilingOffsetMeters,
                    horizontalCenter.z);
                Vector3 scale = new Vector3(
                    PressureHeuristics.SegmentWidthMeters,
                    PressureHeuristics.SegmentThicknessMeters,
                    segmentLength - PressureHeuristics.SegmentGapMeters);

                var floor = RuntimeVisualUtil.Primitive(PrimitiveType.Cube,
                    "PressureFloorSeg" + order, _pressureRoot, floorPosition, scale,
                    RuntimeVisualUtil.Lit(new Color(0.16f, 0.15f, 0.14f)));
                floor.transform.localRotation = segmentRotation;
                var floorSegment = floor.AddComponent<PressureSegmentController>();
                floorSegment.CloseDirection = -1f;
                floorSegment.CacheRest();

                var ceiling = RuntimeVisualUtil.Primitive(PrimitiveType.Cube,
                    "PressureCeilingSeg" + order, _pressureRoot, ceilingPosition, scale,
                    RuntimeVisualUtil.Lit(new Color(0.14f, 0.14f, 0.15f)));
                ceiling.transform.localRotation = segmentRotation;
                var ceilingSegment = ceiling.AddComponent<PressureSegmentController>();
                ceilingSegment.CloseDirection = 1f;
                ceilingSegment.CacheRest();

                _collapse.RegisterPair(floorSegment, ceilingSegment);
            }
        }

        public void Begin(int pressureSeed, int pressureLevel)
        {
            if (!IsInitialized || _pressureRoot == null) return;
            ResetPressure();
            _timeline = new PressureTimeline(pressureSeed, pressureLevel);
            _timeline.ResetEventCursor();
            if (UsePuzzle) _puzzle.SetSessionSeed(pressureSeed);
            CurrentStage = PressureStage.Stable;
            CurrentIntensity = 0f;
            _pressureRoot.gameObject.SetActive(true);
            _isPaused = false;
            IsRunning = true;
        }

        public void SetPaused(bool paused)
        {
            _isPaused = paused;
            _audio?.SetPaused(paused);
        }

        /// <summary>Called only with active session delta time.</summary>
        public void TickActive(float dt, double remainingFraction)
        {
            if (!IsRunning || _isPaused || _timeline == null ||
                remainingFraction < 0 || dt <= 0f)
                return;

            LastAppliedRemainingFraction = (float)remainingFraction;
            var stage = PressureTimeline.StageFor(remainingFraction);
            AdvanceToStage(stage);

            CurrentIntensity = _timeline.Intensity(remainingFraction);
            _eventPulse = Mathf.Max(0f,
                _eventPulse - dt * PressureHeuristics.EventPulseDecayPerSecond);
            _dueEventBuffer.Clear();
            _timeline.ConsumeDueEvents(remainingFraction, _dueEventBuffer);
            for (int i = 0; i < _dueEventBuffer.Count; i++)
                FireEvent(_dueEventBuffer[i]);
            ApplyLights();
            ApplyCracks();
            ApplyShake(dt);

            if (UsePuzzle)
            {
                // The puzzle segments own their own per-stage schedule, so the
                // controller just tells them the current stage and feeds active time.
                _puzzle.ApplyStage(stage);
                _puzzle.TickActive(dt);
            }
            else
            {
                if (stage >= PressureStage.Late)
                {
                    float progress = Mathf.Clamp01(
                        (PressureTimeline.LateBelow - (float)remainingFraction) /
                        PressureTimeline.LateBelow);
                    _collapse?.SetCollapseProgress(progress);
                }
                _collapse?.TickActive(dt);
            }

            // The console-back wall breaks on the same stages regardless of whether
            // the puzzle or the procedural blockout is the corridor model.
            if (_backWall != null)
            {
                _backWall.ApplyStage(stage);
                _backWall.TickActive(dt);
            }
            _transientFx?.TickActive(dt);
        }

        private void AdvanceToStage(PressureStage targetStage)
        {
            if (targetStage == CurrentStage) return;

            if (targetStage > CurrentStage)
            {
                for (int value = (int)CurrentStage + 1;
                     value <= (int)targetStage; value++)
                {
                    CurrentStage = (PressureStage)value;
                    OnStageEntered(CurrentStage);
                    StageChanged?.Invoke(CurrentStage);
                }
                return;
            }

            // A remaining-time increase is not expected during an active session,
            // but keep the controller recoverable if an external debug tool rearms it.
            CurrentStage = targetStage;
            OnStageEntered(CurrentStage);
            StageChanged?.Invoke(CurrentStage);
        }

        private void OnStageEntered(PressureStage stage)
        {
            if (stage >= PressureStage.Critical)
            {
                _transientFx?.SetCriticalVoidVisible(true);
                if (!_criticalWarningPlayed)
                {
                    _criticalWarningPlayed = true;
                    _audio?.PlayCriticalWarning(_timeline.Level.audioRampCap);
                }
            }
        }

        private void FireEvent(PressureEventDefinition evt)
        {
            if (evt == null || _timeline == null) return;
            float levelMagnitude = Mathf.Clamp01(evt.magnitude *
                                                  _timeline.Level.intensityCap);
            _eventPulse = Mathf.Max(_eventPulse, levelMagnitude);
            switch (evt.kind)
            {
                case PressureEventKind.Rumble:
                    _audio?.PlayRumble(evt.magnitude, _timeline.Level.audioRampCap);
                    break;
                case PressureEventKind.DustBurst:
                    _transientFx?.TriggerDustBurst(levelMagnitude);
                    _audio?.PlayDustBurst(evt.magnitude, _timeline.Level.audioRampCap);
                    break;
                case PressureEventKind.LightFlicker:
                    break;
                case PressureEventKind.CeilingCreak:
                    _transientFx?.TriggerCeilingSignal(levelMagnitude);
                    _audio?.PlayCeilingCreak(evt.magnitude,
                        _timeline.Level.audioRampCap);
                    break;
            }
            PressureEventFired?.Invoke(evt);
        }

        private void ApplyLights()
        {
            float pulse = _eventPulse > 0f
                ? 0.5f + 0.5f * Mathf.Sin(_shakePhase * 5.3f)
                : 0f;
            float strength = Mathf.Clamp01(CurrentIntensity +
                                             _eventPulse * pulse * 0.35f);
            if (_cageLight != null)
            {
                _cageLight.color = Color.Lerp(_cageColor,
                    new Color(0.95f, 0.35f, 0.20f), strength);
                _cageLight.intensity = _cageIntensity * (1f + 0.55f * strength);
            }
            if (_consoleLight != null)
            {
                _consoleLight.color = Color.Lerp(_consoleColor,
                    new Color(0.70f, 0.48f, 0.38f), strength * 0.45f);
                _consoleLight.intensity = _consoleIntensity *
                    (1f - 0.30f * strength);
            }
        }

        private void ApplyCracks()
        {
            bool visible = CurrentStage >= PressureStage.Mid;
            float alpha = _timeline.Level.crackMaxAlpha *
                          Mathf.Clamp01(CurrentIntensity * 1.4f);
            for (int i = 0; i < _crackQuads.Count; i++)
            {
                var renderer = _crackQuads[i];
                if (renderer == null) continue;
                renderer.enabled = visible;
                var material = renderer.sharedMaterial;
                if (material != null && material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", visible
                        ? new Color(0.9f, 0.45f, 0.2f) * alpha *
                          (1f + _eventPulse)
                        : Color.black);
                }
            }
        }

        private void ApplyShake(float dt)
        {
            if (_pressureRoot == null) return;
            if (CurrentStage < PressureStage.Early)
            {
                _pressureRoot.localPosition = _rootRestPosition;
                return;
            }

            _shakePhase += dt * (6f + 10f * CurrentIntensity);
            float amplitude = _timeline.Level.shakeAmplitudeMeters *
                              Mathf.Clamp01(CurrentIntensity + _eventPulse);
            _pressureRoot.localPosition = _rootRestPosition + new Vector3(
                Mathf.Sin(_shakePhase * 1.3f) * amplitude,
                Mathf.Sin(_shakePhase * 1.7f) * amplitude * 0.6f,
                0f);
        }

        /// <summary>Starts the single normal finale path. Returns false if unavailable/already started.</summary>
        public bool TriggerGameOverFinale()
        {
            if (!IsInitialized || _gameOver == null || _finaleTriggered) return false;
            _finaleTriggered = true;
            if (CurrentStage != PressureStage.Expired)
            {
                CurrentIntensity = _timeline?.Intensity(0) ?? 1f;
                AdvanceToStage(PressureStage.Expired);
            }
            if (UsePuzzle)
            {
                // Do not snap S5/S6. The existing finale keeps feeding active time
                // through TickFinaleActive, so the final collapse remains visible and
                // TimeExpired still owns session completion.
                _puzzle.ApplyStage(PressureStage.Expired);
            }
            else
            {
                _collapse?.SetCollapseProgress(1f);
            }
            _backWall?.ApplyStage(PressureStage.Expired);
            _station?.ApplyStage(PressureStage.Expired);
            _transientFx?.SetCriticalVoidVisible(true);
            return _gameOver.BeginFinale();
        }

        public void TickFinaleActive(float dt)
        {
            if (_isPaused || dt <= 0f) return;
            if (UsePuzzle) _puzzle.TickActive(dt);
            else _collapse?.TickActive(dt);
            _backWall?.TickActive(dt);
            _station?.TickActive(dt);
            _transientFx?.TickActive(dt);
            _gameOver?.TickActive(dt);
        }

        private void OnFinaleComplete()
        {
            GameOverFinaleComplete?.Invoke();
        }

        private void OnPuzzleAnimationEvent(PuzzleAnimationEventData data)
        {
            if (data == null) return;
            float audioCap = _timeline != null ? _timeline.Level.audioRampCap : 0.5f;
            switch (data.Kind)
            {
                case PuzzleAnimationEventKind.WarningStarted:
                    _audio?.PlayRumble(data.Magnitude * 0.55f, audioCap);
                    _transientFx?.TriggerCeilingSignalAt(data.Magnitude * 0.35f,
                        data.WorldPosition);
                    break;
                case PuzzleAnimationEventKind.JointLoosened:
                    _audio?.PlayCeilingCreak(data.Magnitude * 0.70f, audioCap);
                    _transientFx?.TriggerDustBurstAt(data.Magnitude * 0.30f,
                        data.WorldPosition);
                    break;
                case PuzzleAnimationEventKind.SegmentDetached:
                    _audio?.PlayCeilingCreak(data.Magnitude, audioCap);
                    _transientFx?.TriggerDustBurstAt(data.Magnitude * 0.70f,
                        data.WorldPosition);
                    break;
                case PuzzleAnimationEventKind.SegmentFalling:
                    _audio?.PlayRumble(data.Magnitude, audioCap);
                    _transientFx?.TriggerDustBurstAt(data.Magnitude,
                        data.WorldPosition);
                    break;
                case PuzzleAnimationEventKind.FinalCollapseStarted:
                    if (!_criticalWarningPlayed)
                    {
                        _criticalWarningPlayed = true;
                        _audio?.PlayCriticalWarning(audioCap);
                    }
                    break;
            }
        }

        private static Transform FindSceneTransformByName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return null;
            Transform[] transforms = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate != null && candidate.gameObject.scene.IsValid() &&
                    string.Equals(candidate.name, objectName,
                        StringComparison.Ordinal))
                    return candidate;
            }
            return null;
        }

        /// <summary>Idempotent full reset for normal, interrupted and failed sessions.</summary>
        public void ResetPressure()
        {
            IsRunning = false;
            CurrentStage = PressureStage.Stable;
            CurrentIntensity = 0f;
            _shakePhase = 0f;
            _eventPulse = 0f;
            _criticalWarningPlayed = false;
            _finaleTriggered = false;
            _isPaused = false;
            LastAppliedRemainingFraction = -1f;
            _dueEventBuffer.Clear();
            _timeline?.ResetEventCursor();
            _timeline = null;

            for (int i = 0; i < _lightSnapshots.Count; i++)
            {
                var snapshot = _lightSnapshots[i];
                if (snapshot.Light == null) continue;
                snapshot.Light.color = snapshot.Color;
                snapshot.Light.intensity = snapshot.Intensity;
            }

            for (int i = 0; i < _crackQuads.Count; i++)
            {
                var renderer = _crackQuads[i];
                if (renderer == null) continue;
                renderer.enabled = false;
                var material = renderer.sharedMaterial;
                if (material != null && material.HasProperty("_EmissionColor"))
                    material.SetColor("_EmissionColor", Color.black);
            }

            _collapse?.ResetAll();
            _puzzle?.ResetCorridor();
            _backWall?.ResetWall();
            _station?.ResetStation();
            _transientFx?.ResetEffects();
            _audio?.ResetAudio();
            _gameOver?.ResetFinale();

            if (_pressureRoot != null)
            {
                _pressureRoot.localPosition = _rootRestPosition;
                _pressureRoot.localRotation = _rootRestRotation;
                _pressureRoot.localScale = _rootRestScale;
                _pressureRoot.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_gameOver != null)
                _gameOver.FinaleComplete -= OnFinaleComplete;
            if (_puzzle != null)
                _puzzle.AnimationEvent -= OnPuzzleAnimationEvent;
        }
    }
}
