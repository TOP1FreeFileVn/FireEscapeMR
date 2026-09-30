using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace FireEscape
{
    /// <summary>
    /// Bố trí vật dụng ảo trong phòng thật (MRUK) theo ràng buộc + điểm số, thay vì toạ độ cố định:
    ///   1. Sinh ứng viên: lấy mẫu dọc mọi bức tường (ở độ cao yêu cầu) hoặc lưới trên sàn.
    ///   2. Loại ứng viên vi phạm: đè cửa ra vào/cửa sổ, đồ đạc chắn phía trước, ngoài phòng,
    ///      bị che khuất khi nhìn từ giữa phòng, quá gần vật đã đặt.
    ///   3. Chấm điểm theo quy tắc của từng vật (khoảng cách tới cửa, độ cao, tách xa lối thoát...) và chọn điểm cao nhất.
    /// Mọi toạ độ trả về là toạ độ thế giới.
    /// </summary>
    public class MRPlacementSolver
    {
        public struct Spot
        {
            public Vector3 pos, normal, along;
            public MRUKAnchor wall;
            public float score;
        }

        readonly MRUKRoom room;
        readonly float floorY;
        readonly Vector3 center;
        readonly List<MRUKAnchor> walls = new List<MRUKAnchor>();
        readonly List<MRUKAnchor> openings = new List<MRUKAnchor>();
        readonly List<Vector3> placed = new List<Vector3>();
        public readonly List<string> report = new List<string>();

        public MRPlacementSolver(MRUKRoom room, float floorY, Vector3 centerWorld)
        {
            this.room = room;
            this.floorY = floorY;
            center = centerWorld;
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.PlaneRect.HasValue) continue;
                if (a.HasAnyLabel(MRUKAnchor.SceneLabels.WALL_FACE)) walls.Add(a);
                if (a.HasAnyLabel(MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME)) openings.Add(a);
            }
        }

        public void Reserve(Vector3 worldPos) => placed.Add(worldPos);

        Vector3 Inward(MRUKAnchor wall, Vector3 p)
        {
            var n = wall.transform.forward; n.y = 0; n.Normalize();
            var toC = center - p; toC.y = 0;
            return Vector3.Dot(n, toC) < 0f ? -n : n;
        }

        // ---------------- sinh ứng viên ----------------
        public IEnumerable<Spot> WallCandidates(float heightAboveFloor, float halfWidth, float step = 0.2f)
        {
            foreach (var w in walls)
            {
                var r = w.PlaneRect.Value;
                var along = w.transform.right; along.y = 0; along.Normalize();
                for (float x = r.xMin + halfWidth + 0.08f; x <= r.xMax - halfWidth - 0.08f; x += step)
                {
                    var p = w.transform.TransformPoint(new Vector3(x, 0f, 0f));
                    p.y = floorY + heightAboveFloor;
                    yield return new Spot { pos = p, normal = Inward(w, p), along = along, wall = w };
                }
            }
        }

        public IEnumerable<Vector3> FloorCandidates(float step = 0.4f)
        {
            var b = room.GetRoomBounds();
            for (float x = b.min.x + 0.3f; x <= b.max.x - 0.3f; x += step)
                for (float z = b.min.z + 0.3f; z <= b.max.z - 0.3f; z += step)
                    yield return new Vector3(x, floorY, z);
        }

        // ---------------- ràng buộc ----------------
        bool OverlapsOpening(Vector3 p, float halfW, float halfH)
        {
            foreach (var o in openings)
            {
                var local = o.transform.InverseTransformPoint(p);
                if (Mathf.Abs(local.z) > 0.3f) continue;
                var r = o.PlaneRect.Value;
                if (local.x > r.xMin - halfW - 0.15f && local.x < r.xMax + halfW + 0.15f &&
                    local.y > r.yMin - halfH - 0.15f && local.y < r.yMax + halfH + 0.15f) return true;
            }
            return false;
        }

        bool FrontBlocked(Vector3 p, Vector3 normal, float depth)
        {
            if (!room.IsPositionInRoom(p + normal * 0.2f, false)) return true;
            for (float d = 0.15f; d <= depth + 0.01f; d += 0.2f)
            {
                var q = p + normal * d;
                if (room.IsPositionInSceneVolume(q, true, 0.05f)) return true;
                var knee = new Vector3(q.x, floorY + 0.5f, q.z);
                if (room.IsPositionInSceneVolume(knee, true, 0.05f)) return true;
            }
            return false;
        }

        bool VisibleFromCenter(Vector3 p)
        {
            var eye = new Vector3(center.x, floorY + 1.6f, center.z);
            var dir = p - eye;
            float dist = dir.magnitude;
            if (dist < 0.3f) return true;
            if (!room.Raycast(new Ray(eye, dir / dist), dist - 0.2f, out _, out MRUKAnchor hitAnchor)) return true;
            return hitAnchor == null || !hitAnchor.VolumeBounds.HasValue;
        }

        float Clearance(Vector3 p)
        {
            float m = float.MaxValue;
            foreach (var q in placed) m = Mathf.Min(m, Vector3.Distance(p, q));
            return m;
        }

        public bool FreeFloor(Vector3 p, float radius)
        {
            var body = p + Vector3.up * 0.9f;
            if (!room.IsPositionInRoom(body, false)) return false;
            if (room.IsPositionInSceneVolume(p + Vector3.up * 0.4f, true, radius)) return false;
            if (room.IsPositionInSceneVolume(body, true, radius)) return false;
            return Clearance(p) > radius * 2f;
        }

        // ---------------- chọn vị trí ----------------
        public bool PlaceOnWall(string what, float height, float halfW, float halfH, float depthClear, float minSpacing,
                                Func<Spot, float> score, out Spot best)
        {
            best = default;
            float bestScore = float.NegativeInfinity;
            int total = 0, valid = 0;
            foreach (var s in WallCandidates(height, halfW))
            {
                total++;
                if (OverlapsOpening(s.pos, halfW, halfH)) continue;
                if (FrontBlocked(s.pos, s.normal, depthClear)) continue;
                if (!VisibleFromCenter(s.pos + s.normal * 0.1f)) continue;
                if (Clearance(s.pos) < minSpacing) continue;
                valid++;
                var sc = score(s);
                if (sc > bestScore) { bestScore = sc; best = s; best.score = sc; }
            }
            if (valid == 0)
            {
                report.Add($"{what}: không có vị trí hợp lệ ({total} ứng viên)");
                return false;
            }
            placed.Add(best.pos);
            report.Add($"{what}: chọn 1/{valid} vị trí hợp lệ ({total} ứng viên)");
            return true;
        }

        public List<Vector3> FreeFloorSpots(Vector3 near, int max, float minDist, float maxDist)
        {
            var list = FloorCandidates()
                .Where(p => FreeFloor(p, 0.3f))
                .Select(p => (p, d: Vector3.Distance(p, near)))
                .Where(t => t.d >= minDist && t.d <= maxDist)
                .OrderBy(t => Mathf.Abs(t.d - (minDist + maxDist) * 0.5f))
                .Select(t => t.p)
                .ToList();
            var chosen = new List<Vector3>();
            foreach (var p in list)
            {
                if (chosen.Any(q => Vector3.Distance(p, q) < 0.9f)) continue;
                chosen.Add(p);
                if (chosen.Count >= max) break;
            }
            report.Add($"Vị trí trống trên sàn cho NPC: {chosen.Count}");
            return chosen;
        }

        /// <summary>Nguồn cháy điện: ưu tiên màn hình/đèn, rồi bàn/tủ; đặt ở mép đồ vật hướng vào phòng, cách cửa chính ≥ 1,5 m.</summary>
        public Vector3 ElectricalOrigin(Vector3 mainDoor, out string source)
        {
            var labels = new[]
            {
                MRUKAnchor.SceneLabels.SCREEN | MRUKAnchor.SceneLabels.LAMP,
                MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.BED
            };
            foreach (var lab in labels)
            {
                var candidates = room.Anchors
                    .Where(a => a != null && a.HasAnyLabel(lab))
                    .Select(a => (a, edge: EdgeTowardRoom(a)))
                    .Where(t => FlatDist(t.edge, mainDoor) >= 1.5f && room.IsPositionInRoom(t.edge + Vector3.up * 0.5f, false))
                    .OrderByDescending(t => FlatDist(t.edge, mainDoor))
                    .ToList();
                if (candidates.Count > 0)
                {
                    var c = candidates[0];
                    source = c.a.Label.ToString();
                    placed.Add(c.edge);
                    report.Add($"Nguồn cháy điện: cạnh {source}, cách cửa chính {FlatDist(c.edge, mainDoor):0.0} m");
                    return c.edge;
                }
            }
            // không có đồ điện/đồ vật: ổ điện trên tường xa cửa nhất
            PlaceOnWall("Nguồn cháy (ổ điện)", 0.3f, 0.1f, 0.1f, 0.4f, 0.5f, s => FlatDist(s.pos, mainDoor), out var spot);
            source = "WALL_OUTLET";
            return spot.pos + spot.normal * 0.2f;
        }

        Vector3 EdgeTowardRoom(MRUKAnchor a)
        {
            var c = a.GetAnchorCenter();
            var toC = center - c; toC.y = 0;
            float reach = 0.3f;
            if (a.VolumeBounds.HasValue)
            {
                var s = a.VolumeBounds.Value.size;
                reach = Mathf.Max(s.x, s.y) * 0.5f + 0.15f;
            }
            var p = c + (toC.sqrMagnitude > 0.001f ? toC.normalized : Vector3.forward) * reach;
            p.y = floorY + 0.25f;
            return p;
        }

        static float FlatDist(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
    }
}
