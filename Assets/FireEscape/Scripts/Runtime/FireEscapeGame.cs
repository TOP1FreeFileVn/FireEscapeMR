using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FireEscape
{
    public enum GameState { Boot, Menu, Briefing, Running, Paused, Result }

    /// <summary>
    /// Điểm vào duy nhất: thêm component này vào một GameObject trống trong scene.
    /// Tự dựng môi trường, người chơi, lớp mô phỏng và UI. Có OVRCameraRig + headset → chạy trên Quest;
    /// có thêm MRUK → dùng phòng thật đã quét (Reality Mapping).
    /// </summary>
    public class FireEscapeGame : MonoBehaviour
    {
        [Tooltip("Bỏ qua headset, luôn chạy chế độ PC (bàn phím & chuột).")]
        public bool forceDesktop;
        [Tooltip("Thời gian chờ MR Utility Kit tải dữ liệu phòng (giây).")]
        public float mrukTimeout = 8f;
        [Tooltip("Thời gian chờ phiên XR (kính / Meta XR Simulator) sẵn sàng trước khi chuyển sang chế độ PC (giây).")]
        public float xrStartupTimeout = 10f;

        public FacilityLayout Layout { get; private set; }
        public EnvironmentBuilder Env { get; private set; }
        public HazardView Hazards { get; private set; }
        public PlayerRig Player { get; private set; }
        public FireEscapeUI UI { get; private set; }
        public ErrorProfile Profile { get; private set; }
        public string ModeName { get; private set; }
        public GameState State { get; private set; }
        public IncidentSimulation Sim { get; private set; }
        public MissionRecord LastRecord { get; private set; }
        public EvaluationReport LastReport { get; private set; }
        public AdaptiveCoach.Recommendation LastRecommendation { get; private set; }

        // bình chữa cháy
        public bool HoldingExtinguisher { get; private set; }
        public bool PinPulled { get; private set; }
        public bool Spraying { get; private set; }
        public float ExtinguisherCharge { get; private set; } = 1f;
        GameObject heldModel;
        ParticleSystem spray;
        string heldFixture;
        int pendingFire = -1;
        float pendingAmount, flushAt, sweepTime, lastAimYaw;
        bool emptyLogged;

        ScenarioDef def;
        MissionRecorder recorder;
        ScenarioRequest lastRequest;
        string prompt;
        float endAt = -1f;

        IEnumerator Start()
        {
            if (Env != null) yield break; // đã được khởi tạo sẵn qua DebugBootNow()
            State = GameState.Boot;
            MatLib.Init();
            Profile = ErrorProfile.Load();

            OVRCameraRig rig = forceDesktop ? null : FindFirstObjectByType<OVRCameraRig>();
            // Phiên XR (kính thật, Link hoặc Meta XR Simulator) cần vài giây mới sẵn sàng — chờ trước khi quyết định chế độ.
            float waited = 0f;
            while (rig != null && !UnityEngine.XR.XRSettings.isDeviceActive && waited < xrStartupTimeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            bool xr = rig != null && UnityEngine.XR.XRSettings.isDeviceActive;
            if (rig != null && !xr) rig.gameObject.SetActive(false);

            FacilityLayout layout = null;
            if (xr)
            {
                var mruk = Meta.XR.MRUtilityKit.MRUK.Instance;
                if (mruk != null)
                {
                    bool loaded = false;
                    mruk.RegisterSceneLoadedCallback(() => loaded = true);
                    float t = 0f;
                    while (!loaded && t < mrukTimeout) { t += Time.deltaTime; yield return null; }
                    var room = loaded ? mruk.GetCurrentRoom() : null;
                    if (room != null) layout = MRRoomMapper.Build(room, rig.centerEyeAnchor);
                }
            }
            if (Env != null) yield break; // đã được khởi tạo sẵn qua DebugBootNow()
            if (layout == null) layout = LayoutFactory.CreateTrainingFloor();
            Setup(layout, rig, xr);
        }

        /// <summary>Khởi tạo đồng bộ ở chế độ PC — cho kiểm thử tự động khi Editor chưa chạy frame nào.</summary>
        public void DebugBootNow()
        {
            if (Env != null) return;
            MatLib.Init();
            Profile ??= ErrorProfile.Load();
            Setup(LayoutFactory.CreateTrainingFloor(), null, false);
        }

        void Setup(FacilityLayout layout, OVRCameraRig rig, bool xr)
        {
            Layout = layout;
            ModeName = xr
                ? (layout.isRealRoom ? "Mixed Reality — Quest, passthrough + phòng thật" : "VR — Quest, không gian mô phỏng")
                : "PC Simulator — bàn phím & chuột";

            Env = new EnvironmentBuilder();
            Env.Build(layout);

            if (xr) Player = XRPlayer.Create(rig, !layout.isRealRoom, layout.floorY);
            else
            {
                foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.gameObject.SetActive(false);
                Player = DesktopPlayer.Create();
            }

            Hazards = new GameObject("Hazards").AddComponent<HazardView>();
            Hazards.Init(layout, Env, Player);
            UI = new GameObject("UI").AddComponent<FireEscapeUI>();
            UI.Init(this, xr);
            Player.Teleport(layout.startPos, layout.startYaw);
            GoMenu();
        }

        // ================= điều hướng =================

        // ================= NPC hướng dẫn (lính cứu hỏa) =================
        FirefighterGuide guide;

        Vector3 GuideSpot(Vector3 origin, float yawDeg, float forward, float right)
        {
            var rot = Quaternion.Euler(0f, yawDeg, 0f);
            var p = origin + rot * new Vector3(right, 0f, forward);
            p.y = 0f;
            if (Layout.freeSpots.Count > 0)
            {
                // phòng thật: chọn chỗ trống gần nhất do bộ giải bố trí tìm được
                Vector3 best = Layout.freeSpots[0];
                foreach (var s in Layout.freeSpots)
                    if (FacilityLayout.Flat(s, p) < FacilityLayout.Flat(best, p)) best = s;
                p = best;
            }
            var z = Layout.ZoneAt(p);
            if (z == null || z.isExit || !z.Contains(p)) p = origin + rot * new Vector3(0f, 0f, 1.2f);
            return p;
        }

        /// <param name="combat">true = thực chiến (MISSION): NPC đeo mặt nạ; false = dạy (menu, PRACTICE): không đeo.</param>
        void PlaceGuide(bool show, bool combat, Vector3 origin, float yawDeg, float forward, float right)
        {
            if (!show) { if (guide) guide.gameObject.SetActive(false); return; }
            var p = GuideSpot(origin, yawDeg, forward, right);
            float faceYaw = Mathf.Atan2(origin.x - p.x, origin.z - p.z) * Mathf.Rad2Deg;
            if (guide == null) guide = FirefighterGuide.Spawn(Layout, UI, p, faceYaw);
            if (guide == null) return;
            guide.gameObject.SetActive(true);
            guide.Place(p, faceYaw, combat);
        }
        void GoMenu()
        {
            State = GameState.Menu;
            Sim = null;
            Player.controlsEnabled = false;
            Player.SetMenuMode(true);
            UI.HideHud();
            UI.ShowMenu();
            PlaceGuide(true, false, Player.FootPos, Player.Yaw, 2.4f, 0.7f);
        }

        public void QuitToMenu()
        {
            ResetExtinguisher();
            Hazards.ResetAll();
            GoMenu();
        }

        public void StartDrill(DrillType d) => StartMission(new ScenarioRequest
        {
            drill = d, type = ScenarioType.ElectricalFire, level = 1, guidance = GuidanceMode.Beginner, seed = Random.Range(1, 1_000_000)
        });

        public void StartMission(ScenarioRequest req)
        {
            lastRequest = req;
            ResetExtinguisher();
            Hazards.ResetAll();
            endAt = -1f;

            def = ScenarioLibrary.Build(req, Layout);
            if (Layout.isRealRoom)
            {
                def.startPos = Player.FootPos;
                def.startYaw = Player.Yaw;
            }
            Sim = new IncidentSimulation(Layout, def);
            Sim.Announced += (text, reliable) => { UI.Announce(text, reliable); Hazards.PlayBeep(); };
            Sim.Hinted += text => { if (def.request.guidance == GuidanceMode.Beginner) UI.Hint(text); };
            Sim.Logged += OnSimLog;
            recorder = new MissionRecorder(def, Layout);

            var start = def.startPos ?? Layout.startPos;
            Player.Teleport(start, def.startYaw ?? Layout.startYaw);
            Player.controlsEnabled = false;
            Player.SetMenuMode(true);
            UI.HideHud();
            Env.SetScanVisible(false);
            PlaceGuide(req.guidance == GuidanceMode.Beginner, req.drill == DrillType.None, start, def.startYaw ?? Layout.startYaw, 1.3f, 0.9f);
            State = GameState.Briefing;
            UI.ShowBriefing(def, Layout.ZoneAt(start)?.name);
        }

        public void BeginRunning()
        {
            if (State != GameState.Briefing) return;
            State = GameState.Running;
            UI.ShowHud(def);
            Player.SetMenuMode(false);
            Player.controlsEnabled = true;
        }

        public void Restart() => StartMission(lastRequest);

        public void ReplaySameScenario()
        {
            if (LastRecord != null) StartMission(LastRecord.def.request);
        }

        void Pause()
        {
            State = GameState.Paused;
            Player.controlsEnabled = false;
            Player.SetMenuMode(true);
            StopSpray();
            AudioListener.pause = true;
            UI.HideHud();
            UI.ShowPause();
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            AudioListener.pause = false;
            State = GameState.Running;
            UI.ShowHud(def);
            Player.SetMenuMode(false);
            Player.controlsEnabled = true;
        }

        public List<WhatIfResult> RunWhatIf(MissionRecord r) => WhatIfSimulator.RunAlternatives(r);

        // ================= kiểm thử tự động (Editor / MCP): tua mô phỏng không cần frame =================
        public void DebugAdvance(Vector3 foot, float yaw, float seconds, bool crouch = false, float dt = 0.1f)
        {
            if (State != GameState.Running || Sim == null) return;
            Player.Teleport(foot, yaw);
            for (float t = 0f; t < seconds && Sim.Running; t += dt)
            {
                Sim.SetAgent(foot, crouch ? 0.95f : 1.65f);
                Sim.Step(dt);
                recorder.Tick(Sim, foot, yaw, crouch, false);
            }
            Hazards.Sync(Sim, dt, def.request.guidance == GuidanceMode.Beginner && def.objective.StartsWith("exit"));
            UI.UpdateHud(Sim, prompt);
        }

        public void DebugEnd()
        {
            if (State == GameState.Running) EndMission();
        }

        // ================= vòng lặp =================
        void Update()
        {
            if (guide != null && guide.gameObject.activeSelf && State != GameState.Running && State != GameState.Paused) guide.TickMenu(Player, Time.deltaTime);
            switch (State)
            {
                case GameState.Briefing:
                    if (Player.ConfirmDown) BeginRunning();
                    break;
                case GameState.Paused:
                    if (Player.MenuDown) Resume();
                    break;
                case GameState.Running:
                    TickMission();
                    break;
            }
        }

        void TickMission()
        {
            if (Player.MenuDown && Sim.Running) { Pause(); return; }
            float dt = Time.deltaTime;
            bool busy = Sim.Running && HandleInteraction(dt);
            if (!Sim.Running) { StopSpray(); prompt = null; }

            Sim.SetAgent(Player.FootPos, Player.HeadHeight);
            Sim.Step(dt * def.timeScale);
            recorder.Tick(Sim, Player.FootPos, Player.Yaw, Player.Crouching, busy);
            Hazards.Sync(Sim, dt, def.request.guidance == GuidanceMode.Beginner && def.objective.StartsWith("exit"));
            UI.UpdateHud(Sim, prompt);
            if (guide != null && guide.gameObject.activeSelf) guide.Tick(Sim, Player, dt);

            if (!Sim.Running)
            {
                if (endAt < 0f) { endAt = Time.time + 2.2f; Player.controlsEnabled = false; }
                else if (Time.time >= endAt) EndMission();
            }
        }

        void EndMission()
        {
            State = GameState.Result;
            StopSpray();
            LastRecord = recorder.Finish(Sim);
            LastReport = MissionEvaluator.Evaluate(LastRecord);
            Profile.Record(LastRecord, LastReport);
            LastRecommendation = AdaptiveCoach.Recommend(Profile, LastReport);
            Hazards.SetIdle();
            UI.HideHud();
            Player.SetMenuMode(true);
            UI.ShowResult();
        }

        void OnSimLog(SimLogEntry e)
        {
            var g = def.request.guidance;
            bool show = e.cat == LogCategory.Incident
                        || (g != GuidanceMode.Expert && (e.cat == LogCategory.Hazard && e.tag == "event" || e.tag == "block" || e.tag == "spread"))
                        || e.tag == "hot_door" || e.tag == "npc_down";
            if (show) UI.Toast(e.text, 4f);
        }

        // ================= tương tác =================
        bool HandleInteraction(float dt)
        {
            prompt = null;
            Interactable target = null;
            var ray = Player.AimRay;
            if (Physics.Raycast(ray, out var hit, 2.8f, ~0, QueryTriggerInteraction.Collide))
            {
                target = hit.collider.GetComponentInParent<Interactable>();
                if (Player is XRPlayer xp) xp.LaserLength = hit.distance;
            }

            string E = Player.Key("E"), F = Player.Key("F"), Q = Player.Key("Q");
            bool expert = def.request.guidance == GuidanceMode.Expert;

            if (HoldingExtinguisher)
            {
                if (Player.DropDown) { DropExtinguisher(); return false; }
                if (!PinPulled)
                {
                    prompt = expert ? $"[{E}] Thao tác bình   [{Q}] Bỏ bình" : $"[{E}] Rút chốt an toàn (Pull)   [{Q}] Bỏ bình";
                    if (Player.InteractDown && (target == null || target.kind == InteractKind.Extinguisher))
                    {
                        PinPulled = true;
                        UI.Toast(Sim.Apply(new SimAction { type = "pin" }));
                        return true;
                    }
                    if (target == null) return false;
                }
                else if (target == null || target.kind == InteractKind.Extinguisher || target.kind == InteractKind.Window)
                {
                    prompt = expert ? $"Giữ [{E}] phun   [{Q}] Bỏ bình" : $"Giữ [{E}] để phun — chĩa vào GỐC lửa, quét qua lại   [{Q}] Bỏ bình";
                    Spray(Player.InteractHeld, dt);
                    return Spraying;
                }
            }
            StopSpray();
            if (target == null) return false;

            switch (target.kind)
            {
                case InteractKind.Alarm:
                    prompt = expert ? $"[{E}] Tương tác" : $"[{E}] Nhấn nút báo cháy";
                    if (Player.InteractDown) { UI.Toast(Sim.Apply(new SimAction { type = "alarm", target = target.id })); return true; }
                    break;

                case InteractKind.Extinguisher:
                    if (HoldingExtinguisher) break;
                    prompt = expert ? $"[{E}] Tương tác" : $"[{E}] Lấy bình chữa cháy";
                    if (Player.InteractDown) { PickUpExtinguisher(target.id); return true; }
                    break;

                case InteractKind.Door:
                {
                    var ps = Sim.portals[target.id];
                    if (ps.blocked) { prompt = "Lối này đã bị chặn"; break; }
                    prompt = ps.doorOpen
                        ? (expert ? $"[{E}] Tương tác" : $"[{E}] Đóng cửa")
                        : (expert ? $"[{E}] Tương tác   [{F}] Chạm tay" : $"[{E}] Mở cửa   [{F}] Kiểm tra độ nóng (mu bàn tay)");
                    if (Player.SecondaryDown && !ps.doorOpen) { UI.Toast(Sim.Apply(new SimAction { type = "check_door", target = target.id })); return true; }
                    if (Player.InteractDown) { UI.Toast(Sim.Apply(new SimAction { type = "door", target = target.id })); return true; }
                    break;
                }

                case InteractKind.Elevator:
                    prompt = expert ? $"[{E}] Tương tác" : $"[{E}] Gọi thang máy";
                    if (Player.InteractDown) { UI.Toast(Sim.Apply(new SimAction { type = "elevator" })); return true; }
                    break;

                case InteractKind.Window:
                    prompt = expert ? $"[{E}] Tương tác" : $"[{E}] Tới cửa sổ / ra tín hiệu";
                    if (Player.InteractDown) { UI.Toast(Sim.Apply(new SimAction { type = "window", target = target.id }), 5f); return true; }
                    break;

                case InteractKind.Npc:
                {
                    var n = Sim.npcs.Find(x => x.id.ToString() == target.id);
                    if (n == null || n.mode == NpcMode.Evacuated || n.mode == NpcMode.Incapacitated || n.mode == NpcMode.Following) break;
                    prompt = expert ? $"[{E}] Nói chuyện" : n.need switch
                    {
                        NpcNeed.Lost => $"[{E}] Chỉ đường, bảo họ đi theo bạn",
                        NpcNeed.Slow => $"[{E}] Dìu họ đi cùng",
                        NpcNeed.Panicked => $"[{E}] Trấn an",
                        _ => $"[{E}] Hướng dẫn từ xa"
                    };
                    if (Player.InteractDown) { UI.Toast(Sim.Apply(new SimAction { type = "npc", target = target.id }), 4f); return true; }
                    break;
                }
            }
            return false;
        }

        // ================= bình chữa cháy (PASS) =================
        void PickUpExtinguisher(string fixtureId)
        {
            HoldingExtinguisher = true;
            heldFixture = fixtureId;
            if (Env.fixtureObjects.TryGetValue(fixtureId, out var fx)) fx.SetActive(false);
            var anchor = Player.HoldAnchor;
            heldModel = EnvironmentBuilder.BuildExtinguisherModel(anchor, Player.IsXR ? new Vector3(0, -0.45f, 0.05f) : new Vector3(0, -0.45f, 0));
            heldModel.transform.localScale = Vector3.one * (Player.IsXR ? 0.9f : 0.8f);
            var sg = new GameObject("Spray");
            sg.transform.SetParent(anchor, false);
            sg.transform.localPosition = new Vector3(0, 0.05f, 0.2f);
            spray = sg.AddComponent<ParticleSystem>();
            spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = spray.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f); main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.95f, 0.97f, 1f, 0.7f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            main.gravityModifier = 0.15f; // CO2 lạnh, nặng hơn không khí: chìm xuống dần
            var sh = spray.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.02f;
            var sz = spray.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.3f, 1, 5f));
            var lv = spray.limitVelocityOverLifetime; lv.enabled = true; lv.limit = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 8f, 1, 0.6f)); lv.dampen = 0.25f;
            var nz = spray.noise; nz.enabled = true; nz.strength = 0.4f; nz.frequency = 1.1f; nz.scrollSpeed = 0.6f;
            var col = spray.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(0.85f, 0.88f, 0.92f), 1) },
                      new[] { new GradientAlphaKey(0.85f, 0), new GradientAlphaKey(0.5f, 0.35f), new GradientAlphaKey(0f, 1) });
            col.color = g;
            var tsa = spray.textureSheetAnimation; tsa.enabled = true; tsa.numTilesX = 2; tsa.numTilesY = 2;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f); tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
            var em = spray.emission; em.rateOverTime = 0f;
            var r = sg.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FxTextures.ParticleMat(FxTextures.Smoke, false, Color.white);
            spray.Play();
            UI.Toast(Sim.Apply(new SimAction { type = "pickup", target = fixtureId }));
        }

        void DropExtinguisher()
        {
            StopSpray();
            if (heldModel) Destroy(heldModel);
            if (spray) Destroy(spray.gameObject);
            if (heldFixture != null && Env != null && Env.fixtureObjects.TryGetValue(heldFixture, out var fx)) fx.SetActive(true);
            HoldingExtinguisher = false;
            heldFixture = null;
        }

        void ResetExtinguisher()
        {
            DropExtinguisher();
            foreach (var kv in Env.fixtureObjects) if (!kv.Value.activeSelf) kv.Value.SetActive(true);
            PinPulled = false;
            ExtinguisherCharge = 1f;
            emptyLogged = false;
            pendingFire = -1; pendingAmount = 0f; sweepTime = 0f;
        }

        void StopSpray()
        {
            if (Spraying) FlushExtinguish();
            Spraying = false;
            if (spray) { var em = spray.emission; em.rateOverTime = 0f; }
        }

        void Spray(bool held, float dt)
        {
            Spraying = held && ExtinguisherCharge > 0f;
            var em = spray.emission;
            em.rateOverTime = Spraying ? 160f : 0f;
            if (!Spraying)
            {
                FlushExtinguish();
                if (held && ExtinguisherCharge <= 0f && !emptyLogged)
                {
                    emptyLogged = true;
                    UI.Toast(Sim.Apply(new SimAction { type = "ext_empty" }));
                }
                return;
            }
            ExtinguisherCharge = Mathf.Max(0f, ExtinguisherCharge - dt / 14f);

            var ray = Player.AimRay;
            FireSource best = null;
            float bestLat = float.MaxValue, bestT = 0f, bestH = 0f;
            foreach (var f in Sim.fires)
            {
                if (f.Out) continue;
                var basePos = new Vector3(f.pos.x, Layout.floorY + 0.15f, f.pos.z);
                float t = Vector3.Dot(basePos - ray.origin, ray.direction);
                if (t < 0.3f || t > 5f) continue;
                var closest = ray.origin + ray.direction * t;
                float lat = new Vector2(closest.x - basePos.x, closest.z - basePos.z).magnitude;
                float h = closest.y - Layout.floorY;
                if (lat > 0.5f + 0.9f * f.intensity || h > 1.2f + 2.5f * f.intensity) continue;
                if (lat < bestLat) { bestLat = lat; best = f; bestT = t; bestH = h; }
            }

            var dir = ray.direction;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float yawSpeed = Mathf.Abs(Mathf.DeltaAngle(yaw, lastAimYaw)) / Mathf.Max(dt, 0.0001f);
            lastAimYaw = yaw;
            sweepTime = yawSpeed > 20f ? sweepTime + dt : Mathf.Max(0f, sweepTime - dt * 0.5f);
            bool sweeping = sweepTime > 0.5f;

            if (best == null) return;
            bool baseAim = bestH < 0.7f;
            if (baseAim && !Sim.flags.Contains("pass_aim")) Sim.Apply(new SimAction { type = "aim_base" });
            if (!baseAim && !Sim.flags.Contains("aim_top")) Sim.Apply(new SimAction { type = "aim_top" });
            if (sweeping && !Sim.flags.Contains("pass_sweep")) Sim.Apply(new SimAction { type = "sweep" });

            float distF = bestT >= 1.2f && bestT <= 3.8f ? 1f : 0.55f;
            float amount = 0.11f * dt * (baseAim ? 1f : 0.3f) * (sweeping ? 1.5f : 1f) * distF;
            if (pendingFire != best.id) { FlushExtinguish(); pendingFire = best.id; }
            pendingAmount += amount;
            if (Time.time >= flushAt) FlushExtinguish();
        }

        void FlushExtinguish()
        {
            flushAt = Time.time + 0.25f;
            if (pendingFire < 0 || pendingAmount <= 0f || Sim == null) { pendingAmount = 0f; return; }
            var msg = Sim.Apply(new SimAction { type = "extinguish", target = pendingFire.ToString(), value = pendingAmount });
            pendingAmount = 0f;
            if (msg != null) UI.Toast(msg);
        }
    }
}
