# SAFE_SPACE_AURORA_SETUP.md — 2026-07-15

Aurora Borealis 360 panorama kao mirna pozadina Safe Space-a.

## Format i konverzija (VAŽNO)

Priloženi fajl je **AVIF** (`Assets/Art/SafeSpace/AuroraPanorama_source.avif`, sačuvan kao
izvor). **Unity ne uvozi AVIF**, a u ovom okruženju nema AV1 codeca za konverziju iz CLI-a
(`System.Drawing` i `ffmpeg` ne mogu da ga dekodiraju). Zato:

### Ručni korak (NOT RUN — traži alat sa AVIF podrškom)

1. Konvertuj `AuroraPanorama_source.avif` → **PNG** (ili visokokvalitetni JPG), zadrži
   equirectangular 2:1 odnos ako slika to jeste. Alati: GIMP, Photoshop, `magick`,
   online konverter, ili Windows Photos (sa AV1 Video Extension) → Save As.
2. **Ne koristi runtime AVIF dekoder.** Originalni AVIF ostaje kao izvor.
3. Sačuvaj kao **`Assets/Resources/SafeSpace/AuroraPanorama.png`**
   (folder `Resources/SafeSpace/`, ime `AuroraPanorama`).
4. Import postavke teksture:
   - **Texture Shape**: 2D (koristi se kao equirectangular na sferi) —
     ili, ako želiš pravi skybox, vidi „Alternativa" dolje.
   - **Wrap Mode**: Repeat po U (da nema šava po horizontali).
   - **Max Size**: 2048 (Quest 3 — ne pretjeruj).
   - **Compression**: Normal Quality.
   - **sRGB**: uključeno.

## Kako kod koristi teksturu (URAĐENO)

`SafeSpaceBuilder.TryBuildAuroraDome()` na `Build()`:
- Učita `Resources.Load<Texture2D>("SafeSpace/AuroraPanorama")`.
- Ako **postoji**: gradi veliku sferu `AuroraPanorama` (radius 400, negativna X skala →
  normale ka unutra) sa `RuntimeVisualUtil.UnlitTextured` (equirectangular, unlit), sakrije
  drifting Sun, i postavi **plavo-zeleno ambijentalno svjetlo** (spec §10). `AuroraApplied=true`.
- Ako **ne postoji**: zadrži postojeći gradijentni `SkyDome` (graceful fallback) + Console
  napomenu. `AuroraApplied=false`. Test `SafeSpace_FallsBackToGradientDome_WhenAuroraTextureMissing`.

## Zahtjevi iz spec-a i status

| Zahtjev | Status |
|---|---|
| Panorama okružuje cijeli Safe Space | ✅ inward sfera radius 400 |
| Bez crnih zona | ✅ (kada je tekstura uvezena; fallback je gradijent, ne crno) |
| Bez paralakse / mirna | ✅ sfera je statična, nema camera motion |
| Plavo-zelena ambijentalna svjetlost | ✅ SafeSpaceLight postaje (0.62,0.86,0.9) |
| Ne utiče na pressure/corridor | ✅ zaseban root, aktivira se samo u Safe Space fazama |
| Bez vidljivog vertikalnog šava | ⚠ primitivna sfera ima blagi šav na UV wrap-u i pinch na polovima |

## Alternativa za savršen 360 bez šava (opciono, NOT DONE)

Umjesto inward sfere: `Skybox/Panoramic` materijal + `RenderSettings.skybox`. Problem: skybox
je **globalan** i vidio bi se i u hodniku (ista scena). Trebalo bi ga mijenjati komponentom
pri ulasku/izlasku iz Safe Space-a (`SceneZoneController.ZoneChanged`). Inward sfera je
izabrana jer je self-contained i per-space; šav je prihvatljiv trade-off za MVP.

## Nije mijenjano

Trajanje breathing-reference faze, HR logika, SSQ/scoring — netaknuto.
