# FABLE_MASTER_PROGRESS.md — append-only dnevnik

---

## 2026-07-13 — FAZA 2 + FAZA 3 (core) / M2_PRODUCTION_FLOW_SKELETON + M3_CORSI_FUNCTIONAL (kod)

- **Završeno — FAZA 2 (production skeleton)**:
  - Data v2: `TaskType.CorsiSequence=4`, `ValidityStatus.IncompleteTimeExpired=9`,
    `HrSourceType.NativeAdbPlaceholder=3` + `HeartRateMode` enum, `SemanticAction.Corsi0..8`
    (svi APPEND — bez reorder-a).
  - `UserProfileData` v2: `currentCorsiLevel`, `tutorialStates` (TaskTutorialStateData),
    `GetTaskLevel/GetOrCreateTutorialState`; `SessionSummaryData` v2: production polja
    (selection/blockOrder/pressure/layout seedovi, selected/omitted, blockOrder,
    globalDuration, remainingAtEnd, scheduleOverride, pressureStageAtEnd, flanker/corsi
    task-summary metrike); `SessionPlan` v2; `TrialRecord` v2 (corsi polja +
    remainingGlobalTimeSeconds).
  - `SchemaMigrationService`: v0/v1→v2 za profile i sesije (additive, normalizacija,
    newer-schema nikad downgrade).
  - `SeededConstrainedTaskSelector` (sel-v1): deterministički 3-of-4, forced include
    prethodno izostavljenog (validna produkcija), config-disable, reason codes,
    `ResolvePreviousOmitted` ignoriše demo/invalid.
  - `ProductionSessionPlanGenerator`: 15 blokova (5×3), seeded balansirani interleave
    bez uzastopnog ponavljanja, imenovani sub-seedovi (`TaskSeedService.DeriveNamedSeed`
    + salts), neutral/pressure dijele identičan task sadržaj.
  - `SessionClock`: globalni timer (SetGlobalDuration/Remaining/Expired/Fraction) +
    `AddPenaltySeconds`; `TimerPenaltyConfig` proširen (per-error/per-task/pressure
    multiplikatori, minRemaining — SVE default disabled); `SessionConfig` production
    polja (globalDuration 900s PROJECT_HEURISTIC, task pool toggles).
  - `ProductionSessionFlow` (podređen SessionCoordinator-u): puni stage tok
    (SSQ placeholder → STAI placeholder → baseline priprema → PRAVI baseline
    (BaselineAccumulator + zone) → coping → tutorial decision (per-task verzije,
    backward Corsi = nova verzija) → ready → corridor + global timer start →
    15-blok petlja BEZ povratka u SafeSpace (between-block tablet ekran + countdown +
    global timer prikaz) → time-expired/user-termination → recovery (RecoveryEvaluator)
    → post SSQ/TLX/STAI placeholderi → validity (+TimeExpired/ScheduleOverride) →
    adaptation stub (FAZA 6) → save (CompleteSession samo usable+completed) → summary.
  - `SessionCoordinator`: mode selekcija (produkcija sa eligibility/override tokom vs
    demo), `Ctx` preusmjeravanje clock/pause na produkcijski kontekst, delegacija
    BlockCompleted/EndSession/Tick, dispose. `AppBootstrapper` kreira i instalira flow.
  - `SessionValidityEvaluator`: `TimeExpired` → IncompleteTimeExpired; ScheduleOverride
    → warning.
- **Završeno — FAZA 3 (Corsi core)**:
  - `CorsiLayout` (9 nepravilnih pozicija; ISTI izvor za tablet mapu i konzolu;
    user-perspective mirror za fizičku zonu; ActionFor/IndexOf/ControlId CORSI_0..8).
  - `CorsiTaskDefinition` (deterministički generator; progresivna dužina; encode/decode
    "corsi:2-7-4"); `CorsiDifficultyConfig` = Corsi sekcija u TaskDifficultyConfig
    (L1/L2 forward, L3 BACKWARD; PROJECT_HEURISTIC).
  - `CorsiTaskRuntime` (ITaskRuntime; prezentacija korak-po-korak → retention →
    response window; first-error termination; timeout; pause restart istog triala;
    Abort/Reset; puni CorsiTrialDetail).
  - Integracija: ConsoleInputRouter (CorsiActions + status boja), DemoTaskFactory
    (practice 3 sekvence, demo clamp 4–6, runtime factory, DisplayName
    „Sekvencijalna memorija“), TaskRunner (Corsi eventi → tablet mapa; kontrole
    ISKLJUČENE tokom prezentacije, uključene na ResponsePhaseStarted; persist corsi
    polja; StartScoredPlannedBlock za produkciju), TabletViewModel/Display (CorsiMap
    kind, 9-cell mapa, lit/dim/response hint; mapa ostaje kroz retention/response).
- **Novi fajlovi**: Session/SeededConstrainedTaskSelector.cs,
  Session/ProductionSessionPlanGenerator.cs, Session/ProductionSessionFlow.cs,
  Tasks/CorsiTask.cs, Tests/EditMode/FableProductionSkeletonTests.cs,
  Tests/EditMode/FableCorsiTests.cs.
- **Izmijenjeni**: Data/CoreEnums, Data/SemanticAction, Data/UserProfileData,
  Data/SessionSummaryData, Data/SessionPlanData, Data/TrialRecord,
  Persistence/SchemaMigrationService, Tasks/TaskSeedService, Tasks/TaskDifficultyConfig,
  Tasks/TaskContracts (CorsiTrialDetail), Tasks/DemoTaskFactory, Tasks/TaskRunner,
  Console/ConsoleInputRouter, Tablet/TabletViewModel, Tablet/TabletDisplayController,
  Core/SessionClock, Core/StressTrainingConfig, Session/ActiveSessionContext,
  Session/SessionValidityEvaluator, Session/SessionCoordinator, Core/AppBootstrapper.
- **Scene**: bez izmjena (fizička Corsi dugmad = FAZA 4).
- **Compile**: pokušaj slijedi (Coplay retry).
- **Poznato otvoreno**: fizička Corsi zona + modularni slotovi (F4), upitnici (F5),
  scheduler 4-task (F6), pressure/game-over prezentacija (F7).
- **Naredna akcija**: FAZA 4 — CorsiControlsAnchor + 9 dugmadi + modularne bočne zone
  (seeded) u ConsoleLayoutBuilder; RobotArmTabletPresenter produkcijska integracija.

---

## 2026-07-13 — FAZA 1 / M1_EXISTING_DEMO_STABLE (kod završen, compile provjera slijedi)

- **Faza**: FAZA 1 — Stabilizacija tri postojeća taska
- **Završeno**:
  1. **LEFT/RIGHT fix**: `ConsoleLayoutBuilder` — novi `UserPerspectiveX(userSlot, spacing)`
     helper (igrač gleda −Z ⇒ njegova lijeva = local +X); primijenjen u `Build` I
     `RevalidateExistingStandardLayout`. Red sada iz perspektive korisnika čita
     LEFT | MATCH | GO | NO MATCH | RIGHT.
  2. **Go/No-Go crno-na-bijelom**: `TabletViewModel.Decode` → GoNoGo = bijela pozadina +
     tekst "GO"/"NO GO"; `GoNoGoColor` markiran [Obsolete] (vraća bijelu);
     `TabletDisplayController.RenderStimulus` → bijeli panel proširen na ~cijelu
     aktivnu površinu (496×268), crn tekst, bez "STOP" teksta; tutorial/practice
     tekstovi u `SessionCoordinator` i cue u `DemoTaskFactory` očišćeni od boja.
  3. **UI overlap fix**: `MenuPanelBase` — BodyContainer sa `RectMask2D` (klipovanje),
     opcije su bottom-anchored footer koji rezerviše prostor; `BodyBottomOffset` se
     preračunava u `SetOptions`; javni `BodyBottomOffset`/`FooterHeight` za testove.
  4. **Regresioni testovi**: `Tests/EditMode/FableStabilizationTests.cs` (6 testova:
     user-perspective mapping, izgrađeni red, GoNoGo B/W + tier-invarijantnost,
     body/footer separacija, footer rast).
- **Izmijenjeni fajlovi**: Console/ConsoleLayoutBuilder.cs, Tablet/TabletViewModel.cs,
  Tablet/TabletDisplayController.cs, Session/SessionCoordinator.cs (samo tekstovi),
  Tasks/TaskRunner.cs (tekst), Tasks/DemoTaskFactory.cs (tekst), UI/MenuPanelBase.cs.
- **Novi fajlovi**: Tests/EditMode/FableStabilizationTests.cs.
- **Scene**: bez izmjena (layout se generiše pri Play; Revalidate ažurira postojeći root).
- **Compile**: pokušaj slijedi (Coplay/MCP); status će biti upisan ispod.
- **Rizik/napomena**: `RevalidateExistingStandardLayout` premješta postojeća dugmad na
  nove pozicije ako je stari root preživio u sceni — idempotentno i poželjno.
- **Naredna akcija**: compile provjera; zatim FAZA 2 (production skeleton).

---

## 2026-07-13T??:??Z — FAZA 0 / M0_ARCHITECTURE_MAPPED

- **Faza**: FAZA 0 — Audit i checkpoint
- **Milestone**: M0_ARCHITECTURE_MAPPED
- **Završeno**:
  - PRE_FABLE safety snapshot: `_implementation_backup/PRE_FABLE_GIT_STATUS.txt`,
    `PRE_FABLE_DIFF.patch`, `PRE_FABLE_HASHES.sha256`, `pre_fable_files/` (kopije svih
    tracked izmijenjenih fajlova + MainScene.unity).
  - Pročitani: THREE_TASK_DEMO_REPORT, CODEX_PROGRESS_AUDIT, ključni runtime kod
    (AppBootstrapper, SessionCoordinator, TaskRunner, ThreeTaskDemoFlow, DemoTaskFactory,
    TaskContracts, ConsoleLayoutBuilder, QuestControllerPokeSystem, QuestRayUiSystem,
    UiNavigationInput, StressTrainingConfig, AppStateMachine, UserProfileData,
    SessionPlanGenerator/Data, installer, TabletViewModel).
  - FABLE_ARCHITECTURE_MAP.md kreiran (aktivno/demo-only/legacy/nepovezano + mapa novih funkcija).
- **Kreirani fajlovi**: FABLE_ARCHITECTURE_MAP.md, FABLE_MASTER_PROGRESS.md,
  FABLE_CURRENT_STATE.json, NEXT_AGENT_HANDOFF.md, FABLE_RESUME_PROMPT.md,
  MANUAL_ASSET_TASKS.md, SCIENTIFIC_TRACEABILITY.md, backup fajlovi.
- **Izmijenjeni fajlovi**: nijedan projektni runtime fajl još.
- **Scene**: bez izmjena.
- **Compile**: baseline = prethodni Codex izvještaj PASS (DLL artefakti 2026-07-12);
  novi compile još nije pokretan.
- **EditMode testovi**: baseline 41 deklarisan, Test Runner NIJE izvršen (naslijeđeno stanje).
- **PlayMode**: 1 smoke deklarisan, nije izvršen.
- **Ručno potvrđeno (od korisnika)**: tri demo taska rade na Questu; poznati problemi:
  UI overlap tutorial dugmadi, LEFT/RIGHT zamijenjeni, Go/No-Go pretrpan bojom/tekstom.
- **Neprovjereno**: Unity Editor dostupnost za ovu sesiju (provjera slijedi u FAZI 10).
- **Poznati problemi**: vidi FABLE_CURRENT_STATE.json blockingIssues.
- **Git status**: vidi PRE_FABLE_GIT_STATUS.txt (7 modified tracked, mnogo untracked novih).
- **Naredna akcija**: FAZA 1 — LEFT/RIGHT mirror fix u ConsoleLayoutBuilder (build +
  revalidate putanje), Go/No-Go crno-na-bijelom u TabletViewModel/TabletDisplayController,
  MenuPanelBase footer/body separacija.

---

## 2026-07-13 — FAZA 1–7 (retroaktivni sažetak — sesije su se prekidale prije upisa)

- **Milestones**: M1–M7 (demo stabilizacija; production skeleton 3-of-4 + 15 blokova +
  globalni tajmer; Corsi; konzola v2 + robot arm presenter; pre/post tok sa SSQ/STAI/TLX,
  baseline, coping, recovery, validity; scheduler v2 za 4 taska; pressure 70/50/30/10/0 +
  game over). Detalji po fajlovima: FABLE_ARCHITECTURE_MAP.md + git diff.
- **Verifikacija u trenutku rada**: compile PASS poslije svake faze (Coplay);
  EditMode 74/74 (prije pressure/questionnaire/HR test fajlova).

## 2026-07-14 — FAZA 8 ZAVRŠENA (M8_HR_ARCHITECTURE_COMPLETE) + recovery/stabilizacija

- **Recovery**: rekonstruisano stanje poslije prekida; korisnički questionnaire UI patch
  (QuestionnairePanel/QuestionnaireFlowController/QuestionnairePanelTests, 2026-07-14 07:22)
  IDENTIFIKOVAN I SAČUVAN — paged SSQ (16 Kennedy simptoma, 4 po stranici, dugmad 0–3).
- **FAZA 8 fixevi**:
  - `HeartRateService.Shutdown()` — gasi aktivni + SVE konfigurisane source-ove
    (Network UDP thread više ne preživljava izlazak iz Play Mode-a); poziva se iz
    `AppBootstrapper.ShutdownRuntime()`.
  - `WristWatchDisplay`: `CreateOrFind` (ponovljeni bootstrap ne pravi drugi sat),
    fallback anchor kad left controller anchor ne postoji (`UsedFallbackAnchor`),
    `RefreshNow()` javno za testove, `sharedMaterial` umjesto `material` (bez klona),
    NativeAdbPlaceholder više ne prikazuje "Signal izgubljen" (nikad nije ni bio povezan).
- **Voice (FAZA 9 = PARTIAL, samo stabilizacija)**: svi pozivi `Voice?.Play(...)`, bez
  event pretplata, subtitle fallback bez klipova — kompajlira i radi; ništa novo dodato.
- **Novi testovi**: `FableHrArchitectureTests.cs` — 10 testova (simulated labeling,
  disconnected bez emisije, placeholder nikad Connected, mode switch gasi prethodni source,
  Shutdown idempotentan, participant linija bez BPM, dev linija BPM+mode+SIMULIRANI PODACI,
  fallback anchor, ponovljeni bootstrap bez duplikata, Network mode + clean shutdown).
- **Compile**: PASS (Coplay check_compile_errors, 2026-07-14).
- **EditMode**: **121/121 PASS** (zvanični Unity Test Runner via TestRunnerApi, 2.2 s).
- **PlayMode (HMD-free probe preko execute_script)**: boot OK; 1× bootstrapper/watch/
  pressure/voice/subtitles/questionnaire panel; hrMode=Simulated, receiving=True;
  watchStatus='Povezan · Stabilno' (bez cifara); watchDev='72 bpm · Simulated ·
  SIMULIRANI PODACI'; profil panel aktivan; poslije izlaska 0 MissingReference/Error.
- **Izmijenjeni fajlovi**: HeartRateService.cs, WristWatchDisplay.cs, AppBootstrapper.cs.
- **Novi fajlovi**: Tests/EditMode/FableHrArchitectureTests.cs.
- **Quest**: NIJE verifikovano poslije Fable izmjena (čeka FAZA 10 checklist).
- **Naredna akcija**: FAZA 10 (vidi FABLE_CURRENT_STATE.json → nextExactAction).

## 2026-07-14 — STANDALONE HR PIPELINE (band → telefon → standalone Quest)

Cilj sesije (jedini): stvarni BPM sa Band 9 → Android telefon → standalone Quest APK,
bez računara/interneta u radu. Feasibility-first pristup (spec §5/§6/§11).

- **Odluka o izvodljivosti**: stvarni HrItem POSTOJI u Mi Fitness logcat-u (dokazano
  `HR_REAL_DEBUG_SNAPSHOT.json`). Jedini praktičan put da obična aplikacija bez root-a
  to pročita = `READ_LOGS` (grant jednom preko ADB-a pri instalaciji). Direktni BLE
  (Gadgetbridge-stil), Health Connect, notification listener, native ADB u APK-u —
  provjereni i odbačeni/neprikladni (STANDALONE_HR_ARCHITECTURE.md §2).
- **Android companion `android_companion/HrRelay`** (novo): čita logcat lokalno na
  telefonu, parsira HrItem (port dokazanog Python parsera), šalje protokol-v1 UDP
  unicast na Quest; foreground service + PARTIAL_WAKE_LOCK + heartbeat 5 s; minimalni UI;
  bez cloud-a/naloga/HR istorije. Java (min API 26).
- **Unity očvršćavanje** (izmijenjeno):
  - `HrPacketParser` — `kind`=heartbeat|sample; heartbeat parsira bez BPM-a, nikad uzorak.
  - `HrRelayIngest` (NOVO, pure C#) — identitet sesije (bind na prvi token, odbij drugi),
    dedup, redosljed sekvence (seq 0 = restart), monotonost watch-timestamp-a.
  - `NetworkHeartRateSource` — ingest wiring; heartbeat/reject nikad ne postaju RawHrSample;
    izloženi BoundSessionToken/RejectedPacketCount/LastHeartbeat/LastAcceptedSampleRealtime.
  - `WristWatchDisplay` — participant zonu vidi SAMO u Network modu (Simulated/placeholder/
    disconnected → „Sat nije povezan", bez zone; ispravljen 1 pali test).
  - Produkcijski gate `GetProductionReadiness()` (već postojao) i dalje traži real
    NetworkBridge + svjež validan uzorak; simulated/placeholder odbijeni.
- **Verifikacija (stvarno izvršeno)**:
  - Feasibility parser (pure JVM, realni podatak): **24/24 PASS** (`javac`+`java`).
  - Android companion JUnit: **7/7 PASS** (`gradlew test`).
  - **Android APK build: PASS** — `app-debug.apk` ~3.1 MB (Unity-bundled Gradle 8.13 +
    OpenJDK 17 + build-tools 36; AGP 8.7.2; kotlin-bom 1.8.22 za stdlib konflikt).
  - Unity compile: **PASS**. Unity EditMode: **148/148 PASS** (novi HrRelayIngestTests x8).
- **NIJE verifikovano (traži uređaje, NE tvrditi)**: real band acquisition, phone→Quest
  na uređaju, screen-lock, hotspot, 60-min stabilnost.
- **Build prepreke riješene usput**: MSYS `-g` path (IOException), env ANDROID_HOME MSYS
  form, `local.properties` sa single-backslash escapes (→ forward slashes), AGP tražio
  build-tools 34 u read-only SDK (→ pin 36.0.0), kotlin-stdlib jdk7/jdk8 duplikati (→ kotlin-bom).
- **Novi fajlovi**: `Assets/Scripts/HR/HrRelayIngest.cs`, `Assets/Scripts/Tests/EditMode/
  HrRelayIngestTests.cs`, `STANDALONE_HR_ARCHITECTURE.md`, `STANDALONE_HR_SETUP.md`,
  `STANDALONE_HR_TEST_MATRIX.md`, `STANDALONE_HR_KNOWN_LIMITATIONS.md`, cijeli
  `android_companion/` (HR Relej Gradle projekat + tools/HrItemParserTest.java).
- **Izmijenjeni**: `HrPacketParser.cs`, `NetworkHeartRateSource.cs`, `WristWatchDisplay.cs`.
- **Python bridge**: NETAKNUT (ostaje dev fallback).
- **Naredna akcija**: DEVICE TEST (vidi FABLE_CURRENT_STATE.json → nextExactAction).

## 2026-07-14 — Wrist watch: BPM vidljiv samo tokom baseline-a (zahtjev korisnika)

- `WristWatchDisplay`: participant vidi tačan BPM ("Povezan · 85 bpm") SAMO dok je
  `AppState.Baseline`; poslije baseline-a → samo zona ("Povezan · Stabilno"). Broj se
  prikazuje isključivo za stvaran Network izvor koji prima svjež uzorak.
- Provider `() => stateMachine.Current == AppState.Baseline` proslijeđen kroz
  `WristWatchDisplay.CreateOrFind(...)` iz `AppBootstrapper`. Čist static helper
  `WristWatchDisplay.ParticipantSuffix(...)` (testabilan).
- Kontekst: standalone HR pipeline VERIFIKOVAN na uređaju (PULS 85 bpm, star 0.5 s,
  QUEST povezan, seq raste); vidi STANDALONE_HR_TEST_MATRIX.md.
- Compile PASS; EditMode 149/149 (novi test ParticipantSuffix_ShowsBpmOnlyDuringBaselineOnRealLink).

## 2026-07-14 — Dvije fokusirane korekcije (compile fix + tehnička HR pauza)

### 1. Compile fix (FablePhase86Tests)
- Uzrok: test koristi `TaskSeedService` (namespace `StressTraining.Tasks`) ali je import
  nedostajao. Dodato `using StressTraining.Tasks;`. Nije pravljena nova/duplirana klasa.

### 2. Tehnička pauza kada HR uzorak zastari (spec 8.6)
- Novo: `HrTechnicalPauseController` (čist C# state machine: Inactive→Held→Resuming,
  hook-driven, testabilan). `PauseController` proširen na DVA nezavisna uzroka
  (manual + technical), efektivno = OR, idempotentno po uzroku (nema dvostruke pauze).
- Povezano sa postojećim sistemima (ne pravi novu računicu starosti):
  - freshness = postojeći `HeartRateService.IsReceiving` (prag `HrZoneConfig.staleSignalSeconds=8s`);
    heartbeat ne kreira uzorak → ne osvježava IsReceiving → ne uklanja stale.
  - globalni tajmer: `PauseController.HoldTechnical` → `SessionClock.SetPaused(true)` (global stoji).
  - pressure: isti `PauseChanged` event → `ProductionSessionFlow.OnPauseChanged` → `PressureSystem.SetPaused`.
  - task + input: `TaskRunner.Pause()` (Tick i input već no-op kad je paused).
  - baseline/recovery timer: postojeći gate `if (_pause.IsPaused) return;` u ProductionSessionFlow
    → staje dok traje hold, stale period ne ulazi u trajanje/agregat.
  - resume: jedan 3-2-1 countdown (`_config.session.resumeCountdownSeconds`) pa `ReleaseTechnical` +
    `TaskRunner.Resume`; nastavlja tačno gdje je stalo.
  - abort prag: `_config.session.hrStaleTechnicalAbortSeconds=120s` (PROJECT_HEURISTIC) →
    `ProductionSessionFlow.HandleTechnicalFailure()` → `FinishBlocks(SystemTerminated,...)` →
    validity `InvalidTechnicalFailure` (NE korisnikov poraz, NE TimeExpired) → bezbjedan recovery/post.
  - `SessionValidityEvaluator`: `SystemTerminated && !TimeExpired` → InvalidTechnicalFailure
    (game-over `SystemTerminated && TimeExpired` ostaje IncompleteTimeExpired).
  - odvojeno od ručne pauze: tehnička ne otvara pause meni; ručna pauza blokirana dok traje
    tehnički hold; reset na start/kraj/dispose sesije (nema curenja u narednu sesiju).
- Izmijenjeni: `Core/PauseController.cs`, `Core/StressTrainingConfig.cs`, `Session/SessionCoordinator.cs`,
  `Session/ProductionSessionFlow.cs`, `Session/SessionValidityEvaluator.cs`,
  `Tests/EditMode/FablePhase86Tests.cs`. Novi: `Session/HrTechnicalPauseController.cs`,
  `Tests/EditMode/FableTechnicalPauseTests.cs`.
- Compile PASS; EditMode **177/177 PASS**.
- Ručni Quest test: prekid Mi Fitness/logcat toka tokom aktivnog bloka i baseline-a → provjeriti
  poruku, zamrznut globalni tajmer/pressure/task, 3-2-1 na povratku, nastavak istog bloka;
  te da ručna PAUSE i tehnička pauza ne kvare jedna drugu.

## 2026-07-14 — PAUZA: samo Y na lijevom kontroleru; automatska (tehnička) pauza UKLONJENA

Razlog (korisnik): HR pipeline je uglavnom pouzdan i sam se oporavi za sekundu-dvije, ali
automatska tehnička pauza je okidala na svaki kratak prekid → stalne pauze, spor povratak,
"haos". Odluka: pauza je ISKLJUČIVO ručna.

### 1. Automatska pauza uklonjena u potpunosti
- OBRISANO: `Session/HrTechnicalPauseController.cs` i `Tests/EditMode/FableTechnicalPauseTests.cs`
  (backup: `_implementation_backup/pause_rework_2026-07-14/`).
- `Core/PauseController.cs` vraćen na JEDNU (ručnu) pauzu — nema više manual/technical uzroka.
- `Session/SessionCoordinator.cs`: uklonjeno cijelo tehničko-pauzno wiring (polja, hooks,
  TickTechnicalPause, reset-ovi, guard u PauseSession).
- `Session/ProductionSessionFlow.cs`: uklonjeni `HandleTechnicalFailure()` i `ReleaseTechnical`.
- `Core/StressTrainingConfig.cs`: uklonjen `hrStaleTechnicalAbortSeconds`.
- POSLJEDICA (namjerna): globalni tajmer, pressure, task i **baseline/recovery mjerenje se
  NIKAD ne zaustavljaju** zbog HR prekida. Prekid pulsa = samo "Signal izgubljen" na satu +
  metrika kvaliteta podataka (valid-sample ratio / dužina rupe). Time otpada i potreba za
  "waiting time" prije pauziranja baseline-a/recovery-ja — oni se više uopšte ne pauziraju.

### 2. Pauza = isključivo Y na LIJEVOM kontroleru (jedna jedina putanja)
- `Console/InputAdapters.cs`: `OVRInput.RawButton.Y` → `SemanticAction.PauseToggle`
  (bilo: `OVRInput.Button.Start`). Ovo je JEDINO mjesto odakle pauza može nastati na Questu.
- POPRAVLJEN SKRIVENI BUG: pauza se okidala kroz DVIJE putanje — `UiNavigationInput.PausePressed`
  → `UIManager.PauseRequested` → TogglePause, i `InputAdapters` → router → TaskRunner →
  TogglePause. U Editoru je `P` tako pozivao TogglePause DVA puta (pauza pa odmah countdown).
  Uklonjeni `PausePressed` (UiNavigationInput) i `PauseRequested` (UIManager) + pretplata u
  koordinatoru. Ostala je tačno jedna pretplata: `_taskRunner.PauseToggleRequested += TogglePause`.
- Dev tastatura `P` ostaje (samo `#if UNITY_EDITOR`), kroz istu jedinu putanju.

### 3. PAUSE dugme skinuto sa konzole
- `Console/ConsoleLayoutBuilder.cs`: uklonjeni `PauseId`, `pauseButton` layout polje, binding
  i kreiranje dugmeta. `RebuildProductionContents` briše svu djecu i gradi iznova → staro
  `btn_pause` nestaje i iz zatečene scene. Konzola sada: 5 task dugmadi + 9 Corsi = 14.
- Testovi ažurirani: bindings 15→14, dugmad 15→14, novi test
  `ProductionBuild_HasNoPauseButtonOnConsole`.

### Verifikacija
- Grep: nema nijedne mrtve reference; zagrade balansirane u svim izmijenjenim fajlovima.
- **Compile i EditMode testovi NISU pokrenuti** — Coplay MCP veza sa Unity Editorom je pala
  u ovoj sesiji. Korisnik treba da provjeri Console i pokrene EditMode Run All.

## 2026-07-14 — FINALNA INTEGRACIJA: spojena breathing/reference faza, novi pre-session redosljed, scheduler audit, indikatori nivoa

### Novi pre-session redosljed
HR gate → [STAI-6 samo prva sesija ciklusa] → **BreathingReferenceBaseline** → pre-session SSQ
→ tutorial (samo ako treba) → Ready → 9 blokova → recovery → post-SSQ → NASA-TLX
→ [STAI-6 samo posljednja sesija] → validity → adaptation → save → summary.

### Spojena faza (KLJUČNO)
- `ProductionFlowStage.HeartRateBaseline` + `CopingPreparation` → **`BreathingReferenceBaseline`** (vrijednost 5).
- `SessionHrPhase.NeutralBaseline` → **`BreathingReferenceBaseline`** (vrijednost 3; `CopingBreathing=4` legacy).
- Disanje i mjerenje dijele **isti interval** = `config.baseline.durationSeconds`.
  `BreathingPanel.BeginReference(...)` + `_flowDriven` → panel NIKAD ne pokazuje svoj Continue;
  fazu završava tok (`TickQuietMeasurement` → `CompleteBreathingReference` → SSQ).
- Tokom faze: ritam, napredak %, preostalo vrijeme, HR status, tačan BPM (i na satu).
- **METODOLOGIJA**: PROJECT-DEFINED BREATHING-ASSISTED REFERENCE — NIJE neutralni resting
  baseline. Zapisano u SCIENTIFIC_TRACEABILITY.md.
- Stale HR: **ne pauzira ništa**, nema poruke/countdowna; rupa → metrika kvaliteta;
  posljednji BPM se ne ponavlja; ako nema dovoljno uzoraka → postojeća validity logika.

### Scheduler audit (SCHEDULER_VERIFICATION_REPORT.md)
Cijeli lanac praćen; **10/10 provjera PASS, nijedan bug, algoritam NIJE mijenjan**.
Potvrđeno da odluka stvarno mijenja narednu sesiju:
`decision.newLevel → profile.current*Level → SaveProfile(disk) → sljedeći Generate()`.
Rule 1 (`P_TASK_INCREASED_HOLD`) sprečava istovremeni rast pressure-a i taska.

### Indikatori nivoa
- Participant tablet (već postojalo): Blok X/9 · Runda Y/3 · naziv · nivo N · globalno vrijeme. Ništa više.
- NOVO `ProductionSessionFlow.BuildPlanIndicators()` u summary-ju: TRENUTNA SESIJA
  (condition, pressure nivo, 3 taska + nivoi, seed/plan ID, budžet, default vs scheduler-generated)
  + NAREDNA SESIJA (directive → novi nivo po tasku i pressure, fired rules, izvorni sessionId,
  status Applied/Pending/Not generated/Rejected due to invalid data).

### Izmijenjeni fajlovi
`Session/ProductionSessionFlow.cs`, `Session/ActiveSessionContext.cs`, `UI/FlowPanels.cs`,
`Tests/EditMode/FablePhase86Tests.cs`. Novo: `Tests/EditMode/FableFinalIntegrationTests.cs`,
`FINAL_SESSION_FLOW_AUDIT.md`, `SCHEDULER_VERIFICATION_REPORT.md`,
`FULL_9_BLOCK_MANUAL_TEST_CHECKLIST.md`.

### Verifikacija
- Grep: nema mrtvih referenci; zagrade balansirane; API potpisi provjereni.
- **Unity compile: NOT RUN. EditMode Run All: NOT RUN.** (Coplay MCP veza sa Editorom je pala;
  Unity drži projekat pa ni batchmode nije moguć.) Korisnik pokreće Run All.
- Quest ručni tok: NOT RUN → FULL_9_BLOCK_MANUAL_TEST_CHECKLIST.md.

---

## 2026-07-15 — ART/UX PROLAZ (puzzle hodnik + aurora + UiTheme)

### Prioritet 1 — puzzle hodnik i pressure urušavanje
- FBX `Assets/Art/PuzzleCorridor/PuzzleCorridor.fbx` (kopiran). Binarni FBX parsiran:
  `Corridor_Modular_Puzzle_ROOT` → `Segment_S1..S6` + `Segment_Safe`; svaki
  Floor/Ceiling/Wall_L/Wall_R + CollapsePivot/FXAnchor/LightAnchor. Pokriva Z −5..+5 (10 m),
  ~3 m — identično postojećem hodniku; `Segment_Safe` obuhvata konzolu+igrača.
- NOVO: `Pressure/PuzzleCollapseData.cs` (čist C# deterministička sekvenca), 
  `Pressure/PuzzleSegmentController.cs`, `Pressure/PuzzleCorridorController.cs`.
- `PressureController` prima `AttachPuzzleCorridor()` — gasi proceduralne ploče kad je puzzle
  prisutan, fallback + Console upozorenje inače; vozi puzzle na 70/50/30/10/0; reset/pauza.
- `AppBootstrapper.FindOrBindPuzzleCorridor()` veže root po imenu.
- Safe segment: pod nije movable → učesnik nikad ne gubi tlo.

### Prioritet 2 — Safe Space aurora
- `SafeSpaceBuilder.TryBuildAuroraDome()` + `RuntimeVisualUtil.UnlitTextured()`. Inward sfera
  iz `Resources/SafeSpace/AuroraPanorama`, plavo-zeleno svjetlo. AVIF→PNG je ručni korak.
  Fallback na gradijentni dome (AuroraApplied=false).

### Prioritet 3 — UiTheme
- NOVO `UI/UiTheme.cs` centralni dizajn tokeni; `UiBuilder` ih čita. Puni per-panel sweep i
  tranzicije nisu završeni (UX_UI_POLISH_REPORT.md).

### Testovi: `FablePuzzleCorridorTests` (16), `FableSafeSpaceAndUiTests` (5).
### Dokumenti: PUZZLE_CORRIDOR_INTEGRATION_REPORT, PRESSURE_COLLAPSE_SEQUENCE,
  SAFE_SPACE_AURORA_SETUP, UX_UI_POLISH_REPORT, ART_UX_MANUAL_TEST_CHECKLIST.
### Compile/EditMode/Play/Quest: NOT RUN (korisnik; braces provjereni). FBX uvoz + AVIF
  konverzija su ručni Editor koraci.
