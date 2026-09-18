# TECHNICAL TRUTH MAP — Adaptive Stress Corridor VR (v4)

**Osnova:** statička analiza repozitorijuma, HEAD `v1.0-thesis` + necommitovane izmjene u `android_companion/HrRelay/`, `hr_dashboard_v2.py` i `Assets/Scripts/Adaptation/`.
**v4 dodatno:** izmijenjen tok dobrovoljnog prekida sesije, ispravljena semantika ciklusa i popravljen zapis ciklusa; dodani glasovni snimci i ambijentalni zvuk. Flanker je dobio vizuelnu progresiju kroz nivoe. **Cio EditMode suite prolazi** (Unity Test Runner, 2026-09-12; 345 testova prije Flanker izmjene, plus 29 novih).
**v3:** scheduler izmjene verifikovane sa 21 testom; konfiguracija i format podataka potvrđeni na Quest 3 uređaju (v. §14).
**Oznake:** `CONFIRMED` = potvrđeno kodom · `PROJECT_HEURISTIC` = projektna radna vrijednost, tako označena u samom kodu · `STALE` = dokumentacija protivrječi implementaciji · `NOT CONFIRMED` = ne može se utvrditi statičkom analizom.
**Pravilo:** gdje se dokumentacija i kod ne slažu, kod pobjeđuje; konflikt je zabilježen u §12.

> **Terminološka konvencija koja važi kroz cijeli dokument**
> **Pressure LEVEL (1–3)** = *međusesijska* konfiguracija intenziteta. Bira ga scheduler, čuva se u profilu (`currentPressureLevel`), mijenja se najviše za ±1 između sesija.
> **Pressure STAGE** (`Stable / Early / Mid / Late / Critical / Expired`) = *unutarsesijska* vremenska faza. Određuje je preostala frakcija globalnog vremena, mijenja se kontinuirano tokom aktivnog dijela sesije.
> Ta dva pojma se **nikad ne smiju miješati**: level određuje *koliko jako*, stage određuje *kada i dokle*.

---

## 1. SISTEM U JEDNOM POGLEDU

| # | Činjenica | Status |
|---|---|---|
| 1 | Meta Quest 3 standalone, Unity 6000.3.16f1, URP 17.0.3, OpenXR 1.14.0, Meta XR 201.0.0 | CONFIRMED — `ProjectVersion.txt`, `Packages/manifest.json` |
| 2 | Jedna scena u build listi: `Assets/Scenes/MainScene.unity` | CONFIRMED — `ProjectSettings/EditorBuildSettings.asset` |
| 3 | Composition root: `AppBootstrapper` (MonoBehaviour, singleton) | CONFIRMED — `Core/AppBootstrapper.cs:17` |
| 4 | Vlasnik app state machine-a, pauze i tick-a: `SessionCoordinator` | CONFIRMED — `Session/SessionCoordinator.cs` |
| 5 | `ProductionSessionFlow` sekvencira produkcijski tok preko eksplicitne stage-machine strukture (`SetStage(...)` + `Tick`). Enum `ProductionFlowStage` ima **20 vrijednosti (0–19)**; **17 se stvarno postavlja** u toku, `None` je početno/reset stanje, a `ScheduleEligibility` i `CopingPreparation` nisu dio aktivnog toka (v. §3.2) | CONFIRMED — `Session/ProductionSessionFlow.cs:16-44`; popis `SetStage` poziva |
| 6 | Četiri kognitivna zadatka: N-back, Go/No-Go, Flanker, Corsi-inspired | CONFIRMED — `Data/CoreEnums.cs:13-22` |
| 7 | Po sesiji se bira **3 od 4** zadatka, seeded i deterministički | CONFIRMED — `Session/SeededConstrainedTaskSelector.cs` |
| 8 | **9 blokova = 3 runde × 3 zadatka**, tvrdo zaključano (baca izuzetak) | CONFIRMED — `Session/ProductionSessionPlanGenerator.cs:34-36,101-103` |
| 9 | Controlled pressure ima **dvije nezavisne ose**: pressure **level 1–3** (međusesijski intenzitet) i pressure **stage** (unutarsesijska progresija) | CONFIRMED — `Pressure/PressureTimeline.cs:7-15,112-122` |
| 10 | Pressure **stage** se mijenja po **preostaloj frakciji** globalnog vremena: 0.70 / 0.50 / 0.30 / 0.10 / 0.00 | CONFIRMED — `Pressure/PressureTimeline.cs:31-35` |
| 11 | Pressure **level** ne mijenja trajanje sesije (svi `DurationMultiplier` = 1.00) | CONFIRMED — `PressureTimeline.cs:42-44` |
| 12 | HR ulazi isključivo kao **cjelobrojni BPM** preko UDP-a; nema PPG/IBI/RR/HRV | CONFIRMED — `HR/HeartRateContracts.cs:13` |
| 13 | HR zone su **relativne prema referentnoj vrijednosti**, nikad apsolutni BPM prag | CONFIRMED — `HR/HeartRateZoneEvaluator.cs:25-38` |
| 14 | Referentno mjerenje je **breathing-assisted**, 300 s, ne resting baseline | CONFIRMED — `Core/StressTrainingConfig.cs:44`, `ProductionSessionFlow.cs:23-27` |
| 15 | Tri instrumenta: SSQ (2×), Raw NASA-TLX (1×), STAI-6 (uslovno, početak/kraj ciklusa) | CONFIRMED — `Questionnaires/QuestionnaireCatalog.cs` |
| 16 | Scheduler je rule-based, radi **samo između sesija**, nikad unutar bloka | CONFIRMED — `Adaptation/AdaptationScheduler.cs:9-10` |
| 17 | Task difficulty i pressure level su **odvojene dimenzije**; ne povećavaju se istovremeno | CONFIRMED — `Adaptation/AdaptationRuleSet.cs:149-154` |
| 18 | Persistence: `session.json` + tri JSONL toka (`trials`, `hr`, `events`) po sesiji | CONFIRMED — `Persistence/PersistencePaths.cs:36-48` |
| 19 | Nevalidne i prekinute sesije se **nikad ne brišu** | CONFIRMED — `Persistence/SessionRepository.cs:75-93`, `Session/SessionValidityEvaluator.cs:23` |
| 20 | **Ne postoji export podataka iz aplikacije** — podaci se dobijaju samo pristupom fajl sistemu Questa | CONFIRMED (odsustvo koda) |

---

## 2. RUNTIME ARHITEKTURA

```
AppBootstrapper                                   Core/AppBootstrapper.cs:17
│  Start() → InitializeRuntime()                  ln 70-260
│
├─ INFRASTRUKTURA (ln 99-111)
│   PersistencePaths ─ ConfigService ─ SchemaMigrationService
│   BackupRecoveryService ─ ProfileRepository ─ SessionRepository
│   UserProfileService ─ AppStateMachine ─ SessionClock ─ PauseController
│
├─ HR SLOJ (ln 113-126)
│   HeartRateService ←── SimulatedHeartRateSource
│                    ←── NetworkHeartRateSource   (UDP :5005)
│                    ←── NativeAdbHeartRateSource (placeholder)
│   SetMode(Network)
│
├─ PROSTOR / UI (ln 128-176)
│   SafeSpaceBuilder.Build()  → runtime, NIJE u sceni
│   SceneZoneController ─ UIManager ─ ConsoleLayoutBuilder
│   ConsoleInputRouter ─ RobotArmTabletPresenter
│
├─ ProductionSessionFlow           kreiran ln 207
├─ PressureController              kreiran ln 224
├─ PlayerFallController            kreiran ln 236
├─ ThumbstickLocomotionDriver      kreiran ln 249
│
└─ SessionCoordinator              kreiran ln 251  (POSLIJE flow-a)
    └─ fallController.PlayerLost += _coordinator.HandlePlayerLost   ln 259
```

### 2.1 Komponente

| Komponenta | Odgovornost | Kreira je | Posjeduje stanje | Direktna komunikacija |
|---|---|---|---|---|
| `AppBootstrapper` | jedina tačka ulaza; gradi sve i registruje u `ServiceRegistry` | Unity (`Start()`) | reference na servise | sve |
| `SessionCoordinator` | app state machine, pauza/nastavak, tick, profil UI, demo tok | `AppBootstrapper:251` | `AppState`, aktivni panel, demo summary | `ProductionSessionFlow`, `UserProfileService`, `UIManager`, `HeartRateService` |
| `ProductionSessionFlow` | sekvenciranje produkcijskih faza; vlasnik writera i `Summary` | `AppBootstrapper:207` | `Stage`, `Context`, `Summary`, `_baselineSummary`, 3 writera | `TaskRunner`, `PressureController`, `HeartRateService`, `QuestionnaireFlowController`, `AdaptationScheduler`, `SessionRepository` |
| `TaskRunner` | izvršavanje jednog bloka; trial loop, scoring | `AppBootstrapper` | aktivni blok, trial index | `ProductionSessionFlow` (callback `OnBlockCompleted`), `TrialLogWriter` |
| `PressureController` | pressure timeline, stage tranzicije, događaji, kolaps koridora, finale | `AppBootstrapper:224` | `CurrentStage`, `CurrentIntensity`, `_timeline`, `_shakePhase`, `_eventPulse` | `PuzzleCorridorController` ili `CorridorCollapseController`, `PressureAudioController`, `PressureTransientFxController`, `GameOverController` |
| `HeartRateService` | prijem/validacija/zone/agregacija HR uzoraka | `AppBootstrapper:114` | `CurrentBpm`, `CurrentZone`, `SessionPeakBpm`, agregati | `IHeartRateSource` ×3, `HeartRateLogWriter`, `HeartRateZoneEvaluator` |
| `QuestionnaireFlowController` | prikaz i prikupljanje odgovora | `ProductionSessionFlow` | trenutni upitnik, odgovori | `QuestionnaireCatalog`, `QuestionnaireScoringService` |
| `AdaptationScheduler` | rule-based odluka između sesija | `ProductionSessionFlow:1379` | bez trajnog stanja (stateless) | `AdaptationRuleSet`, `AdaptationExplanationBuilder` |
| `UserProfileService` | životni ciklus profila i ciklusa; primjena odluke | `AppBootstrapper:108` | `ActiveProfile` | `ProfileRepository` |
| `PlayerFallController` | posljedica gubitka (fade / pad / devirtualizacija) | `AppBootstrapper:236` | `_falling`, `_phase` | `SessionCoordinator.HandlePlayerLost` |

### 2.2 Jedini servisni interfejs

`IHeartRateSource` — `HR/HeartRateContracts.cs:30-38`: `SourceType`, `IsRunning`, `StartSource()`, `StopSource()`, `DrainSamples(List<RawHrSample>)`.

`ITaskScheduler` — **NOT CONFIRMED / ne postoji** (grep: nema `interface ITaskScheduler`).

---

## 3. KANONSKI TOK SESIJE

Dokaz redosljeda — `ProductionSessionFlow.cs:600-602` (komentar u kodu):
> `HR connect → [STAI-6 if first session of cycle] → BreathingReferenceBaseline → pre-session SSQ → tutorial (only when needed) → Ready.`

SSQ prije sesije dolazi **poslije** referentnog mjerenja, ne prije (`ProductionSessionFlow.cs:614`, `:871`).

### 3.1 Stvarno dostižne faze

Tabela sadrži samo faze koje se stvarno postavljaju u toku (`SetStage(...)`), plus izbor profila koji je u nadležnosti `SessionCoordinator`-a.

| # | Faza (`ProductionFlowStage`) | Obavezna? | Uslov | Proizvodi | Sljedeći korak |
|---|---|---|---|---|---|
| 1 | *(izbor profila — `SessionCoordinator`)* | obavezna | — | `ActiveProfile` | provjera termina |
| 2 | *(provjera termina — `SessionCoordinator.cs:227-249`)* | obavezna | `EarlyWithWarning` je upozorenje, ne blokada | `scheduleOverride`, `scheduleOverrideReason` | HR gate |
| 3 | `BaselinePreparation` (**HR gate**) | obavezna | petlja dok `GetProductionReadiness().IsReady == false` | readiness status | STAI ili referenca |
| 4 | `PreCycleStai` | **uslovna** | preskače ako `!ShouldAdministerCycleStartStai()` | `staiScoreCycleStart` | referenca |
| 5 | `BreathingReferenceBaseline` | obavezna | — | `BaselineSummaryData`, `schedulerEligible`, zone baseline | pre-SSQ **ili retry** |
| 6 | `PreSessionSSQ` | obavezna | — | `SsqPreTotal` | tutorial |
| 7 | `TaskTutorialDecision` | **uslovna** | preskače za zadatke/nivoe koji su već viđeni | `tutorialsSeen` | Ready |
| 8 | `Ready` → `EnterCorridor()` | obavezna | — | teleport u koridor, start globalnog sata, **armiranje pressure-a** | blok 1 |
| 9 | `BetweenBlocks` / `ActiveBlock` × **9** | obavezna | — | `TrialRecord` ×N, `TaskSessionSummaryData` | sljedeći blok / kraj |
| 10 | `SessionEnding` | obavezna | — | flush writera, `endedAtUtcIso`, tajming | recovery |
| 11 | `Recovery` | **uslovna (v4)** | izvršava se za uredan završetak i `TimeExpired`; **preskače se pri dobrovoljnom prekidu** — v. §3.3 | `RecoverySummaryData` | post-SSQ |
| 12 | `PostSessionSSQ` | **uslovna (v4)** | preskače se pri dobrovoljnom prekidu | `SsqPostTotal` | NASA-TLX |
| 13 | `NasaTlx` | **uslovna (v4)** | preskače se pri dobrovoljnom prekidu | `tlxTotal` + 6 dimenzija | STAI ili validity |
| 14 | `PostCycleStai` | **uslovna** | preskače ako `!ShouldAdministerCycleEndStai()`, i pri dobrovoljnom prekidu (v4) | `staiScoreCycleEnd` | validity |
| 15 | `Validity` | obavezna | — | `ValidityStatus`, `validityNotes` | adaptation |
| 16 | `Adaptation` | obavezna (pravila uslovna) | pravila se ne izvršavaju ako `!schedulerEligible` | `AdaptationDecisionData` ili `null` | save |
| 17 | `Save` | obavezna | — | profil ažuriran, nivoi primijenjeni | summary |
| 18 | `Summary` | obavezna | — | prikaz | kraj |

### 3.2 Enum vrijednosti koje nisu dio aktivnog toka

`ProductionFlowStage` ima **20 vrijednosti (0–19)**. `SetStage(...)` se poziva za **17** njih. Preostale tri:

| Vrijednost | Status | Dokaz |
|---|---|---|
| `None = 0` | početno / reset stanje, nije faza toka | `ProductionSessionFlow.cs:93` (inicijalizacija), `:1840` (`Stage = ProductionFlowStage.None` pri cleanup-u) |
| `ScheduleEligibility = 1` | **NOT CONFIRMED kao dostižna** — nijedan `SetStage` poziv; provjera termina se izvodi u `SessionCoordinator`-u prije ulaska u flow | grep: nijedna referenca izvan deklaracije enuma |
| `CopingPreparation = 6` | **legacy, nedostižna** — kod sam navodi: *„Legacy: breathing was a separate stage before 2026-07-14. **Never entered now**"* | `ProductionSessionFlow.cs:29-30`; jedina preostala referenca je mapiranje `HrPhaseForStage` na ln 1808, označeno komentarom `// legacy` |

Nijedna druga vrijednost nije nedostižna — provjereno poređenjem enuma i svih `SetStage` poziva.

### 3.3 Tri načina zatvaranja sesije (v4)

Odluka je izdvojena u čistu funkciju `SessionEndPolicy.PathFor(preCorridor, voluntaryAbort)` — `ProductionSessionFlow` traži punu Unity scenu pa se ne može konstruisati u EditMode testu, a **sama odluka** može i testirana je.

| Put | Kada | Šta se izvršava |
|---|---|---|
| `RecoveryThenQuestionnaires` | uredan završetak **i** `TimeExpired` | Recovery (90 s) → post-SSQ → NASA-TLX → [PostCycle STAI] → validity → adaptation → save |
| `QuestionnairesOnly` | prekid **prije** ulaska u koridor | bez Recovery-ja; upitnici → validity → … |
| **`StraightToValidity`** *(v4)* | **dobrovoljni prekid iz aktivnog dijela** | **bez Recovery-ja, bez ijednog post-upitnika** → validity → adaptation → save → summary |

**Zašto prekid preskače mjerenja.** Sesija postaje neupotrebljiva za adaptaciju u trenutku prekida (`SessionValidityEvaluator` pravila 4 i 7), pa bi se Recovery i tri upitnika prikupljali pod nelagodom i onda odbacili. Učesnik kome je muka ne ostaje u headsetu još 90 s plus tri forme. `TimeExpired` **nije** prekid — tamo je učesnik prošao cijeli aktivni izazov i mjerenje oporavka nosi smisao, pa se zadržava.

Dokaz: `ProductionSessionFlow.HandleUserTermination` (prosljeđuje `skipPostMeasurements: true`), `FinishBlocks` (grananje kroz `SessionEndPolicy`), `SessionEndPolicy.PathFor`.

Prekid se bilježi kao `user_abort_fast_exit` u `events.jsonl` sa spiskom preskočenih faza. Writeri se flushuju i summary se snima **prije** grananja, pa je svaki podatak do trenutka prekida sačuvan.

### 3.3.1 Status po razlogu prekida

| Razlog | Status | Pravilo |
|---|---|---|
| Simulator sickness | `InvalidSimulatorSickness` | pravilo 4 — grana `UserReason`, **ne traži post-SSQ** |
| Obični prekid | `IncompleteUserTerminated` | pravilo 7 |
| Tracking lost | `InvalidTrackingFailure` | pravilo 3 |
| Tehnički problem | `IncompleteUserTerminated` | **nema zasebnog tehničkog statusa** za korisnički prekid |

Nijedan nije u `{Valid, ValidWithWarnings}`, pa gate 2 schedulera zamrzava sve nivoe. Scheduler se i dalje **poziva** i proizvodi `NoDecisionInvalidSession` sa kodom `SESSION_NOT_USABLE_<status>` — nivoi se ne mijenjaju, ali postoji eksplicitan zapis zašto, umjesto `schedulerDecision = null`.

### 3.4 Ključne tačke grananja

**HR gate** — `ProductionSessionFlow.cs:695-705`. Osvježava se periodično; izlaz je moguć preko `CancelPreSession()` (ln 673-680), dozvoljen samo u `{BaselinePreparation, BreathingReferenceBaseline, Ready}` (ln 682-693). Nema zapisa sesije jer ništa nije bodovano.

**Referenca može odbiti nastavak** — `CompleteBreathingReference()` ln 855-870: ako `quality != BaselineQuality.Good`, nudi se samo *„Pokušaj ponovo"* → `EnterBaselinePreparation()`. Komentar ln 867: *„Nema skrivenog participant override-a."*

**Globalni izazovni sat ne radi tokom priprema** — `ProductionSessionFlow.cs:603`: *„The global challenge clock NEVER runs during any of this."* Posljedica: pressure stage progresija počinje tek ulaskom u koridor.

**TimeExpired** — `HandleGlobalTimeExpired()` ln 1164-1173 → `pressureStageAtEnd = "Expired"` → loss consequence hold → `FinishBlocks(timeExpired: true)`. **Recovery i post-upitnici se svejedno izvršavaju.**

**Countdown** — `resumeCountdownSeconds = 3f` (`StressTrainingConfig.cs:24`) po komentaru je *„3-2-1 after Continue"*, dakle poslije pauze. `transitionToCorridorSeconds = 4f` (ln 25). Poseban „ready countdown" prije prvog bloka — **NOT CONFIRMED**.

---

## 4. TASK SISTEM

### 4.1 Kontrole

Konzola ima **5 semantičkih dugmadi + 9 Corsi pozicija** — `Console/ConsoleLayoutBuilder.cs:84-98`:
`Left`, `Match`, `Go`, `NoMatch`, `Right` + `CorsiLayout.PositionCount = 9` (`Tasks/CorsiTask.cs:20`).

### 4.2 Zadaci

**N-back** — `Tasks/NBackTask.cs`
Korisnik gleda niz simbola na tabletu i za svaki odlučuje da li se poklapa sa onim prije *n* koraka. Odgovor: `Match` / `NoMatch` (ln 109-110). Bez odgovora = `SemanticAction.None`.

| Parametar | L1 | L2 | L3 | Status |
|---|---|---|---|---|
| `nBackN` | 1 | 2 | 2 | PROJECT_HEURISTIC |
| `stimulusSetSize` | 4 | 6 | 8 | PROJECT_HEURISTIC |
| `trialCount` | 12 | 14 | 16 | PROJECT_HEURISTIC |
| `stimulusDurationSeconds` | 1.5 | 1.2 | 1.0 | PROJECT_HEURISTIC |
| `responseWindowSeconds` | 2.6 | 2.2 | 1.8 | PROJECT_HEURISTIC |
| `interTrialIntervalSeconds` | 1.0 | 0.9 | 0.7 | PROJECT_HEURISTIC |
| `targetMatchProportion` | 0.35 | 0.30 | 0.30 | PROJECT_HEURISTIC |

Izvor: `Tasks/TaskDifficultyConfig.cs:124-146`. L1→L2 mijenja *n* i set; L2→L3 mijenja **samo set i tempo**, *n* ostaje 2.

**Go/No-Go** — `Tasks/GoNoGoTask.cs`
Korisnik pritiska `Go` na GO stimulus i **suzdržava se** na NO-GO. Očekivani odgovor za NO-GO je `SemanticAction.None` (ln 52). Relevantna akcija je samo `Go` (ln 68-69).

| Parametar | L1 | L2 | L3 | Status |
|---|---|---|---|---|
| `trialCount` | 20 | 24 | 28 | PROJECT_HEURISTIC |
| `noGoProportion` | 0.20 | 0.30 | 0.40 | PROJECT_HEURISTIC |
| `stimulusSimilarityTier` | 1 | 2 | 3 | PROJECT_HEURISTIC |
| `stimulusDurationSeconds` | 0.8 | 0.6 | 0.5 | PROJECT_HEURISTIC |
| `responseWindowSeconds` | 1.2 | 1.0 | 0.8 | PROJECT_HEURISTIC |
| `interTrialIntervalSeconds` | 1.2 | 1.0 | 0.8 | PROJECT_HEURISTIC |

Izvor: `TaskDifficultyConfig.cs:148-170`. `stimulusSimilarityTier` je opisan kao `1 = clearly distinct … 3 = visually similar` (ln 31).

**Flanker** — `Tasks/FlankerTask.cs`
Eriksen flanker. Korisnik **uvijek** odgovara na **smjer centralne strelice relevantnog reda** (`CentralDirection()`). Odgovor: `Left` / `Right`, fizički dugmad na odgovarajućim stranama konzole.

| Parametar | L1 | L2 | L3 | Status |
|---|---|---|---|---|
| `trialCount` | 16 | 20 | 24 | PROJECT_HEURISTIC |
| `incongruentProportion` | 0.35 | 0.50 | 0.60 | PROJECT_HEURISTIC |
| `neutralProportion` | 0.15 | 0.10 | **0.0** | PROJECT_HEURISTIC |
| `stimulusDurationSeconds` | 1.0 | 0.8 | 0.7 | PROJECT_HEURISTIC |
| `responseWindowSeconds` | 1.6 | 1.3 | 1.0 | PROJECT_HEURISTIC |
| `interTrialIntervalSeconds` | 1.0 | 0.9 | 0.75 | PROJECT_HEURISTIC |
| **`flankerDistractorRows`** *(v4)* | **0** | **2** | **2** | PROJECT_HEURISTIC |
| **`flankerDistractorDensity`** *(v4)* | **0.0** | **0.55** | **1.00** | PROJECT_HEURISTIC |

Izvor: `TaskDifficultyConfig.cs` (Flanker blok). Na L3 neutralni trialovi potpuno nestaju.

**Vizuelna progresija kroz nivoe (v4).** Broj redova, ne pravilo, raste sa nivoom:

| Nivo | Prikaz | Relevantan red |
|---|---|---|
| L1 | **jedan** red od pet strelica — klasičan flanker | taj jedini red |
| L2 | **tri** reda: jedan iznad, relevantni, jedan ispod | **srednji** |
| L3 | **tri** reda, gušća distrakcija u gornjem i donjem | **srednji** |

Dva reda se **namjerno ne koriste**: kod neparnog broja redova srednji red je geometrijski jednoznačan, pa je uputstvo „centralna strelica srednjeg reda" tačno bez dodatnog objašnjenja.

Gornji i donji red su **isključivo vizuelni**. Svaka njihova ćelija je nezavisno izvučena iz istog seeded RNG-a: sa vjerovatnoćom `flankerDistractorDensity` dobija nasumičan smjer, inače neutralnu crticu. Ne ulaze ni u `expectedAction`, ni u congruent/incongruent klasifikaciju, ni u scoring.

**Kodiranje.** `arr:<gornji>|<relevantni>|<donji>` kad ima distraktora, `arr:<relevantni>` kad nema. `RelevantRow()` uvijek vraća srednji element, pa je L1 poseban slučaj samo po tome što ima jedan red.

**Determinizam je očuvan.** Distraktori se izvlače **poslije** relevantnog reda i samo kad `flankerDistractorRows > 0`, pa L1 troši isti broj slučajnih poteza kao prije ove izmjene i proizvodi identične stimuluse. L2/L3 su deterministični za dati seed, ali njihov niz nije isti kao prije v4 — proporcije jesu iste, raspored lijevo/desno nije.

**Uputstvo.** `TaskInstructions` koristi postojeći task+level tutorial sistem (`NeedsTutorial` / `TutorialVersionFor`, gdje je verzija = nivo). Pri prvom susretu sa L2 tekst eksplicitno kaže da se prikazuju tri reda, da se odgovara prema centralnoj strelici u srednjem redu i da ostale treba ignorisati; na L3 se ponavlja da je srednji red i dalje jedini relevantan. **Nije uveden nikakav paralelni sistem.**

**Corsi-inspired** — `Tasks/CorsiTask.cs` (599 ln)
Devet neoznačenih pozicija; tablet prikazuje sekvencu, konzola prima reprodukciju. **Prva greška prekida trial**, čuva se tačan prefiks.

| Parametar | L1 | L2 | L3 | Status |
|---|---|---|---|---|
| `trialCount` | 6 | 7 | 6 | PROJECT_HEURISTIC |
| `corsiMin/MaxSequenceLength` | 3–5 | 4–6 | 3–5 | PROJECT_HEURISTIC |
| `corsiPresentationStepSeconds` | 0.9 | 0.65 | 0.8 | PROJECT_HEURISTIC |
| `corsiInterStepSeconds` | 0.35 | 0.25 | 0.3 | PROJECT_HEURISTIC |
| `corsiRetentionSeconds` | 1.0 | 1.0 | 1.2 | PROJECT_HEURISTIC |
| `corsiResponseBaseSeconds` | 2.5 | 2.0 | 2.5 | PROJECT_HEURISTIC |
| `corsiResponsePerItemSeconds` | 1.2 | 1.0 | 1.3 | PROJECT_HEURISTIC |
| **`corsiBackward`** | false | false | **true** | CONFIRMED |

Izvor: `TaskDifficultyConfig.cs:82-122`. **L3 mijenja pravilo, ne tempo** — obrnuta reprodukcija; kod eksplicitno navodi *„rule change → tutorial repeats"* (ln 111). Sigurnosni timeout neaktivnosti 30 s (ln 65) — nije scoring timeout.

### 4.3 Metrike po trialu

`Data/TrialRecord.cs` (`schemaVersion = 1`), ravna struktura po dizajnu (ln 7-8).

Zajedničke: `correct`, `missed` (omisija), `falsePositive` (komisija), `reactionTimeMs` (−1 = nema odgovora), `stimulus`, `expectedResponse`, `actualResponse`, `isWarmup`.
Kontekst: `difficultyLevel`, `pressureLevel`, `condition`, `currentBpm` (−1 = nema), `hrZone`, `wasPausedDuringTrial`, `remainingBlockTimeSeconds`, `remainingGlobalTimeSeconds`, `sessionSeed`, `blockSeed`.
Corsi (ln 48-61): `corsiPresentedSequence`, `corsiRequiredResponseOrder`, `corsiResponsePrefix`, `corsiSequenceLength`, `corsiCorrectlyReproducedCount`, `corsiFirstErrorIndex`, `corsiFirstWrongButtonId`, `corsiStartResponseMs`, `corsiInterPressIntervalsMs`, `corsiTotalResponseMs`, `corsiBackward`, `corsiAbandonedByInactivity`.

> `remainingGlobalTimeSeconds` je polje koje omogućava post-hoc rekonstrukciju **pressure stage-a** u kojem je trial izveden; `pressureLevel` bilježi **pressure level** sesije. Dva različita podatka.

### 4.4 Šta ide u adaptaciju

Agregira se u `TaskSessionSummaryData` (`Data/SessionSummaryData.cs:104-131`): `accuracy`, `meanReactionTimeMs`, `medianReactionTimeMs`, `accuracyStdAcrossBlocks`, `maxBpmDuringTask`, `avgBpmDuringTask`, `elevatedOrHighZoneSeconds`, flanker congruency polja, `corsiMaxCorrectSequenceLength`, `corsiMeanCorrectPrefix`.

### 4.5 Izbor zadataka i raspored

**3 od 4** — `Session/SeededConstrainedTaskSelector.cs`, `AlgorithmVersion = "sel-v1"` (ln 20). Deklarisana pravila ln 27-39:
1. isti seed + ista istorija + ista konfiguracija → ista selekcija — CONFIRMED
2. tačno tri **različita** zadatka, jedan izostavljen — CONFIRMED
3. zadatak izostavljen u prethodnoj **VALIDNOJ** produkcijskoj sesiji **garantovano ulazi sada** — CONFIRMED
4. tie-breaking seeded — CONFIRMED
5. nevalidne/prekinute/demo sesije se ne smiju predati kao istorija; poziv razrješava preko `ResolvePreviousOmitted` — CONFIRMED

Reason kodovi (ln 8-16): `IncludedBecausePreviouslyOmitted`, `IncludedByBalancedSeededRotation`, `OmittedByBalancedSeededRotation`, `OmittedByConfigDisable`, `IncludedAllRemainingEnabled`.

**9 blokova** — `Session/ProductionSessionPlanGenerator.cs`. Tvrdo zaključano: ln 34-36 i ln 101-103 bacaju izuzetak ako broj rundi nije 3.

**Pravilo protiv uzastopnog ponavljanja** — ln 89-91, 105-121: svaka runda je seeded Fisher-Yates permutacija tri zadatka; ako bi runda počela zadatkom kojim je prethodna završila, permutacija se **rotira ulijevo jednom**. Deterministički, bez odbacivanja i ponovnog izvlačenja.

**Seedovi** — `TaskSeedService.DeriveNamedSeed(masterSeed, salt)`: `taskSelectionSeed`, `blockOrderSeed`, `pressureSeed`, `consoleLayoutSeed`. Svi se čuvaju u `SessionSummaryData:39-43`.

---

## 5. CONTROLLED PRESSURE

### 5.0 Dvije ose — obavezno razdvojiti

| | Pressure **LEVEL** | Pressure **STAGE** |
|---|---|---|
| Domen | 1 / 2 / 3 | `Stable → Early → Mid → Late → Critical → Expired` |
| Vremenska skala | **između sesija** | **unutar sesije**, kontinuirano |
| Ko ga određuje | `AdaptationScheduler` → `profile.currentPressureLevel` | preostala frakcija globalnog vremena |
| Šta kontroliše | *koliko jako* — intensity cap, broj događaja, amplituda podrhtavanja, audio cap, alpha pukotina | *kada i dokle* — koje se promjene koridora pokreću i kojim redom |
| Tip | `PressureLevelConfig` (`PressureTimeline.cs:112-122`) | `PressureStage` enum (`PressureTimeline.cs:7-15`) |
| Mijenja trajanje sesije? | **ne** (`DurationMultiplier` = 1.00 na sva tri nivoa) | n/a — stage *jeste* funkcija preostalog vremena |

### 5.1 Mapa

```
pressureLevel (1|2|3)  ←  profile.currentPressureLevel  ←  scheduler (između sesija)
        │
        ▼
PressureLevelConfig.Get(level)          PressureTimeline.cs:124
  intensityCap · scheduledEventCount · shakeAmplitudeMeters
  audioRampCap · crackMaxAlpha · globalDurationMultiplier(=1.00)
        │
        ▼
pressureSeed  →  deterministički raspored događaja
  PressureEventDefinition{ atRemainingFraction, kind, magnitude }
  "derived only from the pressure seed and level"   PressureTimeline.cs:98
        │
        ▼
═══ UNUTAR SESIJE: stage progresija po preostaloj frakciji ═══
  1.00 .. 0.70  Stable
  0.70 .. 0.50  Early        EarlyThreshold    = 0.70
  0.50 .. 0.30  Mid          MidThreshold      = 0.50
  0.30 .. 0.10  Late         LateThreshold     = 0.30
  0.10 .. 0.00  Critical     CriticalThreshold = 0.10
       = 0.00   Expired      ExpiredThreshold  = 0.00
        │
        ├─► FIZIČKA PROMJENA PROSTORA
        │     modularna putanja:  _puzzle.ApplyStage(stage) po segmentu
        │     fallback putanja:   parovi pod/plafon se zatvaraju od Late
        │
        ├─► KONTINUIRANI VIZUELNI MEHANIZAM
        │     ApplyLights()  — boja/intenzitet prate CurrentIntensity
        │     shake          — _pressureRoot.localPosition, amplituda iz levela
        │
        └─► DISKRETNI DOGAĐAJI (seeded, v. §5.4)
              Rumble · DustBurst · CeilingCreak
        │
        ▼
Expired  →  GameOverController: crna sfera oko glave, fade 2.50 s
         →  loss consequence hold  →  FinishBlocks(timeExpired: true)
```

### 5.2 Vrijednosti i njihov status

Sve iz `Pressure/PressureTimeline.cs:29-96`, klasa `PressureHeuristics`. Klasa se sama deklariše (ln 25-27): *„Centralized PROJECT_HEURISTIC values for the pressure prototype. These are design values, not scientifically validated thresholds."*

**Svi su `public const` → hard-coded, NISU configurable kroz `config.json`.**

Per **LEVEL** (međusesijski intenzitet):

| Vrijednost | L1 | L2 | L3 | Ln | Status |
|---|---|---|---|---|---|
| `IntensityCap` | 0.50 | 0.75 | 1.00 | 46-48 | hard-coded PROJECT_HEURISTIC |
| `EventCount` | 3 | 5 | 8 | 50-52 | hard-coded PROJECT_HEURISTIC |
| `ShakeMeters` | 0.004 | 0.008 | 0.012 | 54-56 | hard-coded PROJECT_HEURISTIC |
| `AudioCap` | 0.40 | 0.70 | 1.00 | 58-60 | hard-coded PROJECT_HEURISTIC |
| `CrackAlpha` | 0.35 | 0.60 | 0.85 | 62-64 | hard-coded PROJECT_HEURISTIC |
| `DurationMultiplier` | **1.00** | **1.00** | **1.00** | 42-44 | hard-coded |

Per **STAGE** (unutarsesijske granice, izražene kao preostala frakcija):

| Prag | Vrijednost | Ln | Status |
|---|---|---|---|
| `EarlyThreshold` | 0.70 | 31 | hard-coded PROJECT_HEURISTIC |
| `MidThreshold` | 0.50 | 32 | hard-coded PROJECT_HEURISTIC |
| `LateThreshold` | 0.30 | 33 | hard-coded PROJECT_HEURISTIC |
| `CriticalThreshold` | 0.10 | 34 | hard-coded PROJECT_HEURISTIC |
| `ExpiredThreshold` | 0.00 | 35 | hard-coded |

Ograničenja rasporeda događaja: `FirstEventMaximumFraction = 0.68`, `LastEventMinimumFraction = 0.04`, `EventMinimumMagnitude = 0.40`, `EventMagnitudeRange = 0.60` (ln 37-40); jezgro rasporeda `CoreEarlyEventMinimumFraction = 0.52`, `CoreMidEventMaximumFraction = 0.49`, `CoreMidEventMinimumFraction = 0.32` (ln 66-68) — svi hard-coded PROJECT_HEURISTIC.

Geometrija (ln 70-78): `SegmentPairCount = 4`, `SegmentSpanMeters = 3.60`, `SegmentWidthMeters = 3.10`, `SegmentCloseDistanceMeters = 1.40`, `SegmentCloseSeconds = 6.0`, `FloorOffsetMeters = 0.015`, `CeilingOffsetMeters = 2.98`.
Finale (ln 89-90): `FinaleFadeSeconds = 2.50`, `FinaleSafetyFallbackSeconds = 4.00`.

**Izvedene vrijednosti (nisu konstante):**
- `CurrentIntensity` — iz `_timeline.Intensity(...)`, ograničen `intensityCap`-om levela
- collapse progress (fallback putanja) — `(0.30 − remainingFraction) / 0.30`, `PressureController.cs:634-636`
- `levelMagnitude = evt.magnitude * _timeline.Level.intensityCap`, `PressureController.cs:691-692`
- shake amplituda — `_timeline.Level.shakeAmplitudeMeters * Clamp01(CurrentIntensity + _eventPulse)`, `PressureController.cs:767-768` → **level daje maksimum, stage/intenzitet ga skalira**
- `ClosablePairCount = _pairs.Count − 1`, `PressureController.cs:82` → **jedan par uvijek ostaje otvoren**

### 5.3 Fizička promjena prostora — dvije putanje

`PressureController.cs:624-637`:

- **modularna putanja** (`_puzzle != null`, primarna): `_puzzle.ApplyStage(stage)` + `TickActive(dt)`. Svaki segment ima **sopstveni raspored po stage-u** — `PuzzleCorridorController.ApplyStage()` ln 166-187 prosljeđuje stage svakom `PressureSegmentController`-u. Na prelazu u `Expired` emituje se jednokratni `FinalCollapseStarted` za `S5/S6` (ln 174-186).
- **fallback putanja** (`_collapse`): aktivna **tek od `Late`**; `CorridorCollapseController.SetCollapseProgress()` zatvara parove pod/plafon srazmjerno progresu kroz Late+Critical raspon.

`PuzzleCorridorController` takođe drži logičku provjeru tla nezavisnu od fizičkih kolidera (ln 189-192) koju koristi sistem pada.

### 5.4 Kategorizacija pressure mehanizama — po stvarnoj implementaciji

Tačan sadržaj `FireEvent()` — `PressureController.cs:694-711`:

| `PressureEventKind` | Šta kod stvarno radi | Kategorija |
|---|---|---|
| `Rumble` | `_audio?.PlayRumble(magnitude, level.audioRampCap)` — **samo audio** | **zvučni cue** |
| `DustBurst` | `_transientFx?.TriggerDustBurst(levelMagnitude)` **+** `_audio?.PlayDustBurst(...)` | **vizuelni efekat + prateći zvuk** |
| `CeilingCreak` | `_transientFx?.TriggerCeilingSignal(levelMagnitude)` **+** `_audio?.PlayCeilingCreak(...)` | **vizuelni signal + prateći zvuk** |
| `LightFlicker` | `case PressureEventKind.LightFlicker: break;` — **prazno** | **NIJE IMPLEMENTIRAN** |

**Nema haptike ni vibracije u pressure sistemu.** Provjereno grep-om (`Haptic`, `Vibrat`, `SetControllerVibration`): jedini pogoci su u `Console/ConsoleControlBase.cs:42-47`, i odnose se na Meta `FeedbackManager` za poke interakciju sa konzolom — nema veze sa pritiskom. `Rumble` je naziv zvučnog cue-a, ne haptičkog.

**Ambijentalni sloj (v4).** Do v4 je hodnik između četiri zakazana događaja bio potpuno tih, što se čita kao praznina a ne kao napetost. Dodan je `CorridorAmbience.wav` — proceduralno generisan besprekidni loop od 6 s (duboki brown-noise sloj ispod 110 Hz, dvije spore rezonance 57/88.5 Hz koje se nikad ne slože u jasan ton, i jedva čujan visoki sloj). Pušta se od `Begin()` i traje kroz cio aktivni dio; glasnoća prati `CurrentIntensity` od `AmbienceFloorShare = 0.35` do `AmbienceVolumeCap = 0.18`, pomnoženo `audioRampCap`-om nivoa. Nije prostorizovan (`spatialBlend = 0`) jer pripada prostoriji, ne tački u njoj. Oba praga su **PROJECT_HEURISTIC**.

**Jedan izvor po cue-u, sa zaštitom od ponovnog okidanja (v4).** Svaki zvučni cue ima svoj `AudioSource`, a `Play()` radi `Stop()` pa `Play()` — pa se isti cue nikad ne gomila sam na sebe, ali je do v4 mogao sam sebe da **presiječe**. Tokom kolapsa `OnPuzzleAnimationEvent` okida po segmentu, znatno brže nego što klip traje, i sve to ide u samo dva zajednička izvora (`rumble`, `ceiling`), pa se škripa pretvarala u stuttering. Od v4 novo okidanje istog cue-a unutar `MinRetriggerSeconds = 0.12` se **odbacuje** i zvuk koji već svira se pusti da završi. Zakazani pressure događaji su razmaknuti sekundama i ovim nisu dodirnuti. Glasnoće i broj zvukova nisu mijenjani.

Mehanizmi koji **nisu** diskretni događaji i moraju se voditi odvojeno:

| Mehanizam | Implementacija | Kategorija |
|---|---|---|
| Promjena osvjetljenja | `ApplyLights()` — `PressureController.cs:714-734`. `_cageLight` prelazi ka narandžastoj i raste do +55 %, `_consoleLight` se prigušuje do −30 %, srazmjerno `CurrentIntensity` + `_eventPulse` | **kontinuirani vizuelni mehanizam** |
| Podrhtavanje prostora | `PressureController.cs:766-772` — pomjera `_pressureRoot.localPosition` sinusno; amplituda = `level.shakeAmplitudeMeters × Clamp01(CurrentIntensity + _eventPulse)` | **kontinuirani prostorni mehanizam** |
| Kolaps geometrije | `_puzzle.ApplyStage(stage)` / `SetCollapseProgress(...)` | **fizička promjena prostora** |
| Kritično upozorenje | `OnStageEntered()` ln 680-684 — jednokratni `_audio?.PlayCriticalWarning(...)` pri ulasku u `Critical` | **jednokratni zvučni cue vezan za stage** |

**Ispravka v1:** v1 je naveo da se pri `Critical` prikazuje „critical void". `PressureTransientFxController.SetCriticalVoidVisible(bool visible) { _ = visible; }` — `PressureTransientFxController.cs:166` — je **no-op**; komentar iznad: *„No-op. Kept so PressureController's stage/finale calls stay unchanged; see CriticalVoidVisible for why the cube is gone."* Jedini stvarni efekat ulaska u `Critical` je zvučno upozorenje. `CriticalVoidBehindDistanceMeters = 2.80` (`PressureTimeline.cs:87`) je zaostala konstanta bez efekta.

### 5.5 Tri tvrdnje koje traže potvrdu

| Tvrdnja | Status | Dokaz |
|---|---|---|
| Pressure **level** ne mijenja trajanje sesije | **CONFIRMED** | `PressureTimeline.cs:42-44`, sva tri multiplikatora 1.00. Trajanje zavisi isključivo od `globalDifficultyTimeMultiplierLevel1/2/3` (task difficulty), `StressTrainingConfig.cs:31-33` |
| Pressure **stage** progresija teče **tokom task blokova** | CONFIRMED | `PressureController` se armira pri ulasku u koridor (`EnterCorridor()`, ln 997+) i tika kroz `TickPressure` (`ProductionSessionFlow.cs:591`) paralelno sa blokovima; globalni sat ne radi ni u jednoj pripremnoj fazi (ln 603) |
| Task difficulty i pressure **level** su odvojene dimenzije | CONFIRMED | Odvojena polja u profilu (`currentNBackLevel`… vs `currentPressureLevel`); odvojene tabele pravila; `AdaptationRuleSet.cs:149-154` zabranjuje istovremeno povećanje |

### 5.6 Timeout

`TriggerGameOverFinale()` (`PressureController.cs:776`) → `CurrentIntensity = 1`, `AdvanceToStage(Expired)` (ln 782-783) → `GameOverFinaleComplete` (ln 815).
`GameOverController` (ln 133+) pravi crnu sferu `GameOverHeadFade` oko `CenterEye` sa `scale (−8, 8, 8)` (negativan X = okrenuta ka unutra). Eksplicitna garancija ln 130-132: *„The center-eye transform is an anchor only and is **never moved**."*

**Time penalties:** `TimerPenaltyConfig.timePenaltyOnErrorEnabled = false` — `StressTrainingConfig.cs:89`; komentar ln 86-88: *„ENTIRE FEATURE DISABLED BY DEFAULT until a final design decision is confirmed."* Arhitektura postoji (per-error-type, per-task, per-pressure-level množioci, `minRemainingSeconds = 30`), ali je neaktivna.

---

## 6. HR PIPELINE

### 6.1 Puni tok

```
Xiaomi Smart Band 9  ──BLE──►  Mi Fitness (Android)
   šalje: PPG-izveden BPM                    [izvan koda projekta]
        │
        ▼  zapis u Android logcat
"single_heart_rate=[HrItem(sid=…, time=…, hr=N)]"
        │
        ▼  HR Relej (telefon)      android_companion/HrRelay/  [NECOMMITOVAN]
HrItemParser.parseAndAccept()
  · skala se DETEKTUJE iz podataka (plain bpm vs bpm×5)
  · plauzibilnost 30..220 na DEKODIRANOJ vrijednosti
  · dedup po watch timestampu (samo strogo noviji)
HrRelaySender  →  UDP unicast :5005
        │
        ▼
Quest: NetworkHeartRateSource (bind 0.0.0.0:5005)
        │
        ▼  HrPacketParser.Parse()
  · protocolVersion == 1
  · phone-relay bez sessionToken → ODBIJEN
  · kind=="heartbeat" → Ok, ali Bpm=0, nikad uzorak
        │
        ▼
HrRelayIngest.Evaluate()  →  AcceptSample | Heartbeat | Reject
  · vezivanje za PRVI viđeni token; drugi token → Reject
  · sequence ordering, sequence==0 = restart
  · watch-time ordering (stale replay → Reject)
        │
        ▼
HeartRateService.Accept()
  · 30..220 → inače counted-not-used
  · quality = signalAge > 8 s ? Stale : Good
  · zona = Classify(bpm) RELATIVNO prema referenci
        │
        ├──►  CurrentBpm / CurrentZone / SessionPeakBpm
        ├──►  hr.jsonl  (HeartRateSampleRecord)
        └──►  task agregati (pauza se ISKLJUČUJE)
                    │
                    ▼
        TaskSessionSummaryData → BuildAdaptationInput()
                    │
                    ▼
        AdaptationInput.avgBpmDelta / elevatedZoneRatio /
                       sessionAvgBpmDelta / elevatedOrHighSeconds
```

### 6.2 Šta dolazi sa sata

**Samo BPM.** `RawHrSample.Bpm` je `int` — `HR/HeartRateContracts.cs:13`.
`HeartRateSampleRecord` nema nijedno polje za PPG, IBI, RR ni HRV.
**CONFIRMED: ne postoje PPG/IBI/RR/HRV podaci nigdje u sistemu.** `SCIENTIFIC_TRACEABILITY.md` ln 12 to nezavisno potvrđuje (*„bez HRV"*).

### 6.3 Šta telefon šalje

Format `RelayPacket` — `HR/HrPacketParser.cs:47-57`:
```json
{"protocolVersion":1,"sessionToken":"…","sequence":N,
 "timestampUtc":"2026-07-14T10:39:13Z","bpm":85,
 "source":"phone-relay","kind":"sample"}
```
`kind` = `"sample"` | `"heartbeat"`; prazan `kind` se tretira kao sample (ln 56).

Sistem prihvata i dva starija formata: `BridgePacket` (`schemaVersion`, `hr`, `sourceTimestampUtc`, `sentAtUtc`, `source`) ln 36-45 i `LegacyPacket` (`{"hr":82,"ts":"15:31:50"}`) ln 59-64.

**Skala na strani telefona** — `android_companion/HrRelay/.../HrItemParser.java`. Mi Fitness mijenja jedinicu između verzija: `hr=85` (2026-07-14) i `hr=425` za isti puls (2026-08-23, ×5). Skala se detektuje iz podataka, ne dijeli se fiksno. **STATUS: necommitovano.**

### 6.4 Šta Quest validira

| Provjera | Lokacija | Ponašanje |
|---|---|---|
| `protocolVersion != 1` | `HrPacketParser.cs:89-90` | odbijeno |
| `source=="phone-relay"` bez tokena | `HrPacketParser.cs:96-98` | odbijeno (i heartbeat i sample) |
| `kind=="heartbeat"` | `HrPacketParser.cs:100-110` | Ok, `Bpm=0`, **ne gated na plauzibilnost**, nikad uzorak |
| token ≠ prvi vezani | `HrRelayIngest.cs:70-80` | Reject, `WrongTokenCount++` |
| duplikat / neispravan redosljed | `HrRelayIngest.cs` | Reject; `sequence == 0` = legitiman restart pošiljaoca |
| stale watch timestamp | `HrRelayIngest.cs` | Reject, `StaleWatchCount++` |
| `bpm` van 30..220 | `HeartRateService.cs:215-216` | `TotalSamples++` ali **`return`** — *„counted, not used"* |
| `signalAgeMs > 8000` | `HeartRateService.cs:227-228` | `HrQualityStatus.Stale`; ne osvježava `_lastGoodSampleRealtime` |

### 6.5 Šta se zapisuje

`HeartRateSampleRecord` → `hr.jsonl`, polja iz `HeartRateService.cs:235-250`:
`bpm`, `receivedAtUtcIso`, `sourceTimestampUtcIso`, `monotonicReceiveSeconds`, `signalAgeMs`, `qualityStatus`, `sourceType`, `sequence`, `isPaused`, `sessionState`, `taskType`, `blockId`, `trialId`, `sampleId` (auto-increment, `LogWriters.cs:109-116`).

**Snima se kontinuirano kroz cijelu sesiju**, ali samo uzorci iz referentne faze ulaze u referentnu vrijednost.

### 6.6 Zone i referenca

`HeartRateZoneEvaluator.Classify()` — `HR/HeartRateZoneEvaluator.cs:25-38`:
```csharp
if (signalLost || bpm <= 0) return HrZone.SignalLost;
if (BaselineAvgBpm <= 0)    return HrZone.Stable;   // prije reference — dokumentovano ograničenje
float delta = bpm - BaselineAvgBpm;
if (delta >= highDeltaBpm)     return HrZone.High;      // +22
if (delta >= elevatedDeltaBpm) return HrZone.Elevated;  // +10
return HrZone.Stable;
```
**Ne postoji nijedan apsolutni BPM prag za stres** — provjereno grep-om; jedine apsolutne granice su plauzibilnost (30/220) i prikazni uslovi `bpm > 0`.

Referenca se postavlja tek na kraju referentne faze: `ZoneEvaluator.SetBaseline(_baselineSummary.averageBpm)` — `ProductionSessionFlow.cs:850`.

### 6.7 Šta scheduler stvarno koristi

Ne sirove uzorke, nego četiri agregata: `TaskMetricsInput.avgBpmDelta`, `TaskMetricsInput.elevatedZoneRatio`, `AdaptationInput.sessionAvgBpmDelta`, `AdaptationInput.elevatedOrHighSeconds` (+ `totalActiveSeconds` kao imenilac).

### 6.8 Produkcijska kapija

`HeartRateService.GetProductionReadiness()` ln 283+. Odbija: `Simulated` (ln 293-297), `NativeAdbPlaceholder` (ln 299-303), sve osim `NetworkBridge` (ln 305-308). Komentar ln 278-281: *„This is an engineering gate, not a psychological-stress assessment."*

---

## 7. MJERENJA I VALIDNOST

### 7.1 Sistemske uloge

| Instrument | Kada | Rezultat | Ide scheduleru | Utiče na validity |
|---|---|---|---|---|
| **Raw NASA-TLX** | 1× post-session, faza 13 | `totalScore` = aritmetička sredina 6 dimenzija + svaka dimenzija zasebno (`QuestionnaireScoringService.cs:75-87`) | **DA** — svih 7 vrijednosti (`AdaptationInput.cs:57-63`) | ne |
| **SSQ** | 2× — pre (faza 6) i post (faza 12) | `totalScore = 3.74 × (rawN+rawO+rawD)`, subskale ×9.54/7.58/13.92 (`QuestionnaireScoringService.cs:19-22,55-73`) | **NE** — nema SSQ polja u `AdaptationInput` | **DA** — `SsqPost − SsqPre ≥ 20` → `InvalidSimulatorSickness` |
| **STAI-6** | uslovno — prva i posljednja sesija ciklusa | `totalScore = sum × 20/6`, opseg 20–80 (`QuestionnaireScoringService.cs:103`) | **NE** — CONFIRMED, nema STAI polja u `AdaptationInput` | ne |

STAI se čuva u `profile.cycleSummaries[*].staiScoreCycleStart` / `staiScoreCycleEnd`.

**Uslovi STAI** — `Session/UserProfileService.cs:75-94` (traženi `_staiPreDue`/`_staiPostDue` **ne postoje**):
```csharp
IsFirstSessionOfCycle(p) => p.currentCycleSessionIndex == 0;                              // 75-76
IsLastSessionOfCycle(p)  => p.currentCycleSessionIndex >= p.plannedCycleSessionCount - 1; // 93-94

ShouldAdministerCycleStartStai(p):   // 78-83
    IsFirstSessionOfCycle(p) && (cycle == null || cycle.staiScoreCycleStart < 0f)
ShouldAdministerCycleEndStai(p):     // 85-90
    IsLastSessionOfCycle(p)  && (cycle == null || cycle.staiScoreCycleEnd < 0f)
```
Dvostruki uslov (pozicija **I** `< 0f`) sprječava dvostruko administriranje.

### 7.2 Validity decision tabela — stvarni redosljed

`Session/SessionValidityEvaluator.Evaluate()` ln 51-124. **Prvo pravilo koje se poklopi pobjeđuje** (deklarisano ln 20-22).

| # | Uslov | Rezultat | Ln | Prag |
|---|---|---|---|---|
| 1 | `IsDemoPlan` | `DemoOnly` | 55-59 | — |
| 2 | `UnhandledException` ∨ `Abandoned` ∨ (`SystemTerminated` ∧ ¬`TimeExpired`) | `InvalidTechnicalFailure` | 61-69 | — |
| 3 | `TrackingLost` (sistem) ∨ `UserReason == TrackingLost` | `InvalidTrackingFailure` | 71-76 | — |
| 4 | `UserReason == SimulatorSickness` ∨ (`SsqPre ≥ 0` ∧ `SsqPost ≥ 0` ∧ `SsqPost − SsqPre ≥ 20`) | `InvalidSimulatorSickness` | 78-87 | **20** PROJECT_HEURISTIC |
| 5 | `HrWasExpected` ∧ `hrRequiredForValidity` ∧ `ratio ≥ 0` ∧ `ratio < 0.30` | `InvalidInsufficientHeartRate` | 89-95 | **0.30** PROJECT_HEURISTIC |
| 6 | `TimeExpired` | `IncompleteTimeExpired` | 97-101 | — |
| 7 | `CompletionStatus == UserTerminated` | `IncompleteUserTerminated` | 103-107 | — |
| 8 | `warnings > 2` ∨ `ratio < 0.30` ∨ `HeartRateSignalLost` ∨ `ScheduleOverride` | `ValidWithWarnings` | 109-121 | **2**, **0.30** PROJECT_HEURISTIC |
| 9 | inače | `Valid` | 123 | — |

**Kritično za razumijevanje:** `hrRequiredForValidity = false` po defaultu (`SessionValidityConfig` ln 13, komentar *„MVP default: HR loss degrades, does not invalidate"*). **Pravilo 5 se u praksi nikad ne aktivira**; gubitak HR-a pada na pravilo 8 → `ValidWithWarnings`.

Pragovi su `[Serializable]` polja u `SessionValidityConfig` (ln 9-15) → **configurable**, za razliku od pressure konstanti.

`ScheduleOverride` je **upozorenje, nikad invalidacija** (komentar ln 40).

---

## 8. ADAPTIVNI SCHEDULER

Fajlovi: `Adaptation/AdaptationScheduler.cs` (186), `AdaptationRuleSet.cs` (194), `AdaptationConfig.cs` (43), `AdaptationInput.cs` (77), `AdaptationExplanationBuilder.cs` (175).

**Kada:** faza 16, poslije validity-ja. `AdaptationScheduler.cs:9-10`: *„Runs ONLY after a session is closed — never during an active block (architecture rules 17/18)."* Potvrda i u `TaskDifficultyConfig.cs:53`: *„Difficulty changes ONLY between sessions."*

> Scheduler mijenja **pressure level (1–3)**, nikad pressure stage. Stage je funkcija vremena unutar sesije i scheduler ga ne dodiruje.

### A. Scheduler gates

**Gate 1 — `schedulerEligible`**, `ProductionSessionFlow.cs:831-836`:
```csharp
return !developerTest
    && sourceType == HrSourceType.NetworkBridge
    && baselineQuality == BaselineQuality.Good;
```
Postavlja se na kraju referentne faze (ln 846-849). Pad → `schedulerDecision = null`, log `adaptation_skipped` (ln 1368-1375), nivoi nepromijenjeni. **Simulirani HR nikad ne otključava scheduler.**

**Gate 2 — validity**, `AdaptationScheduler.cs:33-56`:
```csharp
bool sessionUsable = validityStatus == Valid || validityStatus == ValidWithWarnings;
```
Pad → svi zadaci `FrozenTask(...)` sa kodom `SESSION_NOT_USABLE_<status>`, pressure `NoDecisionInvalidSession`, fired rule `G_SESSION_INVALID`, explanation je jedna rečenica.

**Kontekst v4 — kapija se sada dostiže i bez post-mjerenja.** Otkad dobrovoljni prekid ide `StraightToValidity` (§3.3), scheduler se poziva i za sesije bez post-SSQ, NASA-TLX i oporavka. To ništa ne mijenja u ishodu: takva sesija nikad ne dobija status iz `{Valid, ValidWithWarnings}`, pa je gate 2 uvijek zatvara prije nego što ijedno pravilo pogleda ulaze. `AdaptationInput` je tada djelimično prazan (`tlxTotal = -1`, `recoveryMeasured = false`), ali se ipak serijalizuje u `inputSnapshotJson` — zapis „šta je sistem znao u trenutku prekida“ ima vrijednost za analizu čak i kad odluke nema.

**Gate 3 — po zadatku:** `!wasSelectedThisSession` → `newLevel = currentLevel`, kod `NOT_SELECTED_THIS_SESSION` (ln 111-117). Komentar: *„no performance data exists, so its level is held verbatim (spec §33)."*

### Ulazi i njihovi izvori

| Ulaz | Izvor |
|---|---|
| `accuracy`, `meanReactionTimeMs`, `missCount`, `falsePositiveCount`, `blockAccuracyStd` | `BuildTaskSummaries()` nad `trials.jsonl` → `TaskSessionSummaryData` |
| `avgBpmDelta`, `elevatedZoneRatio` | `HeartRateService` per-task agregacija |
| `sessionAvgBpmDelta`, `elevatedOrHighSeconds`, `totalActiveSeconds` | sesijska agregacija `HeartRateService` |
| `recoveryReachedZone`, `recoverySeconds` | `RecoveryEvaluator`, faza 11 |
| `tlxTotal` + 6 dimenzija | `QuestionnaireScoringService.ScoreTlx` + `TlxDimension()` |
| `validityStatus`, `timeExpired`, `scheduleOverride` | `SessionValidityEvaluator` |
| `wasSelectedThisSession` | `SeededConstrainedTaskSelector` |
| `currentLevel`, `currentPressureLevel` | `UserProfileData` |
| `baselineBpm` | `BaselineSummaryData.averageBpm` |
| **`sessionAvgBpmDelta`** *(v3, sad živ)* | `HeartRateService.SessionAverageBpm − baselineBpm`, postavlja se samo kad su obje vrijednosti `> 0`; inače ostaje `-999f` |
| **`recoveryMeasured`** *(v3, novo)* | `Summary.recovery.averageBpm >= 0` — `RecoveryEvaluator.ComputeSummary` (`BaselineAccumulator.cs:112`) vraća default objekat sa `averageBpm = -1` kad nema nijednog uzorka |

`previousAccuraciesNBack/GoNoGo/Flanker` + `previousSessionCount` postoje u `AdaptationInput`, ali **NOT CONFIRMED da se igdje čitaju** — `AdaptationRuleSet.cs` ih ne referencira. Za Corsi ekvivalent ne postoji. (Nepromijenjeno u v3.)

### Izvedene metrike — `AdaptationRuleSet.cs`

```csharp
HighCost(s,m,c):
  physio       = m.avgBpmDelta > -900 && m.avgBpmDelta >= 18
  subjective   = (s.tlxFrustration >= 0 && >= 60) || (s.tlxTotal >= 0 && >= 65)
  return physio || subjective && SlowRecovery(s,c)   // && veže jače od ||

SlowRecovery(s,c):                                   // v3
  if (!s.recoveryMeasured) return false;             // neizmjereno ≠ sporo
  return !s.recoveryReachedZone || (s.recoverySeconds >= 0 && > 60)

HasCostEvidence(s,m):                                // v3
  physiologyKnown = m.avgBpmDelta > -900
  subjectiveKnown = s.tlxTotal >= 0 && s.tlxFrustration >= 0
  return physiologyKnown && subjectiveKnown

LowArousal(s,m,c):
  delta = m.avgBpmDelta > -900 ? m.avgBpmDelta : s.sessionAvgBpmDelta
  return delta > -900 && delta <= 5

DominantDimension(s,dim,c):
  if (dim < 60) return false
  count = |{ other : other >= 0 && dim >= other + 10 }|
  return count >= 4     // "exceeds at least 4 of the other 5 dimensions"
```

**Sentinel `-900`** razdvaja „nepoznato" (`-999f` default) od stvarne vrijednosti. To je cijeli missing-data mehanizam za HR.

**Uklonjeno u v3:** grana `m.elevatedZoneRatio` iz `HighCost`. Polje se nikad nije popunjavalo i **nije se moglo popuniti** — `elevatedOrHighZoneSeconds` postoji po zadatku, ali per-task aktivno trajanje ne postoji nigdje (`TaskRunner.cs:13-24` `TaskRunnerResult` nema polje trajanja, ni `TaskSessionSummaryData`). Imenilac se ne može izvesti bez nove pretpostavke, pa je polje uklonjeno umjesto izmišljeno. Fiziološka cijena po zadatku mjeri se preko `avgBpmDelta`; ratio ostaje živ samo na nivou sesije, gdje imenilac (`totalActiveSeconds`) postoji.

### B. Task-level adaptation

`AdaptationRuleSet.TaskRules` ln 62-137. Iteracija `AdaptationScheduler.cs:119-127` sa `break` na prvom pogotku — **prioritet je redosljed u listi**.

| # | Rule ID | Uslov (doslovno) | Direktiva | Posljedica po nivo | PROJECT_HEURISTIC |
|---|---|---|---|---|---|
| 1 | `T_STABILITY_REPEAT` | `blockAccuracyStd > 0.18` | RepeatForStability | nepromijenjen | 0.18 |
| 2 | `T_LOW_ACC_LOW_AROUSAL_REPEAT` | `acc ≥ 0 ∧ acc < 0.55 ∧ LowArousal ∧ (tlxTotal < 0 ∨ tlxTotal ≤ 30)` | RepeatForStability | nepromijenjen | 0.55 · 5 · 30 |
| 3 | `T_LOW_ACC_DECREASE` | `acc ≥ 0 ∧ acc < 0.55` | Decrease | −1, ili min-guard | 0.55 |
| 4 | `T_FRUSTRATION_HOLD` | `tlxFrustration ≥ 0 ∧ ≥ 60` | Hold | nepromijenjen | 60 |
| 5 | `T_HIGH_ACC_HIGH_COST_HOLD` | `acc ≥ 0.85 ∧ HighCost` | Hold | nepromijenjen | 0.85 · 18 · 0.5 · 60 · 65 · 60 |
| 6 | `T_TEMPORAL_DOMINANT_HOLD` | `acc ≥ 0.85 ∧ DominantDimension(tlxTemporal)` | Hold | nepromijenjen | 0.85 · 60 · 10 |
| 7 | `T_MENTAL_DOMINANT_MEMORY_HOLD` | `(NBack ∨ CorsiSequence) ∧ acc ≥ 0.85 ∧ DominantDimension(tlxMental)` | Hold | nepromijenjen | 0.85 · 60 · 10 |
| **8** | **`T_INSUFFICIENT_DATA_HOLD`** *(v3)* | `acc ≥ 0.85 ∧ ¬HasCostEvidence(s,m)` | Hold | nepromijenjen | — |
| 9 | `T_HIGH_ACC_INCREASE` | `acc ≥ 0.85` | Increase | +1, ili max-guard | 0.85 |
| 10 | `T_DEFAULT_HOLD` | uvijek istinito | Hold | nepromijenjen | — |

Pravilo 8 je **data-completeness gate**: povećanje težine je opravdano samo kad se cijena te izvedbe može procijeniti. Ako nedostaje task HR agregat ili NASA-TLX, odluka je `Hold` sa eksplicitnim kodom, umjesto da nedostatak tiho isključi zaštitna pravila 4–7 i propusti `Increase`.

**Granice** — `AdaptationScheduler.DecideTask()` ln 129-149:
- `Increase` ∧ `level ≥ maxLevel(3)` → **direktiva se mijenja u `Hold`** + dodatni kod `AT_MAX_LEVEL`
- `Decrease` ∧ `level ≤ minLevel(1)` → **direktiva se mijenja u `RepeatForStability`** + kod `AT_MIN_LEVEL_REPEAT`

Obrati pažnju: kod ne samo da ograničava nivo — **mijenja i zapisanu direktivu**, pa se u podacima vidi `Hold`, ne `Increase`.

Pravila 6 i 7 su semantički obrazložena u zaglavlju (ln 24-25): *„Dominant Temporal Demand blocks further time shortening (all levels shorten time)"* i *„Dominant Mental Demand slows n-back increases."*

### C. Pressure adaptation (mijenja LEVEL, ne stage)

`AdaptationRuleSet.EvaluatePressure()` ln 146-192, izvršava se **poslije svih task odluka**. Kontekst iz `AdaptationScheduler.cs:63-75`:
- `AnyTaskIncreased` = bilo koja od 4 direktive == `Increase`
- `AnyTaskDecreasedOrRepeat` = bilo koja == `Decrease` ∨ `RepeatForStability` (`IsDownOrRepeat`, ln 86-87)

```csharp
highCostGlobal    = (sessionAvgBpmDelta > -900 && >= 18)                  // HR
                 || (totalActiveSeconds > 0 && elevatedOrHighSeconds/totalActiveSeconds >= 0.5)  // HR
                 || (tlxFrustration >= 0 && >= 60)                        // ne-HR
slowRecovery      = SlowRecovery(s,c)                                     // HR, traži recoveryMeasured
nonHrCorroboration = (tlxFrustration >= 0 && >= 60)                       // v3
                  || (tlxTotal >= 0 && >= 65)
                  || ctx.AnyTaskDecreasedOrRepeat
```

| # | Rule ID | Uslov | Direktiva | Posljedica | PROJECT_HEURISTIC |
|---|---|---|---|---|---|
| 1 | `P_TASK_INCREASED_HOLD` | `AnyTaskIncreased` | Hold | nepromijenjen | — |
| 2 | `P_HIGH_COST_DECREASE` | `highCostGlobal ∧ slowRecovery ∧ **nonHrCorroboration**` | Decrease | −1, ili min-guard | 18 · 0.5 · 60 · 65 |
| **3** | **`P_HIGH_COST_HR_ONLY_HOLD`** *(v3)* | `highCostGlobal ∧ slowRecovery` bez ne-HR potvrde | Hold | nepromijenjen | — |
| 4 | `P_HIGH_COST_HOLD` | `highCostGlobal` | Hold | nepromijenjen | 18 · 0.5 · 60 |
| 5 | `P_TASKS_STRUGGLING_HOLD` | `AnyTaskDecreasedOrRepeat` | Hold | nepromijenjen | — |
| 6 | `P_ALL_STABLE_INCREASE` | ∀ zadataka sa `wasSelectedThisSession`: `acc ≥ 0.85` | Increase | +1, ili max-guard | 0.85 |
| 7 | `P_DEFAULT_HOLD` | inače | Hold | nepromijenjen | — |

**Napomena o dvostrukoj ulozi `tlxFrustration`:** prag 60 je istovremeno treći član `highCostGlobal` **i** prvi član `nonHrCorroboration`. Visoka frustracija zato sama zadovoljava i uslov cijene i uslov potvrde — što je konzistentno sa Z2 (frustracija JESTE ne-HR signal), ali znači da se HR članovi mogu izolovati u testu samo preko `tlxTotal`, koji ulazi isključivo u `nonHrCorroboration`.

**Granice** — `ApplyPressure()` ln 153-184: `Increase` na 3 → `Hold` + `AT_MAX_PRESSURE`; `Decrease` na 1 → `Hold` + `AT_MIN_PRESSURE`. Opseg 1–3 (`AdaptationConfig.cs:40-41`) — **P0 ne postoji**.

Pravilo 5 sudi samo po zadacima koji su stvarno izvedeni — `AdaptationRuleSet.cs:180`: *„'All stable' is judged over the tasks that actually RAN this session."*

### Zabrana istovremenog povećanja

`AdaptationRuleSet.cs:149-154`, prvo pravilo pressure tabele:
```csharp
// Rule 1: never raise task and pressure in the same decision.
if (ctx.AnyTaskIncreased) { firedRules.Add("P_TASK_INCREASED_HOLD"); return Hold; }
```
Deklarisano i u zaglavlju klase (ln 19-20) kao **hard constraint #1**. CONFIRMED.

**Ne postoji cooldown ni hysteresis** kao vremenski mehanizam — jedina inercija su min/max granice i `RepeatForStability`.

### Missing-data fallback — zbirno

| Nedostaje | Vrijednost | Efekat (v3) |
|---|---|---|
| `avgBpmDelta` (task HR) | `-999f` | `physio` grana mrtva; `LowArousal` pada na `sessionAvgBpmDelta`; **`HasCostEvidence` false → `T_INSUFFICIENT_DATA_HOLD` blokira `Increase`** |
| `tlxTotal` / `tlxFrustration` | `-1f` | svaki uslov ima `≥ 0` čuvar; **`HasCostEvidence` false → `T_INSUFFICIENT_DATA_HOLD` blokira `Increase`** |
| `accuracy` | `-1f` | `acc ≥ 0` false → pravila 2, 3 preskočena; `acc ≥ 0.85` false → pravila 8, 9 preskočena → `T_DEFAULT_HOLD` |
| `sessionAvgBpmDelta` | `-999f` | `> -900` false → prvi član `highCostGlobal` neaktivan |
| `recoveryMeasured = false` | — | `SlowRecovery()` vraća `false`; neizmjeren oporavak **nije** dokaz sporog oporavka, pa sam po sebi ne može izazvati `Decrease` |
| `totalActiveSeconds = 0` | — | čuvar `> 0` sprječava dijeljenje; ratio član neaktivan |
| zadatak neizabran | — | `NOT_SELECTED_THIS_SESSION`, nivo doslovno zadržan |
| sesija nevalidna | — | `NoDecisionInvalidSession` za sve |

**Invarijanta (v3): nedostajući podaci ne mogu proizvesti ni `Increase` ni `Decrease`.**

U v2 je ova invarijanta bila **netačna** u drugoj polovini: tvrdnja „sve grane vode ka `Hold`" nije važila jer je `T_HIGH_ACC_INCREASE` bio dostupan čim `accuracy ≥ 0.85`, a nedostajući HR/TLX su tiho isključivali zaštitna pravila 4–7 i time povećanje činili **vjerovatnijim** nego sa potpunim podacima. `T_INSUFFICIENT_DATA_HOLD` zatvara taj put, a `recoveryMeasured` zatvara simetričan problem na strani smanjenja.

### D. Explanation / traceability

Tri artefakta po odluci, svi u `AdaptationDecisionData`:

1. **`firedRules`** — `List<string>`. Za zadatke format `"{taskType}:{rule.Id}"` (`AdaptationScheduler.cs:125`); za pressure goli id (`AdaptationRuleSet.cs:152,166,171,176,186,190`). Za nevalidnu sesiju samo `G_SESSION_INVALID` (ln 53).
2. **`inputSnapshotJson`** — `JsonUtility.ToJson(input)`, `AdaptationScheduler.cs:30`. **Cijeli `AdaptationInput` serijalizovan** → svaka odluka je rekonstruktibilna bez pristupa sirovim trialovima.
3. **`humanExplanation`** — `AdaptationExplanationBuilder.Build(decision, input, cfg)` ln 13. Nevalidna sesija → jedna rečenica (ln 17-24); inače `TaskLine(...)` po zadatku + pressure linija. Deklaracija ln 7-10: *„No clinical claims — only what the rules actually saw."* Kompaktna varijanta `BuildCompact(...)` za summary ekran (`ProductionSessionFlow.cs:1462`).

Uz to: `reasonCodes` po zadatku i po pressure odluci (`rule.Id` + eventualni `AT_MAX_LEVEL` / `AT_MIN_LEVEL_REPEAT` / `AT_MAX_PRESSURE` / `AT_MIN_PRESSURE`).

### Primjena na profil

`UserProfileService.CompleteSession()` ln 128-155:
- ln 131-134: **idempotentno** — ponovljeni poziv sa istim `sessionId` izlazi bez ikakve izmjene
- ln 145-153: `currentNBackLevel`, `currentGoNoGoLevel`, `currentFlankerLevel`, `currentPressureLevel` ← `decision.*.newLevel`; Corsi ima dodatni čuvar `newLevel > 0`
- `currentCycleSessionIndex++` i `completedSessionCount++` — **od v4 kroz `CountsTowardCycle(status)`**, pa neupotrebljiva sesija ne troši mjesto u ciklusu (§9.5.1)
- ln 154: `_repository.SaveProfile(profile)`

Ako je `decision == null` (gate 1 pao), blok ln 145-153 se preskače → **nivoi ostaju nepromijenjeni, a sesija se svejedno upisuje u punu istoriju.** Da li će pritom napredovati i brojač ciklusa zavisi isključivo od validnosti, ne od toga da li je odluka postojala.

### Izolacija profila

Fajlovi po `userId` (`sessions/<userId>/<sessionId>/`); scheduler prima isključivo `_profile` aktivne sesije; nema statičkog mutable stanja u `AdaptationScheduler` (konstruktor samo čuva `_cfg`). CONFIRMED.

---

## 9. PERSISTENCE I PROFIL

### 9.1 Raspored — `Persistence/PersistencePaths.cs:20-61`

```
{Application.persistentDataPath}/StressTrainingData/
├─ profiles/
│   ├─ index.json                     ln 31
│   ├─ <userId>.profile.json          ln 32
│   └─ backups/                       ln 30
├─ sessions/<userId>/<sessionId>/     ln 36-37
│   ├─ session.json                   ln 39
│   ├─ trials.jsonl                   ln 41
│   ├─ hr.jsonl                       ln 43
│   ├─ events.jsonl                   ln 45
│   └─ questionnaires.json            ln 47
├─ config/config.json                 ln 50
└─ logs/                              ln 51
```
Na Questu: `/sdcard/Android/data/<package>/files/StressTrainingData/`.
Root je injektabilan (`baseDir`, ln 24-27) — koristi se u testovima.

### 9.2 Modeli

| Model | Format | Fajl | Kada se piše | Čuva se ako je sesija nevalidna? |
|---|---|---|---|---|
| `UserProfileData` | JSON (pretty) | `profiles/<userId>.profile.json` | pri promjeni profila, `CompleteSession` | n/a |
| `SessionSummaryData` v4 | JSON (pretty) | `session.json` | **9+ puta kroz sesiju** (start, poslije reference, recovery, validity, adaptation, save…) | **DA** |
| `SessionPlanData` v4 | JSON, unutar `session.json` | — | pri generisanju plana | DA |
| `TrialRecord` v1 | **JSONL** | `trials.jsonl` | po završetku trial-a | DA |
| `HeartRateSampleRecord` v1 | **JSONL** | `hr.jsonl` | po prihvaćenom uzorku | DA |
| `EventRecord` v1 | **JSONL** | `events.jsonl` | na svaku bitnu odluku | DA |
| `QuestionnaireResultData` v1 | JSON | `questionnaires.json` + u `session.json` | po završetku upitnika | DA |
| `AdaptationDecisionData` v1 | JSON, unutar `session.json` | — | faza 16 | DA (kao `null` ako gate pao) |

### 9.3 Atomic write — `Persistence/AtomicFileWriter.cs:16-45+`

Protokol (ln 7-14): upis u `path.tmp` → prethodni validan fajl se kopira u `path.prev` → `File.Replace(tmp, path, null)`.
Fallback `SafeFallback(tmp, path)` za `PlatformNotSupportedException`, `UnauthorizedAccessException`, `IOException` (ln 35-45).
Garancija ln 12-13: *„A crash at any step leaves either the old valid file, the .prev copy, or both on disk — never a torn write at the target path."*

**Zašto je bitno za rad:** `session.json` se prepisuje devet i više puta tokom sesije; bez atomskog upisa pad aplikacije u bilo kom od tih trenutaka ostavio bi neupotrebljiv zapis.

### 9.4 JSONL — `Persistence/LogWriters.cs:15-117`

`FileMode.Append`, `FileShare.Read`, UTF-8 bez BOM (ln 29-31). Buferovan; `Flush()` se zove poslije bitnih događaja, **ne po frejmu** (ln 17-18: *„never per-frame (Quest performance rule)"*).
Neuspjesi se broje u `FailedWrites` i **nikad ne bacaju u gameplay** (ln 43, 50-52); `Dispose()` je zaštićen (*„disposing must never throw"*, ln 73).
Append-only format znači da prekid sesije ostavlja sve do tada zapisane redove netaknutim — nema polupisanog niza.

### 9.5 Nepotpune i nevalidne sesije

`SessionRepository.DetectAndMarkAbandonedSessions(userId)` ln 75-93, **na boot-u**: sesija bez `endedAtUtcIso` i sa `completionStatus == Unknown` dobija `Abandoned` + `InvalidTechnicalFailure` + notu *„Marked Abandoned at boot: session had no end timestamp."*

Deklarisano ln 12-14: *„Data from unfinished sessions is preserved for audit (spec §6): on boot, sessions without an end timestamp are marked Abandoned, never deleted."*
Isto i `SessionValidityEvaluator.cs:23`: *„An invalid session is NEVER deleted — data stays, scheduler receives the status."*

`LoadSessionSummary` ln 42-57: greška čitanja → `SESSION_LOAD_FAILED`, vraća `null`, ne baca.

### 9.5.1 Semantika ciklusa (v4)

`plannedCycleSessionCount = 5` znači **pet upotrebljivih sesija, ne pet pokušaja**.

`UserProfileService.CompleteSession` je do v4 bezuslovno radio `currentCycleSessionIndex++` i `completedSessionCount++`. Sada oba idu kroz `CountsTowardCycle(status)`, koji propušta samo `Valid` i `ValidWithWarnings` — ista dva statusa koja scheduler smatra upotrebljivim, pa „pet sesija u ciklusu“ i „pet sesija koje su hranile adaptaciju“ ne mogu da se raziđu. Prekinuta, istekla ili nevažeća sesija ostaje u punoj istoriji ali ne troši mjesto.

**Posljedice koje treba znati:**
- `sessionNumberInCycle` se može ponoviti — prekinut pokušaj i njegovo ponavljanje nose isti broj. U analizi razlikovati po `sessionId`.
- `nextRecommendedSessionAtUtcIso` se i dalje pomjera za 48 h i poslije prekida. `EarlyWithWarning` dozvoljava raniji nastavak uz upozorenje. **Nepromijenjeno u v4.**

### 9.5.2 Popravljen zapis ciklusa (v4) — bio stvarni gubitak podataka

`ProfileRepository.CreateProfile` dodjeljuje `activeCycleId` ali **nikad nije upisivao odgovarajući `TrainingCycleSummaryData`**, a `EnsureActiveCycle` je izlazio odmah čim vidi postojeći id. Rezultat: svaki profil je prvi ciklus vodio **bez zapisa u koji bi se pisalo**.

**Posljedica:** `ProductionSessionFlow` upisuje STAI-6 skor ciklusa samo `if (cycle != null)`, pa su **skorovi STAI-6 na nivou ciklusa tiho odbacivani**; isto i `completedSessionCount`. Sirovi odgovori nisu gubljeni — ostaju u `questionnaires.json` i u `session.json` — ali agregat jeste, a on je ujedno i zaštita od dvostrukog administriranja.

**Potvrđeno na stvarnom profilu sa uređaja:** `activeCycleId` postavljen, `cycleSummaries: []`, pet odrađenih sesija.

**Popravka:** `EnsureActiveCycle` sada kreira nedostajući zapis umjesto da izađe, i rekonstruiše `completedSessionCount` iz istorije sesija filtrirane po `cycleId`, brojeći samo upotrebljive. Postojeći profili se liječe pri sljedećem pokretanju, bez restarta ciklusa i bez gubitka mjesta.

### 9.6 Šta Profile UI čita

Kroz `UserProfileService` → `ProfileRepository` → `profiles/index.json` + `<userId>.profile.json`:
`ValidateNewUsername` (`SessionCoordinator.cs:191`), `CreateProfile` (198), `SelectProfile` (205), `CheckEligibility(profile, UtcTime.Now(), out wait)` (227), prikaz `currentCycleSessionIndex + 1 / plannedCycleSessionCount` (240), `ClearActiveProfile` (892).

### 9.7 Export

**Ne postoji.** Nema `adb pull`, ZIP, share intent-a ni bilo kakvog izvoza iz aplikacije. CONFIRMED odsustvom koda.

---

## 10. PROJECT HEURISTIC REGISTER

Sve stavke su radne projektne vrijednosti. **Nijedna se ne smije predstaviti kao naučno validirana univerzalna vrijednost.** Kod ih tako i označava na više mjesta: `StressTrainingConfig.cs:8-9`, `TaskDifficultyConfig.cs:8-11`, `AdaptationConfig.cs:7-9`, `PressureTimeline.cs:26-27`.

| ID | Vrijednost / pravilo | Gdje se koristi | Zašto je potrebna | File / class | Status |
|---|---|---|---|---|---|
| H-01 | breathing reference **300 s** | faza 5 | trajanje referentnog mjerenja pulsa | `StressTrainingConfig.cs:44` `BaselineConfig.durationSeconds` | configurable |
| H-02 | recovery **90 s** | faza 11 | trajanje mjerenja oporavka | `StressTrainingConfig.cs:54` `RecoveryConfig.durationSeconds` | configurable |
| H-03 | udah **4 s** / izdah **6 s**, hold 0 | faza 5 | ritam vođenog disanja | `StressTrainingConfig.cs:75-78` `BreathingGuidanceConfig` | configurable |
| H-04 | recovery zone delta **+8 bpm** | `RecoveryEvaluator` | definicija „vratio se u radnu zonu" | `StressTrainingConfig.cs:55` | configurable |
| H-05 | baseline kvalitet: min **10** validnih uzoraka, ratio **0.5**, gap **15 s** | referenca | određuje `BaselineQuality` | `StressTrainingConfig.cs:45-47` | configurable |
| H-06 | HR zone **+10** / **+22 bpm** iznad reference | `HeartRateZoneEvaluator` | Elevated / High | `StressTrainingConfig.cs:62-63` `HrZoneConfig` | configurable |
| H-07 | stale signal **8 s** | `HeartRateService.Accept` | uzorak se markira `Stale` | `StressTrainingConfig.cs:64` | configurable |
| H-08 | plauzibilnost **30–220 bpm** | parser + service + relej | odbacivanje nemogućih vrijednosti | `StressTrainingConfig.cs:65-66`; `HrItemParser.java` | configurable (Unity) / hard-coded (relej) |
| H-09 | N-back L1/L2/L3 (7 parametara × 3) | task runtime | stepenovanje težine | `TaskDifficultyConfig.cs:124-146` | hard-coded |
| H-10 | Go/No-Go L1/L2/L3 (6 × 3) | task runtime | stepenovanje težine | `TaskDifficultyConfig.cs:148-170` | hard-coded |
| H-11 | Flanker L1/L2/L3 (6 × 3) | task runtime | stepenovanje težine | `TaskDifficultyConfig.cs:172-194` | hard-coded |
| **H-52** *(v4)* | **Flanker distraktorski redovi** 0 / 2 / 2 i gustina 0.00 / 0.55 / 1.00 | `FlankerTaskDefinition.Generate` | vizuelna distrakcija raste sa nivoom, pravilo ostaje isto | `TaskDifficultyConfig.cs` | hard-coded |
| **H-53** *(v4)* | **vertikalni razmak distraktorskih redova 68 px**, alfa **0.45** | `TabletDisplayController` | irelevantni redovi se vide ali se ne mogu zamijeniti sa srednjim | `TabletDisplayController.cs` | hard-coded |
| **H-54** *(v4)* | **`MinRetriggerSeconds = 0.12`** — najkraći razmak između dva puštanja istog cue-a | `PressureAudioController.Play` | sprječava da po-segmentni kolaps reže isti zvuk u stuttering | `PressureAudioController.cs` | hard-coded |
| H-12 | Corsi L1/L2/L3 (8 × 3) + backward na L3 | task runtime | stepenovanje + promjena pravila | `TaskDifficultyConfig.cs:82-122` | hard-coded |
| H-13 | feedback **0.35 s** / Corsi **0.45 s**; Corsi inactivity **30 s** | task runtime | tempo i sigurnosni izlaz | `TaskDifficultyConfig.cs:61-65` | hard-coded |
| H-14 | **pressure STAGE** pragovi **0.70 / 0.50 / 0.30 / 0.10 / 0.00** (preostala frakcija) | `PressureTimeline` | granice unutarsesijske progresije | `PressureTimeline.cs:31-35` | hard-coded |
| H-15 | **pressure LEVEL** intensity cap **0.50 / 0.75 / 1.00** | L1–L3 | gornja granica intenziteta | `PressureTimeline.cs:46-48` | hard-coded |
| H-16 | **pressure LEVEL** event count **3 / 5 / 8** | L1–L3 | broj zakazanih događaja | `PressureTimeline.cs:50-52` | hard-coded |
| H-17 | **pressure LEVEL** shake **0.004 / 0.008 / 0.012 m** | L1–L3 | maksimalna amplituda podrhtavanja | `PressureTimeline.cs:54-56` | hard-coded |
| H-18 | **pressure LEVEL** audio cap **0.40 / 0.70 / 1.00** | L1–L3 | gornja granica glasnoće | `PressureTimeline.cs:58-60` | hard-coded |
| H-19 | **pressure LEVEL** crack alpha **0.35 / 0.60 / 0.85** | L1–L3 | vidljivost pukotina | `PressureTimeline.cs:62-64` | hard-coded |
| H-20 | **pressure LEVEL** duration multiplier **1.00 / 1.00 / 1.00** | L1–L3 | level ne mijenja trajanje | `PressureTimeline.cs:42-44` | hard-coded |
| H-21 | raspored događaja: first ≤ **0.68**, last ≥ **0.04**, magnitude **0.40 + 0.60** | `PressureTimeline` | ograničenja seeded rasporeda | `PressureTimeline.cs:37-40` | hard-coded |
| H-22 | core event prozori **0.52 / 0.49 / 0.32** | `PressureTimeline` | fiksni prototipski cue-ovi | `PressureTimeline.cs:66-68` | hard-coded |
| H-23 | geometrija: 4 para, span **3.60 m**, close **1.40 m** / **6.0 s** | kolaps | fizička dinamika | `PressureTimeline.cs:70-78` | hard-coded |
| H-24 | finale fade **2.50 s**, safety fallback **4.00 s** | `GameOverController` | trajanje posljedice | `PressureTimeline.cs:89-90` | hard-coded |
| H-25 | loss consequence hold **1.6 s** | `PlayerFallController` | da se gubitak osjeti | `StressTrainingConfig.cs:156` | configurable |
| H-26 | fall max distance **3 m** | `PlayerFallController` | tvrdi stop pada | `StressTrainingConfig.cs:151` | configurable |
| H-27 | SSQ validity delta **≥ 20** | `SessionValidityEvaluator` #4 | invalidacija zbog mučnine | `SessionValidityEvaluator.cs:11` | configurable |
| H-28 | HR valid sample ratio **0.30** | `SessionValidityEvaluator` #5/#8 | granica upotrebljivosti HR-a | `SessionValidityEvaluator.cs:12` | configurable |
| H-29 | `hrRequiredForValidity = false` | `SessionValidityEvaluator` | gubitak HR degradira, ne invalidira | `SessionValidityEvaluator.cs:13` | configurable |
| H-30 | max technical warnings **2** | `SessionValidityEvaluator` #8 | granica za „čistu" sesiju | `SessionValidityEvaluator.cs:14` | configurable |
| H-31 | accuracy **0.85** / **0.55** | scheduler task pravila | gornji/donji pojas izvedbe | `AdaptationConfig.cs:16-17` | serialized, ali **nije povezan sa `config.json`** |
| H-32 | frustration **60**, TLX total **65** / **30** | scheduler | subjektivna cijena | `AdaptationConfig.cs:20-22` | isto |
| H-33 | dominantna dimenzija: min **60**, margin **10**, ≥ 4 od 5 | scheduler pravila 6, 7 | prepoznavanje dominantnog opterećenja | `AdaptationConfig.cs:23-24`; `AdaptationRuleSet.cs:49-59` | isto |
| H-34 | session delta BPM **18** (high) / **5** (low) | scheduler | fiziološka cijena / niska pobuđenost | `AdaptationConfig.cs:27-28` | isto |
| H-35 | elevated zone ratio **0.5** | scheduler, **samo na nivou sesije** (`elevatedOrHighSeconds / totalActiveSeconds`) | udio vremena iznad Stable | `AdaptationConfig.cs:29` | isto |
| **H-47** *(v3)* | **ne-HR potvrda obavezna za `Decrease` pritiska**: `tlxFrustration ≥ 60 ∨ tlxTotal ≥ 65 ∨ neki zadatak Decrease/Repeat` | `EvaluatePressure` pravilo 2 | Z2 — HR sam ne smije pomjeriti nivo | `AdaptationRuleSet.cs` | hard-coded pravilo |
| **H-48** *(v3)* | **data-completeness gate**: povećanje traži `avgBpmDelta > −900 ∧ tlxTotal ≥ 0 ∧ tlxFrustration ≥ 0` | `TaskRules` pravilo 8 | nedostatak podataka ne smije olakšati povećanje | `AdaptationRuleSet.HasCostEvidence` | hard-coded pravilo |
| **H-49** *(v3)* | **neizmjeren oporavak ≠ spor oporavak**: `SlowRecovery` traži `recoveryMeasured` | `HighCost`, `EvaluatePressure` | razdvajanje „nema podatka" od „spor oporavak" | `AdaptationRuleSet.SlowRecovery` | hard-coded pravilo |
| **H-50** *(v4)* | **ambijent**: volume cap **0.18**, floor share **0.35** intenziteta | `PressureAudioController` | kontinuirani sloj prisustva prostora između događaja | `PressureAudioController.cs` | hard-coded |
| **H-51** *(v4)* | **ciklus broji samo upotrebljive sesije** (`Valid` ∨ `ValidWithWarnings`) | `UserProfileService.CountsTowardCycle` | „pet sesija” znači pet sesija koje su hranile adaptaciju | `UserProfileService.cs` | hard-coded pravilo |
| H-36 | recovery slow **60 s** | scheduler | granica „sporog oporavka" | `AdaptationConfig.cs:32` | isto |
| H-37 | block accuracy std **0.18** | scheduler pravilo 1 | nestabilnost kroz blokove | `AdaptationConfig.cs:35` | isto |
| H-38 | task nivoi **1–3**, pressure **LEVEL** **1–3** | scheduler granice | opseg adaptacije | `AdaptationConfig.cs:38-41` | isto |
| H-39 | **zabrana istovremenog povećanja** task difficulty i pressure levela | `EvaluatePressure` pravilo 1 | odvajanje dvije dimenzije opterećenja | `AdaptationRuleSet.cs:149-154` | hard-coded pravilo |
| H-40 | **prioritet pravila** (prvo koje odgovara pobjeđuje) | scheduler | determinizam odluke | `AdaptationScheduler.cs:119-127` | hard-coded pravilo |
| H-41 | sentinel **−900** za nepoznat HR | scheduler | missing-data fallback | `AdaptationRuleSet.cs:34,45-46` | hard-coded |
| H-42 | ciklus **5 sesija**, interval **48 h** | `UserProfileService` | struktura treninga | `StressTrainingConfig.cs:19-20` | configurable |
| H-43 | **9 blokova** (3 × 3) | plan generator | struktura sesije | `StressTrainingConfig.cs:34`; zaključano u `ProductionSessionPlanGenerator.cs:34-36` | configurable polje, **ali generator baca ako nije 3** |
| H-44 | pauza između blokova **12 s**, countdown **3 s**, tranzicija **4 s** | tok sesije | tempo | `StressTrainingConfig.cs:23-25` | configurable |
| H-45 | global difficulty time multiplier **1.20 / 1.00 / 0.85** | budžet sesije | težina skraćuje vrijeme | `StressTrainingConfig.cs:31-33` | configurable |
| H-46 | time penalties **isključene**; 2 s / 1 s / 1 s, min remaining 30 s | `TimerPenaltyConfig` | neaktivna mogućnost | `StressTrainingConfig.cs:89-109` | configurable, default `false` |

**Važna napomena uz H-31…H-38:** `AdaptationConfig` je `[Serializable]` i komentar tvrdi da je *„centralized so a researcher can tune them"*, ali `AdaptationScheduler` se instancira **bez argumenta** (`ProductionSessionFlow.cs:1379`: `new Adaptation.AdaptationScheduler()`), a konstruktor koristi `cfg ?? new AdaptationConfig()` (ln 19-22). `AdaptationConfig` nije član `StressTrainingConfig`, pa ga `config/config.json` **ne dohvata**. U praksi se uvijek koriste vrijednosti iz koda. Ovo je razlika između deklarisane namjere i stvarnog ponašanja — zabilježiti u radu ako se tvrdi da su pragovi podesivi bez rekompilacije.

---

## 11. TRACEABILITY PREMA Z1–Z5

Formulacije zahtjeva su kanonske, preuzete iz metodologije.

---

**Z1 — Težina kognitivnog zadatka i intenzitet kontrolisanog pritiska moraju se voditi kao odvojene dimenzije sistema.**

| | |
|---|---|
| **Komponenta** | `AdaptationScheduler`, `AdaptationRuleSet`, `UserProfileData`, `TaskDifficultyConfig` / `PressureLevelConfig` |
| **Mehanizam** | Dva nezavisna skupa nivoa u profilu: `currentNBackLevel` / `currentGoNoGoLevel` / `currentFlankerLevel` / `currentCorsiLevel` (težina zadatka) i `currentPressureLevel` (intenzitet pritiska). Dvije odvojene tabele pravila (9 task pravila, 6 pressure pravila), pressure se evaluira tek poslije svih task odluka. Tvrdo pravilo `P_TASK_INCREASED_HOLD` zabranjuje istovremeno povećanje. Odvojene i konfiguracione tabele: težina zadatka iz `TaskDifficultyConfig`, intenzitet pritiska iz `PressureLevelConfig`. |
| **Dokaz u kodu** | `AdaptationRuleSet.cs:149-154` (zabrana); `AdaptationRuleSet.cs:62-137` vs `:146-192` (odvojene tabele); `AdaptationScheduler.cs:58-80`; `UserProfileService.cs:145-153` (odvojena polja profila); `TaskDifficultyConfig.cs:69-80` vs `PressureTimeline.cs:124` |
| **Kasnija moguća provjera** | Iz niza `session.json` uporediti `schedulerDecision.<task>.directive` i `schedulerDecision.pressure.directive` — `Increase` se nikad ne smije pojaviti u istoj odluci na obje ose |

---

**Z2 — Srčana frekvencija koristi se kao pokazatelj promjene fiziološke pobuđenosti, bez tumačenja BPM-a kao univerzalnog praga psihološkog stresa ili jedinog osnova adaptacije.**

| | |
|---|---|
| **Komponenta** | `HeartRateZoneEvaluator`, `HeartRateService`, `AdaptationInput`, `AdaptationRuleSet` |
| **Mehanizam** | (a) Zone se računaju **isključivo kao razlika prema individualnoj referentnoj vrijednosti** (`bpm − BaselineAvgBpm` vs +10 / +22), nikad kao apsolutni BPM prag; prije postojanja reference zona je `Stable` uz dokumentovano ograničenje. (b) **HR sam nikad ne mijenja nivo.** Na strani zadatka nijedno pravilo se ne oslanja samo na HR (`T_HIGH_ACC_HIGH_COST_HOLD` uvijek traži i tačnost). Na strani pritiska `P_HIGH_COST_DECREASE` od v3 dodatno traži `nonHrCorroboration` — subjektivno opterećenje ili stvarnu poteškoću u zadacima; bez toga ishod je `P_HIGH_COST_HR_ONLY_HOLD`, dakle `Hold`, nikad `Decrease`. (c) HR i dalje zadržava zaštitnu funkciju: može blokirati povećanje i doprinijeti smanjenju. (d) HR nikad ne okida promjenu unutar sesije — ulazi samo kao sesijski agregat. |
| **Dokaz u kodu** | `HeartRateZoneEvaluator.cs:25-38` (relativna klasifikacija); `HeartRateService.cs:278-281` (kapija je „engineering gate, not a psychological-stress assessment"); `AdaptationRuleSet.cs` `HighCost` (HR je jedan uslov u konjunkciji), `EvaluatePressure` (`nonHrCorroboration` + `P_HIGH_COST_HR_ONLY_HOLD`); `AdaptationInput.cs` (HR agregati stoje uz accuracy i TLX) |
| **Testovi** | `SchedulerConsistencyTests.HrOnlyHighCostAndSlowRecovery_HoldsPressure_DoesNotDecrease`, `HrOnlyHighCost_NeverIncreasesPressureEither`, `HighCostCorroboratedByFrustration_DecreasesPressure`, `HighCostCorroboratedByTaskDifficulty_DecreasesPressure` |
| **Kasnija moguća provjera** | Grep na apsolutna BPM poređenja — trenutno postoje samo u `HrPacketParser.cs:171` (plauzibilnost 30/220) i prikaznim uslovima `bpm > 0`; nijedno nije prag stresa. Dodatno: u `schedulerDecision.firedRules` kroz niz sesija provjeriti da `P_HIGH_COST_DECREASE` nikad ne stoji sam bez ne-HR signala u `inputSnapshotJson` |

---

**Z3 — Kontrolisani pritisak treba da se razvija postepeno tokom aktivnog dijela treninga.**

| | |
|---|---|
| **Komponenta** | `PressureTimeline`, `PressureController`, `PuzzleCorridorController` / `PressureSegmentController` (modularna putanja), `CorridorCollapseController` (fallback), `PressureTransientFxController`, `PressureAudioController` |
| **Mehanizam** | **Primarni dokaz je unutarsesijska progresija.** Pritisak nije stanje uključeno/isključeno nego monotona progresija kroz pet faza vezanih za **preostalu frakciju globalnog vremena**: `Stable → Early (0.70) → Mid (0.50) → Late (0.30) → Critical (0.10) → Expired (0.00)`. Progresija se odvija **isključivo tokom aktivnog dijela** — globalni sat ne radi ni u jednoj pripremnoj fazi, a `PressureController` se armira tek pri `EnterCorridor()`. Svaki prelaz faze nosi konkretne promjene: (1) **fizičku** — `_puzzle.ApplyStage(stage)` prosljeđuje fazu svakom segmentu koridora, koji ima sopstveni raspored zatvaranja; fallback putanja zatvara parove pod/plafon progresivno kroz Late+Critical raspon `(0.30 − remaining) / 0.30`; (2) **kontinuiranu vizuelnu** — `ApplyLights()` i podrhtavanje prostora rastu srazmjerno `CurrentIntensity`; (3) **diskretne događaje** — seeded raspored (`Rumble` / `DustBurst` / `CeilingCreak`) raspoređen po `atRemainingFraction` uz ograničenja first ≤ 0.68 i last ≥ 0.04, tako da su događaji raspoređeni kroz cijeli aktivni period a ne skoncentrisani; (4) **jednokratni cue na `Critical`** — zvučno upozorenje. Postepenost je deterministička i reproducibilna jer raspored zavisi samo od `pressureSeed` i levela. **Dodatni, sekundarni mehanizam postepenosti — između sesija:** pressure **level** se mijenja najviše za ±1 po sesiji i ograničen je na 1–3, pa se ni okvir intenziteta ne mijenja skokovito. |
| **Dokaz u kodu** | `PressureTimeline.cs:7-15` (`PressureStage` enum); `PressureTimeline.cs:31-35` (pragovi 0.70/0.50/0.30/0.10/0.00); `PressureTimeline.cs:98-105` (event definicije izvedene iz seeda i levela); `PressureTimeline.cs:37-40` (raspoređenost događaja kroz period); `PressureController.cs:624-637` (stage → modularna ili fallback putanja); `PressureController.cs:634-636` (progresivno zatvaranje kroz Late+Critical); `PressureController.cs:675-686` (`OnStageEntered`, Critical cue); `PressureController.cs:714-734` (kontinuirano osvjetljenje); `PressureController.cs:766-772` (podrhtavanje skalirano intenzitetom); `PuzzleCorridorController.cs:166-187` (`ApplyStage` po segmentu + `FinalCollapseStarted` na Expired); `ProductionSessionFlow.cs:591` (`TickPressure` tokom blokova), `:603` (sat ne radi u pripremi). Sekundarni: `AdaptationScheduler.cs:164-182`, `PressureTimeline.cs:122` |
| **Kasnija moguća provjera** | Iz `events.jsonl` jedne sesije rekonstruisati vremena prelaza faza i uporediti ih sa `remainingGlobalTimeSeconds` u `trials.jsonl` — prelazi moraju pasti na 70/50/30/10 % preostalog vremena i redosljed faza mora biti monoton. Dodatno iz istorije profila provjeriti da `\|pressureLevel[n+1] − pressureLevel[n]\| ≤ 1` |

---

**Z4 — Rezultati završene sesije koriste se pri određivanju konfiguracije naredne sesije.**

| | |
|---|---|
| **Komponenta** | `AdaptationScheduler` → `UserProfileService.CompleteSession` → `ProductionSessionFlow.Start` → `ProductionSessionPlanGenerator` |
| **Mehanizam** | Metrike završene sesije (tačnost, RT, stabilnost kroz blokove, HR agregati, oporavak, TLX) ulaze u `AdaptationInput`; odluka po zadatku i po pressure levelu upisuje se u profil kao `newLevel`. Naredna sesija čita `profile.GetTaskLevel(...)` i `profile.currentPressureLevel` pri generisanju plana. Uz to, izbor zadataka zavisi od prethodne **validne** sesije: izostavljeni zadatak garantovano ulazi. Primjena je idempotentna — isti `sessionId` ne može dvaput pomjeriti nivoe. |
| **Dokaz u kodu** | `ProductionSessionFlow.cs:1394+` (`BuildAdaptationInput`); `UserProfileService.cs:128-155` (upis `newLevel`, idempotencija ln 131-134); `ProductionSessionFlow.cs:418-422` (plan iz `profile.GetTaskLevel` i `profile.currentPressureLevel`), `:437-441` (nivoi u `ActiveSessionContext`); `SeededConstrainedTaskSelector.cs:27-39` + `ResolvePreviousOmitted` |
| **Semantika ciklusa (v4)** | „Pet sesija po ciklusu” znači **pet upotrebljivih sesija**. `CountsTowardCycle(status)` propušta samo `Valid` i `ValidWithWarnings` — ista dva statusa koja otvaraju scheduler gate 2 — pa se brojač ciklusa i skup sesija koje su stvarno pomjerale nivoe ne mogu da se raziđu. Prekinuta sesija ostaje u punoj istoriji, ali ne troši mjesto. Vidi §9.5.1. |
| **Ispravka zapisa (v4)** | Do v4 prvi ciklus svakog profila nije imao `TrainingCycleSummaryData`, pa su STAI-6 skorovi ciklusa tiho odbacivani. `EnsureActiveCycle` sada kreira nedostajući zapis i rekonstruiše brojač iz istorije. Vidi §9.5.2. |
| **Kasnija moguća provjera** | Uporediti `schedulerDecision.*.newLevel` iz sesije *n* sa `pressureLevel` i nivoima zadataka zabilježenim u `session.json` sesije *n+1* — moraju se poklapati. Odvojeno: `cycleSummaries[*].completedSessionCount` mora biti jednak broju sesija tog ciklusa sa upotrebljivim statusom |

---

**Z5 — Adaptivna odluka treba da koristi više vrsta podataka, pri čemu fiziološki odgovor, uspješnost u zadatku i subjektivna procjena imaju različite uloge.**

| | |
|---|---|
| **Komponenta** | `AdaptationInput`, `AdaptationRuleSet`, `SessionValidityEvaluator` |
| **Mehanizam** | Tri tipa podataka ulaze u odluku sa **različitim ulogama, ne kao zbir**: (1) **uspješnost u zadatku** (`accuracy`, `blockAccuracyStd`) je **primarni okidač smjera** — pravila 1, 2, 3, 8 se oslanjaju na nju; (2) **fiziološki odgovor** (`avgBpmDelta`, `elevatedZoneRatio`, oporavak) djeluje kao **ograničenje naviše** — ulazi u `HighCost`, koji dobru izvedbu pretvara iz `Increase` u `Hold` (pravilo 5) i snižava pressure level (pravilo P2); (3) **subjektivna procjena** (NASA-TLX, 6 dimenzija) djeluje kao **zaštita i profil opterećenja** — frustracija ≥ 60 zaustavlja povećanje (pravilo 4), a dominantna Temporal / Mental dimenzija selektivno blokira povećanja koja bi pogoršala baš tu vrstu opterećenja (pravila 6, 7). Nijedan tip podataka sam ne može proizvesti povećanje. |
| **Dokaz u kodu** | `AdaptationInput.cs` (tri grupe polja); `AdaptationRuleSet.cs` `HighCost` (kombinuje fiziologiju i subjektivnu procjenu), `TaskRules` (pravila 1-3, 9 iz izvedbe; 4 iz frustracije; 5 iz cijene; 6-7 iz dominantne dimenzije; **8 `HasCostEvidence` traži prisustvo i fiziološkog i subjektivnog podatka prije povećanja**) |
| **Testovi** | `HighAccuracyWithMissingTaskHeartRate_HoldsTaskLevel`, `HighAccuracyWithMissingNasaTlx_HoldsTaskLevel`, `HighAccuracyWithCompleteLowCostData_StillIncreases` |
| **Kasnija moguća provjera** | Iz `schedulerDecision.firedRules` kroz niz sesija provjeriti da su aktivirana pravila iz sve tri grupe, a ne samo iz jedne |

> **Napomena uz Z5 — podaci koji NISU dio adaptivne odluke.** SSQ ulazi isključivo u validity/safety sloj (`SessionValidityEvaluator` pravilo 4) i nema polja u `AdaptationInput`. STAI-6 se čuva na nivou ciklusa (`cycleSummaries[*].staiScoreCycleStart` / `staiScoreCycleEnd`) i takođe ne ulazi u scheduler. Od upitnika samo NASA-TLX učestvuje u odluci. Ta razdvojenost uloga je dio Z5, ali se ne smije predstaviti kao da SSQ ili STAI utiču na adaptaciju.

---

## 12. NE SMIJE U RAD KAO TRENUTNA ČINJENICA

| Zastarjela tvrdnja | Izvor | Stvarno stanje | Status |
|---|---|---|---|
| „tri zadatka" | stariji dokumenti | **Četiri** implementirana (`NBack`, `GoNoGo`, `Flanker`, `CorsiSequence`); **tri se biraju po sesiji** — razlikovati implementirano od izabranog | STALE |
| „15 blokova" | stariji planovi | **9** (3 runde × 3 zadatka), tvrdo zaključano izuzetkom | STALE |
| „2 zadatka: Button Sequence + Dial/Slider Calibration" | `CLAUDE.md` MVP Scope | Te klase **ne postoje** u kodu | STALE |
| tabela „10 task tipova" | `CLAUDE.md` | Nijedan od tih naziva ne postoji | STALE |
| „resting baseline" / „neutralno mjerenje u mirovanju" | opšta pretpostavka | **Breathing-assisted reference** — učesnik namjerno diše po vođenom ritmu tokom mjerenja | STALE |
| „baseline 120 s" | `SCIENTIFIC_TRACEABILITY.md:30` | **300 s** | STALE |
| „ADB + Python je HR pipeline" | `CLAUDE.md` dijagram | Produkcijski put je telefon → UDP → `NetworkHeartRateSource`. `hr_dashboard_v2.py` je **dev fallback** | STALE |
| „`HRReceiver.cs` prima HR u Unity" | `CLAUDE.md` | `HRReceiver` ima **0 pominjanja u `MainScene.unity`** — nije zakačen ni na jedan GameObject | STALE |
| „pressure P0" ili nivo 0 | pretpostavka | Pressure **level** opseg je **1–3**; `ClampLevel` forsira minimum 1 | STALE |
| **„pressure level i pressure stage su isto"** | pretpostavka | Dvije nezavisne ose — level je međusesijski intenzitet (1–3), stage je unutarsesijska faza (`Stable`…`Expired`). Vidi §5.0 | STALE |
| „kavez sa vratima koja se otvaraju" | `CLAUDE.md` Scenario | Pritisak je **kolaps modularnog koridora** (parovi pod/plafon). Nema mehanike otvaranja vrata u pressure kodu. `Cage_Placeholder` postoji u sceni kao **legacy geometrija** | STALE |
| `TaskDefinition`, `UserStressProfile`, `TaskResult`, `StressMetrics`, `ITaskScheduler`, `RuleBasedTaskScheduler`, `TaskManager`, `ConsoleGridManager`, `ThreatController`, `SessionDebriefManager` | `CLAUDE.md` „Predložena arhitektura" | **Svih 10 ne postoji.** Ekvivalenti: `AdaptationScheduler`, `TaskRunner`, `ConsoleLayoutBuilder`, `PressureController`, `UserProfileData`, `TrialRecord` | STALE |
| `GameManager`, `PuzzleManager`, `PuzzleButton`, `StressRoomController`, `DebriefingManager`, `HRDisplay` „ostaju / evoluiraju" | `CLAUDE.md` mapa skripti | Fajlovi postoje, **0 pominjanja u sceni** za sve → mrtav kod | STALE |
| „`CopingPreparation` je faza toka" | enum vrijednost 6 | Sam kod je označava: *„Legacy: breathing was a separate stage before 2026-07-14. **Never entered now**"*; nema `SetStage` poziva | STALE |
| **„`ScheduleEligibility` je faza produkcijskog toka"** | enum vrijednost 1 | Nijedan `SetStage(ProductionFlowStage.ScheduleEligibility)` poziv; provjera termina se izvodi u `SessionCoordinator.cs:227-249` prije ulaska u flow | NOT CONFIRMED kao dostižna |
| **„sesija prolazi kroz 19 faza"** | broj vrijednosti enuma | Enum ima **20 vrijednosti (0–19)**; **17** se stvarno postavlja; `None` je reset stanje, `ScheduleEligibility` i `CopingPreparation` nisu u aktivnom toku | STALE (ispravljeno u v2) |
| „upitnici su placeholder info ekrani" | `ProductionSessionFlow.cs:53-57` | Koristi se stvarni `QuestionnaireFlowController` + `QuestionnaireCatalog` + `QuestionnaireScoringService`. Komentar je zastario | STALE |
| „scheduler pragovi su podesivi kroz config.json" | `AdaptationConfig.cs:9-10` | `AdaptationConfig` nije dio `StressTrainingConfig`; scheduler se instancira bez konfiguracije → koriste se code defaults | STALE |
| „`LightFlicker` je pressure događaj" | `PressureEventKind` enum | `case PressureEventKind.LightFlicker: break;` — **prazna implementacija**. Deklarisana ali neimplementirana funkcija | STALE |
| **„`Rumble` je haptički/vibracioni cue"** | naziv enum vrijednosti | `FireEvent` case `Rumble` poziva **samo** `_audio.PlayRumble(...)`. **Nema haptike ni vibracije u pressure sistemu** — grep na `Haptic`/`Vibrat` daje pogotke samo u `Console/ConsoleControlBase.cs:42-47` (poke feedback konzole, nepovezano) | STALE |
| **„na `Critical` se prikazuje critical void"** | `SetCriticalVoidVisible` poziv + konstanta `CriticalVoidBehindDistanceMeters` | Metoda je **no-op**: `PressureTransientFxController.cs:166` → `{ _ = visible; }`, komentar *„No-op… why the cube is gone"*. Jedini stvarni efekat ulaska u `Critical` je zvučno upozorenje | STALE (ispravljeno u v2) |
| „MainScene sadrži samo Cube i Plane; nedostaje hodnik i konzola" | `CLAUDE.md` Scene stanje | Scena sadrži `Corridor_Modular_Puzzle_ROOT`, `Console_BlenderPrototype`, robotsku ruku, zidove, osvjetljenje; 822 `PrefabInstance` unosa | STALE |
| „nema `_Recovery`" | `CLAUDE.md` Repo stanje | `Assets/_Recovery/` sadrži **9 `.unity` fajlova** | STALE |
| „FAZA 0 je trenutna faza; scheduler nije implementiran" | `CLAUDE.md` Status | Faze 1–7 implementirane; `Adaptation/` 675 linija | STALE |
| „pressure level mijenja trajanje sesije" | pretpostavka | Sva tri `DurationMultiplier` = **1.00**; trajanje zavisi samo od task difficulty multiplikatora | STALE |
| **„`sessionAvgBpmDelta` i `elevatedZoneRatio` su mrtva polja"** | v2 §8 | **Riješeno u v3.** `sessionAvgBpmDelta` se sada popunjava iz `HeartRateService.SessionAverageBpm`; `elevatedZoneRatio` je uklonjen jer per-task trajanje ne postoji ni u `TaskRunnerResult` ni u `TaskSessionSummaryData` | ISPRAVLJENO |
| **„nedostajući podaci ne mogu proizvesti `Increase`; sve grane vode ka `Hold`"** | v2 §8 | Druga polovina je bila **netačna** do v3 — `T_HIGH_ACC_INCREASE` je prolazio čim `accuracy ≥ 0.85`, a nedostatak HR/TLX je isključivao zaštite 4–7. `T_INSUFFICIENT_DATA_HOLD` to zatvara | ISPRAVLJENO |
| **„Z2 je u potpunosti zadovoljen"** | v2 §11 | Do v3 **djelimično** — `P_HIGH_COST_DECREASE` je dozvoljavao HR-only smanjenje. Sada traži `nonHrCorroboration` | ISPRAVLJENO |
| **„Recovery se izvršava za svaku sesiju koja je ušla u aktivni dio"** | v3 §3.1 i §3.3 | Važilo do v4. Od v4 dobrovoljni prekid ide `StraightToValidity` — bez Recovery-ja i bez ijednog post-upitnika. `TimeExpired` i dalje prolazi puni put | ISPRAVLJENO (v4) |
| **„`currentCycleSessionIndex` raste poslije svake završene sesije"** | v3 §8 | Važilo do v4 i bilo je pogrešno: prekinut pokušaj je trošio mjesto u ciklusu od pet. Sada raste samo za upotrebljive sesije | ISPRAVLJENO (v4) |
| **„STAI-6 skor ciklusa se pouzdano čuva"** | v3 §9 | Netačno do v4 — profili kreirani prije popravke nisu imali `TrainingCycleSummaryData`, pa je agregat tiho odbacivan (sirovi odgovori nisu). Potvrđeno na uređaju: `cycleSummaries: []` uz pet sesija | ISPRAVLJENO (v4) |
| **„pressure zvuk postoji samo u četiri zakazana događaja"** | v3 §5.4 | Važilo do v4. Dodat je kontinuirani ambijentalni sloj koji svira kroz cio aktivni dio i čija glasnoća prati intenzitet (H-50) | ISPRAVLJENO (v4) |
| **„Flanker je uvijek jedan red od pet strelica"** | v3 §4.2 | Važilo do v4. Od v4: L1 jedan red, L2 i L3 tri reda (gornji i donji su čisto vizuelni). Pravilo, proporcije i vremena nepromijenjeni | ISPRAVLJENO (v4) |
| **„pressure cue ne može sam sebe da prekine"** | pretpostavka | Mogao je do v4 — jedan izvor po cue-u plus `Stop()` prije `Play()` znači da brže okidanje reže prethodni zvuk. Zatvoreno sa `MinRetriggerSeconds` | ISPRAVLJENO (v4) |
| **„per-task elevated-zone ratio postoji"** | naziv polja | Ne postoji i ne može se izvesti: `elevatedOrHighZoneSeconds` se bilježi po zadatku, ali per-task aktivno trajanje nigdje | STALE |

---

## 13. OTVORENA PITANJA

Samo stvari koje **ne mogu** potvrditi statičkom analizom trenutnog repozitorijuma.

| # | Pitanje | Šta pregledati / pokrenuti |
|---|---|---|
| 1 | **ZATVORENO za EditMode (v4).** Pun EditMode suite je pokrenut u Test Runneru i **prolazi u cjelini**, uključujući `SchedulerConsistencyTests` (21), `SessionAbortFlowTests` (19) i `FlankerDistractorRowTests` (29). PlayMode suite **i dalje nije pokrenut** | Unity → Test Runner → **PlayMode → Run All**; sačuvati izvještaj |
| 2 | Da li se `previousAccuraciesNBack/GoNoGo/Flanker` i `previousSessionCount` (`AdaptationInput.cs:66-69`) igdje čitaju? U `AdaptationRuleSet.cs` nema referenci | Grep po cijelom rješenju; ako se ne čitaju, to je mrtvo polje u serijalizovanom snapshotu i treba tako i opisati |
| 3 | Postoji li zaseban „ready countdown" prije prvog bloka, odvojen od `resumeCountdownSeconds` (pauza) i `transitionToCorridorSeconds`? | Pročitati `EnterReady()` (`ProductionSessionFlow.cs:980-996`) i `EnterCorridor()` (ln 997-1055) u cjelini |
| 4 | Koji su stvarni tekstovi NASA-TLX i STAI-6 stavki i koje su `reverseScored`? Pročitao sam samo SSQ stavke | `QuestionnaireCatalog.cs:73-141` u cjelini |
| 5 | Šta `PressureSegmentController.ApplyStage(stage)` konkretno radi po fazi (1163 linije)? Potvrđeno je da postoji, da se poziva iz `PuzzleCorridorController.ApplyStage` i da segmenti imaju sopstveni raspored — ali ne i tačne animacione faze i pomjeraji po segmentu. **Ovo je jedini preostali dio Z3 lanca koji nije opisan do nivoa pojedinačnog segmenta** | `Pressure/PuzzleSegmentController.cs` + `PuzzleCollapseAnimationProfile.cs` (`PuzzleAnimationPhase`, `PuzzleAnimationEventKind`, `WaveSettings`) |
| 6 | Koliko traje jedna produkcijska sesija u praksi? `nominalPlanSeconds` se računa iz plana, ali stvarna vrijednost zavisi od izabranih zadataka i nivoa. Bez toga se ne može izraziti ni koliko realnog vremena traje svaka pressure faza | Pokrenuti `ProductionSessionPlanGenerator.Generate` sa realnim ulazima ili očitati `nominalPlanSeconds` iz postojećeg `session.json` |
| 7 | **Djelimično zatvoreno (v4).** Posljednji APK je izgrađen **2026-09-12 17:10** (`RESULT=Succeeded`, 0 grešaka, 10:05, 91.047.131 B) i instaliran preko postojeće verzije (`lastUpdateTime 2026-09-12 17:10:55`, `firstInstallTime` iz jula očuvan → nadogradnja na licu mjesta, podaci sačuvani). Sadrži i Flanker izmjenu i audio korekciju. **Funkcionalna sesija na uređaju još nije odrađena** — build se kompajlira i instalira, ali nije poznato da li ambijentalni sloj i nova grana prekida rade u headsetu | Pokrenuti aplikaciju na Questu, čuti ambijent u koridoru, prekinuti jednu sesiju iz aktivnog dijela i provjeriti da `events.jsonl` sadrži `user_abort_fast_exit` i da nema post-upitnika |
| 8 | Radi li HR relej sa trenutnim Mi Fitness formatom u realnim uslovima? Izmjene u `android_companion/HrRelay/` su **necommitovane** i testirane samo jediničnim testovima + snimljenim logom | Pokrenuti relej uz aktivan sat i Quest; potvrditi da `hr.jsonl` sadrži uzorke kroz cijelu sesiju |
| 9 | **ZATVORENO (v3).** `config/config.json` je pročitan sa Questa i **ne sadrži nijedan override** — sve vrijednosti su identične code defaults (baseline 300 s, recovery 90 s, disanje 4/6, zone +10/+22, stale 8 s, 30–220, 3 bloka, ciklus 5/48 h, time penalties `false`) | — |

---

---

## 14. VERIFIKACIJA NA UREĐAJU (v3, dopunjeno u v4)

Quest 3 (`2G0YC1ZG6903S0`), paket `com.DefaultCompany.VR_StressTraining`. Ništa nije mijenjano na uređaju — samo čitanje.

| Provjera | Rezultat |
|---|---|
| `config/config.json` | Postoji; **nijedan override** — sve identično code defaults |
| Sesije na uređaju | 11; jedna kompletna (`3a98219e…`, **2026-09-11**) |
| Format zapisa | `schemaVersion: 4`; `session.json` + `.prev` uz svaki fajl → atomic write potvrđen na stvarnim podacima |
| `recovery.averageBpm` | `90.98` → potvrđuje da je `recoveryMeasured` marker ispravan na stvarnim podacima |
| `baseline` | `averageBpm 92.4`, `validSampleRatio 1.0`, `signalGapCount 0`, `durationSeconds 300.01` |
| `elevatedOrHighZoneSeconds` | `0.0` na sva tri zadatka — **korektno**, ne greška: `avgBpmDuringTask` 95–97 vs referenca 92.4 → delta +3…+5, ispod praga +10 |
| `avgBpmDuringTask` | popunjen (95.7 / 97.3 / 95.2) |
| Scheduler gate 2 | Sesija `IncompleteUserTerminated` → `G_SESSION_INVALID`, svi nivoi zamrznuti, `SESSION_NOT_USABLE_IncompleteUserTerminated` — **potvrđeno na stvarnoj sesiji** |
| **Produkcijski HR pipeline** | **351 uzoraka; `sourceType: 2` = `NetworkBridge`; `qualityStatus: 1` = `Good` za sve; BPM 78–112 (prosjek 92.2); svi sa `sequence` i `sourceTimestampUtcIso` → protocol-v1 relay paketi** |

Mrežni BPM stvarno stiže u produkcijski tok — potvrđeno bez izvođenja nove sesije.

**Dopuna v4 — build i instalacija (2026-09-12).** Dva Android builda istog dana, oba sinhrono preko Coplay `execute_script`, oba `RESULT=Succeeded` sa 0 grešaka:

| Vrijeme | Trajanje | APK | Sadržaj |
|---|---|---|---|
| 11:46 | 30:23 | 90.641.354 B | prvi build poslije 20. jula — najveći dio vremena otišao na hladan import shader-graphova |
| **17:10** | **10:05** | **91.047.131 B** | dodatno Flanker progresija kroz nivoe i audio retrigger zaštita |

Drugi build je trostruko brži jer je shader keš već bio topao. `adb install -r` je prošao (`Success`); `dumpsys` potvrđuje `lastUpdateTime 2026-09-12 17:10:55` uz zadržan `firstInstallTime 2026-07-20`, dakle nadogradnja u mjestu bez brisanja podataka.

**Šta to jeste dokaz:** da se cio projekat sa v4 izmjenama (grana prekida, brojanje ciklusa, ambijentalni sloj, tekstura pijeska, novi glasovni cue-ovi) uspješno kompajlira za Android/IL2CPP i pakuje u instalabilan APK.
**Šta nije:** nijedna sesija nije odrađena na uređaju poslije ovog builda. Ponašanje u headsetu ostaje neprovjereno — v. otvoreno pitanje 7.

**Napomena:** `accuracyStdAcrossBlocks = 0.000` na sva tri zadatka jer je sesija prekinuta prije nego što je više blokova po zadatku odrađeno. Pravilo `T_STABILITY_REPEAT` zato još nije viđeno na stvarnim podacima.

---

### Granice ovog dokumenta

Statička analiza izvora na dan **2026-09-12**, HEAD `v1.0-thesis` + necommitovane izmjene.

Šta jeste izvršeno: Unity kompajliranje (0 `error CS`), **pun EditMode suite** u Test Runneru (prolazi u cjelini), čitanje konfiguracije i podataka sa povezanog Quest uređaja, i **Android build + instalacija na uređaj** (2026-09-12).

Šta nije: PlayMode suite i nova eksperimentalna sesija u headsetu — build se instalira, ali nijedna sesija nije odrađena poslije njega. Android relej je necommitovan (10 izmijenjenih fajlova + `LanBroadcast.java` nepraćen); tvrdnje o Quest strani izvedene su iz Unity koda i podataka na uređaju, ne iz tog stanja.

---

## V4 CHANGELOG

Promjena toka zatvaranja sesije, popravka jednog stvarnog gubitka podataka, ambijentalni sloj i Flanker progresija. **Pun EditMode suite prolazi.**

- **Dobrovoljni prekid preskače post-mjerenja.** Nova `SessionEndPolicy.PathFor(preCorridor, voluntaryAbort)` sa tri puta; prekid iz aktivnog dijela ide `StraightToValidity` — bez Recovery-ja, post-SSQ, NASA-TLX i PostCycle STAI. Razlog: sesija je već neupotrebljiva po `SessionValidityEvaluator` pravilima 4 i 7, pa bi se ta mjerenja prikupljala pod nelagodom i zatim odbacila. `TimeExpired` **nije** prekid i zadržava puni put. Odluka je izdvojena u čistu funkciju jer se `ProductionSessionFlow` ne može konstruisati u EditMode testu. §3.1, §3.3, §3.3.1
- **Ciklus broji upotrebljive sesije, ne pokušaje.** `UserProfileService.CountsTowardCycle(status)` — `currentCycleSessionIndex` i `completedSessionCount` rastu samo za `Valid` / `ValidWithWarnings`. Posljedica koju treba znati pri analizi: `sessionNumberInCycle` se može ponoviti. §9.5.1, H-51
- **Popravljen gubitak STAI-6 skorova ciklusa.** `ProfileRepository.CreateProfile` je dodjeljivao `activeCycleId` bez odgovarajućeg `TrainingCycleSummaryData`, a `EnsureActiveCycle` je izlazio odmah — pa je `if (cycle != null)` tiho odbacivao agregat. Potvrđeno na stvarnom profilu sa uređaja (`cycleSummaries: []` uz pet sesija). `EnsureActiveCycle` sada kreira zapis koji nedostaje i rekonstruiše brojač iz istorije, pa se postojeći profili liječe bez restarta ciklusa. Sirovi odgovori nikad nisu bili izgubljeni. §9.5.2
- **Ambijentalni sloj koridora.** `CorridorAmbience.wav`, besprekidni loop od 6 s, svira kroz cio aktivni dio; glasnoća prati `CurrentIntensity` između `AmbienceFloorShare = 0.35` i `AmbienceVolumeCap = 0.18`, skalirano `audioRampCap`-om nivoa. Do sada je koridor između četiri zakazana događaja bio potpuno tih. §5.4, H-50
- **Testovi.** Novi `SessionAbortFlowTests.cs` (19 testova) nad `SessionEndPolicy` i `CountsTowardCycle`. Dva su pala na prvom prolazu i time otkrila gornji bug sa `cycleSummaries` — nije bila greška u testu
- **Nov Quest build.** Dva builda 2026-09-12, oba uspješna; posljednji u **17:10** (10:05, 91.047.131 B) nosi i Flanker izmjenu i audio korekciju, instaliran preko postojeće verzije bez gubitka podataka na uređaju. §14
- **Flanker dobio vizuelnu progresiju kroz nivoe.** L1 ostaje jedan red; L2 i L3 prikazuju tri reda, gdje su gornji i donji čisto vizuelni distraktori generisani iz istog seeded RNG-a. Relevantan je uvijek **srednji** red, i samo njegova centralna strelica određuje `expectedAction`, congruency i scoring. Dodata su dva parametra težine (`flankerDistractorRows`, `flankerDistractorDensity`, oba PROJECT_HEURISTIC); **nijedan postojeći broj nije promijenjen** — proporcije 35/50/60 % incongruent i 15/10/0 % neutral i sva vremena su ista. L1 troši isti broj slučajnih poteza kao prije, pa su mu stimulusi identični. Uputstvo ide kroz **postojeći** task+level tutorial sistem. §4.2, H-52, H-53
- **Pressure cue više ne reže sam sebe.** `MinRetriggerSeconds = 0.12` — tokom kolapsa su po-segmentni događaji retriggerovali ista dva izvora brže nego što klip traje, pa je škripa postajala stuttering. Glasnoće, broj zvukova i kontinuirani ambijent nisu dirani. §5.4, H-54
- **Otvoreno pitanje 1 zatvoreno za EditMode.** PlayMode suite i dalje nije pokrenut; pitanje 7 je djelimično zatvoreno — build se instalira, sesija u headsetu još nije odrađena
- **Zadržane invarijante.** Sve iz V3 changeloga važe nepromijenjeno; prekinuta sesija se i dalje u cjelini zapisuje, samo ne troši mjesto u ciklusu

---

## V3 CHANGELOG

Četiri ciljane izmjene schedulera, bez refaktora. Kompajlirano; `SchedulerConsistencyTests` 21/21 prolazi.

- **Z2 — HR sam više ne mijenja nivo.** `P_HIGH_COST_DECREASE` dobio uslov `nonHrCorroboration` (`tlxFrustration ≥ 60 ∨ tlxTotal ≥ 65 ∨ neki zadatak Decrease/Repeat`). HR-only visoka cijena sada završava u novom `P_HIGH_COST_HR_ONLY_HOLD` → `Hold`. Zaštitna funkcija HR-a ostaje: i dalje blokira povećanje i doprinosi smanjenju. §11 Z2: `partially satisfied` → **`fully satisfied`**
- **Data-completeness gate.** Novo pravilo `T_INSUFFICIENT_DATA_HOLD` (prioritet 8, ispred `T_HIGH_ACC_INCREASE`): povećanje težine traži i task HR agregat i NASA-TLX. Ispravlja netačnu v2 tvrdnju da nedostajući podaci ne mogu otvoriti put ka povećanju
- **`sessionAvgBpmDelta` više nije mrtav.** Popunjava se iz `HeartRateService.SessionAverageBpm − baselineBpm`, samo kad su obje vrijednosti `> 0`
- **`elevatedZoneRatio` uklonjen umjesto izmišljen.** Per-task aktivno trajanje ne postoji ni u `TaskRunnerResult` ni u `TaskSessionSummaryData`, pa imenilac nije izvodljiv. Sesijski ratio, čiji imenilac postoji, ostaje u upotrebi
- **Neizmjeren oporavak ≠ spor oporavak.** Novo `recoveryMeasured` polje i `SlowRecovery()` helper; nedostatak recovery HR podataka više ne doprinosi smanjenju
- **Testovi.** Novi `SchedulerConsistencyTests.cs`, 21 test: HR-only Hold, potkrijepljeni Decrease, missing HR/TLX Hold, potpuni podaci Increase, neizmjeren oporavak, oba HR ulaza živa, task+pressure nikad zajedno, plus invarijante (invalid session, granice 1–3, determinizam, traceability)
- **Quest verifikacija.** Nova §14: konfiguracija bez override-a, format podataka potvrđen, produkcijski HR pipeline prima mrežni BPM (351 uzorak, `NetworkBridge`, svi `Good`)
- **Zadržane invarijante.** Scheduler samo između sesija; task i pressure odvojeni i nikad se ne povećavaju zajedno; nivoi 1–3; neizabrani zadatak zadržava nivo; invalidna sesija ne prilagođava nivoe; `firedRules`, `reasonCodes`, `inputSnapshotJson` i human explanation rade; nevalidne i nepotpune sesije se čuvaju

---

## V2 CHANGELOG

- **corrected Z3 traceability** — glavni dokaz Z3 je sada unutarsesijska progresija pressure **stage-a** (`Stable → Early → Mid → Late → Critical → Expired` po preostaloj frakciji 0.70/0.50/0.30/0.10/0.00) uz fizičke, kontinuirane vizuelne i diskretne događajne promjene koridora; međusesijska ±1 promjena pressure **levela** zadržana je kao sekundarni mehanizam
- **clarified active flow vs enum values** — `ProductionFlowStage` ima **20 vrijednosti (0–19)**, ne 19; **17** se stvarno postavlja; `None` je reset stanje, `CopingPreparation` je legacy, `ScheduleEligibility` je NOT CONFIRMED kao dostižna (nema `SetStage` poziva). §1 i §3 više ne sugerišu „19 faza sesije"
- **clarified Recovery semantics** — Recovery je obavezan završni korak mjerenja za svaku sesiju koja je ušla u aktivni dio; preskače se isključivo kod prekida prije ulaska u koridor (`preCorridor == true`), uz zadržan kodski uslov
- **corrected pressure event categorization** — `Rumble` = **zvučni cue** (samo `PlayRumble`), `DustBurst` i `CeilingCreak` = **vizuelni efekat + prateći zvuk**, `LightFlicker` = neimplementiran; kontinuirano osvjetljenje (`ApplyLights`), podrhtavanje i kolaps geometrije razdvojeni kao zasebni mehanizmi
- **explicitly separated pressure level vs pressure stage** — dodata terminološka konvencija na vrhu dokumenta i §5.0; razdvojeno kroz §1, §4.3, §5, §8, §10 (H-14…H-20, H-38, H-39) i §12
- **synchronized Z1–Z5 wording with methodology** — sve formulacije zahtjeva preuzete doslovno; Z2 dopunjen dokazom da HR nije jedini osnov adaptacije; Z5 preformulisan oko različitih uloga tri tipa podataka, sa SSQ/STAI napomenom izdvojenom ispod glavnog reda

**Dodatno ispravljeno tokom provjere (kontradikcije zatečene u v1):**

- **`Rumble` nije haptika** — metodološki audit je predložio klasifikaciju `Rumble` = haptika/vibracija; kod je ne podržava. Provjera grep-om (`Haptic`, `Vibrat`, `SetControllerVibration`) daje pogotke samo u `Console/ConsoleControlBase.cs:42-47` (Meta `FeedbackManager` za poke interakciju konzole), potpuno nepovezano sa pressure sistemom. Primijenjena je tačna klasifikacija; netačna tvrdnja upisana u §12
- **`SetCriticalVoidVisible` je no-op** — v1 je u §5.2 naveo prikaz „critical void" pri ulasku u `Critical`. `PressureTransientFxController.cs:166` → `{ _ = visible; }`, uz komentar *„No-op… why the cube is gone"*. Jedini stvarni efekat ulaska u `Critical` je jednokratno zvučno upozorenje; konstanta `CriticalVoidBehindDistanceMeters = 2.80` je zaostala bez efekta
- **broj enum vrijednosti** — v1 je naveo 19; stvarno ih je 20 (0–19)
- **shake potvrđen kao stvarno primijenjen** — `PressureController.cs:766-772` pomjera `_pressureRoot.localPosition`, amplituda = `level.shakeAmplitudeMeters × Clamp01(CurrentIntensity + _eventPulse)`; v1 je vrijednost naveo u tabeli bez potvrde da se koristi
