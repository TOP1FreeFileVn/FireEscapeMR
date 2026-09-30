using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace FireEscape
{
    public class FireEscapeUI : MonoBehaviour
    {
        FireEscapeGame game;
        bool xr;
        Canvas menuCanvas, hudCanvas;
        RectTransform menuRoot;

        // HUD
        Text hudTitle, hudClock, hudStatus, hudPrompt, hudToast, hudAnnounce, hudHint, hudWarn, hudHelp;
        Image hudExposure;
        GameObject crosshair, promptBox, toastBox, announceBox, hintBox, warnBox, helpBox;
        float toastUntil, announceUntil, hintUntil;

        // Mission setup
        ScenarioType selType = ScenarioType.ElectricalFire;
        int selLevel = 1;
        GuidanceMode selGuidance = GuidanceMode.Beginner;
        bool selStress;
        int knowModule, knowCard;
        bool resetConfirm;

        // Replay
        MissionRecord rp;
        MapView map;
        Slider slider;
        Text timeLabel, whatIfTitle;
        float rpT, rpSpeed = 1f;
        bool rpPlaying, sliderLock, replayActive;
        List<WhatIfResult> alts;
        int altSel = -1;
        RectTransform whatIfBox;
        Button playBtn, speedBtn;
        static readonly Color[] AltColors = { new Color(1f, 0.6f, 0.2f, 0.95f), new Color(0.85f, 0.45f, 1f, 0.95f), new Color(1f, 0.9f, 0.3f, 0.95f) };
        static readonly Color YouColor = new Color(0.35f, 0.75f, 1f, 0.95f);

        // XR pointer
        Selectable hovered;
        Image xrCursor;

        public bool MenuVisible => menuCanvas.gameObject.activeSelf;

        // ================================================================
        public void Init(FireEscapeGame g, bool isXR)
        {
            game = g;
            xr = isXR;
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                var m = es.AddComponent<InputSystemUIInputModule>();
                m.AssignDefaultActions();
            }
            menuCanvas = MakeCanvas("MenuCanvas", 10, new Vector2(1600, 900), 0.0009f);
            menuRoot = (RectTransform)menuCanvas.transform;
            hudCanvas = MakeCanvas("HudCanvas", 5, xr ? new Vector2(1000, 560) : new Vector2(1600, 900), 0.0011f);
            BuildHud();
            hudCanvas.gameObject.SetActive(false);
            if (xr)
            {
                xrCursor = UIKit.Panel(menuRoot, "XRCursor", Pal.Fire);
                xrCursor.rectTransform.sizeDelta = new Vector2(14, 14);
                xrCursor.raycastTarget = false;
            }
        }

        Canvas MakeCanvas(string name, int order, Vector2 refSize, float worldScale)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var c = go.AddComponent<Canvas>();
            if (xr)
            {
                c.renderMode = RenderMode.WorldSpace;
                ((RectTransform)go.transform).sizeDelta = refSize;
                go.transform.localScale = Vector3.one * worldScale;
                c.worldCamera = game.Player.Cam;
            }
            else
            {
                c.renderMode = RenderMode.ScreenSpaceOverlay;
                c.sortingOrder = order;
                var s = go.AddComponent<CanvasScaler>();
                s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                s.referenceResolution = refSize;
                s.matchWidthOrHeight = 0.5f;
            }
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        void PlaceInFront(Canvas c, float dist, float down)
        {
            if (!xr) return;
            var head = game.Player.Head;
            var fwd = head.forward; fwd.y = 0;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            fwd.Normalize();
            c.transform.position = head.position + fwd * dist - Vector3.up * down;
            c.transform.rotation = Quaternion.LookRotation(fwd);
        }

        RectTransform NewScreen(Color? bg = null)
        {
            replayActive = false;
            for (int i = menuRoot.childCount - 1; i >= 0; i--)
            {
                var ch = menuRoot.GetChild(i);
                if (xrCursor != null && ch == xrCursor.transform) continue;
                ch.gameObject.SetActive(false);   // Destroy chỉ có hiệu lực cuối frame → ẩn ngay để màn cũ không vẽ đè màn mới
                Destroy(ch.gameObject);
            }
            menuCanvas.gameObject.SetActive(true);
            var img = UIKit.Panel(menuRoot, "Screen", bg ?? Pal.Bg);
            UIKit.Stretch(img.rectTransform);
            img.transform.SetAsFirstSibling();
            PlaceInFront(menuCanvas, 1.35f, 0.1f);
            hovered = null;
            return img.rectTransform;
        }

        public void HideMenu()
        {
            menuCanvas.gameObject.SetActive(false);
            replayActive = false;
        }

        static Text H(Transform p, string t, int size = 34, Color? c = null) => UIKit.Label(p, t, size, c ?? Pal.Text, TextAnchor.UpperLeft, FontStyle.Bold);
        static Text P(Transform p, string t, int size = 20, Color? c = null) => UIKit.Label(p, t, size, c ?? Pal.Text);
        static Text M(Transform p, string t, int size = 18) => UIKit.Label(p, t, size, Pal.Muted);

        // ================================================================ MENU
        public void ShowMenu()
        {
            game.Hazards.SetIdle();
            var s = NewScreen(new Color(0, 0, 0, xr ? 0.85f : 0f));
            var left = UIKit.Card(s, "Left", 40, 14, new Color(0.045f, 0.05f, 0.065f, 0.95f));
            var lrt = left.rectTransform;
            lrt.anchorMin = new Vector2(0, 0); lrt.anchorMax = new Vector2(0, 1); lrt.pivot = new Vector2(0, 0.5f);
            lrt.sizeDelta = new Vector2(660, 0); lrt.anchoredPosition = Vector2.zero;

            UIKit.Label(left.transform, "FIREESCAPE MR", 58, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            M(left.transform, "Hệ thống huấn luyện ứng phó hỏa hoạn bằng Mixed Reality", 19);
            UIKit.Label(left.transform, "Train before the emergency.", 24, Pal.Text, TextAnchor.UpperLeft, FontStyle.Italic);
            M(left.transform, "Quan sát → suy nghĩ → quyết định → hành động dưới áp lực thời gian. AI phân tích hành vi và tạo bài tập tiếp theo từ chính lỗi của bạn.", 17);
            Spacer(left.transform, 6);

            BigButton(left.transform, "1 · KNOW", "Safety Foundation — nhận diện nguy hiểm, quy tắc, thiết bị", () => ShowKnow());
            BigButton(left.transform, "2 · PRACTICE", "Free Practice — Drill A · B · C · D", () => ShowPractice());
            BigButton(left.transform, "3 · MISSION", "8 nhóm tình huống × 4 level · Dynamic Fire · Time Pressure", () => ShowMissionSetup());
            var rec = AdaptiveCoach.Recommend(game.Profile, game.LastReport);
            BigButton(left.transform, "AI COACH · Nhiệm vụ đề xuất", rec.reason, () => game.StartMission(rec.request), Pal.BtnSel, 96);
            BigButton(left.transform, "Hồ sơ phản ứng", game.Profile.HasData ? $"{game.Profile.data.missions} lượt đã ghi nhận · điểm yếu: {Txt.Name(game.Profile.Weakest())}" : "Personal Error Profile", () => ShowProfile());
            if (game.LastRecord != null)
                BigButton(left.transform, "Kết quả gần nhất", game.LastRecord.def.title, () => ShowResult());

            var flex = UIKit.Rect(left.transform, "Flex"); UIKit.LE(flex, flexH: 1);
            M(left.transform, xr ? "Trigger phải: chọn · Joystick phải: cuộn" : "Chuột: chọn · Trong nhiệm vụ: WASD, chuột, E, F, C, Q, Esc", 16);

            var rm = UIKit.Card(s, "Mapping", 24, 8, new Color(0.03f, 0.08f, 0.1f, 0.92f));
            UIKit.Place(rm.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-30, 30), new Vector2(600, 400));
            UIKit.Label(rm.transform, "REALITY MAPPING", 22, new Color(0.3f, 0.95f, 1f), TextAnchor.UpperLeft, FontStyle.Bold);
            P(rm.transform, $"{game.ModeName}\n<b>{game.Layout.name}</b>", 18);
            P(rm.transform, "Đã nhận diện: " + string.Join(" · ", game.Layout.mappedElements), 17, Pal.Muted);
            P(rm.transform, "Lớp mô phỏng phủ lên không gian: <color=#FF7A2E>Fire</color> · <color=#B8BCC4>Smoke</color> · <color=#FF5A50>Alarm</color> · <color=#35D680>Emergency Exit</color> · <color=#D98CFF>NPC</color>", 17);
            if (game.Layout.placementReport.Count > 0)
                M(rm.transform, "Bố trí tự động: " + string.Join(" · ", game.Layout.placementReport.Take(4)), 14);
            M(rm.transform, game.Layout.isRealRoom
                ? "Nguy cơ được đưa vào chính căn phòng bạn đang đứng."
                : "Chưa có phòng thật: dùng không gian mô phỏng một dãy lớp học. Trên Quest, phòng thật sẽ được quét qua Meta MR Utility Kit.", 15);
            game.Env.SetScanVisible(true);
        }

        void BigButton(Transform parent, string title, string sub, Action onClick, Color? bg = null, float h = 76f)
        {
            var b = UIKit.Button(parent, "", onClick, bg, 22, h);
            var t = b.GetComponentInChildren<Text>();
            t.alignment = TextAnchor.MiddleLeft;
            t.fontStyle = FontStyle.Normal;
            t.text = $"<b>{title}</b>\n<size=15><color=#{Pal.Hex(Pal.Muted)}>{sub}</color></size>";
            UIKit.Stretch(t.rectTransform, 20, 6, 16, 6);
        }

        static void Spacer(Transform p, float h) { var r = UIKit.Rect(p, "Space"); UIKit.LE(r, prefH: h, minH: h); }

        RectTransform ScreenColumns(out RectTransform left, out RectTransform right, float leftW, string title, string subtitle)
        {
            var s = NewScreen();
            var v = UIKit.VStack(s, 16, 36);
            v.childForceExpandHeight = false;
            H(s, title, 36, Pal.Fire);
            if (subtitle != null) M(s, subtitle, 18);
            var row = UIKit.Rect(s, "Cols");
            var h = UIKit.HStack(row, 20); h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            UIKit.LE(row, flexH: 1);
            left = UIKit.Rect(row, "Left"); UIKit.VStack(left, 10); UIKit.LE(left, prefW: leftW, minW: leftW);
            right = UIKit.Rect(row, "Right"); UIKit.VStack(right, 12); UIKit.LE(right, flexW: 1);
            return s;
        }

        // ================================================================ KNOW
        public void ShowKnow()
        {
            ScreenColumns(out var left, out var right, 380, "KNOW — Safety Foundation", "Mục tiêu: \"Tôi biết mình đang nhìn thấy cái gì và nó có ý nghĩa gì.\"");
            for (int i = 0; i < SafetyKnowledge.Modules.Count; i++)
            {
                int idx = i;
                var m = SafetyKnowledge.Modules[i];
                UIKit.Button(left, $"{m.code}  {m.title}", () => { knowModule = idx; knowCard = 0; ShowKnow(); }, i == knowModule ? Pal.BtnSel : Pal.Btn, 20, 64)
                    .GetComponentInChildren<Text>().alignment = TextAnchor.MiddleLeft;
            }
            var flex = UIKit.Rect(left, "Flex"); UIKit.LE(flex, flexH: 1);
            UIKit.Button(left, "Sang PRACTICE ›", ShowPractice, Pal.BtnSel);
            UIKit.Button(left, "‹ Về menu", ShowMenu);

            var mod = SafetyKnowledge.Modules[knowModule];
            knowCard = Mathf.Clamp(knowCard, 0, mod.cards.Count - 1);
            var card = mod.cards[knowCard];
            var c = UIKit.Card(right, "Card", 34, 16);
            UIKit.LE(c, flexH: 1);
            UIKit.Label(c.transform, $"{mod.code} · {mod.title.ToUpper()}", 17, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            M(c.transform, mod.goal, 17);
            Spacer(c.transform, 8);
            H(c.transform, card.title, 38);
            P(c.transform, card.body, 24);
            var mean = UIKit.Card(c.transform, "Meaning", 18, 6, new Color(0.08f, 0.2f, 0.28f, 1f));
            UIKit.Label(mean.transform, "Ý NGHĨA", 15, Pal.Info, TextAnchor.UpperLeft, FontStyle.Bold);
            P(mean.transform, card.meaning, 21);
            var f2 = UIKit.Rect(c.transform, "Flex"); UIKit.LE(f2, flexH: 1);
            var nav = UIKit.Row(c.transform, 12, 56);
            UIKit.Button(nav, "‹ Trước", () => { if (knowCard > 0) knowCard--; else if (knowModule > 0) { knowModule--; knowCard = 99; } ShowKnow(); });
            UIKit.Label(nav, $"{knowCard + 1} / {mod.cards.Count}", 20, Pal.Muted, TextAnchor.MiddleCenter);
            UIKit.Button(nav, "Tiếp ›", () =>
            {
                if (knowCard < mod.cards.Count - 1) knowCard++;
                else if (knowModule < SafetyKnowledge.Modules.Count - 1) { knowModule++; knowCard = 0; }
                else { ShowPractice(); return; }
                ShowKnow();
            }, Pal.BtnSel);
        }

        // ================================================================ PRACTICE
        public void ShowPractice()
        {
            var s = NewScreen();
            UIKit.VStack(s, 16, 36);
            H(s, "PRACTICE — Free Practice Mode", 36, Pal.Fire);
            M(s, "Trước khi bước vào nhiệm vụ thật, tập riêng từng kỹ năng. Drill dùng chế độ Beginner (có gợi ý).", 18);
            var grid = UIKit.Rect(s, "Grid");
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(750, 250); g.spacing = new Vector2(20, 20);
            UIKit.LE(grid, flexH: 1);
            DrillCard(grid, DrillType.Reaction, "Một sự cố xuất hiện bất ngờ trong phòng. Đo thời gian từ dấu hiệu đầu tiên tới khi bạn kích hoạt chuông báo cháy.", "Phản ứng · Nhận diện");
            DrillCard(grid, DrillType.Navigation, "Chuông đã kêu. Tìm exit trong môi trường thay đổi — một tuyến có thể bị vật cản chặn hoặc đầy khói.", "Định hướng · Thích nghi");
            DrillCard(grid, DrillType.Interaction, "Tương tác với thiết bị mô phỏng: lấy bình chữa cháy và thực hiện PASS (Pull → Aim → Squeeze → Sweep) với đám cháy nhỏ.", "Thao tác thiết bị");
            DrillCard(grid, DrillType.Physical, xr
                ? "Physical Practice: lớp khói đã xuống thấp. Bạn phải thực sự cúi người và di chuyển tới lối thoát."
                : "Physical Practice: lớp khói đã xuống thấp. Giữ C/Ctrl để cúi (trên Quest: cúi người thật) và di chuyển tới lối thoát.", "Thể chất · Nhận diện khói");
            var row = UIKit.Row(s, 12, 56);
            UIKit.Button(row, "‹ Về menu", ShowMenu);
            UIKit.Button(row, "Sang MISSION ›", ShowMissionSetup, Pal.BtnSel);
        }

        void DrillCard(Transform parent, DrillType d, string desc, string skills)
        {
            var c = UIKit.Card(parent, "Drill", 24, 10);
            H(c.transform, Txt.Name(d), 26);
            P(c.transform, desc, 19);
            UIKit.Label(c.transform, "Luyện: " + skills, 16, Pal.Info);
            var f = UIKit.Rect(c.transform, "Flex"); UIKit.LE(f, flexH: 1);
            UIKit.Button(c.transform, "Bắt đầu drill", () => game.StartDrill(d), Pal.BtnSel, 20, 50);
        }

        // ================================================================ MISSION SETUP
        static readonly Dictionary<ScenarioType, string> Summaries = new Dictionary<ScenarioType, string>
        {
            { ScenarioType.ElectricalFire, "Một thiết bị điện gặp sự cố, sau đó xuất hiện khói và lửa. Nhận biết dấu hiệu bất thường, xác định khu vực nguy hiểm, quyết định phù hợp thay vì phản xạ." },
            { ScenarioType.SmokeFilledRoom, "Khói tích tụ nhanh khiến tầm nhìn giảm. Khói cũng là mối nguy hiểm, không chỉ ngọn lửa. Quyết định phải thay đổi khi điều kiện thay đổi." },
            { ScenarioType.BlockedExit, "Bạn đã chọn một lối thoát nhưng trên đường đi, lối đó trở nên không khả dụng. Kiểm tra khả năng thích nghi, không phải trí nhớ." },
            { ScenarioType.FireSpreading, "Cháy bắt đầu ở khu vực khác rồi ảnh hưởng tới vị trí của bạn. Nhận biết nguy hiểm gián tiếp, đừng chờ thấy lửa mới phản ứng." },
            { ScenarioType.PowerFailure, "Hệ thống điện gián đoạn trong lúc cháy. Xử lý khi ánh sáng và thông tin bị hạn chế." },
            { ScenarioType.MultipleHazards, "Đám cháy + khói + lối ra bị chặn + mất điện. Nhận diện → Ưu tiên → Quyết định → Thay đổi kế hoạch." },
            { ScenarioType.PersonInNeed, "Một người khác gặp khó khăn khi sơ tán. Đánh giá hoàn cảnh trước khi hành động — cứu người không mặc định là đúng." },
            { ScenarioType.DecisionTrap, "Nhóm bài kiểm tra sai lầm trong quyết định: đường quen thuộc, hoảng loạn, chần chừ, thông tin sai lệch." },
        };

        static readonly Dictionary<ScenarioType, string> VariantText = new Dictionary<ScenarioType, string>
        {
            { ScenarioType.ElectricalFire, "Cháy nhỏ vừa phát hiện · Khói trước rồi mới có lửa · Cháy gần lối ra · Sự cố khi bạn ở phòng khác" },
            { ScenarioType.SmokeFilledRoom, "Khói tăng chậm · Khói tăng rất nhanh · Một phần không thể quan sát · Mất phương hướng" },
            { ScenarioType.BlockedExit, "Cửa bị lửa chặn · Hành lang đầy khói · Có vật cản · Lối thoát không còn phù hợp sau một thời gian" },
            { ScenarioType.FireSpreading, "Cháy phòng bên cạnh · Cháy tầng dưới · Cháy phía sau bạn · Nhiều khu vực lần lượt bị ảnh hưởng" },
            { ScenarioType.PowerFailure, "Mất điện ngay từ đầu · Mất điện giữa nhiệm vụ · Một số khu vực tối hoàn toàn · Đèn khẩn cấp hỏng" },
            { ScenarioType.MultipleHazards, "00:15 chưa báo động → leo thang · 00:35 một tuyến bị chặn · 01:10 khói ở khu vực khác · 01:30 NPC cần giúp · 01:50 lối đang tới không còn phù hợp" },
            { ScenarioType.PersonInNeed, "Người không biết đường · Người di chuyển chậm · Người hoảng loạn · Người bị mắc kẹt trong vùng cháy" },
            { ScenarioType.DecisionTrap, "Đường quen thuộc · Hoảng loạn · Chần chừ · Thông tin sai lệch" },
        };

        public void ShowMissionSetup()
        {
            ScreenColumns(out var left, out var right, 560, "MISSION — Scenario System", "\"Bạn đang ở đây. Sự cố vừa xảy ra. Hãy tự xử lý.\"  Mỗi tình huống có nhiều level và biến thể — không có một đường đi duy nhất để học thuộc.");
            var grid = UIKit.Rect(left, "Grid");
            var g = grid.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = new Vector2(270, 74); g.spacing = new Vector2(12, 12);
            UIKit.LE(grid, prefH: 4 * 74 + 3 * 12);
            foreach (var t in Txt.AllScenarios)
            {
                var tt = t;
                int cleared = game.Profile.ClearedLevel(t);
                var b = UIKit.Button(grid, "", () => { selType = tt; ShowMissionSetup(); }, t == selType ? Pal.BtnSel : Pal.Btn);
                b.GetComponentInChildren<Text>().text = $"<b>{Txt.Name(t)}</b>\n<size=14><color=#{Pal.Hex(Pal.Muted)}>Đã vượt qua: {cleared}/4</color></size>";
            }
            var flex = UIKit.Rect(left, "Flex"); UIKit.LE(flex, flexH: 1);
            UIKit.Button(left, "‹ Về menu", ShowMenu);

            var c = UIKit.Card(right, "Detail", 30, 12);
            UIKit.LE(c, flexH: 1);
            H(c.transform, Txt.Name(selType), 34);
            P(c.transform, Summaries[selType], 20);
            UIKit.Label(c.transform, "Biến thể: " + VariantText[selType], 17, Pal.Muted);
            Spacer(c.transform, 6);

            UIKit.Label(c.transform, "LEVEL", 16, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            var lv = UIKit.Row(c.transform, 10, 64);
            for (int i = 1; i <= 4; i++)
            {
                int l = i;
                var b = UIKit.Button(lv, "", () => { selLevel = l; ShowMissionSetup(); }, l == selLevel ? Pal.BtnSel : Pal.Btn, 18, 64);
                b.GetComponentInChildren<Text>().text = $"<b>Level {l}</b>\n<size=13>{ScenarioLibrary.LevelNames[l]}</size>";
            }
            UIKit.Label(c.transform, "CHẾ ĐỘ HƯỚNG DẪN", 16, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            var gm = UIKit.Row(c.transform, 10, 64);
            foreach (GuidanceMode m in Enum.GetValues(typeof(GuidanceMode)))
            {
                var mm = m;
                var b = UIKit.Button(gm, "", () => { selGuidance = mm; ShowMissionSetup(); }, m == selGuidance ? Pal.BtnSel : Pal.Btn, 18, 64);
                b.GetComponentInChildren<Text>().text = $"<b>{Txt.Name(m)}</b>\n<size=13>{Txt.Describe(m)}</size>";
            }
            var st = UIKit.Row(c.transform, 10, 56);
            UIKit.Button(st, selStress ? "STRESS MODE: BẬT" : "STRESS MODE: TẮT", () => { selStress = !selStress; ShowMissionSetup(); }, selStress ? Pal.BtnSel : Pal.Btn, 18);
            M(st, "Chuông lớn, tiếng người, diễn biến nhanh hơn 20%", 15);

            var f2 = UIKit.Rect(c.transform, "Flex"); UIKit.LE(f2, flexH: 1);
            UIKit.Button(c.transform, "BẮT ĐẦU NHIỆM VỤ", () => game.StartMission(new ScenarioRequest
            {
                type = selType, level = selLevel, guidance = selGuidance, stress = selStress, seed = UnityEngine.Random.Range(1, 1_000_000)
            }), Pal.Fire, 26, 70);
        }

        // ================================================================ BRIEFING
        public void ShowBriefing(ScenarioDef d, string where)
        {
            var s = NewScreen(new Color(0, 0, 0, 0.55f));
            var c = UIKit.Card(s, "Brief", 40, 14);
            UIKit.Place(c.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 620));
            UIKit.Label(c.transform, d.request.drill != DrillType.None ? "PRACTICE" : "MISSION", 18, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            H(c.transform, d.request.drill != DrillType.None ? d.title : Txt.Name(d.request.type) + " — Level " + d.request.level, 40);
            P(c.transform, "Vị trí của bạn: <b>" + where + "</b>", 20, Pal.Muted);
            UIKit.Label(c.transform, d.briefing, 28, Pal.Text, TextAnchor.UpperLeft, FontStyle.Italic);
            if (d.request.drill == DrillType.None)
                P(c.transform, $"Chế độ: <b>{Txt.Name(d.request.guidance)}</b> — {Txt.Describe(d.request.guidance)}{(d.request.stress ? " · <color=#FF7A2E>STRESS MODE</color>" : "")}", 19);
            var f = UIKit.Rect(c.transform, "Flex"); UIKit.LE(f, flexH: 1);
            M(c.transform, xr
                ? "Đi lại thật trong phòng · Trigger phải: tương tác / phun · A: kiểm tra cửa · B: bỏ bình · Menu: tạm dừng" + (game.Layout.isRealRoom ? "" : " · Joystick trái: di chuyển")
                : "WASD: di chuyển · Chuột: nhìn · Shift: chạy · C/Ctrl: cúi · E / chuột trái: tương tác, giữ để phun · F: kiểm tra cửa · Q: bỏ bình · Esc: tạm dừng", 16);
            UIKit.Button(c.transform, xr ? "BẮT ĐẦU (Trigger / A)" : "BẮT ĐẦU (Enter)", game.BeginRunning, Pal.Fire, 24, 64);
        }

        // ================================================================ HUD
        void BuildHud()
        {
            var r = (RectTransform)hudCanvas.transform;
            float sx = xr ? 1000 : 1600, sy = xr ? 560 : 900;

            var tl = UIKit.Card(r, "TopLeft", 14, 2, new Color(0, 0, 0, 0.55f));
            UIKit.Place(tl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -20), new Vector2(xr ? 360 : 460, 96));
            hudTitle = UIKit.Label(tl.transform, "", 17, Pal.Muted);
            hudClock = UIKit.Label(tl.transform, "00:00", 40, Pal.Text, TextAnchor.UpperLeft, FontStyle.Bold);

            var tr = UIKit.Card(r, "TopRight", 14, 6, new Color(0, 0, 0, 0.55f));
            UIKit.Place(tr.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -20), new Vector2(xr ? 360 : 420, 110));
            UIKit.Label(tr.transform, "PHƠI NHIỄM KHÓI / NHIỆT", 14, Pal.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            hudExposure = UIKit.Bar(tr.transform, Pal.Safe, 14);
            hudStatus = UIKit.Label(tr.transform, "", 15, Pal.Text);

            crosshair = UIKit.Panel(r, "Crosshair", new Color(1, 1, 1, 0.7f)).gameObject;
            UIKit.Place((RectTransform)crosshair.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 6));
            crosshair.SetActive(!xr);

            promptBox = Box(r, new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(760, 50), new Color(0, 0, 0, 0.6f), out hudPrompt, 20, Pal.Text);
            toastBox = Box(r, new Vector2(0.5f, 0), new Vector2(0, xr ? 70 : 110), new Vector2(xr ? 900 : 1000, 60), new Color(0.05f, 0.05f, 0.06f, 0.8f), out hudToast, 21, Pal.Text);
            announceBox = Box(r, new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(xr ? 560 : 820, 70), new Color(0.3f, 0.22f, 0.02f, 0.9f), out hudAnnounce, 21, Pal.Warn);
            hintBox = Box(r, new Vector2(0, 0), new Vector2(xr ? 250 : 330, 40), new Vector2(xr ? 460 : 600, 90), new Color(0.02f, 0.22f, 0.12f, 0.88f), out hudHint, 18, new Color(0.7f, 1f, 0.8f));
            warnBox = Box(r, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(700, 50), new Color(0.4f, 0.03f, 0.03f, 0.8f), out hudWarn, 20, Color.white);
            helpBox = Box(r, new Vector2(1, 0), new Vector2(-270, 30), new Vector2(520, 60), new Color(0, 0, 0, 0.45f), out hudHelp, 14, Pal.Muted);
            hudHint.alignment = TextAnchor.MiddleLeft;
            hudHelp.text = "WASD di chuyển · Chuột nhìn · Shift chạy · C cúi\nE tương tác (giữ để phun) · F kiểm tra cửa · Q bỏ bình · Esc dừng";
            helpBox.SetActive(false);
            promptBox.SetActive(false); toastBox.SetActive(false); announceBox.SetActive(false); hintBox.SetActive(false); warnBox.SetActive(false);
        }

        static GameObject Box(RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color bg, out Text text, int fs, Color fc)
        {
            var img = UIKit.Panel(parent, "Box", bg);
            UIKit.Place(img.rectTransform, anchor, new Vector2(0.5f, 0.5f), pos, size);
            text = UIKit.Label(img.transform, "", fs, fc, TextAnchor.MiddleCenter);
            UIKit.Stretch(text.rectTransform, 14, 4, 14, 4);
            return img.gameObject;
        }

        public void ShowHud(ScenarioDef d)
        {
            HideMenu();
            hudCanvas.gameObject.SetActive(true);
            hudTitle.text = d.request.drill != DrillType.None ? d.title : $"{Txt.Name(d.request.type)} · L{d.request.level} · {Txt.Name(d.request.guidance)}{(d.request.stress ? " · STRESS" : "")}";
            helpBox.SetActive(!xr && d.request.guidance != GuidanceMode.Expert);
            toastUntil = announceUntil = hintUntil = 0f;
            game.Env.SetScanVisible(false);
        }

        public void HideHud() => hudCanvas.gameObject.SetActive(false);

        public void Toast(string msg, float secs = 3.5f)
        {
            if (string.IsNullOrEmpty(msg)) return;
            hudToast.text = msg; toastUntil = Time.time + secs;
        }

        public void Announce(string msg, bool reliable)
        {
            hudAnnounce.text = msg; announceUntil = Time.time + 6f;
        }

        public void Hint(string msg)
        {
            hudHint.text = "GỢI Ý: " + msg; hintUntil = Time.time + 8f;
        }

        public void UpdateHud(IncidentSimulation sim, string prompt)
        {
            if (!hudCanvas.gameObject.activeSelf) return;
            bool expert = sim.def.request.guidance == GuidanceMode.Expert;
            bool beginner = sim.def.request.guidance == GuidanceMode.Beginner;
            hudClock.text = Txt.Time(sim.time);
            float e = Mathf.Clamp01(sim.exposure / 100f);
            hudExposure.fillAmount = e;
            hudExposure.color = e < 0.4f ? Pal.Safe : e < 0.7f ? Pal.Warn : Pal.Danger;

            string ext = game.HoldingExtinguisher
                ? (game.PinPulled ? $"Bình: {game.ExtinguisherCharge * 100f:0}%{(game.Spraying ? " · ĐANG PHUN" : "")}" : "Bình: chưa rút chốt")
                : "Bình: —";
            hudStatus.text = expert
                ? ext + (game.Player.Crouching ? " · Đang cúi" : "")
                : $"Chuông: {(sim.alarm ? "<color=#FF5A50>ĐANG KÊU</color>" : "tắt")} · Điện: {(sim.power ? "có" : "<color=#FFCC40>MẤT</color>")}\n{ext}{(game.Player.Crouching ? " · <color=#35D680>Đang cúi</color>" : "")}";

            promptBox.SetActive(!string.IsNullOrEmpty(prompt));
            if (!string.IsNullOrEmpty(prompt)) hudPrompt.text = prompt;
            toastBox.SetActive(Time.time < toastUntil);
            announceBox.SetActive(Time.time < announceUntil);
            hintBox.SetActive(beginner && Time.time < hintUntil);
            bool warn = beginner && sim.headInSmoke;
            warnBox.SetActive(warn || (!sim.Running));
            if (!sim.Running)
                hudWarn.text = sim.outcome == MissionOutcome.Failed ? "THẤT BẠI — " + sim.outcomeReason : "HOÀN THÀNH — " + sim.outcomeReason;
            else if (warn)
                hudWarn.text = $"ĐẦU BẠN ĐANG Ở TRONG LỚP KHÓI — CÚI THẤP ({game.Player.Key("C")})";
            ((Image)warnBox.GetComponent<Image>()).color = !sim.Running && sim.outcome != MissionOutcome.Failed ? new Color(0.05f, 0.35f, 0.18f, 0.9f) : new Color(0.4f, 0.03f, 0.03f, 0.85f);
        }

        void LateUpdate()
        {
            if (xr && menuCanvas.gameObject.activeSelf)
            {
                // menu nằm ngoài tầm nhìn (> 55°) hoặc quá xa → đưa về trước mặt người chơi
                var head = game.Player.Head;
                var toMenu = menuCanvas.transform.position - head.position;
                if (Vector3.Angle(head.forward, toMenu) > 55f || toMenu.magnitude > 3f || toMenu.magnitude < 0.5f)
                    PlaceInFront(menuCanvas, 1.35f, 0.1f);
            }
            if (xr && hudCanvas.gameObject.activeSelf)
            {
                var head = game.Player.Head;
                var target = head.position + head.forward * 1.15f;
                var t = hudCanvas.transform;
                t.position = Vector3.Lerp(t.position, target, 6f * Time.deltaTime);
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(t.position - head.position), 6f * Time.deltaTime);
            }
        }

        // ================================================================ PAUSE
        public void ShowPause()
        {
            var s = NewScreen(new Color(0, 0, 0, 0.6f));
            var c = UIKit.Card(s, "Pause", 36, 14);
            UIKit.Place(c.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560, 400));
            H(c.transform, "TẠM DỪNG", 40);
            M(c.transform, "Thời gian mô phỏng đang dừng.", 18);
            UIKit.Button(c.transform, "Tiếp tục", game.Resume, Pal.Fire);
            UIKit.Button(c.transform, "Chơi lại từ đầu", game.Restart);
            UIKit.Button(c.transform, "Thoát về menu", game.QuitToMenu);
        }

        // ================================================================ RESULT + AI ERROR ANALYSIS
        public void ShowResult()
        {
            var r = game.LastRecord; var rep = game.LastReport;
            if (r == null || rep == null) { ShowMenu(); return; }
            var s = NewScreen();
            UIKit.VStack(s, 12, 30);

            var head = UIKit.Row(s, 20, 90);
            var hl = UIKit.Rect(head, "L"); UIKit.VStack(hl, 4); UIKit.LE(hl, flexW: 1);
            var col = rep.outcome == MissionOutcome.Failed ? Pal.Danger : Pal.Safe;
            UIKit.Label(hl, $"{rep.headline}  <size=22><color=#{Pal.Hex(Pal.Muted)}>{r.def.title}</color></size>", 38, col, TextAnchor.UpperLeft, FontStyle.Bold);
            P(hl, rep.reason, 19, Pal.Muted);
            var hr = UIKit.Rect(head, "R"); UIKit.VStack(hr, 0); UIKit.LE(hr, prefW: 300, minW: 300);
            UIKit.Label(hr, $"{rep.total:0}", 60, Pal.Score(rep.total), TextAnchor.UpperRight, FontStyle.Bold);
            UIKit.Label(hr, rep.verdict, 20, rep.verdict.Contains("High Risk") ? Pal.Danger : Pal.Safe, TextAnchor.UpperRight, FontStyle.Bold);

            var sr = UIKit.Scroll(s, out var content, 14, 4);

            // điểm theo nhiều yếu tố
            var scores = UIKit.Card(content, "Scores", 20, 10);
            UIKit.Label(scores.transform, "ĐÁNH GIÁ ĐA YẾU TỐ — không tối ưu đơn thuần cho tốc độ", 15, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            var srow = UIKit.Row(scores.transform, 18);
            ScoreCell(srow, "Safety", rep.safety);
            ScoreCell(srow, "Decision Quality", rep.decision);
            ScoreCell(srow, "Time", rep.timeScore);
            ScoreCell(srow, "Route Efficiency", rep.route);
            ScoreCell(srow, "Response Consistency", rep.consistency);

            var te = UIKit.Card(content, "Time", 20, 10);
            UIKit.Label(te.transform, "TIME ENGINE", 15, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            var trow = UIKit.Row(te.transform, 18);
            TimeCell(trow, "T1 Reaction", rep.T1, "dấu hiệu → hành động đầu tiên");
            TimeCell(trow, "T2 Decision", rep.T2, "hành động → chọn tuyến");
            TimeCell(trow, "T3 Execution", rep.T3, "chọn tuyến → kết thúc");
            TimeCell(trow, "T4 Recovery", rep.T4, "tuyến bị chặn → đổi kế hoạch");
            TimeCell(trow, "Total Response", rep.totalTime, $"mục tiêu ≤ {r.def.parTime:0}s");
            if (!string.IsNullOrEmpty(rep.drillResult)) P(te.transform, "<b>Kết quả drill:</b> " + rep.drillResult, 19, Pal.Info);

            var sc = UIKit.Card(content, "Scenario", 20, 6);
            UIKit.Label(sc.transform, "TÌNH HUỐNG VỪA TRẢI QUA", 15, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            P(sc.transform, $"<b>{r.def.variant}</b>", 21);
            if (!string.IsNullOrEmpty(r.def.cause)) P(sc.transform, "Nguyên nhân: " + r.def.cause, 18, Pal.Muted);
            if (!string.IsNullOrEmpty(r.def.initialSigns)) P(sc.transform, "Dấu hiệu ban đầu: " + r.def.initialSigns, 18, Pal.Muted);
            if (!string.IsNullOrEmpty(r.def.development)) P(sc.transform, "Diễn biến: " + r.def.development, 18, Pal.Muted);
            P(sc.transform, "Mục tiêu học: " + r.def.learningGoal, 18, Pal.Muted);

            UIKit.Label(content, "AI ERROR ANALYSIS", 24, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            var problems = rep.Problems.ToList();
            if (problems.Count == 0) P(content, "Không phát hiện lỗi đáng kể trong lượt này.", 19, Pal.Safe);
            foreach (var f in problems) FindingCard(content, f);

            var good = rep.Strengths.ToList();
            if (good.Count > 0)
            {
                var gc = UIKit.Card(content, "Good", 20, 6, new Color(0.05f, 0.16f, 0.1f, 1f));
                UIKit.Label(gc.transform, "LÀM TỐT", 15, Pal.Safe, TextAnchor.UpperLeft, FontStyle.Bold);
                foreach (var f in good) P(gc.transform, $"<b>{f.title}</b> — {f.what}", 18);
            }

            var sk = UIKit.Card(content, "Skills", 20, 6);
            UIKit.Label(sk.transform, "KỸ NĂNG ĐO ĐƯỢC TRONG LƯỢT NÀY (cập nhật vào hồ sơ)", 15, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            foreach (var kv in rep.skills) SkillRow(sk.transform, Txt.Name(kv.Key), kv.Value, null);

            var rec = game.LastRecommendation;
            if (rec != null)
            {
                var ac = UIKit.Card(content, "Adaptive", 20, 8, new Color(0.2f, 0.09f, 0.04f, 1f));
                UIKit.Label(ac.transform, "ADAPTIVE AI — BÀI TẬP TIẾP THEO", 15, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
                P(ac.transform, rec.reason, 19);
                if (rec.suggestedDrill != DrillType.None) M(ac.transform, "Gợi ý thêm: " + Txt.Name(rec.suggestedDrill), 17);
            }

            var btns = UIKit.Row(s, 12, 60);
            UIKit.Button(btns, "Incident Replay", () => ShowReplay(false));
            UIKit.Button(btns, "What if?", () => ShowReplay(true));
            if (rec != null) UIKit.Button(btns, "AI: nhiệm vụ tiếp theo", () => game.StartMission(rec.request), Pal.Fire);
            if (rec != null && rec.suggestedDrill != DrillType.None) UIKit.Button(btns, Txt.Name(rec.suggestedDrill), () => game.StartDrill(rec.suggestedDrill));
            UIKit.Button(btns, "Chơi lại", game.Restart);
            UIKit.Button(btns, "Menu", ShowMenu);
        }

        static void ScoreCell(Transform row, string name, float v)
        {
            var c = UIKit.Rect(row, name); UIKit.VStack(c, 4); UIKit.LE(c, flexW: 1);
            UIKit.Label(c, name, 16, Pal.Muted);
            UIKit.Label(c, $"{v:0}", 32, Pal.Score(v), TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Bar(c, Pal.Score(v), 8).fillAmount = v / 100f;
        }

        static void TimeCell(Transform row, string name, float v, string desc)
        {
            var c = UIKit.Rect(row, name); UIKit.VStack(c, 2); UIKit.LE(c, flexW: 1);
            UIKit.Label(c, name, 16, Pal.Muted, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Label(c, Txt.Secs(v), 28, Pal.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Label(c, desc, 13, Pal.Muted);
        }

        static void FindingCard(Transform parent, Finding f)
        {
            var sev = f.severity >= 3 ? Pal.Danger : f.severity == 2 ? Pal.Fire : Pal.Warn;
            var row = UIKit.Panel(parent, "Finding", Pal.Card);
            var h = UIKit.HStack(row, 16, 0); h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            var stripe = UIKit.Panel(row.transform, "Stripe", sev); UIKit.LE(stripe, prefW: 6, minW: 6);
            var body = UIKit.Rect(row.transform, "Body"); var v = UIKit.VStack(body, 6); v.padding = new RectOffset(4, 20, 16, 16); UIKit.LE(body, flexW: 1);
            string sevText = f.severity >= 3 ? "NGUY HIỂM" : f.severity == 2 ? "NGHIÊM TRỌNG" : "CẦN CẢI THIỆN";
            UIKit.Label(body, $"<b>{f.title}</b>  <size=15><color=#{Pal.Hex(sev)}>{sevText}</color> · {Txt.Name(f.skill)}{(f.time >= 0 ? " · " + Txt.Time(f.time) : "")}</size>", 22, Pal.Text);
            QA(body, "What happened?", f.what);
            QA(body, "What went wrong?", f.wrong);
            QA(body, "Why?", f.why);
            QA(body, "Risk?", f.risk);
            var rule = SafetyKnowledge.Rule(f.ruleId);
            if (rule != null) QA(body, "Better approach?", $"<b>{rule.title}.</b> {rule.text}");
            QA(body, "How to improve?", f.improve);
        }

        static void QA(Transform p, string q, string a)
        {
            if (string.IsNullOrEmpty(a)) return;
            UIKit.Label(p, $"<color=#{Pal.Hex(Pal.Muted)}><b>{q}</b></color>  {a}", 17, Pal.Text);
        }

        static void SkillRow(Transform p, string name, float v, string extra)
        {
            var r = UIKit.Row(p, 14, 30);
            var n = UIKit.Label(r, name, 18, Pal.Text, TextAnchor.MiddleLeft); UIKit.LE(n, prefW: 230, minW: 230, flexW: 0);
            var b = UIKit.Bar(r, Pal.Score(v), 16); b.fillAmount = v / 100f;
            ((RectTransform)b.transform.parent).GetComponent<LayoutElement>().flexibleWidth = 1;
            var t = UIKit.Label(r, $"{v:0}" + (extra != null ? $"  <size=13><color=#{Pal.Hex(Pal.Muted)}>{extra}</color></size>" : ""), 18, Pal.Score(v), TextAnchor.MiddleRight, FontStyle.Bold);
            UIKit.LE(t, prefW: 120, minW: 120, flexW: 0);
        }

        // ================================================================ REPLAY + WHAT IF
        public void ShowReplay(bool runWhatIf)
        {
            rp = game.LastRecord;
            if (rp == null) { ShowMenu(); return; }
            var s = NewScreen();
            UIKit.VStack(s, 12, 26);
            H(s, "INCIDENT REPLAY — " + rp.def.title, 30, Pal.Fire);
            M(s, "Xem lại toàn bộ hành trình của chính bạn. Nhấn vào một mốc trên timeline để nhảy tới thời điểm đó.", 16);

            var cols = UIKit.Rect(s, "Cols");
            var h = UIKit.HStack(cols, 18); h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            UIKit.LE(cols, flexH: 1);

            var left = UIKit.Rect(cols, "Left"); UIKit.VStack(left, 10); UIKit.LE(left, prefW: 1020, minW: 1020);
            map = new MapView(left, rp.layout, new Vector2(1020, 360));
            var ctr = UIKit.Row(left, 10, 48);
            playBtn = UIKit.Button(ctr, "II", TogglePlay, Pal.Btn, 20, 48); UIKit.LE(playBtn, prefW: 70, minW: 70, prefH: 48, minH: 48);
            speedBtn = UIKit.Button(ctr, "x1", CycleSpeed, Pal.Btn, 18, 48); UIKit.LE(speedBtn, prefW: 70, minW: 70, prefH: 48, minH: 48);
            slider = MakeSlider(ctr);
            timeLabel = UIKit.Label(ctr, "00:00", 20, Pal.Text, TextAnchor.MiddleRight, FontStyle.Bold); UIKit.LE(timeLabel, prefW: 150, minW: 150);
            M(left, $"<color=#{Pal.Hex(YouColor)}>— Bạn</color>   <color=#{Pal.Hex(AltColors[0])}>— What-if</color>   nét mờ = đang cúi · <color=#FF7A2E>●</color> lửa · vùng xám = khói · <color=#F2474A>▬</color> lối bị chặn · <color=#D98CFF>●</color> NPC", 15);

            var wi = UIKit.Card(left, "WhatIf", 14, 8, new Color(0.13f, 0.08f, 0.05f, 1f));
            whatIfBox = (RectTransform)wi.transform;
            UIKit.LE(wi, flexH: 1);
            whatIfTitle = UIKit.Label(wi.transform, "WHAT IF? — Nếu bạn chọn tuyến khác thì sao?", 17, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            if (alts == null || !ReferenceEquals(altsFor, rp))
            {
                alts = null; altSel = -1;
                UIKit.Button(wi.transform, "Chạy mô phỏng nhánh khác", () => { RunWhatIf(); ShowReplay(false); }, Pal.BtnSel, 18, 48);
                M(wi.transform, "Hệ thống dựng lại đúng tình huống (cùng seed), phát lại hành trình của bạn tới thời điểm quyết định, rồi cho một tác tử đi theo lối thoát khác.", 15);
            }
            else BuildWhatIfResults(wi.transform);

            var right = UIKit.Rect(cols, "Right"); UIKit.VStack(right, 8); UIKit.LE(right, flexW: 1);
            UIKit.Label(right, "TIMELINE", 16, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Scroll(right, out var tl, 4, 0);
            foreach (var e in rp.log)
            {
                if (e.tag == "zone" || e.tag == "fire" && e.time > 0.5f && rp.log.Count > 40) continue;
                var ev = e;
                var c = e.cat switch
                {
                    LogCategory.Incident => Pal.Fire,
                    LogCategory.Decision => Pal.Info,
                    LogCategory.Action => Pal.Safe,
                    LogCategory.Hazard => new Color(1f, 0.6f, 0.45f),
                    LogCategory.Npc => new Color(0.85f, 0.6f, 1f),
                    LogCategory.Outcome => Pal.Warn,
                    _ => Pal.Muted
                };
                var b = UIKit.Button(tl, "", () => { rpT = ev.time; rpPlaying = false; RefreshReplay(); }, new Color(0.12f, 0.13f, 0.16f, 1f), 16, 40);
                var t = b.GetComponentInChildren<Text>();
                t.alignment = TextAnchor.MiddleLeft; t.fontStyle = FontStyle.Normal; t.fontSize = 16;
                t.text = $"<b>{Txt.Time(e.time)}</b>  <color=#{Pal.Hex(c)}>{e.text}</color>";
                UIKit.LE(b, prefH: 44, minH: 36);
            }

            var btns = UIKit.Row(s, 12, 56);
            UIKit.Button(btns, "‹ Kết quả & AI phân tích", ShowResult);
            UIKit.Button(btns, "Chơi lại nhánh này (cùng tình huống)", game.ReplaySameScenario, Pal.Fire);
            UIKit.Button(btns, "Menu", ShowMenu);

            replayActive = true;
            rpT = 0f; rpPlaying = true;
            if (runWhatIf && (alts == null || !ReferenceEquals(altsFor, rp))) { RunWhatIf(); ShowReplay(false); return; }
            RefreshReplay();
        }

        MissionRecord altsFor;

        void RunWhatIf()
        {
            alts = game.RunWhatIf(rp);
            altsFor = rp;
            altSel = -1;
        }

        void BuildWhatIfResults(Transform wi)
        {
            float dt = alts.Count > 0 ? alts[0].decisionTime : 0f;
            whatIfTitle.text = $"WHAT IF? — rẽ nhánh tại {Txt.Time(dt)} (thời điểm bạn chọn tuyến)";
            var you = UIKit.Row(wi, 10, 40);
            var yb = UIKit.Button(you, "", () => { altSel = -1; rpT = 0; rpPlaying = true; }, altSel < 0 ? Pal.BtnSel : Pal.Btn, 16, 40);
            UIKit.LE(yb, prefW: 150, minW: 150, prefH: 40, minH: 40);
            yb.GetComponentInChildren<Text>().text = $"<color=#{Pal.Hex(YouColor)}>Bạn</color>";
            P(you, $"{(rp.outcome == MissionOutcome.Failed ? "<color=#F2474A>Thất bại</color>" : "<color=#35D680>Thoát</color>")} sau {Txt.Time(rp.endTime)} · phơi nhiễm {rp.maxExposure:0} · {rp.reason}", 15);
            for (int i = 0; i < alts.Count; i++)
            {
                var a = alts[i]; int idx = i;
                var row = UIKit.Row(wi, 10, 40);
                var b = UIKit.Button(row, "", () => { altSel = idx; rpT = 0; rpPlaying = true; }, altSel == i ? Pal.BtnSel : Pal.Btn, 16, 40);
                UIKit.LE(b, prefW: 150, minW: 150, prefH: 40, minH: 40);
                b.GetComponentInChildren<Text>().text = $"<color=#{Pal.Hex(AltColors[i % AltColors.Length])}>{a.exitName}</color>";
                string res = a.Escaped ? $"<color=#35D680>Thoát</color> sau {Txt.Time(a.endTime)}" : "<color=#F2474A>Thất bại</color>";
                string diff = a.Escaped && rp.outcome != MissionOutcome.Failed ? $" ({(a.endTime < rp.endTime ? "nhanh hơn" : "chậm hơn")} {Mathf.Abs(a.endTime - rp.endTime):0}s)" : "";
                var ev = a.events.Where(x => x.cat == LogCategory.Hazard || x.cat == LogCategory.Decision).Select(x => x.text).Take(2);
                P(row, $"{res}{diff} · phơi nhiễm {a.maxExposure:0} · {string.Join(" ", ev)}", 15);
            }
            M(wi, "\"Một quyết định ở phút đầu có thể thay đổi toàn bộ diễn biến sau đó.\" Chọn một nhánh để xem diễn biến của nó trên bản đồ.", 14);
        }

        Slider MakeSlider(Transform parent)
        {
            var root = UIKit.Rect(parent, "Slider");
            UIKit.LE(root, flexW: 1, prefH: 48, minH: 48);
            var bg = UIKit.Panel(root, "Bg", new Color(1, 1, 1, 0.1f));
            bg.rectTransform.anchorMin = new Vector2(0, 0.5f); bg.rectTransform.anchorMax = new Vector2(1, 0.5f);
            bg.rectTransform.sizeDelta = new Vector2(0, 10);
            var fillArea = UIKit.Rect(root, "FillArea");
            fillArea.anchorMin = new Vector2(0, 0.5f); fillArea.anchorMax = new Vector2(1, 0.5f); fillArea.sizeDelta = new Vector2(0, 10);
            var fill = UIKit.Panel(fillArea, "Fill", Pal.Fire);
            fill.rectTransform.sizeDelta = Vector2.zero;
            var handleArea = UIKit.Rect(root, "HandleArea");
            UIKit.Stretch(handleArea, 10, 0, 10, 0);
            var handle = UIKit.Panel(handleArea, "Handle", Color.white);
            handle.rectTransform.sizeDelta = new Vector2(18, 30);
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = 0; s.maxValue = 1;
            s.onValueChanged.AddListener(v => { if (sliderLock) return; rpT = v * rp.endTime; rpPlaying = false; RefreshReplay(); });
            return s;
        }

        void TogglePlay()
        {
            if (rp == null) return;
            if (!rpPlaying && rpT >= MaxT() - 0.05f) rpT = 0f;
            rpPlaying = !rpPlaying;
        }

        void CycleSpeed()
        {
            rpSpeed = rpSpeed >= 8f ? 1f : rpSpeed * 2f;
            UIKit.SetButton(speedBtn, "x" + rpSpeed);
        }

        float MaxT()
        {
            float m = rp.endTime;
            if (alts != null && ReferenceEquals(altsFor, rp)) foreach (var a in alts) m = Mathf.Max(m, a.endTime);
            return m;
        }

        static PathSample SampleAt(List<PathSample> path, float t)
        {
            if (path.Count == 0) return null;
            if (t <= path[0].t) return path[0];
            for (int i = 1; i < path.Count; i++)
                if (path[i].t >= t) return new PathSample { t = t, pos = Vector3.Lerp(path[i - 1].pos, path[i].pos, Mathf.InverseLerp(path[i - 1].t, path[i].t, t)), crouch = path[i].crouch };
            return path[path.Count - 1];
        }

        static SimSnapshot SnapAt(List<SimSnapshot> snaps, float t)
        {
            SimSnapshot best = snaps.Count > 0 ? snaps[0] : null;
            foreach (var s in snaps) { if (s.time <= t) best = s; else break; }
            return best;
        }

        void RefreshReplay()
        {
            if (!replayActive || rp == null || map == null) return;
            bool showAlts = alts != null && ReferenceEquals(altsFor, rp);
            var snap = showAlts && altSel >= 0 ? SnapAt(alts[altSel].snaps, rpT) : rp.SnapshotAt(rpT);
            map.ShowSnapshot(snap);
            map.DrawPath("you", rp.path, rpT, YouColor, 4f);
            var you = rp.SampleAt(Mathf.Min(rpT, rp.endTime));
            map.Marker("you", you.pos, YouColor, 18f);
            if (showAlts)
                for (int i = 0; i < alts.Count; i++)
                {
                    var col = AltColors[i % AltColors.Length];
                    if (altSel >= 0 && altSel != i) col.a = 0.3f;
                    map.DrawPath("alt" + i, alts[i].path, rpT, col, 3f);
                    var a = SampleAt(alts[i].path, Mathf.Min(rpT, alts[i].endTime));
                    map.Marker("alt" + i, a?.pos, col, 14f);
                }
            sliderLock = true;
            slider.value = Mathf.Clamp01(rpT / Mathf.Max(0.1f, rp.endTime));
            sliderLock = false;
            timeLabel.text = $"{Txt.Time(rpT)} / {Txt.Time(MaxT())}";
            UIKit.SetButton(playBtn, rpPlaying ? "II" : "►");
        }

        // ================================================================ PROFILE
        public void ShowProfile()
        {
            ScreenColumns(out var left, out var right, 760, "PERSONAL ERROR PROFILE", "Hồ sơ phản ứng của bạn qua nhiều nhiệm vụ — insight có giá trị hơn một con số điểm đơn thuần.");
            var p = game.Profile;
            var sk = UIKit.Card(left, "Skills", 24, 10);
            UIKit.Label(sk.transform, "HỒ SƠ PHẢN ỨNG", 16, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            foreach (var s in Txt.AllSkills)
            {
                var e = p.Get(s);
                SkillRow(sk.transform, Txt.Name(s), e.samples > 0 ? e.value : 0f, e.samples > 0 ? $"{e.samples} lượt" : "chưa đo");
            }
            var ins = UIKit.Card(left, "Insights", 24, 8, new Color(0.08f, 0.14f, 0.2f, 1f));
            UIKit.Label(ins.transform, "AI PHÁT HIỆN", 16, Pal.Info, TextAnchor.UpperLeft, FontStyle.Bold);
            foreach (var s in p.Insights()) P(ins.transform, "• " + s, 19);
            var fl = UIKit.Rect(left, "Flex"); UIKit.LE(fl, flexH: 1);
            var row = UIKit.Row(left, 12, 56);
            UIKit.Button(row, "‹ Về menu", ShowMenu);
            UIKit.Button(row, resetConfirm ? "Nhấn lần nữa để xoá" : "Xoá hồ sơ", () =>
            {
                if (resetConfirm) { p.Reset(); resetConfirm = false; } else resetConfirm = true;
                ShowProfile();
            }, resetConfirm ? Pal.Danger : Pal.Btn);

            var rec = AdaptiveCoach.Recommend(p, game.LastReport);
            var ac = UIKit.Card(right, "Adaptive", 24, 10, new Color(0.2f, 0.09f, 0.04f, 1f));
            UIKit.Label(ac.transform, "ADAPTIVE AI", 16, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            M(ac.transform, "PLAY → DECISION → FAIL/SUCCESS → AI ANALYSIS → IDENTIFY WEAKNESS → GENERATE TARGETED TRAINING → PLAY AGAIN", 14);
            P(ac.transform, rec.reason, 20);
            UIKit.Button(ac.transform, "Bắt đầu nhiệm vụ AI đề xuất", () => game.StartMission(rec.request), Pal.Fire);
            if (rec.suggestedDrill != DrillType.None) UIKit.Button(ac.transform, "Luyện " + Txt.Name(rec.suggestedDrill), () => game.StartDrill(rec.suggestedDrill));

            UIKit.Label(right, "LỊCH SỬ", 16, Pal.Fire, TextAnchor.UpperLeft, FontStyle.Bold);
            UIKit.Scroll(right, out var hist, 6);
            if (p.data.history.Count == 0) M(hist, "Chưa có nhiệm vụ nào.", 17);
            foreach (var h in p.data.history)
            {
                var c = UIKit.Card(hist, "H", 12, 2, Pal.Card2);
                P(c.transform, $"<b>{h.title}</b>  <size=15><color=#{Pal.Hex(Pal.Muted)}>{h.date}</color></size>", 18);
                P(c.transform, $"<color=#{Pal.Hex(Pal.Score(h.total))}>{h.total:0} điểm</color> · {h.outcome} · {h.verdict} · {Txt.Secs(h.responseTime)}{(string.IsNullOrEmpty(h.weakest) ? "" : " · yếu nhất: " + h.weakest)}", 15, Pal.Muted);
            }
        }

        // ================================================================ UPDATE: replay + XR pointer
        void Update()
        {
            if (replayActive && rp != null && rpPlaying)
            {
                rpT += Time.deltaTime * rpSpeed;
                if (rpT >= MaxT()) { rpT = MaxT(); rpPlaying = false; }
                RefreshReplay();
            }
            if (xr) XRPointer();
        }

        void XRPointer()
        {
            if (!menuCanvas.gameObject.activeSelf) { if (xrCursor) xrCursor.gameObject.SetActive(false); return; }
            var player = game.Player;
            var ray = player.AimRay;
            var ct = menuCanvas.transform;
            var plane = new Plane(-ct.forward, ct.position);
            if (!plane.Raycast(ray, out float d) || d > 6f) { xrCursor.gameObject.SetActive(false); return; }
            var hit = ray.GetPoint(d);
            if (player is XRPlayer xp) xp.LaserLength = d;
            xrCursor.gameObject.SetActive(true);
            xrCursor.transform.position = hit - ct.forward * 0.002f;
            xrCursor.transform.SetAsLastSibling();

            Selectable found = null;
            foreach (var sel in menuCanvas.GetComponentsInChildren<Selectable>())
            {
                if (!sel.IsInteractable()) continue;
                var rt = (RectTransform)sel.transform;
                if (!rt.rect.Contains((Vector2)rt.InverseTransformPoint(hit))) continue;
                var mask = sel.GetComponentInParent<RectMask2D>();
                if (mask != null && !mask.rectTransform.rect.Contains((Vector2)mask.rectTransform.InverseTransformPoint(hit))) continue;
                found = sel;
            }
            if (found != hovered)
            {
                var ped = new PointerEventData(EventSystem.current);
                if (hovered != null) hovered.OnPointerExit(ped);
                if (found != null) found.OnPointerEnter(ped);
                hovered = found;
            }
            if (found is Slider sl && player.InteractHeld)
            {
                var rt = (RectTransform)sl.transform;
                var lp = rt.InverseTransformPoint(hit);
                sl.value = Mathf.InverseLerp(rt.rect.xMin, rt.rect.xMax, lp.x);
            }
            else if (found is Button b && player.InteractDown)
            {
                game.Hazards.PlayBeep();
                b.onClick.Invoke();
                return;
            }
            var stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.RTouch);
            if (Mathf.Abs(stick.y) > 0.2f)
            {
                ScrollRect target = found != null ? found.GetComponentInParent<ScrollRect>() : null;
                if (target == null)
                    foreach (var s in menuCanvas.GetComponentsInChildren<ScrollRect>())
                    {
                        var rt = (RectTransform)s.transform;
                        if (rt.rect.Contains((Vector2)rt.InverseTransformPoint(hit))) target = s;
                    }
                if (target != null)
                    target.verticalNormalizedPosition = Mathf.Clamp01(target.verticalNormalizedPosition + stick.y * Time.deltaTime * 0.8f);
            }
        }
    }
}
