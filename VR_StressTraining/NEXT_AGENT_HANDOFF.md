# NEXT_AGENT_HANDOFF.md

Posljednje ažuriranje: 2026-07-15 — ART/UX PROLAZ (puzzle hodnik + aurora + UiTheme).

## ART/UX PROLAZ (najnovije)

### Puzzle hodnik + collapse (Prioritet 1)
- FBX `Assets/Art/PuzzleCorridor/PuzzleCorridor.fbx`. Hijerarhija (parsirana iz binarnog
  FBX-a): `Corridor_Modular_Puzzle_ROOT` → `Segment_S1..S6` + `Segment_Safe`, svaki
  Floor/Ceiling/Wall_L/Wall_R + CollapsePivot/FXAnchor/LightAnchor. Pokriva Z −5..+5 (10 m),
  ~3 m široko — **identično postojećem hodniku**. `Segment_Safe` (Z −5..−2.5) = konzola+igrač.
- **Collapse engine (KOD, testiran):** `Pressure/PuzzleCollapseData.cs` (čist C#) +
  `PuzzleSegmentController` + `PuzzleCorridorController`. Determinističan: S1 pada prvi → S6;
  **Safe nikad, Safe pod nije movable part**. Pragovi 70/50/30/10/0.
- **PressureController.AttachPuzzleCorridor()** se zove PRIJE Initialize() → gasi proceduralne
  ploče. Fallback + Console upozorenje kad puzzle nedostaje. Vozi puzzle stage/tick umjesto slabs.
- **RUČNI Editor korak (NOT RUN):** uvoz FBX-a (skala/axis/materijali; collider SAMO na
  `Segment_Safe_Floor`), postaviti root imenom `Corridor_Modular_Puzzle_ROOT`. Vidi
  `PUZZLE_CORRIDOR_INTEGRATION_REPORT.md` §3 i `PRESSURE_COLLAPSE_SEQUENCE.md`.

### Safe Space aurora (Prioritet 2)
- `SafeSpaceBuilder.TryBuildAuroraDome()` učita `Resources/SafeSpace/AuroraPanorama` (2D),
  inward sfera + plavo-zeleno svjetlo. **AVIF se NE uvozi u Unity, nema codeca ovdje** →
  RUČNA konverzija u PNG (`SAFE_SPACE_AURORA_SETUP.md`). Bez teksture: gradijentni dome fallback.

### UiTheme (Prioritet 3)
- `Assets/Scripts/UI/UiTheme.cs` centralni tokeni; `UiBuilder` ih čita. Puni per-panel
  token-sweep i tranzicije (§21) NISU završeni — vidi `UX_UI_POLISH_REPORT.md`.

### Novi testovi: `FablePuzzleCorridorTests` (16), `FableSafeSpaceAndUiTests` (5).
### Compile/EditMode/Play/Quest: **NOT RUN** (korisnik; braces provjereni).

---

Prethodno ažuriranje: 2026-07-14 — UX KOREKCIJE (poslije finalne integracije).

## UX KOREKCIJE — 7 popravki (najnovije)

1. **Profil UI** — create-mode nije imao NIJEDNO `Button` (samo tekstualni strip),
   a `UIManager` je gasio controller navigaciju → ray nije imao šta da klikne, A/B
   se nisu čitali. Sad: prava uGUI dugmad za sva slova/brojeve/akcije + vidljivo
   NAZAD. `UiNavigationInput` koristi **`RawButton.A/B`** (desni kontroler) —
   NE `Button.One/Two`, jer to hvata i X/Y na lijevom, a **Y je pauza**.
2. **Pauza** — i dalje SAMO Y. Panel dobio tačne instrukcije; tablet overlay
   („PAUZA") sad kaže gdje su kontrole.
3. **Corsi 180°** — `GetConsoleLocalPosition` sad negira **OBJE** ose
   (`−norm.x`, `−norm.y`). Ranije je bila flipovana samo Z = **ogledalo, ne rotacija**.
   Izvod: spawn Y=180 → učesnik gleda −Z → lijeva mu je +X; konzola je identity →
   njen +X JESTE učesnikova lijeva; tablet gleda učesnika → tabletni +X je desna.
   **Ne dirati bez ovog izvoda.** Tabela svih 9 pozicija: `FULL_9_BLOCK_MANUAL_TEST_CHECKLIST.md` §H.
4. **Flanker** — koristi cijeli centralni StimulusArea, bez kartice, 80 % širine.
5. **Uputstva po nivou** — novi `Tasks/TaskInstructions.cs` čita STVARNE brojeve iz
   `TaskDifficultyConfig`. Tutorial verzija = **nivo** → stranica se vidi prvi put za
   zadatak i prvi put za novi nivo, ne pred svaki blok.
6. **Scheduler summary** — `AdaptationExplanationBuilder.BuildCompact()` (prikaz).
   Odluka/algoritam NISU dirani.
7. **Pressure** — sistem je bio ispravan; **`developer.productionUsePressureCondition`
   je bio `false`**, pa je svaka sesija bila Neutral i ništa se nije dešavalo u sobi.
   Dodat dev prekidač uslova na profilnom ekranu + `PressureDiagnostics()`.

> `AdaptationDirective` default je **0 = NoDecisionInvalidSession** — uvijek postavi
> `Hold` eksplicitno, inače validna sesija ispadne nevalidna.

---

Prethodno ažuriranje: 2026-07-14 — FINALNA INTEGRACIJA.

## NAJVAŽNIJE (novo)

- **Pre-session redosljed**: HR gate → [STAI-6 samo prva sesija ciklusa] →
  **BreathingReferenceBaseline (disanje + referentno HR mjerenje ZAJEDNO)** →
  pre-session SSQ → tutorial (ako treba) → 9 blokova → recovery → post-SSQ →
  NASA-TLX → [STAI-6 samo posljednja sesija] → validity/adaptation/save/summary.
- **METODOLOGIJA**: referenca je *PROJECT-DEFINED BREATHING-ASSISTED REFERENCE*,
  **NIJE** neutralni resting baseline. Ne koristiti naziv „NeutralBaseline".
  (`SessionHrPhase.BreathingReferenceBaseline` = 3, `ProductionFlowStage.BreathingReferenceBaseline` = 5.)
- **NEMA automatske pauze.** Prekid HR-a NIKAD ne pauzira ništa. Pauza = **samo Y na
  lijevom kontroleru** (P samo u Editoru). Nema PAUSE dugmeta na konzoli.
  `HrTechnicalPauseController` je OBRISAN — test `HrTechnicalPauseController_NoLongerExists` to čuva.
- **Scheduler**: audit 10/10 PASS, algoritam netaknut → `SCHEDULER_VERIFICATION_REPORT.md`.
- **Nivoi**: participant tablet = Blok/Runda/naziv/nivo/vrijeme; summary =
  `ProductionSessionFlow.BuildPlanIndicators()` (trenutna + naredna sesija).

## STATUS VERIFIKACIJE (iskreno)

- Unity compile: **NOT RUN** (Coplay MCP veza sa Editorom pala; Unity drži projekat → ni batchmode).
- EditMode Run All: **NOT RUN** — korisnik pokreće.
- Quest tok: **NOT RUN** → `FULL_9_BLOCK_MANUAL_TEST_CHECKLIST.md` (sve NOT RUN).

## NE VRAĆATI

automatsku HR pauzu · HrTechnicalPauseController · PAUSE dugme na konzoli ·
Menu/Start za pauzu · drugi PauseController · Python kao production zavisnost ·
naziv NeutralBaseline · Corsi 180° orijentaciju (namjerna, VR-verifikovana).

---

Ažurira se kontinuirano. Posljednje ažuriranje: 2026-07-14, STANDALONE HR PIPELINE.

## STANDALONE HR PIPELINE (posljednja sesija) — implementirano + build-verifikovano

Cilj: stvarni BPM Band 9 → telefon → standalone Quest, bez računara u radu.

- **Arhitektura**: Band 9 → Mi Fitness → HrItem u logcat-u → **HR Relej** (custom Android
  app, `android_companion/HrRelay`, čita logcat lokalno) → UDP protokol-v1 unicast → Quest
  `NetworkHeartRateSource` → `HeartRateService`. Detalji: `STANDALONE_HR_ARCHITECTURE.md`.
- **Jedina koncesija**: `READ_LOGS` grant jednom preko ADB-a pri instalaciji
  (`adb shell pm grant me.djurovic.hrrelay android.permission.READ_LOGS`). Poslije toga bez
  računara. Python bridge NETAKNUT (dev fallback).
- **Verifikovano**: parser JVM 24/24; Android JUnit 7/7; **Android APK build PASS**
  (app-debug.apk ~3.1MB); Unity compile PASS; Unity EditMode **148/148** (HrRelayIngestTests).
- **NIJE verifikovano (traži uređaje)**: real band acquisition, phone→Quest na uređaju,
  screen-lock, hotspot, 60-min. NE tvrditi bez device testa.
- **Nova/izmijenjena Unity logika**: `HR/HrRelayIngest.cs` (NOVO — identitet/dedup/order/
  watch-freshness/heartbeat), `HrPacketParser.cs` (kind heartbeat|sample), `NetworkHeartRateSource.cs`
  (ingest wiring), `WristWatchDisplay.cs` (zona samo u Network modu). NE duplirati ingest.
- **Naredna akcija**: DEVICE TEST (FABLE_CURRENT_STATE.json → nextExactAction), pa upis u
  `STANDALONE_HR_TEST_MATRIX.md`. Build companion: `android_companion/HrRelay/gradlew.bat
  assembleDebug` (koristi `local.properties` sa **forward slashes** za sdk.dir).

## Ranije stanje (FAZE 0–9)

- FAZE 0–8 ZAVRŠENE; FAZA 9 (voice) = PARTIAL; FAZA 10 nije počela.
- Detalji i tačna naredna akcija: `FABLE_CURRENT_STATE.json` → `nextExactAction`.
- Arhitektura: `FABLE_ARCHITECTURE_MAP.md`. Composition root: `Core/AppBootstrapper.cs`.
  Orkestrator: `Session/SessionCoordinator.cs` + `Session/ProductionSessionFlow.cs`.

## Posljednja verifikacija (2026-07-14)

- Compile: PASS (Coplay `check_compile_errors`).
- EditMode: **121/121 PASS** (zvanični Unity Test Runner, TestRunnerApi).
- PlayMode HMD-free probe: boot OK, bez duplikata (1× watch/pressure/voice/subtitles/
  questionnaire panel), HR Simulated + "SIMULIRANI PODACI" na dev liniji, participant
  linija bez raw BPM, 0 MissingReference poslije izlaska.
- Quest Link poslije Fable izmjena: **NIJE provjereno** (FAZA 10 checklist).

## KORISNIČKE IZMJENE — NE DIRATI / NE VRAĆATI

- `UI/QuestionnairePanel.cs`, `Questionnaires/QuestionnaireFlowController.cs`,
  `Tests/EditMode/QuestionnairePanelTests.cs` (2026-07-14): novi paged questionnaire UI
  (SSQ 16 Kennedy simptoma, 4 po stranici, prava Button dugmad 0–3, ray+trigger;
  TLX korak 5). Ne vraćati stari jedan-item-po-ekranu UI; ne mijenjati Kennedy
  redosljed, scoring servis ni persistence format.

## HR arhitektura (FAZA 8 — završeno)

- Modovi: `Disconnected / Simulated / Network / NativeAdbPlaceholder`
  (`HeartRateService.ConfigureSources` + `SetMode`; jedan aktivni source).
- `HeartRateService.Shutdown()` gasi SVE source-ove — poziva se iz
  `AppBootstrapper.ShutdownRuntime()`; ne uklanjati (Network UDP thread bi inače
  preživio Play Mode i držao port).
- `NativeAdbHeartRateSource` = trajni placeholder (`State=NotImplemented`, Connect
  vraća false, nikad ne emituje sample). Pravi plan: `HR_NATIVE_ADB_PLAN.md`.
  Nikad ne pakovati desktop adb.exe u APK.
- `WristWatchDisplay.CreateOrFind(anchor, fallback, hr, devMode)` — jedina ulazna
  tačka; idempotentno; participant NIKAD ne vidi raw BPM; dev linija nosi
  "SIMULIRANI PODACI". Python bridge (`hr_dashboard_v2.py`) OSTAJE.

## Voice (FAZA 9 — PARTIAL)

- `Audio/VoiceCueSystem.cs`: 28 cue ID-jeva, subtitle fallback uvijek radi, klipovi
  opcioni (`Resources/VoiceCues/<CueId>.wav`). Svi pozivi `Voice?.Play(...)` — bez
  event pretplata, nema šta da curi. NEDOSTAJE: `VOICE_SCRIPT_BCS.md`, klipovi, polish.

## Ne praviti ponovo / ne duplirati

- AppBootstrapper, SessionCoordinator, ProductionSessionFlow, TaskRunner, UIManager,
  ProfileRepository, ConsoleInputRouter, Quest poke/ray sistemi, TabletDisplayController,
  PressureController, HeartRateService, VoiceCueManager, WristWatchDisplay.
- Ne dodavati treći input sistem ni treći persistence format.

## Naredne akcije (FAZA 10)

1. `VOICE_SCRIPT_BCS.md` (zatvara FAZU 9).
2. Dokumenti spec §46 (PRODUCTION_SESSION_FLOW, FOUR_TASK_ARCHITECTURE,
   CORSI_IMPLEMENTATION, CONSOLE_LAYOUT_V2, PRESSURE_SYSTEM, QUESTIONNAIRE_FLOW,
   ADAPTATION_V2, THESIS_CHANGE_NOTES, QUEST_MANUAL_TEST_CHECKLIST, FABLE_FINAL_REPORT).
3. Developer panel (spec §38).
4. Obrisati TEMP: `_FableTestRunner.cs`, `FABLE_EDITMODE_TEST_RESULTS.txt*`.
5. Quest Link ručni prolaz po checklisti; po mogućnosti Android build.

## Kako nastaviti

1. `FABLE_CURRENT_STATE.json` → `nextExactAction`.
2. `FABLE_ARCHITECTURE_MAP.md` (tabela novih funkcija).
3. Ne ponavljati završene faze iz `FABLE_MASTER_PROGRESS.md`.
4. Poslije svake logičke cjeline ažurirati continuation fajlove.
