using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FireEscape
{
    /// <summary>Vật liệu, texture, font và âm thanh sinh bằng code — prototype không cần asset ngoài.</summary>
    public static class MatLib
    {
        static Shader lit, unlit, particle;
        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();
        static Texture2D soft;
        static Font font;

        public static void Init()
        {
            lit = FindShader("FE_Lit", "Universal Render Pipeline/Lit", "Standard");
            unlit = FindShader("FE_Unlit", "Universal Render Pipeline/Unlit", "Unlit/Color");
            particle = FindShader("FE_Particle", "Universal Render Pipeline/Particles/Unlit", "Sprites/Default");
        }

        static Shader FindShader(string template, string name, string fallback)
        {
            var m = Resources.Load<Material>("FireEscapeMats/" + template);
            if (m != null && m.shader != null) return m.shader;
            return Shader.Find(name) ?? Shader.Find(fallback);
        }

        public static Material Lit(Color c, float smoothness = 0.25f, float metallic = 0f)
        {
            string key = $"lit{c}{smoothness}{metallic}";
            if (cache.TryGetValue(key, out var m)) return m;
            m = new Material(lit) { color = c };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            cache[key] = m;
            return m;
        }

        public static Material Emissive(Color baseColor, Color emission)
        {
            var m = new Material(lit) { color = baseColor };
            m.SetColor("_BaseColor", baseColor);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        public static void SetEmission(Material m, Color emission)
        {
            m.SetColor("_EmissionColor", emission);
        }

        public static Material Transparent(Color c, bool cullOff = true)
        {
            var m = new Material(unlit) { color = c };
            MakeTransparent(m, false);
            m.SetColor("_BaseColor", c);
            if (cullOff) m.SetFloat("_Cull", (float)CullMode.Off);
            return m;
        }

        public static Material Particle(bool additive)
        {
            var m = new Material(particle);
            MakeTransparent(m, additive);
            m.SetTexture("_BaseMap", Soft);
            m.mainTexture = Soft;
            m.SetColor("_BaseColor", Color.white);
            return m;
        }

        public static void MakeTransparent(Material m, bool additive)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)RenderQueue.Transparent;
        }

        public static Texture2D Soft
        {
            get
            {
                if (soft != null) return soft;
                const int n = 64;
                soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        px[y * n + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
                    }
                soft.SetPixels(px);
                soft.Apply();
                return soft;
            }
        }

        public static Font Font
        {
            get
            {
                if (font != null) return font;
                try { font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial", "Roboto", "Noto Sans", "Helvetica" }, 32); }
                catch { font = null; }
                if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return font;
            }
        }

        public static TextMesh Label3D(Transform parent, string text, Vector3 localPos, float size, Color color, bool billboard = false)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var tm = go.AddComponent<TextMesh>();
            tm.font = Font;
            tm.text = text;
            tm.fontSize = 64;
            tm.characterSize = size / 10f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            go.GetComponent<MeshRenderer>().sharedMaterial = Font.material;
            if (billboard) go.AddComponent<Billboard>();
            return tm;
        }

        public static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        /// <summary>Model dựng bằng Blender (BlenderSource/fire_equipment.py). Trả về null nếu chưa có.</summary>
        public static GameObject Model(string name, Transform parent, Vector3 localPos, float scale = 1f)
        {
            var prefab = Resources.Load<GameObject>("FireEscapeModels/" + name);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = name;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * scale;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            return go;
        }

        public static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.Destroy(go.GetComponent<Collider>());
            return go;
        }

        // ---------------- âm thanh sinh bằng code ----------------
        static AudioClip alarm, crackle, murmur, beep;

        public static AudioClip AlarmClip => alarm ??= Make("alarm", 2f, (t, i) =>
        {
            float f = (t % 1f) < 0.5f ? 880f : 660f;
            float s = Mathf.Sin(2f * Mathf.PI * f * t);
            return Mathf.Sign(s) * 0.25f + s * 0.25f;
        });

        public static AudioClip CrackleClip => crackle ??= MakeNoise("crackle", 3f, 101, (t, r) =>
        {
            float pop = r.NextDouble() < 0.0015 ? (float)(r.NextDouble() * 2 - 1) : 0f;
            return pop * 0.9f + (float)(r.NextDouble() * 2 - 1) * 0.06f;
        });

        public static AudioClip MurmurClip => murmur ??= MakeNoise("murmur", 4f, 7, (t, r) =>
        {
            float env = 0.5f + 0.5f * Mathf.Sin(t * 5.3f) * Mathf.Sin(t * 2.1f + 1f);
            return (float)(r.NextDouble() * 2 - 1) * 0.18f * env * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 180f * t));
        });

        public static AudioClip BeepClip => beep ??= Make("beep", 0.12f, (t, i) => Mathf.Sin(2f * Mathf.PI * 1200f * t) * 0.3f * (1f - t / 0.12f));

        static AudioClip Make(string name, float seconds, System.Func<float, int, float> f)
        {
            const int rate = 22050;
            int n = Mathf.CeilToInt(seconds * rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = f((float)i / rate, i);
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeNoise(string name, float seconds, int seed, System.Func<float, System.Random, float> f)
        {
            var r = new System.Random(seed);
            float lp = 0f;
            return Make(name, seconds, (t, i) => { lp = Mathf.Lerp(lp, f(t, r), 0.35f); return lp; });
        }
    }

    public class Billboard : MonoBehaviour
    {
        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var d = transform.position - cam.transform.position;
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d);
        }
    }
}
