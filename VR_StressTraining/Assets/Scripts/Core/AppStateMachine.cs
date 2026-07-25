using System;
using System.Collections.Generic;

namespace StressTraining.Core
{
    /// <summary>
    /// Pure-C# application state machine with an explicit transition table.
    /// - Rejects invalid transitions (returns false, raises <see cref="TransitionRejected"/>).
    /// - Emits <see cref="StateChanged"/> with previous state, new state, reason and forced flag.
    /// - Does NOT drive UI panels directly; SessionCoordinator/UIManager subscribe to events.
    /// Testable without MonoBehaviour.
    /// </summary>
    public sealed class AppStateMachine
    {
        public struct Transition
        {
            public AppState Previous;
            public AppState Next;
            public string Reason;
            public bool Forced;
            public string TimestampUtcIso;
        }

        public AppState Current { get; private set; } = AppState.Boot;

        public event Action<Transition> StateChanged;
        public event Action<AppState, AppState, string> TransitionRejected;

        private readonly List<Transition> _history = new List<Transition>(64);
        public IReadOnlyList<Transition> History => _history;

        private static readonly HashSet<(AppState, AppState)> Allowed = new HashSet<(AppState, AppState)>
        {
            (AppState.Boot,                      AppState.ProfileSelection),
            (AppState.ProfileSelection,          AppState.Preparation),
            (AppState.ProfileSelection,          AppState.PreSessionQuestionnaires),
            (AppState.PreSessionQuestionnaires,  AppState.Preparation),
            (AppState.Preparation,               AppState.Baseline),
            // Three-task demo skips user-facing HR baseline and enters its
            // explicit tutorial/practice flow directly.
            (AppState.Preparation,               AppState.Ready),
            (AppState.Preparation,               AppState.ProfileSelection),
            (AppState.Baseline,                  AppState.Ready),
            (AppState.Baseline,                  AppState.CopingTraining),
            (AppState.CopingTraining,            AppState.Tutorial),
            // Tutorial may be skipped in later sessions of a cycle
            (AppState.CopingTraining,            AppState.Ready),
            (AppState.Tutorial,                  AppState.Ready),
            // The three-task demo opens its first tutorial in SafeSpace after
            // session creation; the physical corridor begins at practice.
            (AppState.Ready,                     AppState.ActiveSession),
            (AppState.Ready,                     AppState.TransitionToCorridor),
            (AppState.TransitionToCorridor,      AppState.ActiveSession),
            (AppState.ActiveSession,             AppState.Paused),
            (AppState.Paused,                    AppState.ActiveSession),
            (AppState.Paused,                    AppState.SessionEnding),
            (AppState.ActiveSession,             AppState.SessionEnding),
            (AppState.SessionEnding,             AppState.Recovery),
            (AppState.SessionEnding,             AppState.PostSessionSummary),
            (AppState.Recovery,                  AppState.PostSessionQuestionnaires),
            (AppState.PostSessionQuestionnaires, AppState.AdaptationReview),
            (AppState.AdaptationReview,          AppState.PostSessionSummary),
            (AppState.PostSessionSummary,        AppState.ProfileSelection),
            // Error handling: Error is reachable from anywhere (special-cased below);
            // recovery from Error returns to profile selection.
            (AppState.Error,                     AppState.ProfileSelection),
        };

        /// <summary>Validates without changing state.</summary>
        public bool CanTransition(AppState from, AppState to)
        {
            if (to == AppState.Error) return true; // error is always reachable
            return Allowed.Contains((from, to));
        }

        /// <summary>
        /// Requests a validated transition. Returns false and raises
        /// <see cref="TransitionRejected"/> when the transition is not in the table.
        /// </summary>
        public bool RequestTransition(AppState to, string reason)
        {
            if (!CanTransition(Current, to))
            {
                TransitionRejected?.Invoke(Current, to, reason);
                return false;
            }
            Apply(to, reason, forced: false);
            return true;
        }

        /// <summary>
        /// Developer/diagnostic transition that bypasses the table. Always logged as forced.
        /// Never used by the normal user flow.
        /// </summary>
        public void ForceTransition(AppState to, string reason)
        {
            Apply(to, reason, forced: true);
        }

        private void Apply(AppState to, string reason, bool forced)
        {
            var t = new Transition
            {
                Previous = Current,
                Next = to,
                Reason = reason ?? string.Empty,
                Forced = forced,
                TimestampUtcIso = UtcTime.NowIso()
            };
            Current = to;
            _history.Add(t);
            StateChanged?.Invoke(t);
        }
    }
}
