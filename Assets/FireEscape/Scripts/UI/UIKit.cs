using System;
using UnityEngine;
using UnityEngine.UI;

namespace FireEscape
{
    public static class Pal
    {
        public static readonly Color Bg = new Color(0.055f, 0.06f, 0.075f, 0.94f);
        public static readonly Color Card = new Color(0.11f, 0.12f, 0.145f, 0.97f);
        public static readonly Color Card2 = new Color(0.15f, 0.16f, 0.19f, 1f);
        public static readonly Color Line = new Color(1f, 1f, 1f, 0.08f);
        public static readonly Color Fire = new Color(1f, 0.45f, 0.16f);
        public static readonly Color FireDim = new Color(0.55f, 0.22f, 0.08f);
        public static readonly Color Safe = new Color(0.2f, 0.84f, 0.5f);
        public static readonly Color Warn = new Color(1f, 0.8f, 0.25f);
        public static readonly Color Danger = new Color(0.95f, 0.28f, 0.25f);
        public static readonly Color Info = new Color(0.35f, 0.75f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.94f, 0.96f);
        public static readonly Color Muted = new Color(0.6f, 0.64f, 0.7f);
        public static readonly Color Btn = new Color(0.18f, 0.19f, 0.23f, 1f);
        public static readonly Color BtnSel = new Color(0.45f, 0.2f, 0.08f, 1f);

        public static Color Score(float v) => v >= 75f ? Safe : v >= 50f ? Warn : Danger;
        public static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
    }

    /// <summary>Bộ dựng UGUI bằng code, dùng layout group để không phải đặt toạ độ thủ công.</summary>
    public static class UIKit
    {
        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rt, float l = 0, float t = 0, float r = 0, float b = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b); rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color c)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            return img;
        }

        public static Text Label(Transform parent, string text, int size = 22, Color? color = null, TextAnchor anchor = TextAnchor.UpperLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = MatLib.Font;
            t.text = text;
            t.fontSize = size;
            t.color = color ?? Pal.Text;
            t.alignment = anchor;
            t.fontStyle = style;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.1f;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, string text, Action onClick, Color? bg = null, int size = 22, float height = 56f)
        {
            var img = Panel(parent, "Button", bg ?? Pal.Btn);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = Color.white;
            colors.colorMultiplier = 1.4f;
            b.colors = colors;
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            var t = Label(img.transform, text, size, Pal.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(t.rectTransform, 12, 4, 12, 4);
            LE(img, prefH: height, minH: height);
            return b;
        }

        public static void SetButton(Button b, string text = null, Color? bg = null)
        {
            if (text != null) b.GetComponentInChildren<Text>().text = text;
            if (bg.HasValue) ((Image)b.targetGraphic).color = bg.Value;
        }

        public static LayoutElement LE(Component c, float minH = -1, float prefH = -1, float flexW = -1, float flexH = -1, float prefW = -1, float minW = -1)
        {
            var le = c.GetComponent<LayoutElement>();
            if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
            le.minHeight = minH; le.preferredHeight = prefH; le.flexibleWidth = flexW; le.flexibleHeight = flexH;
            le.preferredWidth = prefW; le.minWidth = minW;
            return le;
        }

        public static VerticalLayoutGroup VStack(Component c, float spacing = 10, int pad = 0, bool expandW = true)
        {
            var v = c.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing;
            v.padding = new RectOffset(pad, pad, pad, pad);
            v.childControlWidth = true; v.childControlHeight = true;
            v.childForceExpandWidth = expandW; v.childForceExpandHeight = false;
            return v;
        }

        public static HorizontalLayoutGroup HStack(Component c, float spacing = 10, int pad = 0, bool expandW = true)
        {
            var h = c.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.padding = new RectOffset(pad, pad, pad, pad);
            h.childControlWidth = true; h.childControlHeight = true;
            h.childForceExpandWidth = expandW; h.childForceExpandHeight = false;
            return h;
        }

        public static RectTransform Row(Transform parent, float spacing = 10, float height = -1)
        {
            var rt = Rect(parent, "Row");
            HStack(rt, spacing);
            if (height > 0) LE(rt, prefH: height, minH: height);
            return rt;
        }

        public static Image Card(Transform parent, string name = "Card", int pad = 18, float spacing = 10, Color? c = null)
        {
            var img = Panel(parent, name, c ?? Pal.Card);
            VStack(img, spacing, pad);
            return img;
        }

        /// <summary>Thanh tiến độ: trả về Image fill (dùng fillAmount).</summary>
        public static Image Bar(Transform parent, Color fillColor, float height = 14f)
        {
            var bg = Panel(parent, "Bar", new Color(1f, 1f, 1f, 0.08f));
            LE(bg, prefH: height, minH: height, flexW: 1);
            var fill = Panel(bg.transform, "Fill", fillColor);
            Stretch(fill.rectTransform);
            fill.sprite = WhiteSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillAmount = 0f;
            return fill;
        }

        static Sprite white;
        public static Sprite WhiteSprite
        {
            get
            {
                if (white != null) return white;
                var t = Texture2D.whiteTexture;
                white = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f));
                return white;
            }
        }

        public static ScrollRect Scroll(Transform parent, out RectTransform content, float spacing = 10, int pad = 0)
        {
            var root = Rect(parent, "Scroll");
            var sr = root.gameObject.AddComponent<ScrollRect>();
            var vp = Rect(root, "Viewport");
            Stretch(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            var vpImg = vp.gameObject.AddComponent<Image>();
            vpImg.color = new Color(0, 0, 0, 0.001f);
            content = Rect(vp, "Content");
            content.anchorMin = new Vector2(0, 1); content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.offsetMin = content.offsetMax = Vector2.zero;
            VStack(content, spacing, pad);
            var fit = content.gameObject.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.viewport = vp;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            LE(root, flexH: 1, flexW: 1);
            return sr;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
        }

        public static Text Chip(Transform parent, string text, Color c, int size = 18)
        {
            var img = Panel(parent, "Chip", new Color(c.r, c.g, c.b, 0.18f));
            var h = HStack(img, 0, 0);
            h.padding = new RectOffset(12, 12, 4, 4);
            var t = Label(img.transform, text, size, c, TextAnchor.MiddleCenter, FontStyle.Bold);
            var fit = img.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            return t;
        }
    }
}
