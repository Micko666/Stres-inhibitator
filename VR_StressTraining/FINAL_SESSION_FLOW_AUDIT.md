# FINAL_SESSION_FLOW_AUDIT.md — 2026-07-14

## 1. Novi produkcijski tok (implementiran)

```
1.  HR povezivanje (BaselinePreparation gate)      — zaključano dok nema stvarnog svježeg uzorka
2.  STAI-6                                          — SAMO prva sesija ciklusa
3.  BreathingReferenceBaseline  ← SPOJENA FAZA      — vođeno disanje + referentno mjerenje pulsa
4.  Pre-session SSQ                                 — „Priprema 2/2 — Kratki upitnik“
5.  Tutorial                                        — samo kada je potreban (novo pravilo / nova verzija)
6.  Ready → countdown → korridor
7.  9 blokova (3 runde × 3)
8.  Recovery
9.  Post-session SSQ
10. NASA-TLX
11. STAI-6                                          — SAMO posljednja sesija ciklusa
12. Validity → Adaptation → Save → Summary → profil
```

Srednje sesije: disanje+referenca → SSQ → (tutorial ako treba) → igre.

## 2. Spajanje disanja i referentnog mjerenja

- `ProductionFlowStage.HeartRateBaseline` + `CopingPreparation` → **jedna** faza
  `ProductionFlowStage.BreathingReferenceBaseline` (vrijednost 5 zadržana).
- `SessionHrPhase.NeutralBaseline` → **`SessionHrPhase.BreathingReferenceBaseline`** (vrijednost 3 zadržana;
  `CopingBreathing = 4` ostaje samo kao legacy za stare zapise).
- **Isti interval**: faza traje tačno `config.baseline.durationSeconds` (postojeće trajanje).
  `BreathingPanel.BeginReference(cfg, duration, firstOfCycle)` vrti disanje za taj interval;
  **fazu završava tok, ne panel** → nikad dva Continue dugmeta.
- Tokom faze prikazano: ritam disanja, napredak %, preostalo vrijeme, status HR signala,
  tačan BPM (i na virtuelnom satu, jer je `AppState.Baseline`).
- `BeginReference` postavlja `_flowDriven = true` pa panel **nikad** ne prikaže svoj Continue.

## 3. Metodološka oznaka (VAŽNO)

> **PROJECT-DEFINED BREATHING-ASSISTED REFERENCE.**
> Ovo **NIJE** standardno neutralno resting baseline mjerenje — učesnik namjerno diše po
> vođenom ritmu dok se mjeri puls. Vrijednost se koristi kao **projektna referentna BPM
> vrijednost** za tu sesiju (zone se računaju relativno u odnosu na nju).
> Nikakva nova naučna tvrdnja se ne uvodi. Vidi `SCIENTIFIC_TRACEABILITY.md`.

Nazivi „NeutralBaseline“ / „resting neutral baseline“ / „prirodni mirni baseline“ **se više ne koriste**.

## 4. HR ponašanje u fazi (i svuda)

- HR se snima **kontinuirano** (SSQ, tutorial, blokovi, recovery).
- **Samo** uzorci iz `BreathingReferenceBaseline` faze ulaze u referentnu vrijednost
  (`ProductionSessionFlow.OnHeartRateSample` → `_baseline.AddSample` samo u toj fazi).
- Ako HR zastari: **ne pauzira se ništa**, nema blokirajuće poruke, nema countdowna.
  Rupa se jednostavno ne upisuje u akumulator → pada u metrike kvaliteta.
  Posljednji BPM se **ne ponavlja** kao novi uzorak (dedup je u `HrRelayIngest` + `HeartRateService`).
- Ako nema dovoljno validnih uzoraka → `BaselineQuality != Good` → postojeća
  `SessionValidityEvaluator` / `schedulerEligible` logika odlučuje. **Vrijednost se ne izmišlja.**

## 5. Globalni tajmer

| Faza | Tajmer |
|---|---|
| HR gate, STAI, disanje/referenca, SSQ, tutorial, countdown | **STOJI** (`GlobalChallengeClockState.Ready`) |
| stimulus / response / Corsi / ITI (aktivni blok) | **RADI** |
| ručna pauza (Y) | **STOJI** |
| 0 s | sesija → `TimeExpired` |
| 9/9 blokova prije nule | uspješan završetak |

Test: `PreSessionStages_NeverRunTheGlobalChallengeClock`, `GlobalTimerReachingZero_MarksTimeExpiredNotSuccess`.

## 6. Pressure

- Radi **samo** u `SessionCondition.Pressure` (`PressureController.ShouldRunFor`).
- Prati 70/50/30/10/0 % preostalog vremena.
- Stoji tokom ručne pauze (`PauseChanged` → `PressureSystem.SetPaused`).
- Ne radi tokom pre-session toka (`TickPressure` traži `Clock.IsGlobalChallengeRunning`).

## 7. Ručna pauza (nepromijenjeno — potvrđeno)

- **Y na lijevom kontroleru** (`OVRInput.RawButton.Y`) — jedina XR putanja.
- `P` samo `#if UNITY_EDITOR`, kroz **istu** putanju.
- Nema PAUSE dugmeta na konzoli (14 kontrola: 5 task + 9 Corsi).
- Nema automatske/tehničke pauze — `HrTechnicalPauseController` je obrisan i test to čuva.
- Tačno **jedna** pretplata na `TogglePause` (`_taskRunner.PauseToggleRequested`).

## 8. TimeExpired tok

`HandleGlobalTimeExpired` → `FinishBlocks(SystemTerminated, timeExpired: true)` →
Recovery → post-SSQ → NASA-TLX → (STAI ako kraj ciklusa) → Validity → Adaptation → Save.
Parcijalni rezultati se čuvaju; **nijedna post faza se ne preskače**.
Validity → `IncompleteTimeExpired`; `CompleteSession(profile, summary, null)` → cycle napreduje,
**nivoi se ne mijenjaju**.

## 9. Statusi verifikacije

| Stavka | Status |
|---|---|
| Unity compile | **NOT RUN** — Coplay MCP veza pala; korisnik pokreće |
| EditMode Run All | **NOT RUN** — isto |
| Dead references (tehnička pauza) | **PASS** (grep) |
| PAUSE dugme ne postoji | **PASS** (grep + test) |
| Jedna XR pause putanja | **PASS** (grep + test) |
| Play Mode 9-block tok | **NOT RUN** |
| Quest ručni tok | **NOT RUN** — vidi `FULL_9_BLOCK_MANUAL_TEST_CHECKLIST.md` |
