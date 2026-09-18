namespace StressTraining.Core
{
    /// <summary>
    /// Top-level application states. The full user flow is documented in docs/SESSION_FLOW.md.
    /// Transitions are validated by <see cref="AppStateMachine"/>.
    /// </summary>
    public enum AppState
    {
        Boot,
        ProfileSelection,
        PreSessionQuestionnaires,
        Preparation,
        Baseline,
        CopingTraining,
        Tutorial,
        Ready,
        TransitionToCorridor,
        ActiveSession,
        Paused,
        SessionEnding,
        Recovery,
        PostSessionQuestionnaires,
        AdaptationReview,
        PostSessionSummary,
        Error
    }
}
