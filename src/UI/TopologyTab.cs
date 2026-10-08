using System.Collections.Generic;
using System.Text;
using StationFlow.Data;
using StationFlow.VanillaBridge;
using UnityEngine;
using UnityEngine.UI;

namespace StationFlow.UI
{
    /// <summary>
    /// 「物流网」页签：星球（列）× 物品（行）矩阵。
    /// 格子颜色 = 实际流向（绿=输出 / 橙=输入 / 黄=进出 / 深色=仅有配置未流动 / 灰=仓储）；
    /// 格子文本 = 流量（↑/↓ k/min）或塔数；悬停查看详情。
    /// </summary>
    public class TopologyTab
    {
        private const float ColW = 72f;
        private const float RowH = 22f;
        private const float NameW = 170f;
        private const float HeaderH = 38f;

        private RectTransform root;
        private MainPanel owner;
        private ScrollRect scroll;
        private RectTransform content;
        private Text statText;
        private Text emptyText;
        private TopologyModel model;

        public void Init(RectTransform parent, MainPanel owner)
        {
            this.owner = owner;
            root = UIFactory.CreateNode(parent, "TopologyTab");
            UIFactory.Stretch(root);
            BuildUI();
        }

        private void BuildUI()
        {
            var reloadBtn = UIFactory.CreateButton(root, "Reload", "刷新", 13, DoRefresh);
            UIFactory.PlaceTopLeft((RectTransform)reloadBtn.transform, 8, 6, 64, 26);

            statText = UIFactory.CreateText(root, "Stat",
                "物流网：行=物品，列=星球。绿色=星际输出、橙色=输入、黄色=进出、深色=仅有配置、灰=仓储。悬停格子看详情。",
                12, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(statText.rectTransform, 80, 6, 1010, 26);

            // 双轴滚动区
            var scrollGo = UIFactory.CreateNode(root, "Scroll");
            var scrollRt = scrollGo;
            UIFactory.PlaceTopLeft(scrollGo, 8, 38, 1094, 514);
            scroll = scrollGo.gameObject.AddComponent<ScrollRect>();

            var vp = UIFactory.CreateImage(scrollRt, "Viewport", new Color(0f, 0f, 0f, 0.22f));
            UIFactory.Stretch(vp.rectTransform);
            vp.gameObject.AddComponent<RectMask2D>();
            vp.gameObject.AddComponent<ModScrollZone>(); // 滚轮防穿透（复用游戏机制）
            scroll.viewport = vp.rectTransform;

            content = UIFactory.CreateNode(vp.rectTransform, "Content");
            content.anchorMin = content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            scroll.content = content;
            scroll.horizontal = true;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            emptyText = UIFactory.CreateText(root, "Empty", "", 13, TextAnchor.MiddleCenter, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(emptyText.rectTransform, 8, 260, 1094, 24);
            emptyText.gameObject.SetActive(false);
        }

        /// <summary>页面显示/数据刷新时重建。</summary>
        public void Refresh()
        {
            Rebuild();
        }

        public void Update() { }

        private void DoRefresh()
        {
            if (owner != null) owner.RefreshData();
            Rebuild();
        }

        // ================= 重建矩阵 =================

        private void Rebuild()
        {
            if (!StationOps.GameReady)
            {
                SetEmpty("游戏未就绪（请先进入存档）");
                return;
            }
            var rows = owner != null ? owner.AllRows : null;
            if (rows == null || rows.Count == 0)
            {
                SetEmpty("没有物流塔数据——请先在「总览与命名」页刷新。");
                return;
            }

            model = TopologyModel.Build(rows);

            // 清空旧内容（先禁用再销毁：Destroy 帧末才生效，避免同帧新旧内容叠加显示）
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var old = content.GetChild(i).gameObject;
                old.SetActive(false);
                Object.Destroy(old);
            }

            int cols = model.PlanetIds.Count;
            const int MaxRows = 120; // 大矩阵保护：行数上限（Rows 已按流量降序，低流量物品在后）
            int rowsCnt = Mathf.Min(model.Rows.Count, MaxRows);
            content.sizeDelta = new Vector2(NameW + cols * ColW + 8f, HeaderH + rowsCnt * RowH + 8f);

            // 列头
            var corner = UIFactory.CreateText(content, "Corner", "物品 ＼ 星球", 11, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(corner.rectTransform, 4, 8, NameW - 8, 20);
            for (int i = 0; i < cols; i++)
            {
                var t = UIFactory.CreateText(content, "P" + i, model.PlanetNames[i], 10, TextAnchor.MiddleCenter, UIFactory.ColAccent);
                t.lineSpacing = 0.9f;
                UIFactory.PlaceTopLeft(t.rectTransform, NameW + i * ColW, 2, ColW, HeaderH - 4);
            }

            // 数据行
            for (int r = 0; r < rowsCnt; r++)
            {
                var row = model.Rows[r];
                float y = HeaderH + r * RowH;

                // 行背景（隔行）
                if (r % 2 == 1)
                {
                    var bg = UIFactory.CreateImage(content, "RowBg", new Color(1f, 1f, 1f, 0.03f));
                    UIFactory.PlaceTopLeft(bg.rectTransform, 0, y, NameW + cols * ColW + 4f, RowH);
                    bg.raycastTarget = false;
                }

                // 物品图标 + 名称 + 总流量
                var icon = UIFactory.CreateImage(content, "Ico" + r, Color.white);
                var sprite = StationOps.ItemIcon(row.ItemId);
                if (sprite != null) icon.sprite = sprite;
                else icon.color = new Color(0.4f, 0.4f, 0.4f, 0.8f);
                icon.raycastTarget = false;
                UIFactory.PlaceTopLeft(icon.rectTransform, 2, y + 3, 16, 16);

                string label = row.TotalFlow > 0
                    ? $"{row.ItemName}  {Fmt(row.TotalFlow)}/min"
                    : row.ItemName;
                var nameText = UIFactory.CreateText(content, "Name" + r, label, 11,
                    TextAnchor.MiddleLeft, row.TotalFlow > 0 ? UIFactory.ColText : UIFactory.ColTextDim);
                UIFactory.PlaceTopLeft(nameText.rectTransform, 22, y + 2, NameW - 26, RowH - 4);

                // 格子
                for (int c = 0; c < cols; c++)
                {
                    var cell = row.Cells[c];
                    if (cell == null) continue;
                    BuildCell(cell, NameW + c * ColW, y);
                }
            }

            string tip = $"{cols} 星球 × {model.Rows.Count} 物品 · 全星区物流量 {Fmt(model.TotalFlowAll)}/min（约最近1分钟）";
            if (model.Rows.Count > rowsCnt) tip += $" · 仅显示流量最高的 {rowsCnt} 类物品（其余 {model.Rows.Count - rowsCnt} 类未显示）";
            if (model.TotalFlowAll == 0) tip += " · 暂无星际物流——检查配对/供应是否就绪";
            statText.text = tip;
            emptyText.gameObject.SetActive(false);
        }

        private void BuildCell(TopoCell c, float x, float y)
        {
            var img = UIFactory.CreateImage(content, "C", CellColor(c));
            UIFactory.PlaceTopLeft(img.rectTransform, x + 1, y + 1, ColW - 3, RowH - 3);

            var hover = img.gameObject.AddComponent<IconHover>();
            hover.Tip = CellTip(c);

            string txt = CellText(c);
            if (!string.IsNullOrEmpty(txt))
            {
                var t = UIFactory.CreateText(img.transform, "T", txt, 10, TextAnchor.MiddleCenter, Color.white);
                UIFactory.Stretch(t.rectTransform);
            }
        }

        private void SetEmpty(string msg)
        {
            if (emptyText != null)
            {
                emptyText.text = msg;
                emptyText.gameObject.SetActive(true);
            }
            if (statText != null) statText.text = "物流网";
            if (content != null)
            {
                for (int i = content.childCount - 1; i >= 0; i--)
                {
                    var old = content.GetChild(i).gameObject;
                    old.SetActive(false);
                    Object.Destroy(old);
                }
                content.sizeDelta = Vector2.zero;
            }
        }

        // ================= 单元显示逻辑 =================

        private static Color CellColor(TopoCell c)
        {
            bool hasOut = c.OutputPerMin > 0, hasIn = c.InputPerMin > 0;
            if (hasOut && hasIn) return new Color(0.48f, 0.44f, 0.13f, 0.88f); // 黄：进出
            if (hasOut) return new Color(0.15f, 0.44f, 0.26f, 0.88f);          // 绿：输出
            if (hasIn) return new Color(0.52f, 0.34f, 0.13f, 0.88f);           // 橙：输入
            if (c.SupplyTowers > 0 && c.DemandTowers > 0) return new Color(0.24f, 0.34f, 0.20f, 0.6f);
            if (c.SupplyTowers > 0) return new Color(0.15f, 0.28f, 0.19f, 0.55f);
            if (c.DemandTowers > 0) return new Color(0.34f, 0.24f, 0.13f, 0.55f);
            return new Color(0.24f, 0.24f, 0.24f, 0.45f);                      // 灰：仓储
        }

        private static string CellText(TopoCell c)
        {
            if (c.OutputPerMin > 0 || c.InputPerMin > 0)
            {
                if (c.OutputPerMin >= c.InputPerMin) return "↑" + Fmt(c.OutputPerMin);
                return "↓" + Fmt(c.InputPerMin);
            }
            if (c.SupplyTowers > 0 && c.DemandTowers > 0) return "供需";
            if (c.SupplyTowers > 0) return "供" + c.SupplyTowers;
            if (c.DemandTowers > 0) return "需" + c.DemandTowers;
            if (c.StorageTowers > 0) return "储" + c.StorageTowers;
            return "";
        }

        private static string CellTip(TopoCell c)
        {
            var sb = new StringBuilder();
            sb.Append(StationOps.GalaxyName(c.PlanetId)).Append(' ').Append(StationOps.ShortPlanetName(c.PlanetId))
              .Append(" · ").Append(StationOps.ItemName(c.ItemId));
            if (c.SupplyTowers > 0) sb.Append($" ｜ 星际供应 {c.SupplyTowers} 塔（库存 {Fmt(c.SupplyCount)}）");
            if (c.DemandTowers > 0) sb.Append($" ｜ 星际需求 {c.DemandTowers} 塔（缺口 {Fmt(c.DemandGap)}）");
            if (c.StorageTowers > 0) sb.Append($" ｜ 星际仓储 {c.StorageTowers} 塔");
            if (c.HasActivity)
                sb.Append($" ｜ 输入 {Fmt(c.InputPerMin)}/min · 输出 {Fmt(c.OutputPerMin)}/min");
            return sb.ToString();
        }

        private static string Fmt(long v)
        {
            if (v >= 1000000) return (v / 1000000.0).ToString("F1") + "M";
            if (v >= 1000) return (v / 1000.0).ToString("F1") + "k";
            return v.ToString();
        }
    }
}
