using UnityEngine;

/// <summary>
/// ══════════════════════════════════════════════════════════════════════
///  TEMPORARY POSE TESTER — mechanical chain verification only.
///  Remove or disable before shipping.  Not gameplay logic.
/// ══════════════════════════════════════════════════════════════════════
///
/// Safety guarantees:
///  • On Awake, caches the original localPosition + localRotation + localScale
///    of every pivot it will touch.
///  • Rotations are applied as:  original * Quaternion.Euler(offset)
///    — never overwriting raw eulerAngles from zero.
///  • Only the four pivot EMPTIES are animated:
///      BasePivot, ShoulderPivot, ElbowPivot, WristPivot
///  • Mesh objects (BaseJoint, UpperArm, Forearm, Claw_A, Claw_B …)
///    are never touched.
///  • Claw_A / Claw_B are intentionally NOT animated — they are mesh
///    objects, not pivot empties.  Add ClawPivot_A / ClawPivot_B empties
///    in Blender first if claw animation is needed.
///  • MountPlate is a sibling of BasePivot (child of RobotArm_Main_WallMount),
///    so BasePivot rotation never moves the mount plate.
/// ══════════════════════════════════════════════════════════════════════
/// </summary>
[AddComponentMenu("VR Stress Training/[TEMP] Robot Arm Pose Tester")]
public class RobotArmPoseTester : MonoBehaviour
{
    // ── Rest-pose cache ───────────────────────────────────────────────
    struct PoseCache
    {
        public Vector3    pos;
        public Quaternion rot;
        public Vector3    scale;

        public static PoseCache From(Transform t) => new PoseCache
        {
            pos   = t.localPosition,
            rot   = t.localRotation,
            scale = t.localScale,
        };
    }

    PoseCache _baseCache;
    PoseCache _shoulderCache;
    PoseCache _elbowCache;
    PoseCache _wristCache;

    Transform _basePivot;
    Transform _shoulderPivot;
    Transform _elbowPivot;
    Transform _wristPivot;

    bool _ready = false;

    // ── Inspector ─────────────────────────────────────────────────────

    [Header("⚠  TEMP TESTER — remove before shipping  ⚠")]

    [Header("Target offset angles  (degrees, additive from rest pose)")]
    [Range(-90f,  90f)] public float baseYaw      = 0f;
    [Range(-90f,  90f)] public float shoulderPitch = 0f;
    [Range(-120f, 10f)] public float elbowPitch    = 0f;
    [Range(-90f,  90f)] public float wristPitch    = 0f;

    [Header("Loop test")]
    public bool  playTestLoop    = false;
    [Range(0.1f, 5f)]
    public float animationSpeed  = 1f;

    // ── Internal loop state ───────────────────────────────────────────
    float _loopTimer = 0f;
    int   _loopPhase = 0;   // 0 = idle, 1 = reach, 2 = present, 3 = reset

    // ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        _basePivot     = FindDeep("BasePivot");
        _shoulderPivot = FindDeep("ShoulderPivot");
        _elbowPivot    = FindDeep("ElbowPivot");
        _wristPivot    = FindDeep("WristPivot");

        bool ok = true;
        if (_basePivot     == null) { Log("BasePivot not found");     ok = false; }
        if (_shoulderPivot == null) { Log("ShoulderPivot not found"); ok = false; }
        if (_elbowPivot    == null) { Log("ElbowPivot not found");    ok = false; }
        if (_wristPivot    == null) { Log("WristPivot not found");    ok = false; }

        if (!ok) return;

        // Cache rest pose BEFORE any rotation is applied
        _baseCache     = PoseCache.From(_basePivot);
        _shoulderCache = PoseCache.From(_shoulderPivot);
        _elbowCache    = PoseCache.From(_elbowPivot);
        _wristCache    = PoseCache.From(_wristPivot);

        _ready = true;
        Debug.Log("[RobotArmPoseTester] Rest pose cached for all 4 pivots. Ready.");
    }

    void Update()
    {
        if (!_ready) return;

        if (playTestLoop) AdvanceLoop();

        ApplyPivotRotations();
    }

    // ── Core ──────────────────────────────────────────────────────────

    /// Apply all four pivot offsets additively from their cached rest rotation.
    void ApplyPivotRotations()
    {
        Additive(_basePivot,     _baseCache.rot,     Quaternion.Euler(0f, baseYaw,       0f));
        Additive(_shoulderPivot, _shoulderCache.rot, Quaternion.Euler(shoulderPitch, 0f, 0f));
        Additive(_elbowPivot,    _elbowCache.rot,    Quaternion.Euler(elbowPitch,    0f, 0f));
        Additive(_wristPivot,    _wristCache.rot,    Quaternion.Euler(wristPitch,    0f, 0f));
    }

    static void Additive(Transform t, Quaternion restRot, Quaternion offset)
    {
        if (t != null) t.localRotation = restRot * offset;
    }

    // ── Loop test ─────────────────────────────────────────────────────

    void AdvanceLoop()
    {
        float duration = 2.5f / animationSpeed;
        _loopTimer += Time.deltaTime;
        if (_loopTimer < duration) return;

        _loopTimer = 0f;
        _loopPhase = (_loopPhase + 1) % 4;
        switch (_loopPhase)
        {
            case 0: SetIdlePose();    break;
            case 1: SetReachPose();   break;
            case 2: SetPresentPose(); break;
            case 3: ResetPose();      break;
        }
    }

    // ── Public API ────────────────────────────────────────────────────

    /// Restore all pivots to their exact rest rotation (from Awake cache).
    public void ResetPose()
    {
        baseYaw       = 0f;
        shoulderPitch = 0f;
        elbowPitch    = 0f;
        wristPitch    = 0f;
    }

    /// Neutral hanging pose — arm relaxed at side.
    public void SetIdlePose()
    {
        baseYaw       =   0f;
        shoulderPitch =   0f;
        elbowPitch    = -10f;
        wristPitch    =   5f;
    }

    /// Arm extends toward console surface.
    public void SetReachPose()
    {
        baseYaw       =  12f;
        shoulderPitch = -25f;
        elbowPitch    = -45f;
        wristPitch    =  20f;
    }

    /// Arm presents toward player.
    public void SetPresentPose()
    {
        baseYaw       = -12f;
        shoulderPitch = -35f;
        elbowPitch    = -20f;
        wristPitch    =  30f;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    Transform FindDeep(string name)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static void Log(string msg) =>
        Debug.LogWarning($"[RobotArmPoseTester] {msg}");
}
