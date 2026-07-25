using System.Collections.Generic;
using StressTraining.Core;
using UnityEngine;

namespace StressTraining.Pressure
{
    /// <summary>
    /// Large inward-facing sky sphere around the corridor. Built once, always
    /// present, and almost entirely occluded by the corridor geometry — so it is
    /// only seen through the gaps that open as walls/ceiling fall. That gives a real
    /// backdrop behind a collapse instead of black. Uses CullFront + positive scale
    /// (the negative-scale winding flip does not work in this URP setup — see
    /// RuntimeVisualUtil.CullFront). Loaded from Resources; missing texture ⇒ a plain
    /// dusk tint so it never renders black.
    /// </summary>
    public sealed class CorridorSkyBackdrop : MonoBehaviour
    {
        public const string ResourcePath = "SafeSpace/CorridorSky";
        public const string ObjectName = "CorridorSkyBackdrop";

        public bool TextureApplied { get; private set; }

        public void Build(Transform corridorRoot, Vector3 corridorCenterLocal, float radius)
        {
            if (corridorRoot == null) return;
            var existing = corridorRoot.Find(ObjectName);
            if (existing != null) return;

            var texture = Resources.Load<Texture2D>(ResourcePath);
            Material mat = texture != null
                ? RuntimeVisualUtil.UnlitTextured(texture, Color.white)
                : RuntimeVisualUtil.Unlit(new Color(0.42f, 0.5f, 0.62f));
            RuntimeVisualUtil.CullFront(mat);
            TextureApplied = texture != null;

            var sphere = RuntimeVisualUtil.Primitive(PrimitiveType.Sphere, ObjectName,
                corridorRoot, corridorCenterLocal, Vector3.one * radius, mat);
            sphere.transform.SetAsFirstSibling();   // drawn first, always behind
        }
    }

    /// <summary>
    /// A breakable wall of panels behind the console, at the far −Z end the
    /// participant faces while working. Solid in Neutral (it closes the corridor);
    /// under ControlledPressure it cracks, then breaks apart panel-by-panel and
    /// finally clears — so the collapse is visible even while the participant stares
    /// at the tablet. Purely transform + renderer driven, deterministic, pause-aware
    /// (fed active time only) and fully resettable. No physics, no per-frame alloc.
    /// </summary>
    public sealed class ConsoleBackWall : MonoBehaviour
    {
        private sealed class Panel
        {
            public Transform Node;
            public Renderer Renderer;
            public Material Material;
            public Vector3 RestLocalPos;
            public Quaternion RestLocalRot;
            public int Order;          // break order (bottom-centre last)
            public float Progress;     // 0..1 fall progress
            public bool Hidden;
        }

        public const string ObjectName = "ConsoleBackWall";
        private const int Columns = 3;
        private const int Rows = 3;

        private readonly List<Panel> _panels = new List<Panel>(Columns * Rows);
        private PressureStage _stage = PressureStage.Stable;
        private float _shakePhase;
        private static readonly Color SolidColor = new Color(0.20f, 0.21f, 0.25f);
        private static readonly Color CrackColor = new Color(0.10f, 0.09f, 0.10f);

        public int PanelCount => _panels.Count;
        public int FallenPanelCount
        {
            get { int n = 0; for (int i = 0; i < _panels.Count; i++) if (_panels[i].Hidden) n++; return n; }
        }

        /// <summary>Builds the panel grid to fill the given interior rectangle.</summary>
        public void Build(Transform parent, Vector3 centerLocal, float width, float height,
            float thickness, Quaternion faceRotation)
        {
            if (parent == null || _panels.Count > 0) return;

            var root = new GameObject(ObjectName).transform;
            root.SetParent(parent, false);
            root.localPosition = centerLocal;
            root.localRotation = faceRotation;

            float panelW = width / Columns;
            float panelH = height / Rows;
            // Break the top corners first, the bottom-centre panel last — that panel
            // sits lowest in the player's view and reads as "the last thing standing".
            for (int r = 0; r < Rows; r++)
            {
                for (int c = 0; c < Columns; c++)
                {
                    float x = (c - (Columns - 1) * 0.5f) * panelW;
                    float y = (r - (Rows - 1) * 0.5f) * panelH;
                    var mat = RuntimeVisualUtil.Lit(SolidColor, 0.1f, 0.35f);
                    var go = RuntimeVisualUtil.Primitive(PrimitiveType.Cube, "BackPanel_" + r + "_" + c,
                        root, new Vector3(x, y, 0f),
                        new Vector3(panelW * 0.98f, panelH * 0.98f, thickness), mat);
                    int order = r * Columns + Mathf.Abs(c - 1);   // top + corners early, bottom-centre late
                    _panels.Add(new Panel
                    {
                        Node = go.transform,
                        Renderer = go.GetComponent<Renderer>(),
                        Material = mat,
                        RestLocalPos = go.transform.localPosition,
                        RestLocalRot = go.transform.localRotation,
                        Order = order
                    });
                }
            }
        }

        public void ApplyStage(PressureStage stage) => _stage = stage;

        public void TickActive(float dt)
        {
            if (dt <= 0f || _panels.Count == 0) return;
            _shakePhase += dt * 7f;

            foreach (var p in _panels)
            {
                if (p.Node == null) continue;

                // Break threshold per panel: earlier order breaks at an earlier stage.
                // order 0..1 → Late(30%); 2..3 → Critical(10%); 4+ → Expired(0%).
                PressureStage breakAt = p.Order <= 1 ? PressureStage.Late
                    : p.Order <= 3 ? PressureStage.Critical : PressureStage.Expired;
                bool breaking = _stage >= breakAt;
                bool cracking = _stage >= PressureStage.Mid && !breaking;

                if (breaking)
                {
                    p.Progress = Mathf.Min(1f, p.Progress + dt / 1.0f);
                    float s = p.Progress * p.Progress * (3f - 2f * p.Progress);
                    // Fall down and tip toward the player (+local -Z rotates forward).
                    p.Node.localPosition = p.RestLocalPos + new Vector3(0f, -2.4f * s, 0.15f * s);
                    p.Node.localRotation = p.RestLocalRot * Quaternion.Euler(-35f * s, 0f, 0f);
                    if (p.Progress >= 1f) Hide(p);
                }
                else if (cracking)
                {
                    SetColor(p, CrackColor);
                    float amp = 0.012f;
                    p.Node.localPosition = p.RestLocalPos + new Vector3(
                        Mathf.Sin(_shakePhase * 1.3f + p.Order) * amp, 0f, 0f);
                }
                else
                {
                    Restore(p);
                }
            }
        }

        private void Hide(Panel p)
        {
            if (p.Hidden) return;
            p.Hidden = true;
            if (p.Renderer != null) p.Renderer.enabled = false;
        }

        private void SetColor(Panel p, Color c)
        {
            if (p.Material == null) return;
            if (p.Material.HasProperty("_BaseColor")) p.Material.SetColor("_BaseColor", c);
            if (p.Material.HasProperty("_Color")) p.Material.SetColor("_Color", c);
        }

        private void Restore(Panel p)
        {
            p.Progress = 0f;
            if (p.Hidden) { p.Hidden = false; if (p.Renderer != null) p.Renderer.enabled = true; }
            if (p.Node != null)
            {
                p.Node.localPosition = p.RestLocalPos;
                p.Node.localRotation = p.RestLocalRot;
            }
            SetColor(p, SolidColor);
        }

        public void ResetWall()
        {
            _stage = PressureStage.Stable;
            _shakePhase = 0f;
            foreach (var p in _panels) Restore(p);
        }
    }

    /// <summary>
    /// The final loss beat: when the collapse finale reaches Expired, the
    /// participant's own station — the Safe platform they stand on, the console and
    /// the robot arm — drops away too, so a loss reads as "everything goes, including
    /// where you sit", not just the player's view. Each target is driven straight
    /// world-down (expressed in its own parent space) with a small tip, staggered so
    /// the corridor is seen to go first. It only drives objects the puzzle segment
    /// controller does NOT own — the Safe floor is non-movable there, the console and
    /// arm are independent scene objects — so nothing fights it. Deterministic,
    /// pause-aware (fed active time only), no physics, no per-frame allocation, fully
    /// resettable.
    /// </summary>
    public sealed class StationCollapse : MonoBehaviour
    {
        private sealed class Piece
        {
            public Transform Node;
            public Vector3 RestLocalPos;
            public Quaternion RestLocalRot;
            public Vector3 LocalDown;    // world-down expressed in the parent's space
            public float Delay;
            public float FallDistance;
            public float TiltDegrees;
        }

        public const string ObjectName = "StationCollapse";
        private const float FallSeconds = 1.3f;

        private readonly List<Piece> _pieces = new List<Piece>(3);
        private PressureStage _stage = PressureStage.Stable;
        private float _elapsed;

        public int PieceCount => _pieces.Count;
        public bool IsCollapsing => _stage >= PressureStage.Expired && _elapsed > 0f;

        /// <summary>Registers a target and caches its rest pose. No-op if null.</summary>
        public void AddTarget(Transform node, float delay, float fallDistance, float tiltDegrees)
        {
            if (node == null) return;
            Transform parent = node.parent;
            Vector3 localDown = parent != null
                ? parent.InverseTransformDirection(Vector3.down)
                : Vector3.down;
            _pieces.Add(new Piece
            {
                Node = node,
                RestLocalPos = node.localPosition,
                RestLocalRot = node.localRotation,
                LocalDown = localDown.sqrMagnitude > 1e-6f ? localDown.normalized : Vector3.down,
                Delay = Mathf.Max(0f, delay),
                FallDistance = Mathf.Max(0f, fallDistance),
                TiltDegrees = tiltDegrees
            });
        }

        public void ApplyStage(PressureStage stage)
        {
            if (stage >= PressureStage.Expired && _stage < PressureStage.Expired)
                _elapsed = 0f;   // begin the drop when the finale reaches Expired
            _stage = stage;
        }

        public void TickActive(float dt)
        {
            if (_stage < PressureStage.Expired || dt <= 0f || _pieces.Count == 0) return;
            _elapsed += dt;
            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                if (p.Node == null) continue;
                float t = Mathf.Max(0f, _elapsed - p.Delay);
                float s = FallSeconds <= 0f ? 1f : Mathf.Clamp01(t / FallSeconds);
                s = s * s;   // ease-in: gives way slowly, then plummets (gravity-like)
                p.Node.localPosition = p.RestLocalPos + p.LocalDown * (p.FallDistance * s);
                p.Node.localRotation = p.TiltDegrees != 0f
                    ? p.RestLocalRot * Quaternion.Euler(p.TiltDegrees * s, 0f, 0f)
                    : p.RestLocalRot;
            }
        }

        public void ResetStation()
        {
            _stage = PressureStage.Stable;
            _elapsed = 0f;
            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                if (p.Node == null) continue;
                p.Node.localPosition = p.RestLocalPos;
                p.Node.localRotation = p.RestLocalRot;
            }
        }
    }
}
