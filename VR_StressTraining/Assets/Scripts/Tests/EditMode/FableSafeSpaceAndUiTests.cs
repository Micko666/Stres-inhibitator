#if UNITY_EDITOR && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using StressTraining.Session;
using StressTraining.UI;
using UnityEngine;

namespace StressTraining.Tests.EditMode
{
    /// <summary>Safe Space panorama scaffold and the centralized UI theme.</summary>
    public sealed class FableSafeSpaceAndUiTests
    {
        private GameObject _safeRoot;

        [TearDown]
        public void TearDown()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            if (_safeRoot != null) Object.DestroyImmediate(_safeRoot);
            var stray = GameObject.Find(SafeSpaceBuilder.RootName);
            if (stray != null) Object.DestroyImmediate(stray);
        }

        [Test]
        public void SafeSpace_IsNotAnEmptyBlackVoid()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            _safeRoot = SafeSpaceBuilder.Build().gameObject;

            // A sky (panorama when imported, gradient dome otherwise) and a lit
            // platform must exist so the Safe Space is never a plain black room.
            bool hasSky = _safeRoot.transform.Find("SkyDome") != null ||
                          _safeRoot.transform.Find(SafeSpaceBuilder.PanoramaName) != null;
            Assert.That(hasSky, Is.True, "Safe Space needs a sky/panorama, not a black void");
            Assert.That(_safeRoot.transform.Find("Platform"), Is.Not.Null);
            Assert.That(_safeRoot.GetComponentInChildren<Light>(), Is.Not.Null, "Safe Space is lit");
        }

        [Test]
        public void SafeSpace_UsesTheImportedPanorama()
        {
            // The converted LDR panorama now ships in Resources, so Build must pick it
            // up rather than fall back to the gradient dome.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            _safeRoot = SafeSpaceBuilder.Build().gameObject;

            Assert.That(SafeSpaceBuilder.PanoramaApplied, Is.True,
                "Resources/" + SafeSpaceBuilder.PanoramaResourcePath + " must be found and applied");
            Assert.That(_safeRoot.transform.Find(SafeSpaceBuilder.PanoramaName), Is.Not.Null);
        }

        [Test]
        public void Panorama_IsVisibleFromInside_NotFlippedByNegativeScale()
        {
            // Regression guard. The dome used to rely on a negative axis scale to flip
            // the winding; in this URP setup that does NOT work — the sphere rendered
            // only from the outside and the Safe Space read as pure black from within.
            // It also mirrored the panorama. The fix is an explicit _Cull = Front with
            // a normal positive scale.
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            _safeRoot = SafeSpaceBuilder.Build().gameObject;

            var dome = _safeRoot.transform.Find(SafeSpaceBuilder.PanoramaName);
            Assert.That(dome, Is.Not.Null);

            Vector3 scale = dome.localScale;
            Assert.That(scale.x, Is.GreaterThan(0f), "no negative scale — it mirrors the panorama");
            Assert.That(scale.y, Is.GreaterThan(0f));
            Assert.That(scale.z, Is.GreaterThan(0f));

            var mat = dome.GetComponent<Renderer>().sharedMaterial;
            Assert.That(mat.HasProperty("_Cull"), Is.True);
            Assert.That(mat.GetFloat("_Cull"),
                Is.EqualTo((float)UnityEngine.Rendering.CullMode.Front),
                "front faces must be culled so the sphere is seen from inside");
        }

        [Test]
        public void SafeSpace_HasStableSpawnAndUiAnchor()
        {
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            _safeRoot = SafeSpaceBuilder.Build().gameObject;
            Assert.That(_safeRoot.transform.Find(SafeSpaceBuilder.SpawnName), Is.Not.Null);
            Assert.That(_safeRoot.transform.Find(SafeSpaceBuilder.UiAnchorName), Is.Not.Null);
        }

        [Test]
        public void UiTheme_TokensAreDistinct_AndUiBuilderReadsThem()
        {
            // The design system must actually differentiate roles, and the legacy
            // UiBuilder aliases must resolve to the theme (spec §13).
            Assert.That(UiTheme.TextPrimary, Is.Not.EqualTo(UiTheme.TextSecondary));
            Assert.That(UiTheme.ButtonNormal, Is.Not.EqualTo(UiTheme.ButtonHighlighted));
            Assert.That(UiTheme.Accent, Is.Not.EqualTo(UiTheme.Danger));

            Assert.That(UiBuilder.PanelBg, Is.EqualTo(UiTheme.PanelBackground));
            Assert.That(UiBuilder.TextColor, Is.EqualTo(UiTheme.TextPrimary));
            Assert.That(UiBuilder.RowSelectedBg, Is.EqualTo(UiTheme.ButtonSelected));
        }

        [Test]
        public void UiTheme_DangerButton_UsesTheDangerFamily()
        {
            var normal = UiTheme.Button(danger: false);
            var danger = UiTheme.Button(danger: true);
            Assert.That(danger.normal, Is.Not.EqualTo(normal.normal),
                "the End-Session/danger button must look different from a normal one");
        }
    }
}
#endif
