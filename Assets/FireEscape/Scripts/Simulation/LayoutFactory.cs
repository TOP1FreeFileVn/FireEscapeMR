using UnityEngine;

namespace FireEscape
{
    public static class LayoutFactory
    {
        /// <summary>
        /// Không gian mặc định khi chưa quét phòng thật: tầng 2 của một dãy lớp học.
        ///
        ///   z=6  +---------------+---------------+
        ///        | P.thí nghiệm  |  Phòng học    |  (cửa sổ phía bắc)
        ///        |     203       |     204       |
        ///   z=0  +---[cửa]-------+--[sau]--[trước]+
        ///   [Cầu thang A] | HL Tây | HL Giữa | HL Đông | [Lối B]
        ///   x=-29        -26       -5        5         26    29
        /// </summary>
        public static FacilityLayout CreateTrainingFloor()
        {
            var L = new FacilityLayout { name = "Tầng 2 — Dãy lớp học" };

            L.zones.Add(new Zone { id = "classroom", name = "Phòng học 204", area = new Rect(0, 0, 8, 6) });
            L.zones.Add(new Zone { id = "lab", name = "Phòng thí nghiệm 203", area = new Rect(-8, 0, 8, 6) });
            L.zones.Add(new Zone { id = "corrW", name = "Hành lang phía Tây", area = new Rect(-26, -3, 21, 3), servesExit = "stairsA" });
            L.zones.Add(new Zone { id = "corrM", name = "Hành lang giữa", area = new Rect(-5, -3, 10, 3) });
            L.zones.Add(new Zone { id = "corrE", name = "Hành lang phía Đông", area = new Rect(5, -3, 21, 3), servesExit = "exitB" });
            L.zones.Add(new Zone { id = "stairsA", name = "Cầu thang bộ A", area = new Rect(-29, -3, 3, 3), isExit = true });
            L.zones.Add(new Zone { id = "exitB", name = "Lối thoát khẩn cấp B", area = new Rect(26, -3, 3, 3), isExit = true });

            L.portals.Add(new Portal { id = "p_class_back", name = "Cửa sau phòng 204", a = "classroom", b = "corrM", pos = new Vector3(1.5f, 0, 0), width = 1f, hasDoor = true });
            L.portals.Add(new Portal { id = "p_class_front", name = "Cửa trước phòng 204", a = "classroom", b = "corrE", pos = new Vector3(6.5f, 0, 0), width = 1f, hasDoor = true });
            L.portals.Add(new Portal { id = "p_lab", name = "Cửa phòng thí nghiệm", a = "lab", b = "corrM", pos = new Vector3(-3f, 0, 0), width = 1f, hasDoor = true, doorStartsOpen = false });
            L.portals.Add(new Portal { id = "p_wm", name = "Hành lang Tây–Giữa", a = "corrW", b = "corrM", pos = new Vector3(-5f, 0, -1.5f), width = 3f });
            L.portals.Add(new Portal { id = "p_me", name = "Hành lang Giữa–Đông", a = "corrM", b = "corrE", pos = new Vector3(5f, 0, -1.5f), width = 3f });
            L.portals.Add(new Portal { id = "p_stairs", name = "Cửa chống cháy cầu thang A", a = "corrW", b = "stairsA", pos = new Vector3(-26f, 0, -1.5f), width = 1.6f, hasDoor = true });
            L.portals.Add(new Portal { id = "p_exitB", name = "Cửa thoát hiểm B", a = "corrE", b = "exitB", pos = new Vector3(26f, 0, -1.5f), width = 1.6f, hasDoor = true });
            L.portals.Add(new Portal { id = "p_vent1", name = "Khe trần 203/204", a = "lab", b = "classroom", pos = new Vector3(0, 0, 3f), width = 0f, walkable = false });
            L.portals.Add(new Portal { id = "p_vent2", name = "Khe trần 203/hành lang", a = "lab", b = "corrW", pos = new Vector3(-6.5f, 0, 0), width = 0f, walkable = false });

            void F(string id, FixtureKind k, string zone, Vector3 pos, Vector3 facing, string label = null) =>
                L.fixtures.Add(new Fixture { id = id, kind = k, zone = zone, pos = pos, facing = facing, label = label });

            F("alarm_class", FixtureKind.AlarmStation, "classroom", new Vector3(7.6f, 1.3f, 0.1f), Vector3.forward, "BÁO CHÁY");
            F("alarm_corrW", FixtureKind.AlarmStation, "corrW", new Vector3(-15f, 1.3f, -2.9f), Vector3.forward, "BÁO CHÁY");
            F("alarm_corrE", FixtureKind.AlarmStation, "corrE", new Vector3(18f, 1.3f, -0.1f), Vector3.back, "BÁO CHÁY");
            F("ext_class", FixtureKind.Extinguisher, "classroom", new Vector3(0.2f, 0f, 1.2f), Vector3.right, "BÌNH CO2");
            F("ext_corrE", FixtureKind.Extinguisher, "corrE", new Vector3(14f, 0f, -2.8f), Vector3.forward, "BÌNH BỘT");
            F("elevator", FixtureKind.Elevator, "corrM", new Vector3(0f, 0f, -2.9f), Vector3.forward, "THANG MÁY");
            F("window1", FixtureKind.Window, "classroom", new Vector3(3f, 1.5f, 5.9f), Vector3.back);
            F("window2", FixtureKind.Window, "classroom", new Vector3(5.5f, 1.5f, 5.9f), Vector3.back);
            F("sign_A", FixtureKind.ExitSign, "corrW", new Vector3(-25.9f, 2.45f, -1.5f), Vector3.right, "EXIT  ←");
            F("sign_B", FixtureKind.ExitSign, "corrE", new Vector3(25.9f, 2.45f, -1.5f), Vector3.left, "EXIT  →");
            F("sign_mid", FixtureKind.ExitSign, "corrM", new Vector3(0f, 2.6f, -2.9f), Vector3.forward, "← A    EXIT    B →");
            F("sign_W", FixtureKind.ExitSign, "corrW", new Vector3(-15f, 2.6f, -0.1f), Vector3.back, "←  EXIT A");
            F("sign_E", FixtureKind.ExitSign, "corrE", new Vector3(15f, 2.6f, -0.1f), Vector3.back, "EXIT B  →");
            F("sign_classF", FixtureKind.ExitSign, "classroom", new Vector3(6.5f, 2.4f, 0.1f), Vector3.forward, "EXIT");
            F("outlet_back", FixtureKind.PowerOutlet, "classroom", new Vector3(7.9f, 0.3f, 4.6f), Vector3.left);
            F("outlet_front", FixtureKind.PowerOutlet, "classroom", new Vector3(7.9f, 0.3f, 1.2f), Vector3.left);
            F("screen", FixtureKind.Screen, "classroom", new Vector3(7.9f, 1.6f, 3f), Vector3.left);
            F("hose_corrM", FixtureKind.HoseCabinet, "corrM", new Vector3(3.2f, 0f, -2.9f), Vector3.forward);
            F("hose_corrW", FixtureKind.HoseCabinet, "corrW", new Vector3(-20f, 0f, -2.9f), Vector3.forward);
            F("blanket_lab", FixtureKind.FireBlanket, "lab", new Vector3(-7.9f, 1.3f, 4.5f), Vector3.right);
            F("panel_lab", FixtureKind.ElectricalPanel, "lab", new Vector3(-4f, 1.2f, 5.9f), Vector3.back, "TỦ ĐIỆN");

            int d = 0;
            foreach (var x in new[] { 2f, 4f, 6f })
                foreach (var z in new[] { 2.3f, 3.5f, 4.7f })
                    F("desk" + d++, FixtureKind.Desk, "classroom", new Vector3(x, 0, z), Vector3.back);
            F("desk_teacher", FixtureKind.Desk, "classroom", new Vector3(6.6f, 0, 1.3f), Vector3.forward);
            foreach (var x in new[] { -6f, -2.5f })
                F("bench" + d++, FixtureKind.Desk, "lab", new Vector3(x, 0, 3.5f), Vector3.back);

            L.startPos = new Vector3(3f, 0, 2.9f);
            L.startYaw = 180f;
            L.startZone = "classroom";
            L.neighborZone = "lab";
            L.hubZone = "corrM";
            L.primaryExit = "stairsA";
            L.secondaryExit = "exitB";
            L.originFixture = "outlet_back";
            L.altOriginFixture = "outlet_front";
            L.neighborOrigin = "panel_lab";

            L.mappedElements.AddRange(new[] { "Wall ×14", "Door ×5", "Desk ×12", "Window ×2", "Corridor ×3", "Stairs ×1" });
            return L;
        }
    }
}
