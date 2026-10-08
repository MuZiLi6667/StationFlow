using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using StationFlow.Data;
using StationFlow.Exec;
using StationFlow.PluginCore;
using StationFlow.Rules;
using StationFlow.VanillaBridge;
using UnityEngine;
using UnityEngine.UI;

namespace StationFlow.UI
{
    /// <summary>
    /// 「规则配对」页（模式一 · 命名规则驱动）：
    /// 参数（TopK / 最小供应占比 / 跨星系）→ 生成配对计划 → 预览 → 事务执行 → 撤销。
    /// </summary>
    public class RuleTab
    {
        private const int PreviewLimit = 30;
        private const int WarnShowLimit = 7;

        private RectTransform root;
        private MainPanel owner;

        // 参数
        private PairSource source = PairSource.ByName;
        private PairMethod method = PairMethod.P2P;
        private int topK = 3;
        private float minRatio = 0.2f;
        private bool allowCrossAstro = true;

        private static readonly int[] KOptions = { 1, 2, 3, 4, 5 };
        private static readonly float[] RatioOptions = { 0.1f, 0.2f, 0.3f, 0.5f };

        private const string DescByName =
            "解析塔名字中的 [物品][角色]（严格按星际逻辑），自动为同物品的供需塔建立 P2P：\n" +
            "需求塔按缺口从大到小、就近挑选供应塔（每塔最多配 K 个）。\n" +
            "单供应源物品无需配对（官方兜底自动服务）；星际仓储/本地逻辑不参与。";
        private const string DescBySlot =
            "直接读取全部塔的星际供/需槽位（无需命名），自动为同物品的供需塔建立 P2P：\n" +
            "需求塔按缺口从大到小、就近挑选供应塔（每塔最多配 K 个）。\n" +
            "未命名塔同样参与；星际仓储/本地逻辑不参与。";

        // 控件
        private Button[] kButtons;
        private Button[] ratioButtons;
        private Button crossBtn;
        private Button sourceNameBtn, sourceSlotBtn;
        private Button methodP2PBtn, methodPlanetBtn, methodAstroBtn;
        private Button generateBtn, executeBtn, undoBtn;
        private Button autoBtn;
        private Text titleText, descText, statText, progressLabel, warnText, previewTitle, previewText;
        private Text autoStatusText;
        private string lastAutoInfo;
        private Image progressFill;

        // 结果
        private PairingResult lastResult;
        private List<RoutePlanItem> lastRoutes;   // 航路模式结果
        private ExecutionPlan lastPlan;

        public void Init(RectTransform parent, MainPanel owner)
        {
            this.owner = owner;

            // 从配置读取上次的偏好（重启记忆）
            topK = Mathf.Clamp(StationFlowPlugin.CfgTopK.Value, 1, 5);
            minRatio = Mathf.Clamp(StationFlowPlugin.CfgMinRatio.Value, 0.1f, 0.5f);
            allowCrossAstro = StationFlowPlugin.CfgCrossAstro.Value;
            source = (PairSource)Mathf.Clamp(StationFlowPlugin.CfgPairSource.Value, 0, 1);
            method = (PairMethod)Mathf.Clamp(StationFlowPlugin.CfgPairMethod.Value, 0, 2);

            root = UIFactory.CreateNode(parent, "RuleTab");
            UIFactory.Stretch(root);
            BuildUI();
        }

        // ================= UI 构建 =================

        private void BuildUI()
        {
            float y = 8;
            titleText = UIFactory.CreateText(root, "Title", "规则配对 · 模式一（命名驱动）", 15, TextAnchor.MiddleLeft, UIFactory.ColAccent);
            UIFactory.PlaceTopLeft(titleText.rectTransform, 12, y, 400, 24);

            // 自动巡检开关（标题行右侧）
            autoBtn = UIFactory.CreateButton(root, "Auto", "自动巡检：关", 12, ToggleAuto);
            UIFactory.PlaceTopLeft((RectTransform)autoBtn.transform, 560, 8, 130, 24);
            autoStatusText = UIFactory.CreateText(root, "AutoSt", "", 11, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(autoStatusText.rectTransform, 698, 8, 400, 24);
            y += 28;

            // 数据源切换：按名字（模式一） / 按槽位（模式二）
            sourceNameBtn = UIFactory.CreateButton(root, "SrcName", "按名字（模式一）", 12, () => SetSource(PairSource.ByName));
            UIFactory.PlaceTopLeft((RectTransform)sourceNameBtn.transform, 12, y, 160, 24);
            sourceSlotBtn = UIFactory.CreateButton(root, "SrcSlot", "按槽位（模式二）", 12, () => SetSource(PairSource.BySlot));
            UIFactory.PlaceTopLeft((RectTransform)sourceSlotBtn.transform, 176, y, 160, 24);
            y += 30;

            // 配对方式：用哪种官方机制表达（点对点 / 星球航路 / 行星系航路）
            methodP2PBtn = UIFactory.CreateButton(root, "MtdP2P", "点对点", 12, () => SetMethod(PairMethod.P2P));
            UIFactory.PlaceTopLeft((RectTransform)methodP2PBtn.transform, 12, y, 106, 24);
            methodPlanetBtn = UIFactory.CreateButton(root, "MtdPl", "星球航路", 12, () => SetMethod(PairMethod.PlanetRoute));
            UIFactory.PlaceTopLeft((RectTransform)methodPlanetBtn.transform, 122, y, 106, 24);
            methodAstroBtn = UIFactory.CreateButton(root, "MtdAs", "行星系航路", 12, () => SetMethod(PairMethod.AstroRoute));
            UIFactory.PlaceTopLeft((RectTransform)methodAstroBtn.transform, 232, y, 106, 24);
            y += 30;

            var kLabel = UIFactory.CreateText(root, "KL", "每塔最多配几个供应塔（TopK）：", 12, TextAnchor.MiddleLeft, UIFactory.ColText);
            UIFactory.PlaceTopLeft(kLabel.rectTransform, 12, y, 320, 16); y += 20;
            kButtons = new Button[KOptions.Length];
            float bx = 12;
            for (int i = 0; i < KOptions.Length; i++)
            {
                int k = KOptions[i];
                kButtons[i] = UIFactory.CreateButton(root, "K" + k, k.ToString(), 12, () =>
                {
                    topK = k;
                    UpdateParamButtons();
                    StationFlowPlugin.CfgTopK.Value = k;
                });
                UIFactory.PlaceTopLeft((RectTransform)kButtons[i].transform, bx, y, 44, 24);
                bx += 48;
            }
            y += 32;

            var rLabel = UIFactory.CreateText(root, "RL", "最小供应能力（占需求容量的比例）：", 12, TextAnchor.MiddleLeft, UIFactory.ColText);
            UIFactory.PlaceTopLeft(rLabel.rectTransform, 12, y, 320, 16); y += 20;
            ratioButtons = new Button[RatioOptions.Length];
            bx = 12;
            for (int i = 0; i < RatioOptions.Length; i++)
            {
                float r = RatioOptions[i];
                ratioButtons[i] = UIFactory.CreateButton(root, "R" + i, (r * 100f).ToString("F0") + "%", 12, () =>
                {
                    minRatio = r;
                    UpdateParamButtons();
                    StationFlowPlugin.CfgMinRatio.Value = r;
                });
                UIFactory.PlaceTopLeft((RectTransform)ratioButtons[i].transform, bx, y, 56, 24);
                bx += 60;
            }
            y += 32;

            crossBtn = UIFactory.CreateButton(root, "Cross", "", 12, () =>
            {
                allowCrossAstro = !allowCrossAstro;
                UpdateParamButtons();
                StationFlowPlugin.CfgCrossAstro.Value = allowCrossAstro;
            });
            UIFactory.PlaceTopLeft((RectTransform)crossBtn.transform, 12, y, 200, 24); y += 34;

            generateBtn = UIFactory.CreateButton(root, "Gen", "生成配对计划", 13, DoGenerate);
            UIFactory.PlaceTopLeft((RectTransform)generateBtn.transform, 12, y, 200, 30); y += 38;

            executeBtn = UIFactory.CreateButton(root, "Exec", "执行计划", 13, DoExecute);
            UIFactory.PlaceTopLeft((RectTransform)executeBtn.transform, 12, y, 96, 30);
            undoBtn = UIFactory.CreateButton(root, "Undo", "撤销执行", 13, DoUndo);
            UIFactory.PlaceTopLeft((RectTransform)undoBtn.transform, 116, y, 96, 30); y += 38;

            statText = UIFactory.CreateText(root, "Stat", "尚未生成计划。", 12, TextAnchor.UpperLeft, UIFactory.ColText);
            statText.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIFactory.PlaceTopLeft(statText.rectTransform, 12, y, 336, 32); y += 36;

            // 进度条
            var bar = UIFactory.CreateImage(root, "ProgressBar", new Color(0.10f, 0.15f, 0.20f, 0.9f));
            UIFactory.PlaceTopLeft(bar.rectTransform, 12, y, 336, 12); y += 16;
            progressFill = UIFactory.CreateImage(bar.transform, "Fill", UIFactory.ColAccent);
            UIFactory.Stretch(progressFill.rectTransform);
            progressFill.type = Image.Type.Filled;
            progressFill.fillMethod = Image.FillMethod.Horizontal;
            progressFill.fillAmount = 0f;
            progressFill.raycastTarget = false;

            progressLabel = UIFactory.CreateText(root, "Progress", "", 12, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(progressLabel.rectTransform, 12, y, 336, 16); y += 22;

            var wLabel = UIFactory.CreateText(root, "WL", "不一致警告（名字与槽位不匹配）：", 12, TextAnchor.MiddleLeft, UIFactory.ColWarn);
            UIFactory.PlaceTopLeft(wLabel.rectTransform, 12, y, 336, 16); y += 18;
            warnText = UIFactory.CreateText(root, "Warn", "（生成计划后显示）", 11, TextAnchor.UpperLeft, UIFactory.ColTextDim);
            warnText.horizontalOverflow = HorizontalWrapMode.Wrap;
            warnText.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.PlaceTopLeft(warnText.rectTransform, 12, y, 336, 116);

            // 右侧预览：标题 + 说明（随数据源切换） + 预览文本
            previewTitle = UIFactory.CreateText(root, "PT", "配对预览", 14, TextAnchor.MiddleLeft, UIFactory.ColAccent);
            UIFactory.PlaceTopLeft(previewTitle.rectTransform, 360, 8, 736, 22);
            descText = UIFactory.CreateText(root, "Desc", DescByName, 12, TextAnchor.UpperLeft, UIFactory.ColTextDim);
            descText.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIFactory.PlaceTopLeft(descText.rectTransform, 360, 34, 740, 54);
            previewText = UIFactory.CreateText(root, "Preview",
                "点「生成配对计划」查看结果。\n\n符号说明：＋ = 将新建   ✓ = 已存在",
                12, TextAnchor.UpperLeft, UIFactory.ColText);
            previewText.horizontalOverflow = HorizontalWrapMode.Overflow;
            previewText.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.PlaceTopLeft(previewText.rectTransform, 360, 92, 740, 462);

            UpdateParamButtons();
            SetSource(source, silent: true);
            SetMethod(method, silent: true);
        }

        private void SetMethod(PairMethod m)
        {
            SetMethod(m, silent: false);
        }

        private void SetMethod(PairMethod m, bool silent)
        {
            method = m;
            if (!silent) StationFlowPlugin.CfgPairMethod.Value = (int)m;
            SetBtnColor(methodP2PBtn, m == PairMethod.P2P);
            SetBtnColor(methodPlanetBtn, m == PairMethod.PlanetRoute);
            SetBtnColor(methodAstroBtn, m == PairMethod.AstroRoute);
            if (!silent && owner != null)
            {
                switch (m)
                {
                    case PairMethod.P2P:
                        owner.SetStatus("配对方式：点对点（塔↔塔，精确到每一对塔）");
                        break;
                    case PairMethod.PlanetRoute:
                        owner.SetStatus("配对方式：星球航路（星球对+物品聚合；1 条航路覆盖两地间该物品的全部塔对）");
                        break;
                    default:
                        owner.SetStatus("配对方式：行星系航路（行星系对+物品聚合，粗粒度覆盖整片区域）");
                        break;
                }
            }
        }

        private void SetSource(PairSource s)
        {
            SetSource(s, silent: false);
        }

        private void SetSource(PairSource s, bool silent)
        {
            source = s;
            if (!silent) StationFlowPlugin.CfgPairSource.Value = (int)s;
            SetBtnColor(sourceNameBtn, s == PairSource.ByName);
            SetBtnColor(sourceSlotBtn, s == PairSource.BySlot);
            if (descText != null) descText.text = s == PairSource.ByName ? DescByName : DescBySlot;
            if (titleText != null) titleText.text = s == PairSource.ByName ? "规则配对 · 模式一（命名驱动）" : "规则配对 · 模式二（槽位直读）";
            if (!silent && owner != null)
            {
                owner.SetStatus(s == PairSource.ByName
                    ? "数据源：按名字（解析 [物品][角色]，需规范命名）"
                    : "数据源：按槽位（直读星际供需，无需命名，未命名塔同样参与）");
            }
        }

        private void UpdateParamButtons()
        {
            if (kButtons != null)
            {
                for (int i = 0; i < kButtons.Length; i++)
                    SetBtnColor(kButtons[i], KOptions[i] == topK);
            }
            if (ratioButtons != null)
            {
                for (int i = 0; i < ratioButtons.Length; i++)
                    SetBtnColor(ratioButtons[i], Mathf.Approximately(RatioOptions[i], minRatio));
            }
            if (crossBtn != null)
            {
                var label = crossBtn.GetComponentInChildren<Text>();
                if (label != null) label.text = allowCrossAstro ? "允许跨星系：是" : "允许跨星系：否";
                SetBtnColor(crossBtn, allowCrossAstro);
            }
        }

        private static void SetBtnColor(Button b, bool active)
        {
            var img = b != null ? b.GetComponent<Image>() : null;
            if (img != null) img.color = active ? UIFactory.ColBtnHighlight : UIFactory.ColBtn;
        }

        // ================= 页面刷新（由 MainPanel 驱动） =================

        /// <summary>页面显示时调用。</summary>
        public void Refresh()
        {
            if (statText != null && lastResult == null)
            {
                statText.text = owner != null && owner.AllRows.Count > 0
                    ? $"当前星区共 {owner.AllRows.Count} 个塔。设置参数后点「生成配对计划」。"
                    : "尚未生成计划。设置参数后点「生成配对计划」。";
            }
            UpdateAutoUI();
        }

        /// <summary>每帧（面板可见时）调用：进度条与按钮状态。</summary>
        public void Update()
        {
            var ex = Executor.Instance;
            if (ex == null) return;
            if (ex.IsRunning)
            {
                float p = ex.Progress;
                if (progressFill != null) progressFill.fillAmount = p;
                if (progressLabel != null) progressLabel.text = $"{ex.Stage} {(p * 100f):F0}%";
            }
            if (generateBtn != null) generateBtn.interactable = !ex.IsRunning;
            if (executeBtn != null) executeBtn.interactable = !ex.IsRunning && lastPlan != null && lastPlan.Actions.Count > 0;
            if (undoBtn != null) undoBtn.interactable = !ex.IsRunning && ex.UndoAvailable;

            // 自动巡检状态同步（字符串引用比较，变化时才更新）
            var ap = AutoPilot.Instance;
            if (ap != null && autoStatusText != null && ap.LastRunInfo != lastAutoInfo)
            {
                lastAutoInfo = ap.LastRunInfo;
                autoStatusText.text = ap.LastRunInfo;
            }
        }

        // ================= 自动巡检 =================

        private void ToggleAuto()
        {
            var ap = AutoPilot.Instance;
            if (ap == null) { owner.SetStatus("AutoPilot 未初始化"); return; }
            ap.Enabled = !ap.Enabled;
            StationFlowPlugin.CfgAutoEnabled.Value = ap.Enabled;
            UpdateAutoUI();
            owner.SetStatus(ap.Enabled
                ? $"自动巡检已开启：每 30 秒检查新建塔，变化时自动增量配对（每次最多 {StationFlowPlugin.CfgAutoMaxActions.Value} 条）"
                : "自动巡检已关闭");
        }

        private void UpdateAutoUI()
        {
            var ap = AutoPilot.Instance;
            bool on = ap != null && ap.Enabled;
            if (autoBtn != null)
            {
                var label = autoBtn.GetComponentInChildren<Text>();
                if (label != null) label.text = on ? "自动巡检：开" : "自动巡检：关";
                SetBtnColor(autoBtn, on);
            }
            if (autoStatusText != null && (ap == null || string.IsNullOrEmpty(ap.LastRunInfo)))
            {
                autoStatusText.text = on ? "等待检测新增塔…" : "";
            }
        }

        // ================= 生成计划 =================

        private void DoGenerate()
        {
            if (!StationOps.GameReady)
            {
                owner.SetStatus("游戏未就绪（请先进入存档）");
                return;
            }
            var ex = Executor.Instance;
            if (ex != null && ex.IsRunning)
            {
                owner.SetStatus("有执行任务进行中，请稍候…");
                return;
            }

            var rows = StationSnapshot.Build();
            var rule = new PairRule
            {
                Source = source,
                TopK = topK,
                MinSupplyRatio = minRatio,
                AllowCrossAstro = allowCrossAstro,
            };

            var sw = Stopwatch.StartNew();
            lastResult = PairingEngine.Build(rows, rule);
            lastPlan = PlanBuilder.FromPairs(lastResult, source, method, 0, $"配对(K={topK})", out lastRoutes);
            sw.Stop();

            RenderPreview();
            if (method == PairMethod.P2P)
            {
                statText.text = $"计划完成（{sw.ElapsedMilliseconds}ms）：共 {lastResult.Pairs.Count} 条，" +
                                $"新增 {lastResult.NewCount}、已存在 {lastResult.ExistsCount}、" +
                                $"提示 {lastResult.Notices.Count}、警告 {lastResult.Warnings.Count}";
                owner.SetStatus($"配对计划：新增 {lastResult.NewCount} 条，已存在 {lastResult.ExistsCount} 条（{sw.ElapsedMilliseconds}ms）");
            }
            else
            {
                int cover = 0, newKeys = 0, planetLevel = 0;
                foreach (var r in lastRoutes)
                {
                    cover += r.PairCount;
                    newKeys += r.ItemIds.Count - r.ExistItemCount;
                    if (r.PlanetLevel && method == PairMethod.AstroRoute) planetLevel++;
                }
                string degrade = planetLevel > 0
                    ? $"（其中 {planetLevel} 条为同恒星系内塔对，行星系航路无法表达、已降级为星球航路）" : "";
                statText.text = $"计划完成（{sw.ElapsedMilliseconds}ms）：{lastRoutes.Count} 条航路键（新增 {newKeys}）{degrade}、" +
                                $"覆盖 {cover} 对塔通信、提示 {lastResult.Notices.Count}、警告 {lastResult.Warnings.Count}";
                owner.SetStatus($"航路计划：{lastRoutes.Count} 条（新增键 {newKeys}）{(degrade.Length > 0 ? "，含同系降级" : "")}，覆盖 {cover} 对塔通信（{sw.ElapsedMilliseconds}ms）");
            }

            // 提示与警告入日志便于排查
            foreach (var n in lastResult.Notices) StationFlowPlugin.LogInfo($"[配对提示] {n}");
            foreach (var w in lastResult.Warnings) StationFlowPlugin.LogInfo($"[配对警告] {w}");
        }

        private void RenderPreview()
        {
            var r = lastResult;

            if (method != PairMethod.P2P && lastRoutes != null)
            {
                // ===== 航路预览 =====
                int cover = 0;
                foreach (var rt in lastRoutes) cover += rt.PairCount;
                previewTitle.text = $"航路预览（{(method == PairMethod.AstroRoute ? "行星系" : "星球")}航路 {lastRoutes.Count} 条，覆盖 {cover} 对塔通信；按覆盖数降序）";

                var rb = new StringBuilder();
                int showR = Mathf.Min(lastRoutes.Count, PreviewLimit);
                for (int i = 0; i < showR; i++)
                {
                    var rt = lastRoutes[i];
                    rb.Append(rt.AllExist ? "✓" : "＋").Append(' ')
                      .Append(Short(rt.AName)).Append(" ↔ ").Append(Short(rt.BName))
                      .Append("  ").Append(Trunc(rt.ItemText, 24))
                      .Append("  (").Append(rt.PairCount).Append("对塔)")
                      .Append('\n');
                }
                if (lastRoutes.Count == 0)
                    rb.Append("没有可聚合的配对。\n\n航路由塔对计划聚合而来——先用「点对点」模式生成计划，\n若有塔对可配，本模式即可把它们精简为星球/行星系级航路。");
                previewText.text = rb.ToString();
            }
            else
            {
                // ===== P2P 预览 =====
                previewTitle.text = $"配对预览（共 {r.Pairs.Count} 条，新增 {r.NewCount}；按距离升序" +
                                    (r.Pairs.Count > PreviewLimit ? $"，显示前 {PreviewLimit} 条）" : "）");

                var sb = new StringBuilder();
                int show = Mathf.Min(r.Pairs.Count, PreviewLimit);
                for (int i = 0; i < show; i++)
                {
                    var p = r.Pairs[i];
                    sb.Append(p.Exists ? "✓" : "＋").Append(' ')
                      .Append('#').Append(p.SupplyGid).Append('[').Append(Short(p.SupplyPlanet)).Append(']')
                      .Append(" → ")
                      .Append('#').Append(p.DemandGid).Append('[').Append(Short(p.DemandPlanet)).Append(']')
                      .Append("  ").Append(Trunc(p.ItemText, 20))
                      .Append("  d=").Append(FmtDist(p.Distance))
                      .Append('\n');
                }
                if (r.Pairs.Count == 0) sb.Append("没有找到可配对的组合。\n\n检查左下提示/警告，或切换数据源（模式一需规范命名；模式二直读槽位）。");
                previewText.text = sb.ToString();
            }

            RenderNotices(r);
        }

        /// <summary>提示（◆，物品级结构说明）+ 警告（·，塔级不一致）合并渲染。</summary>
        private void RenderNotices(PairingResult r)
        {
            var wb = new StringBuilder();
            int budget = WarnShowLimit;
            foreach (var n in r.Notices)
            {
                if (budget-- <= 0) break;
                wb.Append("◆ ").Append(n).Append('\n');
            }
            int wShow = Mathf.Min(r.Warnings.Count, Mathf.Max(0, budget));
            for (int i = 0; i < wShow; i++)
            {
                wb.Append("· ").Append(r.Warnings[i]).Append('\n');
            }
            int total = r.Notices.Count + r.Warnings.Count;
            if (total > WarnShowLimit) wb.Append("……共 ").Append(total).Append(" 条（详见日志）");
            warnText.text = wb.Length == 0 ? "无" : wb.ToString();
        }

        // ================= 执行 / 撤销 =================

        private void DoExecute()
        {
            if (lastPlan == null || lastPlan.Actions.Count == 0)
            {
                owner.SetStatus("没有可执行的新增配对——请先「生成配对计划」，或所有配对均已存在");
                return;
            }
            var ex = Executor.Instance;
            if (ex == null) { owner.SetStatus("Executor 未初始化"); return; }
            if (ex.IsRunning) { owner.SetStatus("已有执行任务进行中"); return; }

            ex.OnCompleted += OnExecCompleted;
            if (!ex.Execute(lastPlan))
            {
                ex.OnCompleted -= OnExecCompleted;
                owner.SetStatus("执行未启动（游戏未就绪或任务冲突）");
                return;
            }
            owner.SetStatus($"开始执行 {lastPlan.Actions.Count} 条新增配对…");
        }

        private void DoUndo()
        {
            var ex = Executor.Instance;
            if (ex == null || ex.IsRunning) { owner.SetStatus("当前无法撤销"); return; }
            if (!ex.UndoAvailable) { owner.SetStatus("没有可撤销的执行记录"); return; }

            ex.OnCompleted += OnExecCompleted;
            if (!ex.Undo())
            {
                ex.OnCompleted -= OnExecCompleted;
                owner.SetStatus("撤销未启动");
                return;
            }
            owner.SetStatus("正在撤销上次执行…");
        }

        private void OnExecCompleted()
        {
            var ex = Executor.Instance;
            if (ex != null) ex.OnCompleted -= OnExecCompleted;

            var r = ex != null ? ex.LastReport : null;
            string summary = r != null ? r.Summary : "完成";
            if (r != null)
            {
                if (progressFill != null) progressFill.fillAmount = 1f;
                if (progressLabel != null) progressLabel.text = summary;
                foreach (var e in r.Errors) StationFlowPlugin.LogWarn($"[执行错误] {e}");
            }

            owner.RefreshData();
            DoGenerate(); // 重算：原新增边现在应显示 ✓ 已存在
            owner.SetStatus("执行完成：" + summary); // 放在最后（DoGenerate 会覆盖状态栏）
        }

        // ================= 格式化辅助 =================

        private static string Short(string planet)
        {
            if (string.IsNullOrEmpty(planet)) return "";
            int i = planet.LastIndexOf('-');
            string s = i >= 0 ? planet.Substring(i + 1).Trim() : planet;
            return s.Length <= 8 ? s : s.Substring(0, 8);
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private static string FmtDist(double d)
        {
            if (double.IsInfinity(d) || d >= double.MaxValue) return "?";
            if (d >= 1e9) return (d / 1e9).ToString("F2") + "G";
            if (d >= 1e6) return (d / 1e6).ToString("F2") + "M";
            if (d >= 1e3) return (d / 1e3).ToString("F1") + "k";
            return d.ToString("F0");
        }
    }
}
