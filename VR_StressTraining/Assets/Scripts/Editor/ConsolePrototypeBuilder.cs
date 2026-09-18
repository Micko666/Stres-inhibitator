using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor shape-preview tool — Adaptive Stress Corridor console silhouette.
///
/// Menu: VR StressTraining → Console Prototype
///   Build Clean Shape Preview  → creates Console_Prototype_Preview at PreviewPosition
///   Clear Console Preview      → removes it
///
/// Workflow: change a constant → Build → evaluate silhouette → iterate.
/// No colliders, no gameplay logic, never touches MainScene / XR rig / Project Settings.
///
/// Shape: single connected crescent/boomerang.
///   _LeftWing_Pivot  / _RightWing_Pivot  — empty pivots, each rotated ±WingAngle Y.
///   Body + Top pieces are children of the correct pivot so they rotate as one unit.
///   Panel zones and rims sit on top of the unified surface.
/// </summary>
public static class ConsolePrototypeBuilder
{
    // ──────────────────────────────────────────────────────────────────────────
    // SHAPE CONSTANTS  ← adjust these, click Build, see result.
    // ──────────────────────────────────────────────────────────────────────────

    /// Total crescent tip-to-tip apparent width (m). Drives WingLength.
    private const float ConsoleWidth       = 5.80f;

    /// Width of the straight central body (m).
    private const float CenterWidth        = 2.20f;

    /// Front-to-back depth of every body section (m).
    private const float ConsoleDepth       = 0.95f;

    /// Height of the lower cabinet body (m).
    private const float ConsoleHeight      = 0.88f;

    /// Degrees each wing sweeps toward the player. Higher = more curved crescent.
    private const float WingAngle          = 20f;

    /// Thickness of the angled top slab (m).
    private const float TopThickness       = 0.12f;

    /// Tilt of the top surface toward the operator (negative = forward lean, degrees).
    private const float SurfaceAngle       = -10f;

    /// Width of the operator opening / inner cutout — reference constant, not a mesh.
    private const float InnerCutoutWidth   = 1.90f;

    /// Depth of the operator opening — reference constant, not a mesh.
    private const float InnerCutoutDepth   = 0.55f;

    /// Height and depth of the inner-rim strip (m).
    private const float RimThickness       = 0.10f;

    /// World-space placement of the preview root. Z=20 keeps it clear of the corridor.
    private static readonly Vector3 PreviewPosition = new Vector3(0f, 0f, 20f);

    // ── Derived (do not change) ───────────────────────────────────────────────
    private const float WingLength         = (ConsoleWidth - CenterWidth) * 0.5f;
    private const float PedestalHeight     = 0.12f;
    private const float EndCapThickness    = 0.16f;
    private const float PanelInset         = 0.016f;
    private const float PanelThickness     = 0.022f;

    private const string RootName = "Console_Prototype_Preview";
    private const string MatName  = "MAT_Console_Prototype_Grey";
    private const string MatPath  = "Assets/Materials/" + MatName + ".mat";

    // ──────────────────────────────────────────────────────────────────────────
    // MENU
    // ──────────────────────────────────────────────────────────────────────────

    [MenuItem("VR StressTraining/Console Prototype/Build Clean Shape Preview")]
    public static void BuildPreview()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        Material mat = GetOrCreateMaterial();

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Console Shape Preview");
        root.transform.position = PreviewPosition;

        // Wing pivots created first — body, tops, panels all parented under them.
        Transform leftPivot  = MakePivot(root.transform, "_LeftWing_Pivot",
            new Vector3(-CenterWidth * 0.5f, 0f, 0f),
            Quaternion.Euler(0f, WingAngle, 0f));      // +angle → tip sweeps toward player

        Transform rightPivot = MakePivot(root.transform, "_RightWing_Pivot",
            new Vector3( CenterWidth * 0.5f, 0f, 0f),
            Quaternion.Euler(0f, -WingAngle, 0f));     // mirror

        BuildBodies(root.transform, leftPivot, rightPivot, mat);
        BuildTops(root.transform, leftPivot, rightPivot, mat);
        BuildRims(root.transform, leftPivot, rightPivot, mat);
        BuildPanels(root.transform, leftPivot, rightPivot, mat);
        BuildWallMounts(root.transform, mat);

        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("[ConsolePrototypeBuilder] Preview built. Change constants → Build to iterate.");
    }

    [MenuItem("VR StressTraining/Console Prototype/Clear Console Preview")]
    public static void ClearPreview()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing);
            Debug.Log("[ConsolePrototypeBuilder] Preview cleared.");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // BODIES
    // ──────────────────────────────────────────────────────────────────────────

    private static void BuildBodies(
        Transform root, Transform lp, Transform rp, Material mat)
    {
        float midY = PedestalHeight + ConsoleHeight * 0.5f;

        // Wide pedestal plate that visually unifies the entire crescent base.
        float pedestalSpan = CenterWidth + WingLength * 1.55f;
        Prim(root, "Pedestal",
            new Vector3(0f, PedestalHeight * 0.5f, 0f),
            new Vector3(pedestalSpan, PedestalHeight, ConsoleDepth + 0.20f),
            Vector3.zero, mat);

        // ── Center ───────────────────────────────────────────────────────────
        Prim(root, "Body_Center",
            new Vector3(0f, midY, 0f),
            new Vector3(CenterWidth, ConsoleHeight, ConsoleDepth),
            Vector3.zero, mat);

        // ── Left wing  (child of lp, so it inherits WingAngle rotation) ──────
        // Overlap the wing slightly into the center (+0.08) to hide the junction gap.
        Prim(lp, "Body_LeftWing",
            new Vector3(-(WingLength * 0.5f - 0.08f), midY, 0f),
            new Vector3(WingLength + 0.16f, ConsoleHeight, ConsoleDepth),
            Vector3.zero, mat);

        Prim(lp, "Body_LeftEndCap",
            new Vector3(-WingLength - EndCapThickness * 0.5f, midY, 0f),
            new Vector3(EndCapThickness, ConsoleHeight + 0.06f, ConsoleDepth + 0.14f),
            Vector3.zero, mat);

        // ── Right wing (mirror) ───────────────────────────────────────────────
        Prim(rp, "Body_RightWing",
            new Vector3( WingLength * 0.5f - 0.08f, midY, 0f),
            new Vector3(WingLength + 0.16f, ConsoleHeight, ConsoleDepth),
            Vector3.zero, mat);

        Prim(rp, "Body_RightEndCap",
            new Vector3( WingLength + EndCapThickness * 0.5f, midY, 0f),
            new Vector3(EndCapThickness, ConsoleHeight + 0.06f, ConsoleDepth + 0.14f),
            Vector3.zero, mat);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TOPS  (same pivot parents as bodies → surface follows wing rotation)
    // ──────────────────────────────────────────────────────────────────────────

    private static void BuildTops(
        Transform root, Transform lp, Transform rp, Material mat)
    {
        float topY  = PedestalHeight + ConsoleHeight + TopThickness * 0.5f;
        float zNudge = -0.04f;   // slight forward shift so angled slab overhangs inner edge

        Prim(root, "Top_Center",
            new Vector3(0f, topY, zNudge),
            new Vector3(CenterWidth + 0.12f, TopThickness, ConsoleDepth + 0.18f),
            new Vector3(SurfaceAngle, 0f, 0f), mat);

        Prim(lp, "Top_LeftWing",
            new Vector3(-(WingLength * 0.5f - 0.08f), topY, zNudge),
            new Vector3(WingLength + 0.18f, TopThickness, ConsoleDepth + 0.18f),
            new Vector3(SurfaceAngle, 0f, 0f), mat);

        Prim(rp, "Top_RightWing",
            new Vector3( WingLength * 0.5f - 0.08f, topY, zNudge),
            new Vector3(WingLength + 0.18f, TopThickness, ConsoleDepth + 0.18f),
            new Vector3(SurfaceAngle, 0f, 0f), mat);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // RIMS
    // ──────────────────────────────────────────────────────────────────────────

    private static void BuildRims(
        Transform root, Transform lp, Transform rp, Material mat)
    {
        float rimY     = PedestalHeight + ConsoleHeight + RimThickness * 0.5f;
        float innerZ   = -(ConsoleDepth * 0.5f + 0.01f);  // front face of inner edge
        float outerZ   =  (ConsoleDepth * 0.5f + 0.02f);  // back face

        // Inner_Rim: three-piece strip that follows each section's inner face.
        // Center piece sits directly under root; wing pieces follow pivot rotation.
        Prim(root, "Inner_Rim",
            new Vector3(0f, rimY, innerZ),
            new Vector3(CenterWidth + 0.08f, RimThickness, 0.09f),
            Vector3.zero, mat);

        Prim(lp, "Inner_Rim_LeftWing",
            new Vector3(-(WingLength * 0.5f - 0.08f), rimY, innerZ),
            new Vector3(WingLength + 0.12f, RimThickness, 0.09f),
            Vector3.zero, mat);

        Prim(rp, "Inner_Rim_RightWing",
            new Vector3( WingLength * 0.5f - 0.08f, rimY, innerZ),
            new Vector3(WingLength + 0.12f, RimThickness, 0.09f),
            Vector3.zero, mat);

        // Outer_Back_Rim: single thick bar at the back of the center section.
        Prim(root, "Outer_Back_Rim",
            new Vector3(0f, rimY, outerZ),
            new Vector3(CenterWidth + 0.14f, RimThickness, 0.12f),
            Vector3.zero, mat);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PANEL ZONES  (thin inset slabs — placeholder areas for future controls)
    // ──────────────────────────────────────────────────────────────────────────

    private static void BuildPanels(
        Transform root, Transform lp, Transform rp, Material mat)
    {
        // Panel sits slightly below the top surface level.
        float panelY = PedestalHeight + ConsoleHeight + TopThickness - PanelInset;
        float panelW = WingLength * 0.58f;
        float panelD = ConsoleDepth * 0.62f;

        // Center panel
        Prim(root, "Panel_Center",
            new Vector3(0f, panelY, 0f),
            new Vector3(CenterWidth * 0.84f, PanelThickness, ConsoleDepth * 0.65f),
            new Vector3(SurfaceAngle, 0f, 0f), mat);

        // Left panels (pivot space → rotate with wing)
        float lInner = -(WingLength * 0.30f);
        float lOuter = -(WingLength * 0.78f);

        Prim(lp, "Panel_Left",
            new Vector3(lInner, panelY, 0f),
            new Vector3(panelW, PanelThickness, panelD),
            new Vector3(SurfaceAngle, 0f, 0f), mat);

        Prim(lp, "Panel_LeftOuter",
            new Vector3(lOuter, panelY, 0f),
            new Vector3(panelW, PanelThickness, panelD),
            new Vector3(SurfaceAngle, 0f, 0f), mat);

        // Right panels (mirror)
        Prim(rp, "Panel_Right",
            new Vector3(-lInner, panelY, 0f),
            new Vector3(panelW, PanelThickness, panelD),
            new Vector3(SurfaceAngle, 0f, 0f), mat);

        Prim(rp, "Panel_RightOuter",
            new Vector3(-lOuter, panelY, 0f),
            new Vector3(panelW, PanelThickness, panelD),
            new Vector3(SurfaceAngle, 0f, 0f), mat);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // WALL MOUNTS  (behind console, not attached to it)
    // ──────────────────────────────────────────────────────────────────────────

    private static void BuildWallMounts(Transform root, Material mat)
    {
        float mountZ    = ConsoleDepth * 0.5f + 0.70f;   // clearly behind the console
        float mountMidY = PedestalHeight + ConsoleHeight * 0.38f;
        float mountX    = CenterWidth * 0.5f + 0.55f;

        // Vertical pillar
        Prim(root, "ManipulatorWallMount_Left",
            new Vector3(-mountX, mountMidY, mountZ),
            new Vector3(0.26f, ConsoleHeight * 0.75f, 0.26f),
            Vector3.zero, mat);

        // Cap plate on top of pillar
        Prim(root, "ManipulatorWallMount_Left_Cap",
            new Vector3(-mountX, PedestalHeight + ConsoleHeight * 0.75f + 0.03f, mountZ),
            new Vector3(0.42f, 0.06f, 0.42f),
            Vector3.zero, mat);

        Prim(root, "ManipulatorWallMount_Right",
            new Vector3( mountX, mountMidY, mountZ),
            new Vector3(0.26f, ConsoleHeight * 0.75f, 0.26f),
            Vector3.zero, mat);

        Prim(root, "ManipulatorWallMount_Right_Cap",
            new Vector3( mountX, PedestalHeight + ConsoleHeight * 0.75f + 0.03f, mountZ),
            new Vector3(0.42f, 0.06f, 0.42f),
            Vector3.zero, mat);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // HELPERS
    // ──────────────────────────────────────────────────────────────────────────

    private static Transform MakePivot(
        Transform parent, string name, Vector3 localPos, Quaternion localRot)
    {
        GameObject pivot = new GameObject(name);
        pivot.transform.SetParent(parent);
        pivot.transform.localPosition = localPos;
        pivot.transform.localRotation = localRot;
        return pivot.transform;
    }

    private static GameObject Prim(
        Transform parent, string name,
        Vector3 localPos, Vector3 localScale, Vector3 euler,
        Material mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.localPosition = localPos;
        obj.transform.localScale    = localScale;
        obj.transform.localRotation = Quaternion.Euler(euler);

        // Preview only — no physics.
        Object.DestroyImmediate(obj.GetComponent<Collider>());
        obj.GetComponent<Renderer>().sharedMaterial = mat;
        return obj;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // MATERIAL
    // ──────────────────────────────────────────────────────────────────────────

    private static Material GetOrCreateMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");

        Material mat = new Material(shader)
        {
            name  = MatName,
            color = new Color(0.44f, 0.44f, 0.42f)
        };

        Directory.CreateDirectory("Assets/Materials");
        AssetDatabase.CreateAsset(mat, MatPath);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
