using System;
using System.Collections.Generic;

namespace StressTraining.Pressure
{
    public enum SegmentPart
    {
        Floor = 0,
        Ceiling = 1,
        WallLeft = 2,
        WallRight = 3
    }

    /// <summary>Visible state of one structural part. Ordered for diagnostics/tests.</summary>
    public enum PuzzleAnimationPhase
    {
        Stable = 0,
        Warning = 1,
        Loosening = 2,
        Detached = 3,
        Falling = 4,
        Hidden = 5,
        Resetting = 6
    }

    public enum PuzzleAnimationEventKind
    {
        WarningStarted = 0,
        JointLoosened = 1,
        SegmentDetached = 2,
        SegmentFalling = 3,
        SegmentHidden = 4,
        FinalCollapseStarted = 5
    }

    /// <summary>Allocation-free event payload reused by the corridor controller.</summary>
    public sealed class PuzzleAnimationEventData
    {
        public PuzzleAnimationEventKind Kind;
        public string SegmentId;
        public SegmentPart Part;
        public float Magnitude;
        public UnityEngine.Vector3 WorldPosition;
    }

    /// <summary>
    /// Logical collapse plan for one segment. It decides WHAT is allowed and WHEN;
    /// all durations, curves, distances and rotations live in
    /// <see cref="PuzzleCollapseAnimationProfile"/>.
    /// </summary>
    public sealed class PuzzleSegmentPlan
    {
        public string SegmentId;
        public int CollapseOrder;
        public bool IsSafe;
        public bool CanPhysicallyFall;

        public PressureStage WarningStage;
        public PressureStage LoosenStage;
        public PressureStage CollapseStage;
        public PressureStage HideStage;

        // Compatibility aliases retained for existing tests/reports. They are logical
        // stage aliases only; animation tuning no longer lives in this data object.
        public PressureStage DamageStage => WarningStage;
        public PressureStage DetachStage => CollapseStage;
        public PressureStage VanishStage => HideStage;

        public static bool StageReached(PressureStage current, PressureStage threshold) =>
            threshold != PressureStage.Stable && current >= threshold;
    }

    /// <summary>
    /// Fixed far-to-near logical schedule. PressureTimeline remains the only owner
    /// of the 70/50/30/10/0 thresholds; this class maps its stages onto segments.
    /// </summary>
    public static class PuzzleCollapseSequence
    {
        public const string SafeId = "Safe";
        public static readonly string[] CollapseIds = { "S1", "S2", "S3", "S4", "S5", "S6" };
        public static readonly string[] AllIds = { "S1", "S2", "S3", "S4", "S5", "S6", "Safe" };

        public static string ParseId(string nodeName)
        {
            if (string.IsNullOrEmpty(nodeName)) return null;
            const string prefix = "Segment_";
            if (!nodeName.StartsWith(prefix, StringComparison.Ordinal)) return null;
            string rest = nodeName.Substring(prefix.Length);
            int underscore = rest.IndexOf('_');
            return underscore >= 0 ? rest.Substring(0, underscore) : rest;
        }

        public static bool IsSafe(string id) =>
            string.Equals(id, SafeId, StringComparison.Ordinal);

        public static List<PuzzleSegmentPlan> Build(IReadOnlyCollection<string> discoveredIds)
        {
            var present = new HashSet<string>(
                discoveredIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var plans = new List<PuzzleSegmentPlan>(7);

            for (int order = 0; order < CollapseIds.Length; order++)
            {
                string id = CollapseIds[order];
                if (present.Contains(id)) plans.Add(BuildCollapsePlan(id, order));
            }

            if (present.Contains(SafeId)) plans.Add(BuildSafePlan());
            plans.Sort((a, b) => a.CollapseOrder.CompareTo(b.CollapseOrder));
            return plans;
        }

        private static PuzzleSegmentPlan BuildCollapsePlan(string id, int order)
        {
            PressureStage warning;
            PressureStage loosen;
            PressureStage collapse;
            PressureStage hide;

            if (order <= 1)
            {
                warning = PressureStage.Early;       // 70 %
                loosen = PressureStage.Mid;          // 50 %
                collapse = PressureStage.Late;       // 30 %
                hide = PressureStage.Late;
            }
            else if (order <= 3)
            {
                warning = PressureStage.Mid;         // 50 %
                loosen = PressureStage.Late;         // 30 %
                collapse = PressureStage.Critical;   // 10 %
                hide = PressureStage.Critical;
            }
            else
            {
                warning = PressureStage.Late;        // 30 %
                loosen = PressureStage.Critical;     // 10 %
                collapse = PressureStage.Expired;    // 0 %
                hide = PressureStage.Expired;
            }

            return new PuzzleSegmentPlan
            {
                SegmentId = id,
                CollapseOrder = order,
                IsSafe = false,
                CanPhysicallyFall = true,
                WarningStage = warning,
                LoosenStage = loosen,
                CollapseStage = collapse,
                HideStage = hide
            };
        }

        private static PuzzleSegmentPlan BuildSafePlan()
        {
            return new PuzzleSegmentPlan
            {
                SegmentId = SafeId,
                CollapseOrder = 100,
                IsSafe = true,
                CanPhysicallyFall = false,
                WarningStage = PressureStage.Critical,
                LoosenStage = PressureStage.Critical,
                CollapseStage = PressureStage.Stable,
                HideStage = PressureStage.Stable
            };
        }

        /// <summary>All authored parts are bound; safe-floor movability is checked separately.</summary>
        public static IEnumerable<SegmentPart> AllParts()
        {
            yield return SegmentPart.Floor;
            yield return SegmentPart.Ceiling;
            yield return SegmentPart.WallLeft;
            yield return SegmentPart.WallRight;
        }

        public static IEnumerable<SegmentPart> MovableParts(bool isSafe)
        {
            if (!isSafe) yield return SegmentPart.Floor;
            yield return SegmentPart.Ceiling;
            yield return SegmentPart.WallLeft;
            yield return SegmentPart.WallRight;
        }
    }

    /// <summary>Stable seed hashing; never uses Unity instance ids or Random.Range.</summary>
    public static class PuzzleChoreography
    {
        public static bool LeftWallFirst(int sessionSeed, string segmentId)
        {
            return (Hash(sessionSeed, segmentId, 0x51ED270B) & 1u) == 0u;
        }

        public static float SignedVariation(int sessionSeed, string segmentId,
            SegmentPart part, int salt)
        {
            uint hash = Hash(sessionSeed ^ ((int)part * 486187739), segmentId, salt);
            float normalized = (hash & 0x00FFFFFFu) / 16777215f;
            return normalized * 2f - 1f;
        }

        public static uint Hash(int seed, string text, int salt)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)seed) * 16777619u;
                hash = (hash ^ (uint)salt) * 16777619u;
                if (text != null)
                {
                    for (int i = 0; i < text.Length; i++)
                        hash = (hash ^ text[i]) * 16777619u;
                }
                hash ^= hash >> 13;
                hash *= 0x5bd1e995u;
                hash ^= hash >> 15;
                return hash;
            }
        }
    }
}
