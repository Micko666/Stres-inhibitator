# Codex Vertical Slice Report

Datum: 2026-07-12  
Unity verzija: `6000.3.16f1`  
Quest status: **QUEST_RUNTIME_NOT_VERIFIED**

## Outcome

Implementiran je prvi kompletan code-level vertical slice: jedan bootstrap entry point konstruše profile/persistence, simulated HR, UI, SafeSpace, corridor zone controller, tablet, console/controller/keyboard input, jedan seeded 1-back task runner i session coordinator. Tok podržava profil, kratki baseline, corridor, N-back, logging, pause, 3-2-1 continue, end reason, session save, SafeSpace return i summary.

MainScene još nema entry point jer idempotentni Unity menu installer nije izvršen: Unity proces je ostao živ bez vidljivog prozora, a Unity-side MCP portovi 8090/8091 su zatvoreni poslije domain reload-a. Scena nije ručno YAML editovana. Kod installera je kompajliran i spreman za `Stress Training → Install Vertical Slice` nakon što se editor/MCP ponovo pokrene.

## Git Status Before

Početni tracked diff je ostao: `MainScene.unity`, `URP_Balanced.asset`, `hr_dashboard_v2.py`, `start.bat`. Svi Claude folderi i dokumenti bili su untracked. Nije korišten reset/restore/checkout i nijedna prethodna izmjena nije odbačena.

## Files Created

Runtime:

- `Assets/Scripts/Core/AppBootstrapper.cs`
- `Assets/Scripts/Session/SessionCoordinator.cs`
- `Assets/Scripts/Tasks/TaskRunner.cs`
- `Assets/Scripts/UI/ProgressPanel.cs`
- `Assets/Scripts/UI/PauseMenuPanel.cs`
- `Assets/Scripts/Editor/StressTrainingSceneInstaller.cs`

EditMode tests:

- `AppStateMachineTests.cs`
- `SessionClockTests.cs`
- `UsernameValidatorTests.cs`
- `ProfileRepositoryTests.cs`
- `AtomicFileWriterTests.cs`
- `NBackTaskTests.cs`
- `HeartRateServiceTests.cs`

PlayMode:

- `Assets/Scripts/Tests/PlayMode/VerticalSliceSmokeTests.cs`

Dokumentacija:

- `CODEX_VERTICAL_SLICE_PLAN.md`
- `CODEX_VERTICAL_SLICE_REPORT.md`
- `SCENE_INSTALLATION_REPORT.md`
- `CODEX_AUDIT_HANDOFF.md`

Unity je generisao `.meta` fajlove za importovane izvore; PlayMode smoke meta je dodat deterministički jer editor više nije radio refresh.

## Files Modified

- `Core/AppStateMachine.cs`: dozvoljene skraćene vertical-slice tranzicije.
- `Core/StressTrainingConfig.cs`: centralni demo baseline/trial/seed parametri.
- `Core/PauseController.cs`: reset između sesija.
- `HR/HeartRateService.cs`: jedna stale vremenska osnova, session-relative log timestamp i aktivni session aggregate.
- `HR/SimulatedHeartRateSource.cs`: deterministički reset clock/queue/sequence pri startu.
- `Persistence/AtomicFileWriter.cs`: cross-platform exception fallback bez delete-before-replacement gap-a; `.prev` ostaje.
- `Tasks/TaskContracts.cs` i `TaskRuntimeBase.cs`: `Aborted`, `Abort`, `Reset`, abort event, bez lažnog block success-a.
- `Session/ActiveSessionContext.cs`: injektovani clock/pause ownership.
- `UI/FlowPanels.cs`: postojeći zbirni Progress/Pause tipovi označeni deferred; produkcijski tipovi su u pravilno imenovanim fajlovima.

Nijesu mijenjani package fajlovi, URP asset, Python bridge ili network HR transport. `MainScene.unity` nije mijenjan u ovoj iteraciji.

## Audit Risks Fixed

### HR timebase

Freshness više ne poredi source-relative/process uptime sa service-relative vremenom. Pri prihvatu sample-a service bilježi vlastiti monotonic clock; JSONL dobija vrijeme relativno na attach sesije. UTC/source timestamp ostaju odvojena polja. Paused samples su tagovani i isključeni iz aktivnog session/task prosjeka.

### Atomic persistence

`PlatformNotSupportedException`, `UnauthorizedAccessException` i `IOException` vode u fallback koji čuva `.prev` i ne briše jedinu validnu kopiju prije replacement-a. Profil i session putanje ostaju pod `Application.persistentDataPath/StressTrainingData`.

### Task lifecycle

`Abort()` ne emituje `BlockFinished` niti sintetički rezultat. `Reset()` vraća runtime u čist Idle. Pause usred otvorenog trial-a odbacuje djelimični pokušaj i ponavlja isti trial poslije countdown-a; input latch se resetuje i input router je blokiran tokom pause-a.

## AppBootstrapper Ownership

`AppBootstrapper` je jedini MonoBehaviour entry point. On posjeduje composition/disposal lifecycle i eksplicitno instalira registry: errors/config/paths, repositories, profile service, state machine, jedan SessionClock/PauseController, simulated source/HR service, zones/UI/tablet/console, TaskRunner i SessionCoordinator. `OnDestroy` odjavljuje/dispose-uje coordinator/runner, zaustavlja HR i resetuje registry. Duplikat se odbija privatnim guard-om; nema javnog globalnog singleton API-ja.

## SessionCoordinator Flow

`Boot → ProfileSelection → Preparation → Baseline → Ready → TransitionToCorridor → ActiveSession ↔ Paused → SessionEnding → PostSessionSummary → ProfileSelection`.

Coordinator učitava/kreira/bira profil, kreira ID/context i početni session.json, upravlja baseline accumulator-om, tranzicijama zona, pause/countdown/end, summary-jem, session/profile save-om i writer lifecycle-om. Ne računa N-back i ne piše raw JSON direktno.

## TaskRunner Flow

TaskRunner pravi seeded level-1 1-back demo, sluša samo `Match`/`NoMatch`, prikazuje stimulus na tabletu, bilježi trial rezultat, upisuje `trials.jsonl`, agregira task HR i emituje block completion. Podržava Pause/Resume/Abort/Dispose i odjavljuje svaki event handler.

## Persistence Layout

Runtime root:

```text
Application.persistentDataPath/StressTrainingData/
  profiles/index.json
  profiles/<userId>.profile.json
  profiles/backups/
  sessions/<userId>/<sessionId>/session.json
  sessions/<userId>/<sessionId>/trials.jsonl
  sessions/<userId>/<sessionId>/hr.jsonl
  sessions/<userId>/<sessionId>/events.jsonl
  config/config.json
```

Početni `session.json` se piše prije baseline-a radi abandoned recovery-ja; završni summary i profil se pišu i za completed i za user-terminated tok. Prekinuta sesija ne povećava completed cycle index.

## Input Mapping

- Keyboard: `M` Match, `N` NoMatch, `P` Pause; arrows/Enter/Escape za UI.
- Quest OVR controller: `A` Match, `B` NoMatch, right index trigger Go (ne koristi se u slice-u), left menu/Start Pause.
- Runtime console: programatski Match/NoMatch kontrole mapirane kroz `ConsoleInputRouter`.
- Fizički poke nije proglašen funkcionalnim niti je obavezan; controller/keyboard su pouzdani slice input.

## Scene Installation

1. Otvoriti projekat isključivo u Unity `6000.3.16f1`.
2. Otvoriti `Assets/Scenes/MainScene.unity`.
3. Sačekati clean compile.
4. Izabrati `Stress Training → Install Vertical Slice`.
5. Provjeriti generisani `SCENE_INSTALLATION_REPORT.md`.
6. Ponoviti menu komandu jednom radi idempotency provjere; drugi run ne smije dodati duplikate.

Installer prije promjene kopira scenu u repo `_implementation_backup`, dodaje `AppRoot`, `SystemsRoot`, `RuntimeUIRoot`, `DebugRoot`, tačno jedan bootstrapper, `CorridorSpawn`, XR rig/corridor/root reference i save-uje samo ako postoji promjena. Ne briše/reparentuje corridor, console ili robot arm i ne dira URP.

## How to Run Demo

1. Nakon instalacije pritisnuti Play.
2. U SafeSpace profilu izabrati postojeći ili `+ Novi profil`.
3. Pokrenuti demo; 5 s simulated baseline prelazi na Ready.
4. Potvrditi ulazak u corridor.
5. Odgovarati `M/N` ili `A/B` za svaki 1-back stimulus.
6. `P`/Start otvara pause. Continue radi 3-2-1; End bira razlog i čuva partial podatke.
7. Po block completion-u aplikacija čuva session/profile, vraća SafeSpace i prikazuje summary.
8. Stop/Play i ponovno biranje profila potvrđuje reload istorije.

## Compile Status

**PASS** za runtime, editor installer, EditMode test assembly i kombinovani PlayMode smoke source koristeći Unity `6000.3.16f1` `NetCoreRuntime/dotnet.exe` + `DotNetSdkRoslyn/csc.dll` i Unity-generated Bee response fajlove. Nema C# grešaka u finalnoj provjeri.

Otvoreni editor nije emitovao finalne `Library/ScriptAssemblies` kopije jer je Unity-side MCP server prestao slušati nakon domain reload-a; zato je compile dokaz Unity Roslyn/Bee, ne normalni završni Editor refresh artefakt.

## EditMode Test Status

- Stvarni test source: 14 test metoda u 7 fajlova.
- Test assembly compile: **PASS**.
- Izolovani Unity Mono reflection run: **12 PASS**, **2 NOT EXECUTABLE IN HARNESS**.
- Dva persistence testa nijesu izvršiva van Unity player/editor procesa zato što `JsonUtility` zahtijeva native injected call; to nije assertions failure.
- Zvanični Unity Test Runner run: **NOT RUN — MCP/Editor unavailable**.

Testovi pokrivaju valid/invalid state, active/paused clock, username, stable userId/recovery, atomic `.prev`, deterministic N-back/expected/warmup/miss/abort i HR stale/paused sample.

## PlayMode Test Status

`VerticalSliceSmokeTests.cs` postoji i kompajlira. Izolovano pravi test rig/corridor/roots i temp persistence path, zatim provjerava bootstrap, SafeSpace, profil, baseline, corridor, N-back input, pause clock, completion, SafeSpace return i saved summary. Zvanični PlayMode run je **NOT RUN — MCP/Editor unavailable**.

## Play Mode and Scene Status

- MainScene entry point: **NOT INSTALLED YET**.
- Scene YAML: nije ručno mijenjan.
- Scene installer: **COMPILE PASS, READY TO RUN**.
- Editor Play Mode: **NOT VERIFIED**.
- Quest: **QUEST_RUNTIME_NOT_VERIFIED**.

## Known Issues / Remaining Blockers

1. **BLOCKER — scene installer nije izvršen**, pa stvarni MainScene još nema `AppRoot/AppBootstrapper`.
2. **VERIFICATION BLOCKER — Unity-side MCP/editor nije dostupan**, pa official EditMode/PlayMode run i vizuelni Play Mode tok nijesu izvršeni.

Non-blocking: runtime SafeSpace/UI/tablet/console su prototip primitive/uGUI vizuali; physical poke, robot presenter i Quest positioning nijesu verifikovani; finalni questionnaire/scheduler/pressure/audio/network HR ostaju van scope-a.

## Explicitly Not Implemented

Go/No-Go, Flanker, pressure/collapse, pravi Xiaomi/ADB/network HR tok, SSQ, NASA-TLX, STAI-6, scheduler UI/final flow, audio/voice/subtitles, final beach art i obavezni hand tracking nijesu dodati u vertical slice.

## Final Git Status

```text
 M VR_StressTraining/Assets/Scenes/MainScene.unity
 M VR_StressTraining/Assets/Settings/URP_Balanced.asset
 M hr_dashboard_v2.py
 M start.bat
?? IMPLEMENTATION_PLAN.md
?? Prva_iteracija_diplomskog_rada_Mihailo_Djurovic.docx
?? VR_StressTraining/.codex/
?? VR_StressTraining/Assets/Scripts/Adaptation.meta
?? VR_StressTraining/Assets/Scripts/Adaptation/
?? VR_StressTraining/Assets/Scripts/Console.meta
?? VR_StressTraining/Assets/Scripts/Console/
?? VR_StressTraining/Assets/Scripts/Core.meta
?? VR_StressTraining/Assets/Scripts/Core/
?? VR_StressTraining/Assets/Scripts/Data.meta
?? VR_StressTraining/Assets/Scripts/Data/
?? VR_StressTraining/Assets/Scripts/Editor/StressTraining.Editor.asmdef
?? VR_StressTraining/Assets/Scripts/Editor/StressTraining.Editor.asmdef.meta
?? VR_StressTraining/Assets/Scripts/Editor/StressTrainingSceneInstaller.cs
?? VR_StressTraining/Assets/Scripts/Editor/StressTrainingSceneInstaller.cs.meta
?? VR_StressTraining/Assets/Scripts/HR/HeartRateContracts.cs
?? VR_StressTraining/Assets/Scripts/HR/HeartRateContracts.cs.meta
?? VR_StressTraining/Assets/Scripts/HR/HeartRateService.cs
?? VR_StressTraining/Assets/Scripts/HR/HeartRateService.cs.meta
?? VR_StressTraining/Assets/Scripts/HR/HeartRateZoneEvaluator.cs
?? VR_StressTraining/Assets/Scripts/HR/HeartRateZoneEvaluator.cs.meta
?? VR_StressTraining/Assets/Scripts/HR/HrPacketParser.cs
?? VR_StressTraining/Assets/Scripts/HR/HrPacketParser.cs.meta
?? VR_StressTraining/Assets/Scripts/HR/NetworkHeartRateSource.cs
?? VR_StressTraining/Assets/Scripts/HR/NetworkHeartRateSource.cs.meta
?? VR_StressTraining/Assets/Scripts/HR/SimulatedHeartRateSource.cs
?? VR_StressTraining/Assets/Scripts/HR/SimulatedHeartRateSource.cs.meta
?? VR_StressTraining/Assets/Scripts/Persistence.meta
?? VR_StressTraining/Assets/Scripts/Persistence/
?? VR_StressTraining/Assets/Scripts/Questionnaires.meta
?? VR_StressTraining/Assets/Scripts/Questionnaires/
?? VR_StressTraining/Assets/Scripts/Session.meta
?? VR_StressTraining/Assets/Scripts/Session/
?? VR_StressTraining/Assets/Scripts/StressTraining.asmdef
?? VR_StressTraining/Assets/Scripts/StressTraining.asmdef.meta
?? VR_StressTraining/Assets/Scripts/Tablet.meta
?? VR_StressTraining/Assets/Scripts/Tablet/
?? VR_StressTraining/Assets/Scripts/Tasks.meta
?? VR_StressTraining/Assets/Scripts/Tasks/
?? VR_StressTraining/Assets/Scripts/Tests.meta
?? VR_StressTraining/Assets/Scripts/Tests/
?? VR_StressTraining/Assets/Scripts/UI.meta
?? VR_StressTraining/Assets/Scripts/UI/
?? VR_StressTraining/CODEX_AUDIT_HANDOFF.md
?? VR_StressTraining/CODEX_PROGRESS_AUDIT.md
?? VR_StressTraining/CODEX_VERTICAL_SLICE_PLAN.md
?? VR_StressTraining/CODEX_VERTICAL_SLICE_REPORT.md
?? VR_StressTraining/PROJECT_AUDIT_FOR_CHATGPT.md
?? VR_StressTraining/SCENE_INSTALLATION_REPORT.md
?? _implementation_backup/
?? hr_bridge_config.example.json
```

Postojeće pre-iteration izmjene su sačuvane. Hash `MainScene.unity` je i dalje identičan `MainScene.unity.bak`; nova scene izmjena nije nastala jer installer nije izvršen.
