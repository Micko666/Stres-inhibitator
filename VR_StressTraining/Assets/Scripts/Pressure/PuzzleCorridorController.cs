using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace StressTraining.Pressure
{
    /// <summary>
    /// Central tick/coordinator for the imported puzzle corridor. PressureController
    /// remains the pressure director and PressureTimeline remains the stage owner.
    /// This component binds geometry once, advances segment state without coroutines,
    /// owns lightweight local void backings, and restores everything between sessions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PuzzleCorridorController : MonoBehaviour
    {
        private sealed class SegmentVoid
        {
            public string SegmentId;
            public GameObject Object;
            public Renderer Renderer;
        }

        public const string RootNodeName = "Corridor_Modular_Puzzle_ROOT";
        public const string DefaultProfileResourcePath =
            "Pressure/PuzzleCollapseAnimationProfile_Default";

        [SerializeField] private PuzzleCollapseAnimationProfile animationProfile;
        [SerializeField, Range(0f, 1f)] private float developerScrubNormalized;

        private readonly List<PuzzleSegmentController> _segments =
            new List<PuzzleSegmentController>(7);
        private readonly List<SegmentVoid> _voids = new List<SegmentVoid>(6);
        private readonly PuzzleSafetyVolumeSet _safety = new PuzzleSafetyVolumeSet();
        private readonly PuzzleAnimationEventData _finalEventData =
            new PuzzleAnimationEventData();
        private PuzzleSegmentController _safe;
        private PressureStage _stage = PressureStage.Stable;
        private Transform _puzzleRoot;
        private Material _voidMaterial;
        private int _sessionSeed;
        private bool _bound;
        private bool _usingFallbackProfile;
        private bool _finalCollapseEventSent;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _developerPreviewRunning;
        private bool _developerPreviewPaused;
        private bool _developerTwoMinutePreview;
        private float _developerPreviewRemaining;
#endif

        public bool IsBound => _bound;
        public int SegmentCount => _segments.Count;
        public bool HasSafeSegment => _safe != null;
        public PressureStage CurrentStage => _stage;
        public int SessionSeed => _sessionSeed;
        public bool UsingFallbackProfile => _usingFallbackProfile;
        public PuzzleCollapseAnimationProfile AnimationProfile => animationProfile;
        public int VoidCount => _voids.Count;
        public int ActiveVoidCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _voids.Count; i++)
                    if (_voids[i].Object != null && _voids[i].Object.activeSelf) count++;
                return count;
            }
        }

        public event Action<PuzzleAnimationEventData> AnimationEvent;

        public void ConfigureAnimationProfile(PuzzleCollapseAnimationProfile profile)
        {
            if (_bound)
                Debug.LogWarning("[PuzzleCorridor] Animation profile changed after Bind; " +
                                 "rebind the corridor before the next preview/session.");
            animationProfile = profile;
            _usingFallbackProfile = profile == null;
        }

        public bool Bind(Transform corridorRoot)
        {
            ResetCorridor();
            _segments.Clear();
            _safe = null;
            _bound = false;
            _puzzleRoot = null;
            _finalCollapseEventSent = false;
            if (corridorRoot == null) return false;

            ResolveAnimationProfile();
            _puzzleRoot = FindDeep(corridorRoot, RootNodeName) ?? corridorRoot;

            var discovered = new List<string>(7);
            var nodeById = new Dictionary<string, Transform>(7);
            CollectSegmentNodes(_puzzleRoot, discovered, nodeById);
            if (discovered.Count == 0) return false;

            Transform safeNode = null;
            nodeById.TryGetValue(PuzzleCollapseSequence.SafeId, out safeNode);
            Vector3 safePosition = safeNode != null
                ? safeNode.position
                : _puzzleRoot.position;

            var plans = PuzzleCollapseSequence.Build(discovered);
            for (int i = 0; i < plans.Count; i++)
            {
                PuzzleSegmentPlan plan = plans[i];
                if (!nodeById.TryGetValue(plan.SegmentId, out var segmentNode) ||
                    segmentNode == null)
                    continue;

                var parts = CollectParts(segmentNode, plan.SegmentId);
                if (parts.Count == 0)
                {
                    Debug.LogError("[PuzzleCorridor] Segment_" + plan.SegmentId +
                                   " has no bindable Floor/Ceiling/Wall parts.");
                    continue;
                }

                Vector3 away = Vector3.ProjectOnPlane(segmentNode.position - safePosition,
                    _puzzleRoot.up);
                if (away.sqrMagnitude < 0.0001f)
                    away = _puzzleRoot.forward * Mathf.Max(1, plan.CollapseOrder + 1);

                Transform collapsePivot = FindDeep(segmentNode,
                    "Segment_" + plan.SegmentId + "_CollapsePivot");
                Transform fxAnchor = FindDeep(segmentNode,
                    "Segment_" + plan.SegmentId + "_FXAnchor");
                Transform lightAnchor = FindDeep(segmentNode,
                    "Segment_" + plan.SegmentId + "_LightAnchor");

                var controller = segmentNode.GetComponent<PuzzleSegmentController>();
                if (controller == null)
                    controller = segmentNode.gameObject.AddComponent<PuzzleSegmentController>();
                controller.Bind(plan, parts, animationProfile, _sessionSeed, _safety,
                    collapsePivot, fxAnchor, lightAnchor, away, OnSegmentAnimationEvent);

                _segments.Add(controller);
                if (plan.IsSafe)
                    _safe = controller;
                else
                    CreateOrReuseVoid(plan.SegmentId, segmentNode, away);
            }

            _bound = _segments.Count > 0;
            if (!_bound) ResetVoidObjects();
            return _bound;
        }

        public void ConfigureSafety(Transform head, Transform console,
            Transform tablet, Transform controls)
        {
            _safety.Configure(head, console, tablet, controls, animationProfile);
        }

        public void SetSessionSeed(int sessionSeed)
        {
            _sessionSeed = sessionSeed;
            for (int i = 0; i < _segments.Count; i++)
                _segments[i].SetSessionSeed(sessionSeed);
        }

        public void ApplyStage(PressureStage stage)
        {
            if (!_bound) return;
            PressureStage previous = _stage;
            _stage = stage;
            for (int i = 0; i < _segments.Count; i++)
                _segments[i].ApplyStage(stage);

            if (stage == PressureStage.Expired && previous != PressureStage.Expired &&
                !_finalCollapseEventSent)
            {
                _finalCollapseEventSent = true;
                _finalEventData.Kind = PuzzleAnimationEventKind.FinalCollapseStarted;
                _finalEventData.SegmentId = "S5/S6";
                _finalEventData.Part = SegmentPart.Ceiling;
                _finalEventData.Magnitude = 1f;
                _finalEventData.WorldPosition = _safe != null
                    ? _safe.transform.position
                    : transform.position;
                AnimationEvent?.Invoke(_finalEventData);
            }
        }

        /// <summary>
        /// Logical ground check used by the fall system (independent of physics
        /// colliders): is there still a solid floor under this world point?
        ///   • Over the Safe segment → always true (the participant is never dropped there).
        ///   • Over a collapse segment → true until that floor detaches, then false.
        ///   • Not over any segment footprint → false (open space).
        /// Returns true when unbound so nothing falls outside a pressure session.
        /// </summary>
        public bool HasFloorAt(Vector3 worldPos)
        {
            if (!_bound) return true;
            bool insideAnyFootprint = false;
            for (int i = 0; i < _segments.Count; i++)
            {
                if (!_segments[i].TryGetFloorFootprint(out Vector3 c, out float hx, out float hz))
                    continue;
                if (Mathf.Abs(worldPos.x - c.x) > hx || Mathf.Abs(worldPos.z - c.z) > hz)
                    continue;
                insideAnyFootprint = true;
                if (_segments[i].FloorIsSolid()) return true;   // solid floor here
            }
            // Inside a footprint but that floor is gone → a hole. Outside every
            // footprint → open space. Either way, no floor to stand on.
            _ = insideAnyFootprint;
            return false;
        }

        /// <summary>Fed active, pause-aware time only by PressureController.</summary>
        public void TickActive(float dt)
        {
            if (!_bound || dt <= 0f) return;
            for (int i = 0; i < _segments.Count; i++)
            {
                PuzzleSegmentController segment = _segments[i];
                segment.TickActive(dt);
                if (segment.ConsumeVoidRevealRequest())
                    SetVoidVisible(segment.SegmentId, true);
            }
        }

        public void ResetCorridor()
        {
            _stage = PressureStage.Stable;
            _finalCollapseEventSent = false;
            for (int i = 0; i < _segments.Count; i++)
                if (_segments[i] != null) _segments[i].ResetSegment();
            ResetVoidObjects();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _developerPreviewRunning = false;
            _developerPreviewPaused = false;
            _developerTwoMinutePreview = false;
            _developerPreviewRemaining = 0f;
#endif
        }

        private void ResolveAnimationProfile()
        {
            if (animationProfile == null)
                animationProfile = Resources.Load<PuzzleCollapseAnimationProfile>(
                    DefaultProfileResourcePath);
            _usingFallbackProfile = animationProfile == null ||
                animationProfile.name.IndexOf("RuntimeFallback",
                    StringComparison.Ordinal) >= 0;
            if (animationProfile == null)
            {
                animationProfile = PuzzleCollapseAnimationProfile.CreateRuntimeFallback();
                _usingFallbackProfile = true;
                Debug.LogWarning("[PuzzleCorridor] Default animation profile asset was not " +
                                 "loaded. Stable runtime fallback values are active.");
            }
            animationProfile.EnsureCurves();
        }

        private void OnSegmentAnimationEvent(PuzzleAnimationEventData data)
        {
            if (data == null) return;
            AnimationEvent?.Invoke(data);
        }

        private void CreateOrReuseVoid(string segmentId, Transform segmentNode,
            Vector3 awayWorld)
        {
            // Disabled by default (see profile.useVoidBackings): the puzzle corridor
            // exposes the real space behind a fallen segment, so a fake dark quad only
            // reads as a panel floating in the room.
            if (animationProfile == null || !animationProfile.useVoidBackings) return;

            SegmentVoid existing = FindVoid(segmentId);
            GameObject go = existing != null ? existing.Object : null;
            if (go == null)
            {
                Transform existingTransform = FindDirectOrDeep(_puzzleRoot,
                    "PuzzleVoid_" + segmentId);
                if (existingTransform != null)
                {
                    go = existingTransform.gameObject;
                }
                else
                {
                    go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = "PuzzleVoid_" + segmentId;
                    Collider collider = go.GetComponent<Collider>();
                    if (collider != null)
                    {
#if UNITY_EDITOR
                        if (!Application.isPlaying) DestroyImmediate(collider);
                        else Destroy(collider);
#else
                        Destroy(collider);
#endif
                    }
                }
                go.transform.SetParent(_puzzleRoot, true);
                existing = new SegmentVoid
                {
                    SegmentId = segmentId,
                    Object = go,
                    Renderer = go.GetComponent<Renderer>()
                };
                _voids.Add(existing);
            }

            if (go.transform.parent != _puzzleRoot)
                go.transform.SetParent(_puzzleRoot, true);

            EnsureVoidMaterial();
            if (existing.Renderer != null && _voidMaterial != null)
                existing.Renderer.sharedMaterial = _voidMaterial;

            Renderer[] renderers = segmentNode.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = CalculateBounds(renderers, segmentNode.position);
            Vector3 up = _puzzleRoot.up.normalized;
            awayWorld = Vector3.ProjectOnPlane(awayWorld, up);
            if (awayWorld.sqrMagnitude < 0.0001f) awayWorld = _puzzleRoot.forward;
            awayWorld.Normalize();
            Vector3 right = Vector3.Cross(up, awayWorld).normalized;
            if (right.sqrMagnitude < 0.0001f) right = _puzzleRoot.right;

            float width = Mathf.Max(0.25f, ProjectedSize(bounds.extents, right));
            float height = Mathf.Max(0.25f, ProjectedSize(bounds.extents, up));
            go.transform.position = bounds.center + awayWorld *
                animationProfile.voidSurfaceOffsetMeters;
            go.transform.rotation = Quaternion.LookRotation(awayWorld, up);
            go.transform.localScale = new Vector3(width, height,
                animationProfile.voidThicknessMeters);
            go.SetActive(false);
        }

        private void EnsureVoidMaterial()
        {
            if (_voidMaterial != null) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("[PuzzleCorridor] No supported Unlit shader was found " +
                               "for local void backings. Geometry animation will continue.");
                return;
            }
            _voidMaterial = new Material(shader)
            {
                name = "PuzzleVoid_Shared_Runtime",
                color = animationProfile.voidColor,
                hideFlags = HideFlags.DontSave
            };
            if (_voidMaterial.HasProperty("_BaseColor"))
                _voidMaterial.SetColor("_BaseColor", animationProfile.voidColor);
            if (_voidMaterial.HasProperty("_Color"))
                _voidMaterial.SetColor("_Color", animationProfile.voidColor);
        }

        private SegmentVoid FindVoid(string segmentId)
        {
            for (int i = 0; i < _voids.Count; i++)
                if (string.Equals(_voids[i].SegmentId, segmentId,
                    StringComparison.Ordinal)) return _voids[i];
            return null;
        }

        private void SetVoidVisible(string segmentId, bool visible)
        {
            SegmentVoid item = FindVoid(segmentId);
            if (item != null && item.Object != null) item.Object.SetActive(visible);
        }

        private void ResetVoidObjects()
        {
            for (int i = 0; i < _voids.Count; i++)
                if (_voids[i].Object != null) _voids[i].Object.SetActive(false);
        }

        public PuzzleSegmentController GetSegment(string segmentId)
        {
            for (int i = 0; i < _segments.Count; i++)
                if (string.Equals(_segments[i].SegmentId, segmentId,
                    StringComparison.Ordinal)) return _segments[i];
            return null;
        }

        public int CollapsedSegmentCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _segments.Count; i++)
                    if (_segments[i].HasFullyCollapsed) count++;
                return count;
            }
        }

        public int AnimatedPartCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _segments.Count; i++)
                    count += _segments[i].AnimatedPartCount;
                return count;
            }
        }

        public int ActiveRendererCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _segments.Count; i++)
                    count += _segments[i].ActiveRendererCount();
                for (int i = 0; i < _voids.Count; i++)
                    if (_voids[i].Renderer != null && _voids[i].Renderer.enabled &&
                        _voids[i].Object.activeSelf) count++;
                return count;
            }
        }

        public string ActiveSegmentId
        {
            get
            {
                for (int i = 0; i < _segments.Count; i++)
                    if (_segments[i].HasActiveAnimation) return _segments[i].SegmentId;
                return "—";
            }
        }

        public string Diagnostics()
        {
            var sb = new StringBuilder(320);
            sb.Append("PuzzleCorridor: ").Append(_bound ? "bound" : "NOT BOUND");
            sb.Append(" · profile ").Append(_usingFallbackProfile ? "FALLBACK" : animationProfile.name);
            sb.Append(" · condition stage ").Append(_stage);
            sb.Append(" · seed ").Append(_sessionSeed);
            sb.Append(" · segmenata ").Append(_segments.Count);
            sb.Append(" · safe ").Append(_safe != null ? "DA" : "NE");
            sb.Append(" · urušeno ").Append(CollapsedSegmentCount);
            sb.Append(" · animirani djelovi ").Append(AnimatedPartCount);
            sb.Append(" · aktivni rendereri ").Append(ActiveRendererCount);
            sb.Append(" · void ").Append(ActiveVoidCount).Append('/').Append(_voids.Count);
            sb.Append(" · aktivan ").Append(ActiveSegmentId);
            for (int i = 0; i < _segments.Count; i++)
            {
                if (!_segments[i].HasActiveAnimation) continue;
                sb.Append(" · ").Append(_segments[i].Diagnostics());
                break;
            }
            return sb.ToString();
        }

        // Development-only preview commands. They drive the same segment state
        // machine used by production; no alternate fake animation exists.
        [ContextMenu("Pressure Preview/70% Warning")]
        private void Preview70() => StartStagePreview(PressureStage.Early);

        [ContextMenu("Pressure Preview/50% Loosening")]
        private void Preview50() => StartStagePreview(PressureStage.Mid);

        [ContextMenu("Pressure Preview/30% First Collapse")]
        private void Preview30() => StartStagePreview(PressureStage.Late);

        [ContextMenu("Pressure Preview/10% Critical Collapse")]
        private void Preview10() => StartStagePreview(PressureStage.Critical);

        [ContextMenu("Pressure Preview/0% Final Collapse")]
        private void Preview0() => StartStagePreview(PressureStage.Expired);

        [ContextMenu("Pressure Preview/Pause")]
        private void PausePreview()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _developerPreviewPaused = true;
#endif
        }

        [ContextMenu("Pressure Preview/Resume")]
        private void ResumePreview()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _developerPreviewPaused = false;
#endif
        }

        [ContextMenu("Pressure Preview/Reset")]
        private void ResetPreview() => ResetCorridor();

        [ContextMenu("Pressure Preview/Complete Two-Minute Preview")]
        private void CompleteTwoMinutePreview()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ResetCorridor();
            _developerPreviewRemaining = 120f;
            _developerTwoMinutePreview = true;
            _developerPreviewRunning = true;
            _developerPreviewPaused = false;
#endif
        }

        [ContextMenu("Pressure Preview/Scrub Current Stage")]
        private void ScrubCurrentStage()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PressureStage target = _stage;
            ResetCorridor();
            ApplyStage(target);
            float total = EstimatedDurationThroughStage(target);
            float elapsed = total * Mathf.Clamp01(developerScrubNormalized);
            const float step = 1f / 60f;
            int iterations = Mathf.CeilToInt(elapsed / step);
            for (int i = 0; i < iterations; i++) TickActive(step);
#endif
        }

        private void StartStagePreview(PressureStage stage)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ApplyStage(stage);
            _developerTwoMinutePreview = false;
            _developerPreviewRunning = true;
            _developerPreviewPaused = false;
#else
            ApplyStage(stage);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void Update()
        {
            if (!_developerPreviewRunning || _developerPreviewPaused || !_bound) return;
            float dt = Time.unscaledDeltaTime;
            if (_developerTwoMinutePreview)
            {
                _developerPreviewRemaining = Mathf.Max(0f,
                    _developerPreviewRemaining - dt);
                double fraction = _developerPreviewRemaining / 120.0;
                ApplyStage(PressureTimeline.StageFor(fraction));
                if (_developerPreviewRemaining <= 0f)
                    _developerTwoMinutePreview = false;
            }
            TickActive(dt);
        }
#endif

        private float EstimatedDurationThroughStage(PressureStage stage)
        {
            if (animationProfile == null) return 4f;
            var wave = animationProfile.farWave;
            float warning = wave.warningDuration + animationProfile.floorDelay;
            float loosen = wave.loosenDuration + animationProfile.floorDelay;
            float collapse = wave.detachDuration + wave.detachedHoldSeconds +
                             wave.fallDuration + wave.settleDuration +
                             wave.hideDelaySeconds + animationProfile.floorDelay +
                             animationProfile.pairedSegmentDelay;
            float result = stage >= PressureStage.Early ? warning : 0f;
            if (stage >= PressureStage.Mid) result += loosen;
            if (stage >= PressureStage.Late) result += collapse;
            if (stage >= PressureStage.Critical) result += collapse;
            if (stage >= PressureStage.Expired) result += collapse;
            return Mathf.Max(0.01f, result);
        }

        private static void CollectSegmentNodes(Transform root, List<string> discovered,
            Dictionary<string, Transform> nodeById)
        {
            foreach (Transform child in root)
            {
                string id = PuzzleCollapseSequence.ParseId(child.name);
                bool isSegmentRoot = id != null && IsKnownId(id) &&
                                     !child.name.Contains("_Floor") &&
                                     !child.name.Contains("_Ceiling") &&
                                     !child.name.Contains("_Wall") &&
                                     !child.name.Contains("Pivot") &&
                                     !child.name.Contains("Anchor");
                if (isSegmentRoot && !nodeById.ContainsKey(id))
                {
                    discovered.Add(id);
                    nodeById[id] = child;
                }
                CollectSegmentNodes(child, discovered, nodeById);
            }
        }

        private static Dictionary<SegmentPart, Transform> CollectParts(Transform segmentNode,
            string id)
        {
            var parts = new Dictionary<SegmentPart, Transform>(4);
            string prefix = "Segment_" + id + "_";
            AssignPart(segmentNode, prefix + "Floor", SegmentPart.Floor, parts);
            AssignPart(segmentNode, prefix + "Ceiling", SegmentPart.Ceiling, parts);
            AssignPart(segmentNode, prefix + "Wall_L", SegmentPart.WallLeft, parts);
            AssignPart(segmentNode, prefix + "Wall_R", SegmentPart.WallRight, parts);
            return parts;
        }

        private static void AssignPart(Transform segmentNode, string exactName,
            SegmentPart part, Dictionary<SegmentPart, Transform> parts)
        {
            Transform node = FindDeep(segmentNode, exactName);
            if (node != null) parts[part] = node;
            else Debug.LogWarning("[PuzzleCorridor] Missing part: " + exactName);
        }

        private static bool IsKnownId(string id)
        {
            for (int i = 0; i < PuzzleCollapseSequence.AllIds.Length; i++)
                if (string.Equals(PuzzleCollapseSequence.AllIds[i], id,
                    StringComparison.Ordinal)) return true;
            return false;
        }

        private static Transform FindDirectOrDeep(Transform root, string name)
        {
            if (root == null) return null;
            return FindDeep(root, name);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private static Bounds CalculateBounds(Renderer[] renderers, Vector3 fallback)
        {
            bool has = false;
            var bounds = new Bounds(fallback, Vector3.zero);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null) continue;
                if (!has)
                {
                    bounds = renderer.bounds;
                    has = true;
                }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (!has) bounds = new Bounds(fallback, new Vector3(2.8f, 5.6f, 1f));
            return bounds;
        }

        private void OnDestroy()
        {
            if (_voidMaterial == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) DestroyImmediate(_voidMaterial);
            else Destroy(_voidMaterial);
#else
            Destroy(_voidMaterial);
#endif
            _voidMaterial = null;
        }

        private static float ProjectedSize(Vector3 extents, Vector3 direction)
        {
            direction = new Vector3(Mathf.Abs(direction.x),
                Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            return Mathf.Max(0.01f, Vector3.Dot(extents, direction) * 2f);
        }
    }
}
