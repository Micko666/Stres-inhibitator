using System;
using System.Collections.Generic;
using StressTraining.Console;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.HR;
using StressTraining.Persistence;
using StressTraining.Session;
using StressTraining.Tablet;

namespace StressTraining.Tasks
{
    public sealed class TaskRunnerResult
    {
        public TaskBlockResult Block;
        public TaskRunKind RunKind;
        public float AverageBpm = -1;
        public int MaxBpm = -1;
        public double ElevatedSeconds;
        public int HrSampleCount;

        public bool IsPractice => RunKind == TaskRunKind.Practice;
        public bool ContributesToSessionScore => TaskRunPolicy.ContributesToSessionScore(RunKind);
    }

    /// <summary>
    /// Owns exactly one active task runtime. The same execution and feedback
    /// path is used for all three tasks, while TaskRunKind is the hard boundary
    /// between non-persisted practice and scored session data.
    /// </summary>
    public sealed class TaskRunner : IDisposable
    {
        private readonly TabletDisplayController _tablet;
        private readonly ConsoleInputRouter _input;
        private readonly HeartRateService _heartRate;

        private ActiveSessionContext _context;
        private SessionClock _clock;
        private TrialLogWriter _writer;
        private ITaskRuntime _runtime;
        private string _blockId = "";
        private string _activeTrialId = "";
        private int _level;
        private int _seed;
        private int _scoreableTrialCount;
        private int _warmupCount;
        private TaskType _currentTask = TaskType.None;
        private TaskRunKind _runKind = TaskRunKind.Scored;
        private bool _hrAggregationActive;
        private bool _paused;
        private bool _disposed;

        public bool IsActive { get; private set; }
        public bool IsPaused => _paused;
        public bool IsPractice => IsActive && _runKind == TaskRunKind.Practice;
        public TaskRunKind CurrentRunKind => _runKind;
        public TaskType CurrentTask => _currentTask;
        public TaskTrialDefinition CurrentTrial => _runtime?.CurrentTrial;
        public TrialPhase CurrentPhase => _runtime?.Phase ?? TrialPhase.Idle;
        public TaskBlockResult LastBlockResult { get; private set; }
        public TaskRunnerResult LastResult { get; private set; }

        public event Action<TaskRunnerResult> BlockCompleted;
        public event Action PauseToggleRequested;

        public TaskRunner(TabletDisplayController tablet, ConsoleInputRouter input,
            HeartRateService heartRate)
        {
            _tablet = tablet;
            _input = input;
            _heartRate = heartRate ?? throw new ArgumentNullException(nameof(heartRate));
            if (_input != null) _input.ActionTriggered += OnActionTriggered;
        }

        public void ConfigureSession(ActiveSessionContext context, TrialLogWriter writer)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _clock = context.Clock;
            _writer = writer;
        }

        public void StartPractice(TaskType taskType)
        {
            StartRun(taskType, TaskRunKind.Practice, 1, 0,
                DemoTaskFactory.CreatePracticeTrials(taskType), null);
        }

        public void StartScoredDemoBlock(TaskType taskType, int level, int seed,
            int requestedScoreableTrialCount = 0, string blockId = null)
        {
            StartRun(taskType, TaskRunKind.Scored, level, seed,
                DemoTaskFactory.CreateShortScoredTrials(
                    taskType, level, seed, requestedScoreableTrialCount), blockId);
        }

        /// <summary>
        /// Production path: runs one planned mini-block with FULL trial counts
        /// from the difficulty table (no demo clamp), the plan's block seed and
        /// the plan's stable blockId.
        /// </summary>
        public void StartScoredPlannedBlock(SessionBlockPlan block)
        {
            if (block == null) throw new ArgumentNullException(nameof(block));
            StartRun(block.taskType, TaskRunKind.Scored, block.difficultyLevel, block.blockSeed,
                GenerateFullTrials(block.taskType, block.difficultyLevel, block.blockSeed),
                block.blockId);
        }

        private static List<TaskTrialDefinition> GenerateFullTrials(TaskType type, int level, int seed)
        {
            switch (type)
            {
                case TaskType.NBack: return new NBackTaskDefinition().GenerateTrials(level, seed);
                case TaskType.GoNoGo: return new GoNoGoTaskDefinition().GenerateTrials(level, seed);
                case TaskType.Flanker: return new FlankerTaskDefinition().GenerateTrials(level, seed);
                case TaskType.CorsiSequence: return new CorsiTaskDefinition().GenerateTrials(level, seed);
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        // Compatibility wrappers for the existing coordinator and explicit task flow.
        public void StartNBackBlock(int level, int seed, int scoreableTrialCount) =>
            StartScoredDemoBlock(TaskType.NBack, level, seed, scoreableTrialCount);

        public void StartGoNoGoBlock(int level, int seed, int scoreableTrialCount = 0) =>
            StartScoredDemoBlock(TaskType.GoNoGo, level, seed, scoreableTrialCount);

        public void StartFlankerBlock(int level, int seed, int scoreableTrialCount = 0) =>
            StartScoredDemoBlock(TaskType.Flanker, level, seed, scoreableTrialCount);

        private void StartRun(TaskType taskType, TaskRunKind runKind, int level, int seed,
            List<TaskTrialDefinition> trials, string requestedBlockId)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TaskRunner));
            if (taskType == TaskType.None) throw new ArgumentOutOfRangeException(nameof(taskType));
            if (runKind == TaskRunKind.Scored && (_context == null || _clock == null))
                throw new InvalidOperationException("ConfigureSession must be called before a scored block.");

            Abort();

            _currentTask = taskType;
            _runKind = runKind;
            _level = TaskDifficultyConfig.Clamp(level);
            _seed = seed;
            string prefix = runKind == TaskRunKind.Practice ? "practice-" : "";
            _blockId = string.IsNullOrWhiteSpace(requestedBlockId)
                ? prefix + taskType.ToString().ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N")
                : requestedBlockId;
            _activeTrialId = "";
            _scoreableTrialCount = DemoTaskFactory.ScoreableCount(trials);
            _warmupCount = DemoTaskFactory.WarmupCount(trials);

            _runtime = DemoTaskFactory.CreateRuntime(taskType, trials, _level);
            _runtime.StimulusShown += OnStimulusShown;
            _runtime.StimulusHidden += OnStimulusHidden;
            _runtime.TrialCompleted += OnTrialCompleted;
            _runtime.BlockFinished += OnBlockFinished;
            _runtime.BlockAborted += OnBlockAborted;
            if (_runtime is CorsiTaskRuntime corsi)
            {
                corsi.PositionLit += OnCorsiPositionLit;
                corsi.PositionsDimmed += OnCorsiPositionsDimmed;
                corsi.ResponsePhaseStarted += OnCorsiResponsePhaseStarted;
            }

            if (_context != null) _context.ActiveTrialId = "";
            if (TaskRunPolicy.ContributesToSessionScore(runKind)) _clock.BeginBlock();
            if (TaskRunPolicy.AggregatesTaskHeartRate(runKind))
            {
                _heartRate.BeginTaskAggregation();
                _hrAggregationActive = true;
            }

            _paused = false;
            IsActive = true;
            _input?.SetGameplayInputEnabled(true);
            TaskType initialControls = taskType == TaskType.CorsiSequence
                ? TaskType.None : taskType;
            _input?.SetActiveTask(initialControls);
            _tablet?.SetHeader(DemoTaskFactory.DisplayName(taskType) +
                               (runKind == TaskRunKind.Practice ? ": vježba" : ""));
            _tablet?.SetActiveControls(initialControls);
            _tablet?.SetTrialProgress(0, _scoreableTrialCount);
            _tablet?.ClearFeedback();
            _runtime.Begin();
        }

        public void Tick(double activeDeltaSeconds)
        {
            if (!IsActive || _paused || _runtime == null) return;
            _runtime.Tick(activeDeltaSeconds);
        }

        public void Pause()
        {
            if (!IsActive || _paused) return;
            _paused = true;
            _input?.SetGameplayInputEnabled(false);
            _input?.SetActiveTask(TaskType.None);
            _runtime?.OnPauseInterrupt();
            _tablet?.SetActiveControls(TaskType.None);
            _tablet?.SetPausedOverlay(true);
        }

        public void Resume()
        {
            if (!IsActive || !_paused) return;
            _paused = false;
            _input?.SetGameplayInputEnabled(true);
            // A paused Corsi attempt restarts in inter-trial/presentation. Its
            // physical buttons reopen only when ResponsePhaseStarted fires.
            TaskType resumedControls = _currentTask == TaskType.CorsiSequence
                ? TaskType.None : _currentTask;
            _input?.SetActiveTask(resumedControls);
            _tablet?.SetActiveControls(resumedControls);
            _tablet?.SetPausedOverlay(false);
        }

        public void Abort()
        {
            if (_runtime == null) return;
            bool wasActive = IsActive;
            if (wasActive)
            {
                LastBlockResult = TaskBlockResult.Aggregate(
                    _blockId, _currentTask, _level, _seed, _runtime.Results);
                LastResult = CreateResult(LastBlockResult);
            }

            _runtime.Abort();
            if (TaskRunPolicy.PersistsTrials(_runKind)) _writer?.Flush();
            IsActive = false;
            _paused = false;
            _input?.SetGameplayInputEnabled(false);
            _input?.SetActiveTask(TaskType.None);
            _tablet?.SetActiveControls(TaskType.None);
            _activeTrialId = "";
            if (_context != null) _context.ActiveTrialId = "";
            UnsubscribeRuntime();
            _runtime = null;
            _currentTask = TaskType.None;
        }

        private void OnActionTriggered(SemanticAction action, string source)
        {
            if (action == SemanticAction.PauseToggle)
            {
                PauseToggleRequested?.Invoke();
                return;
            }
            if (!IsActive || _paused || _runtime == null) return;
            if (_runtime.CurrentTrial == null || _runtime.CurrentTrial.isWarmup) return;
            if (!DemoTaskFactory.IsActionForTask(_currentTask, action)) return;
            _runtime.SubmitAction(action);
        }

        private void OnStimulusShown(TaskTrialDefinition trial)
        {
            _activeTrialId = _blockId + "-trial-" + trial.index;
            if (TaskRunPolicy.PersistsTrials(_runKind) && _context != null)
            {
                _context.ActiveTrialId = _activeTrialId;
                _clock.BeginTrial();
            }

            int progress = trial.isWarmup ? 0 : Math.Max(1, trial.index - _warmupCount + 1);
            _tablet?.SetTrialProgress(progress, _scoreableTrialCount);
            _tablet?.ClearFeedback();
            _tablet?.ShowStimulus(trial.stimulus);
            if (_runKind == TaskRunKind.Practice && !string.IsNullOrWhiteSpace(trial.practiceCue))
                _tablet?.ShowTrialFeedback(trial.practiceCue, true);

            // Corsi: physical buttons must NOT accept input during the tablet
            // presentation (spec §18.2) — they open on ResponsePhaseStarted.
            TaskType activeControls = trial.isWarmup || _currentTask == TaskType.CorsiSequence
                ? TaskType.None : _currentTask;
            _input?.SetActiveTask(activeControls);
            _tablet?.SetActiveControls(activeControls);

            bool showRealHr = _heartRate.ActiveSource != null &&
                              _heartRate.ActiveSource.SourceType != HrSourceType.Simulated &&
                              _heartRate.IsReceiving;
            _tablet?.SetHrZone(_heartRate.CurrentZone, showRealHr,
                _heartRate.ActiveSource?.SourceType ?? HrSourceType.None);
        }

        private void OnStimulusHidden()
        {
            // Corsi: the map must stay visible through retention + response —
            // only the highlight dims (PositionsDimmed). Other tasks clear fully.
            if (_currentTask != TaskType.CorsiSequence) _tablet?.HideStimulus();
        }

        private void OnCorsiPositionLit(int positionIndex, int step, int total) =>
            _tablet?.ShowCorsiLit(positionIndex, step, total);

        private void OnCorsiPositionsDimmed() => _tablet?.DimCorsiCells();

        private void OnCorsiResponsePhaseStarted()
        {
            if (_paused || !IsActive) return;
            _input?.SetActiveTask(TaskType.CorsiSequence);
            _tablet?.SetActiveControls(TaskType.CorsiSequence);
            _tablet?.ShowCorsiResponseHint((_runtime as CorsiTaskRuntime)?.Backward ?? false);
        }

        private void OnTrialCompleted(TaskTrialResult result)
        {
            if (TaskRunPolicy.PersistsTrials(_runKind)) PersistTrial(result);

            string feedback = FeedbackFor(result, _currentTask);
            bool positive = result.Correct || result.Definition.isWarmup;
            _tablet?.ShowTrialFeedback(feedback, positive);
        }

        private void PersistTrial(TaskTrialResult result)
        {
            if (_context == null) return;
            var def = result.Definition;
            var record = new TrialRecord
            {
                trialId = _activeTrialId,
                sessionId = _context.SessionId,
                blockId = _blockId,
                taskType = _currentTask,
                trialIndexInBlock = def.index,
                isWarmup = def.isWarmup,
                stimulus = def.stimulus,
                expectedResponse = def.expectedAction.ToString(),
                actualResponse = result.ActualAction.ToString(),
                stimulusPresentedAtUtcIso = result.StimulusPresentedAtUtcIso,
                stimulusPresentedAtActiveSeconds = result.StimulusOnsetActiveSeconds,
                responseAtUtcIso = result.ResponseAtUtcIso,
                reactionTimeMs = result.ReactionTimeMs,
                correct = result.Correct,
                missed = result.Missed,
                falsePositive = result.FalsePositive,
                difficultyLevel = _level,
                pressureLevel = _context.PressureLevel,
                condition = _context.Condition,
                currentBpm = _heartRate.CurrentBpm,
                hrZone = _heartRate.CurrentZone,
                wasPausedDuringTrial = result.WasInterruptedByPause,
                remainingBlockTimeSeconds = -1,
                remainingGlobalTimeSeconds = _clock != null && _clock.HasGlobalTimer
                    ? _clock.RemainingGlobalSeconds : -1,
                sessionSeed = _context.MasterSeed,
                blockSeed = _seed
            };

            // Corsi reproduction detail — every field needed to reconstruct the
            // trial (spec §18.4). Empty/-1 for the other three tasks.
            if (result.Corsi != null)
            {
                var c = result.Corsi;
                record.corsiPresentedSequence = string.Join("-", c.PresentedSequence);
                record.corsiRequiredResponseOrder = string.Join("-", c.RequiredResponseOrder);
                record.corsiResponsePrefix = string.Join("-", c.ResponsePrefix);
                record.corsiSequenceLength = c.SequenceLength;
                record.corsiCorrectlyReproducedCount = c.CorrectlyReproducedCount;
                record.corsiFirstErrorIndex = c.FirstErrorIndex;
                record.corsiFirstWrongButtonId = c.FirstWrongButtonId;
                record.corsiStartResponseMs = c.StartResponseMs;
                record.corsiInterPressIntervalsMs = JoinMs(c.InterPressIntervalsMs);
                record.corsiTotalResponseMs = c.TotalResponseMs;
                record.corsiBackward = c.Backward;
                record.corsiAbandonedByInactivity = c.AbandonedByInactivity;
            }
            _writer?.Log(record);
        }

        private static string JoinMs(List<double> values)
        {
            if (values == null || values.Count == 0) return "";
            var sb = new System.Text.StringBuilder(values.Count * 6);
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(values[i].ToString("0", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private void OnBlockFinished()
        {
            if (_runtime == null) return;

            TaskType finishedTask = _currentTask;
            TaskRunKind finishedKind = _runKind;
            LastBlockResult = TaskBlockResult.Aggregate(
                _blockId, finishedTask, _level, _seed, _runtime.Results);
            var completed = CreateResult(LastBlockResult);
            LastResult = completed;

            if (TaskRunPolicy.PersistsTrials(finishedKind)) _writer?.Flush();
            IsActive = false;
            _paused = false;
            _activeTrialId = "";
            if (_context != null) _context.ActiveTrialId = "";
            _input?.SetGameplayInputEnabled(false);
            _input?.SetActiveTask(TaskType.None);
            _tablet?.SetActiveControls(TaskType.None);
            _tablet?.ShowInstruction(
                finishedKind == TaskRunKind.Practice ? "Vježba završena" : "Blok završen",
                $"{DemoTaskFactory.DisplayName(finishedTask)} · Tačnost: {LastBlockResult.Accuracy * 100f:0}%");

            UnsubscribeRuntime();
            _runtime = null;
            _currentTask = TaskType.None;
            BlockCompleted?.Invoke(completed);
        }

        private TaskRunnerResult CreateResult(TaskBlockResult block)
        {
            var result = new TaskRunnerResult { Block = block, RunKind = _runKind };
            if (_hrAggregationActive)
            {
                var hr = _heartRate.EndTaskAggregation();
                _hrAggregationActive = false;
                result.AverageBpm = hr.avgBpm;
                result.MaxBpm = hr.maxBpm;
                result.ElevatedSeconds = hr.elevatedSeconds;
                result.HrSampleCount = hr.sampleCount;
            }
            return result;
        }

        private static string FeedbackFor(TaskTrialResult result, TaskType taskType)
        {
            if (result.Definition.isWarmup) return "Uvodni simbol - zapamti ga";
            if (result.Correct) return "Tačno";
            if (taskType == TaskType.CorsiSequence && result.Corsi != null)
            {
                if (result.Corsi.FirstErrorIndex >= 0)
                    return $"Pogrešna pozicija ({result.Corsi.CorrectlyReproducedCount}/{result.Corsi.SequenceLength} tačno)";
                if (result.Corsi.AbandonedByInactivity)
                    return "Pokušaj prekinut zbog neaktivnosti";
                if (result.Corsi.TimedOut) return "Vrijeme je isteklo";
            }
            if (result.Missed) return "Vrijeme je isteklo";
            if (taskType == TaskType.GoNoGo &&
                result.Definition.expectedAction == SemanticAction.None &&
                result.ActualAction == SemanticAction.Go)
                return "Ne pritiskaj na NO GO";
            if (taskType == TaskType.Flanker) return "Prati centralnu strelicu";
            return "Netačno";
        }

        private void OnBlockAborted()
        {
            IsActive = false;
            _tablet?.HideStimulus();
            _tablet?.ClearFeedback();
        }

        private void UnsubscribeRuntime()
        {
            if (_runtime == null) return;
            _runtime.StimulusShown -= OnStimulusShown;
            _runtime.StimulusHidden -= OnStimulusHidden;
            _runtime.TrialCompleted -= OnTrialCompleted;
            _runtime.BlockFinished -= OnBlockFinished;
            _runtime.BlockAborted -= OnBlockAborted;
            if (_runtime is CorsiTaskRuntime corsi)
            {
                corsi.PositionLit -= OnCorsiPositionLit;
                corsi.PositionsDimmed -= OnCorsiPositionsDimmed;
                corsi.ResponsePhaseStarted -= OnCorsiResponsePhaseStarted;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Abort();
            if (_input != null) _input.ActionTriggered -= OnActionTriggered;
            _disposed = true;
        }
    }
}
