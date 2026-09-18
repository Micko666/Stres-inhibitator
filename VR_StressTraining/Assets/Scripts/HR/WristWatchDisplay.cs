using StressTraining.Core;
using StressTraining.Data;
using UnityEngine;

namespace StressTraining.HR
{
    /// <summary>
    /// Primitive-based wrist-watch placeholder on the LEFT controller anchor
    /// (spec §36). No Blender model required (MANUAL_ASSET_TASKS §3).
    ///
    /// Participant view: connection state ("Sat nije povezan" / "Povezivanje" /
    /// "Povezan") + the discrete zone (Stabilno / Povišeno / Visoko / Signal
    /// izgubljen). RAW BPM IS NEVER SHOWN during tasks. Developer mode adds BPM
    /// and the source type; simulated data is always labelled "SIMULIRANI PODACI"
    /// and never presented as a real measurement.
    /// </summary>
    public sealed class WristWatchDisplay : MonoBehaviour
    {
        public const string RootName = "WristWatchRoot";

        [SerializeField] private float refreshSeconds = 0.5f;   // no per-frame text rebuilds

        private HeartRateService _heartRate;
        private bool _developerMode;
        // Returns true only while numeric BPM may be shown to the participant
        // (baseline measurement phase). Null = never show the raw number.
        private System.Func<bool> _showNumericBpm;
        private TextMesh _statusText;
        private TextMesh _devText;
        private Renderer _zoneDot;
        private float _nextRefresh;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>True when the controller anchor was missing and the watch attached to the fallback.</summary>
        public bool UsedFallbackAnchor { get; private set; }
        /// <summary>Participant-visible line (tests assert it never carries raw BPM).</summary>
        public string StatusLine => _statusText != null ? _statusText.text : "";
        /// <summary>Developer-only line ("" outside developer mode).</summary>
        public string DevLine => _devText != null ? _devText.text : "";

        /// <summary>
        /// Reuses the scene's existing watch instead of spawning a second one on a
        /// repeated bootstrap; Initialize itself is idempotent (childCount guard).
        /// </summary>
        public static WristWatchDisplay CreateOrFind(Transform preferredAnchor,
            Transform fallbackAnchor, HeartRateService heartRate, bool developerMode,
            System.Func<bool> showNumericBpm = null)
        {
            WristWatchDisplay watch = null;
            foreach (var w in Resources.FindObjectsOfTypeAll<WristWatchDisplay>())
            {
                if (w != null && w.gameObject.scene.IsValid()) { watch = w; break; }
            }
            if (watch == null)
                watch = new GameObject(RootName).AddComponent<WristWatchDisplay>();
            watch.Initialize(preferredAnchor, fallbackAnchor, heartRate, developerMode, showNumericBpm);
            return watch;
        }

        public void Initialize(Transform leftControllerAnchor, Transform fallbackAnchor,
            HeartRateService heartRate, bool developerMode,
            System.Func<bool> showNumericBpm = null)
        {
            _heartRate = heartRate;
            _developerMode = developerMode;
            _showNumericBpm = showNumericBpm;
            if (transform.childCount > 0) return;   // already built (repeated bootstrap)

            // Missing controller anchor must never abort boot: fall back to the
            // rig root (watch renders static there — harmless, still honest).
            Transform anchor = leftControllerAnchor != null ? leftControllerAnchor : fallbackAnchor;
            UsedFallbackAnchor = leftControllerAnchor == null && fallbackAnchor != null;
            if (anchor == null) return;             // headless/test setup without any rig

            transform.SetParent(anchor, false);
            transform.localPosition = new Vector3(0f, 0.012f, -0.065f); // wrist side of the grip
            transform.localRotation = Quaternion.identity;

            RuntimeVisualUtil.Primitive(PrimitiveType.Cube, "WatchBody", transform,
                Vector3.zero, new Vector3(0.045f, 0.012f, 0.035f),
                RuntimeVisualUtil.Lit(new Color(0.08f, 0.08f, 0.10f), 0.3f, 0.6f));
            RuntimeVisualUtil.Primitive(PrimitiveType.Cube, "WatchFace", transform,
                new Vector3(0f, 0.007f, 0f), new Vector3(0.040f, 0.002f, 0.030f),
                RuntimeVisualUtil.Lit(new Color(0.02f, 0.02f, 0.03f), 0.1f, 0.9f));

            _zoneDot = RuntimeVisualUtil.Primitive(PrimitiveType.Sphere, "ZoneDot", transform,
                new Vector3(0.014f, 0.009f, 0.010f), Vector3.one * 0.006f,
                RuntimeVisualUtil.Lit(Color.gray)).GetComponent<Renderer>();

            _statusText = RuntimeVisualUtil.Label("Sat nije povezan", transform,
                new Vector3(0f, 0.010f, 0f), 0.0035f, new Color(0.85f, 0.88f, 0.92f));
            _statusText.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            if (_developerMode)
            {
                // Developer-only transport diagnostics sit beside the watch face.
                // Participant mode never creates this text object.
                _devText = RuntimeVisualUtil.Label("", transform,
                    new Vector3(0.032f, 0.012f, -0.020f), 0.0018f, new Color(0.6f, 0.75f, 0.6f));
                _devText.anchor = TextAnchor.UpperLeft;
                _devText.alignment = TextAlignment.Left;
                _devText.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + refreshSeconds;
            RefreshNow();
        }

        /// <summary>Recomputes both text lines. Public so tests drive it without a play loop.</summary>
        public void RefreshNow()
        {
            if (_heartRate == null || _statusText == null) return;

            string connection;
            switch (_heartRate.Mode)
            {
                case HeartRateMode.Disconnected:
                    connection = "Sat nije povezan"; break;
                case HeartRateMode.NativeAdbPlaceholder:
                case HeartRateMode.Simulated:
                    connection = "Sat nije povezan"; break;   // placeholders/simulation never claim a participant link
                default:
                    connection = _heartRate.IsReceiving ? "Povezan" : "Povezivanje"; break;
            }

            // Only a real external link (Network) may show a zone to the
            // participant. Simulated / placeholder / disconnected NEVER present a
            // zone — simulated data must not read as a real measurement, so the
            // participant line stays exactly "Sat nije povezan" (spec §8/§36).
            bool participantLink = _heartRate.Mode == HeartRateMode.Network;

            string zoneLabel;
            Color zoneColor;
            if (!participantLink)
            {
                zoneLabel = "";
                zoneColor = Color.gray;
            }
            else if (!_heartRate.IsReceiving)
            {
                zoneLabel = "Signal izgubljen";
                zoneColor = Color.gray;
            }
            else
            {
                switch (_heartRate.CurrentZone)
                {
                    case HrZone.Elevated: zoneLabel = "Povišeno"; zoneColor = new Color(0.95f, 0.75f, 0.2f); break;
                    case HrZone.High: zoneLabel = "Visoko"; zoneColor = new Color(0.9f, 0.3f, 0.2f); break;
                    case HrZone.Stable: zoneLabel = "Stabilno"; zoneColor = new Color(0.25f, 0.75f, 0.35f); break;
                    default: zoneLabel = "Signal izgubljen"; zoneColor = Color.gray; break;
                }
            }

            // During the baseline measurement phase the exact BPM is shown so the
            // sensor reading can be confirmed; during tasks it is hidden again so
            // the raw number never becomes a stressor (participant request).
            bool numericAllowed = _showNumericBpm != null && _showNumericBpm();
            string participantSuffix = ParticipantSuffix(participantLink,
                _heartRate.IsReceiving, _heartRate.CurrentBpm, zoneLabel, numericAllowed);

            _statusText.text = string.IsNullOrEmpty(participantSuffix)
                ? connection : connection + " · " + participantSuffix;
            // sharedMaterial: Primitive() already assigned a unique instance, so
            // this mutates no asset and avoids renderer.material clones in tests.
            if (_zoneDot != null && _zoneDot.sharedMaterial != null &&
                _zoneDot.sharedMaterial.HasProperty(BaseColorId))
                _zoneDot.sharedMaterial.SetColor(BaseColorId, zoneColor);

            if (_devText != null)
            {
                bool simulated = _heartRate.ActiveSource?.SourceType == HrSourceType.Simulated;
                string headline = _heartRate.CurrentBpm > 0
                    ? $"{_heartRate.CurrentBpm} bpm · {_heartRate.Mode}" +
                      (simulated ? " · SIMULIRANI PODACI" : "")
                    : _heartRate.Mode.ToString();

                if (_heartRate.ActiveSource is NetworkHeartRateSource network)
                    _devText.text = headline + "\n" +
                        network.BuildDeveloperDiagnostics(Time.realtimeSinceStartupAsDouble);
                else
                    _devText.text = headline;
            }
        }

        /// <summary>
        /// Pure decision for the participant status suffix. The raw BPM is shown
        /// only for a real link (participantLink) that is receiving a plausible
        /// value AND numericAllowed (baseline phase); otherwise the discrete zone.
        /// </summary>
        public static string ParticipantSuffix(bool participantLink, bool receiving,
            int bpm, string zoneLabel, bool numericAllowed)
        {
            if (participantLink && receiving && bpm > 0 && numericAllowed)
                return bpm + " bpm";
            return zoneLabel;
        }
    }
}
