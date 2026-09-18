using UnityEngine;

namespace StressTraining.Core
{
    /// <summary>
    /// One-line trace of what the session flow is actually doing.
    ///
    /// Until this existed, a standalone build logged nothing between
    /// "Vertical slice initialized" and the end of a session, so a participant
    /// reporting "it threw me out immediately" left nothing behind to read: the
    /// JSONL writers buffer and only flush when a session closes cleanly, which is
    /// exactly the case that fails. Every line here goes to logcat immediately.
    ///
    /// Kept deliberately dumb — a prefix and a string. It must never allocate
    /// per-frame work or change behaviour, only report it.
    /// </summary>
    public static class FlowTrace
    {
        /// <summary>
        /// Off turns every call into a no-op. Left ON by default: these lines are
        /// rare (stage changes, zone changes, losses, voice cues), and the cost of
        /// not having them is a debugging session that cannot start.
        /// </summary>
        public static bool Enabled = true;

        public static void Log(string area, string message)
        {
            if (!Enabled) return;
            Debug.Log("[Flow] " + area + " · " + message);
        }

        public static void Warn(string area, string message)
        {
            if (!Enabled) return;
            Debug.LogWarning("[Flow] " + area + " · " + message);
        }

        /// <summary>
        /// Reports anything drawing with Unity's error shader — the magenta that means
        /// a material's shader was not included in this build. It is invisible in the
        /// Editor, where every shader is available, so the device has to report it
        /// itself. Runs once at startup and walks the scene, so it must not be called
        /// per frame.
        /// </summary>
        public static void ReportMagentaRenderers()
        {
            if (!Enabled) return;

            int found = 0;
            var renderers = Object.FindObjectsByType<Renderer>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var r in renderers)
            {
                var mats = r.sharedMaterials;
                if (mats == null) continue;
                foreach (var m in mats)
                {
                    bool broken = m == null || m.shader == null ||
                                  m.shader.name.Contains("InternalErrorShader") ||
                                  !m.shader.isSupported;
                    if (!broken) continue;
                    found++;
                    Warn("Shader", "magenta na \"" + Path(r.transform) + "\" @ " +
                        r.transform.position.ToString("F2") + "  materijal=" +
                        (m == null ? "null" : m.name) + "  shader=" +
                        (m == null || m.shader == null ? "null" : m.shader.name));
                    break;
                }
            }
            Log("Shader", "provjera završena, problematičnih renderera: " + found);
        }

        private static string Path(Transform t)
        {
            var sb = new System.Text.StringBuilder(t.name);
            while (t.parent != null) { t = t.parent; sb.Insert(0, t.name + "/"); }
            return sb.ToString();
        }
    }
}
