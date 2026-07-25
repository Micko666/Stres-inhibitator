# THREE TASK DEMO — završni izvještaj

Datum: 13. jul 2026.  
Projekat: `VR_StressTraining`  
Unity: `6000.3.16f1`

## 1. Rezultat iteracije

Postojeći vertical slice sada ima kompletan, ograničen demo tok za tri kognitivna zadatka:

1. N-back (1-back);
2. Go/No-Go;
3. Flanker.

Svaki zadatak ima tutorial, fiksnu vježbu, eksplicitnu potvrdu prije bodovanog bloka, countdown, jedan kratki bodovani blok, precizan feedback i rezultat u završnom sažetku. Konzola je jedini task-input na Questu; ray ostaje za menije. U ovoj iteraciji nijesu dodati upitnici, pravi HR, pressure/collapse, novi scheduler/adaptacija, voice-over, Blender modeli niti finalni art pass.

## 2. Novi korisnički tok

1. `Profile Selection` u SafeSpace-u.
2. Izbor/kreiranje profila otvara `Demo Overview`; task se ne pokreće.
3. `Pokreni demo` kreira `DemoOnly` sesiju i otvara N-back tutorial na stabilnom SafeSpace UI anchoru.
4. `Pokreni vježbu` tek tada premješta korisnika u corridor, aktivira konzolu i pokreće practice.
5. Poslije practice-a prikazuje se rezultat i izbor `Ponovi vježbu` / `Nastavi`.
6. `Nastavi` pokreće countdown od 3 s, zatim jedan bodovani blok.
7. Poslije bloka korisnik se vraća u SafeSpace na tutorial sljedeće igre.
8. Isti tok se ponavlja za Go/No-Go i Flanker.
9. Poslije Flankera prikazuje se `Session Summary` u SafeSpace-u.
10. `Nazad na profile` vraća na izbor profila.

Tutoriali, summary i ostali SafeSpace paneli koriste isti World Space Canvas/anchor. Practice-review ostaje uz fizičku konzolu u corridoru jer se odatle bira ponavljanje ili nastavak.

## 3. SafeSpace UI

- Položaj je izveden iz `SafeSpaceSpawn`, ne iz apsolutne world Y vrijednosti.
- Serialized vrijednosti su `eyeHeightMeters = 1.65` i `distanceMeters = 1.7`.
- `WorldSpaceUiOrientation.PlaceFromSpawn` koristi spawn forward/up i postavlja čitljivu stranu canvasa prema korisniku.
- Profil, overview, tutorial i summary koriste `SafeSpaceUIAnchor`; svi runtime paneli su na jednom stabilnom canvasu.
- `SafeSpaceUiAnchorGizmo` prikazuje panel i readable forward samo u Editoru.
- Prelazak na corridor se obavlja tek kada korisnik pokrene practice; XR rig se ne rotira nasumično radi korekcije UI-a.

## 4. Direktna Quest interakcija sa konzolom

### Meta XR putanja

`MainScene` već sadrži aktivan zvanični Meta `[BuildingBlock] OVRInteractionComprehensive` sa lijevim i desnim controller `PokeInteractor` komponentama. Runtime ih strukturno pronalazi ispod aktivnog `OVRCameraRig` objekta, uključujući trenutno neaktivne child objekte, i ponovo ih koristi. Time se ne kreira drugi par interactora i izbjegava se dupli Select/haptic događaj.

Za minimalne/test scene koje nemaju comprehensive building block postoji fallback: `QuestControllerPokeSystem` instancira zvanični Meta `ControllerPokeInteractor` prefab i povezuje ga sa OVR controller anchorima preko `OVRControllerPokeSource`. Fallback ne implementira sopstveni hit-test; Meta `PokeInteractor` ostaje jedini direct-interaction sistem.

`FeedbackManager` prefab je instaliran u `MainScene`. Meta feedback putanja je povezana, ali fizička vibracija još nije potvrđena na Quest hardveru poslije posljednjih izmjena. Bootstrap sada prekida pokretanje sa jasnom greškom ako validan controller-poke put ne može biti inicijalizovan.

### Hijerarhija i layout

```text
Console_BlenderPrototype
└── ConsoleControlsAnchor                 local (0, 0.985, -0.023)
    └── ConsoleControlsRoot               generiše se pri pokretanju
        ├── btn_left
        ├── btn_match
        ├── btn_go
        ├── btn_nomatch
        ├── btn_right
        ├── ind_status
        └── btn_pause
```

Svako standardno dugme ima bazu, `MovingCap`, veliki label, collider, `PlaneSurface + CircleSurface + PokeInteractable`, hover/pressed/disabled vizuelno stanje, dubinu pritiska od 0.008 m i povratak na početnu poziciju poslije `Unselect`.

`ConsoleControlsAnchor` je child stvarnog `Console_BlenderPrototype` shell-a. Položaji su lokalni i serialized kroz `ConsoleLayoutConfig`; nema hardkodovanih apsolutnih world pozicija. Editor-only gizmo pokazuje radnu površinu anchora.

Primarni red koristi razmak od 0.15 m. Legacy confirm/toggle/lever/knob/central/distractor klase ostaju u projektu, ali `showLegacyDemoControls = false`, pa se ne prikazuju u standardnom demo layout-u.

### Aktivne kontrole

| Stanje | Aktivne task kontrole |
|---|---|
| N-back | `MATCH`, `NO MATCH` |
| Go/No-Go | `GO` |
| Flanker | `LEFT`, `RIGHT` |
| Bez aktivnog taska | nijedna task kontrola |

Neaktivne kontrole su zatamnjene, njihov `PokeInteractable` je isključen i ne mogu poslati odgovor. Statusna lampica mijenja boju po tasku. `PAUSE` je fizički dostupan samo dok task stvarno traje; tutorial/countdown/pause meni koriste normalni ray UI.

Jedan Meta `Select` emituje najviše jedan `SemanticAction`. Latch važi na nivou cijele kontrole, uključujući slučaj da drugi pointer dodirne već pritisnuto dugme. Programatski fallback ima debounce od 0.25 s.

### Ray i controller mapping

- Desni controller ray: profil, overview, tutorial, practice-review, Pause meni i summary.
- Konzola je na `Ignore Raycast` layeru i izuzeta iz menu-ray putanje.
- A/B nijesu task odgovori; OVR controller adapter zadržava samo `Start/Menu → Pause`.
- Quest task odgovori dolaze isključivo iz direktnog Meta poke-a.

Editor-only tastatura:

- `M` → Match
- `N` → NoMatch
- `Space` → Go
- `Left Arrow` → Left
- `Right Arrow` → Right
- `P` → Pause

Keyboard kod je pod `UNITY_EDITOR`; nije uključen u Quest Development Build.

## 5. Pravila, practice i scoring

| Task | Practice | Bodovani demo blok |
|---|---|---|
| N-back | 1 uvodni A bez odgovora + A/MATCH + B/NO MATCH + B/MATCH; 3 scoreable practice trial-a | 8 scoreable + 1 warm-up; konfiguracioni clamp 6–10 |
| Go/No-Go | tačan GO, tačno suzdržavanje, missed GO/omission, pritisak na NO-GO/commission | 10 seeded trial-a; clamp 8–12 |
| Flanker | 2 congruent + 2 incongruent primjera | 10 seeded trial-a; clamp 8–12 |

### N-back

- Pravilo je eksplicitno 1-back: isti prethodni simbol = `MATCH`, različit = `NO MATCH`.
- Prvi stimulus je uvodni/warm-up i ne prima odgovor.
- Tutorial prikazuje A → A/MATCH i A → B/NO MATCH primjer.

### Go/No-Go

- Zeleni `GO` traži pritisak centralnog GO dugmeta.
- Crveni `STOP / NE PRITISKAJ` traži suzdržavanje.
- Commission i omission greške imaju odvojeno bilježenje i prikaz u summary-ju.

### Flanker

- Odgovor određuje isključivo centralna strelica.
- Tutorial primjeri naglašavaju centralnu strelicu; bodovani stimulus je ne naglašava.
- Bilježe se tačnost i reaction time.

Practice koristi isti runtime i isti feedback put kao bodovani blok, ali se ne upisuje u `trials.jsonl`, ne ulazi u score i ne ulazi u task-HR agregat. Korisnik mora eksplicitno pritisnuti `Nastavi` prije svakog pravog bloka.

Precizan feedback obuhvata: `Uvodni simbol — zapamti ga`, `Tačno`, `Netačno`, `Vrijeme je isteklo`, `Ne pritiskaj na NO-GO` i `Prati centralnu strelicu`. Nejasni generički `Odgovor primljen` put je uklonjen.

## 6. Tablet UX

Tablet je display-only i nije UI input. Prikazuje:

- naziv taska i `blok 1/1`;
- tačne aktivne kontrole;
- task-specifičnu instrukciju prije prvog practice stimulusa;
- countdown;
- stimulus;
- `Trial x/y` progress;
- precizan response feedback;
- završetak practice-a/bloka.

Go/No-Go koristi jasno obojeni GO/STOP prikaz. Flanker stimulus dekodira niz strelica, a N-back prikazuje veliki simbol.

## 7. Simulated HR

- `SimulatedHeartRateSource` ostaje aktivan radi tehničkog logovanja.
- Svaki raw HR zapis zadržava `sourceType = Simulated`.
- Simulirani HR se ne prikazuje kao realna fiziologija na tabletu ili user summary-ju.
- U task summary podacima simulirani `avg/max BPM` su maskirani sa `-1`, a elevated/high seconds sa `0`.
- Završni korisnički summary ne sadrži BPM, HR zonu niti tvrdnju da je sat povezan.
- Demo može normalno raditi bez pravog HR izvora.

## 8. Summary i persistence

User summary prikazuje samo:

- tačnost sva tri taska;
- Go/No-Go commission i omission greške;
- prosječno vrijeme reakcije;
- ukupno trajanje;
- broj pauza;
- completion status.

Ne prikazuje scheduler odluku, kliničku interpretaciju, upitnike ili simulirani HR.

Bodovani demo zapis sadrži 29 trial redova: 28 scoreable trial-a i jedan N-back warm-up. Practice trial-i nijesu persistirani. Završeni demo se dodaje istoriji profila sa `ValidityStatus.DemoOnly` i sintetičkim `demo-<sessionId>` cycle ID-em, ali ne mijenja pravi `activeCycleId`, cycle index/summaries, `lastSessionAtUtcIso`, next recommendation ili difficulty/adaptation nivoe.

## 9. Relevantni promijenjeni/dodati fajlovi

### Scene i instalacija

- `Assets/Scenes/MainScene.unity`
- `Assets/Scripts/Editor/StressTrainingSceneInstaller.cs`
- `Assets/Resources/QuestPokePrefabReference.asset`

### Core, session i UI

- `Assets/Scripts/Core/AppBootstrapper.cs`
- `Assets/Scripts/Core/AppStateMachine.cs`
- `Assets/Scripts/Core/StressTrainingConfig.cs`
- `Assets/Scripts/Session/SessionCoordinator.cs`
- `Assets/Scripts/Session/ThreeTaskDemoFlow.cs`
- `Assets/Scripts/Session/DemoSummaryFormatter.cs`
- `Assets/Scripts/Session/SafeSpaceBuilder.cs`
- `Assets/Scripts/Session/SceneZoneController.cs`
- `Assets/Scripts/UI/UIManager.cs`
- `Assets/Scripts/UI/QuestRayUiSystem.cs`
- `Assets/Scripts/UI/UiNavigationInput.cs`

### Taskovi i tablet

- `Assets/Scripts/Tasks/TaskContracts.cs`
- `Assets/Scripts/Tasks/TaskRuntimeBase.cs`
- `Assets/Scripts/Tasks/TaskRunner.cs`
- `Assets/Scripts/Tasks/DemoTaskFactory.cs`
- `Assets/Scripts/Tasks/FlankerTask.cs`
- `Assets/Scripts/Tablet/TabletViewModel.cs`
- `Assets/Scripts/Tablet/TabletDisplayController.cs`

### Konzola/XR

- `Assets/Scripts/Console/ConsoleInputRouter.cs`
- `Assets/Scripts/Console/ConsoleControlBase.cs`
- `Assets/Scripts/Console/ConsoleControls.cs`
- `Assets/Scripts/Console/ConsoleLayoutBuilder.cs`
- `Assets/Scripts/Console/InputAdapters.cs`
- `Assets/Scripts/Console/QuestControllerPokeSystem.cs`
- `Assets/Scripts/Console/QuestPokePrefabReference.cs`

### Testovi

- `Assets/Scripts/Tests/EditMode/AppStateMachineTests.cs`
- `Assets/Scripts/Tests/EditMode/SafeSpaceUiLayoutTests.cs`
- `Assets/Scripts/Tests/EditMode/NBackTaskTests.cs`
- `Assets/Scripts/Tests/EditMode/GoNoGoTaskTests.cs`
- `Assets/Scripts/Tests/EditMode/FlankerTaskTests.cs`
- `Assets/Scripts/Tests/EditMode/DemoTaskFactoryTests.cs`
- `Assets/Scripts/Tests/EditMode/DemoSummaryFormatterTests.cs`
- `Assets/Scripts/Tests/EditMode/ConsoleInteractionTests.cs`
- `Assets/Scripts/Tests/EditMode/HeartRateServiceTests.cs`
- `Assets/Scripts/Tests/PlayMode/VerticalSliceSmokeTests.cs`

Odgovarajući `.meta` fajlovi postoje za nove Unity skripte i assete.

## 10. Test i compile status

| Provjera | Status |
|---|---|
| Runtime `StressTraining` assembly, Unity Roslyn/Bee reference set | PASS — exit code 0 |
| `StressTraining.Editor` assembly compile | PASS — exit code 0 |
| EditMode test assembly compile | PASS — exit code 0 |
| PlayMode test assembly compile | PASS — exit code 0 |
| Izolovani pure-task harness | PASS — 21/21 provjera |
| Zvanični Unity EditMode Test Runner | NIJE IZVRŠEN |
| Zvanični Unity PlayMode Test Runner | NIJE IZVRŠEN |
| Ručni Quest Link test poslije posljednjih izmjena | NIJE IZVRŠEN |

U projektu je deklarisan 41 EditMode test/test-case i jedan HMD-free PlayMode smoke test. Smoke pokriva profil, overview, sva tri tutorial/practice/block toka, pause/resume, oba countdown overshoot slučaja, summary, tačan broj persistiranih trial-a, practice/score razdvajanje, DemoOnly scheduler izolaciju i simulated-HR provenance/masking.

Važno: test assembly kompilacija nije isto što i izvršen Unity Test Runner. Raniji MCP/Test Runner pozivi nijesu završili, zato se 41 EditMode i jedan PlayMode test ovdje ne označavaju kao izvršeni/passed.

## 11. Ručna Quest provjera

Status poslije ove iteracije: **PENDING**.

Raniji Quest test je potvrdio da tekst više nije ogledalski i da menu ray/trigger rade. Nakon novih SafeSpace, full-flow i direct-poke izmjena potreban je novi prolaz:

- [ ] Profile panel je približno u visini očiju.
- [ ] `Pokreni demo` otvara tutorial i ne pokreće task.
- [ ] Match/No Match pravilo je jasno.
- [ ] Practice počinje tek poslije `Pokreni vježbu` i premješta korisnika do konzole.
- [ ] Konzola koristi direct controller poke, ne menu ray.
- [ ] Hover/proximity stanje je vidljivo.
- [ ] Pritisak ima animaciju i stvarnu Quest haptiku.
- [ ] Jedan fizički pritisak daje tačno jedan odgovor.
- [ ] Neaktivna dugmad ne daju odgovor.
- [ ] Go/No-Go se može završiti sa GO i suzdržavanjem na STOP.
- [ ] Flanker se može završiti sa LEFT/RIGHT.
- [ ] Pause meni radi ray pointerom i nema duplog inputa.
- [ ] Summary sadrži sva tri rezultata.
- [ ] Simulated HR nije predstavljen kao stvarno mjerenje.

## 12. Poznati problemi / ograničenja

1. Direct poke hover/select, fizička dubina, domet, ergonomija i haptika nijesu verifikovani na stvarnom Questu poslije posljednjih izmjena.
2. `ConsoleControlsRoot` se generiše pri ulasku u Play; anchor je serialized u sceni. Vizuelni fit sa stvarnim shell-om zato još zahtijeva ručnu provjeru.
3. HMD-free smoke potvrđuje fallback konstrukciju, ne stvarni tracked pose ili Meta hover/select/haptic tok. `MainScene` u normalnom radu koristi postojeći comprehensive building block.
4. Zvanični Unity Test Runner nije izvršen; najnoviji kod i oba test assembly-ja jesu kompajlirani bez grešaka.
5. U fatalnom broken-install bootstrap slučaju servisi se zaustavljaju i greška se loguje, ali nema posebnog participant-facing recovery panela; parcijalno kreirane scene komponente ostaju do scene reload-a.

Nema poznatog P0/P1 runtime blokera iz završnog statičkog audita. Sljedeći korak je isključivo ručni Quest Link Play Mode prolaz po gornjoj checklisti, bez širenja scope-a.
