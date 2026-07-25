# FABLE_ARCHITECTURE_MAP.md

Kreirao: Fable iteracija, 2026-07-13. Izvor istine: trenutni kod (ne stariji izvještaji).
Stanje verifikovano čitanjem koda + THREE_TASK_DEMO_REPORT.md + CODEX_PROGRESS_AUDIT.md.
Korisnik je RUČNO POTVRDIO na Questu da tri demo taska rade (N-back, Go/No-Go, Flanker).

## 1. Bootstrap tok (AKTIVNO, scene-integrisano)

```
MainScene: AppRoot (scene objekat, serialized reference preko StressTrainingSceneInstaller)
  └─ AppBootstrapper (MonoBehaviour, composition root)
      Start() → InitializeRuntime():
        ServiceRegistry.Reset → AppErrorService → PersistencePaths(Application.persistentDataPath)
        → ConfigService/StressTrainingConfig → SchemaMigrationService → BackupRecoveryService
        → ProfileRepository → SessionRepository → UserProfileService → AppStateMachine
        → SessionClock → PauseController → SimulatedHeartRateSource → HeartRateService
        → SafeSpaceBuilder.Build (idempotentno) → SceneZoneController.Initialize
        → UIManager.Initialize + RegisterPanel (ProfileSelectionPanel, InfoPanel,
          ProgressPanel, PauseMenuPanel)
        → TabletDisplayController.CreateOrFind + AlignReadableFaceToViewer
        → ConsoleLayoutBuilder.Build (na ConsoleControlsAnchor, child Console_BlenderPrototype)
        → ConsoleInputRouter.Initialize + OVRControllerInputAdapter + KeyboardInputAdapter
        → QuestRayUiSystem.Initialize (OVRInputModule + OVRRaycaster, desni controller ray)
        → QuestControllerPokeSystem.Initialize (REUSE Meta OVRInteractionComprehensive
          PokeInteractors iz scene; fallback: prefab iz Resources/QuestPokePrefabReference)
        → new TaskRunner(tablet, router, heartRate)
        → new SessionCoordinator(...18 zavisnosti...) → coordinator.Boot()
      Update() → _coordinator.Tick(Time.unscaledDeltaTime)
      OnDestroy → ShutdownRuntime (Dispose lanac + ServiceRegistry.Reset)
```

## 2. Ownership servisa

| Servis | Vlasnik/kreator | Životni ciklus | Ticked by |
|---|---|---|---|
| AppStateMachine | AppBootstrapper | app lifetime | event-driven |
| SessionClock + PauseController | AppBootstrapper (JEDNA instanca, dijeli je ActiveSessionContext) | app lifetime, StartSession per sesija | SessionCoordinator.Tick |
| SessionCoordinator (plain C#, IDisposable) | AppBootstrapper | app lifetime | AppBootstrapper.Update |
| TaskRunner (plain C#, IDisposable) | AppBootstrapper | app lifetime | SessionCoordinator.Tick (samo ActiveSession) |
| HeartRateService | AppBootstrapper | app lifetime | SessionCoordinator.Tick |
| UIManager/paneli | AppBootstrapper (RegisterPanel = AddComponent na UIManager GO) | app lifetime | Unity |
| Writers (Trial/Hr/Event JSONL) | SessionCoordinator per sesija | StartDemo → FinishSession/Dispose | — |
| ConsoleControlsRoot | ConsoleLayoutBuilder.Build pri Play (anchor serialized u sceni) | scene | Unity |

## 3. State machine

`Core/AppState.cs` (17 stanja) + `Core/AppStateMachine.cs` (eksplicitna transition tabela,
RequestTransition/ForceTransition/CanTransition, history, eventi). Demo koristi podskup:
Boot→ProfileSelection→Preparation→Ready→ActiveSession⇄Paused→SessionEnding→PostSessionSummary→ProfileSelection.
Demo prezentacioni sloj: `DemoFlowStage` enum + `ThreeTaskDemoFlow` statičko mapiranje
(tutorial/practice/review/countdown/block per task). NIJE druga state mašina — čisto
prezentaciono stanje unutar SessionCoordinator-a.

## 4. Demo flow (AKTIVNO — ručno potvrđeno na Questu; NE SMIJE SE SLOMITI)

SessionCoordinator: profile selection (SafeSpace, ray UI) → ShowDemoOverview →
StartDemo (kreira DemoOnly sesiju, writers, seed=config.developer.verticalSliceSeed)
→ per task: ShowTaskTutorial (SafeSpace) → BeginPractice (ulazak u corridor,
TaskRunner.StartPractice, DemoTaskFactory fiksne practice sekvence sa practiceCue)
→ ShowPracticeReview (Ponovi vježbu / Nastavi) → ContinueAfterPractice (countdown)
→ StartScoredBlock (TaskRunner.StartScoredDemoBlock, seeded) → OnBlockCompleted
→ sljedeći task ili FinishSession → summary (DemoSummaryFormatter, bez HR-a) → profili.

Demo izolacija: ValidityStatus.DemoOnly, cycleId="demo-<id>", NE dira activeCycleId,
cycle index, lastSessionAtUtcIso (osim ne-demo grana), nivoe, next recommendation.
Practice: TaskRunKind.Practice → TaskRunPolicy blokira persist/HR-aggregate/score.

## 5. Task runtime

- `TaskContracts.cs`: TaskRunKind (Practice/Scored), TaskRunPolicy, TrialPhase (+Aborted),
  TaskTrialDefinition (+practiceCue, congruency), TaskTrialResult, TaskBlockResult.Aggregate,
  ITaskRuntime (+Abort/Reset/BlockAborted).
- `TaskRuntimeBase.cs`: ITI→Stimulus→ResponseOpen→Feedback; first-response latch;
  pause interrupt = bezbjedno ponavljanje trial-a; Abort/Reset.
- Taskovi: `NBackTask.cs` (warm-up=n, exact match count), `GoNoGoTask.cs` (exact no-go
  count, commission/omission), `FlankerTask.cs` (+CentralDirection helper, congruency effect).
- `TaskDifficultyConfig.cs`: centralna tabela nivoa 1-3 po tasku (PROJECT_HEURISTIC).
- `TaskSeedService.cs`: DeriveBlockSeed(masterSeed, blockIndex, taskType).
- `DemoTaskFactory.cs`: fiksne practice sekvence + clamp-ovani kratki scored blokovi;
  IsActionForTask; DisplayName.
- `TaskRunner.cs`: jedan aktivni runtime; input filter (warmup/pause/task-relevantnost);
  TrialRecord persist (samo Scored); tablet stimulus/progress/feedback; HR agregacija po bloku.

## 6. Input routing

```
FIZIČKI: Meta PokeInteractor (scene OVRInteractionComprehensive, reuse)
  → PokeInteractable na svakom dugmetu (ConsoleLayoutBuilder.AddCircularPokeSurface)
  → ConsoleControlBase (latch, debounce, vizuelna stanja, haptika hook)
  → Activated event → ConsoleInputRouter (bindings controlId→SemanticAction,
     SetActiveTask gating, SetGameplayInputEnabled) → ActionTriggered
  → TaskRunner.OnActionTriggered → ITaskRuntime.SubmitAction
MENIJI: QuestRayUiSystem (OVRInputModule/OVRRaycaster) → uGUI Button na MenuPanelBase redovima
ADAPTERI: OVRControllerInputAdapter (SAMO Start→PauseToggle), KeyboardInputAdapter (UNITY_EDITOR only)
```
Konzola je na Ignore Raycast layeru — menu ray ne aktivira task kontrole.

## 7. Konzola (trenutna)

`ConsoleLayoutConfig` (serialized na AppBootstrapper): anchorLocalPosition (0,0.985,-0.023)
na Console_BlenderPrototype; primarni red spacing 0.15; **POZNAT BUG: LEFT je na local X
-0.30 = korisnikova DESNA strana** (korisnik gleda -Z, njegova lijeva = world +X).
Redosljed build-a: Left(-2s) Match(-s) Go(0) NoMatch(+s) Right(+2s) + ind_status + PAUSE.
Legacy kontrole (confirm/toggle/lever/knob/distraktori) iza `showLegacyDemoControls=false`.
`RevalidateExistingStandardLayout` čini Build idempotentnim.

## 8. Tablet

`TabletDisplayController` (display-only; CreateOrFind("TaskTablet") na corridor rootu;
AlignReadableFaceToViewer). API: ShowInstruction/ShowStimulus/HideStimulus/ShowCountdown/
HideCountdown/SetHeader/SetTimer/SetHrZone/SetActiveControls/SetTrialProgress/
ShowTrialFeedback/ClearFeedback/SetPausedOverlay/ShowIdle. `TabletViewModel.Decode`
(sym:/col:/pos:/go:T/nogo:T/arr:) — **Go/No-Go trenutno koristi BOJU (zelena/crvena) —
POZNAT ZAHTJEV: redizajn u crno-na-bijelom GO / NO GO tekst.**

## 9. Profili i persistence

`UserProfileData` (schemaVersion=1, nivoi: nback/gonogo/flanker/pressure — **NEMA
currentCorsiLevel — treba migracija v2**). ProfileRepository (atomic+backup+recovery+index),
SessionRepository (abandoned detection), JSONL writeri, QuestionnaireRepository,
SchemaMigrationService (v1 identitet; ovdje ide v1→v2 Corsi migracija).
Session zapis: SessionSummaryData v1 — **treba proširenje: taskSelectionSeed, blockOrderSeed,
pressureSeed, consoleLayoutSeed, selectedTasks, omittedTask, globalDuration, scheduleOverride…**

## 10. HR

`IHeartRateSource` (DrainSamples pattern) + SimulatedHeartRateSource (scenariji) +
NetworkHeartRateSource (UDP 0.0.0.0:5005, bg thread, reconnect) + HrPacketParser
(novi/legacy format) + HeartRateZoneEvaluator (baseline-relative) + HeartRateService
(hub; AttachSession context provider; agregacija per blok; maskiranje simuliranih
vrijednosti radi Codex fix-a). Demo koristi ISKLJUČIVO Simulated. **Nedostaje: enum
HR modova (Disconnected/Simulated/Network/NativeAdbPlaceholder), NativeAdb sloj, watch.**
Python `hr_dashboard_v2.py` v3 = dev/dijagnostički bridge (ostaje).

## 11. Upitnici / Adaptacija / Session pomoćni (KOD POSTOJI, NIJE U RUNTIME TOKU)

- Questionnaires: katalog (SSQ/TLX/STAI-6, NEEDS_VALIDATED_ITEM_TEXT), scoring, flow,
  QuestionnairePanel (registrovan NIJE).
- Adaptation: AdaptationConfig/Input/RuleSet/Scheduler/ExplanationBuilder — čisto,
  determinističko; NIKO ga ne poziva. **Treba proširenje za Corsi + selection prioritete.**
- Session: SessionPlanGenerator (3-task interleaved; demo ga NE koristi),
  BaselineAccumulator, RecoveryEvaluator, SessionValidityEvaluator (nema
  IncompleteTimeExpired), UserProfileService (eligibility/cycles dijelom korišćen).
- UI: BreathingPanel, ProgressPanel postoje; QuestionnairePanel postoji — nepovezani.
- Tablet: RobotArmTabletPresenter postoji — NIJE instanciran nigdje.

## 12. Legacy (bez aktivnih referenci u novom toku)

GameManager, PuzzleManager, PuzzleButton, StressRoomController, DebriefingManager,
HRReceiver, HRDisplay (globalni namespace, kompajliraju u StressTraining assembly),
RobotArmPoseTester (JEDINI stari script serijalizovan u sceni; TEMP inspekcioni alat,
playTestLoop=false), Editor/SpatialBlockoutBuilder, Editor/ConsolePrototypeBuilder.
Legacy console kontrole (toggle/lever/knob/central/distraktori) iza config flag-a.

## 13. Scene integracija

MainScene (modified, uncommitted): AppRoot+AppBootstrapper (serialized refs),
SystemsRoot, RuntimeUIRoot, DebugRoot, FeedbackManager (Meta haptics prefab),
Corridor_Blockout/CorridorSpawn, Console_BlenderPrototype/ConsoleControlsAnchor,
Assets/Resources/QuestPokePrefabReference.asset. SafeSpaceRoot/ConsoleControlsRoot/
TaskTablet/MainUICanvas se generišu pri Play (runtime, idempotentno).
Installer: `Editor/StressTrainingSceneInstaller.cs` (menu "Stress Training/Install
Vertical Slice", backup + report + idempotentan).

## 14. Test assemblies

EditMode (15 fajlova; ~41 test/case deklarisano): AppStateMachine, AtomicFileWriter,
ConsoleInteraction, DemoSummaryFormatter, DemoTaskFactory, Flanker, GoNoGo, NBack,
HeartRateService, ProfileRepository, SafeSpaceUiLayout, SessionClock, UsernameValidator,
WorldSpaceUiOrientation. PlayMode: VerticalSliceSmokeTests (HMD-free full demo smoke).
Zvanični Unity Test Runner NIJE izvršen u prethodnoj iteraciji (samo compile PASS).

## 15. Mapa novih funkcija ove iteracije

| Nova funkcija | Proširuje postojeće | Novi tip (ako treba) | Ne smije duplirati |
|---|---|---|---|
| LEFT/RIGHT fix | ConsoleLayoutBuilder/Config | — | novi layout builder |
| Go/No-Go B/W | TabletViewModel.Decode + TabletDisplayController.RenderStimulus | — | novi tablet |
| UI overlap fix | MenuPanelBase (footer/body separacija) | — | novi panel sistem |
| Production flow | SessionCoordinator (podređeni podflow) | ProductionSessionFlow (samo ako podređen koordinatoru) | drugi koordinator/state mašina |
| 3-of-4 izbor | — | SeededConstrainedTaskSelector (Session) | scheduler randomizacija bez seed-a |
| 15-blok plan | SessionPlanGenerator → ProductionSessionPlanGenerator (ekstenzija) | ProductionSessionPlan polja u SessionPlan | novi plan DTO paralelan |
| Corsi | TaskType enum append (CorsiSequence=4), TaskDifficultyConfig, TaskRunner factory putevi | CorsiTask.cs (definition+runtime), CorsiDifficultyConfig sekcija | poseban task framework |
| Corsi konzola | ConsoleLayoutBuilder + ConsoleLayoutConfig | CorsiControlsAnchor, CORSI_0..8 kontrole | drugi input router |
| Modularne zone | ConsoleLayoutBuilder | ModularLeft/RightAnchor + seeded slot layout | — |
| Global timer | SessionClock (remaining global time) | — | drugi clock |
| Pressure | — | StressTraining.Pressure.* (Controller/Timeline/LevelConfig/Segment/Collapse/GameOver) | tiger/cage logika |
| Upitnici runtime | SessionCoordinator + UIManager.RegisterPanel(QuestionnairePanel) | — | novi questionnaire engine |
| Scheduler 4-task | AdaptationScheduler/Input/RuleSet/Config | selection priority polja | paralelni scheduler |
| Profile v2 | UserProfileData schemaVersion=2 + SchemaMigrationService v1→v2 | — | novi profile format |
| HR modovi | HeartRateService + HrSourceType append | NativeAdbHeartRateSource, INativeAdbBridge, NativeAdbConnectionState | treći persistence/input sistem |
| Wrist watch | — | StressTraining.HR.WristWatchDisplay (primitives) | tablet duplikat |
| Voice/subtitle | — | StressTraining.Audio.* (VoiceCueId/Definition/Library/Manager/SubtitlePresenter/Config) | — |
| Dev panel | UIManager panel | StressTraining.Debugging.DeveloperPanel | — |

## 16. Robot arm pivot audit (iz CLAUDE.md + koda; model NIJE mijenjan)

RobotArm_Scene → RobotArm_Placeholder (FBX) → RobotArm_Main_WallMount →
MountPlate/Bolts [FIXED] + BasePivot(Y) → ShoulderPivot(X) → ElbowPivot(X) → WristPivot(X).
KRITIČNO: sva 4 pivota ko-locirana na FBX originu iznad meša → samo mali uglovi
(testirano: Base ±12°, Shoulder −35..0°, Elbow −45..−10°, Wrist +5..+30°), uvijek
aditivno od rest pose-a. Klješta nemaju pivote (Blender fix potreban za claw).
RobotArmTabletPresenter postoji i poštuje pravila; tablet se NE parentuje na ruku
(ko-locirani pivoti → vizuelni detach); ruka radi prezentacioni gest. Fallback: statična.
