using System;
using System.Collections.Generic;
using StationFlow.Data;
using StationFlow.PluginCore;
using StationFlow.VanillaBridge;
using UnityEngine;
using UnityEngine.UI;

namespace StationFlow.UI
{
    /// <summary>
    /// 虚拟滚动物流塔表格：恒定行视图池，滚动时只复用可见行（几百行数据保持 60fps）。
    /// 列：名字 / 供(图标) / 需(图标) / 星球 / 行为 / 组 / P2P。
    /// 选择模型：单击=单选、Ctrl+单击=切换多选、Shift+单击=范围选择。
    /// </summary>
    public class StationTable
    {
        private const float RowH = 24f;
        private const float HeaderH = 26f;
        private const int MaxIcons = 5;          // 每侧最多显示图标数
        private const float IconSize = 18f;
        private const float IconStep = 20f;

        // 列布局（x, width）——总宽约 782，适配 810 宽表格
        private const float XName = 4f,   WName = 214f;
        private const float XSupply = 222f, WSupply = 130f;
        private const float XDemand = 356f, WDemand = 130f;
        private const float XPlanet = 490f, WPlanet = 116f;
        private const float XBehavior = 610f, WBehavior = 76f;
        private const float XGroup = 690f, WGroup = 44f;
        private const float XP2P = 738f, WP2P = 44f;

        /// <summary>供列配色（绿）。</summary>
        public static readonly Color ColSupply = new Color(0.45f, 0.9f, 0.55f, 1f);
        /// <summary>需列配色（橙）。</summary>
        public static readonly Color ColDemand = new Color(1f, 0.72f, 0.35f, 1f);

        private static readonly (string title, float x, float w, TextAnchor anchor, Color color)[] HeaderCols =
        {
            ("名字", XName, WName, TextAnchor.MiddleLeft, UIFactory.ColAccent),
            ("供", XSupply, WSupply, TextAnchor.MiddleLeft, ColSupply),
            ("需", XDemand, WDemand, TextAnchor.MiddleLeft, ColDemand),
            ("星球", XPlanet, WPlanet, TextAnchor.MiddleLeft, UIFactory.ColAccent),
            ("行为", XBehavior, WBehavior, TextAnchor.MiddleCenter, UIFactory.ColAccent),
            ("组", XGroup, WGroup, TextAnchor.MiddleCenter, UIFactory.ColAccent),
            ("P2P", XP2P, WP2P, TextAnchor.MiddleCenter, UIFactory.ColAccent),
        };

        public GameObject Root { get; private set; }
        public Action SelectionChanged;

        private ScrollRect scrollRect;
        private RectTransform content;
        private float viewportHeight;
        private readonly List<RowView> pool = new List<RowView>();
        private List<StationRow> rows = new List<StationRow>();
        private readonly HashSet<int> selected = new HashSet<int>();
        private int lastClickIndex = -1;

        public List<StationRow> Rows { get { return rows; } }

        public List<StationRow> SelectedRows
        {
            get
            {
                var result = new List<StationRow>();
                foreach (var r in rows)
                {
                    if (selected.Contains(r.Gid)) result.Add(r);
                }
                return result;
            }
        }

        public int SelectedCount { get { return selected.Count; } }

        // ================= 初始化 =================

        public void Init(Transform parent, float width, float height)
        {
            viewportHeight = height - HeaderH;

            Root = UIFactory.CreateNode(parent, "StationTable").gameObject;
            var rootRt = (RectTransform)Root.transform;
            rootRt.sizeDelta = new Vector2(width, height);

            // 表头
            var header = UIFactory.CreateImage(rootRt, "Header", UIFactory.ColPanel);
            UIFactory.PlaceTopLeft(header.rectTransform, 0, 0, width, HeaderH);
            foreach (var col in HeaderCols)
            {
                var t = UIFactory.CreateText(header.transform, col.title, col.title, 13, col.anchor, col.color);
                UIFactory.PlaceTopLeft(t.rectTransform, col.x, 4, col.w - 6, HeaderH - 6);
            }

            // 滚动区（注册进游戏 UIScrollZone：悬停时滚轮不再缩放游戏镜头）
            RectTransform viewport;
            scrollRect = UIFactory.CreateScrollView(rootRt, "Scroll", out viewport, out content);
            UIFactory.PlaceTopLeft((RectTransform)scrollRect.transform, 0, HeaderH, width, viewportHeight);
            viewport.gameObject.AddComponent<ModScrollZone>();
            scrollRect.onValueChanged.AddListener(_ => UpdateVisibleRows());
        }

        // ================= 数据 =================

        public void SetData(List<StationRow> data, bool keepSelection)
        {
            rows = data ?? new List<StationRow>();
            if (!keepSelection)
            {
                selected.Clear();
                lastClickIndex = -1;
            }
            else
            {
                // 移除已不存在的 gid（先建集合再过滤，O(N+S) 而非 O(N×S)）
                var alive = new HashSet<int>();
                foreach (var r in rows) alive.Add(r.Gid);
                selected.RemoveWhere(gid => !alive.Contains(gid));
            }

            content.sizeDelta = new Vector2(0, rows.Count * RowH);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Min(content.anchoredPosition.y,
                Mathf.Max(0, rows.Count * RowH - viewportHeight)));
            UpdateVisibleRows();
            SelectionChanged?.Invoke();
        }

        public void SelectAll()
        {
            selected.Clear();
            foreach (var r in rows) selected.Add(r.Gid);
            RefreshVisuals();
            SelectionChanged?.Invoke();
        }

        public void SelectNone()
        {
            selected.Clear();
            lastClickIndex = -1;
            RefreshVisuals();
            SelectionChanged?.Invoke();
        }

        public void InvertSelection()
        {
            foreach (var r in rows)
            {
                if (!selected.Add(r.Gid)) selected.Remove(r.Gid);
            }
            RefreshVisuals();
            SelectionChanged?.Invoke();
        }

        // ================= 滚动与行池 =================

        private void UpdateVisibleRows()
        {
            if (content == null) return;
            float scrollY = content.anchoredPosition.y;
            int first = Mathf.Max(0, Mathf.FloorToInt(scrollY / RowH));
            int visible = Mathf.CeilToInt(viewportHeight / RowH) + 1;
            int need = Mathf.Min(visible + 1, Mathf.Max(0, rows.Count - first));
            EnsurePool(visible + 1);

            for (int i = 0; i < pool.Count; i++)
            {
                var rv = pool[i];
                int dataIndex = first + i;
                if (i >= need || dataIndex >= rows.Count)
                {
                    if (rv.Go.activeSelf) rv.Go.SetActive(false);
                    continue;
                }
                BindRow(rv, rows[dataIndex], dataIndex);
                rv.Rt.anchoredPosition = new Vector2(0, -dataIndex * RowH);
                if (!rv.Go.activeSelf) rv.Go.SetActive(true);
            }
        }

        private void EnsurePool(int count)
        {
            while (pool.Count < count) pool.Add(CreateRowView());
        }

        private RowView CreateRowView()
        {
            var rv = new RowView();
            var img = UIFactory.CreateImage(content, "Row", UIFactory.ColRow);
            rv.Go = img.gameObject;
            rv.Rt = img.rectTransform;
            rv.Bg = img;
            rv.Rt.anchorMin = new Vector2(0, 1);
            rv.Rt.anchorMax = new Vector2(1, 1);
            rv.Rt.pivot = new Vector2(0.5f, 1f);
            rv.Rt.sizeDelta = new Vector2(0, RowH - 1);

            var btn = rv.Go.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.5f, 1.5f, 1.5f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.fadeDuration = 0.0f;
            btn.colors = colors;
            btn.onClick.AddListener(() => OnRowClicked(rv.BoundIndex));

            rv.NameText = UIFactory.CreateText(rv.Rt, "Name", "", 13, TextAnchor.MiddleLeft, UIFactory.ColText);
            UIFactory.PlaceTopLeft(rv.NameText.rectTransform, XName, 3, WName - 8, RowH - 6);
            rv.PlanetText = UIFactory.CreateText(rv.Rt, "Planet", "", 13, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(rv.PlanetText.rectTransform, XPlanet, 3, WPlanet - 8, RowH - 6);
            rv.BehaviorText = UIFactory.CreateText(rv.Rt, "Behavior", "", 13, TextAnchor.MiddleCenter, UIFactory.ColText);
            UIFactory.PlaceTopLeft(rv.BehaviorText.rectTransform, XBehavior, 3, WBehavior - 8, RowH - 6);
            rv.GroupText = UIFactory.CreateText(rv.Rt, "Group", "", 13, TextAnchor.MiddleCenter, UIFactory.ColText);
            UIFactory.PlaceTopLeft(rv.GroupText.rectTransform, XGroup, 3, WGroup - 8, RowH - 6);
            rv.P2PText = UIFactory.CreateText(rv.Rt, "P2P", "", 13, TextAnchor.MiddleCenter, UIFactory.ColText);
            UIFactory.PlaceTopLeft(rv.P2PText.rectTransform, XP2P, 3, WP2P - 8, RowH - 6);

            rv.SupplyIcons = CreateIconSet(rv.Rt, XSupply, "S", ColSupply);
            rv.DemandIcons = CreateIconSet(rv.Rt, XDemand, "D", ColDemand);
            return rv;
        }

        private IconSet CreateIconSet(RectTransform parent, float x, string prefix, Color col)
        {
            var set = new IconSet();
            for (int i = 0; i < MaxIcons; i++)
            {
                var img = UIFactory.CreateImage(parent, prefix + i, Color.white);
                UIFactory.PlaceTopLeft(img.rectTransform, x + i * IconStep, 3, IconSize, IconSize);
                var hover = img.gameObject.AddComponent<IconHover>();
                img.gameObject.SetActive(false);
                set.Icons.Add(img);
                set.Hovers.Add(hover);
            }
            set.Overflow = UIFactory.CreateText(parent, prefix + "N", "", 12, TextAnchor.MiddleLeft, col);
            UIFactory.PlaceTopLeft(set.Overflow.rectTransform, x + MaxIcons * IconStep, 3, 30, RowH - 6);
            return set;
        }

        private void BindRow(RowView rv, StationRow row, int index)
        {
            rv.BoundIndex = index;
            rv.BoundGid = row.Gid;
            rv.NameText.text = "#" + row.Gid + "  " + (row.HasName ? row.Name : "＜未命名＞");
            rv.NameText.color = row.HasName ? UIFactory.ColText : UIFactory.ColTextDim;
            rv.PlanetText.text = row.PlanetName;
            rv.BehaviorText.text = row.BehaviorName;
            rv.BehaviorText.color = row.Behavior == 1 ? UIFactory.ColTextDim :
                                    row.Behavior >= 3 ? UIFactory.ColWarn :
                                    UIFactory.ColText;
            rv.GroupText.text = FormatGroups(row.GroupMask);
            rv.P2PText.text = row.PairP2P > 0 ? row.PairP2P.ToString() : "-";
            rv.P2PText.color = row.PairP2P > 0 ? UIFactory.ColAccent : UIFactory.ColTextDim;

            BindIconSet(rv.SupplyIcons, row, supply: true);
            BindIconSet(rv.DemandIcons, row, supply: false);

            rv.SetSelected(selected.Contains(row.Gid), index % 2 == 1);
        }

        /// <summary>物品图标缺失只提示一次（避免刷屏）。</summary>
        private static bool warnedIconMissing;

        private void BindIconSet(IconSet set, StationRow row, bool supply)
        {
            int wantLogic = supply ? 1 : 2; // ELogisticStorage.Supply=1 / Demand=2（严格星际逻辑）
            int shown = 0;
            int total = 0;
            if (row.Slots != null)
            {
                foreach (var slot in row.Slots)
                {
                    if (slot.RemoteLogic != wantLogic) continue;
                    total++;
                    if (shown >= MaxIcons) continue;
                    var img = set.Icons[shown];
                    var sprite = StationOps.ItemIcon(slot.ItemId);
                    if (sprite == null)
                    {
                        img.sprite = null;
                        img.color = new Color(0.55f, 0.55f, 0.55f, 0.85f); // 灰块占位
                        if (!warnedIconMissing)
                        {
                            warnedIconMissing = true;
                            StationFlowPlugin.LogWarn($"物品图标缺失：itemId={slot.ItemId}（物品原型可能未加载），暂时显示灰色占位");
                        }
                    }
                    else
                    {
                        img.sprite = sprite;
                        img.color = Color.white;
                    }
                    img.gameObject.SetActive(true);
                    set.Hovers[shown].Tip = $"{slot.ItemName}（{slot.Count}/{slot.Max}）· {slot.RemoteLogicText} · 塔 #{row.Gid}";
                    shown++;
                }
            }
            for (int i = shown; i < MaxIcons; i++)
            {
                if (set.Icons[i].gameObject.activeSelf) set.Icons[i].gameObject.SetActive(false);
            }
            if (total == 0) set.Overflow.text = "-";
            else if (total > MaxIcons) set.Overflow.text = "+" + (total - MaxIcons);
            else set.Overflow.text = "";
        }

        private static string FormatGroups(long mask)
        {
            if (mask == 0) return "-";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 62; i++)
            {
                if ((mask & (1L << i)) != 0)
                {
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(i + 1); // 显示为 1-based 组号
                }
            }
            return sb.ToString();
        }

        // ================= 选择 =================

        private void OnRowClicked(int index)
        {
            if (index < 0 || index >= rows.Count) return;
            var row = rows[index];
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (shift && lastClickIndex >= 0 && lastClickIndex < rows.Count)
            {
                int a = Mathf.Min(lastClickIndex, index);
                int b = Mathf.Max(lastClickIndex, index);
                for (int i = a; i <= b; i++) selected.Add(rows[i].Gid);
            }
            else if (ctrl)
            {
                if (!selected.Add(row.Gid)) selected.Remove(row.Gid);
                lastClickIndex = index;
            }
            else
            {
                selected.Clear();
                selected.Add(row.Gid);
                lastClickIndex = index;
            }

            RefreshVisuals();
            SelectionChanged?.Invoke();
        }

        public void RefreshVisuals()
        {
            foreach (var rv in pool)
            {
                if (!rv.Go.activeSelf || rv.BoundIndex < 0 || rv.BoundIndex >= rows.Count) continue;
                rv.SetSelected(selected.Contains(rv.BoundGid), rv.BoundIndex % 2 == 1);
            }
        }

        private class IconSet
        {
            public readonly List<Image> Icons = new List<Image>();
            public readonly List<IconHover> Hovers = new List<IconHover>();
            public Text Overflow;
        }

        private class RowView
        {
            public GameObject Go;
            public RectTransform Rt;
            public Image Bg;
            public Text NameText, PlanetText, BehaviorText, GroupText, P2PText;
            public IconSet SupplyIcons, DemandIcons;
            public int BoundIndex = -1;
            public int BoundGid = -1;

            public void SetSelected(bool isSelected, bool alt)
            {
                Bg.color = isSelected ? UIFactory.ColRowSelected : (alt ? UIFactory.ColRowAlt : UIFactory.ColRow);
            }
        }
    }
}
