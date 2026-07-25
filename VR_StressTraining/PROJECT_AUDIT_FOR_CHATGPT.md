# Project Audit

Audit date: 2026-07-12  
Audited project root: `C:\Users\djuro\Desktop\Stres-inhibitator-main\VR_StressTraining`  
Repository root: `C:\Users\djuro\Desktop\Stres-inhibitator-main`

Status vocabulary used below:

- **IMPLEMENTED**: present, serialized/connected, and supported by a concrete project artifact.
- **PARTIAL**: a useful fragment exists, but the target behavior is not complete or not connected.
- **MISSING**: no implementation was found in the inspected source, scene, prefab, or settings.
- **BROKEN**: a concrete defect was reproduced or a required referenced path was proven absent.
- **UNUSED**: an asset/code path exists but has no serialized reference in the current app scene/assets.
- **UNKNOWN**: the available evidence cannot determine the fact without executing or visually inspecting Unity/Quest.

## 1. Executive Summary

The current repository is a **spatial/XR prototype, not yet a functioning multi-session stress-training application**. The only build scene is `Assets/Scenes/MainScene.unity`; it contains an OVR camera rig, controller and hand-tracking building blocks, passthrough, one generic poke-interaction sample, a static corridor blockout, a Blender console shell, a cage placeholder, and a robot-arm model. Evidence: `ProjectSettings/EditorBuildSettings.asset`, `Assets/Scenes/MainScene.unity` root objects `[BuildingBlock] Camera Rig`, `[BuildingBlock] Passthrough`, `Poke Interaction`, `Corridor_Blockout`, `RobotArm_Scene`, and prefab instance `Console_BlenderPrototype`.

Only one project-owned runtime script is serialized in `MainScene`: `RobotArmPoseTester` on `RobotArm_Scene`. `GameManager`, `PuzzleManager`, `PuzzleButton`, `StressRoomController`, `HRReceiver`, `HRDisplay`, and `DebriefingManager` exist as C# files but have zero serialized references in any current `.unity`, `.prefab`, or `.asset` under `Assets`. Evidence: script GUID search from `Assets/Scripts/*.cs.meta` against `Assets/Scenes/MainScene.unity` and all serialized project assets; only GUID `cf0ee29f28ff78d4a8f126bf60802668` (`RobotArmPoseTester.cs`) occurs.

The external HR proof-of-concept is materially developed but disconnected from the Unity scene. `hr_dashboard_v2.py` reads Mi Fitness/Xiaomi logcat through ADB, exposes Flask on TCP 8888, and sends `{"hr": <int>, "ts": "HH:mm:ss"}` over UDP to `127.0.0.1:5005`; `Assets/Scripts/HR/HRReceiver.cs` can receive that packet. However, `HRReceiver` is not instantiated in `MainScene`, `start.bat` and `hr_dashboard_v2.py` point to nonexistent `C:\Users\Korisnik\Desktop\Diplomski\...` paths, and loopback UDP cannot carry PC packets to a standalone Quest process. Evidence: `hr_dashboard_v2.py` constants `ADB_PATH`, `UDP_IP`, `UDP_PORT`; `start.bat`; `HRReceiver.Start()`; path existence check returned `HardcodedAdbExists=False`, `HardcodedBridgeExists=False`, while repository `platform-tools\adb.exe` exists.

The following target capabilities are **MISSING** from the connected app: SafeSpace; username/profile selection; persistent multi-user data; 48-hour scheduling; tablet UI; n-back; go/no-go; flanker; post-session scheduler; SSQ; NASA-TLX; pause/terminate session flow; event-level data logging; voice/audio; neutral/stress mode selection; and functional corridor collapse. Evidence: no matching classes, serialized objects, canvases, audio files, AudioSources, or persistence APIs were found; `MainScene.unity` contains `Canvas=0`, `AudioSource=0`, `ParticleSystem=0`.

The strongest reusable assets are the XR rig/build settings, corridor dimensions, console shell, cage geometry, Poke/teleport interaction examples, URP materials, robot-arm model, and the HR parser/UDP receiver concept. The current `CLAUDE.md` contains several stale claims (for example, that `GameManager`, `HRReceiver`, and `HRDisplay` are in the scene), contradicted by the serialized scene GUID audit. Evidence: `CLAUDE.md` lines under “Scene stanje” versus `Assets/Scenes/MainScene.unity` script references.

No screenshot folder was created. No package install/update, scene load/save, compile command, build, or automatic upgrade was executed.

## 2. Repository and Unity Environment

### Git

- Git repository: **yes**; `git rev-parse --show-toplevel` returned `C:/Users/djuro/Desktop/Stres-inhibitator-main`.
- Branch: `main` (`git branch --show-current`).
- Initial `git status --short` before audit:

```text
 M VR_StressTraining/Assets/Scenes/MainScene.unity
 M VR_StressTraining/Assets/Settings/URP_Balanced.asset
?? "High-Credibility Literature Shortlist for Adaptive Stress Corridor VR.docx"
?? Prva_iteracija_diplomskog_rada_Mihailo_Djurovic.docx
?? VR_StressTraining/.codex/
```

- The pre-existing `MainScene.unity` diff removes `Socket_Center` and `Insert_Origin` children from the console instance (74 deletions, 1 addition). The pre-existing `URP_Balanced.asset` diff changes `m_RenderScale` from `1` to `1.6`. Evidence: `git diff -- VR_StressTraining/Assets/Scenes/MainScene.unity` and `git diff -- VR_StressTraining/Assets/Settings/URP_Balanced.asset` captured before audit.
- Last five commits:

```text
5aa9e63  2026-06-02  Disable SmoothMovementTunneling vignette on rotation
316d051  2026-06-02  Simplify Console_BlenderPrototype: remove duplicate Visual, replace proxy boxes with MeshCollider
716820a  2026-06-02  Document robot arm animation rules (inspection only, no animation)
1136257  2026-06-02  Document URP magenta fix and Coplay build; ignore transient test-run files
9ebfd44  2026-06-02  Activate URP render pipeline and convert scene materials to URP/Lit
```

### `.gitignore`

The root `.gitignore` excludes Unity-generated `Library`, `Temp`, `Obj`, `Build`, `Builds`, `Logs`, `UserSettings`, generated IDE files, Node `node_modules`, Python caches/venvs, `.utmp`, Burst `*DoNotShip`, APK/AAB/unitypackage artifacts, secrets (`.env`, `*.local.json`, `*auth_key*`, `*.key`, `stari fajlovi/auth_key.txt`), and `platform-tools/`. Evidence: repository `.gitignore`. `VR_StressTraining/.gitignore` duplicates core Unity/IDE/build/cache exclusions. A notable exception is that three backup archives and authentication-related source are already tracked: `stari fajlovi/mifitness_backup.ab`, `wearable_backup.ab`, `xiaomi_backup.ab`, and `ble_hr_auth.py` (`git ls-files`).

### Unity and platform settings

| Setting | Actual value | Evidence |
|---|---|---|
| Unity version | `6000.3.16f1` revision `a56f230f6470` | `ProjectSettings/ProjectVersion.txt` |
| Build scene | only `Assets/Scenes/MainScene.unity`, enabled, index 0 | `ProjectSettings/EditorBuildSettings.asset` |
| Target profile | `Meta Quest.asset`, `m_BuildTarget: 13` (Android serialized target) | `Assets/Settings/Build Profiles/Meta Quest.asset` |
| Product / package | `VR_StressTraining`; `com.DefaultCompany.VR_StressTraining` in base settings | `ProjectSettings/ProjectSettings.asset` |
| Android min / target API | 32 / 34 | `ProjectSettings/ProjectSettings.asset`; build profile lines 230–231 |
| Architecture | serialized `AndroidTargetArchitectures: 2` (ARM64 in Unity Android settings) | `ProjectSettings/ProjectSettings.asset:271`; build profile line 321 |
| Scripting backend | build-profile Android override `1` (IL2CPP) | `Assets/Settings/Build Profiles/Meta Quest.asset:748-749` |
| API compatibility | serialized value `6`; human-facing enum label **UNKNOWN** without opening Player Settings | `ProjectSettings/ProjectSettings.asset:801`; profile line 842 |
| Active input | base setting `1`; build-profile snapshot `2`; effective runtime choice **UNKNOWN** because values conflict and active profile state was not queried | `ProjectSettings/ProjectSettings.asset:803`; `Meta Quest.asset:844` |
| Color space | serialized `1` (Linear) | `ProjectSettings/ProjectSettings.asset:50` |
| Quality | Android default index 2 = `Medium`; editor current index 5 = `Ultra`; additional `Meta Quest (Build Profile)` profile exists at index 6 | `ProjectSettings/QualitySettings.asset` |
| Render pipeline | URP asset `Assets/Settings/URP_Balanced.asset` | `ProjectSettings/GraphicsSettings.asset` GUID `be832...`; asset name `URP_Balanced` |
| URP notable values | HDR off, MSAA 2x, render scale 1.6 (uncommitted), main shadows on at 2048, shadow distance 50, additional lights per-object 4, additional-light shadows off | `Assets/Settings/URP_Balanced.asset` |
| XR loader | OpenXR loader listed for Android and Standalone; `m_InitManagerOnStart: 1` | `Assets/XR/XRGeneralSettingsPerBuildTarget.asset`; `OpenXRLoader.asset` |
| XR automatic flags | manager `m_AutomaticLoading: 0`, `m_AutomaticRunning: 0`; initialization still enabled through XRGeneralSettings | `Assets/XR/XRGeneralSettingsPerBuildTarget.asset` |
| OpenXR Android features | Meta XR Feature on; Meta XR foveation on; Oculus Touch profile on; Unity HandTracking feature off; Meta Quest Touch Plus profile off | `Assets/XR/Settings/OpenXR Package Settings.asset` |
| Meta hand setting | `handTrackingSupport: 1`, frequency 0; exact enum semantics **UNKNOWN** statically | `Assets/Oculus/OculusProjectConfig.asset` |
| Android manifest | Quest 2/Pro/3/3S, VR headtracking required, optional hand tracking/passthrough, hand/scene/anchor/camera permissions | `Assets/Plugins/Android/AndroidManifest.xml` |

Two APK artifacts prove that Android builds existed, not that the current working tree builds: `api.apk` 94,038,121 bytes dated 2026-06-03 and `Builds/VR_StressTraining.apk` 84,510,550 bytes dated 2026-06-02.

### Packages

Direct dependencies in `Packages/manifest.json`:

- Git: `com.coplaydev.coplay` (`#beta`), `com.gamelovers.mcp-unity`.
- Meta: `com.meta.xr.mrutilitykit 201.0.0`, `runtimeoptimizer 0.2.2`, `sdk.core 201.0.0`, `sdk.haptics 201.0.0`, `sdk.interaction 201.0.0`, `sdk.interaction.ovr 201.0.0`, `sdk.platform 201.0.0`.
- Unity: AI Assistant `2.9.0-pre.2`, AI Inference `2.6.1`, Multiplayer Center `1.0.1`, URP declared `17.0.3`, XR Management `4.5.0`, OpenXR `1.14.0`, plus the built-in modules listed in the manifest (accessibility, adaptive performance, AI, Android JNI, animation, asset bundle, cloth, director, image conversion, IMGUI, JSON serialization, particles, physics/2D, screenshot, terrain/physics, tilemap, UI/UIElements, Umbra, analytics, web request variants, vector graphics, vehicles, video, VR, wind, XR).

Important lockfile resolution: `packages-lock.json` resolves `com.unity.render-pipelines.universal` to `17.3.0` even though the manifest declares `17.0.3`; it also locks Input System `1.19.0`, Burst `1.8.29`, Collections `2.6.6`, Mathematics `1.3.3`, TextMeshPro `5.0.0`, uGUI `2.0.0`, XR Hands `1.7.3`, XR Core Utils `2.6.0`, Newtonsoft JSON `3.2.2`, and Test Framework `1.6.0`. This manifest/lock discrepancy is evidence of package-state drift; no package operation was performed.

There are **no project-owned `.asmdef` files under `Assets`**; all runtime C# therefore compiles into `Assembly-CSharp`, and editor scripts into `Assembly-CSharp-Editor`. Evidence: `rg --files Assets -g '*.asmdef'` returned none; `Library/ScriptAssemblies/Assembly-CSharp.dll` and `Assembly-CSharp-Editor.dll` exist. Relevant package assembly relationships include `Oculus.Interaction -> GUID:6055...`, `Oculus.Interaction.OVR -> Oculus.Interaction + Oculus.VR`, `Oculus.VR -> URP + OpenXR + XR Management + Input System + XR Hands`, `McpUnity.Editor -> GUID:478a...`, and `Meta.XR.BuildingBlocks -> Oculus.VR + Unity.InputSystem + ImmersiveDebugger.Interface`; evidence is the corresponding package `.asmdef` files under `Library/PackageCache`.

## 3. Relevant File Tree

Generated folders (`Library`, `Temp`, `Logs`, `Builds`, `.utmp`, caches, IDE projects) are intentionally omitted.

```text
Stres-inhibitator-main/
├─ .gitignore
├─ .mcp.json
├─ CLAUDE.md
├─ hr_dashboard_v2.py                 [Python/ADB/Flask/UDP HR bridge]
├─ start.bat                          [legacy hard-coded launcher]
├─ mcp_diag.ps1                       [MCP diagnostic utility]
├─ tools/run-mcp-unity.bat            [MCP Node wrapper]
├─ platform-tools/                    [ADB binaries; gitignored]
├─ stari fajlovi/                     [33 tracked legacy HR/BLE/debug/backup files]
└─ VR_StressTraining/
   ├─ .mcp.json
   ├─ .codex/config.toml              [untracked; created before audit]
   ├─ Assets/
   │  ├─ Audio/                       [empty]
   │  ├─ Animations/                  [empty]
   │  ├─ Materials/
   │  │  ├─ Blockout/                 [7 URP/blockout materials]
   │  │  └─ MAT_Console_Prototype_Grey.mat
   │  ├─ Models/
   │  │  ├─ Console/ConsoleBody_Crescent_UnityFit.fbx
   │  │  └─ RobotArm/RobotArm_Placeholder.fbx
   │  ├─ Oculus/                      [Meta project config/setup]
   │  ├─ Plugins/Android/AndroidManifest.xml
   │  ├─ Prefabs/                     [empty]
   │  ├─ Resources/                   [Meta runtime/input/audio settings; no game data SO]
   │  ├─ Scenes/MainScene.unity       [only scene]
   │  ├─ Scripts/
   │  │  ├─ DebriefingManager.cs
   │  │  ├─ GameManager.cs
   │  │  ├─ PuzzleButton.cs
   │  │  ├─ PuzzleManager.cs
   │  │  ├─ StressRoomController.cs
   │  │  ├─ HR/{HRReceiver.cs, HRDisplay.cs}
   │  │  ├─ RobotArm/RobotArmPoseTester.cs
   │  │  └─ Editor/{SpatialBlockoutBuilder.cs, ConsolePrototypeBuilder.cs}
   │  ├─ Settings/                    [URP + Meta Quest build profile]
   │  ├─ StreamingAssets/             [empty]
   │  ├─ Tree.prefab + Tree_Textures/ [unused legacy/sample asset]
   │  └─ XR/                          [OpenXR loader/settings]
   ├─ Packages/{manifest.json, packages-lock.json}
   └─ ProjectSettings/                [Unity/graphics/quality/build/XR/MCP settings]
```

No project-owned Input Actions `.inputactions` file, UI folder/content, ScriptableObject data model, test folder, audio file, or networking plugin exists. Evidence: extension inventory under `Assets` found 10 `.cs`, 1 `.unity`, 1 `.prefab`, 2 `.fbx`, 20 `.asset`, 12 `.mat`, 4 `.png`, 1 `.xml`, and no `.inputactions`/audio extensions. `Assets/Resources/InputActions.asset` is a Meta runtime settings asset with empty `InputActionDefinitions` and `InputActionSets`, not an authored Input Action Asset.

## 4. Scenes and Build Flow

### Scene inventory and build order

| Scene | Build index | Status | Evidence |
|---|---:|---|---|
| `Assets/Scenes/MainScene.unity` | 0 | PARTIAL | Sole `.unity` file and sole enabled `EditorBuildSettings` entry |

No SafeSpace, menu, debrief/end, test, or obsolete `.unity` scenes exist. Therefore there is no scene transition flow and no `SceneManager` use in `Assets/Scripts`.

### `MainScene` hierarchy

Main root objects (YAML `Transform.m_Father: {fileID: 0}`):

```text
[BuildingBlock] Camera Rig
[BuildingBlock] Cube
[BuildingBlock] Passthrough
Corridor_Blockout
Directional Light
Poke Interaction
RobotArm_Scene
Teleport Hotspot
Console_BlenderPrototype             [root FBX prefab instance]
```

Relevant corridor hierarchy:

```text
Corridor_Blockout
├─ Wall_Left / Wall_Right
├─ Wall_ConsoleEnd / Wall_CageEnd
├─ Floor / Ceiling
├─ PlayerSpawnPoint                  local (0, 0, -3.5), no consumer script
├─ Console_Placeholder               inactive
│  ├─ Surface
│  └─ Body
├─ Cage_Placeholder
│  ├─ Rail_Top / Rail_Bottom
│  ├─ Bar_Right1 / Bar_Right2 / Bar_Right3 / Bar_Centre
│  ├─ Post_Left / Post_Right
│  └─ Door_Pivot / Door_Panel
├─ Light_ConsoleZone
└─ Light_CageZone
```

`Console_BlenderPrototype` is an active FBX prefab instance at `(0,0,-4.277)`. Its two source proxy colliders were removed and a non-convex `MeshCollider` was added to the shell; its material is overridden to `M_KnownGood_NoMagenta_URP.mat`. Evidence: `MainScene.unity` `PrefabInstance &1920961862` and `MeshCollider &1135227842`. The uncommitted scene diff removes previously added `Socket_Center` and `Insert_Origin`, so the current working scene has no console socket/insert anchors.

`RobotArm_Scene` is at `(-1.15,1.67,-2.97)`, contains a `RobotArm_Placeholder.fbx` instance, and has `RobotArmPoseTester`; its loop flag is serialized false. Evidence: `MainScene.unity` and `Assets/Scripts/RobotArm/RobotArmPoseTester.cs`.

The camera rig is an `OVRCameraRig` with `OVRManager`, `OVRHeadsetEmulator`, controller-tracking blocks, hand-tracking blocks, left/right/center eye anchors, and an `AudioListener` on `CenterEyeAnchor`. Evidence: `MainScene.unity` GUID resolution to `OVRCameraRig.cs`, `OVRManager.cs`, `OVRHeadsetEmulator.cs`. The rig root is at `(0,0,0)`, not linked to `PlayerSpawnPoint`; actual HMD-relative initial pose in Play Mode is therefore **UNKNOWN**.

There are three lights: Directional intensity 1, point `Light_ConsoleZone` intensity 1.2/range 8, point `Light_CageZone` intensity 0.5/range 7. All serialize shadow type 2. Evidence: the three `Light` YAML documents in `MainScene.unity`.

Scene component counts: 290 GameObjects, 21 MeshRenderers, 35 SkinnedMeshRenderers (mostly embedded controller models), 3 Lights, 3 Cameras, 1 AudioListener, 0 Canvas, 0 AudioSource, and 0 ParticleSystem. Evidence: YAML document type counts.

Missing-script status is **UNKNOWN** without a successful current Unity Console read. Static YAML proves all project-script references resolve and all inspected Meta component GUIDs resolve to installed packages, but a complete package-wide GUID integrity pass was not used as proof.

## 5. Prefabs, Models and Assets

The only project `.prefab` is `Assets/Tree.prefab` (42,327 bytes), with zero references in `MainScene`; status **UNUSED/legacy sample**. It uses four tiny textures in `Assets/Tree_Textures` and has no documented source/license in the repository.

| Asset | Components / scene use | Classification | Evidence |
|---|---|---|---|
| `Assets/Models/Console/ConsoleBody_Crescent_UnityFit.fbx` | Active root instance `Console_BlenderPrototype`; shell material override; non-convex MeshCollider; no Rigidbody, authored controls, sockets, animator, or AudioSource in scene | PARTIAL visual prototype | FBX meta GUID `9a121...`; `MainScene` prefab instance and added MeshCollider |
| `Assets/Models/RobotArm/RobotArm_Placeholder.fbx` | Child of `RobotArm_Scene`; four material mappings; pose tester searches `BasePivot`, `ShoulderPivot`, `ElbowPivot`, `WristPivot`; no production animator/control | PARTIAL/temporary | FBX meta GUID `096a...`; `RobotArmPoseTester.cs` |
| `Corridor_Blockout` scene geometry | BoxCollider + MeshRenderer + MeshFilter on walls/floor/ceiling/end walls; static dimensions approx. 3.2m x 5.6m x 10.4m | IMPLEMENTED blockout | `MainScene.unity`; `SpatialBlockoutBuilder.cs` |
| `Cage_Placeholder` scene geometry | static rails/posts/bars/door pivot; door has collider; no connected threat controller | PARTIAL visual blockout | `MainScene.unity`; `SpatialBlockoutBuilder.BuildCagePlaceholder()` |
| `Poke Interaction` | Meta `PokeInteractable`, Plane/Clipped surface, visual/animator feedback; independent at world origin; no project gameplay callback | PARTIAL interaction sample | `MainScene.unity`; package `PokeInteractable.cs`; no `PuzzleButton` GUID reference |
| `Teleport Hotspot` | Meta `TeleportInteractable`, `ColliderSurface`, reticle visuals | IMPLEMENTED sample locomotion target, not session flow | `MainScene.unity` package GUIDs |
| `[BuildingBlock] Cube` | MeshRenderer/Filter/BoxCollider and BuildingBlock tag; no project gameplay script | UNUSED test object | `MainScene.unity` |

No console switches, levers, knobs/potentiometers, task buttons, tablet, watch model, HR UI prefab, audio prefab, or particle prefab exists as a project asset. Evidence: prefab/model inventory and scene names/components.

Materials: only `M_KnownGood_NoMagenta_URP.mat` is directly referenced by the scene (18 YAML references, including the console override). Imported console/robot materials are mapped in FBX metas. The other blockout materials and `MAT_Console_Prototype_Grey.mat` have zero serialized scene references and are editor-builder/prototype leftovers. Evidence: material GUID reference counts.

## 6. C# Architecture

All project classes use the global namespace. No ScriptableObject or plain domain/data class exists.

| Path / class | Type | Responsibility and dependencies | Serialized use / data effects | Status |
|---|---|---|---|---|
| `Assets/Scripts/GameManager.cs` / `GameManager` | MonoBehaviour singleton | Four enum phases; `calibrationDuration`; three UnityEvents; `DontDestroyOnLoad`; methods `StartCalibration`, `StartCalmRoom`, `StartStressRoom`, `StartDebriefing` | No asset/scene references; changes only in-memory `CurrentPhase`; does not load scenes, time tasks, save, pause, or validate transitions | UNUSED / PARTIAL concept |
| `Assets/Scripts/PuzzleManager.cs` / `PuzzleManager` | MonoBehaviour singleton | Generates random integer sequence; shows highlights; accepts button indices; replays after error; 3 hints; UnityEvents | No references; in-memory sequence/player index only; no seed, logs, reaction time, miss timeout, blocks, persistence, or difficulty model | UNUSED / PARTIAL old memory task |
| `Assets/Scripts/PuzzleButton.cs` / `PuzzleButton` | MonoBehaviour | Renderer color feedback; `buttonIndex`; calls `PuzzleManager.Instance.OnPlayerPressedButton` from public `OnPressed()` | No references; comment says Meta Poke or raycast but no serialized event links it | UNUSED |
| `Assets/Scripts/StressRoomController.cs` / `StressRoomController` | MonoBehaviour | Moves four assigned walls for 120s; opens cage; optionally triggers tiger Animator and AudioClips | No references; no pause/reset-to-start, neutral mode, levels, game-over, scheduler, or task connection | UNUSED / PARTIAL old threat concept |
| `Assets/Scripts/HR/HRReceiver.cs` / `HRReceiver` | MonoBehaviour | Background UDP listener on loopback port 5005; parses `hr`/`ts`; validates 30–220; main-thread event `OnHRUpdated`; 10s stale reset | No scene reference; static current HR/timestamp only; no sample persistence/session correlation/reconnect state beyond receive loop | UNUSED / PARTIAL bridge endpoint |
| `Assets/Scripts/HR/HRDisplay.cs` / `HRDisplay` | MonoBehaviour | Subscribes to `OnHRUpdated`; updates TMP labels/Image with fixed absolute HR zones | No scene reference and no Canvas/TMP target; displays raw BPM, contrary to target “zone only during task” | UNUSED / PARTIAL prototype |
| `Assets/Scripts/DebriefingManager.cs` / `DebriefingManager` | MonoBehaviour | Collects HR event values in memory; shows completion time, max/avg HR, hints; draws LineRenderer graph | No references; `sessionStartTime` assigned but unused; no baseline/delta/recovery/TLX/SSQ/save | UNUSED / PARTIAL prototype |
| `Assets/Scripts/RobotArm/RobotArmPoseTester.cs` / `RobotArmPoseTester` | MonoBehaviour | Finds four named pivots, caches rest transforms, applies additive test poses/optional loop | Serialized once on `RobotArm_Scene`; explicitly documented “TEMP TESTER — remove before shipping” | IMPLEMENTED temporary diagnostic |
| `Assets/Scripts/Editor/SpatialBlockoutBuilder.cs` / `SpatialBlockoutBuilder` | static Editor class | Menu tool creates `Corridor_Blockout`, shell, inactive placeholder console, cage, lights, spawn marker | Editor-only; generated geometry is saved in scene; not runtime code | IMPLEMENTED editor utility |
| `Assets/Scripts/Editor/ConsolePrototypeBuilder.cs` / `ConsolePrototypeBuilder` | static Editor class | Menu tool builds an unsaved primitive console preview at z=20; can create `MAT_Console_Prototype_Grey.mat` | No runtime/scene reference; material exists | UNUSED editor prototype utility |

Event topology is limited to: `HRReceiver.OnHRUpdated -> HRDisplay.OnHR` and `DebriefingManager.OnHRSample` in code; `PuzzleButton -> PuzzleManager`; and `PuzzleManager` UnityEvents. None of those participants is serialized in the current scene. `GameManager` UnityEvents also have no serialized host. Evidence: source methods/events and zero serialized GUID references.

No duplicate class names were found among the 10 active `Assets/Scripts` files. Legacy duplicates exist outside Unity under `stari fajlovi/HRReceiver.cs` and `HRDisplay.cs`, but that folder is outside `Assets` and does not compile into Unity.

## 7. Actual Current User Flow

| Step | Actual behavior | Status / evidence |
|---:|---|---|
| 1 | Application loads `MainScene` because it is the only enabled build scene. | IMPLEMENTED — `EditorBuildSettings.asset` |
| 2 | User is placed under an OVR camera rig at scene origin; HMD determines eye pose. A static corridor, console shell, cage, robot arm, passthrough, poke sample, teleport sample, and test cube are present. | PARTIAL — `MainScene.unity`; exact first-person visual is UNKNOWN without Game View/Quest screenshot |
| 3 | There is no start/menu/profile action; no Canvas or session controller exists. | MISSING — `Canvas=0`; no relevant script reference |
| 4 | `PlayerSpawnPoint` exists at `(0,0,-3.5)` but no component reads it, so it does not place the rig. | PARTIAL/UNUSED — scene transform + no script reference |
| 5 | User may use the packaged teleport hotspot depending on runtime controller state; there is no coded route to the console. | PARTIAL — `TeleportInteractable` exists; no session flow |
| 6 | No tablet exists. | MISSING — no object/asset/class named tablet and no Canvas |
| 7 | One standalone Meta poke sample can animate/select visually; console shell has only a MeshCollider and no authored controls. | PARTIAL — Poke objects and console instance; no `PuzzleButton` reference |
| 8 | No correct/incorrect gameplay evaluation is connected. | MISSING — puzzle code unused; Poke Unity wrapper has no gameplay select callback |
| 9 | No active sequence exists. | MISSING at runtime — `PuzzleManager` unused |
| 10 | No session/task timer exists. | MISSING — only unused wall duration and calibration Invoke fields exist |
| 11 | Corridor does not move; cage door is static; `StressRoomController` is unused. | MISSING at runtime |
| 12 | No pause input/menu/system exists. | MISSING — no matching source or scene object |
| 13 | No session completion path exists. | MISSING |
| 14 | Nothing is saved by Unity gameplay. | MISSING — no PlayerPrefs, file, database, JSON save, or persistence class |
| 15 | Restart reloads `MainScene`; only Unity/Meta framework state persists, not a user/session model. | PARTIAL — build flow and absence of save code |

## 8. User Profiles and Persistence

All requested profile/persistence fields are **MISSING**: username, user list, create/select profile, active profile, multiple profiles, last/next session date, configurable interval/48-hour default, history, task levels, pressure level, scores, reaction times, errors, pulse samples, questionnaires, scheduler decision, and reason log. Evidence: repository search across project source for `PlayerPrefs`, `persistentDataPath`, `username`, `UserProfile`, `PlayerProfile`, `DateTime`, `next session`, `NASA`, `TLX`, `SSQ`, `scheduler`, `JsonUtility`, `SQLite`, `CSV`, and file-write APIs found no gameplay implementation.

Actual data model: none. Actual save format/location/corruption handling/user separation: **MISSING**. Uninstall behavior: there is no app-owned training record to lose; Meta/Unity platform settings are not a training profile. Evidence: no persistence code or data asset.

## 9. SafeSpace

SafeSpace is **MISSING**. There is no SafeSpace scene, room root, preparation UI, profile selection, questionnaire, baseline flow, coping tutorial, or post-session review area. Evidence: only `MainScene.unity`; its relevant roots and component inventory contain no SafeSpace or Canvas. `GameManager` has an unused `CalmRoom` enum/event, but no serialized instance or spatial implementation.

## 10. Tablet and Console

### Tablet

Tablet is **MISSING**: no model, prefab, GameObject, World Space Canvas, RenderTexture, controller class, input, instruction/sequence/result display, or “tablet” string exists in project source/scene. Evidence: file/name/code search and `Canvas=0` in `MainScene`.

### Console

- Visual shell: **PARTIAL**, `Console_BlenderPrototype` from `ConsoleBody_Crescent_UnityFit.fbx` at z=-4.277.
- Physics: one enabled non-trigger, non-convex `MeshCollider`; no Rigidbody. Evidence: `MeshCollider &1135227842`.
- Controls: **MISSING**. No buttons/switches/levers/knobs/sliders are children of the console instance, and current uncommitted scene changes removed `Socket_Center` and `Insert_Origin` anchors.
- Interaction: the independent `Poke Interaction` sample at world origin has Meta Poke/visual components, but it is not parented to or linked with the console and has no project gameplay callback.
- Feedback: sample poke has animator/color/debug visuals; no console-specific haptic/audio/result feedback exists. Evidence: `PokeInteractableVisual`, `InteractableColorVisual`, and zero scene AudioSources.
- Input mode: camera rig includes controller and hand-tracking building blocks. Effective runtime hand/controller availability is **UNKNOWN**; OpenXR’s generic HandTracking feature is disabled, while Meta project `handTrackingSupport` is serialized as 1.
- Wrong-element logging, modular slots, overlap prevention, and tablet-as-display/console-as-only-input separation are **MISSING**.

## 11. Tasks and Sequence Logic

| Task/feature | Actual state | Evidence |
|---|---|---|
| Old memory sequence | PARTIAL code, UNUSED runtime | `PuzzleManager.GenerateAndShowSequence`, `ShowSequence`, `OnPlayerPressedButton`; zero serialized references |
| n-back | MISSING | no class/string/state model |
| go/no-go | MISSING | no class/string/state model |
| flanker | MISSING | no class/string/state model |
| Sequence generator | PARTIAL | `Random.Range(0, buttons.Length)` for `sequenceLength=5` |
| Seed/reproducibility | MISSING | no seed storage or `Random.InitState` |
| Blocks/trials | MISSING | no block/trial classes |
| Stimulus timing | PARTIAL old sequence | display 1.0s + gap 0.5s in unused `PuzzleManager` |
| Response deadline/missed response | MISSING | player turn has no timeout |
| Correct/wrong | PARTIAL old sequence | index equality; error replays sequence after 2s |
| Reaction time | MISSING | no timestamp per response |
| Score | MISSING | only solved/hints in memory |
| Difficulty | PARTIAL static inspector values | `sequenceLength`, durations; no scheduler or persisted levels |

The old sequence displays stimuli by changing the same physical button renderers, not on a tablet. Its input is `PuzzleButton.OnPressed()`. Its output is three UnityEvents and in-memory `IsSolved/HintsUsed`; it saves nothing. Evidence: `PuzzleManager.cs`, `PuzzleButton.cs`.

## 12. Heart-Rate Pipeline

### Actual coded chain

```text
Xiaomi Band 9 / Redmi Watch 3 Active
  -> Mi Fitness on Android phone (external app; no phone source in repo)
  -> ADB-over-Wi-Fi :5555
  -> `adb logcat -v time`
  -> `hr_dashboard_v2.py` regex parsing/deduplication
  -> UDP JSON to 127.0.0.1:5005
  -> `HRReceiver.cs` (only if instantiated in the same machine/process environment)
  -> static CurrentHR/LastTimestamp + OnHRUpdated
  -> `HRDisplay` / `DebriefingManager` (not instantiated)
```

Concrete bridge details:

- ADB source: `hr_dashboard_v2.py` runs `adb connect <host>:5555` and `adb -s <host> logcat -v time`.
- Host discovery: scans local `/24` for TCP 5555, fallback `192.168.1.222:5555`; local subnet discovery attempts a UDP connect to `8.8.8.8:80` to determine the local IP. Evidence: `find_adb_host()`.
- Parsers: Xiaomi `HrItem(... time=<unix>, hr=<int>)`, `single_heart_rate`, `latestHrRecord`, `realTimeHeartRate`, and generic `hr=` patterns; accepted range 30–220. Evidence: `HR_PATTERNS`, `HR_ITEM_TS_RE`.
- Deduplication: highest watch Unix timestamp, or same-HR 3-second throttle for records without watch timestamp; logcat lines older than 120 seconds are skipped. Evidence: `_highest_watch_ts`, `MAX_AGE_SEC`.
- UDP format: UTF-8 JSON `{"hr": 82, "ts": "15:31:50"}`; port 5005. Unity repeats 30–220 validation. No source device ID, session ID, monotonic/event timestamp, sequence number, or checksum exists.
- Flask dashboard: binds `0.0.0.0:8888`, route `/api/hr`, in-memory last 200 values; no authentication/TLS/persistent log. Evidence: `app.run(...)`, `hr_data`.
- Keepalive: posts Android notification every 180 seconds; failure is printed, not recovered by a state machine. Evidence: `keepalive_pinger()`.
- Reconnect: **MISSING** after logcat process exits; no supervising loop restarts ADB/logcat. Unity receiver only waits on one UDP socket.
- No-data behavior: Unity resets `CurrentHR` to 0 after 10 seconds and logs “signal lost”; Python has no explicit stale reset of dashboard `current`.
- Session/task association: **MISSING**. `DebriefingManager` only appends integer samples and does not store timestamps/events.

Concrete blockers:

1. `start.bat` and `hr_dashboard_v2.py` hard-code `C:\Users\Korisnik\Desktop\Diplomski\...`; those files do not exist on this machine, while repo ADB is at `C:\Users\djuro\Desktop\Stres-inhibitator-main\platform-tools\adb.exe`. Status **BROKEN** for the current checkout.
2. `HRReceiver` is absent from `MainScene`. Status **MISSING at runtime**.
3. UDP destination and bind are loopback. This can work with Unity Editor on the PC, but standalone Quest’s `127.0.0.1` is the headset itself; the Python process on PC cannot reach it. Status **BROKEN for standalone Quest architecture** unless a non-loopback transport/address is introduced.

Legacy folder `stari fajlovi` contains older BLE/auth/cloud/ADB experiments and logs, but none is imported by active `hr_dashboard_v2.py` or Unity. It is not evidence of a production pipeline.

## 13. Pause and Session Termination

Every requested pause/termination feature is **MISSING**: pause input, pause menu, Continue, End Session, timer/task/pressure freeze, console input blocking, countdown resume, headset-removal pause, HR `paused` labeling, termination reason, technical/user reason taxonomy, and validity flag. Evidence: no pause/session classes or serialized UI; no `Time.timeScale` use; no Canvas.

`PuzzleManager.pauseBetweenSteps` is merely a sequence gap and is not a pause system. `start.bat`’s shell `pause` is unrelated to Unity runtime.

## 14. Audio and Voice

There are no project AudioClip files, no `AudioSource` component in `MainScene`, no AudioMixer, no narration/TTS/subtitle/localization script, and `Assets/Audio` is empty. Evidence: asset extension inventory and scene component count.

| Existing audio-capable code | Reference status | Actual use |
|---|---|---|
| `StressRoomController.ambientAudio`, `tigerRoarClip`, `wallCreakClip` | Script unused; fields unassigned because no component instance | UNUSED |
| Meta XR audio/acoustic settings in `Assets/Resources` | Framework settings only | Not evidence of clips or narration |

All standardized voice points are **MISSING**: welcome, safety warning, baseline, breathing, tutorial, task start, block transition, pause, ending, and post-session review.

## 15. Corridor Pressure and Collapse

The scene has static corridor shell/cage geometry only. `StressRoomController` contains an unused prototype that would move four walls toward the user over 120 seconds, open a cage after 10 seconds, activate an optional tiger after 15 seconds, and play two optional creaks. No instance exists, and `MainScene` has no tiger Animator/asset or AudioSource. Evidence: source and zero GUID reference.

| Pressure feature | Status / evidence |
|---|---|
| Moving walls/floor/ceiling | MISSING runtime; unused wall-only prototype code |
| Cage-door rotation | MISSING runtime; static `Door_Pivot`; unused prototype method |
| Physics collapse | MISSING; no Rigidbody/physics system on corridor |
| Particles/cracks | MISSING; 0 ParticleSystems, no crack assets |
| Audio/light ramping | MISSING; static lights, 0 AudioSources |
| Timer linkage | MISSING |
| Multiple pressure levels | MISSING |
| Neutral mode | MISSING |
| Reset/game-over | MISSING; `Deactivate()` stops callbacks but does not restore moved walls/door |

Quest performance of an actual collapse is **UNKNOWN** because it does not exist. Current static blockout is modest (21 MeshRenderers), but the scene carries 35 SkinnedMeshRenderers through controller model hierarchies, three realtime-shadow lights, URP render scale 1.6, 2048 main shadow map, and 50m shadow distance; these are concrete profiling risks for Quest 3. Evidence: scene component counts and `URP_Balanced.asset`.

## 16. Data Logging

Unity gameplay data logging is **MISSING**. No JSON/CSV/SQLite/file writer, event schema, session ID, user ID, task result, HR sample file, questionnaire record, scheduler decision, or reason log exists. Evidence: code search and absence of data classes.

Python keeps only current HR plus up to 200 integers in RAM and prints readings to stdout. Flask `/api/hr` returns current HR and last `HH:mm:ss`. `start.bat` redirects Python stdout/stderr to a hard-coded `bridge.log` path that does not exist on this machine. These are development diagnostics, not reconstructable session logging.

## 17. Build Health and Quest Readiness

### Confirmed

- Exact Unity `6000.3.16f1` is installed and was used to open the project earlier in this task; no alternate version was used.
- `Assembly-CSharp.dll` and `Assembly-CSharp-Editor.dll` are dated 2026-06-02, later than the latest project script timestamp, proving a successful historical compile of those source versions. This does **not** prove the current July Editor console is error-free.
- Prior APK artifacts exist as described in Section 2.
- Android min 32, target 34, ARM64 serialized architecture, build-profile IL2CPP, OpenXR loader, Quest manifest/device declarations, and Quest permissions are present.
- `McpUnitySettings.json` uses port 8091, timeout 60, auto-start true, remote false. MCP is development tooling and not app functionality.

### Unknown/not executed

Current compile errors and the current Editor log are **UNKNOWN** because sandboxed reads of `C:\Users\djuro\AppData\Local\Unity\Editor\Editor.log` were denied. A compile/batch validation was not launched: Unity had been opened interactively, and an additional batch process could alter generated project state or conflict with the open Editor; the audit rules prohibit risky mutations. No current Build And Run was executed.

Missing Script and package GUID integrity are **UNKNOWN** at complete package scope. Static inspection found no missing project-script GUID and resolved the inspected Meta components (OVRManager/OVRCameraRig/Passthrough, Poke, Teleport, surfaces, visuals) to installed packages.

### Quest risks supported by settings

- URP render scale is 1.6 in the working tree, changed from committed 1.0; this increases pixel workload. Evidence: Git diff.
- Main-light shadows: enabled, 2048 map, 50m distance; all three scene Lights serialize shadows. Evidence: URP asset and scene.
- 35 SkinnedMeshRenderers embedded across many controller-model variants may add hierarchy/update cost; runtime activation behavior requires profiling. Evidence: scene YAML counts/hierarchy.
- Console uses a non-convex MeshCollider. It is static now, but cannot be used as a dynamic Rigidbody collider and may be more expensive than primitive/convex interaction colliders. Evidence: `MeshCollider.m_Convex: 0`.
- No large textures/models were found: largest FBX is 108,908 bytes; four project PNGs are 1.7–2.3KB. No uncompressed audio exists. Evidence: asset size inventory.
- Manifest has no explicit `android.permission.INTERNET`; `ForceInternetPermission: 0`. ADB is external and UDP socket use may cause Unity/Android to add networking permission depending on build processing; final manifest result is **UNKNOWN** without inspecting built APK manifest.
- OpenXR Unity HandTracking is off and Meta Quest Touch Plus profile is off while scene contains hand/controller building blocks. Effective device interaction requires Quest verification.

## 18. Asset Licensing

No repository-level or `Assets` license/README/NOTICE documents identify the provenance or redistribution rights of `ConsoleBody_Crescent_UnityFit.fbx`, `RobotArm_Placeholder.fbx`, `Tree.prefab`, their materials, or tree textures. Evidence: targeted file search returned no license/readme outside package caches. Git history records commits by Mihailo Đurović but does not establish creation provenance or license.

Installed packages carry their own package-cache licenses, but package-cache files are generated and were not copied into this report. The Git dependencies (`Coplay`, `mcp-unity`) and Meta/Unity packages must be reviewed under their upstream licenses before redistribution.

High-risk repository content: 33 legacy files under `stari fajlovi` are tracked, including `mifitness_backup.ab`, `wearable_backup.ab`, `xiaomi_backup.ab`, authentication/cloud/BLE scripts, and captured logs. Those may contain personal/device/account data or proprietary app data and should not be distributed or submitted academically until reviewed/redacted. Evidence: `git ls-files 'stari fajlovi/**'`. The `.gitignore` protects `auth_key.txt` but does not untrack the three `.ab` backups.

## 19. Implemented / Partial / Missing Matrix

| Capability | Status | Concrete evidence |
|---|---|---|
| Quest/OpenXR project base | IMPLEMENTED | XR settings, Android manifest, prior APKs |
| XR rig | IMPLEMENTED/PARTIAL verification | OVR camera/controller/hand blocks in `MainScene`; runtime device behavior UNKNOWN |
| Corridor blockout | IMPLEMENTED | `Corridor_Blockout` scene hierarchy |
| SafeSpace | MISSING | no scene/root/UI |
| Console shell | PARTIAL | active FBX shell + MeshCollider; no controls |
| Tablet | MISSING | no model/UI/Canvas/code |
| Poke interaction sample | IMPLEMENTED sample / UNUSED gameplay | Meta Poke object, no project callback |
| Memory sequence | PARTIAL code / UNUSED | `PuzzleManager`, zero references |
| n-back | MISSING | no implementation |
| go/no-go | MISSING | no implementation |
| flanker | MISSING | no implementation |
| Timer | MISSING | no session/task timer |
| Neutral/stress modes | MISSING | no connected selector/controller |
| Collapse/pressure | PARTIAL unused code | `StressRoomController`, zero references |
| HR ADB/Python parser | PARTIAL/BROKEN launcher | developed bridge; hard-coded nonexistent paths |
| HR Unity receiver | PARTIAL/UNUSED | `HRReceiver.cs`, zero scene references |
| HR standalone Quest transport | BROKEN | loopback-only PC/Quest boundary |
| Profiles/save/load | MISSING | no data/persistence code |
| 48h scheduling | MISSING | no dates/scheduler |
| SSQ/NASA-TLX | MISSING | no models/UI |
| Pause/end session | MISSING | no input/menu/state model |
| Audio/voice | MISSING | no clips/sources/mixer/narration |
| Debrief | PARTIAL code / UNUSED | `DebriefingManager`, zero references |
| Data/reason logging | MISSING | no file/schema/logger |

## 20. Keep / Refactor / Replace Matrix

| Komponenta | Trenutno stanje | Zadržati | Refaktorisati | Zamijeniti | Razlog |
|---|---|:---:|:---:|:---:|---|
| Hodnik | Static blockout with colliders/lights | Yes | Yes | No | Useful dimensions/layout; needs modular pressure pieces and Quest lighting pass (`MainScene`, `SpatialBlockoutBuilder`) |
| SafeSpace | Missing | No | No | Create | No existing artifact |
| XR rig | OVR/Meta building blocks | Yes | Yes | No | Strong base; verify input/provider consistency and spawn/session ownership |
| Konzola | Blender shell + static MeshCollider | Yes | Yes | No | Reusable visual shell; controls/sockets/collision architecture absent |
| Tablet | Missing | No | No | Create | No existing display surface/UI |
| Interakcije | Generic poke + teleport samples | Yes as reference | Yes | Possibly | Package examples work as base but are not console gameplay |
| Sekvence | Unused Simon-style memory code | Maybe concept only | Yes | Likely | Does not match tablet stimulus/task metrics |
| Zadaci | Three target tasks missing | No | No | Create | No n-back/go-no-go/flanker implementation |
| Timer | Missing | No | No | Create | Required shared session clock |
| Urušavanje | Unused four-wall prototype | Keep only concept | Yes | Likely | No levels/pause/reset/neutral/performance model |
| Puls | Python parser + UDP receiver disconnected | Yes | Yes | Transport may need replacement | Valuable parsing; paths, persistence, standalone networking broken |
| Profili | Missing | No | No | Create | No data model |
| Save/load | Missing | No | No | Create | No persistence |
| Pause | Missing | No | No | Create | Must coordinate clock/task/pressure/HR |
| Audio | Missing | No | No | Create | No assets/sources |
| UI | Missing except interaction visuals | Keep package visuals | Yes | Create app UI | No Canvas/tablet/forms |
| Scheduler | Missing | No | No | Create | No rules/history/reason log |
| Robot arm | Model + temp pose tester | Yes model | Yes | Tester replace | Model reusable; code explicitly temporary |

## 21. Main Risks

Ordered by severity (maximum 15):

1. **No connected application loop.** Evidence: only `RobotArmPoseTester` is serialized among project scripts. Consequence: no training session can run. Blocks MVP: **yes**. Deferrable: **no**.
2. **Target tasks absent.** Evidence: no n-back/go-no-go/flanker classes/assets. Consequence: thesis core cannot be demonstrated/measured. Blocks MVP: **yes**. Deferrable: **no**.
3. **Profiles/persistence/scheduler absent.** Evidence: no data classes/save APIs. Consequence: multi-session/adaptive claim unsupported. Blocks MVP: **yes**. Deferrable: **no**.
4. **No SafeSpace/tablet/questionnaires.** Evidence: one scene, zero Canvas, no tablet/SSQ/TLX. Consequence: required preparation, safety, instruction and post-session flow absent. Blocks MVP: **yes**. Deferrable: **no** for minimum safe study flow.
5. **Standalone HR transport cannot work as coded.** Evidence: both Python destination and Unity bind use `127.0.0.1:5005`. Consequence: PC bridge cannot reach Quest process. Blocks HR-enabled standalone MVP: **yes**. Deferrable: only for Link-only prototype.
6. **HR launcher is machine-broken.** Evidence: nonexistent `C:\Users\Korisnik\Desktop\Diplomski` paths. Consequence: bridge does not start from current checkout. Blocks repeatable HR demo: **yes**. Deferrable: **no** once HR phase starts.
7. **No pause/termination/safety state.** Evidence: no source/UI/timeScale/state. Consequence: user cannot safely stop or invalidate a session; task/pressure cannot coordinate. Blocks study-ready MVP: **yes**. Deferrable: **no**.
8. **No event-level logging.** Evidence: Python RAM list only; no Unity logger. Consequence: scheduler decisions and thesis evaluation cannot be reconstructed. Blocks adaptive/evaluation MVP: **yes**. Deferrable: **no**.
9. **Current documentation is stale/inconsistent.** Evidence: `CLAUDE.md` claims GameManager/HR scene objects; scene GUID audit disproves it. Consequence: agents may implement against false assumptions. Blocks MVP: **no**, but creates high rework risk. Deferrable: **no** for team coordination.
10. **Uncommitted scene/render settings are already present.** Evidence: initial Git status/diff. Consequence: concurrent agents can overwrite `MainScene` or misattribute changes. Blocks MVP: **no**. Deferrable: **no** before multi-agent edits.
11. **Sensitive/unclear legacy data is tracked.** Evidence: three `.ab` backups and auth/cloud scripts under `stari fajlovi`. Consequence: privacy/security/academic distribution risk. Blocks MVP: **no**; blocks clean distribution: **yes**. Deferrable: before sharing/submission.
12. **Asset provenance is undocumented.** Evidence: no asset license/README. Consequence: distribution/presentation uncertainty. Blocks MVP: **no**; deferrable until pre-submission, but should be resolved early.
13. **Quest render configuration may be expensive.** Evidence: render scale 1.6, 2048/50m main shadows, 35 skinned renderers, three shadow lights. Consequence: frame-time/comfort risk. Blocks MVP: only if profiling fails. Deferrable: until first integrated vertical slice.
14. **Input configuration is internally inconsistent/unclear.** Evidence: base `activeInputHandler:1`, build profile snapshot `2`; OpenXR HandTracking off, Meta hand setting 1. Consequence: controller/hand behavior may differ Editor vs Quest. Blocks interaction MVP if reproduced. Deferrable: **no** after interaction slice.
15. **Manifest/lock URP version drift.** Evidence: manifest `17.0.3`, lock `17.3.0`. Consequence: restore/reproducibility risk across machines. Blocks MVP: currently no. Deferrable: until environment freeze, without auto-upgrading.

## 22. Recommended Development Order

No code changes are proposed in this audit; these are phase gates based on actual dependencies.

### Phase 1 — Repository and runtime baseline

- Goal: establish one clean, reproducible working tree and verified current compile/Quest-Link scene.
- Reuse: `MainScene`, XR rig, current packages/settings.
- Missing: ownership rules for two agents; current Console log/build evidence.
- Dependencies: preserve exact Unity 6000.3.16f1 and package lock.
- Done when: Git baseline is intentional, Editor console is clean, rig/controller or hand path is explicitly chosen, and a screenshot/log proves `MainScene` in Play Mode.

### Phase 2 — Session shell and SafeSpace

- Goal: create deterministic app states (profile/select/prep/baseline/task/pause/post-session) before task logic.
- Reuse: one-scene strategy if desired, OVR rig, corridor.
- Missing: state machine, SafeSpace, tablet/display architecture, pause/exit safety.
- Dependencies: Phase 1 input decision.
- Done when: one test profile can enter preparation, pause/resume/end, transition to corridor, and return without real tasks.

### Phase 3 — Data contract and persistence

- Goal: define user/session/trial/HR/questionnaire/scheduler records and atomic per-user storage.
- Reuse: none beyond HR packet shape.
- Missing: all models/save/load/corruption/version handling/48h interval/reason log.
- Dependencies: session state names from Phase 2.
- Done when: restart restores multiple profiles and last session; corrupted record handling is demonstrated; every state transition has a reason/timestamp.

### Phase 4 — Console input and tablet output vertical slice

- Goal: one console control drives one tablet-presented stimulus with feedback and event logging.
- Reuse: console FBX, Meta Poke sample, interaction package.
- Missing: modular sockets/controls, tablet World Space UI, app callback/haptic/audio feedback.
- Dependencies: event schema from Phase 3.
- Done when: correct, wrong and missed responses produce logged timestamps while tablet remains display-only.

### Phase 5 — Three tasks

- Goal: implement n-back, go/no-go, flanker under a common task interface.
- Reuse: Phase 4 control/display/log pipeline; old `PuzzleManager` only as a behavioral reference.
- Missing: trial generators, deterministic seeds, block configs, scoring, reaction time, local difficulty.
- Dependencies: Phase 4.
- Done when: each task can replay a seeded block and produce complete trial results; difficulty changes only between sessions/blocks as specified.

### Phase 6 — HR transport and baseline

- Goal: make HR repeatable in Link and standalone topology, with quality/stale/reconnect/session timestamps.
- Reuse: Xiaomi regex/dedupe logic and `HRReceiver` concept.
- Missing: portable paths/config, Quest-reachable transport, reconnect, persistent samples, baseline state.
- Dependencies: Phase 3 logging and Phase 2 baseline flow.
- Done when: synthetic and real HR streams correlate with session/task events on target mode, survive disconnect/reconnect, and mark paused samples.

### Phase 7 — Neutral and pressure conditions

- Goal: same task works in neutral and controlled-pressure modes.
- Reuse: corridor/cage geometry, `StressRoomController` movement concept.
- Missing: pressure controller, levels, audio/light/collapse cues, pause/reset, performance-safe implementation.
- Dependencies: shared clock/pause from Phase 2 and task system from Phase 5.
- Done when: neutral and pressure runs share identical seeded task data; pause freezes task/clock/pressure; reset restores geometry; Quest profiling meets comfort target.

### Phase 8 — Questionnaires and explainable scheduler

- Goal: SSQ/NASA-TLX/post-session flow and rules that select next local/global levels with reason logs.
- Reuse: `DebriefingManager` max/avg/graph concept only.
- Missing: forms, scoring, safety validity, history aggregation, transparent rules.
- Dependencies: complete task, HR, and session schemas.
- Done when: predefined fixture sessions yield deterministic, human-readable scheduler decisions and next-session date.

### Phase 9 — Audio, usability, evaluation readiness

- Goal: standardized voice/ambient cues, accessibility, license/privacy cleanup, Quest build evidence.
- Reuse: scene layout and Meta audio settings.
- Missing: all audio assets/narration/subtitles, consent/safety polish, asset provenance.
- Dependencies: stable state/task flow.
- Done when: full scripted session can be completed/aborted on Quest 3, logs are exportable and explainable, assets are licensed, and no sensitive legacy files ship.

## 23. Unknowns and Questions

1. **UNKNOWN:** current Unity Console errors/warnings; Editor log read was denied. Provide Console screenshot filtered to Errors/Warnings.
2. **UNKNOWN:** exact Game View/Quest appearance and whether the player starts inside geometry; provide Game View and headset screenshots from initial spawn.
3. **UNKNOWN:** whether poke, ray, teleport, controller tracking and hand tracking all work in the currently selected runtime; provide a short Play Mode observation or video.
4. **UNKNOWN:** whether `Meta Quest.asset` is the active build profile; static file presence does not prove active selection.
5. **UNKNOWN:** effective input-handling enum due base/profile conflict.
6. **UNKNOWN:** built APK final merged permissions/manifest; inspect APK manifest if networking is evaluated.
7. **UNKNOWN:** provenance/license of console, robot arm, tree textures, and materials.
8. **UNKNOWN:** whether the tracked `.ab` archives contain personal/device credentials; they were not opened.
9. Which interaction target is authoritative: hand tracking, controllers, or both?
10. Is the final demonstration required to run standalone with live HR, or is Quest Link acceptable for the HR-enabled demo?
11. Should the existing tiger/cage concept be retained, or should pressure be exclusively corridor collapse as described in the thesis?
12. Are SSQ and NASA-TLX to be completed inside VR on the tablet, in SafeSpace, or externally?

## 24. Exact Files ChatGPT Should Inspect Next

Priority order:

1. `Assets/Scenes/MainScene.unity` — current serialized world and only build scene.
2. `Assets/Scripts/HR/HRReceiver.cs` and root `hr_dashboard_v2.py` — transport boundary and data shape.
3. Root `start.bat` — broken machine-specific launcher.
4. `Assets/Scripts/PuzzleManager.cs` and `PuzzleButton.cs` — obsolete sequence/task assumptions.
5. `Assets/Scripts/StressRoomController.cs` — obsolete threat/pause/reset assumptions.
6. `Assets/Scripts/GameManager.cs` — unused phase concept.
7. `Assets/Scripts/DebriefingManager.cs` and `HRDisplay.cs` — prototype UI/data limitations.
8. `Assets/Models/Console/ConsoleBody_Crescent_UnityFit.fbx` plus its `.meta` — console shell/import/material/collider plan.
9. `Assets/Models/RobotArm/RobotArm_Placeholder.fbx`, `.meta`, and `RobotArmPoseTester.cs` — model/pivot constraints.
10. `ProjectSettings/EditorBuildSettings.asset`, `ProjectSettings/ProjectSettings.asset`, `ProjectSettings/QualitySettings.asset`, `ProjectSettings/GraphicsSettings.asset`.
11. `Assets/XR/XRGeneralSettingsPerBuildTarget.asset`, `Assets/XR/Settings/OpenXR Package Settings.asset`, `Assets/Oculus/OculusProjectConfig.asset`.
12. `Packages/manifest.json` and `Packages/packages-lock.json` — especially URP version discrepancy.
13. Root `CLAUDE.md` — treat as historical intent only; reconcile stale scene claims.
14. Root `.gitignore` and tracked `stari fajlovi` list — privacy/license cleanup planning.

## 25. Git Status Before and After Audit

Before audit:

```text
 M VR_StressTraining/Assets/Scenes/MainScene.unity
 M VR_StressTraining/Assets/Settings/URP_Balanced.asset
?? "High-Credibility Literature Shortlist for Adaptive Stress Corridor VR.docx"
?? Prva_iteracija_diplomskog_rada_Mihailo_Djurovic.docx
?? VR_StressTraining/.codex/
```

After-audit status is recorded after this report is written. The expected only audit-created delta is:

```text
?? VR_StressTraining/PROJECT_AUDIT_FOR_CHATGPT.md
```

Final `git status --short` after audit:

```text
 M VR_StressTraining/Assets/Scenes/MainScene.unity
 M VR_StressTraining/Assets/Settings/URP_Balanced.asset
?? "High-Credibility Literature Shortlist for Adaptive Stress Corridor VR.docx"
?? Prva_iteracija_diplomskog_rada_Mihailo_Djurovic.docx
?? VR_StressTraining/.codex/
?? VR_StressTraining/PROJECT_AUDIT_FOR_CHATGPT.md
```

Final `git diff --name-only` still lists only the two pre-existing tracked-file changes:

```text
VR_StressTraining/Assets/Scenes/MainScene.unity
VR_StressTraining/Assets/Settings/URP_Balanced.asset
```

**Verification:** the audit did not modify any existing project file. The only file created by the audit is `VR_StressTraining/PROJECT_AUDIT_FOR_CHATGPT.md`.
