using System.Collections.Generic;
using UnityEngine;

namespace FireEscape
{
    /// <summary>
    /// NPC lính cứu hỏa hướng dẫn (chế độ Beginner):
    /// DẠY (menu, KNOW, PRACTICE, giới thiệu, kết quả): không đeo mặt nạ, trò chuyện.
    /// THỰC CHIẾN (MISSION): đeo mặt nạ thở ngay khi vào nhiệm vụ, chỉ hướng lối thoát an toàn,
    /// dẫn đường đi trước người chơi, chờ và vẫy gọi nếu người chơi tụt lại, cúi thấp khi đi qua khói.
    /// Tới nơi an toàn thì tháo mặt nạ. Mặt nạ do lớp Animator "Mask" riêng điều khiển (chỉ xương Mask),
    /// nên mọi động tác (Talk, Point, Walk...) đều kết hợp được với cả hai trạng thái đeo / tháo.
    /// </summary>
    public class FirefighterGuide : MonoBehaviour
    {
        Animator anim;
        TextMesh bubble;
        FacilityLayout L;
        FireEscapeUI ui;
        Vector3 pos;          // toạ độ layout (y = 0 là sàn)
        float yaw;
        string state;
        bool maskOn, pointed, combat, removedAfter;
        float busyUntil, repathAt, bubbleUntil, now;   // now: đồng hồ riêng, dừng khi game tạm dừng
        List<Vector3> path;
        Vector3? waitSpot;    // chỗ đứng chờ trong lối thoát (tránh chắn cửa)
        PlayerRig viewer;
        string exitId;
        readonly Dictionary<string, float> spokenAt = new Dictionary<string, float>();

        public static FirefighterGuide Spawn(FacilityLayout layout, FireEscapeUI ui, Vector3 layoutPos, float yaw)
        {
            var prefab = Resources.Load<GameObject>("FireEscapeModels/Firefighter/FirefighterGuide");
            if (prefab == null) return null;   // chưa chạy menu FireEscape/4
            var go = Instantiate(prefab);
            go.name = "FirefighterGuide";
            var g = go.AddComponent<FirefighterGuide>();
            g.L = layout; g.ui = ui;
            g.anim = go.GetComponentInChildren<Animator>();
            var ctrl = Resources.Load<RuntimeAnimatorController>("FireEscapeModels/Firefighter/FirefighterGuide");
            if (g.anim != null && ctrl != null) g.anim.runtimeAnimatorController = ctrl;   // luôn dùng controller mới nhất
            g.bubble = MatLib.Label3D(go.transform, "", new Vector3(0, 2.3f, 0), 0.11f, new Color(1f, 0.95f, 0.75f), true);
            // thân rắn: người chơi (CharacterController / XR locomotion) không đi xuyên qua NPC
            var col = go.AddComponent<CapsuleCollider>();
            col.center = new Vector3(0f, 0.9f, 0f); col.height = 1.8f; col.radius = 0.28f;
            g.Place(layoutPos, yaw, false);
            g.Play("Idle", 0f);
            return g;
        }

        /// <param name="combatMode">true = thực chiến (đeo mặt nạ); false = dạy bình thường (không đeo).</param>
        public void Place(Vector3 layoutPos, float yawDeg, bool combatMode)
        {
            pos = new Vector3(layoutPos.x, 0f, layoutPos.z);
            yaw = yawDeg;
            combat = combatMode; pointed = false; removedAfter = false; path = null; waitSpot = null; busyUntil = 0f;
            SetMask(false, false);
            spokenAt.Clear();
            Apply();
        }

        void Apply()
        {
            transform.position = new Vector3(pos.x, L.floorY + pos.y, pos.z);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            // bong bóng chữ luôn quay về phía người chơi (không phụ thuộc Camera.main / LateUpdate)
            var cam = viewer != null ? viewer.Cam : null;
            if (cam != null)
            {
                var d = bubble.transform.position - cam.transform.position;
                if (d.sqrMagnitude > 0.0001f) bubble.transform.rotation = Quaternion.LookRotation(d);
            }
        }

        void Play(string s, float fade = 0.25f)
        {
            if (state == s || anim == null) return;
            state = s;
            anim.speed = 1f;
            anim.CrossFadeInFixedTime(s, fade, 0);
        }

        /// <summary>Mất tham chiếu (vd. Unity biên dịch lại giữa lúc Play) → tự huỷ, game sẽ tạo lại NPC.</summary>
        bool Lost()
        {
            if (L != null && anim != null && bubble != null) return false;
            Destroy(gameObject);
            return true;
        }

        /// <summary>Đeo / tháo mặt nạ. animate = diễn động tác tay (1,6 s); false = đặt ngay trạng thái.</summary>
        void SetMask(bool on, bool animate)
        {
            maskOn = on;
            if (anim == null) return;
            if (anim.layerCount > 1)
            {
                string s = animate ? (on ? "MaskOn" : "MaskOff") : (on ? "MaskOnHold" : "MaskOffHold");
                anim.CrossFadeInFixedTime(s, animate ? 0.1f : 0f, 1);
            }
            if (animate)
            {
                Play(on ? "MaskOn" : "MaskOff", 0.15f);   // động tác tay ở lớp thân
                busyUntil = now + 1.7f;
            }
        }

        void Say(string key, string text, float cooldown = 12f)
        {
            if (spokenAt.TryGetValue(key, out float t) && now - t < cooldown) return;
            spokenAt[key] = now;
            bubble.text = Wrap(text, 30);
            bubbleUntil = now + 4f;
            ui?.Hint("Lính cứu hỏa: " + text);
        }

        static string Wrap(string s, int width)
        {
            var sb = new System.Text.StringBuilder();
            int line = 0;
            foreach (var w in s.Split(' '))
            {
                if (line > 0 && line + w.Length + 1 > width) { sb.Append('\n'); line = 0; }
                else if (line > 0) { sb.Append(' '); line++; }
                sb.Append(w); line += w.Length;
            }
            return sb.ToString();
        }

        void Face(Vector3 layoutTarget, float dt, float speed = 360f)
        {
            var d = layoutTarget - pos; d.y = 0;
            if (d.sqrMagnitude < 0.0004f) return;
            float target = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            yaw = Mathf.MoveTowardsAngle(yaw, target, speed * dt);
        }

        /// <summary>Dạy bình thường (menu, KNOW, giới thiệu, kết quả): không đeo mặt nạ, đứng trò chuyện.</summary>
        public void TickMenu(PlayerRig player, float dt)
        {
            if (Lost()) return;
            viewer = player;
            now += dt;
            if (now > bubbleUntil) bubble.text = "";
            if (maskOn && now >= busyUntil) SetMask(false, true);
            Face(player.FootPos, dt, 180f);
            if (now >= busyUntil) Play("Talk");
            Apply();
        }

        public void Tick(IncidentSimulation sim, PlayerRig player, float dt)
        {
            if (Lost()) return;
            viewer = player;
            now += dt;
            if (now > bubbleUntil) bubble.text = "";
            var myZone = L.ZoneAt(pos);
            var zs = sim.zones[myZone.id];
            var playerPos = player.FootPos;
            float toPlayer = FacilityLayout.Flat(pos, playerPos);

            if (now < busyUntil) { Apply(); return; }

            if (!sim.Running)
            {
                // đã tới nơi an toàn: tháo mặt nạ rồi mới nói chuyện
                if (maskOn && sim.outcome != MissionOutcome.Failed && !removedAfter)
                {
                    removedAfter = true;
                    SetMask(false, true);
                    Say("safe", "An toàn rồi. Tôi tháo mặt nạ — cùng xem lại bạn đã làm gì nhé.", 30f);
                    Apply();
                    return;
                }
                Face(playerPos, dt);
                Play(sim.outcome == MissionOutcome.Failed ? "Idle" : "Talk");
                Apply();
                return;
            }

            // THỰC CHIẾN: đeo mặt nạ thở ngay khi bắt đầu nhiệm vụ
            if (combat && !maskOn)
            {
                SetMask(true, true);
                Say("mask", "Vào thực chiến — tôi đeo mặt nạ thở. Bạn quan sát và làm theo tôi.");
                Apply();
                return;
            }

            // trước sự cố: đứng cạnh người chơi, sẵn sàng
            if (sim.incidentTime < 0f)
            {
                Face(playerPos, dt, 180f);
                Play("Idle");
                Apply();
                return;
            }

            if (now >= repathAt || path == null)
            {
                repathAt = now + 0.5f;
                path = sim.SafestPath(pos, out exitId);
            }

            // 2) chỉ hướng lối thoát an toàn (một lần)
            if (!pointed && path != null && path.Count > 0)
            {
                pointed = true;
                yaw = Mathf.Atan2(path[0].x - pos.x, path[0].z - pos.z) * Mathf.Rad2Deg;
                Play("Point", 0.2f);
                busyUntil = now + 2.2f;
                var ex = L.GetZone(exitId);
                Say("point", $"Lối thoát an toàn: {ex?.name}. Đi theo tôi, đừng dùng thang máy!");
                if (!sim.alarm) Say("alarm", "Nhớ nhấn chuông báo cháy trên đường đi!", 30f);
                Apply();
                return;
            }

            if (path == null || path.Count == 0)
            {
                Face(playerPos, dt);
                Play("Talk");
                Say("trapped", "Không còn lối an toàn — đóng cửa, chèn khe cửa, ra tín hiệu ở cửa sổ!", 20f);
                Apply();
                return;
            }

            // 3) đã tới lối thoát: quay lại gọi người chơi
            var exitZone = L.GetZone(exitId);
            if (exitZone != null && exitZone.Contains(pos + Vector3.zero) || FacilityLayout.Flat(pos, path[path.Count - 1]) < 0.4f)
            {
                // vào sâu trong lối thoát rồi đứng lệch sang một bên — chừa trống cửa cho người chơi đi qua
                if (waitSpot == null && exitZone != null)
                {
                    var c = exitZone.Center;
                    var inDir = c - pos; inDir.y = 0f;
                    inDir = inDir.sqrMagnitude > 0.01f ? inDir.normalized : Vector3.forward;
                    var side = new Vector3(inDir.z, 0f, -inDir.x);
                    var s = c + inDir * 0.6f + side * 0.8f;
                    var a = exitZone.area;
                    s.x = Mathf.Clamp(s.x, a.xMin + 0.35f, a.xMax - 0.35f);
                    s.z = Mathf.Clamp(s.z, a.yMin + 0.35f, a.yMax - 0.35f);
                    waitSpot = s;
                }
                Say("exit", "Ra đây! Đến điểm tập kết và báo cho đội cứu hộ nếu còn người bên trong.", 20f);
                if (waitSpot.HasValue && FacilityLayout.Flat(pos, waitSpot.Value) > 0.1f)
                {
                    var d = waitSpot.Value - pos; d.y = 0f;
                    pos += d.normalized * Mathf.Min(1.2f * dt, d.magnitude);
                    Face(waitSpot.Value, dt, 300f);
                    Play("Walk");
                    Apply();
                    return;
                }
                Face(playerPos, dt);
                Play(toPlayer > 1.5f ? "Beckon" : "Talk");
                Say("exit", "Ra đây! Đến điểm tập kết và báo cho đội cứu hộ nếu còn người bên trong.", 20f);
                Apply();
                return;
            }

            // 4) người chơi tụt lại → quay lại vẫy gọi
            if (toPlayer > 4.5f)
            {
                Face(playerPos, dt);
                Play("Beckon");
                Say("wait", "Nhanh lên, đi theo tôi!", 8f);
                Apply();
                return;
            }

            // 5) dẫn đường: đi trước người chơi 1,5–3 m; cúi thấp khi khói dày
            bool crouch = zs.smoke > 0.35f;
            if (crouch) Say("low", "Khói dày — cúi thấp xuống, không khí sạch ở sát sàn!", 15f);
            if (sim.headInSmoke) Say("playerlow", "Cúi thấp nữa! Đầu bạn đang ở trong lớp khói.", 10f);

            var next = path[0];
            if (FacilityLayout.Flat(pos, next) < 0.35f && path.Count > 1) { path.RemoveAt(0); next = path[0]; }
            float speed = crouch ? 0.9f : 1.35f;
            if (toPlayer < 1.2f) speed *= 1.15f;
            if (toPlayer > 3f) speed *= 0.5f;
            var dir = next - pos; dir.y = 0;
            if (dir.magnitude > 0.05f)
            {
                pos += dir.normalized * Mathf.Min(speed * dt, dir.magnitude);
                Face(next, dt, 300f);
            }
            Play(crouch ? "CrouchWalk" : "Walk");
            anim.speed = crouch ? 1f : Mathf.Clamp(speed / 1.35f, 0.7f, 1.3f);
            Apply();
        }

        void OnDisable()
        {
            if (anim != null) anim.speed = 1f;
        }
    }
}
