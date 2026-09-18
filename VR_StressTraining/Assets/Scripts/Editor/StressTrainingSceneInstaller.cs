using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StressTraining.Core;
using StressTraining.Console;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StressTraining.Editor
{
    public static class StressTrainingSceneInstaller
    {
        private const string MenuPath = "Stress Training/Install Vertical Slice %#i";
        private const string FeedbackManagerPrefabPath =
            "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/Feedback/FeedbackManager.prefab";

        [MenuItem(MenuPath)]
        public static void InstallVerticalSlice()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                EditorUtility.DisplayDialog("Stress Training", "Otvori MainScene prije instalacije.", "OK");
                return;
            }

            var added = new List<string>();
            var linked = new List<string>();
            var missing = new List<string>();
            string backup = BackupScene(scene.path);
            bool changed = false;

            GameObject appRoot = FindOrCreateRoot(scene, "AppRoot", added, ref changed);
            GameObject systemsRoot = FindOrCreateRoot(scene, "SystemsRoot", added, ref changed);
            GameObject uiRoot = FindOrCreateRoot(scene, "RuntimeUIRoot", added, ref changed);
            FindOrCreateRoot(scene, "DebugRoot", added, ref changed);

            EnsureMetaFeedbackManager(scene, systemsRoot.transform, added, missing, ref changed);

            var bootstrap = appRoot.GetComponent<AppBootstrapper>();
            if (bootstrap == null)
            {
                bootstrap = Undo.AddComponent<AppBootstrapper>(appRoot);
                added.Add("AppRoot/AppBootstrapper");
                changed = true;
            }

            GameObject corridor = FindInScene(scene, "Corridor_Blockout");
            if (corridor == null) missing.Add("Corridor_Blockout");
            else linked.Add("Corridor_Blockout");

            Transform rig = FindRig(scene);
            if (rig == null) missing.Add("OVRCameraRig/XR rig root");
            else linked.Add("XR rig: " + rig.name);

            Transform corridorSpawn = null;
            if (corridor != null)
            {
                corridorSpawn = corridor.transform.Find("CorridorSpawn");
                if (corridorSpawn == null)
                {
                    var go = new GameObject("CorridorSpawn");
                    Undo.RegisterCreatedObjectUndo(go, "Create CorridorSpawn");
                    corridorSpawn = go.transform;
                    corridorSpawn.SetParent(corridor.transform, false);
                    corridorSpawn.position = new Vector3(0f, 0f, -3.2f);
                    corridorSpawn.rotation = Quaternion.Euler(0f, 180f, 0f);
                    added.Add("Corridor_Blockout/CorridorSpawn");
                    changed = true;
                }
            }

            GameObject consoleShell = FindInScene(scene, "Console_BlenderPrototype") ??
                                      FindInScene(scene, "Console_Placeholder");
            if (consoleShell == null)
            {
                missing.Add("Console_BlenderPrototype");
            }
            else
            {
                var defaults = new ConsoleLayoutConfig();
                Transform anchor = consoleShell.transform.Find(ConsoleLayoutBuilder.AnchorName);
                if (anchor == null)
                {
                    var anchorObject = new GameObject(ConsoleLayoutBuilder.AnchorName);
                    Undo.RegisterCreatedObjectUndo(anchorObject, "Create ConsoleControlsAnchor");
                    anchor = anchorObject.transform;
                    anchor.SetParent(consoleShell.transform, false);
                    added.Add(consoleShell.name + "/" + ConsoleLayoutBuilder.AnchorName);
                    changed = true;
                }
                if (anchor.localPosition != defaults.anchorLocalPosition ||
                    anchor.localEulerAngles != defaults.anchorLocalEuler)
                {
                    Undo.RecordObject(anchor, "Align ConsoleControlsAnchor");
                    anchor.localPosition = defaults.anchorLocalPosition;
                    anchor.localRotation = Quaternion.Euler(defaults.anchorLocalEuler);
                    anchor.localScale = Vector3.one;
                    changed = true;
                }
                if (anchor.GetComponent<ConsoleControlsAnchorGizmo>() == null)
                {
                    Undo.AddComponent<ConsoleControlsAnchorGizmo>(anchor.gameObject);
                    changed = true;
                }
                linked.Add("Console controls anchor -> " + consoleShell.name);
            }

            var serialized = new SerializedObject(bootstrap);
            changed |= SetReference(serialized, "xrRigRoot", rig, linked);
            changed |= SetReference(serialized, "corridorRoot", corridor, linked);
            changed |= SetReference(serialized, "corridorSpawn", corridorSpawn, linked);
            changed |= SetReference(serialized, "systemsRoot", systemsRoot.transform, linked);
            changed |= SetReference(serialized, "runtimeUiRoot", uiRoot.transform, linked);
            serialized.ApplyModifiedProperties();

            if (changed)
            {
                EditorUtility.SetDirty(bootstrap);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    missing.Add("Scene save failed; inspect Editor log.");
            }

            WriteReport(scene.path, backup, changed, added, linked, missing);
            AssetDatabase.Refresh();
            Debug.Log($"[StressTrainingSceneInstaller] Complete. changed={changed}, backup={backup}");
        }

        private static bool SetReference(SerializedObject serialized, string propertyName,
            UnityEngine.Object value, List<string> linked)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null) return false;
            if (property.objectReferenceValue == value) return false;
            property.objectReferenceValue = value;
            linked.Add(propertyName + " -> " + (value != null ? value.name : "NULL"));
            return true;
        }

        private static void EnsureMetaFeedbackManager(Scene scene, Transform parent,
            List<string> added, List<string> missing, ref bool changed)
        {
            if (FindInScene(scene, "FeedbackManager") != null) return;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FeedbackManagerPrefabPath);
            if (prefab == null)
            {
                missing.Add("Meta FeedbackManager prefab: " + FeedbackManagerPrefabPath);
                return;
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (instance == null)
            {
                missing.Add("Meta FeedbackManager could not be instantiated.");
                return;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Add Meta FeedbackManager");
            instance.name = "FeedbackManager";
            added.Add("SystemsRoot/FeedbackManager (Meta Interaction SDK haptics)");
            changed = true;
        }

        private static GameObject FindOrCreateRoot(Scene scene, string name,
            List<string> added, ref bool changed)
        {
            var existing = scene.GetRootGameObjects().FirstOrDefault(go => go.name == name);
            if (existing != null) return existing;
            var created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            SceneManager.MoveGameObjectToScene(created, scene);
            added.Add(name);
            changed = true;
            return created;
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t => t.name == name);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        private static Transform FindRig(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var rig = root.GetComponentsInChildren<MonoBehaviour>(true)
                    .FirstOrDefault(component => component != null && component.GetType().Name == "OVRCameraRig");
                if (rig != null) return rig.transform;
            }
            return null;
        }

        private static string BackupScene(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string repoRoot = Directory.GetParent(projectRoot).FullName;
            string backupDir = Path.Combine(repoRoot, "_implementation_backup");
            Directory.CreateDirectory(backupDir);
            string backup = Path.Combine(backupDir,
                "CODEX_VERTICAL_SLICE_MainScene_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".unity.bak");
            File.Copy(Path.Combine(projectRoot, assetPath), backup, true);
            return backup;
        }

        private static void WriteReport(string scenePath, string backup, bool changed,
            List<string> added, List<string> linked, List<string> missing)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string report = Path.Combine(projectRoot, "SCENE_INSTALLATION_REPORT.md");
            string Lines(IEnumerable<string> values) => values.Any()
                ? string.Join("\n", values.Distinct().Select(v => "- " + v)) : "- None";
            File.WriteAllText(report,
                "# Scene Installation Report\n\n" +
                "- UTC: " + DateTime.UtcNow.ToString("O") + "\n" +
                "- Scene: `" + scenePath + "`\n" +
                "- Backup: `" + backup + "`\n" +
                "- Scene changed: **" + changed + "**\n\n" +
                "## Objects Added\n\n" + Lines(added) + "\n\n" +
                "## References Linked\n\n" + Lines(linked) + "\n\n" +
                "## Missing or Manual Follow-up\n\n" + Lines(missing) + "\n\n" +
                "The installer is idempotent and does not delete or reparent the corridor, console, robot arm, or modify URP assets.\n");
        }
    }
}
