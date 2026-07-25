using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StressTraining.Pressure
{
    /// <summary>
    /// Curve-driven animation for one imported puzzle segment. The controller owns
    /// no stage thresholds and no session completion path: it receives an authorized
    /// logical stage from PuzzleCorridorController and advances only with active,
    /// pause-aware delta time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleSegmentController : MonoBehaviour
    {
        private enum PartAction
        {
            None = 0,
            Warning = 1,
            Loosen = 2,
            Detach = 3,
            DetachedHold = 4,
            Fall = 5,
            Settle = 6,
            HideDelay = 7
        }

        private sealed class RigidbodySnapshot
        {
            public Rigidbody Body;
            public bool IsKinematic;
            public bool UseGravity;
            public bool DetectCollisions;
            public RigidbodyConstraints Constraints;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
        }

        private sealed class Part
        {
            public SegmentPart Kind;
            public Transform Node;
            public bool Movable;
            public bool OriginalActiveSelf;
            public Vector3 RestLocalPosition;
            public Quaternion RestLocalRotation;
            public Vector3 RestLocalScale;
            public Vector3[] LocalBoundsCorners;
            public Vector3 RestWorldBoundsCenter;
            public Bounds RestWorldBounds;   // axis-aligned footprint used for ground checks
            public Vector3 PivotWorld;
            public Vector3 OutwardWorld;

            public Renderer[] Renderers;
            public bool[] RendererEnabled;
            public MaterialPropertyBlock[] RendererPropertyBlocks;
            public Collider[] Colliders;
            public bool[] ColliderEnabled;
            public bool[] ColliderTrigger;
            public RigidbodySnapshot[] Rigidbodies;

            public PuzzleAnimationPhase Phase;
            public PartAction Action;
            public float DelayRemaining;
            public float Elapsed;
            public float Duration;
            public float NormalizedProgress;
            public Vector3 ActionStartLocalPosition;
            public Quaternion ActionStartLocalRotation;
            public Vector3 ActionTargetLocalPosition;
            public Quaternion ActionTargetLocalRotation;
            public Vector3 LoosenedLocalPosition;
            public Quaternion LoosenedLocalRotation;
            public Vector3 FallFinalLocalPosition;
            public Quaternion FallFinalLocalRotation;

            public bool WarningComplete;
            public bool LoosenComplete;
            public bool DetachComplete;
            public bool FallComplete;
            public bool RenderersHidden;
            public bool VoidRequested;
        }

        public PuzzleSegmentPlan Plan { get; private set; }
        public bool IsSafe => Plan != null && Plan.IsSafe;
        public string SegmentId => Plan != null ? Plan.SegmentId : string.Empty;
        public bool HasFullyCollapsed { get; private set; }
        public bool HasActiveAnimation { get; private set; }
        public int BoundPartCount => _parts.Count;
        public int AnimatedPartCount { get; private set; }
        public PuzzleAnimationPhase DominantPhase { get; private set; }

        private readonly List<Part> _parts = new List<Part>(4);
        private readonly PuzzleAnimationEventData _eventData = new PuzzleAnimationEventData();
        private PuzzleCollapseAnimationProfile _profile;
        private PuzzleCollapseAnimationProfile.WaveSettings _wave;
        private PuzzleSafetyVolumeSet _safety;
        private Action<PuzzleAnimationEventData> _eventSink;
        private Transform _collapsePivot;
        private Transform _fxAnchor;
        private Transform _lightAnchor;
        private Light[] _localLights = Array.Empty<Light>();
        private float[] _localLightIntensity = Array.Empty<float>();
        private Renderer[] _localSignalRenderers = Array.Empty<Renderer>();
        private MaterialPropertyBlock[] _localSignalOriginalBlocks = Array.Empty<MaterialPropertyBlock>();
        private MaterialPropertyBlock[] _localSignalWorkingBlocks = Array.Empty<MaterialPropertyBlock>();
        private Color[] _localSignalEmission = Array.Empty<Color>();
        private PressureStage _desiredStage = PressureStage.Stable;
        private int _sessionSeed;
        private bool _leftWallFirst;
        private bool _bound;
        private bool _warningEventSent;
        private bool _loosenEventSent;
        private bool _detachEventSent;
        private bool _fallEventSent;
        private bool _hiddenEventSent;
        private bool _voidRevealPending;
        private Vector3 _segmentUpWorld;
        private Vector3 _segmentAwayWorld;
        private Vector3 _segmentRightWorld;

        /// <summary>
        /// Ground check for the fall system. Returns true while this segment's floor
        /// still provides footing: the Safe floor is always solid; a collapse floor
        /// becomes a hole once it detaches. Segments with no Floor part are never
        /// solid (nothing to stand on).
        /// </summary>
        public bool FloorIsSolid()
        {
            Part floor = FindPart(SegmentPart.Floor);
            if (floor == null) return false;
            if (IsSafe) return true;
            return floor.Phase < PuzzleAnimationPhase.Detached;
        }

        /// <summary>World XZ footprint of this segment's floor (rest pose). False if no floor.</summary>
        public bool TryGetFloorFootprint(out Vector3 centerWorld, out float halfX, out float halfZ)
        {
            centerWorld = Vector3.zero; halfX = 0f; halfZ = 0f;
            Part floor = FindPart(SegmentPart.Floor);
            if (floor == null) return false;
            centerWorld = floor.RestWorldBounds.center;
            halfX = Mathf.Max(0.01f, floor.RestWorldBounds.extents.x);
            halfZ = Mathf.Max(0.01f, floor.RestWorldBounds.extents.z);
            return true;
        }

        private Part FindPart(SegmentPart kind)
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i].Kind == kind) return _parts[i];
            return null;
        }

        public void Bind(PuzzleSegmentPlan plan,
            IReadOnlyDictionary<SegmentPart, Transform> partNodes,
            PuzzleCollapseAnimationProfile profile,
            int sessionSeed,
            PuzzleSafetyVolumeSet safety,
            Transform collapsePivot,
            Transform fxAnchor,
            Transform lightAnchor,
            Vector3 awayFromUserWorld,
            Action<PuzzleAnimationEventData> eventSink)
        {
            if (_bound) ResetSegment();

            Plan = plan;
            _profile = profile != null ? profile : PuzzleCollapseAnimationProfile.CreateRuntimeFallback();
            _profile.EnsureCurves();
            _wave = _profile.GetWave(plan);
            _safety = safety;
            _collapsePivot = collapsePivot;
            _fxAnchor = fxAnchor;
            _lightAnchor = lightAnchor;
            _eventSink = eventSink;
            _sessionSeed = sessionSeed;
            _leftWallFirst = PuzzleChoreography.LeftWallFirst(sessionSeed, SegmentId);

            _segmentUpWorld = transform.up.normalized;
            _segmentAwayWorld = Vector3.ProjectOnPlane(awayFromUserWorld, _segmentUpWorld);
            if (_segmentAwayWorld.sqrMagnitude < 0.0001f)
                _segmentAwayWorld = Vector3.ProjectOnPlane(transform.forward, _segmentUpWorld);
            if (_segmentAwayWorld.sqrMagnitude < 0.0001f) _segmentAwayWorld = Vector3.forward;
            _segmentAwayWorld.Normalize();
            _segmentRightWorld = Vector3.Cross(_segmentUpWorld, _segmentAwayWorld).normalized;
            if (_segmentRightWorld.sqrMagnitude < 0.0001f) _segmentRightWorld = transform.right;

            _parts.Clear();
            foreach (SegmentPart kind in PuzzleCollapseSequence.AllParts())
            {
                if (partNodes == null || !partNodes.TryGetValue(kind, out var node) || node == null)
                    continue;
                var part = CapturePart(kind, node);
                part.Movable = !Plan.IsSafe || kind != SegmentPart.Floor;
                _parts.Add(part);
            }

            CacheLocalSignals();
            _bound = _parts.Count > 0;
            ResetSegment();
        }

        /// <summary>Compatibility overload for isolated tests/tools.</summary>
        public void Bind(PuzzleSegmentPlan plan,
            IReadOnlyDictionary<SegmentPart, Transform> partNodes)
        {
            Bind(plan, partNodes, null, 0, null, null, null, null,
                transform.forward, null);
        }

        public void SetSessionSeed(int sessionSeed)
        {
            _sessionSeed = sessionSeed;
            _leftWallFirst = PuzzleChoreography.LeftWallFirst(sessionSeed, SegmentId);
        }

        public void ApplyStage(PressureStage stage)
        {
            _desiredStage = stage;
        }

        /// <summary>
        /// Returns true once when the configured fall progress reveals this segment's
        /// local void backing. PuzzleCorridorController consumes the request so the
        /// backing is not shown immediately when Falling starts.
        /// </summary>
        public bool ConsumeVoidRevealRequest()
        {
            if (!_voidRevealPending) return false;
            _voidRevealPending = false;
            return true;
        }

        public void TickActive(float dt)
        {
            if (!_bound || Plan == null || dt <= 0f) return;

            HasActiveAnimation = false;
            AnimatedPartCount = 0;
            DominantPhase = PuzzleAnimationPhase.Stable;

            for (int i = 0; i < _parts.Count; i++)
            {
                Part part = _parts[i];
                if (part.Node == null || !part.Movable) continue;
                TickPart(part, dt);
                if (part.Action != PartAction.None)
                {
                    HasActiveAnimation = true;
                    AnimatedPartCount++;
                }
                if ((int)part.Phase > (int)DominantPhase)
                    DominantPhase = part.Phase;
            }

            UpdateCollapsedState();
        }

        private Part CapturePart(SegmentPart kind, Transform node)
        {
            var part = new Part
            {
                Kind = kind,
                Node = node,
                OriginalActiveSelf = node.gameObject.activeSelf,
                RestLocalPosition = node.localPosition,
                RestLocalRotation = node.localRotation,
                RestLocalScale = node.localScale,
                Renderers = node.GetComponentsInChildren<Renderer>(true),
                Colliders = node.GetComponentsInChildren<Collider>(true),
                Phase = PuzzleAnimationPhase.Stable,
                Action = PartAction.None
            };

            part.RendererEnabled = new bool[part.Renderers.Length];
            part.RendererPropertyBlocks = new MaterialPropertyBlock[part.Renderers.Length];
            for (int i = 0; i < part.Renderers.Length; i++)
            {
                Renderer renderer = part.Renderers[i];
                part.RendererEnabled[i] = renderer != null && renderer.enabled;
                var block = new MaterialPropertyBlock();
                if (renderer != null) renderer.GetPropertyBlock(block);
                part.RendererPropertyBlocks[i] = block;
            }

            part.ColliderEnabled = new bool[part.Colliders.Length];
            part.ColliderTrigger = new bool[part.Colliders.Length];
            for (int i = 0; i < part.Colliders.Length; i++)
            {
                Collider collider = part.Colliders[i];
                if (collider == null) continue;
                part.ColliderEnabled[i] = collider.enabled;
                part.ColliderTrigger[i] = collider.isTrigger;
            }

            Rigidbody[] bodies = node.GetComponentsInChildren<Rigidbody>(true);
            part.Rigidbodies = new RigidbodySnapshot[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                part.Rigidbodies[i] = new RigidbodySnapshot
                {
                    Body = body,
                    IsKinematic = body.isKinematic,
                    UseGravity = body.useGravity,
                    DetectCollisions = body.detectCollisions,
                    Constraints = body.constraints,
                    LinearVelocity = body.linearVelocity,
                    AngularVelocity = body.angularVelocity
                };
            }

            Bounds bounds = CalculateRendererBounds(part.Renderers, node.position);
            part.RestWorldBoundsCenter = bounds.center;
            part.RestWorldBounds = bounds;
            part.LocalBoundsCorners = BuildLocalBoundsCorners(node, bounds);
            part.OutwardWorld = CalculateOutwardWorld(kind, bounds.center);
            part.PivotWorld = CalculatePivotWorld(kind, bounds, part.OutwardWorld);
            return part;
        }

        private void TickPart(Part part, float dt)
        {
            if (part.Phase == PuzzleAnimationPhase.Hidden) return;

            if (part.Action == PartAction.None)
                StartNextAuthorizedAction(part);
            if (part.Action == PartAction.None) return;

            float activeDt = ConsumeDelay(part, dt);
            if (activeDt <= 0f) return;

            part.Elapsed += activeDt;
            part.NormalizedProgress = Mathf.Clamp01(part.Elapsed / Mathf.Max(0.0001f, part.Duration));

            switch (part.Action)
            {
                case PartAction.Warning:
                    TickWarning(part);
                    break;
                case PartAction.Loosen:
                    TickLoosen(part);
                    break;
                case PartAction.Detach:
                    TickDetach(part);
                    break;
                case PartAction.DetachedHold:
                    TickDetachedHold(part);
                    break;
                case PartAction.Fall:
                    TickFall(part);
                    break;
                case PartAction.Settle:
                    TickSettle(part);
                    break;
                case PartAction.HideDelay:
                    TickHideDelay(part);
                    break;
            }
        }

        private float ConsumeDelay(Part part, float dt)
        {
            if (part.DelayRemaining <= 0f) return dt;
            float consumed = Mathf.Min(part.DelayRemaining, dt);
            part.DelayRemaining -= consumed;
            return dt - consumed;
        }

        private void StartNextAuthorizedAction(Part part)
        {
            if (!part.WarningComplete &&
                PuzzleSegmentPlan.StageReached(_desiredStage, Plan.WarningStage))
            {
                BeginAction(part, PartAction.Warning, PuzzleAnimationPhase.Warning,
                    CatchUpDuration(Plan.WarningStage, _wave.warningDuration),
                    CatchUpDelay(Plan.WarningStage, DelayFor(part, 0.20f)));
                if (!_warningEventSent)
                {
                    _warningEventSent = true;
                    Emit(PuzzleAnimationEventKind.WarningStarted, part.Kind, 0.35f);
                }
                return;
            }

            if (!part.LoosenComplete &&
                PuzzleSegmentPlan.StageReached(_desiredStage, Plan.LoosenStage))
            {
                PrepareLoosenTarget(part);
                DisableMovingColliders(part);
                BeginAction(part, PartAction.Loosen, PuzzleAnimationPhase.Loosening,
                    CatchUpDuration(Plan.LoosenStage, _wave.loosenDuration),
                    CatchUpDelay(Plan.LoosenStage, DelayFor(part, 0.65f)));
                return;
            }

            if (Plan.CanPhysicallyFall && !part.DetachComplete &&
                PuzzleSegmentPlan.StageReached(_desiredStage, Plan.CollapseStage))
            {
                PrepareDetachTarget(part);
                DisableMovingColliders(part);
                BeginAction(part, PartAction.Detach, PuzzleAnimationPhase.Loosening,
                    _wave.detachDuration, DelayFor(part, 1f));
            }
        }

        private void BeginAction(Part part, PartAction action,
            PuzzleAnimationPhase phase, float duration, float delay)
        {
            part.Action = action;
            part.Phase = phase;
            part.Elapsed = 0f;
            part.Duration = Mathf.Max(0.0001f, duration);
            part.DelayRemaining = Mathf.Max(0f, delay);
            part.NormalizedProgress = 0f;
            part.ActionStartLocalPosition = part.Node.localPosition;
            part.ActionStartLocalRotation = part.Node.localRotation;
        }

        private float CatchUpDuration(PressureStage actionStage, float normalDuration)
        {
            return _desiredStage > actionStage
                ? Mathf.Min(normalDuration, 0.14f)
                : normalDuration;
        }

        private float CatchUpDelay(PressureStage actionStage, float normalDelay)
        {
            return _desiredStage > actionStage ? normalDelay * 0.15f : normalDelay;
        }

        private float DelayFor(Part part, float partDelayScale)
        {
            float pairDelay = (Plan.CollapseOrder >= 0 && (Plan.CollapseOrder & 1) == 1)
                ? _profile.pairedSegmentDelay
                : 0f;
            float seeded = PuzzleChoreography.SignedVariation(_sessionSeed,
                SegmentId, part.Kind, 0x1173) * _profile.seededDelayVariation;
            float partDelay = _profile.BasePartDelay(part.Kind, _leftWallFirst) * partDelayScale;
            return Mathf.Max(0f, _wave.segmentStartDelay + pairDelay + partDelay + seeded);
        }

        private void TickWarning(Part part)
        {
            float t = part.NormalizedProgress;
            float envelope = _profile.warningShakeEnvelope.Evaluate(t);
            float phase = (part.Elapsed * _wave.warningFrequencyHz * Mathf.PI * 2f) +
                          StablePhase(part.Kind, 0xA91D);
            float secondary = Mathf.Sin(phase * 0.63f + 1.17f);
            float primary = Mathf.Sin(phase);
            float positional = _wave.warningPositionMeters * envelope;
            float rotational = _wave.warningRotationDegrees * envelope;

            Vector3 offsetWorld = (_segmentRightWorld * primary +
                                   _segmentUpWorld * secondary * 0.45f) * positional;
            Quaternion shakeWorld = Quaternion.AngleAxis(
                rotational * secondary, _segmentAwayWorld);
            ApplyWorldDeltaFromRest(part, offsetWorld, shakeWorld,
                part.RestLocalPosition, part.RestLocalRotation);
            ApplyLocalLightSignal(_profile.lightFlickerCurve.Evaluate(t));

            if (t < 1f) return;
            Vector3 residual = _segmentAwayWorld *
                               Mathf.Min(0.002f, _wave.warningResidualGapMeters);
            ApplyWorldDeltaFromRest(part, residual, Quaternion.identity,
                part.RestLocalPosition, part.RestLocalRotation);
            part.WarningComplete = true;
            part.Action = PartAction.None;
            part.NormalizedProgress = 1f;
            RestoreLocalLightSignal();
            StartNextAuthorizedAction(part);
        }

        private void PrepareLoosenTarget(Part part)
        {
            float gap = _wave.loosenGapMeters;
            float translationLimit = float.PositiveInfinity;
            float rotationLimit = float.PositiveInfinity;
            Vector3 translationWorld;
            Vector3 axisWorld;
            float angle;

            switch (part.Kind)
            {
                case SegmentPart.Ceiling:
                    translationWorld = (Plan.IsSafe ? _segmentUpWorld : -_segmentUpWorld) *
                                       _wave.loosenCeilingDropMeters +
                                       _segmentAwayWorld * gap * 0.35f;
                    axisWorld = _segmentRightWorld;
                    angle = (Plan.IsSafe ? -1f : 1f) * _wave.loosenCeilingRotationDegrees;
                    if (Plan.IsSafe)
                    {
                        translationLimit = _profile.safeCeilingMaximumTranslation;
                        rotationLimit = _profile.safeMaximumRotationDegrees;
                    }
                    break;
                case SegmentPart.WallLeft:
                case SegmentPart.WallRight:
                    translationWorld = part.OutwardWorld * gap +
                                       _segmentAwayWorld * gap * 0.18f;
                    axisWorld = _segmentAwayWorld;
                    angle = WallRotationSign(part) * _wave.loosenWallRotationDegrees;
                    if (Plan.IsSafe)
                    {
                        translationLimit = _profile.safeWallMaximumTranslation;
                        rotationLimit = _profile.safeMaximumRotationDegrees;
                    }
                    break;
                default:
                    translationWorld = -_segmentUpWorld * _wave.loosenFloorDropMeters +
                                       _segmentAwayWorld * gap * 0.20f;
                    axisWorld = _segmentRightWorld;
                    angle = -0.35f * _wave.loosenCeilingRotationDegrees;
                    break;
            }

            translationWorld = Vector3.ClampMagnitude(translationWorld, translationLimit);
            angle = Mathf.Clamp(angle, -rotationLimit, rotationLimit);
            BuildSafeTarget(part, part.Node.localPosition, part.Node.localRotation,
                translationWorld, axisWorld, angle,
                out part.ActionTargetLocalPosition, out part.ActionTargetLocalRotation);
            part.LoosenedLocalPosition = part.ActionTargetLocalPosition;
            part.LoosenedLocalRotation = part.ActionTargetLocalRotation;
        }

        private void TickLoosen(Part part)
        {
            float positionT = Mathf.Clamp01(
                _profile.loosenPositionCurve.Evaluate(part.NormalizedProgress));
            float rotationT = Mathf.Clamp01(
                _profile.loosenRotationCurve.Evaluate(part.NormalizedProgress));
            SetLocalPositionAndRotation(part,
                Vector3.LerpUnclamped(part.ActionStartLocalPosition,
                    part.ActionTargetLocalPosition, positionT),
                Quaternion.SlerpUnclamped(part.ActionStartLocalRotation,
                    part.ActionTargetLocalRotation, rotationT));

            if (part.NormalizedProgress < 1f) return;
            SetLocalPositionAndRotation(part, part.ActionTargetLocalPosition,
                part.ActionTargetLocalRotation);
            part.LoosenComplete = true;
            part.Phase = PuzzleAnimationPhase.Loosening;
            part.Action = PartAction.None;
            if (!_loosenEventSent)
            {
                _loosenEventSent = true;
                Emit(PuzzleAnimationEventKind.JointLoosened, part.Kind, 0.55f);
            }
            StartNextAuthorizedAction(part);
        }

        private void PrepareDetachTarget(Part part)
        {
            Vector3 translationWorld;
            Vector3 axisWorld;
            float variation = PuzzleChoreography.SignedVariation(_sessionSeed,
                SegmentId, part.Kind, 0x7F41) * _profile.seededTiltVariationDegrees;
            float angle;

            switch (part.Kind)
            {
                case SegmentPart.Ceiling:
                    translationWorld = -_segmentUpWorld * 0.06f +
                                       _segmentAwayWorld * _wave.awayReleaseMeters;
                    axisWorld = _segmentRightWorld;
                    angle = 5f + variation;
                    break;
                case SegmentPart.WallLeft:
                case SegmentPart.WallRight:
                    translationWorld = part.OutwardWorld * _wave.lateralReleaseMeters +
                                       _segmentAwayWorld * _wave.awayReleaseMeters;
                    axisWorld = _segmentAwayWorld;
                    angle = WallRotationSign(part) * (6f + variation);
                    break;
                default:
                    translationWorld = -_segmentUpWorld * 0.05f +
                                       _segmentAwayWorld * _wave.awayReleaseMeters * 0.55f;
                    axisWorld = _segmentRightWorld;
                    angle = -3f + variation * 0.5f;
                    break;
            }

            BuildSafeTarget(part, part.Node.localPosition, part.Node.localRotation,
                translationWorld, axisWorld, angle,
                out part.ActionTargetLocalPosition, out part.ActionTargetLocalRotation);
        }

        private void TickDetach(Part part)
        {
            float t = Mathf.Clamp01(_profile.detachCurve.Evaluate(part.NormalizedProgress));
            SetLocalPositionAndRotation(part,
                Vector3.LerpUnclamped(part.ActionStartLocalPosition,
                    part.ActionTargetLocalPosition, t),
                Quaternion.SlerpUnclamped(part.ActionStartLocalRotation,
                    part.ActionTargetLocalRotation, t));

            if (part.NormalizedProgress < 1f) return;
            SetLocalPositionAndRotation(part, part.ActionTargetLocalPosition,
                part.ActionTargetLocalRotation);
            part.DetachComplete = true;
            part.Phase = PuzzleAnimationPhase.Detached;
            if (!_detachEventSent)
            {
                _detachEventSent = true;
                Emit(PuzzleAnimationEventKind.SegmentDetached, part.Kind, 0.72f);
            }
            BeginAction(part, PartAction.DetachedHold, PuzzleAnimationPhase.Detached,
                Mathf.Max(0.0001f, _wave.detachedHoldSeconds), 0f);
        }

        private void TickDetachedHold(Part part)
        {
            if (part.NormalizedProgress < 1f) return;
            PrepareFallTarget(part);
            BeginAction(part, PartAction.Fall, PuzzleAnimationPhase.Falling,
                _wave.fallDuration, 0f);
            if (!_fallEventSent)
            {
                _fallEventSent = true;
                Emit(PuzzleAnimationEventKind.SegmentFalling, part.Kind, 0.85f);
            }
        }

        private void PrepareFallTarget(Part part)
        {
            Vector3 translationWorld = -_segmentUpWorld * _wave.fallDistanceMeters +
                                       _segmentAwayWorld * _wave.awayReleaseMeters;
            Vector3 axisWorld;
            float sign;
            switch (part.Kind)
            {
                case SegmentPart.WallLeft:
                case SegmentPart.WallRight:
                    translationWorld += part.OutwardWorld * _wave.lateralReleaseMeters;
                    axisWorld = _segmentAwayWorld;
                    sign = WallRotationSign(part);
                    break;
                case SegmentPart.Ceiling:
                    axisWorld = _segmentRightWorld;
                    sign = 1f;
                    break;
                default:
                    axisWorld = _segmentRightWorld;
                    sign = -0.55f;
                    break;
            }

            float variation = PuzzleChoreography.SignedVariation(_sessionSeed,
                SegmentId, part.Kind, 0xC0DE) * _profile.seededTiltVariationDegrees;
            BuildSafeTarget(part, part.Node.localPosition, part.Node.localRotation,
                translationWorld, axisWorld,
                sign * (_wave.fallRotationDegrees + variation),
                out part.FallFinalLocalPosition, out part.FallFinalLocalRotation);
        }

        private void TickFall(Part part)
        {
            float positionT = Mathf.Clamp01(
                _profile.fallPositionCurve.Evaluate(part.NormalizedProgress));
            float rotationT = Mathf.Clamp01(
                _profile.fallRotationCurve.Evaluate(part.NormalizedProgress));
            SetLocalPositionAndRotation(part,
                Vector3.LerpUnclamped(part.ActionStartLocalPosition,
                    part.FallFinalLocalPosition, positionT),
                Quaternion.SlerpUnclamped(part.ActionStartLocalRotation,
                    part.FallFinalLocalRotation, rotationT));

            if (!part.VoidRequested &&
                part.NormalizedProgress >= _profile.revealVoidAtFallProgress)
            {
                part.VoidRequested = true;
                _voidRevealPending = true;
            }

            float visibility = _profile.visibilityCurve.Evaluate(part.NormalizedProgress);
            if (!part.RenderersHidden &&
                part.NormalizedProgress >= _profile.hideRendererAtFallProgress &&
                visibility <= 0.10f)
                SetRenderersVisible(part, false);

            if (part.NormalizedProgress < 1f) return;
            SetLocalPositionAndRotation(part, part.FallFinalLocalPosition,
                part.FallFinalLocalRotation);
            part.FallComplete = true;
            if (_wave.settleDuration > 0f)
            {
                BeginAction(part, PartAction.Settle, PuzzleAnimationPhase.Falling,
                    _wave.settleDuration, 0f);
            }
            else
            {
                BeginAction(part, PartAction.HideDelay, PuzzleAnimationPhase.Falling,
                    Mathf.Max(0.0001f, _wave.hideDelaySeconds), 0f);
            }
        }

        private void TickSettle(Part part)
        {
            float t = _profile.settleCurve.Evaluate(part.NormalizedProgress);
            Vector3 tinyDropLocal = part.Node.parent != null
                ? part.Node.parent.InverseTransformVector(-_segmentUpWorld * 0.035f)
                : -_segmentUpWorld * 0.035f;
            SetLocalPositionAndRotation(part,
                part.FallFinalLocalPosition + tinyDropLocal * (t - 1f),
                part.FallFinalLocalRotation);
            if (part.NormalizedProgress < 1f) return;
            SetLocalPositionAndRotation(part, part.FallFinalLocalPosition,
                part.FallFinalLocalRotation);
            BeginAction(part, PartAction.HideDelay, PuzzleAnimationPhase.Falling,
                Mathf.Max(0.0001f, _wave.hideDelaySeconds), 0f);
        }

        private void TickHideDelay(Part part)
        {
            if (part.NormalizedProgress < 1f) return;
            SetRenderersVisible(part, false);
            part.Phase = PuzzleAnimationPhase.Hidden;
            part.Action = PartAction.None;
            part.NormalizedProgress = 1f;
            UpdateCollapsedState();
        }

        private void UpdateCollapsedState()
        {
            if (!Plan.CanPhysicallyFall)
            {
                HasFullyCollapsed = false;
                return;
            }

            bool anyMovable = false;
            bool allHidden = true;
            for (int i = 0; i < _parts.Count; i++)
            {
                Part part = _parts[i];
                if (!part.Movable) continue;
                anyMovable = true;
                if (part.Phase != PuzzleAnimationPhase.Hidden) allHidden = false;
            }
            HasFullyCollapsed = anyMovable && allHidden;
            if (HasFullyCollapsed && !_hiddenEventSent)
            {
                _hiddenEventSent = true;
                Emit(PuzzleAnimationEventKind.SegmentHidden, SegmentPart.Floor, 1f);
            }
        }

        private void ApplyWorldDeltaFromRest(Part part, Vector3 translationWorld,
            Quaternion rotationWorld, Vector3 baseLocalPosition,
            Quaternion baseLocalRotation)
        {
            Transform parent = part.Node.parent;
            Vector3 baseWorldPosition = parent != null
                ? parent.TransformPoint(baseLocalPosition)
                : baseLocalPosition;
            Quaternion baseWorldRotation = parent != null
                ? parent.rotation * baseLocalRotation
                : baseLocalRotation;
            Vector3 targetWorldPosition = part.PivotWorld +
                rotationWorld * (baseWorldPosition - part.PivotWorld) + translationWorld;
            Quaternion targetWorldRotation = rotationWorld * baseWorldRotation;
            Vector3 targetLocalPosition = parent != null
                ? parent.InverseTransformPoint(targetWorldPosition)
                : targetWorldPosition;
            Quaternion targetLocalRotation = parent != null
                ? Quaternion.Inverse(parent.rotation) * targetWorldRotation
                : targetWorldRotation;
            SetLocalPositionAndRotation(part, targetLocalPosition, targetLocalRotation);
        }

        private void BuildSafeTarget(Part part, Vector3 startLocalPosition,
            Quaternion startLocalRotation, Vector3 translationWorld,
            Vector3 rotationAxisWorld, float rotationDegrees,
            out Vector3 targetLocalPosition, out Quaternion targetLocalRotation)
        {
            Transform parent = part.Node.parent;
            Vector3 startWorldPosition = parent != null
                ? parent.TransformPoint(startLocalPosition)
                : startLocalPosition;
            Quaternion startWorldRotation = parent != null
                ? parent.rotation * startLocalRotation
                : startLocalRotation;
            Quaternion deltaRotation = Quaternion.AngleAxis(rotationDegrees,
                rotationAxisWorld.normalized);
            Vector3 targetWorldPosition = part.PivotWorld +
                deltaRotation * (startWorldPosition - part.PivotWorld) + translationWorld;
            Quaternion targetWorldRotation = deltaRotation * startWorldRotation;
            targetLocalPosition = parent != null
                ? parent.InverseTransformPoint(targetWorldPosition)
                : targetWorldPosition;
            targetLocalRotation = parent != null
                ? Quaternion.Inverse(parent.rotation) * targetWorldRotation
                : targetWorldRotation;

            if (_safety == null || !_safety.IsConfigured ||
                !TargetIntersectsSafety(part, targetLocalPosition, targetLocalRotation))
                return;

            Vector3 unsafePosition = targetLocalPosition;
            Quaternion unsafeRotation = targetLocalRotation;
            float low = 0f;
            float high = 1f;
            for (int i = 0; i < _profile.safetyClampIterations; i++)
            {
                float mid = (low + high) * 0.5f;
                Vector3 candidatePosition = Vector3.Lerp(startLocalPosition,
                    unsafePosition, mid);
                Quaternion candidateRotation = Quaternion.Slerp(startLocalRotation,
                    unsafeRotation, mid);
                if (TargetIntersectsSafety(part, candidatePosition, candidateRotation))
                    high = mid;
                else
                    low = mid;
            }
            targetLocalPosition = Vector3.Lerp(startLocalPosition, unsafePosition, low);
            targetLocalRotation = Quaternion.Slerp(startLocalRotation, unsafeRotation, low);
        }

        private bool TargetIntersectsSafety(Part part, Vector3 localPosition,
            Quaternion localRotation)
        {
            if (part.LocalBoundsCorners == null || part.LocalBoundsCorners.Length == 0)
                return false;
            Transform parent = part.Node.parent;
            Matrix4x4 parentMatrix = parent != null ? parent.localToWorldMatrix : Matrix4x4.identity;
            Matrix4x4 worldMatrix = parentMatrix * Matrix4x4.TRS(
                localPosition, localRotation, part.RestLocalScale);
            Vector3 first = worldMatrix.MultiplyPoint3x4(part.LocalBoundsCorners[0]);
            var bounds = new Bounds(first, Vector3.zero);
            for (int i = 1; i < part.LocalBoundsCorners.Length; i++)
                bounds.Encapsulate(worldMatrix.MultiplyPoint3x4(part.LocalBoundsCorners[i]));
            return _safety.Intersects(bounds);
        }

        private void DisableMovingColliders(Part part)
        {
            if (!part.Movable || (Plan.IsSafe && part.Kind == SegmentPart.Floor)) return;
            for (int i = 0; i < part.Colliders.Length; i++)
                if (part.Colliders[i] != null) part.Colliders[i].enabled = false;
        }

        private void SetRenderersVisible(Part part, bool visible)
        {
            for (int i = 0; i < part.Renderers.Length; i++)
            {
                Renderer renderer = part.Renderers[i];
                if (renderer == null) continue;
                renderer.enabled = visible ? part.RendererEnabled[i] : false;
            }
            part.RenderersHidden = !visible;
        }

        private void SetLocalPositionAndRotation(Part part, Vector3 position,
            Quaternion rotation)
        {
#if UNITY_6000_0_OR_NEWER
            part.Node.SetLocalPositionAndRotation(position, rotation);
#else
            part.Node.localPosition = position;
            part.Node.localRotation = rotation;
#endif
        }

        private float StablePhase(SegmentPart part, int salt)
        {
            float signed = PuzzleChoreography.SignedVariation(_sessionSeed,
                SegmentId, part, salt);
            return (signed + 1f) * Mathf.PI;
        }

        private float WallRotationSign(Part part)
        {
            return Vector3.Dot(part.OutwardWorld, _segmentRightWorld) >= 0f ? -1f : 1f;
        }

        private Vector3 CalculateOutwardWorld(SegmentPart kind, Vector3 boundsCenter)
        {
            Vector3 fromCenter = Vector3.ProjectOnPlane(boundsCenter - transform.position,
                _segmentUpWorld);
            fromCenter = Vector3.ProjectOnPlane(fromCenter, _segmentAwayWorld);
            if (fromCenter.sqrMagnitude > 0.0001f) return fromCenter.normalized;
            if (kind == SegmentPart.WallLeft) return -_segmentRightWorld;
            if (kind == SegmentPart.WallRight) return _segmentRightWorld;
            return _segmentAwayWorld;
        }

        private Vector3 CalculatePivotWorld(SegmentPart kind, Bounds bounds,
            Vector3 outwardWorld)
        {
            Vector3 calculated = bounds.center;
            if (kind == SegmentPart.WallLeft || kind == SegmentPart.WallRight)
                calculated += outwardWorld * ProjectedExtent(bounds.extents, outwardWorld);
            else
                calculated += _segmentAwayWorld * ProjectedExtent(bounds.extents,
                    _segmentAwayWorld);

            // The authored CollapsePivot remains part of the hinge solution when it
            // exists, while the bounds-derived edge prevents all four pieces from
            // rotating as one rigid box around a single central point.
            if (_collapsePivot != null)
                calculated = Vector3.Lerp(calculated, _collapsePivot.position, 0.35f);
            return calculated;
        }

        private static float ProjectedExtent(Vector3 extents, Vector3 direction)
        {
            direction = new Vector3(Mathf.Abs(direction.x),
                Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            return Vector3.Dot(extents, direction);
        }

        private static Bounds CalculateRendererBounds(Renderer[] renderers,
            Vector3 fallbackCenter)
        {
            bool hasBounds = false;
            var bounds = new Bounds(fallbackCenter, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            if (!hasBounds) bounds = new Bounds(fallbackCenter, Vector3.one * 0.05f);
            return bounds;
        }

        private static Vector3[] BuildLocalBoundsCorners(Transform node, Bounds worldBounds)
        {
            Vector3 min = worldBounds.min;
            Vector3 max = worldBounds.max;
            var corners = new Vector3[8];
            int index = 0;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                Vector3 world = new Vector3(x == 0 ? min.x : max.x,
                    y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                corners[index++] = node.InverseTransformPoint(world);
            }
            return corners;
        }

        private void CacheLocalSignals()
        {
            if (_lightAnchor == null)
            {
                _localLights = Array.Empty<Light>();
                _localLightIntensity = Array.Empty<float>();
                _localSignalRenderers = Array.Empty<Renderer>();
                _localSignalOriginalBlocks = Array.Empty<MaterialPropertyBlock>();
                _localSignalWorkingBlocks = Array.Empty<MaterialPropertyBlock>();
                _localSignalEmission = Array.Empty<Color>();
                return;
            }

            _localLights = _lightAnchor.GetComponentsInChildren<Light>(true);
            _localLightIntensity = new float[_localLights.Length];
            for (int i = 0; i < _localLights.Length; i++)
                if (_localLights[i] != null) _localLightIntensity[i] = _localLights[i].intensity;

            _localSignalRenderers = _lightAnchor.GetComponentsInChildren<Renderer>(true);
            _localSignalOriginalBlocks = new MaterialPropertyBlock[_localSignalRenderers.Length];
            _localSignalWorkingBlocks = new MaterialPropertyBlock[_localSignalRenderers.Length];
            _localSignalEmission = new Color[_localSignalRenderers.Length];
            for (int i = 0; i < _localSignalRenderers.Length; i++)
            {
                Renderer renderer = _localSignalRenderers[i];
                var original = new MaterialPropertyBlock();
                if (renderer != null) renderer.GetPropertyBlock(original);
                _localSignalOriginalBlocks[i] = original;
                _localSignalWorkingBlocks[i] = new MaterialPropertyBlock();
                Material material = renderer != null ? renderer.sharedMaterial : null;
                _localSignalEmission[i] = material != null && material.HasProperty("_EmissionColor")
                    ? material.GetColor("_EmissionColor")
                    : Color.black;
            }
        }

        private void ApplyLocalLightSignal(float curveValue)
        {
            float multiplier = Mathf.Lerp(0.60f, 1f, Mathf.Clamp01(curveValue));
            for (int i = 0; i < _localLights.Length; i++)
                if (_localLights[i] != null)
                    _localLights[i].intensity = _localLightIntensity[i] * multiplier;

            for (int i = 0; i < _localSignalRenderers.Length; i++)
            {
                Renderer renderer = _localSignalRenderers[i];
                if (renderer == null) continue;
                renderer.SetPropertyBlock(_localSignalOriginalBlocks[i]);
                MaterialPropertyBlock working = _localSignalWorkingBlocks[i];
                renderer.GetPropertyBlock(working);
                working.SetColor("_EmissionColor", _localSignalEmission[i] * multiplier);
                renderer.SetPropertyBlock(working);
            }
        }

        private void RestoreLocalLightSignal()
        {
            for (int i = 0; i < _localLights.Length; i++)
                if (_localLights[i] != null)
                    _localLights[i].intensity = _localLightIntensity[i];
            for (int i = 0; i < _localSignalRenderers.Length; i++)
                if (_localSignalRenderers[i] != null)
                    _localSignalRenderers[i].SetPropertyBlock(_localSignalOriginalBlocks[i]);
        }

        private void Emit(PuzzleAnimationEventKind kind, SegmentPart part,
            float magnitude)
        {
            if (_eventSink == null) return;
            _eventData.Kind = kind;
            _eventData.SegmentId = SegmentId;
            _eventData.Part = part;
            _eventData.Magnitude = Mathf.Clamp01(magnitude);
            _eventData.WorldPosition = _fxAnchor != null
                ? _fxAnchor.position
                : transform.position;
            _eventSink(_eventData);
        }

        public bool TryGetPartDiagnostics(SegmentPart kind,
            out PuzzleAnimationPhase phase, out float normalizedProgress,
            out float delayRemaining, out bool hidden, out bool movable)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                Part part = _parts[i];
                if (part.Kind != kind) continue;
                phase = part.Phase;
                normalizedProgress = Mathf.Clamp01(part.NormalizedProgress);
                delayRemaining = Mathf.Max(0f, part.DelayRemaining);
                hidden = part.RenderersHidden;
                movable = part.Movable;
                return true;
            }
            phase = PuzzleAnimationPhase.Stable;
            normalizedProgress = 0f;
            delayRemaining = 0f;
            hidden = false;
            movable = false;
            return false;
        }

        public Transform GetBoundPartNode(SegmentPart kind)
        {
            for (int i = 0; i < _parts.Count; i++)
                if (_parts[i].Kind == kind) return _parts[i].Node;
            return null;
        }

        public int ActiveRendererCount()
        {
            int count = 0;
            for (int i = 0; i < _parts.Count; i++)
            for (int r = 0; r < _parts[i].Renderers.Length; r++)
                if (_parts[i].Renderers[r] != null && _parts[i].Renderers[r].enabled)
                    count++;
            return count;
        }

        public string Diagnostics()
        {
            Part active = null;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].Action != PartAction.None)
                {
                    active = _parts[i];
                    break;
                }
            }
            if (active == null)
                return SegmentId + ": phase=" + DominantPhase + " action=— hidden=" +
                       HasFullyCollapsed;
            return SegmentId + ": part=" + active.Kind + " phase=" + active.Phase +
                   " action=" + active.Action + " progress=" +
                   active.NormalizedProgress.ToString("0.000") + " delay=" +
                   active.DelayRemaining.ToString("0.000") + " hidden=" +
                   active.RenderersHidden;
        }

        /// <summary>Idempotent restoration of every state captured during Bind.</summary>
        public void ResetSegment()
        {
            _desiredStage = PressureStage.Stable;
            HasFullyCollapsed = false;
            HasActiveAnimation = false;
            AnimatedPartCount = 0;
            DominantPhase = PuzzleAnimationPhase.Resetting;
            _warningEventSent = false;
            _loosenEventSent = false;
            _detachEventSent = false;
            _fallEventSent = false;
            _hiddenEventSent = false;
            _voidRevealPending = false;

            for (int i = 0; i < _parts.Count; i++) RestorePart(_parts[i]);
            RestoreLocalLightSignal();
            DominantPhase = PuzzleAnimationPhase.Stable;
        }

        private void RestorePart(Part part)
        {
            if (part.Node == null) return;
            part.Node.gameObject.SetActive(part.OriginalActiveSelf);
            part.Node.localPosition = part.RestLocalPosition;
            part.Node.localRotation = part.RestLocalRotation;
            part.Node.localScale = part.RestLocalScale;

            for (int i = 0; i < part.Renderers.Length; i++)
            {
                Renderer renderer = part.Renderers[i];
                if (renderer == null) continue;
                renderer.enabled = part.RendererEnabled[i];
                renderer.SetPropertyBlock(part.RendererPropertyBlocks[i]);
            }
            for (int i = 0; i < part.Colliders.Length; i++)
            {
                Collider collider = part.Colliders[i];
                if (collider == null) continue;
                collider.isTrigger = part.ColliderTrigger[i];
                collider.enabled = part.ColliderEnabled[i];
            }
            for (int i = 0; i < part.Rigidbodies.Length; i++)
            {
                RigidbodySnapshot snapshot = part.Rigidbodies[i];
                if (snapshot == null || snapshot.Body == null) continue;
                snapshot.Body.isKinematic = snapshot.IsKinematic;
                snapshot.Body.useGravity = snapshot.UseGravity;
                snapshot.Body.detectCollisions = snapshot.DetectCollisions;
                snapshot.Body.constraints = snapshot.Constraints;
                snapshot.Body.linearVelocity = snapshot.LinearVelocity;
                snapshot.Body.angularVelocity = snapshot.AngularVelocity;
            }

            part.Phase = PuzzleAnimationPhase.Stable;
            part.Action = PartAction.None;
            part.DelayRemaining = 0f;
            part.Elapsed = 0f;
            part.Duration = 0f;
            part.NormalizedProgress = 0f;
            part.WarningComplete = false;
            part.LoosenComplete = false;
            part.DetachComplete = false;
            part.FallComplete = false;
            part.RenderersHidden = false;
            part.VoidRequested = false;
        }
    }
}
