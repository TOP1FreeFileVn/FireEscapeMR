using System;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace FireEscape
{
    /// <summary>
    /// Reality Mapping: chuyển căn phòng thật (đã quét bằng Space Setup, đọc qua MR Utility Kit)
    /// thành FacilityLayout để lửa, khói, lối thoát và NPC được đặt ngay trong không gian quen thuộc của người dùng.
    /// Toạ độ y trong layout tính từ mặt sàn.
    /// </summary>
    public static class MRRoomMapper
    {
        struct Door { public Vector3 pos, outward, along; public float width, height; public bool real; }

        public static FacilityLayout Build(MRUKRoom room, Transform head)
        {
            var L = new FacilityLayout { name = "Phòng thật đã quét", isRealRoom = true };
            var floorAnchor = room.FloorAnchors != null && room.FloorAnchors.Count > 0 ? room.FloorAnchors[0] : null;
            float floorY = floorAnchor != null ? floorAnchor.transform.position.y : 0f;
            L.floorY = floorY;
            Vector3 Rel(Vector3 w) => new Vector3(w.x, w.y - floorY, w.z);

            // ---- biên của phòng từ các bức tường ----
            float xmin = float.MaxValue, zmin = float.MaxValue, xmax = float.MinValue, zmax = float.MinValue, height = 2.6f;
            var walls = new List<MRUKAnchor>();
            foreach (var w in room.WallAnchors)
            {
                if (w == null || !w.PlaneRect.HasValue) continue;
                walls.Add(w);
                var r = w.PlaneRect.Value;
                foreach (var x in new[] { r.xMin, r.xMax })
                {
                    var p = w.transform.TransformPoint(new Vector3(x, 0, 0));
                    xmin = Mathf.Min(xmin, p.x); xmax = Mathf.Max(xmax, p.x);
                    zmin = Mathf.Min(zmin, p.z); zmax = Mathf.Max(zmax, p.z);
                }
                height = Mathf.Max(height, r.height);
            }
            if (walls.Count == 0)
            {
                var h = head.position;
                xmin = h.x - 2f; xmax = h.x + 2f; zmin = h.z - 2f; zmax = h.z + 2f;
            }
            var roomZone = new Zone { id = "room", name = "Phòng của bạn", area = Rect.MinMaxRect(xmin, zmin, xmax, zmax), ceiling = Mathf.Clamp(height, 2.2f, 4f) };
            var center = roomZone.Center;

            Vector3 Outward(Vector3 pos, Vector3 normal)
            {
                normal.y = 0;
                if (normal.sqrMagnitude < 0.001f) normal = pos - center;
                normal.y = 0; normal.Normalize();
                var toPos = pos - center; toPos.y = 0;
                return Vector3.Dot(normal, toPos) < 0f ? -normal : normal;
            }

            // ---- cửa ra vào thật ----
            var doors = new List<Door>();
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.HasAnyLabel(MRUKAnchor.SceneLabels.DOOR_FRAME) || !a.PlaneRect.HasValue) continue;
                var p = Rel(a.GetAnchorCenter()); p.y = 0;
                var along = a.transform.right; along.y = 0; along.Normalize();
                doors.Add(new Door { pos = p, outward = Outward(p, a.transform.forward), along = along, width = a.PlaneRect.Value.width, height = a.PlaneRect.Value.height, real = true });
            }

            Door WallDoor(MRUKAnchor w)
            {
                var p = Rel(w.GetAnchorCenter()); p.y = 0;
                var along = w.transform.right; along.y = 0; along.Normalize();
                return new Door { pos = p, outward = Outward(p, w.transform.forward), along = along, width = 0.9f, height = 2.05f, real = false };
            }

            var solver = new MRPlacementSolver(room, floorY, new Vector3(center.x, floorY, center.z));
            var headRel0 = Rel(head.position);

            // Lối thoát ảo: một khoảng tường trống ~1 m, không đè cửa sổ, không bị đồ đạc chắn.
            Door VirtualDoor(string what, Func<MRPlacementSolver.Spot, float> score, MRUKAnchor fallbackWall)
            {
                if (solver.PlaceOnWall(what, 1.05f, 0.5f, 1.05f, 1.2f, 1.2f, score, out var s))
                    return new Door { pos = new Vector3(s.pos.x, 0, s.pos.z), outward = -s.normal, along = s.along, width = 0.9f, height = 2.05f, real = false };
                return WallDoor(fallbackWall);
            }

            if (doors.Count == 0 && walls.Count > 0)
                doors.Add(VirtualDoor("Lối thoát ảo chính", s => -Mathf.Abs(FacilityLayout.Flat(Rel(s.pos), headRel0) - 2.5f),
                                      walls.OrderByDescending(w => w.PlaneRect.Value.width).First()));
            if (doors.Count < 2 && walls.Count > 0)
            {
                var first = doors[0].pos;
                // nguyên tắc "luôn có hai lối thoát": lối thứ hai càng xa lối thứ nhất càng tốt
                doors.Add(VirtualDoor("Lối thoát ảo thứ hai", s => FacilityLayout.Flat(Rel(s.pos), first),
                                      walls.OrderByDescending(w => FacilityLayout.Flat(Rel(w.GetAnchorCenter()), first)).First()));
            }
            if (doors.Count == 0)
            {
                doors.Add(new Door { pos = new Vector3(xmin, 0, center.z), outward = Vector3.left, along = Vector3.forward, width = 0.9f, height = 2.05f });
                doors.Add(new Door { pos = new Vector3(xmax, 0, center.z), outward = Vector3.right, along = Vector3.forward, width = 0.9f, height = 2.05f });
            }
            var headRel = Rel(head.position);
            doors = doors.OrderBy(d => FacilityLayout.Flat(d.pos, headRel)).ToList();

            // ---- lối thoát (thêm trước phòng để ZoneAt ưu tiên) ----
            for (int i = 0; i < doors.Count && i < 3; i++)
            {
                var d = doors[i];
                string id = "exit" + (char)('A' + i);
                var c = d.pos + d.outward * 0.35f;
                L.zones.Add(new Zone
                {
                    id = id, isExit = true,
                    name = d.real ? (i == 0 ? "Cửa ra vào chính" : $"Cửa ra vào {i + 1}") : "Lối thoát khẩn cấp (ảo)",
                    area = new Rect(c.x - 0.5f, c.z - 0.5f, 1f, 1f), isVirtual = !d.real
                });
                L.portals.Add(new Portal { id = "door" + i, name = d.real ? "Cửa ra vào" : "Cửa thoát hiểm ảo", a = "room", b = id, pos = d.pos, width = d.width });
                L.fixtures.Add(new Fixture { id = "sign" + i, kind = FixtureKind.ExitSign, zone = "room", pos = d.pos + Vector3.up * Mathf.Min(d.height + 0.15f, roomZone.ceiling - 0.2f) - d.outward * 0.05f, facing = -d.outward, label = d.real ? "EXIT" : "EXIT (ảo)" });
            }
            L.zones.Add(roomZone);

            // ---- phòng bên cạnh (ảo) phía sau bức tường xa lối thoát nhất ----
            if (walls.Count > 0)
            {
                var far = walls.OrderByDescending(w => doors.Min(d => FacilityLayout.Flat(Rel(w.GetAnchorCenter()), d.pos))).First();
                var wc = Rel(far.GetAnchorCenter()); wc.y = 0;
                var n = Outward(wc, far.transform.forward);
                var nc = wc + n * 1.8f;
                L.zones.Add(new Zone { id = "neighbor", name = "Phòng bên cạnh", area = new Rect(nc.x - 1.5f, nc.z - 1.5f, 3f, 3f), isVirtual = true, ceiling = roomZone.ceiling });
                L.portals.Add(new Portal { id = "vent", name = "Khe tường", a = "neighbor", b = "room", pos = wc, walkable = false });
                L.neighborZone = "neighbor";
                L.neighborOrigin = "neighbor";
            }

            // ---- bố trí vật dụng bằng bộ giải ràng buộc (MRPlacementSolver) ----
            var d0 = doors[0];
            var d1 = doors.Count > 1 ? doors[1] : d0;
            Vector3 W(Vector3 rel) => new Vector3(rel.x, rel.y + floorY, rel.z);
            foreach (var d in doors) solver.Reserve(W(d.pos + Vector3.up * 1f));

            // 1) nguồn cháy điện: màn hình/đèn → bàn/tủ → ổ điện trên tường xa cửa
            var originW = solver.ElectricalOrigin(W(d0.pos), out _);
            var origin = Rel(originW); origin.y = 0.3f;
            var toC = center - origin; toC.y = 0;
            L.fixtures.Add(new Fixture { id = "origin", kind = FixtureKind.PowerOutlet, zone = "room", pos = origin, facing = toC.normalized });
            L.originFixture = "origin";

            // 2) nút báo cháy: tường, cao 1,3 m, gần cửa chính (~0,8 m) — thấy được trên đường thoát
            if (solver.PlaceOnWall("Nút báo cháy", 1.3f, 0.12f, 0.15f, 0.5f, 0.5f,
                    s => -Mathf.Abs(FacilityLayout.Flat(Rel(s.pos), d0.pos) - 0.8f), out var al))
                L.fixtures.Add(new Fixture { id = "alarm", kind = FixtureKind.AlarmStation, zone = "room", pos = Rel(al.pos), facing = al.normal, label = "BÁO CHÁY" });
            else
                L.fixtures.Add(new Fixture { id = "alarm", kind = FixtureKind.AlarmStation, zone = "room", pos = d0.pos + d0.along * (d0.width / 2f + 0.45f) - d0.outward * 0.04f + Vector3.up * 1.3f, facing = -d0.outward, label = "BÁO CHÁY" });

            // 3) bình chữa cháy: sát tường, 0,8–1,5 m từ lối thoát thứ hai, phía trước trống 0,7 m, cách nguồn cháy ≥ 2 m
            if (solver.PlaceOnWall("Bình chữa cháy", 0.05f, 0.2f, 0.6f, 0.7f, 1.0f,
                    s => -Mathf.Abs(FacilityLayout.Flat(Rel(s.pos), d1.pos) - 1.1f) + Mathf.Min(2f, FacilityLayout.Flat(Rel(s.pos), origin)) * 0.5f, out var ex))
                L.fixtures.Add(new Fixture { id = "ext", kind = FixtureKind.Extinguisher, zone = "room", pos = new Vector3(ex.pos.x, 0f, ex.pos.z), facing = ex.normal, label = "BÌNH CO2" });
            else
            {
                float side = doors.Count > 1 ? 1f : -1f;
                L.fixtures.Add(new Fixture { id = "ext", kind = FixtureKind.Extinguisher, zone = "room", pos = d1.pos + d1.along * side * (d1.width / 2f + 0.5f) - d1.outward * 0.12f, facing = -d1.outward, label = "BÌNH CO2" });
            }

            // 4) chăn chống cháy: tường, cao 1,4 m, cách nguồn cháy ~2 m (đủ gần để dùng, đủ xa để lấy an toàn)
            if (solver.PlaceOnWall("Chăn chống cháy", 1.4f, 0.15f, 0.2f, 0.5f, 0.8f,
                    s => -Mathf.Abs(FacilityLayout.Flat(Rel(s.pos), origin) - 2f), out var bl))
                L.fixtures.Add(new Fixture { id = "blanket", kind = FixtureKind.FireBlanket, zone = "room", pos = Rel(bl.pos), facing = bl.normal });

            foreach (var a in room.Anchors)
            {
                if (a == null || !a.HasAnyLabel(MRUKAnchor.SceneLabels.WINDOW_FRAME)) continue;
                var p = Rel(a.GetAnchorCenter());
                L.fixtures.Add(new Fixture { id = "window" + L.fixtures.Count, kind = FixtureKind.Window, zone = "room", pos = p - Outward(p, a.transform.forward) * 0.03f, facing = -Outward(p, a.transform.forward) });
            }

            // 5) nguồn cháy phụ "gần lối ra": điểm sàn trống ngay trong cửa chính
            var alt = d0.pos - d0.outward * 0.8f - d0.along * (d0.width / 2f + 0.3f);
            foreach (var off in new[] { 0f, 0.4f, -0.4f, 0.8f, -0.8f })
            {
                var cand = d0.pos - d0.outward * 0.8f + d0.along * off;
                if (solver.FreeFloor(W(cand), 0.2f)) { alt = cand; break; }
            }
            alt.y = 0.3f;
            L.fixtures.Add(new Fixture { id = "origin_door", kind = FixtureKind.PowerOutlet, zone = "room", pos = alt, facing = d0.outward });
            L.altOriginFixture = "origin_door";

            // 6) chỗ trống trên sàn cho NPC (không đè đồ đạc), 1,2–5 m từ người chơi
            foreach (var p in solver.FreeFloorSpots(W(headRel0), 6, 1.2f, 5f)) L.freeSpots.Add(Rel(p));

            L.placementReport.AddRange(solver.report);
            foreach (var line in solver.report) Debug.Log("[FireEscape MR] " + line);

            // ---- nhãn Reality Mapping ----
            var counts = new Dictionary<string, int>();
            foreach (var a in room.Anchors)
            {
                if (a == null) continue;
                string label = a.Label.ToString().Replace("_FACE", "").Replace("_FRAME", "").Replace("TABLE", "DESK");
                if (label.Contains("FLOOR") || label.Contains("CEILING") || label.Contains("GLOBAL_MESH")) continue;
                counts[label] = counts.TryGetValue(label, out int c) ? c + 1 : 1;
                L.scanLabels.Add(new ScanLabel { text = label, pos = Rel(a.GetAnchorCenter()) + Vector3.up * 0.25f });
            }
            foreach (var kv in counts) L.mappedElements.Add($"{kv.Key} ×{kv.Value}");
            if (doors.Any(d => !d.real)) L.mappedElements.Add("Lối thoát ảo ×" + doors.Count(d => !d.real));

            L.startPos = new Vector3(headRel.x, 0, headRel.z);
            L.startYaw = head.eulerAngles.y;
            L.startZone = "room";
            L.primaryExit = "exitA";
            L.secondaryExit = doors.Count > 1 ? "exitB" : "exitA";
            return L;
        }
    }
}
