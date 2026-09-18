using System;
using UnityEngine;

namespace StressTraining.Pressure
{
    /// <summary>
    /// Centralized PROJECT_HEURISTIC tuning for the imported puzzle corridor.
    /// Logical stage ownership remains in PressureTimeline/PuzzleCollapseData; this
    /// asset only describes how an already-authorized action looks.
    /// </summary>
    [CreateAssetMenu(
        menuName = "Stress Training/Pressure/Puzzle Collapse Animation Profile",
        fileName = "PuzzleCollapseAnimationProfile_Default")]
    public sealed class PuzzleCollapseAnimationProfile : ScriptableObject
    {
        [Serializable]
        public sealed class WaveSettings
        {
            [Min(0.05f)] public float warningDuration = 0.75f;
            [Min(0f)] public float warningPositionMeters = 0.010f;
            [Min(0f)] public float warningRotationDegrees = 0.50f;
            [Min(0.1f)] public float warningFrequencyHz = 9f;
            [Min(0f)] public float warningResidualGapMeters = 0.0015f;

            [Min(0.05f)] public float loosenDuration = 0.95f;
            [Min(0f)] public float loosenGapMeters = 0.040f;
            [Min(0f)] public float loosenCeilingDropMeters = 0.035f;
            [Min(0f)] public float loosenWallRotationDegrees = 2.5f;
            [Min(0f)] public float loosenCeilingRotationDegrees = 1.25f;
            [Min(0f)] public float loosenFloorDropMeters = 0.008f;

            [Min(0f)] public float segmentStartDelay = 0.0f;
            [Min(0.02f)] public float detachDuration = 0.28f;
            [Min(0f)] public float detachedHoldSeconds = 0.10f;
            [Min(0.10f)] public float fallDuration = 1.65f;
            [Min(0f)] public float settleDuration = 0.12f;
            [Min(0f)] public float fallDistanceMeters = 4.2f;
            [Min(0f)] public float lateralReleaseMeters = 0.22f;
            [Min(0f)] public float awayReleaseMeters = 0.16f;
            [Min(0f)] public float fallRotationDegrees = 24f;
            [Min(0f)] public float hideDelaySeconds = 0.05f;
        }

        [Header("Wave profiles")]
        public WaveSettings farWave = new WaveSettings
        {
            warningDuration = 0.78f,
            warningPositionMeters = 0.012f,
            warningRotationDegrees = 0.65f,
            warningFrequencyHz = 9.5f,
            loosenDuration = 0.95f,
            loosenGapMeters = 0.045f,
            loosenCeilingDropMeters = 0.040f,
            loosenWallRotationDegrees = 3.0f,
            loosenCeilingRotationDegrees = 1.5f,
            loosenFloorDropMeters = 0.010f,
            detachDuration = 0.30f,
            detachedHoldSeconds = 0.11f,
            fallDuration = 1.72f,
            settleDuration = 0.12f,
            fallDistanceMeters = 4.6f,
            lateralReleaseMeters = 0.24f,
            awayReleaseMeters = 0.18f,
            fallRotationDegrees = 30f,
            hideDelaySeconds = 0.08f
        };

        public WaveSettings middleWave = new WaveSettings
        {
            warningDuration = 0.74f,
            warningPositionMeters = 0.011f,
            warningRotationDegrees = 0.55f,
            warningFrequencyHz = 9f,
            loosenDuration = 0.90f,
            loosenGapMeters = 0.035f,
            loosenCeilingDropMeters = 0.032f,
            loosenWallRotationDegrees = 2.4f,
            loosenCeilingRotationDegrees = 1.25f,
            loosenFloorDropMeters = 0.008f,
            detachDuration = 0.27f,
            detachedHoldSeconds = 0.10f,
            fallDuration = 1.50f,
            settleDuration = 0.10f,
            fallDistanceMeters = 4.2f,
            lateralReleaseMeters = 0.22f,
            awayReleaseMeters = 0.17f,
            fallRotationDegrees = 25f,
            hideDelaySeconds = 0.06f
        };

        public WaveSettings nearWave = new WaveSettings
        {
            warningDuration = 0.70f,
            warningPositionMeters = 0.009f,
            warningRotationDegrees = 0.45f,
            warningFrequencyHz = 8.5f,
            loosenDuration = 0.84f,
            loosenGapMeters = 0.032f,
            loosenCeilingDropMeters = 0.028f,
            loosenWallRotationDegrees = 2.1f,
            loosenCeilingRotationDegrees = 1.0f,
            loosenFloorDropMeters = 0.006f,
            segmentStartDelay = 0.08f,
            detachDuration = 0.25f,
            detachedHoldSeconds = 0.09f,
            fallDuration = 1.38f,
            settleDuration = 0.09f,
            fallDistanceMeters = 3.8f,
            lateralReleaseMeters = 0.26f,
            awayReleaseMeters = 0.24f,
            fallRotationDegrees = 21f,
            hideDelaySeconds = 0.05f
        };

        public WaveSettings safeWave = new WaveSettings
        {
            warningDuration = 0.65f,
            warningPositionMeters = 0.003f,
            warningRotationDegrees = 0.15f,
            warningFrequencyHz = 7f,
            warningResidualGapMeters = 0f,
            loosenDuration = 1.05f,
            loosenGapMeters = 0.045f,
            loosenCeilingDropMeters = 0.035f,
            loosenWallRotationDegrees = 2.0f,
            loosenCeilingRotationDegrees = 1.0f,
            loosenFloorDropMeters = 0f,
            detachDuration = 0.25f,
            detachedHoldSeconds = 0f,
            fallDuration = 0.8f,
            settleDuration = 0f,
            fallDistanceMeters = 0f,
            lateralReleaseMeters = 0f,
            awayReleaseMeters = 0f,
            fallRotationDegrees = 0f,
            hideDelaySeconds = 0f
        };

        [Header("Deterministic part stagger")]
        [Min(0f)] public float ceilingDelay = 0.00f;
        [Min(0f)] public float firstWallDelay = 0.12f;
        [Min(0f)] public float secondWallDelay = 0.24f;
        [Min(0f)] public float floorDelay = 0.35f;
        [Min(0f)] public float pairedSegmentDelay = 0.42f;
        [Min(0f)] public float seededDelayVariation = 0.035f;
        [Min(0f)] public float seededTiltVariationDegrees = 2.0f;

        [Header("Curves (normalized 0-1)")]
        public AnimationCurve warningShakeEnvelope;
        public AnimationCurve loosenPositionCurve;
        public AnimationCurve loosenRotationCurve;
        public AnimationCurve detachCurve;
        public AnimationCurve fallPositionCurve;
        public AnimationCurve fallRotationCurve;
        public AnimationCurve settleCurve;
        public AnimationCurve lightFlickerCurve;
        public AnimationCurve visibilityCurve;

        [Header("Safety bounds")]
        public Vector3 headSafetyExtents = new Vector3(0.38f, 0.40f, 0.38f);
        public Vector3 consoleSafetyExtents = new Vector3(0.95f, 0.70f, 0.70f);
        public Vector3 tabletSafetyExtents = new Vector3(0.55f, 0.45f, 0.35f);
        public Vector3 controlsSafetyExtents = new Vector3(1.20f, 0.55f, 0.65f);
        [Min(0f)] public float safetyPaddingMeters = 0.08f;
        [Range(2, 12)] public int safetyClampIterations = 8;
        [Min(0f)] public float safeWallMaximumTranslation = 0.060f;
        [Min(0f)] public float safeCeilingMaximumTranslation = 0.050f;
        [Min(0f)] public float safeMaximumRotationDegrees = 3.0f;

        [Header("Visibility / void")]
        /// <summary>
        /// Local void backings: a dark quad spawned behind each segment to fake
        /// emptiness. OFF by default — the imported puzzle corridor already exposes
        /// the real space when a segment falls, so the backing only reads as a fake
        /// dark panel hanging in the room. A real sky backdrop replaces it.
        /// </summary>
        public bool useVoidBackings = false;

        [Range(0f, 1f)] public float revealVoidAtFallProgress = 0.24f;
        [Range(0f, 1f)] public float hideRendererAtFallProgress = 0.96f;
        [Min(0.001f)] public float voidThicknessMeters = 0.025f;
        [Min(0f)] public float voidSurfaceOffsetMeters = 0.055f;
        public Color voidColor = new Color(0.003f, 0.004f, 0.006f, 1f);

        [Header("Optional physics handoff (disabled by default)")]
        public bool usePhysicsAfterDetach = false;

        public WaveSettings GetWave(PuzzleSegmentPlan plan)
        {
            if (plan == null || plan.IsSafe) return safeWave;
            if (plan.CollapseOrder <= 1) return farWave;
            if (plan.CollapseOrder <= 3) return middleWave;
            return nearWave;
        }

        public float BasePartDelay(SegmentPart part, bool leftWallFirst)
        {
            switch (part)
            {
                case SegmentPart.Ceiling:
                    return ceilingDelay;
                case SegmentPart.WallLeft:
                    return leftWallFirst ? firstWallDelay : secondWallDelay;
                case SegmentPart.WallRight:
                    return leftWallFirst ? secondWallDelay : firstWallDelay;
                default:
                    return floorDelay;
            }
        }

        public static PuzzleCollapseAnimationProfile CreateRuntimeFallback()
        {
            var profile = CreateInstance<PuzzleCollapseAnimationProfile>();
            profile.name = "PuzzleCollapseAnimationProfile_RuntimeFallback";
            profile.hideFlags = HideFlags.HideAndDontSave;
            profile.EnsureCurves();
            profile.ClampValues();
            return profile;
        }

        public void EnsureCurves()
        {
            EnsureWaveObjects();
            if (warningShakeEnvelope == null || warningShakeEnvelope.length == 0)
                warningShakeEnvelope = new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 5f),
                    new Keyframe(0.16f, 1f),
                    new Keyframe(0.68f, 0.72f),
                    new Keyframe(1f, 0f, -4f, 0f));
            if (loosenPositionCurve == null || loosenPositionCurve.length == 0)
                loosenPositionCurve = SmoothCurve();
            if (loosenRotationCurve == null || loosenRotationCurve.length == 0)
                loosenRotationCurve = SmoothCurve();
            if (detachCurve == null || detachCurve.length == 0)
                detachCurve = new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 0.25f),
                    new Keyframe(0.35f, 0.10f),
                    new Keyframe(1f, 1f, 2.1f, 0f));
            if (fallPositionCurve == null || fallPositionCurve.length == 0)
                fallPositionCurve = new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 0.05f),
                    new Keyframe(0.17f, 0.025f),
                    new Keyframe(0.46f, 0.28f),
                    new Keyframe(1f, 1f, 1.8f, 0f));
            if (fallRotationCurve == null || fallRotationCurve.length == 0)
                fallRotationCurve = new AnimationCurve(
                    new Keyframe(0f, 0f, 0f, 0.2f),
                    new Keyframe(0.34f, 0.18f),
                    new Keyframe(0.82f, 0.86f),
                    new Keyframe(1f, 1f, 0.7f, 0f));
            if (settleCurve == null || settleCurve.length == 0)
                settleCurve = new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.55f, 1.08f),
                    new Keyframe(1f, 1f));
            if (lightFlickerCurve == null || lightFlickerCurve.length == 0)
                lightFlickerCurve = new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(0.22f, 0.62f),
                    new Keyframe(0.38f, 0.94f),
                    new Keyframe(0.63f, 0.72f),
                    new Keyframe(1f, 1f));
            if (visibilityCurve == null || visibilityCurve.length == 0)
                visibilityCurve = new AnimationCurve(
                    new Keyframe(0f, 1f),
                    new Keyframe(0.88f, 1f),
                    new Keyframe(1f, 0f));
        }

        private static AnimationCurve SmoothCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(1f, 1f, 0f, 0f));
        }

        private void OnEnable()
        {
            EnsureCurves();
            ClampValues();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureCurves();
            ClampValues();
        }
#endif

        private void ClampValues()
        {
            ClampWave(farWave);
            ClampWave(middleWave);
            ClampWave(nearWave);
            ClampWave(safeWave);
            safetyClampIterations = Mathf.Clamp(safetyClampIterations, 2, 12);
            revealVoidAtFallProgress = Mathf.Clamp01(revealVoidAtFallProgress);
            float minimumHideProgress = Mathf.Min(1f,
                revealVoidAtFallProgress + 0.05f);
            hideRendererAtFallProgress = Mathf.Clamp(
                hideRendererAtFallProgress, minimumHideProgress, 1f);
            safeWallMaximumTranslation = Mathf.Clamp(safeWallMaximumTranslation, 0f, 0.06f);
            safeCeilingMaximumTranslation = Mathf.Clamp(safeCeilingMaximumTranslation, 0f, 0.05f);
            safeMaximumRotationDegrees = Mathf.Clamp(safeMaximumRotationDegrees, 0f, 3f);
        }

        private void EnsureWaveObjects()
        {
            if (farWave == null) farWave = new WaveSettings();
            if (middleWave == null) middleWave = new WaveSettings();
            if (nearWave == null) nearWave = new WaveSettings();
            if (safeWave == null) safeWave = new WaveSettings();
        }

        private static void ClampWave(WaveSettings wave)
        {
            if (wave == null) return;
            wave.warningDuration = Mathf.Max(0.05f, wave.warningDuration);
            wave.warningFrequencyHz = Mathf.Max(0.1f, wave.warningFrequencyHz);
            wave.loosenDuration = Mathf.Max(0.05f, wave.loosenDuration);
            wave.detachDuration = Mathf.Max(0.02f, wave.detachDuration);
            wave.fallDuration = Mathf.Max(0.10f, wave.fallDuration);
            wave.settleDuration = Mathf.Max(0f, wave.settleDuration);
        }
    }
}
