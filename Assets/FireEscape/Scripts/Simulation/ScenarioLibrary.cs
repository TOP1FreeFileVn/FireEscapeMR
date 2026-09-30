using UnityEngine;

namespace FireEscape
{
    /// <summary>
    /// Thư viện các tình huống khẩn cấp có diễn biến. Một loại tình huống → nhiều level → nhiều biến thể
    /// (chọn theo seed nên có thể tái tạo chính xác cho Replay và What-if).
    /// </summary>
    public static class ScenarioLibrary
    {
        public static readonly string[] LevelNames = { "", "Phát hiện sớm", "Diễn biến xấu đi", "Điều kiện thay đổi", "Tổ hợp nguy hiểm" };

        public static ScenarioDef Build(ScenarioRequest r, FacilityLayout L)
        {
            r.level = Mathf.Clamp(r.level, 1, 4);
            var d = new ScenarioDef { request = r };
            var rng = new System.Random(r.seed);

            if (r.drill != DrillType.None) BuildDrill(d, r, L, rng);
            else
            {
                switch (r.type)
                {
                    case ScenarioType.ElectricalFire: Electrical(d, r.level, L, rng); break;
                    case ScenarioType.SmokeFilledRoom: SmokeRoom(d, r.level, L, rng); break;
                    case ScenarioType.BlockedExit: BlockedExit(d, r.level, L, rng); break;
                    case ScenarioType.FireSpreading: Spreading(d, r.level, L, rng); break;
                    case ScenarioType.PowerFailure: Power(d, r.level, L, rng); break;
                    case ScenarioType.MultipleHazards: Multiple(d, r.level, L, rng); break;
                    case ScenarioType.PersonInNeed: PersonInNeed(d, r.level, L, rng); break;
                    case ScenarioType.DecisionTrap: DecisionTrap(d, r.level, L, rng); break;
                }
                d.title = $"{Txt.Name(r.type)} — Level {r.level}";
                Common(d, L);
            }

            if (r.guidance == GuidanceMode.Beginner) BeginnerHints(d, L);
            if (r.stress) Stress(d, rng);

            if (string.IsNullOrEmpty(d.briefing))
                d.briefing = "Bạn đang ở đây. Sự cố vừa xảy ra. Hãy tự xử lý.";
            return d;
        }

        // ---------- helpers ----------
        static string S(FacilityLayout L) => L.startZone;
        static string N(FacilityLayout L) => L.neighborZone ?? L.startZone;
        static string Hub(FacilityLayout L) => L.hubZone ?? L.startZone;
        static string A(FacilityLayout L) => L.primaryExit;
        static string B(FacilityLayout L) => L.secondaryExit ?? L.primaryExit;
        static string CorrA(FacilityLayout L) => L.ApproachOf(A(L));
        static string CorrB(FacilityLayout L) => L.ApproachOf(B(L));
        static string Origin(FacilityLayout L) => L.originFixture ?? L.startZone;
        static string AltOrigin(FacilityLayout L) => L.altOriginFixture ?? Origin(L);
        static string NOrigin(FacilityLayout L) => L.neighborOrigin ?? N(L);
        static string ExitDoor(FacilityLayout L, string exit) => L.ExitPortal(exit)?.id;

        static string Behind(FacilityLayout L, float dist)
        {
            var fwd = Quaternion.Euler(0, L.startYaw, 0) * Vector3.forward;
            var p = L.startPos - fwd * dist;
            return $"@{p.x:0.00},{p.z:0.00}";
        }

        static void Common(ScenarioDef d, FacilityLayout L)
        {
            // Time Pressure Engine: bản đồ không đứng yên chờ người chơi.
            d.IfMissing("alarm", 15f, "Chưa ai kích hoạt báo động — đám cháy phát triển mà không ai hay biết.")
                .Spread(1.25f).Smoke(S(L), 0.004f);
            d.IfMissing("route_committed", 40f, "Bạn vẫn chưa rời khu vực — khói bắt đầu lan khắp tầng.")
                .Spread(1.3f).Smoke(CorrA(L), 0.006f).Smoke(CorrB(L), 0.004f);
            d.IfMissing("route_committed", 70f, "Hành lang gần như mù mịt — thời gian đang cạn dần.")
                .Spread(1.3f).Smoke(Hub(L), 0.01f);
        }

        static void BeginnerHints(ScenarioDef d, FacilityLayout L)
        {
            d.At(4f).Hint("Quan sát trước: lửa ở đâu, khói đi hướng nào, lối thoát nào gần nhất?");
            if (d.objective == "exit" || d.objective == "exit_low")
                d.IfMissing("alarm", 9f).Hint("Chưa có chuông báo cháy: tìm hộp đỏ BÁO CHÁY và nhấn E.");
            d.OnFlag("head_in_smoke", 0f).Hint("Đầu bạn đang ở trong lớp khói: cúi thấp (C hoặc Ctrl) để thở không khí sạch hơn.");
            d.OnFlag("route_blocked", 0.5f).Hint("Lối này không đi được nữa. Quay lại dùng lối thoát còn lại — đừng cố chấp.");
            d.OnFlag("near_npc", 0f).Hint("Có người cần hỗ trợ. Đánh giá trước: tiếp cận họ có an toàn không?");
            d.OnFlag("near_closed_door", 0f).Hint("Trước khi mở cửa đóng: nhấn F để kiểm tra độ nóng bằng mu bàn tay.");
        }

        static void Stress(ScenarioDef d, System.Random rng)
        {
            d.timeScale = 1.2f;
            d.At(0f).Flag("stress");
            d.At(5f + (float)rng.NextDouble() * 3f).Announce("(Tiếng la hét và tiếng chạy rầm rập ngoài hành lang)", true).As(LogCategory.Info);
            d.At(18f + (float)rng.NextDouble() * 6f).Announce("Ai đó hét: \"Cháy! Cháy rồi! Chạy đi!\"", true).As(LogCategory.Info);
            d.At(36f + (float)rng.NextDouble() * 8f).Announce("(Điện thoại reo liên tục, có tiếng khóc)", true).As(LogCategory.Info);
            d.At(55f).Announce("(Tiếng kính vỡ ở đâu đó)", true).As(LogCategory.Info);
        }

        // ---------- 1. Chập điện ----------
        static void Electrical(ScenarioDef d, int lv, FacilityLayout L, System.Random rng)
        {
            bool otherRoom = !L.isRealRoom && L.neighborZone != null;
            int v = lv == 1 ? rng.Next(2) : rng.Next(otherRoom ? 4 : 3);
            string[] names = { "Cháy nhỏ, vừa mới phát hiện", "Khói xuất hiện trước rồi mới có lửa", "Cháy ở vị trí gần lối ra", "Sự cố xuất hiện khi bạn đang ở phòng khác" };
            d.variant = names[v];
            d.cause = "Ổ cắm kéo dài quá tải (sạc laptop + máy chiếu) bị chập.";
            string origin = v == 2 ? AltOrigin(L) : Origin(L);
            float growth = 0.010f + 0.005f * lv;

            if (v == 1)
            {
                d.initialSigns = "Mùi khét, khói mỏng, chưa thấy lửa.";
                d.At(2f, "Mùi khét — khói mỏng bốc lên từ ổ điện.").As(LogCategory.Incident).Flag("incident").Smoke(S(L), 0.006f);
                d.At(14f, "Ổ điện bắt lửa.").Ignite(origin, 0.1f, growth);
            }
            else
            {
                d.initialSigns = "Tiếng lách tách, tia lửa nhỏ ở ổ điện.";
                d.At(2f, "Tiếng lách tách — tia lửa bắn ra từ ổ điện.").As(LogCategory.Incident).Flag("incident")
                    .Ignite(origin, lv == 1 ? 0.07f : 0.1f, growth);
            }

            if (v == 3)
            {
                var n = L.GetZone(L.neighborZone);
                d.startPos = n.Center + new Vector3(0.5f, 0, 0.5f);
                d.startYaw = 180f;
                d.initialSigns = "Bạn đang ở phòng bên cạnh. Mùi khét từ phía hành lang.";
            }

            if (lv >= 2) d.OnFlag("route_committed", 3f, "Khói lan ra đoạn hành lang phía trước bạn.").SmokeRoute(0.015f);
            if (lv >= 3)
            {
                d.At(16f, "Mất điện toàn tầng — chỉ còn đèn thoát hiểm.").PowerOff(true);
                d.OnFlag("route_committed", 6f, "Khói dày đặc tràn vào lối thoát bạn đang tới.").BlockRoute(BlockCause.Smoke);
            }
            if (lv >= 4)
            {
                d.At(8f, "Có người gọi: \"Chân tôi bị đau, tôi không đi nhanh được!\"").As(LogCategory.Npc).Npc(Hub(L), NpcNeed.Slow, 1f, -0.3f);
                d.OnFlag("route_committed", 4f, "Lửa chặn cửa lối thoát bạn đang tới.").BlockRoute(BlockCause.Fire);
                d.OnFlag("route_changed", 5f, "Khói bắt đầu lan vào tuyến thay thế.").SmokeRoute(0.012f);
            }

            d.development = lv switch
            {
                1 => "Cháy nhỏ → phát hiện sớm → ít khói.",
                2 => "Khói tăng → một hướng không còn thuận lợi.",
                3 => "Mất điện → khói nhiều → lối thoát thay đổi.",
                _ => "Chập điện + khói + lối thoát bị chặn + người cần hỗ trợ."
            };
            d.learningGoal = "Nhận biết dấu hiệu bất thường, báo động, quyết định dập lửa hay thoát thay vì hành động theo phản xạ.";
            d.parTime = 45f + 5f * lv;
        }

        // ---------- 2. Phòng đầy khói ----------
        static void SmokeRoom(ScenarioDef d, int lv, FacilityLayout L, System.Random rng)
        {
            float[] rates = { 0, 0.008f, 0.022f, 0.018f, 0.028f };
            string[] names = { "", "Khói tăng chậm", "Khói tăng rất nhanh", "Một phần không gian gần như không thể quan sát", "Mất phương hướng trong khói và bóng tối" };
            d.variant = names[lv];
            d.cause = "Dây điện âm trần phía trên phòng bên cạnh chập; khói theo khe trần tràn sang.";
            d.initialSigns = "Khói xám tràn ra từ khe trần, mùi nhựa cháy.";
            d.At(1f, "Khói xám tràn ra từ khe trần.").As(LogCategory.Incident).Flag("incident")
                .Ignite(NOrigin(L), 0.35f, 0.02f).Smoke(S(L), rates[lv]);
            if (lv == 3) d.At(12f, "Đèn trong phòng tắt — khói che gần hết một nửa phòng.").PowerOff(true, S(L));
            if (lv == 4) d.At(8f, "Mất điện hoàn toàn. Không còn đèn khẩn cấp.").PowerOff(false);
            d.development = "Khói tích tụ từ trần xuống, tầm nhìn giảm dần. Quyết định phải thay đổi khi điều kiện thay đổi.";
            d.learningGoal = "Hiểu khói cũng là mối nguy hiểm; cúi thấp, không mất thời gian vào hành động không cần thiết.";
            d.parTime = 40f + 3f * lv;
        }

        // ---------- 3. Lối thoát bị chặn ----------
        static void BlockedExit(ScenarioDef d, int lv, FacilityLayout L, System.Random rng)
        {
            int v = lv == 1 ? rng.Next(3) : rng.Next(4);
            string[] names = { "Cửa bị lửa chặn", "Hành lang đầy khói", "Có vật cản", "Lối thoát trở nên không phù hợp sau một khoảng thời gian" };
            BlockCause[] causes = { BlockCause.Fire, BlockCause.Smoke, BlockCause.Debris, BlockCause.Smoke };
            d.variant = names[v];
            d.cause = "Cháy trong phòng bên cạnh; chuông của tòa nhà đã kêu.";
            d.initialSigns = "Chuông báo cháy vang, khói ở phòng bên cạnh.";
            d.At(1f, "Chuông báo cháy toàn tòa nhà vang lên. Có khói ở phòng bên cạnh.").As(LogCategory.Incident)
                .Flag("incident").Alarm().Ignite(NOrigin(L), 0.3f, 0.018f);

            if (v < 3)
                d.OnFlag("route_committed", 2f + rng.Next(0, 3), $"Phía trước: lối thoát bị chặn bởi {Txt.Name(causes[v])}.").BlockRoute(causes[v]);
            else if (ExitDoor(L, A(L)) != null)
                d.At(22f - 2f * lv, "Cầu thang A không còn phù hợp: khói tràn vào buồng thang.").Block(ExitDoor(L, A(L)), BlockCause.Smoke);

            if (lv >= 2) d.OnFlag("route_changed", 6f, "Tuyến thay thế bắt đầu có khói — đi nhanh và cúi thấp.").SmokeRoute(0.012f);
            if (lv >= 3) d.At(28f, "Mất điện.").PowerOff(true);
            if (lv >= 4) d.At(10f, "Một người đứng ngơ ngác giữa hành lang.").As(LogCategory.Npc).Npc(Hub(L), NpcNeed.Lost, 1.5f, 0f);

            d.development = "Người chơi xác định một lối thoát, nhưng trên đường đi lối đó trở nên không khả dụng.";
            d.learningGoal = "Nhận ra kế hoạch ban đầu đã thất bại, không cố chấp, nhanh chóng tìm phương án khác.";
            d.parTime = 60f;
        }

        // ---------- 4. Cháy lan ----------
        static void Spreading(ScenarioDef d, int lv, FacilityLayout L, System.Random rng)
        {
            int v = lv switch { 1 => 0, 2 => rng.Next(2), 3 => 1 + rng.Next(2), _ => 3 };
            if (L.isRealRoom && v == 1) v = 0;
            string[] names = { "Cháy ở phòng bên cạnh", "Cháy ở tầng dưới", "Cháy ở khu vực phía sau bạn", "Nhiều khu vực lần lượt bị ảnh hưởng" };
            d.variant = names[v];
            d.cause = v == 1 ? "Cháy kho ở tầng 1, khói theo cầu thang bốc lên." : "Cháy phòng thí nghiệm bên cạnh.";
            float g = 0.02f + 0.004f * lv;
            switch (v)
            {
                case 0:
                    d.initialSigns = "Chưa thấy lửa — chỉ có khói lọt qua khe trần.";
                    d.At(0f, "Cháy bắt đầu ở phòng bên cạnh — bạn chưa nhìn thấy lửa.").As(LogCategory.Incident).Flag("incident").Ignite(NOrigin(L), 0.3f, g);
                    break;
                case 1:
                    d.initialSigns = "Khói bốc lên từ phía cầu thang.";
                    d.At(0f, "Khói bốc lên từ cầu thang — cháy ở tầng dưới.").As(LogCategory.Incident).Flag("incident")
                        .Smoke(A(L), 0.03f).Smoke(CorrA(L), 0.01f);
                    if (ExitDoor(L, A(L)) != null)
                        d.At(15f, "Buồng thang A đặc khói.").SetSmoke(A(L), 0.9f).Block(ExitDoor(L, A(L)), BlockCause.Smoke);
                    break;
                case 2:
                    d.initialSigns = "Tiếng nổ nhỏ phía sau lưng.";
                    d.At(1f, "Có tiếng nổ nhỏ phía sau lưng bạn.").As(LogCategory.Incident).Flag("incident").Ignite(Behind(L, 2.2f), 0.2f, g);
                    break;
                default:
                    d.initialSigns = "Khói ở phòng bên cạnh, rồi lan dần.";
                    d.At(0f, "Cháy bắt đầu ở phòng bên cạnh.").As(LogCategory.Incident).Flag("incident").Ignite(NOrigin(L), 0.3f, g);
                    d.At(18f, "Lửa đã lan ra hành lang phía cầu thang A.").Ignite(CorrA(L), 0.25f, g);
                    d.At(30f, "Khói xuất hiện ở hành lang phía lối B.").Smoke(CorrB(L), 0.01f);
                    break;
            }
            d.At(0f).Spread(1f + 0.15f * lv);
            d.development = "Sự cố bắt đầu ở nơi khác rồi ảnh hưởng dần đến vị trí của bạn.";
            d.learningGoal = "Nhận biết nguy hiểm gián tiếp, không chờ thấy lửa mới phản ứng; quyết định trước khi tuyến đường xấu đi.";
            d.parTime = 55f;
        }

        // ---------- 5. Mất điện ----------
        static void Power(ScenarioDef d, int lv, FacilityLayout L, System.Random rng)
        {
            string[] names = { "", "Mất điện ngay khi bắt đầu", "Mất điện giữa nhiệm vụ", "Một số khu vực tối hoàn toàn", "Đèn khẩn cấp cũng hỏng" };
            d.variant = names[lv];
            d.cause = "Chập tủ điện phòng bên cạnh làm nhảy aptomat toàn tầng.";
            d.initialSigns = "Đèn tắt, mùi khói.";
            var e = d.At(lv == 2 ? 1f : 0f, lv == 2 ? "Mùi khói từ phòng bên cạnh." : "Mất điện toàn tầng. Có mùi khói.")
                .As(LogCategory.Incident).Flag("incident").Ignite(NOrigin(L), 0.3f, 0.02f);
            switch (lv)
            {
                case 1: e.PowerOff(true); break;
                case 2: d.OnFlag("route_committed", 2f, "Mất điện đột ngột giữa lúc bạn đang di chuyển.").PowerOff(true); break;
                case 3: e.PowerOff(true, CorrA(L)); break;
                default: e.PowerOff(false).Smoke(S(L), 0.006f); break;
            }
            d.development = "Ánh sáng và thông tin bị hạn chế; phải nhận diện lại không gian.";
            d.learningGoal = "Không phụ thuộc hoàn toàn vào tiện ích điện tử; dựa vào đèn thoát hiểm, tường và trí nhớ không gian.";
            d.parTime = 55f;
        }

        // ---------- 6. Nhiều nguy hiểm cùng lúc ----------
        static void Multiple(ScenarioDef d, int lv, FacilityLayout L, System.Random rng)
        {
            float k = 1f - 0.1f * (lv - 1);
            d.variant = "Đám cháy + khói + lối ra bị chặn + mất điện";
            d.cause = "Chập điện ở phòng học lan sang hệ thống dây trần.";
            d.initialSigns = "Tia lửa, khói, chuông chưa kêu.";
            d.At(0f, "Chập điện — lửa và khói xuất hiện.").As(LogCategory.Incident).Flag("incident").Ignite(Origin(L), 0.15f, 0.02f + 0.004f * lv);
            d.At(20f * k, "Một tuyến thoát bắt đầu bị chặn.").BlockRoute(BlockCause.Fire);
            if (lv >= 2) d.At(12f * k, "Mất điện.").PowerOff(true);
            d.At(30f * k, "Khói xuất hiện ở một khu vực khác.").Smoke(CorrA(L), 0.012f);
            d.At(38f * k, "Một người cần trợ giúp.").As(LogCategory.Npc).Npc(Hub(L), lv >= 3 ? NpcNeed.Panicked : NpcNeed.Slow, -1f, -0.5f);
            d.At(50f * k, "Khói dày lan vào lối thoát bạn đang hướng tới — đi nhanh, cúi thấp.").SmokeRoute(0.02f);
            d.development = "Nhận diện → Ưu tiên → Quyết định → Thay đổi kế hoạch.";
            d.learningGoal = "Khi có quá nhiều vấn đề xảy ra cùng lúc, bạn ưu tiên xử lý điều gì trước?";
            d.parTime = 75f;
        }

        // ---------- 7. Có người cần hỗ trợ ----------
        static void PersonInNeed(ScenarioDef d, int lv, FacilityLayout L, System.Random rng)
        {
            d.cause = "Cháy phòng bên cạnh trong giờ học, nhiều người đang sơ tán.";
            d.initialSigns = "Chuông báo cháy, tiếng người.";
            d.At(0f, "Chuông báo cháy vang lên. Cháy ở phòng bên cạnh.").As(LogCategory.Incident).Flag("incident")
                .Alarm().Ignite(NOrigin(L), 0.35f, 0.02f);
            switch (lv)
            {
                case 1:
                    d.variant = "Một người không biết đường";
                    d.At(3f, "Một bạn mới chuyển trường đứng lúng túng trong phòng.").As(LogCategory.Npc).Npc(S(L), NpcNeed.Lost, 1.2f, 0.8f);
                    break;
                case 2:
                    var need = rng.Next(2) == 0 ? NpcNeed.Slow : NpcNeed.Lost;
                    d.variant = "Một người " + Txt.Name(need);
                    d.At(3f, "Có người ở hành lang cần giúp.").As(LogCategory.Npc).Npc(Hub(L), need, 1.5f, -0.3f);
                    break;
                case 3:
                    d.variant = "Một người hoảng loạn";
                    d.At(3f, "Một người đang hoảng loạn chạy qua lại.").As(LogCategory.Npc).Npc(Hub(L), NpcNeed.Panicked, -1f, 0f);
                    break;
                default:
                    d.variant = "Một người bị mắc kẹt trong vùng cháy + một người lạc";
                    d.At(3f, "Tiếng kêu cứu từ phòng đang cháy: \"Cứu với! Tôi bị kẹt!\"").As(LogCategory.Npc).Npc(N(L), NpcNeed.Trapped, 1.5f, 1f);
                    d.At(8f, "Một người đứng ngơ ngác ở hành lang.").As(LogCategory.Npc).Npc(Hub(L), NpcNeed.Lost, 2f, 0f);
                    break;
            }
            d.development = "Bạn phát hiện người khác gặp khó khăn trong quá trình sơ tán.";
            d.learningGoal = "Đánh giá hoàn cảnh trước khi hành động; cân nhắc giữa hỗ trợ và nguy cơ cho chính mình. Cứu người không mặc định là đúng.";
            d.parTime = 70f;
        }

        // ---------- 8. Decision Trap ----------
        static void DecisionTrap(ScenarioDef d, int lv, FacilityLayout L, System.Random rng)
        {
            int v = rng.Next(4);
            if (v == 1 && Hub(L) == S(L)) v = 2;
            string[] names = { "Đường quen thuộc", "Hoảng loạn", "Chần chừ", "Thông tin sai lệch" };
            d.variant = names[v];
            switch (v)
            {
                case 0:
                    d.familiarExit = A(L);
                    d.cause = "Cháy phòng bên cạnh, gần phía cầu thang quen thuộc.";
                    d.initialSigns = "Chuông báo cháy.";
                    d.briefing = $"Mỗi ngày bạn đều đi {L.GetZone(A(L)).name} — lối quen thuộc nhất. Sự cố vừa xảy ra. Hãy tự xử lý.";
                    d.At(0f, "Chuông báo cháy vang lên.").As(LogCategory.Incident).Flag("incident").Alarm().Ignite(NOrigin(L), 0.3f, 0.02f);
                    d.At(6f, "Khói tràn vào hành lang dẫn tới lối quen thuộc.").Smoke(CorrA(L), 0.02f + 0.005f * lv);
                    if (ExitDoor(L, A(L)) != null)
                        d.OnFlag("route:" + A(L), 3f, "Lối quen thuộc đầy khói — không còn phù hợp.").Block(ExitDoor(L, A(L)), BlockCause.Smoke);
                    break;
                case 1:
                    d.cause = "Chập điện ngay gần chỗ bạn.";
                    d.initialSigns = "Lửa bùng lên gần bạn.";
                    d.At(1f, "Lửa bùng lên gần chỗ bạn ngồi!").As(LogCategory.Incident).Flag("incident").Ignite(Origin(L), 0.25f, 0.03f);
                    d.At(0f).SetSmoke(Hub(L), 0.35f).Smoke(Hub(L), 0.004f);
                    d.OnEnter(Hub(L), 0f, "Bạn lao ra lối gần nhất mà không quan sát — khu vực này mù mịt khói.", 10f)
                        .SetSmoke(Hub(L), 0.85f).Smoke(Hub(L), 0.01f).Flag("panic_trap").As(LogCategory.Decision);
                    break;
                case 2:
                    d.cause = "Chập điện, ban đầu chỉ là đám cháy nhỏ.";
                    d.initialSigns = "Tia lửa nhỏ.";
                    d.At(1f, "Tia lửa nhỏ ở ổ điện.").As(LogCategory.Incident).Flag("incident").Ignite(Origin(L), 0.12f, 0.02f);
                    d.IfMissing("route_committed", 18f, "Bạn quan sát quá lâu — tình huống phức tạp hơn.")
                        .Spread(1.5f).Smoke(CorrA(L), 0.015f).Smoke(CorrB(L), 0.008f).Flag("hesitation_trap").As(LogCategory.Decision);
                    d.IfMissing("route_committed", 32f, "Lửa đã chặn một lối thoát.").BlockRoute(BlockCause.Fire);
                    break;
                default:
                    d.cause = "Cháy gần phía cầu thang A, loa thông báo sai hướng.";
                    d.initialSigns = "Chuông báo cháy và loa thông báo.";
                    d.At(0f, "Chuông báo cháy vang lên.").As(LogCategory.Incident).Flag("incident").Alarm().Ignite(NOrigin(L), 0.35f, 0.02f);
                    d.At(6f).Announce("Loa: \"Mọi người di chuyển về " + L.GetZone(A(L)).name + "!\"", false, A(L)).Smoke(CorrA(L), 0.025f);
                    if (lv <= 2)
                        d.At(14f).Announce("Một người chạy ngược lại: \"Đừng đi hướng đó, khói đầy rồi!\"", true, B(L));
                    break;
            }
            d.development = "Môi trường thay đổi để kiểm tra sai lầm trong ra quyết định.";
            d.learningGoal = "Rèn cách ra quyết định trong khủng hoảng: không theo thói quen, không hoảng loạn, không chần chừ, kiểm chứng thông tin.";
            d.parTime = 55f;
        }

        // ---------- Drills ----------
        static void BuildDrill(ScenarioDef d, ScenarioRequest r, FacilityLayout L, System.Random rng)
        {
            d.title = Txt.Name(r.drill);
            d.timeLimit = 90f;
            switch (r.drill)
            {
                case DrillType.Reaction:
                    float delay = 3f + (float)rng.NextDouble() * 5f;
                    d.variant = "Một sự cố xuất hiện — hãy phản ứng";
                    d.objective = "alarm";
                    d.cause = "Ổ điện chập.";
                    d.briefing = "Đứng yên, quan sát xung quanh. Khi thấy sự cố: tới hộp BÁO CHÁY đỏ gần nhất và nhấn E càng nhanh càng tốt.";
                    d.At(delay, "Tia lửa bắn ra từ ổ điện!").As(LogCategory.Incident).Flag("incident").Ignite(Origin(L), 0.06f, 0.004f);
                    d.parTime = 10f;
                    break;
                case DrillType.Navigation:
                    d.variant = "Tìm exit trong môi trường thay đổi";
                    d.objective = "exit";
                    d.briefing = "Chuông đã kêu. Tìm đường ra lối thoát. Tuyến đường có thể thay đổi bất ngờ.";
                    d.At(0f, "Có cháy — hãy tìm lối thoát.").As(LogCategory.Incident).Flag("incident").Alarm().Ignite(NOrigin(L), 0.3f, 0.015f);
                    if (rng.Next(2) == 0) d.OnFlag("route_committed", 1.5f, "Có vật cản chắn lối phía trước.").BlockRoute(BlockCause.Debris);
                    else d.At(0f).Smoke(rng.Next(2) == 0 ? CorrA(L) : CorrB(L), 0.03f);
                    d.parTime = 30f;
                    break;
                case DrillType.Interaction:
                    d.variant = "Tương tác với thiết bị mô phỏng: báo cháy + bình chữa cháy";
                    d.objective = "extinguish";
                    d.briefing = "Một đám cháy rất nhỏ. Báo động, lấy bình chữa cháy, thực hiện PASS: rút chốt (E) → chĩa vào GỐC lửa → giữ E để phun → quét qua lại.";
                    d.At(1f, "Một đám cháy nhỏ ở ổ điện.").As(LogCategory.Incident).Flag("incident").Ignite(Origin(L), 0.14f, 0.003f);
                    d.parTime = 25f;
                    break;
                default:
                    d.variant = "Di chuyển thật sự: cúi thấp dưới lớp khói";
                    d.objective = "exit_low";
                    d.briefing = "Lớp khói dày đã xuống thấp. Hãy thực sự cúi người (MR) hoặc giữ C/Ctrl (PC) và ra tới lối thoát mà đầu không chạm lớp khói.";
                    var e = d.At(0f, "Khói dày đặc đã xuống thấp.").As(LogCategory.Incident).Flag("incident");
                    foreach (var z in L.zones)
                        if (!z.isExit) e.SetSmoke(z.id, z.id == L.startZone ? 0.45f : 0.72f).Smoke(z.id, 0.002f);
                    d.parTime = 35f;
                    break;
            }
            d.learningGoal = d.variant;
        }
    }
}
