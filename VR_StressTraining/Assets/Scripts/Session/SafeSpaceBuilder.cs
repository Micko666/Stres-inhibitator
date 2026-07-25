using System;
using StressTraining.Core;
using StressTraining.UI;
using UnityEngine;

namespace StressTraining.Session
{
    /// <summary>
    /// Stable, spawn-relative placement for every SafeSpace menu panel. The eye
    /// height is deliberately measured from the spawn floor, not from the HMD
    /// pose, because the center-eye anchor can still report y=0 during startup.
    /// </summary>
    [Serializable]
    public sealed class SafeSpaceUiLayoutConfig
    {
        [Min(0.35f)] public float distanceMeters = 1.7f;
        [Min(0.8f)] public float eyeHeightMeters = 1.65f;
    }

    /// <summary>
    /// Procedural SafeSpace (spec §7): a calm beach-inspired platform far away
    /// from the corridor. Unity primitives + URP materials only — no external
    /// assets. Stable horizon, no camera motion, no fast particles, one slow
    /// distant element (the sun disc drifting), a UI anchor and spawn point.
    /// Idempotent: returns the existing root when already built.
    /// </summary>
    public static class SafeSpaceBuilder
    {
        public const string RootName = "SafeSpaceRoot";
        public static readonly Vector3 Origin = new Vector3(200f, 0f, 0f); // far from corridor

        public const string SpawnName = "SafeSpaceSpawn";
        public const string UiAnchorName = "SafeSpaceUIAnchor";
        /// <summary>Invisible thick floor box; the visual disc carries no collider.</summary>
        public const string PlatformColliderName = "PlatformCollider";

        // Equirectangular Safe Space panorama, loaded at runtime if it has been
        // imported. The source art (AVIF/EXR HDRI) cannot be used directly — Unity
        // does not read AVIF, and a 4K HDR EXR is far too heavy for a Quest backdrop
        // — so it is converted once to a 2048x1024 LDR PNG placed here
        // (see SAFE_SPACE_AURORA_SETUP.md). Missing ⇒ gradient dome fallback.
        public const string PanoramaResourcePath = "SafeSpace/SafeSpacePanorama";
        public const string PanoramaName = "SafeSpacePanorama";

        /// <summary>True after the last Build found and applied the panorama texture.</summary>
        public static bool PanoramaApplied { get; private set; }

        public static Transform Build(Transform parent = null, SafeSpaceUiLayoutConfig uiLayout = null)
        {
            uiLayout ??= new SafeSpaceUiLayoutConfig();
            var existing = GameObject.Find(RootName);
if (existing != null)
{
    // Ukloni prethodno nebo kako plavi SkyDome ne bi zaklanjao panoramu.
    var oldSky = existing.transform.Find("SkyDome");
    if (oldSky != null)
        UnityEngine.Object.Destroy(oldSky.gameObject);

    var oldAurora = existing.transform.Find(PanoramaName);
    if (oldAurora != null)
        UnityEngine.Object.Destroy(oldAurora.gameObject);

    // Ponovo učitaj panoramu čak i kada SafeSpaceRoot već postoji.
    bool existingPanorama = TryBuildPanoramaDome(existing.transform);
if (!existingPanorama)
    {
        // CullFront + positive scale — see TryBuildPanoramaDome.
        var skyMat = RuntimeVisualUtil.CullFront(
            RuntimeVisualUtil.Unlit(new Color(0.36f, 0.48f, 0.62f)));
        RuntimeVisualUtil.Primitive(
            PrimitiveType.Sphere,
            "SkyDome",
            existing.transform,
            Vector3.zero,
            new Vector3(380f, 380f, 380f),
            skyMat);
    }

    var existingSun = existing.transform.Find("Sun");
    if (existingSun != null)
        existingSun.gameObject.SetActive(!existingPanorama);

    ConfigureUiAnchor(existing.transform, uiLayout);
    return existing.transform;
}

            var root = new GameObject(RootName).transform;
            if (parent != null) root.SetParent(parent, false);
            root.position = Origin;

            // Platform — warm sand-colored disc (visual only)
            var sand = RuntimeVisualUtil.Lit(new Color(0.82f, 0.72f, 0.55f), 0f, 0.15f);
            RuntimeVisualUtil.Primitive(PrimitiveType.Cylinder, "Platform", root,
                new Vector3(0, -0.05f, 0), new Vector3(9f, 0.05f, 9f), sand);

            // Physics floor kept SEPARATE from the visual disc. The disc is only 0.1 m
            // thick, and a thin non-convex MeshCollider is not a reliable floor: a
            // capsule falling under gravity tunnels straight through it. A thick box
            // whose TOP sits exactly at y = 0 cannot be tunnelled and is cheaper.
            var floorCollider = new GameObject(PlatformColliderName);
            floorCollider.transform.SetParent(root, false);
            floorCollider.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            var box = floorCollider.AddComponent<BoxCollider>();
            box.size = new Vector3(9f, 1f, 9f);   // top face at y = 0, 1 m deep

            // The sea plane was removed: it hid the lower half of the panorama.

            // Sky — inward-facing sphere. When the panorama is imported it wraps
            // the whole Safe Space; otherwise the calm dusk gradient stays as-is.
            bool panorama = TryBuildPanoramaDome(root);
            if (!panorama)
            {
                // CullFront + positive scale — see TryBuildPanoramaDome.
                var skyMat = RuntimeVisualUtil.CullFront(
                    RuntimeVisualUtil.Unlit(new Color(0.36f, 0.48f, 0.62f)));
                RuntimeVisualUtil.Primitive(PrimitiveType.Sphere, "SkyDome", root,
                    new Vector3(0, 0, 0), new Vector3(380f, 380f, 380f), skyMat);
            }

            // Distant sun — slow drifting warm disc (the single slow-moving element).
            // Hidden under the panorama, which already provides a sky.
            var sunMat = RuntimeVisualUtil.Unlit(new Color(0.98f, 0.83f, 0.55f));
            var sun = RuntimeVisualUtil.Primitive(PrimitiveType.Sphere, "Sun", root,
                new Vector3(-60f, 22f, 140f), Vector3.one * 9f, sunMat);
            if (panorama) sun.SetActive(false);

            // Local soft light so the platform reads even in a dark scene. Under the
            // panorama it is a neutral daylight tint to match the photographic sky.
            var lightGo = new GameObject("SafeSpaceLight");
            lightGo.transform.SetParent(root, false);
            lightGo.transform.localPosition = new Vector3(0, 6f, 2f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 25f;
            light.intensity = panorama ? 1.05f : 1.2f;
            light.color = panorama ? new Color(0.92f, 0.94f, 1f) : new Color(1f, 0.93f, 0.82f);
            light.shadows = LightShadows.None; // Quest perf

            // Spawn point + UI anchor
            var spawn = new GameObject(SpawnName).transform;
            spawn.SetParent(root, false);
            spawn.localPosition = Vector3.zero;
            spawn.localRotation = Quaternion.identity;

            var uiAnchor = new GameObject(UiAnchorName).transform;
            uiAnchor.SetParent(root, false);
            ConfigureUiAnchor(root, uiLayout);

            var animator = root.gameObject.AddComponent<SafeSpaceAnimator>();
            animator.Initialize(null, sun.transform);

            return root;
        }

        /// <summary>
        /// Builds the Safe Space panorama sphere when the converted texture is present in
        /// Resources. A large inward-facing sphere with an unlit equirectangular
        /// material wraps the Safe Space with no camera parallax. Returns false when
        /// the texture is missing so the caller keeps the gradient dome.
        /// </summary>
        private static bool TryBuildPanoramaDome(Transform root)
        {
            PanoramaApplied = false;
            var texture = Resources.Load<Texture2D>(PanoramaResourcePath);
            if (texture == null)
            {
                Debug.Log("[SafeSpaceBuilder] Panorama not found at Resources/" +
                          PanoramaResourcePath + " — using the calm gradient dome. " +
                          "Convert the AVIF to PNG and import it (SAFE_SPACE_AURORA_SETUP.md).");
                return false;
            }

            // White tint so the panorama shows unmodified; unlit so it reads as sky.
            // CullFront (not a negative scale) makes it visible from INSIDE: the
            // negative-scale winding flip does not work here — the sphere could only
            // be seen from outside, so from within the Safe Space it was pure black.
            // Positive scale also keeps the panorama from being mirrored.
            var mat = RuntimeVisualUtil.CullFront(
                RuntimeVisualUtil.UnlitTextured(texture, Color.white));
            RuntimeVisualUtil.Primitive(PrimitiveType.Sphere, PanoramaName, root,
                Vector3.zero, new Vector3(400f, 400f, 400f), mat);
            PanoramaApplied = true;
            Debug.Log("[SafeSpaceBuilder] Panorama applied (" +
                      texture.width + "x" + texture.height + ").");
            return true;
        }

        /// <summary>Applies the serialized layout without consulting the live HMD pose.</summary>
        public static void ConfigureUiAnchor(Transform safeSpaceRoot, SafeSpaceUiLayoutConfig layout)
        {
            if (safeSpaceRoot == null) return;
            layout ??= new SafeSpaceUiLayoutConfig();
            Transform spawn = safeSpaceRoot.Find(SpawnName);
            Transform anchor = safeSpaceRoot.Find(UiAnchorName);
            if (spawn == null || anchor == null) return;

            WorldSpaceUiOrientation.PlaceFromSpawn(anchor, spawn,
                layout.distanceMeters, layout.eyeHeightMeters);
            if (anchor.GetComponent<SafeSpaceUiAnchorGizmo>() == null)
                anchor.gameObject.AddComponent<SafeSpaceUiAnchorGizmo>();
        }
    }

    /// <summary>Editor-only visual guide for the stable SafeSpace menu plane.</summary>
    public sealed class SafeSpaceUiAnchorGizmo : MonoBehaviour
    {
#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.25f, 0.85f, 1f, 0.9f);
            // Main UI canvas is 700x560 px at 0.0012 metres per pixel.
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.84f, 0.672f, 0.01f));
            // The readable uGUI face is local -Z (toward the spawn/user).
            Gizmos.DrawLine(Vector3.zero, Vector3.back * 0.3f);
        }
#endif
    }

    /// <summary>
    /// The only motion in the SafeSpace: barely-visible water UV drift and a very
    /// slow sun drift. Purely decorative, framerate-independent, no physics.
    /// </summary>
    public sealed class SafeSpaceAnimator : MonoBehaviour
    {
        private Renderer _sea;
        private Transform _sun;
        private Vector3 _sunStart;
        private float _t;
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        public void Initialize(Renderer sea, Transform sun)
        {
            _sea = sea;
            _sun = sun;
            if (_sun != null) _sunStart = _sun.localPosition;
        }

        private void Update()
        {
            _t += Time.unscaledDeltaTime;
            if (_sea != null && _sea.material.HasProperty(BaseMapId))
                _sea.material.SetTextureOffset(BaseMapId, new Vector2(_t * 0.004f, _t * 0.002f));
            if (_sun != null)
                _sun.localPosition = _sunStart + new Vector3(Mathf.Sin(_t * 0.01f) * 6f, Mathf.Sin(_t * 0.006f) * 1.5f, 0);
        }
    }
}
