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
        public AudioClip clip;                  // optional; loaded lazily from Resources
        public bool clipLookupDone;
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

            Add(VoiceCueId.Welcome, "Dobro došao. Izaberi svoj profil da počnemo.");
            Add(VoiceCueId.ProfileSelected, "Profil je učitan.");
            Add(VoiceCueId.ScheduleWarning, "Preporučeni termin još nije stigao.");
            Add(VoiceCueId.PreSSQ, "Prvo kratki upitnik o tome kako se osjećaš.");
            Add(VoiceCueId.BaselineStart, "Slijedi mirno mjerenje pulsa. Samo miruj i prirodno diši.");
            Add(VoiceCueId.BaselineComplete, "Mjerenje je završeno. Hvala.");
            Add(VoiceCueId.CopingIntro, "Prije zadataka, kratka priprema.");
            Add(VoiceCueId.BreathingPrompt, "Prati krug: udahni dok se širi, izdahni dok se skuplja.");
            Add(VoiceCueId.TutorialIntro, "Evo kratkog podsjetnika pravila.");
            Add(VoiceCueId.Ready, "Kada budeš spreman, ulazimo u hodnik.");
            Add(VoiceCueId.SessionStart, "Sesija počinje. Radi redom, korak po korak.");
            Add(VoiceCueId.TaskTransitionNBack, "Sljedeći zadatak: N-back.");
            Add(VoiceCueId.TaskTransitionGoNoGo, "Sljedeći zadatak: Go, No-Go.");
            Add(VoiceCueId.TaskTransitionFlanker, "Sljedeći zadatak: Flanker.");
            Add(VoiceCueId.TaskTransitionCorsi, "Sljedeći zadatak: sekvencijalna memorija.");
            Add(VoiceCueId.PauseOpened, "Pauza. Nastavi kada budeš spreman.");
            Add(VoiceCueId.ResumeCountdown, "Nastavljamo za tri, dva, jedan.");
            Add(VoiceCueId.TimeWarning70, "Prošla je trećina vremena.");
            Add(VoiceCueId.TimeWarning50, "Pola vremena je prošlo.");
            Add(VoiceCueId.TimeWarning30, "Ostalo je manje od trećine vremena.");
            Add(VoiceCueId.TimeWarning10, "Vrijeme skoro ističe.");
            Add(VoiceCueId.GameOver, "Vrijeme je isteklo. Sesija se završava.");
            Add(VoiceCueId.SessionComplete, "Svi blokovi su završeni. Odlično.");
            Add(VoiceCueId.RecoveryStart, "Sada kratko mirovanje. Prirodno diši.");
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
                // One lazy Resources lookup per cue; a missing clip is normal.
                cue.clip = Resources.Load<AudioClip>(_resourcesFolder + "/" + id);
            }
            return cue;
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

        public void Play(VoiceCueId id)
        {
            var cue = _library?.Get(id);
            if (cue == null) return;

            if (Config.voiceEnabled && cue.clip != null && _source != null)
                _source.PlayOneShot(cue.clip, Config.voiceVolume);

            if (Config.subtitlesEnabled && !string.IsNullOrEmpty(cue.subtitleBcs))
            {
                float seconds = cue.clip != null
                    ? Mathf.Max(Config.defaultSubtitleSeconds, cue.clip.length)
                    : Config.defaultSubtitleSeconds;
                _subtitles?.Show(cue.subtitleBcs, seconds);
            }
        }
    }
}
