using System.Collections.Generic;
using StationFlow.VanillaBridge;

namespace StationFlow.Data
{
    /// <summary>矩阵单元：某星球 × 某物品的供需状态与流量。</summary>
    public class TopoCell
    {
        public int PlanetId;
        public int ItemId;
        public int SupplyTowers, DemandTowers, StorageTowers;
        public long SupplyCount;    // 供应塔该物品库存合计
        public long DemandGap;      // 需求塔该物品缺口合计（max - count）
        public long InputPerMin;    // 星际输入流量（约最近 1 分钟）
        public long OutputPerMin;   // 星际输出流量

        public bool HasActivity { get { return InputPerMin > 0 || OutputPerMin > 0; } }
    }

    /// <summary>矩阵行（一个物品）。</summary>
    public class TopoRow
    {
        public int ItemId;
        public string ItemName;
        public long TotalFlow;      // 全星区总流量（排序用）

        public TopoCell[] Cells;    // 与 PlanetIds 对齐
    }

    /// <summary>
    /// 物流网拓扑数据：星球（列）× 物品（行）矩阵。
    /// 数据源：StationSnapshot（塔槽供需）+ 游戏流量统计（factoryTrafficPool）。
    /// </summary>
    public class TopologyModel
    {
        public List<int> PlanetIds = new List<int>();
        public List<string> PlanetNames = new List<string>();
        public List<TopoRow> Rows = new List<TopoRow>();
        public long TotalFlowAll;

        /// <summary>主线程构建。</summary>
        public static TopologyModel Build(List<StationRow> rows)
        {
            var model = new TopologyModel();
            if (rows == null || rows.Count == 0) return model;

            // 1. 收集列（有塔的星球，按 id 升序）
            var planetSet = new HashSet<int>();
            foreach (var r in rows) planetSet.Add(r.PlanetId);
            model.PlanetIds.AddRange(planetSet);
            model.PlanetIds.Sort();
            var planetIndex = new Dictionary<int, int>();
            for (int i = 0; i < model.PlanetIds.Count; i++)
            {
                planetIndex[model.PlanetIds[i]] = i;
                model.PlanetNames.Add(Trunc(StationOps.ShortPlanetName(model.PlanetIds[i]), 6)
                    + "\n" + Trunc(StationOps.GalaxyName(model.PlanetIds[i]), 6));
            }

            // 2. 聚合单元（星球 × 物品）
            var cellMap = new Dictionary<long, TopoCell>();
            foreach (var r in rows)
            {
                if (r.Slots == null) continue;
                int pi = planetIndex[r.PlanetId];
                foreach (var slot in r.Slots)
                {
                    long key = ((long)pi << 32) | (uint)slot.ItemId;
                    TopoCell c;
                    if (!cellMap.TryGetValue(key, out c))
                    {
                        c = new TopoCell { PlanetId = r.PlanetId, ItemId = slot.ItemId };
                        cellMap[key] = c;
                    }
                    switch (slot.RemoteLogic)
                    {
                        case 1:
                            c.SupplyTowers++;
                            c.SupplyCount += slot.Count;
                            break;
                        case 2:
                            c.DemandTowers++;
                            long gap = (long)slot.Max - slot.Count;
                            c.DemandGap += gap > 0 ? gap : 0;
                            break;
                        default:
                            c.StorageTowers++;
                            break;
                    }
                }
            }

            // 3. 收集行（有过供需储的物品）+ 读取流量
            var itemSet = new HashSet<int>();
            foreach (var c in cellMap.Values) itemSet.Add(c.ItemId);

            foreach (var itemId in itemSet)
            {
                var row = new TopoRow
                {
                    ItemId = itemId,
                    ItemName = StationOps.ItemName(itemId),
                    Cells = new TopoCell[model.PlanetIds.Count],
                };
                long flow = 0;
                for (int pi = 0; pi < model.PlanetIds.Count; pi++)
                {
                    long key = ((long)pi << 32) | (uint)itemId;
                    TopoCell c;
                    if (!cellMap.TryGetValue(key, out c)) continue;
                    StationOps.GetTraffic(c.PlanetId, itemId,
                        out c.InputPerMin, out c.OutputPerMin, out _, out _);
                    flow += c.InputPerMin + c.OutputPerMin;
                    row.Cells[pi] = c;
                }
                row.TotalFlow = flow;
                model.TotalFlowAll += flow;
                model.Rows.Add(row);
            }

            // 4. 排序：有流量的在前（流量降序），其余按物品 id
            model.Rows.Sort((a, b) =>
            {
                if (a.TotalFlow != b.TotalFlow) return b.TotalFlow.CompareTo(a.TotalFlow);
                return a.ItemId.CompareTo(b.ItemId);
            });
            return model;
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
