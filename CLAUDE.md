# CLAUDE.md — SmartLine VR / Diplomski rad

Guidance for Claude Code sessions on this repository.

## Projekt

**VR Stress Inoculation Training** — diplomski rad, FIST fakultet.  
Student: Mihailo Đurović  
Cilj: VR okruženje gdje korisnik rješava zadatak pod stresom (zidovi se zatvaraju, tigar iz kaveza) dok se mjeri HR u realnom vremenu.

## HR Pipeline (RADI)

```
Xiaomi Band 9 → Mi Fitness (Android, Samsung telefon)
  → ADB WiFi (192.168.1.222:5555)
  → hr_dashboard_v2.py (Flask na :8888 + UDP :5005)
  → Unity VR (HRReceiver.cs)
```

Pokretanje: dvostruki klik na `start.bat`  
Dashboard: http://127.0.0.1:8888  
UDP JSON format: `{"hr": 82, "ts": "15:31:50"}`

### Ključni fajlovi
| Fajl | Opis |
|------|------|
| `hr_dashboard_v2.py` | Glavni bridge: ADB logcat → Flask dashboard + UDP izlaz |
| `start.bat` | One-click pokretanje (kill stari Python, ADB connect, pokreni bridge, otvori Chrome) |

### Poznate činjenice
- ADB path: `[DIPLOMSKI_FOLDER]\platform-tools\adb.exe`
- ADB host: `192.168.1.222:5555`
- Mi Fitness je u Samsung work profilu (user 150) — ADB ne može pokretati Mi Fitness aktivnosti
- HR podaci dolaze samo kad je HR ekran aktivan na satu (~5 min window)
- Band 9 auth key: u `stari fajlovi/auth_key.txt` = `a77277a213dde02e338a08b1ae3fd712`
- UDP šalje i na 127.0.0.1:5005 (za Unity)

## Unity VR Projekat (SETUP GOTOV)

**Target**: Meta Quest 3  
**Stack**: Unity 6.3 LTS (6000.3.16f1) + Meta XR All-in-One SDK 201.0  
**Projekat**: `[DIPLOMSKI_FOLDER]\VR_StressTraining\`

### Šta instalirati na novom računaru

1. **Unity Hub** — winget install Unity.UnityHub
2. **Unity 6.3.16f1** — Unity Hub → Installs → Install Editor → 6000.3.16f1
   - Obavezni moduli: Android Build Support + Android SDK & NDK Tools + OpenJDK
3. **Node.js 18+** — za MCP Unity server (winget install OpenJS.NodeJS.LTS)
4. **Meta Horizon Developer nalog** — developers.meta.com (besplatno, jednom)
5. **Developer Mode na Quest 3** — Meta Horizon app → headset → Developer Mode ON
6. **ADB** — već u `platform-tools/` folderu

### Šta NE treba ponovo raditi
- Meta XR All-in-One SDK je već importovan (Library/ i Packages/)
- Project Setup Tool je već pokrenut — 78 Verified Items
- OpenXR + Meta XR feature group već konfigurisano
- Sve skripte napisane i u Assets/Scripts/
- MCP Unity server već kompajliran (`Library/PackageCache/com.gamelovers.mcp-unity@aade29c7dd84/Server~/build/`)

### Otvaranje projekta na novom računaru
1. Kopiraj cijeli `Diplomski/` folder na novi računar
2. Unity Hub → Projects → Add → odaberi `VR_StressTraining/` folder
3. Unity će reimportovati Library (5-10 min, jednom — neizbježno)
4. Ako pita za verziju editora → odaberi 6000.3.16f1
5. Otvori `Assets/Scenes/MainScene`

### Build na Quest 3
- **File → Build Profiles → Android™** (NE Meta Quest — bug: `ovr-manifest-write-failed`)
- Quest 3 priključen USB-C, Developer Mode uključen
- Strelica ▼ pored "Build And Run" → **Patch And Run** za brze iteracije
- Za prvi build: "Build And Run" (jednom, traje ~5-10 min zbog shader keširanja)

## MCP Unity — Claude Code integracija

MCP Unity daje Claude direktan pristup Unity Editoru (čitanje scene, kreiranje objekata itd.)

### Fajlovi (već postoje, ne diraj)
- `VR_StressTraining/.mcp.json` — Unity auto-generisan config za MCP server
- `Diplomski/.mcp.json` — Claude Code config (apsolutne putanje, env vars)
- `VR_StressTraining/Library/PackageCache/com.gamelovers.mcp-unity@aade29c7dd84/Server~/build/` — kompajliran Node.js server

### Pokretanje na novom računaru
1. Otvori Unity Editor sa projektom
2. Provjeri Tools → MCP Unity → Server Window → Status: **Server Online** (auto-start je uključen)
3. Otvori Claude Code iz `Diplomski/` foldera
4. MCP Unity alati se automatski pojavljuju (get_scene_info, create_gameobject itd.)

### Poznati problem (RIJEŠEN NA NOVOM RAČUNARU)
Na starom računaru MCP Unity je primao zahtjeve ali response nije stigao nazad zbog višestrukih WebSocket konekcija od debug testova. Na novom računaru — čista instalacija, jedan klijent → treba raditi.

### Ako ne radi na novom računaru
```bash
# Rekompajliraj Node.js server (radi se samo jednom)
cd "VR_StressTraining/Library/PackageCache/com.gamelovers.mcp-unity@aade29c7dd84/Server~"
npm run build
```
Provjeri log.txt u `Diplomski/` folderu nakon prvog tool call-a.

## Scenario — "Tigar i Puzzle"

```
FAZA 1 — Mirna soba (učenje):
  Prikaži sekvencu dugmadi polako → igrač memorira → HR baseline

FAZA 2 — Stresna soba (ista sekvenca):
  Zidovi se zatvaraju + kavez se otvara + tigar → ponovi sekvencu
  Hint sistem (max 3) ako zaboravi → HR spike snima se

FAZA 3 — Debriefing:
  HR graf tokom sesije + statistike (max HR, avg HR, hints korišćeni, trajanje)
```

**Interakcije** (Meta XR Interaction SDK):
- `PokeInteractable` — pritiskanje dugmadi prstom (PRIMARNO za puzzle)
- `GrabInteractable` — hvatanje predmeta
- `DistanceGrabInteractable` — hvatanje na daljinu

## Skripte (Assets/Scripts/) — SVE NAPISANE ✅

| Skripta | Opis |
|---------|------|
| `HR/HRReceiver.cs` | UDP listener port 5005, `HRReceiver.CurrentHR` + `OnHRUpdated` static event |
| `HR/HRDisplay.cs` | TextMeshPro UI prikaz HR-a sa bojama po zonama |
| `GameManager.cs` | Singleton, faze: Calibration→CalmRoom→StressRoom→Debriefing |
| `PuzzleManager.cs` | Generira sekvencu, prikazuje je, validira input, hint sistem (maxHints=3) |
| `PuzzleButton.cs` | Dugme sa highlight/press vizualnim feedbackom, poziva PuzzleManager |
| `StressRoomController.cs` | Pomiče 4 zida, otvara kavez (rotacija vrata), aktivira tigar animator |
| `DebriefingManager.cs` | HR graf (LineRenderer), max/avg HR, completion time, hints used |

## Scena (Assets/Scenes/MainScene) — TRENUTNO STANJE

**U sceni postoji:**
- ✅ `[BuildingBlock] Camera Rig` — OVRCameraRig sa VR kamerom
- ✅ `[BuildingBlock] Passthrough` — AR passthrough
- ✅ `[BuildingBlock] Poke Interaction` — finger poke input
- ✅ `[BuildingBlock] Cube` — test interaktivni objekt
- ✅ `Directional Light`
- ✅ `Plane` (pod)

**Nedostaje (TODO):**
- ❌ GameManager GameObject (prazan GO sa GameManager.cs komponentom)
- ❌ HRReceiver GameObject
- ❌ HRDisplay GameObject (TextMeshPro UI u world spaceu)
- ❌ Calm Room geometrija
- ❌ Stress Room geometrija (sa kavezom)
- ❌ 5x PuzzleButton GameObjecti
- ❌ Tigar (3D model + Animator)
- ❌ Debriefing Canvas

## TODO — Prioritetni redoslijed

### FAZA A — Scene setup (radi odmah)
- [ ] Dodati GameManager, HRReceiver GameObjecte u scenu
- [ ] Napraviti Calm Room: soba 8x4x8m, stol u centru, 5 puzzle dugmadi na stolu
- [ ] Napraviti Stress Room: iste dimenzije, kavez sa tigar modelom, 4 pokretna zida
- [ ] Postaviti PuzzleButton objekte i assignati u PuzzleManager.buttons[]

### FAZA B — Assets (nabavka)
- [ ] Tigar 3D model sa Idle+Walk+Attack animacijama (Unity Asset Store — besplatno: "Tiger" ili "Low Poly Animals")
- [ ] Zvučni efekti: tigar rika, zidovi skripanje, ambient tenzija

### FAZA C — Finalizacija
- [ ] HRDisplay world-space UI panel (iznad vrata ili na zidu)
- [ ] Debriefing ekran sa HR grafom
- [ ] End-to-end test sa Quest 3

### FAZA D — Bonus
- [ ] ML stress klasifikacija (sklearn ili Unity Sentis)

## Teorijska osnova

- **Meichenbaum (1985)** — Stress Inoculation Training (3 faze: edukacija → vježbanje → primjena)
- **Rizzo et al. (2011)** — VR za military stress training
- **Ključna tvrdnja**: "Ponavljana kontrolisana izloženost stresorima u VR smanjuje fiziološki HR odgovor"

## Status projekta

- [x] HR pipeline radi (Band 9 → Python → UDP → Unity)
- [x] start.bat kreiran
- [x] Unity 6.3 LTS + Android moduli
- [x] Meta XR All-in-One SDK 201.0 (u Packages/)
- [x] Project Setup Tool — 78 Verified Items
- [x] OpenXR + Meta XR konfigurisano
- [x] Quest 3 — APK deployovan i testiran
- [x] Sve core skripte napisane (7 fajlova)
- [x] MainScene sa Camera Rig + Passthrough + Poke Interaction
- [x] MCP Unity server setup (.mcp.json + Node.js build)
- [ ] **Sobe i scenarij u sceni** ← SLJEDEĆE
- [ ] HR integracija u scenu
- [ ] Tigar asset + animacije
- [ ] ML model (bonus)
