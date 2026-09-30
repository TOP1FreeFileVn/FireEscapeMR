using System.Collections.Generic;
using UnityEngine;

namespace FireEscape
{
    public enum TriggerType { AtTime, FlagMissingAt, OnFlag, OnEnterZone }

    public enum EffectType
    {
        Ignite, Smoke, SetSmoke, SmokeRoute, PowerOff, BlockPortal, BlockRoute,
        SpawnNpc, Announce, Spread, Flag, Hint, Door, Alarm
    }

    public class ScenarioEffect
    {
        public EffectType type;
        public string target;
        public float value, value2;
        public string text;
        public bool on = true;
        public NpcNeed need;
        public BlockCause cause;
    }

    /// <summary>Một diễn biến trong kịch bản: điều kiện kích hoạt + các hiệu ứng lên môi trường.</summary>
    public class ScenarioEvent
    {
        public TriggerType trigger;
        public float time;      // AtTime/FlagMissingAt: thời điểm; OnEnterZone: hạn chót (0 = không giới hạn)
        public string key;      // flag hoặc zone
        public float delay;
        public string narrative;
        public LogCategory category = LogCategory.Hazard;
        public readonly List<ScenarioEffect> effects = new List<ScenarioEffect>();

        // trạng thái runtime
        public bool fired;
        public float armedAt = -1f;

        ScenarioEvent Add(ScenarioEffect e) { effects.Add(e); return this; }

        public ScenarioEvent Ignite(string where, float intensity, float growth) =>
            Add(new ScenarioEffect { type = EffectType.Ignite, target = where, value = intensity, value2 = growth });
        public ScenarioEvent Smoke(string zone, float ratePerSec) =>
            Add(new ScenarioEffect { type = EffectType.Smoke, target = zone, value = ratePerSec });
        public ScenarioEvent SetSmoke(string zone, float level) =>
            Add(new ScenarioEffect { type = EffectType.SetSmoke, target = zone, value = level });
        public ScenarioEvent SmokeRoute(float ratePerSec) =>
            Add(new ScenarioEffect { type = EffectType.SmokeRoute, value = ratePerSec });
        public ScenarioEvent PowerOff(bool emergencyLights, string darkZone = null) =>
            Add(new ScenarioEffect { type = EffectType.PowerOff, on = emergencyLights, target = darkZone });
        public ScenarioEvent Block(string portal, BlockCause cause) =>
            Add(new ScenarioEffect { type = EffectType.BlockPortal, target = portal, cause = cause });
        public ScenarioEvent BlockRoute(BlockCause cause) =>
            Add(new ScenarioEffect { type = EffectType.BlockRoute, cause = cause });
        public ScenarioEvent Npc(string zone, NpcNeed need, float offsetX = 0f, float offsetZ = 0f) =>
            Add(new ScenarioEffect { type = EffectType.SpawnNpc, target = zone, need = need, value = offsetX, value2 = offsetZ });
        public ScenarioEvent Announce(string text, bool reliable, string pointsToExit = null) =>
            Add(new ScenarioEffect { type = EffectType.Announce, text = text, on = reliable, target = pointsToExit });
        public ScenarioEvent Spread(float multiplier) =>
            Add(new ScenarioEffect { type = EffectType.Spread, value = multiplier });
        public ScenarioEvent Flag(string flag) =>
            Add(new ScenarioEffect { type = EffectType.Flag, target = flag });
        public ScenarioEvent Hint(string text) =>
            Add(new ScenarioEffect { type = EffectType.Hint, text = text });
        public ScenarioEvent Door(string portal, bool open) =>
            Add(new ScenarioEffect { type = EffectType.Door, target = portal, on = open });
        public ScenarioEvent Alarm() =>
            Add(new ScenarioEffect { type = EffectType.Alarm });
        public ScenarioEvent As(LogCategory c) { category = c; return this; }
    }

    public struct ScenarioRequest
    {
        public ScenarioType type;
        public int level;
        public int seed;
        public GuidanceMode guidance;
        public bool stress;
        public DrillType drill;

        public string Describe() =>
            drill != DrillType.None
                ? Txt.Name(drill)
                : $"{Txt.Name(type)} · Level {level} · {Txt.Name(guidance)}{(stress ? " · Stress" : "")}";
    }

    /// <summary>
    /// Cấu trúc chuẩn của một Scenario:
    /// Nguyên nhân → Dấu hiệu ban đầu → Tình huống phát triển → (người chơi quan sát/quyết định)
    /// → Hệ thống phản ứng → Tình huống thay đổi → (người chơi thích nghi) → Escape / Fail.
    /// </summary>
    public class ScenarioDef
    {
        public ScenarioRequest request;
        public string title, variant;
        public string cause, initialSigns, development, learningGoal, briefing;
        public float parTime = 60f, timeScale = 1f, timeLimit = 240f;
        public Vector3? startPos;
        public float? startYaw;
        public string familiarExit;
        public string objective = "exit";   // exit | alarm | extinguish | exit_low
        public readonly List<ScenarioEvent> events = new List<ScenarioEvent>();
        public readonly List<string> skillsTrained = new List<string>();

        ScenarioEvent New(TriggerType t, float time, string key, float delay, string narrative)
        {
            var e = new ScenarioEvent { trigger = t, time = time, key = key, delay = delay, narrative = narrative };
            events.Add(e);
            return e;
        }

        public ScenarioEvent At(float t, string narrative = null) => New(TriggerType.AtTime, t, null, 0f, narrative);
        public ScenarioEvent IfMissing(string flag, float t, string narrative = null) => New(TriggerType.FlagMissingAt, t, flag, 0f, narrative);
        public ScenarioEvent OnFlag(string flag, float delay, string narrative = null) => New(TriggerType.OnFlag, 0f, flag, delay, narrative);
        public ScenarioEvent OnEnter(string zone, float delay, string narrative = null, float deadline = 0f) => New(TriggerType.OnEnterZone, deadline, zone, delay, narrative);
    }
}
