# CLAUDE.md — Adaptive Stress Corridor VR / Diplomski rad

Guidance for Claude Code sessions on this repository.

---

## Projekt

**Adaptive VR Stress Training System** — diplomski rad, FIST fakultet.
Student: Mihailo Đurović
Cilj: Research-informed prototype adaptivnog VR stress-training sistema koji koristi HR biofeedback i rule-based adaptive task sequencing.

**Akademski okvir:**
- Stress Inoculation Training (Meichenbaum, 1985) — 3 faze: edukacija → vježbanje → primjena
- Rizzo et al. (2011) — VR za military stress training
- Tvrdnja rada: "Ponavljana kontrolisana izloženost stresorima u VR smanjuje fiziološki HR odgovor"
- Sistem je research-informed prototype / MVP — nije klinički validiran

**Terminologija koja se koristi u pisanom radu:**
- adaptive VR stress-training environment
- controlled exposure to stressors
- threat imminence
- HR biofeedback / physiologically informed task sequencing
- rule-based adaptive scheduler (transparentan model za MVP)
- stress response monitoring

> Pisani/teorijski dio vodi student u posebnom ChatGPT chatu.
> Claude Code radi: Unity scena, C# arhitektura, tehnička integracija, cleanup, build/test workflow.

---

## Scenario — Adaptive Stress Corridor

Jedan uski, stariji, mračni VR hodnik. Na jednom kraju je interaktivna **task konzola** sa dugmadima, polugama, sliderima, dialovima, ekranima i robotskim rukama. Na drugom kraju je **kavez sa prijetnjom** — vrata se sporo otvaraju kako vrijeme ističe, stvarajući perceptivni pritisak bez nužnog fizičkog kontakta.

Korisnik stoji u hodniku i izvršava zadatke na konzoli pod vremenskim i perceptivnim pritiskom. Sistem mjeri puls u realnom vremenu, bilježi HR odgovor na svaki zadatak, te rule-based scheduler bira naredni zadatak na osnovu stresnog i performansnog profila korisnika.

**Istraživački fokus:**
- VR okruženje za kontrolisanu izloženost stresoru
- HR biofeedback u realnom vremenu (Xiaomi Band 9)
- Task baza sa ponderima težine i kategorijama
- Korisnički stres profil koji se gradi tokom sesije
- Rule-based adaptivni scheduler (MVP — transparentan, reproducibilan)
- Debriefing analiza: HR, maxHR, avgHR, hrDelta, recovery, greške, trajanje, uspjeh
- ML/ONNX extension je moguć u budućnosti, nije MVP

> **Napomena:** Projekat je ranije bio koncipiovan kao "Tigar i Puzzle" sa dvije sobe (Calm Room + Stress Room).
> Taj koncept je rana iteracija/prototype. Finalni koncept je Adaptive Stress Corridor. Stare skripte se mapiraju.

---

## Current Development Workflow

| Zadatak | Metoda |
|---------|--------|
| Svakodnevni razvoj | **Quest Link / Meta Horizon Link + Unity Play Mode** |
| Milestone testovi | Build And Run (Android™) — spor, samo za finalna testiranja |
| Manje standalone provjere | Patch And Run — samo kad Development Build već postoji |

**Pravila:**
- Unity Hub otvara `VR_StressTraining/` folder
- Claude Code se otvara iz **root** foldera repozitorijuma
- Glavna scena je `Assets/Scenes/MainScene.unity` — jedina scena u Build Scene List
- Ne dirati `Packages/`, `Project Settings/`, `Build Profiles/` bez konkretnog razloga (invalidira cache)
- Build Profiles: koristiti **Android™**, ne "Meta Quest" (bug: `ovr-manifest-write-failed`)
- Active Input Handling: ne mijenjati bez razloga
- Desktop/Standalone OpenXR: radi za Link Play Mode
- Android/OpenXR: ostaje za standalone Quest 3 build

---

## HR Pipeline

```
Xiaomi Band 9 → Mi Fitness (Android, Samsung telefon)
  → ADB WiFi (192.168.1.222:5555)
  → hr_dashboard_v2.py (Flask na :8888 + UDP :5005)
  → Unity VR (HRReceiver.cs)
```

Pokretanje: dvostruki klik na `start.bat`
Dashboard: http://127.0.0.1:8888
UDP JSON format: `{"hr": 82, "ts": "15:31:50"}`

> `start.bat` je development/test alat za HR pipeline. Nije finalni produkcijski launcher.

### Ključni fajlovi
| Fajl | Opis |
|------|------|
| `hr_dashboard_v2.py` | Glavni bridge: ADB logcat → Flask dashboard + UDP izlaz |
| `start.bat` | One-click pokretanje (kill stari Python, ADB connect, pokreni bridge, otvori Chrome) |

### Poznate činjenice
- ADB path: `[ROOT_FOLDER]\platform-tools\adb.exe`
- ADB host: `192.168.1.222:5555`
- Mi Fitness je u Samsung work profilu (user 150) — ADB ne može pokretati Mi Fitness aktivnosti
- HR podaci dolaze samo kad je HR ekran aktivan na satu (~5 min window)
- Band 9 auth key: u `stari fajlovi/` — **ne commitovati vrijednost ovdje niti u kod**
- UDP šalje i na 127.0.0.1:5005 (za Unity)

---

## Unity VR Projekat

**Target**: Meta Quest 3
**Stack**: Unity 6.3 LTS (6000.3.16f1) + granularni Meta XR paketi (201.0.0)
**Projekat**: `[ROOT]\VR_StressTraining\`

### Instalirani paketi (manifest.json — ne mijenjati bez potrebe)
```
com.meta.xr.sdk.core             201.0.0
com.meta.xr.sdk.interaction      201.0.0
com.meta.xr.sdk.interaction.ovr  201.0.0
com.meta.xr.sdk.haptics          201.0.0
com.meta.xr.sdk.platform         201.0.0
com.meta.xr.mrutilitykit         201.0.0
com.meta.xr.runtimeoptimizer     0.2.2
com.unity.xr.openxr              1.14.0
com.unity.xr.management          4.5.0
com.unity.render-pipelines.universal  17.0.3
com.unity.ai.inference           2.6.1   (za budući ML/Sentis/ONNX)
com.gamelovers.mcp-unity         (git)
```

> `com.meta.xr.sdk.all` je uklonjen — vukao je Voice/Wit/Conduit koji nije potreban.
> Ne vraćati sdk.all. manifest.json je čist.

### Otvaranje projekta na novom računaru
1. Kopiraj cijeli root folder
2. Unity Hub → Projects → Add → odaberi `VR_StressTraining/`
3. Library reimport (~5-10 min, jednom)
4. Ako pita za verziju → 6000.3.16f1
5. Otvori `Assets/Scenes/MainScene`

### Build na Quest 3
- **File → Build Profiles → Android™** (NE Meta Quest)
- Quest 3: USB-C, Developer Mode ON
- First build: Build And Run (~5-10 min)
- Iteracije: Quest Link + Unity Play Mode

---

## MCP Unity — Claude Code integracija

### Arhitektura (two-tier)
```
Claude Code  ←stdio→  Node.js (build/index.js)  ←WebSocket:8092→  Unity Editor
```
- Node.js process se pokreće automatski kada Claude Code učita `.mcp.json`
- Node.js se konektuje na Unity-jev WebSocket server na `ws://localhost:8092/McpUnity`
- Unity mora biti otvoren i MCP Server Window mora biti **Online** da bi konekcija radila
- Svaka Unity kompilacija/domain reload privremeno prekida WebSocket — sačekati da završi

### Konfiguracija — root `.mcp.json` (Windows wrapper pristup)
```json
{
  "mcpServers": {
    "mcp-unity": {
      "command": "cmd.exe",
      "args": ["/c", "tools\\run-mcp-unity.bat"],
      "env": { "LOGGING_FILE": "true" }
    }
  }
}
```

### Windows wrapper — `tools/run-mcp-unity.bat`
```bat
@echo off
cd /d "%~dp0..\VR_StressTraining"
node "Library\PackageCache\com.gamelovers.mcp-unity@aade29c7dd84\Server~\build\index.js"
```

**Zašto wrapper, a ne direktan `"cwd"` u `.mcp.json`?**
Claude Code ne primjenjuje `"cwd"` iz project `.mcp.json` pouzdano na Windowsu.
Wrapper rješava problem: `cd /d "%~dp0..\VR_StressTraining"` osigurava da node
startuje sa ispravnim `process.cwd()`.

**Zašto je `process.cwd()` = `VR_StressTraining/` kritičan?**
`com.gamelovers.mcp-unity` čita timeout ovako (mcpUnity.ts, ln 10):
```typescript
path.resolve(process.cwd(), './ProjectSettings/McpUnitySettings.json')
```
Bez ispravnog cwd, fajl nije nađen → timeout pada na **hardcoded 10s default** →
svaki request timeoutuje, Unity ne stigne da odgovori.

Sa wrapperom:
- `process.cwd()` = `VR_StressTraining/`
- `ProjectSettings/McpUnitySettings.json` nađen ✓
- `RequestTimeoutSeconds: 120` pročitan ✓

| Fajl | Ko ga koristi | Napomena |
|------|--------------|----------|
| Root `.mcp.json` | **Claude Code** (iz root foldera) | koristi `cmd.exe` + wrapper |
| `tools/run-mcp-unity.bat` | pozvan iz `.mcp.json` | cd u VR_StressTraining, startuje node |
| `VR_StressTraining/.mcp.json` | Unity auto-generisan | ignoriše ga Claude Code iz root-a |

> Claude Code **uvijek** otvarati iz root foldera repozitorijuma (`Stres-inhibitator-main/`).
> Wrapper automatski ulazi u `VR_StressTraining/` — nema potrebe za promjenom kako se otvara CC.

### Ključne postavke — `ProjectSettings/McpUnitySettings.json`
```json
{
  "Port": 8092,
  "RequestTimeoutSeconds": 120,
  "AutoStartServer": true,
  "EnableInfoLogs": true
}
```
> **Trenutni port je 8092.** Ne vraćati ga naslijepo na stari 8090/8091;
> uvijek prvo pročitati `ProjectSettings/McpUnitySettings.json` i Server Window.
>
> `RequestTimeoutSeconds` je **120**, ne 10. Sa 10s Unity može timeoutovati
> tokom scene importa ili kada Editor radi background task.
>
> **VAŽNO:** Ako je Unity otvoren dok se fajl mijenja, Unity može ga resetovati.
> Pouzdan način da se postavi: **Tools → MCP Unity → Server Window →
> polja "Connection Port" i "Request Timeout (seconds)"** → ukucati vrijednosti → Enter.

### Normalno pokretanje
1. Otvori Unity Editor sa `VR_StressTraining/` projektom
2. Sačekaj da kompajliranje završi (Console bez grešaka)
3. **Tools → MCP Unity → Server Window** → Status: **Server Online**
4. **Ne klikati "Start" ponovo ako je već Online**
5. **Request Timeout = 120** (provjeriti u Server Window)
6. Otvori Claude Code iz root foldera repozitorijuma
7. Prihvati MCP server prompt
8. MCP alati se automatski pojavljuju

### Rebuild Node servera (samo ako je potrebno)
```bash
cd "VR_StressTraining/Library/PackageCache/com.gamelovers.mcp-unity@aade29c7dd84/Server~"
npm install
npm run build
```
Rebuild je potreban samo ako je Library reimportovana ili ako `build/index.js` ne postoji.

---

## MCP Unity — Troubleshooting

### Dijagnoza: `netstat -ano | findstr :8092`
> Port se mora poklapati sa `ProjectSettings/McpUnitySettings.json`; trenutno je 8092.

| Rezultat | Značenje | Akcija |
|----------|----------|--------|
| `Unity.exe LISTENING` + Server Window = Online | ✅ Normalno | Ništa |
| `Unity.exe LISTENING` + Server Window = Offline | Unity drži port, server nije startovao | Restart Unity |
| `node.exe LISTENING` | Stari Node process živi | Zatvoriti Claude Code, ubiti node.exe |
| Ništa | Port slobodan, server nije pokrenut | Otvoriti Unity, pokrenuti MCP Server Window |
| PID bez procesa (zombie socket) | Kernel drži socket od mrtvog procesa | Izaberi novi slobodan port i ažuriraj Server Window + dokumentaciju |

### Uzroci timeouta `get_scene_info`
1. **`RequestTimeoutSeconds: 10`** — prekratko. Fix: postaviti na `120` u McpUnitySettings.json ← **najčešći uzrok**
2. **Unity kompajlira** — domain reload prekida WebSocket. Sačekati da završi, pa pozvati ponovo
3. **Unity u Play Mode-u** — MCP radi u Play Mode-u, ali neke operacije su ograničene
4. **Modal dialog otvoren** — Unity blokirano dijalogom (npr. "Import settings changed"). Zatvoriti dialog
5. **Stari Node process** — zatvori Claude Code, sačekaj 5s, ponovo otvori

### Procedura resetovanja ako ništa ne pomaže
```
1. Zatvori Claude Code
2. Zatvori Unity
3. Pročitaj port iz McpUnitySettings.json, zatim: netstat -ano | findstr :8092
4. Ako node.exe ili Unity.exe drži port → ubiti u Task Manager-u
5. Otvoriti Unity, sačekati import/compile
6. Tools → MCP Unity → Server Window → provjeriti Server Online
7. Provjeriti Request Timeout = 120
8. Otvoriti Claude Code iz root foldera
9. Testirati: pozvati get_scene_info
```

### Safe test procedura (korak po korak)
```
1. Unity otvoren, projekt učitan
2. NIJE u Play Mode-u
3. Console je miran (bez compile errors ili ongoing import)
4. Tools → MCP Unity → Server Window → Status: Server Online
5. Request Timeout = 120  (promijeniti ako piše 10)
6. Otvoriti Claude Code iz: C:\Users\djuro\Desktop\Stres-inhibitator-main\
7. Prihvatiti MCP server
8. Poslati: "Test MCP Unity connection. Do not modify anything. Call get_scene_info only."
9. Ako timeoutuje → pogledati Unity Console za MCP greške
10. Ako i dalje timeoutuje → proći kroz "Procedura resetovanja" iznad
```

### MCP Debug logging (kad treba dublje dijagnosticirati)
U `ProjectSettings/McpUnitySettings.json` postaviti `"EnableInfoLogs": true` (već je true).
Na Node strani, dodati u root `.mcp.json` pod `"env"`:
```json
"env": { "LOGGING_FILE": "true" }
```
Log se kreira kao `log.txt` u root folderu. `.gitignore` već ignoriše `log.txt`.

---

## Meta XR Required Fix (dokumentacija — ne rješavati sada)

Unity može stalno pokazivati "1 Required Fix" u Meta XR Project Setup Tool.
- Klik na "Fix" mijenja neki `ProjectSettings` fajl
- Ako se Fix vraća, fajl se možda ne čuva ili se override-uje
- **Rješenje (kad dođe red):**
  1. Kliknuti Fix
  2. `File → Save Project`
  3. `git status` → identificirati koji fajl se promijenio
  4. Commitovati promjenu
- Ne diraj sada — ne utiče na funkcionisanje projekta

---

## URP Rendering — Troubleshooting (magenta/rozo)

> **Riješeno 2026-06-02, commit `9ebfd44`.** Ova sekcija objašnjava uzrok i fix
> ako se magenta/rozo ponovo pojavi. Detaljnije u memory: `project_urp_render_pipeline.md`.

### Simptom
Objekti (Corridor_Blockout, Console, RobotArm) se renderuju **magenta/rozo** u
Scene view-u ili na Questu.

### Ključno razumijevanje — magenta = shader ↔ pipeline mismatch
Magenta NIJE problem materijala nego **nepoklapanja shadera i aktivnog render pipeline-a**:

| Shader na materijalu | Built-in pipeline | URP pipeline aktivan |
|----------------------|-------------------|----------------------|
| Standard             | renderuje OK      | **MAGENTA**          |
| URP/Lit              | **MAGENTA**       | renderuje OK         |

Quest build koristi URP. Zato je Standard bio magenta na Questu; kad smo konvertovali
u URP/Lit postao je magenta u Editoru — jer **nije bio dodijeljen URP pipeline asset**.

### Root cause (šta smo našli)
Projekat NIJE imao aktivan URP Render Pipeline Asset:
- `ProjectSettings/GraphicsSettings.asset` → `m_CustomRenderPipeline: {fileID: 0}` (null)
- `ProjectSettings/QualitySettings.asset` → svih 7 levela `customRenderPipeline: {fileID: 0}` (null)
- Nijedan `UniversalRenderPipelineAsset` nije postojao u `Assets/`

> ⚠️ `Assets/UniversalRenderPipelineGlobalSettings.asset` NIJE pipeline asset —
> to su samo global/shader-stripping settings i ne aktiviraju URP sam po sebi.

### Fix (već primijenjen — postoji u repou)
Kreiran preko **Unity API-ja**, NE hand-written YAML:
- `Assets/Settings/URP_Balanced.asset` (Forward, MSAA 2x, HDR off, render scale 1.0)
- `Assets/Settings/URP_Balanced_Renderer.asset` (xrSystemData + postProcessData popunjeni za Quest)
- Dodijeljen u `GraphicsSettings.defaultRenderPipeline` + svih 7 quality levela

### Dijagnostika ako se magenta vrati
```bash
# 1. Pipeline NE smije biti null (mora imati guid, ne {fileID: 0})
grep "m_CustomRenderPipeline" VR_StressTraining/ProjectSettings/GraphicsSettings.asset
# 2. URP asset mora postojati
ls VR_StressTraining/Assets/Settings/URP_Balanced.asset
# 3. Materijal mora biti URP/Lit (shader guid 933532a4fcc9baf4fa0491de14d08ed7)
```

### Pravila
- **NE konvertovati materijale u URP/Lit bez aktivnog URP pipeline-a** (prvo pipeline, pa materijali)
- URP assete kreirati preko `UniversalRenderPipelineAsset.Create(rendererData)` — NE hand-written YAML
- `QualitySettings.SetRenderPipelineAssetAt(...)` **NE postoji** u Unity 6 →
  koristiti `SerializedObject` na `customRenderPipeline` polju svakog quality levela
- FBX embedded materijali se resetuju na `SaveAndReimport()` → ekstraktovati u
  eksterni `.mat` (`materialLocation = External`) prije konverzije shadera

---

## Build na Quest preko Coplay (execute_script)

Kad je Coplay MCP konektovan, build se može pokrenuti programski (bez ručnog GUI klika):

1. **Provjeri readiness** (execute_script): `EditorUserBuildSettings.activeBuildTarget == Android`
2. **Provjeri da build već nije aktivan:** `BuildPipeline.isBuildingPlayer == false`.
3. **Pokreni samo build** preko `BuildPipeline.BuildPlayer(BuildPlayerOptions)`:
   - scenes iz `EditorBuildSettings`, `target = BuildTarget.Android`,
     `options = BuildOptions.None` (**bez** deploy/launch koraka)
   - output: `Builds/VR_StressTraining.apk` (`Builds/` je gitignored)
   - **`BuildPlayerOptions` = klasičan Android build** — zaobilazi "Meta Quest" build
     profile i njegov `ovr-manifest-write-failed` bug
   - `EditorApplication.delayCall` može vratiti MCP poziv prije početka builda;
     queue-ovati samo jednom i ispisati jedinstvene `QUEUED`, `START` i `RESULT` markere
4. **Ne zaključivati da build nije krenuo samo zato što Coplay/watch timeoutuje.**
   Tokom builda Editor/MCP je nedostupan. Provjeriti `Editor.log`, `RESULT` marker i
   timestamp/veličinu `Builds/VR_StressTraining.apk`.
5. **Deploy odvojeno poslije uspješnog builda:**
   - `platform-tools\adb.exe devices` → Quest mora biti `device`
   - `platform-tools\adb.exe install -r VR_StressTraining\Builds\VR_StressTraining.apk`
   - tek zatim pokrenuti aplikaciju na Questu

> Potvrđeno 2026-07-20: build označen sa `[BuildOnly] QUEUED` jeste prešao u
> `[BuildOnly] START` i završio sa `RESULT=Succeeded` za oko 4:55. Fokus Unity
> prozora nije bio dokazani uzrok. Stari zaključak „delayCall se ne prazni dok
> Editor nije fokusiran” ne koristiti kao dijagnozu.

> Helper `execute_script` fajlove staviti **VAN `Assets/` foldera** (npr. project root
> `VR_StressTraining/`) da Unity ne kompajlira/pollutuje projekat. Obrisati nakon builda.
> **Nikad ne commitovati** `_Temp_*` ni `[InitializeOnLoad]` helper skripte.

---

## Robot Arm — Pravila za animaciju (inspekcija 2026-06-02; NE animirati još)

> Robotska ruka `RobotArm_Scene` je inspektovana ali **NIJE** spremna za animaciju
> velikog opsega. Ova sekcija je obavezno štivo prije pisanja BILO KAKVE animacije
> ruke. Cilj: **bez čudnog pomjeranja zglobova, bez vizuelnog rastavljanja (detach).**
> Trenutno aktivan: `RobotArmPoseTester.cs` (TEMP, `playTestLoop=false`).

### Hijerarhija — tri klase objekata
```
RobotArm_Scene                         ← root, ovdje je RobotArmPoseTester
└─ RobotArm_Placeholder                ← FBX instanca: pos(0.297,1.948,1.712) rot(90,0,0)
   └─ RobotArm_Main_WallMount          ← strukturni čvor (local identity)
      ├─ MountPlate            [FIXED]  ← NIKAD ne pomjerati (zidni nosač)
      ├─ MountBolt_01..04      [FIXED]  ← NIKAD ne pomjerati
      ├─ BasePivot          ★ [PIVOT 1] ← rotirati: local Y (yaw)
      │  ├─ BaseJoint          [MESH]   ← rigid, prati pivot
      │  ├─ Cable_01_01..04    [MESH]   ← rigid kablovi (fake, ne savijaju se)
      │  └─ ShoulderPivot   ★ [PIVOT 2] ← rotirati: local X (pitch)
      │     ├─ UpperArm        [MESH]
      │     └─ ElbowPivot    ★ [PIVOT 3] ← rotirati: local X (pitch)
      │        ├─ ElbowJoint        [MESH]
      │        ├─ Forearm           [MESH]
      │        ├─ MutedAccent_SmallPlate [MESH]
      │        ├─ Cable_02_01..02   [MESH]
      │        └─ WristPivot ★ [PIVOT 4] ← rotirati: local X (pitch)
      │           ├─ WristJoint     [MESH]
      │           ├─ ToolEnd_Housing[MESH]
      │           ├─ ClawPalm       [MESH]
      │           ├─ Claw_A / Claw_B[MESH]  ← NEMAJU pivot (vidi dolje)
      │           ├─ ClawPad_A / _B [MESH]
      │           └─ ClawSocket_AttachPoint [ATTACH] ← end-effector, za alat; ne rotirati
```

- **PIVOTI (4 empties)** — JEDINO ove rotirati: `BasePivot, ShoulderPivot, ElbowPivot, WristPivot`
- **MESH LINKOVI** — rigid djelovi, prate svoj pivot; **NIKAD ne rotirati/pomjerati pojedinačno**
- **FIXED MOUNT** — `MountPlate` + `MountBolt_01..04`; sibling su BasePivot-a (ne dijete), pa rotacija ruke ih ne pomjera. **NIKAD ne dirati.**
- **ClawSocket_AttachPoint** — tačka za kačenje alata (end-effector). Ne animirati.

### Os rotacije i opseg po zglobu (iz known-good `RobotArmPoseTester`)
| Pivot | Os (local) | Testirani opseg (poze) | Hard clamp |
|-------|-----------|------------------------|------------|
| BasePivot     | **Y** (yaw)   | −12° … +12°   | ±90°       |
| ShoulderPivot | **X** (pitch) | −35° … 0°     | ±90°       |
| ElbowPivot    | **X** (pitch) | −45° … −10°   | −120°…+10° |
| WristPivot    | **X** (pitch) | +5° … +30°    | ±90°       |

> Poze koje rade (Idle/Reach/Present) drže se gornjeg **testiranog** opsega, NE hard clampa.

### ⚠ KRITIČNO — pivoti su ko-locirani na FBX originu
Sva 4 pivota imaju `localPosition (0,0,0)` i `localRotation` identity → **svi rotiraju oko
ISTE tačke** (FBX origin), a ne oko svoje mehaničke pozicije zgloba. Ta tačka je čak iznad
vidljive ruke (≈ world `(-1.45, 3.62, -4.68)`, dok meš ruke ide do y≈2.54).

Posljedica:
- **Mali uglovi izgledaju OK** (student potvrdio) — luk je mali.
- **Veliki uglovi = nerealan zamah oko udaljene tačke = izgleda kao da se ruka raspada/detachuje.**
- Najgori su Elbow i Wrist (najdalji od zajedničke tačke).

### Pravila kodiranja (OBAVEZNO — da se izbjegne weird movement / detach)
1. Rotiraj **SAMO** 4 pivota. Nikad mesh, nikad mount, nikad ClawSocket.
2. **Uvijek additivno** od cache-ovanog rest pose-a: `localRotation = restRot * Quaternion.Euler(offset)`.
   **NIKAD** `localEulerAngles = X` od nule, NIKAD overwrite sirovih uglova.
3. Cache rest pose u `Awake()` PRIJE ikakve rotacije (pos+rot+scale svakog pivota).
4. Osi su fiksne: **Base=Y, Shoulder/Elbow/Wrist=X**. Ne izmišljati druge ose.
5. **Drži se testiranih malih opsega** iz tabele. Veći uglovi → detach-look (vidi gore).
6. **NIKAD** ne reparentuj, ne mijenjaj `localScale`, ne diraj collidere (ruka ih nema — namjerno).
7. **Klješta se NE mogu otvarati/zatvarati** — `Claw_A/Claw_B` su meš objekti BEZ pivot
   emptyja. Animiranje meša direktno = rastavljanje. Za claw animaciju treba **Blender**:
   dodati `ClawPivot_A/ClawPivot_B` empties na zglob klješta, pa re-export.
8. `RobotArmAnimation.cs` je OBRISAN jer je kršio #1 i #2 (animirao meš + overwrite euler) →
   vizuelni rastav. Ne vraćati ga.

### Za buduću realističniju animaciju — dvije opcije
- **A (MVP, sigurno):** skripta malih uglova + tween između poza (kao `RobotArmPoseTester`),
  strogo unutar testiranog opsega. Bez Blender izmjena.
- **B (proper, veći opseg):** Blender re-rig — pomjeriti origin SVAKOG pivota na stvarnu
  poziciju zgloba (sad su svi na FBX originu), dodati claw pivote, re-export FBX. Tek onda
  Unity Animator/IK daje realnu kinematiku bez detach-a.

---

## MVP Scope — Zaključan

1. Jedna scena: `Assets/Scenes/MainScene.unity`
2. Jedan prostor: Adaptive Stress Corridor (hodnik + konzola + kavez)
3. Task konzola sa randomizovanim rasporedom elemenata
4. Arhitektura koja podržava 10 task tipova
5. MVP implementacija: **2 zadatka** (Button Sequence + Dial/Slider Calibration)
6. Rule-based scheduler (ne ML za MVP)
7. HR biofeedback integrisan u task cycle
8. Debriefing ekran na kraju sesije

### Task tipovi (svih 10 — implementirati postepeno)
| # | Task tip | Status |
|---|----------|--------|
| 1 | Button Sequence Memory Task | MVP |
| 2 | Dial/Slider Calibration Task | MVP |
| 3 | Lever Order Task | Faza 8 |
| 4 | Symbol/Color Matching Task | Faza 8 |
| 5 | Reaction Task | Faza 8 |
| 6 | Code Input Task | Faza 8 |
| 7 | Cable/Connector Matching Task | Faza 8 |
| 8 | Tool Task (via robotic arm) | Faza 8 |
| 9 | Stabilization Task | Faza 8 |
| 10 | Combined Task | Faza 8 |

---

## Predložena C# Arhitektura (nije implementirana — čeka potvrdu)

### Data Layer

**`TaskDefinition.cs`** — ScriptableObject
- `id`, `displayName`, `category`, `taskType`
- `baseDifficulty`, `cognitiveLoad`, `motorLoad`, `timePressure`, `memoryLoad`, `precisionRequirement`, `threatPressure`
- `estimatedStressWeight`, `timeLimit`, `successCriteria`

**`UserStressProfile.cs`**
- `baselineHR`, `stressThreshold`, `currentDifficultyLevel`
- `successRate`, `averageRecoveryTime`
- `weakCognitive`, `weakMotor`, `weakMemory`, `weakPrecision`
- `taskHistory: List<TaskResult>`

**`TaskResult.cs`**
- `taskId`, `success`, `completionTime`, `errors`, `hintsUsed`
- `maxHR`, `avgHR`, `hrDelta`, `recoveryTime`, `stressScore`, `performanceScore`

### Utilities

**`StressMetrics.cs`**
- Static helper: `maxHR`, `avgHR`, `hrDelta`, `elevatedDuration`, `recoveryTime`, `stressScore`

### Core Systems

**`ITaskScheduler.cs`** — Interface
- `SelectNextTask(UserStressProfile profile, List<TaskDefinition> available) : TaskDefinition`

**`RuleBasedTaskScheduler.cs`** — MVP implementacija
- success + low stress → povećaj težinu
- success + moderate stress → ostani u zoni
- fail + high stress → smanji težinu
- fail + low stress → ponovi ili pojednostavi
- weak spot detection → prioritizuj relevantne zadatke kontrolirane težine

**`TaskManager.cs`**
- Pokreće zadatak, prati vrijeme, sakuplja `TaskResult`, poziva scheduler

**`ConsoleGridManager.cs`**
- Randomizovan raspored konzole (4×4 grid), spawn/activate interaktivnih elemenata

**`ThreatController.cs`**
- Vrata kaveza se otvaraju tokom zadatka (brzina ∝ timePressure × difficulty)
- Reset između zadataka

**`SessionDebriefManager.cs`** (ili proširenje `DebriefingManager.cs`)
- HR graf, maxHR, avgHR, hrDelta, recoveryTime, successRate, task history

---

## Mapa postojećih skripti → nova arhitektura

| Skripta | Status | Mapiranje |
|---------|--------|-----------|
| `HR/HRReceiver.cs` | Ostaje | Nepromijenjeno — UDP listener port 5005 |
| `HR/HRDisplay.cs` | Ostaje | Nepromijenjeno — world-space TextMeshPro UI |
| `GameManager.cs` | Ostaje/evoluira | Phase/session controller za novu arhitekturu |
| `PuzzleManager.cs` | Prototype → evoluira | Osnova za TaskManager + ButtonSequenceTask |
| `PuzzleButton.cs` | Ostaje/evoluira | Osnova za console button input |
| `StressRoomController.cs` | Prototype → evoluira | Osnova za ThreatController |
| `DebriefingManager.cs` | Ostaje/proširuje se | SessionDebriefManager ili direktno proširenje |

> Ne brisati postojeće skripte bez potvrde.

---

## Phased Development Plan

### FAZA 0 — Infrastructure Freeze ← TRENUTNA FAZA
- [x] MainScene jedina scena u projektu
- [x] Quest Link Play Mode radi
- [x] Android Build And Run radi
- [x] manifest.json čist (bez sdk.all/Voice/Wit)
- [x] Repo cleanup (nema _Recovery, nema .bak fajlova)
- [x] CLAUDE.md ažuriran sa Adaptive Stress Corridor konceptom
- [ ] Git repo inicijalizovan i initial commit napravljen
- [ ] MCP Unity potvrđeno kao funkcionalno na desktop računaru

### FAZA 1 — Spatial Blockout
- [ ] Placeholder hodnik (~3m × 3m × 12m)
- [ ] Realna skala, početna pozicija korisnika
- [ ] Placeholder konzola (jedan kraj)
- [ ] Placeholder kavez (drugi kraj)
- [ ] Osnovno osvjetljenje
- [ ] Provjera u Quest Link Play Mode

### FAZA 2 — Interaction Base
- [ ] Konzola sa grid slotovima
- [ ] Osnovni button prefab (PokeInteractable)
- [ ] Osnovni dial/slider prefab
- [ ] Test interakcija rukama i kontrolerima

### FAZA 3 — First Task
- [ ] Button Sequence Memory Task
- [ ] Task start/end flow
- [ ] TaskResult data capture, basic scoring

### FAZA 4 — HR Integration
- [ ] HRReceiver GameObject u sceni
- [ ] HRDisplay world-space panel
- [ ] HR sample recording tokom zadatka
- [ ] StressMetrics calculation

### FAZA 5 — Threat System
- [ ] Kavez placeholder sa vratima
- [ ] Vrata otvaranje vezano za timePressure/difficulty
- [ ] Reset između zadataka

### FAZA 6 — Adaptive Scheduler MVP
- [ ] UserStressProfile, RuleBasedTaskScheduler
- [ ] Automatic next task selection

### FAZA 7 — Debriefing
- [ ] maxHR, avgHR, hrDelta, recoveryTime, successRate, task history prikaz

### FAZA 8 — Additional Tasks
- [ ] Dial/Slider Calibration Task
- [ ] Ostali task tipovi — po jedan

### FAZA 9 — Polish / Final Build
- [ ] Low poly assets, lighting, baked/static
- [ ] Profiling, standalone Quest 3 build i test

---

## Scene stanje (MainScene)

**Postoji:**
- `[BuildingBlock] Camera Rig` — OVRCameraRig
- `[BuildingBlock] Passthrough` — AR passthrough
- `[BuildingBlock] Poke Interaction` — finger poke input
- `[BuildingBlock] Cube` — test interaktivni objekt
- `Directional Light`, `Plane` (pod)

**Nedostaje (Faza 1+):**
- Hodnik geometrija, task konzola, kavez
- GameManager, HRReceiver, HRDisplay GameObjecti
- Task interactive elements (buttons, dials, sliders)
- Debriefing Canvas

---

## Repo stanje

```
Stres-inhibitator-main/          ← root, Claude Code otvara odavde
  CLAUDE.md
  .gitignore
  .mcp.json                      ← relativna putanja ka MCP Unity serveru
  platform-tools/                ← ADB
  stari fajlovi/                 ← stari Python HR eksperimenti (ne commitovati tajne)
  VR_StressTraining/
    .mcp.json                    ← Unity auto-generisan
    Assets/
      Scenes/MainScene.unity     ← JEDINA SCENA
      Scripts/
        HR/HRReceiver.cs
        HR/HRDisplay.cs
        GameManager.cs
        PuzzleManager.cs
        PuzzleButton.cs
        StressRoomController.cs
        DebriefingManager.cs
    Packages/
      manifest.json              ← granularni Meta paketi, BEZ sdk.all
      packages-lock.json
```

---

## Status projekta

- [x] HR pipeline radi (Band 9 → Python → UDP → Unity)
- [x] start.bat kreiran (dev/test tool za HR)
- [x] Unity 6.3 LTS (6000.3.16f1) + Android moduli
- [x] Granularni Meta XR paketi 201.0.0 (bez sdk.all/Voice/Wit)
- [x] Project Setup Tool — 78 Verified Items
- [x] OpenXR + Meta XR konfigurisano
- [x] Quest 3 — APK deployovan i testiran
- [x] Sve core skripte napisane (7 fajlova u Assets/Scripts/)
- [x] MainScene sa Camera Rig + Passthrough + Poke Interaction
- [x] MCP Unity server setup (.mcp.json + Node.js build)
- [x] Quest Link / Meta Horizon Link Play Mode radi
- [x] Repo cleanup (nema _Recovery, nema .bak, nema sdk.all)
- [x] CLAUDE.md ažuriran sa Adaptive Stress Corridor konceptom
- [ ] **Git repo inicijalizovan** ← SLJEDEĆE
- [ ] Spatial blockout (Faza 1) — čeka potvrdu
- [ ] Task arhitektura implementirana — čeka potvrdu
- [ ] HR integracija u task cycle
- [ ] Adaptive scheduler
- [ ] ML model (bonus, future extension)
