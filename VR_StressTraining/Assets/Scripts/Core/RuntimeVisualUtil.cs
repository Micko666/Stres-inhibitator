using UnityEngine;

namespace StressTraining.Core
{
    /// <summary>
    /// Asset-free visual helpers. Everything the runtime UI/props need is built
    /// from Unity primitives + URP shaders so the project has zero external
    /// visual dependencies (spec §7 SafeSpace rules, §31 visual standard).
    /// </summary>
    public static class RuntimeVisualUtil
    {
        private static Shader _lit;
        private static Shader _unlit;
        private static Font _font;
        private static bool _renderPathLogged;

        public static Shader LitShader
        {
            get
            {
                if (_lit == null)
                {
                    _lit = RequireShader("Universal Render Pipeline/Lit");
                    LogRenderPathOnce();
                }
                return _lit;
            }
        }

        /// <summary>
        /// Required URP Unlit shader. It is explicitly retained in GraphicsSettings so
        /// Editor and standalone builds cannot silently use different materials.
        /// </summary>
        public static Shader UnlitShader
        {
            get
            {
                if (_unlit == null)
                {
                    _unlit = RequireShader("Universal Render Pipeline/Unlit");
                    LogRenderPathOnce();
                }
                return _unlit;
            }
        }

        private static Shader RequireShader(string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new System.InvalidOperationException(
                    "Required runtime shader is missing from this build: " + shaderName);
            return shader;
        }

        private static void LogRenderPathOnce()
        {
            if (_renderPathLogged) return;
            _renderPathLogged = true;
            Debug.Log("[RuntimeVisualUtil] graphicsApi=" + SystemInfo.graphicsDeviceType +
                      ", activeColorSpace=" + QualitySettings.activeColorSpace +
                      ", lit=" + (_lit != null ? _lit.name : "not-loaded") +
                      ", unlit=" + (_unlit != null ? _unlit.name : "not-loaded"));
        }

        public static Font BuiltinFont
        {
            get
            {
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Material Lit(Color color, float metallic = 0.1f, float smoothness = 0.4f)
        {
            var m = new Material(LitShader);
            SetColorCompat(m, color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        public static Material Unlit(Color color)
        {
            var m = new Material(UnlitShader);
            SetColorCompat(m, color);
            return m;
        }

        /// <summary>
        /// Unlit material carrying an equirectangular texture — used for the Safe
        /// Space aurora panorama sphere. White base tint so the panorama shows
        /// unmodified; no lighting so it reads as a distant sky, not a lit surface.
        /// </summary>
        public static Material UnlitTextured(Texture texture, Color tint)
        {
            var m = new Material(UnlitShader);
            SetColorCompat(m, tint);
            if (texture != null)
            {
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", texture);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", texture);
            }
            return m;
        }

        /// <summary>
        /// Renders a mesh from the INSIDE by culling front faces instead of back
        /// faces. This is how a skydome/panorama sphere must be built here.
        ///
        /// The usual trick — a negative axis scale to flip the winding — does NOT
        /// work reliably in this URP setup: the sphere stayed visible only from the
        /// outside and read as pure black from within. It also mirrors the texture
        /// horizontally. Setting _Cull explicitly is deterministic and keeps the
        /// panorama unmirrored, so the sphere can use a normal positive scale.
        /// </summary>
        public static Material CullFront(Material m)
        {
            if (m != null && m.HasProperty("_Cull"))
                m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
            return m;
        }

        public static Material Emissive(Color baseColor, Color emission, float intensity = 1.5f)
        {
            var m = Lit(baseColor);
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission * intensity);
            return m;
        }

        private static void SetColorCompat(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>Primitive without its default collider (colliders are added deliberately).</summary>
        public static GameObject Primitive(PrimitiveType type, string name, Transform parent,
            Vector3 localPos, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        /// <summary>Small 3D text label (legacy TextMesh — no TMP asset dependency).</summary>
        public static TextMesh Label(string text, Transform parent, Vector3 localPos,
            float characterSizeMeters = 0.02f, Color? color = null)
        {
            var go = new GameObject("Label_" + text);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = BuiltinFont;
            tm.fontSize = 48;
            tm.characterSize = characterSizeMeters;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color ?? Color.white;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = BuiltinFont.material;
            return tm;
        }
    }
}
