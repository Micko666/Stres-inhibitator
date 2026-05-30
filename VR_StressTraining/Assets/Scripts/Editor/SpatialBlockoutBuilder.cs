using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Phase 1 — Spatial Blockout
/// Menu: VR StressTraining → Build Spatial Blockout
///
/// Creates a placeholder Adaptive Stress Corridor:
///   - 2.8 m wide × 2.8 m tall × 10 m long
///   - Console placeholder at the near end (player faces it)
///   - Cage placeholder at the far end (threat source)
///   - Two point lights for basic corridor atmosphere
///   - PlayerSpawnPoint marker (reference only — do not move OVRCameraRig)
///
/// All pieces are marked Static for future baked lighting.
/// No gameplay scripts are attached — blockout geometry only.
///
/// Usage:
///   1. Open MainScene in Unity
///   2. Menu → VR StressTraining → Build Spatial Blockout
///   3. Ctrl+S to save the scene
///   4. Optional: disable or delete the existing "Plane" root object
///      (the blockout Floor replaces it)
/// </summary>
public static class SpatialBlockoutBuilder
{
    // ── Corridor dimensions ─────────────────────────────────────────
    const float W = 2.8f;   // width  (X)
    const float H = 2.8f;   // height (Y)
    const float L = 10.0f;  // length (Z)  — player end at -L/2, cage end at +L/2
    const float T = 0.2f;   // wall / floor / ceiling thickness

    [MenuItem("VR StressTraining/Build Spatial Blockout", priority = 100)]
    static void Build()
    {
        var existing = GameObject.Find("Corridor_Blockout");
        if (existing != null)
        {
            bool rebuild = EditorUtility.DisplayDialog(
                "Rebuild Spatial Blockout?",
                "Corridor_Blockout already exists in the scene.\nDestroy it and rebuild from scratch?",
                "Rebuild", "Cancel");
            if (!rebuild) return;
            Undo.DestroyObjectImmediate(existing);
        }

        // Root object — all blockout children live here
        var root = new GameObject("Corridor_Blockout");
        Undo.RegisterCreatedObjectUndo(root, "Build Spatial Blockout");

        BuildCorridorShell(root);
        BuildConsolePlaceholder(root);
        BuildCagePlaceholder(root);
        BuildCorridorLighting(root);
        BuildPlayerSpawn(root);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = root;

        Debug.Log(
            "[SpatialBlockout] Done.\n" +
            $"  Corridor : {W} m wide × {H} m tall × {L} m long\n" +
            $"  Floor    : y = 0   Ceiling : y = {H}\n" +
            $"  Player Z : {-L / 2 + 1.5f:F2}   Console Z : {-L / 2 + 0.55f:F2}   Cage Z : {L / 2 - 0.35f:F2}\n" +
            "  Press Ctrl+S to save. Disable the existing Plane if not needed.");
    }

    // ── Corridor shell: floor, ceiling, 3 walls (cage end open) ────

    static void BuildCorridorShell(GameObject root)
    {
        // Floor — top surface at y = 0
        MakeCube(root, "Floor",
            0f,         -T / 2f,        0f,
            W + T * 2,  T,              L + T * 2);

        // Ceiling
        MakeCube(root, "Ceiling",
            0f,         H + T / 2f,     0f,
            W + T * 2,  T,              L + T * 2);

        // Left wall
        MakeCube(root, "Wall_Left",
            -(W / 2 + T / 2),  H / 2,  0f,
            T,                 H,       L);

        // Right wall
        MakeCube(root, "Wall_Right",
            W / 2 + T / 2,     H / 2,  0f,
            T,                 H,       L);

        // Console-end back wall (solid)
        MakeCube(root, "Wall_ConsoleEnd",
            0f,         H / 2,          -(L / 2 + T / 2),
            W + T * 2,  H,              T);

        // Cage end: no solid wall — cage structure itself closes the space
    }

    // ── Console placeholder: surface + body, against the back wall ─

    static void BuildConsolePlaceholder(GameObject root)
    {
        var con = EmptyChild(root, "Console_Placeholder");

        float cz = -L / 2 + 0.55f;  // console face centre Z  (≈ -4.45)

        // Flat work surface (top at y ≈ 0.98 m — comfortable standing height)
        MakeCube(con, "Surface",
            0f,  0.95f,  cz,
            2.0f, 0.06f, 0.55f);

        // Console body / housing below the surface
        MakeCube(con, "Body",
            0f,  0.47f,  cz,
            2.0f, 0.88f, 0.45f);
    }

    // ── Cage placeholder: frame + door pivot ───────────────────────

    static void BuildCagePlaceholder(GameObject root)
    {
        var cage = EmptyChild(root, "Cage_Placeholder");

        float gz  = L / 2 - 0.35f;  // cage front face Z  (≈ +4.65)
        float gW  = 1.6f;            // cage gate width
        float gH  = 2.2f;            // cage gate height
        float bar = 0.1f;            // bar/rail cross-section

        // Horizontal rails
        MakeCube(cage, "Rail_Top",
            0f,      gH,       gz,
            gW,      bar,      bar);

        MakeCube(cage, "Rail_Bottom",
            0f,      bar / 2,  gz,
            gW,      bar,      bar);

        // Vertical side posts
        MakeCube(cage, "Post_Left",
            -gW / 2, gH / 2,   gz,
            bar,     gH,       bar);

        MakeCube(cage, "Post_Right",
            gW / 2,  gH / 2,   gz,
            bar,     gH,       bar);

        // Centre bar (divides gate into two halves)
        MakeCube(cage, "Bar_Centre",
            0f,      gH / 2,   gz,
            0.08f,   gH,       0.08f);

        // Door pivot — hinge at the LEFT edge of the left half
        // ThreatController will rotate this around Y to open the door.
        var doorPivot = EmptyChild(cage, "Door_Pivot");
        doorPivot.transform.localPosition = new Vector3(-gW / 2, 0f, gz);

        // Door panel is a child of Door_Pivot, offset so its left edge aligns with pivot
        float panelW = gW / 2 - bar;
        float panelH = gH - bar;
        MakeCube(doorPivot, "Door_Panel",
            panelW / 2,   gH / 2,  0f,   // local: offset right from pivot
            panelW,       panelH,  0.05f);
    }

    // ── Two point lights for basic corridor atmosphere ──────────────

    static void BuildCorridorLighting(GameObject root)
    {
        // Brighter light above the console / task area
        AddPointLight(root, "Light_ConsoleZone",
            0f,  H - 0.1f,  -L / 2 + 2.5f,
            intensity: 1.2f, range: 8f);

        // Dimmer, redder light near the cage for tension
        AddPointLight(root, "Light_CageZone",
            0f,  H - 0.1f,   L / 2 - 2.5f,
            intensity: 0.5f, range: 7f,
            color: new Color(1f, 0.55f, 0.35f));
    }

    // ── Player spawn marker (does NOT move OVRCameraRig) ───────────

    static void BuildPlayerSpawn(GameObject root)
    {
        var spawn = EmptyChild(root, "PlayerSpawnPoint");
        spawn.transform.localPosition = new Vector3(0f, 0f, -L / 2 + 1.5f);
        // z ≈ -3.5 → ~0.95 m from console face, comfortable interaction distance
    }

    // ── Helpers ────────────────────────────────────────────────────

    static void MakeCube(GameObject parent, string name,
                          float x, float y, float z,
                          float sx, float sy, float sz)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.isStatic = true;
        go.transform.SetParent(parent.transform, worldPositionStays: false);
        go.transform.localPosition = new Vector3(x, y, z);
        go.transform.localScale    = new Vector3(sx, sy, sz);
    }

    static GameObject EmptyChild(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, worldPositionStays: false);
        return go;
    }

    static void AddPointLight(GameObject parent, string name,
                               float x, float y, float z,
                               float intensity, float range,
                               Color? color = null)
    {
        var go = EmptyChild(parent, name);
        go.transform.localPosition = new Vector3(x, y, z);
        var light = go.AddComponent<Light>();
        light.type      = LightType.Point;
        light.intensity = intensity;
        light.range     = range;
        light.shadows   = LightShadows.Soft;
        if (color.HasValue) light.color = color.Value;
    }
}
