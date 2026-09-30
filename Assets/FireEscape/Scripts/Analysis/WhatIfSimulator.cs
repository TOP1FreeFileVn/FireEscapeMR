using System.Collections.Generic;
using UnityEngine;

namespace FireEscape
{
    public class WhatIfResult
    {
        public string exitId, exitName;
        public float decisionTime;
        public MissionOutcome outcome;
        public string reason;
        public float endTime, maxExposure, timeInSmoke;
        public readonly List<PathSample> path = new List<PathSample>();
        public readonly List<SimLogEntry> events = new List<SimLogEntry>();
        public readonly List<SimSnapshot> snaps = new List<SimSnapshot>();
        public bool Escaped => outcome == MissionOutcome.Escaped || outcome == MissionOutcome.DrillComplete;
    }

    /// <summary>
    /// "What if you chose another route?" — dựng lại đúng kịch bản (cùng seed), phát lại chính xác hành trình và
    /// hành động của người chơi tới thời điểm quyết định, rồi để một tác tử đi theo nhánh khác.
    /// Hệ quả vẫn do Consequence Engine quyết định, nên nhánh khác cũng có thể gặp diễn biến mới.
    /// </summary>
    public static class WhatIfSimulator
    {
        public static float DecisionTime(MissionRecord r)
        {
            if (r.routeTime >= 0f) return r.routeTime;
            if (r.firstActionTime >= 0f) return r.firstActionTime;
            return Mathf.Max(0f, r.incidentTime);
        }

        public static List<WhatIfResult> RunAlternatives(MissionRecord r)
        {
            var list = new List<WhatIfResult>();
            string used = r.escapedVia ?? r.committedExit;
            float t = DecisionTime(r);
            foreach (var ex in r.layout.Exits)
                if (ex.id != used) list.Add(Run(r, ex.id, t));
            if (list.Count == 0 && used != null) list.Add(Run(r, used, Mathf.Max(0f, r.ResponseStart)));
            return list;
        }

        public static WhatIfResult Run(MissionRecord r, string exitId, float decisionTime)
        {
            var layout = r.layout;
            var def = ScenarioLibrary.Build(r.def.request, layout);
            var sim = new IncidentSimulation(layout, def);
            var res = new WhatIfResult { exitId = exitId, exitName = layout.GetZone(exitId)?.name, decisionTime = decisionTime };
            const float dt = 0.1f;
            int ai = 0;
            int logStart = 0;
            Vector3 pos = r.startPos;
            float nextSample = 0f, nextSnap = 0f, repath = 0f;
            List<Vector3> path = null;
            bool rerouted = false;

            while (sim.Running && sim.time < def.timeLimit)
            {
                bool crouch;
                if (sim.time < decisionTime)
                {
                    var s = r.SampleAt(sim.time);
                    pos = s.pos;
                    crouch = s.crouch;
                    while (ai < r.actions.Count && r.actions[ai].time <= sim.time)
                    {
                        var a = r.actions[ai++];
                        sim.Apply(new SimAction { type = a.type, target = a.target, value = a.value });
                    }
                    logStart = sim.log.Count;
                }
                else
                {
                    var z = layout.ZoneAt(pos);
                    var zs = sim.zones[z.id];
                    crouch = zs.smoke > 0.3f;
                    if (sim.time >= repath || path == null || path.Count == 0)
                    {
                        repath = sim.time + 0.5f;
                        path = layout.FindPath(pos, exitId, sim.IsPassable, out _);
                        if (path == null)
                        {
                            path = sim.SafestPath(pos, out var other);
                            if (!rerouted && other != null)
                            {
                                rerouted = true;
                                res.events.Add(new SimLogEntry { time = sim.time, cat = LogCategory.Decision, tag = "whatif_reroute", text = $"{res.exitName} bị chặn — nhánh này buộc phải chuyển sang {layout.GetZone(other).name}." });
                            }
                        }
                    }
                    if (path != null && path.Count > 0)
                    {
                        var next = path[0];
                        var dir = next - pos; dir.y = 0;
                        float speed = crouch ? 1.0f : 1.5f;
                        if (dir.magnitude < 0.3f) path.RemoveAt(0);
                        else pos += dir.normalized * Mathf.Min(speed * dt, dir.magnitude);
                    }
                }

                sim.SetAgent(pos, crouch ? 0.95f : 1.65f);
                sim.Step(dt);

                if (sim.time >= nextSample) { nextSample = sim.time + 0.2f; res.path.Add(new PathSample { t = sim.time, pos = pos, crouch = crouch }); }
                if (sim.time >= nextSnap) { nextSnap = sim.time + 0.5f; res.snaps.Add(sim.Capture()); }
            }

            for (int i = logStart; i < sim.log.Count; i++) res.events.Add(sim.log[i]);
            res.events.Sort((a, b) => a.time.CompareTo(b.time));
            res.outcome = sim.outcome == MissionOutcome.Running ? MissionOutcome.Failed : sim.outcome;
            res.reason = sim.outcomeReason ?? "Hết thời gian mô phỏng.";
            res.endTime = sim.time;
            res.maxExposure = sim.maxExposure;
            res.timeInSmoke = sim.timeUprightInSmoke;
            return res;
        }
    }
}
