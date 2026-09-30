using System.Collections.Generic;
using UnityEngine;

namespace FireEscape
{
    public class DoorView
    {
        public string portalId;
        public Transform pivot;
        public Quaternion closed, open;
    }

    /// <summary>
    /// Dựng hình học cho không gian huấn luyện. Ở chế độ MR (phòng thật) chỉ dựng các vật thể ảo
    /// (chuông, bình chữa cháy, biển EXIT...) vì tường, cửa, bàn đã có thật qua passthrough.
    /// </summary>
    public class EnvironmentBuilder
    {
        public Transform root;
        public GameObject scanRoot;
        public readonly Dictionary<string, DoorView> doors = new Dictionary<string, DoorView>();
        public readonly Dictionary<string, GameObject> fixtureObjects = new Dictionary<string, GameObject>();
        public readonly List<KeyValuePair<string, Light>> zoneLights = new List<KeyValuePair<string, Light>>();
        public readonly List<Light> emergencyLights = new List<Light>();
        public readonly List<Light> exitLights = new List<Light>();
        public readonly List<Material> exitSignMats = new List<Material>();
        public readonly List<Material> screenMats = new List<Material>();

        FacilityLayout L;
        bool real;

        static readonly Color ExitGreen = new Color(0.1f, 0.85f, 0.4f);

        public void Build(FacilityLayout layout)
        {
            L = layout;
            real = layout.isRealRoom;
            root = new GameObject("Facility").transform;
            root.position = new Vector3(0, layout.floorY, 0);

            if (!real)
            {
                foreach (var z in L.zones) BuildZone(z);
                foreach (var p in L.portals) if (p.hasDoor && p.walkable) BuildDoor(p);
                BuildStairs();
            }
            foreach (var f in L.fixtures) BuildFixture(f);
            BuildScanLabels();
        }

        // ---------------- phòng, tường, trần ----------------
        void BuildZone(Zone z)
        {
            var zr = new GameObject(z.id).transform;
            zr.SetParent(root, false);
            var a = z.area;
            Color floorC, wallC;
            if (z.isExit) { floorC = new Color(0.32f, 0.36f, 0.34f); wallC = new Color(0.62f, 0.64f, 0.63f); }
            else if (z.id.StartsWith("corr")) { floorC = new Color(0.52f, 0.54f, 0.56f); wallC = new Color(0.84f, 0.86f, 0.87f); }
            else if (z.id == "lab") { floorC = new Color(0.66f, 0.7f, 0.7f); wallC = new Color(0.8f, 0.87f, 0.89f); }
            else { floorC = new Color(0.6f, 0.47f, 0.34f); wallC = new Color(0.93f, 0.9f, 0.8f); }

            MatLib.Box(zr, "Floor", new Vector3(a.center.x, -0.05f, a.center.y), new Vector3(a.width, 0.1f, a.height), MatLib.Lit(floorC, 0.35f));
            var ceil = MatLib.Box(zr, "Ceiling", new Vector3(a.center.x, z.ceiling + 0.05f, a.center.y), new Vector3(a.width, 0.1f, a.height), MatLib.Lit(new Color(0.88f, 0.88f, 0.86f)));
            ceil.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var wm = MatLib.Lit(wallC, 0.1f);
            Edge(zr, z, true, a.yMin, a.xMin, a.xMax, +1f, wm);
            Edge(zr, z, true, a.yMax, a.xMin, a.xMax, -1f, wm);
            Edge(zr, z, false, a.xMin, a.yMin, a.yMax, +1f, wm);
            Edge(zr, z, false, a.xMax, a.yMin, a.yMax, -1f, wm);

            // chân tường tối màu cho dễ nhận biết ranh giới khi có khói
            if (!z.isExit)
            {
                var skirt = MatLib.Lit(new Color(0.25f, 0.25f, 0.27f));
                MatLib.Box(zr, "Skirt", new Vector3(a.center.x, 0.05f, a.yMin + 0.11f), new Vector3(a.width - 0.3f, 0.1f, 0.02f), skirt, false);
            }

            // đèn trần
            int n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(a.width, a.height) / 5f));
            for (int i = 0; i < n; i++)
            {
                float k = (i + 0.5f) / n;
                var pos = a.width >= a.height
                    ? new Vector3(Mathf.Lerp(a.xMin, a.xMax, k), z.ceiling - 0.25f, a.center.y)
                    : new Vector3(a.center.x, z.ceiling - 0.25f, Mathf.Lerp(a.yMin, a.yMax, k));
                var lg = new GameObject("Light");
                lg.transform.SetParent(zr, false);
                lg.transform.localPosition = pos;
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = Mathf.Max(a.width, a.height) / n + 4f;
                l.intensity = 1.6f;
                l.color = new Color(1f, 0.96f, 0.9f);
                l.shadows = LightShadows.None;
                zoneLights.Add(new KeyValuePair<string, Light>(z.id, l));
                MatLib.Box(zr, "Lamp", pos + new Vector3(0, 0.2f, 0), new Vector3(1.2f, 0.04f, 0.3f), MatLib.Emissive(Color.white, new Color(1f, 0.97f, 0.9f) * 1.2f), false);
            }

            // đèn khẩn cấp (chỉ bật khi mất điện)
            if (!z.isExit)
            {
                var eg = new GameObject("EmergencyLight");
                eg.transform.SetParent(zr, false);
                eg.transform.localPosition = new Vector3(a.xMin + 0.4f, z.ceiling - 0.4f, a.yMin + 0.3f);
                var el = eg.AddComponent<Light>();
                el.type = LightType.Point;
                el.range = Mathf.Max(a.width, a.height) * 0.9f;
                el.intensity = 0.9f;
                el.color = new Color(1f, 0.95f, 0.85f);
                el.enabled = false;
                emergencyLights.Add(el);
                var em = MatLib.Model("EmergencyLight", eg.transform, new Vector3(0, 0, -0.2f));
                if (em != null) em.transform.localRotation = Quaternion.identity;
                else MatLib.Box(eg.transform, "Box", Vector3.zero, new Vector3(0.3f, 0.1f, 0.08f), MatLib.Lit(new Color(0.9f, 0.9f, 0.9f)), false);
            }
        }

        void Edge(Transform parent, Zone z, bool alongX, float fixedCoord, float from, float to, float inward, Material mat)
        {
            var gaps = new List<Vector2>();
            foreach (var p in L.portals)
            {
                if (!p.walkable || !p.Touches(z.id)) continue;
                float pc = alongX ? p.pos.z : p.pos.x;
                if (Mathf.Abs(pc - fixedCoord) > 0.05f) continue;
                float t = alongX ? p.pos.x : p.pos.z;
                if (t < from - 0.01f || t > to + 0.01f) continue;
                gaps.Add(new Vector2(t - p.width / 2f, t + p.width / 2f));
            }
            gaps.Sort((u, v) => u.x.CompareTo(v.x));
            float c = z.ceiling, cur = from;
            foreach (var g in gaps)
            {
                if (g.x > cur + 0.01f) Seg(parent, alongX, fixedCoord, inward, cur, g.x, 0f, c, mat);
                if (c > 2.2f) Seg(parent, alongX, fixedCoord, inward, g.x, g.y, 2.2f, c, mat);
                cur = Mathf.Max(cur, g.y);
            }
            if (to > cur + 0.01f) Seg(parent, alongX, fixedCoord, inward, cur, to, 0f, c, mat);
        }

        static void Seg(Transform parent, bool alongX, float fixedCoord, float inward, float a, float b, float y0, float y1, Material mat)
        {
            float len = b - a, mid = (a + b) / 2f, h = y1 - y0, off = fixedCoord + inward * 0.05f;
            var pos = alongX ? new Vector3(mid, (y0 + y1) / 2f, off) : new Vector3(off, (y0 + y1) / 2f, mid);
            var size = alongX ? new Vector3(len, h, 0.1f) : new Vector3(0.1f, h, len);
            MatLib.Box(parent, "Wall", pos, size, mat);
        }

        // ---------------- cửa ----------------
        void BuildDoor(Portal p)
        {
            var za = L.GetZone(p.a); var zb = L.GetZone(p.b);
            bool alongX = Mathf.Abs(p.pos.z - za.area.yMin) < 0.06f || Mathf.Abs(p.pos.z - za.area.yMax) < 0.06f;
            var edgeDir = alongX ? Vector3.right : Vector3.forward;
            float baseYaw = alongX ? 0f : -90f;
            var pivot = new GameObject("Door_" + p.id).transform;
            pivot.SetParent(root, false);
            pivot.localPosition = p.pos - edgeDir * (p.width / 2f);
            pivot.localRotation = Quaternion.Euler(0, baseYaw, 0);

            bool fireDoor = za.isExit || zb.isExit;
            var mat = MatLib.Lit(fireDoor ? new Color(0.55f, 0.13f, 0.1f) : new Color(0.5f, 0.33f, 0.2f), 0.3f);
            float h = 2.15f;
            var panel = MatLib.Box(pivot, "Panel", new Vector3(p.width / 2f, h / 2f, 0), new Vector3(p.width - 0.02f, h, 0.05f), mat);
            MatLib.Box(pivot, "Handle", new Vector3(p.width - 0.12f, 1.0f, 0.05f), new Vector3(0.1f, 0.03f, 0.04f), MatLib.Lit(new Color(0.8f, 0.8f, 0.8f), 0.8f, 1f), false);
            MatLib.Box(pivot, "Handle2", new Vector3(p.width - 0.12f, 1.0f, -0.05f), new Vector3(0.1f, 0.03f, 0.04f), MatLib.Lit(new Color(0.8f, 0.8f, 0.8f), 0.8f, 1f), false);
            if (!fireDoor) MatLib.Box(pivot, "Glass", new Vector3(p.width / 2f, 1.6f, 0), new Vector3(0.3f, 0.4f, 0.06f), MatLib.Lit(new Color(0.6f, 0.75f, 0.85f), 0.9f), false);
            var it = panel.AddComponent<Interactable>();
            it.kind = InteractKind.Door; it.id = p.id;

            // hướng mở: về phía khu vực b
            var toB = zb.Center - p.pos; toB.y = 0;
            float theta = 95f;
            if (Vector3.Dot(Quaternion.Euler(0, baseYaw + theta, 0) * Vector3.right, toB) < 0f) theta = -95f;
            var dv = new DoorView
            {
                portalId = p.id, pivot = pivot,
                closed = Quaternion.Euler(0, baseYaw, 0),
                open = Quaternion.Euler(0, baseYaw + theta, 0)
            };
            pivot.localRotation = p.doorStartsOpen ? dv.open : dv.closed;
            doors[p.id] = dv;
        }

        void BuildStairs()
        {
            var z = L.GetZone("stairsA");
            if (z == null) return;
            var sr = new GameObject("Stairs").transform;
            sr.SetParent(root, false);
            var m = MatLib.Lit(new Color(0.55f, 0.55f, 0.53f));
            for (int i = 0; i < 6; i++)
                MatLib.Box(sr, "Step", new Vector3(z.area.xMin + 0.4f + i * 0.28f * 0f, 0.09f + i * 0.17f, z.area.yMax - 0.6f - i * 0.3f),
                    new Vector3(1.2f, 0.17f, 0.3f), m, false);
            MatLib.Label3D(sr, "↓ TẦNG 1 — RA NGOÀI", new Vector3(z.area.center.x, 2.3f, z.area.yMin + 0.15f), 0.18f, ExitGreen).transform.rotation = Quaternion.LookRotation(Vector3.back);
            var exitB = L.GetZone("exitB");
            if (exitB != null)
                MatLib.Label3D(sr, "ĐIỂM TẬP KẾT →", new Vector3(exitB.area.xMax - 0.15f, 1.8f, exitB.area.center.y), 0.2f, ExitGreen).transform.rotation = Quaternion.LookRotation(Vector3.right);
        }

        // ---------------- thiết bị ----------------
        void BuildFixture(Fixture f)
        {
            if (real && f.kind == FixtureKind.Desk) return;
            var go = new GameObject(f.id);
            go.transform.SetParent(root, false);
            go.transform.localPosition = f.pos;
            var facing = f.facing; facing.y = 0;
            if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
            go.transform.localRotation = Quaternion.LookRotation(facing);
            var t = go.transform;
            fixtureObjects[f.id] = go;

            switch (f.kind)
            {
                case FixtureKind.AlarmStation:
                {
                    if (MatLib.Model("CallPoint", t, Vector3.zero, 1.2f) == null)
                    {
                        var red = MatLib.Emissive(new Color(0.85f, 0.08f, 0.06f), new Color(0.35f, 0.02f, 0.01f));
                        MatLib.Box(t, "Box", new Vector3(0, 0, 0.03f), new Vector3(0.14f, 0.18f, 0.06f), red, false);
                        MatLib.Box(t, "Button", new Vector3(0, 0, 0.065f), new Vector3(0.08f, 0.08f, 0.02f), MatLib.Lit(Color.white), false);
                    }
                    MatLib.Model("FireBell", t, new Vector3(0, 0.55f, 0));
                    MatLib.Label3D(t, f.label ?? "BÁO CHÁY", new Vector3(0, 0.16f, 0.04f), 0.05f, new Color(1f, 0.3f, 0.25f));
                    var col = go.AddComponent<BoxCollider>();
                    col.isTrigger = true; col.center = new Vector3(0, 0, 0.1f); col.size = new Vector3(0.35f, 0.4f, 0.25f);
                    go.AddComponent<Interactable>().kind = InteractKind.Alarm;
                    go.GetComponent<Interactable>().id = f.id;
                    break;
                }
                case FixtureKind.Extinguisher:
                {
                    bool powder = f.label != null && f.label.Contains("BỘT");
                    if (MatLib.Model(powder ? "Extinguisher_Powder" : "Extinguisher_CO2", t, new Vector3(0, 0.02f, 0.14f)) == null)
                    {
                        BuildExtinguisherModel(t, new Vector3(0, 0.3f, 0.12f));
                        MatLib.Box(t, "Bracket", new Vector3(0, 0.55f, 0.02f), new Vector3(0.2f, 0.05f, 0.04f), MatLib.Lit(new Color(0.3f, 0.3f, 0.3f)), false);
                    }
                    MatLib.Label3D(t, f.label ?? "BÌNH CHỮA CHÁY", new Vector3(0, 1.05f, 0.05f), 0.045f, new Color(1f, 0.35f, 0.3f));
                    var col = go.AddComponent<BoxCollider>();
                    col.isTrigger = true; col.center = new Vector3(0, 0.55f, 0.15f); col.size = new Vector3(0.4f, 0.8f, 0.4f);
                    var it = go.AddComponent<Interactable>(); it.kind = InteractKind.Extinguisher; it.id = f.id;
                    break;
                }
                case FixtureKind.Elevator:
                {
                    var steel = MatLib.Lit(new Color(0.72f, 0.74f, 0.77f), 0.8f, 0.9f);
                    MatLib.Box(t, "Frame", new Vector3(0, 1.15f, 0.02f), new Vector3(1.9f, 2.3f, 0.04f), MatLib.Lit(new Color(0.4f, 0.42f, 0.45f)), false);
                    MatLib.Box(t, "DoorL", new Vector3(-0.45f, 1.05f, 0.05f), new Vector3(0.88f, 2.1f, 0.04f), steel, false);
                    MatLib.Box(t, "DoorR", new Vector3(0.45f, 1.05f, 0.05f), new Vector3(0.88f, 2.1f, 0.04f), steel, false);
                    MatLib.Box(t, "Call", new Vector3(1.15f, 1.1f, 0.04f), new Vector3(0.1f, 0.18f, 0.03f), MatLib.Emissive(Color.gray, new Color(0.3f, 0.5f, 1f) * 0.5f), false);
                    MatLib.Label3D(t, f.label ?? "THANG MÁY", new Vector3(0, 2.45f, 0.05f), 0.08f, Color.white);
                    MatLib.Label3D(t, "KHÔNG SỬ DỤNG THANG MÁY KHI CÓ CHÁY", new Vector3(1.15f, 1.45f, 0.05f), 0.025f, new Color(1f, 0.8f, 0.3f));
                    var col = go.AddComponent<BoxCollider>();
                    col.center = new Vector3(0.3f, 1.1f, 0.1f); col.size = new Vector3(2.2f, 2.2f, 0.2f);
                    var it = go.AddComponent<Interactable>(); it.kind = InteractKind.Elevator; it.id = f.id;
                    break;
                }
                case FixtureKind.Window:
                {
                    MatLib.Box(t, "Frame", new Vector3(0, 0, 0.01f), new Vector3(1.7f, 1.3f, 0.03f), MatLib.Lit(new Color(0.9f, 0.9f, 0.9f)), false);
                    MatLib.Box(t, "Glass", new Vector3(0, 0, 0.03f), new Vector3(1.55f, 1.15f, 0.02f), MatLib.Emissive(new Color(0.55f, 0.75f, 0.95f), new Color(0.35f, 0.5f, 0.7f)), false);
                    MatLib.Box(t, "Mullion", new Vector3(0, 0, 0.045f), new Vector3(0.04f, 1.15f, 0.02f), MatLib.Lit(new Color(0.9f, 0.9f, 0.9f)), false);
                    var col = go.AddComponent<BoxCollider>();
                    col.isTrigger = true; col.center = new Vector3(0, 0, 0.2f); col.size = new Vector3(1.7f, 1.3f, 0.4f);
                    var it = go.AddComponent<Interactable>(); it.kind = InteractKind.Window; it.id = f.id;
                    break;
                }
                case FixtureKind.ExitSign:
                {
                    var m = MatLib.Emissive(ExitGreen, ExitGreen * 1.6f);
                    exitSignMats.Add(m);
                    float w = Mathf.Max(0.5f, (f.label ?? "EXIT").Length * 0.06f);
                    MatLib.Box(t, "Sign", new Vector3(0, 0, 0.03f), new Vector3(w, 0.2f, 0.05f), m, false);
                    MatLib.Label3D(t, f.label ?? "EXIT", new Vector3(0, 0, 0.06f), 0.07f, Color.white);
                    var l = new GameObject("Glow").AddComponent<Light>();
                    l.transform.SetParent(t, false);
                    l.transform.localPosition = new Vector3(0, -0.2f, 0.3f);
                    l.type = LightType.Point; l.range = 3f; l.intensity = 0.8f; l.color = ExitGreen; l.shadows = LightShadows.None;
                    exitLights.Add(l);
                    break;
                }
                case FixtureKind.Desk:
                {
                    var wood = MatLib.Lit(new Color(0.72f, 0.56f, 0.38f), 0.3f);
                    var metal = MatLib.Lit(new Color(0.25f, 0.25f, 0.27f), 0.5f, 0.6f);
                    bool bench = f.id.StartsWith("bench");
                    var size = bench ? new Vector3(2.4f, 0.06f, 0.9f) : new Vector3(1.1f, 0.05f, 0.55f);
                    MatLib.Box(t, "Top", new Vector3(0, 0.75f, 0), size, bench ? MatLib.Lit(new Color(0.2f, 0.22f, 0.24f), 0.6f) : wood, false);
                    foreach (var sx in new[] { -1f, 1f })
                        foreach (var sz in new[] { -1f, 1f })
                            MatLib.Box(t, "Leg", new Vector3(sx * (size.x / 2f - 0.05f), 0.37f, sz * (size.z / 2f - 0.05f)), new Vector3(0.04f, 0.74f, 0.04f), metal, false);
                    if (!bench)
                    {
                        MatLib.Box(t, "Seat", new Vector3(0, 0.45f, 0.55f), new Vector3(0.42f, 0.04f, 0.4f), wood, false);
                        MatLib.Box(t, "Back", new Vector3(0, 0.7f, 0.74f), new Vector3(0.42f, 0.4f, 0.03f), wood, false);
                    }
                    var col = go.AddComponent<BoxCollider>();
                    col.center = new Vector3(0, 0.4f, bench ? 0f : 0.15f);
                    col.size = new Vector3(size.x, 0.8f, bench ? size.z : size.z + 0.4f);
                    break;
                }
                case FixtureKind.PowerOutlet:
                    MatLib.Box(t, "Outlet", new Vector3(0, 0, 0.01f), new Vector3(0.12f, 0.08f, 0.03f), MatLib.Lit(new Color(0.95f, 0.95f, 0.92f)), false);
                    MatLib.Box(t, "Strip", new Vector3(0.1f, -0.27f, 0.25f), new Vector3(0.35f, 0.04f, 0.07f), MatLib.Lit(new Color(0.9f, 0.9f, 0.9f)), false);
                    MatLib.Box(t, "Cable", new Vector3(0, -0.14f, 0.12f), new Vector3(0.015f, 0.25f, 0.015f), MatLib.Lit(new Color(0.1f, 0.1f, 0.1f)), false);
                    MatLib.Box(t, "Charger", new Vector3(0.2f, -0.24f, 0.25f), new Vector3(0.08f, 0.05f, 0.1f), MatLib.Lit(new Color(0.15f, 0.15f, 0.15f)), false);
                    break;
                case FixtureKind.Screen:
                {
                    var m = MatLib.Emissive(new Color(0.05f, 0.05f, 0.07f), new Color(0.1f, 0.18f, 0.35f));
                    screenMats.Add(m);
                    MatLib.Box(t, "Screen", new Vector3(0, 0, 0.03f), new Vector3(1.8f, 1.05f, 0.05f), m, false);
                    break;
                }
                case FixtureKind.HoseCabinet:
                    if (MatLib.Model("HoseCabinet", t, Vector3.zero) == null)
                        MatLib.Box(t, "Cabinet", new Vector3(0, 0.8f, 0.11f), new Vector3(0.7f, 0.95f, 0.22f), MatLib.Lit(new Color(0.62f, 0.05f, 0.03f)), false);
                    var hc = go.AddComponent<BoxCollider>();
                    hc.center = new Vector3(0, 0.7f, 0.12f); hc.size = new Vector3(0.72f, 1.4f, 0.25f);
                    break;
                case FixtureKind.FireBlanket:
                    if (MatLib.Model("FireBlanket", t, Vector3.zero) == null)
                        MatLib.Box(t, "Blanket", new Vector3(0, 0, 0.03f), new Vector3(0.28f, 0.34f, 0.06f), MatLib.Lit(new Color(0.55f, 0.05f, 0.04f)), false);
                    break;
                case FixtureKind.ElectricalPanel:
                    MatLib.Box(t, "Panel", new Vector3(0, 0, 0.08f), new Vector3(0.5f, 0.7f, 0.15f), MatLib.Lit(new Color(0.55f, 0.57f, 0.6f), 0.4f, 0.5f), false);
                    MatLib.Label3D(t, f.label ?? "TỦ ĐIỆN", new Vector3(0, 0.45f, 0.1f), 0.04f, new Color(1f, 0.85f, 0.2f));
                    break;
            }

            // TextMesh đọc được khi nhìn theo hướng +Z của nó; thiết bị quay mặt (+Z) vào phòng,
            // nên xoay chữ 180° để người đứng trong phòng đọc đúng chiều.
            foreach (var tm in go.GetComponentsInChildren<TextMesh>(true))
                if (tm.GetComponent<Billboard>() == null) tm.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        }

        public static GameObject BuildExtinguisherModel(Transform parent, Vector3 localPos)
        {
            var model = MatLib.Model("Extinguisher_CO2", parent, localPos + new Vector3(0, -0.25f, 0));
            if (model != null) return model;
            var g = new GameObject("Extinguisher");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            var red = MatLib.Lit(new Color(0.8f, 0.07f, 0.05f), 0.6f);
            var black = MatLib.Lit(new Color(0.08f, 0.08f, 0.08f), 0.4f);
            MatLib.Prim(PrimitiveType.Cylinder, g.transform, "Body", new Vector3(0, 0.25f, 0), new Vector3(0.15f, 0.25f, 0.15f), red);
            MatLib.Prim(PrimitiveType.Sphere, g.transform, "Top", new Vector3(0, 0.5f, 0), new Vector3(0.15f, 0.08f, 0.15f), red);
            MatLib.Prim(PrimitiveType.Cylinder, g.transform, "Valve", new Vector3(0, 0.57f, 0), new Vector3(0.04f, 0.04f, 0.04f), black);
            MatLib.Box(g.transform, "Handle", new Vector3(0.03f, 0.62f, 0), new Vector3(0.14f, 0.015f, 0.03f), black, false);
            MatLib.Box(g.transform, "Pin", new Vector3(-0.02f, 0.6f, 0.03f), new Vector3(0.01f, 0.03f, 0.04f), MatLib.Lit(new Color(0.9f, 0.8f, 0.2f), 0.8f, 1f), false);
            var hose = MatLib.Prim(PrimitiveType.Cylinder, g.transform, "Hose", new Vector3(0.09f, 0.4f, 0.03f), new Vector3(0.025f, 0.17f, 0.025f), black);
            hose.transform.localRotation = Quaternion.Euler(0, 0, -12f);
            return g;
        }

        // ---------------- Reality Mapping: nhãn nhận diện ----------------
        void BuildScanLabels()
        {
            scanRoot = new GameObject("RealityMapping");
            scanRoot.transform.SetParent(root, false);
            var cyan = new Color(0.3f, 0.95f, 1f);
            var list = new List<ScanLabel>(L.scanLabels);
            if (list.Count == 0)
            {
                foreach (var z in L.zones)
                {
                    string tag = z.isExit ? (z.id.Contains("stairs") ? "STAIRS" : "EXIT") : z.id.StartsWith("corr") ? "CORRIDOR" : z.id == "lab" ? "LAB" : "CLASSROOM";
                    list.Add(new ScanLabel { text = tag, pos = z.Center + Vector3.up * 2.3f });
                    if (!z.isExit) list.Add(new ScanLabel { text = "WALL", pos = new Vector3(z.area.center.x + 1.2f, 1.9f, z.area.yMax - 0.15f) });
                }
                foreach (var p in L.portals)
                    if (p.hasDoor) list.Add(new ScanLabel { text = "DOOR", pos = p.pos + Vector3.up * 2.45f });
                int i = 0;
                foreach (var f in L.fixtures)
                {
                    if (f.kind == FixtureKind.Desk && i++ % 4 == 0) list.Add(new ScanLabel { text = "DESK", pos = f.pos + Vector3.up * 1.1f });
                    if (f.kind == FixtureKind.Window) list.Add(new ScanLabel { text = "WINDOW", pos = f.pos + Vector3.up * 0.85f });
                }
            }
            foreach (var s in list)
            {
                var tm = MatLib.Label3D(scanRoot.transform, "[ " + s.text + " ]", s.pos, 0.09f, cyan, true);
                tm.gameObject.name = "Scan_" + s.text;
            }
        }

        public void SetScanVisible(bool v)
        {
            if (scanRoot != null) scanRoot.SetActive(v);
        }
    }
}
