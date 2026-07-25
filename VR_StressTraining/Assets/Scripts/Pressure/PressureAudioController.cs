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

        private AudioSource _rumbleSource;
        private AudioSource _ceilingSource;
        private AudioSource _dustSource;
        private AudioSource _criticalSource;
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public bool HasAnyClip => rumbleClip != null || ceilingCreakClip != null ||
                                  dustBurstClip != null || criticalWarningClip != null;

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
            _initialized = true;
        }

        public void ConfigureClips(AudioClip rumble, AudioClip ceilingCreak,
            AudioClip dustBurst, AudioClip criticalWarning)
        {
            rumbleClip = rumble;
            ceilingCreakClip = ceilingCreak;
            dustBurstClip = dustBurst;
            criticalWarningClip = criticalWarning;
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
                PressureHeuristics.RumbleSourceVolumeCap);
        }

        public void PlayCeilingCreak(float magnitude, float levelAudioCap)
        {
            Play(_ceilingSource, ceilingCreakClip, magnitude, levelAudioCap,
                PressureHeuristics.CeilingSourceVolumeCap);
        }

        public void PlayDustBurst(float magnitude, float levelAudioCap)
        {
            Play(_dustSource, dustBurstClip, magnitude, levelAudioCap,
                PressureHeuristics.DustSourceVolumeCap);
        }

        public void PlayCriticalWarning(float levelAudioCap)
        {
            Play(_criticalSource, criticalWarningClip, 0.75f, levelAudioCap,
                PressureHeuristics.CriticalSourceVolumeCap);
        }

        private static void Play(AudioSource source, AudioClip clip, float magnitude,
            float levelAudioCap, float absoluteCap)
        {
            if (source == null || clip == null) return;
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
            Stop(_rumbleSource);
            Stop(_ceilingSource);
            Stop(_dustSource);
            Stop(_criticalSource);
        }

        private static void Stop(AudioSource source)
        {
            if (source == null) return;
            source.Stop();
            source.clip = null;
        }
    }
}
