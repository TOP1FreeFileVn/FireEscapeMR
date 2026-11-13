using System.Collections;
using System.IO;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace FireEscape
{
    /// <summary>
    /// Công cụ quay video demo (chỉ dùng khi phát triển, không tự bật trong game):
    /// hiện phòng MRUK thành lưới trong suốt (passthrough không quay được), tự lái người chơi theo NPC ra lối thoát
    /// (di chuyển OVRCameraRig — MR không có joystick) và ghi mỗi khung hình từ mắt người chơi ra JPG với bước thời gian cố định.
    /// Gọi DemoRecorder.Begin(...) khi game đang ở Menu; khi xong sẽ ghi file done.txt vào thư mục đầu ra.
    /// </summary>
    public class DemoRecorder : MonoBehaviour
    {
        public string outDir;
        public int width = 1280, height = 720, fps = 30;
        public float briefingSeconds = 3f, afterEndSeconds = 5f;

        FireEscapeGame game;
        Camera shotCam;
        RenderTexture rt;
        Texture2D tex;
        int frame;
        float endTimer = -1f;
        bool crouched, guideMoving = true;
        Vector3 lastGuidePos;
        GameObject roomMesh;
        float guideCheck;

        public static DemoRecorder Begin(string outDir, ScenarioRequest req)
        {
            var g = FindFirstObjectByType<FireEscapeGame>();
            var r = g.gameObject.AddComponent<DemoRecorder>();
            r.outDir = outDir;
            r.game = g;
            Directory.CreateDirectory(outDir);
            if (MRUK.Instance != null) MRUK.Instance.EnableWorldLock = false;
            r.MoveToFarCorner();
            g.StartMission(req);
            return r;
        }

        /// <summary>Đặt người chơi ở chỗ trống xa lối thoát nhất (phòng mẫu có thể cho đứng ngay cạnh cửa), quay mặt vào giữa phòng.</summary>
        void MoveToFarCorner()
        {
            var L = game.Layout;
            if (L == null || L.freeSpots.Count == 0) return;
            Vector3 best = L.freeSpots[0]; float bestD = -1f;
            foreach (var s in L.freeSpots)
            {
                float d = float.MaxValue;
                foreach (var z in L.zones) if (z.isExit) d = Mathf.Min(d, z.DistanceTo(s));
                if (d > bestD) { bestD = d; best = s; }
            }
            var head = game.Player.Head;
            var rig = head.root;
            var h = head.position;
            rig.position += new Vector3(best.x - h.x, 0f, best.z - h.z);
            Vector3 center = Vector3.zero; int n = 0;
            foreach (var z in L.zones) if (!z.isExit && !z.isVirtual) { center += z.Center; n++; }
            if (n > 0)
            {
                var to = center / n - head.position; to.y = 0;
                var f = head.forward; f.y = 0;
                if (to.sqrMagnitude > 0.01f) rig.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(f, to, Vector3.up));
            }
        }

        void Start()
        {
            Time.captureFramerate = fps;
            // World Lock của MRUK chỉnh lại gốc tracking mỗi khung hình → sẽ triệt tiêu việc tự lái rig
            if (MRUK.Instance != null) MRUK.Instance.EnableWorldLock = false;
            ShowRoomMesh();
            var go = new GameObject("DemoShotCam");
            shotCam = go.AddComponent<Camera>();
            shotCam.CopyFrom(game.Player.Cam);
            shotCam.clearFlags = CameraClearFlags.SolidColor;
            shotCam.backgroundColor = new Color(0.16f, 0.17f, 0.19f);
            shotCam.fieldOfView = 78f;
            shotCam.nearClipPlane = 0.05f;
            shotCam.enabled = false;
            rt = new RenderTexture(width, height, 24);
            tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            StartCoroutine(CaptureLoop());
        }

        /// <summary>Tường, sàn, đồ đạc của phòng MRUK thành lưới mờ để người xem video thấy căn phòng.</summary>
        void ShowRoomMesh()
        {
            var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
            if (room == null) return;
            var go = new GameObject("DemoRoomMesh");
            roomMesh = go;
            var em = go.AddComponent<EffectMesh>();
            em.SpawnOnStart = MRUK.RoomFilter.None;
            em.CutHoles = MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME;
            // không vẽ tấm cửa/cửa sổ (chắn ngang lối thoát), không trần; không collider
            em.Labels = (MRUKAnchor.SceneLabels)~0 & ~MRUKAnchor.SceneLabels.GLOBAL_MESH & ~MRUKAnchor.SceneLabels.CEILING
                        & ~MRUKAnchor.SceneLabels.DOOR_FRAME & ~MRUKAnchor.SceneLabels.WINDOW_FRAME;
            em.Colliders = false;
            em.MeshMaterial = MatLib.Transparent(new Color(0.78f, 0.76f, 0.72f, 0.55f));
            em.CreateMesh(room);
        }

        void Update()
        {
            if (game == null || game.Player == null) return;
            float dt = Time.deltaTime;
            switch (game.State)
            {
                case GameState.Briefing:
                    if (Time.timeSinceLevelLoad > 0 && (briefingSeconds -= dt) <= 0f) game.BeginRunning();
                    break;
                case GameState.Running:
                    if (game.Sim != null && game.Sim.Running) Drive(dt);
                    else FaceRoom(dt);
                    break;
                default:
                    if (endTimer < 0f)
                    {
                        endTimer = afterEndSeconds;
                        LogNearCamera();
                    }
                    FaceRoom(dt);
                    break;
            }
        }

        /// <summary>Đi theo tuyến an toàn nhất, giữ khoảng cách với NPC, cúi thấp khi có khói.</summary>
        void Drive(float dt)
        {
            var player = game.Player;
            var rig = player.Head.root;
            var p = player.FootPos; p.y = 0;
            var guide = FindFirstObjectByType<FirefighterGuide>();
            var gp = guide ? guide.transform.position : p; gp.y = 0;
            float toGuide = Vector3.Distance(p, gp);
            var zone = game.Layout.ZoneAt(p);
            bool smoke = game.Sim.headInSmoke || (zone != null && game.Sim.zones[zone.id].smoke > 0.35f);
            Vector3 look = gp - p;
            // NPC đứng yên (đã tới cửa, đang chờ) → tự đi tiếp; NPC đang đi → giữ khoảng cách phía sau
            guideCheck -= dt;
            if (guideCheck <= 0f) { guideMoving = Vector3.Distance(gp, lastGuidePos) > 0.1f; lastGuidePos = gp; guideCheck = 1f; }
            bool guideAtExit = false;
            foreach (var z in game.Layout.zones) if (z.isExit && z.DistanceTo(gp) < 1.2f) guideAtExit = true;
            if (game.Sim.incidentTime >= 0f)
            {
                // NPC đã tới cửa và đứng chờ → ra cửa theo tuyến an toàn; NPC đi trước xa → bám theo NPC; còn lại đứng chờ
                Vector3? target = null;
                if (!guideMoving && guideAtExit)
                {
                    var path = game.Sim.SafestPath(p, out _);
                    if (path != null && path.Count > 0)
                    {
                        var next = path[0];
                        if (Vector3.Distance(p, new Vector3(next.x, 0, next.z)) < 0.35f && path.Count > 1) next = path[1];
                        target = next;
                    }
                }
                else if (toGuide > 1.9f) target = gp;
                if (target.HasValue)
                {
                    var d = target.Value - p; d.y = 0;
                    float speed = smoke ? 0.9f : 1.3f;
                    if (d.magnitude > 0.02f)
                    {
                        rig.position += d.normalized * Mathf.Min(speed * dt, d.magnitude);
                        look = d;
                    }
                }
            }
            // xoay người (quay cả rig quanh đầu) về hướng đi / hướng NPC
            look.y = 0;
            if (look.sqrMagnitude > 0.0001f)
            {
                var f = player.Head.forward; f.y = 0;
                float delta = Vector3.SignedAngle(f, look, Vector3.up);
                rig.RotateAround(player.Head.position, Vector3.up, Mathf.Clamp(delta, -120f * dt, 120f * dt));
            }
            // cúi người dưới lớp khói: hạ rig xuống
            crouched = smoke;
            var pos = rig.position;
            pos.y = Mathf.MoveTowards(pos.y, crouched ? -0.65f : 0f, 1.5f * dt);
            rig.position = pos;
        }

        /// <summary>Đã ra tới cửa: quay người nhìn lại vào phòng (thấy NPC, đám khói và bảng kết quả).</summary>
        void FaceRoom(float dt)
        {
            var head = game.Player.Head;
            Vector3 center = Vector3.zero; int n = 0;
            foreach (var z in game.Layout.zones) if (!z.isExit && !z.isVirtual) { center += z.Center; n++; }
            if (n == 0) return;
            var to = center / n - head.position; to.y = 0;
            var f = head.forward; f.y = 0;
            if (to.sqrMagnitude < 0.01f) return;
            float delta = Vector3.SignedAngle(f, to, Vector3.up);
            head.root.RotateAround(head.position, Vector3.up, Mathf.Clamp(delta, -150f * dt, 150f * dt));
        }

        /// <summary>Ghi các renderer nằm sát trước mắt (chẩn đoán vật che camera).</summary>
        void LogNearCamera()
        {
            var head = game.Player.Head;
            var sb = new System.Text.StringBuilder();
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var b = r.bounds;
                var p = b.ClosestPoint(head.position);
                float d = Vector3.Distance(p, head.position);
                if (d < 0.8f) sb.AppendLine($"{d:0.00} {r.GetType().Name} {r.name} parent={(r.transform.parent ? r.transform.parent.name : "-")} mat={(r.sharedMaterial ? r.sharedMaterial.name : "-")} size={b.size}");
            }
            File.WriteAllText(Path.Combine(outDir, "near_camera.txt"), sb.ToString());
        }

        IEnumerator CaptureLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                var head = game.Player.Head;
                shotCam.transform.SetPositionAndRotation(head.position, Quaternion.Euler(Mathf.Clamp(head.eulerAngles.x > 180 ? head.eulerAngles.x - 360 : head.eulerAngles.x, -10f, 25f) + 6f, head.eulerAngles.y, 0f));
                shotCam.targetTexture = rt;
                shotCam.Render();
                shotCam.targetTexture = null;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(outDir, $"f_{frame:00000}.jpg"), tex.EncodeToJPG(92));
                frame++;
                if (endTimer >= 0f && (endTimer -= 1f / fps) <= 0f)
                {
                    File.WriteAllText(Path.Combine(outDir, "done.txt"), frame.ToString());
                    Time.captureFramerate = 0;
                    enabled = false;
                    yield break;
                }
            }
        }

        void OnDestroy()
        {
            Time.captureFramerate = 0;
            if (rt) rt.Release();
        }
    }
}
