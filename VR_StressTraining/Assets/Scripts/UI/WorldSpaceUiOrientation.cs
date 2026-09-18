using UnityEngine;

namespace StressTraining.UI
{
    /// <summary>
    /// Places a world-space Canvas in front of a viewer. Unity uGUI's readable
    /// face is local -Z, therefore the Canvas transform.forward points from the
    /// viewer toward the panel (away from the viewer), not back at the viewer.
    /// </summary>
    public static class WorldSpaceUiOrientation
    {
        public static void PlaceForViewer(Transform anchor, Transform viewer,
            float distanceMeters, float verticalOffsetMeters = 0f)
        {
            if (anchor == null || viewer == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(viewer.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            anchor.position = viewer.position + forward * Mathf.Max(0.35f, distanceMeters) +
                              Vector3.up * verticalOffsetMeters;
            anchor.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public static void PlaceFromSpawn(Transform anchor, Transform spawn,
            float distanceMeters, float heightMeters)
        {
            if (anchor == null || spawn == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(spawn.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            anchor.position = spawn.position + forward * distanceMeters + Vector3.up * heightMeters;
            anchor.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public static bool IsReadableFrom(Transform canvasTransform, Transform viewer,
            float minimumFacingDot = 0.8f)
        {
            if (canvasTransform == null || viewer == null) return false;
            Vector3 viewerToCanvas = (canvasTransform.position - viewer.position).normalized;
            bool facing = Vector3.Dot(canvasTransform.forward, viewerToCanvas) >= minimumFacingDot;
            bool upright = Vector3.Dot(canvasTransform.up, Vector3.up) >= 0.8f;
            return facing && upright && canvasTransform.lossyScale.x * canvasTransform.lossyScale.y > 0f;
        }
    }
}
