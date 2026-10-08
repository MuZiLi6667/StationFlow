using System;
using System.Collections.Generic;
using System.Text;
using StationFlow.Data;
using StationFlow.Exec;
using StationFlow.PluginCore;
using UnityEngine;
using UnityEngine.UI;

namespace StationFlow.UI
{
    /// <summary>
    /// 「批量工具」页签（模式三）：对总览页选中的塔批量操作——
    /// 拓扑配对（Ring/Chain/Star/AllToAll）+ 批量设行为 / 批量分组。
    /// 全部走事务执行器（分帧 + 单次刷新 + 可撤销）。
    /// </summary>
    public class BatchTab
    {
        private enum TopoMode { Ring, Chain, Star, AllToAll }

        private RectTransform root;
        private MainPanel owner;

        private Text selText;
        private Text topoStat;
        private Text topoPreview;
        private InputField groupInput;
        private Text statusText;

        private List<PlanAction> topoPlan;   // 当前拓扑预览生成的边

        public void Init(RectTransform parent, MainPanel owner)
        {
            this.owner = owner;
            root = UIFactory.CreateNode(parent, "BatchTab");
            UIFactory.Stretch(root);
            BuildUI();
        }

        // ================= UI =================

        private void BuildUI()
        {
            float y = 8;
            var title = UIFactory.CreateText(root, "T", "批量工具 · 对「总览与命名」页选中的塔生效", 15, TextAnchor.MiddleLeft, UIFactory.ColAccent);
            UIFactory.PlaceTopLeft(title.rectTransform, 12, y, 600, 24); y += 30;

            selText = UIFactory.CreateText(root, "Sel", "未选中任何塔——请先到「总览与命名」页选择（Ctrl/Shift 多选）。", 13, TextAnchor.MiddleLeft, UIFactory.ColText);
            UIFactory.PlaceTopLeft(selText.rectTransform, 12, y, 1080, 22); y += 32;

            // ---- 拓扑配对 ----
            var sec1 = UIFactory.CreateText(root, "S1", "── 拓扑配对（按选中顺序生成 P2P 边；同星球自动跳过）──", 13, TextAnchor.MiddleLeft, UIFactory.ColAccent);
            UIFactory.PlaceTopLeft(sec1.rectTransform, 12, y, 800, 20); y += 26;

            var desc = UIFactory.CreateText(root, "D",
                "Ring 环线（首尾相连）· Chain 链式 · Star 星形（第一个为心）· AllToAll 全连（边数 = n×(n-1)/2，多选时慎用）",
                12, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(desc.rectTransform, 12, y, 1080, 18); y += 22;

            float bx = 12;
            AddTopoButton("Ring 环线", TopoMode.Ring, ref bx, y);
            AddTopoButton("Chain 链式", TopoMode.Chain, ref bx, y);
            AddTopoButton("Star 星形", TopoMode.Star, ref bx, y);
            AddTopoButton("AllToAll 全连", TopoMode.AllToAll, ref bx, y);
            y += 32;

            topoStat = UIFactory.CreateText(root, "TS", "", 12, TextAnchor.MiddleLeft, UIFactory.ColWarn);
            UIFactory.PlaceTopLeft(topoStat.rectTransform, 12, y, 1080, 18); y += 20;

            topoPreview = UIFactory.CreateText(root, "TP", "点上面的按钮生成配对预览。", 12, TextAnchor.UpperLeft, UIFactory.ColText);
            topoPreview.horizontalOverflow = HorizontalWrapMode.Wrap;
            topoPreview.verticalOverflow = VerticalWrapMode.Truncate;
            UIFactory.PlaceTopLeft(topoPreview.rectTransform, 12, y, 1080, 92); y += 98;

            var execTopoBtn = UIFactory.CreateButton(root, "ET", "执行配对", 13, DoExecuteTopo);
            UIFactory.PlaceTopLeft((RectTransform)execTopoBtn.transform, 12, y, 120, 28); y += 42;

            // ---- 批量设置 ----
            var sec2 = UIFactory.CreateText(root, "S2", "── 批量设置（立即应用到选中塔，可撤销）──", 13, TextAnchor.MiddleLeft, UIFactory.ColAccent);
            UIFactory.PlaceTopLeft(sec2.rectTransform, 12, y, 800, 20); y += 26;

            var bLabel = UIFactory.CreateText(root, "BL", "行为：", 13, TextAnchor.MiddleLeft, UIFactory.ColText);
            UIFactory.PlaceTopLeft(bLabel.rectTransform, 12, y, 52, 24);
            bx = 64;
            AddBehaviorButton("忽略", 1, ref bx, y);
            AddBehaviorButton("优先", 2, ref bx, y);
            AddBehaviorButton("仅配对", 3, ref bx, y);
            AddBehaviorButton("指定", 4, ref bx, y);
            y += 32;

            var gLabel = UIFactory.CreateText(root, "GL", "分组（1~30）：", 13, TextAnchor.MiddleLeft, UIFactory.ColText);
            UIFactory.PlaceTopLeft(gLabel.rectTransform, 12, y, 104, 24);
            groupInput = UIFactory.CreateInputField(root, "GI", "组号", 13);
            UIFactory.PlaceTopLeft((RectTransform)groupInput.transform, 118, y, 56, 24);
            var joinBtn = UIFactory.CreateButton(root, "Join", "加入组", 12, () => DoGroup(true));
            UIFactory.PlaceTopLeft((RectTransform)joinBtn.transform, 182, y, 76, 24);
            var leaveBtn = UIFactory.CreateButton(root, "Leave", "移出组", 12, () => DoGroup(false));
            UIFactory.PlaceTopLeft((RectTransform)leaveBtn.transform, 264, y, 76, 24);
            y += 36;

            statusText = UIFactory.CreateText(root, "St", "就绪", 12, TextAnchor.MiddleLeft, UIFactory.ColTextDim);
            UIFactory.PlaceTopLeft(statusText.rectTransform, 12, y, 1080, 20);
        }

        private void AddTopoButton(string label, TopoMode mode, ref float x, float y)
        {
            var btn = UIFactory.CreateButton(root, "TM" + mode, label, 12, () => BuildTopoPreview(mode));
            UIFactory.PlaceTopLeft((RectTransform)btn.transform, x, y, 122, 26);
            x += 126;
        }

        private void AddBehaviorButton(string label, int behavior, ref float x, float y)
        {
            var btn = UIFactory.CreateButton(root, "BH" + behavior, label, 12, () => DoBehavior(behavior));
            UIFactory.PlaceTopLeft((RectTransform)btn.transform, x, y, 66, 24);
            x += 70;
        }

        // ================= 页面刷新 =================

        public void Refresh()
        {
            UpdateSelText();
        }

        public void Update() { }

        private void UpdateSelText()
        {
            var table = owner != null ? owner.Table : null;
            if (table == null || selText == null) return;
            int count = table.SelectedCount;
            if (count == 0)
            {
                selText.text = "未选中任何塔——请先到「总览与命名」页选择（Ctrl/Shift 多选）。";
                return;
            }
            var rows = table.SelectedRows;
            var sb = new StringBuilder($"已选 {count} 塔：");
            for (int i = 0; i < Mathf.Min(rows.Count, 12); i++)
            {
                sb.Append(" #").Append(rows[i].Gid);
                if (i == 11 && rows.Count > 12) sb.Append(" …");
            }
            selText.text = sb.ToString();
        }

        private List<StationRow> GetSelectedRows()
        {
            var table = owner != null ? owner.Table : null;
            return table != null ? table.SelectedRows : new List<StationRow>();
        }

        // ================= 拓扑配对 =================

        private void BuildTopoPreview(TopoMode mode)
        {
            var sel = GetSelectedRows();
            if (sel.Count < 2)
            {
                topoPlan = null;
                topoStat.text = "至少需要选中 2 个塔。";
                topoPreview.text = "";
                return;
            }

            var byGid = new Dictionary<int, StationRow>();
            var gids = new List<int>();
            foreach (var r in sel)
            {
                byGid[r.Gid] = r;
                gids.Add(r.Gid);
            }

            var actions = new List<PlanAction>();
            var seen = new HashSet<long>();
            int sameSkip = 0;

            Action<int, int> add = (ga, gb) =>
            {
                if (ga == gb) return;
                var a = byGid[ga];
                var b = byGid[gb];
                if (a.PlanetId == b.PlanetId) { sameSkip++; return; } // 同星球交给行星内物流
                int lo = Math.Min(ga, gb), hi = Math.Max(ga, gb);
                long key = (long)lo | ((long)hi << 32);
                if (!seen.Add(key)) return;
                actions.Add(new PlanAction { Type = PlanActionType.AddP2P, Gid = lo, GidB = hi });
            };

            int n = gids.Count;
            string modeName;
            switch (mode)
            {
                case TopoMode.Ring:
                    modeName = "Ring 环线";
                    for (int i = 0; i < n; i++) add(gids[i], gids[(i + 1) % n]);
                    break;
                case TopoMode.Chain:
                    modeName = "Chain 链式";
                    for (int i = 0; i + 1 < n; i++) add(gids[i], gids[i + 1]);
                    break;
                case TopoMode.Star:
                    modeName = "Star 星形";
                    for (int i = 1; i < n; i++) add(gids[0], gids[i]);
                    break;
                default:
                    modeName = "AllToAll 全连";
                    for (int i = 0; i < n; i++)
                        for (int j = i + 1; j < n; j++) add(gids[i], gids[j]);
                    break;
            }

            topoPlan = actions;
            string warn = (mode == TopoMode.AllToAll && n > 12) ? " ⚠ 边数较多" : "";
            topoStat.text = $"{modeName}：{n} 塔 → {actions.Count} 条边"
                + (sameSkip > 0 ? $"（跳过同星球 {sameSkip} 条）" : "") + warn;

            var sb = new StringBuilder();
            int show = Mathf.Min(actions.Count, 8);
            for (int i = 0; i < show; i++)
            {
                var a = actions[i];
                sb.Append('#').Append(a.Gid).Append(' ').Append(NameOf(byGid[a.Gid]))
                  .Append("  ↔  #").Append(a.GidB).Append(' ').Append(NameOf(byGid[a.GidB]))
                  .Append('\n');
            }
            if (actions.Count > show) sb.Append("… 共 ").Append(actions.Count).Append(" 条\n");
            if (actions.Count == 0) sb.Append("没有可生成的边（全部为同星球配对，已跳过）。");
            topoPreview.text = sb.ToString();

            SetStatus($"已生成配对预览：{actions.Count} 条边，点「执行配对」写入。");
        }

        private void DoExecuteTopo()
        {
            if (topoPlan == null || topoPlan.Count == 0)
            {
                SetStatus("请先点拓扑按钮生成配对预览。");
                return;
            }
            RunPlan(new ExecutionPlan { Title = "拓扑配对", Actions = topoPlan });
        }

        // ================= 批量设置 =================

        private void DoBehavior(int behavior)
        {
            var sel = GetSelectedRows();
            if (sel.Count == 0) { SetStatus("未选中塔。"); return; }

            var plan = new ExecutionPlan { Title = $"批量设行为({BehaviorName(behavior)})" };
            foreach (var r in sel)
            {
                plan.Actions.Add(new PlanAction { Type = PlanActionType.SetBehavior, Gid = r.Gid, IntValue = behavior });
            }
            RunPlan(plan);
        }

        private void DoGroup(bool join)
        {
            var sel = GetSelectedRows();
            if (sel.Count == 0) { SetStatus("未选中塔。"); return; }
            int idx;
            if (groupInput == null || !int.TryParse(groupInput.text.Trim(), out idx) || idx < 1 || idx > 30)
            {
                SetStatus("请输入组号（1~30）。");
                return;
            }

            int bit = idx - 1;
            var plan = new ExecutionPlan { Title = $"批量{(join ? "加入" : "移出")}分组{idx}" };
            foreach (var r in sel)
            {
                plan.Actions.Add(new PlanAction { Type = PlanActionType.SetGroupBit, Gid = r.Gid, IntValue = bit, BoolValue = join });
            }
            RunPlan(plan);
        }

        // ================= 执行 =================

        private void RunPlan(ExecutionPlan plan)
        {
            var ex = Executor.Instance;
            if (ex == null) { SetStatus("Executor 未初始化。"); return; }
            if (ex.IsRunning) { SetStatus("已有执行任务进行中，请稍候…"); return; }

            ex.OnCompleted += OnExecCompleted;
            if (!ex.Execute(plan))
            {
                ex.OnCompleted -= OnExecCompleted;
                SetStatus("执行未启动（游戏未就绪或任务冲突）。");
                return;
            }
            SetStatus($"执行中：{plan.Title}（{plan.Actions.Count} 条）…");
        }

        private void OnExecCompleted()
        {
            var ex = Executor.Instance;
            if (ex != null) ex.OnCompleted -= OnExecCompleted;
            var r = ex != null ? ex.LastReport : null;
            SetStatus(r != null ? "完成：" + r.Summary : "完成");
            if (owner != null) owner.RefreshData();
        }

        // ================= 辅助 =================

        private void SetStatus(string msg)
        {
            if (statusText != null) statusText.text = msg;
            if (owner != null) owner.SetStatus(msg);
            StationFlowPlugin.LogInfo($"[批量] {msg}");
        }

        private static string BehaviorName(int b)
        {
            switch (b)
            {
                case 1: return "忽略";
                case 2: return "优先";
                case 3: return "仅配对";
                default: return "指定";
            }
        }

        private static string NameOf(StationRow r)
        {
            if (r == null) return "";
            string n = r.HasName ? r.Name : "未命名";
            return n.Length <= 14 ? n : n.Substring(0, 14) + "…";
        }
    }
}
