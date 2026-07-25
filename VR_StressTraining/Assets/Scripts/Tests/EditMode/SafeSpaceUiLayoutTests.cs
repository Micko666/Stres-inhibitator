using NUnit.Framework;
using StressTraining.Session;
using StressTraining.UI;
using UnityEngine;

namespace StressTraining.Tests.EditMode
{
    public sealed class SafeSpaceUiLayoutTests
    {
        [Test]
        public void EarlyZeroHeightViewer_DoesNotOverwriteSpawnRelativeAnchor()
        {
            var host = new GameObject("zone-controller");
            var rig = new GameObject("rig");
            var safe = new GameObject("safe");
            var corridor = new GameObject("corridor");
            var safeSpawn = new GameObject("safe-spawn").transform;
            var safeAnchor = new GameObject("safe-ui-anchor").transform;
            var corridorSpawn = new GameObject("corridor-spawn").transform;
            var corridorAnchor = new GameObject("corridor-ui-anchor").transform;
            var viewer = new GameObject("early-center-eye").transform;
            var uiHost = new GameObject("ui-manager");

            safeSpawn.SetParent(safe.transform, false);
            safeAnchor.SetParent(safe.transform, false);
            corridorSpawn.SetParent(corridor.transform, false);
            corridorAnchor.SetParent(corridor.transform, false);

            try
            {
                safeSpawn.position = new Vector3(4f, 2f, -3f);
                safeSpawn.rotation = Quaternion.Euler(0f, 35f, 0f);
                viewer.position = Vector3.zero; // common before the first valid XR pose

                var layout = new SafeSpaceUiLayoutConfig
                {
                    distanceMeters = 1.9f,
                    eyeHeightMeters = 1.68f
                };
                var zones = host.AddComponent<SceneZoneController>();
                zones.Initialize(rig.transform, safe, corridor, safeSpawn, corridorSpawn,
                    safeAnchor, corridorAnchor, layout);

                Vector3 flatForward = Vector3.ProjectOnPlane(safeSpawn.forward, Vector3.up).normalized;
                Vector3 expected = safeSpawn.position + flatForward * layout.distanceMeters +
                                   Vector3.up * layout.eyeHeightMeters;
                Assert.That(Vector3.Distance(safeAnchor.position, expected), Is.LessThan(0.001f));
                Assert.That(safeAnchor.GetComponent<SafeSpaceUiAnchorGizmo>(), Is.Not.Null);

                Vector3 stablePosition = safeAnchor.position;
                Quaternion stableRotation = safeAnchor.rotation;
                var ui = uiHost.AddComponent<UIManager>();
                ui.Initialize(safeAnchor, viewer, 1.6f);
                viewer.position = new Vector3(0f, 1.85f, 0f);
                ui.SetAnchor(safeAnchor, 1.6f);

                Assert.That(Vector3.Distance(safeAnchor.position, stablePosition), Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(safeAnchor.rotation, stableRotation), Is.LessThan(0.01f));
                Assert.That(ui.MainCanvas.transform.parent, Is.SameAs(safeAnchor));
            }
            finally
            {
                Object.DestroyImmediate(uiHost);
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(rig);
                Object.DestroyImmediate(safe);
                Object.DestroyImmediate(corridor);
                Object.DestroyImmediate(viewer.gameObject);
            }
        }
    }
}
