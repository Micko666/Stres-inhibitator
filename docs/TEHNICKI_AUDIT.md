# Tehnički audit artefakta

**Projekat:** Adaptive Stress Corridor VR — „Virtualna realnost za izgradnju otpornosti na stres"
**Datum audita:** 2026-09-18
**Verzija artefakta:** tag `v1.0-thesis`
**Unity:** 6000.3.16f1 (rev `a56f230f6470`)

Ovaj dokument bilježi stanje utvrđeno čitanjem izvornog koda, testova i sačuvanih
zapisa sesija. Izvor istine je **kod u ovom repozitorijumu**, ne ranija dokumentacija.
Gdje se dokumentacija razlikovala od koda, mjerodavan je kod.

Audit ne dokazuje da sistem povećava otpornost na stres, da ima kliničku efikasnost,
niti da su pragovi psihološki optimalni. Empirijska evaluacija sa korisnicima nije
predmet rada. Dokazuju se isključivo osobine samog artefakta i njegovog ponašanja.

---

## 1. Rezultat kompletnog test skupa

Pokrenuto nad neizmijenjenim projektom, Unity u batch režimu:

```
Unity verzija:   6000.3.16f1
NUnit engine:    3.5.0.0
Test mode:       EditMode
Ukupan broj:     386
Passed:          386
Failed:          0
Skipped/Ignored: 0
Inconclusive:    0
Trajanje:        7.87 s
Vrijeme:         2026-09-18 17:20:25Z → 17:20:33Z
Rezultat:        Passed
```

Komanda:

```bash
Unity.exe -runTests -batchmode -projectPath VR_StressTraining -testPlatform EditMode -testResults results.xml -logFile run.log
```

Broj 386 se nezavisno potvrđuje i iz izvornog koda: 349 metoda sa `[Test]` plus
37 instanci `[TestCase(...)]`, raspoređenih u 33 test klase.

Pored toga postoji **1 PlayMode test** (`VerticalSliceSmokeTests`, `[UnityTest]`).
On nije obuhvaćen brojem 386 i nije pokrenut u ovom auditu.

**Code coverage izvještaj ne postoji** i paket `com.unity.testtools.codecoverage`
nije instaliran. Procenat pokrivenosti se zato nigdje ne navodi.

---

## 2. Struktura test skupa

### 2.1 Po oblastima

| Oblast provjere | Primjeri provjerenih svojstava | Broj testova |
|---|---|---:|
| Adaptivni scheduler | prioritet pravila, ograničenje nivoa 1–3, zadržavanje pri nedostajućim podacima, determinizam odluke | 26 |
| Lanac mjerenja pulsa | odbacivanje duplikata, paketa van redosljeda, zastarjelih vremenskih oznaka i paketa tuđe sesije; uslov za produkcijski prijem | 33 |
| Tok sesije i trajnost | procjena validnosti, prekid sesije, istek vremena, idempotentno upisivanje, oporavak oštećenog profila | 73 |
| Kognitivni zadaci | generisanje stimulusa po nivoima, bodovanje, Corsi raspon, Flanker redovi distraktora | 70 |
| Sistem pritiska i koridor | faze vremenske linije, ograničenja intenziteta po nivou, segmenti koridora, posljedica pada | 122 |
| Korisnički interfejs i upitnici | bodovanje NASA-TLX i SSQ, orijentacija panela, unos na konzoli, validacija imena | 62 |
| **Ukupno (EditMode)** | | **386** |

Svaka test klasa je pripisana svojoj dominantnoj oblasti; pojedine klase dodiruju
više oblasti.

### 2.2 Po test klasama

| Test klasa | Testova |
|---|---:|
| FablePressureAnimationRefactorTests | 54 |
| FablePuzzleCorridorTests | 30 |
| FlankerDistractorRowTests | 29 |
| FableUxCorrectionTests | 27 |
| FablePressureTests | 21 |
| SchedulerConsistencyTests | 21 |
| FableCorsiTests | 20 |
| SessionAbortFlowTests | 20 |
| FableProductionSkeletonTests | 17 |
| QuestionnairePanelTests | 17 |
| FableFinalIntegrationTests | 16 |
| FableHrArchitectureTests | 13 |
| FableStabilizationTests | 9 |
| ConsoleInteractionTests | 8 |
| DemoTaskFactoryTests | 8 |
| FablePhase86Tests | 8 |
| HeartRateServiceTests | 8 |
| HrRelayIngestTests | 7 |
| DeveloperSandboxIsolationTests | 6 |
| FableSafeSpaceAndUiTests | 6 |
| SessionClockTests | 6 |
| FlankerTaskTests | 5 |
| HrRelayAckProtocolTests | 5 |
| NBackTaskTests | 5 |
| PressureInsufficientDataTests | 5 |
| AppStateMachineTests | 4 |
| GoNoGoTaskTests | 3 |
| ProfileRepositoryTests | 2 |
| UsernameValidatorTests | 2 |
| AtomicFileWriterTests | 1 |
| DemoSummaryFormatterTests | 1 |
| SafeSpaceUiLayoutTests | 1 |
| WorldSpaceUiOrientationTests | 1 |

Nijedan test nije označen sa `[Ignore]` ili `[Explicit]`; nijedan ne postoji a da
se ne izvršava.

### 2.3 Stepen pokrivenosti po svojstvu

Razlikuju se tri nivoa: **DIREKTAN** (test tvrdi baš to svojstvo), **INDIREKTAN**
(kod se izvršava, ali svojstvo nije predmet tvrdnje) i **NETESTIRANO**.

| Svojstvo | Nivo | Osnov |
|---|---|---|
| prioritet pravila | DIREKTAN | 3 testa redosljeda |
| ograničenje nivoa zadatka i pritiska | DIREKTAN | `LevelsStayWithinOneToThree` |
| determinizam odluke | DIREKTAN (jedan ulazni vektor) | `DecisionIsDeterministic` |
| zadržavanje pri nedostajućem pulsu | DIREKTAN | 3 testa |
| zadržavanje pri nedostajućem NASA-TLX | DIREKTAN | 3 testa |
| duplikat HR paketa | DIREKTAN | `DuplicateSequenceIsRejected` |
| paket van redosljeda | DIREKTAN | `OutOfOrderSequenceIsRejected` |
| zastarjela vremenska oznaka | DIREKTAN | `StaleWatchTimestampIsRejectedEvenWhenSequenceAdvances` |
| paket tuđe sesije | DIREKTAN | `FirstTokenBindsAndDifferentTokenIsRejected` |
| ponovno pokretanje pošiljaoca | DIREKTAN | `SenderRestartAtSequenceZeroRecovers` |
| procjena validnosti sesije | DIREKTAN | 8 statusa se pojavljuje u tvrdnjama |
| istek vremena sesije | DIREKTAN | `Validity_TimeExpired_YieldsIncompleteTimeExpired` |
| idempotentnost primjene sesije | DIREKTAN | `SameSessionAppliedTwice_ChangesNothingTheSecondTime` |
| trajnost i oporavak profila | DIREKTAN | 3 testa |
| zapis razloga odluke | DIREKTAN | `DecisionStaysTraceable` |
| izbor 3 od 4 zadatka | DIREKTAN | `PreviouslyOmittedTask_IsNeverOmittedTwiceInARow` |
| determinizam sjemena | INDIREKTAN | `TaskSeedService` je čista funkcija; nema testa koji poredi dva izvođenja |
| determinizam redosljeda blokova | INDIREKTAN | provjerava se struktura plana, ne determinizam |
| neispravan (malformed) paket | INDIREKTAN | parser se poziva validnim ulazima; nema testa sa pokvarenim JSON-om |
| razdvojenost profila | SLAB DIREKTAN | poredi dva objekta u memoriji; razdvajanje na disku nije testirano |

---

## 3. Adaptivni scheduler

Fajl: `Assets/Scripts/Adaptation/AdaptationRuleSet.cs`
Pragovi: `Assets/Scripts/Adaptation/AdaptationConfig.cs`

Pravila su uređena; **prvo pravilo koje vrati direktiv pobjeđuje**. Identifikatori
pravila u kodu su semantički stringovi (`T_STABILITY_REPEAT`, `P_ALL_STABLE_INCREASE`
i slično), ne numeričke oznake. Numeracija ispod je pozicija u tabeli, radi
lakšeg pozivanja u tekstu rada.

### 3.1 Pravila za težinu zadatka (10)

| # | Identifikator | Uslov | Ishod |
|---|---|---|---|
| T1 | `T_STABILITY_REPEAT` | standardna devijacija tačnosti po blokovima > 0,18 | ponovi nivo |
| T2 | `T_LOW_ACC_LOW_AROUSAL_REPEAT` | tačnost < 0,55 uz nisku pobuđenost i nisko opterećenje | ponovi nivo |
| T3 | `T_LOW_ACC_DECREASE` | tačnost < 0,55 | smanji |
| T4 | `T_FRUSTRATION_HOLD` | frustracija ≥ 60 | zadrži |
| T5 | `T_HIGH_ACC_HIGH_COST_HOLD` | tačnost ≥ 0,85 uz visoku fiziološku ili subjektivnu cijenu | zadrži |
| T6 | `T_TEMPORAL_DOMINANT_HOLD` | tačnost ≥ 0,85 uz dominantnu vremensku dimenziju | zadrži |
| T7 | `T_MENTAL_DOMINANT_MEMORY_HOLD` | memorijski zadatak, tačnost ≥ 0,85, dominantna mentalna dimenzija | zadrži |
| T8 | `T_INSUFFICIENT_DATA_HOLD` | tačnost ≥ 0,85 a cijena se ne može procijeniti | zadrži |
| T9 | `T_HIGH_ACC_INCREASE` | tačnost ≥ 0,85 | povećaj |
| T10 | `T_DEFAULT_HOLD` | uvijek | zadrži |

### 3.2 Pravila za globalni nivo pritiska (8)

| # | Identifikator | Uslov | Ishod |
|---|---|---|---|
| P1 | `P_TASK_INCREASED_HOLD` | bilo koji zadatak je povećan u ovoj odluci | zadrži |
| P2 | `P_HIGH_COST_DECREASE` | visoka cijena, spor oporavak i potvrda koja nije iz pulsa | smanji |
| P3 | `P_HIGH_COST_HR_ONLY_HOLD` | visoka cijena i spor oporavak, bez potvrde van pulsa | zadrži |
| P4 | `P_HIGH_COST_HOLD` | visoka cijena bez sporog oporavka | zadrži |
| P5 | `P_TASKS_STRUGGLING_HOLD` | neki zadatak je smanjen ili ponovljen | zadrži |
| P6 | `P_INSUFFICIENT_DATA_HOLD` | cijena sesije se ne može procijeniti | zadrži |
| P7 | `P_ALL_STABLE_INCREASE` | svi izvedeni zadaci iznad praga visoke tačnosti | povećaj |
| P8 | `P_DEFAULT_HOLD` | uvijek | zadrži |

### 3.3 Dosegljivost pravila

Statičkom analizom je za **svih 18 pravila** utvrđena bar jedna kombinacija ulaza
pri kojoj bi pravilo bilo prvo zadovoljeno. Nijedno pravilo nije mrtvo i nijedno
nije trajno presječeno ranijim pravilom. Konkretni ulazi po pravilu izvedeni su iz
pragova navedenih u odjeljku 4.

Dvije napomene o strukturi:

1. **T9 se oslanja na redosljed.** Njegov predikat je samo „tačnost ≥ 0,85"; uslov
   „uz prihvatljivu cijenu i uz postojeći dokaz cijene" ostvaruju T5 i T8, koji stoje
   ispred njega. Promjena redosljeda pravila promijenila bi ponašanje T9.

2. **P7 ima i drugi put.** Zastavica „neki zadatak je povećan" računa se *nakon*
   ograničenja nivoa. Zadatak koji je već na najvišem nivou biva zadržan i zato ne
   blokira povećanje pritiska. Kad su svi zadaci na maksimumu, a tačnost visoka,
   pritisak može porasti.

3. **P3 nije dostižan preko frustracije, i to je namjerno.** Visoka frustracija
   istovremeno aktivira i „visoku cijenu" i „potvrdu van pulsa", pa pobjeđuje P2.
   P3 ostaje rezervisan za slučaj kada visoka cijena dolazi isključivo iz pulsa.

### 3.4 Načelo nedostajućih podataka

Dva čuvara sprečavaju da odsustvo mjerenja postane razlog za veći zahtjev:

- `HasCostEvidence` — po zadatku; traži poznat prosječni otkucaj za taj zadatak i
  popunjen NASA-TLX. Bez toga se aktivira T8.
- `HasSessionCostEvidence` — po sesiji; traži poznat prosječni otkucaj sesije i
  popunjen NASA-TLX. Bez toga se aktivira P6.

Udio vremena provedenog u povišenoj zoni **namjerno se ne prihvata kao dokaz**:
ta vrijednost je nula i kada puls nikad nije porastao i kada nikad nije ni primljen,
pa ne razlikuje jeftinu sesiju od neizmjerene.

Nedostajuće vrijednosti nose sentinele: `-999` za razliku otkucaja u odnosu na
referentnu vrijednost, `-1` za tačnost i za NASA-TLX.

---

## 4. Heuristički pragovi

Sve vrijednosti su u kodu označene kao `PROJECT_HEURISTIC` — radne vrijednosti
ovog prototipa, centralizovane radi podešavanja, **bez tvrdnje o validaciji**.

### 4.1 Pragovi odluke (`AdaptationConfig.cs`)

| Parametar | Vrijednost | Gdje utiče |
|---|---:|---|
| `accuracyHigh` | 0,85 | T5, T6, T7, T8, T9, P7 |
| `accuracyLow` | 0,55 | T2, T3 |
| `frustrationHigh` | 60 | T4, visoka cijena, potvrda van pulsa |
| `tlxTotalHigh` | 65 | visoka cijena, potvrda van pulsa |
| `tlxTotalLow` | 30 | T2 |
| `dominantDimensionMin` | 60 | T6, T7 |
| `dominantDimensionMargin` | 10 | T6, T7 |
| `sessionDeltaBpmHigh` | 18 | visoka cijena |
| `sessionDeltaBpmLow` | 5 | niska pobuđenost, T2 |
| `elevatedZoneRatioHigh` | 0,50 | visoka cijena na nivou sesije |
| `recoverySlowSeconds` | 60 | spor oporavak → T5, P2, P3, P4 |
| `blockAccuracyStdHigh` | 0,18 | T1 |
| `minLevel` / `maxLevel` | 1 / 3 | ograničenje nivoa zadatka |
| `minPressureLevel` / `maxPressureLevel` | 1 / 3 | ograničenje nivoa pritiska |

### 4.2 Pragovi validnosti (`SessionValidityEvaluator.cs`)

| Parametar | Vrijednost | Uloga |
|---|---:|---|
| `ssqPostPreDeltaInvalid` | 20 | porast SSQ rezultata koji poništava sesiju |
| `minHrValidSampleRatio` | 0,30 | ispod ovoga sesija dobija upozorenje |
| `hrRequiredForValidity` | false | gubitak pulsa degradira sesiju, ne poništava je |
| `maxTechnicalWarningsForClean` | 2 | iznad ovoga sesija je „validna uz upozorenja" |

### 4.3 Pragovi vremenske linije pritiska (`PressureTimeline.cs`)

Preostali udio vremena: 0,70 / 0,50 / 0,30 / 0,10 / 0,00 razdvajaju faze
Stable → Early → Mid → Late → Critical → Expired.

**Ovo nisu granice adaptivne odluke.** Ne pojavljuju se ni u jednom pravilu
schedulera; određuju kada scena eskalira *unutar* jedne sesije. Njihova analiza
je pitanje dizajna doživljaja, a ne osjetljivosti adaptivnog ponašanja, i zato se
vodi odvojeno.

---

## 5. Lanac mjerenja pulsa

### 5.1 Produkcijska putanja

```
Xiaomi Smart Band 9
  → Mi Fitness (telefon)
  → HR relej (Android, vlastita aplikacija)
  → lokalna mreža, UDP, protokol verzije 1
  → Quest / Unity: parser → ulazni filter → HeartRateService
  → agregati po zadatku i po sesiji
  → scheduler (nakon zatvaranja sesije)
```

Raniji most zasnovan na Python skripti i dalje se prihvata radi kompatibilnosti,
ali produkcijski tok ide preko telefonskog releja.

### 5.2 Ulazni filter

`HrRelayIngest.Evaluate` primjenjuje, tim redom:

1. **identitet sesije** — vezuje se za prvi viđeni token i odbacuje svaki paket sa
   drugim tokenom;
2. **redosljed sekvence** — odbacuje duplikate i zaostale pakete; sekvenca nula se
   tumači kao ponovno pokretanje pošiljaoca i ponovo postavlja osnovu;
3. **razdvajanje otkucaja srca od signala života** — „heartbeat" paket održava vezu
   svježom ali nikad ne postaje fiziološki uzorak;
4. **monotonost vremena sa sata** — uzorak čija oznaka nije novija od najnovije
   prihvaćene se odbacuje.

Filter broji odbacivanja po razlogu (duplikat ili van redosljeda, zastarjelo,
pogrešan token, ukupno).

**Ti brojači se trenutno ne upisuju ni u jedan fajl.** Postoje samo u memoriji
tokom rada. To je najjeftinija instrumentacija koja nedostaje: jedno polje u zapisu
sesije pretvorilo bi „filter postoji" u „filter je odbacio N paketa, po ovim
razlozima".

### 5.3 Vremenske veličine — šta se smije tvrditi

Tri različite veličine se ne smiju miješati.

**A. Interval između vrijednosti otkucaja — MJERLJIVO.**
Iz sačuvanih zapisa sesija: medijana **1,53 s**, deveti decil 4,0 s. Uzastopno
ponovljena vrijednost javlja se u oko 2 % slučajeva, pa sat zaista uzorkuje često
umjesto da ponavlja jednu vrijednost.

**B. Kašnjenje prenosa telefon → Quest — NIJE POUZDANO MJERLJIVO.**
Zapisuje se polje „starost uzorka", izračunato kao razlika vremena prijema na
Questu i vremenske oznake izvora. Medijana je reda **1,4 s**. Međutim, **satovi
nisu sinhronizovani** — ta razlika sadrži i stvarno kašnjenje i nepoznat pomak
između satova, i ta dva se iz postojećih podataka ne mogu razdvojiti. Vremenska
oznaka izvora ima rezoluciju od jedne sekunde, što dodatno ograničava preciznost.

Smije se reći: *starost uzorka u trenutku upotrebe je reda jedne do dvije sekunde.*
Ne smije se reći: *izmjereno kašnjenje prenosa iznosi toliko.*

**C. Kašnjenje od stvarnog otkucaja do Questa — NIJE MJERLJIVO.**
Tri nezavisna razloga: značenje vremenske oznake koju daje proizvođač sata nije
dokumentovano ni dokazano; sat nije sinhronizovan sa Questom; uređaj ne izlaže
sirove intervale između otkucaja, nego samo izvedenu vrijednost. Za ovu veličinu
bilo bi potrebno referentno mjerenje izvan opsega ovog rada.

### 5.4 Vremensko vezivanje pulsa za zadatak

| Nivo | Veza u zapisu | Ulazi u adaptaciju |
|---|---|---|
| sesija | prosjek nepauziranih uzoraka | **da** |
| zadatak / blok | prosjek tokom bloka | **da** |
| blok | oznaka bloka na uzorku | ne |
| pokušaj | oznaka pokušaja na uzorku | **ne** |
| faza pritiska | oznaka stanja na uzorku | ne |

Uzorci se označavaju sve do nivoa pojedinačnog pokušaja radi kasnije analize, ali
**nijedno pravilo ne čita pojedinačni uzorak**. Adaptacija se donosi iz agregata
cijelog bloka i cijele sesije, i to tek nakon što je sesija zatvorena.

Posljedica: kašnjenje reda sekunde i po jeste metodološko ograničenje za svaku
buduću analizu na nivou pokušaja, ali ne utiče na adaptivnu odluku, jer se prosjek
preko stotina uzoraka time ne mijenja značajno.

---

## 6. Ponašanje pri otkazima

| Slučaj | Reakcija sistema | Test |
|---|---|---|
| duplikat paketa | odbacuje se, broji se | direktan |
| zastarjela oznaka | odbacuje se, broji se | direktan |
| paket van redosljeda | odbacuje se, broji se | direktan |
| zakašnjeli paket u ispravnom redosljedu | prihvata se; starost se bilježi; označava se kao zastario ako pređe prag | djelimičan |
| neispravan paket | ne postaje uzorak | indirektan |
| paket tuđe sesije | odbacuje se, broji se | direktan |
| puls nestane | detektuje se gubitak, zona prelazi u „signal izgubljen", vrijednost se ne zamrzava | direktan |
| nedovoljno uzoraka pulsa | sesija dobija upozorenje, ne poništava se | direktan |
| NASA-TLX nedostaje | T8 i P6 zadržavaju nivoe | direktan |
| sesija prekinuta | svi nivoi zamrznuti, podaci sačuvani | direktan |
| isteklo vrijeme | zaseban ishod, nije tehnički kvar | direktan |
| sesija nevalidna | podaci sačuvani, adaptacija se ne hrani | direktan |
| ista sesija obrađena dvaput | druga obrada ne mijenja ništa | direktan |
| isti prihvaćeni ulaz ponovo | ista pravila, isti direktiv | direktan |

### 6.1 Dvije tvrdnje koje se ne smiju izjednačiti

**Tvrdnja koja stoji.** Za isti prihvaćeni snimak ulaznih podataka i isto prethodno
stanje profila, scheduler daje isti rezultat. Funkcija odlučivanja nema generator
slučajnih brojeva, ne čita vrijeme ni u jednoj grani i ne iterira preko neuređenih
kolekcija. Jedino polje koje se razlikuje između dva izvođenja je vremenska oznaka
donošenja odluke, koja je metapodatak.

**Tvrdnja koja NE stoji.** Da različite sekvence mrežnih događaja uvijek daju istu
adaptivnu odluku. Ulazni filter je **namjerno zavisan od redosljeda dolaska**:
prihvata samo monotono rastući niz. Isti paketi koji stignu drugačijim redom daju
drugačiji skup prihvaćenih uzoraka, a time potencijalno i drugačiji prosjek. U
jednom stvarnom zapisu raspon sekvenci pokriva 571 poziciju, a prihvaćen je 381
uzorak.

Ispravna formulacija je da mrežni poremećaj **ne može unijeti neprovjeren uzorak u
odluku**, a ne da je skup prihvaćenih uzoraka nezavisan od redosljeda.

---

## 7. Idempotentnost

Čuvar se nalazi u `UserProfileService.CompleteSession`. Identifikator je
**oznaka sesije**. Prije ijedne izmjene provjerava se da li je sesija sa tom
oznakom već u istoriji profila; ako jeste, metoda odmah izlazi.

Zbog toga druga obrada iste sesije **ne može**:

- pomjeriti nivo nijednog zadatka,
- pomjeriti nivo pritiska,
- uvećati redni broj sesije u ciklusu,
- uvećati brojač završenih sesija u ciklusu,
- dodati duplikat u istoriju.

Provjereno testom `SameSessionAppliedTwice_ChangesNothingTheSecondTime`, koji drugu
obradu poziva sa odlukom koja bi sve nivoe podigla da je propuštena.

**Ograničenja.** Čuvar štiti profil. On ne sprečava ponovno izvođenje samog
schedulera u toku jedne sesije — to bi dalo ista pravila i isti direktiv, ali novu
vremensku oznaku. Sinhronizacije nema; sve se izvršava na glavnoj niti.

---

## 8. Sljedivost odluke

Uz svaku sesiju se trajno čuva:

| Podatak | Prisutan |
|---|---|
| prethodni nivoi po zadatku i za pritisak | da |
| novi nivoi | da |
| pun snimak ulaza koji je scheduler vidio | da |
| status validnosti | da |
| lista aktiviranih pravila | da |
| šifre razloga po zadatku | da |
| čitljivo objašnjenje za korisnika | da |
| vrijeme donošenja odluke | da |
| oznaka sesije | da |
| verzija schedulera | da |
| oznaka profila | posredno, preko putanje do zapisa |

### 8.1 Primjer stvarnog zapisa

```
SESSION    trajanje 615,3 s aktivno, validnost: valid, uslov: neutralan
INPUT      referentna vrijednost 73,3
           N-back   nivo 1  izveden  tačnost 0,889  SD 0,079
           Go/No-Go nivo 2  NIJE izabran u ovoj sesiji
           Flanker  nivo 1  izveden  tačnost 0,958  SD 0,029
           Corsi    nivo 1  izveden  tačnost 0,000  SD 0,000
           NASA-TLX ukupno 16,7   frustracija 0,0

RULE       NBack:T_HIGH_ACC_INCREASE
           GoNoGo:NOT_SELECTED_THIS_SESSION
           Flanker:T_HIGH_ACC_INCREASE
           CorsiSequence:T_LOW_ACC_LOW_AROUSAL_REPEAT
           P_TASK_INCREASED_HOLD

DECISION   N-back   povećanje  1 → 2
           Go/No-Go zadržano   2 → 2
           Flanker  povećanje  1 → 2
           Corsi    ponavljanje 1 → 1
           pritisak zadržano   1 → 1

REASON     „Globalni nivo pritiska je zadržan jer se u ovoj odluci povećava
            težina zadatka — nikad oboje istovremeno."
```

Zapis ilustruje načelo da nulta tačnost nije automatski kažnjena: uz nisku
pobuđenost i malo subjektivno opterećenje, nivo se ponavlja umjesto da se smanji,
jer je vjerovatnije da zadatak nije bio shvaćen nego da je bio pretežak.

### 8.2 Sljedivo nije isto što i potpuno ponovljivo

Sačuvani snimci ulaza nastali su prije nego što je u model ulaza dodato polje koje
razlikuje *neizmjeren* oporavak od *sporog* oporavka. Iz zapisa se **u potpunosti
vidi šta je odlučeno i na osnovu čega**, ali ponovno izvođenje starog snimka kroz
današnja pravila ne bi bila vjerna rekonstrukcija, jer bi to polje dobilo
podrazumijevanu vrijednost.

Ovu razliku treba držati i u tekstu rada: sistem je **sljediv**, nije tvrđeno da je
svaka istorijska odluka **ponovljiva bit po bit**.

---

## 9. Trajnost podataka

```
StressTrainingData/
├─ profiles/           index.json, <profil>.profile.json, backups/
├─ sessions/<profil>/<sesija>/
│                     session.json, trials.jsonl, hr.jsonl,
│                     events.jsonl, questionnaires.json
├─ config/
└─ logs/
```

Zapisi tokom sesije pišu se kao **JSONL sa dodavanjem na kraj**; profil se upisuje
**atomično**, preko privremenog fajla i zamjene, uz čuvanje prethodne ispravne
kopije. Oporavak iz oštećenog profila je pokriven testom.

**Poznato ograničenje.** Ako se proces prekine usred upisa, posljednja linija
`hr.jsonl` može ostati nedovršena. U pregledanom skupu zapisa to se dogodilo u
6 od 32 fajla, i svaki put je to bila tačno posljednja linija. Podaci prije nje su
netaknuti. Svaki alat koji čita ove zapise mora tolerisati nepotpunu posljednju
liniju.

---

## 10. Šta ostaje neprovjereno

Pošteno navedeno, jer se na to ne smije oslanjati:

1. **Nijedna sačuvana adaptivna odluka ne potiče od trenutne verzije koda.**
   Postojeći zapisi odluka stariji su od posljednjih izmjena schedulera.
2. **Prosječna razlika otkucaja na nivou sesije nije potvrđena u stvarnoj sesiji
   na trenutnoj verziji.** U sačuvanim odlukama ta vrijednost nosi sentinel
   „nepoznato". Ako se to ponovi i danas, čuvar P6 bi zadržavao pritisak trajno.
   Provjera zahtijeva jednu produkcijsku sesiju sa povezanim uređajem.
3. **Razdvojenost profila na disku nije testirana** — struktura putanja je razdvaja,
   ali nijedan test ne drži dva profila istovremeno.
4. **Ponašanje pri neispravnim paketima nije iscrpno testirano.**
5. **Osjetljivost na pragove nije mjerena.** Nijedan sistematski pomjeraj pragova
   nije izveden.
6. **Oscilacija nivoa kroz više sesija nije ispitana.** Za to bi bio potreban
   sintetički profil ponašanja; iz samog schedulera se ne može zaključivati koliko
   bi se stvarni korisnik „stabilizovao", jer scheduler ne sadrži model korisnika.
7. **PlayMode test nije pokrenut** u ovom auditu.

---

## 11. Tvrdnje koje se ne smiju izvoditi iz ovog audita

- da različite sekvence mrežnih događaja uvijek daju istu odluku;
- da je kašnjenje prenosa izmjereno;
- da je poznato vrijeme od otkucaja srca do Questa;
- da su sačuvane odluke potpuno ponovljive kroz današnja pravila;
- da je pokrivenost koda bilo koji procenat;
- da su heuristički pragovi validirani;
- da je broj otkucaja srca mjera stresa;
- da je poznato koliko sesija treba korisniku do stabilnog nivoa;
- da sistem povećava otpornost na stres.

---

## 12. Napomena o higijeni repozitorijuma

Istorija repozitorijuma je 2026-09-18 ponovo zasnovana kako bi se iz nje uklonili
rani eksperimentalni fajlovi koji nisu dio artefakta i nisu smjeli biti
distribuirani. Pripadajući pristupni podaci su poništeni. Folder sa tim ranim
eksperimentima trajno je isključen iz repozitorijuma.

Iz repozitorijuma su namjerno izostavljeni i: literatura (radovi trećih lica pod
autorskim pravom), lične fotografije, ADB binarni alati i Unity `Library` folder.

---

## 13. Kako ponoviti provjeru

```bash
git clone https://github.com/Micko666/Stres-inhibitator.git
cd Stres-inhibitator
git checkout v1.0-thesis
```

Otvoriti `VR_StressTraining/` u Unity Hub-u verzijom 6000.3.16f1, sačekati uvoz,
pa `Window → General → Test Runner → EditMode → Run All`.

Očekivano: **386 testova, svi prolaze**.
