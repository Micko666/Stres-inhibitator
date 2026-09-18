using System;

namespace StressTraining.Core
{
    public enum GlobalChallengeClockState
    {
        NotArmed = 0,
        Ready = 1,
        Running = 2,
        Paused = 3,
        Expired = 4,
        Stopped = 5
    }

    /// <summary>
    /// Central session time source. The production global challenge clock is
    /// explicitly gated and advances only while active gameplay is running.
    /// Questionnaires, baseline, coping, tutorials, transitions, countdowns and
    /// manual pauses therefore cannot consume the challenge budget.
    /// </summary>
    public sealed class SessionClock
    {
        public bool IsRunning { get; private set; }
        public bool IsPaused { get; private set; }

        public double RealElapsedSeconds { get; private set; }
        public double ActiveElapsedSeconds { get; private set; }
        public double PausedElapsedSeconds { get; private set; }
        public double BlockElapsedSeconds { get; private set; }
        public double TrialElapsedSeconds { get; private set; }
        public int PauseCount { get; private set; }

        public double GlobalDurationSeconds { get; private set; } = -1;
        public double PenaltySeconds { get; private set; }
        public double GlobalElapsedSeconds { get; private set; }
        public GlobalChallengeClockState GlobalClockState { get; private set; } =
            GlobalChallengeClockState.NotArmed;

        public bool HasGlobalTimer => GlobalDurationSeconds > 0;
        public bool GlobalChallengeStarted => HasGlobalTimer &&
            GlobalClockState != GlobalChallengeClockState.NotArmed &&
            GlobalClockState != GlobalChallengeClockState.Ready;
        public bool IsGlobalChallengeRunning => HasGlobalTimer &&
            GlobalClockState == GlobalChallengeClockState.Running && !IsPaused;
        public bool IsGlobalChallengePaused => HasGlobalTimer &&
            (GlobalClockState == GlobalChallengeClockState.Paused ||
             (GlobalClockState == GlobalChallengeClockState.Running && IsPaused));
        public double RemainingGlobalSeconds => !HasGlobalTimer
            ? -1
            : Math.Max(0, GlobalDurationSeconds - GlobalElapsedSeconds - PenaltySeconds);
        public bool GlobalTimeExpired => HasGlobalTimer && RemainingGlobalSeconds <= 0;
        public double RemainingGlobalFraction => !HasGlobalTimer || GlobalDurationSeconds <= 0
            ? -1
            : Math.Max(0, RemainingGlobalSeconds / GlobalDurationSeconds);

        public event Action<bool> PauseStateChanged;
        public event Action<GlobalChallengeClockState> GlobalClockStateChanged;

        public void StartSession()
        {
            IsRunning = true;
            IsPaused = false;
            RealElapsedSeconds = 0;
            ActiveElapsedSeconds = 0;
            PausedElapsedSeconds = 0;
            BlockElapsedSeconds = 0;
            TrialElapsedSeconds = 0;
            PauseCount = 0;
            GlobalDurationSeconds = -1;
            PenaltySeconds = 0;
            GlobalElapsedSeconds = 0;
            SetGlobalState(GlobalChallengeClockState.NotArmed);
        }

        /// <summary>Arms the budget but does not start countdown.</summary>
        public void SetGlobalDuration(double seconds)
        {
            GlobalDurationSeconds = seconds > 0 ? seconds : -1;
            PenaltySeconds = 0;
            GlobalElapsedSeconds = 0;
            SetGlobalState(HasGlobalTimer
                ? GlobalChallengeClockState.Ready
                : GlobalChallengeClockState.NotArmed);
        }

        /// <summary>
        /// Opens or closes the active challenge gate. Closing the gate preserves
        /// elapsed time and is used for instructions, countdowns and transitions.
        /// </summary>
        public void SetGlobalChallengeRunning(bool running)
        {
            if (!IsRunning || !HasGlobalTimer || GlobalTimeExpired)
            {
                if (GlobalTimeExpired) SetGlobalState(GlobalChallengeClockState.Expired);
                return;
            }
            if (running)
            {
                SetGlobalState(GlobalChallengeClockState.Running);
                return;
            }

            // An armed clock that has never run is still Ready, not Paused.
            // This preserves the distinction between setup/countdown before the
            // first block and a transition after challenge time has started.
            if (GlobalClockState != GlobalChallengeClockState.Ready)
                SetGlobalState(GlobalChallengeClockState.Paused);
        }

        public void StopGlobalChallenge()
        {
            if (!HasGlobalTimer) return;
            SetGlobalState(GlobalTimeExpired
                ? GlobalChallengeClockState.Expired
                : GlobalChallengeClockState.Stopped);
        }

        public void AddPenaltySeconds(double seconds)
        {
            if (seconds <= 0 || !HasGlobalTimer) return;
            PenaltySeconds += seconds;
            if (GlobalTimeExpired) SetGlobalState(GlobalChallengeClockState.Expired);
        }

        public void StopSession()
        {
            IsRunning = false;
            if (HasGlobalTimer && GlobalClockState != GlobalChallengeClockState.Expired)
                SetGlobalState(GlobalChallengeClockState.Stopped);
        }

        public void SetPaused(bool paused)
        {
            if (!IsRunning || IsPaused == paused) return;
            IsPaused = paused;
            if (paused) PauseCount++;
            PauseStateChanged?.Invoke(paused);
        }

        public void Tick(double realDeltaSeconds)
        {
            if (!IsRunning || realDeltaSeconds <= 0) return;
            RealElapsedSeconds += realDeltaSeconds;
            if (IsPaused)
            {
                PausedElapsedSeconds += realDeltaSeconds;
                return;
            }

            ActiveElapsedSeconds += realDeltaSeconds;
            BlockElapsedSeconds += realDeltaSeconds;
            TrialElapsedSeconds += realDeltaSeconds;

            if (GlobalClockState == GlobalChallengeClockState.Running && HasGlobalTimer)
            {
                GlobalElapsedSeconds += realDeltaSeconds;
                if (GlobalTimeExpired)
                    SetGlobalState(GlobalChallengeClockState.Expired);
            }
        }

        public void BeginBlock() => BlockElapsedSeconds = 0;
        public void BeginTrial() => TrialElapsedSeconds = 0;

        private void SetGlobalState(GlobalChallengeClockState state)
        {
            if (GlobalClockState == state) return;
            GlobalClockState = state;
            GlobalClockStateChanged?.Invoke(state);
        }
    }
}
