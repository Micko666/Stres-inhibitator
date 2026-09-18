# PRESSURE_COLLAPSE_SEQUENCE.md — 2026-07-15

Deterministička sekvenca urušavanja puzzle hodnika. Definisana u
`Assets/Scripts/Pressure/PuzzleCollapseData.cs` (`PuzzleCollapseSequence`), vozi je
`PressureController` preko `PuzzleCorridorController`. **Ništa nije nasumično** — isti
skup segmenata daje identičan raspored pri svakom pokretanju.

Aktivno SAMO u `ControlledPressure`. `Neutral` je potpuno stabilan (test
`Neutral_DoesNotMoveAnySegment`).

## Pragovi → stage

`PressureTimeline.StageFor` (nepromijenjeno): 70%→Early, 50%→Mid, 30%→Late, 10%→Critical, 0%→Expired.

## Tri faze po segmentu

- **damage** — spoj se blago razdvaja / segment podrhtava (ništa ne pada)
- **detach** — dijelovi se stvarno odvajaju i putuju (pad + rotacija)
- **vanish** — dijelovi se sakriju i otvara se void iza njih

## Tabela mapiranja (svih 7 segmenata)

| Segment | Order | Damage | Detach | Vanish | Smjer | Rotacija | Nestaje | Safe |
|---|---|---|---|---|---|---|---|---|
| S1 | 0 | 70% Early | 30% Late | 10% Critical | pod↓ / plafon↓ / zid nagib | 22° | da | ne |
| S2 | 1 | 70% Early | 30% Late | 10% Critical | isto | 22° | da | ne |
| S3 | 2 | 50% Mid | 10% Critical | 0% Expired | isto | 22° | da | ne |
| S4 | 3 | 50% Mid | 10% Critical | 0% Expired | isto | 22° | da | ne |
| S5 | 4 | 30% Late | 0% Expired | 0% Expired | isto | 22° | da | ne |
| S6 | 5 | 30% Late | 0% Expired | 0% Expired | isto | 22° | da | ne |
| **Safe** | 100 | 10% Critical | **nikad** | **nikad** | samo zid/plafon nagib | 4° | **ne** | **da** |

Trajanje detach animacije: **1.1 s** po segmentu (Safe cosmetic lean 1.4 s). Easing:
smoothstep. Pomjeraj pada: **2.2 m** (Safe: 0 m). Damage trešnja: ±1.5 cm.

## Šta se dešava na svakom pragu

- **70% (Early):** S1,S2 blago podrhtavaju (dalje, iza korisnika) + svjetla trepere
  (`PressureController.ApplyLights` rampa). **Ništa ne pada.**
- **50% (Mid):** S1,S2 se i dalje „razdvajaju"; S3,S4 počinju da podrhtavaju.
- **30% (Late):** **S1,S2 padaju** + void iza njih; S3,S4 se razdvajaju; S5,S6 počinju.
- **10% (Critical):** S1,S2 nestali; **S3,S4 padaju**; S5,S6 se razdvajaju (blizu → bočno/naprijed);
  Safe segment blago nagne zidove/plafon.
- **0% (Expired):** S3,S4 nestaju; **S5,S6 padaju** → finalna sekvenca (`TriggerGameOverFinale`
  snapuje ostatak i pokreće fade). Nastavlja se postojeći `TimeExpired` → recovery → post-session.

## Tri pravca vidljivosti (spec §6)

- **Pozadi:** glavni red S1→S6 (iza korisnika).
- **Bočno:** svaki segment ima Wall_L/Wall_R koji se naginju unutra — periferni vid.
- **Naprijed/gore:** plafonski dijelovi (Ceiling) padaju nadolje; Safe plafon se nagne
  na kraju. Nikad ne zaklanja tablet/konzolu (Safe pod i geometrija ispred ostaju).

## Reset ponašanje

`PuzzleCorridorController.ResetCorridor()` → svaki `PuzzleSegmentController.ResetSegment()`
vraća sve part-node na cache-ovanu rest transformaciju i ponovo uključuje sve renderere.
Idempotentno. Poziva se iz `PressureController.ResetPressure()` (početak/kraj svake sesije).
Testovi: `Reset_RestoresEverySegmentAndRenderer`, `Segment_DoesNotRetriggerOnceCollapsed`.

## Pauza

Segmenti se pomjeraju samo kroz `TickActive(dt)` sa **aktivnim** vremenom. Ručna (Y) pauza
i PressureTimeline pauza jednostavno prestaju da hrane vrijeme → animacija se zamrzne i
nastavi tačno odakle je stala. Test `Pause_FreezesAnimation_AndResumeContinues`.

## Napomena o determinizmu

Order, pragovi i trajanja su PROJECT_HEURISTIC dizajn — nisu validirani model, ali su
potpuno reproducibilni (test `Schedule_IsDeterministic_AndOrderedFarToNear`).
