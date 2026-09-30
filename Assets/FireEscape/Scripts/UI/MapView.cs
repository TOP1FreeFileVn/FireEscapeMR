using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FireEscape
{
    /// <summary>Sơ đồ 2D của không gian: dùng cho Incident Replay và What-if (chạy được cả trên PC lẫn trong headset).</summary>
    public class MapView
    {
        public readonly RectTransform root;
        readonly FacilityLayout L;
        readonly float scale;
        readonly Vector2 origin;
        readonly Bounds wb;
        readonly Image[] zoneFill, smokeFill;
        readonly Image[] portalImg;
        readonly Color[] zoneBase, portalBase;
        readonly List<Image> firePool = new List<Image>(), npcPool = new List<Image>();
        readonly Dictionary<string, List<Image>> lines = new Dictionary<string, List<Image>>();
        readonly Dictionary<string, Image> markers = new Dictionary<string, Image>();
        RectTransform lineLayer, dotLayer;

        public MapView(Transform parent, FacilityLayout layout, Vector2 size)
        {
            L = layout;
            var bg = UIKit.Panel(parent, "Map", new Color(0.02f, 0.025f, 0.03f, 0.9f));
            root = bg.rectTransform;
            UIKit.LE(bg, minH: size.y, prefH: size.y, prefW: size.x, minW: size.x);
            wb = L.WorldBounds();
            float pad = 26f;
            scale = Mathf.Min((size.x - 2 * pad) / Mathf.Max(1f, wb.size.x), (size.y - 2 * pad) / Mathf.Max(1f, wb.size.z));
            origin = new Vector2((size.x - wb.size.x * scale) / 2f, (size.y - wb.size.z * scale) / 2f);

            int nz = L.zones.Count;
            zoneFill = new Image[nz]; smokeFill = new Image[nz]; zoneBase = new Color[nz];
            for (int i = 0; i < nz; i++)
            {
                var z = L.zones[i];
                var c = z.isExit ? new Color(0.08f, 0.3f, 0.18f) : new Color(0.16f, 0.17f, 0.2f);
                zoneBase[i] = c;
                var img = UIKit.Panel(root, "Zone_" + z.id, c);
                var min = P(new Vector3(z.area.xMin, 0, z.area.yMin));
                var max = P(new Vector3(z.area.xMax, 0, z.area.yMax));
                Anchor(img.rectTransform, min, max - min);
                var ol = img.gameObject.AddComponent<Outline>();
                ol.effectColor = new Color(0.55f, 0.6f, 0.66f, 0.8f);
                ol.effectDistance = new Vector2(1.2f, -1.2f);
                zoneFill[i] = img;
                var sm = UIKit.Panel(img.transform, "Smoke", new Color(0.55f, 0.55f, 0.57f, 0f));
                UIKit.Stretch(sm.rectTransform);
                smokeFill[i] = sm;
                var t = UIKit.Label(img.transform, z.name, 13, new Color(1f, 1f, 1f, 0.55f), TextAnchor.UpperCenter);
                UIKit.Stretch(t.rectTransform, 2, 2, 2, 2);
            }

            int np = L.portals.Count;
            portalImg = new Image[np]; portalBase = new Color[np];
            for (int i = 0; i < np; i++)
            {
                var p = L.portals[i];
                if (!p.walkable) continue;
                bool exit = L.GetZone(p.a).isExit || L.GetZone(p.b).isExit;
                var c = exit ? Pal.Safe : p.hasDoor ? new Color(0.75f, 0.52f, 0.3f) : new Color(0.16f, 0.17f, 0.2f);
                portalBase[i] = c;
                float w = Mathf.Max(6f, p.width * scale);
                var img = UIKit.Panel(root, "Portal_" + p.id, c);
                var za = L.GetZone(p.a);
                bool alongX = Mathf.Abs(p.pos.z - za.area.yMin) < 0.06f || Mathf.Abs(p.pos.z - za.area.yMax) < 0.06f;
                Anchor(img.rectTransform, P(p.pos) - (alongX ? new Vector2(w / 2f, 3f) : new Vector2(3f, w / 2f)), alongX ? new Vector2(w, 6f) : new Vector2(6f, w));
                portalImg[i] = img;
            }

            foreach (var f in L.fixtures)
            {
                Color c; float s = 7f;
                switch (f.kind)
                {
                    case FixtureKind.AlarmStation: c = Pal.Danger; break;
                    case FixtureKind.Extinguisher: c = new Color(1f, 0.5f, 0.5f); break;
                    case FixtureKind.Elevator: c = new Color(0.6f, 0.6f, 0.65f); s = 14f; break;
                    case FixtureKind.Window: c = new Color(0.5f, 0.75f, 1f); s = 10f; break;
                    case FixtureKind.Desk: c = new Color(0.35f, 0.3f, 0.25f); s = 9f; break;
                    default: continue;
                }
                var img = UIKit.Panel(root, "Fx_" + f.id, c);
                Anchor(img.rectTransform, P(f.pos) - Vector2.one * s / 2f, Vector2.one * s);
            }

            lineLayer = UIKit.Rect(root, "Lines");
            UIKit.Stretch(lineLayer);
            dotLayer = UIKit.Rect(root, "Dots");
            UIKit.Stretch(dotLayer);
        }

        public Vector2 P(Vector3 w) => origin + new Vector2(w.x - wb.min.x, w.z - wb.min.z) * scale;

        static void Anchor(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        Image Dot(List<Image> pool, int i, Color c, float size)
        {
            while (pool.Count <= i)
            {
                var img = UIKit.Panel(dotLayer, "Dot", c);
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                img.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                pool.Add(img);
            }
            var d = pool[i];
            d.gameObject.SetActive(true);
            d.color = c;
            d.rectTransform.sizeDelta = Vector2.one * size;
            return d;
        }

        public void ShowSnapshot(SimSnapshot s)
        {
            if (s == null) return;
            for (int i = 0; i < zoneFill.Length; i++)
            {
                var c = zoneBase[i];
                if (s.fire[i] > 0.02f) c = Color.Lerp(c, new Color(0.45f, 0.1f, 0.04f), Mathf.Clamp01(s.fire[i] + 0.2f));
                if (s.dark[i] || (!s.power && !s.emergency)) c = Color.Lerp(c, Color.black, 0.55f);
                zoneFill[i].color = c;
                smokeFill[i].color = new Color(0.62f, 0.62f, 0.64f, Mathf.Clamp01(s.smoke[i]) * 0.75f);
            }
            for (int i = 0; i < portalImg.Length; i++)
            {
                if (portalImg[i] == null) continue;
                var c = portalBase[i];
                if (s.blocked[i]) c = Pal.Danger;
                else if (L.portals[i].hasDoor && !s.doorOpen[i]) c = Color.Lerp(c, Color.black, 0.5f);
                portalImg[i].color = c;
            }
            int k = 0;
            foreach (var f in s.fires)
            {
                var d = Dot(firePool, k++, new Color(1f, 0.5f + 0.3f * (1 - f.z), 0.1f, 0.95f), 8f + 26f * f.z);
                d.rectTransform.anchoredPosition = P(new Vector3(f.x, 0, f.y));
            }
            for (; k < firePool.Count; k++) firePool[k].gameObject.SetActive(false);
            k = 0;
            foreach (var n in s.npcs)
            {
                var mode = (NpcMode)(int)n.z;
                if (mode == NpcMode.Evacuated) continue;
                var d = Dot(npcPool, k++, mode == NpcMode.Incapacitated ? Color.gray : new Color(0.85f, 0.55f, 1f), 12f);
                d.rectTransform.anchoredPosition = P(new Vector3(n.x, 0, n.y));
            }
            for (; k < npcPool.Count; k++) npcPool[k].gameObject.SetActive(false);
        }

        public void DrawPath(string layer, List<PathSample> path, float upTo, Color c, float width = 3f)
        {
            if (!lines.TryGetValue(layer, out var pool)) lines[layer] = pool = new List<Image>();
            int k = 0;
            for (int i = 2; i < path.Count; i += 2)
            {
                if (path[i].t > upTo) break;
                var a = P(path[i - 2].pos); var b = P(path[i].pos);
                var d = b - a;
                if (d.sqrMagnitude < 0.5f) continue;
                while (pool.Count <= k)
                {
                    var img = UIKit.Panel(lineLayer, "Seg_" + layer, c);
                    img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                    img.rectTransform.pivot = new Vector2(0f, 0.5f);
                    pool.Add(img);
                }
                var s = pool[k++];
                s.gameObject.SetActive(true);
                s.color = path[i].crouch ? new Color(c.r, c.g, c.b, c.a * 0.55f) : c;
                s.rectTransform.anchoredPosition = a;
                s.rectTransform.sizeDelta = new Vector2(d.magnitude, width);
                s.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
            for (; k < pool.Count; k++) pool[k].gameObject.SetActive(false);
        }

        public void Marker(string key, Vector3? pos, Color c, float size = 16f)
        {
            if (!markers.TryGetValue(key, out var m))
            {
                m = UIKit.Panel(dotLayer, "Marker_" + key, c);
                m.rectTransform.anchorMin = m.rectTransform.anchorMax = Vector2.zero;
                m.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                var ol = m.gameObject.AddComponent<Outline>();
                ol.effectColor = Color.white; ol.effectDistance = new Vector2(1.5f, -1.5f);
                markers[key] = m;
            }
            m.gameObject.SetActive(pos.HasValue);
            if (!pos.HasValue) return;
            m.color = c;
            m.rectTransform.sizeDelta = Vector2.one * size;
            m.rectTransform.anchoredPosition = P(pos.Value);
            m.rectTransform.SetAsLastSibling();
        }
    }
}
