using System;
using System.Collections.Generic;
using StationFlow.Data;
using StationFlow.VanillaBridge;

namespace StationFlow.Rules
{
    /// <summary>数据源：供需池的构建方式。</summary>
    public enum PairSource
    {
        ByName, // 模式一：解析塔名字 [物品][角色]（需命名规范）
        BySlot, // 模式二：直读槽位的星际供需（rl=1/2，无需命名）
    }

    /// <summary>配对方式：用哪种官方机制表达配对。</summary>
    public enum PairMethod
    {
        P2P,         // 点对点（塔↔塔）
        PlanetRoute, // 星球航路（星球对+物品）
        AstroRoute,  // 行星系航路（行星系对+物品）
    }

    /// <summary>配对参数。</summary>
    public class PairRule
    {
        public PairSource Source = PairSource.ByName;
        public int TopK = 3;                // 每个需求塔最多配对 K 个供应塔
        public float MinSupplyRatio = 0.2f; // 供应能力 < 需求容量 × 该比例 → 跳过（防雨露均沾）
        public bool AllowCrossAstro = true; // 允许跨行星系配对
    }

    /// <summary>一条配对计划（塔对粒度；同一塔对的多个物品合并展示）。</summary>
    public class PairPlanItem
    {
        public int GidA, GidB;                 // 规范化塔对（小<大），对应 P2P key
        public int SupplyGid, DemandGid;       // 方向（供 → 需）
        public int SupplyPlanetId, DemandPlanetId;
        public List<int> ItemIds = new List<int>();
        public List<string> ItemNames = new List<string>();
        public double Distance;                // 宇宙距离（相对排序用）
        public bool Exists;                    // 已存在（不计入新增）
        public string SupplyName, DemandName;  // 展示用（名字截断）
        public string SupplyPlanet, DemandPlanet;

        public string ItemText { get { return string.Join(",", ItemNames.ToArray()); } }
    }

    /// <summary>一条航路计划（星球对/行星系对 + 物品集合；覆盖多对塔通信）。</summary>
    public class RoutePlanItem
    {
        public int AId, BId;                   // 星球 id（星球航路）或行星系 id（行星系航路）
        public bool PlanetLevel;               // true=星球级航路（行星系模式下同系塔对自动降级）
        public List<int> ItemIds = new List<int>();
        public List<string> ItemNames = new List<string>();
        public int PairCount;                  // 覆盖的塔对通信数
        public bool AllExist;                  // 全部物品键均已存在
        public int ExistItemCount;             // 已存在的物品键数
        public string AName, BName;            // 展示名

        public string ItemText { get { return string.Join(",", ItemNames.ToArray()); } }
    }

    /// <summary>配对结果：计划边 + 一致性警告 + 结构提示。</summary>
    public class PairingResult
    {
        public List<PairPlanItem> Pairs = new List<PairPlanItem>();
        public List<string> Warnings = new List<string>(); // 名字/槽位不一致（塔级）
        public List<string> Notices = new List<string>();  // 供需结构提示（物品级，如单供应源）

        public int NewCount
        {
            get { int n = 0; foreach (var p in Pairs) if (!p.Exists) n++; return n; }
        }

        public int ExistsCount { get { return Pairs.Count - NewCount; } }
    }

    /// <summary>
    /// 模式一核心：按「名字声明的物品+角色」对全星区塔做供需配对。
    ///
    /// 语义（严格按星际/远程逻辑，含官方机制考量）：
    ///  - 供/需 = remoteLogic 的 Supply/Demand；星际仓储（None）不参与供需；本地逻辑不参与。
    ///  - 官方机制：任何跨星球供/需塔对**默认就有"兜底配对"**（行为=优先时生效），
    ///    因此**单供应源物品无需 P2P**（所有需求塔自动共享该源）——跳过并提示。
    ///    例外：需求塔行为=仅配对/指定（无兜底，不配会停工）或供应塔行为=指定（普通配对被排除）。
    /// </summary>
    public static class PairingEngine
    {
        private class Entry
        {
            public StationRow Row;
            public SlotInfo Slot;
        }

        private class NoteCount
        {
            public string ItemName;
            public int Count;
        }

        private struct Pos { public double X, Y, Z; public bool Ok; }

        public static PairingResult Build(List<StationRow> rows, PairRule rule)
        {
            var result = new PairingResult();
            if (rows == null || rows.Count == 0) return result;
            if (rule == null) rule = new PairRule();

            // ===== 1. 构建供需池（按数据源：名字解析 / 槽位直读） =====
            var supplyByItem = new Dictionary<int, List<Entry>>();
            var demandByItem = new Dictionary<int, List<Entry>>();

            if (rule.Source == PairSource.BySlot)
            {
                BuildPoolsBySlot(rows, supplyByItem, demandByItem);
            }
            else
            {
                int storageSkipped = 0;
                BuildPoolsByName(rows, supplyByItem, demandByItem, result, ref storageSkipped);
                if (storageSkipped > 0)
                    Note(result, $"{storageSkipped} 处物品槽位为星际仓储（名字未声明角色）→ 按槽位判定不参与配对");
            }

            // ===== 2. 需求任务：跨物品收集 + 缺口降序 =====
            var tasks = new List<KeyValuePair<Entry, int>>(); // (需求条目, itemId)
            foreach (var kv in demandByItem)
            {
                foreach (var d in kv.Value) tasks.Add(new KeyValuePair<Entry, int>(d, kv.Key));
            }
            tasks.Sort((x, y) => Gap(y.Key).CompareTo(Gap(x.Key)));

            // ===== 3. 配对（塔对去重；K 额度按需求塔累计） =====
            var pairMap = new Dictionary<long, PairPlanItem>();
            var pairedCount = new Dictionary<int, int>();
            var posCache = new Dictionary<int, Pos>();
            var singleSource = new Dictionary<int, NoteCount>(); // 单供应源跳过的聚合
            var noSupply = new Dictionary<int, NoteCount>();     // 有需求无供应的聚合

            foreach (var t in tasks)
            {
                var d = t.Key;
                int itemId = t.Value;
                int done;
                pairedCount.TryGetValue(d.Row.Gid, out done);
                if (done >= rule.TopK) continue;

                List<Entry> supplies;
                if (!supplyByItem.TryGetValue(itemId, out supplies) || supplies.Count == 0)
                {
                    Bump(noSupply, itemId, d.Slot.ItemName);
                    continue;
                }

                // 单供应源：官方"兜底配对"已让全部需求塔共享该源，P2P 无增益；
                // 例外：需求塔行为=仅配对/指定（无兜底会停工）或供应塔行为=指定（普通配对被排除）
                if (supplies.Count == 1)
                {
                    bool demandNeedsRoute = d.Row.Behavior >= 3;
                    bool supplyDesignated = supplies[0].Row.Behavior == 4;
                    if (!demandNeedsRoute && !supplyDesignated)
                    {
                        Bump(singleSource, itemId, d.Slot.ItemName);
                        continue;
                    }
                }

                // 候选：排除同星球（交给行星内物流）→ 距离升序
                var candidates = new List<KeyValuePair<Entry, double>>();
                var dPos = GetPos(posCache, d.Row.PlanetId);
                foreach (var s in supplies)
                {
                    if (s.Row.Gid == d.Row.Gid) continue;
                    if (s.Row.PlanetId == d.Row.PlanetId) continue;
                    if (!rule.AllowCrossAstro && StationOps.AstroId(s.Row.PlanetId) != StationOps.AstroId(d.Row.PlanetId)) continue;
                    var sPos = GetPos(posCache, s.Row.PlanetId);
                    candidates.Add(new KeyValuePair<Entry, double>(s, Distance(sPos, dPos)));
                }
                candidates.Sort((x, y) => x.Value.CompareTo(y.Value));

                foreach (var c in candidates)
                {
                    if (done >= rule.TopK) break;
                    // 防雨露均沾：供应能力过小的候选跳过
                    double need = Math.Max(1, d.Slot.Max);
                    if (c.Key.Slot.Max < need * rule.MinSupplyRatio) continue;

                    var s = c.Key;
                    long key = PairKey(s.Row.Gid, d.Row.Gid);
                    PairPlanItem item;
                    if (pairMap.TryGetValue(key, out item))
                    {
                        if (!item.ItemIds.Contains(itemId))
                        {
                            item.ItemIds.Add(itemId);
                            item.ItemNames.Add(d.Slot.ItemName);
                        }
                    }
                    else
                    {
                        item = new PairPlanItem
                        {
                            GidA = Math.Min(s.Row.Gid, d.Row.Gid),
                            GidB = Math.Max(s.Row.Gid, d.Row.Gid),
                            SupplyGid = s.Row.Gid,
                            DemandGid = d.Row.Gid,
                            SupplyPlanetId = s.Row.PlanetId,
                            DemandPlanetId = d.Row.PlanetId,
                            SupplyName = Trunc(s.Row.Name),
                            DemandName = Trunc(d.Row.Name),
                            SupplyPlanet = s.Row.PlanetName,
                            DemandPlanet = d.Row.PlanetName,
                            Distance = c.Value,
                            Exists = StationOps.HasP2P(s.Row.Gid, d.Row.Gid),
                        };
                        item.ItemIds.Add(itemId);
                        item.ItemNames.Add(d.Slot.ItemName);
                        pairMap[key] = item;
                    }
                    done++;
                }
                pairedCount[d.Row.Gid] = done;
            }

            // ===== 4. 聚合提示（物品级结构说明） =====
            foreach (var kv in singleSource)
            {
                Note(result, $"「{kv.Value.ItemName}」仅 1 个星际供应塔：{kv.Value.Count} 个需求塔由官方自动配对服务（无需 P2P，已跳过）");
            }
            foreach (var kv in noSupply)
            {
                Note(result, $"「{kv.Value.ItemName}」有 {kv.Value.Count} 个星际需求塔、但没有星际供应塔 → 无法配对（请检查供应塔/产线）");
            }

            result.Pairs.AddRange(pairMap.Values);
            result.Pairs.Sort((x, y) => x.Distance.CompareTo(y.Distance));
            return result;
        }

        // ================= 供需池构建 =================

        /// <summary>模式一：名字驱动——解析 [物品][角色]，并与槽位星际逻辑做一致性校验。</summary>
        private static void BuildPoolsByName(List<StationRow> rows,
            Dictionary<int, List<Entry>> supplyByItem, Dictionary<int, List<Entry>> demandByItem,
            PairingResult result, ref int storageSkipped)
        {
            foreach (var r in rows)
            {
                if (!r.HasName || r.Slots == null || r.Slots.Count == 0) continue;
                var pn = NameParser.Parse(r.Name);
                if (!pn.HasItem) continue;

                for (int k = 0; k < pn.ItemIds.Count; k++)
                {
                    int itemId = pn.ItemIds[k];
                    string itemName = pn.ItemNames[k];
                    var slot = FindSlot(r, itemId);
                    if (slot == null)
                    {
                        Warn(result, $"#{r.Gid}「{Trunc(r.Name)}」名字声明「{itemName}」但塔内无该物品槽 → 跳过");
                        continue;
                    }
                    int rl = slot.RemoteLogic; // 严格星际逻辑

                    // 角色判定：名字写了角色 → 以名字为准（并校验一致性）；
                    // 名字没写角色 → 用槽位星际逻辑推断
                    bool wantSup, wantDem;
                    if (pn.Role == 0)
                    {
                        wantSup = rl == 1;
                        wantDem = rl == 2;
                    }
                    else
                    {
                        wantSup = pn.Role == 1 || pn.Role == 3;
                        wantDem = pn.Role == 2 || pn.Role == 3;
                    }

                    if (wantSup && rl == 1)
                    {
                        AddEntry(supplyByItem, itemId, r, slot);
                    }
                    else if (wantDem && rl == 2)
                    {
                        AddEntry(demandByItem, itemId, r, slot);
                    }
                    else
                    {
                        string rlName = rl == 0 ? "星际仓储" : rl == 1 ? "星际供应" : "星际需求";
                        if (pn.Role == 0 && rl == 0)
                        {
                            // 名字未声明角色 + 槽位为星际仓储：正常状态（该物品不参与星际供需），
                            // 聚合为一条提示而非逐塔警告（避免「储X」模板命名后刷屏）
                            storageSkipped++;
                        }
                        else if (pn.Role == 4 && rl == 0)
                        {
                            // 声明「储」且槽位一致 → 安静跳过
                        }
                        else if (pn.Role == 4)
                        {
                            Warn(result, $"#{r.Gid}「{Trunc(r.Name)}」名字声明「储」但「{itemName}」槽位为{rlName}（若要参与星际供需请改名）→ 跳过");
                        }
                        else
                        {
                            Warn(result, $"#{r.Gid}「{Trunc(r.Name)}」角色={pn.RoleName} 与「{itemName}」槽位（{rlName}）不一致 → 跳过");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 模式二：槽位直读——不需要名字，遍历全部塔的槽位：
        /// rl=1（星际供应）→ 供应池；rl=2（星际需求）→ 需求池；星际仓储/本地逻辑不参与。
        /// </summary>
        private static void BuildPoolsBySlot(List<StationRow> rows,
            Dictionary<int, List<Entry>> supplyByItem, Dictionary<int, List<Entry>> demandByItem)
        {
            foreach (var r in rows)
            {
                if (r.Slots == null || r.Slots.Count == 0) continue;
                foreach (var slot in r.Slots)
                {
                    if (slot.RemoteLogic == 1) AddEntry(supplyByItem, slot.ItemId, r, slot);
                    else if (slot.RemoteLogic == 2) AddEntry(demandByItem, slot.ItemId, r, slot);
                }
            }
        }

        // ================= 航路聚合 =================

        /// <summary>
        /// 把塔对计划聚合为航路计划：
        /// astroLevel=false → 星球航路（按 planetId 对）；true → 行星系航路（按 astroId 对）。
        /// 同一（节点对, 物品）只产生一条航路；PairCount = 覆盖的塔对-物品通信数。
        /// </summary>
        public static List<RoutePlanItem> AggregateRoutes(List<PairPlanItem> pairs, bool astroLevel)
        {
            var map = new Dictionary<long, RoutePlanItem>();
            if (pairs != null)
            {
                foreach (var p in pairs)
                {
                    int a = astroLevel ? StationOps.AstroId(p.SupplyPlanetId) : p.SupplyPlanetId;
                    int b = astroLevel ? StationOps.AstroId(p.DemandPlanetId) : p.DemandPlanetId;
                    bool planetLevel = !astroLevel;
                    if (astroLevel && a == b)
                    {
                        // 同一恒星系内的跨星球塔对：行星系航路无法表达（两端同系），
                        // 自动降级为星球航路（官方机制限制，不丢配对）
                        a = p.SupplyPlanetId;
                        b = p.DemandPlanetId;
                        planetLevel = true;
                    }
                    if (a <= 0 || b <= 0 || a == b) continue;

                    int lo = Math.Min(a, b), hi = Math.Max(a, b);
                    for (int i = 0; i < p.ItemIds.Count; i++)
                    {
                        int itemId = p.ItemIds[i];
                        string itemName = i < p.ItemNames.Count ? p.ItemNames[i] : itemId.ToString();

                        long key = (long)lo | ((long)hi << 22) | ((long)itemId << 44);
                        RoutePlanItem item;
                        if (!map.TryGetValue(key, out item))
                        {
                            item = new RoutePlanItem
                            {
                                AId = lo,
                                BId = hi,
                                PlanetLevel = planetLevel,
                                AName = planetLevel ? StationOps.PlanetName(lo) : StationOps.GalaxyName(lo),
                                BName = planetLevel ? StationOps.PlanetName(hi) : StationOps.GalaxyName(hi),
                            };
                            map[key] = item;
                        }
                        if (!item.ItemIds.Contains(itemId))
                        {
                            item.ItemIds.Add(itemId);
                            item.ItemNames.Add(itemName);
                        }
                        item.PairCount++;
                    }
                }
            }

            var list = new List<RoutePlanItem>(map.Values);
            foreach (var item in list)
            {
                item.ExistItemCount = 0;
                for (int i = 0; i < item.ItemIds.Count; i++)
                {
                    if (StationOps.HasAstroRoute(item.AId, item.BId, item.ItemIds[i])) item.ExistItemCount++;
                }
                item.AllExist = item.ExistItemCount == item.ItemIds.Count;
            }
            list.Sort((x, y) => y.PairCount.CompareTo(x.PairCount));
            return list;
        }

        // ================= 辅助 =================

        private static SlotInfo FindSlot(StationRow row, int itemId)
        {
            if (row.Slots == null) return null;
            foreach (var s in row.Slots)
            {
                if (s.ItemId == itemId) return s;
            }
            return null;
        }

        private static void AddEntry(Dictionary<int, List<Entry>> map, int itemId, StationRow row, SlotInfo slot)
        {
            List<Entry> list;
            if (!map.TryGetValue(itemId, out list))
            {
                list = new List<Entry>();
                map[itemId] = list;
            }
            list.Add(new Entry { Row = row, Slot = slot });
        }

        /// <summary>缺口（还能收多少）：Max - Count，至少按 1 计。</summary>
        private static long Gap(Entry d)
        {
            long gap = (long)d.Slot.Max - d.Slot.Count;
            return gap > 0 ? gap : 1;
        }

        private static long PairKey(int a, int b)
        {
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            return (long)lo | ((long)hi << 32);
        }

        private static Pos GetPos(Dictionary<int, Pos> cache, int planetId)
        {
            Pos p;
            if (cache.TryGetValue(planetId, out p)) return p;
            double x, y, z;
            p.Ok = StationOps.TryGetPlanetPosition(planetId, out x, out y, out z);
            p.X = x; p.Y = y; p.Z = z;
            cache[planetId] = p;
            return p;
        }

        private static double Distance(Pos a, Pos b)
        {
            if (!a.Ok || !b.Ok) return double.MaxValue; // 坐标未知排最后
            double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static void Bump(Dictionary<int, NoteCount> map, int itemId, string itemName)
        {
            NoteCount c;
            if (!map.TryGetValue(itemId, out c))
            {
                c = new NoteCount { ItemName = itemName };
                map[itemId] = c;
            }
            c.Count++;
        }

        private static void Warn(PairingResult r, string msg)
        {
            if (r.Warnings.Count < 50) r.Warnings.Add(msg);
            else if (r.Warnings.Count == 50) r.Warnings.Add("……更多不一致项已省略");
        }

        private static void Note(PairingResult r, string msg)
        {
            if (r.Notices.Count < 30) r.Notices.Add(msg);
        }

        private static string Trunc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= 18 ? s : s.Substring(0, 18) + "…";
        }
    }
}
