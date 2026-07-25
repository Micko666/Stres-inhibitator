# UX_UI_POLISH_REPORT.md — 2026-07-15

Prioritet 3 iz art/UX prolaza. Cilj: dosljedan, čistiji UX bez promjene toka ili pravila.

## Šta je urađeno u ovom prolazu

### Centralni dizajn sistem (spec §13) — `Assets/Scripts/UI/UiTheme.cs`
Jedan izvor tokena, koje `UiBuilder` sada čita (aliasi ostaju stabilni):

- Površine: `PanelBackground`, `PanelBorder`, `HeaderBackground`, `Card`, `CardAlt`
- Tekst: `TextPrimary`, `TextSecondary`
- Accent/status: `Accent` (cyan/aurora), `AccentSoft`, `Success`, `Warning`, `Danger`
- Dugmad: `ButtonNormal/Highlighted/Pressed/Selected/Disabled` + `Button(danger)` helper
- Razmaci: `SpacingXs/Sm/Md/Lg`; tipografija: `TitleSize/SubtitleSize/BodySize/CaptionSize/ButtonSize`

Stil: tamno plavo-siva osnova, umjeren cyan/aurora accent, visok kontrast teksta. Bez novog
UI framework-a, bez promjene font paketa (`RuntimeVisualUtil.BuiltinFont`).

`UiBuilder.PanelBg/HeaderBg/TextColor/TextDim/Accent/RowBg/RowSelectedBg/Warning` sada
pokazuju na `UiTheme`. Test `UiTheme_TokensAreDistinct_AndUiBuilderReadsThem`.

## Već popravljeno u prethodnom UX prolazu (2026-07-14) — i dalje važi

Ovi ekrani su stabilizovani u ranijem turnusu (vidi NEXT_AGENT_HANDOFF.md „UX KOREKCIJE"):

- **Profile UI** (§14): prava klikabilna dugmad (ray+trigger), vidljivo NAZAD, nema
  instrukcije za kontrolu koja ne radi, developer Neutral/ControlledPressure prekidač
  odvojen i vidljiv samo u developerModeEnabled.
- **Pause UX** (§19): panel „Pauza" + „Nastavi"/„Završi sesiju", tablet zatamnjen, task
  input blokiran, Y = jedan 3-2-1 countdown, konzola bez PAUSE dugmeta.
- **Task tablet / Flanker** (§17): Flanker koristi cijeli centralni StimulusArea bez kartice.
- **Tutoriali po nivou** (§18): stvarne razlike iz `TaskDifficultyConfig`, jedno Continue.
- **Summary** (§20): jedan red po zadatku „nivo X → nivo Y", razlog ispod
  (`AdaptationExplanationBuilder.BuildCompact`), scheduler debug samo dev.

## Šta OSTAJE (nije završeno u ovom prolazu — pošteno)

- **Primjena `UiTheme` na SVE panele pojedinačno** — tokeni postoje i `UiBuilder` ih čita,
  ali pojedinačni paneli (Questionnaire, FlowPanels, Info) i dalje koriste lokalne boje na
  par mjesta; nisu svi prevedeni na `UiTheme.*` reference. Vizuelno su konzistentni jer dijele
  `UiBuilder`, ali puni token-sweep nije urađen.
- **Tranzicije (§21)** — suptilni fade/scale-in nisu dodati. Foundation (jedan canvas,
  `ShowPanel`) postoji; tranzicije su zaseban, mjerljiv dodatak i nisu počete.
- **Pre-session ujednačavanje (§15)** — koristi postojeće panele/anchore; nije napravljen
  jedinstveni `PreSessionRoot`.
- **Zaobljeni uglovi** — traže sprite podršku; nisu dodati (ostaje pravougaono).

## Compile / EditMode / Play / Quest

- Compile: **NOT RUN** (korisnik). EditMode: novi/ažurirani testovi (vidi ART_UX_MANUAL_TEST_CHECKLIST).
- Play Mode / Quest: **NOT RUN**.
