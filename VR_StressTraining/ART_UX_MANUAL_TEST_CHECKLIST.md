# ART_UX_MANUAL_TEST_CHECKLIST.md — 2026-07-15

Status: **PASS / FAIL / NOT RUN**. Ne označavati PASS bez stvarnog testa.
Sve **NOT RUN** dok se FBX ne uveze i ne prođe Play Mode / Quest.

## Preduslov (Editor, ručno)

| # | Korak | Status |
|---|---|---|
| P1 | FBX uvezen kao `Corridor_Modular_Puzzle_ROOT` u scenu (PUZZLE_CORRIDOR_INTEGRATION_REPORT §3) | NOT RUN |
| P2 | AVIF konvertovan u `Assets/Resources/SafeSpace/AuroraPanorama.png` (SAFE_SPACE_AURORA_SETUP) | NOT RUN |
| P3 | Console pri boot-u ispisuje „Puzzle corridor bound: … 7 segmenata" | NOT RUN |

## A. Puzzle soba i pressure

| # | Provjera | Status |
|---|---|---|
| A1 | Model pravilno skaliran (~3 m širok, 10 m dug), bez negativne skale | NOT RUN |
| A2 | Korisnik i konzola na istom mjestu (Segment_Safe ih obuhvata) | NOT RUN |
| A3 | Nema vidljivih rupa između puzzle segmenata u mirovanju | NOT RUN |
| A4 | Neutral: nijedan segment se ne pomjera | NOT RUN |
| A5 | 70%: svjetla trepere + S1/S2 blago podrhtavaju, ništa ne pada | NOT RUN |
| A6 | 50%: S3/S4 počinju, S1/S2 se razdvajaju | NOT RUN |
| A7 | 30%: S1/S2 padaju + void; vidljivo iz položaja za konzolom | NOT RUN |
| A8 | 10%: S3/S4 padaju, bočni/plafonski dijelovi u perifernom vidu | NOT RUN |
| A9 | 0%: S5/S6 padaju, finalna sekvenca, pa TimeExpired tok | NOT RUN |
| A10 | Vidljivo naprijed, bočno I pozadi | NOT RUN |
| A11 | SafeSegment pod ostaje stabilan cijelo vrijeme | NOT RUN |
| A12 | Tablet/konzola/stimulus nikad zaklonjeni; geometrija ne udara kameru | NOT RUN |
| A13 | Manual (Y) pauza zamrzava urušavanje, nastavlja se | NOT RUN |
| A14 | Reset naredne sesije: svi segmenti u početnom stanju | NOT RUN |
| A15 | Stari proceduralni blockout NIJE aktivan uz puzzle (dev log potvrđuje) | NOT RUN |

## B. Safe Space aurora

| # | Provjera | Status |
|---|---|---|
| B1 | Panorama okružuje 360°, Safe Space nije crn | NOT RUN |
| B2 | Nema crnog šava / horizont nije ukrivljen | NOT RUN |
| B3 | Plavo-zelena ambijentalna svjetlost, UI čitljiv | NOT RUN |
| B4 | Panorama SAMO u Safe Space fazama (ne u hodniku) | NOT RUN |
| B5 | Pressure efekti NISU aktivni u Safe Space-u | NOT RUN |
| B6 | Bez teksture: gradijentni dome fallback (ne crno), Console napomena | NOT RUN |

## C. UX/UI

| # | Provjera | Status |
|---|---|---|
| C1 | Profil: izbor, novi profil, NAZAD rade | NOT RUN |
| C2 | Nema dva Continue dugmeta u pre-session toku | NOT RUN |
| C3 | Questionnaire navigacija (Prethodno/Dalje) radi, SSQ paged | NOT RUN |
| C4 | Flanker koristi veliki StimulusArea | NOT RUN |
| C5 | Pause panel blokira task input | NOT RUN |
| C6 | Summary redovi odgovaraju stvarnim scheduler odlukama | NOT RUN |
| C7 | Ponovni ulazak ne duplira UI evente | NOT RUN |
| C8 | Boje dosljedne (UiTheme) kroz sve panele | NOT RUN |

## EditMode (automatski — pokreće korisnik)

| # | Test | Status |
|---|---|---|
| E1 | `FablePuzzleCorridorTests` (16) | NOT RUN |
| E2 | `FableSafeSpaceAndUiTests` (5) | NOT RUN |
| E3 | Postojeći `FablePressureTests`, `FableUxCorrectionTests`, `FableCorsiTests` i dalje zeleni | NOT RUN |
