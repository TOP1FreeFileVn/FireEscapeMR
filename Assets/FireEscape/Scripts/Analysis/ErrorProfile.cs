using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FireEscape
{
    [Serializable] public class SkillEntry { public Skill skill; public float value = 50f; public int samples; public float last = -1f; }
    [Serializable] public class CountEntry { public string key; public int count; }
    [Serializable] public class LevelEntry { public ScenarioType type; public int cleared; }

    [Serializable]
    public class MissionSummary
    {
        public string date, title, outcome, verdict, weakest;
        public ScenarioType type;
        public DrillType drill;
        public int level;
        public float total, responseTime;
    }

    [Serializable]
    public class ProfileData
    {
        public List<SkillEntry> skills = new List<SkillEntry>();
        public List<CountEntry> errors = new List<CountEntry>();
        public List<LevelEntry> levels = new List<LevelEntry>();
        public List<MissionSummary> history = new List<MissionSummary>();
        public int missions;
    }

    /// <summary>Personal Error Profile: không chỉ lưu điểm mà lưu "hồ sơ phản ứng" qua nhiều nhiệm vụ.</summary>
    public class ErrorProfile
    {
        public ProfileData data = new ProfileData();
        string FilePath => Path.Combine(Application.persistentDataPath, "fireescape_profile.json");

        public static ErrorProfile Load()
        {
            var p = new ErrorProfile();
            try
            {
                if (File.Exists(p.FilePath)) p.data = JsonUtility.FromJson<ProfileData>(File.ReadAllText(p.FilePath)) ?? new ProfileData();
            }
            catch (Exception e) { Debug.LogWarning("FireEscape: không đọc được hồ sơ — " + e.Message); }
            foreach (var s in Txt.AllSkills)
                if (p.data.skills.All(x => x.skill != s)) p.data.skills.Add(new SkillEntry { skill = s });
            return p;
        }

        public void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(data, true)); }
            catch (Exception e) { Debug.LogWarning("FireEscape: không lưu được hồ sơ — " + e.Message); }
        }

        public void Reset()
        {
            data = new ProfileData();
            foreach (var s in Txt.AllSkills) data.skills.Add(new SkillEntry { skill = s });
            Save();
        }

        public SkillEntry Get(Skill s) => data.skills.First(x => x.skill == s);
        public bool HasData => data.missions > 0;

        public int ErrorCount(string key) => data.errors.FirstOrDefault(e => e.key == key)?.count ?? 0;

        public int ClearedLevel(ScenarioType t) => data.levels.FirstOrDefault(l => l.type == t)?.cleared ?? 0;

        public void Record(MissionRecord r, EvaluationReport rep)
        {
            data.missions++;
            foreach (var kv in rep.skills)
            {
                var e = Get(kv.Key);
                e.value = e.samples == 0 ? kv.Value : Mathf.Lerp(e.value, kv.Value, 0.35f);
                e.last = kv.Value;
                e.samples++;
            }
            foreach (var f in rep.Problems)
            {
                var c = data.errors.FirstOrDefault(x => x.key == f.key);
                if (c == null) data.errors.Add(c = new CountEntry { key = f.key });
                c.count++;
            }
            var req = r.def.request;
            if (req.drill == DrillType.None && rep.outcome == MissionOutcome.Escaped && rep.total >= 60f)
            {
                var l = data.levels.FirstOrDefault(x => x.type == req.type);
                if (l == null) data.levels.Add(l = new LevelEntry { type = req.type });
                l.cleared = Mathf.Max(l.cleared, req.level);
            }
            data.history.Insert(0, new MissionSummary
            {
                date = DateTime.Now.ToString("dd/MM HH:mm"),
                title = r.def.title,
                type = req.type, drill = req.drill, level = req.level,
                outcome = rep.headline, verdict = rep.verdict,
                total = rep.total, responseTime = rep.totalTime,
                weakest = rep.weakest.HasValue ? Txt.Name(rep.weakest.Value) : ""
            });
            if (data.history.Count > 30) data.history.RemoveRange(30, data.history.Count - 30);
            Save();
        }

        public Skill Weakest()
        {
            var measured = data.skills.Where(s => s.samples > 0).ToList();
            if (measured.Count == 0) return Skill.Reaction;
            return measured.OrderBy(s => s.value).First().skill;
        }

        public float Average()
        {
            var m = data.skills.Where(s => s.samples > 0).ToList();
            return m.Count == 0 ? 0f : m.Average(s => s.value);
        }

        /// <summary>Insight có giá trị hơn một con số: mô tả khuôn mẫu hành vi lặp lại.</summary>
        public List<string> Insights()
        {
            var list = new List<string>();
            if (!HasData) { list.Add("Chưa có dữ liệu. Hoàn thành một nhiệm vụ để AI bắt đầu xây dựng hồ sơ phản ứng của bạn."); return list; }
            float V(Skill s) => Get(s).samples > 0 ? Get(s).value : -1f;

            if (V(Skill.Reaction) >= 65f && V(Skill.Adaptability) >= 0f && V(Skill.Adaptability) < 50f)
                list.Add("Bạn phản ứng nhanh nhưng thường mất nhiều thời gian khi tuyến đường ban đầu bị thay đổi.");
            if (V(Skill.Reaction) >= 0f && V(Skill.Reaction) < 45f && V(Skill.DecisionMaking) >= 65f)
                list.Add("Khi đã hành động, quyết định của bạn khá tốt — nhưng bạn nhận ra sự cố quá muộn.");
            if (V(Skill.TimeManagement) >= 70f && V(Skill.RiskRecognition) >= 0f && V(Skill.RiskRecognition) < 50f)
                list.Add("Bạn thoát ra nhanh nhưng chấp nhận rủi ro cao (Fast + High Risk). Tốc độ không bù được phơi nhiễm khói.");
            if (V(Skill.RiskRecognition) >= 70f && V(Skill.TimeManagement) >= 0f && V(Skill.TimeManagement) < 45f)
                list.Add("Bạn rất thận trọng nhưng chậm — cẩn thận quá mức cũng là rủi ro khi tình huống đang xấu đi.");
            if (V(Skill.Navigation) >= 0f && V(Skill.Navigation) < 50f)
                list.Add("Bạn thường đi vòng hoặc quay đầu nhiều lần — hãy xác định hai lối thoát ngay từ đầu.");

            void Pattern(string key, string text) { int c = ErrorCount(key); if (c >= 2) list.Add($"{text} ({c} lần)."); }
            Pattern("upright_smoke", "Lặp lại: đi thẳng người trong khói thay vì cúi thấp");
            Pattern("no_alarm", "Lặp lại: rời đi mà không kích hoạt báo động");
            Pattern("hot_door", "Lặp lại: mở cửa đóng mà không kiểm tra độ nóng");
            Pattern("slow_recovery", "Lặp lại: chậm đổi hướng khi lối thoát bị chặn");
            Pattern("large_fire", "Lặp lại: cố dập đám cháy đã quá lớn");
            Pattern("hesitation", "Lặp lại: chần chừ quá lâu");
            Pattern("misinfo", "Lặp lại: tin vào thông tin sai lệch");
            Pattern("slow_reaction", "Lặp lại: phản ứng chậm với dấu hiệu ban đầu");

            if (list.Count == 0)
                list.Add($"Điểm yếu tương đối hiện tại: {Txt.Name(Weakest())} ({Get(Weakest()).value:0}). Chưa phát hiện khuôn mẫu lỗi lặp lại.");
            return list;
        }
    }

    /// <summary>
    /// Adaptive AI: PLAY → DECISION → FAIL/SUCCESS → ANALYSIS → WEAKNESS → TARGETED TRAINING → PLAY AGAIN.
    /// </summary>
    public static class AdaptiveCoach
    {
        public class Recommendation
        {
            public ScenarioRequest request;
            public string reason;
            public DrillType suggestedDrill;
        }

        public static Recommendation Recommend(ErrorProfile p, EvaluationReport last = null)
        {
            var rec = new Recommendation();
            var r = new ScenarioRequest { seed = UnityEngine.Random.Range(1, 1_000_000), level = 1, guidance = GuidanceMode.Beginner };

            if (!p.HasData)
            {
                r.type = ScenarioType.ElectricalFire;
                rec.request = r;
                rec.suggestedDrill = DrillType.Reaction;
                rec.reason = "Bắt đầu từ tình huống nền tảng: Chập điện Level 1, có hướng dẫn.";
                return rec;
            }

            Skill w = last != null && last.weakest.HasValue && last.skills[last.weakest.Value] < 50f ? last.weakest.Value : p.Weakest();
            var e = p.Get(w);
            var pool = w switch
            {
                Skill.Reaction => new[] { ScenarioType.ElectricalFire, ScenarioType.FireSpreading },
                Skill.RiskRecognition => new[] { ScenarioType.SmokeFilledRoom, ScenarioType.FireSpreading, ScenarioType.PersonInNeed },
                Skill.DecisionMaking => new[] { ScenarioType.DecisionTrap, ScenarioType.MultipleHazards, ScenarioType.PersonInNeed },
                Skill.Navigation => new[] { ScenarioType.PowerFailure, ScenarioType.SmokeFilledRoom },
                Skill.TimeManagement => new[] { ScenarioType.MultipleHazards, ScenarioType.DecisionTrap },
                _ => new[] { ScenarioType.BlockedExit }
            };
            r.type = pool[UnityEngine.Random.Range(0, pool.Length)];
            if (last != null && pool.Length > 1 && r.type == ScenarioType.DecisionTrap && last.weakest == w) r.type = pool[1];

            int cleared = p.ClearedLevel(r.type);
            r.level = Mathf.Clamp(cleared + 1, 1, 4);
            if (e.value < 35f) r.level = Mathf.Max(1, r.level - 1);

            float avg = p.Average();
            r.guidance = avg < 45f ? GuidanceMode.Beginner : avg < 72f ? GuidanceMode.Intermediate : GuidanceMode.Expert;
            r.stress = w == Skill.TimeManagement || avg >= 75f;

            rec.suggestedDrill = w switch
            {
                Skill.Reaction => DrillType.Reaction,
                Skill.Navigation => DrillType.Navigation,
                Skill.RiskRecognition => DrillType.Physical,
                _ => DrillType.None
            };
            rec.request = r;
            string why = w switch
            {
                Skill.Reaction => "cần phản ứng sớm hơn với dấu hiệu ban đầu",
                Skill.RiskRecognition => "cần nhận diện khói và nhiệt là mối nguy hiểm",
                Skill.DecisionMaking => "cần luyện ra quyết định khi có bẫy và nhiều yếu tố cùng lúc",
                Skill.Navigation => "cần định hướng khi tầm nhìn và ánh sáng bị hạn chế",
                Skill.TimeManagement => "cần quyết định nhanh hơn dưới áp lực thời gian",
                _ => "cần thích nghi nhanh khi kế hoạch ban đầu thất bại"
            };
            rec.reason = $"Điểm yếu: {Txt.Name(w)} ({e.value:0}/100) — bạn {why}. " +
                         $"AI tạo: {Txt.Name(r.type)} Level {r.level}, {Txt.Name(r.guidance)}{(r.stress ? ", Stress Mode" : "")}.";
            return rec;
        }
    }
}
