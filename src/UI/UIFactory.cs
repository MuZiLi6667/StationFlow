using System;
using UnityEngine;
using UnityEngine.UI;

namespace StationFlow.UI
{
    /// <summary>
    /// uGUI 构造辅助：全部用代码构建（不克隆游戏 prefab，避免与官方 UI 版本耦合）。
    /// 独立 Overlay Canvas，ConstantPixelSize（1 单位 = 1 像素），坐标系与屏幕像素一致。
    /// </summary>
    public static class UIFactory
    {
        // ---- 配色（贴近 DSP 深色调） ----
        public static readonly Color ColBg = new Color(0.04f, 0.07f, 0.10f, 0.96f);
        public static readonly Color ColPanel = new Color(0.07f, 0.12f, 0.16f, 0.95f);
        public static readonly Color ColRow = new Color(0.10f, 0.15f, 0.20f, 0.55f);
        public static readonly Color ColRowAlt = new Color(0.08f, 0.12f, 0.16f, 0.55f);
        public static readonly Color ColRowSelected = new Color(0.16f, 0.45f, 0.55f, 0.85f);
        public static readonly Color ColBtn = new Color(0.14f, 0.25f, 0.32f, 0.9f);
        public static readonly Color ColBtnHighlight = new Color(0.25f, 0.55f, 0.65f, 0.95f);
        public static readonly Color ColText = new Color(0.85f, 0.90f, 0.92f, 1f);
        public static readonly Color ColTextDim = new Color(0.55f, 0.62f, 0.66f, 1f);
        public static readonly Color ColAccent = new Color(0.45f, 0.85f, 0.90f, 1f);
        public static readonly Color ColWarn = new Color(1f, 0.65f, 0.3f, 1f);

        private static Font uiFont;

        public static Font UiFont
        {
            get
            {
                if (uiFont == null)
                {
                    try { uiFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 14); }
                    catch { }
                    if (uiFont == null)
                    {
                        try { uiFont = Font.CreateDynamicFontFromOSFont("Arial", 14); }
                        catch { }
                    }
                    if (uiFont == null)
                    {
                        try { uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
                        catch { }
                    }
                }
                return uiFont;
            }
        }

        // ================= 节点构造 =================

        /// <summary>创建独立 Overlay Canvas 根（1 单位=1 像素）。</summary>
        public static GameObject CreateCanvasRoot(string name, int sortingOrder)
        {
            var go = new GameObject(name);
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            go.AddComponent<GraphicRaycaster>();
            return go;
        }

        /// <summary>空 RectTransform 节点。</summary>
        public static RectTransform CreateNode(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        public static Image CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        public static Text CreateText(Transform parent, string name, string content, int fontSize, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = UiFont;
            text.fontSize = fontSize;
            text.text = content ?? "";
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>构造按钮（背景 Image + 文本 + hover 颜色）。</summary>
        public static Button CreateButton(Transform parent, string name, string label, int fontSize, Action onClick)
        {
            var img = CreateImage(parent, name, ColBtn);
            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = 0.05f;
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() => onClick());

            var text = CreateText(img.transform, "Label", label, fontSize, TextAnchor.MiddleCenter, ColText);
            Stretch(text.rectTransform);
            return btn;
        }

        /// <summary>构造输入框（背景 + 文本 + placeholder）。</summary>
        public static InputField CreateInputField(Transform parent, string name, string placeholder, int fontSize)
        {
            var img = CreateImage(parent, name, new Color(0.02f, 0.04f, 0.06f, 0.95f));
            var input = img.gameObject.AddComponent<InputField>();
            input.targetGraphic = img;

            var text = CreateText(img.transform, "Text", "", fontSize, TextAnchor.MiddleLeft, ColText);
            text.supportRichText = false;
            SetRect(text.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(-12, -4));
            input.textComponent = text;

            var ph = CreateText(img.transform, "Placeholder", placeholder, fontSize, TextAnchor.MiddleLeft, ColTextDim);
            ph.fontStyle = FontStyle.Italic;
            SetRect(ph.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(-12, -4));
            input.placeholder = ph;

            return input;
        }

        /// <summary>构造垂直滚动容器（返回 viewport 与 content）。</summary>
        public static ScrollRect CreateScrollView(Transform parent, string name, out RectTransform viewport, out RectTransform content)
        {
            var rootRt = CreateNode(parent, name);
            var scroll = rootRt.gameObject.AddComponent<ScrollRect>();

            var vpImg = CreateImage(rootRt, "Viewport", new Color(0, 0, 0, 0.25f));
            viewport = vpImg.rectTransform;
            Stretch(viewport);
            vpImg.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;

            content = CreateNode(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 0);
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return scroll;
        }

        // ================= Rect 辅助 =================

        /// <summary>按左上角锚点摆放（IMGUI 风格坐标：x 向右、y 向下）。</summary>
        public static void PlaceTopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void Stretch(RectTransform rt, float padLeft = 0, float padTop = 0, float padRight = 0, float padBottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(padLeft, padBottom);
            rt.offsetMax = new Vector2(-padRight, -padTop);
        }

        public static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
        }
    }
}
