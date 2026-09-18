using System;
using System.Collections.Generic;
using StressTraining.Core;
using UnityEngine;
using UnityEngine.UI;

namespace StressTraining.Audio
{
    /// <summary>Every standardized voice point (spec §37). Append-only.</summary>
    public enum VoiceCueId
    {
        Welcome = 0,
        ProfileSelected = 1,
        ScheduleWarning = 2,
        PreSSQ = 3,
        BaselineStart = 4,
        BaselineComplete = 5,
        CopingIntro = 6,
        BreathingPrompt = 7,
        TutorialIntro = 8,
        Ready = 9,
        SessionStart = 10,
        TaskTransitionNBack = 11,
        TaskTransitionGoNoGo = 12,
        TaskTransitionFlanker = 13,
        TaskTransitionCorsi = 14,
        PauseOpened = 15,
        ResumeCountdown = 16,
        TimeWarning70 = 17,
        TimeWarning50 = 18,
        TimeWarning30 = 19,
        TimeWarning10 = 20,
        GameOver = 21,
        SessionComplete = 22,
        RecoveryStart = 23,
        PostSSQ = 24,
        NasaTlx = 25,
        AdaptationExplanation = 26,
        Goodbye = 27
    }

    [Serializable]
    public sealed class VoiceCueConfig
    {
        public bool voiceEnabled = true;        // clips are optional; subtitles always work
        public bool subtitlesEnabled = true;
        public float voiceVolume = 1f;
        public float defaultSubtitleSeconds = 3.5f;   // PROJECT_HEURISTIC
        public string resourcesFolder = "VoiceCues";  // Resources/VoiceCues/<CueId>.wav
    }

    [Serializable]
    public sealed class VoiceCueDefinition
    {
        public VoiceCueId id;
        public string subtitleBcs;              // full sentences: VOICE_SCRIPT_BCS.md
        /// <summary>
        /// Recorded variants of this line, empty when none exist. Several readings of
        /// the same sentence keep a cue that fires every session from wearing out;
        /// one recording is still a perfectly valid set.
        /// </summary>
        public AudioClip[] clips = Array.Empty<AudioClip>();
        public bool clipLookupDone;

        [NonSerialized] private int _lastIndex = -1;

        public bool HasClip => clips != null && clips.Length > 0;

        /// <summary>
        /// One variant at random, never the same one twice in a row. With a single
        /// recording this always returns it; with none it returns null.
        /// </summary>
        public AudioClip PickClip()
        {
            if (clips == null || clips.Length == 0) return null;
            if (clips.Length == 1) return clips[0];
            int i = UnityEngine.Random.Range(0, clips.Length);
            if (i == _lastIndex) i = (i + 1) % clips.Length;
            _lastIndex = i;
            return clips[i];
        }
    }

    /// <summary>
    /// All cue subtitles in one place. A voice actor later records clips into
    /// Assets/Resources/VoiceCues/&lt;CueId&gt;.wav — NO code change required.
    /// </summary>
    public sealed class VoiceCueLibrary
    {
        private readonly Dictionary<VoiceCueId, VoiceCueDefinition> _cues =
            new Dictionary<VoiceCueId, VoiceCueDefinition>();
        private readonly string _resourcesFolder;

        public VoiceCueLibrary(string resourcesFolder)
        {
            _resourcesFolder = resourcesFolder;
            void Add(VoiceCueId id, string bcs) =>
                _cues[id] = new VoiceCueDefinition { id = id, subtitleBcs = bcs };

            // Wording rules for every line below (they are spoken aloud AND shown as
            // subtitles, so they must read naturally in both):
            //  • rodno neutralno — nikad "spreman", "došao", "efikasan/na";
            //  • bez internog žargona — učesnik ne zna šta je "blok", "budžet" ni "HR";
            //  • kratko, jedna misao po rečenici, bez podređenih konstrukcija;
            //  • formulacije moraju da se poklapaju sa tekstom na panelima
            //    (npr. disanje: "širi se dok udišeš" i ovdje i u BreathingPanel).
            Add(VoiceCueId.Welcome, "Zdravo. Za početak izaberi svoj profil.");
            Add(VoiceCueId.ProfileSelected, "Profil je spreman.");
            Add(VoiceCueId.ScheduleWarning, "Još nije vrijeme za narednu sesiju.");
            Add(VoiceCueId.PreSSQ, "Prvo kratak upitnik o tome kako se osjećaš.");
            Add(VoiceCueId.BaselineStart, "Sada mirno mjerenje pulsa. Samo miruj i diši prirodno.");
            Add(VoiceCueId.BaselineComplete, "Mjerenje je završeno. Hvala.");
            Add(VoiceCueId.CopingIntro, "Prije zadataka, kratka priprema.");
            Add(VoiceCueId.BreathingPrompt, "Prati krug — širi se dok udišeš, skuplja dok izdišeš.");
            Add(VoiceCueId.TutorialIntro, "Kratak podsjetnik na pravila.");
            Add(VoiceCueId.Ready, "Ulazimo u hodnik kad ti kažeš.");
            Add(VoiceCueId.SessionStart, "Počinjemo. Idi korak po korak, bez žurbe.");
            Add(VoiceCueId.TaskTransitionNBack, "Naredni zadatak: N-back.");
            Add(VoiceCueId.TaskTransitionGoNoGo, "Naredni zadatak: Go, No-Go.");
            Add(VoiceCueId.TaskTransitionFlanker, "Naredni zadatak: Flanker.");
            Add(VoiceCueId.TaskTransitionCorsi, "Naredni zadatak: sekvencijalna memorija.");
            Add(VoiceCueId.PauseOpened, "Pauza. Nastavi kad želiš.");
            Add(VoiceCueId.ResumeCountdown, "Nastavljamo za tri, dva, jedan.");
            Add(VoiceCueId.TimeWarning70, "Prošla je trećina vremena.");
            Add(VoiceCueId.TimeWarning50, "Prošla je polovina vremena.");
            Add(VoiceCueId.TimeWarning30, "Ostalo je manje od trećine vremena.");
            Add(VoiceCueId.TimeWarning10, "Vrijeme je pri kraju.");
            Add(VoiceCueId.GameOver, "Vrijeme je isteklo. Sesija se završava.");
            Add(VoiceCueId.SessionComplete, "Svi zadaci su završeni. Bravo.");
            Add(VoiceCueId.RecoveryStart, "Sada kratak odmor. Diši prirodno.");
            Add(VoiceCueId.PostSSQ, "Još nekoliko pitanja o tome kako se osjećaš.");
            Add(VoiceCueId.NasaTlx, "Ocijeni koliko je sesija bila zahtjevna.");
            Add(VoiceCueId.AdaptationExplanation, "Evo šta se mijenja za narednu sesiju.");
            Add(VoiceCueId.Goodbye, "To je sve za danas. Hvala i doviđenja.");
        }

        public VoiceCueDefinition Get(VoiceCueId id)
        {
            if (!_cues.TryGetValue(id, out var cue)) return null;
            if (!cue.clipLookupDone)
            {
                cue.clipLookupDone = true;
                cue.clips = LoadVariants(id);
            }
            return cue;
        }

        /// <summary>Highest variant suffix probed: &lt;CueId&gt;_1 … &lt;CueId&gt;_9.</summary>
        private const int MaxVariants = 9;

        /// <summary>
        /// Lazily collects every recording of one cue: the plain
        /// <c>&lt;CueId&gt;</c> file plus any numbered variants. Missing files are the
        /// normal case and never an error — a cue with no recording stays subtitle-only.
        /// Probing per cue (rather than Resources.LoadAll on the folder) keeps unplayed
        /// cues out of memory, which matters on the headset.
        /// </summary>
        private AudioClip[] LoadVariants(VoiceCueId id)
        {
            var found = new List<AudioClip>(MaxVariants + 1);
            var single = Resources.Load<AudioClip>(_resourcesFolder + "/" + id);
            if (single != null) found.Add(Ready(single));
            for (int n = 1; n <= MaxVariants; n++)
            {
                var v = Resources.Load<AudioClip>(_resourcesFolder + "/" + id + "_" + n);
                if (v != null) found.Add(Ready(v));
            }
            return found.Count == 0 ? Array.Empty<AudioClip>() : found.ToArray();
        }

        /// <summary>
        /// The importer leaves these clips with preloadAudioData off, so a freshly
        /// loaded clip sits in <see cref="AudioDataLoadState.Unloaded"/>. PlayOneShot
        /// on an unloaded clip reports success and produces no sound, with nothing in
        /// the console to show for it. Loading here costs one decode per cue, once.
        /// </summary>
        private static AudioClip Ready(AudioClip clip)
        {
            if (clip != null && clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();
            return clip;
        }
    }

    /// <summary>
    /// World-space subtitle line under the active UI anchor. Every cue works
    /// with subtitle only — audio clips are never a runtime dependency.
    /// </summary>
    public sealed class SubtitlePresenter : MonoBehaviour
    {
        private Text _text;
        private float _hideAt = -1f;

        public void Initialize(Transform canvasRoot)
        {
            if (_text != null || canvasRoot == null) return;
            var go = new GameObject("SubtitleLine", typeof(RectTransform));
            go.transform.SetParent(canvasRoot, false);
            _text = go.AddComponent<Text>();
            _text.font = RuntimeVisualUtil.BuiltinFont;
            _text.fontSize = 20;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.color = new Color(1f, 0.95f, 0.75f);
            _text.raycastTarget = false;
            var rt = _text.rectTransform;
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(1, 0);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-20, 34);
            rt.anchoredPosition = new Vector2(0, -6);
            _text.text = "";
        }

        public void Show(string line, float seconds)
        {
            if (_text == null) return;
            _text.text = line ?? "";
            _hideAt = Time.unscaledTime + Mathf.Max(1f, seconds);
        }

        private void Update()
        {
            if (_text == null || _hideAt < 0) return;
            if (Time.unscaledTime >= _hideAt)
            {
                _text.text = "";
                _hideAt = -1;
            }
        }
    }

    /// <summary>
    /// Plays a cue: optional clip + subtitle. Missing clips are silent, never an
    /// error (spec §37). During active trials the callers use cues minimally so
    /// speech never becomes an additional cognitive stimulus.
    /// </summary>
    public sealed class VoiceCueManager : MonoBehaviour
    {
        public VoiceCueConfig Config { get; private set; } = new VoiceCueConfig();
        private VoiceCueLibrary _library;
        private SubtitlePresenter _subtitles;
        private AudioSource _source;

        public void Initialize(VoiceCueConfig config, SubtitlePresenter subtitles)
        {
            Config = config ?? new VoiceCueConfig();
            _library = new VoiceCueLibrary(Config.resourcesFolder);
            _subtitles = subtitles;
            _source = gameObject.GetComponent<AudioSource>();
            if (_source == null) _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;   // narration is non-diegetic
        }

        /// <summary>
        /// How long after load a cue is held back instead of played. The welcome line
        /// fires from Start(), on the very first frame, and on the headset the audio
        /// output is not running yet at that point: the engine accepts the clip and
        /// reports isPlaying, but nothing is ever heard and nothing is logged. Holding
        /// the cue for a moment is the difference between a greeting and silence.
        /// PROJECT_HEURISTIC.
        /// </summary>
        private const float WarmupSeconds = 1.5f;

        private bool _warm;
        private bool _hasPending;
        private VoiceCueId _pending;

        private void Update()
        {
            if (_warm || Time.realtimeSinceStartup < WarmupSeconds) return;
            _warm = true;
            if (!_hasPending) return;
            _hasPending = false;
            Play(_pending);
        }

        public void Play(VoiceCueId id)
        {
            // Only the most recent cue is kept: warm-up spans a frame or two, so a
            // backlog would mean replaying lines the participant has moved past.
            if (!_warm && Time.realtimeSinceStartup < WarmupSeconds)
            {
                _pending = id;
                _hasPending = true;
                return;
            }
            _warm = true;

            var cue = _library?.Get(id);
            if (cue == null) return;

            // Pick ONCE: the subtitle must stay on screen for the length of the take
            // that is actually playing, not a different randomly chosen variant.
            AudioClip clip = cue.PickClip();

            if (Config.voiceEnabled && clip != null && _source != null)
            {
                // A narrator has one mouth. PlayOneShot layers instead of replacing,
                // so two cues fired close together used to speak over each other —
                // the participant then hears two sentences at once and understands
                // neither. Assigning the clip and calling Play cuts the previous line.
                bool interrupted = _source.isPlaying;
                _source.Stop();
                _source.clip = clip;
                _source.volume = Config.voiceVolume;
                _source.Play();
                Core.FlowTrace.Log("Voice", id + " → " + clip.name +
                    (interrupted ? "  (prekinuo prethodni)" : ""));
            }
            else
            {
                Core.FlowTrace.Log("Voice", id + " → bez snimka (samo titl)");
            }

            if (Config.subtitlesEnabled && !string.IsNullOrEmpty(cue.subtitleBcs))
            {
                float seconds = clip != null
                    ? Mathf.Max(Config.defaultSubtitleSeconds, clip.length)
                    : Config.defaultSubtitleSeconds;
                _subtitles?.Show(cue.subtitleBcs, seconds);
            }
        }
    }
}
