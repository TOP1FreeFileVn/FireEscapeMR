using System;
using UnityEngine;

namespace FireEscape
{
    public enum ScenarioType { ElectricalFire, SmokeFilledRoom, BlockedExit, FireSpreading, PowerFailure, MultipleHazards, PersonInNeed, DecisionTrap }
    public enum DrillType { None, Reaction, Navigation, Interaction, Physical }
    public enum GuidanceMode { Beginner, Intermediate, Expert }
    public enum Skill { Reaction, RiskRecognition, DecisionMaking, Navigation, TimeManagement, Adaptability }
    public enum NpcNeed { Trapped, Lost, Panicked, Slow }
    public enum BlockCause { Fire, Smoke, Debris }
    public enum MissionOutcome { Running, Escaped, Failed, DrillComplete }
    public enum FixtureKind { AlarmStation, Extinguisher, Elevator, Window, ExitSign, Desk, PowerOutlet, Screen, ElectricalPanel, HoseCabinet, FireBlanket }
    public enum LogCategory { Incident, Action, Decision, Hazard, Route, Npc, Info, Outcome }

    public static class Txt
    {
        public static readonly ScenarioType[] AllScenarios = (ScenarioType[])Enum.GetValues(typeof(ScenarioType));
        public static readonly Skill[] AllSkills = (Skill[])Enum.GetValues(typeof(Skill));

        public static string Name(ScenarioType t) => t switch
        {
            ScenarioType.ElectricalFire => "Chập điện",
            ScenarioType.SmokeFilledRoom => "Phòng đầy khói",
            ScenarioType.BlockedExit => "Lối thoát bị chặn",
            ScenarioType.FireSpreading => "Cháy lan",
            ScenarioType.PowerFailure => "Mất điện",
            ScenarioType.MultipleHazards => "Nhiều nguy hiểm",
            ScenarioType.PersonInNeed => "Có người cần hỗ trợ",
            ScenarioType.DecisionTrap => "Decision Trap",
            _ => t.ToString()
        };

        public static string Name(Skill s) => s switch
        {
            Skill.Reaction => "Phản ứng",
            Skill.RiskRecognition => "Nhận diện rủi ro",
            Skill.DecisionMaking => "Ra quyết định",
            Skill.Navigation => "Định hướng",
            Skill.TimeManagement => "Quản lý thời gian",
            Skill.Adaptability => "Thích nghi",
            _ => s.ToString()
        };

        public static string Name(GuidanceMode g) => g switch
        {
            GuidanceMode.Beginner => "Beginner",
            GuidanceMode.Intermediate => "Intermediate",
            _ => "Expert"
        };

        public static string Describe(GuidanceMode g) => g switch
        {
            GuidanceMode.Beginner => "Có mũi tên chỉ đường và gợi ý",
            GuidanceMode.Intermediate => "Chỉ có thông tin hạn chế",
            _ => "Không mũi tên, không gợi ý, không đáp án"
        };

        public static string Name(DrillType d) => d switch
        {
            DrillType.Reaction => "Drill A – Reaction",
            DrillType.Navigation => "Drill B – Navigation",
            DrillType.Interaction => "Drill C – Interaction",
            DrillType.Physical => "Drill D – Physical Practice",
            _ => "Mission"
        };

        public static string Name(NpcNeed n) => n switch
        {
            NpcNeed.Trapped => "bị mắc kẹt",
            NpcNeed.Lost => "không biết đường",
            NpcNeed.Panicked => "đang hoảng loạn",
            NpcNeed.Slow => "di chuyển chậm",
            _ => n.ToString()
        };

        public static string Name(BlockCause c) => c switch
        {
            BlockCause.Fire => "lửa",
            BlockCause.Smoke => "khói dày",
            _ => "vật cản"
        };

        public static string Time(float seconds)
        {
            if (seconds < 0f || float.IsNaN(seconds) || float.IsInfinity(seconds)) return "--:--";
            int s = Mathf.FloorToInt(seconds);
            return $"{s / 60:00}:{s % 60:00}";
        }

        public static string Secs(float seconds) =>
            seconds < 0f || float.IsNaN(seconds) || float.IsInfinity(seconds) ? "—" : $"{seconds:0.0}s";
    }
}
