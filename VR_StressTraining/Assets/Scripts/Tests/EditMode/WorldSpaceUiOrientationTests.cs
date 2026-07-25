using NUnit.Framework;
using StressTraining.UI;
using UnityEngine;

namespace StressTraining.Tests.EditMode
{
    public sealed class WorldSpaceUiOrientationTests
    {
        [Test]
        public void CanvasReadableFace_IsUprightAndNotMirrored()
        {
            var viewer = new GameObject("viewer").transform;
            var anchor = new GameObject("anchor").transform;
            try
            {
                viewer.position = Vector3.zero;
                viewer.rotation = Quaternion.identity;
                WorldSpaceUiOrientation.PlaceForViewer(anchor, viewer, 1.5f);

                Assert.That(anchor.position.z, Is.EqualTo(1.5f).Within(0.001f));
                Assert.That(Vector3.Dot(anchor.up, Vector3.up), Is.GreaterThan(0.99f));
                Assert.That(WorldSpaceUiOrientation.IsReadableFrom(anchor, viewer), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(anchor.gameObject);
                Object.DestroyImmediate(viewer.gameObject);
            }
        }
    }
}
