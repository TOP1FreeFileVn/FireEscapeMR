using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace FireEscape
{
    /// <summary>Texture hiệu ứng sinh bằng noise lúc chạy — không cần asset ngoài.</summary>
    public static class FxTextures
    {
        static Texture2D flame, smoke, noise, scorch;

        public static Texture2D Flame => flame != null ? flame : (flame = MakeFlame());
        public static Texture2D Smoke => smoke != null ? smoke : (smoke = MakeSmoke());
        public static Texture2D Noise => noise != null ? noise : (noise = MakeNoise());
        public static Texture2D Scorch => scorch != null ? scorch : (scorch = MakeScorch());

        static float Fbm(float x, float y, int oct = 4)
        {
            float s = 0f, a = 0.5f, f = 1f, n = 0f;
            for (int i = 0; i < oct; i++) { s += a * Mathf.PerlinNoise(x * f + 17.3f * i, y * f + 9.1f * i); n += a; a *= 0.5f; f *= 2f; }
            return s / n;
        }

        static float Smooth(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / (e1 - e0)); return t * t * (3f - 2f * t); }

        /// <summary>Flipbook 4×4 ngọn lửa: hình giọt nước ngược, mép bị noise cuốn lên theo thời gian.</summary>
        static Texture2D MakeFlame()
        {
            const int F = 64, T = 4, N = F * T;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "FE_Flame" };
            var px = new Color[N * N];
            for (int fi = 0; fi < T * T; fi++)
            {
                int fx = fi % T, fy = (T - 1) - fi / T;
                float t = fi * 0.23f;
                for (int y = 0; y < F; y++)
                    for (int x = 0; x < F; x++)
                    {
                        float u = (x + 0.5f) / F * 2f - 1f, v = (y + 0.5f) / F;
                        float n = Fbm(x * 0.07f, y * 0.07f - t * 4f);
                        float n2 = Fbm(x * 0.15f + 5f, y * 0.12f - t * 6f, 3);
                        float width = Mathf.Lerp(0.62f, 0.03f, Mathf.Pow(v, 0.75f)) * (0.85f + 0.3f * n2);
                        float sway = (n - 0.5f) * 0.5f * v;
                        float d = Mathf.Abs(u - sway) / Mathf.Max(width, 0.001f);
                        float shape = Mathf.Clamp01(1f - d + (n - 0.5f) * 0.7f);
                        shape *= Smooth(0f, 0.12f, v) * Smooth(1f, 0.55f, v + (n - 0.5f) * 0.4f);
                        float a = Smooth(0.05f, 0.6f, shape) * (0.55f + 0.6f * n);
                        float core = Smooth(0.35f, 0.95f, shape) * (1f - v * 0.7f);
                        float b = Mathf.Lerp(0.55f, 1f, core);
                        px[(fy * F + y) * N + fx * F + x] = new Color(b, b, b, Mathf.Clamp01(a));
                    }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Atlas 2×2 cụm khói: tròn méo bởi noise, sáng phía trên (giả chiếu sáng).</summary>
        static Texture2D MakeSmoke()
        {
            const int F = 128, T = 2, N = F * T;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "FE_Smoke" };
            var px = new Color[N * N];
            for (int fi = 0; fi < 4; fi++)
            {
                int fx = fi % T, fy = fi / T;
                for (int y = 0; y < F; y++)
                    for (int x = 0; x < F; x++)
                    {
                        float u = (x + 0.5f) / F * 2f - 1f, v = (y + 0.5f) / F * 2f - 1f;
                        float n = Fbm(x * 0.045f + fi * 11f, y * 0.045f + fi * 7f, 5);
                        float r = Mathf.Sqrt(u * u + v * v) / (0.62f + 0.38f * (n - 0.35f));
                        float a = Mathf.Pow(Mathf.Clamp01(1f - r), 1.3f) * Smooth(0.15f, 0.75f, n + 0.15f);
                        float light = 0.62f + 0.38f * Mathf.Clamp01(0.5f + v * 0.5f) * (0.7f + 0.3f * n);
                        px[(fy * F + y) * N + fx * F + x] = new Color(light, light, light, Mathf.Clamp01(a * 1.25f));
                    }
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Noise liền mạch (tileable) cho lớp khói trôi và màn khói trước mắt.</summary>
        static Texture2D MakeNoise()
        {
            const int N = 128;
            const float P = 6f;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "FE_Noise" };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float fx = x / (float)N * P, fy = y / (float)N * P;
                    float tx = fx / P, ty = fy / P;
                    float a = Mathf.Lerp(Mathf.Lerp(Fbm(fx, fy), Fbm(fx - P, fy), tx), Mathf.Lerp(Fbm(fx, fy - P), Fbm(fx - P, fy - P), tx), ty);
                    float v = Smooth(0.3f, 0.75f, a);
                    px[y * N + x] = new Color(1f, 1f, 1f, v);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        static Texture2D MakeScorch()
        {
            const int N = 128;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "FE_Scorch" };
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float n = Fbm(x * 0.06f, y * 0.06f, 5);
                    float r = Mathf.Sqrt(u * u + v * v) / (0.55f + 0.45f * n);
                    float a = Mathf.Clamp01(1f - r) * (0.6f + 0.4f * n);
                    px[y * N + x] = new Color(0.04f, 0.035f, 0.03f, Smooth(0f, 0.6f, a));
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        public static Material ParticleMat(Texture2D tex, bool additive, Color tint)
        {
            var m = MatLib.Particle(additive);
            m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            m.SetColor("_BaseColor", tint);
            return m;
        }
    }

    /// <summary>Một đám cháy nhiều lớp: lõi, ngọn lửa, khói cuộn chạm trần, tàn lửa, tia lửa điện, ánh sáng, vết cháy xém.</summary>
    public class FireEffect : MonoBehaviour
    {
        ParticleSystem core, flames, smoke, embers, sparks, steam;
        Light glow;
        AudioSource crackle;
        Transform scorch;
        Material scorchMat;
        float maxI, sparkAt, seed, ceilingY, floorY;
        bool electrical, lowQuality, dying;

        static Material flameMat, coreMat, smokeMat, emberMat, sparkMat, steamMat;

        public static FireEffect Create(Transform parent, Vector3 worldPos, float floorY, float ceilingY, bool electrical, bool lowQuality)
        {
            var go = new GameObject("Fire");
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            var fx = go.AddComponent<FireEffect>();
            fx.floorY = floorY; fx.ceilingY = ceilingY; fx.electrical = electrical; fx.lowQuality = lowQuality;
            fx.seed = Random.value * 100f;
            fx.Build();
            return fx;
        }

        static void EnsureMaterials()
        {
            if (flameMat != null) return;
            flameMat = FxTextures.ParticleMat(FxTextures.Flame, true, new Color(1.45f, 1.35f, 1.25f, 1f));
            coreMat = FxTextures.ParticleMat(FxTextures.Flame, true, new Color(1.2f, 1.1f, 0.95f, 1f));
            smokeMat = FxTextures.ParticleMat(FxTextures.Smoke, false, Color.white);
            emberMat = FxTextures.ParticleMat(MatLib.Soft, true, new Color(3f, 3f, 3f, 1f));
            sparkMat = FxTextures.ParticleMat(MatLib.Soft, true, new Color(4f, 4f, 4f, 1f));
            steamMat = FxTextures.ParticleMat(FxTextures.Smoke, false, Color.white);
        }

        static ParticleSystem PS(Transform parent, string name, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.playOnAwake = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static Gradient G(params (Color c, float t)[] keys)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[keys.Length];
            var ak = new GradientAlphaKey[keys.Length];
            for (int i = 0; i < keys.Length; i++) { ck[i] = new GradientColorKey(keys[i].c, keys[i].t); ak[i] = new GradientAlphaKey(keys[i].c.a, keys[i].t); }
            g.SetKeys(ck, ak);
            return g;
        }

        static void Sheet(ParticleSystem ps, int tiles, bool animate)
        {
            var ts = ps.textureSheetAnimation;
            ts.enabled = true;
            ts.numTilesX = tiles; ts.numTilesY = tiles;
            if (animate)
            {
                ts.animation = ParticleSystemAnimationType.WholeSheet;
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0, 1, 1));
                ts.cycleCount = 1;
            }
            else
            {
                ts.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
                ts.startFrame = new ParticleSystem.MinMaxCurve(0f, tiles * tiles - 0.01f);
            }
        }

        void Build()
        {
            EnsureMaterials();
            int q = lowQuality ? 1 : 2;

            // ngọn lửa chính
            flames = PS(transform, "Flames", flameMat, 120 * q);
            var m = flames.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
            m.startRotation = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
            var sh = flames.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 4f; sh.radius = 0.2f; sh.rotation = new Vector3(-90, 0, 0);
            var vol = flames.velocityOverLifetime; vol.enabled = true; vol.space = ParticleSystemSimulationSpace.World;
            vol.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.3f, 1, 1.6f));
            vol.x = new ParticleSystem.MinMaxCurve(0f, AnimationCurve.Constant(0f, 1f, 0f));
            vol.z = new ParticleSystem.MinMaxCurve(0f, AnimationCurve.Constant(0f, 1f, 0f));
            var sz = flames.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.25f, 1f), new Keyframe(1, 0.35f)));
            var col = flames.colorOverLifetime; col.enabled = true;
            col.color = G((new Color(1f, 0.85f, 0.55f, 0f), 0f), (new Color(1f, 0.62f, 0.2f, 0.95f), 0.12f), (new Color(0.95f, 0.32f, 0.06f, 0.75f), 0.55f), (new Color(0.35f, 0.07f, 0.02f, 0f), 1f));
            var noise = flames.noise; noise.enabled = true; noise.strength = 0.35f; noise.frequency = 1.4f; noise.scrollSpeed = 1.2f; noise.quality = ParticleSystemNoiseQuality.Medium;
            Sheet(flames, 4, true);
            flames.Play();

            // lõi trắng-vàng ở chân lửa
            core = PS(transform, "Core", coreMat, 50 * q);
            m = core.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            m.startColor = new Color(1f, 0.9f, 0.65f, 0.9f);
            sh = core.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.12f; sh.rotation = new Vector3(-90, 0, 0);
            sz = core.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 1f, 1, 0.4f));
            Sheet(core, 4, true);
            core.Play();

            // khói cuộn: chân ám cam, bốc lên chạm trần rồi loang ngang
            smoke = PS(transform, "Smoke", smokeMat, 70 * q);
            smoke.transform.localPosition = new Vector3(0, 0.5f, 0);
            m = smoke.main; m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.25f; sh.rotation = new Vector3(-90, 0, 0);
            var rot = smoke.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.35f, 1, 1f));
            col = smoke.colorOverLifetime; col.enabled = true;
            col.color = G((new Color(0.35f, 0.2f, 0.1f, 0f), 0f), (new Color(0.16f, 0.14f, 0.13f, 0.7f), 0.18f), (new Color(0.22f, 0.22f, 0.22f, 0.45f), 0.7f), (new Color(0.28f, 0.28f, 0.28f, 0f), 1f));
            noise = smoke.noise; noise.enabled = true; noise.strength = 0.45f; noise.frequency = 0.35f; noise.scrollSpeed = 0.2f;
            Sheet(smoke, 2, false);
            var coll = smoke.collision; coll.enabled = true; coll.type = ParticleSystemCollisionType.Planes; coll.mode = ParticleSystemCollisionMode.Collision3D;
            coll.dampen = 0.85f; coll.bounce = 0.02f; coll.lifetimeLoss = 0f; coll.radiusScale = 0.3f;
            var ceil = new GameObject("CeilingPlane").transform;
            ceil.SetParent(transform, false);
            ceil.position = new Vector3(transform.position.x, ceilingY - 0.15f, transform.position.z);
            ceil.rotation = Quaternion.Euler(180f, 0f, 0f);
            coll.SetPlane(0, ceil);
            smoke.Play();

            // tàn lửa bay xoáy
            embers = PS(transform, "Embers", emberMat, 60 * q);
            m = embers.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.8f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.035f); m.gravityModifier = -0.12f;
            sh = embers.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 22f; sh.radius = 0.25f; sh.rotation = new Vector3(-90, 0, 0);
            col = embers.colorOverLifetime; col.enabled = true;
            col.color = G((new Color(1f, 0.85f, 0.5f, 1f), 0f), (new Color(1f, 0.45f, 0.1f, 1f), 0.5f), (new Color(0.6f, 0.1f, 0.02f, 0f), 1f));
            noise = embers.noise; noise.enabled = true; noise.strength = 1.1f; noise.frequency = 1.8f; noise.scrollSpeed = 0.8f;
            var er = embers.GetComponent<ParticleSystemRenderer>(); er.renderMode = ParticleSystemRenderMode.Stretch; er.velocityScale = 0.015f; er.lengthScale = 1.2f;
            embers.Play();

            // tia lửa điện (chỉ cháy điện): bắn theo từng đợt, rơi và nảy trên sàn
            if (electrical)
            {
                sparks = PS(transform, "Sparks", sparkMat, 120);
                m = sparks.main; m.loop = true; m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.7f); m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f); m.gravityModifier = 1f;
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.92f, 1f), new Color(1f, 0.85f, 0.5f));
                var em = sparks.emission; em.rateOverTime = 0f;
                sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.05f; sh.rotation = new Vector3(-90, 0, 0);
                col = sparks.colorOverLifetime; col.enabled = true;
                col.color = G((new Color(1f, 1f, 1f, 1f), 0f), (new Color(1f, 0.7f, 0.3f, 1f), 0.5f), (new Color(0.8f, 0.3f, 0.05f, 0f), 1f));
                var sc = sparks.collision; sc.enabled = true; sc.type = ParticleSystemCollisionType.Planes; sc.bounce = 0.35f; sc.dampen = 0.3f; sc.lifetimeLoss = 0.3f;
                var fl = new GameObject("FloorPlane").transform;
                fl.SetParent(transform, false);
                fl.position = new Vector3(transform.position.x, floorY + 0.01f, transform.position.z);
                sc.SetPlane(0, fl);
                var sr = sparks.GetComponent<ParticleSystemRenderer>(); sr.renderMode = ParticleSystemRenderMode.Stretch; sr.velocityScale = 0.06f; sr.lengthScale = 1f;
                sparks.Play();
            }

            // hơi nước khi dập tắt
            steam = PS(transform, "Steam", steamMat, 60);
            m = steam.main; m.loop = false; m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f); m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.2f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var sem = steam.emission; sem.rateOverTime = 0f;
            sh = steam.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 25f; sh.radius = 0.3f; sh.rotation = new Vector3(-90, 0, 0);
            col = steam.colorOverLifetime; col.enabled = true;
            col.color = G((new Color(0.9f, 0.9f, 0.92f, 0f), 0f), (new Color(0.85f, 0.86f, 0.88f, 0.55f), 0.15f), (new Color(0.7f, 0.7f, 0.72f, 0f), 1f));
            sz = steam.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.5f, 1, 2.2f));
            Sheet(steam, 2, false);

            // ánh sáng
            var lg = new GameObject("Glow");
            lg.transform.SetParent(transform, false);
            glow = lg.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.color = new Color(1f, 0.45f, 0.14f);
            glow.shadows = lowQuality ? LightShadows.None : LightShadows.Soft;
            glow.shadowStrength = 0.6f;

            // vết cháy xém trên sàn
            var sq = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(sq.GetComponent<Collider>());
            sq.name = "Scorch";
            scorch = sq.transform;
            scorch.SetParent(transform, false);
            scorch.position = new Vector3(transform.position.x, floorY + 0.006f, transform.position.z);
            scorch.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            scorchMat = MatLib.Transparent(new Color(1f, 1f, 1f, 0f), false);
            scorchMat.SetTexture("_BaseMap", FxTextures.Scorch);
            scorchMat.mainTexture = FxTextures.Scorch;
            scorchMat.renderQueue = 2990;
            sq.GetComponent<Renderer>().sharedMaterial = scorchMat;
            sq.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            crackle = gameObject.AddComponent<AudioSource>();
            crackle.clip = MatLib.CrackleClip; crackle.loop = true; crackle.spatialBlend = 1f;
            crackle.minDistance = 1f; crackle.maxDistance = 14f; crackle.rolloffMode = AudioRolloffMode.Linear;
            crackle.Play();
        }

        /// <summary>I = cường độ 0..1 lấy từ mô phỏng.</summary>
        public void SetIntensity(float I)
        {
            if (dying) return;
            I = Mathf.Clamp01(I);
            maxI = Mathf.Max(maxI, I);
            float q = lowQuality ? 0.6f : 1f;

            var em = flames.emission; em.rateOverTime = (18f + 110f * I) * q;
            var m = flames.main;
            m.startSize = new ParticleSystem.MinMaxCurve(0.35f + 0.7f * I, 0.6f + 1.5f * I);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f + 0.5f * I, 0.5f + 1.2f * I);
            var sh = flames.shape; sh.radius = 0.08f + 0.75f * I;

            em = core.emission; em.rateOverTime = (6f + 22f * I) * q;
            m = core.main; m.startSize = new ParticleSystem.MinMaxCurve(0.1f + 0.2f * I, 0.18f + 0.35f * I);
            sh = core.shape; sh.radius = 0.05f + 0.45f * I;

            em = smoke.emission; em.rateOverTime = (2f + 16f * I) * q;
            m = smoke.main; m.startSize = new ParticleSystem.MinMaxCurve(0.5f + 1.2f * I, 1f + 2.2f * I);
            smoke.transform.localPosition = new Vector3(0, 0.35f + 0.9f * I, 0);
            sh = smoke.shape; sh.radius = 0.15f + 0.6f * I;

            em = embers.emission; em.rateOverTime = (3f + 22f * I) * q;
            sh = embers.shape; sh.radius = 0.1f + 0.6f * I;

            if (sparks != null && Time.time >= sparkAt)
            {
                // tia lửa điện dày khi cháy còn nhỏ (giai đoạn chập), thưa dần khi lửa lớn
                sparkAt = Time.time + Random.Range(0.4f, 1.6f) * (1f + I * 3f);
                sparks.Emit(Random.Range(12, 35));
                glow.intensity += 4f;
            }

            float t = Time.time + seed;
            float flicker = 0.72f + 0.2f * Mathf.PerlinNoise(t * 7f, seed) + 0.12f * Mathf.PerlinNoise(t * 21f, seed + 3f);
            float target = (1f + 4.2f * I) * flicker;
            glow.intensity = Mathf.Lerp(glow.intensity, target, 0.5f);
            glow.range = 2.5f + 7f * I;
            glow.transform.localPosition = new Vector3((Mathf.PerlinNoise(t * 3f, 1f) - 0.5f) * 0.15f, 0.3f + 0.9f * I, (Mathf.PerlinNoise(t * 3f, 2f) - 0.5f) * 0.15f);

            float s = 0.4f + 2.6f * maxI;
            scorch.localScale = new Vector3(s, s, 1f);
            scorchMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, Mathf.Clamp01(0.25f + maxI)));

            crackle.volume = 0.15f + 0.65f * I;
            crackle.pitch = 1.1f - 0.35f * I;
        }

        /// <summary>Lửa tắt: ngừng phát, phụt hơi nước/khói trắng, giữ lại vết cháy xém.</summary>
        public void Extinguish()
        {
            if (dying) return;
            dying = true;
            foreach (var ps in new[] { flames, core, embers, sparks })
            {
                if (ps == null) continue;
                var em = ps.emission; em.enabled = false;
            }
            var sm = smoke.emission; sm.rateOverTime = 2f;
            steam.Emit(Mathf.RoundToInt(15 + 35 * maxI));
            glow.enabled = false;
            crackle.Stop();
            Destroy(flames.gameObject, 1.5f);
            Destroy(core.gameObject, 1.5f);
            if (sparks != null) Destroy(sparks.gameObject, 1.5f);
            Invoke(nameof(StopSmoke), 3f);
        }

        void StopSmoke()
        {
            var sm = smoke.emission; sm.enabled = false;
        }
    }

    /// <summary>Lớp khói tích tụ dưới trần: nhiều mặt phẳng noise trôi cuộn + cụm khói, thay cho khối hộp xám.</summary>
    public class SmokeLayer : MonoBehaviour
    {
        Transform[] sheets;
        Material[] mats;
        ParticleSystem billow;
        Vector2[] drift;
        Rect area;
        float ceiling, floorY;

        public static SmokeLayer Create(Transform parent, Rect area, float floorY, float ceiling, bool lowQuality)
        {
            var go = new GameObject("SmokeLayer");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<SmokeLayer>();
            s.area = area; s.floorY = floorY; s.ceiling = ceiling;
            int n = lowQuality ? 2 : 4;
            s.sheets = new Transform[n]; s.mats = new Material[n]; s.drift = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.name = "Sheet" + i;
                q.transform.SetParent(go.transform, false);
                q.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                q.transform.localScale = new Vector3(area.width, area.height, 1f);
                var m = MatLib.Transparent(new Color(0.3f, 0.3f, 0.31f, 0f));
                m.SetTexture("_BaseMap", FxTextures.Noise);
                m.mainTexture = FxTextures.Noise;
                m.SetTextureScale("_BaseMap", new Vector2(area.width / 3.5f, area.height / 3.5f));
                q.GetComponent<Renderer>().sharedMaterial = m;
                q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                s.sheets[i] = q.transform; s.mats[i] = m;
                float ang = i * 2.1f;
                s.drift[i] = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (0.015f + 0.01f * i);
            }
            var bg = new GameObject("Billow");
            bg.transform.SetParent(go.transform, false);
            s.billow = bg.AddComponent<ParticleSystem>();
            s.billow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = s.billow.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = lowQuality ? 150 : 350;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rot = s.billow.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
            var col = s.billow.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 0), new GradientColorKey(new Color(0.22f, 0.22f, 0.23f), 1) },
                      new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(0.4f, 0.3f), new GradientAlphaKey(0f, 1) });
            col.color = g;
            var sh = s.billow.shape; sh.shapeType = ParticleSystemShapeType.Box;
            var tsa = s.billow.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 2; tsa.numTilesY = 2;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            var r = bg.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FxTextures.ParticleMat(FxTextures.Smoke, false, Color.white);
            r.shadowCastingMode = ShadowCastingMode.Off;
            var em = s.billow.emission; em.rateOverTime = 0f;
            s.billow.Play();
            go.SetActive(false);
            return s;
        }

        /// <summary>smoke 0..1, fire 0..1 (khói gần lửa tối và ám đỏ hơn).</summary>
        public void Set(float smoke, float fire)
        {
            bool on = smoke > 0.02f;
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
            if (!on) return;
            float depth = Mathf.Max(0.05f, ceiling * smoke * 0.85f);
            float bottom = floorY + ceiling - depth;
            float gray = Mathf.Lerp(0.34f, 0.15f, fire);
            for (int i = 0; i < sheets.Length; i++)
            {
                float k = sheets.Length == 1 ? 0f : i / (float)(sheets.Length - 1);
                sheets[i].position = new Vector3(area.center.x, Mathf.Lerp(bottom + 0.05f, floorY + ceiling - 0.05f, k), area.center.y);
                var off = mats[i].GetTextureOffset("_BaseMap") + drift[i] * Time.deltaTime * (1f + fire * 2f);
                mats[i].SetTextureOffset("_BaseMap", off);
                float a = Mathf.Clamp01((0.18f + 0.6f * smoke) * (i == 0 ? 0.55f : 0.4f + 0.5f * k));
                mats[i].SetColor("_BaseColor", new Color(gray + 0.03f * fire, gray, gray * 0.98f, a));
            }
            billow.transform.position = new Vector3(area.center.x, bottom + depth * 0.5f, area.center.y);
            var sh = billow.shape; sh.scale = new Vector3(area.width * 0.95f, depth, area.height * 0.95f);
            var em = billow.emission; em.rateOverTime = 3f + 30f * smoke * Mathf.Clamp(area.width * area.height / 25f, 0.3f, 2f);
        }
    }

    /// <summary>Hậu kỳ cho PC: Bloom + ACES để lửa "phát sáng"; Vignette dùng làm cảm giác choáng khi hít khói.</summary>
    public static class PostFX
    {
        static Vignette vignette;

        public static void Setup(Camera cam, bool enable)
        {
            if (!enable || cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
            cam.allowHDR = true;
            var go = new GameObject("FireEscape PostFX");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.1f);
            bloom.intensity.Override(0.55f);
            bloom.scatter.Override(0.72f);
            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.35f);
            ca.contrast.Override(8f);
            vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.18f);
            vignette.smoothness.Override(0.5f);
            vignette.color.Override(Color.black);
            vol.sharedProfile = profile;
        }

        /// <summary>exposure 0..100 → viền tối đỏ dần.</summary>
        public static void SetExposure(float exposure)
        {
            if (vignette == null) return;
            float k = Mathf.InverseLerp(40f, 100f, exposure);
            vignette.intensity.Override(0.18f + 0.35f * k);
            vignette.color.Override(Color.Lerp(Color.black, new Color(0.35f, 0f, 0f), k));
        }
    }
}
