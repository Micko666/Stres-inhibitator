# SCHEDULER_VERIFICATION_REPORT.md — 2026-07-14

Audit cijelog lanca (kod je praćen linija po liniju, algoritam NIJE mijenjan).

## Lanac

```
blokovi → SessionSummaryData
  → CompleteBreathingReference()      : Summary.schedulerEligible
        = !developerTest && source == NetworkBridge && baselineQuality == Good
  → EnterValidity()                   : Summary.validityStatus  (SessionValidityEvaluator)
  → EnterAdaptation()                 : ako !schedulerEligible → decision = null (preskočeno)
                                        inače AdaptationScheduler.Decide(BuildAdaptationInput())
                                        → Summary.schedulerDecision
  → EnterSave()                       : usable = Valid || ValidWithWarnings
                                        if (usable && Completed) UserProfileService.CompleteSession(profile, summary, decision)
                                        else if (TimeExpired)     CompleteSession(profile, summary, null)   ← cycle++, BEZ nivoa
                                        else                      samo istorija, BEZ nivoa
  → CompleteSession()                 : profile.currentNBackLevel/GoNoGo/Flanker/Corsi/Pressure = decision.*.newLevel
                                        _repository.SaveProfile(profile)      ← DISK
  → naredna sesija: ProductionSessionFlow.Start()
                                        → profile.currentNBackLevel … currentPressureLevel
                                        → ProductionSessionPlanGenerator.Generate(...)
```

## Nalazi (svi PASS — nijedan bug nije nađen, ništa nije mijenjano)

| # | Provjera | Nalaz |
|---|---|---|
| 1 | Prva sesija koristi default nivoe | **PASS** — `UserProfileData` default = 1 za sve (`currentNBackLevel/GoNoGo/Flanker/Corsi/Pressure`) |
| 2 | Validna završena sesija pravi odluku | **PASS** — `EnterAdaptation` → `AdaptationScheduler.Decide` → `Summary.schedulerDecision` |
| 3 | Odluka se čuva uz PRAVI profil | **PASS** — `CompleteSession(profile, …)` upisuje u taj `profile` i zove `_repository.SaveProfile(profile)` |
| 4 | Preživi izlaz iz Play Mode / reload | **PASS** — `SaveProfile` piše na disk (`ProfileRepository`, atomski upis) |
| 5 | Naredna sesija čita sačuvane nivoe | **PASS** — `Start()` čita `profile.current*Level` i `profile.GetTaskLevel` za plan |
| 6 | Drugi profil ne dobija plan prvog | **PASS** — plan se generiše iz TOG profila; `masterSeed` je per-sesija (`TaskSeedService.NewMasterSeed()`); `ResolvePreviousOmitted(profile.sessionSummaries)` je per-profil |
| 7 | Invalidna sesija ne izaziva adaptaciju | **PASS** — dvostruka brana: `schedulerEligible` (HR/baseline/dev) **i** `usable` (Valid/ValidWithWarnings) + `Completed` |
| 8 | Globalni pressure i lokalna težina se NE dižu istovremeno | **PASS** — `AdaptationRuleSet.EvaluatePressure` **Rule 1**: `if (ctx.AnyTaskIncreased) → Hold` (`P_TASK_INCREASED_HOLD`) |
| 9 | Task selection balancing | **PASS** — `SeededConstrainedTaskSelector`: 3-of-4, prethodno izostavljen task je **prisilno uključen** |
| 10 | Nijedan task izostavljen dva puta zaredom | **PASS** — isto pravilo; test `PreviouslyOmittedTask_IsNeverOmittedTwiceInARow` (4 taska × 30 seedova) |

## Odgovor na pitanje „da li scheduler zaista mijenja narednu sesiju?"

**DA — lanac je zatvoren i dokazan u kodu:**
`decision.*.newLevel` → `profile.current*Level` → `SaveProfile` (disk) →
sljedeći `Start()` → `Generate(...)` → `SessionPlan.blocks[i].difficultyLevel`.

Uslovi pod kojima se **NE** primijeni (namjerno):
- `schedulerEligible == false` (dev test, simulirani HR, ili `BaselineQuality != Good`) → `decision = null`;
- validnost nije `Valid`/`ValidWithWarnings`;
- `completionStatus != Completed` (npr. `TimeExpired` → cycle napreduje, nivoi se **ne** mijenjaju).

## Gdje se to vidi

- **Participant tablet (tokom zadatka)**: Blok X/9 · Runda Y/3 · naziv zadatka · nivo N · globalno vrijeme.
  *Ništa više* — bez scoring-a, razloga i narednog plana.
- **Post-session summary / developer**: `ProductionSessionFlow.BuildPlanIndicators()` →
  TRENUTNA SESIJA (condition, pressure nivo, izabrana 3 taska + nivoi, seed/plan ID, budžet,
  default vs scheduler-generated) i NAREDNA SESIJA (pressure + po tasku: directive → novi nivo,
  fired rules, sessionId izvora, status **Applied / Pending / Not generated / Rejected due to invalid data**).

## Napomena

Algoritam schedulera **nije mijenjan** — nijedan dokazani bug nije nađen.
