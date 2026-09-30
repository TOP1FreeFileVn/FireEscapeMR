using System.Collections.Generic;
using UnityEngine;

namespace FireEscape
{
    public class PathSample
    {
        public float t;
        public Vector3 pos;
        public float yaw;
        public bool crouch;
    }

    /// <summary>Toàn bộ dữ liệu của một lượt chơi: đủ để Replay, phân tích và chạy What-if.</summary>
    public class MissionRecord
    {
        public ScenarioDef def;
        public FacilityLayout layout;
        public readonly List<PathSample> path = new List<PathSample>();
        public readonly List<SimSnapshot> snaps = new List<SimSnapshot>();
        public List<SimLogEntry> log = new List<SimLogEntry>();
        public List<SimAction> actions = new List<SimAction>();
        public HashSet<string> flags = new HashSet<string>();
        public List<NpcState> npcs = new List<NpcState>();

        public MissionOutcome outcome;
        public string reason;
        public float endTime;
        public float incidentTime = -1f, firstActionTime = -1f, routeTime = -1f, alarmTime = -1f;
        public float recoveryTotal;
        public int recoveries, routeChanges, reversals;
        public float idleTime, distance, optimalDistance = -1f;
        public float maxExposure, timeUprightInSmoke, timeNearFire;
        public string escapedVia, committedExit, blockedExit;
        public Vector3 startPos;

        public float ResponseStart => incidentTime >= 0f ? incidentTime : 0f;

        public SimSnapshot SnapshotAt(float t)
        {
            if (snaps.Count == 0) return null;
            int lo = 0, hi = snaps.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (snaps[mid].time <= t) lo = mid; else hi = mid - 1;
            }
            return snaps[lo];
        }

        public PathSample SampleAt(float t)
        {
            if (path.Count == 0) return new PathSample { t = t, pos = startPos };
            if (t <= path[0].t) return path[0];
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i].t < t) continue;
                var a = path[i - 1]; var b = path[i];
                float k = Mathf.InverseLerp(a.t, b.t, t);
                return new PathSample { t = t, pos = Vector3.Lerp(a.pos, b.pos, k), yaw = Mathf.LerpAngle(a.yaw, b.yaw, k), crouch = b.crouch };
            }
            return path[path.Count - 1];
        }
    }

    public class MissionRecorder
    {
        public readonly MissionRecord rec;
        float nextSample, nextSnap;
        Vector3 lastPos, anchor, prevDir, incidentPos;
        bool hasPrevDir, started, incidentSeen;
        int actionsAtIncident;
        float lastTime;

        public MissionRecorder(ScenarioDef def, FacilityLayout layout)
        {
            rec = new MissionRecord { def = def, layout = layout };
        }

        public void Tick(IncidentSimulation sim, Vector3 pos, float yaw, bool crouch, bool busy)
        {
            float t = sim.time;
            float dt = t - lastTime;
            lastTime = t;
            if (!started)
            {
                started = true;
                rec.startPos = pos;
                lastPos = anchor = pos;
            }

            if (!incidentSeen && sim.incidentTime >= 0f)
            {
                incidentSeen = true;
                incidentPos = pos;
                actionsAtIncident = sim.actions.Count;
            }
            if (incidentSeen && rec.firstActionTime < 0f &&
                (FacilityLayout.Flat(pos, incidentPos) > 1.2f || sim.actions.Count > actionsAtIncident))
                rec.firstActionTime = t;

            float step = FacilityLayout.Flat(pos, lastPos);
            if (rec.firstActionTime >= 0f && !busy && dt > 0f && step / dt < 0.25f) rec.idleTime += dt;
            rec.distance += step;
            lastPos = pos;

            if (FacilityLayout.Flat(pos, anchor) > 1.0f)
            {
                var dir = pos - anchor; dir.y = 0; dir.Normalize();
                if (hasPrevDir && Vector3.Dot(dir, prevDir) < -0.5f) rec.reversals++;
                prevDir = dir; hasPrevDir = true;
                anchor = pos;
            }

            if (t >= nextSample)
            {
                nextSample = t + 0.2f;
                rec.path.Add(new PathSample { t = t, pos = pos, yaw = yaw, crouch = crouch });
            }
            if (t >= nextSnap)
            {
                nextSnap = t + 0.5f;
                rec.snaps.Add(sim.Capture());
            }
        }

        public MissionRecord Finish(IncidentSimulation sim)
        {
            var r = rec;
            r.snaps.Add(sim.Capture());
            r.outcome = sim.outcome;
            r.reason = sim.outcomeReason;
            r.endTime = sim.time;
            r.incidentTime = sim.incidentTime;
            r.alarmTime = sim.flags.Contains("alarm_by_player") ? FindTime(sim, "alarm") : -1f;
            r.routeTime = FindTime(sim, "route");
            r.recoveryTotal = sim.recoveryTotal;
            r.recoveries = sim.recoveries;
            r.routeChanges = sim.routeChanges;
            r.maxExposure = sim.maxExposure;
            r.timeUprightInSmoke = sim.timeUprightInSmoke;
            r.timeNearFire = sim.timeNearFire;
            r.escapedVia = sim.escapedVia;
            r.committedExit = sim.committedExit;
            r.blockedExit = sim.blockedExit;
            r.log = new List<SimLogEntry>(sim.log);
            r.actions = new List<SimAction>(sim.actions);
            r.flags = new HashSet<string>(sim.flags);
            r.npcs = new List<NpcState>(sim.npcs);
            if (r.escapedVia != null)
            {
                var start = sim.def.startPos ?? sim.layout.startPos;
                sim.layout.FindPath(start, r.escapedVia, null, out float opt);
                r.optimalDistance = opt;
            }
            return r;
        }

        static float FindTime(IncidentSimulation sim, string tag)
        {
            foreach (var e in sim.log) if (e.tag == tag) return e.time;
            return -1f;
        }
    }
}
