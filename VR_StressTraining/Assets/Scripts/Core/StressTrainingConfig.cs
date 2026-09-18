using System;

namespace StressTraining.Core
{
    // =========================================================================
    //  CENTRAL CONFIGURATION
    //
    //  Every value marked PROJECT_HEURISTIC is a project working value chosen
    //  for this MVP prototype. These values are NOT claimed to be clinically or
    //  psychometrically validated. They are centralized here (and overridable
    //  through StressTrainingData/config/config.json) so a researcher can tune
    //  them without touching code. See docs/TASKS_AND_DIFFICULTY.md and
    //  docs/ADAPTATION_RULES.md.
    // =========================================================================

    [Serializable]
    public sealed class SessionConfig
    {
        public int defaultSessionIntervalHours = 48;      // study design default; per-profile overridable
        public int plannedCycleSessionCount = 5;          // PROJECT_HEURISTIC — sessions per training cycle
        public int blocksPerTaskFull = 3;                 // PROJECT_HEURISTIC — 3 selected tasks × 3 blocks = 9
        public int blocksPerTaskDemo = 1;                 // short demo plan
        public float breakBetweenBlocksSeconds = 12f;     // PROJECT_HEURISTIC — inter-block rest on tablet
        public float resumeCountdownSeconds = 3f;         // 3-2-1 after Continue
        public float transitionToCorridorSeconds = 4f;    // PROJECT_HEURISTIC — fade/walk-in time

        // ── Production session (PHASE 8.6) ─────────────────────────────
        // The global challenge budget is estimated from the concrete 9-block
        // plan. These PROJECT_HEURISTIC multipliers are global difficulty timing
        // values; pressure effects still do not multiply session duration.
        public float globalDifficultyTimeMultiplierLevel1 = 1.20f;
        public float globalDifficultyTimeMultiplierLevel2 = 1.00f;
        public float globalDifficultyTimeMultiplierLevel3 = 0.85f;
        public int productionBlocksPerSelectedTask = 3;       // PROJECT_HEURISTIC — 3 × 3 = 9 blocks
        public bool taskPoolNBackEnabled = true;
        public bool taskPoolGoNoGoEnabled = true;
        public bool taskPoolFlankerEnabled = true;
        public bool taskPoolCorsiEnabled = true;
    }

    [Serializable]
    public sealed class BaselineConfig
    {
        public float durationSeconds = 300f;              // PROJECT_HEURISTIC — production resting baseline: 5 min
        public int minValidSamples = 10;                  // PROJECT_HEURISTIC — below this quality = Low
        public float minValidSampleRatio = 0.5f;          // PROJECT_HEURISTIC
        public float maxSignalGapSeconds = 15f;           // PROJECT_HEURISTIC — counts as a gap beyond this
        public bool allowContinueWithoutHrInDevMode = false; // production never bypasses HR quality
    }

    [Serializable]
    public sealed class RecoveryConfig
    {
        public float durationSeconds = 90f;               // PROJECT_HEURISTIC — post-session quiet measurement
        public float recoveryZoneDeltaBpm = 8f;           // PROJECT_HEURISTIC — "returned to working zone" = baseline + this
    }

    [Serializable]
    public sealed class HrZoneConfig
    {
        // Zones are relative to the per-session baseline average, never absolute BPM.
        public float elevatedDeltaBpm = 10f;              // PROJECT_HEURISTIC — baseline+10 → Elevated
        public float highDeltaBpm = 22f;                  // PROJECT_HEURISTIC — baseline+22 → High
        public float staleSignalSeconds = 8f;             // PROJECT_HEURISTIC — no packet for this long → SignalLost
        public int minPlausibleBpm = 30;
        public int maxPlausibleBpm = 220;
    }

    [Serializable]
    public sealed class BreathingGuidanceConfig
    {
        // PROJECT_HEURISTIC — these are project configuration values for a calm,
        // unforced breathing visual. They are NOT presented to the user as a
        // medically validated protocol (see docs/SESSION_FLOW.md, CopingTraining).
        public float inhaleSeconds = 4f;
        public float holdAfterInhaleSeconds = 0f;
        public float exhaleSeconds = 6f;
        public float holdAfterExhaleSeconds = 0f;
        public float totalDurationSecondsFirstSession = 120f;
        public float totalDurationSecondsReminder = 30f;
    }

    [Serializable]
    public sealed class TimerPenaltyConfig
    {
        // ENTIRE FEATURE DISABLED BY DEFAULT until a final design decision is
        // confirmed (spec §25). Tasks only emit standardized error events; the
        // session/pressure layer decides about penalties using this config.
        public bool timePenaltyOnErrorEnabled = false;
        public float penaltySecondsPerError = 2f;         // PROJECT_HEURISTIC — legacy generic value

        // Per-error-type penalties (seconds). PROJECT_HEURISTIC placeholders.
        public float penaltySecondsCommission = 2f;
        public float penaltySecondsOmission = 1f;
        public float penaltySecondsIncorrect = 1f;

        // Per-task multipliers (index by TaskType name for readability).
        public float multiplierNBack = 1f;
        public float multiplierGoNoGo = 1f;
        public float multiplierFlanker = 1f;
        public float multiplierCorsi = 1f;

        // Global pressure-level multiplier (1..3 → index 0..2).
        public float pressureLevel1Multiplier = 1f;
        public float pressureLevel2Multiplier = 1f;
        public float pressureLevel3Multiplier = 1f;

        // A penalty can never push the global timer below this remainder.
        public float minRemainingSeconds = 30f;           // PROJECT_HEURISTIC
    }

    [Serializable]
    public sealed class NetworkConfig
    {
        public int hrUdpListenPort = 5005;
        public string hrUdpBindAddress = "0.0.0.0";       // any interface — required for standalone Quest
    }

    /// <summary>
    /// What happens when the participant ends up over a hole (a collapsed segment)
    /// or the global timer expires. Whatever the mode, the loss is REGISTERED at 0:00
    /// (measurement stops); the mode only chooses how the consequence looks, and the
    /// consequence lingers for developer.lossConsequenceHoldSeconds before the
    /// participant is returned to the Safe Space — so it is felt, not an instant snap.
    ///
    /// • COMFORT FADE — default, and the only one safe for a measured session: the
    ///   view fades out in place. A real drop moves the camera without the body moving,
    ///   one of the strongest simulator-sickness triggers in VR — and this study
    ///   measures SSQ after every session, so a scripted fall would contaminate the
    ///   very instrument it is measured with (and the HR response with it).
    /// • REAL FALL — the rig drops (capped) then fades. Demo/preview and art tuning.
    /// • DEVIRTUALIZE — the room and the participant "de-render" (a Code-Lyoko-style
    ///   digital dissolve) instead of anyone physically falling. The intended
    ///   long-term consequence once an avatar body exists: it reads as a real loss
    ///   with NO camera translation, so it stays measurement-safe like the fade.
    /// </summary>
    public enum FallMode
    {
        ComfortFade = 0,
        RealFall = 1,
        Devirtualize = 2
    }

    [Serializable]
    public sealed class DeveloperConfig
    {
        public bool developerModeEnabled = true;          // MVP default; disable for real study sessions

        // ── Falling / loss ────────────────────────────────────────────────
        public FallMode fallMode = FallMode.ComfortFade;
        public float fallMaxDistanceMeters = 3f;   // hard stop — never fall forever
        public float groundCheckDistanceMeters = 0.6f;
        public bool fallThroughMissingFloorEnabled = true;     // real consequence for standing over a hole
        // How long the fully-applied consequence lingers before the return-to-safe
        // teleport, so a loss is felt instead of snapping away instantly.
        public float lossConsequenceHoldSeconds = 1.6f;
        public bool bypassSessionInterval = false;        // every bypass is logged to the event stream
        public bool useSimulatedHeartRate = false;        // test-only source; never an automatic production fallback
        public float verticalSliceBaselineSeconds = 5f;   // PROJECT_HEURISTIC - short Editor demo
        public int verticalSliceNBackTrials = 8;          // PROJECT_HEURISTIC - scoreable demo trials
        public int verticalSliceGoNoGoTrials = 10;        // PROJECT_HEURISTIC - short demo block
        public int verticalSliceFlankerTrials = 10;       // PROJECT_HEURISTIC - short demo block
        public float verticalSliceBlockCountdownSeconds = 3f;
        public int verticalSliceSeed = 12072026;          // fixed, persisted demo seed

        // ── Production developer overrides ────────────────────────────────
        public int forcedProductionMasterSeed = 0;        // 0 = random; non-zero = reproducible session

        // THE SESSION CONDITION SWITCH. false → SessionCondition.Neutral: the corridor
        // never collapses, lights never ramp, and reaching 0:00 shows no finale — that
        // is CORRECT for a neutral control session, not a bug. true → ControlledPressure:
        // PressureController arms at corridor entry and drives 70/50/30/10/0.
        // The pressure system has been complete since FAZA 7; this flag is the only
        // thing that decides whether a session sees it.
        public bool productionUsePressureCondition = false;

        public float productionDevBaselineSeconds = 8f;   // short dev baseline (real config in baseline.durationSeconds)
        public bool useShortDevBaseline = false;          // explicit developer-only, real-HR test mode
        public float productionDevRecoverySeconds = 6f;   // short dev recovery
        public bool useShortDevRecovery = false;
    }

    /// <summary>Master serializable configuration. Loaded/overridden by <see cref="ConfigService"/>.</summary>
    [Serializable]
    public sealed class StressTrainingConfig
    {
        public int schemaVersion = 1;
        public string configVersion = "mvp-1";
        public SessionConfig session = new SessionConfig();
        public BaselineConfig baseline = new BaselineConfig();
        public RecoveryConfig recovery = new RecoveryConfig();
        public HrZoneConfig hrZones = new HrZoneConfig();
        public BreathingGuidanceConfig breathing = new BreathingGuidanceConfig();
        public TimerPenaltyConfig timerPenalty = new TimerPenaltyConfig();
        public NetworkConfig network = new NetworkConfig();
        public DeveloperConfig developer = new DeveloperConfig();
    }
}
