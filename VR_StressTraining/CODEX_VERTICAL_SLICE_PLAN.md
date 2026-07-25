# Codex Vertical Slice Plan

Datum: 2026-07-12  
Unity: isključivo `6000.3.16f1`  
Scope: profil → simulated baseline → hodnik → kratki 1-back blok → pause/continue/end → persistence → SafeSpace summary.

## Sigurnosni snapshot

Prije izmjena zabilježeni su `git status --short`, `git diff --name-only` i `git diff --stat`. Postojeće izmjene `MainScene.unity`, `URP_Balanced.asset`, `hr_dashboard_v2.py`, `start.bat` i svi Claude fajlovi ostaju sačuvani. Ne koriste se reset/restore/checkout, package upgrade, druga Unity verzija, Build & Run ili pravi HR/ADB.

## Fajlovi koji se mijenjaju

- `Assets/Scripts/HR/HeartRateService.cs`: jedinstvena monotonic vremenska osnova i testabilan stale tick.
- `Assets/Scripts/HR/SimulatedHeartRateSource.cs`: konzistentan monotonic receive timestamp.
- `Assets/Scripts/Persistence/AtomicFileWriter.cs`: sigurniji cross-platform replace/fallback bez brisanja jedine validne kopije.
- `Assets/Scripts/Persistence/LogWriters.cs`: minimalan flush/dispose i context-friendly API potreban slice-u.
- `Assets/Scripts/Tasks/TaskRuntimeBase.cs`: kontrolisani Abort/Reset lifecycle i zaštita nakon pause/resume.
- `Assets/Scripts/UI/UIManager.cs`, `ProfileSelectionPanel.cs`, `FlowPanels.cs`: samo javni API/callback veze potrebne coordinator-u.
- `Assets/Scripts/Session/SceneZoneController.cs`, `SafeSpaceBuilder.cs`: stabilni anchor-i i transition-only XR rig premještanje.
- Po potrebi data/session modeli: samo polja neophodna za stvarni summary/log/persistence tok.
- `Assets/Scenes/MainScene.unity`: samo kroz idempotentni editor installer, nakon backup-a i samo ako Unity omogućava bezbjedno izvršenje.

Questionnaire, scheduler, Go/No-Go, Flanker, pressure i pravi network HR se ne povezuju.

## Fajlovi koji se kreiraju

- `Assets/Scripts/Core/AppBootstrapper.cs`
- `Assets/Scripts/Session/SessionCoordinator.cs`
- `Assets/Scripts/Tasks/TaskRunner.cs`
- `Assets/Scripts/Editor/StressTrainingSceneInstaller.cs`
- sedam traženih EditMode test fajlova u `Assets/Scripts/Tests/EditMode/`
- `Assets/Scripts/Tests/PlayMode/VerticalSliceSmokeTests.cs`
- odgovarajući Unity `.meta` fajlovi nastaju kroz Unity import; ne generišu se ručno osim ako je potrebno za determinističku scene referencu.
- `SCENE_INSTALLATION_REPORT.md`
- `CODEX_VERTICAL_SLICE_REPORT.md`
- `CODEX_AUDIT_HANDOFF.md`

## Runtime ownership

- **AppBootstrapper** je jedini scene entry point. Posjeduje config, persistence paths/repositories, profile service, state machine, simulated HR source/service, UI/zone/tablet/console composition root, TaskRunner i SessionCoordinator. Jedini registruje servise i jedini radi Stop/Dispose/registry reset.
- **SessionCoordinator** je jedini owner korisničkog i session toka. Kreira/selektuje profil i session context, vodi baseline, zone tranzicije, pause/countdown/end, kreira summary i nalaže persistence. Ne pravi UI objekte, ne računa task odgovore i ne piše raw JSON.
- **TaskRunner** je jedini owner aktivnog N-back runtime-a i njegovih event subscription-a. Povezuje semantic input, tablet, clock, HR context i trial writer. Ignoriše input van aktivnog taska i garantuje Abort/Dispose.
- **UIManager/paneli** samo prikazuju stanje i emituju intent događaje.
- **HeartRateService** je jedini izvor prihvaćenih HR sample događaja; source samo generiše raw sample.
- **Repositories/writer-i** jedini pišu trajne podatke; coordinator upravlja njihovim session lifecycle-om.
- **SceneZoneController** jedini mijenja aktivnu zonu i pomjera XR rig, isključivo na tranziciji.

## Scene instalacija

`Stress Training/Install Vertical Slice` pravi backup scene, nalazi ili kreira `AppRoot`, `SystemsRoot`, `RuntimeUIRoot`, `DebugRoot`, dodaje tačno jedan `AppBootstrapper`, pronalazi `Corridor_Blockout` i postojeći XR rig, postavlja serialized reference, ne briše/reparentuje postojeći hodnik/konzolu/ruku i čuva scenu samo kad ima stvarnih promjena. Ponovno pokretanje mora biti idempotentno. Ako editor alat nije moguće bezbjedno pozvati, scena se ne uređuje ručno i izvještaj navodi tačan menu korak.

## Test plan

EditMode:

1. validan/nevalidan app state prelaz;
2. real/active/paused session clock;
3. prazan i case-insensitive duplicate username;
4. profile save/load i stabilan userId;
5. atomic save i recovery iz `.prev` poslije korupcije;
6. deterministički N-back, expected action, warm-up, missed response i abort;
7. simulated HR stale signal i paused sample oznaka.

PlayMode smoke:

- bootstrap postoji i startuje SafeSpace;
- profil se programatski kreira;
- baseline → corridor → N-back radi bez headseta;
- semantic response se prihvati;
- pause zaustavlja active clock;
- abort vraća SafeSpace i čuva summary.

Pokretanje: preferirano Unity Test Framework kroz otvoreni 6000.3.16f1 editor/MCP; batch samo ako nema otvorenog konfliktnog editora. Rezultati se ne proglašavaju PASS bez stvarnog run-a.

## Rollback rizici

- Scene installer je jedina namjerna scene mutacija; prije nje nastaje timestamp backup.
- Runtime auto-import može kreirati `.meta` i Library artefakte; source `.meta` su očekivani dio Unity projekta.
- Persistence testovi koriste privremene foldere, nikada stvarni `Application.persistentDataPath` korisnika.
- Event subscription/disposal greška može duplirati input između sesija; zato svaki owner dobija eksplicitan Dispose/Abort.
- Runtime-generisani UI/SafeSpace mogu imati Quest vizuelne/performance razlike; slice se prvo verifikuje u Editoru i označava `QUEST_RUNTIME_NOT_VERIFIED`.
- Ako se compile prekine, scena installer se ne pokreće; izvještaj zadržava preciznu grešku i najmanji sljedeći korak.
