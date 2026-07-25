using System;
using System.Linq;
using StressTraining.Console;
using StressTraining.HR;
using StressTraining.Persistence;
using StressTraining.Pressure;
using StressTraining.Session;
using StressTraining.Tablet;
using StressTraining.Tasks;
using StressTraining.UI;
using UnityEngine;

namespace StressTraining.Core
{
    /// <summary>Single composition root and runtime entry point for the vertical slice.</summary>
    [DisallowMultipleComponent]
    public sealed class AppBootstrapper : MonoBehaviour
    {
        [Header("Scene references installed by StressTrainingSceneInstaller")]
        [SerializeField] private Transform xrRigRoot;
        [SerializeField] private GameObject corridorRoot;
        [SerializeField] private Transform corridorSpawn;
        [SerializeField] private Transform systemsRoot;
        [SerializeField] private Transform runtimeUiRoot;
        [SerializeField] private SafeSpaceUiLayoutConfig safeSpaceUiLayout =
            new SafeSpaceUiLayoutConfig();
        [SerializeField] private ConsoleLayoutConfig consoleLayout = new ConsoleLayoutConfig();
        [NonSerialized] private string persistentDataPathOverride;

        private static AppBootstrapper _active;
        private AppErrorService _errors;
        private SimulatedHeartRateSource _simulatedHr;
        private HeartRateService _heartRate;
        private TaskRunner _taskRunner;
        private SessionCoordinator _coordinator;
        private ProductionSessionFlow _productionFlow;
        private bool _initialized;

        public bool IsInitialized => _initialized;
        public SessionCoordinator Coordinator => _coordinator;
        public HeartRateService HeartRate => _heartRate;

        public void ConfigureSceneReferences(Transform rig, GameObject corridor,
            Transform corridorSpawnPoint, Transform systems, Transform uiRoot)
        {
            xrRigRoot = rig;
            corridorRoot = corridor;
            corridorSpawn = corridorSpawnPoint;
            systemsRoot = systems;
            runtimeUiRoot = uiRoot;
        }

        public void ConfigurePersistentDataPathForTests(string path)
        {
            if (_initialized) throw new InvalidOperationException("Persistent path must be configured before Start.");
            persistentDataPathOverride = path;
        }

        private void Awake()
        {
            if (_active != null && _active != this)
            {
                Debug.LogError("[AppBootstrapper] Duplicate bootstrapper disabled.");
                enabled = false;
                return;
            }
            _active = this;
        }

        private void Start()
        {
            if (_active != this || !enabled) return;
            try
            {
                InitializeRuntime();
            }
            catch (Exception ex)
            {
                _errors?.Report("BOOTSTRAP_FAILED", "Aplikacija nije mogla biti pokrenuta.",
                    ex.ToString(), false, ErrorSessionImpact.Fatal, ex);
                Debug.LogException(ex);
                ShutdownRuntime();
                enabled = false;
            }
        }

        private void InitializeRuntime()
        {
            ServiceRegistry.Reset();
            _errors = new AppErrorService();

            systemsRoot = systemsRoot != null ? systemsRoot : FindOrCreateRoot("SystemsRoot");
            runtimeUiRoot = runtimeUiRoot != null ? runtimeUiRoot : FindOrCreateRoot("RuntimeUIRoot");
            corridorRoot = corridorRoot != null ? corridorRoot : FindSceneObject("Corridor_Blockout");
            xrRigRoot = xrRigRoot != null ? xrRigRoot : FindRigRoot();
            if (corridorRoot == null) throw new InvalidOperationException("Corridor_Blockout is required.");
            if (xrRigRoot == null) throw new InvalidOperationException("XR rig root is required.");

            var paths = new PersistencePaths(string.IsNullOrEmpty(persistentDataPathOverride)
                ? Application.persistentDataPath : persistentDataPathOverride);
            paths.EnsureBaseDirectories();
            var configService = new ConfigService(paths.ConfigDir, _errors);
            var config = configService.Config;
            var migration = new SchemaMigrationService();
            var backup = new BackupRecoveryService(paths);
            var profileRepository = new ProfileRepository(paths, migration, backup, _errors);
            var sessionRepository = new SessionRepository(paths, migration, _errors);
            var profileService = new UserProfileService(profileRepository, config.session);
            var stateMachine = new AppStateMachine();
            var sessionClock = new SessionClock();
            var pauseController = new PauseController(sessionClock);

            _simulatedHr = new SimulatedHeartRateSource();
            _heartRate = new HeartRateService(config.hrZones, _errors);
            // All four HR modes exist from boot (spec §35.1); the network source
            // stays constructed-but-stopped until its mode is selected, and the
            // NativeAdb slot is an honest placeholder that never claims a link.
            var networkHr = new NetworkHeartRateSource(
                config.network.hrUdpBindAddress, config.network.hrUdpListenPort,
                config.hrZones.minPlausibleBpm, config.hrZones.maxPlausibleBpm);
            var nativeAdbHr = new NativeAdbHeartRateSource();
            _heartRate.ConfigureSources(_simulatedHr, networkHr, nativeAdbHr);
            // Production always listens for a real external network source.
            // Simulated HR remains constructed for isolated tests only and is
            // never selected as an automatic fallback.
            _heartRate.SetMode(Data.HeartRateMode.Network);

            Transform safeRoot = SafeSpaceBuilder.Build(systemsRoot, safeSpaceUiLayout);
            Transform safeSpawn = safeRoot.Find(SafeSpaceBuilder.SpawnName);
            Transform safeUi = safeRoot.Find(SafeSpaceBuilder.UiAnchorName);
            corridorSpawn = corridorSpawn != null ? corridorSpawn : EnsureCorridorSpawn(corridorRoot.transform);
            Transform corridorUi = EnsureCorridorUiAnchor(corridorRoot.transform);

            var zones = GetOrAdd<SceneZoneController>(systemsRoot.gameObject);
            zones.Initialize(xrRigRoot, safeRoot.gameObject, corridorRoot,
                safeSpawn, corridorSpawn, safeUi, corridorUi, safeSpaceUiLayout);

            var ui = GetOrAdd<UIManager>(runtimeUiRoot.gameObject);
            var ovrRig = xrRigRoot.GetComponent<OVRCameraRig>();
            if (ovrRig == null) throw new InvalidOperationException("OVRCameraRig component is required on xrRigRoot.");
            Transform viewer = ovrRig.centerEyeAnchor;
            if (viewer == null)
                Debug.LogWarning("[AppBootstrapper] centerEyeAnchor nije pronađen; pressure finale koristi bezbjedni corridor fallback.");
            ui.Initialize(safeUi, viewer, 1.6f);
            var profilePanel = ui.RegisterPanel<ProfileSelectionPanel>("ProfileSelectionPanel");
            var infoPanel = ui.RegisterPanel<InfoPanel>("VerticalSliceInfoPanel");
            var progressPanel = ui.RegisterPanel<ProgressPanel>("BaselineProgressPanel");
            var pausePanel = ui.RegisterPanel<PauseMenuPanel>("PauseMenuPanel");
            var questionnairePanel = ui.RegisterPanel<QuestionnairePanel>("QuestionnairePanel");
            var breathingPanel = ui.RegisterPanel<BreathingPanel>("BreathingPanel");

            var tablet = TabletDisplayController.CreateOrFind(corridorRoot.transform);
            // Production uses one deterministic corridor-space pose. In particular,
            // do not discover/attach TabletAnchor under ClawPalm: inheriting that
            // animated hierarchy made standalone position and facing differ from Play.
            tablet.PlaceFixedInCorridor(corridorRoot.transform,
                corridorSpawn.position + Vector3.up * 1.6f);
            GameObject consoleShell = FindSceneObject("Console_BlenderPrototype") ??
                                      FindSceneObject("Console_Placeholder");
            if (consoleShell == null) throw new InvalidOperationException("Console_BlenderPrototype is required.");
            Transform controls = ConsoleLayoutBuilder.Build(consoleShell.transform, consoleLayout);
            var inputRouter = GetOrAdd<ConsoleInputRouter>(systemsRoot.gameObject);
            inputRouter.Initialize(controls, ConsoleLayoutBuilder.DefaultBindings());
            GetOrAdd<OVRControllerInputAdapter>(systemsRoot.gameObject).Initialize(inputRouter);
            GetOrAdd<KeyboardInputAdapter>(systemsRoot.gameObject).Initialize(inputRouter);
            var rayUi = GetOrAdd<QuestRayUiSystem>(systemsRoot.gameObject);
            rayUi.Initialize(ovrRig, ovrRig.rightControllerAnchor, ui);
            var controllerPoke = GetOrAdd<QuestControllerPokeSystem>(systemsRoot.gameObject);
            controllerPoke.Initialize(ovrRig);
            if (!controllerPoke.IsInitialized)
                throw new InvalidOperationException(
                    "Quest controller poke initialization failed: " + controllerPoke.Status);

            // Robot arm tablet presenter (arm rules: FABLE_ARCHITECTURE_MAP §16).
            // Fallback is a static arm — never a blocker.
            var armPresenter = GetOrAdd<RobotArmTabletPresenter>(systemsRoot.gameObject);
            GameObject armScene = FindSceneObject("RobotArm_Scene");
            if (armScene != null) armPresenter.Initialize(armScene.transform);
            pauseController.PauseChanged += (paused, _) => armPresenter.SetPaused(paused);
            zones.ZoneChanged += zone =>
            {
                if (zone == SceneZoneController.Zone.Corridor) armPresenter.MoveToPresent();
                else armPresenter.MoveToRest();
            };

            // Wrist-watch placeholder on the left controller (spec §36). CreateOrFind
            // reuses an existing watch on repeated bootstrap; a missing controller
            // anchor falls back to the rig root instead of aborting boot.
            // Numeric BPM is shown to the participant ONLY during the baseline phase
            // (sensor-check confidence); hidden during tasks so it is not a stressor.
            WristWatchDisplay.CreateOrFind(ovrRig.leftControllerAnchor, xrRigRoot,
                _heartRate, false,
                () => stateMachine.Current == AppState.Baseline);

            // Voice/subtitle skeleton (spec §37): clips are optional; every cue
            // works subtitle-only. Recording later = drop wavs into
            // Assets/Resources/VoiceCues/<CueId>.wav (see VOICE_SCRIPT_BCS.md).
            var subtitles = runtimeUiRoot.gameObject.GetComponent<StressTraining.Audio.SubtitlePresenter>();
            if (subtitles == null)
                subtitles = runtimeUiRoot.gameObject.AddComponent<StressTraining.Audio.SubtitlePresenter>();
            subtitles.Initialize(ui.MainCanvas.transform);
            var voice = GetOrAdd<StressTraining.Audio.VoiceCueManager>(runtimeUiRoot.gameObject);
            voice.Initialize(new StressTraining.Audio.VoiceCueConfig(), subtitles);
            ServiceRegistry.Install(voice);

            _taskRunner = new TaskRunner(tablet, inputRouter, _heartRate);
            _productionFlow = new ProductionSessionFlow(config, stateMachine, _errors, paths,
                profileRepository, sessionRepository, profileService, _heartRate, _taskRunner,
                zones, ui, tablet, infoPanel, progressPanel, sessionClock, pauseController)
            {
                QuestionnairePanel = questionnairePanel,
                BreathingPanel = breathingPanel,
                Voice = voice
            };
            _productionFlow.RebuildModularZones = layoutSeed =>
            {
                _ = layoutSeed;
                ConsoleLayoutBuilder.RevalidateProductionLayout(controls, consoleLayout);
                inputRouter.Initialize(controls, ConsoleLayoutBuilder.DefaultBindings());
            };

            // FAZA 7 pressure layer: one runtime component, idempotent initialization,
            // subordinate to ProductionSessionFlow. No scene wiring is required.
            var pressure = GetOrAdd<PressureController>(systemsRoot.gameObject);
            // Prefer the imported modular puzzle corridor as the visible collapse
            // model. When present it disables the procedural slabs; when missing,
            // PressureController falls back to the procedural blockout with a warning.
            var puzzleController = FindOrBindPuzzleCorridor();
            if (puzzleController != null) pressure.AttachPuzzleCorridor(puzzleController);
            pressure.Initialize(corridorRoot.transform, viewer, corridorSpawn);
            _productionFlow.SetPressureSystem(pressure);

            // Fall / loss handling (over a collapsed floor, or on TimeExpired). Armed
            // only in the corridor; ComfortFade by default (never a scripted camera
            // drop during a measured session — see FallMode).
            var fallController = GetOrAdd<PlayerFallController>(systemsRoot.gameObject);
            var physicsCharacter = FindSceneObject(SceneZoneController.PhysicsCharacterName);
            fallController.Initialize(xrRigRoot, viewer,
                physicsCharacter != null ? physicsCharacter.transform : null,
                puzzleController, config.developer);
            zones.ZoneChanged += zone =>
                fallController.SetArmed(zone == SceneZoneController.Zone.Corridor);
            // So a real timed-out session ends exactly like the dev preview: finale →
            // loss consequence (fade/devirt/pad + hold) → finish.
            _productionFlow.FallController = fallController;

            // Smooth thumbstick locomotion — the scene's FirstPersonLocomotor has no
            // input handler wired, so without this the sticks do nothing.
            GetOrAdd<ThumbstickLocomotionDriver>(systemsRoot.gameObject).Initialize(viewer);

            _coordinator = new SessionCoordinator(config, stateMachine, _errors, paths,
                profileRepository, sessionRepository, profileService, _heartRate, _taskRunner,
                zones, ui, tablet, profilePanel, infoPanel, progressPanel, pausePanel,
                sessionClock, pauseController, _productionFlow)
            {
                Voice = voice,
                FallController = fallController
            };
            fallController.PlayerLost += _coordinator.HandlePlayerLost;

            ServiceRegistry.Install(_errors);
            ServiceRegistry.Install(configService);
            ServiceRegistry.Install(config);
            ServiceRegistry.Install(paths);
            ServiceRegistry.Install(profileRepository);
            ServiceRegistry.Install(sessionRepository);
            ServiceRegistry.Install(profileService);
            ServiceRegistry.Install(stateMachine);
            ServiceRegistry.Install(sessionClock);
            ServiceRegistry.Install(pauseController);
            ServiceRegistry.Install(_simulatedHr);
            ServiceRegistry.Install(_heartRate);
            ServiceRegistry.Install(zones);
            ServiceRegistry.Install(ui);
            ServiceRegistry.Install(inputRouter);
            ServiceRegistry.Install(rayUi);
            ServiceRegistry.Install(controllerPoke);
            ServiceRegistry.Install(tablet);
            ServiceRegistry.Install(_taskRunner);
            ServiceRegistry.Install(pressure);
            ServiceRegistry.Install(_productionFlow);
            ServiceRegistry.Install(_coordinator);

            _coordinator.Boot();
            _initialized = true;
            Debug.Log("[AppBootstrapper] Vertical slice initialized at " + paths.Root);
        }

        private void Update()
        {
            if (_initialized) _coordinator.Tick(Time.unscaledDeltaTime);
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }

        private static Transform FindOrCreateRoot(string name)
        {
            var found = FindSceneObject(name);
            return found != null ? found.transform : new GameObject(name).transform;
        }

        private static GameObject FindSceneObject(string name)
        {
            return Resources.FindObjectsOfTypeAll<Transform>()
                .Where(t => t != null && t.gameObject.scene.IsValid() && t.name == name)
                .Select(t => t.gameObject).FirstOrDefault();
        }

        private static Transform FindRigRoot()
        {
            var rig = Resources.FindObjectsOfTypeAll<OVRCameraRig>()
                .FirstOrDefault(r => r != null && r.gameObject.scene.IsValid());
            return rig != null ? rig.transform : null;
        }

        /// <summary>
        /// Finds the imported puzzle corridor root in the scene and binds a
        /// <see cref="PuzzleCorridorController"/> to it. Returns null when the model
        /// has not been imported yet, so pressure falls back to the procedural
        /// blockout (with a console warning) instead of failing.
        /// </summary>
        private PuzzleCorridorController FindOrBindPuzzleCorridor()
        {
            // Unity names the instantiated FBX root after the FILE, not after the
            // model's own root node, so a freshly dragged-in model is called
            // "PuzzleCorridor". Fall back to that, and finally to locating the root
            // through the safe segment, so a re-import can never silently disable
            // the puzzle corridor.
            GameObject rootObject = FindSceneObject(PuzzleCorridorController.RootNodeName)
                                    ?? FindSceneObject("PuzzleCorridor")
                                    ?? FindPuzzleRootViaSafeSegment();
            if (rootObject == null)
            {
                Debug.LogWarning("[AppBootstrapper] Puzzle corridor '" +
                    PuzzleCorridorController.RootNodeName +
                    "' not found in scene — pressure uses the procedural blockout fallback.");
                return null;
            }
            var controller = rootObject.GetComponent<PuzzleCorridorController>();
            if (controller == null) controller = rootObject.AddComponent<PuzzleCorridorController>();
            if (!controller.Bind(rootObject.transform))
            {
                Debug.LogError("[AppBootstrapper] Puzzle corridor root found but no Segment_* nodes bound.");
                return null;
            }
            Debug.Log("[AppBootstrapper] Puzzle corridor bound: " + controller.Diagnostics());
            return controller;
        }

        /// <summary>
        /// Last-resort discovery: the safe segment is unique to the puzzle model, so
        /// its parent is the corridor root whatever the instance happens to be called.
        /// </summary>
        /// <summary>Finds a hand-placed TabletAnchor anywhere in the scene.</summary>
        private static Transform FindTabletAnchor()
        {
            foreach (var gizmo in Resources.FindObjectsOfTypeAll<TabletAnchorGizmo>())
                if (gizmo != null && gizmo.gameObject.scene.IsValid())
                    return gizmo.transform;
            return null;
        }

        private static GameObject FindPuzzleRootViaSafeSegment()
        {
            GameObject safe = FindSceneObject("Segment_Safe");
            Transform parent = safe != null ? safe.transform.parent : null;
            return parent != null ? parent.gameObject : null;
        }

        private static Transform EnsureCorridorSpawn(Transform corridor)
        {
            var existing = corridor.Find("CorridorSpawn");
            if (existing != null) return existing;
            var spawn = new GameObject("CorridorSpawn").transform;
            spawn.SetParent(corridor, false);
            spawn.position = new Vector3(0f, 0f, -3.2f);
            spawn.rotation = Quaternion.Euler(0f, 180f, 0f);
            return spawn;
        }

        private static Transform EnsureCorridorUiAnchor(Transform corridor)
        {
            var existing = corridor.Find("CorridorUIAnchor");
            if (existing != null) return existing;
            var anchor = new GameObject("CorridorUIAnchor").transform;
            anchor.SetParent(corridor, false);
            anchor.position = new Vector3(0f, 1.55f, -3.65f);
            anchor.rotation = Quaternion.Euler(0f, 180f, 0f);
            return anchor;
        }

        private void OnDestroy()
        {
            if (_active != this) return;
            ShutdownRuntime();
        }

        private void ShutdownRuntime()
        {
            _coordinator?.Dispose();
            _productionFlow?.Dispose();
            _taskRunner?.Dispose();
            // Stops ALL configured HR sources (incl. the Network UDP thread —
            // a leaked receive thread would keep the port bound across runs).
            _heartRate?.Shutdown();
            ServiceRegistry.Reset();
            _initialized = false;
            if (_active == this) _active = null;
        }
    }
}
