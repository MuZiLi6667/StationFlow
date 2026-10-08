using System.Text;
using StationFlow.Rules;
using StationFlow.VanillaBridge;
using UnityEngine;
using UnityEngine.UI;

namespace StationFlow.UI
{
    /// <summary>「诊断」页签：离线健康检查（停工风险 / 断供物品 / 供应偏紧）。</summary>
    public class DiagTab
    {
        private RectTransform root;
        private MainPanel owner;
        private Text overviewText;
        private Text listText;
        private RectTransform content;

        public void Init(RectTransform parent, MainPanel owner)
        {
            this.owner = owner;
            root = UIFactory.CreateNode(parent, "DiagTab");
            UIFactory.Stretch(root);
            BuildUI();
        }

        private void BuildUI()
        {
            var runBtn = UIFactory.CreateButton(root, "Run", "执行诊断", 13, DoRun);
            UIFactory.PlaceTopLeft((RectTransform)runBtn.transform, 8, 6, 90, 26);

            overviewText = UIFactory.CreateText(root, "Ov", "点「执行诊断」检查配置健康度…", 12, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(overviewText.rectTransform, 106, 6, 970, 26);

            // 滚动列表
            RectTransform viewport;
            var scroll = UIFactory.CreateScrollView(root, "Scroll", out viewport, out content);
            UIFactory.PlaceTopLeft((RectTransform)scroll.transform, 8, 38, 1094, 514);
            viewport.gameObject.AddComponent<ModScrollZone>();

            listText = UIFactory.CreateText(content, "List", "", 12, TextAnchor.UpperLeft, UIFactory.ColText);
            listText.supportRichText = true;
            listText.horizontalOverflow = HorizontalWrapMode.Wrap;
            listText.verticalOverflow = VerticalWrapMode.Overflow;
            listText.rectTransform.anchorMin = new Vector2(0, 1);
            listText.rectTransform.anchorMax = new Vector2(1, 1);
            listText.rectTransform.pivot = new Vector2(0.5f, 1f);
            listText.rectTransform.anchoredPosition = new Vector2(0, -4);
            listText.rectTransform.sizeDelta = new Vector2(-16, 100);
        }

        /// <summary>页面显示时自动执行一次。</summary>
        public void Refresh()
        {
            DoRun();
        }

        public void Update() { }

        private void DoRun()
        {
            if (!StationOps.GameReady)
            {
                if (overviewText != null) overviewText.text = "游戏未就绪（请先进入存档）";
                if (listText != null) listText.text = "";
                return;
            }
            var rows = owner != null ? owner.AllRows : null;
            var report = Diagnostics.Run(rows);
            overviewText.text = report.Overview;

            var sb = new StringBuilder();
            if (report.Items.Count == 0)
            {
                sb.Append("<color=#7fd68f>✔ 未发现高危问题——配对配置看起来正常。</color>\n\n");
                sb.Append("（诊断覆盖：停工风险塔 / 断供物品 / 供应偏紧。）\n");
                sb.Append("物流是否真的在流动，请看「物流网」页的流量数据。");
            }
            else
            {
                sb.Append($"<color=#ff6b5a>✖ 高危 {report.DangerCount}</color>   <color=#ffb84d>▲ 警告 {report.WarnCount}</color>\n\n");
                foreach (var it in report.Items)
                {
                    string color = it.Level == DiagLevel.Danger ? "#ff6b5a"
                                 : it.Level == DiagLevel.Warn ? "#ffb84d" : "#9adcff";
                    string icon = it.Level == DiagLevel.Danger ? "✖"
                                : it.Level == DiagLevel.Warn ? "▲" : "●";
                    sb.Append($"<color={color}>{icon} {it.Title}</color>\n");
                    if (!string.IsNullOrEmpty(it.Detail)) sb.Append($"      → {it.Detail}\n");
                    sb.Append('\n');
                }
            }
            listText.text = sb.ToString();

            // 列表高度自适应
            float h = listText.preferredHeight;
            listText.rectTransform.sizeDelta = new Vector2(-16f, h);
            content.sizeDelta = new Vector2(0, h + 12f);
        }
    }
}
