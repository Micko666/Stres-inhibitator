namespace StressTraining.Data
{
    // NOTE on persistence: JsonUtility serializes enums as integers.
    // Numeric values are therefore part of the on-disk schema — NEVER reorder
    // members of these enums; only append. See docs/DATA_SCHEMA.md.

    public enum SessionCondition
    {
        Neutral = 0,
        Pressure = 1
    }

    public enum TaskType
    {
        None = 0,
        NBack = 1,
        GoNoGo = 2,
        Flanker = 3,
        // Corsi-inspired sequential spatial reproduction task (participant UI
        // name: "Sekvencijalna memorija"). Appended for schema v2 — never reorder.
        CorsiSequence = 4
    }

    public enum CompletionStatus
    {
        Unknown = 0,
        Completed = 1,          // full plan finished
        UserTerminated = 2,     // ended via pause menu End Session
        SystemTerminated = 3,   // ended by system-detected failure
        Abandoned = 4           // app closed/crashed mid-session (detected on next load)
    }

    /// <summary>User-selected end reason (spec §16). Stored separately from system-detected causes.</summary>
    public enum UserTerminationReason
    {
        None = 0,
        UserRequested = 1,
        UserDiscomfort = 2,
        SimulatorSickness = 3,
        TaskUnclear = 4,
        TechnicalProblem = 5,
        TrackingLost = 6,
        HeartRateSignalLost = 7,
        Other = 8
    }

    /// <summary>System-detected termination/degradation causes. Stored separately from user-selected reasons.</summary>
    public enum SystemDetectedIssue
    {
        None = 0,
        TrackingLost = 1,
        HeartRateSignalLost = 2,
        UnhandledException = 3,
        HmdRemoved = 4
    }

    public enum ValidityStatus
    {
        Unknown = 0,
        Valid = 1,
        ValidWithWarnings = 2,
        InvalidSimulatorSickness = 3,
        InvalidTechnicalFailure = 4,
        InvalidInsufficientHeartRate = 5,
        InvalidTrackingFailure = 6,
        IncompleteUserTerminated = 7,
        DemoOnly = 8,
        // Global session timer reached zero before the last block (game over).
        // Appended for schema v2 — never reorder.
        IncompleteTimeExpired = 9
    }

    public enum HrZone
    {
        SignalLost = 0,
        Stable = 1,
        Elevated = 2,
        High = 3
    }

    public enum HrQualityStatus
    {
        Unknown = 0,
        Good = 1,
        Suspect = 2,     // out of trend / parse warnings
        Invalid = 3,     // out of plausible range
        Stale = 4        // received long after source timestamp
    }

    public enum HrSourceType
    {
        None = 0,
        Simulated = 1,
        NetworkBridge = 2,
        // Future in-APK native ADB client (spec §35). Placeholder source exists;
        // it must NEVER falsely report a connection. Appended — never reorder.
        NativeAdbPlaceholder = 3
    }

    /// <summary>User-facing HR connection mode (spec §35.1).</summary>
    public enum HeartRateMode
    {
        Disconnected = 0,
        Simulated = 1,
        Network = 2,
        NativeAdbPlaceholder = 3
    }

    public enum BaselineQuality
    {
        Unknown = 0,
        Good = 1,
        Low = 2,         // insufficient valid samples — flagged, session may continue with warning
        Missing = 3      // no HR at all (mock/dev only)
    }

    public enum QuestionnairePhase
    {
        PreSession = 0,
        PostSession = 1,
        CycleStart = 2,
        CycleEnd = 3
    }
}
