using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FireEscape
{
    /// <summary>Một phát hiện của AI Error Analysis, theo khung What / Wrong / Why / Risk / Better / Improve.</summary>
    public class Finding
    {
        public string key;
        public bool positive;
        public Skill skill;
        public int severity;          // 0 tốt · 1 nhẹ · 2 nghiêm trọng · 3 nguy hiểm tính mạng
        public float time = -1f;
        public string title, what, wrong, why, risk, ruleId, improve;
        public string Better => SafetyKnowledge.Rule(ruleId)?.text;
    }

    public class EvaluationReport
    {
        public MissionOutcome outcome;
        public string reason, headline, verdict;
        public float safety, decision, timeScore, route, consistency, total;
        public float T1 = -1f, T2 = -1f, T3 = -1f, T4 = -1f, totalTime = -1f;
        public readonly Dictionary<Skill, float> skills = new Dictionary<Skill, float>();
        public readonly List<Finding> findings = new List<Finding>();
        public Skill? weakest;
        public string drillResult;

        public IEnumerable<Finding> Problems => findings.Where(f => !f.positive).OrderByDescending(f => f.severity);
        public IEnumerable<Finding> Strengths => findings.Where(f => f.positive);
    }

    /// <summary>
    /// Phân tích hành vi dựa trên luật (rule-based). Mọi "Better approach" đều trích từ SafetyKnowledge —
    /// AI không tự tạo quy tắc an toàn mới, chỉ xác định người chơi đã lệch khỏi quy tắc nào, khi nào và vì sao.
    /// </summary>
    public static class MissionEvaluator
    {
        public static EvaluationReport Evaluate(MissionRecord r)
        {
            var rep = new EvaluationReport { outcome = r.outcome, reason = r.reason };
            var fl = r.flags;
            bool failed = r.outcome == MissionOutcome.Failed;
            bool drill = r.def.request.drill != DrillType.None;
            float inc = r.ResponseStart;

            // ---------- Time Engine ----------
            if (r.firstActionTime >= 0f) rep.T1 = r.firstActionTime - inc;
            if (r.routeTime >= 0f && r.firstActionTime >= 0f) rep.T2 = Mathf.Max(0f, r.routeTime - r.firstActionTime);
            if (r.routeTime >= 0f) rep.T3 = r.endTime - r.routeTime;
            if (r.blockedExit != null || r.recoveries > 0) rep.T4 = r.recoveryTotal;
            rep.totalTime = r.endTime - inc;

            var F = rep.findings;
            Finding Add(string key, Skill s, int sev, string title, string what, string wrong, string why, string risk, string rule, string improve, float t = -1f)
            {
                var f = new Finding { key = key, skill = s, severity = sev, title = title, what = what, wrong = wrong, why = why, risk = risk, ruleId = rule, improve = improve, time = t };
                F.Add(f);
                return f;
            }
            void Good(string key, Skill s, string title, string what, string rule, float t = -1f) =>
                F.Add(new Finding { key = key, skill = s, positive = true, title = title, what = what, ruleId = rule, time = t });
            float TimeOf(string tag) { var e = r.log.FirstOrDefault(x => x.tag == tag); return e != null ? e.time : -1f; }

            // ---------- Lỗi ----------
            if (failed)
                Add("outcome", Skill.RiskRecognition, 3, "Không thoát được", r.reason,
                    "Nhiệm vụ kết thúc trước khi bạn tới nơi an toàn.",
                    "Tổng hợp của các quyết định bên dưới.", "Trong thực tế đây là thương vong.", "two_exits",
                    "Xem Replay để tìm thời điểm tình huống bắt đầu vượt khỏi tầm kiểm soát.", r.endTime);

            if (fl.Contains("used_elevator"))
                Add("elevator", Skill.DecisionMaking, 3, "Dùng thang máy khi có cháy", "Bạn gọi thang máy để thoát xuống.",
                    "Thang máy không phải phương tiện thoát nạn khi cháy.", "Quyết định — chọn lối quen tiện thay vì lối an toàn.",
                    "Kẹt trong buồng thang giữa tầng khi mất điện, hoặc cửa mở ra đúng tầng cháy.", "no_elevator",
                    "Ôn lại Know 1.2 và chơi Decision Trap.", TimeOf("elevator"));

            if (!drill && r.incidentTime >= 0f && !fl.Contains("alarm"))
                Add("no_alarm", Skill.DecisionMaking, 2, "Không báo động", "Bạn không kích hoạt chuông báo cháy trong suốt nhiệm vụ.",
                    "Người khác trong tòa nhà không được cảnh báo.", "Quyết định — bỏ qua bước báo động vốn chỉ tốn vài giây.",
                    "Mọi người phát hiện muộn; hệ thống đã cho đám cháy phát triển nhanh hơn từ giây 15.", "alarm",
                    "Luyện Drill A – Reaction để hình thành phản xạ báo động.");
            else if (r.alarmTime >= 0f && r.alarmTime - inc > 20f)
                Add("late_alarm", Skill.Reaction, 1, "Báo động muộn", $"Bạn kích hoạt chuông sau {Txt.Secs(r.alarmTime - inc)}.",
                    "Khoảng thời gian vàng để cảnh báo đã trôi qua.", "Thời gian — nhận ra sự cố nhưng chưa coi báo động là ưu tiên.",
                    "Người khác mất thêm thời gian sơ tán.", "alarm", "Drill A – Reaction.", r.alarmTime);

            if (rep.T1 > 8f)
                Add("slow_reaction", Skill.Reaction, rep.T1 > 15f ? 2 : 1, "Phản ứng chậm", $"Mất {Txt.Secs(rep.T1)} từ khi có dấu hiệu đến hành động đầu tiên.",
                    "Bạn chờ quá lâu trước khi làm bất cứ điều gì.", "Nhận diện — chưa coi dấu hiệu ban đầu (mùi khét, tia lửa, khói mỏng) là nguy hiểm.",
                    "Đám cháy nhỏ có thể lớn gấp đôi mỗi phút.", "observe", "Ôn Know 1.1 và Drill A – Reaction.", r.firstActionTime);

            if (fl.Contains("ignored_hot_door"))
                Add("ignored_hot_door", Skill.DecisionMaking, 3, "Mở cửa dù biết cửa nóng", "Bạn đã kiểm tra, thấy cửa nóng nhưng vẫn mở.",
                    "Mở cửa khi phía sau có lửa.", "Quyết định — có thông tin đúng nhưng hành động ngược lại.",
                    "Luồng khói nóng và lửa có thể bùng ra ngay khi cửa mở.", "door_check", "Chơi Blocked Exit để luyện tìm lối khác.", TimeOf("hot_door"));
            else if (fl.Contains("opened_hot_door"))
                Add("hot_door", Skill.RiskRecognition, 2, "Mở cửa nóng không kiểm tra", "Bạn mở một cánh cửa đóng mà không kiểm tra độ nóng.",
                    "Phía sau là khu vực đang cháy.", "Thao tác — bỏ qua bước kiểm tra bằng mu bàn tay.",
                    "Khói và nhiệt ập vào người (phơi nhiễm +22).", "door_check", "Luôn nhấn F kiểm tra trước khi mở cửa đóng.", TimeOf("hot_door"));

            if (r.timeUprightInSmoke > 4f)
                Add("upright_smoke", Skill.RiskRecognition, r.timeUprightInSmoke > 10f ? 2 : 1, "Đứng thẳng trong khói",
                    $"Đầu bạn nằm trong lớp khói tổng cộng {r.timeUprightInSmoke:0}s.",
                    "Bạn di chuyển đứng thẳng qua khu vực có khói.", "Nhận diện — chưa coi khói là mối nguy hiểm ngang với lửa.",
                    "Hít khói độc gây choáng và ngạt chỉ sau vài hơi thở.", "low", "Drill D – Physical Practice: cúi thấp dưới lớp khói.");

            if (r.timeNearFire > 3f)
                Add("near_fire", Skill.RiskRecognition, 2, "Tiến quá gần đám cháy", $"Bạn ở trong vùng nhiệt của đám cháy {r.timeNearFire:0}s.",
                    "Khoảng cách tới lửa quá gần.", "Nhận diện — đánh giá thấp bức xạ nhiệt.",
                    "Bỏng, bắt lửa quần áo.", "extinguish", "Giữ khoảng cách 2–3 m nếu dập lửa; nếu không dập thì tránh xa.");

            if (fl.Contains("fought_large_fire"))
                Add("large_fire", Skill.DecisionMaking, 2, "Cố dập đám cháy quá lớn", "Bạn dùng bình chữa cháy khi lửa đã lan rộng.",
                    "Một bình chữa cháy xách tay chỉ hiệu quả với đám cháy mới bắt đầu.", "Quyết định — đánh giá sai quy mô đám cháy.",
                    "Mất thời gian thoát hiểm trong khi lửa và khói vẫn tăng.", "extinguish", "Chơi Chập điện Level 2–3 để luyện quyết định dập hay thoát.", TimeOf("fought_large_fire"));
            else if (fl.Contains("fought_before_alarm") && !drill)
                Add("fight_no_alarm", Skill.DecisionMaking, 1, "Dập lửa trước khi báo động", "Bạn dùng bình chữa cháy khi chưa ai được cảnh báo.",
                    "Thứ tự ưu tiên chưa đúng.", "Quyết định — bỏ qua bước báo động.", "Nếu dập thất bại, người khác mất thời gian phản ứng.",
                    "alarm", "Báo động trước, rồi mới quyết định dập.");

            if (fl.Contains("aim_top") && !fl.Contains("pass_aim"))
                Add("aim_top", Skill.RiskRecognition, 1, "Phun vào ngọn lửa", "Bạn chĩa vòi vào ngọn lửa thay vì gốc lửa.",
                    "Chất chữa cháy bay qua mà không cắt được nguồn cháy.", "Thao tác — sai bước Aim trong PASS.",
                    "Hết bình trước khi lửa tắt.", "pass", "Drill C – Interaction.");

            if (r.blockedExit != null && r.recoveryTotal > 8f)
                Add("slow_recovery", Skill.Adaptability, r.recoveryTotal > 15f ? 2 : 1, "Chậm thích nghi khi lối thoát bị chặn",
                    $"Bạn mất {Txt.Secs(r.recoveryTotal)} để chuyển sang lối khác sau khi tuyến ban đầu không còn phù hợp.",
                    "Bạn tiếp tục bám kế hoạch cũ.", "Quyết định — cố chấp với \"đường quen thuộc\".",
                    "Mỗi giây đứng trước lối bị chặn là một giây khói và lửa tiến gần.", "two_exits",
                    "AI sẽ ưu tiên các nhiệm vụ Blocked Exit cho bạn.", r.log.FirstOrDefault(e => e.tag == "route_blocked")?.time ?? -1f);
            if (failed && r.blockedExit != null && r.routeChanges == 0)
                Add("no_recovery", Skill.Adaptability, 3, "Không đổi kế hoạch", "Lối thoát bị chặn nhưng bạn không chuyển sang lối khác.",
                    "Không có kế hoạch dự phòng.", "Quyết định — chưa xác định lối thoát thứ hai.", "Mắc kẹt.", "two_exits", "Blocked Exit Level 1–2.");

            if (fl.Contains("window_trap"))
                Add("window", Skill.DecisionMaking, 1, "Đến cửa sổ thay vì lối thoát", "Bạn đến cửa sổ khi vẫn còn lối thoát khác.",
                    "Cửa sổ tầng cao không phải lối thoát.", "Quyết định — phản xạ tìm \"ánh sáng\" thay vì tìm lối thoát.",
                    "Mất thời gian; nguy cơ nhảy xuống.", "window", "Ôn Know 1.2.", TimeOf("window"));

            if (fl.Contains("followed_misinfo"))
                Add("misinfo", Skill.DecisionMaking, 2, "Tin thông tin sai lệch", "Bạn đi theo thông báo dù hướng đó đang có khói nhìn thấy được.",
                    "Lời nói được ưu tiên hơn quan sát trực tiếp.", "Quyết định — không kiểm chứng thông tin.",
                    "Đi thẳng vào vùng khói.", "verify_info", "Decision Trap — Thông tin sai lệch.", TimeOf("followed_misinfo"));
            else if (fl.Contains("misinfo") && !failed)
                Good("verified", Skill.DecisionMaking, "Kiểm chứng thông tin", "Bạn không đi theo thông báo sai lệch mà dựa vào quan sát.", "verify_info");

            if (fl.Contains("panic_trap"))
                Add("panic", Skill.DecisionMaking, 2, "Chạy ngay theo phản xạ", "Bạn lao ra lối gần nhất trong vài giây đầu mà không quan sát.",
                    "Tuyến đó là vùng khói dày.", "Quyết định — hoảng loạn, bỏ qua bước quan sát.",
                    "Chọn phải tuyến nguy hiểm.", "observe", "Decision Trap — Hoảng loạn.", TimeOf("event"));

            if (fl.Contains("hesitation_trap") || (r.idleTime > 12f && !drill))
                Add("hesitation", Skill.TimeManagement, r.idleTime > 20f ? 2 : 1, "Chần chừ", $"Bạn đứng yên tổng cộng {r.idleTime:0}s sau khi đã bắt đầu phản ứng.",
                    "Quan sát quá lâu.", "Thời gian — quá trình ra quyết định kéo dài.",
                    "Tình huống trở nên phức tạp hơn (hệ thống đã leo thang khi bạn chần chừ).", "no_return", "Bật Stress Mode / Time Pressure.");

            if (fl.Contains("unsafe_rescue"))
                Add("unsafe_rescue", Skill.RiskRecognition, 2, "Tiếp cận người bị kẹt một cách mạo hiểm", "Bạn đi vào vùng đang cháy để tới chỗ người bị kẹt.",
                    "Không đánh giá rủi ro cho bản thân trước khi hỗ trợ.", "Nhận diện — \"cứ cứu người là đúng\".",
                    "Có thể thành nạn nhân thứ hai.", "help_assess", "Person in Need Level 4.", TimeOf("unsafe_rescue"));

            foreach (var n in r.npcs)
            {
                if (!n.seen || n.need == NpcNeed.Trapped) continue;
                if (n.mode != NpcMode.Evacuated && n.mode != NpcMode.Following && !r.flags.Contains("npc_helped"))
                    Add("npc_left", Skill.DecisionMaking, 1, "Bỏ lại người cần hỗ trợ",
                        $"Bạn đã thấy một người {Txt.Name(n.need)} nhưng không hỗ trợ hay hướng dẫn họ.",
                        "Việc hỗ trợ này có thể thực hiện an toàn.", "Quyết định — tập trung hoàn toàn vào bản thân.",
                        "Người đó có thể không thoát ra được.", "help_assess", "Person in Need Level 1–3.");
            }
            if (fl.Contains("npc_lost"))
                Add("npc_down", Skill.TimeManagement, 1, "Có người bất tỉnh", "Một người trong tầng đã bất tỉnh vì khói.",
                    "Cảnh báo/hỗ trợ đến quá muộn.", "Thời gian.", "Thương vong.", "alarm", "Báo động sớm hơn.");

            if (r.escapedVia != null && r.optimalDistance > 0f && r.distance > r.optimalDistance * 1.7f && r.blockedExit == null)
                Add("route_ineff", Skill.Navigation, 1, "Tuyến đi vòng", $"Bạn đi {r.distance:0} m trong khi tuyến ngắn nhất khoảng {r.optimalDistance:0} m.",
                    "Đi vòng hoặc đi lạc.", "Định hướng — chưa nắm rõ sơ đồ lối thoát.", "Kéo dài thời gian trong vùng nguy hiểm.",
                    "two_exits", "Drill B – Navigation.");

            if (r.reversals >= 3)
                Add("reversals", Skill.Navigation, 1, "Đổi hướng liên tục", $"Bạn quay đầu {r.reversals} lần.",
                    "Di chuyển thiếu kế hoạch.", "Định hướng — mất phương hướng.", "Lãng phí thời gian, dễ hoảng loạn.", "observe", "Drill B – Navigation.");

            if (!failed && r.maxExposure > 60f)
                Add("high_exposure", Skill.RiskRecognition, 1, "Suýt ngạt", $"Mức phơi nhiễm cao nhất {r.maxExposure:0}/100.",
                    "Bạn thoát được nhưng sát ngưỡng nguy hiểm.", "Nhận diện.", "Một chút chậm trễ nữa là thất bại.", "low", "Drill D – Physical Practice.");

            // ---------- Điểm tốt ----------
            if (r.alarmTime >= 0f && r.alarmTime - inc <= 12f)
                Good("alarm_fast", Skill.Reaction, "Báo động kịp thời", $"Kích hoạt chuông sau {Txt.Secs(r.alarmTime - inc)}.", "alarm", r.alarmTime);
            if (fl.Contains("closed_door_behind"))
                Good("closed_door", Skill.DecisionMaking, "Đóng cửa phía sau", "Bạn đóng cửa khu vực có lửa/khói khi rời đi.", "door_close", TimeOf("door_close_good"));
            if (fl.Contains("checked_door") && !fl.Contains("ignored_hot_door") && !fl.Contains("opened_hot_door"))
                Good("checked_door", Skill.RiskRecognition, "Kiểm tra cửa trước khi mở", "Bạn kiểm tra độ nóng trước khi mở cửa.", "door_check");
            if (r.blockedExit != null && r.recoveries > 0 && r.recoveryTotal <= 5f)
                Good("fast_recovery", Skill.Adaptability, "Đổi hướng nhanh", $"Chỉ mất {Txt.Secs(r.recoveryTotal)} để chuyển sang lối khác.", "two_exits");
            if ((fl.Contains("npc_helped") || fl.Contains("npc_instructed")) && !fl.Contains("unsafe_rescue"))
                Good("helped", Skill.DecisionMaking, "Hỗ trợ người khác an toàn", "Bạn hỗ trợ người gặp khó khăn mà không tự đặt mình vào vùng nguy hiểm.", "help_assess");
            if (r.npcs.Any(n => n.reported))
                Good("reported", Skill.DecisionMaking, "Báo vị trí người bị kẹt", "Bạn báo cho lực lượng cứu hộ vị trí người còn kẹt bên trong.", "report");
            if (fl.Contains("head_in_smoke") && r.timeUprightInSmoke <= 3f)
                Good("stayed_low", Skill.RiskRecognition, "Giữ người thấp trong khói", "Bạn cúi thấp khi đi qua khu vực có khói.", "low");
            if (fl.Contains("extinguished") && !fl.Contains("fought_large_fire"))
                Good("extinguished", Skill.DecisionMaking, "Dập đám cháy nhỏ đúng lúc", "Bạn dập tắt đám cháy khi nó còn nhỏ.", "extinguish");
            if (fl.Contains("pass_pull") && fl.Contains("pass_aim") && fl.Contains("pass_sweep"))
                Good("pass", Skill.RiskRecognition, "Thực hiện đủ PASS", "Pull → Aim → Squeeze → Sweep.", "pass");
            if (fl.Contains("signaled_window"))
                Good("signal", Skill.DecisionMaking, "Ra tín hiệu khi bị kẹt", "Khi không còn lối thoát, bạn ra tín hiệu ở cửa sổ thay vì liều lĩnh.", "trapped");

            // ---------- Điểm số ----------
            int Pen(int sev) => sev switch { 1 => 8, 2 => 18, 3 => 35, _ => 0 };
            rep.safety = 100f - r.maxExposure * 0.5f - r.timeUprightInSmoke * 1.5f - r.timeNearFire * 4f
                         - (fl.Contains("used_elevator") ? 60f : 0f)
                         - (fl.Contains("opened_hot_door") || fl.Contains("ignored_hot_door") ? 20f : 0f)
                         - (fl.Contains("unsafe_rescue") ? 15f : 0f);
            if (failed) rep.safety = Mathf.Min(rep.safety, 25f);

            rep.decision = 100f
                - F.Where(f => !f.positive && f.key != "outcome" && (f.skill == Skill.DecisionMaking || f.skill == Skill.Adaptability || f.skill == Skill.RiskRecognition)).Sum(f => Pen(f.severity))
                + F.Count(f => f.positive) * 3f;
            if (failed) rep.decision = Mathf.Min(rep.decision, 50f);

            float ratio = rep.totalTime / Mathf.Max(5f, r.def.parTime);
            rep.timeScore = ratio <= 1f ? 100f : Mathf.Lerp(100f, 0f, (ratio - 1f) / 2f);
            if (failed) rep.timeScore *= 0.4f;

            rep.route = r.escapedVia != null && r.distance > 0.5f && r.optimalDistance > 0f
                ? Mathf.Clamp01(r.optimalDistance / r.distance) * 100f
                : (drill ? 70f : 20f);
            if (r.blockedExit != null && r.escapedVia != null) rep.route = Mathf.Max(rep.route, 60f);

            int needless = Mathf.Max(0, r.routeChanges - r.recoveries);
            rep.consistency = 100f - r.reversals * 8f - r.idleTime * 2f - needless * 10f;

            rep.safety = Clamp(rep.safety); rep.decision = Clamp(rep.decision); rep.timeScore = Clamp(rep.timeScore);
            rep.route = Clamp(rep.route); rep.consistency = Clamp(rep.consistency);
            rep.total = rep.safety * 0.3f + rep.decision * 0.3f + rep.timeScore * 0.15f + rep.route * 0.1f + rep.consistency * 0.15f;

            bool fast = ratio <= 1.1f;
            bool safe = rep.safety >= 70f && !F.Any(f => !f.positive && f.severity >= 3);
            rep.verdict = fast && safe ? "Fast + Safe" : fast ? "Fast + High Risk" : safe ? "Slow + Safe" : "Slow + High Risk";
            rep.headline = failed ? "THẤT BẠI" : drill ? "HOÀN THÀNH DRILL" : "THOÁT HIỂM THÀNH CÔNG";

            // ---------- Kỹ năng đo được trong lượt này ----------
            if (rep.T1 >= 0f)
            {
                float react = Mathf.Lerp(100f, 0f, Mathf.InverseLerp(2f, 20f, rep.T1));
                if (r.alarmTime >= 0f) react = (react + Mathf.Lerp(100f, 0f, Mathf.InverseLerp(4f, 30f, r.alarmTime - inc))) * 0.5f;
                else if (!drill && !fl.Contains("alarm_auto")) react *= 0.7f;
                rep.skills[Skill.Reaction] = react;
            }
            rep.skills[Skill.RiskRecognition] = Clamp(100f - r.timeUprightInSmoke * 4f - r.timeNearFire * 6f
                - (fl.Contains("opened_hot_door") || fl.Contains("ignored_hot_door") ? 30f : 0f)
                - (fl.Contains("fought_large_fire") ? 20f : 0f) - (fl.Contains("unsafe_rescue") ? 20f : 0f)
                - (failed && r.reason != null && r.reason.Contains("khói") ? 30f : 0f));
            rep.skills[Skill.DecisionMaking] = rep.decision;
            if (!drill || r.def.request.drill == DrillType.Navigation)
                rep.skills[Skill.Navigation] = Clamp(rep.route - r.reversals * 5f);
            rep.skills[Skill.TimeManagement] = Clamp(rep.timeScore - r.idleTime * 2f);
            if (r.blockedExit != null)
                rep.skills[Skill.Adaptability] = r.recoveries > 0
                    ? Mathf.Lerp(100f, 0f, Mathf.InverseLerp(3f, 20f, r.recoveryTotal))
                    : 10f;

            if (rep.skills.Count > 0) rep.weakest = rep.skills.OrderBy(kv => kv.Value).First().Key;

            if (drill)
            {
                switch (r.def.request.drill)
                {
                    case DrillType.Reaction:
                        rep.drillResult = r.alarmTime >= 0f ? $"Thời gian từ sự cố tới báo động: {Txt.Secs(r.alarmTime - inc)} (mục tiêu ≤ 10s)" : "Chưa kích hoạt báo động.";
                        break;
                    case DrillType.Interaction:
                        rep.drillResult = $"PASS: Pull {(fl.Contains("pass_pull") ? "[x]" : "[ ]")} · Aim {(fl.Contains("pass_aim") ? "[x]" : "[ ]")} · Squeeze {(fl.Contains("used_extinguisher") ? "[x]" : "[ ]")} · Sweep {(fl.Contains("pass_sweep") ? "[x]" : "[ ]")}";
                        break;
                    case DrillType.Physical:
                        rep.drillResult = $"Thời gian đầu nằm trong khói: {r.timeUprightInSmoke:0.0}s · Phơi nhiễm cao nhất {r.maxExposure:0}/100";
                        break;
                    default:
                        rep.drillResult = $"Thời gian tới lối thoát: {Txt.Secs(rep.totalTime)} · Quãng đường {r.distance:0} m";
                        break;
                }
            }
            return rep;
        }

        static float Clamp(float v) => Mathf.Clamp(v, 0f, 100f);
    }
}
