using System;
using System.Collections.Generic;
using UnityEngine;

namespace FireEscape
{
    public class ZoneState { public float smoke, fire, smokeRate; public bool dark; }

    public class PortalState
    {
        public bool doorOpen = true, blocked, dynamicFire;
        public BlockCause cause;
    }

    public class FireSource
    {
        public int id;
        public string zone;
        public Vector3 pos;
        public float intensity, growth;
        public bool barrier;               // lửa chặn cửa: không lớn thêm, không lan
        public bool Out => intensity <= 0.01f;
    }

    public enum NpcMode { Waiting, Wandering, Panicking, Following, Evacuating, Evacuated, Incapacitated }

    public class NpcState
    {
        public int id;
        public NpcNeed need;
        public NpcMode mode;
        public Vector3 pos, home;
        public string zone;
        public int calm;
        public bool seen, known, instructed, reported;
        public float exposure, speed = 1.3f;
        internal List<Vector3> path;
        internal float repathAt, wanderAt;
        internal Vector3 wanderTarget;
        public string Key => "npc" + id;
    }

    public class SimAction
    {
        public float time;
        public string type, target;
        public float value;
    }

    public class SimLogEntry
    {
        public float time;
        public LogCategory cat;
        public string tag, text;
    }

    public class SimSnapshot
    {
        public float time;
        public float[] smoke, fire;
        public bool[] dark, blocked, doorOpen;
        public List<Vector4> fires = new List<Vector4>();   // x,z, intensity, id
        public List<Vector4> npcs = new List<Vector4>();    // x,z, mode, id
        public bool power, emergency, alarm;
        public float exposure;
    }

    /// <summary>
    /// Mô hình vụ cháy thuần dữ liệu (không phụ thuộc scene), để có thể chạy "headless" cho What-if.
    /// Diễn biến phụ thuộc: thời gian, vị trí người chơi, quyết định, trạng thái môi trường và các sự kiện đã thiết kế trước.
    /// </summary>
    public class IncidentSimulation
    {
        public readonly FacilityLayout layout;
        public readonly ScenarioDef def;

        public float time;
        public MissionOutcome outcome = MissionOutcome.Running;
        public string outcomeReason;

        public readonly Dictionary<string, ZoneState> zones = new Dictionary<string, ZoneState>();
        public readonly Dictionary<string, PortalState> portals = new Dictionary<string, PortalState>();
        public readonly List<FireSource> fires = new List<FireSource>();
        public readonly List<NpcState> npcs = new List<NpcState>();
        public readonly HashSet<string> flags = new HashSet<string>();
        public readonly List<SimLogEntry> log = new List<SimLogEntry>();
        public readonly List<SimAction> actions = new List<SimAction>();

        public bool power = true, emergencyLights = true, alarm;
        public float spreadMul = 1f;
        public string committedExit, playerZone, misinfoExit, blockedExit, escapedVia;
        public Vector3 playerPos;
        public float headHeight = 1.65f;
        public float exposure, maxExposure, heat;
        public bool headInSmoke;
        public float timeUprightInSmoke, timeNearFire;
        public float incidentTime = -1f, alarmTime = -1f;
        public float recoveryStart = -1f, recoveryTotal;
        public int recoveries, routeChanges;
        public float misinfoTime = -1f;

        public event Action<SimLogEntry> Logged;
        public event Action<string, bool> Announced;
        public event Action<string> Hinted;

        readonly System.Random rng;
        int nextFireId = 1, nextNpcId = 1;
        readonly Dictionary<string, float> spreadProgress = new Dictionary<string, float>();
        readonly HashSet<string> spreadDone = new HashSet<string>();
        readonly Dictionary<string, float> doorCheckedAt = new Dictionary<string, float>();
        readonly HashSet<string> once = new HashSet<string>();

        public IncidentSimulation(FacilityLayout layout, ScenarioDef def)
        {
            this.layout = layout;
            this.def = def;
            rng = new System.Random(def.request.seed * 7919 + 17);
            foreach (var z in layout.zones) zones[z.id] = new ZoneState();
            foreach (var p in layout.portals) portals[p.id] = new PortalState { doorOpen = !p.hasDoor || p.doorStartsOpen };
            playerPos = def.startPos ?? layout.startPos;
            playerZone = layout.ZoneAt(playerPos)?.id;
        }

        public bool Running => outcome == MissionOutcome.Running;

        // ================= vòng lặp =================
        public void SetAgent(Vector3 footPos, float head)
        {
            playerPos = footPos;
            headHeight = head;
        }

        public void Step(float dt)
        {
            if (!Running) return;
            time += dt;
            ProcessEvents();
            UpdateFires(dt);
            UpdateSmoke(dt);
            UpdateSpread(dt);
            UpdateBlocks();
            UpdateNpcs(dt);
            UpdateAgent(dt);
            CheckObjective();
            if (Running && time >= def.timeLimit)
                End(MissionOutcome.Failed, "Hết thời gian — bạn vẫn còn ở trong tòa nhà.");
        }

        // ================= sự kiện kịch bản =================
        void ProcessEvents()
        {
            foreach (var e in def.events)
            {
                if (e.fired) continue;
                bool go = false;
                switch (e.trigger)
                {
                    case TriggerType.AtTime:
                        go = time >= e.time;
                        break;
                    case TriggerType.FlagMissingAt:
                        if (time >= e.time)
                        {
                            if (flags.Contains(e.key)) e.fired = true;
                            else go = true;
                        }
                        break;
                    case TriggerType.OnFlag:
                        if (flags.Contains(e.key))
                        {
                            if (e.armedAt < 0f) e.armedAt = time;
                            go = time >= e.armedAt + e.delay;
                        }
                        break;
                    case TriggerType.OnEnterZone:
                        if (e.armedAt < 0f && playerZone == e.key)
                        {
                            if (e.time <= 0f || time <= e.time) e.armedAt = time;
                            else e.fired = true;
                        }
                        go = e.armedAt >= 0f && time >= e.armedAt + e.delay;
                        break;
                }
                if (!go) continue;
                e.fired = true;
                if (!string.IsNullOrEmpty(e.narrative))
                    Log(e.category, e.category == LogCategory.Incident ? "incident" : "event", e.narrative);
                foreach (var f in e.effects) ApplyEffect(f);
            }
        }

        void ApplyEffect(ScenarioEffect f)
        {
            switch (f.type)
            {
                case EffectType.Ignite:
                    if (ResolvePoint(f.target, out var pos, out var zone)) AddFire(pos, zone, f.value, f.value2, false);
                    break;
                case EffectType.Smoke:
                    if (zones.TryGetValue(f.target, out var zs)) zs.smokeRate += f.value;
                    break;
                case EffectType.SetSmoke:
                    if (zones.TryGetValue(f.target, out var zs2)) zs2.smoke = Mathf.Max(zs2.smoke, f.value);
                    break;
                case EffectType.SmokeRoute:
                {
                    var ex = committedExit ?? LikelyExit();
                    if (ex != null)
                    {
                        var ap = layout.ApproachOf(ex);
                        zones[ap].smokeRate += f.value;
                        zones[ap].smoke = Mathf.Max(zones[ap].smoke, 0.45f);
                        flags.Add("route_smoked");
                    }
                    break;
                }
                case EffectType.PowerOff:
                    if (power)
                    {
                        power = false;
                        emergencyLights = f.on;
                        flags.Add("power_off");
                        if (!string.IsNullOrEmpty(f.target) && zones.ContainsKey(f.target)) zones[f.target].dark = true;
                        if (!emergencyLights) foreach (var z in zones.Values) z.dark = true;
                    }
                    break;
                case EffectType.BlockPortal:
                    if (!string.IsNullOrEmpty(f.target)) BlockPortal(f.target, f.cause);
                    break;
                case EffectType.BlockRoute:
                {
                    var ex = committedExit ?? LikelyExit();
                    var p = ex != null ? layout.ExitPortal(ex) : null;
                    if (p != null)
                    {
                        blockedExit = ex;
                        BlockPortal(p.id, f.cause);
                        if (ex == committedExit) StartRecovery();
                    }
                    break;
                }
                case EffectType.SpawnNpc:
                    SpawnNpc(f.target, f.need, new Vector3(f.value, 0, f.value2));
                    break;
                case EffectType.Announce:
                    Announced?.Invoke(f.text, f.on);
                    Log(LogCategory.Info, f.on ? "announce" : "misinfo", f.text);
                    if (!f.on)
                    {
                        misinfoExit = f.target;
                        misinfoTime = time;
                        flags.Add("misinfo");
                    }
                    break;
                case EffectType.Spread:
                    spreadMul *= f.value;
                    break;
                case EffectType.Flag:
                    flags.Add(f.target);
                    if (f.target == "incident" && incidentTime < 0f) incidentTime = time;
                    break;
                case EffectType.Hint:
                    Hinted?.Invoke(f.text);
                    break;
                case EffectType.Door:
                    if (portals.TryGetValue(f.target, out var ps)) ps.doorOpen = f.on;
                    break;
                case EffectType.Alarm:
                    if (!alarm)
                    {
                        alarm = true;
                        flags.Add("alarm");
                        flags.Add("alarm_auto");
                        if (alarmTime < 0f) alarmTime = time;
                        WakeNpcs();
                    }
                    break;
            }
        }

        bool ResolvePoint(string target, out Vector3 pos, out string zone)
        {
            pos = Vector3.zero; zone = null;
            if (string.IsNullOrEmpty(target)) return false;
            if (target.StartsWith("@"))
            {
                var parts = target.Substring(1).Split(',');
                pos = new Vector3(float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), 0.2f,
                                  float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                zone = layout.ZoneAt(pos)?.id;
                return zone != null;
            }
            var fx = layout.GetFixture(target);
            if (fx != null)
            {
                pos = new Vector3(fx.pos.x, 0.2f, fx.pos.z) + fx.facing * 0.25f;
                zone = fx.zone;
                return true;
            }
            var z = layout.GetZone(target);
            if (z != null)
            {
                pos = z.Center + new Vector3(0, 0.2f, 0);
                zone = z.id;
                return true;
            }
            return false;
        }

        public FireSource AddFire(Vector3 pos, string zone, float intensity, float growth, bool barrier)
        {
            var f = new FireSource { id = nextFireId++, zone = zone, pos = pos, intensity = intensity, growth = growth, barrier = barrier };
            fires.Add(f);
            if (!barrier)
                Log(LogCategory.Hazard, "fire", $"Lửa xuất hiện tại {layout.GetZone(zone)?.name}.");
            return f;
        }

        void BlockPortal(string portalId, BlockCause cause)
        {
            var p = layout.GetPortal(portalId);
            if (p == null) return;
            var ps = portals[portalId];
            ps.blocked = true;
            ps.cause = cause;
            ps.doorOpen = true;
            if (cause == BlockCause.Smoke)
            {
                // buồng thang/hành lang đặc khói: đi vào là ngạt — coi như không đi qua được
                foreach (var zid in new[] { p.a, p.b })
                {
                    zones[zid].smoke = Mathf.Max(zones[zid].smoke, 0.9f);
                    zones[zid].smokeRate += 0.02f;
                }
            }
            else
            {
                if (cause == BlockCause.Fire)
                {
                    var zone = layout.GetZone(p.a).isExit ? p.b : p.a;
                    AddFire(p.pos + new Vector3(0, 0.2f, 0), zone, 0.9f, 0f, true);
                }
            }
            flags.Add("route_blocked");
            flags.Add("blocked:" + portalId);
            Log(LogCategory.Hazard, "block", $"{p.name} bị chặn bởi {Txt.Name(cause)}.");
        }

        /// <summary>Lối thoát gần nhất (theo đường đi) mà người chơi có khả năng đang hướng tới.</summary>
        public string LikelyExit()
        {
            string best = null; float bl = float.PositiveInfinity;
            foreach (var ex in layout.Exits)
            {
                layout.FindPath(playerPos, ex.id, IsPassable, out float len);
                if (len < bl) { bl = len; best = ex.id; }
            }
            return best ?? layout.primaryExit;
        }

        public bool IsPassable(Portal p) => !portals[p.id].blocked;

        /// <summary>Tuyến an toàn nhất hiện tại (dùng cho mũi tên Beginner và agent What-if).</summary>
        public List<Vector3> SafestPath(Vector3 from, out string exitId)
        {
            exitId = null;
            List<Vector3> best = null; float bc = float.PositiveInfinity;
            foreach (var ex in layout.Exits)
            {
                var path = layout.FindPath(from, ex.id, IsPassable, out float len);
                if (path == null) continue;
                var ap = zones[layout.ApproachOf(ex.id)];
                float cost = len + ap.smoke * 40f + ap.fire * 60f + zones[ex.id].smoke * 30f;
                if (cost < bc) { bc = cost; best = path; exitId = ex.id; }
            }
            return best;
        }

        // ================= vật lý đơn giản của lửa và khói =================
        void UpdateFires(float dt)
        {
            foreach (var z in zones.Values) z.fire = 0f;
            foreach (var f in fires)
            {
                if (f.Out) continue;
                if (!f.barrier)
                    f.intensity = Mathf.Min(1f, f.intensity + f.growth * spreadMul * dt * (0.4f + f.intensity));
                var zs = zones[f.zone];
                zs.fire = Mathf.Max(zs.fire, f.barrier ? 0.3f : f.intensity);
            }
        }

        void UpdateSmoke(float dt)
        {
            foreach (var kv in zones)
            {
                var z = kv.Value;
                z.smoke += (z.fire * 0.05f + z.smokeRate) * spreadMul * dt;
            }

            foreach (var p in layout.portals)
            {
                var ps = portals[p.id];
                float k;
                if (!p.walkable) k = 0.012f;
                else if (ps.blocked) k = 0.1f;
                else if (!p.hasDoor) k = 0.1f;
                else k = ps.doorOpen ? 0.06f : 0.006f;
                var a = zones[p.a]; var b = zones[p.b];
                float flow = (a.smoke - b.smoke) * k * dt;
                a.smoke -= flow;
                b.smoke += flow;
            }

            foreach (var z in layout.zones)
            {
                var s = zones[z.id];
                if (z.isExit && s.smokeRate <= 0f) s.smoke *= 1f - 0.6f * dt;
                s.smoke = Mathf.Clamp01(s.smoke);
            }
        }

        void UpdateSpread(float dt)
        {
            int count = fires.Count;
            for (int i = 0; i < count; i++)
            {
                var f = fires[i];
                if (f.Out || f.barrier || f.intensity < 0.7f) continue;
                foreach (var p in layout.portals)
                {
                    if (!p.Touches(f.zone)) continue;
                    if (FacilityLayout.Flat(f.pos, p.pos) > 7f) continue;
                    string key = f.id + ":" + p.id;
                    if (spreadDone.Contains(key)) continue;
                    var ps = portals[p.id];
                    float rate = 0.025f * spreadMul * (!p.walkable ? 0.25f : (!p.hasDoor || ps.doorOpen) ? 1f : 0.3f);
                    spreadProgress.TryGetValue(key, out float prog);
                    prog += rate * dt;
                    spreadProgress[key] = prog;
                    if (prog < 1f) continue;
                    spreadDone.Add(key);
                    string other = p.Other(f.zone);
                    var oz = layout.GetZone(other);
                    if (oz == null || oz.isExit) continue;
                    var dir = oz.Center - p.pos; dir.y = 0;
                    var at = p.pos + dir.normalized * 1.2f + new Vector3(0, 0.2f, 0);
                    AddFire(at, other, 0.15f, f.growth, false);
                    Log(LogCategory.Hazard, "spread", $"Lửa lan sang {oz.name}{(p.hasDoor && !ps.doorOpen ? " (dù cửa đã đóng)" : "")}.");
                }
            }
        }

        void UpdateBlocks()
        {
            foreach (var p in layout.portals)
            {
                if (!p.walkable) continue;
                var ps = portals[p.id];
                bool fireHere = false;
                foreach (var f in fires)
                    if (!f.Out && !f.barrier && f.intensity > 0.55f && FacilityLayout.Flat(f.pos, p.pos) < 1.3f) { fireHere = true; break; }
                if (fireHere && !ps.blocked)
                {
                    ps.blocked = true; ps.cause = BlockCause.Fire; ps.dynamicFire = true;
                    flags.Add("blocked:" + p.id);
                    Log(LogCategory.Hazard, "block", $"Lửa chặn {p.name}.");
                    var ex = layout.GetZone(p.a).isExit ? p.a : layout.GetZone(p.b).isExit ? p.b : null;
                    if (ex != null && ex == committedExit) { blockedExit = ex; flags.Add("route_blocked"); StartRecovery(); }
                }
                else if (!fireHere && ps.blocked && ps.dynamicFire)
                {
                    ps.blocked = false; ps.dynamicFire = false;
                    Log(LogCategory.Hazard, "unblock", $"{p.name} thông trở lại.");
                }
            }
        }

        // ================= người chơi =================
        void UpdateAgent(float dt)
        {
            var z = layout.ZoneAt(playerPos);
            if (z == null) return;
            if (z.id != playerZone)
            {
                playerZone = z.id;
                flags.Add("entered:" + z.id);
                Log(LogCategory.Route, "zone", $"Vào {z.name}.");
                var ex = layout.ExitServedBy(z.id);
                if (ex != null) Commit(ex);
                if (z.isExit)
                {
                    escapedVia = z.id;
                    Escape();
                    return;
                }
            }

            // cam kết tuyến theo khoảng cách (phòng thật không có hành lang)
            foreach (var ex in layout.Exits)
            {
                var ep = layout.ExitPortal(ex.id);
                if (ep != null && FacilityLayout.Flat(playerPos, ep.pos) < 1.8f) Commit(ex.id);
            }

            // tuyến đã chọn trở nên không dùng được và người chơi đã nhìn thấy
            if (committedExit != null && recoveryStart < 0f)
            {
                var ep = layout.ExitPortal(committedExit);
                var ap = zones[layout.ApproachOf(committedExit)];
                bool bad = (ep != null && portals[ep.id].blocked) || ap.smoke > 0.8f || zones[committedExit].smoke > 0.8f;
                if (bad && ep != null && FacilityLayout.Flat(playerPos, ep.pos) < 9f)
                {
                    blockedExit = committedExit;
                    flags.Add("route_blocked");
                    StartRecovery();
                }
            }

            // phơi nhiễm khói và nhiệt
            var zs = zones[z.id];
            float bottom = z.ceiling * (1f - zs.smoke * 0.85f);
            headInSmoke = zs.smoke > 0.15f && headHeight > bottom;
            float rate = headInSmoke ? zs.smoke * zs.smoke * 14f : zs.smoke * 1.2f;
            if (headInSmoke)
            {
                timeUprightInSmoke += dt;
                if (zs.smoke > 0.3f) flags.Add("head_in_smoke");
            }

            heat = 0f;
            foreach (var f in fires)
            {
                if (f.Out) continue;
                float r = 0.8f + 2f * f.intensity;
                float d = FacilityLayout.Flat(playerPos, f.pos);
                if (d < r) heat = Mathf.Max(heat, f.intensity * (1f - d / r));
            }
            rate += heat * 50f;
            if (heat > 0.15f) timeNearFire += dt;

            if (rate < 0.5f) exposure = Mathf.Max(0f, exposure - 1.5f * dt);
            else exposure += rate * dt;
            maxExposure = Mathf.Max(maxExposure, exposure);

            // phát hiện NPC
            foreach (var n in npcs)
            {
                if (n.mode == NpcMode.Evacuated || n.mode == NpcMode.Incapacitated) continue;
                float d = FacilityLayout.Flat(playerPos, n.pos);
                if (d < 7f && (n.zone == playerZone || d < 3.5f) && !n.seen)
                {
                    n.seen = true;
                    flags.Add("near_npc");
                    Log(LogCategory.Npc, "npc_seen", $"Phát hiện một người {Txt.Name(n.need)}.");
                }
                if (n.need == NpcNeed.Trapped && zones[n.zone].fire > 0.4f && playerZone == n.zone && Once("unsafe_" + n.id))
                {
                    flags.Add("unsafe_rescue");
                    Log(LogCategory.Decision, "unsafe_rescue", "Đi vào vùng đang cháy để tiếp cận người bị kẹt.");
                }
            }

            // gần cửa đóng
            foreach (var p in layout.portals)
                if (p.hasDoor && !portals[p.id].doorOpen && FacilityLayout.Flat(playerPos, p.pos) < 2f) flags.Add("near_closed_door");

            if (exposure >= 100f)
                End(MissionOutcome.Failed, heat > 0.3f
                    ? "Bạn bị bỏng nặng do tiến quá gần đám cháy."
                    : "Bạn bị ngạt khói trước khi thoát ra ngoài.");
        }

        void Commit(string exit)
        {
            if (committedExit == exit) return;
            if (committedExit != null)
            {
                routeChanges++;
                flags.Add("route_changed");
                Log(LogCategory.Decision, "route_changed", $"Đổi kế hoạch: chuyển sang {layout.GetZone(exit).name}.");
                if (recoveryStart >= 0f)
                {
                    recoveryTotal += time - recoveryStart;
                    recoveries++;
                    recoveryStart = -1f;
                }
            }
            else
            {
                Log(LogCategory.Decision, "route", $"Chọn tuyến: {layout.GetZone(exit).name}.");
            }
            committedExit = exit;
            flags.Add("route_committed");
            flags.Add("route:" + exit);
            if (misinfoExit != null && exit == misinfoExit && time > misinfoTime && zones[layout.ApproachOf(exit)].smoke > 0.35f)
            {
                flags.Add("followed_misinfo");
                Log(LogCategory.Decision, "followed_misinfo", "Đi theo thông báo dù hướng đó đang có khói.");
            }
            if (def.familiarExit != null && exit == def.familiarExit) flags.Add("took_familiar");
        }

        void StartRecovery()
        {
            if (recoveryStart >= 0f) return;
            recoveryStart = time;
            Log(LogCategory.Hazard, "route_blocked", $"Tuyến {layout.GetZone(committedExit ?? blockedExit)?.name} không còn phù hợp.");
        }

        void Escape()
        {
            int evac = 0;
            foreach (var n in npcs)
            {
                if (n.mode == NpcMode.Following && FacilityLayout.Flat(n.pos, playerPos) < 6f)
                {
                    n.mode = NpcMode.Evacuated;
                    evac++;
                }
                if (n.need == NpcNeed.Trapped && (n.seen || n.known) && n.mode != NpcMode.Incapacitated)
                {
                    n.reported = true;
                    Log(LogCategory.Npc, "npc_report", "Báo vị trí người bị kẹt cho lực lượng cứu hộ.");
                }
            }
            if (evac > 0) Log(LogCategory.Npc, "npc_evacuated", $"Cùng {evac} người ra ngoài an toàn.");
            if (recoveryStart >= 0f)
            {
                recoveryTotal += time - recoveryStart;
                recoveryStart = -1f;
            }
            if (def.objective == "exit_low" && maxExposure > 45f)
                End(MissionOutcome.Failed, "Ra tới lối thoát nhưng đã hít quá nhiều khói — cần giữ đầu thấp hơn lớp khói.");
            else
                End(def.request.drill != DrillType.None ? MissionOutcome.DrillComplete : MissionOutcome.Escaped,
                    $"Thoát ra an toàn qua {layout.GetZone(escapedVia)?.name}.");
        }

        void CheckObjective()
        {
            if (!Running) return;
            if (def.objective == "alarm" && flags.Contains("alarm_by_player"))
                End(MissionOutcome.DrillComplete, $"Kích hoạt báo động sau {Txt.Secs(time - incidentTime)} kể từ khi sự cố xuất hiện.");
            else if (def.objective == "extinguish" && flags.Contains("extinguished"))
                End(MissionOutcome.DrillComplete, "Dập tắt đám cháy nhỏ bằng kỹ thuật PASS.");
        }

        void End(MissionOutcome o, string reason)
        {
            if (!Running) return;
            outcome = o;
            outcomeReason = reason;
            Log(LogCategory.Outcome, o == MissionOutcome.Failed ? "fail" : "exit", reason);
        }

        // ================= hành động của người chơi =================
        public bool IsDoorHot(string portalId, string fromZone)
        {
            var p = layout.GetPortal(portalId);
            if (p == null) return false;
            string other = p.Other(fromZone ?? playerZone);
            var zs = zones[other];
            if (zs.fire > 0.35f || zs.smoke > 0.7f) return true;
            foreach (var f in fires)
                if (!f.Out && f.zone == other && f.intensity > 0.3f && FacilityLayout.Flat(f.pos, p.pos) < 3f) return true;
            return false;
        }

        /// <summary>Mọi hành động đều đi qua đây để được ghi lại và phát lại (Replay / What-if).</summary>
        public string Apply(SimAction a)
        {
            if (!Running) return null;
            a.time = time;
            actions.Add(a);
            switch (a.type)
            {
                case "alarm":
                    flags.Add("alarm_by_player");
                    if (alarm) return "Chuông đã kêu từ trước.";
                    alarm = true;
                    alarmTime = time;
                    flags.Add("alarm");
                    Log(LogCategory.Action, "alarm", "Kích hoạt chuông báo cháy.");
                    WakeNpcs();
                    return "Đã kích hoạt chuông báo cháy.";

                case "check_door":
                {
                    doorCheckedAt[a.target] = time;
                    bool hot = IsDoorHot(a.target, playerZone);
                    flags.Add("checked_door");
                    Log(LogCategory.Action, hot ? "door_check_hot" : "door_check", hot ? "Kiểm tra cửa: cửa nóng." : "Kiểm tra cửa: không nóng.");
                    return hot ? "Cửa NÓNG! Phía sau có thể có lửa — không nên mở." : "Cửa không nóng. Mở từ từ và quan sát.";
                }

                case "door":
                {
                    var p = layout.GetPortal(a.target);
                    var ps = portals[a.target];
                    if (ps.blocked) return "Lối này đã bị chặn.";
                    if (!ps.doorOpen)
                    {
                        ps.doorOpen = true;
                        if (IsDoorHot(a.target, playerZone))
                        {
                            bool warned = doorCheckedAt.TryGetValue(a.target, out float t) && time - t < 20f;
                            flags.Add(warned ? "ignored_hot_door" : "opened_hot_door");
                            zones[playerZone].smoke = Mathf.Min(1f, zones[playerZone].smoke + 0.35f);
                            exposure += 22f;
                            Log(LogCategory.Decision, "hot_door", warned ? "Mở cửa dù đã biết cửa nóng — khói và nhiệt ập vào." : "Mở cửa nóng mà không kiểm tra — khói và nhiệt ập vào.");
                            return "Khói và hơi nóng ập vào mặt bạn!";
                        }
                        Log(LogCategory.Action, "door_open", $"Mở {p.name}.");
                        return null;
                    }
                    ps.doorOpen = false;
                    var side = p.Other(playerZone);
                    bool leavingFire = zones[side].fire > 0.05f || zones[side].smoke > 0.3f;
                    if (leavingFire) flags.Add("closed_door_behind");
                    Log(LogCategory.Action, leavingFire ? "door_close_good" : "door_close", $"Đóng {p.name}.");
                    return leavingFire ? "Đã đóng cửa — làm chậm lửa và khói lan ra." : null;
                }

                case "pickup":
                    flags.Add("has_extinguisher");
                    Log(LogCategory.Action, "pickup", "Cầm bình chữa cháy.");
                    return "Đã cầm bình. Nhấn E để rút chốt (Pull).";

                case "pin":
                    flags.Add("pass_pull");
                    Log(LogCategory.Action, "pass_pull", "PASS — rút chốt an toàn.");
                    return "Đã rút chốt. Chĩa vào GỐC lửa và giữ E để phun.";

                case "aim_base": if (Once("aim_base")) { flags.Add("pass_aim"); Log(LogCategory.Action, "pass_aim", "PASS — chĩa vòi vào gốc lửa."); } return null;
                case "aim_top": if (Once("aim_top")) { flags.Add("aim_top"); Log(LogCategory.Action, "aim_top", "Phun vào ngọn lửa thay vì gốc lửa."); } return null;
                case "sweep": if (Once("sweep")) { flags.Add("pass_sweep"); Log(LogCategory.Action, "pass_sweep", "PASS — quét qua lại."); } return null;

                case "extinguish":
                {
                    int id = int.Parse(a.target);
                    var f = fires.Find(x => x.id == id);
                    if (f == null || f.Out) return null;
                    if (Once("ext_start"))
                    {
                        flags.Add("used_extinguisher");
                        Log(LogCategory.Action, "extinguish_start", $"Phun bình chữa cháy vào đám cháy (cường độ {f.intensity * 100f:0}%).");
                        if (f.intensity > 0.45f || f.barrier)
                        {
                            flags.Add("fought_large_fire");
                            Log(LogCategory.Decision, "fought_large_fire", "Cố dập một đám cháy đã quá lớn so với một bình chữa cháy.");
                        }
                        if (!alarm) flags.Add("fought_before_alarm");
                    }
                    float eff = f.barrier ? 0.15f : f.intensity > 0.5f ? 0.3f : 1f;
                    f.intensity = Mathf.Max(0f, f.intensity - a.value * eff);
                    if (f.Out)
                    {
                        f.intensity = 0f;
                        flags.Add("extinguished");
                        Log(LogCategory.Action, "extinguished", "Dập tắt một đám cháy.");
                        return "Đã dập tắt! Rời khỏi khu vực và báo cho người khác.";
                    }
                    return null;
                }

                case "ext_empty":
                    Log(LogCategory.Action, "ext_empty", "Bình chữa cháy đã hết.");
                    return "Bình đã hết. Nếu lửa chưa tắt — thoát ra ngay.";

                case "elevator":
                    flags.Add("used_elevator");
                    Log(LogCategory.Decision, "elevator", "Dùng thang máy khi đang có cháy.");
                    End(MissionOutcome.Failed, "Thang máy mất điện và kẹt giữa tầng. Không bao giờ dùng thang máy khi có cháy.");
                    return null;

                case "window":
                {
                    bool trapped = SafestPath(playerPos, out _) == null;
                    flags.Add(trapped ? "signaled_window" : "window_trap");
                    Log(LogCategory.Decision, trapped ? "window_signal" : "window", trapped ? "Ra tín hiệu cầu cứu ở cửa sổ khi bị kẹt." : "Đến cửa sổ thay vì tìm lối thoát.");
                    return trapped
                        ? "Bạn vẫy tay ra tín hiệu. Đội cứu hộ đã nhìn thấy bạn — giữ người thấp, chèn khe cửa."
                        : "Đây là tầng 2. Cửa sổ không phải lối thoát khi vẫn còn lối khác.";
                }

                case "npc":
                    return InteractNpc(int.Parse(a.target));
            }
            return null;
        }

        string InteractNpc(int id)
        {
            var n = npcs.Find(x => x.id == id);
            if (n == null || n.mode == NpcMode.Evacuated || n.mode == NpcMode.Incapacitated) return null;
            n.seen = true;
            switch (n.need)
            {
                case NpcNeed.Lost:
                    n.mode = NpcMode.Following; n.speed = 1.4f;
                    flags.Add("npc_helped");
                    Log(LogCategory.Npc, "npc_help", "Chỉ đường cho người không biết lối, họ đi theo bạn.");
                    return "\"Cảm ơn! Tôi đi theo bạn.\"";
                case NpcNeed.Slow:
                    n.mode = NpcMode.Following; n.speed = 0.9f;
                    flags.Add("npc_helped");
                    Log(LogCategory.Npc, "npc_help", "Dìu người di chuyển chậm.");
                    return "Bạn dìu họ đi cùng. Tốc độ của cả hai sẽ chậm hơn.";
                case NpcNeed.Panicked:
                    n.calm++;
                    if (n.calm == 1)
                    {
                        n.mode = NpcMode.Waiting;
                        Log(LogCategory.Npc, "npc_calm", "Trấn an người đang hoảng loạn.");
                        return "\"Bình tĩnh, nghe tôi nói...\" — Họ dừng lại. Nói thêm lần nữa để dẫn họ đi.";
                    }
                    n.mode = NpcMode.Following; n.speed = 1.4f;
                    flags.Add("npc_helped");
                    Log(LogCategory.Npc, "npc_help", "Dẫn người đã bình tĩnh đi theo mình.");
                    return "\"Được... tôi đi theo bạn.\"";
                default:
                    n.instructed = true;
                    flags.Add("npc_instructed");
                    Log(LogCategory.Npc, "npc_instruct", "Hướng dẫn người bị kẹt: cúi thấp, che mũi, chèn khe cửa, chờ cứu hộ.");
                    return "\"Cúi thấp, che mũi, chèn khe cửa! Tôi sẽ báo cứu hộ vị trí của bạn!\"";
            }
        }

        // ================= NPC =================
        void SpawnNpc(string zoneId, NpcNeed need, Vector3 offset)
        {
            var z = layout.GetZone(zoneId) ?? layout.GetZone(layout.startZone);
            var pos = z.Center + offset;
            pos.x = Mathf.Clamp(pos.x, z.area.xMin + 0.5f, z.area.xMax - 0.5f);
            pos.z = Mathf.Clamp(pos.z, z.area.yMin + 0.5f, z.area.yMax - 0.5f);
            if (layout.freeSpots.Count > 0 && !z.isVirtual)
            {
                // phòng thật: dùng chỗ trống gần nhất do bộ giải bố trí tìm được (không đè lên đồ đạc)
                Vector3 best = pos; float bd = float.MaxValue;
                foreach (var s in layout.freeSpots)
                {
                    if (npcs.Exists(n => FacilityLayout.Flat(n.pos, s) < 0.8f)) continue;
                    float d = FacilityLayout.Flat(s, pos);
                    if (d < bd) { bd = d; best = s; }
                }
                pos = new Vector3(best.x, 0f, best.z);
            }
            var n = new NpcState { id = nextNpcId++, need = need, pos = pos, home = pos, zone = z.id };
            n.mode = need switch
            {
                NpcNeed.Panicked => NpcMode.Panicking,
                NpcNeed.Lost => NpcMode.Wandering,
                NpcNeed.Slow => alarm ? NpcMode.Evacuating : NpcMode.Waiting,
                _ => NpcMode.Waiting
            };
            n.speed = need == NpcNeed.Slow ? 0.45f : need == NpcNeed.Panicked ? 1.8f : 1.3f;
            n.known = need == NpcNeed.Trapped;
            npcs.Add(n);
        }

        void WakeNpcs()
        {
            foreach (var n in npcs)
                if (n.need == NpcNeed.Slow && n.mode == NpcMode.Waiting) n.mode = NpcMode.Evacuating;
        }

        void UpdateNpcs(float dt)
        {
            foreach (var n in npcs)
            {
                if (n.mode == NpcMode.Evacuated || n.mode == NpcMode.Incapacitated) continue;
                var z = layout.ZoneAt(n.pos);
                n.zone = z.id;
                var zs = zones[z.id];

                float h = 0f;
                foreach (var f in fires)
                {
                    if (f.Out) continue;
                    float r = 0.8f + 2f * f.intensity, d = FacilityLayout.Flat(n.pos, f.pos);
                    if (d < r) h = Mathf.Max(h, f.intensity * (1f - d / r));
                }
                float mul = n.need == NpcNeed.Trapped ? (n.instructed ? 0.25f : 0.5f) : 1f;
                n.exposure += (zs.smoke * zs.smoke * 3f + h * 25f) * mul * dt;
                if (n.exposure >= 100f)
                {
                    n.mode = NpcMode.Incapacitated;
                    flags.Add("npc_lost");
                    Log(LogCategory.Npc, "npc_down", $"Một người {Txt.Name(n.need)} đã bất tỉnh vì khói.");
                    continue;
                }

                Vector3? target = null;
                string targetZone = null;
                switch (n.mode)
                {
                    case NpcMode.Following:
                        if (FacilityLayout.Flat(n.pos, playerPos) > 1.3f) { target = playerPos; targetZone = playerZone; }
                        break;
                    case NpcMode.Evacuating:
                        SafestPath(n.pos, out var ex);
                        if (ex != null) { targetZone = ex; target = layout.GetZone(ex).Center; }
                        break;
                    case NpcMode.Panicking:
                        if (time >= n.wanderAt)
                        {
                            n.wanderAt = time + 1.5f + (float)rng.NextDouble() * 1.5f;
                            n.wanderTarget = n.home + new Vector3((float)rng.NextDouble() * 4f - 2f, 0, (float)rng.NextDouble() * 2f - 1f);
                        }
                        target = n.wanderTarget; targetZone = n.zone;
                        break;
                }
                if (target == null) continue;

                if (targetZone != n.zone && time >= n.repathAt)
                {
                    n.repathAt = time + 0.7f;
                    n.path = layout.FindPath(n.pos, targetZone, IsPassable, out _);
                }
                Vector3 next = target.Value;
                if (targetZone != n.zone && n.path != null && n.path.Count > 0)
                {
                    next = n.path[0];
                    if (FacilityLayout.Flat(n.pos, next) < 0.4f) n.path.RemoveAt(0);
                }
                var dir = next - n.pos; dir.y = 0;
                float step = n.speed * (zs.smoke > 0.5f ? 0.7f : 1f) * dt;
                if (dir.magnitude > 0.05f) n.pos += dir.normalized * Mathf.Min(step, dir.magnitude);

                var nz = layout.ZoneAt(n.pos);
                if (nz != null && nz.isExit && n.mode != NpcMode.Following)
                {
                    n.mode = NpcMode.Evacuated;
                    Log(LogCategory.Npc, "npc_out", "Một người đã tự thoát ra ngoài.");
                }
            }
        }

        // ================= tiện ích =================
        bool Once(string key) => once.Add(key);

        void Log(LogCategory cat, string tag, string text)
        {
            var e = new SimLogEntry { time = time, cat = cat, tag = tag, text = text };
            log.Add(e);
            Logged?.Invoke(e);
        }

        public SimSnapshot Capture()
        {
            int nz = layout.zones.Count, np = layout.portals.Count;
            var s = new SimSnapshot
            {
                time = time, smoke = new float[nz], fire = new float[nz], dark = new bool[nz],
                blocked = new bool[np], doorOpen = new bool[np],
                power = power, emergency = emergencyLights, alarm = alarm, exposure = exposure
            };
            for (int i = 0; i < nz; i++)
            {
                var z = zones[layout.zones[i].id];
                s.smoke[i] = z.smoke; s.fire[i] = z.fire; s.dark[i] = z.dark;
            }
            for (int i = 0; i < np; i++)
            {
                var p = portals[layout.portals[i].id];
                s.blocked[i] = p.blocked; s.doorOpen[i] = p.doorOpen;
            }
            foreach (var f in fires) if (!f.Out) s.fires.Add(new Vector4(f.pos.x, f.pos.z, f.intensity, f.id));
            foreach (var n in npcs) s.npcs.Add(new Vector4(n.pos.x, n.pos.z, (int)n.mode, n.id));
            return s;
        }
    }
}
