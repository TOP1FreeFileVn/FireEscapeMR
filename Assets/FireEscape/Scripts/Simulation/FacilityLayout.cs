using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireEscape
{
    /// <summary>Một khu vực của không gian huấn luyện (phòng, đoạn hành lang, lối thoát).</summary>
    public class Zone
    {
        public string id, name;
        public Rect area;                 // x = trục X thế giới, y = trục Z thế giới
        public float ceiling = 3f;
        public bool isExit;
        public bool isVirtual;            // không có hình học thật (vùng ảo trong MR)
        public string servesExit;         // hành lang dẫn tới lối thoát nào

        public Vector3 Center => new Vector3(area.center.x, 0f, area.center.y);
        public bool Contains(Vector3 p) => area.Contains(new Vector2(p.x, p.z));

        public float DistanceTo(Vector3 p)
        {
            float dx = Mathf.Max(area.xMin - p.x, 0f, p.x - area.xMax);
            float dz = Mathf.Max(area.yMin - p.z, 0f, p.z - area.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }

    /// <summary>Chỗ nối hai khu vực: cửa ra vào, lối đi mở, hoặc khe thông gió (chỉ khói đi qua).</summary>
    public class Portal
    {
        public string id, name, a, b;
        public Vector3 pos;
        public float width = 1f;
        public bool hasDoor;
        public bool doorStartsOpen = true;
        public bool walkable = true;

        public bool Touches(string zone) => a == zone || b == zone;
        public string Other(string zone) => zone == a ? b : a;
    }

    public class Fixture
    {
        public string id, zone, label;
        public FixtureKind kind;
        public Vector3 pos;
        public Vector3 facing = Vector3.forward;
    }

    public class ScanLabel
    {
        public string text;
        public Vector3 pos;
    }

    public class FacilityLayout
    {
        public readonly List<ScanLabel> scanLabels = new List<ScanLabel>();
        public readonly List<Vector3> freeSpots = new List<Vector3>();      // chỗ trống trên sàn (phòng thật) để đặt NPC
        public readonly List<string> placementReport = new List<string>();
        public string name;
        public bool isRealRoom;
        public float floorY;
        public readonly List<Zone> zones = new List<Zone>();
        public readonly List<Portal> portals = new List<Portal>();
        public readonly List<Fixture> fixtures = new List<Fixture>();
        public readonly List<string> mappedElements = new List<string>();

        public Vector3 startPos;
        public float startYaw;
        public string startZone, neighborZone, hubZone;
        public string primaryExit, secondaryExit;   // primary = lối quen thuộc
        public string originFixture, altOriginFixture, neighborOrigin;

        public Zone GetZone(string id) { foreach (var z in zones) if (z.id == id) return z; return null; }
        public Portal GetPortal(string id) { foreach (var p in portals) if (p.id == id) return p; return null; }
        public Fixture GetFixture(string id) { foreach (var f in fixtures) if (f.id == id) return f; return null; }

        public Zone ZoneAt(Vector3 p)
        {
            foreach (var z in zones) if (z.Contains(p)) return z;
            Zone best = null; float bd = float.MaxValue;
            foreach (var z in zones)
            {
                if (z.isExit) continue;
                float d = z.DistanceTo(p);
                if (d < bd) { bd = d; best = z; }
            }
            return best;
        }

        public IEnumerable<Zone> Exits { get { foreach (var z in zones) if (z.isExit) yield return z; } }

        public Portal ExitPortal(string exitZone)
        {
            foreach (var p in portals) if (p.walkable && p.Touches(exitZone)) return p;
            return null;
        }

        /// <summary>Khu vực tiếp cận của lối thoát (hành lang dẫn tới nó), dùng để phát hiện người chơi đã "chọn tuyến".</summary>
        public string ApproachOf(string exitZone)
        {
            foreach (var z in zones) if (!z.isExit && z.servesExit == exitZone) return z.id;
            var p = ExitPortal(exitZone);
            return p != null ? p.Other(exitZone) : exitZone;
        }

        public string ExitServedBy(string zoneId)
        {
            var z = GetZone(zoneId);
            if (z == null) return null;
            if (z.isExit) return z.id;
            return z.servesExit;
        }

        public static float Flat(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Dijkstra trên đồ thị cửa. Trả về danh sách waypoint (không gồm điểm xuất phát).</summary>
        public List<Vector3> FindPath(Vector3 from, string targetZone, Func<Portal, bool> passable, out float length)
        {
            length = float.PositiveInfinity;
            var start = ZoneAt(from);
            var target = GetZone(targetZone);
            if (start == null || target == null) return null;
            Vector3 goal = target.Center;
            if (start == target)
            {
                length = Flat(from, goal);
                return new List<Vector3> { goal };
            }

            var ps = new List<Portal>();
            foreach (var p in portals)
                if (p.walkable && (passable == null || passable(p))) ps.Add(p);

            int n = ps.Count;
            var dist = new float[n];
            var prev = new int[n];
            var done = new bool[n];
            for (int i = 0; i < n; i++)
            {
                dist[i] = ps[i].Touches(start.id) ? Flat(from, ps[i].pos) : float.PositiveInfinity;
                prev[i] = -1;
            }

            for (int iter = 0; iter < n; iter++)
            {
                int u = -1; float bu = float.PositiveInfinity;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < bu) { bu = dist[i]; u = i; }
                if (u < 0) break;
                done[u] = true;
                for (int v = 0; v < n; v++)
                {
                    if (done[v] || !Shares(ps[u], ps[v])) continue;
                    float nd = dist[u] + Flat(ps[u].pos, ps[v].pos);
                    if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
                }
            }

            int bi = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (!ps[i].Touches(target.id) || float.IsInfinity(dist[i])) continue;
                float c = dist[i] + Flat(ps[i].pos, goal);
                if (c < best) { best = c; bi = i; }
            }
            if (bi < 0) return null;

            var path = new List<Vector3> { goal };
            for (int i = bi; i >= 0; i = prev[i]) path.Insert(0, ps[i].pos);
            length = best;
            return path;
        }

        static bool Shares(Portal p, Portal q) =>
            p.a == q.a || p.a == q.b || p.b == q.a || p.b == q.b;

        public Bounds WorldBounds()
        {
            var b = new Bounds();
            bool first = true;
            foreach (var z in zones)
            {
                var zb = new Bounds(z.Center, new Vector3(z.area.width, 0.1f, z.area.height));
                if (first) { b = zb; first = false; } else b.Encapsulate(zb);
            }
            return b;
        }
    }
}
