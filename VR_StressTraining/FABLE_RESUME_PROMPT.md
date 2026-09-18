# FABLE_RESUME_PROMPT.md

Nastavi postojeću implementaciju. Prvo pročitaj FABLE_CURRENT_STATE.json,
NEXT_AGENT_HANDOFF.md, FABLE_MASTER_PROGRESS.md i FABLE_ARCHITECTURE_MAP.md.
Ne ponavljaj završene faze.

Projekat: C:\Users\djuro\Desktop\Stres-inhibitator-main\VR_StressTraining
Unity: 6000.3.16f1 · Target: Meta Quest 3 · UI jezik: crnogorski/ijekavica.

Trenutna faza i tačna naredna akcija su u FABLE_CURRENT_STATE.json
(polja currentPhase / nextExactAction). Faze idu redom:
F0 audit → F1 stabilizacija demoa → F2 production skeleton (3-of-4, 15 blokova)
→ F3 Corsi → F4 konzola/robot → F5 pre/post upitnici+baseline → F6 scheduler
→ F7 pressure → F8 HR/watch → F9 voice/UX → F10 integracija/verifikacija.

STANJE 2026-07-14: F0–F8 ZAVRŠENE (compile PASS, EditMode 148/148); F9 PARTIAL (voice
subtitle-only; fali VOICE_SCRIPT_BCS.md); F10 nije počela. Korisnički questionnaire UI
patch (QuestionnairePanel — paged SSQ, 4 simptoma po stranici, prava dugmad 0–3) je
NAMJERNA izmjena — ne vraćati staro.

STANDALONE HR PIPELINE (posebna sesija): Band 9 → Mi Fitness → HrItem u logcat-u →
custom Android app `android_companion/HrRelay` (čita logcat, READ_LOGS grant jednom
preko ADB) → UDP protokol-v1 → Quest. Implementirano + build-verifikovano (APK PASS,
parser 24/24, JUnit 7/7, Unity EditMode 148/148). NIJE testirano na uređaju (real
acquisition/screen-lock/hotspot/60min). Vidi STANDALONE_HR_ARCHITECTURE.md i
STANDALONE_HR_TEST_MATRIX.md. Python bridge ostaje dev fallback. Ne duplirati
HeartRateService/NetworkHeartRateSource/HrRelayIngest.

ZABRANE:
- ne lomi ručno potvrđeni three-task demo (SessionCoordinator demo grane,
  DemoTaskFactory, ThreeTaskDemoFlow, konzolni poke put);
- ne pravi paralelni AppBootstrapper/SessionCoordinator/TaskRunner/UIManager/
  ProfileRepository/scheduler/input/persistence sistem;
- ne koristi git reset/restore/checkout -- /clean;
- ne mijenjaj Unity/package verzije;
- ne briši legacy kod sa aktivnim referencama;
- ne prepisuj Blender konzolu ni robot arm model;
- scene mijenjaj samo idempotentnim installerom/builderima uz backup;
- demo ne smije mijenjati produkcijske nivoe/cycle/timestampove;
- težina se ne mijenja tokom aktivne sesije; adaptacija važi za narednu;
- sve heuristike centralizuj i označi PROJECT_HEURISTIC;
- nepotvrđeni tekstovi upitnika nose NEEDS_VALIDATED_ITEM_TEXT;
- ne tvrdi da nešto radi bez dokaza (compile/test/Play Mode/Quest).

Poslije svake logičke cjeline ažuriraj: FABLE_MASTER_PROGRESS.md (append),
FABLE_CURRENT_STATE.json, NEXT_AGENT_HANDOFF.md, a po potrebi i
FABLE_ARCHITECTURE_MAP.md, MANUAL_ASSET_TASKS.md, SCIENTIFIC_TRACEABILITY.md.


STANJE 2026-07-14 (FINALNA INTEGRACIJA):
- Pre-session: HR gate → [STAI prva sesija] → BreathingReferenceBaseline (disanje+referenca
  ZAJEDNO, isti interval = baseline duration) → SSQ → tutorial → 9 blokova → recovery →
  post-SSQ → NASA-TLX → [STAI posljednja sesija] → summary.
- Referenca je PROJECT-DEFINED BREATHING-ASSISTED REFERENCE — NIJE resting baseline.
- Automatska pauza NE POSTOJI. Pauza = samo Y (lijevi kontroler). Konzola bez PAUSE dugmeta.
- Scheduler audit: 10/10 PASS, algoritam netaknut (SCHEDULER_VERIFICATION_REPORT.md).
- Compile/EditMode/Quest: NOT RUN — korisnik verifikuje.
