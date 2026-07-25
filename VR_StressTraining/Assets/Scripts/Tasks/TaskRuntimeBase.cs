using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;

namespace StressTraining.Tasks
{
    /// <summary>
    /// Pure-C# trial state machine shared by all three tasks. No UnityEngine
    /// dependency except UtcTime — fully testable in EditMode.
    ///
    /// Phases per trial:
    ///   InterTrialInterval → Stimulus (visible, window open)
    ///   → ResponseOpen (hidden, window still open) → Feedback → next trial.
    ///
    /// Timing: driven exclusively by Tick(activeDeltaSeconds). The caller
    /// (TaskRunner) does not tick while paused, so pause time can never leak
    /// into reaction times or windows.
    ///
    /// Pause rule (documented, spec §16): when a pause interrupts a trial in
    /// Stimulus/ResponseOpen phase, the trial is SAFELY REPEATED — the partial
    /// attempt is discarded, the same stimulus definition restarts after resume,
    /// and no double response registration is possible because the response
    /// latch resets with the trial.
    /// </summary>
    public abstract class TaskRuntimeBase : ITaskRuntime
    {
        public abstract TaskType TaskType { get; }

        public TrialPhase Phase { get; private set; } = TrialPhase.Idle;
        public int CurrentTrialIndex { get; private set; } = -1;
        public int TrialCount => _trials.Count;
        public TaskTrialDefinition CurrentTrial =>
            CurrentTrialIndex >= 0 && CurrentTrialIndex < _trials.Count ? _trials[CurrentTrialIndex] : null;

        public List<TaskTrialResult> Results { get; } = new List<TaskTrialResult>();

        public event Action<TaskTrialDefinition> StimulusShown;
        public event Action StimulusHidden;
        public event Action<TaskTrialResult> TrialCompleted;
        public event Action BlockFinished;
        public event Action BlockAborted;

        private readonly List<TaskTrialDefinition> _trials;
        private double _phaseTimer;
        private double _activeSeconds;             // accumulated active time inside this block
        private double _stimulusOnsetActiveSeconds;
        private string _stimulusOnsetUtcIso = "";
        private bool _responseLatched;             // guards against double registration
        private SemanticAction _latchedAction;
        private double _latchedReactionMs;
        private string _latchedResponseUtcIso;
        private bool _trialInterruptedByPause;

        private const double FeedbackSeconds = TaskDifficultyConfig.StandardFeedbackSeconds;

        protected TaskRuntimeBase(List<TaskTrialDefinition> trials)
        {
            _trials = trials ?? throw new ArgumentNullException(nameof(trials));
        }

        /// <summary>Which semantic actions this task listens to; all others are ignored.</summary>
        protected abstract bool IsRelevantAction(SemanticAction action);

        /// <summary>
        /// Task-specific scoring. Called once per trial when the window closes or a
        /// response arrives. For expected==None tasks (no-go), any response is a
        /// commission error.
        /// </summary>
        protected abstract void Evaluate(TaskTrialResult result);

        public void Begin()
        {
            if (Phase != TrialPhase.Idle) return;
            CurrentTrialIndex = -1;
            AdvanceToNextTrial();
        }

        public void Tick(double dt)
        {
            if (dt <= 0 || Phase == TrialPhase.Idle || Phase == TrialPhase.Finished) return;
            _activeSeconds += dt;
            _phaseTimer += dt;

            switch (Phase)
            {
                case TrialPhase.InterTrialInterval:
                    if (_phaseTimer >= CurrentTrial.interTrialIntervalSeconds)
                        EnterStimulus();
                    break;

                case TrialPhase.Stimulus:
                    if (_responseLatched) { CompleteTrial(); break; }
                    if (_phaseTimer >= CurrentTrial.responseWindowSeconds)
                    {
                        CompleteTrial();
                    }
                    else if (_phaseTimer >= CurrentTrial.stimulusDurationSeconds)
                    {
                        StimulusHidden?.Invoke();
                        Phase = TrialPhase.ResponseOpen;
                        // _phaseTimer keeps running from stimulus onset for window checks
                    }
                    break;

                case TrialPhase.ResponseOpen:
                    if (_responseLatched || _phaseTimer >= CurrentTrial.responseWindowSeconds)
                        CompleteTrial();
                    break;

                case TrialPhase.Feedback:
                    if (_phaseTimer >= FeedbackSeconds)
                        AdvanceToNextTrial();
                    break;
            }
        }

        public void SubmitAction(SemanticAction action)
        {
            if (Phase != TrialPhase.Stimulus && Phase != TrialPhase.ResponseOpen) return;
            // Intro/warmup stimuli establish the memory context and never expect
            // a response. Ignoring input here prevents an accidental press from
            // ending the introductory stimulus early.
            if (CurrentTrial == null || CurrentTrial.isWarmup) return;
            if (!IsRelevantAction(action)) return;
            if (_responseLatched) return;   // first response wins; no double registration

            _responseLatched = true;
            _latchedAction = action;
            _latchedReactionMs = (_activeSeconds - _stimulusOnsetActiveSeconds) * 1000.0;
            _latchedResponseUtcIso = UtcTime.NowIso();
        }

        public void OnPauseInterrupt()
        {
            if (Phase == TrialPhase.Stimulus || Phase == TrialPhase.ResponseOpen)
            {
                // Discard partial attempt; repeat same trial after resume.
                if (Phase != TrialPhase.InterTrialInterval) StimulusHidden?.Invoke();
                _trialInterruptedByPause = true;
                _responseLatched = false;
                Phase = TrialPhase.InterTrialInterval;
                _phaseTimer = 0;
            }
        }

        public void Abort()
        {
            if (Phase == TrialPhase.Aborted || Phase == TrialPhase.Finished || Phase == TrialPhase.Idle) return;
            if (Phase == TrialPhase.Stimulus || Phase == TrialPhase.ResponseOpen)
                StimulusHidden?.Invoke();
            _responseLatched = false;
            _latchedAction = SemanticAction.None;
            Phase = TrialPhase.Aborted;
            _phaseTimer = 0;
            BlockAborted?.Invoke();
        }

        public void Reset()
        {
            if (Phase != TrialPhase.Idle && Phase != TrialPhase.Finished && Phase != TrialPhase.Aborted)
                Abort();
            CurrentTrialIndex = -1;
            Results.Clear();
            Phase = TrialPhase.Idle;
            _phaseTimer = 0;
            _activeSeconds = 0;
            _stimulusOnsetActiveSeconds = 0;
            _stimulusOnsetUtcIso = "";
            _responseLatched = false;
            _latchedAction = SemanticAction.None;
            _latchedReactionMs = -1;
            _latchedResponseUtcIso = "";
            _trialInterruptedByPause = false;
        }

        private void AdvanceToNextTrial()
        {
            CurrentTrialIndex++;
            if (CurrentTrialIndex >= _trials.Count)
            {
                Phase = TrialPhase.Finished;
                BlockFinished?.Invoke();
                return;
            }
            Phase = TrialPhase.InterTrialInterval;
            _phaseTimer = 0;
            _responseLatched = false;
            _trialInterruptedByPause = false;
        }

        private void EnterStimulus()
        {
            Phase = TrialPhase.Stimulus;
            _phaseTimer = 0;
            _stimulusOnsetActiveSeconds = _activeSeconds;
            _stimulusOnsetUtcIso = UtcTime.NowIso();
            _responseLatched = false;
            StimulusShown?.Invoke(CurrentTrial);
        }

        private void CompleteTrial()
        {
            if (Phase == TrialPhase.Stimulus) StimulusHidden?.Invoke();

            var result = new TaskTrialResult
            {
                Definition = CurrentTrial,
                ActualAction = _responseLatched ? _latchedAction : SemanticAction.None,
                ReactionTimeMs = _responseLatched ? _latchedReactionMs : -1,
                ResponseAtUtcIso = _responseLatched ? _latchedResponseUtcIso : "",
                StimulusPresentedAtUtcIso = _stimulusOnsetUtcIso,
                StimulusOnsetActiveSeconds = _stimulusOnsetActiveSeconds,
                WasInterruptedByPause = _trialInterruptedByPause
            };
            Evaluate(result);
            Results.Add(result);

            Phase = TrialPhase.Feedback;
            _phaseTimer = 0;
            TrialCompleted?.Invoke(result);
        }
    }
}
