using System.Collections.Generic;
using UnityEngine;

namespace FireEscape
{
    /// <summary>Lớp mô phỏng phủ lên không gian: 🔥 lửa, 💨 khói, 🚨 chuông, 🚪 lối thoát, 👤 NPC.</summary>
    public class HazardView : MonoBehaviour
    {
        class NpcFx { public GameObject go; public Transform body; public TextMesh tag; }

        FacilityLayout L;
        EnvironmentBuilder env;
        PlayerRig player;
        bool real;

        readonly Dictionary<int, FireEffect> fires = new Dictionary<int, FireEffect>();
        readonly Dictionary<string, SmokeLayer> smokes = new Dictionary<string, SmokeLayer>();
        readonly Dictionary<int, NpcFx> npcs = new Dictionary<int, NpcFx>();
        readonly Dictionary<string, GameObject> blockers = new Dictionary<string, GameObject>();

        AudioSource alarmSrc, murmurSrc, sfxSrc;
        Material screenMat, fireMat, smokeMat;
        Transform screenQuad, arrow;
        List<Vector3> arrowPath;
        float arrowRepath, flickerUntil;
        bool lastPower = true;

        public void Init(FacilityLayout layout, EnvironmentBuilder builder, PlayerRig rig)
        {
            L = layout; env = builder; player = rig; real = layout.isRealRoom;
            fireMat = MatLib.Particle(true);
            smokeMat = MatLib.Particle(false);

            alarmSrc = gameObject.AddComponent<AudioSource>();
            alarmSrc.clip = MatLib.AlarmClip; alarmSrc.loop = true; alarmSrc.volume = 0.3f; alarmSrc.playOnAwake = false;
            murmurSrc = gameObject.AddComponent<AudioSource>();
            murmurSrc.clip = MatLib.MurmurClip; murmurSrc.loop = true; murmurSrc.volume = 0f; murmurSrc.playOnAwake = false;
            sfxSrc = gameObject.AddComponent<AudioSource>();
            sfxSrc.playOnAwake = false;

            foreach (var z in L.zones)
            {
                var layer = SmokeLayer.Create(transform, z.area, L.floorY, z.ceiling, player.IsXR);
                layer.name = "Smoke_" + z.id;
                smokes[z.id] = layer;
            }

            var cam = player.Cam;
            if (cam != null)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.name = "SmokeScreen";
                q.transform.SetParent(cam.transform, false);
                q.transform.localPosition = new Vector3(0, 0, 0.2f);
                q.transform.localScale = new Vector3(1.4f, 1.0f, 1f);
                screenMat = MatLib.Transparent(new Color(0.3f, 0.3f, 0.3f, 0f));
                screenMat.SetTexture("_BaseMap", FxTextures.Noise);
                screenMat.mainTexture = FxTextures.Noise;
                screenMat.SetTextureScale("_BaseMap", new Vector2(1.6f, 1.1f));
                screenMat.renderQueue = 3500;
                screenMat.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                q.GetComponent<Renderer>().sharedMaterial = screenMat;
                screenQuad = q.transform;
            }

            arrow = BuildArrow();
            arrow.gameObject.SetActive(false);
            // hậu kỳ chỉ bật trên PC — trên kính giữ khung hình ổn định
            PostFX.Setup(player.Cam, !player.IsXR);
            SetIdle();
        }

        Vector3 W(Vector3 v) => new Vector3(v.x, L.floorY + v.y, v.z);

        // ---------------- trạng thái ----------------
        public void SetIdle()
        {
            alarmSrc.Stop();
            murmurSrc.Stop();
            ApplyLighting(true, true, null);
            if (!real)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = 0.004f;
                RenderSettings.fogColor = new Color(0.35f, 0.36f, 0.38f);
            }
            if (screenMat != null) screenMat.SetColor("_BaseColor", new Color(0, 0, 0, 0));
            PostFX.SetExposure(0f);
            arrow.gameObject.SetActive(false);
        }

        public void ResetAll()
        {
            foreach (var f in fires.Values) if (f) Destroy(f.gameObject);
            fires.Clear();
            foreach (var n in npcs.Values) if (n.go) Destroy(n.go);
            npcs.Clear();
            foreach (var b in blockers.Values) if (b) Destroy(b);
            blockers.Clear();
            foreach (var s in smokes.Values) s.Set(0f, 0f);
            foreach (var d in env.doors.Values)
            {
                var p = L.GetPortal(d.portalId);
                d.pivot.localRotation = p.doorStartsOpen ? d.open : d.closed;
            }
            lastPower = true;
            SetIdle();
        }

        public void PlayBeep() { sfxSrc.PlayOneShot(MatLib.BeepClip, 0.6f); }

        public void Sync(IncidentSimulation sim, float dt, bool showArrow)
        {
            SyncFires(sim);
            SyncSmoke(sim);
            SyncDoorsAndBlocks(sim, dt);
            SyncNpcs(sim, dt);

            if (sim.power != lastPower)
            {
                lastPower = sim.power;
                flickerUntil = Time.time + 0.9f;
            }
            ApplyLighting(sim.power, sim.emergencyLights, sim);

            if (sim.alarm && !alarmSrc.isPlaying) alarmSrc.Play();
            alarmSrc.volume = sim.flags.Contains("stress") ? 0.45f : 0.28f;
            bool stress = sim.flags.Contains("stress");
            if (stress && !murmurSrc.isPlaying) murmurSrc.Play();
            murmurSrc.volume = stress ? 0.35f : 0f;

            // khói trước mắt + mức phơi nhiễm
            var zs = sim.zones[sim.playerZone ?? L.startZone];
            float a = sim.headInSmoke ? 0.22f + 0.6f * zs.smoke : zs.smoke * 0.12f;
            var c = Color.Lerp(new Color(0.28f, 0.28f, 0.29f), new Color(0.35f, 0.02f, 0.02f), Mathf.InverseLerp(50f, 100f, sim.exposure));
            a = Mathf.Max(a, Mathf.InverseLerp(55f, 100f, sim.exposure) * 0.45f);
            if (!sim.power && !sim.emergencyLights) { c = Color.Lerp(c, Color.black, 0.5f); }
            c.a = Mathf.Clamp01(a);
            if (screenMat != null)
            {
                screenMat.SetColor("_BaseColor", c);
                screenMat.SetTextureOffset("_BaseMap", new Vector2(Time.time * 0.03f, Time.time * 0.045f));
            }
            PostFX.SetExposure(sim.exposure);

            if (!real)
            {
                RenderSettings.fogDensity = 0.012f + zs.smoke * 0.16f + (sim.headInSmoke ? 0.1f : 0f);
                RenderSettings.fogColor = sim.power ? new Color(0.36f, 0.36f, 0.37f) : new Color(0.05f, 0.05f, 0.06f);
            }

            // mũi tên hướng dẫn (Beginner)
            arrow.gameObject.SetActive(showArrow && sim.Running);
            if (showArrow)
            {
                if (Time.time >= arrowRepath)
                {
                    arrowRepath = Time.time + 0.3f;
                    arrowPath = sim.SafestPath(player.FootPos, out _);
                }
                if (arrowPath != null)
                {
                    Vector3 next = arrowPath[arrowPath.Count - 1];
                    foreach (var p in arrowPath)
                        if (FacilityLayout.Flat(p, player.FootPos) > 0.9f) { next = p; break; }
                    var head = player.Head;
                    var fwd = head.forward; fwd.y = 0; fwd.Normalize();
                    arrow.position = head.position + fwd * 1.1f - Vector3.up * 0.45f;
                    var dir = W(next) - arrow.position; dir.y = 0;
                    if (dir.sqrMagnitude > 0.01f) arrow.rotation = Quaternion.Slerp(arrow.rotation, Quaternion.LookRotation(dir), 10f * Time.deltaTime);
                }
                else arrow.gameObject.SetActive(false);
            }
        }

        void ApplyLighting(bool power, bool emergency, IncidentSimulation sim)
        {
            bool flicker = Time.time < flickerUntil;
            foreach (var kv in env.zoneLights)
            {
                bool dark = sim != null && sim.zones[kv.Key].dark;
                bool on = power && !dark;
                if (flicker) on = Random.value > 0.5f;
                kv.Value.enabled = on;
            }
            foreach (var l in env.emergencyLights) l.enabled = !power && emergency && !flicker;
            bool signs = power || emergency;
            foreach (var m in env.exitSignMats) MatLib.SetEmission(m, signs ? new Color(0.1f, 0.85f, 0.4f) * 1.6f : Color.black);
            foreach (var l in env.exitLights) l.enabled = signs;
            foreach (var m in env.screenMats) MatLib.SetEmission(m, power ? new Color(0.1f, 0.18f, 0.35f) : Color.black);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = power ? new Color(0.4f, 0.4f, 0.43f) : emergency ? new Color(0.06f, 0.06f, 0.07f) : new Color(0.012f, 0.012f, 0.015f);
        }

        // ---------------- lửa ----------------
        void SyncFires(IncidentSimulation sim)
        {
            foreach (var f in sim.fires)
            {
                if (!fires.TryGetValue(f.id, out var fx))
                {
                    if (f.Out) continue;
                    fx = MakeFire(f);
                    fires[f.id] = fx;
                }
                if (fx == null) continue;
                if (f.Out) fx.Extinguish();
                else fx.SetIntensity(Mathf.Max(0.05f, f.intensity));
            }
        }

        FireEffect MakeFire(FireSource f)
        {
            var zone = L.GetZone(f.zone) ?? L.ZoneAt(f.pos);
            float ceiling = zone != null ? zone.ceiling : 3f;
            // cháy điện nếu nguồn nằm cạnh ổ điện / màn hình / tủ điện
            bool electrical = false;
            foreach (var fixture in L.fixtures)
                if ((fixture.kind == FixtureKind.PowerOutlet || fixture.kind == FixtureKind.Screen || fixture.kind == FixtureKind.ElectricalPanel)
                    && FacilityLayout.Flat(fixture.pos, f.pos) < 0.9f) { electrical = true; break; }
            var fx = FireEffect.Create(transform, W(new Vector3(f.pos.x, 0.02f, f.pos.z)), L.floorY, L.floorY + ceiling, electrical && !f.barrier, player.IsXR);
            fx.name = "Fire_" + f.id;
            return fx;
        }

        static ParticleSystem MakePS(GameObject go, Material mat, float lifetime, float speed, float size, int max, Color color)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.loop = true;
            main.playOnAwake = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        static Gradient Grad(Color a, Color b, Color c)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 0.3f), new GradientColorKey(c, 1) },
                      new[] { new GradientAlphaKey(a.a, 0), new GradientAlphaKey(b.a, 0.3f), new GradientAlphaKey(c.a, 1) });
            return g;
        }

        static Gradient Grad4(Color a, Color b, Color c, Color d)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, 0.15f), new GradientColorKey(c, 0.6f), new GradientColorKey(d, 1) },
                      new[] { new GradientAlphaKey(a.a, 0), new GradientAlphaKey(b.a, 0.15f), new GradientAlphaKey(c.a, 0.6f), new GradientAlphaKey(d.a, 1) });
            return g;
        }

        // ---------------- khói theo khu vực ----------------
        void SyncSmoke(IncidentSimulation sim)
        {
            foreach (var z in L.zones)
            {
                var s = sim.zones[z.id];
                smokes[z.id].Set(s.smoke, s.fire);
            }
        }

        // ---------------- cửa, vật cản ----------------
        void SyncDoorsAndBlocks(IncidentSimulation sim, float dt)
        {
            foreach (var d in env.doors.Values)
            {
                var target = sim.portals[d.portalId].doorOpen ? d.open : d.closed;
                d.pivot.localRotation = Quaternion.RotateTowards(d.pivot.localRotation, target, 260f * dt);
            }
            foreach (var p in L.portals)
            {
                var ps = sim.portals[p.id];
                bool has = blockers.TryGetValue(p.id, out var go) && go != null;
                if (ps.blocked && !has) blockers[p.id] = MakeBlocker(p, ps.cause);
                else if (!ps.blocked && has) { Destroy(go); blockers.Remove(p.id); }
            }
        }

        GameObject MakeBlocker(Portal p, BlockCause cause)
        {
            var go = new GameObject("Block_" + p.id);
            go.transform.SetParent(transform, false);
            go.transform.position = W(p.pos);
            var col = go.AddComponent<BoxCollider>();
            float s = p.width + 0.3f;
            col.center = new Vector3(0, 1.1f, 0);
            col.size = new Vector3(s, 2.2f, s);
            if (cause == BlockCause.Debris)
            {
                var rng = new System.Random(p.id.GetHashCode());
                var m1 = MatLib.Lit(new Color(0.3f, 0.27f, 0.24f));
                var m2 = MatLib.Lit(new Color(0.45f, 0.4f, 0.35f));
                for (int i = 0; i < 9; i++)
                {
                    var b = MatLib.Box(go.transform, "Debris",
                        new Vector3((float)rng.NextDouble() * s - s / 2f, 0.2f + (float)rng.NextDouble() * 0.9f, (float)rng.NextDouble() * s - s / 2f),
                        new Vector3(0.3f + (float)rng.NextDouble() * 0.8f, 0.15f + (float)rng.NextDouble() * 0.4f, 0.2f + (float)rng.NextDouble() * 0.6f),
                        i % 2 == 0 ? m1 : m2, false);
                    b.transform.localRotation = Quaternion.Euler((float)rng.NextDouble() * 50f, (float)rng.NextDouble() * 180f, (float)rng.NextDouble() * 50f);
                }
                MatLib.Label3D(go.transform, "TRẦN SẬP", new Vector3(0, 1.7f, 0), 0.08f, new Color(1f, 0.8f, 0.3f), true);
            }
            else if (cause == BlockCause.Smoke)
            {
                var ps = MakePS(go, smokeMat, 3f, 0.4f, 1.6f, 500, new Color(0.08f, 0.08f, 0.09f, 0.85f));
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(s, 2.2f, s);
                sh.position = new Vector3(0, 1.1f, 0);
                var em = ps.emission; em.rateOverTime = 120f;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(wall.GetComponent<Collider>());
                wall.transform.SetParent(go.transform, false);
                wall.transform.localPosition = new Vector3(0, 1.3f, 0);
                wall.transform.localScale = new Vector3(s, 2.6f, s);
                wall.GetComponent<Renderer>().sharedMaterial = MatLib.Transparent(new Color(0.07f, 0.07f, 0.08f, 0.8f));
                MatLib.Label3D(go.transform, "KHÓI ĐẶC — KHÔNG THỂ ĐI QUA", new Vector3(0, 2.0f, 0), 0.06f, new Color(1f, 0.8f, 0.3f), true);
            }
            return go;
        }

        // ---------------- NPC ----------------
        void SyncNpcs(IncidentSimulation sim, float dt)
        {
            foreach (var n in sim.npcs)
            {
                if (!npcs.TryGetValue(n.id, out var v)) { v = MakeNpc(n); npcs[n.id] = v; }
                if (n.mode == NpcMode.Evacuated) { v.go.SetActive(false); continue; }
                var target = W(n.pos);
                var move = target - v.go.transform.position; move.y = 0;
                v.go.transform.position = target;
                if (move.sqrMagnitude > 0.00001f) v.go.transform.rotation = Quaternion.Slerp(v.go.transform.rotation, Quaternion.LookRotation(move), 8f * dt);
                bool down = n.mode == NpcMode.Incapacitated;
                v.body.localRotation = down ? Quaternion.Euler(-90, 0, 0) : Quaternion.identity;
                v.body.localPosition = down ? new Vector3(0, 0.2f, 0) : new Vector3(0, Mathf.Abs(Mathf.Sin(Time.time * 8f)) * (move.sqrMagnitude > 0.00001f ? 0.03f : 0f), 0);
                v.tag.text = down ? "" : n.mode == NpcMode.Following ? "OK" : n.need switch
                {
                    NpcNeed.Lost => "?",
                    NpcNeed.Slow => "...",
                    NpcNeed.Panicked => n.calm > 0 ? "!" : "!!",
                    _ => "SOS"
                };
            }
        }

        NpcFx MakeNpc(NpcState n)
        {
            var go = new GameObject("Npc_" + n.id);
            go.transform.SetParent(transform, false);
            var body = new GameObject("Body").transform;
            body.SetParent(go.transform, false);
            Color shirt = n.need switch
            {
                NpcNeed.Lost => new Color(0.2f, 0.45f, 0.85f),
                NpcNeed.Slow => new Color(0.55f, 0.35f, 0.7f),
                NpcNeed.Panicked => new Color(0.95f, 0.8f, 0.2f),
                _ => new Color(0.95f, 0.45f, 0.15f)
            };
            var skin = MatLib.Lit(new Color(0.93f, 0.76f, 0.62f));
            var pants = MatLib.Lit(new Color(0.15f, 0.17f, 0.22f));
            MatLib.Prim(PrimitiveType.Capsule, body, "Torso", new Vector3(0, 1.15f, 0), new Vector3(0.42f, 0.35f, 0.26f), MatLib.Lit(shirt));
            MatLib.Prim(PrimitiveType.Sphere, body, "Head", new Vector3(0, 1.62f, 0), new Vector3(0.22f, 0.25f, 0.22f), skin);
            MatLib.Prim(PrimitiveType.Capsule, body, "LegL", new Vector3(-0.1f, 0.42f, 0), new Vector3(0.14f, 0.42f, 0.14f), pants);
            MatLib.Prim(PrimitiveType.Capsule, body, "LegR", new Vector3(0.1f, 0.42f, 0), new Vector3(0.14f, 0.42f, 0.14f), pants);
            var tag = MatLib.Label3D(go.transform, "?", new Vector3(0, 2.05f, 0), 0.12f, new Color(1f, 0.85f, 0.3f), true);
            var col = go.AddComponent<CapsuleCollider>();
            col.isTrigger = true; col.center = new Vector3(0, 0.9f, 0); col.height = 1.8f; col.radius = 0.35f;
            var it = go.AddComponent<Interactable>();
            it.kind = InteractKind.Npc; it.id = n.id.ToString();
            go.transform.position = W(n.pos);
            return new NpcFx { go = go, body = body, tag = tag };
        }

        Transform BuildArrow()
        {
            var root = new GameObject("GuideArrow").transform;
            root.SetParent(transform, false);
            var m = MatLib.Emissive(new Color(0.1f, 0.9f, 0.45f), new Color(0.1f, 0.9f, 0.45f) * 1.4f);
            var shaft = MatLib.Box(root, "Shaft", new Vector3(0, 0, -0.08f), new Vector3(0.05f, 0.02f, 0.22f), m, false);
            var h1 = MatLib.Box(root, "HeadL", new Vector3(-0.04f, 0, 0.06f), new Vector3(0.03f, 0.02f, 0.14f), m, false);
            h1.transform.localRotation = Quaternion.Euler(0, 40f, 0);
            var h2 = MatLib.Box(root, "HeadR", new Vector3(0.04f, 0, 0.06f), new Vector3(0.03f, 0.02f, 0.14f), m, false);
            h2.transform.localRotation = Quaternion.Euler(0, -40f, 0);
            return root;
        }
    }
}
