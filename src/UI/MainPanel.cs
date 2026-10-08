using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using StationFlow.Data;
using StationFlow.PluginCore;
using StationFlow.Rules;
using StationFlow.VanillaBridge;

namespace StationFlow.UI
{
    /// <summary>
    /// 阶段 1 主面板（F8）：物流塔总览表格 + 多选 + 批量命名生成器。
    /// 懒构建：首次打开时创建全部 uGUI 控件；关闭仅隐藏。
    /// </summary>
    public class MainPanel : MonoBehaviour
    {
        public static MainPanel Instance;

        private const float PanelW = 1110f;
        private const float PanelH = 640f;

        private GameObject canvasGo;
        private RectTransform panelRt;
        private bool built;
        private bool visible;

        private StationTable table;
        private List<StationRow> allRows = new List<StationRow>();

        /// <summary>当前快照（供其他页签只读）。</summary>
        public List<StationRow> AllRows { get { return allRows; } }

        /// <summary>总览表格（供其他页签读取选中塔）。</summary>
        public StationTable Table { get { return table; } }

        /// <summary>面板是否可见。</summary>
        public bool IsVisible { get { return visible; } }

        /// <summary>自动巡检完成通知（面板可见时刷新数据并提示）。</summary>
        public void NotifyAutoRun(string info)
        {
            if (visible) RefreshData();
            SetStatus("[自动巡检] " + info);
        }

        // Tab 结构
        private GameObject tab1Group, tab2Group, tab3Group, tab4Group, tab5Group;
        private Button tab1Btn, tab2Btn, tab3Btn, tab4Btn, tab5Btn;
        private int tabIndex;
        private RuleTab ruleTab;
        private TopologyTab topoTab;
        private DiagTab diagTab;
        private BatchTab batchTab;

        // 侧栏控件
        private InputField templateInput;
        private Text previewText;
        private Text selectionText;
        private Text statusText;
        private Text exampleText;
        private Button skipModeBtn, overwriteModeBtn;
        private NamingService.ExistingNameMode nameMode = NamingService.ExistingNameMode.Skip;

        private List<RenamePlanItem> currentPlan;
        private readonly List<RenameUndoEntry> lastUndo = new List<RenameUndoEntry>();
        private string lastStatus = "就绪";

        // 预设模板（label=按钮文字, tpl=模板, desc=悬停说明）
        // 语义：{供}/{需}/{储} 均为**星际（远程）逻辑**的物品，不含本地逻辑
        private static readonly (string label, string tpl, string desc)[] PresetTemplates =
        {
            ("全称", "供{供}需{需} {星系}{行星}{序号}号塔", "星际供需全称格式：供X需Y 星系+行星+几号塔"),
            ("经典", "[{物品}][{角色}]-{序号:2}",            "单物品塔经典格式：物品+角色+序号"),
            ("星球", "[{星球}]-{物品}-{序号:2}",             "按星球归类命名"),
            ("仓储", "储{储} {星系}{行星}{序号}号塔",        "星际仓储塔（无星际供需、纯中转存放）"),
        };

        private class RenameUndoEntry
        {
            public int Gid;
            public string OldName;
        }

        private void Awake()
        {
            Instance = this;
        }

        // ================= 显示控制 =================

        public void Toggle()
        {
            if (visible) Hide();
            else Show();
        }

        public void Show()
        {
            if (!built) BuildUI();
            canvasGo.SetActive(true);
            visible = true;
            RefreshData();
            ShowTab(tabIndex);
        }

        public void Hide()
        {
            if (canvasGo != null) canvasGo.SetActive(false);
            visible = false;
        }

        private void Update()
        {
            if (!visible) return;
            if (ruleTab != null) ruleTab.Update();
            if (topoTab != null) topoTab.Update();
            if (batchTab != null) batchTab.Update();
        }

        private void ShowTab(int idx)
        {
            tabIndex = idx;
            if (tab1Group != null) tab1Group.SetActive(idx == 0);
            if (tab2Group != null) tab2Group.SetActive(idx == 1);
            if (tab3Group != null) tab3Group.SetActive(idx == 2);
            if (tab4Group != null) tab4Group.SetActive(idx == 3);
            if (tab5Group != null) tab5Group.SetActive(idx == 4);
            SetTabBtnColor(tab1Btn, idx == 0);
            SetTabBtnColor(tab2Btn, idx == 1);
            SetTabBtnColor(tab3Btn, idx == 2);
            SetTabBtnColor(tab4Btn, idx == 3);
            SetTabBtnColor(tab5Btn, idx == 4);
            if (idx == 1 && ruleTab != null) ruleTab.Refresh();
            if (idx == 2 && topoTab != null) topoTab.Refresh();
            if (idx == 3 && diagTab != null) diagTab.Refresh();
            if (idx == 4 && batchTab != null) batchTab.Refresh();
        }

        private static void SetTabBtnColor(Button b, bool active)
        {
            var img = b != null ? b.GetComponent<Image>() : null;
            if (img != null) img.color = active ? UIFactory.ColBtnHighlight : UIFactory.ColBtn;
        }

        // ================= UI 构建 =================

        private void BuildUI()
        {
            canvasGo = UIFactory.CreateCanvasRoot("StationFlow_MainPanel", 31000);

            // 面板背景（同时充当输入阻挡：raycast 会命中它，游戏世界点击被屏蔽）
            var bg = UIFactory.CreateImage(canvasGo.transform, "Panel", UIFactory.ColBg);
            panelRt = bg.rectTransform;
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0, 1);
            panelRt.pivot = new Vector2(0, 1);
            panelRt.anchoredPosition = new Vector2(StationFlowPlugin.CfgPanelX.Value, -StationFlowPlugin.CfgPanelY.Value);
            panelRt.sizeDelta = new Vector2(PanelW, PanelH);

            // 标题栏（可拖拽）
            var titleBar = UIFactory.CreateImage(panelRt, "TitleBar", UIFactory.ColPanel);
            UIFactory.PlaceTopLeft(titleBar.rectTransform, 0, 0, PanelW, 32);
            var drag = titleBar.gameObject.AddComponent<DragHandler>();
            drag.Target = panelRt;
            drag.OnEndMove = () =>
            {
                StationFlowPlugin.CfgPanelX.Value = panelRt.anchoredPosition.x;
                StationFlowPlugin.CfgPanelY.Value = -panelRt.anchoredPosition.y;
            };
            var title = UIFactory.CreateText(titleBar.transform, "Title", "StationFlow · 物流塔管理", 15, TextAnchor.MiddleLeft, UIFactory.ColAccent);
            UIFactory.PlaceTopLeft(title.rectTransform, 10, 4, 400, 24);
            var closeBtn = UIFactory.CreateButton(titleBar.transform, "Close", "✕", 14, Hide);
            UIFactory.PlaceTopLeft((RectTransform)closeBtn.transform, PanelW - 36, 4, 26, 24);

            // 页签栏
            var tabBar = UIFactory.CreateImage(panelRt, "TabBar", UIFactory.ColPanel);
            UIFactory.PlaceTopLeft(tabBar.rectTransform, 0, 32, PanelW, 28);
            tab1Btn = UIFactory.CreateButton(tabBar.transform, "Tab1", "总览与命名", 13, () => ShowTab(0));
            UIFactory.PlaceTopLeft((RectTransform)tab1Btn.transform, 4, 3, 116, 22);
            tab2Btn = UIFactory.CreateButton(tabBar.transform, "Tab2", "规则配对", 13, () => ShowTab(1));
            UIFactory.PlaceTopLeft((RectTransform)tab2Btn.transform, 124, 3, 100, 22);
            tab3Btn = UIFactory.CreateButton(tabBar.transform, "Tab3", "物流网", 13, () => ShowTab(2));
            UIFactory.PlaceTopLeft((RectTransform)tab3Btn.transform, 228, 3, 100, 22);
            tab4Btn = UIFactory.CreateButton(tabBar.transform, "Tab4", "诊断", 13, () => ShowTab(3));
            UIFactory.PlaceTopLeft((RectTransform)tab4Btn.transform, 332, 3, 84, 22);
            tab5Btn = UIFactory.CreateButton(tabBar.transform, "Tab5", "批量工具", 13, () => ShowTab(4));
            UIFactory.PlaceTopLeft((RectTransform)tab5Btn.transform, 420, 3, 96, 22);

            // Tab1 内容容器
            float contentH = PanelH - 60 - 22;
            tab1Group = UIFactory.CreateNode(panelRt, "Tab1").gameObject;
            UIFactory.PlaceTopLeft((RectTransform)tab1Group.transform, 0, 60, PanelW, contentH);
            var t1 = (RectTransform)tab1Group.transform;

            // 工具条
            var toolbar = UIFactory.CreateImage(t1, "Toolbar", UIFactory.ColPanel);
            UIFactory.PlaceTopLeft(toolbar.rectTransform, 0, 0, PanelW, 30);
            float bx = 8;
            AddToolButton(toolbar.transform, "刷新", ref bx, 64, RefreshData);
            AddToolButton(toolbar.transform, "全选 (A)", ref bx, 84, () => { if (table != null) table.SelectAll(); });
            AddToolButton(toolbar.transform, "全不选", ref bx, 64, () => { if (table != null) table.SelectNone(); });
            AddToolButton(toolbar.transform, "反选", ref bx, 56, () => { if (table != null) table.InvertSelection(); });
            AddToolButton(toolbar.transform, "Dump CSV", ref bx, 84, () =>
            {
                string p = DumpTool.Dump();
                SetStatus(p != null ? "已导出: " + p : "Dump 失败（游戏未就绪）");
            });
            selectionText = UIFactory.CreateText(toolbar.transform, "Sel", "已选 0", 13, TextAnchor.MiddleLeft, UIFactory.ColText);
            UIFactory.PlaceTopLeft(selectionText.rectTransform, bx + 10, 3, 200, 24);

            // 表格（左）
            float tableH = contentH - 34;
            table = new StationTable();
            table.Init(t1, 810, tableH);
            UIFactory.PlaceTopLeft((RectTransform)table.Root.transform, 8, 34, 810, tableH);
            table.SelectionChanged = () =>
            {
                UpdateSelectionLabel();
                UpdateTemplateExample();
                currentPlan = null;
                if (previewText != null) previewText.text = "";
            };

            // 侧栏（右）
            var side = UIFactory.CreateImage(t1, "Side", UIFactory.ColPanel);
            UIFactory.PlaceTopLeft(side.rectTransform, 826, 34, 276, tableH);
            BuildSidePanel(side.rectTransform);

            // Tab2 容器（规则配对）
            tab2Group = UIFactory.CreateNode(panelRt, "Tab2").gameObject;
            UIFactory.PlaceTopLeft((RectTransform)tab2Group.transform, 0, 60, PanelW, contentH);
            ruleTab = new RuleTab();
            ruleTab.Init((RectTransform)tab2Group.transform, this);

            // Tab3 容器（物流网拓扑）
            tab3Group = UIFactory.CreateNode(panelRt, "Tab3").gameObject;
            UIFactory.PlaceTopLeft((RectTransform)tab3Group.transform, 0, 60, PanelW, contentH);
            topoTab = new TopologyTab();
            topoTab.Init((RectTransform)tab3Group.transform, this);

            // Tab4 容器（诊断）
            tab4Group = UIFactory.CreateNode(panelRt, "Tab4").gameObject;
            UIFactory.PlaceTopLeft((RectTransform)tab4Group.transform, 0, 60, PanelW, contentH);
            diagTab = new DiagTab();
            diagTab.Init((RectTransform)tab4Group.transform, this);

            // Tab5 容器（批量工具）
            tab5Group = UIFactory.CreateNode(panelRt, "Tab5").gameObject;
            UIFactory.PlaceTopLeft((RectTransform)tab5Group.transform, 0, 60, PanelW, contentH);
            batchTab = new BatchTab();
            batchTab.Init((RectTransform)tab5Group.transform, this);

            // 状态栏（底部单行，叠在表格底部上方——放工具条下方右侧更稳，这里用底部条）
            var statusBar = UIFactory.CreateImage(panelRt, "StatusBar", new Color(0.03f, 0.05f, 0.08f, 0.95f));
            UIFactory.PlaceTopLeft(statusBar.rectTransform, 0, PanelH - 22, PanelW, 22);
            statusText = UIFactory.CreateText(statusBar.transform, "Status", "就绪", 12, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(statusText.rectTransform, 8, 1, PanelW - 16, 20);

            // 图标悬停 → 状态栏显示物品详情；移出 → 恢复最后状态
            IconHover.OnHoverText = msg =>
            {
                if (statusText == null) return;
                statusText.text = string.IsNullOrEmpty(msg) ? lastStatus : msg;
            };

            built = true;
        }

        private void AddToolButton(Transform parent, string label, ref float x, float w, Action onClick)
        {
            var btn = UIFactory.CreateButton(parent, label, label, 13, onClick);
            UIFactory.PlaceTopLeft((RectTransform)btn.transform, x, 3, w, 24);
            x += w + 6;
        }

        private void BuildSidePanel(RectTransform side)
        {
            float y = 8;
            var title = UIFactory.CreateText(side, "T", "批量命名（选中塔）", 14, TextAnchor.MiddleLeft, UIFactory.ColAccent);
            UIFactory.PlaceTopLeft(title.rectTransform, 8, y, 260, 22); y += 26;

            var tplLabel = UIFactory.CreateText(side, "TL", "命名模板：", 12, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(tplLabel.rectTransform, 8, y, 120, 16); y += 18;

            templateInput = UIFactory.CreateInputField(side, "Template", "[{物品}][{角色}]-{序号:2}", 13);
            UIFactory.PlaceTopLeft((RectTransform)templateInput.transform, 8, y, 260, 26); y += 30;
            templateInput.onValueChanged.AddListener(_ => UpdateTemplateExample());

            var vars = UIFactory.CreateText(side, "Vars", "变量：{物品}{角色}{供}{需}{储}{星系}{行星}{星球}{gid}{序号:2}", 11, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(vars.rectTransform, 8, y, 260, 14); y += 15;
            var vars2 = UIFactory.CreateText(side, "Vars2", "{供}星际供应 {需}星际需求 {储}星际仓储（只看星际逻辑）", 11, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(vars2.rectTransform, 8, y, 260, 14); y += 17;

            // 实时示例：当前模板 × 首个选中塔
            exampleText = UIFactory.CreateText(side, "Example", "示例：（未选中塔）", 12, TextAnchor.MiddleLeft, new Color(0.55f, 0.9f, 0.6f, 1f));
            UIFactory.PlaceTopLeft(exampleText.rectTransform, 8, y, 260, 18); y += 22;

            // 预设模板按钮（悬停显示完整模板）
            var presetLabel = UIFactory.CreateText(side, "PL", "预设模板（点击填入）：", 12, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(presetLabel.rectTransform, 8, y, 200, 16); y += 18;

            float bx = 8;
            foreach (var p in PresetTemplates)
            {
                string tpl = p.tpl;
                string desc = p.desc;
                var btn = UIFactory.CreateButton(side, "Pre_" + p.label, p.label, 12, () =>
                {
                    if (templateInput != null) templateInput.text = tpl;
                    UpdateTemplateExample();
                    SetStatus($"已填入模板：{tpl}（{desc}）");
                });
                UIFactory.PlaceTopLeft((RectTransform)btn.transform, bx, y, 62, 24);
                var hover = btn.gameObject.AddComponent<IconHover>();
                hover.Tip = $"{desc}｜{tpl}";
                bx += 65;
            }
            y += 28;

            var autoBtn = UIFactory.CreateButton(side, "AutoTpl", "✨ 自动生成模板（分析选中塔）", 12, DoAutoTemplate);
            UIFactory.PlaceTopLeft((RectTransform)autoBtn.transform, 8, y, 260, 24); y += 30;

            var modeLabel = UIFactory.CreateText(side, "ML", "对已有名字的塔：", 12, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(modeLabel.rectTransform, 8, y, 160, 16); y += 18;

            skipModeBtn = UIFactory.CreateButton(side, "Skip", "跳过（推荐）", 12, () => SetNameMode(NamingService.ExistingNameMode.Skip));
            UIFactory.PlaceTopLeft((RectTransform)skipModeBtn.transform, 8, y, 126, 24);
            overwriteModeBtn = UIFactory.CreateButton(side, "Ow", "覆盖", 12, () => SetNameMode(NamingService.ExistingNameMode.Overwrite));
            UIFactory.PlaceTopLeft((RectTransform)overwriteModeBtn.transform, 140, y, 126, 24); y += 30;

            var previewBtn = UIFactory.CreateButton(side, "Prev", "生成预览", 13, DoPreview);
            UIFactory.PlaceTopLeft((RectTransform)previewBtn.transform, 8, y, 126, 26);
            var applyBtn = UIFactory.CreateButton(side, "Apply", "执行改名", 13, DoApplyRename);
            UIFactory.PlaceTopLeft((RectTransform)applyBtn.transform, 140, y, 126, 26); y += 32;

            previewText = UIFactory.CreateText(side, "Preview", "", 12, TextAnchor.UpperLeft, UIFactory.ColText);
            previewText.horizontalOverflow = HorizontalWrapMode.Wrap;
            previewText.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.PlaceTopLeft(previewText.rectTransform, 8, y, 260, 178); y += 184;

            var undoBtn = UIFactory.CreateButton(side, "Undo", "撤销上次改名", 13, DoUndo);
            UIFactory.PlaceTopLeft((RectTransform)undoBtn.transform, 8, y, 258, 26);

            SetNameMode(NamingService.ExistingNameMode.Skip);
        }

        private void SetNameMode(NamingService.ExistingNameMode mode)
        {
            nameMode = mode;
            var skipImg = skipModeBtn != null ? skipModeBtn.GetComponent<Image>() : null;
            var owImg = overwriteModeBtn != null ? overwriteModeBtn.GetComponent<Image>() : null;
            if (skipImg != null) skipImg.color = mode == NamingService.ExistingNameMode.Skip ? UIFactory.ColBtnHighlight : UIFactory.ColBtn;
            if (owImg != null) owImg.color = mode == NamingService.ExistingNameMode.Overwrite ? UIFactory.ColBtnHighlight : UIFactory.ColBtn;
        }

        // ================= 数据 =================

        public void RefreshData()
        {
            if (!StationOps.GameReady)
            {
                SetStatus("游戏未就绪（请先进入存档）");
                return;
            }
            allRows = StationSnapshot.Build();
            if (table != null) table.SetData(allRows, keepSelection: true);
            UpdateSelectionLabel();
            UpdateTemplateExample();
            SetStatus($"共 {allRows.Count} 个物流塔");
        }

        private void UpdateSelectionLabel()
        {
            if (selectionText != null && table != null)
            {
                selectionText.text = $"已选 {table.SelectedCount} / {allRows.Count}";
            }
        }

        public void SetStatus(string msg, bool log = true)
        {
            lastStatus = msg;
            if (statusText != null) statusText.text = msg;
            if (log) StationFlowPlugin.LogInfo($"[面板] {msg}");
        }

        // ================= 模板示例 / 自动模板 =================

        /// <summary>实时示例：当前模板 × 首个选中塔 → 立即看到生成效果。</summary>
        private void UpdateTemplateExample()
        {
            if (exampleText == null) return;
            if (table == null)
            {
                exampleText.text = "示例：（先刷新数据）";
                return;
            }
            var rows = table.SelectedRows;
            if (rows.Count == 0)
            {
                exampleText.text = "示例：（未选中塔，先在左侧表格点选）";
                return;
            }
            string tpl = templateInput != null ? templateInput.text : "";
            if (string.IsNullOrEmpty(tpl) || tpl.Trim().Length == 0)
            {
                exampleText.text = "示例：（模板为空）";
                return;
            }
            var r0 = rows[0];
            string result = NamingService.Format(tpl, r0, 1).Trim();
            if (result.Length > 34) result = result.Substring(0, 34) + "…";
            exampleText.text = $"示例：#{r0.Gid} → {result}";
        }

        /// <summary>自动生成模板：分析选中塔的供需槽位分布，推荐最合适的模板并填入。</summary>
        private void DoAutoTemplate()
        {
            if (table == null) return;
            var rows = table.SelectedRows;
            if (rows.Count == 0)
            {
                SetStatus("请先在表格中选中一些塔，再点「自动生成模板」");
                return;
            }

            bool anyMulti = false;   // 存在多物品塔
            int supplyOnly = 0, demandOnly = 0, mixed = 0, storageOnly = 0;
            foreach (var r in rows)
            {
                int s = 0, d = 0, st = 0;
                if (r.Slots != null)
                {
                    foreach (var slot in r.Slots)
                    {
                        if (slot.RemoteLogic == 1) s++;       // 星际供应
                        else if (slot.RemoteLogic == 2) d++;  // 星际需求
                        else st++;                            // 星际仓储
                    }
                }
                if (s + d + st > 1) anyMulti = true;
                if (s > 0 && d > 0) mixed++;
                else if (s > 0) supplyOnly++;
                else if (d > 0) demandOnly++;
                else if (st > 0) storageOnly++;
            }

            string tpl;
            string why;
            if (supplyOnly + demandOnly + mixed == 0 && storageOnly > 0)
            {
                tpl = "储{储} {星系}{行星}{序号}号塔";
                why = "选中的塔均为星际仓储（无星际供需） → 仓储格式";
            }
            else if (!anyMulti)
            {
                tpl = "[{物品}][{角色}]-{序号:2}";
                why = "选中的塔均为单物品 → 经典格式";
            }
            else if (mixed > 0 || (supplyOnly > 0 && demandOnly > 0))
            {
                tpl = "供{供}需{需} {星系}{行星}{序号}号塔";
                why = "存在多物品/供需混合塔 → 供需全称格式";
            }
            else if (supplyOnly > 0)
            {
                tpl = "供{供} {星系}{行星}{序号}号塔";
                why = "多物品纯供应 → 全称格式";
            }
            else
            {
                tpl = "需{需} {星系}{行星}{序号}号塔";
                why = "多物品纯需求 → 全称格式";
            }

            if (templateInput != null) templateInput.text = tpl;
            UpdateTemplateExample();
            string extra = storageOnly > 0 && (supplyOnly + demandOnly + mixed > 0)
                ? $"；另有 {storageOnly} 个塔含星际仓储物品（可用「储」变量命名）" : "";
            SetStatus($"自动模板：{why}，已填入{extra}");
        }

        // ================= 批量改名 =================

        private void DoPreview()
        {
            if (table == null) return;
            var selectedRows = table.SelectedRows;
            if (selectedRows.Count == 0)
            {
                SetStatus("请先在表格中选中要改名的塔");
                return;
            }
            string template = templateInput != null ? templateInput.text : "";
            if (string.IsNullOrEmpty(template) || template.Trim().Length == 0)
            {
                SetStatus("请输入命名模板（可点「预设模板」或「自动生成模板」快速填入）");
                return;
            }

            currentPlan = NamingService.Generate(selectedRows, allRows, template, nameMode);

            // 诊断日志：选中数/计划数/前 3 条的槽位统计与名字（排查"变量为空"类问题时看日志）
            var log = new StringBuilder($"预览: 选中 {selectedRows.Count}, 计划 {currentPlan.Count}, 模式={nameMode}");
            for (int i = 0; i < Mathf.Min(3, currentPlan.Count); i++)
            {
                var it = currentPlan[i];
                int s = 0, d = 0;
                if (it.Row.Slots != null)
                {
                    foreach (var sl in it.Row.Slots)
                    {
                        if (sl.RemoteLogic == 1) s++;
                        else if (sl.RemoteLogic == 2) d++;
                    }
                }
                log.Append($"; #{it.Row.Gid}(供{s}/需{d}) \"{it.OldName}\"→\"{it.NewName}\"");
            }
            StationFlowPlugin.LogInfo(log.ToString());

            if (currentPlan.Count == 0)
            {
                if (previewText != null) previewText.text = "";
                if (nameMode == NamingService.ExistingNameMode.Skip)
                    SetStatus($"选中的 {selectedRows.Count} 个塔都已有名字，当前为「跳过」模式。可切换为「覆盖」，或改选未命名的塔");
                else
                    SetStatus("模板生成结果为空，请检查模板内容");
                return;
            }

            var sb = new StringBuilder();
            int show = Mathf.Min(currentPlan.Count, 10);
            for (int i = 0; i < show; i++)
            {
                var it = currentPlan[i];
                sb.Append("#").Append(it.Row.Gid).Append("  ").Append(it.NewName).AppendLine();
            }
            if (currentPlan.Count > show) sb.Append("… 共 ").Append(currentPlan.Count).Append(" 条");
            if (previewText != null) previewText.text = sb.ToString();

            int skipped = selectedRows.Count - currentPlan.Count;
            SetStatus($"预览：将改名 {currentPlan.Count} 条" + (skipped > 0 ? $"，跳过 {skipped} 个（已有名字）" : ""));
        }

        private void DoApplyRename()
        {
            if (currentPlan == null || currentPlan.Count == 0)
            {
                SetStatus("请先点「生成预览」");
                return;
            }
            int ok = 0, fail = 0;
            lastUndo.Clear();
            foreach (var item in currentPlan)
            {
                var st = StationOps.GetStation(item.Row.Gid);
                if (st == null) { fail++; continue; }
                if (StationOps.SetName(st, item.NewName))
                {
                    ok++;
                    lastUndo.Add(new RenameUndoEntry { Gid = item.Row.Gid, OldName = item.OldName });
                }
                else fail++;
            }
            currentPlan = null;
            if (previewText != null) previewText.text = "";
            SetStatus($"改名完成：成功 {ok}" + (fail > 0 ? $"，失败 {fail}" : "") + "（可点「撤销上次改名」回滚）");
            RefreshData();
        }

        private void DoUndo()
        {
            if (lastUndo.Count == 0)
            {
                SetStatus("没有可撤销的改名记录（仅保留最近一次执行）");
                return;
            }
            int ok = 0;
            foreach (var u in lastUndo)
            {
                var st = StationOps.GetStation(u.Gid);
                if (st != null && StationOps.SetName(st, u.OldName)) ok++;
            }
            SetStatus($"已撤销 {ok} 个改名");
            lastUndo.Clear();
            RefreshData();
        }
    }
}
