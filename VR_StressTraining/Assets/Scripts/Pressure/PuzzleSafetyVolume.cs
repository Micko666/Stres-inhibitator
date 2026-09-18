using UnityEngine;

namespace StressTraining.Pressure
{
    /// <summary>
    /// Small fixed set of world-space AABBs around the participant and interaction
    /// hardware. It stores references only; bounds are recomputed when an animation
    /// target is authored so head movement between stages is respected.
    /// </summary>
    public sealed class PuzzleSafetyVolumeSet
    {
        private Transform _head;
        private Transform _console;
        private Transform _tablet;
        private Transform _controls;
        private Vector3 _headExtents;
        private Vector3 _consoleExtents;
        private Vector3 _tabletExtents;
        private Vector3 _controlsExtents;
        private float _padding;

        public bool IsConfigured => _head != null || _console != null ||
                                    _tablet != null || _controls != null;

        public Vector3 Center
        {
            get
            {
                if (_head != null) return _head.position;
                if (_tablet != null) return _tablet.position;
                if (_console != null) return _console.position;
                if (_controls != null) return _controls.position;
                return Vector3.zero;
            }
        }

        public void Configure(Transform head, Transform console, Transform tablet,
            Transform controls, PuzzleCollapseAnimationProfile profile)
        {
            _head = head;
            _console = console;
            _tablet = tablet;
            _controls = controls;
            if (profile == null) return;
            _headExtents = profile.headSafetyExtents;
            _consoleExtents = profile.consoleSafetyExtents;
            _tabletExtents = profile.tabletSafetyExtents;
            _controlsExtents = profile.controlsSafetyExtents;
            _padding = Mathf.Max(0f, profile.safetyPaddingMeters);
        }

        public bool Intersects(Bounds worldBounds)
        {
            if (_head != null && Intersects(worldBounds, _head.position, _headExtents)) return true;
            if (_console != null && Intersects(worldBounds, _console.position, _consoleExtents)) return true;
            if (_tablet != null && Intersects(worldBounds, _tablet.position, _tabletExtents)) return true;
            if (_controls != null && Intersects(worldBounds, _controls.position, _controlsExtents)) return true;
            return false;
        }

        private bool Intersects(Bounds moving, Vector3 center, Vector3 extents)
        {
            extents.x = Mathf.Max(0f, extents.x) + _padding;
            extents.y = Mathf.Max(0f, extents.y) + _padding;
            extents.z = Mathf.Max(0f, extents.z) + _padding;
            var safety = new Bounds(center, extents * 2f);
            return moving.Intersects(safety);
        }
    }
}
