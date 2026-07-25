using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.HR;
using StressTraining.Persistence;
using StressTraining.Pressure;
using StressTraining.Questionnaires;
using StressTraining.Tasks;
using StressTraining.Tablet;
using StressTraining.UI;

namespace StressTraining.Session
{
    /// <summary>Presentation stages of the production session (spec §9).</summary>
    public enum ProductionFlowStage
    {
        None = 0,
        ScheduleEligibility = 1,
        PreSessionSSQ = 2,
        PreCycleStai = 3,
        BaselinePreparation = 4,
        /// <summary>
        /// Guided breathing AND the reference HR measurement, as ONE phase sharing
        /// one interval (config baseline duration). PROJECT-DEFINED BREATHING-ASSISTED
        /// REFERENCE — not a neutral resting baseline. Value kept at 5.
        /// </summary>
        BreathingReferenceBaseline = 5,
        /// <summary>Legacy: breathing was a separate stage before 2026-07-14. Never entered now.</summary>
        CopingPreparation = 6,
        TaskTutorialDecision = 7,
        Ready = 8,
        BetweenBlocks = 9,
        ActiveBlock = 10,
        SessionEnding = 11,
        Recovery = 12,
        PostSessionSSQ = 13,
        NasaTlx = 14,
        PostCycleStai = 15,
        Validity = 16,
        Adaptation = 17,
        Save = 18,
        Summary = 19
    }

    /// <summary>
    /// Production session orchestration, strictly SUBORDINATE to
    /// SessionCoordinator (spec §9): the coordinator owns pause/resume, the app
    /// state machine and ticking; this class sequences the production stages,
    /// owns the production writers/summary and runs the continuous 9-block
    /// / 3-round loop (the user is NOT returned to the SafeSpace between blocks).
    ///
    /// FAZA 2 skeleton status:
    /// - questionnaire stages are wired through PLACEHOLDER info screens and
    ///   are replaced by the real QuestionnaireFlowController in FAZA 5;
    /// - the scheduler decision is attached in FAZA 6 (levels stay unchanged);
    /// - the pressure system attaches to the same global timer in FAZA 7.
    /// </summary>
    public sealed class ProductionSessionFlow : IDisposable
    {
        private readonly StressTrainingConfig _config;
        private readonly AppStateMachine _state;
        private readonly AppErrorService _errors;
        private readonly PersistencePaths _paths;
        private readonly ProfileRepository _profiles;
        private readonly SessionRepository _sessions;
        private readonly UserProfileService _profileService;
        private readonly HeartRateService _heartRate;
        private readonly TaskRunner _taskRunner;
        private readonly SceneZoneController _zones;
        private readonly UIManager _ui;
        private readonly TabletDisplayController _tablet;
        private readonly InfoPanel _infoPanel;
        private readonly ProgressPanel _progressPanel;
        private readonly SessionClock _clock;
        private readonly PauseController _pause;
        private readonly SessionValidityEvaluator _validity = new SessionValidityEvaluator();
        private readonly QuestionnaireRepository _questionnaires;

        // Optional real questionnaire/coping panels (installed by AppBootstrapper);
        // when absent the flow falls back to labelled placeholders.
        public QuestionnairePanel QuestionnairePanel;
        public BreathingPanel BreathingPanel;

        private SessionQuestionnairesFile _questionnaireFile;
        private float _ssqPreTotal = -1f, _ssqPostTotal = -1f;
        private QuestionnaireResultData _tlxResult;
        private Action _questionnaireDone;
        private QuestionnaireFlowController _activeQuestionnaireFlow;
        private bool _preSessionQuestionnairesCompleted;

        public bool IsActive { get; private set; }
        public ProductionFlowStage Stage { get; private set; } = ProductionFlowStage.None;
        public ActiveSessionContext Context { get; private set; }
        public SessionSummaryData Summary { get; private set; }
        public SessionPlan Plan { get; private set; }

        public event Action<ProductionFlowStage> StageChanged;
        public event Action<SessionSummaryData> Finished;

        /// <summary>
        /// Installed by AppBootstrapper as a compatibility callback: revalidates
        /// the fixed production console layout and re-scans the input router.
        /// The seed is retained for API/persistence compatibility but does not
        /// randomize the Phase 8.5 console or Corsi geometry.
        /// </summary>
        public Action<int> RebuildModularZones;

        /// <summary>Optional narration/subtitles (FAZA 9); every call is null-safe.</summary>
        public Audio.VoiceCueManager Voice;

        /// <summary>Deterministic pressure system (FAZA 7); neutral mode stays dormant.</summary>
        public PressureController PressureSystem { get; private set; }

        /// <summary>
        /// Loss consequence player (fade / devirtualize / real fall + hold). Injected by
        /// AppBootstrapper. On global time-out the production flow reproduces the dev
        /// preview: it plays this after the collapse finale, before finishing, so a
        /// real timed-out session and the preview end identically. Null-safe.
        /// </summary>
        public PlayerFallController FallController;

        private double _gameOverFinaleRemaining = -1;
        private bool _awaitingGameOverFinale;
        private bool _awaitingLossConsequence;
        private double _lossConsequenceRemaining = -1;
        private const double LossConsequenceSafetySeconds = 5.0;   // never hang if it fails to commit
        private bool _pressureEventsSubscribed;
        private bool _finishBlocksStarted;

        private TrialLogWriter _trialWriter;
        private HeartRateLogWriter _hrWriter;
        private EventLogWriter _eventWriter;

        private UserProfileData _profile;
        private bool _scheduleOverride;
        private string _scheduleOverrideReason = "";

        private int _nextBlockIndex;
        private readonly List<TaskRunnerResult> _blockResults = new List<TaskRunnerResult>(16);
        private double _stageTimer;
        private double _betweenBlockRemaining;
        private int _lastCountdownShown = -1;
        private bool _timeExpiredHandled;

        private BaselineAccumulator _baseline;
        private RecoveryEvaluator _recovery;
        private BaselineSummaryData _baselineSummary;
        private double _measureElapsed;
        private double _measureDuration;
        private bool _measuring;
        private bool _developerTestSession;
        private bool? _lastHrGateReady;
        private string _lastHrGateReason = "";
        private double _nextHrGateRefresh;

        public ProductionSessionFlow(StressTrainingConfig config, AppStateMachine state,
            AppErrorService errors, PersistencePaths paths, ProfileRepository profiles,
            SessionRepository sessions, UserProfileService profileService,
            HeartRateService heartRate, TaskRunner taskRunner, SceneZoneController zones,
            UIManager ui, TabletDisplayController tablet, InfoPanel infoPanel,
            ProgressPanel progressPanel, SessionClock clock, PauseController pause)
        {
            _config = config;
            _state = state;
            _errors = errors;
            _paths = paths;
            _profiles = profiles;
            _sessions = sessions;
            _profileService = profileService;
            _heartRate = heartRate;
            _taskRunner = taskRunner;
            _zones = zones;
            _ui = ui;
            _tablet = tablet;
            _infoPanel = infoPanel;
            _progressPanel = progressPanel;
            _clock = clock;
            _pause = pause;
            _pause.PauseChanged += OnPauseChanged;
            _questionnaires = new QuestionnaireRepository(paths, errors);
        }

        public void SetPressureSystem(PressureController pressureSystem)
        {
            if (ReferenceEquals(PressureSystem, pressureSystem)) return;
            UnsubscribePressureEvents();
            PressureSystem?.ResetPressure();
            PressureSystem = pressureSystem;
            PressureSystem?.SetPaused(_pause.IsPaused ||
                Context?.Clock.IsGlobalChallengeRunning != true);
            if (IsActive) SubscribePressureEvents();
        }

        private void OnPauseChanged(bool paused, string reason)
        {
            PressureSystem?.SetPaused(paused ||
                Context?.Clock.IsGlobalChallengeRunning != true);
        }

        private void SubscribePressureEvents()
        {
            if (_pressureEventsSubscribed || PressureSystem == null) return;
            PressureSystem.StageChanged += OnPressureStageChanged;
            PressureSystem.PressureEventFired += OnPressureEventFired;
            PressureSystem.GameOverFinaleComplete += OnPressureGameOverFinaleComplete;
            _pressureEventsSubscribed = true;
        }

        private void UnsubscribePressureEvents()
        {
            if (!_pressureEventsSubscribed || PressureSystem == null)
            {
                _pressureEventsSubscribed = false;
                return;
            }
            PressureSystem.StageChanged -= OnPressureStageChanged;
            PressureSystem.PressureEventFired -= OnPressureEventFired;
            PressureSystem.GameOverFinaleComplete -= OnPressureGameOverFinaleComplete;
            _pressureEventsSubscribed = false;
        }

        private void OnPressureStageChanged(PressureStage stage)
        {
            if (!IsActive || Summary == null) return;
            Summary.pressureStageAtEnd = stage.ToString();
            LogEvent("pressure_stage", "{\"stage\":\"" + stage + "\"}");
            switch (stage)
            {
                case PressureStage.Early: Voice?.Play(Audio.VoiceCueId.TimeWarning70); break;
                case PressureStage.Mid: Voice?.Play(Audio.VoiceCueId.TimeWarning50); break;
                case PressureStage.Late: Voice?.Play(Audio.VoiceCueId.TimeWarning30); break;
                case PressureStage.Critical:
                    Voice?.Play(Audio.VoiceCueId.TimeWarning10);
                    _tablet?.ShowTrialFeedback("Kritično vrijeme!", false);
                    break;
                case PressureStage.Expired: Voice?.Play(Audio.VoiceCueId.GameOver); break;
            }
        }

        private void OnPressureEventFired(PressureEventDefinition evt)
        {
            if (!IsActive || evt == null) return;
            string fraction = evt.atRemainingFraction.ToString("0.000",
                System.Globalization.CultureInfo.InvariantCulture);
            string magnitude = evt.magnitude.ToString("0.000",
                System.Globalization.CultureInfo.InvariantCulture);
            LogEvent("pressure_event", "{\"kind\":\"" + evt.kind +
                "\",\"remainingFraction\":" + fraction +
                ",\"magnitude\":" + magnitude + "}");
        }

        private void OnPressureGameOverFinaleComplete()
        {
            if (!IsActive || !_awaitingGameOverFinale || _finishBlocksStarted) return;
            _awaitingGameOverFinale = false;
            _gameOverFinaleRemaining = -1;
            LogEvent("pressure_finale_complete", "{}");
            BeginLossConsequenceThenFinish();
        }

        /// <summary>
        /// Reproduces the dev preview at the end of a real session: after the collapse
        /// finale, play the loss consequence (fade / devirtualize / real fall + hold)
        /// and only then finish. FinishBlocks is deferred (via _awaitingLossConsequence
        /// in Tick) so the room stays collapsed while it plays; PlayerLost then returns
        /// the participant to the Safe Space. With no FallController it finishes at once,
        /// exactly as before.
        /// </summary>
        private void BeginLossConsequenceThenFinish()
        {
            if (_finishBlocksStarted || _awaitingLossConsequence) return;
            if (FallController != null)
            {
                _awaitingLossConsequence = true;
                _lossConsequenceRemaining = LossConsequenceSafetySeconds;
                LogEvent("loss_consequence_begin",
                    "{\"mode\":\"" + FallController.CurrentMode + "\"}");
                FallController.TriggerLoss();
                return;
            }
            FinishBlocks(CompletionStatus.SystemTerminated,
                UserTerminationReason.None, timeExpired: true);
        }

        // ── real questionnaire administration (spec §11/§30) ─────────────

        /// <summary>
        /// Runs one or more real questionnaires through QuestionnairePanel.
        /// Results are stored in the session summary AND questionnaires.json;
        /// SSQ totals feed validity, TLX feeds the scheduler. Falls back to a
        /// labelled placeholder screen when the panel is not installed.
        /// </summary>
        private void RunQuestionnaires(List<QuestionnaireDefinitionData> definitions,
            QuestionnairePhase phase, string placeholderTitle, Action onDone)
        {
            if (QuestionnairePanel == null)
            {
                ShowPlaceholderQuestionnaire(placeholderTitle,
                    "Upitnik nije instaliran u UI (fallback).", onDone);
                return;
            }

            DetachQuestionnaireFlow();
            _activeQuestionnaireFlow = new QuestionnaireFlowController(definitions, phase);
            _activeQuestionnaireFlow.QuestionnaireCompleted += OnQuestionnaireResult;
            _questionnaireDone = onDone;
            QuestionnairePanel.Finished -= OnQuestionnairePanelFinished;
            QuestionnairePanel.Finished += OnQuestionnairePanelFinished;
            QuestionnairePanel.Begin(_activeQuestionnaireFlow);
            _ui?.ShowPanel(QuestionnairePanel);
        }

        private void OnQuestionnairePanelFinished()
        {
            QuestionnairePanel.Finished -= OnQuestionnairePanelFinished;
            DetachQuestionnaireFlow();
            var done = _questionnaireDone;
            _questionnaireDone = null;
            done?.Invoke();
        }

        private void DetachQuestionnaireFlow()
        {
            if (_activeQuestionnaireFlow == null) return;
            _activeQuestionnaireFlow.QuestionnaireCompleted -= OnQuestionnaireResult;
            _activeQuestionnaireFlow = null;
        }

        private void OnQuestionnaireResult(QuestionnaireResultData result)
        {
            _questionnaireFile ??= new SessionQuestionnairesFile { sessionId = Context.SessionId };
            _questionnaireFile.results.Add(result);
            _questionnaires.Save(Context.UserId, _questionnaireFile);
            Summary.questionnaireResults.Add(result);
            _sessions.SaveSessionSummary(Summary);

            switch (result.questionnaireId)
            {
                case QuestionnaireCatalog.SsqId:
                    if (result.phase == QuestionnairePhase.PreSession) _ssqPreTotal = result.totalScore;
                    else _ssqPostTotal = result.totalScore;
                    break;
                case QuestionnaireCatalog.TlxId:
                    _tlxResult = result;
                    break;
                case QuestionnaireCatalog.StaiId:
                    var cycle = _profile.cycleSummaries.Find(c => c.cycleId == _profile.activeCycleId);
                    if (cycle != null)
                    {
                        if (result.phase == QuestionnairePhase.CycleStart)
                            cycle.staiScoreCycleStart = result.totalScore;
                        else cycle.staiScoreCycleEnd = result.totalScore;
                        _profiles.SaveProfile(_profile);
                    }
                    break;
            }
            LogEvent("questionnaire_completed",
                "{\"id\":\"" + result.questionnaireId + "\",\"phase\":\"" + result.phase + "\"}");
        }

        // ── entry ────────────────────────────────────────────────────────

        /// <summary>
        /// Starts the production pre-session flow for the active profile.
        /// The schedule-override decision (early start) is made by the caller's
        /// UI; both the flag and the reason are persisted (spec §10).
        /// </summary>
        public void Start(UserProfileData profile, bool scheduleOverride, string overrideReason)
        {
            if (IsActive || profile == null) return;
            _profile = profile;
            _scheduleOverride = scheduleOverride;
            _scheduleOverrideReason = overrideReason ?? "";
            _timeExpiredHandled = false;
            _finishBlocksStarted = false;
            _awaitingGameOverFinale = false;
            _gameOverFinaleRemaining = -1;
            _awaitingLossConsequence = false;
            _lossConsequenceRemaining = -1;
            PressureSystem?.ResetPressure();
            _nextBlockIndex = 0;
            _blockResults.Clear();
            _baselineSummary = null;
            _questionnaireFile = null;
            _ssqPreTotal = -1f;
            _ssqPostTotal = -1f;
            _tlxResult = null;
            _developerTestSession = false;
            _preSessionQuestionnairesCompleted = false;
            _lastHrGateReady = null;
            _lastHrGateReason = "";
            _nextHrGateRefresh = 0;

            _profileService.EnsureActiveCycle(profile);

            // ── deterministic identity ───────────────────────────────────
            int masterSeed = _config.developer.forcedProductionMasterSeed != 0
                ? _config.developer.forcedProductionMasterSeed
                : TaskSeedService.NewMasterSeed();
            int selectionSeed = TaskSeedService.DeriveNamedSeed(masterSeed, TaskSeedService.SaltTaskSelection);
            TaskType previousOmitted = SeededConstrainedTaskSelector.ResolvePreviousOmitted(profile.sessionSummaries);
            var pool = EnabledPool();
            TaskSelectionResult selection;
            try
            {
                selection = SeededConstrainedTaskSelector.Select(selectionSeed, previousOmitted, pool);
            }
            catch (Exception ex)
            {
                _errors.Report("TASK_SELECTION_FAILED", "Izbor zadataka nije uspio.",
                    ex.Message, true, ErrorSessionImpact.Fatal, ex);
                return;
            }

            SessionCondition condition = _config.developer.productionUsePressureCondition
                ? SessionCondition.Pressure : SessionCondition.Neutral;
            Plan = ProductionSessionPlanGenerator.Generate(
                masterSeed, condition, profile.currentPressureLevel, selection,
                profile.GetTaskLevel,
                _config.session,
                _config.session.productionBlocksPerSelectedTask);

            // ── session context + persistence ────────────────────────────
            _pause.Reset();
            string sessionId = Guid.NewGuid().ToString("N");
            Context = new ActiveSessionContext(_clock, _pause)
            {
                SessionId = sessionId,
                UserId = profile.userId,
                CycleId = profile.activeCycleId,
                SessionNumberInCycle = profile.currentCycleSessionIndex + 1,
                StartedAtUtcIso = UtcTime.NowIso(),
                Condition = condition,
                MasterSeed = masterSeed,
                Plan = Plan,
                NBackLevel = profile.currentNBackLevel,
                GoNoGoLevel = profile.currentGoNoGoLevel,
                FlankerLevel = profile.currentFlankerLevel,
                CorsiLevel = profile.currentCorsiLevel,
                PressureLevel = profile.currentPressureLevel,
                ValidityStatus = ValidityStatus.Unknown
            };
            Context.Clock.StartSession();  // pre-session time counts as real, not global

            _sessions.CreateSessionDirectory(profile.userId, sessionId);
            _trialWriter = new TrialLogWriter(_paths.TrialsFile(profile.userId, sessionId));
            _hrWriter = new HeartRateLogWriter(_paths.HrFile(profile.userId, sessionId));
            _eventWriter = new EventLogWriter(_paths.EventsFile(profile.userId, sessionId));

            Summary = CreateInitialSummary(profile, selection);
            _sessions.SaveSessionSummary(Summary);

            _heartRate.AttachSession(sessionId, _hrWriter, GetHeartRateContext);
            _heartRate.SampleAccepted += OnHeartRateSample;
            _taskRunner.ConfigureSession(Context, _trialWriter);

            IsActive = true;
            SubscribePressureEvents();
            LogEvent("production_started",
                "{\"seed\":" + masterSeed + ",\"override\":" + (_scheduleOverride ? "true" : "false") + "}");

            // A real HR connection is established before the first pre-session
            // instrument. Once ready, recording remains attached continuously
            // through SSQ/STAI, baseline, coping, tutorial, tasks and recovery.
            EnterBaselinePreparation();
        }

        private List<TaskType> EnabledPool()
        {
            var pool = new List<TaskType>(4);
            if (_config.session.taskPoolNBackEnabled) pool.Add(TaskType.NBack);
            if (_config.session.taskPoolGoNoGoEnabled) pool.Add(TaskType.GoNoGo);
            if (_config.session.taskPoolFlankerEnabled) pool.Add(TaskType.Flanker);
            if (_config.session.taskPoolCorsiEnabled) pool.Add(TaskType.CorsiSequence);
            return pool;
        }

        private SessionSummaryData CreateInitialSummary(UserProfileData profile,
            TaskSelectionResult selection)
        {
            var s = new SessionSummaryData
            {
                sessionId = Context.SessionId,
                userId = profile.userId,
                cycleId = Context.CycleId,
                sessionNumberInCycle = Context.SessionNumberInCycle,
                startedAtUtcIso = Context.StartedAtUtcIso,
                completionStatus = CompletionStatus.Unknown,
                validityStatus = ValidityStatus.Unknown,
                condition = Context.Condition,
                pressureLevel = Context.PressureLevel,
                seed = Context.MasterSeed,
                isProductionSession = true,
                taskSelectionSeed = Plan.taskSelectionSeed,
                blockOrderSeed = Plan.blockOrderSeed,
                pressureSeed = Plan.pressureSeed,
                consoleLayoutSeed = Plan.consoleLayoutSeed,
                omittedTaskType = Plan.omittedTaskType,
                selectionAlgorithmVersion = Plan.selectionAlgorithmVersion,
                nominalPlanSeconds = Plan.nominalPlanSeconds,
                globalDifficultyTimeMultiplier = Plan.globalDifficultyTimeMultiplier,
                globalDurationSeconds = Plan.globalDurationSeconds,
                plannedRoundCount = SessionPlan.RoundCount,
                developerTestSession = false,
                schedulerEligible = false,
                scheduleOverride = _scheduleOverride,
                scheduleOverrideReason = _scheduleOverrideReason,
                appVersion = UnityEngine.Application.version,
                configVersion = _config.configVersion
            };
            s.selectedTaskTypes.AddRange(Plan.selectedTaskTypes);
            s.selectionReasonCodes.AddRange(Plan.selectionReasonCodes);
            foreach (var b in Plan.blocks) s.blockOrder.Add(b.blockId);
            if (selection != null) s.selectionReasonCodes.Add("previousOmittedResolved");
            return s;
        }

        // ── tick (driven by SessionCoordinator) ──────────────────────────

        public void Tick(double realDeltaSeconds)
        {
            if (!IsActive || realDeltaSeconds <= 0) return;
            if (_pause.IsPaused) return;   // every stage timer is pause-aware

            _stageTimer += realDeltaSeconds;

            // Normal completion comes from PressureController.GameOverFinaleComplete.
            // This timer is only a safety fallback and advances with active time.
            if (_awaitingGameOverFinale)
            {
                PressureSystem?.TickFinaleActive((float)realDeltaSeconds);
                if (!_awaitingGameOverFinale || _finishBlocksStarted) return;

                _gameOverFinaleRemaining -= realDeltaSeconds;
                if (_gameOverFinaleRemaining <= 0)
                {
                    _awaitingGameOverFinale = false;
                    LogEvent("pressure_finale_fallback_timeout", "{}");
                    BeginLossConsequenceThenFinish();
                }
                return;
            }

            // The loss consequence (fade/devirt/pad + hold) plays after the finale;
            // keep the collapsed room ticking and finish once it commits (or the safety
            // timeout elapses), so the session ends exactly like the preview.
            if (_awaitingLossConsequence)
            {
                PressureSystem?.TickFinaleActive((float)realDeltaSeconds);
                _lossConsequenceRemaining -= realDeltaSeconds;
                bool committed = FallController == null || !FallController.IsFalling;
                if (committed || _lossConsequenceRemaining <= 0)
                {
                    _awaitingLossConsequence = false;
                    LogEvent("loss_consequence_complete", "{}");
                    FinishBlocks(CompletionStatus.SystemTerminated,
                        UserTerminationReason.None, timeExpired: true);
                }
                return;
            }

            switch (Stage)
            {
                case ProductionFlowStage.BaselinePreparation:
                    if (_stageTimer >= _nextHrGateRefresh)
                    {
                        _nextHrGateRefresh = _stageTimer + 0.5;
                        RefreshProductionHrGate(false);
                    }
                    break;

                case ProductionFlowStage.BreathingReferenceBaseline:
                case ProductionFlowStage.Recovery:
                    TickQuietMeasurement(realDeltaSeconds);
                    break;

                case ProductionFlowStage.BetweenBlocks:
                    TickBetweenBlocks(realDeltaSeconds);
                    break;

                case ProductionFlowStage.ActiveBlock:
                    UpdateGlobalTimerDisplay();
                    TickPressure(realDeltaSeconds);
                    if (!_timeExpiredHandled && Context.Clock.GlobalTimeExpired)
                        HandleGlobalTimeExpired();
                    break;
            }
        }

        private void TickPressure(double dt)
        {
            if (PressureSystem == null || !PressureSystem.IsRunning || Context == null ||
                !Context.Clock.IsGlobalChallengeRunning) return;
            PressureSystem.TickActive((float)dt, Context.Clock.RemainingGlobalFraction);
        }

        // ── pre-session stages (placeholders → FAZA 5) ───────────────────

        // Pre-session order (2026-07-14):
        //   HR connect → [STAI-6 if first session of cycle] → BreathingReferenceBaseline
        //   → pre-session SSQ → tutorial (only when needed) → Ready.
        // The global challenge clock NEVER runs during any of this.
        private void EnterPreSessionQuestionnaireSequence()
        {
            _state.RequestTransition(AppState.PreSessionQuestionnaires, "production_pre_session");
            _tablet?.SetHeartRateRecordingActive(true);
            if (UserProfileService.ShouldAdministerCycleStartStai(_profile))
                EnterPreCycleStai();
            else
                BeginBreathingReference();
        }

        /// <summary>Pre-session SSQ — runs AFTER the breathing/reference phase.</summary>
        private void EnterPreSessionSsq()
        {
            SetStage(ProductionFlowStage.PreSessionSSQ);
            _tablet?.SetHeartRateRecordingActive(true);
            _tablet?.ShowIdle("HR snimanje aktivno · Priprema 2/2 — Kratki upitnik");
            RunQuestionnaires(
                new List<QuestionnaireDefinitionData> { QuestionnaireCatalog.BuildSsq() },
                QuestionnairePhase.PreSession,
                "Priprema 2/2 — Kratki upitnik",
                () =>
                {
                    _preSessionQuestionnairesCompleted = true;
                    EnterTutorialDecision();
                });
        }

        /// <summary>STAI-6 opens the FIRST session of a cycle, before everything else.</summary>
        private void EnterPreCycleStai()
        {
            SetStage(ProductionFlowStage.PreCycleStai);
            RunQuestionnaires(
                new List<QuestionnaireDefinitionData> { QuestionnaireCatalog.BuildStai6() },
                QuestionnairePhase.CycleStart,
                "STAI-6 — početak ciklusa",
                BeginBreathingReference);
        }

        private void EnterBaselinePreparation()
        {
            SetStage(ProductionFlowStage.BaselinePreparation);
            _state.RequestTransition(AppState.Preparation, "baseline_preparation");
            _lastHrGateReady = null;
            _lastHrGateReason = "";
            _nextHrGateRefresh = 0;
            RefreshProductionHrGate(true);
        }

        private void ContinueAfterHrGate()
        {
            if (!_heartRate.GetProductionReadiness().IsReady)
            {
                EnterBaselinePreparation();
                return;
            }

            _tablet?.SetHeartRateRecordingActive(true);
            // Pre-session order: [STAI if first] -> breathing/reference -> SSQ -> tutorial.
            if (_preSessionQuestionnairesCompleted) BeginBreathingReference();
            else EnterPreSessionQuestionnaireSequence();
        }

        /// <summary>
        /// Backs out of the pre-corridor preparation (e.g. the HR gate, which otherwise
        /// blocks until a real HR sample arrives) via the UI. Nothing has been scored or
        /// saved yet, so this is a clean cancel: tear down and return to profile
        /// selection — no session record, no recovery, no post-session questionnaires.
        /// Guarded to pre-corridor stages so it can never abandon a scored session.
        /// </summary>
        public void CancelPreSession()
        {
            if (!IsActive || _finishBlocksStarted || !IsPreCorridorStage()) return;
            LogEvent("production_cancelled_pre_session", "{\"stage\":\"" + Stage + "\"}");
            var summary = Summary;
            Cleanup();                  // IsActive=false, HR detached, pressure reset, clock stopped
            Finished?.Invoke(summary);  // → coordinator returns to profile selection
        }

        private bool IsPreCorridorStage()
        {
            switch (Stage)
            {
                case ProductionFlowStage.BaselinePreparation:
                case ProductionFlowStage.BreathingReferenceBaseline:
                case ProductionFlowStage.Ready:
                    return true;
                default:
                    return false;
            }
        }

        private void RefreshProductionHrGate(bool force)
        {
            if (Stage != ProductionFlowStage.BaselinePreparation) return;
            ProductionHrReadiness readiness = _heartRate.GetProductionReadiness();
            if (!force && _lastHrGateReady == readiness.IsReady &&
                string.Equals(_lastHrGateReason, readiness.Reason, StringComparison.Ordinal))
                return;

            _lastHrGateReady = readiness.IsReady;
            _lastHrGateReason = readiness.Reason ?? "";
            var options = new List<(string, Action)>();
            if (readiness.IsReady)
                options.Add((_preSessionQuestionnairesCompleted
                    ? "Ponovi referentno mjerenje"
                    : "Nastavi", ContinueAfterHrGate));
            // Always an exit: the HR gate blocks until a real sample arrives, so without
            // this it is a dead-end whenever the watch/relay is not sending yet.
            options.Add(("Nazad na profile", CancelPreSession));

            string body = readiness.IsReady
                ? "Mjerenje pulsa je povezano i snimanje ostaje aktivno kroz cijelu pripremu, " +
                  "aktivnu sesiju i oporavak.\n\n" +
                  (_preSessionQuestionnairesCompleted
                      ? "Slijedi ponovno vođeno disanje sa referentnim mjerenjem pulsa."
                      : "Slijedi: Priprema 1/2 — vođeno disanje sa mjerenjem referentnog pulsa, " +
                        "zatim Priprema 2/2 — kratki upitnik, pa trening.")
                : "Poveži uređaj za mjerenje pulsa.\n\n" + readiness.Reason +
                  "\n\nNastavak je zaključan dok stvarni mrežni HR izvor ne pošalje svjež validan uzorak.";

            _infoPanel?.Configure("Provjera mjerenja pulsa", body, options,
                readiness.IsReady ? "HR snimanje spremno." : "Čeka se stvarni HR signal.");
            ShowInfo();
        }

        private bool ShouldUseShortDeveloperBaseline()
        {
            return _config.developer.developerModeEnabled &&
                   _config.developer.useShortDevBaseline &&
                   _heartRate.GetProductionReadiness().IsReady;
        }

        private void MarkDeveloperTestSession()
        {
            if (_developerTestSession) return;
            _developerTestSession = true;
            Summary.developerTestSession = true;
            Summary.schedulerEligible = false;
            Summary.validityNotes.Add("DEVELOPER_TEST_NON_RESEARCH: shortened baseline with real HR source.");
            Summary.technicalWarnings.Add("Developer test session: scheduler/profile levels disabled.");
            _sessions.SaveSessionSummary(Summary);
        }

        private double MeasureDuration(bool baseline)
        {
            if (baseline)
                return ShouldUseShortDeveloperBaseline()
                    ? _config.developer.productionDevBaselineSeconds
                    : _config.baseline.durationSeconds;
            return _developerTestSession && _config.developer.useShortDevRecovery
                ? _config.developer.productionDevRecoverySeconds
                : _config.recovery.durationSeconds;
        }

        /// <summary>
        /// MERGED PHASE (2026-07-14): guided breathing AND the reference HR measurement
        /// run together over ONE interval — the existing baseline duration from config.
        /// Only samples collected here feed the session reference BPM.
        ///
        /// METHODOLOGY: this is a PROJECT-DEFINED BREATHING-ASSISTED REFERENCE, NOT a
        /// neutral/resting baseline — the participant breathes to a guided rhythm.
        /// No new scientific claim is made (see SCIENTIFIC_TRACEABILITY.md).
        ///
        /// A stale HR signal here NEVER pauses the phase and never blocks the user:
        /// the gap is simply absent from the accumulator and lands in the data-quality
        /// metrics (BaselineAccumulator / SessionValidityEvaluator decide usability).
        /// </summary>
        private void BeginBreathingReference()
        {
            if (!_heartRate.GetProductionReadiness().IsReady)
            {
                // Continuous recording stays attached, but the reference measurement
                // may only START from a confirmed real, fresh source.
                EnterBaselinePreparation();
                return;
            }
            if (ShouldUseShortDeveloperBaseline()) MarkDeveloperTestSession();

            SetStage(ProductionFlowStage.BreathingReferenceBaseline);
            _state.RequestTransition(AppState.Baseline, "breathing_reference_start");
            Voice?.Play(Audio.VoiceCueId.BaselineStart);

            _baseline = new BaselineAccumulator(_config.baseline);
            _measureDuration = MeasureDuration(true);   // existing baseline duration
            _measureElapsed = 0;
            _measuring = true;
            _tablet?.SetHeartRateRecordingActive(true);
            _tablet?.ShowIdle("HR snimanje aktivno · Priprema 1/2 — Disanje i mjerenje pulsa");

            bool firstOfCycle = UserProfileService.IsFirstSessionOfCycle(_profile);
            if (BreathingPanel != null)
            {
                // Breathing visuals run for exactly the measurement interval; the flow
                // (not the panel) ends the phase, so there is only ever one Continue.
                BreathingPanel.BeginReference(_config.breathing, (float)_measureDuration, firstOfCycle);
                _ui?.ShowPanel(BreathingPanel);
                return;
            }

            _progressPanel?.Configure("Priprema 1/2 — Vođeno disanje i mjerenje pulsa",
                "Prati ritam disanja. Samo uzorci iz ove faze ulaze u referentnu vrijednost pulsa.",
                _developerTestSession
                    ? "DEVELOPER TEST · non-research · bez scheduler uticaja"
                    : "Tačan BPM je vidljiv na satu samo tokom ove faze.");
            if (_progressPanel != null) _ui?.ShowPanel(_progressPanel);
        }

        private void TickQuietMeasurement(double dt)
        {
            if (!_measuring) return;
            _measureElapsed += dt;
            float t01 = (float)(_measureElapsed / Math.Max(1e-3, _measureDuration));

            if (Stage == ProductionFlowStage.BreathingReferenceBaseline && BreathingPanel != null)
                BreathingPanel.SetReferenceStatus(t01,
                    Math.Max(0, _measureDuration - _measureElapsed),
                    _heartRate.IsReceiving, _heartRate.CurrentBpm);
            else
                _progressPanel?.SetProgress(t01);

            if (_measureElapsed < _measureDuration) return;
            _measuring = false;

            if (Stage == ProductionFlowStage.BreathingReferenceBaseline) CompleteBreathingReference();
            else CompleteRecovery();
        }

        public static bool IsSchedulerEligibleForProductionSession(bool developerTest,
            HrSourceType sourceType, BaselineQuality baselineQuality)
        {
            return !developerTest && sourceType == HrSourceType.NetworkBridge &&
                   baselineQuality == BaselineQuality.Good;
        }

        /// <summary>
        /// Closes the merged breathing/reference phase and hands over to the
        /// pre-session SSQ. The reference BPM comes ONLY from this phase.
        /// </summary>
        private void CompleteBreathingReference()
        {
            _baselineSummary = _baseline.ComputeSummary((float)_measureElapsed);
            Summary.baseline = _baselineSummary;
            Summary.schedulerEligible = IsSchedulerEligibleForProductionSession(
                _developerTestSession,
                _heartRate.ActiveSource?.SourceType ?? HrSourceType.None,
                _baselineSummary.quality);
            _heartRate.ZoneEvaluator.SetBaseline(_baselineSummary.averageBpm);
            _sessions.SaveSessionSummary(Summary);
            LogEvent("breathing_reference_complete",
                "{\"quality\":\"" + _baselineSummary.quality + "\"}");

            bool tooWeak = _baselineSummary.quality != BaselineQuality.Good;
            if (tooWeak)
            {
                // Not enough valid/fresh samples: no value is invented — the existing
                // quality + SessionValidityEvaluator logic decides. Participant may retry.
                _infoPanel?.Configure("Referentno mjerenje nije validno",
                    "Mjerenje nije dalo dovoljno validnih i svježih uzoraka pulsa. " +
                    "Produkcijska sesija ne može nastaviti bez provjere kvaliteta reference.",
                    new List<(string, Action)>
                    {
                        ("Pokušaj ponovo", EnterBaselinePreparation)
                    },
                    "Nema skrivenog participant override-a.");
                ShowInfo();
                return;
            }
            EnterPreSessionSsq();
        }

        private readonly List<TaskType> _tutorialQueue = new List<TaskType>(3);
        private int _tutorialPageIndex;

        private void EnterTutorialDecision()
        {
            SetStage(ProductionFlowStage.TaskTutorialDecision);
            _tablet?.SetHeartRateRecordingActive(true);
            _tablet?.ShowIdle("HR snimanje aktivno · Safe Space · Uputstva");
            bool ok = _state.RequestTransition(AppState.Tutorial, "tutorial_decision");
            if (!ok) _state.RequestTransition(AppState.Ready, "tutorial_skipped");

            _tutorialQueue.Clear();
            _tutorialPageIndex = 0;
            foreach (var t in Plan.selectedTaskTypes)
                if (NeedsTutorial(t)) _tutorialQueue.Add(t);

            if (_tutorialQueue.Count == 0)
            {
                // Every selected task is already known AT ITS CURRENT LEVEL — no
                // page is shown before every block of a level already seen.
                MarkTutorialsSeenAndReady();
                return;
            }

            var names = _tutorialQueue.ConvertAll(t =>
                DemoTaskFactory.DisplayName(t) + " (nivo " + _profile.GetTaskLevel(t) + ")");
            _infoPanel?.Configure("Uputstva prije treninga",
                "Zadaci ove sesije: " +
                string.Join(", ", Plan.selectedTaskTypes.ConvertAll(DemoTaskFactory.DisplayName)) +
                "\nIzostavljen: " + DemoTaskFactory.DisplayName(Plan.omittedTaskType) +
                "\n\nNovo za tebe: " + string.Join(", ", names) +
                "\n\nSlijedi kratko uputstvo za svaki od njih.",
                new List<(string, Action)>
                {
                    ("Prikaži uputstva", ShowNextTutorialPage),
                    ("Vježbaj u demo režimu (izlaz iz sesije)", AbortToDemoSuggestion)
                },
                "Uputstvo se prikazuje samo prvi put za taj zadatak i taj nivo.");
            ShowInfo();
        }

        /// <summary>
        /// A tutorial page is due the first time a task is met, and again the first
        /// time a NEW LEVEL of it is met (the level is the version, see
        /// <see cref="TutorialVersionFor"/>). Blocks of an already-seen level show none.
        /// </summary>
        private bool NeedsTutorial(TaskType t)
        {
            var st = _profile.GetOrCreateTutorialState(t);
            return !st.taskTutorialCompleted ||
                   st.taskTutorialVersion < TutorialVersionFor(t);
        }

        private void ShowNextTutorialPage()
        {
            if (_tutorialPageIndex >= _tutorialQueue.Count)
            {
                MarkTutorialsSeenAndReady();
                return;
            }

            TaskType task = _tutorialQueue[_tutorialPageIndex];
            int level = _profile.GetTaskLevel(task);
            _tutorialPageIndex++;
            bool last = _tutorialPageIndex >= _tutorialQueue.Count;

            _infoPanel?.Configure(
                TaskInstructions.Title(task, level),
                TaskInstructions.Page(task, level) + "\n\nKontrole — " + ControlsHint(task),
                new List<(string, Action)>
                {
                    (last ? "Razumijem — nastavi" : "Dalje", ShowNextTutorialPage)
                },
                "Uputstvo " + _tutorialPageIndex + "/" + _tutorialQueue.Count);
            ShowInfo();
            LogEvent("tutorial_page", "{\"task\":\"" + task + "\",\"level\":" + level + "}");
        }

        /// <summary>
        /// The tutorial version IS the difficulty level. Every level of every task
        /// changes something real the participant must be told (window, mix, set
        /// size, no-go share, sequence length — and for Corsi level 3 the rule
        /// itself flips to backward reproduction).
        /// </summary>
        private int TutorialVersionFor(TaskType t) =>
            TaskDifficultyConfig.Clamp(_profile.GetTaskLevel(t));

        private void MarkTutorialsSeenAndReady()
        {
            foreach (var t in Plan.selectedTaskTypes)
            {
                var st = _profile.GetOrCreateTutorialState(t);
                st.taskTutorialCompleted = true;
                st.taskTutorialVersion = TutorialVersionFor(t);
                st.lastTutorialAtUtcIso = UtcTime.NowIso();
            }
            _profiles.SaveProfile(_profile);
            EnterReady();
        }

        private void AbortToDemoSuggestion()
        {
            // Controlled exit before the corridor: nothing scored yet.
            FinishAborted(UserTerminationReason.UserRequested, "left_for_demo_practice");
        }

        private void EnterReady()
        {
            SetStage(ProductionFlowStage.Ready);
            if (_state.Current != AppState.Ready)
                _state.RequestTransition(AppState.Ready, "production_ready");
            int budgetSeconds = (int)Math.Ceiling(Plan.globalDurationSeconds);
            string budgetText = $"{budgetSeconds / 60}:{budgetSeconds % 60:00}";
            _infoPanel?.Configure("Spreman/na?",
                $"Sesija: 9 blokova u 3 runde, bez povratka u mirni prostor između blokova.\n" +
                $"Globalni aktivni budžet: {budgetText} — prikazano odvojeno na tabletu.\n" +
                "Vrijeme ne teče tokom uputstva, prelaza, countdowna ili ručne pauze.\n" +
                "Pauza: dugme Y na LIJEVOM kontroleru (konzola nema PAUSE dugme).",
                new List<(string, Action)> { ("Uđi u hodnik", EnterCorridor) },
                "Težina se ne mijenja tokom sesije.");
            ShowInfo();
        }

        private void EnterCorridor()
        {
            _state.RequestTransition(AppState.TransitionToCorridor, "enter_corridor");
            try
            {
                RebuildModularZones?.Invoke(Plan.consoleLayoutSeed); // compatibility hook: production cleanup only
            }
            catch (Exception ex)
            {
                _errors.Report("MODULAR_ZONE_BUILD_FAILED",
                    "Produkcijski raspored konzole nije mogao biti potvrđen.",
                    ex.Message, true, ErrorSessionImpact.None, ex);
            }
            _zones?.EnterCorridor();
            _ui?.SetAnchor(_zones?.CorridorUiAnchor);
            _ui?.HideAll();

            // Pressure only in the Pressure condition; the neutral corridor stays
            // intact. The condition is NEVER skipped silently — a Neutral session
            // showing no collapse is correct and is logged as such; a Pressure
            // session that cannot arm the controller is a real error.
            if (PressureController.ShouldRunFor(Context.Condition))
            {
                if (PressureSystem == null || !PressureSystem.IsInitialized)
                {
                    _errors.Report("PRESSURE_NOT_INITIALIZED",
                        "Pritisak nije mogao biti pokrenut.",
                        "PressureController missing/uninitialized (corridorRoot or " +
                        "center-eye reference absent) — ControlledPressure session has no effects.",
                        true, ErrorSessionImpact.Degraded);
                    Summary.technicalWarnings.Add("ControlledPressure: PressureController nije inicijalizovan.");
                }
                else
                {
                    PressureSystem.Begin(Plan.pressureSeed, Context.PressureLevel);
                    PressureSystem.SetPaused(true);
                }
            }
            else
                PressureSystem?.ResetPressure();

            LogEvent("pressure_condition", "{\"condition\":\"" + Context.Condition +
                "\",\"level\":" + Context.PressureLevel +
                ",\"armed\":" + (PressureSystem != null && PressureSystem.IsRunning ? "true" : "false") + "}");
            UnityEngine.Debug.Log("[Pressure] " + PressureDiagnostics());

            // Arm only. Countdown and task transition screens do not consume
            // the active challenge budget.
            Context.Clock.SetGlobalDuration(Plan.globalDurationSeconds);
            Context.Clock.SetGlobalChallengeRunning(false);
            Voice?.Play(Audio.VoiceCueId.SessionStart);
            _state.RequestTransition(AppState.ActiveSession, "production_session_start");
            LogEvent("global_timer_armed",
                "{\"durationSeconds\":" + Plan.globalDurationSeconds.ToString("0") + "}");
            EnterBetweenBlocks(firstBlock: true);
        }

        // ── block loop ───────────────────────────────────────────────────

        private void EnterBetweenBlocks(bool firstBlock)
        {
            Context?.Clock.SetGlobalChallengeRunning(false);
            PressureSystem?.SetPaused(true);
            SetStage(ProductionFlowStage.BetweenBlocks);
            var block = Plan.blocks[_nextBlockIndex];
            _tablet?.SetSessionProgress(_nextBlockIndex + 1, Plan.blocks.Count,
                block.RoundNumber, SessionPlan.RoundCount,
                DemoTaskFactory.DisplayName(block.taskType), block.difficultyLevel);
            _betweenBlockRemaining = firstBlock
                ? Math.Max(3f, _config.session.breakBetweenBlocksSeconds * 0.5f)
                : _config.session.breakBetweenBlocksSeconds;
            _lastCountdownShown = -1;

            switch (block.taskType)
            {
                case TaskType.NBack: Voice?.Play(Audio.VoiceCueId.TaskTransitionNBack); break;
                case TaskType.GoNoGo: Voice?.Play(Audio.VoiceCueId.TaskTransitionGoNoGo); break;
                case TaskType.Flanker: Voice?.Play(Audio.VoiceCueId.TaskTransitionFlanker); break;
                case TaskType.CorsiSequence: Voice?.Play(Audio.VoiceCueId.TaskTransitionCorsi); break;
            }
            _tablet?.SetActiveControls(TaskType.None);
            _tablet?.ShowInstruction(
                $"Blok {_nextBlockIndex + 1}/{Plan.blocks.Count}",
                $"Sljedeći zadatak: {DemoTaskFactory.DisplayName(block.taskType)}\n" +
                ControlsHint(block.taskType) + "\nPripremi se…");
            UpdateGlobalTimerDisplay();
        }

        private static string ControlsHint(TaskType t)
        {
            switch (t)
            {
                case TaskType.NBack: return "Kontrole: MATCH · NO MATCH";
                case TaskType.GoNoGo: return "Kontrola: GO";
                case TaskType.Flanker: return "Kontrole: LEFT · RIGHT";
                case TaskType.CorsiSequence: return "Kontrole: 9 pozicija sekvence";
                default: return "";
            }
        }

        private void TickBetweenBlocks(double dt)
        {
            UpdateGlobalTimerDisplay();
            if (!_timeExpiredHandled && Context.Clock.GlobalTimeExpired)
            {
                HandleGlobalTimeExpired();
                return;
            }

            _betweenBlockRemaining -= dt;
            int shown = (int)Math.Ceiling(Math.Max(0, _betweenBlockRemaining));
            if (shown != _lastCountdownShown && shown <= 3 && shown >= 1)
            {
                _lastCountdownShown = shown;
                _tablet?.ShowCountdown(shown);
            }
            if (_betweenBlockRemaining > 0) return;

            _tablet?.HideCountdown();
            StartPlannedBlock();
        }

        private void StartPlannedBlock()
        {
            var block = Plan.blocks[_nextBlockIndex];
            SetStage(ProductionFlowStage.ActiveBlock);
            bool firstStart = !Context.Clock.GlobalChallengeStarted;
            Context.Clock.SetGlobalChallengeRunning(true);
            PressureSystem?.SetPaused(_pause.IsPaused);
            if (firstStart)
                LogEvent("global_timer_started", "{}");
            Context.CurrentBlockIndex = _nextBlockIndex;
            LogEvent("block_started", "{\"blockId\":\"" + block.blockId + "\"}");
            _taskRunner.StartScoredPlannedBlock(block);
            _tablet?.SetSessionProgress(_nextBlockIndex + 1, Plan.blocks.Count,
                block.RoundNumber, SessionPlan.RoundCount,
                DemoTaskFactory.DisplayName(block.taskType), block.difficultyLevel);
        }

        /// <summary>Forwarded by SessionCoordinator when a production block completes.</summary>
        public void OnBlockCompleted(TaskRunnerResult result)
        {
            if (!IsActive || _finishBlocksStarted || _timeExpiredHandled ||
                result?.Block == null) return;
            _blockResults.Add(result);
            Context.Clock.SetGlobalChallengeRunning(false);
            PressureSystem?.SetPaused(true);
            LogEvent("block_completed",
                "{\"blockId\":\"" + result.Block.BlockId + "\",\"accuracy\":" +
                result.Block.Accuracy.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}");

            _nextBlockIndex++;
            if (Context.Clock.GlobalTimeExpired) { HandleGlobalTimeExpired(); return; }
            if (_nextBlockIndex >= Plan.blocks.Count)
            {
                FinishBlocks(CompletionStatus.Completed, UserTerminationReason.None);
                return;
            }
            EnterBetweenBlocks(firstBlock: false);
        }

        private void UpdateGlobalTimerDisplay()
        {
            if (Context == null || !Context.Clock.HasGlobalTimer) return;
            _tablet?.SetTimer(Context.Clock.RemainingGlobalSeconds);
        }

        private void HandleGlobalTimeExpired()
        {
            if (_timeExpiredHandled || _finishBlocksStarted) return;
            _timeExpiredHandled = true;
            LogEvent("global_time_expired", "{}");
            _taskRunner.Abort();
            var partial = _taskRunner.LastResult;
            if (partial?.Block != null && partial.Block.Trials.Count > 0)
                _blockResults.Add(partial);
            Summary.pressureStageAtEnd = PressureStage.Expired.ToString();
            _tablet?.ShowInstruction("Vrijeme je isteklo", "Sesija se završava…");

            if (PressureSystem != null && PressureSystem.IsRunning &&
                PressureSystem.TriggerGameOverFinale())
            {
                _awaitingGameOverFinale = true;
                _gameOverFinaleRemaining =
                    PressureHeuristics.FinaleSafetyFallbackSeconds;
                return;
            }

            FinishBlocks(CompletionStatus.SystemTerminated, UserTerminationReason.None,
                timeExpired: true);
        }

        /// <summary>User chose End Session from the pause menu (forwarded by the coordinator).</summary>
        public void HandleUserTermination(UserTerminationReason reason)
        {
            if (!IsActive || _finishBlocksStarted) return;
            _taskRunner.Abort();
            var partial = _taskRunner.LastResult;
            if (partial?.Block != null && partial.Block.Trials.Count > 0)
                _blockResults.Add(partial);
            FinishBlocks(CompletionStatus.UserTerminated, reason);
        }

        private void FinishAborted(UserTerminationReason reason, string note)
        {
            LogEvent("production_aborted_pre_corridor", "{\"note\":\"" + note + "\"}");
            FinishBlocks(CompletionStatus.UserTerminated, reason, preCorridor: true);
        }

        // ── ending / recovery / post stages ──────────────────────────────

        private void FinishBlocks(CompletionStatus status, UserTerminationReason reason,
            bool timeExpired = false, bool preCorridor = false)
        {
            if (_finishBlocksStarted || !IsActive || Context == null || Summary == null) return;
            _finishBlocksStarted = true;
            _awaitingGameOverFinale = false;
            _gameOverFinaleRemaining = -1;
            _awaitingLossConsequence = false;
            _lossConsequenceRemaining = -1;

            Context.Clock.StopGlobalChallenge();
            Summary.pressureStageAtEnd = PressureController.ShouldRunFor(Context.Condition)
                ? (PressureSystem?.CurrentStage.ToString() ?? Summary.pressureStageAtEnd)
                : "Neutral";
            PressureSystem?.ResetPressure();
            SetStage(ProductionFlowStage.SessionEnding);
            if (_pause.IsPaused) _pause.Resume("session_ending");
            if (_state.Current == AppState.ActiveSession || _state.Current == AppState.Paused)
                _state.RequestTransition(AppState.SessionEnding, "production_session_ending");

            Context.CompletionStatus = status;
            Context.UserTerminationReason = reason;
            Summary.completionStatus = status;
            Summary.userTerminationReason = reason;
            Summary.endedAtUtcIso = UtcTime.NowIso();
            Summary.totalActiveSeconds = Context.Clock.ActiveElapsedSeconds;
            Summary.globalChallengeElapsedSeconds = (float)Context.Clock.GlobalElapsedSeconds;
            Summary.totalPausedSeconds = Context.Clock.PausedElapsedSeconds;
            Summary.pauseCount = Context.Clock.PauseCount;
            Summary.remainingGlobalSecondsAtEnd = Context.Clock.HasGlobalTimer
                ? (float)Context.Clock.RemainingGlobalSeconds : -1f;
            BuildTaskSummaries();

            _trialWriter?.Flush();
            _eventWriter?.Flush();
            _hrWriter?.Flush();
            _sessions.SaveSessionSummary(Summary);

            _timeExpiredForValidity = timeExpired;

            // Return to SafeSpace, then quiet HR recovery (skip for pre-corridor aborts).
            _zones?.EnterSafeSpace();
            _ui?.SetAnchor(_zones?.SafeSpaceUiAnchor);
            _tablet?.SetActiveControls(TaskType.None);
            _tablet?.ShowIdle("Sesija je završena.");

            if (preCorridor)
            {
                _state.ForceTransition(AppState.Recovery, "pre_corridor_abort_recovery_skip");
                EnterPostSsq();
                return;
            }
            BeginRecovery();
        }

        private bool _timeExpiredForValidity;

        private void BeginRecovery()
        {
            SetStage(ProductionFlowStage.Recovery);
            if (_state.Current != AppState.Recovery)
                _state.RequestTransition(AppState.Recovery, "recovery_start");
            Voice?.Play(Audio.VoiceCueId.RecoveryStart);
            float baselineAvg = _baselineSummary?.averageBpm ?? -1f;
            _recovery = new RecoveryEvaluator(_config.recovery, baselineAvg, Context.SessionPeakBpm);
            _measureDuration = MeasureDuration(false);
            _measureElapsed = 0;
            _measuring = true;
            _tablet?.SetHeartRateRecordingActive(true);
            _tablet?.ShowIdle("HR snimanje aktivno · Oporavak");
            _progressPanel?.Configure("Oporavak — mirno mjerenje",
                "Sesija je gotova. HR snimanje ostaje aktivno. Miruj i prirodno diši tokom oporavka.",
                "Bez prikaza brojčanih vrijednosti.");
            if (_progressPanel != null) _ui?.ShowPanel(_progressPanel);
        }

        private void CompleteRecovery()
        {
            Summary.recovery = _recovery.ComputeSummary((float)_measureElapsed);
            _sessions.SaveSessionSummary(Summary);
            LogEvent("recovery_complete", "{}");
            EnterPostSsq();
        }

        private void EnterPostSsq()
        {
            SetStage(ProductionFlowStage.PostSessionSSQ);
            if (_state.Current != AppState.PostSessionQuestionnaires)
                _state.RequestTransition(AppState.PostSessionQuestionnaires, "post_questionnaires");
            Voice?.Play(Audio.VoiceCueId.PostSSQ);
            RunQuestionnaires(
                new List<QuestionnaireDefinitionData> { QuestionnaireCatalog.BuildSsq() },
                QuestionnairePhase.PostSession,
                "SSQ — poslije sesije",
                EnterNasaTlx);
        }

        private void EnterNasaTlx()
        {
            SetStage(ProductionFlowStage.NasaTlx);
            RunQuestionnaires(
                new List<QuestionnaireDefinitionData> { QuestionnaireCatalog.BuildNasaTlx() },
                QuestionnairePhase.PostSession,
                "NASA-TLX",
                () =>
                {
                    if (UserProfileService.ShouldAdministerCycleEndStai(_profile)) EnterPostCycleStai();
                    else EnterValidity();
                });
        }

        private void EnterPostCycleStai()
        {
            SetStage(ProductionFlowStage.PostCycleStai);
            RunQuestionnaires(
                new List<QuestionnaireDefinitionData> { QuestionnaireCatalog.BuildStai6() },
                QuestionnairePhase.CycleEnd,
                "STAI-6 — kraj ciklusa",
                EnterValidity);
        }

        private void EnterValidity()
        {
            SetStage(ProductionFlowStage.Validity);
            if (_developerTestSession)
            {
                Summary.validityStatus = ValidityStatus.DemoOnly;
                Summary.schedulerEligible = false;
                Context.ValidityStatus = Summary.validityStatus;
                _sessions.SaveSessionSummary(Summary);
                EnterAdaptation();
                return;
            }

            var input = new SessionValidityEvaluator.Input
            {
                IsDemoPlan = false,
                CompletionStatus = Summary.completionStatus,
                UserReason = Summary.userTerminationReason,
                SystemIssues = Summary.systemDetectedIssues,
                SsqPreTotal = _ssqPreTotal,
                SsqPostTotal = _ssqPostTotal,
                HrWasExpected = _heartRate.ActiveSource?.SourceType == HrSourceType.NetworkBridge,
                HrValidSampleRatio = _heartRate.ValidSampleRatio,
                TechnicalWarningCount = Summary.technicalWarnings.Count,
                TimeExpired = _timeExpiredForValidity,
                ScheduleOverride = _scheduleOverride
            };
            Summary.validityStatus = _validity.Evaluate(input, Summary.validityNotes);
            Context.ValidityStatus = Summary.validityStatus;
            _sessions.SaveSessionSummary(Summary);
            EnterAdaptation();
        }

        private void EnterAdaptation()
        {
            SetStage(ProductionFlowStage.Adaptation);
            if (_state.Current == AppState.PostSessionQuestionnaires)
                _state.RequestTransition(AppState.AdaptationReview, "adaptation_review");

            if (!Summary.schedulerEligible)
            {
                Summary.schedulerDecision = null;
                LogEvent("adaptation_skipped", "{\"reason\":\"developer_test_or_hr_invalid\"}");
                _sessions.SaveSessionSummary(Summary);
                ShowAdaptationExplanation();
                return;
            }

            try
            {
                var scheduler = new Adaptation.AdaptationScheduler();
                Summary.schedulerDecision = scheduler.Decide(BuildAdaptationInput());
                LogEvent("adaptation_decided",
                    "{\"rules\":" + Summary.schedulerDecision.firedRules.Count + "}");
            }
            catch (Exception ex)
            {
                Summary.schedulerDecision = null;
                _errors.Report("SCHEDULER_FAILED", "Prilagođavanje nije izračunato.",
                    ex.ToString(), true, ErrorSessionImpact.Degraded, ex);
            }
            _sessions.SaveSessionSummary(Summary);
            ShowAdaptationExplanation();
        }

        private Adaptation.AdaptationInput BuildAdaptationInput()
        {
            var input = new Adaptation.AdaptationInput
            {
                sessionId = Summary.sessionId,
                validityStatus = Summary.validityStatus,
                condition = Summary.condition,
                currentPressureLevel = _profile.currentPressureLevel,
                baselineBpm = _baselineSummary?.averageBpm ?? -1f,
                recoveryReachedZone = Summary.recovery?.reachedWorkingZone ?? false,
                recoverySeconds = Summary.recovery?.secondsToWorkingZone ?? -1f,
                timeExpired = _timeExpiredForValidity,
                scheduleOverride = _scheduleOverride,
                previousSessionCount = _profile.sessionSummaries.Count
            };

            if (_tlxResult != null)
            {
                input.tlxTotal = _tlxResult.totalScore;
                input.tlxMental = QuestionnaireScoringService.TlxDimension(_tlxResult, "tlx_mental");
                input.tlxPhysical = QuestionnaireScoringService.TlxDimension(_tlxResult, "tlx_physical");
                input.tlxTemporal = QuestionnaireScoringService.TlxDimension(_tlxResult, "tlx_temporal");
                input.tlxPerformance = QuestionnaireScoringService.TlxDimension(_tlxResult, "tlx_performance");
                input.tlxEffort = QuestionnaireScoringService.TlxDimension(_tlxResult, "tlx_effort");
                input.tlxFrustration = QuestionnaireScoringService.TlxDimension(_tlxResult, "tlx_frustration");
            }

            double totalElevated = 0;
            foreach (TaskType task in SeededConstrainedTaskSelector.FullPool)
            {
                var metrics = input.Metrics(task);
                metrics.currentLevel = _profile.GetTaskLevel(task);
                metrics.wasSelectedThisSession = Plan.selectedTaskTypes.Contains(task);
                if (!metrics.wasSelectedThisSession) continue;

                var ts = Summary.taskSummaries.Find(x => x.taskType == task);
                if (ts == null) continue;
                metrics.accuracy = ts.accuracy;
                metrics.meanReactionTimeMs = ts.meanReactionTimeMs;
                metrics.missCount = ts.missCount;
                metrics.falsePositiveCount = ts.falsePositiveCount;
                metrics.blockAccuracyStd = ts.accuracyStdAcrossBlocks;
                if (ts.avgBpmDuringTask > 0 && input.baselineBpm > 0)
                    metrics.avgBpmDelta = ts.avgBpmDuringTask - input.baselineBpm;
                totalElevated += ts.elevatedOrHighZoneSeconds;
                if (task == TaskType.CorsiSequence)
                {
                    metrics.corsiMaxCorrectSequenceLength = ts.corsiMaxCorrectSequenceLength;
                    metrics.corsiMeanCorrectPrefix = ts.corsiMeanCorrectPrefix;
                }
            }
            input.elevatedOrHighSeconds = totalElevated;
            input.totalActiveSeconds = Summary.totalActiveSeconds;
            return input;
        }

        /// <summary>
        /// Presentation only — the scheduler decision itself is never touched here.
        /// The old screen printed one long paragraph (AdaptationExplanationBuilder);
        /// this renders the same decision as one short line per task with the level
        /// before → after, and the reason underneath.
        /// </summary>
        private void ShowAdaptationExplanation()
        {
            SetStage(ProductionFlowStage.Save);   // explanation + save are one review step
            Voice?.Play(Audio.VoiceCueId.AdaptationExplanation);

            string explanation = Summary.schedulerDecision != null
                ? Adaptation.AdaptationExplanationBuilder.BuildCompact(Summary.schedulerDecision)
                : (!Summary.schedulerEligible
                    ? "Ova sesija nije korišćena za prilagođavanje.\nSvi nivoi ostaju nepromijenjeni."
                    : "Prilagođavanje nije izračunato (tehnička greška).\nSvi nivoi ostaju nepromijenjeni.");

            _infoPanel?.Configure("Prilagođavanje za narednu sesiju", explanation,
                new List<(string, Action)> { ("Nastavi", EnterSave) },
                "Odluka je zapisana uz sesiju.");
            ShowInfo();
        }

        /// <summary>Developer-only pressure chain state; never shown to a participant.</summary>
        public string PressureDiagnostics()
        {
            if (Context == null || Plan == null) return "Condition: — (nema aktivne sesije)";
            var sb = new System.Text.StringBuilder(220);
            sb.Append("Condition: ").Append(Context.Condition == SessionCondition.Pressure
                ? "ControlledPressure" : "Neutral");
            sb.Append(" · pressure nivo ").Append(Context.PressureLevel);

            if (PressureSystem == null)
            {
                sb.Append(" · PressureController: NEDOSTAJE");
                return sb.ToString();
            }

            sb.Append(" · controller ").Append(PressureSystem.IsInitialized ? "init" : "NIJE INIT");
            sb.Append(", ").Append(PressureSystem.IsRunning ? "aktivan" : "neaktivan");
            if (PressureSystem.IsPaused) sb.Append(" (pauza)");
            sb.Append(" · stage ").Append(PressureSystem.CurrentStage);
            sb.Append(" · intenzitet ").Append(PressureSystem.CurrentIntensity.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture));
            float remaining = PressureSystem.LastAppliedRemainingFraction;
            sb.Append(" · preostalo ").Append(remaining < 0 ? "—"
                : (remaining * 100f).ToString("0") + " %");
            sb.Append(" · segmenata ").Append(PressureSystem.ClosedSegmentPairCount)
              .Append('/').Append(PressureSystem.SegmentPairCount);
            sb.Append(" · pukotina ").Append(PressureSystem.CrackCount);
            sb.Append(" · svjetala ").Append(PressureSystem.BoundLightCount);
            sb.Append("\n  ").Append(PressureSystem.PuzzleActive
                ? PressureSystem.PuzzleDiagnostics
                : "PuzzleCorridor: neaktivan (proceduralni fallback)");
            return sb.ToString();
        }

        private void EnterSave()
        {
            SetStage(ProductionFlowStage.Save);
            if (!Summary.schedulerEligible)
            {
                _sessions.SaveSessionSummary(Summary);
                EnterSummary();
                return; // no profile history, schedule or level mutation
            }
            bool usable = Summary.validityStatus == ValidityStatus.Valid ||
                          Summary.validityStatus == ValidityStatus.ValidWithWarnings;
            if (usable && Summary.completionStatus == CompletionStatus.Completed)
            {
                _profileService.CompleteSession(_profile, Summary, Summary.schedulerDecision);
            }
            else if (_timeExpiredForValidity && !_developerTestSession)
            {
                // A real production attempt that reached TimeExpired still
                // completed recovery and post-session instruments, so it advances
                // the questionnaire cycle but never changes difficulty levels.
                _profileService.CompleteSession(_profile, Summary, null);
            }
            else
            {
                if (!_profile.sessionSummaries.Exists(x => x.sessionId == Summary.sessionId))
                    _profile.sessionSummaries.Add(Summary);
                _profile.lastSessionAtUtcIso = Summary.endedAtUtcIso;
                _profiles.SaveProfile(_profile);
            }
            EnterSummary();
        }

        private void EnterSummary()
        {
            SetStage(ProductionFlowStage.Summary);
            if (_state.Current != AppState.PostSessionSummary)
            {
                if (!_state.RequestTransition(AppState.PostSessionSummary, "production_summary"))
                    _state.ForceTransition(AppState.PostSessionSummary, "production_summary");
            }

            _infoPanel?.Configure("Sažetak sesije", BuildSummaryText(),
                new List<(string, Action)> { ("Nazad na profile", FinishToProfiles) },
                "Podaci su sačuvani.");
            ShowInfo();
            _tablet?.ShowIdle("Sesija je završena.");
        }

        private string BuildSummaryText()
        {
            var sb = new System.Text.StringBuilder(512);
            sb.Append("Status: ").Append(StatusText(Summary.completionStatus))
              .Append(" · Validnost: ").Append(Summary.validityStatus).Append('\n');
            sb.Append("Blokova završeno: ").Append(_blockResults.Count)
              .Append('/').Append(Plan.blocks.Count).Append('\n');
            if (Summary.remainingGlobalSecondsAtEnd >= 0)
                sb.Append("Preostalo globalno vrijeme: ")
                  .Append((int)Summary.remainingGlobalSecondsAtEnd).Append(" s\n");
            sb.Append('\n');
            foreach (var ts in Summary.taskSummaries)
                sb.Append(DemoTaskFactory.DisplayName(ts.taskType))
                  .Append(": tačnost ").Append((ts.accuracy * 100f).ToString("0"))
                  .Append("% (").Append(ts.blockCount).Append(" blokova)\n");
            sb.Append("\nPauza: ").Append(Summary.pauseCount).Append("× · ")
              .Append((int)Summary.totalPausedSeconds).Append(" s\n");
            sb.Append("Naredna preporučena sesija: ")
              .Append(string.IsNullOrEmpty(_profile.nextRecommendedSessionAtUtcIso)
                  ? "—" : _profile.nextRecommendedSessionAtUtcIso).Append('\n');
            sb.Append(BuildPlanIndicators());
            if (_config.developer.developerModeEnabled)
                sb.Append("\n[dev] ").Append(PressureDiagnostics()).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// Level indicators (spec §6) — shown ONLY on the post-session summary /
        /// developer surface, never to the participant during an active task.
        /// Reports the plan that RAN and the decision generated for the NEXT session.
        /// </summary>
        public string BuildPlanIndicators()
        {
            var sb = new System.Text.StringBuilder(512);

            sb.Append("\n── TRENUTNA SESIJA ──\n");
            // The condition is stated in full so a session with no visible collapse
            // can be told apart from a broken pressure chain at a glance.
            sb.Append("Uslov: ").Append(Context != null && Context.Condition == SessionCondition.Pressure
                ? "ControlledPressure" : "Neutral");
            sb.Append(" · pritisak nivo ").Append(Plan.pressureLevel).Append('\n');
            sb.Append("Plan: ").Append(Summary.schedulerDecision != null || _profile.sessionSummaries.Count > 1
                ? "scheduler-generated" : "default")
              .Append(" · seed ").Append(Plan.masterSeed)
              .Append(" · budžet ").Append((int)Plan.globalDurationSeconds).Append(" s\n");
            foreach (TaskType t in Plan.selectedTaskTypes)
                sb.Append("  • ").Append(DemoTaskFactory.DisplayName(t))
                  .Append(" — nivo ").Append(LevelThatRan(t)).Append('\n');

            sb.Append("\n── PRILAGOĐAVANJE ZA NAREDNU SESIJU ──\n");
            AdaptationDecisionData d = Summary.schedulerDecision;
            if (d == null)
            {
                sb.Append(Summary.schedulerEligible
                    ? "Nije generisano."
                    : "Odbačeno — podaci nisu validni za prilagođavanje.").Append('\n');
                sb.Append("Svi nivoi ostaju nepromijenjeni.\n");
                return sb.ToString();
            }

            bool applied = _profile.sessionSummaries.Exists(x => x.sessionId == Summary.sessionId) &&
                           (Summary.validityStatus == ValidityStatus.Valid ||
                            Summary.validityStatus == ValidityStatus.ValidWithWarnings) &&
                           Summary.completionStatus == CompletionStatus.Completed;
            sb.Append("Status: ").Append(applied ? "primijenjeno" : "na čekanju").Append('\n');
            sb.Append(Adaptation.AdaptationExplanationBuilder.BuildCompact(d));
            if (d.firedRules != null && d.firedRules.Count > 0)
                sb.Append("\nSignali: ").Append(string.Join(", ", d.firedRules)).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// The level the plan ACTUALLY ran at. Read from the plan, not from the
        /// profile: by the time the summary renders, EnterSave has already written
        /// the NEXT session's levels onto the profile.
        /// </summary>
        private int LevelThatRan(TaskType t)
        {
            foreach (var block in Plan.blocks)
                if (block.taskType == t) return block.difficultyLevel;
            return _profile.GetTaskLevel(t);
        }

        private static string StatusText(CompletionStatus s)
        {
            switch (s)
            {
                case CompletionStatus.Completed: return "završena u cjelini";
                case CompletionStatus.UserTerminated: return "prekinuta na zahtjev";
                case CompletionStatus.SystemTerminated: return "isteklo vrijeme";
                default: return s.ToString();
            }
        }

        private void FinishToProfiles()
        {
            Voice?.Play(Audio.VoiceCueId.Goodbye);
            var summary = Summary;
            Cleanup();
            Finished?.Invoke(summary);
        }

        // ── helpers ──────────────────────────────────────────────────────

        private void BuildTaskSummaries()
        {
            Summary.taskSummaries.Clear();
            foreach (var task in Plan.selectedTaskTypes)
            {
                var blocks = _blockResults.FindAll(r => r.Block.TaskType == task);
                if (blocks.Count == 0) continue;
                var ts = new TaskSessionSummaryData
                {
                    taskType = task,
                    difficultyLevel = blocks[0].Block.DifficultyLevel,
                    blockCount = blocks.Count
                };

                var accuracies = new List<float>();
                float rtSum = 0; int rtN = 0;
                float bpmMax = -1; double bpmSum = 0; int bpmN = 0; double elevated = 0;
                bool simulatedHr = _heartRate.ActiveSource?.SourceType == HrSourceType.Simulated;

                double cCorrect = 0, cTotal = 0, iCorrect = 0, iTotal = 0;
                double cRt = 0, iRt = 0; int cRtN = 0, iRtN = 0;
                int corsiMaxLen = -1; double corsiPrefixSum = 0; int corsiPrefixN = 0;

                foreach (var r in blocks)
                {
                    var b = r.Block;
                    ts.trialCount += b.ScoreableCount;
                    ts.correctCount += b.CorrectCount;
                    ts.missCount += b.MissCount;
                    ts.falsePositiveCount += b.FalsePositiveCount;
                    accuracies.Add(b.Accuracy);
                    if (b.MeanReactionTimeMs >= 0) { rtSum += b.MeanReactionTimeMs; rtN++; }

                    if (!simulatedHr && r.MaxBpm > bpmMax) bpmMax = r.MaxBpm;
                    if (!simulatedHr && r.AverageBpm > 0) { bpmSum += r.AverageBpm; bpmN++; }
                    if (!simulatedHr) elevated += r.ElevatedSeconds;

                    foreach (var trial in b.Trials)
                    {
                        if (trial.Definition.isWarmup) continue;
                        if (task == TaskType.Flanker)
                        {
                            bool cong = trial.Definition.congruency == "congruent";
                            bool incong = trial.Definition.congruency == "incongruent";
                            if (cong) { cTotal++; if (trial.Correct) { cCorrect++; if (trial.ReactionTimeMs >= 0) { cRt += trial.ReactionTimeMs; cRtN++; } } }
                            if (incong) { iTotal++; if (trial.Correct) { iCorrect++; if (trial.ReactionTimeMs >= 0) { iRt += trial.ReactionTimeMs; iRtN++; } } }
                        }
                        if (task == TaskType.CorsiSequence && trial.Corsi != null)
                        {
                            if (trial.Correct && trial.Corsi.SequenceLength > corsiMaxLen)
                                corsiMaxLen = trial.Corsi.SequenceLength;
                            corsiPrefixSum += trial.Corsi.CorrectlyReproducedCount;
                            corsiPrefixN++;
                        }
                    }
                }

                ts.accuracy = ts.trialCount > 0 ? (float)ts.correctCount / ts.trialCount : 0f;
                ts.meanReactionTimeMs = rtN > 0 ? rtSum / rtN : -1f;
                ts.accuracyStdAcrossBlocks = StdDev(accuracies);
                ts.maxBpmDuringTask = bpmMax;
                ts.avgBpmDuringTask = bpmN > 0 ? (float)(bpmSum / bpmN) : -1f;
                ts.elevatedOrHighZoneSeconds = elevated;

                if (task == TaskType.Flanker)
                {
                    ts.congruentAccuracy = cTotal > 0 ? (float)(cCorrect / cTotal) : -1f;
                    ts.incongruentAccuracy = iTotal > 0 ? (float)(iCorrect / iTotal) : -1f;
                    ts.congruentRtMs = cRtN > 0 ? (float)(cRt / cRtN) : -1f;
                    ts.incongruentRtMs = iRtN > 0 ? (float)(iRt / iRtN) : -1f;
                    ts.congruencyEffectMs = ts.congruentRtMs >= 0 && ts.incongruentRtMs >= 0
                        ? ts.incongruentRtMs - ts.congruentRtMs : 0f;
                }
                if (task == TaskType.CorsiSequence)
                {
                    ts.corsiMaxCorrectSequenceLength = corsiMaxLen;
                    ts.corsiMeanCorrectPrefix = corsiPrefixN > 0
                        ? (float)(corsiPrefixSum / corsiPrefixN) : -1f;
                }
                Summary.taskSummaries.Add(ts);
            }
        }

        private static float StdDev(List<float> values)
        {
            if (values.Count < 2) return 0f;
            float mean = 0;
            foreach (var v in values) mean += v;
            mean /= values.Count;
            float sq = 0;
            foreach (var v in values) sq += (v - mean) * (v - mean);
            return (float)Math.Sqrt(sq / values.Count);
        }

        private void ShowPlaceholderQuestionnaire(string title, string body, Action onContinue)
        {
            _infoPanel?.Configure(title,
                body + "\n\nNEEDS_VALIDATED_ITEM_TEXT — placeholder nije validirani instrument " +
                "i ne upisuje odgovore.",
                new List<(string, Action)> { ("Nastavi", onContinue) },
                "Kostur toka (faza 2); pravi upitnik u fazi 5.");
            ShowInfo();
        }

        private void ShowInfo()
        {
            if (_infoPanel != null) _ui?.ShowPanel(_infoPanel);
        }

        private void OnHeartRateSample(HeartRateSampleRecord sample)
        {
            if (!IsActive || Context == null) return;
            Context.HrConnected = _heartRate.IsReceiving;
            Context.CurrentHrZone = _heartRate.CurrentZone;
            Context.LatestBpm = sample.bpm;
            Context.SessionPeakBpm = _heartRate.SessionPeakBpm;

            if (Stage == ProductionFlowStage.BaselinePreparation)
                RefreshProductionHrGate(false);

            if (sample.isPaused) return;   // paused samples never enter measurements
            bool valid = sample.qualityStatus == HrQualityStatus.Good;
            // ONLY samples from the merged breathing/reference phase feed the
            // session reference BPM. HR keeps recording in every other phase.
            if (Stage == ProductionFlowStage.BreathingReferenceBaseline && _measuring)
                _baseline?.AddSample(sample.bpm, valid, _measureElapsed);
            else if (Stage == ProductionFlowStage.Recovery && _measuring)
                _recovery?.AddSample(sample.bpm, _measureElapsed);
        }

        private (bool, string, TaskType, string, string) GetHeartRateContext()
        {
            bool paused = _pause.IsPaused;
            TaskType task = _taskRunner.CurrentTask;
            string block = Context?.CurrentBlock?.blockId ?? "";
            string stateAndPhase = _state.Current + "|HR_PHASE=" +
                (Context?.HrPhase.ToString() ?? SessionHrPhase.None.ToString());
            return (paused, stateAndPhase, task, block, Context?.ActiveTrialId ?? "");
        }

        public static SessionHrPhase HrPhaseForStage(ProductionFlowStage stage)
        {
            switch (stage)
            {
                case ProductionFlowStage.BaselinePreparation: return SessionHrPhase.ConnectionSetup;
                case ProductionFlowStage.PreSessionSSQ:
                case ProductionFlowStage.PreCycleStai: return SessionHrPhase.PreSessionQuestionnaire;
                case ProductionFlowStage.BreathingReferenceBaseline:
                    return SessionHrPhase.BreathingReferenceBaseline;
                case ProductionFlowStage.CopingPreparation: return SessionHrPhase.CopingBreathing; // legacy
                case ProductionFlowStage.TaskTutorialDecision:
                case ProductionFlowStage.Ready: return SessionHrPhase.Tutorial;
                case ProductionFlowStage.BetweenBlocks: return SessionHrPhase.TaskTransition;
                case ProductionFlowStage.ActiveBlock: return SessionHrPhase.ActiveTask;
                case ProductionFlowStage.Recovery: return SessionHrPhase.Recovery;
                case ProductionFlowStage.PostSessionSSQ:
                case ProductionFlowStage.NasaTlx:
                case ProductionFlowStage.PostCycleStai: return SessionHrPhase.PostSessionQuestionnaire;
                case ProductionFlowStage.SessionEnding: return SessionHrPhase.SessionEnding;
                default: return SessionHrPhase.None;
            }
        }

        private void SetStage(ProductionFlowStage stage)
        {
            Stage = stage;
            if (Context != null) Context.HrPhase = HrPhaseForStage(stage);
            _stageTimer = 0;
            StageChanged?.Invoke(stage);
            LogEvent("production_stage", "{\"stage\":\"" + stage + "\"}");
        }

        private void LogEvent(string type, string payload)
        {
            _eventWriter?.Log(type, Context?.SessionId ?? "", _state.Current.ToString(),
                Context?.Clock.RealElapsedSeconds ?? 0, payload);
        }

        private void Cleanup()
        {
            IsActive = false;
            Stage = ProductionFlowStage.None;
            if (QuestionnairePanel != null)
                QuestionnairePanel.Finished -= OnQuestionnairePanelFinished;
            DetachQuestionnaireFlow();
            _questionnaireDone = null;
            PressureSystem?.ResetPressure();
            UnsubscribePressureEvents();
            _awaitingGameOverFinale = false;
            _gameOverFinaleRemaining = -1;
            _awaitingLossConsequence = false;
            _lossConsequenceRemaining = -1;
            _finishBlocksStarted = false;
            _tablet?.SetHeartRateRecordingActive(false);
            _heartRate.SampleAccepted -= OnHeartRateSample;
            _heartRate.DetachSession();
            Context?.Clock.StopSession();
            _trialWriter?.Dispose();
            _hrWriter?.Dispose();
            _eventWriter?.Dispose();
            _trialWriter = null;
            _hrWriter = null;
            _eventWriter = null;
            _measuring = false;
            Context = null;
            _profile = null;
        }

        public void Dispose()
        {
            _pause.PauseChanged -= OnPauseChanged;
            if (IsActive) Cleanup();
            else
            {
                PressureSystem?.ResetPressure();
                UnsubscribePressureEvents();
            }
        }
    }
}
