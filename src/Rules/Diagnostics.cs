using System.Collections.Generic;
using StationFlow.Data;
using StationFlow.VanillaBridge;

namespace StationFlow.Rules
{
    public enum DiagLevel { Danger = 0, Warn = 1, Info = 2 }

    public class DiagItem
    {
        public DiagLevel Level;
        public string Title;
        public string Detail;
    }

    public class DiagReport
    {
        public List<DiagItem> Items = new List<DiagItem>();
        public string Overview = "";

        public int DangerCount
        {
            get { int n = 0; foreach (var it in Items) if (it.Level == DiagLevel.Danger) n++; return n; }
        }

        public int WarnCount
        {
            get { int n = 0; foreach (var it in Items) if (it.Level == DiagLevel.Warn) n++; return n; }
        }
    }

    /// <summary>
    /// 离线诊断（v1 无需 Hook）：检查"配置了但不工作"的高危项。
    /// 检查清单：
    ///  1. 停工风险——行为=仅配对/指定 但无 P2P、无分组、星球无航路（官方机制：无兜底=停工）
    ///  2. 断供物品——有星际需求塔但没有星际供应塔
    ///  3. 供应偏紧——需求总缺口远超供应总库存
    /// </summary>
    public static class Diagnostics
    {
        private const int MaxItems = 200;

        public static DiagReport Run(List<StationRow> rows)
        {
            var report = new DiagReport();
            if (rows == null || rows.Count == 0)
            {
                report.Overview = "无数据（请先进入存档并刷新）。";
                return report;
            }

            // ===== 概览统计 =====
            int named = 0, bIgnore = 0, bPrio = 0, bOnly = 0, bDesig = 0;
            foreach (var r in rows)
            {
                if (r.HasName) named++;
                switch (r.Behavior)
                {
                    case 1: bIgnore++; break;
                    case 3: bOnly++; break;
                    case 4: bDesig++; break;
                    default: bPrio++; break;
                }
            }
            report.Overview = $"塔 {rows.Count}（已命名 {named}）· P2P {StationOps.P2PRouteCount} · 航路 {StationOps.RouteCount}" +
                              $" · 行为：忽略{bIgnore} 优先{bPrio} 仅配对{bOnly} 指定{bDesig}";

            // ===== 1. 停工风险 =====
            // 判定链（对齐官方消费端读取逻辑）：行为=仅配对/指定时只读配对桶、无兜底。
            // P2P / 分组是塔级（官方 P2P 与分组无物品维度）→ 塔有任何一条即放行；
            // 航路是物品级的 → 塔的全部星际供需物品必须逐一被"该物品的"航路覆盖才放行。
            Dictionary<int, HashSet<int>> routePlanetsByItem, routeAstrosByItem;
            StationOps.CollectRouteEndpointsByItem(out routePlanetsByItem, out routeAstrosByItem);
            foreach (var r in rows)
            {
                if (r.Behavior < 3) continue;                // 只查 仅配对/指定
                if (r.P2PRoutes > 0) continue;               // 有 P2P（塔级）
                if (r.GroupMask != 0) continue;              // 有分组（塔级）

                bool anySlot = false, allCovered = true;
                if (r.Slots != null)
                {
                    foreach (var s in r.Slots)
                    {
                        if (s.RemoteLogic != 1 && s.RemoteLogic != 2) continue;
                        anySlot = true;
                        if (!StationOps.IsPlanetCoveredByRouteForItem(r.PlanetId, s.ItemId,
                                routePlanetsByItem, routeAstrosByItem))
                        {
                            allCovered = false;
                            break;
                        }
                    }
                }
                if (!anySlot) continue;       // 无星际供需槽位（纯仓储）→ 不存在停工问题
                if (allCovered) continue;     // 全部物品均有（对应物品的）航路覆盖

                Add(report, DiagLevel.Danger,
                    $"#{r.Gid}「{NameOf(r)}」可能停工：行为=「{r.BehaviorName}」（仅按配对运输），但没有任何 P2P / 分组，部分物品也缺少航路",
                    "到「规则配对」页生成并执行配对；或把该塔行为改回「优先」由官方兜底接管");
                if (report.Items.Count >= MaxItems) break;
            }

            // ===== 2/3. 供需结构 =====
            var supplyTowers = new Dictionary<int, int>();
            var demandTowers = new Dictionary<int, int>();
            var supplyStock = new Dictionary<int, long>();
            var demandGap = new Dictionary<int, long>();
            foreach (var r in rows)
            {
                if (r.Slots == null) continue;
                foreach (var s in r.Slots)
                {
                    if (s.RemoteLogic == 1)
                    {
                        supplyTowers[s.ItemId] = Get(supplyTowers, s.ItemId) + 1;
                        supplyStock[s.ItemId] = Get(supplyStock, s.ItemId) + s.Count;
                    }
                    else if (s.RemoteLogic == 2)
                    {
                        demandTowers[s.ItemId] = Get(demandTowers, s.ItemId) + 1;
                        long gap = (long)s.Max - s.Count;
                        if (gap > 0) demandGap[s.ItemId] = Get(demandGap, s.ItemId) + gap;
                    }
                }
            }

            foreach (var kv in demandTowers)
            {
                if (report.Items.Count >= MaxItems) break;
                int itemId = kv.Key;
                int dCount = kv.Value;
                int sCount = Get(supplyTowers, itemId);
                string iname = StationOps.ItemName(itemId);

                if (sCount == 0)
                {
                    Add(report, DiagLevel.Danger,
                        $"「{iname}」有 {dCount} 个星际需求塔，但全星区没有星际供应塔",
                        "检查供应塔是否把该物品设为「星际供应」，或产线是否已停产");
                }
                else
                {
                    long stock = Get(supplyStock, itemId);
                    long gap = Get(demandGap, itemId);
                    if (gap > 0 && stock * 3 < gap)
                    {
                        Add(report, DiagLevel.Warn,
                            $"「{iname}」供应偏紧：需求缺口 {Fmt(gap)} vs 供应库存 {Fmt(stock)}（供 {sCount} 塔 / 需 {dCount} 塔）",
                            "考虑扩建产线，或让就近的星际仓储塔转为「星际供应」");
                    }
                }
            }

            // 排序：高危在前
            report.Items.Sort((a, b) => a.Level.CompareTo(b.Level));
            return report;
        }

        // ---- 辅助 ----

        private static void Add(DiagReport r, DiagLevel level, string title, string detail)
        {
            if (r.Items.Count >= MaxItems) return;
            r.Items.Add(new DiagItem { Level = level, Title = title, Detail = detail });
        }

        private static int Get(Dictionary<int, int> d, int k)
        {
            int v; return d.TryGetValue(k, out v) ? v : 0;
        }

        private static long Get(Dictionary<int, long> d, int k)
        {
            long v; return d.TryGetValue(k, out v) ? v : 0;
        }

        private static string NameOf(StationRow r)
        {
            string n = r.HasName ? r.Name : "未命名";
            return n.Length <= 16 ? n : n.Substring(0, 16) + "…";
        }

        private static string Fmt(long v)
        {
            if (v >= 1000000) return (v / 1000000.0).ToString("F1") + "M";
            if (v >= 1000) return (v / 1000.0).ToString("F1") + "k";
            return v.ToString();
        }
    }
}
