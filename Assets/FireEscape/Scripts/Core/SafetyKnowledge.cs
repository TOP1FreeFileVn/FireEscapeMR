using System.Collections.Generic;

namespace FireEscape
{
    /// <summary>
    /// Nguyên tắc ứng xử cố định (không để AI tự sinh quy tắc). AI Error Analysis chỉ được trích dẫn
    /// các nguyên tắc trong danh sách này. Nội dung cần được đơn vị PCCC địa phương rà soát trước khi dùng thật.
    /// </summary>
    public class SafetyRule
    {
        public string id, title, text;
        public SafetyRule(string id, string title, string text) { this.id = id; this.title = title; this.text = text; }
    }

    public class KnowledgeCard
    {
        public string title, body, meaning;
        public KnowledgeCard(string title, string body, string meaning) { this.title = title; this.body = body; this.meaning = meaning; }
    }

    public class KnowledgeModule
    {
        public string code, title, goal;
        public List<KnowledgeCard> cards = new List<KnowledgeCard>();
    }

    public static class SafetyKnowledge
    {
        public static readonly Dictionary<string, SafetyRule> Rules = new Dictionary<string, SafetyRule>();

        static void R(string id, string title, string text) => Rules[id] = new SafetyRule(id, title, text);

        static SafetyKnowledge()
        {
            R("alarm", "Báo động ngay", "Khi phát hiện cháy: hô hoán, kích hoạt chuông/nút báo cháy gần nhất và gọi 114. Báo động sớm giúp mọi người có thêm thời gian sơ tán.");
            R("low", "Cúi thấp dưới khói", "Khói nóng bốc lên trần và tích tụ dần xuống. Không khí sạch hơn ở sát sàn: cúi thấp hoặc bò, che mũi miệng khi đi qua khói.");
            R("door_check", "Kiểm tra cửa trước khi mở", "Dùng mu bàn tay chạm vào cửa và tay nắm. Cửa nóng: không mở, tìm lối khác. Cửa không nóng: mở từ từ, đứng nép sau cánh cửa.");
            R("door_close", "Đóng cửa phía sau", "Đóng cửa khi rời phòng cháy để làm chậm lửa và khói lan ra lối thoát của người khác.");
            R("no_elevator", "Không dùng thang máy", "Thang máy có thể mất điện, kẹt giữa tầng hoặc mở ra đúng tầng cháy. Luôn dùng thang bộ thoát nạn.");
            R("two_exits", "Luôn biết hai lối thoát", "Xác định ít nhất hai lối thoát. Khi lối thứ nhất không an toàn, chuyển sang lối thứ hai ngay, không cố chấp.");
            R("extinguish", "Chỉ dập khi đám cháy nhỏ", "Chỉ dùng bình chữa cháy khi đám cháy nhỏ, mới bắt đầu, có lối thoát sau lưng và đã báo động. Nếu lửa lan rộng hoặc khói dày: thoát ra ngay.");
            R("pass", "Kỹ thuật PASS", "Pull – rút chốt; Aim – chĩa vòi vào gốc lửa; Squeeze – bóp cò; Sweep – quét qua lại. Đứng cách đám cháy khoảng 2–3 m.");
            R("electrical", "Cháy điện", "Không dùng nước với cháy điện. Ngắt điện nếu làm được an toàn, dùng bình CO2 hoặc bột.");
            R("no_return", "Không quay lại", "Không quay lại lấy đồ đạc. Không chần chừ quan sát quá lâu khi tình huống đang xấu đi.");
            R("window", "Cửa sổ không phải lối thoát", "Ở tầng cao, cửa sổ chỉ dùng để ra tín hiệu khi thật sự bị kẹt. Không nhảy xuống.");
            R("trapped", "Khi bị kẹt", "Đóng cửa, chèn khe cửa bằng vải, gọi báo vị trí, ra tín hiệu ở cửa sổ và giữ người thấp.");
            R("help_assess", "Đánh giá trước khi hỗ trợ", "Hỗ trợ người khác khi việc đó không đặt bạn vào vùng lửa/khói nguy hiểm. Nếu không thể tiếp cận an toàn: hướng dẫn từ xa và báo vị trí cho lực lượng cứu hộ.");
            R("report", "Báo cho cứu hộ", "Tới điểm tập kết, báo ngay người còn kẹt bên trong và vị trí của họ cho lực lượng chữa cháy.");
            R("verify_info", "Kiểm chứng thông tin", "Khi nhận thông tin trái chiều, tin vào điều bạn quan sát trực tiếp (khói, lửa, biển EXIT) hơn là lời nói vội vàng.");
            R("observe", "Quan sát trước khi chạy", "Dành vài giây quan sát hướng lửa và khói trước khi chọn tuyến. Chạy ngay theo phản xạ dễ lao vào vùng nguy hiểm.");
            Modules = BuildModules(); // phải chạy sau khi Rules đã được nạp
        }

        public static SafetyRule Rule(string id) => Rules.TryGetValue(id, out var r) ? r : null;

        public static readonly List<KnowledgeModule> Modules;

        static List<KnowledgeModule> BuildModules()
        {
            var list = new List<KnowledgeModule>();

            var m1 = new KnowledgeModule { code = "1.1", title = "Nhận diện nguy hiểm", goal = "Nhận ra sự cố trước khi nó trở nên rõ ràng." };
            m1.cards.Add(new KnowledgeCard("Dấu hiệu bất thường",
                "Mùi khét của nhựa cháy, tiếng lách tách từ ổ điện, đèn nhấp nháy, thiết bị nóng bất thường, khói mỏng bốc lên từ khe cửa hoặc dưới bàn.",
                "Đây là giai đoạn có nhiều thời gian nhất để phản ứng. Không chờ thấy lửa mới hành động."));
            m1.cards.Add(new KnowledgeCard("Nguồn nguy hiểm",
                "Ổ cắm quá tải, dây điện hở, thiết bị sạc, máy chiếu, bảng điện; hóa chất trong phòng thí nghiệm; rác và giấy gần nguồn nhiệt.",
                "Biết nguồn nguy hiểm giúp đoán được sự cố sẽ phát triển về hướng nào."));
            m1.cards.Add(new KnowledgeCard("Khói",
                "Khói nóng bốc lên trần rồi dày dần xuống. Khói đen, dày, cuộn nhanh là dấu hiệu đám cháy lớn. Khói làm giảm tầm nhìn và gây ngạt nhanh.",
                "Khói nguy hiểm không kém lửa: phần lớn nạn nhân bị ngạt khói trước khi bị bỏng."));
            m1.cards.Add(new KnowledgeCard("Lửa",
                "Lửa nhỏ có thể lan rộng gấp đôi trong vòng khoảng một phút. Lửa ở gần lối ra hoặc giữa bạn và lối ra là tình huống phải thoát ngay.",
                "Vị trí của lửa so với lối thoát quan trọng hơn kích thước của nó."));
            m1.cards.Add(new KnowledgeCard("Cảnh báo",
                "Chuông báo cháy, đèn chớp, loa thông báo, tiếng hô hoán. Thông tin có thể trái chiều khi hỗn loạn.",
                "Cảnh báo là lệnh hành động, không phải tín hiệu để chờ xem."));
            m1.cards.Add(new KnowledgeCard("Các lối thoát",
                "Biển EXIT màu xanh, đèn thoát hiểm vẫn sáng khi mất điện, sơ đồ thoát nạn ở hành lang, thang bộ thoát hiểm.",
                "Luôn biết ít nhất hai lối thoát từ nơi bạn đang đứng."));
            m1.cards.Add(new KnowledgeCard("Tình huống có thể thay đổi",
                "Lối thoát an toàn lúc đầu có thể bị khói tràn vào sau 30 giây. Điện có thể mất giữa chừng.",
                "Kế hoạch ban đầu chỉ là giả thuyết. Liên tục quan sát và sẵn sàng đổi hướng."));
            list.Add(m1);

            var m2 = new KnowledgeModule { code = "1.2", title = "Quy tắc ứng xử theo tình huống", goal = "Những nguyên tắc đã được kiểm chứng, không để AI tự tạo quy tắc." };
            foreach (var id in new[] { "alarm", "low", "door_check", "door_close", "no_elevator", "two_exits", "extinguish", "electrical", "no_return", "trapped", "help_assess", "report" })
            {
                var r = Rules[id];
                m2.cards.Add(new KnowledgeCard(r.title, r.text, "Nguyên tắc này được dùng để chấm điểm và phân tích lỗi sau mỗi nhiệm vụ."));
            }
            list.Add(m2);

            var m3 = new KnowledgeModule { code = "1.3", title = "Equipment Familiarization", goal = "Tôi biết mình đang nhìn thấy cái gì và nó có ý nghĩa gì." };
            m3.cards.Add(new KnowledgeCard("Fire extinguisher — Bình chữa cháy",
                "Bình màu đỏ treo ở hành lang hoặc gần cửa. Bình CO2 (loa phun to) và bột (đồng hồ áp suất) dùng được cho cháy điện. Thao tác PASS: rút chốt → chĩa vào gốc lửa → bóp cò → quét qua lại.",
                "Trong mô phỏng: nhấn E để cầm, E lần nữa để rút chốt, giữ E để phun, di chuyển tầm nhìn qua lại để quét."));
            m3.cards.Add(new KnowledgeCard("Fire alarm — Nút/chuông báo cháy",
                "Hộp màu đỏ gắn tường, thường gần cửa ra hoặc cầu thang. Nhấn/kéo để kích hoạt chuông toàn tòa nhà.",
                "Kích hoạt sớm giúp mọi người sơ tán sớm. Trong mô phỏng: nhìn vào hộp đỏ và nhấn E."));
            m3.cards.Add(new KnowledgeCard("Emergency exit — Lối thoát khẩn cấp",
                "Biển EXIT xanh lá có mũi tên; cửa chống cháy dẫn vào thang bộ; đèn thoát hiểm dùng pin vẫn sáng khi mất điện.",
                "Đi theo biển EXIT, không đi theo đám đông một cách mù quáng."));
            m3.cards.Add(new KnowledgeCard("Emergency equipment — Thiết bị khác",
                "Chăn chống cháy, mặt nạ lọc độc, đèn pin, hộp cứu thương, sơ đồ thoát nạn.",
                "Thiết bị chỉ hữu ích khi bạn biết nó ở đâu trước khi sự cố xảy ra."));
            m3.cards.Add(new KnowledgeCard("Thang máy",
                "Biển cảnh báo \"Không sử dụng thang máy khi có cháy\" thường dán cạnh cửa thang máy.",
                "Trong mô phỏng, thang máy là một lựa chọn bẫy. Dùng nó sẽ kết thúc nhiệm vụ."));
            list.Add(m3);

            return list;
        }
    }
}
