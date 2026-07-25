using System;
using System.Collections.Generic;
using System.Text;
using StressTraining.Core;
using StressTraining.Data;

namespace StressTraining.Tasks
{
    /// <summary>
    /// Versioned fixed 7×3 Corsi grid. Exactly nine active slots are shared by
    /// the tablet and physical console. Column 0 is participant-left, column 6
    /// participant-right; row 0 is far/top and row 2 near/bottom. The mapping is
    /// a PROJECT_HEURISTIC implementation decision and never changes by seed.
    /// </summary>
    public static class CorsiLayout
    {
        public const int GridColumns = 7;
        public const int GridRows = 3;
        public const int GridSlotCount = GridColumns * GridRows;
        public const int PositionCount = 9;
        public const int CorsiLayoutVersion = 1;

        // PROJECT_HEURISTIC — the former irregular normalized positions were
        // mapped once to the nearest unique slots in a 7×3 grid. This array is
        // now the single, fixed source of truth; no seed or session may change it.
        // slotId = row * GridColumns + column.
        private static readonly int[] ActiveSlotIds =
        {
            1, 2, 4, 13, 8, 10, 7, 17, 12
        };
        private static readonly IReadOnlyList<int> ActiveSlotView = Array.AsReadOnly(ActiveSlotIds);

        public static IReadOnlyList<int> ActiveSlots => ActiveSlotView;

        public static int GetSlotIdForCorsiIndex(int corsiIndex)
        {
            ValidateCorsiIndex(corsiIndex);
            return ActiveSlotIds[corsiIndex];
        }

        public static int GetGridColumn(int corsiIndex) =>
            GetSlotIdForCorsiIndex(corsiIndex) % GridColumns;

        public static int GetGridRow(int corsiIndex) =>
            GetSlotIdForCorsiIndex(corsiIndex) / GridColumns;

        /// <summary>
        /// Shared normalized grid position. Column 0 is participant-left,
        /// column 6 participant-right; row 0 is far/top, row 2 near/bottom.
        /// </summary>
        public static UnityEngine.Vector2 GetNormalizedPosition(int corsiIndex)
        {
            float x = GetGridColumn(corsiIndex) / (float)(GridColumns - 1) * 2f - 1f;
            float y = 1f - GetGridRow(corsiIndex) / (float)(GridRows - 1) * 2f;
            return new UnityEngine.Vector2(x, y);
        }

        public static UnityEngine.Vector2 GetTabletPosition(
            int corsiIndex, float mapWidthPx, float mapHeightPx)
        {
            UnityEngine.Vector2 normalized = GetNormalizedPosition(corsiIndex);
            return new UnityEngine.Vector2(
                normalized.x * mapWidthPx * 0.5f,
                normalized.y * mapHeightPx * 0.5f);
        }

        /// <summary>
        /// Physical Corsi button position on the console, rotated 180° about Y
        /// relative to the tablet map so both read identically FROM THE
        /// PARTICIPANT'S POINT OF VIEW. Corsi indices and ActiveSlots never change.
        ///
        /// Why both axes flip (derived from the scene, not guessed):
        ///   • CorridorSpawn is rotated Y=180° → the participant faces world −Z,
        ///     so the participant's LEFT is world +X.
        ///   • Console_BlenderPrototype sits at identity rotation → console local
        ///     axes ARE world axes, so console local +X is the participant's LEFT.
        ///     (Same fact <see cref="Console.ConsoleLayoutBuilder.UserPerspectiveX"/>
        ///     already relies on for the LEFT/MATCH/GO/NOMATCH/RIGHT row.)
        ///   • The tablet canvas faces the participant, so tablet +X is the
        ///     participant's RIGHT — the OPPOSITE world direction.
        ///   ⇒ console.x = −normalized.x    (tablet-left ⇒ participant-left)
        ///   • Console local +Z points back toward the participant (the console is
        ///     deeper in the corridor), so "far" is −Z while the tablet's "far" is
        ///     the TOP of the map (+y).
        ///   ⇒ console.z = −normalized.y    (tablet-top ⇒ far edge of the console)
        ///
        /// Both axes are negated — that is exactly one 180° rotation about the grid
        /// centre. Flipping only ONE axis is a mirror, not a rotation: it silently
        /// swaps left/right (or near/far) and is the bug this replaces. The centre
        /// position (normalized 0,0 → CORSI_5) is a fixed point of the rotation and
        /// therefore stays central. FableCorsiTests locks all nine positions.
        /// </summary>
        public static UnityEngine.Vector3 GetConsoleLocalPosition(
            int corsiIndex, float halfWidthMeters, float halfDepthMeters)
        {
            UnityEngine.Vector2 normalized = GetNormalizedPosition(corsiIndex);
            return new UnityEngine.Vector3(
                -normalized.x * halfWidthMeters,
                0f,
                -normalized.y * halfDepthMeters);
        }

        // Compatibility wrappers for existing callers; both delegate to the
        // versioned 7×3 mapping above.
        public static UnityEngine.Vector2 TabletPosition(int index, float width, float height) =>
            GetTabletPosition(index, width, height);

        public static UnityEngine.Vector3 ConsoleLocalPosition(int index, float halfWidth, float halfDepth) =>
            GetConsoleLocalPosition(index, halfWidth, halfDepth);

        public static SemanticAction ActionFor(int index)
        {
            ValidateCorsiIndex(index);
            return (SemanticAction)((int)SemanticAction.Corsi0 + index);
        }

        public static int IndexOf(SemanticAction action)
        {
            int i = (int)action - (int)SemanticAction.Corsi0;
            return i >= 0 && i < PositionCount ? i : -1;
        }

        public static string ControlId(int index)
        {
            ValidateCorsiIndex(index);
            return "CORSI_" + index;
        }

        private static void ValidateCorsiIndex(int index)
        {
            if (index < 0 || index >= PositionCount)
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    /// <summary>
    /// Corsi-inspired sequential spatial reproduction task (participant name:
    /// "Sekvencijalna memorija"; NOT presented as the standardized clinical
    /// Corsi test). Scientific anchor: Brunetti, Del Gatto &amp; Delogu (2014),
    /// eCorsi, Frontiers in Psychology 5:939 — thesis reference 34.
    ///
    /// Stimulus encoding: "corsi:2-7-4" (presentation order of position indices).
    /// Sequence lengths progress deterministically from min to max across the
    /// block; positions within one sequence never repeat.
    /// </summary>
    public sealed class CorsiTaskDefinition : ITaskDefinition
    {
        public TaskType TaskType => TaskType.CorsiSequence;
        public string DisplayName => "Sekvencijalna memorija";

        public List<TaskTrialDefinition> GenerateTrials(int difficultyLevel, int seed)
        {
            var p = TaskDifficultyConfig.Get(TaskType.CorsiSequence, difficultyLevel);
            return Generate(p, seed);
        }

        public static List<TaskTrialDefinition> Generate(TaskLevelParameters p, int seed)
        {
            var rng = new Random(seed);
            var trials = new List<TaskTrialDefinition>(p.trialCount);

            for (int t = 0; t < p.trialCount; t++)
            {
                // Deterministic progression: min, min, min+1, min+1, … capped at max.
                int length = Math.Min(p.corsiMinSequenceLength + t / 2, p.corsiMaxSequenceLength);
                var sequence = SampleDistinct(rng, CorsiLayout.PositionCount, length);

                double presentationSeconds =
                    length * p.corsiPresentationStepSeconds +
                    Math.Max(0, length - 1) * p.corsiInterStepSeconds;
                double responseSeconds =
                    p.corsiResponseBaseSeconds + length * p.corsiResponsePerItemSeconds;

                trials.Add(new TaskTrialDefinition
                {
                    index = t,
                    isWarmup = false,
                    stimulus = Encode(sequence),
                    expectedAction = SemanticAction.None,   // multi-press; evaluated by the runtime
                    stimulusDurationSeconds = presentationSeconds,
                    responseWindowSeconds = responseSeconds, // measured from RESPONSE PHASE start
                    interTrialIntervalSeconds = p.interTrialIntervalSeconds
                });
            }
            return trials;
        }

        private static List<int> SampleDistinct(Random rng, int poolSize, int count)
        {
            var pool = new List<int>(poolSize);
            for (int i = 0; i < poolSize; i++) pool.Add(i);
            for (int i = pool.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (pool[i], pool[j]) = (pool[j], pool[i]);
            }
            return pool.GetRange(0, count);
        }

        public static string Encode(List<int> sequence)
        {
            var sb = new StringBuilder("corsi:", 6 + sequence.Count * 2);
            for (int i = 0; i < sequence.Count; i++)
            {
                if (i > 0) sb.Append('-');
                sb.Append(sequence[i]);
            }
            return sb.ToString();
        }

        public static List<int> DecodeSequence(string stimulus)
        {
            var result = new List<int>();
            if (string.IsNullOrEmpty(stimulus) || !stimulus.StartsWith("corsi:")) return result;
            foreach (var token in stimulus.Substring(6).Split('-'))
                if (int.TryParse(token, out int idx)) result.Add(idx);
            return result;
        }
    }

    /// <summary>
    /// Corsi runtime — implements ITaskRuntime directly because the response is
    /// an ORDERED SEQUENCE of presses, not a single action.
    ///
    /// Trial phases:
    ///   InterTrialInterval → Stimulus (tablet map presentation, step by step;
    ///   console input REJECTED) → retention gap → ResponseOpen (window =
    ///   responseWindowSeconds from response-phase start; user reproduces the
    ///   sequence on the physical Corsi buttons; no short scoring timeout,
    ///   only the centralized inactivity safety guard) → Feedback → next.
    ///
    /// First-error rule (spec §18.4): the first wrong physical position ends the
    /// attempt immediately; the correctly reproduced prefix is recorded.
    /// Pause rule: pause during presentation or response discards the partial
    /// attempt and safely restarts the SAME trial after resume.
    /// </summary>
    public sealed class CorsiTaskRuntime : ITaskRuntime
    {
        public TaskType TaskType => TaskType.CorsiSequence;
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

        /// <summary>(positionIndex, stepNumber1Based, totalSteps) — one map position lights up.</summary>
        public event Action<int, int, int> PositionLit;
        /// <summary>All positions dim (between steps / end of presentation).</summary>
        public event Action PositionsDimmed;
        /// <summary>Retention gap ended; the console may now accept the reproduction.</summary>
        public event Action ResponsePhaseStarted;

        public bool Backward { get; }

        private readonly List<TaskTrialDefinition> _trials;
        private readonly TaskLevelParameters _timing;   // central Corsi timing (spec §18.3)

        private double _phaseTimer;
        private double _activeSeconds;
        private string _stimulusOnsetUtcIso = "";
        private double _stimulusOnsetActiveSeconds;
        private bool _trialInterruptedByPause;

        // presentation sub-state
        private List<int> _sequence = new List<int>();
        private int _presentStep;
        private bool _stepLit;
        private double _stepTimer;
        private bool _inRetention;

        // response sub-state
        private bool _responseOpen;
        private double _responseStartActiveSeconds;
        private double _lastPressActiveSeconds;
        private double _lastResponseActivityActiveSeconds;
        private readonly List<int> _responsePrefix = new List<int>();
        private readonly List<double> _interPressMs = new List<double>();
        private List<int> _requiredOrder = new List<int>();

        private const double FeedbackSeconds = TaskDifficultyConfig.CorsiFeedbackSeconds;

        public CorsiTaskRuntime(List<TaskTrialDefinition> trials, TaskLevelParameters timing)
        {
            _trials = trials ?? throw new ArgumentNullException(nameof(trials));
            _timing = timing ?? throw new ArgumentNullException(nameof(timing));
            if (_timing.corsiInactivitySafetyTimeoutSeconds <= 0)
                _timing.corsiInactivitySafetyTimeoutSeconds =
                    TaskDifficultyConfig.CorsiInactivitySafetyTimeoutSeconds;
            Backward = timing.corsiBackward;
        }

        /// <summary>Convenience overload used by tests: timing from the central table.</summary>
        public CorsiTaskRuntime(List<TaskTrialDefinition> trials, bool backward)
            : this(trials, BuildTiming(backward)) { }

        private static TaskLevelParameters BuildTiming(bool backward)
        {
            var p = TaskDifficultyConfig.Get(TaskType.CorsiSequence, backward ? 3 : 1);
            p.corsiBackward = backward;
            return p;
        }

        public void Begin()
        {
            if (Phase != TrialPhase.Idle) return;
            CurrentTrialIndex = -1;
            AdvanceToNextTrial();
        }

        public void Tick(double dt)
        {
            if (dt <= 0) return;
            switch (Phase)
            {
                case TrialPhase.Idle:
                case TrialPhase.Finished:
                case TrialPhase.Aborted:
                    return;
            }
            _activeSeconds += dt;
            _phaseTimer += dt;

            switch (Phase)
            {
                case TrialPhase.InterTrialInterval:
                    if (_phaseTimer >= CurrentTrial.interTrialIntervalSeconds)
                        EnterPresentation();
                    break;

                case TrialPhase.Stimulus:
                    TickPresentation(dt);
                    break;

                case TrialPhase.ResponseOpen:
                    // No short scoring timeout: the global challenge clock supplies
                    // time pressure. This large inactivity guard only prevents a
                    // permanently stuck trial and is recorded as abandoned.
                    if (_activeSeconds - _lastResponseActivityActiveSeconds >=
                        _timing.corsiInactivitySafetyTimeoutSeconds)
                        CompleteTrial(timedOut: false, abandonedByInactivity: true);
                    break;

                case TrialPhase.Feedback:
                    if (_phaseTimer >= FeedbackSeconds)
                        AdvanceToNextTrial();
                    break;
            }
        }

        private void EnterPresentation()
        {
            Phase = TrialPhase.Stimulus;
            _phaseTimer = 0;
            _stepTimer = 0;
            _presentStep = 0;
            _stepLit = false;
            _inRetention = false;
            _responseOpen = false;
            _responsePrefix.Clear();
            _interPressMs.Clear();

            _sequence = CorsiTaskDefinition.DecodeSequence(CurrentTrial.stimulus);
            _requiredOrder = new List<int>(_sequence);
            if (Backward) _requiredOrder.Reverse();

            _stimulusOnsetUtcIso = UtcTime.NowIso();
            _stimulusOnsetActiveSeconds = _activeSeconds;
            StimulusShown?.Invoke(CurrentTrial);   // tablet shows the dimmed map
        }

        private void TickPresentation(double dt)
        {
            int len = _sequence.Count;
            if (len == 0) { CompleteTrial(timedOut: true); return; }

            double stepOn = _timing.corsiPresentationStepSeconds;
            _stepTimer += dt;

            if (_inRetention)
            {
                if (_stepTimer >= _timing.corsiRetentionSeconds)
                {
                    _inRetention = false;
                    BeginResponsePhase();
                }
                return;
            }

            if (_presentStep >= len)
            {
                // presentation done → retention gap
                _inRetention = true;
                _stepTimer = 0;
                StimulusHidden?.Invoke();
                PositionsDimmed?.Invoke();
                return;
            }

            if (!_stepLit)
            {
                _stepLit = true;
                _stepTimer = 0;
                PositionLit?.Invoke(_sequence[_presentStep], _presentStep + 1, len);
            }
            else if (_stepTimer >= stepOn)
            {
                PositionsDimmed?.Invoke();
                _stepLit = false;
                _stepTimer = -_timing.corsiInterStepSeconds; // gap before the next step
                _presentStep++;
            }
        }

        private void BeginResponsePhase()
        {
            Phase = TrialPhase.ResponseOpen;
            _phaseTimer = 0;
            _responseOpen = true;
            _responseStartActiveSeconds = _activeSeconds;
            _lastPressActiveSeconds = -1;
            _lastResponseActivityActiveSeconds = _activeSeconds;
            ResponsePhaseStarted?.Invoke();
        }

        public void SubmitAction(SemanticAction action)
        {
            if (!_responseOpen || Phase != TrialPhase.ResponseOpen) return; // no input during presentation
            int position = CorsiLayout.IndexOf(action);
            if (position < 0) return;

            double now = _activeSeconds;
            if (_responsePrefix.Count == 0)
            {
                // start-of-response latency
                _pendingStartResponseMs = (now - _responseStartActiveSeconds) * 1000.0;
            }
            else if (_lastPressActiveSeconds >= 0)
            {
                _interPressMs.Add((now - _lastPressActiveSeconds) * 1000.0);
            }
            _lastPressActiveSeconds = now;
            _lastResponseActivityActiveSeconds = now;

            if (_responsePrefix.Count >= _requiredOrder.Count)
                return; // protects against duplicate/late events after completion

            int expected = _requiredOrder[_responsePrefix.Count];
            _responsePrefix.Add(position);

            if (position != expected)
            {
                // FIRST-ERROR TERMINATION (spec §18.4) — attempt ends immediately.
                CompleteTrial(timedOut: false, firstError: true);
                return;
            }
            if (_responsePrefix.Count >= _requiredOrder.Count)
            {
                CompleteTrial(timedOut: false);
            }
        }

        private double _pendingStartResponseMs = -1;

        private void CompleteTrial(bool timedOut, bool firstError = false,
            bool abandonedByInactivity = false)
        {
            _responseOpen = false;
            if (Phase == TrialPhase.Stimulus) { StimulusHidden?.Invoke(); PositionsDimmed?.Invoke(); }

            int correctPrefix = CountCorrectPrefix();
            bool complete = !firstError && !timedOut && !abandonedByInactivity &&
                            correctPrefix == _requiredOrder.Count;

            var detail = new CorsiTrialDetail
            {
                PresentedSequence = new List<int>(_sequence),
                RequiredResponseOrder = new List<int>(_requiredOrder),
                ResponsePrefix = new List<int>(_responsePrefix),
                SequenceLength = _sequence.Count,
                CorrectlyReproducedCount = correctPrefix,
                FirstErrorIndex = firstError ? _responsePrefix.Count - 1 : -1,
                FirstWrongButtonId = firstError
                    ? CorsiLayout.ControlId(_responsePrefix[_responsePrefix.Count - 1]) : "",
                StartResponseMs = _pendingStartResponseMs,
                InterPressIntervalsMs = new List<double>(_interPressMs),
                TotalResponseMs = _responsePrefix.Count > 0
                    ? (_lastPressActiveSeconds - _responseStartActiveSeconds) * 1000.0 : -1,
                Backward = Backward,
                TimedOut = timedOut,
                AbandonedByInactivity = abandonedByInactivity
            };

            var result = new TaskTrialResult
            {
                Definition = CurrentTrial,
                ActualAction = SemanticAction.None,
                ReactionTimeMs = _pendingStartResponseMs,
                Correct = complete,
                Missed = timedOut && _responsePrefix.Count == 0,
                FalsePositive = false,
                WasInterruptedByPause = _trialInterruptedByPause,
                StimulusPresentedAtUtcIso = _stimulusOnsetUtcIso,
                ResponseAtUtcIso = _responsePrefix.Count > 0 ? UtcTime.NowIso() : "",
                StimulusOnsetActiveSeconds = _stimulusOnsetActiveSeconds,
                Corsi = detail
            };
            Results.Add(result);

            Phase = TrialPhase.Feedback;
            _phaseTimer = 0;
            _pendingStartResponseMs = -1;
            TrialCompleted?.Invoke(result);
        }

        private int CountCorrectPrefix()
        {
            int n = 0;
            for (int i = 0; i < _responsePrefix.Count && i < _requiredOrder.Count; i++)
            {
                if (_responsePrefix[i] != _requiredOrder[i]) break;
                n++;
            }
            return n;
        }

        public void OnPauseInterrupt()
        {
            if (Phase == TrialPhase.Stimulus || Phase == TrialPhase.ResponseOpen)
            {
                // Discard the partial attempt; the SAME trial restarts after resume.
                StimulusHidden?.Invoke();
                PositionsDimmed?.Invoke();
                _trialInterruptedByPause = true;
                ClearAttemptState();
                Phase = TrialPhase.InterTrialInterval;
                _phaseTimer = 0;
            }
        }

        public void Abort()
        {
            if (Phase == TrialPhase.Finished || Phase == TrialPhase.Aborted) return;
            _responseOpen = false;
            Phase = TrialPhase.Aborted;
            PositionsDimmed?.Invoke();
            BlockAborted?.Invoke();
        }

        public void Reset()
        {
            Phase = TrialPhase.Idle;
            CurrentTrialIndex = -1;
            Results.Clear();
            _activeSeconds = 0;
            _trialInterruptedByPause = false;
            ClearAttemptState();
        }

        private void ClearAttemptState()
        {
            _responseOpen = false;
            _responsePrefix.Clear();
            _interPressMs.Clear();
            _sequence.Clear();
            _requiredOrder.Clear();
            _presentStep = 0;
            _stepLit = false;
            _inRetention = false;
            _phaseTimer = 0;
            _stepTimer = 0;
            _responseStartActiveSeconds = 0;
            _lastPressActiveSeconds = -1;
            _lastResponseActivityActiveSeconds = 0;
            _pendingStartResponseMs = -1;
        }

        private void AdvanceToNextTrial()
        {
            ClearAttemptState();
            CurrentTrialIndex++;
            if (CurrentTrialIndex >= _trials.Count)
            {
                Phase = TrialPhase.Finished;
                BlockFinished?.Invoke();
                return;
            }
            Phase = TrialPhase.InterTrialInterval;
            _phaseTimer = 0;
            _trialInterruptedByPause = false;
        }
    }
}
