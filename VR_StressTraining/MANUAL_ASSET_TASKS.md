# MANUAL_ASSET_TASKS.md — zadaci koji zahtijevaju ljudski Blender/asset rad

Ažurira se tokom iteracije. Kod NIKAD ne smije blokirati na ovim assetima —
za svaki postoji programski placeholder/fallback.

---

## 1. Ravna konzola bez udubljenja (PRIORITET: VISOK)

- **Razlog**: trenutni `ConsoleBody_Crescent_UnityFit.fbx` ima udubljenja; finalni
  layout (Corsi zona + fiksni task red + modularne zone) traži ravnu radnu površinu.
  Kod već koristi anchore (`ConsoleControlsAnchor`, planirani `CorsiControlsAnchor`,
  `FixedTaskControlsAnchor`, `ModularLeftAnchor`, `ModularRightAnchor`) pa zamjena
  meša NE zahtijeva promjenu task logike.
- **Očekivani asset**: `Console_BlenderPrototype_FlatTop_v2.fbx`
- **Dimenzije**: radna ploča ≈ 1.10 m širina (X) × 0.55 m dubina (Z), visina gornje
  površine na Y ≈ 0.985 m od poda (mora odgovarati `ConsoleLayoutConfig.anchorLocalPosition.y`).
  Blagi nagib ploče prema korisniku do 10° je prihvatljiv (anchor rotacija je konfigurabilna).
- **Pivot**: na podu, u centru konzole (kao trenutni model), +Z prema kavezu/zidu,
  korisnik prilazi sa −Z→? — NAPOMENA: u sceni korisnik stoji na Z većem od konzole
  i gleda ka −Z; zadržati istu orijentaciju kao trenutni model (rotacija 0).
- **Object names**: root `ConsoleBody_FlatTop`; bez djece koja se pomjeraju.
- **Moving parts**: nema (sva dugmad su Unity runtime objekti).
- **Scale**: 1 unit = 1 m, primijenjen scale (Ctrl+A) prije exporta.
- **Collider plan**: Unity dodaje MeshCollider (convex=false) kao i sada.
- **Export**: FBX, +Y up, jedan materijal slota (URP/Lit se dodjeljuje u Unityju).
- **Unity putanja**: `Assets/Models/Console/Console_BlenderPrototype_FlatTop_v2.fbx`
- **Zamjena**: u MainScene zamijeniti mesh na `Console_BlenderPrototype` (ili novi
  objekat istog imena na istoj poziciji (0,0,-4.277)); `ConsoleControlsAnchor` ostaje
  child sa istim lokalnim transformom → kontrole se automatski poklapaju.
- **Ne smije se promijeniti**: ime root scene objekta (`Console_BlenderPrototype`),
  svjetska pozicija, Y visina radne površine bez ažuriranja `ConsoleLayoutConfig`.

## 2. Robot arm claw pivoti (PRIORITET: NIZAK — nije potreban za MVP)

- **Razlog**: `Claw_A/Claw_B` su meš objekti bez pivot empties → klješta se ne mogu
  otvarati. Za MVP nije potrebno (ruka ne hvata ništa fizički).
- **Zadatak**: u Blenderu dodati `ClawPivot_A`/`ClawPivot_B` empties na zglob klješta,
  po mogućnosti pomjeriti origin SVAKOG pivota (Base/Shoulder/Elbow/Wrist) na stvarnu
  poziciju zgloba (trenutno su svi ko-locirani na FBX originu iznad meša), re-export.
- **Ne smije se promijeniti**: imena postojećih pivota (BasePivot, ShoulderPivot,
  ElbowPivot, WristPivot), hijerarhija MountPlate/MountBolt (FIXED sibling BasePivot-a).
- **Fallback dok ne postoji**: RobotArmTabletPresenter koristi male aditivne uglove
  u testiranim opsezima; tablet NIJE parentovan na ruku.

## 3. Wrist watch model (PRIORITET: NIZAK)

- **Razlog**: placeholder sat je primitive-based (kutija + mini canvas na lijevom
  controller anchoru). Finalni model je kozmetika.
- **Očekivano**: nizak-poly sat ~0.045×0.035×0.012 m, pivot u centru kaišta,
  `Assets/Models/Watch/WristWatch_v1.fbx`. Zamjena: dodijeliti mesh u
  `WristWatchDisplay` builderu (jedno mjesto u kodu).
