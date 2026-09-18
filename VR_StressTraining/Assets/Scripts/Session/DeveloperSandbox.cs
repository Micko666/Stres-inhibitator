using System;
using System.Collections.Generic;
using StressTraining.Core;
using StressTraining.Data;
using StressTraining.Persistence;
using StressTraining.Pressure;
using StressTraining.Tablet;
using StressTraining.Tasks;
using StressTraining.UI;

namespace StressTraining.Session
{
    /// <summary>
    /// A corridor you can play with. Everything a researcher needs to try by hand —
    /// a task at a chosen level, a chosen pressure level, a session as short as one
    /// minute — without walking the full production preparation each time.
    ///
    /// The single hard rule is isolation. A sandbox run may never influence a real
    /// participant's data: it writes its session with <see cref="ValidityStatus.DemoOnly"/>,
    /// never calls <c>UserProfileService.CompleteSession</c>, and therefore can move
    /// no task level, consume no place in a training cycle and feed no adaptation.
    /// The levels chosen here live in this object and nowhere else.
    ///
    /// It reuses the existing InfoPanel rather than introducing a UI of its own, so
    /// there is no second UI stack to keep working.
    /// </summary>
    public sealed class DeveloperSandbox
    {
        /// <summary>Session lengths offered in the menu, in seconds. PROJECT_HEURISTIC.</summary>
        public static readonly double[] DurationChoices = { 60, 120, 300, 600, 1200 };

        /// <summary>
        /// The validity a sandbox run is written with. Stated once, as a constant, so a
        /// test can assert it rather than trusting a literal buried in the corridor setup.
        /// </summary>
        public const ValidityStatus SandboxValidity = ValidityStatus.DemoOnly;

        /// <summary>
        /// Remaining-fraction values that correspond to the pressure stage thresholds,
        /// so "jump to Late" means exactly what the timeline means by Late.
        /// </summary>
        public static readonly (string Label, double Fraction)[] StageJumps =
        {
            ("Stable", 0.90),
            ("Early", 0.65),
            ("Mid", 0.45),
            ("Late", 0.25),
            ("Critical", 0.05),
            ("Expired", 0.00)
        };

        private readonly TaskRunner _taskRunner;
        private readonly SceneZoneController _zones;
        private readonly UIManager _ui;
        private readonly TabletDisplayController _tablet;
        private readonly InfoPanel _infoPanel;
        private readonly ProductionSessionFlow _production;
        private readonly SessionRepository _sessions;
        private readonly PersistencePaths _paths;
        private readonly SessionClock _clock;
        private readonly PauseController _pause;

        private readonly Dictionary<TaskType, int> _levels = new Dictionary<TaskType, int>
        {
            { TaskType.NBack, 1 },
            { TaskType.GoNoGo, 1 },
            { TaskType.Flanker, 1 },
            { TaskType.CorsiSequence, 1 }
        };

        private int _pressureLevel = 1;
        private int _durationIndex = 1;              // 120 s
        private bool _fallConsequence;               // off by default: sandboxing should not punish
        private bool _pressureRunning;
        private double _remaining;
        private double _total;
        private int _lastShownSecond = -1;
        private UserProfileData _profile;
        private ActiveSessionContext _context;
        private TrialLogWriter _trialWriter;

        public bool IsActive { get; private set; }
        public PlayerFallController FallController { get; set; }
        public Audio.VoiceCueManager Voice { get; set; }
        public Action OnExit;

        public DeveloperSandbox(TaskRunner taskRunner, SceneZoneController zones, UIManager ui,
            TabletDisplayController tablet, InfoPanel infoPanel, ProductionSessionFlow production,
            SessionRepository sessions, PersistencePaths paths, SessionClock clock,
            PauseController pause)
        {
            _taskRunner = taskRunner;
            _zones = zones;
            _ui = ui;
            _tablet = tablet;
            _infoPanel = infoPanel;
            _production = production;
            _sessions = sessions;
            _paths = paths;
            _clock = clock;
            _pause = pause;
        }

        // ── menu: settings, still in the Safe Space ──────────────────────

        public void Open(UserProfileData profile)
        {
            _profile = profile;
            FlowTrace.Log("Dev", "sandbox meni otvoren");
            ShowMenu();
        }

        private void ShowMenu()
        {
            var options = new List<(string, Action)>
            {
                ("Nivoi zadataka — " + LevelSummary(), ShowLevelMenu),
                ("Pritisak: nivo " + _pressureLevel + "  (kruži 1→2→3)", CyclePressureLevel),
                ("Trajanje: " + DurationLabel(), CycleDuration),
                ("Posljedica pada: " + (_fallConsequence ? "UKLJUČENA" : "isključena"),
                    ToggleFallConsequence),
                ("▶  Uđi u koridor", EnterCorridor),
                ("Nazad", Exit)
            };

            _infoPanel?.Configure("Developer — sandbox",
                "Sve odavde je izolovano: zapisuje se kao DemoOnly, ne mijenja nivoe profila, " +
                "ne troši mjesto u ciklusu i ne hrani adaptaciju.\n\n" +
                "Profil: " + (_profile != null ? _profile.username : "—"),
                options,
                "Pravi tok sesije ovim nije dirnut.");
            ShowPanel();
        }

        private void ShowLevelMenu()
        {
            var options = new List<(string, Action)>();
            foreach (var task in TaskOrder())
            {
                TaskType captured = task;
                options.Add((DemoTaskFactory.DisplayName(task) + ": nivo " + _levels[task],
                    () => { CycleLevel(captured); ShowLevelMenu(); }));
            }
            options.Add(("Nazad", ShowMenu));

            _infoPanel?.Configure("Nivoi zadataka",
                "Klik kruži 1 → 2 → 3. Ovo važi samo za sandbox i ne upisuje se u profil.",
                options, "");
            ShowPanel();
        }

        private static IEnumerable<TaskType> TaskOrder()
        {
            yield return TaskType.NBack;
            yield return TaskType.GoNoGo;
            yield return TaskType.Flanker;
            yield return TaskType.CorsiSequence;
        }

        private string LevelSummary()
        {
            return "N" + _levels[TaskType.NBack] +
                   " G" + _levels[TaskType.GoNoGo] +
                   " F" + _levels[TaskType.Flanker] +
                   " C" + _levels[TaskType.CorsiSequence];
        }

        private void CycleLevel(TaskType task)
        {
            int next = _levels[task] + 1;
            _levels[task] = next > TaskDifficultyConfig.MaxLevel ? TaskDifficultyConfig.MinLevel : next;
        }

        private void CyclePressureLevel()
        {
            _pressureLevel = _pressureLevel >= 3 ? 1 : _pressureLevel + 1;
            ShowMenu();
        }

        private void CycleDuration()
        {
            _durationIndex = (_durationIndex + 1) % DurationChoices.Length;
            ShowMenu();
        }

        private string DurationLabel()
        {
            double s = DurationChoices[_durationIndex];
            return s >= 60 ? (s / 60).ToString("0") + " min" : s.ToString("0") + " s";
        }

        private void ToggleFallConsequence()
        {
            _fallConsequence = !_fallConsequence;
            if (FallController != null) FallController.ConsequenceEnabled = _fallConsequence;
            ShowMenu();
        }

        // ── the corridor itself ──────────────────────────────────────────

        private void EnterCorridor()
        {
            if (_profile == null) { Exit(); return; }

            IsActive = true;
            _total = DurationChoices[_durationIndex];
            _remaining = _total;
            _lastShownSecond = -1;

            string sessionId = Guid.NewGuid().ToString("N");
            _pause.Reset();
            _context = new ActiveSessionContext(_clock, _pause)
            {
                SessionId = sessionId,
                UserId = _profile.userId,
                CycleId = "dev-" + sessionId,
                SessionNumberInCycle = 0,
                StartedAtUtcIso = UtcTime.NowIso(),
                Condition = SessionCondition.Pressure,
                NBackLevel = _levels[TaskType.NBack],
                GoNoGoLevel = _levels[TaskType.GoNoGo],
                FlankerLevel = _levels[TaskType.Flanker],
                PressureLevel = _pressureLevel,
                ValidityStatus = SandboxValidity
            };
            _context.Clock.StartSession();

            _sessions.CreateSessionDirectory(_profile.userId, sessionId);
            _trialWriter = new TrialLogWriter(_paths.TrialsFile(_profile.userId, sessionId));
            _taskRunner.ConfigureSession(_context, _trialWriter);

            if (FallController != null) FallController.ConsequenceEnabled = _fallConsequence;

            _zones?.EnterCorridor();
            _ui?.SetAnchor(_zones?.CorridorUiAnchor);

            var pressure = _production?.PressureSystem;
            if (pressure != null && pressure.IsInitialized)
            {
                pressure.ResetPressure();
                pressure.Begin(Environment.TickCount, _pressureLevel);
                pressure.SetPaused(false);
                _pressureRunning = true;
            }

            FlowTrace.Log("Dev", "koridor: " + LevelSummary() + ", pritisak " + _pressureLevel +
                ", trajanje " + DurationLabel() + ", pad " + _fallConsequence);
            ShowCorridorMenu();
        }

        private void ShowCorridorMenu()
        {
            var options = new List<(string, Action)>();
            foreach (var task in TaskOrder())
            {
                TaskType captured = task;
                options.Add(("▶ " + DemoTaskFactory.DisplayName(task) +
                             "  (nivo " + _levels[task] + ")", () => RunTask(captured)));
            }
            options.Add(("Skoči na fazu pritiska…", ShowStageMenu));
            options.Add(("Izađi iz sandboxa", Exit));

            _infoPanel?.Configure("Sandbox — koridor",
                "Preostalo: " + Math.Ceiling(_remaining) + " s od " + DurationLabel() +
                "\nPritisak: nivo " + _pressureLevel +
                "\nPosljedica pada: " + (_fallConsequence ? "UKLJUČENA" : "isključena") +
                "\n\nPokreni bilo koji zadatak; sve se piše kao DemoOnly.",
                options, "");
            ShowPanel();
        }

        private void ShowStageMenu()
        {
            var options = new List<(string, Action)>();
            foreach (var jump in StageJumps)
            {
                var captured = jump;
                options.Add((captured.Label, () => JumpToStage(captured.Label, captured.Fraction)));
            }
            options.Add(("Nazad", ShowCorridorMenu));

            _infoPanel?.Configure("Faza pritiska",
                "Skok postavlja preostalo vrijeme na vrijednost koja odgovara toj fazi, " +
                "pa se koridor ponaša tačno kao u pravoj sesiji na tom mjestu.",
                options, "");
            ShowPanel();
        }

        private void JumpToStage(string label, double fraction)
        {
            _remaining = _total * fraction;
            _lastShownSecond = -1;
            FlowTrace.Log("Dev", "skok na fazu " + label + " (preostalo " +
                          _remaining.ToString("0.0") + " s)");
            ShowCorridorMenu();
        }

        private void ShowExpiredMenu()
        {
            _infoPanel?.Configure("Sat je istekao",
                "Koridor je zaustavljen u fazi Expired. Finale i pad se ovdje ne pokreću sami — " +
                "pokreni ih kad želiš da ih vidiš.",
                new List<(string, Action)>
                {
                    ("▶ Pokreni finale i pad", TriggerFinale),
                    ("Ponovo pokreni sat", RestartClock),
                    ("Izađi iz sandboxa", Exit)
                }, "");
            ShowPanel();
        }

        private void TriggerFinale()
        {
            FlowTrace.Log("Dev", "ručno pokrenuto finale");
            _ui?.HideAll();
            var pressure = _production?.PressureSystem;
            if (pressure != null && pressure.TriggerGameOverFinale()) return;
            FallController?.TriggerLoss();
        }

        private void RestartClock()
        {
            _remaining = _total;
            _lastShownSecond = -1;
            var pressure = _production?.PressureSystem;
            if (pressure != null)
            {
                pressure.ResetPressure();
                pressure.Begin(Environment.TickCount, _pressureLevel);
                pressure.SetPaused(false);
            }
            _pressureRunning = true;
            FlowTrace.Log("Dev", "sat ponovo pokrenut");
            ShowCorridorMenu();
        }

        /// <summary>
        /// A loss committed while the sandbox was running. The participant is already
        /// being returned to the Safe Space by the loss handler, so the sandbox only
        /// has to stop owning the corridor and offer its menu again.
        /// </summary>
        public void HandleLoss()
        {
            FlowTrace.Log("Dev", "gubitak tokom sandboxa — zatvaram koridor");
            _pressureRunning = false;
            _taskRunner.Abort();
            IsActive = false;
            _ui?.SetAnchor(_zones?.SafeSpaceUiAnchor);
            ShowMenu();
        }

        private void RunTask(TaskType task)
        {
            if (_taskRunner.IsActive) return;
            _ui?.HideAll();
            FlowTrace.Log("Dev", "pokrećem " + task + " nivo " + _levels[task]);
            _taskRunner.StartScoredDemoBlock(task, _levels[task], Environment.TickCount);
        }

        /// <summary>The sandbox keeps its own result handling so the demo flow is untouched.</summary>
        public void HandleBlockCompleted(TaskRunnerResult result)
        {
            string line = result?.Block == null
                ? "blok bez rezultata"
                : result.Block.TaskType + " · tačnost " +
                  (result.Block.Accuracy * 100f).ToString("0") + "% · RT " +
                  result.Block.MeanReactionTimeMs.ToString("0") + " ms · promašaja " +
                  result.Block.MissCount;
            FlowTrace.Log("Dev", "blok gotov: " + line);

            _infoPanel?.Configure("Blok završen", line,
                new List<(string, Action)> { ("Nazad u sandbox", ShowCorridorMenu) }, "");
            ShowPanel();
        }

        // ── tick ─────────────────────────────────────────────────────────

        public void Tick(double dt)
        {
            if (!IsActive || dt <= 0) return;

            var pressure = _production?.PressureSystem;
            if (!_pressureRunning || pressure == null) return;

            _remaining = Math.Max(0.0, _remaining - dt);
            double fraction = _total <= 0 ? 0 : Math.Max(0.0, Math.Min(1.0, _remaining / _total));
            pressure.TickActive((float)dt, fraction);

            int shown = (int)Math.Ceiling(_remaining);
            if (shown != _lastShownSecond)
            {
                _lastShownSecond = shown;
                // The console clock is the only always-visible readout in the
                // corridor, so the sandbox drives it the same way the preview does.
                _tablet?.SetTimer(_remaining);
            }

            if (_remaining > 0) return;

            // The clock stops at zero and stays there. A production session would run
            // the collapse finale here, and the finale ends by calling TriggerLoss(),
            // which ignores ConsequenceEnabled and teleports the participant out — the
            // sandbox would eject on its own the moment the chosen duration elapsed.
            // Here the finale is an explicit choice instead of an ambush.
            _pressureRunning = false;
            _tablet?.SetTimer(0);
            FlowTrace.Log("Dev", "sat istekao — zaustavljen na 0, finale se ne okida samo");
            ShowExpiredMenu();
        }

        // ── leaving ──────────────────────────────────────────────────────

        public void Exit()
        {
            if (IsActive)
            {
                FlowTrace.Log("Dev", "izlaz iz sandboxa");
                _taskRunner.Abort();
                _production?.PressureSystem?.ResetPressure();
                _trialWriter?.Flush();
                _trialWriter = null;
                _pressureRunning = false;
                _context = null;
                // Leaving must not hand a disabled consequence to a real session.
                if (FallController != null) FallController.ConsequenceEnabled = true;
                _zones?.EnterSafeSpace();
                _ui?.SetAnchor(_zones?.SafeSpaceUiAnchor);
                _tablet?.SetActiveControls(TaskType.None);
                _tablet?.ShowIdle("Sandbox zatvoren.");
            }
            IsActive = false;
            OnExit?.Invoke();
        }

        private void ShowPanel()
        {
            if (_infoPanel != null) _ui?.ShowPanel(_infoPanel);
        }
    }
}
