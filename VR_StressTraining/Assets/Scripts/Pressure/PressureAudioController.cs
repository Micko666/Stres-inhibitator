using UnityEngine;

namespace StressTraining.Pressure
{
    /// <summary>
    /// Optional pressure audio layer. Missing clips are valid and produce no error.
    /// Sources are created once and reused; no audio asset is required by the patch.
    /// </summary>
    public sealed class PressureAudioController : MonoBehaviour
    {
        [SerializeField] private AudioClip rumbleClip;
        [SerializeField] private AudioClip ceilingCreakClip;
        [SerializeField] private AudioClip dustBurstClip;
        [SerializeField] private AudioClip criticalWarningClip;
        [SerializeField] private AudioClip ambienceClip;

        private AudioSource _rumbleSource;
        private AudioSource _ceilingSource;
        private AudioSource _dustSource;
        private AudioSource _criticalSource;
        private AudioSource _ambienceSource;
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public bool HasAnyClip => rumbleClip != null || ceilingCreakClip != null ||
                                  dustBurstClip != null || criticalWarningClip != null ||
                                  ambienceClip != null;

        /// <summary>Loudest the ambience bed may ever get. PROJECT_HEURISTIC.</summary>
        public const float AmbienceVolumeCap = 0.18f;
        /// <summary>Share of the cap present at Stable, before any pressure builds.</summary>
        public const float AmbienceFloorShare = 0.35f;

        /// <summary>
        /// Shortest gap allowed between two plays of the SAME cue. Each cue owns one
        /// source and playing it restarts it, so during the collapse — where
        /// per-segment events arrive far faster than a clip lasts — every new trigger
        /// cut the previous sound off part-way and a creak became a stutter. Inside
        /// this window the newer trigger is dropped and the sound already playing is
        /// allowed to finish. Scheduled pressure events are seconds apart and are
        /// never affected. PROJECT_HEURISTIC.
        /// </summary>
        public const float MinRetriggerSeconds = 0.12f;

        private float _rumbleLastPlayed = float.NegativeInfinity;
        private float _ceilingLastPlayed = float.NegativeInfinity;
        private float _dustLastPlayed = float.NegativeInfinity;
        private float _criticalLastPlayed = float.NegativeInfinity;

        public void Initialize()
        {
            if (_initialized) return;
            _rumbleSource = CreateSource("PressureAudio_Rumble",
                PressureHeuristics.RumbleSourceVolumeCap);
            _ceilingSource = CreateSource("PressureAudio_Ceiling",
                PressureHeuristics.CeilingSourceVolumeCap);
            _dustSource = CreateSource("PressureAudio_Dust",
                PressureHeuristics.DustSourceVolumeCap);
            _criticalSource = CreateSource("PressureAudio_Critical",
                PressureHeuristics.CriticalSourceVolumeCap);
            _ambienceSource = CreateSource("PressureAudio_Ambience", AmbienceVolumeCap);
            // The bed belongs to the room, not to a point in it: spatialising it would
            // make the corridor sound like it has a humming object at one end.
            _ambienceSource.spatialBlend = 0f;
            _ambienceSource.loop = true;
            _initialized = true;
        }

        public void ConfigureClips(AudioClip rumble, AudioClip ceilingCreak,
            AudioClip dustBurst, AudioClip criticalWarning, AudioClip ambience = null)
        {
            rumbleClip = rumble;
            ceilingCreakClip = ceilingCreak;
            dustBurstClip = dustBurst;
            criticalWarningClip = criticalWarning;
            ambienceClip = ambience;
        }

        /// <summary>
        /// Starts the looping bed. Safe to call repeatedly; does nothing without a clip,
        /// so a project with no ambience file behaves exactly as before.
        /// </summary>
        public void StartAmbience(float levelAudioCap)
        {
            if (_ambienceSource == null || ambienceClip == null) return;
            if (_ambienceSource.clip != ambienceClip)
            {
                _ambienceSource.clip = ambienceClip;
                _ambienceSource.time = 0f;
            }
            SetAmbienceIntensity(0f, levelAudioCap);
            if (!_ambienceSource.isPlaying) _ambienceSource.Play();
        }

        /// <summary>
        /// Tracks pressure intensity. The bed is audible from the start (a corridor with
        /// no room tone reads as empty) and swells toward the level's audio cap, so the
        /// stages have something continuous to ride on rather than only punctuation.
        /// </summary>
        public void SetAmbienceIntensity(float intensity01, float levelAudioCap)
        {
            if (_ambienceSource == null) return;
            float share = AmbienceFloorShare +
                          (1f - AmbienceFloorShare) * Mathf.Clamp01(intensity01);
            _ambienceSource.volume = AmbienceVolumeCap * share * Mathf.Clamp01(levelAudioCap);
        }

        private AudioSource CreateSource(string objectName, float maxVolume)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0.85f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 0.8f;
            source.maxDistance = 8f;
            source.volume = maxVolume;
            return source;
        }

        public void PlayRumble(float magnitude, float levelAudioCap)
        {
            Play(_rumbleSource, rumbleClip, magnitude, levelAudioCap,
                PressureHeuristics.RumbleSourceVolumeCap, ref _rumbleLastPlayed);
        }

        public void PlayCeilingCreak(float magnitude, float levelAudioCap)
        {
            Play(_ceilingSource, ceilingCreakClip, magnitude, levelAudioCap,
                PressureHeuristics.CeilingSourceVolumeCap, ref _ceilingLastPlayed);
        }

        public void PlayDustBurst(float magnitude, float levelAudioCap)
        {
            Play(_dustSource, dustBurstClip, magnitude, levelAudioCap,
                PressureHeuristics.DustSourceVolumeCap, ref _dustLastPlayed);
        }

        public void PlayCriticalWarning(float levelAudioCap)
        {
            Play(_criticalSource, criticalWarningClip, 0.75f, levelAudioCap,
                PressureHeuristics.CriticalSourceVolumeCap, ref _criticalLastPlayed);
        }

        private static void Play(AudioSource source, AudioClip clip, float magnitude,
            float levelAudioCap, float absoluteCap, ref float lastPlayed)
        {
            if (source == null || clip == null) return;
            float now = Time.unscaledTime;
            if (now - lastPlayed < MinRetriggerSeconds) return;
            lastPlayed = now;
            source.Stop();
            source.clip = clip;
            source.volume = Mathf.Min(absoluteCap,
                absoluteCap * Mathf.Clamp01(magnitude) * Mathf.Clamp01(levelAudioCap));
            source.Play();
        }

        public void SetPaused(bool paused)
        {
            SetSourcePaused(_rumbleSource, paused);
            SetSourcePaused(_ceilingSource, paused);
            SetSourcePaused(_dustSource, paused);
            SetSourcePaused(_criticalSource, paused);
            SetSourcePaused(_ambienceSource, paused);
        }

        private static void SetSourcePaused(AudioSource source, bool paused)
        {
            if (source == null) return;
            if (paused)
            {
                if (source.isPlaying) source.Pause();
            }
            else
            {
                source.UnPause();
            }
        }

        public void ResetAudio()
        {
            _rumbleLastPlayed = float.NegativeInfinity;
            _ceilingLastPlayed = float.NegativeInfinity;
            _dustLastPlayed = float.NegativeInfinity;
            _criticalLastPlayed = float.NegativeInfinity;
            Stop(_rumbleSource);
            Stop(_ceilingSource);
            Stop(_dustSource);
            Stop(_criticalSource);
            Stop(_ambienceSource);
        }

        private static void Stop(AudioSource source)
        {
            if (source == null) return;
            source.Stop();
            source.clip = null;
        }
    }
}
