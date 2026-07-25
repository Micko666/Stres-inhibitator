using System;
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using StressTraining.Core;
using StressTraining.Data;
using UnityEngine;

namespace StressTraining.Console
{
    [Serializable]
    public sealed class ConsoleLayoutConfig
    {
        [Header("Anchor relative to Console_BlenderPrototype")]
        public Vector3 anchorLocalPosition = new Vector3(0f, 0.985f, -0.023f);
        public Vector3 anchorLocalEuler = Vector3.zero;

        [Header("Standard five-control row")]
        public Vector3 primaryRowCenter = new Vector3(0f, 0f, 0.10f);
        [Min(0.11f)] public float primaryButtonSpacing = 0.15f;
        // Moved outward (±0.34) so the central Corsi zone never overlaps them.
        public Vector3 statusIndicator = new Vector3(-0.34f, 0.005f, -0.10f); // legacy, not built

        [Header("Fixed Corsi zone (nine unlabeled buttons, spec §19.1)")]
        public Vector3 corsiZoneCenter = new Vector3(0f, 0f, -0.11f);
        [Min(0.15f)] public float corsiZoneHalfWidth = 0.28f;
        [Min(0.05f)] public float corsiZoneHalfDepth = 0.10f;

        [Header("Legacy serialized modular fields (not built in production)")]
        public Vector3 modularLeftCenter = new Vector3(0.40f, 0f, 0.0f);   // player's LEFT = +X
        public Vector3 modularRightCenter = new Vector3(-0.40f, 0f, 0.0f); // player's RIGHT = -X
        [Min(0.05f)] public float modularSlotSpacingZ = 0.11f;

        [Header("Legacy serialized controls (never built by production path)")]
        public bool showLegacyDemoControls = false;
        public Vector3 match = new Vector3(-0.28f, 0f, 0.11f);
        public Vector3 noMatch = new Vector3(-0.14f, 0f, 0.11f);
        public Vector3 confirm = new Vector3(0f, 0f, 0.11f);
        public Vector3 left = new Vector3(0.14f, 0f, 0.11f);
        public Vector3 right = new Vector3(0.28f, 0f, 0.11f);
        public Vector3 toggle = new Vector3(-0.27f, 0f, -0.015f);
        public Vector3 lever = new Vector3(-0.13f, 0f, -0.015f);
        public Vector3 centralButton = new Vector3(0f, 0f, -0.005f);
        public Vector3 knob = new Vector3(0.15f, 0f, -0.015f);
        public Vector3 distractor1 = new Vector3(-0.24f, 0f, -0.13f);
        public Vector3 distractor2 = new Vector3(-0.08f, 0f, -0.13f);
        public Vector3 distractor3 = new Vector3(0.08f, 0f, -0.13f);
        public Vector3 distractor4 = new Vector3(0.24f, 0f, -0.13f);
        public Vector3 indicator2 = new Vector3(0f, 0.005f, -0.21f);
        public Vector3 indicator3 = new Vector3(0.10f, 0.005f, -0.21f);
    }

    /// <summary>
    /// Builds the locked local-space production console on the active shell:
    /// five task controls, PAUSE and nine fixed Corsi buttons only.
    /// </summary>
    public static class ConsoleLayoutBuilder
    {
        public const string AnchorName = "ConsoleControlsAnchor";
        public const string RootName = "ConsoleControlsRoot";
        public const string LeftId = "btn_left";
        public const string MatchId = "btn_match";
        public const string GoId = "btn_go";
        public const string NoMatchId = "btn_nomatch";
        public const string RightId = "btn_right";
        public const string StatusIndicatorId = "ind_status";

        public static string MetaHapticsStatus => ConsoleControlBase.MetaHapticsStatus;

        /// <summary>
        /// Converts a slot index expressed in USER-perspective order (negative =
        /// closer to the user's left hand) into a local-X offset. The player
        /// faces -Z, so the player's left is local +X: userSlot -2 → +2·spacing.
        /// Unit-tested so a future console/mesh change cannot silently mirror
        /// the physical LEFT/RIGHT buttons again.
        /// </summary>
        public static float UserPerspectiveX(int userSlotIndex, float spacing) =>
            -userSlotIndex * spacing;

        public const string CorsiZoneAnchorName = "CorsiControlsAnchor";
        public const string ModularLeftAnchorName = "ModularLeftAnchor";
        public const string ModularRightAnchorName = "ModularRightAnchor";

        public static List<ConsoleBindingDefinition> DefaultBindings()
        {
            var bindings = new List<ConsoleBindingDefinition>
            {
                new ConsoleBindingDefinition(LeftId, SemanticAction.Left),
                new ConsoleBindingDefinition(MatchId, SemanticAction.Match),
                new ConsoleBindingDefinition(GoId, SemanticAction.Go),
                new ConsoleBindingDefinition(NoMatchId, SemanticAction.NoMatch),
                new ConsoleBindingDefinition(RightId, SemanticAction.Right)
            };
            for (int i = 0; i < Tasks.CorsiLayout.PositionCount; i++)
                bindings.Add(new ConsoleBindingDefinition(
                    Tasks.CorsiLayout.ControlId(i), Tasks.CorsiLayout.ActionFor(i)));
            return bindings;
        }

        public static Transform EnsureAnchor(Transform consoleShell, ConsoleLayoutConfig config)
        {
            if (consoleShell == null) throw new ArgumentNullException(nameof(consoleShell));
            config ??= new ConsoleLayoutConfig();
            Transform anchor = consoleShell.Find(AnchorName);
            if (anchor == null)
            {
                anchor = new GameObject(AnchorName).transform;
                anchor.SetParent(consoleShell, false);
            }

            anchor.localPosition = config.anchorLocalPosition;
            anchor.localRotation = Quaternion.Euler(config.anchorLocalEuler);
            anchor.localScale = Vector3.one;
            if (anchor.GetComponent<ConsoleControlsAnchorGizmo>() == null)
                anchor.gameObject.AddComponent<ConsoleControlsAnchorGizmo>();
            return anchor;
        }

        public static Transform Build(Transform consoleShell, ConsoleLayoutConfig config)
        {
            config ??= new ConsoleLayoutConfig();
            Transform anchor = EnsureAnchor(consoleShell, config);
            Transform root = anchor.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName).transform;
                root.SetParent(anchor, false);
            }
            RebuildProductionContents(root, config);
            return root;
        }

        /// <summary>
        /// Rebuilds only the locked production controls. Keeping the root object
        /// stable makes repeated installer/session calls idempotent while every
        /// obsolete lever, knob, toggle, indicator or modular anchor is removed.
        /// </summary>
        private static void RebuildProductionContents(Transform root, ConsoleLayoutConfig config)
        {
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;

            for (int i = root.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);

            Vector3 row = config.primaryRowCenter;
            float spacing = Mathf.Max(0.11f, config.primaryButtonSpacing);
            Button(root, LeftId, row + Vector3.right * UserPerspectiveX(-2, spacing),
                new Color(0.16f, 0.43f, 0.92f), "LEFT", false, true, true);
            Button(root, MatchId, row + Vector3.right * UserPerspectiveX(-1, spacing),
                new Color(0.12f, 0.72f, 0.28f), "MATCH", false, true, true);
            Button(root, GoId, row,
                new Color(0.96f, 0.62f, 0.08f), "GO", false, true, true);
            Button(root, NoMatchId, row + Vector3.right * UserPerspectiveX(1, spacing),
                new Color(0.82f, 0.18f, 0.16f), "NO MATCH", false, true, true);
            Button(root, RightId, row + Vector3.right * UserPerspectiveX(2, spacing),
                new Color(0.16f, 0.43f, 0.92f), "RIGHT", false, true, true);
            BuildCorsiZone(root, config);

            int ignoreRaycast = LayerMask.NameToLayer("Ignore Raycast");
            if (ignoreRaycast >= 0) SetLayerRecursively(root.gameObject, ignoreRaycast);
        }

        /// <summary>
        /// Makes Build idempotent when a generated root is serialized or survives
        /// a reload. Positions, direct-poke subscriptions and ray exclusion are
        /// reapplied instead of silently accepting an outdated runtime layout.
        /// </summary>
        private static void RevalidateExistingStandardLayout(Transform root,
            ConsoleLayoutConfig config) => RebuildProductionContents(root, config);

        /// <summary>Public session/installer cleanup hook; seed is intentionally ignored.</summary>
        public static void RevalidateProductionLayout(Transform root, ConsoleLayoutConfig config)
        {
            if (root == null) return;
            RebuildProductionContents(root, config ?? new ConsoleLayoutConfig());
        }

        private static void RevalidateButton(Transform root, string id, Vector3 localPosition,
            bool large)
        {
            Transform button = root.Find(id);
            if (button == null) return;
            button.localPosition = localPosition;
            var control = button.GetComponent<ConsoleButtonControl>();
            if (control == null) return;

            PokeInteractable poke = button.GetComponentInChildren<PokeInteractable>(true);
            if (poke == null)
            {
                float scale = large ? 1.25f : 1f;
                poke = AddCircularPokeSurface(button, 0.030f * scale, 0.038f * scale);
            }
            control.ConfigurePoke(poke);
        }

        // ── fixed Corsi zone (spec §19.1) ─────────────────────────────────
        // Nine IDENTICAL, UNLABELED buttons in the fixed Corsi 7×3 mapping. Positions are local under CorsiControlsAnchor and never
        // change between sessions (neutral/pressure congruence). The buttons
        // only accept input while the Corsi runtime opens its response phase.
        private static void BuildCorsiZone(Transform root, ConsoleLayoutConfig config)
        {
            var anchor = new GameObject(CorsiZoneAnchorName).transform;
            anchor.SetParent(root, false);
            anchor.localPosition = config.corsiZoneCenter;
            anchor.localRotation = Quaternion.identity;

            var corsiColor = new Color(0.32f, 0.36f, 0.46f); // one shared neutral tone
            for (int i = 0; i < Tasks.CorsiLayout.PositionCount; i++)
            {
                Vector3 local = Tasks.CorsiLayout.GetConsoleLocalPosition(
                    i, config.corsiZoneHalfWidth, config.corsiZoneHalfDepth);
                Button(anchor, Tasks.CorsiLayout.ControlId(i), local,
                    corsiColor, string.Empty, false, false, true);
            }
        }

        // ── removed modular side zones ───────────────────────────────────
        // Compatibility API remains, but Phase 8.5 never instantiates these
        // controls and the layout seed no longer changes console geometry.
        public const string ModularLayoutVersion = "removed-phase8.5";

        /// <summary>
        /// Compatibility entry point retained for ProductionSessionFlow. PHASE
        /// 8.5 removes modular/decorative controls; the seed no longer changes
        /// console geometry and this call only revalidates the production layout.
        /// </summary>
        public static void BuildModularZones(Transform controlsRoot, ConsoleLayoutConfig config,
            int consoleLayoutSeed)
        {
            _ = consoleLayoutSeed;
            RevalidateProductionLayout(controlsRoot, config);
        }

        private static void RemoveExisting(Transform root, string name)
        {
            Transform existing = root == null ? null : root.Find(name);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        private static void BuildLegacyDemonstrationControls(Transform root, ConsoleLayoutConfig config)
        {
            Button(root, "btn_confirm", config.confirm,
                new Color(0.20f, 0.48f, 0.92f), "OK", true, false, false);
            Toggle(root, "tgl_demo", config.toggle);
            Lever(root, "lvr_demo", config.lever);
            Button(root, "btn_center", config.centralButton,
                new Color(0.95f, 0.62f, 0.08f), "DEMO", true, true, false);
            Knob(root, "knb_demo", config.knob);
            Button(root, "btn_dst1", config.distractor1,
                new Color(0.24f, 0.24f, 0.27f), string.Empty, true, false, false);
            Button(root, "btn_dst2", config.distractor2,
                new Color(0.24f, 0.24f, 0.27f), string.Empty, true, false, false);
            Button(root, "btn_dst3", config.distractor3,
                new Color(0.24f, 0.24f, 0.27f), string.Empty, true, false, false);
            Button(root, "btn_dst4", config.distractor4,
                new Color(0.24f, 0.24f, 0.27f), string.Empty, true, false, false);
            Indicator(root, "ind_legacy_1", config.indicator2, new Color(0.2f, 0.65f, 1f));
            Indicator(root, "ind_legacy_2", config.indicator3, new Color(1f, 0.72f, 0.18f));
        }

        private static GameObject ControlRoot(Transform parent, string id, Vector3 position,
            float height, float width = 0.10f)
        {
            var go = new GameObject(id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.identity;
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0, height * 0.5f, 0);
            collider.size = new Vector3(width, height, width);
            return go;
        }

        private static ConsoleButtonControl Button(Transform parent, string id, Vector3 position,
            Color color, string label, bool distractor, bool large, bool addMetaPoke)
        {
            float scale = large ? 1.25f : 1f;
            var go = ControlRoot(parent, id, position, 0.070f * scale, 0.09f * scale);
            RuntimeVisualUtil.Primitive(PrimitiveType.Cylinder, "Base", go.transform,
                new Vector3(0, 0.004f, 0), new Vector3(0.060f, 0.006f, 0.060f) * scale,
                RuntimeVisualUtil.Lit(new Color(0.07f, 0.08f, 0.10f), 0.1f, 0.55f));
            GameObject cap = RuntimeVisualUtil.Primitive(PrimitiveType.Cylinder, "MovingCap", go.transform,
                new Vector3(0, 0.020f * scale, 0), new Vector3(0.044f, 0.010f, 0.044f) * scale,
                RuntimeVisualUtil.Lit(color, 0.05f, 0.45f));
            if (!string.IsNullOrEmpty(label))
            {
                float characterSize = label.Length >= 8 ? 0.0048f : label.Length >= 5 ? 0.0058f : 0.0085f;
                TextMesh text = RuntimeVisualUtil.Label(label, go.transform,
                    new Vector3(0, 0.036f * scale, 0), characterSize, Color.white);
                text.transform.localRotation = Quaternion.Euler(90f, 180f, 0f);
            }

            var control = go.AddComponent<ConsoleButtonControl>();
            control.Configure(id, distractor, cap.GetComponent<Renderer>());
            control.ConfigureCap(cap.transform);
            if (addMetaPoke)
            {
                float surfaceHeight = 0.030f * scale;
                PokeInteractable poke = AddCircularPokeSurface(go.transform, surfaceHeight, 0.038f * scale);
                control.ConfigurePoke(poke);
            }
            return control;
        }

        private static PokeInteractable AddCircularPokeSurface(Transform parent,
            float localHeight, float radius)
        {
            var surfaceObject = new GameObject("MetaPokeSurface");
            surfaceObject.transform.SetParent(parent, false);
            surfaceObject.transform.localPosition = new Vector3(0, localHeight, 0);
            // PlaneSurface uses local +Z for Forward. Rotate +Z onto console +Y.
            surfaceObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            surfaceObject.transform.localScale = Vector3.one;

            var plane = surfaceObject.AddComponent<PlaneSurface>();
            plane.InjectAllPlaneSurface(PlaneSurface.NormalFacing.Forward, false);
            var circle = surfaceObject.AddComponent<CircleSurface>();
            circle.InjectAllCircleSurface(plane);
            circle.InjectOptionalRadius(radius);
            var poke = surfaceObject.AddComponent<PokeInteractable>();
            poke.InjectAllPokeInteractable(circle);
            return poke;
        }

        private static void Toggle(Transform parent, string id, Vector3 position)
        {
            GameObject go = ControlRoot(parent, id, position, 0.075f);
            RuntimeVisualUtil.Primitive(PrimitiveType.Cube, "Base", go.transform,
                new Vector3(0, 0.006f, 0), new Vector3(0.065f, 0.012f, 0.065f),
                RuntimeVisualUtil.Lit(new Color(0.07f, 0.08f, 0.10f)));
            GameObject head = RuntimeVisualUtil.Primitive(PrimitiveType.Cube, "MovingHead", go.transform,
                new Vector3(0, 0.030f, 0), new Vector3(0.018f, 0.045f, 0.025f),
                RuntimeVisualUtil.Lit(new Color(0.72f, 0.76f, 0.82f)));
            var control = go.AddComponent<ConsoleToggleControl>();
            control.Configure(id, true, head.GetComponent<Renderer>());
            control.ConfigureHead(head.transform);
        }

        private static void Lever(Transform parent, string id, Vector3 position)
        {
            GameObject go = ControlRoot(parent, id, position, 0.11f);
            RuntimeVisualUtil.Primitive(PrimitiveType.Cube, "Base", go.transform,
                new Vector3(0, 0.008f, 0), new Vector3(0.07f, 0.016f, 0.075f),
                RuntimeVisualUtil.Lit(new Color(0.07f, 0.08f, 0.10f)));
            var pivot = new GameObject("MovingArmPivot").transform;
            pivot.SetParent(go.transform, false);
            pivot.localPosition = new Vector3(0, 0.016f, 0);
            GameObject arm = RuntimeVisualUtil.Primitive(PrimitiveType.Cube, "Arm", pivot,
                new Vector3(0, 0.045f, 0), new Vector3(0.014f, 0.09f, 0.014f),
                RuntimeVisualUtil.Lit(new Color(0.88f, 0.24f, 0.16f)));
            var control = go.AddComponent<ConsoleLeverControl>();
            control.Configure(id, true, arm.GetComponent<Renderer>());
            control.ConfigureArm(pivot);
        }

        private static void Knob(Transform parent, string id, Vector3 position)
        {
            GameObject go = ControlRoot(parent, id, position, 0.07f);
            GameObject dial = RuntimeVisualUtil.Primitive(PrimitiveType.Cylinder, "MovingDial", go.transform,
                new Vector3(0, 0.018f, 0), new Vector3(0.052f, 0.018f, 0.052f),
                RuntimeVisualUtil.Lit(new Color(0.28f, 0.30f, 0.36f), 0.2f, 0.65f));
            RuntimeVisualUtil.Primitive(PrimitiveType.Cube, "Mark", dial.transform,
                new Vector3(0, 0.65f, 0.35f), new Vector3(0.09f, 0.45f, 0.16f),
                RuntimeVisualUtil.Lit(new Color(0.98f, 0.86f, 0.18f)));
            var control = go.AddComponent<ConsoleKnobControl>();
            control.Configure(id, true, dial.GetComponent<Renderer>());
            control.ConfigureDial(dial.transform);
        }

        private static void Indicator(Transform parent, string id, Vector3 position, Color color)
        {
            GameObject go = ControlRoot(parent, id, position, 0.025f, 0.04f);
            GameObject bulb = RuntimeVisualUtil.Primitive(PrimitiveType.Sphere, "SignalLamp", go.transform,
                new Vector3(0, 0.014f, 0), Vector3.one * 0.026f,
                RuntimeVisualUtil.Emissive(new Color(0.10f, 0.10f, 0.12f), color, 0.2f));
            var control = go.AddComponent<ConsoleIndicatorLight>();
            control.Configure(id, false, bulb.GetComponent<Renderer>());
            control.SetColor(color);
            control.SetOn(true);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            root.layer = layer;
            foreach (Transform child in root.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }

    public sealed class ConsoleControlsAnchorGizmo : MonoBehaviour
    {
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.85f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.80f, 0.025f, 0.42f));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.18f);
        }
#endif
    }
}
