# PUZZLE_CORRIDOR_INTEGRATION_REPORT.md — 2026-07-15

Integracija `puzzle room.fbx` (kopiran u `Assets/Art/PuzzleCorridor/PuzzleCorridor.fbx`).

## 1. Stvarna FBX hijerarhija (parsiran binarni node-tree, nije pretpostavljeno)

```
Corridor_Modular_Puzzle_ROOT           (rot X=90 — Blender Z-up → Unity Y-up)
├─ Segment_S1 … Segment_S6             (6 collapse segmenata)
│   ├─ Segment_S{n}_Floor   → _Mesh
│   ├─ Segment_S{n}_Ceiling → _Mesh
│   ├─ Segment_S{n}_Wall_L  → _Mesh
│   ├─ Segment_S{n}_Wall_R  → _Mesh
│   ├─ Segment_S{n}_CollapsePivot      (empty; tačka rotacije)
│   ├─ Segment_S{n}_FXAnchor           (empty; za prašinu/čestice)
│   └─ Segment_S{n}_LightAnchor        (empty; za svjetlo segmenta)
└─ Segment_Safe                        (najveći — sigurni segment; ista pod-struktura)
```

- **57 Model čvorova**, 7 segmenata × (4 mesha + 3 anchora) + 7 root-ova + ROOT.
- Mesh čvorovi imaju scale 0.01 (model rađen u cm), rotaciju X=90 na svakom (naslijeđeno).
- Materijalni slotovi: po jedan `Material` layer element po meshu (LayerElementMaterial).

## 2. Dimenzije i poravnanje sa postojećom scenom

Iz transformacija (nakon root rotacije, u metrima):

| Segment | Centar Z | Dubina | Uloga |
|---|---|---|---|
| Segment_S1 | +4.375 | 1.25 m | najdalji (uz kavez) — pada prvi |
| Segment_S2 | +3.125 | 1.25 m | |
| Segment_S3 | +1.875 | 1.25 m | |
| Segment_S4 | +0.625 | 1.25 m | |
| Segment_S5 | −0.625 | 1.25 m | |
| Segment_S6 | −1.875 | 1.25 m | najbliži collapse |
| **Segment_Safe** | **−3.75** | **2.5 m** | konzola + igrač; **nikad ne pada** |

Zidovi na X=±1.525 m. **Puzzle model pokriva Z od −5.0 do +5.0 = 10 m, širina ~3 m —
identično postojećem `Corridor_Blockout` (Z −5…+5, 2.8 m unutra).** `Segment_Safe`
(Z −5.0…−2.5) tačno obuhvata konzolu (z≈−4.28) i igrača (spawn z=−3.2, gleda ka −Z).

**Poravnanje: model se NE pomjera da bi se prilagodio, nego se postavlja na postojeći
funkcionalni prostor.** Igrač, konzola, tablet, XR origin i task input ostaju gdje jesu.

## 3. UVOZ U UNITY — URAĐENO 2026-07-15 (preko Coplay `execute_script`, izmjereno)

Status: **DONE**. Scena `Assets/Scenes/MainScene.unity` je sačuvana sa puzzle sobom.

### Stvarne import postavke (izmjerene, ne pretpostavljene)

| Postavka | Vrijednost | Zašto |
|---|---|---|
| `globalScale` (Scale Factor) | **1** | uz useFileScale=false daje tačno 3.3 × 6.1 × 10 m |
| `useFileScale` | **false** | model je autorisan u cm; file scale bi dao 100× premalo |
| `addCollider` | false | collideri se dodaju namjerno, samo gdje trebaju |
| `isReadable` | false | Quest: ne treba CPU kopija |
| `importCameras` / `importLights` | false | nisu potrebni |

> Empirija: `globalScale=1 + useFileScale=true` → 0.033 × 0.061 × 0.1 m (100× premalo).
> `globalScale=100 + useFileScale=false` → 330 × 610 × 1000 m (100× preveliko).
> **`globalScale=1 + useFileScale=false` → 3.3 × 6.1 × 10 m ✓**

### Izmjereno poravnanje (world bounds)

- Ukupno: **X=3.3, Y=6.1, Z=10**; min `(-1.65, -0.25, -5)`, max `(1.65, 5.85, 5)`.
- **Z ide tačno −5.0 … +5.0 — identično `Corridor_Blockout`-u.** Root je na (0,0,0),
  rotacija identity, skala 1 — nikakvo ručno pomjeranje nije bilo potrebno.

| Segment | centerZ | depthZ |
|---|---|---|
| S1 | +4.30 | 1.41 |
| S2 | +3.12 | 1.54 |
| S3 | +1.88 | 1.57 |
| S4 | +0.61 | 1.54 |
| S5 | −0.63 | 1.53 |
| S6 | −1.86 | 1.53 |
| **Safe** | **−3.67** | **2.66** |

`Segment_Safe` pokriva Z −5.0…−2.34 → konzola (z≈−4.28) i igrač (spawn z=−3.2) su unutra ✓.
Dubine su ~1.5 m umjesto nominalnih 1.25 jer **puzzle zupci ulaze u susjedni segment** —
očekivano za slagalicu.

### Šta je urađeno u sceni

1. FBX instanciran; Unity je root nazvao po FAJLU (`PuzzleCorridor`), a **ne** po model
   root čvoru → **preimenovan u `Corridor_Modular_Puzzle_ROOT`** (ime po kojem se veže).
   Kod sad ima i fallback: `PuzzleCorridor` → pa traženje roditelja `Segment_Safe`.
2. Ugašen stari vizuelni shell: `Floor`, `Ceiling`, `Wall_Left`, `Wall_Right`,
   `Wall_ConsoleEnd`, `Wall_CageEnd`. **Zadržani** `Light_ConsoleZone`, `Light_CageZone`,
   `CorridorSpawn` (pressure light ramp i zone anchori ih koriste).
3. `MeshCollider` dodat **samo** na `Segment_Safe_Floor`. Collapse segmenti bez collidera.
4. Scena sačuvana preko `EditorSceneManager.SaveScene` (in-place, ne Save-As).

> Ako puzzle root nedostaje, sistem radi na **proceduralnom fallback-u** uz jasno
> Console upozorenje — ne pada.

## 4. Runtime binding (KOD — ovo je urađeno i testirano)

- `AppBootstrapper.FindOrBindPuzzleCorridor()` nalazi `Corridor_Modular_Puzzle_ROOT`,
  dodaje `PuzzleCorridorController`, veže segmente po imenu.
- `PressureController.AttachPuzzleCorridor(...)` se poziva **prije** `Initialize()`, pa se
  proceduralne ploče i pukotine NE grade kad je puzzle prisutan.
- `PuzzleCorridorController.Bind()` vraća false ako nema segmenata → fallback + Console greška.

## 5. SafeSegment

`Segment_Safe` (CollapseOrder=100, IsSafe=true). Njegov **pod nikad nije „movable part"**
(`MovableParts(isSafe:true)` izostavlja Floor), pa učesnik nikad ne gubi tlo. Na 10%
(Critical) samo mu se zidovi/plafon blago nagnu (4°), na 0% učestvuje u iluziji ali bez
stvarnog pada. Test `SafeSegment_FloorNeverMoves_EvenAtExpired` to čuva.

## 6. Compile / EditMode / Play / Quest

- Compile: **NOT RUN** (korisnik pokreće u Editoru).
- EditMode: novi `FablePuzzleCorridorTests` (16) — collapse jezgro je čist C#/synthetic
  hijerarhija, ne traži uvezeni FBX.
- Play Mode / Quest: **NOT RUN** — traži uvoz FBX-a (ručni Editor korak iz §3).

## 7. Poznata ograničenja

- Fino podešavanje pragova (70 vs 50 razlika „trešnja vs razdvajanje") traži headset.
- Materijali i skala se finalizuju pri uvozu (§3).
- Prašina/void na FXAnchor tačkama koristi postojeći `PressureTransientFxController`
  (nije premješten na FXAnchor pozicije — to je moguće poboljšanje).
