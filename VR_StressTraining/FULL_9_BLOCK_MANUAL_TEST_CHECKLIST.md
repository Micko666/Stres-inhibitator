# FULL_9_BLOCK_MANUAL_TEST_CHECKLIST.md

Status svake stavke: **PASS / FAIL / NOT RUN**.
Ne označavati PASS bez stvarnog prolaza na uređaju.

Stanje na dan 2026-07-14: **sve NOT RUN** (izmjene napravljene, Quest tok još nije vožen).

## A. Priprema

| # | Provjera | Status |
|---|---|---|
| A1 | HR Relej šalje; Quest/Unity prima (ACK potvrđen) | NOT RUN |
| A2 | HR gate se ne otvara bez stvarnog svježeg uzorka | NOT RUN |
| A3 | Band aktivno mjeri (workout) → „star“ < 2 s | NOT RUN |

## B. Pre-session tok

| # | Provjera | Status |
|---|---|---|
| B1 | **Prva sesija ciklusa**: STAI-6 se pojavi PRVI | NOT RUN |
| B2 | Srednja sesija: STAI-6 se NE pojavljuje | NOT RUN |
| B3 | „Priprema 1/2 — Vođeno disanje i mjerenje pulsa“: krug diše I mjeri istovremeno | NOT RUN |
| B4 | Ta faza traje tačno `baseline.durationSeconds` | NOT RUN |
| B5 | Tokom faze vidljivi: ritam, napredak %, preostalo vrijeme, HR status, **tačan BPM** | NOT RUN |
| B6 | Faza se završava sama (nema Continue dugmeta u njoj) | NOT RUN |
| B7 | Odmah zatim „Priprema 2/2 — Kratki upitnik“ (SSQ) — bez praznog ekrana | NOT RUN |
| B8 | Nema dva Continue dugmeta, nema preklapanja teksta, nema ponovnog HR povezivanja | NOT RUN |
| B9 | Tutorial se pojavi SAMO ako je potreban | NOT RUN |
| B10 | **Globalni tajmer ne teče** ni u jednoj pre-session fazi | NOT RUN |
| B11 | Nakon te faze **BPM više nije vidljiv** participantu (samo zona) | NOT RUN |

## C. HR prekid (ne smije praviti pauzu)

| # | Provjera | Status |
|---|---|---|
| C1 | Prekid pulsa tokom disanja/reference: faza se **NE** pauzira, nema poruke, nema countdowna | NOT RUN |
| C2 | Prekid tokom bloka: sesija se **NE** pauzira | NOT RUN |
| C3 | Posljednji BPM se ne ponavlja kao novi uzorak | NOT RUN |
| C4 | Rupa je vidljiva u summary-ju kao kvalitet podataka | NOT RUN |

## D. 9 blokova

| # | Provjera | Status |
|---|---|---|
| D1 | Svih 9 blokova (3 runde × 3) se odvrti do kraja | NOT RUN |
| D2 | Tablet: Blok X/9 · Runda Y/3 · naziv · nivo N · globalno vrijeme | NOT RUN |
| D3 | Tablet **ne** prikazuje scheduler scoring / razloge / naredni plan | NOT RUN |
| D4 | Globalni tajmer teče SAMO u aktivnom bloku | NOT RUN |
| D5 | Neutral: pressure se NE aktivira | NOT RUN |
| D6 | ControlledPressure: pressure prati 70/50/30/10/0 | NOT RUN |
| D7 | 9/9 prije nule → uspješan završetak | NOT RUN |
| D8 | Nula prije bloka 9 → TimeExpired (nije „poraz“) | NOT RUN |

## E. Ručna pauza

| # | Provjera | Status |
|---|---|---|
| E1 | **Y (lijevi kontroler)** otvara pauzu | NOT RUN |
| E2 | Ponovni Y → **jedan** 3-2-1 countdown | NOT RUN |
| E3 | Isti blok/pokušaj se nastavlja | NOT RUN |
| E4 | Globalni tajmer i pressure **stoje** tokom pauze | NOT RUN |
| E5 | Konzola **nema** PAUSE dugme | NOT RUN |
| E6 | Menu/Start dugme **ne** pauzira | NOT RUN |
| E7 | Nikad se ne pauzira samo od sebe | NOT RUN |

## F. Post-session

| # | Provjera | Status |
|---|---|---|
| F1 | Recovery se izvrši | NOT RUN |
| F2 | Post-session SSQ | NOT RUN |
| F3 | NASA-TLX | NOT RUN |
| F4 | **Posljednja sesija ciklusa**: završni STAI-6 nakon TLX | NOT RUN |
| F5 | TimeExpired tok NE preskače recovery/SSQ/TLX | NOT RUN |

## H. Corsi orijentacija — svih 9 pozicija (UX korekcije 2026-07-14)

Fizički grid je rotiran **180° oko Y** u odnosu na tablet (`console.x = −norm.x`,
`console.z = −norm.y`). Ranije je bila flipovana **samo Z** — to je ogledalo, ne
rotacija, pa je lijevo/desno ostajalo zamijenjeno.

Izvod (iz scene, ne iz pretpostavke): `CorridorSpawn` rot Y=180 → učesnik gleda −Z →
njegova LIJEVA je svjetsko +X. `Console_BlenderPrototype` je na identity rotaciji →
lokalni +X konzole **jeste** učesnikova lijeva (isto na čemu počiva
`ConsoleLayoutBuilder.UserPerspectiveX` za LEFT/RIGHT dugmad). Tablet gleda učesnika →
tabletni +X je učesnikova **desna**. Zato X mora da se negira.

Provjeri **svaku** poziciju: pritisni dugme na tabletu koje svijetli i potvrdi da je
fizičko dugme na istom mjestu iz tvoje perspektive.

| Tablet idx | Grid (kol,red) | Tablet pozicija | Fizička pozicija (iz perspektive učesnika) | Status |
|---|---|---|---|---|
| CORSI_0 | (1,0) | lijevo, gore | lijevo, **daleko** | NOT RUN |
| CORSI_1 | (2,0) | blago lijevo, gore | blago lijevo, daleko | NOT RUN |
| CORSI_2 | (4,0) | blago desno, gore | blago desno, daleko | NOT RUN |
| CORSI_3 | (6,1) | krajnje desno, sredina | krajnje desno, sredina | NOT RUN |
| CORSI_4 | (1,1) | lijevo, sredina | lijevo, sredina | NOT RUN |
| CORSI_5 | (3,1) | **CENTAR** | **CENTAR** (fiksna tačka rotacije) | NOT RUN |
| CORSI_6 | (0,1) | krajnje lijevo, sredina | krajnje lijevo, sredina | NOT RUN |
| CORSI_7 | (3,2) | centar, dolje | centar, **blizu** | NOT RUN |
| CORSI_8 | (5,1) | desno, sredina | desno, sredina | NOT RUN |

> Napomena: layout v1 je namjerno nepravilan — red 0 ima 3 pozicije, red 1 ima 5,
> red 2 ima samo jednu (CORSI_7, centar-dolje). Zato **ne postoji** „donje desno"
> polje; provjeri gornje-lijevo (CORSI_0) i centar-dolje (CORSI_7) umjesto toga.

## I. UX korekcije — novi ekrani

| # | Provjera | Status |
|---|---|---|
| I1 | Novi profil: sva slova/brojevi klikabilni zrakom + okidačem | NOT RUN |
| I2 | Novi profil: SAČUVAJ ne radi za prazno/prekratko ime | NOT RUN |
| I3 | Novi profil: NAZAD i OTKAŽI vraćaju na listu profila (nema zaglavljivanja) | NOT RUN |
| I4 | Novi profil: B na desnom kontroleru vraća nazad | NOT RUN |
| I5 | Pauza (Y): panel sa „Nastavi" i „Završi sesiju", oba klikabilna | NOT RUN |
| I6 | Pauza: zadatak je zatamnjen, ne prima input | NOT RUN |
| I7 | Flanker: strelice koriste cijeli centar tableta, bez kartice | NOT RUN |
| I8 | Uputstvo se pojavi za novi nivo, NE pred svaki blok istog nivoa | NOT RUN |
| I9 | ControlledPressure: efekti vidljivi na 70/50/30/10/0 | NOT RUN |
| I10 | Neutral: nema urušavanja (kontrolni test) | NOT RUN |
| I11 | TimeExpired: finale → recovery → SSQ → TLX | NOT RUN |

## G. Scheduler / nivoi

| # | Provjera | Status |
|---|---|---|
| G1 | Summary prikazuje TRENUTNU SESIJU (condition, pressure, 3 taska + nivoi, seed, budžet) | NOT RUN |
| G2 | Summary prikazuje NAREDNU SESIJU (directive → nivo, status Applied/Pending/…) | NOT RUN |
| G3 | Izađi iz Play Mode → uđi ponovo → **nivoi su sačuvani** | NOT RUN |
| G4 | Naredna sesija stvarno kreće sa novim nivoima | NOT RUN |
| G5 | Drugi profil ima svoje (default) nivoe | NOT RUN |
| G6 | Invalidna sesija ne mijenja nivoe | NOT RUN |
