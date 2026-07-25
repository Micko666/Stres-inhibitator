# IMPLEMENTATION_PLAN.md — Adaptive VR Stress Training MVP

Datum: 2026-07-12
Grana: main
Izvor istine o početnom stanju: `VR_StressTraining/PROJECT_AUDIT_FOR_CHATGPT.md`

## 0. Sigurnost (URAĐENO PRIJE PLANA)

- `_implementation_backup/PRE_IMPLEMENTATION_GIT_STATUS.txt` — git status prije izmjena
- `_implementation_backup/PRE_IMPLEMENTATION_DIFF.patch` — puni diff postojećih necommitovanih izmjena
- `_implementation_backup/PRE_IMPLEMENTATION_SHA256.txt` — SHA-256 izmijenjenih fajlova
- `_implementation_backup/MainScene.unity.bak`, `URP_Balanced.asset.bak` — kopije fajlova
- Postojeće izmjene `MainScene.unity` i `URP_Balanced.asset` se NE diraju destruktivno.
- Nema `git reset/checkout/restore`, nema izmjena Unity verzije, paketa, XR providera, render pipeline-a,
  API nivoa, scripting backenda, build profila.

## 1. Ključne arhitekturne odluke

| Odluka | Obrazloženje |
|---|---|
| Novi kod u `StressTraining.*` namespace-ovima, asmdef `StressTraining` u `Assets/Scripts/` | Testabilnost (test asmdef može referencirati runtime asmdef); postojećih 10 skripti ulazi u isti assembly bez pomjeranja fajlova (GUID-ovi netaknuti) |
| Asmdef reference: `UnityEngine.UI`, `Unity.TextMeshPro`, `Oculus.VR`, `Oculus.Interaction`, `Unity.InputSystem` | Nazivi potvrđeni grep-om kroz `Library/PackageCache` |
| Persistencija: `JsonUtility` + ISO-8601 string datumi + JSONL streamovi | Nula novih paketa; bez Dictionary polja u trajnim modelima |
| UI: runtime-generisan World Space `UnityEngine.UI` (legacy Text + LegacyRuntime.ttf) | TMP essentials nijesu importovani u projekat (rizik nevidljivog teksta); legacy UI garantovano renderuje bez ijednog asseta |
| UI navigacija: OVRInput (thumbstick/A/B) + Input System keyboard fallback | Radi u Link Play Mode i standalone; ne zavisi od Meta ray/poke wiring-a kroz kod |
| Task input: konzolne kontrole (collider poke preko sfere na controller anchoru) + OVRInput dugmad kao redundantni put + keyboard (dev) | Fizička konzola je primarni input; redundantni put čini MVP demonstrabilnim i bez preciznog poke-a |
| Scena: jedna (`MainScene`), zone kao root objekti (`SafeSpaceRoot`, `CorridorRoot`…), editor installer (`Tools → Stress Training → Install Scene`) + runtime fallback | Bez destruktivnog rebuilda postojećeg sadržaja; installer je idempotentan; kod radi i ako installer nije pokrenut (fallback pronalazi objekte jednom pri boot-u) |
| Sve heuristike u centralnim config klasama sa markerom `PROJECT_HEURISTIC` | Zahtjev specifikacije |
| Stare skripte: `namespace StressTraining.Legacy` + deprecation header, fajlovi ostaju na mjestu | Scene GUID reference ostaju validne; nema dvije aktivne arhitekture (legacy nije u sceni osim RobotArmPoseTester) |
| `RobotArmPoseTester` ostaje netaknut u sceni; novi `RobotArmTabletPresenter` poštuje pravila iz CLAUDE.md (4 pivota, aditivno, mali uglovi) | CLAUDE.md pravila za ruku |

## 2. Faze i redosljed

1. **F1 Skeleton** — folderi, asmdef-ovi, enums, Core (AppState, AppStateMachine, ServiceRegistry, SessionClock, PauseController, AppErrorService)
2. **F2 Data + Persistence** — svi serializable modeli; AtomicFileWriter, ProfileRepository, SessionRepository, JSONL writeri, QuestionnaireRepository, SchemaMigrationService, BackupRecoveryService
3. **F3 Task engine** — čisti (ne-MonoBehaviour) generatori i runtime-i za n-back, go/no-go, flanker; TaskDifficultyConfig; seed servis
4. **F4 Session** — SessionPlan(Generator), ActiveSessionContext, SessionValidityEvaluator, UserProfileService, Baseline/Recovery akumulatori
5. **F5 HR** — HrPacketParser (novi + legacy format), IHeartRateSource, SimulatedHeartRateSource, NetworkHeartRateSource, HeartRateService, zone evaluator; refaktor `hr_dashboard_v2.py` + prenosivi launcher + config primjer
6. **F6 Adaptacija** — AdaptationConfig/Input/Decision/RuleSet/Scheduler/ExplanationBuilder
7. **F7 Upitnici** — SSQ/NASA-TLX/STAI-6 katalog, scoring, flow
8. **F8 Runtime sloj** — Console kontrole + router + layout, Tablet, UIManager + paneli, SafeSpaceBuilder, PressureController, Audio/VoiceCue sistem, RobotArmTabletPresenter, DeveloperPanel, SessionCoordinator, AppBootstrapper
9. **F9 Editor installer** — scene setup menu item
10. **F10 Testovi** — EditMode baterija; PlayMode minimalni
11. **F11 Verifikacija** — MCP Unity/Coplay: compile check, test run, screenshots (ako je Editor dostupan)
12. **F12 Dokumentacija** — 10 dokumenata + CLAUDE.md update + finalni izvještaj

## 3. Fajlovi koje kreiram (plan)

Kompletna lista po folderima je u ARCHITECTURE.md na kraju; sažetak:

- `Assets/Scripts/StressTraining.asmdef`, `Editor/StressTraining.Editor.asmdef`, `Tests/EditMode/*.asmdef`, `Tests/PlayMode/*.asmdef`
- `Core/`: AppState, AppStateMachine, ServiceRegistry, SessionClock, PauseController, AppErrorService, AppBootstrapper, ConfigService
- `Data/`: CoreEnums, UserProfileData, SessionSummaryData, TrialRecord, HeartRateSampleRecord, EventRecord, QuestionnaireData, AdaptationData, SessionPlanData
- `Persistence/`: PersistencePaths, AtomicFileWriter, JsonlWriter(+3 specijalizacije), ProfileRepository, SessionRepository, QuestionnaireRepository, SchemaMigrationService, BackupRecoveryService
- `Session/`: SessionPlanGenerator, ActiveSessionContext, SessionCoordinator, SessionValidityEvaluator, SceneZoneController, UserProfileService, BaselineAccumulator, RecoveryEvaluator, SafeSpaceBuilder
- `Tasks/`: TaskContracts, TaskDifficultyConfig, TaskSeedService, TaskRuntimeBase, NBackTask, GoNoGoTask, FlankerTask, TaskRunner
- `Console/`: ConsoleActions, ConsoleControlBase, ConsoleButtonControl, ConsoleToggleControl, ConsoleLeverControl, ConsoleKnobControl, ConsoleIndicatorLight, ConsoleControlRegistry, ConsoleInputRouter, ConsoleLayoutBuilder, ConsolePokeTip, OVRControllerInputAdapter, KeyboardInputAdapter
- `Tablet/`: TabletViewModel, TabletDisplayController, RobotArmTabletPresenter
- `HR/`: HeartRateContracts, HrPacketParser, SimulatedHeartRateSource, NetworkHeartRateSource, HeartRateService, HeartRateZoneEvaluator
- `Pressure/`: PressureConfig, PressureTimeline, PressureController
- `Questionnaires/`: QuestionnaireCatalog, QuestionnaireScoringService, QuestionnaireFlowController
- `Adaptation/`: AdaptationConfig, AdaptationModels, AdaptationRuleSet, AdaptationScheduler, AdaptationExplanationBuilder
- `Audio/`: VoiceCueIds, VoiceCueLibrary, VoiceCueManager, SubtitlePresenter, AmbientAudioController, InteractionAudioController, PressureAudioController
- `UI/`: UiBuilder, UiNavigationInput, UIManager, paneli (ProfileSelection, Questionnaire, Baseline, Coping, Tutorial, Ready, PauseMenu, Recovery, AdaptationReview, Summary, Error), HrZoneWidget, CountdownOverlay
- `Debugging/`: DeveloperPanel, DevFixtures
- `Editor/`: StressTrainingSceneInstaller
- `Tests/EditMode/`: ~14 test fajlova; `Tests/PlayMode/`: smoke testovi
- Root: refaktorisan `hr_dashboard_v2.py`, novi `start.bat` (prenosiv), `hr_bridge_config.example.json`
- Docs: `docs/*` + root izvještaji

## 4. Fajlovi koje mijenjam

| Fajl | Izmjena |
|---|---|
| `Assets/Scripts/{GameManager,PuzzleManager,PuzzleButton,StressRoomController,DebriefingManager}.cs` | `namespace StressTraining.Legacy` + deprecation komentar (bez brisanja) |
| `Assets/Scripts/HR/{HRReceiver,HRDisplay}.cs` | isto (zamjenjuju ih NetworkHeartRateSource/HrZoneWidget) |
| `hr_dashboard_v2.py` | prenosive putanje, CLI argumenti, config, reconnect, sequence, UTC timestamp, novi packet format (+legacy polje) |
| `start.bat` | prenosiv launcher (postojeći je dokazano pokvaren — hardkodovane nepostojeće putanje) |
| `Assets/Scenes/MainScene.unity` | SAMO preko editor installera (dodavanje rootova/reparent, bez brisanja postojećeg) — ako je Unity dostupan |
| `CLAUDE.md` | na kraju — stvarno stanje |

## 5. Rizici

1. **Kompajliranje bez Unity poziva** — asmdef reference/API greške se vide tek u Editoru. Mitigacija: MCP/Coplay compile check na kraju + konzervativan API izbor.
2. **TMP essentials nedostaju** — zato legacy UI Text za runtime UI.
3. **Scene izmjene dok je Unity otvoren** — installer je menu item; pokrećem ga preko MCP-a samo kad je Editor Online; u suprotnom dokumentujem korak za studenta.
4. **Poke preciznost na konzoli** — redundantni OVRInput put čini task flow uvijek prohodnim.
5. **Upitnici — validirani tekstovi** — koristim standardne engleske stavke (Kennedy 1993 SSQ; Hart & Staveland NASA-TLX; Marteau & Bekker STAI-6) uz oznaku izvora; BCS prevodi označeni `NEEDS_VALIDATED_ITEM_TEXT`.
6. **Render scale 1.6 (necommitovano)** — ne diram; upozorenje u izvještaju (napomena: radna kopija na disku trenutno pokazuje `m_RenderScale: 1` — vidjeti diff patch u backup folderu).
7. **Obim** — ako ponestane prostora, prioritet: kompletan tok sa mock HR + testovi + dokumentacija; sekundarno: poliranje vizuala.

## 6. Kriterijum završetka

MVP kriterijumi 1–34 iz naloga; svaki dobija status u IMPLEMENTATION_MASTER_REPORT.md (DONE / PARTIAL / BLOCKED sa razlogom).
