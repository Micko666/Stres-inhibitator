using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor shape-preview tool — Adaptive Stress Corridor.
///
/// Menu: VR StressTraining → Console Prototype
///
/// "Build Clean Shape Preview" → creates Console_Prototype_Preview at PreviewPosition.
/// "Clear Console Preview"     → removes it.
///
/// Workflow: change any constant below → Build → evaluate silhouette → iterate.
/// Never touches MainScene, Console_Placeholder, XR rig, or Project Settings.
/// All preview primitives have their colliders removed (visual-only).
/// </summary>
public static class ConsolePrototypeBuilder
{
    // ─────────────────────────────────────────────────────────────────────────
    // SHAPE CONSTANTS
    // Change these, click "Build Clean Shape Preview", see the result.
    // ─────────────────────────────────────────────────────────────────────────

    /// Radius of the crescent arc the 5 sections sit on (metres)
    private const float ConsoleArcRadius     = 2.80f;

    /// Width of each of the 5 arc segments (metres)
    private const float SectionWidth         = 1.80f;

    /// Front-to-back depth of each segment (metres)
    private const float SectionDepth         = 0.95f;

    /// Height of the lower cabinet body per segment (metres)
    private const float BaseHeight           = 0.90f;

    /// Thin plinth/pedestal under each body (metres)
    private const float PedestalHeight       = 0.14f;

    /// Thickness of the angled top slab (metres)
    private const float TopThickness         = 0.13f;

    /// Tilt angle of the top surface toward the operator (degrees, negative = tilt forward)
    private const float SurfaceAngle         = -11f;

    /// Height of the industrial inner-rim strip on the player-facing edge (metres)
    private const float RimHeight            = 0.11f;

    /// Depth of the inner-rim strip (metres)
    private const float RimDepth             = 0.07f;

    /// Half-width of the flat floor tile showing the operator standing zone (metres)
    private const float OperatorCutoutRadius = 1.55f;

    /// Height of the arm-mount pillars (metres)
    private const float ArmMountHeight       = 1.10f;

    /// Lateral (X) distance of arm mounts from centre (metres)
    private const float ArmMountOffsetX      = 2.35f;

    /// Forward (Z) distance of arm mounts from centre (metres)
    private const float ArmMountOffsetZ      = 2.75f;

    /// World-space position where the preview root is placed.
    /// Defaults to Z=20 so it sits clear of the MainScene corridor.
    private static readonly Vector3 PreviewPosition = new Vector3(0f, 0f, 20f);

    // ─────────────────────────────────────────────────────────────────────────
    // INTERNALS (do not need to change these)
    // ─────────────────────────────────────────────────────────────────────────

    private const int    SegmentCount = 5;
    private static readonly float[] SegmentAngles = { -55f, -28f, 0f, 28f, 55f };
    private static readonly string[] SegmentNames =
        { "Seg_LeftOuter", "Seg_Left", "Seg_Center", "Seg_Right", "Seg_RightOuter" };

    private const string RootName = "Console_Prototype_Preview";
    private const string MatName  = "MAT_Console_Prototype_Grey";
    private const string MatPath  = "Assets/Materials/" + MatName + ".mat";

    // ─────────────────────────────────────────────────────────────────────────
    // MENU ITEMS
    // ─────────────────────────────────────────────────────────────────────────

    [MenuItem("VR StressTraining/Console Prototype/Build Clean Shape Preview")]
    public static void BuildPreview()
    {
        // Remove previous preview if present.
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
            Undo.DestroyObjectImmediate(existing);

        Material mat = GetOrCreateMaterial();

        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Console Shape Preview");
        root.transform.position = PreviewPosition;

        BuildCrecentSections(root.transform, mat);
        BuildOperatorZone(root.transform, mat);
        BuildArmMounts(root.transform, mat);

        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();

        Debug.Log($"[ConsolePrototypeBuilder] Preview built at {PreviewPosition}. " +
                  $"Adjust constants and rebuild to iterate.");
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

    // ─────────────────────────────────────────────────────────────────────────
    // CRESCENT SECTIONS
    // ─────────────────────────────────────────────────────────────────────────

    private static void BuildCrecentSections(Transform root, Material mat)
    {
        for (int i = 0; i < SegmentCount; i++)
        {
            float angleDeg = SegmentAngles[i];
            float rad      = angleDeg * Mathf.Deg2Rad;

            Vector3 localPos = new Vector3(
                Mathf.Sin(rad) * ConsoleArcRadius,
                0f,
                Mathf.Cos(rad) * ConsoleArcRadius);

            GameObject seg = new GameObject(SegmentNames[i]);
            seg.transform.SetParent(root);
            seg.transform.localPosition = localPos;
            seg.transform.localRotation = Quaternion.Euler(0f, angleDeg, 0f);

            BuildSection(seg.transform, mat);
        }
    }

    private static void BuildSection(Transform seg, Material mat)
    {
        // 1 — Pedestal: thin plate at the base of each segment
        Prim(seg, "Pedestal",
            new Vector3(0f, PedestalHeight * 0.5f, 0f),
            new Vector3(SectionWidth + 0.12f, PedestalHeight, SectionDepth + 0.12f),
            Vector3.zero, mat);

        // 2 — Body: main lower cabinet block
        Prim(seg, "Body",
            new Vector3(0f, PedestalHeight + BaseHeight * 0.5f, 0f),
            new Vector3(SectionWidth, BaseHeight, SectionDepth),
            Vector3.zero, mat);

        // 3 — TopSurface: angled slab slightly wider/deeper than the body
        float topY = PedestalHeight + BaseHeight + TopThickness * 0.5f;
        Prim(seg, "TopSurface",
            new Vector3(0f, topY, -0.04f),
            new Vector3(SectionWidth + 0.10f, TopThickness, SectionDepth + 0.16f),
            new Vector3(SurfaceAngle, 0f, 0f), mat);

        // 4 — InnerRim: thick industrial strip on the player-facing edge
        float rimY = PedestalHeight + BaseHeight + RimHeight * 0.5f;
        Prim(seg, "InnerRim",
            new Vector3(0f, rimY, -(SectionDepth * 0.5f + RimDepth * 0.5f)),
            new Vector3(SectionWidth + 0.10f, RimHeight, RimDepth),
            Vector3.zero, mat);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OPERATOR STANDING ZONE
    // ─────────────────────────────────────────────────────────────────────────

    private static void BuildOperatorZone(Transform root, Material mat)
    {
        // Flat tile indicating where the operator stands — no collision.
        Prim(root, "OperatorStandingZone",
            new Vector3(0f, 0.01f, 0.45f),
            new Vector3(OperatorCutoutRadius * 1.8f, 0.02f, OperatorCutoutRadius),
            Vector3.zero, mat);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ARM MOUNT PLACEHOLDERS
    // ─────────────────────────────────────────────────────────────────────────

    private static void BuildArmMounts(Transform root, Material mat)
    {
        BuildArmMount(root, "ArmMount_Left",  -ArmMountOffsetX, mat);
        BuildArmMount(root, "ArmMount_Right",  ArmMountOffsetX, mat);
    }

    private static void BuildArmMount(Transform root, string name, float offsetX, Material mat)
    {
        // Vertical pillar
        Prim(root, name + "_Pillar",
            new Vector3(offsetX, ArmMountHeight * 0.5f, ArmMountOffsetZ),
            new Vector3(0.32f, ArmMountHeight, 0.32f),
            Vector3.zero, mat);

        // Cap plate on top of pillar
        Prim(root, name + "_Cap",
            new Vector3(offsetX, ArmMountHeight + 0.04f, ArmMountOffsetZ),
            new Vector3(0.52f, 0.06f, 0.52f),
            Vector3.zero, mat);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PRIMITIVE HELPER — cube, no collider, preview only
    // ─────────────────────────────────────────────────────────────────────────

    private static GameObject Prim(
        Transform parent,
        string    name,
        Vector3   localPos,
        Vector3   localScale,
        Vector3   euler,
        Material  mat)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = name;
        obj.transform.SetParent(parent);
        obj.transform.localPosition = localPos;
        obj.transform.localScale    = localScale;
        obj.transform.localRotation = Quaternion.Euler(euler);

        // Preview only — remove collider so it never affects gameplay or physics.
        Object.DestroyImmediate(obj.GetComponent<Collider>());

        obj.GetComponent<Renderer>().sharedMaterial = mat;
        return obj;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MATERIAL
    // ─────────────────────────────────────────────────────────────────────────

    private static Material GetOrCreateMaterial()
    {
        // Re-use saved asset if it already exists.
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (existing != null) return existing;

        // Prefer URP Lit; fall back to Standard (safe in any pipeline).
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
