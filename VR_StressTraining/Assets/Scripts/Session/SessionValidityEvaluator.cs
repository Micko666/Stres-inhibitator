using System;
using System.Collections.Generic;
using StressTraining.Data;

namespace StressTraining.Session
{
    /// <summary>Configurable validity thresholds. All values PROJECT_HEURISTIC (spec §23).</summary>
    [Serializable]
    public sealed class SessionValidityConfig
    {
        public float ssqPostPreDeltaInvalid = 20f;   // PROJECT_HEURISTIC — SSQ total-score rise that invalidates
        public float minHrValidSampleRatio = 0.30f;  // PROJECT_HEURISTIC — below this, HR-dependent analysis invalid
        public bool hrRequiredForValidity = false;   // MVP default: HR loss degrades, does not invalidate
        public int maxTechnicalWarningsForClean = 2; // PROJECT_HEURISTIC — more → ValidWithWarnings
    }

    /// <summary>
    /// Pure evaluator: session facts in → ValidityStatus + notes out.
    /// Precedence (first match wins):
    ///   DemoOnly → InvalidTechnicalFailure → InvalidTrackingFailure →
    ///   InvalidSimulatorSickness → InvalidInsufficientHeartRate →
    ///   IncompleteUserTerminated → ValidWithWarnings → Valid.
    /// An invalid session is NEVER deleted — data stays, scheduler receives the status.
    /// </summary>
    public sealed class SessionValidityEvaluator
    {
        public sealed class Input
        {
            public bool IsDemoPlan;
            public CompletionStatus CompletionStatus;
            public UserTerminationReason UserReason;
            public List<SystemDetectedIssue> SystemIssues = new List<SystemDetectedIssue>();
            public float SsqPreTotal = -1f;      // -1 = not administered
            public float SsqPostTotal = -1f;
            public float HrValidSampleRatio = -1f; // -1 = HR not expected (simulated ok)
            public bool HrWasExpected;
            public int TechnicalWarningCount;
            /// <summary>Global session timer reached zero before the last block (spec §27).</summary>
            public bool TimeExpired;
            /// <summary>Session started before the recommended interval (spec §10) — warning, never invalid.</summary>
            public bool ScheduleOverride;
        }

        private readonly SessionValidityConfig _cfg;

        public SessionValidityEvaluator(SessionValidityConfig cfg = null)
        {
            _cfg = cfg ?? new SessionValidityConfig();
        }

        public ValidityStatus Evaluate(Input input, List<string> notesOut = null)
        {
            void Note(string s) => notesOut?.Add(s);

            if (input.IsDemoPlan)
            {
                Note("Demo plan — never used for adaptation.");
                return ValidityStatus.DemoOnly;
            }

            if (input.SystemIssues.Contains(SystemDetectedIssue.UnhandledException) ||
                input.CompletionStatus == CompletionStatus.Abandoned ||
                // System-terminated is InvalidTechnicalFailure UNLESS it is the
                // game-over time-expiry case (that is IncompleteTimeExpired below).
                (input.CompletionStatus == CompletionStatus.SystemTerminated && !input.TimeExpired))
            {
                Note("Technical failure (exception/abandoned/system-terminated).");
                return ValidityStatus.InvalidTechnicalFailure;
            }

            if (input.SystemIssues.Contains(SystemDetectedIssue.TrackingLost) ||
                input.UserReason == UserTerminationReason.TrackingLost)
            {
                Note("Tracking failure (system-detected or user-reported).");
                return ValidityStatus.InvalidTrackingFailure;
            }

            bool ssqProblem =
                input.UserReason == UserTerminationReason.SimulatorSickness ||
                (input.SsqPreTotal >= 0 && input.SsqPostTotal >= 0 &&
                 input.SsqPostTotal - input.SsqPreTotal >= _cfg.ssqPostPreDeltaInvalid);
            if (ssqProblem)
            {
                Note($"SSQ problem: pre={input.SsqPreTotal:0.#} post={input.SsqPostTotal:0.#} " +
                     $"(delta threshold {_cfg.ssqPostPreDeltaInvalid}, PROJECT_HEURISTIC) or user-reported sickness.");
                return ValidityStatus.InvalidSimulatorSickness;
            }

            if (input.HrWasExpected && _cfg.hrRequiredForValidity &&
                input.HrValidSampleRatio >= 0 && input.HrValidSampleRatio < _cfg.minHrValidSampleRatio)
            {
                Note($"HR valid-sample ratio {input.HrValidSampleRatio:0.00} below " +
                     $"{_cfg.minHrValidSampleRatio} (PROJECT_HEURISTIC).");
                return ValidityStatus.InvalidInsufficientHeartRate;
            }

            if (input.TimeExpired)
            {
                Note("Global session timer expired before the last block (game over).");
                return ValidityStatus.IncompleteTimeExpired;
            }

            if (input.CompletionStatus == CompletionStatus.UserTerminated)
            {
                Note($"User terminated early (reason: {input.UserReason}).");
                return ValidityStatus.IncompleteUserTerminated;
            }

            bool warnings =
                input.TechnicalWarningCount > _cfg.maxTechnicalWarningsForClean ||
                (input.HrWasExpected && input.HrValidSampleRatio >= 0 &&
                 input.HrValidSampleRatio < _cfg.minHrValidSampleRatio) ||
                input.SystemIssues.Contains(SystemDetectedIssue.HeartRateSignalLost) ||
                input.ScheduleOverride;
            if (input.ScheduleOverride)
                Note("Session was started before the recommended interval (schedule override).");
            if (warnings)
            {
                Note("Completed with technical warnings / degraded HR coverage.");
                return ValidityStatus.ValidWithWarnings;
            }

            return ValidityStatus.Valid;
        }
    }
}
