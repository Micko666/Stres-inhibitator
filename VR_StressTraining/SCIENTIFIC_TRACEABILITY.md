# SCIENTIFIC_TRACEABILITY.md

Mapiranje funkcija sistema na naučnu osnovu. NIJEDNA projektna heuristika se ne
predstavlja kao naučno validirana; sve numeričke radne vrijednosti nose oznaku
PROJECT_HEURISTIC u kodu (centralizovane u StressTrainingConfig, TaskDifficultyConfig,
AdaptationConfig, PressureLevelConfig).

| Funkcija | Naučna osnova | Referenca | Projektna odluka | Heuristika | Potrebna izmjena rada |
|---|---|---|---|---|---|
| SIT priprema (coping/disanje/samoinstrukcije) | Stress Inoculation Training — faze edukacija→vježbanje→primjena | Meichenbaum (1985) | Coping faza poslije baseline-a, prije taskova; kratke samoinstrukcije | trajanje/ritam disanja = PROJECT_HEURISTIC; NE klinički tretman | opis SIT faze u metodologiji |
| VR za stres trening | VR omogućava kontrolisanu izloženost stresorima | Rizzo et al. (2011) | jedan hodnik, konzola, deterministički pritisak | — | — |
| HR kao proxy fiziološke pobuđenosti | HR raste sa simpatičkom aktivacijom; nije mjera "stresa" per se | standardna psihofiziologija | HR zone relativne prema baseline-u; bez HRV; bez real-time adaptacije | pragovi zona (baseline+10/+22) = PROJECT_HEURISTIC | ograničenja HR interpretacije |
| SSQ | validirani upitnik simulatorske mučnine, 16 stavki, N/O/D subskale | Kennedy, Lane, Berbaum & Lilienthal (1993) | pre/post svake sesije; safety+validity uloga | BCS prevod = NEEDS_VALIDATED_ITEM_TEXT; invalidacioni delta prag = PROJECT_HEURISTIC | navesti korišćenu verziju/prevod |
| Raw NASA-TLX | subjektivno opterećenje, 6 dimenzija, raw varijanta bez pairwise pondera | Hart & Staveland (1988); Hart (2006) za RTLX | poslije svake sesije; dimenzije ulaze u scheduler odvojeno | scheduler pragovi (frustracija≥60 itd.) = PROJECT_HEURISTIC | opravdati RTLX umjesto punog TLX |
| STAI-6 | kratka forma state anksioznosti (6 stavki, prorated 20–80) | Marteau & Bekker (1992) | samo prva/posljednja sesija ciklusa; NE ulazi u per-session adaptaciju | BCS prevod = NEEDS_VALIDATED_ITEM_TEXT | — |
| N-back | radna memorija; n-back paradigma | Kirchner (1958); Jaeggi et al. | 1-back/2-back nivoi; match/no-match dugmad | set size/tempo/window po nivou = PROJECT_HEURISTIC | — |
| Go/No-Go | inhibicija odgovora; commission/omission greške | standardna Go/No-Go paradigma (Donders tradicija) | crni GO / NO GO tekst na bijelom (bez boje kao značenja); prepotentnost preko proporcija i tempa | proporcije NO GO i prozori = PROJECT_HEURISTIC; rezultat je performansa sesije, ne osobina | opis redizajna stimulusa |
| Flanker | interferencija distraktora; congruency effect | Eriksen & Eriksen (1974) | centralna strelica određuje odgovor; LEFT/RIGHT fizička dugmad na odgovarajućim stranama | proporcije nekongruentnih/prozori = PROJECT_HEURISTIC | — |
| Corsi-inspirisan zadatak | vizuoprostorna sekvencijalna memorija; digitalna implementacija Corsi block-tapping | **Brunetti, Del Gatto & Delogu (2014), "eCorsi", Frontiers in Psychology 5:939, doi:10.3389/fpsyg.2014.00939 — REFERENCA 34** | 9 identičnih neoznačenih dugmadi u nepravilnom rasporedu (kao Corsi tabla); tablet prikazuje sekvencu, konzola prima odgovor; prva greška završava pokušaj; forward (L1/L2) i backward (L3) | dužine sekvenci, tempo prikaza, response prozori = PROJECT_HEURISTIC; NIJE standardizovani klinički Corsi test | dodati sekciju o četvrtom zadatku + referencu 34 |
| Corsi first-error pravilo | u block-tapping zadacima pogrešan blok prekida reprodukciju pokušaja | praksa eCorsi implementacije (ref. 34) | prvi pogrešan pritisak → trial neuspješan, čuva se correct prefix | — | — |
| Inter-session adaptacija | postupno doziranje izazova između sesija (ne unutar sesije) | SIT princip graduirane izloženosti | rule-based scheduler poslije sesije; težina fiksna tokom sesije | svi pragovi = PROJECT_HEURISTIC | opis pravila |
| Task/pressure razdvajanje | odvajanje kognitivnog opterećenja od anksiogenog konteksta | dizajn odluka projekta | task nivo i globalni pritisak se rutinski ne povećavaju istovremeno | PROJECT_HEURISTIC pravilo | obrazložiti |
| Seed determinizam | reproducibilnost eksperimenta | metodološki standard | svi seedovi sačuvani; neutral/pressure dijele task seedove | — | opis u metodologiji |
| Pressure timeline (70/50/30/10/0%) | perceptivni pritisak bez fizičkog kontakta; predvidljiv rast | projektna odluka (bez direktne reference) | deterministički eventi vezani za procenat preostalog vremena | pragovi 70/50/30/10 i intenziteti = PROJECT_HEURISTIC | opisati kao dizajn odluku |
| Time penalties | — | nema potvrđene osnove | arhitektura postoji, PODRAZUMIJEVANO ISKLJUČENO | cijela funkcija = PROJECT_HEURISTIC | odluka se tek donosi |
| 3-of-4 izbor taskova | balansirana rotacija uslova kroz sesije | metodološka praksa counterbalancing-a | seeded selekcija sa zabranom dvostrukog uzastopnog izostavljanja | tie-breaking pravila = PROJECT_HEURISTIC | opisati algoritam izbora |

## Neriješene naučne odluke (za studenta/mentora)

1. Tačno trajanje baseline-a i recovery-ja u produkciji (trenutno 120 s / 90 s PROJECT_HEURISTIC).
2. SSQ delta prag za invalidaciju sesije (trenutno ≥20 total-score jedinica PROJECT_HEURISTIC).
3. Da li evaluacioni protokol zahtijeva isto globalno trajanje u neutral i pressure uslovu (kod podržava oba).
4. Da li vremenske kazne ulaze u finalni protokol (arhitektura spremna, isključeno).
5. Validirani BCS prevodi SSQ/NASA-TLX/STAI-6 stavki (svi trenutni prevodi su radni).
6. Corsi backward nivo: da li ulazi u studiju ili ostaje opcija (implementiran kao L3).

---

## Dodatak — HR akvizicija (2026-07-14, standalone HR pipeline)

- Mjerenje pulsa (Xiaomi Smart Band 9) je **inženjerski akvizicioni put**, ne validirani
  klinički instrument. U pisanom radu opisati kao "consumer-grade optički PPG senzor
  (Xiaomi Smart Band 9), HR uzorci ~1 Hz preuzeti iz Mi Fitness zapisa i proslijeđeni u
  VR sistem lokalnom vezom". Ne predstavljati kao medicinski/EKG-validiran signal.
- Zone (Stabilno/Povišeno/Visoko) su relativne u odnosu na sopstveni baseline korisnika
  (HeartRateZoneEvaluator), pragovi su PROJECT_HEURISTIC (HrZoneConfig) — dokumentovati
  kao heuristiku, ne kao dijagnostičke granice.
- Simulirani HR se koristi ISKLJUČIVO u razvoju/testovima i nikad ne otključava
  produkcijsku sesiju (GetProductionReadiness gate) — bitno za validnost podataka studije.
- Puna arhitektura i granice: STANDALONE_HR_ARCHITECTURE.md, STANDALONE_HR_KNOWN_LIMITATIONS.md.

---

## METODOLOŠKA PROMJENA — 2026-07-14: NeutralBaseline → BreathingReferenceBaseline

**Šta se promijenilo:** faza vođenog disanja (coping) i referentno mjerenje pulsa
sada se izvode **istovremeno, kao jedna faza** (`BreathingReferenceBaseline`).

**Posljedica za rad (OBAVEZNO opisati u metodologiji):**

> Referentna vrijednost pulsa **NIJE** dobijena standardnim neutralnim mjerenjem u
> mirovanju (resting baseline). Učesnik tokom mjerenja **namjerno diše po vođenom
> ritmu** (PROJECT_HEURISTIC parametri: udah 4 s / izdah 6 s). Zato je ovo
> **projektno definisano referentno mjerenje uz vođeno disanje**
> (*project-defined breathing-assisted reference*), a ne neutralni resting baseline.

**Ne smije se tvrditi:**
- da je to neutralno/mirno resting baseline mjerenje;
- da je to validirani protokol;
- bilo kakva nova naučna tvrdnja izvedena iz same promjene.

**Šta ostaje isto:** zone (Stabilno/Povišeno/Visoko) i dalje se računaju **relativno**
u odnosu na tu referentnu vrijednost (`HeartRateZoneEvaluator`), pragovi su PROJECT_HEURISTIC.

**Kvalitet podataka:** HR se snima kontinuirano kroz cijelu sesiju, ali **samo** uzorci iz
te faze ulaze u referentnu vrijednost. Prekid signala se ne kompenzuje i posljednji BPM se
ne ponavlja — rupa ostaje rupa i ulazi u metrike kvaliteta
(`BaselineQuality`, `SessionValidityEvaluator`). Ako nema dovoljno validnih uzoraka,
sesija nije scheduler-eligible; vrijednost se **ne izmišlja**.

Detalji: `FINAL_SESSION_FLOW_AUDIT.md`.
