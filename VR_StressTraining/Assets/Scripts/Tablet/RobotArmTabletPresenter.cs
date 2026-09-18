using UnityEngine;

namespace StressTraining.Tablet
{
    /// <summary>
    /// Static robot-arm presenter. PHASE 8.5 removes every runtime transition;
    /// the tested present pose is applied once and all legacy public calls are
    /// retained as idempotent/no-op compatibility methods.
    /// </summary>
    public sealed class RobotArmTabletPresenter : MonoBehaviour
    {
        [SerializeField] private Transform armRoot;
        [SerializeField] private Vector4 fixedPresentPose = new Vector4(6f, -20f, -35f, 18f);

        private Transform _basePivot, _shoulderPivot, _elbowPivot, _wristPivot;
        private Quaternion _baseRest, _shoulderRest, _elbowRest, _wristRest;
        private bool _ready;

        public bool StaticPoseApplied { get; private set; }
        public bool HasActiveTransition => false;

        public void Initialize(Transform robotArmRoot)
        {
            armRoot = robotArmRoot;
            CachePivotsAndApplyFixedPose();
        }

        private void Awake()
        {
            if (armRoot != null && !_ready) CachePivotsAndApplyFixedPose();
        }

        private void CachePivotsAndApplyFixedPose()
        {
            if (armRoot == null) { Fallback("armRoot not assigned"); return; }
            _basePivot = FindDeep(armRoot, "BasePivot");
            _shoulderPivot = FindDeep(armRoot, "ShoulderPivot");
            _elbowPivot = FindDeep(armRoot, "ElbowPivot");
            _wristPivot = FindDeep(armRoot, "WristPivot");

            if (_basePivot == null || _shoulderPivot == null ||
                _elbowPivot == null || _wristPivot == null)
            {
                Fallback("pivot hierarchy incomplete");
                return;
            }

            _baseRest = _basePivot.localRotation;
            _shoulderRest = _shoulderPivot.localRotation;
            _elbowRest = _elbowPivot.localRotation;
            _wristRest = _wristPivot.localRotation;
            _ready = true;
            ApplyFixedPose();
        }

        private void Fallback(string reason)
        {
            _ready = false;
            StaticPoseApplied = false;
            Debug.LogWarning($"[RobotArmTabletPresenter] Static authored-pose fallback — {reason}.");
        }

        public void MoveToPresent() => ApplyFixedPose();
        public void MoveToRest() { /* locked static pose: no movement */ }
        public void SetPaused(bool paused) { _ = paused; /* pause never moves the arm */ }

        private void ApplyFixedPose()
        {
            if (!_ready) return;
            Vector4 pose = ClampToTestedRanges(fixedPresentPose);
            _basePivot.localRotation = _baseRest * Quaternion.Euler(0f, pose.x, 0f);
            _shoulderPivot.localRotation = _shoulderRest * Quaternion.Euler(pose.y, 0f, 0f);
            _elbowPivot.localRotation = _elbowRest * Quaternion.Euler(pose.z, 0f, 0f);
            _wristPivot.localRotation = _wristRest * Quaternion.Euler(pose.w, 0f, 0f);
            StaticPoseApplied = true;
        }

        private static Vector4 ClampToTestedRanges(Vector4 pose) => new Vector4(
            Mathf.Clamp(pose.x, -12f, 12f),
            Mathf.Clamp(pose.y, -35f, 0f),
            Mathf.Clamp(pose.z, -45f, -10f),
            Mathf.Clamp(pose.w, 5f, 30f));

        private static Transform FindDeep(Transform root, string childName)
        {
            foreach (var item in root.GetComponentsInChildren<Transform>(true))
                if (item.name == childName) return item;
            return null;
        }
    }
}
