using System.Collections.Generic;
using StationFlow.VanillaBridge;

namespace StationFlow.Data
{
    /// <summary>槽位信息（快照，无游戏引用）。</summary>
    public class SlotInfo
    {
        public int Index;
        public int ItemId;
        public string ItemName;
        public int LocalLogic;   // ELogisticStorage 的 int 值：0=None / 1=Supply / 2=Demand
        public int RemoteLogic;
        public int Count;
        public int Max;

        /// <summary>星际逻辑的官方文本（对齐 UIStationStorage.GetLogisticText）。</summary>
        public string RemoteLogicText
        {
            get
            {
                switch (RemoteLogic)
                {
                    case 1: return "星际供应";
                    case 2: return "星际需求";
                    default: return "星际仓储";
                }
            }
        }
    }

    /// <summary>物流塔只读快照行（不含游戏对象引用，可安全跨帧持有）。</summary>
    public class StationRow
    {
        public int Gid;
        public int EntityId;
        public int PlanetId;
        public string PlanetName;
        public string GalaxyName;       // 恒星系名（如"母星系"）
        public string ShortPlanetName;  // 短行星名（如"IV号星"）
        public string Name;          // 物流塔名字（可能为空）
        public bool IsStellar;
        public bool IsCollector;
        public int Behavior;         // ERemoteRoutePriority 的 int 值：1=Ignore 2=Prioritize 3=Only 4=Designated
        public long GroupMask;
        public int MainItemId;
        public string MainItemName;
        public int PairTotal;
        public int PairP2P;
        public int PairPlanetRoute;
        public int PairAstroRoute;
        public int PairGroup;
        public int PairFallback;
        public int P2PRoutes;        // 该塔的 P2P 路由数（station2stationRoutes 中含它的 key）
        public string Role;          // 供 / 需 / 供需 / -
        public List<SlotInfo> Slots;

        public bool HasName { get { return !string.IsNullOrEmpty(Name); } }

        public string BehaviorName
        {
            get
            {
                switch (Behavior)
                {
                    case 1: return "Ignore";
                    case 2: return "Prioritize";
                    case 3: return "Only";
                    case 4: return "Designated";
                    default: return Behavior.ToString();
                }
            }
        }
    }

    /// <summary>快照构建：主线程调用（读取游戏对象），产出纯数据行。</summary>
    public static class StationSnapshot
    {
        public static List<StationRow> Build()
        {
            var list = new List<StationRow>();
            if (!StationOps.GameReady) return list;

            // 一次遍历构建 gid → P2P 路由数（避免逐塔全表扫描 O(N×M)）
            var p2pCounts = StationOps.GetP2PRouteCounts();

            foreach (var st in StationOps.AllStations())
            {
                int p2pCount;
                p2pCounts.TryGetValue(st.gid, out p2pCount);
                var row = new StationRow
                {
                    Gid = st.gid,
                    EntityId = st.entityId,
                    PlanetId = st.planetId,
                    PlanetName = StationOps.PlanetName(st.planetId),
                    GalaxyName = StationOps.GalaxyName(st.planetId),
                    ShortPlanetName = StationOps.ShortPlanetName(st.planetId),
                    Name = StationOps.GetName(st),
                    IsStellar = st.isStellar,
                    IsCollector = st.isCollector,
                    Behavior = (int)st.routePriority,
                    GroupMask = st.remoteGroupMask,
                    P2PRoutes = p2pCount,
                    Slots = new List<SlotInfo>(),
                };

                // 配对桶计数
                var off = st.remotePairOffsets;
                if (off != null && off.Length >= 7)
                {
                    row.PairTotal = off[6];
                    row.PairP2P = off[2] - off[1];
                    row.PairPlanetRoute = off[3] - off[2];
                    row.PairAstroRoute = off[4] - off[3];
                    row.PairGroup = off[5] - off[4];
                    row.PairFallback = off[6] - off[5];
                }

                // 槽位 + 角色归纳 + 主物品
                bool hasSupply = false, hasDemand = false;
                if (st.storage != null)
                {
                    for (int i = 0; i < st.storage.Length; i++)
                    {
                        var s = st.storage[i];
                        if (s.itemId <= 0) continue;
                        var info = new SlotInfo
                        {
                            Index = i,
                            ItemId = s.itemId,
                            ItemName = StationOps.ItemName(s.itemId),
                            LocalLogic = (int)s.localLogic,
                            RemoteLogic = (int)s.remoteLogic,
                            Count = s.count,
                            Max = s.max,
                        };
                        row.Slots.Add(info);
                        if (row.MainItemId == 0)
                        {
                            row.MainItemId = s.itemId;
                            row.MainItemName = info.ItemName;
                        }
                        // 供需归纳：严格按星际（远程）逻辑；星际仓储（None）不算供需
                        int rl = info.RemoteLogic;
                        if (rl == (int)ELogisticStorage.Supply) hasSupply = true;
                        else if (rl == (int)ELogisticStorage.Demand) hasDemand = true;
                    }
                }
                row.Role = hasSupply && hasDemand ? "供需" : hasSupply ? "供" : hasDemand ? "需" : "-";

                list.Add(row);
            }
            return list;
        }
    }
}
