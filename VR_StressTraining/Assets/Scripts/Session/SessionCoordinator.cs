using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.HR;
using StressTraining.Persistence;
using StressTraining.Tasks;
using StressTraining.Tablet;
using StressTraining.UI;

namespace StressTraining.Session
{
    /// <summary>
    /// Owns the user-visible three-task demo flow. Practice is always completed
    /// before its scored block and only scored blocks enter session persistence.
    /// </summary>
    public sealed class SessionCoordinator : IDisposable
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
        private readonly SessionClock _sessionClock;
        private readonly PauseController _pauseController;
        private readonly ProfileSelectionPanel _profilePanel;
        private readonly InfoPanel _infoPanel;
        private readonly ProgressPanel _progressPanel;
        private readonly PauseMenuPanel _pausePanel;

        private readonly ProductionSessionFlow _production;

        /// <summary>Optional narration/subtitles (FAZA 9); every call is null-safe.</summary>
        public Audio.VoiceCueManager Voice;

        /// <summary>Fall/loss handler (over a hole or on TimeExpired); may be null in tests.</summary>
        public PlayerFallController FallController;

        private ActiveSessionContext _context;
        private BaselineSummaryData _baselineSummary;
        private SessionSummaryData _summary;
        private TrialLogWriter _trialWriter;
        private HeartRateLogWriter _hrWriter;
        private EventLogWriter _eventWriter;
        private TaskRunnerResult _lastPracticeResult;
        private double _resumeCountdownRemaining;
        private double _blockCountdownRemaining;
        private int _lastCountdownValue;
        private bool _resumeCountdownActive;
        private bool _blockCountdownActive;
        private bool _disposed;

        public AppState CurrentState => _state.Current;
        public DemoFlowStage CurrentDemoStage { get; private set; } = DemoFlowStage.ProfileSelection;
        public TaskType CurrentDemoTask { get; private set; } = TaskType.None;
        public ActiveSessionContext ActiveSession => _context;
        public UserProfileData ActiveProfile => _profileService.ActiveProfile;
        public SessionSummaryData LastSummary { get; private set; }
        public TaskRunner TaskRunner => _taskRunner;

        public event Action<AppState> FlowStateChanged;
        public event Action<DemoFlowStage> DemoStageChanged;
        public event Action<SessionSummaryData> SessionFinished;

        /// <summary>Active context — the production flow's context when a production session runs.</summary>
        private ActiveSessionContext Ctx =>
            _production != null && _production.IsActive ? _production.Context : _context;

        public SessionCoordinator(StressTrainingConfig config, AppStateMachine state,
            AppErrorService errors, PersistencePaths paths, ProfileRepository profiles,
            SessionRepository sessions, UserProfileService profileService,
            HeartRateService heartRate, TaskRunner taskRunner, SceneZoneController zones,
            UIManager ui, TabletDisplayController tablet, ProfileSelectionPanel profilePanel,
            InfoPanel infoPanel, ProgressPanel progressPanel, PauseMenuPanel pausePanel,
            SessionClock sessionClock, PauseController pauseController,
            ProductionSessionFlow productionFlow = null)
        {
            _production = productionFlow;
            if (_production != null) _production.Finished += OnProductionFinished;
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
            _profilePanel = profilePanel;
            _infoPanel = infoPanel;
            _progressPanel = progressPanel;
            _pausePanel = pausePanel;
            _sessionClock = sessionClock;
            _pauseController = pauseController;

            _state.StateChanged += OnStateChanged;
            _heartRate.SampleAccepted += OnHeartRateSample;
            _taskRunner.BlockCompleted += OnBlockCompleted;
            _taskRunner.PauseToggleRequested += TogglePause;
            if (_profilePanel != null)
            {
                _profilePanel.ProfileSelected += OnProfileSelected;
                _profilePanel.ProfileCreateRequested += OnProfileCreateRequested;
                _profilePanel.OnBack = ShowProfileSelection;
            }
            if (_pausePanel != null)
            {
                _pausePanel.ContinueRequested += BeginResumeCountdown;
                _pausePanel.EndConfirmed += EndSession;
            }
        }

        // NOTE: there is deliberately NO automatic pause. A brief HR/network
        // hiccup must never stop the session, the global timer or the
        // baseline/recovery measurement — the pipeline recovers on its own within
        // a second or two, and auto-pausing on every gap made the session unusable.
        // Pause is exclusively manual (left-controller Y). HR gaps are recorded as
        // a data-quality metric (valid-sample ratio / gap length), never as a pause.

        public void Boot()
        {
            _zones?.EnterSafeSpace();
            _ui?.SetAnchor(_zones?.SafeSpaceUiAnchor);
            if (_state.Current == AppState.Boot)
                _state.RequestTransition(AppState.ProfileSelection, "bootstrap_complete");
            else
                _state.ForceTransition(AppState.ProfileSelection, "bootstrap_reentry");
            ShowProfileSelection();
            Voice?.Play(Audio.VoiceCueId.Welcome);
        }

        public void Tick(double realDeltaSeconds)
        {
            if (_disposed || realDeltaSeconds < 0) return;

            // The collapse preview owns the frame completely: no HR, no clocks, no
            // tasks, no profile writes. It only drives the pressure visuals.
            if (_previewActive) { TickPressurePreview(realDeltaSeconds); return; }

            if (_resumeCountdownActive)
            {
                // Only the part of this frame that belongs to the countdown is
                // paused time. Any overshoot after the countdown completes must
                // advance the resumed task as active time.
                double pausedDelta = Math.Min(realDeltaSeconds,
                    Math.Max(0.0, _resumeCountdownRemaining));
                Ctx?.Clock.Tick(pausedDelta);
                _heartRate.Update(pausedDelta);
                TickResumeCountdown(pausedDelta);
                realDeltaSeconds -= pausedDelta;
                if (realDeltaSeconds <= 0) return;
            }

            if (_state.Current == AppState.ActiveSession && _context != null &&
                _blockCountdownActive)
            {
                // Count the countdown as active session time, then forward any
                // part of this frame after zero to the newly started task.
                double countdownDelta = Math.Min(realDeltaSeconds,
                    Math.Max(0.0, _blockCountdownRemaining));
                _context.Clock.Tick(countdownDelta);
                _heartRate.Update(countdownDelta);
                TickBlockCountdown(countdownDelta);
                realDeltaSeconds -= countdownDelta;
                if (realDeltaSeconds <= 0) return;
            }

            // HR is pumped every frame. A stale/lost signal NEVER pauses anything —
            // it only shows up as "Signal izgubljen" on the watch and as a
            // data-quality metric; the session keeps running.
            _heartRate.Update(realDeltaSeconds);
            Ctx?.Clock.Tick(realDeltaSeconds);
            _production?.Tick(realDeltaSeconds);

            if (_state.Current != AppState.ActiveSession || Ctx == null) return;
            _taskRunner.Tick(realDeltaSeconds);   // no-ops while the task is paused
        }

        public UserProfileData CreateProfile(string username)
        {
            var validation = _profiles.ValidateNewUsername(username);
            if (validation != UsernameValidationResult.Ok)
            {
                _profilePanel?.ShowUsernameError(validation);
                return null;
            }

            var profile = _profileService.CreateProfile(username);
            ShowModeSelection(profile);
            return profile;
        }

        public UserProfileData SelectProfile(string userId)
        {
            var profile = _profileService.SelectProfile(userId);
            if (profile == null)
            {
                _errors.Report("PROFILE_SELECT_FAILED", "Profil nije moguće učitati.",
                    "Profile repository returned null for " + userId, true,
                    ErrorSessionImpact.None);
                ShowProfileSelection();
                return null;
            }

            _sessions.DetectAndMarkAbandonedSessions(profile.userId);
            ShowModeSelection(profile);
            return profile;
        }

        // ── mode selection: production vs demo (spec §10) ────────────────

        private void ShowModeSelection(UserProfileData profile)
        {
            CurrentDemoTask = TaskType.None;
            SetDemoStage(DemoFlowStage.ProfileSelection);

            var eligibility = _profileService.CheckEligibility(profile, UtcTime.Now(), out var wait);
            string schedule;
            switch (eligibility)
            {
                case SessionEligibility.ReadyFirstSession:
                    schedule = "Prva produkcijska sesija — spremno."; break;
                case SessionEligibility.NewCycleReady:
                    schedule = "Prethodni ciklus je završen — nova sesija otvara novi ciklus."; break;
                case SessionEligibility.EarlyWithWarning:
                    schedule = $"⚠ Preporučeni termin još nije stigao (još {(int)wait.TotalHours} h)."; break;
                default:
                    schedule = "Preporučeni termin je stigao — spremno."; break;
            }
            string cycle = $"Ciklus: sesija {profile.currentCycleSessionIndex + 1}/{profile.plannedCycleSessionCount}";

            bool pressureCondition = _config.developer.productionUsePressureCondition;
            string conditionText = pressureCondition ? "ControlledPressure" : "Neutral";

            var options = new List<(string, Action)>();
            if (_production != null)
            {
                if (eligibility == SessionEligibility.EarlyWithWarning)
                    options.Add(("Produkcijska sesija (ranije od termina)", () => ShowEarlyOverride(profile, wait)));
                else
                    options.Add(("Produkcijska sesija", () => StartProduction(profile, false, "")));
            }
            options.Add(("Demo / tutorial (tri igre)", () => ShowDemoOverview(profile)));
            if (_config.developer.developerModeEnabled)
            {
                // The condition decides whether the corridor collapse/lights/finale run
                // at all. It used to be reachable only by hand-editing config.json, so a
                // Neutral session looked like a broken pressure system.
                options.Add(("⚙ Uslov: " + conditionText + " (klikni za promjenu)", () =>
                {
                    _config.developer.productionUsePressureCondition = !pressureCondition;
                    ShowModeSelection(profile);
                }));
                options.Add(($"⚙ Pregled urušavanja ({PreviewSeconds / 60:0} min)",
                    () => StartPressurePreview()));

                // The consequence plays at the END of the collapse preview, in this
                // mode. Cycles fade → devirtualizacija → stvarni pad → fade.
                options.Add(("⚙ Kraj pregleda: " + LossModeLabel(_config.developer)
                    + " (klikni za promjenu)", () =>
                {
                    _config.developer.fallMode = NextLossMode(_config.developer.fallMode);
                    ShowModeSelection(profile);
                }));
            }
            options.Add(("Nazad", ShowProfileSelection));

            _infoPanel?.Configure("Profil: " + profile.username,
                schedule + "\n" + cycle +
                "\nPosljednja sesija: " + (string.IsNullOrEmpty(profile.lastSessionAtUtcIso)
                    ? "nema" : profile.lastSessionAtUtcIso) +
                "\nNaredna preporučena: " + (string.IsNullOrEmpty(profile.nextRecommendedSessionAtUtcIso)
                    ? "odmah" : profile.nextRecommendedSessionAtUtcIso) +
                "\nUslov naredne sesije: " + conditionText +
                (pressureCondition
                    ? " — hodnik se urušava (70/50/30/10/0)."
                    : " — kontrolni uslov, bez urušavanja."),
                options,
                "Demo ne utiče na produkcijske nivoe ni raspored.");
            if (_infoPanel != null) _ui?.ShowPanel(_infoPanel);
        }

        private void ShowEarlyOverride(UserProfileData profile, TimeSpan wait)
        {
            _infoPanel?.Configure("Ranije od preporučenog termina",
                $"Preporučeni interval još traje (još {(int)wait.TotalHours} h {wait.Minutes} min).\n\n" +
                "Istraživač može eksplicitno pokrenuti sesiju ranije — override se trajno " +
                "bilježi, a validnost sesije nosi upozorenje.",
                new List<(string, Action)>
                {
                    ("Ipak pokreni (override)", () =>
                        StartProduction(profile, true, "researcher_explicit_override")),
                    ("Nazad", () => ShowModeSelection(profile))
                },
                "scheduleOverride=true se upisuje u zapis sesije.");
            if (_infoPanel != null) _ui?.ShowPanel(_infoPanel);
        }

        private void StartProduction(UserProfileData profile, bool scheduleOverride, string reason)
        {
            if (_production == null) return;
            _ui?.HideAll();
            _production.Start(profile, scheduleOverride, reason);
        }

        private void OnProductionFinished(SessionSummaryData summary)
        {
            LastSummary = summary;
            SessionFinished?.Invoke(summary);
            ShowProfileSelection();
        }

        // ── developer collapse preview (spec: art/UX inspection only) ─────
        //
        // Watches the corridor collapse end-to-end on a short budget. It reuses the
        // REAL machinery — the same PressureController, the same PressureTimeline and
        // the same 70/50/30/10/0 thresholds — so what you see is what a production
        // ControlledPressure session does. The ONLY differences: the budget is
        // PreviewSeconds instead of the plan budget, and there are no tasks, no HR, no
        // questionnaires and no profile/scheduler writes.
        public const double PreviewSeconds = 120.0;   // 2 minutes

        private bool _previewActive;
        private double _previewRemaining;
        private bool _previewFinaleRunning;
        private bool _previewFallTriggered;
        private double _previewFinaleElapsed;
        private int _previewLastShownSecond = -1;

        // How long the segment/back-wall finale plays at 0 s before the fall fires,
        // so the collapse finishes on screen first.
        private const double PreviewFinaleBeforeFallSeconds = 1.2;

        public bool IsPressurePreviewActive => _previewActive;
        public double PressurePreviewRemaining => _previewRemaining;

        /// <summary>Remaining fraction fed to the pressure system, exactly as the real global timer does.</summary>
        public static double PreviewRemainingFraction(double remaining, double total) =>
            total <= 0 ? 0 : Math.Max(0.0, Math.Min(1.0, remaining / total));

        public void StartPressurePreview(double seconds = PreviewSeconds)
        {
            var pressure = _production?.PressureSystem;
            if (pressure == null || !pressure.IsInitialized)
            {
                _errors.Report("PREVIEW_NO_PRESSURE", "Pregled urušavanja nije moguć.",
                    "PressureController missing or not initialized.", true, ErrorSessionImpact.None);
                return;
            }

            _ui?.HideAll();
            _zones?.EnterCorridor();
            _ui?.SetAnchor(_zones?.CorridorUiAnchor);

            pressure.ResetPressure();
            // Level 3 = the most visible tuning, so the preview shows the full effect.
            pressure.Begin(Environment.TickCount, 3);
            pressure.SetPaused(false);

            _previewActive = true;
            _previewFinaleRunning = false;
            _previewFallTriggered = false;
            _previewFinaleElapsed = 0;
            _previewRemaining = Math.Max(1.0, seconds);
            _previewLastShownSecond = -1;

            // Clean space: no menu (HideAll above), only the diegetic console timer.
            _tablet?.SetActiveControls(TaskType.None);
            _tablet?.SetSessionProgress(0, 0, 0, 0, "", 0);
            _tablet?.ShowIdle("");
        }

        private void TickPressurePreview(double dt)
        {
            var pressure = _production?.PressureSystem;
            if (pressure == null) { StopPressurePreview(); return; }

            if (_previewFinaleRunning)
            {
                pressure.TickFinaleActive((float)dt);
                _previewFinaleElapsed += dt;
                _previewRemaining -= dt;

                // Once the collapse finale has played, fire the fall in the selected
                // mode — this is the point of the preview: watch the room fall, THEN
                // fall/fade yourself. FallController.PlayerLost → StopPressurePreview.
                if (!_previewFallTriggered &&
                    _previewFinaleElapsed >= PreviewFinaleBeforeFallSeconds)
                {
                    _previewFallTriggered = true;
                    if (FallController != null) FallController.TriggerLoss();
                    else StopPressurePreview();
                }

                // Safety net if the fall never commits.
                if (_previewRemaining <= 0) StopPressurePreview();
                return;
            }

            _previewRemaining = Math.Max(0.0, _previewRemaining - dt);
            double fraction = PreviewRemainingFraction(_previewRemaining, PreviewSeconds);
            pressure.TickActive((float)dt, fraction);

            int shown = (int)Math.Ceiling(_previewRemaining);
            if (shown != _previewLastShownSecond)
            {
                _previewLastShownSecond = shown;
                _tablet?.SetTimer(_previewRemaining);
            }

            if (_previewRemaining > 0) return;

            // 0 % — the same finale the real TimeExpired path triggers, then the fall.
            _tablet?.SetTimer(0);
            _previewFinaleRunning = true;
            _previewFinaleElapsed = 0;
            _previewRemaining = PreviewFinaleBeforeFallSeconds +
                Pressure.PressureHeuristics.FinaleSafetyFallbackSeconds + 2.0;
            pressure.TriggerGameOverFinale();
        }

        public void StopPressurePreview()
        {
            if (!_previewActive) return;
            _previewActive = false;
            _previewFinaleRunning = false;
            _previewRemaining = 0;
            _production?.PressureSystem?.ResetPressure();
            FallController?.ResetState();
            _tablet?.SetTimer(-1);
            _tablet?.ShowIdle("Pregled završen.");
            ShowProfileSelection();
        }

        private static string LossModeLabel(Core.DeveloperConfig dev)
        {
            switch (dev.fallMode)
            {
                case FallMode.RealFall:
                    return "stvarni pad (" + dev.fallMaxDistanceMeters.ToString("0") + " m)";
                case FallMode.Devirtualize:
                    return "devirtualizacija";
                default:
                    return "fade (bez mučnine)";
            }
        }

        private static FallMode NextLossMode(FallMode mode)
        {
            switch (mode)
            {
                case FallMode.ComfortFade: return FallMode.Devirtualize;
                case FallMode.Devirtualize: return FallMode.RealFall;
                default: return FallMode.ComfortFade;
            }
        }

        /// <summary>
        /// Loss committed by PlayerFallController (over a collapsed floor, or on
        /// TimeExpired). The consequence is always the same: return to the Safe Space.
        /// During the preview this simply ends it; in a real session the session flow
        /// still owns scoring/validity — this only moves the participant to safety.
        /// </summary>
        public void HandlePlayerLost()
        {
            Voice?.Play(Audio.VoiceCueId.GameOver);
            if (_previewActive) { StopPressurePreview(); return; }
            _zones?.EnterSafeSpace();
            _ui?.SetAnchor(_zones?.SafeSpaceUiAnchor);
        }

        private void OnProfileSelected(string userId) => SelectProfile(userId);
        private void OnProfileCreateRequested(string username) => CreateProfile(username);

        /// <summary>
        /// Creates the demo session and opens the N-back tutorial. No task starts
        /// until the user explicitly enters practice from that tutorial.
        /// </summary>
        public void StartDemo()
        {
            var profile = _profileService.ActiveProfile;
            if (profile == null)
            {
                ShowProfileSelection();
                return;
            }
            if (_state.Current != AppState.Preparation || _context?.Clock.IsRunning == true) return;

            _pauseController.Reset();
            string sessionId = Guid.NewGuid().ToString("N");
            _context = new ActiveSessionContext(_sessionClock, _pauseController)
            {
                SessionId = sessionId,
                UserId = profile.userId,
                CycleId = "demo-" + sessionId,
                SessionNumberInCycle = 0,
                StartedAtUtcIso = UtcTime.NowIso(),
                Condition = SessionCondition.Neutral,
                MasterSeed = _config.developer.verticalSliceSeed,
                NBackLevel = 1,
                GoNoGoLevel = Math.Max(1, profile.currentGoNoGoLevel),
                FlankerLevel = Math.Max(1, profile.currentFlankerLevel),
                PressureLevel = 1,
                ValidityStatus = ValidityStatus.DemoOnly
            };
            _context.Clock.StartSession();
            _baselineSummary = new BaselineSummaryData
            {
                quality = BaselineQuality.Missing,
                durationSeconds = 0f
            };

            _sessions.CreateSessionDirectory(profile.userId, _context.SessionId);
            _trialWriter = new TrialLogWriter(_paths.TrialsFile(profile.userId, _context.SessionId));
            _hrWriter = new HeartRateLogWriter(_paths.HrFile(profile.userId, _context.SessionId));
            _eventWriter = new EventLogWriter(_paths.EventsFile(profile.userId, _context.SessionId));
            _summary = CreateInitialSummary(profile);
            _summary.baseline = _baselineSummary;
            _sessions.SaveSessionSummary(_summary);
            _heartRate.AttachSession(_context.SessionId, _hrWriter, GetHeartRateContext);
            _taskRunner.ConfigureSession(_context, _trialWriter);

            _state.RequestTransition(AppState.Ready, "three_task_demo_created");
            _state.RequestTransition(AppState.ActiveSession, "safe_space_tutorial_ready");
            ShowTaskTutorial(TaskType.NBack);
        }

        // Compatibility names retained for existing scene/test callers.
        public void BeginBaseline() => StartDemo();
        public void StartNBackSession()
        {
            if (CurrentDemoStage == DemoFlowStage.NBackTutorial) BeginPractice();
        }

        public void BeginPractice()
        {
            if (!IsTutorialStage(CurrentDemoStage) || CurrentDemoTask == TaskType.None) return;
            StartPracticeInternal();
        }

        public void RepeatPractice()
        {
            if (!IsPracticeReviewStage(CurrentDemoStage) || CurrentDemoTask == TaskType.None) return;
            StartPracticeInternal();
        }

        private void StartPracticeInternal()
        {
            bool enteringFromTutorial = IsTutorialStage(CurrentDemoStage);
            if (enteringFromTutorial)
            {
                _zones?.EnterCorridor();
                _ui?.SetAnchor(_zones?.CorridorUiAnchor);
            }
            _lastPracticeResult = null;
            SetDemoStage(ThreeTaskDemoFlow.PracticeStage(CurrentDemoTask));
            _ui?.HideAll();
            _tablet?.ShowInstruction(DemoTaskFactory.DisplayName(CurrentDemoTask) + ": vježba",
                TabletPracticeInstruction(CurrentDemoTask));
            _taskRunner.StartPractice(CurrentDemoTask);
        }

        /// <summary>Explicit user confirmation required after practice.</summary>
        public void ContinueAfterPractice()
        {
            if (!IsPracticeReviewStage(CurrentDemoStage) || CurrentDemoTask == TaskType.None) return;
            SetDemoStage(ThreeTaskDemoFlow.CountdownStage(CurrentDemoTask));
            _ui?.HideAll();
            _tablet?.ShowInstruction(DemoTaskFactory.DisplayName(CurrentDemoTask) + ": blok 1/1",
                "Pravi kratki blok počinje poslije odbrojavanja.");
            _blockCountdownRemaining = Math.Max(1f,
                _config.developer.verticalSliceBlockCountdownSeconds);
            _lastCountdownValue = -1;
            _blockCountdownActive = true;
            UpdateCountdownDisplay(_blockCountdownRemaining);
        }

        private void TickBlockCountdown(double dt)
        {
            _blockCountdownRemaining -= dt;
            if (_blockCountdownRemaining > 0)
            {
                UpdateCountdownDisplay(_blockCountdownRemaining);
                return;
            }

            _blockCountdownActive = false;
            _tablet?.HideCountdown();
            StartScoredBlock(CurrentDemoTask);
        }

        private void StartScoredBlock(TaskType task)
        {
            if (task == TaskType.None || _context == null) return;
            SetDemoStage(ThreeTaskDemoFlow.BlockStage(task));
            _ui?.HideAll();
            _taskRunner.StartScoredDemoBlock(task, LevelFor(task), SeedFor(task), TrialCountFor(task));
        }

        public void TogglePause()
        {
            if (_state.Current == AppState.ActiveSession && _taskRunner.IsActive)
                PauseSession();
            else if (_state.Current == AppState.Paused && !_resumeCountdownActive)
                BeginResumeCountdown();
        }

        public void PauseSession()
        {
            if (_state.Current != AppState.ActiveSession || Ctx == null ||
                !_taskRunner.IsActive || _blockCountdownActive) return;
            _state.RequestTransition(AppState.Paused, "pause_requested");
            Ctx.Pause.Pause("user_pause");
            _taskRunner.Pause();
            Voice?.Play(Audio.VoiceCueId.PauseOpened);
            _pausePanel?.ShowMain();
            if (_pausePanel != null) _ui?.ShowPanel(_pausePanel);
        }

        public void BeginResumeCountdown()
        {
            if (_state.Current != AppState.Paused || _resumeCountdownActive) return;
            _resumeCountdownActive = true;
            _resumeCountdownRemaining = Math.Max(1f, _config.session.resumeCountdownSeconds);
            _lastCountdownValue = -1;
            _ui?.HideAll();
            UpdateCountdownDisplay(_resumeCountdownRemaining);
        }

        private void TickResumeCountdown(double dt)
        {
            _resumeCountdownRemaining -= dt;
            if (_resumeCountdownRemaining > 0)
            {
                UpdateCountdownDisplay(_resumeCountdownRemaining);
                return;
            }

            _resumeCountdownActive = false;
            _tablet?.HideCountdown();
            Ctx?.Pause.Resume("countdown_complete");
            _taskRunner.Resume();
            _state.RequestTransition(AppState.ActiveSession, "resume_countdown_complete");
            _ui?.HideAll();
        }

        private void UpdateCountdownDisplay(double remaining)
        {
            int value = Math.Max(1, (int)Math.Ceiling(remaining));
            if (value == _lastCountdownValue) return;
            _lastCountdownValue = value;
            _tablet?.ShowCountdown(value);
        }

        public void EndSession(UserTerminationReason reason)
        {
            if (_state.Current != AppState.ActiveSession && _state.Current != AppState.Paused) return;
            _resumeCountdownActive = false;
            _blockCountdownActive = false;
            _tablet?.HideCountdown();
            if (_production != null && _production.IsActive)
            {
                if (Ctx != null && Ctx.Pause.IsPaused) Ctx.Pause.Resume("session_ending");
                _ui?.HideAll();
                _production.HandleUserTermination(reason);
                return;
            }
            _taskRunner.Abort();
            FinishSession(CompletionStatus.UserTerminated, reason, _taskRunner.LastResult);
        }

        private void OnBlockCompleted(TaskRunnerResult result)
        {
            if (result?.Block == null) return;
            if (_production != null && _production.IsActive)
            {
                _production.OnBlockCompleted(result);
                return;
            }
            CurrentDemoTask = result.Block.TaskType;

            if (result.IsPractice)
            {
                _lastPracticeResult = result;
                ShowPracticeReview(result);
                return;
            }

            AddTaskSummary(result);
            var next = ThreeTaskDemoFlow.NextTask(result.Block.TaskType);
            if (next != TaskType.None)
                ShowTaskTutorial(next);
            else
                FinishSession(CompletionStatus.Completed, UserTerminationReason.None, null);
        }

        private void ShowTaskTutorial(TaskType task)
        {
            CurrentDemoTask = task;
            SetDemoStage(ThreeTaskDemoFlow.TutorialStage(task));
            _zones?.EnterSafeSpace();
            _ui?.SetAnchor(_zones?.SafeSpaceUiAnchor);
            TutorialCopy(task, out string title, out string body, out string footer);
            _infoPanel?.Configure(title, body,
                new List<(string, Action)> { ("Pokreni vježbu", BeginPractice) }, footer);
            if (_infoPanel != null) _ui?.ShowPanel(_infoPanel);
            _tablet?.SetActiveControls(TaskType.None);
            _tablet?.ShowInstruction(title, body);
        }

        private void ShowPracticeReview(TaskRunnerResult result)
        {
            SetDemoStage(ThreeTaskDemoFlow.PracticeReviewStage(CurrentDemoTask));
            var block = result.Block;
            int errors = Math.Max(0, block.ScoreableCount - block.CorrectCount);
            string body =
                $"Vježba je završena. Tačnost: {block.Accuracy * 100f:0}% · greške: {errors}.\n\n" +
                PracticeReminder(CurrentDemoTask) +
                "\n\nMožeš ponoviti vježbu ili eksplicitno nastaviti na bodovani blok.";
            _infoPanel?.Configure(DemoTaskFactory.DisplayName(CurrentDemoTask) + ": spremno",
                body,
                new List<(string, Action)>
                {
                    ("Ponovi vježbu", RepeatPractice),
                    ("Nastavi", ContinueAfterPractice)
                },
                "Pravi blok se još nije pokrenuo.");
            if (_infoPanel != null) _ui?.ShowPanel(_infoPanel);
            _tablet?.ShowInstruction("Vježba završena", PracticeReminder(CurrentDemoTask));
        }

        private void FinishSession(CompletionStatus status, UserTerminationReason reason,
            TaskRunnerResult partialResult)
        {
            if (_context == null || _summary == null) return;
            AddTaskSummary(partialResult);

            if (_state.Current == AppState.Paused && _context.Pause.IsPaused)
                _context.Pause.Resume("session_ending");
            _state.RequestTransition(AppState.SessionEnding, "session_finish");
            _context.Clock.StopSession();
            _context.CompletionStatus = status;
            _context.UserTerminationReason = reason;

            _summary.endedAtUtcIso = UtcTime.NowIso();
            _summary.completionStatus = status;
            _summary.userTerminationReason = reason;
            _summary.validityStatus = status == CompletionStatus.Completed
                ? ValidityStatus.DemoOnly : ValidityStatus.IncompleteUserTerminated;
            _summary.totalActiveSeconds = _context.Clock.ActiveElapsedSeconds;
            _summary.totalPausedSeconds = _context.Clock.PausedElapsedSeconds;
            _summary.pauseCount = _context.Clock.PauseCount;
            _summary.baseline = _baselineSummary ?? new BaselineSummaryData
            {
                quality = BaselineQuality.Missing
            };

            _eventWriter?.Log("session_finished", _context.SessionId, _state.Current.ToString(),
                _context.Clock.RealElapsedSeconds, "{\"status\":\"" + status + "\"}");
            _trialWriter?.Flush();
            _hrWriter?.Flush();
            _eventWriter?.Flush();
            _heartRate.DetachSession();
            _sessions.SaveSessionSummary(_summary);

            var profile = _profileService.ActiveProfile;
            bool isDemoSession = _context.ValidityStatus == ValidityStatus.DemoOnly;
            if (profile != null && isDemoSession)
            {
                // Keep the demonstration in history, but preserve every real
                // scheduler/cycle timestamp and difficulty field verbatim.
                if (!profile.sessionSummaries.Exists(x => x.sessionId == _summary.sessionId))
                    profile.sessionSummaries.Add(_summary);
                _profiles.SaveProfile(profile);
            }
            else if (profile != null && status == CompletionStatus.Completed)
            {
                _profileService.CompleteSession(profile, _summary, null);
            }
            else if (profile != null)
            {
                if (!profile.sessionSummaries.Exists(x => x.sessionId == _summary.sessionId))
                    profile.sessionSummaries.Add(_summary);
                profile.lastSessionAtUtcIso = _summary.endedAtUtcIso;
                _profiles.SaveProfile(profile);
            }

            CloseSessionWriters();
            LastSummary = _summary;
            CurrentDemoTask = TaskType.None;
            SetDemoStage(DemoFlowStage.SessionSummary);
            _zones?.EnterSafeSpace();
            _ui?.SetAnchor(_zones?.SafeSpaceUiAnchor);
            _state.RequestTransition(AppState.PostSessionSummary, "summary_ready");
            ShowSummary(_summary);
            SessionFinished?.Invoke(_summary);
        }

        private SessionSummaryData CreateInitialSummary(UserProfileData profile) => new SessionSummaryData
        {
            sessionId = _context.SessionId,
            userId = profile.userId,
            cycleId = _context.CycleId,
            sessionNumberInCycle = _context.SessionNumberInCycle,
            startedAtUtcIso = _context.StartedAtUtcIso,
            completionStatus = CompletionStatus.Unknown,
            validityStatus = ValidityStatus.DemoOnly,
            condition = SessionCondition.Neutral,
            pressureLevel = 1,
            seed = _context.MasterSeed,
            appVersion = UnityEngine.Application.version,
            configVersion = _config.configVersion
        };

        private void AddTaskSummary(TaskRunnerResult result)
        {
            if (result?.Block == null || !result.ContributesToSessionScore || _summary == null) return;
            var block = result.Block;
            if (_summary.taskSummaries.Exists(x => x.taskType == block.TaskType)) return;
            bool simulatedHr = _heartRate.ActiveSource?.SourceType == HrSourceType.Simulated;

            _summary.taskSummaries.Add(new TaskSessionSummaryData
            {
                taskType = block.TaskType,
                difficultyLevel = block.DifficultyLevel,
                blockCount = block.Trials.Count > 0 ? 1 : 0,
                trialCount = block.ScoreableCount,
                correctCount = block.CorrectCount,
                missCount = block.MissCount,
                falsePositiveCount = block.FalsePositiveCount,
                accuracy = block.Accuracy,
                meanReactionTimeMs = block.MeanReactionTimeMs,
                medianReactionTimeMs = block.MedianReactionTimeMs,
                // Raw simulated samples remain available in hr.jsonl with
                // sourceType=Simulated, but are never persisted as if they were
                // physiological results in the user-facing task summary.
                maxBpmDuringTask = simulatedHr ? -1f : result.MaxBpm,
                avgBpmDuringTask = simulatedHr ? -1f : result.AverageBpm,
                elevatedOrHighZoneSeconds = simulatedHr ? 0.0 : result.ElevatedSeconds
            });
            _sessions.SaveSessionSummary(_summary);
        }

        private void ShowDemoOverview(UserProfileData profile)
        {
            if (_state.Current == AppState.ProfileSelection)
                _state.RequestTransition(AppState.Preparation, "profile_selected");
            else
                _state.ForceTransition(AppState.Preparation, "profile_selected");

            CurrentDemoTask = TaskType.None;
            SetDemoStage(DemoFlowStage.DemoOverview);
            string last = profile.sessionSummaries.Count > 0
                ? profile.sessionSummaries[profile.sessionSummaries.Count - 1].endedAtUtcIso
                : "nema prethodne sesije";
            _infoPanel?.Configure("Demo: tri kognitivne igre",
                "Profil: " + profile.username + "\nPosljednja sesija: " + last +
                "\n\nSlijede N-back, Go/No-Go i Flanker. Svaka igra ima tutorial, kratku vježbu i jedan kratki blok.\n\nUkupno: 3 bodovana bloka.",
                new List<(string, Action)>
                {
                    ("Pokreni demo", StartDemo),
                    ("Nazad", ShowProfileSelection)
                },
                "Task se neće pokrenuti prije tutoriala i vježbe.");
            if (_infoPanel != null) _ui?.ShowPanel(_infoPanel);
            _tablet?.ShowIdle("Izaberi Pokreni demo kada budeš spreman/na.");
        }

        private void ShowProfileSelection()
        {
            if (_context != null && _context.Clock.IsRunning) return;
            if (_state.Current != AppState.ProfileSelection)
            {
                if (!_state.RequestTransition(AppState.ProfileSelection, "return_to_profiles"))
                    _state.ForceTransition(AppState.ProfileSelection, "return_to_profiles");
            }

            CurrentDemoTask = TaskType.None;
            SetDemoStage(DemoFlowStage.ProfileSelection);
            _zones?.EnterSafeSpace();
            _ui?.SetAnchor(_zones?.SafeSpaceUiAnchor);
            _profileService.ClearActiveProfile();
            _profilePanel?.ShowProfiles(_profiles.ListProfiles(),
                _config.developer.developerModeEnabled);
            if (_profilePanel != null) _ui?.ShowPanel(_profilePanel);
        }

        private void ShowSummary(SessionSummaryData summary)
        {
            HrSourceType sourceType = _heartRate.ActiveSource?.SourceType ?? HrSourceType.None;
            _infoPanel?.Configure("Sažetak demo sesije",
                DemoSummaryFormatter.Format(summary, sourceType),
                new List<(string, Action)> { ("Nazad na profile", ShowProfileSelection) },
                "Rezultat je sačuvan. Simulirani HR nije dio korisničkog sažetka.");
            if (_infoPanel != null) _ui?.ShowPanel(_infoPanel);
            _tablet?.ShowIdle("Sva tri kratka bloka su završena.");
        }

        private void OnHeartRateSample(HeartRateSampleRecord sample)
        {
            if (_production != null && _production.IsActive) return; // flow maintains its own context
            if (_context == null) return;
            _context.HrConnected = _heartRate.IsReceiving;
            _context.CurrentHrZone = _heartRate.CurrentZone;
            _context.LatestBpm = sample.bpm;
            _context.SessionPeakBpm = _heartRate.SessionPeakBpm;
        }

        private (bool, string, TaskType, string, string) GetHeartRateContext()
        {
            bool paused = _context?.Pause.IsPaused ?? false;
            TaskType task = _taskRunner.CurrentTask;
            string block = task == TaskType.None ? "" : task.ToString().ToLowerInvariant();
            return (paused, _state.Current.ToString(), task, block,
                _context?.ActiveTrialId ?? "");
        }

        private int LevelFor(TaskType task)
        {
            switch (task)
            {
                case TaskType.NBack: return _context.NBackLevel;
                case TaskType.GoNoGo: return _context.GoNoGoLevel;
                case TaskType.Flanker: return _context.FlankerLevel;
                default: return 1;
            }
        }

        private int SeedFor(TaskType task) => _context.MasterSeed + (int)task * 1009;

        private int TrialCountFor(TaskType task)
        {
            switch (task)
            {
                case TaskType.NBack: return _config.developer.verticalSliceNBackTrials;
                case TaskType.GoNoGo: return _config.developer.verticalSliceGoNoGoTrials;
                case TaskType.Flanker: return _config.developer.verticalSliceFlankerTrials;
                default: return 0;
            }
        }

        private void SetDemoStage(DemoFlowStage stage)
        {
            CurrentDemoStage = stage;
            DemoStageChanged?.Invoke(stage);
            _eventWriter?.Log("demo_stage", _context?.SessionId ?? "", _state.Current.ToString(),
                _context?.Clock.RealElapsedSeconds ?? 0,
                "{\"stage\":\"" + stage + "\"}");
        }

        private static bool IsTutorialStage(DemoFlowStage stage) =>
            stage == DemoFlowStage.NBackTutorial ||
            stage == DemoFlowStage.GoNoGoTutorial ||
            stage == DemoFlowStage.FlankerTutorial;

        private static bool IsPracticeReviewStage(DemoFlowStage stage) =>
            stage == DemoFlowStage.NBackPracticeReview ||
            stage == DemoFlowStage.GoNoGoPracticeReview ||
            stage == DemoFlowStage.FlankerPracticeReview;

        private static string PracticeReminder(TaskType task)
        {
            switch (task)
            {
                case TaskType.NBack:
                    return "Isti kao prethodni = MATCH. Različit = NO MATCH. Uvodni simbol samo zapamti.";
                case TaskType.GoNoGo:
                    return "GO = pritisni GO. NO GO = ne pritiskaj ništa. Propušten GO je omission; pritisak na NO GO je commission.";
                case TaskType.Flanker:
                    return "Odgovor određuje samo CENTRALNA strelica, čak i kada okolne strelice pokazuju suprotno.";
                default:
                    return "";
            }
        }

        private static string TabletPracticeInstruction(TaskType task)
        {
            switch (task)
            {
                case TaskType.NBack:
                    return "Zapamti prethodni simbol.\n" +
                           "Isti kao prethodni = MATCH. Različit = NO MATCH.\n" +
                           "Uvodni simbol samo zapamti; tada ne pritiskaj ništa.";
                case TaskType.GoNoGo:
                    return "Riječ GO = pritisni GO.\n" +
                           "Riječ NO GO = ne pritiskaj ništa.";
                case TaskType.Flanker:
                    return "Odgovori prema pravcu CENTRALNE strelice.\n" +
                           "Koristi samo LEFT ili RIGHT.";
                default:
                    return "Odgovaraj direktnim pritiskom na osvijetljene kontrole konzole.";
            }
        }

        private static void TutorialCopy(TaskType task, out string title, out string body,
            out string footer)
        {
            switch (task)
            {
                case TaskType.NBack:
                    title = "N-back tutorial (1-back)";
                    body = "Zapamti prethodni simbol.\n\n" +
                           "Ako je novi simbol isti kao prethodni, pritisni MATCH.\n" +
                           "Ako je različit, pritisni NO MATCH.\n\n" +
                           "Primjer:\nA — uvodni simbol, bez odgovora\nA → MATCH\nA pa B → NO MATCH";
                    footer = "Direktno dodirni osvijetljeno MATCH ili NO MATCH dugme na konzoli.";
                    return;
                case TaskType.GoNoGo:
                    title = "Go/No-Go tutorial";
                    body = "Na tabletu se prikazuje velika crna riječ na bijeloj pozadini.\n\n" +
                           "Kada piše GO — pritisni GO dugme.\n" +
                           "Kada piše NO GO — ne pritiskaj ništa.\n\n" +
                           "Vježba sadrži GO i NO GO primjere i daje precizan feedback za " +
                           "propušten GO i pogrešan pritisak na NO GO.";
                    footer = "Samo centralno GO dugme će biti osvijetljeno.";
                    return;
                case TaskType.Flanker:
                    title = "Flanker tutorial";
                    body = "Odgovori prema pravcu CENTRALNE strelice.\n\n" +
                           ">>>>> → RIGHT\n<<<<< → LEFT\n<<[>]<< → RIGHT\n>>[<]>> → LEFT\n\n" +
                           "U vježbi je centralna strelica označena uglastim zagradama; u pravom bloku nije posebno naglašena.";
                    footer = "Direktno dodirni LEFT ili RIGHT dugme na konzoli.";
                    return;
                default:
                    title = "Tutorial";
                    body = "";
                    footer = "";
                    return;
            }
        }

        private void OnStateChanged(AppStateMachine.Transition transition)
        {
            FlowStateChanged?.Invoke(transition.Next);
            _eventWriter?.Log("state_transition", _context?.SessionId ?? "",
                transition.Next.ToString(), _context?.Clock.RealElapsedSeconds ?? 0,
                "{\"from\":\"" + transition.Previous + "\",\"to\":\"" + transition.Next + "\"}");
        }

        private void CloseSessionWriters()
        {
            _trialWriter?.Dispose();
            _hrWriter?.Dispose();
            _eventWriter?.Dispose();
            _trialWriter = null;
            _hrWriter = null;
            _eventWriter = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _state.StateChanged -= OnStateChanged;
            _heartRate.SampleAccepted -= OnHeartRateSample;
            _taskRunner.BlockCompleted -= OnBlockCompleted;
            _taskRunner.PauseToggleRequested -= TogglePause;
            if (_profilePanel != null)
            {
                _profilePanel.ProfileSelected -= OnProfileSelected;
                _profilePanel.ProfileCreateRequested -= OnProfileCreateRequested;
            }
            if (_pausePanel != null)
            {
                _pausePanel.ContinueRequested -= BeginResumeCountdown;
                _pausePanel.EndConfirmed -= EndSession;
            }
            if (_production != null) _production.Finished -= OnProductionFinished;
            _taskRunner.Abort();
            _heartRate.DetachSession();
            CloseSessionWriters();
            _disposed = true;
        }
    }
}
