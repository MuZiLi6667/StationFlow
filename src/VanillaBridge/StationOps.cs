using System;
using System.Collections.Generic;
using System.Diagnostics;
using StationFlow.PluginCore;

namespace StationFlow.VanillaBridge
{
    /// <summary>
    /// 阶段 0 的游戏 API 封装层：**所有直接触碰游戏类型的代码都集中在此**，
    /// 游戏更新时只需定点修复本文件。调用方（UI/规则引擎）不得直接引用游戏类型。
    /// </summary>
    public static class StationOps
    {
        /// <summary>游戏存档是否已就绪（主菜单时为 false）。</summary>
        public static bool GameReady
        {
            get
            {
                try
                {
                    return GameMain.data != null
                        && GameMain.data.galacticTransport != null
                        && GameMain.data.galacticTransport.stationPool != null;
                }
                catch { return false; }
            }
        }

        public static GalacticTransport GT
        {
            get { return GameReady ? GameMain.data.galacticTransport : null; }
        }

        public static int StationCursor
        {
            get { return GT != null ? GT.stationCursor : 0; }
        }

        /// <summary>遍历所有有效物流塔（照抄官方遍历条件）。</summary>
        public static IEnumerable<StationComponent> AllStations()
        {
            var gt = GT;
            if (gt == null) yield break;
            var pool = gt.stationPool;
            int cursor = Math.Min(gt.stationCursor, pool != null ? pool.Length : 0);
            for (int i = 1; i < cursor; i++)
            {
                var st = pool[i];
                if (st != null && st.gid == i && st.id > 0)
                {
                    yield return st;
                }
            }
        }

        public static int CountStations()
        {
            int n = 0;
            foreach (var _ in AllStations()) n++;
            return n;
        }

        /// <summary>按 gid 获取塔（无效返回 null）。</summary>
        public static StationComponent GetStation(int gid)
        {
            var gt = GT;
            if (gt == null || gid <= 0 || gid >= gt.stationPool.Length) return null;
            var st = gt.stationPool[gid];
            if (st != null && st.gid == gid && st.id > 0) return st;
            return null;
        }

        /// <summary>
        /// 按 planetId 定位星球工厂。
        /// ⚠ 关键：GameData.factories 数组用 PlanetData.factoryIndex 索引，**不是** planetId
        /// （实证：StationComponent.cs:3386 `galaxy.PlanetById(planetId).factoryIndex`）。
        /// </summary>
        public static PlanetFactory GetFactory(int planetId)
        {
            try
            {
                var data = GameMain.data;
                var galaxy = GameMain.galaxy;
                if (data == null || data.factories == null || galaxy == null) return null;
                var planet = galaxy.PlanetById(planetId);
                if (planet == null) return null;
                int idx = planet.factoryIndex;
                if (idx < 0 || idx >= data.factories.Length) return null;
                return data.factories[idx];
            }
            catch (Exception ex)
            {
                StationFlowPlugin.LogWarn($"GetFactory({planetId}) 异常: {ex.Message}");
                return null;
            }
        }

        // ================= 名字（实证入口：PlanetFactory.WriteExtraInfoOnEntity） =================

        public static string GetName(StationComponent st)
        {
            if (st == null) return "";
            var factory = GetFactory(st.planetId);
            if (factory == null || st.entityId <= 0) return "";
            try
            {
                return factory.ReadExtraInfoOnEntity(st.entityId) ?? "";
            }
            catch (Exception ex)
            {
                StationFlowPlugin.LogWarn($"GetName(gid={st.gid}) 异常: {ex.Message}");
                return "";
            }
        }

        /// <summary>写名字（空串 = 删除名字）。返回是否成功。</summary>
        public static bool SetName(StationComponent st, string name)
        {
            if (st == null)
            {
                StationFlowPlugin.LogWarn("SetName: 目标塔为空");
                return false;
            }
            var factory = GetFactory(st.planetId);
            if (factory == null)
            {
                StationFlowPlugin.LogWarn($"SetName: 无法定位工厂 (gid={st.gid}, planetId={st.planetId})");
                return false;
            }
            if (st.entityId <= 0)
            {
                StationFlowPlugin.LogWarn($"SetName: entityId 无效 (gid={st.gid})");
                return false;
            }
            try
            {
                factory.WriteExtraInfoOnEntity(st.entityId, (name ?? "").Trim());
                return true;
            }
            catch (Exception ex)
            {
                StationFlowPlugin.LogError($"SetName 异常 (gid={st.gid}, entityId={st.entityId}): {ex}");
                return false;
            }
        }

        // ================= P2P 配对（实证：gt.station2stationRoutes + 统一 RefreshTraffic） =================

        /// <summary>添加 P2P（直写容器，**不刷新**——批量场景由 Executor 归并后统一刷新一次）。</summary>
        public static bool AddP2PRaw(int gidA, int gidB)
        {
            var gt = GT;
            if (gt == null || gidA <= 0 || gidB <= 0 || gidA == gidB) return false;
            if (GetStation(gidA) == null || GetStation(gidB) == null) return false;
            return gt.station2stationRoutes.Add(gt.CalculateStation2StationKey(gidA, gidB));
        }

        /// <summary>移除 P2P（直写容器，**不刷新**）。</summary>
        public static bool RemoveP2PRaw(int gidA, int gidB)
        {
            var gt = GT;
            if (gt == null || gidA <= 0 || gidB <= 0 || gidA == gidB) return false;
            return gt.station2stationRoutes.Remove(gt.CalculateStation2StationKey(gidA, gidB));
        }

        /// <summary>添加点对点配对（直写容器 + 单次刷新；官方 AddStation2StationRoute 每次调用都会触发全量刷新，批量场景禁用）。</summary>
        public static bool AddP2P(int gidA, int gidB)
        {
            bool added = AddP2PRaw(gidA, gidB);
            if (added) RefreshGalacticTraffic("AddP2P");
            return added;
        }

        public static bool RemoveP2P(int gidA, int gidB)
        {
            bool removed = RemoveP2PRaw(gidA, gidB);
            if (removed) RefreshGalacticTraffic("RemoveP2P");
            return removed;
        }

        public static bool HasP2P(int gidA, int gidB)
        {
            var gt = GT;
            if (gt == null) return false;
            return gt.station2stationRoutes.Contains(gt.CalculateStation2StationKey(gidA, gidB));
        }

        /// <summary>统计与指定 gid 相关的 P2P 路由数（station2stationRoutes 的 key：低 32 位 = 小 gid，高 32 位 = 大 gid）。</summary>
        public static int CountP2PRoutesOf(int gid)
        {
            var gt = GT;
            if (gt == null || gid <= 0) return 0;
            int n = 0;
            foreach (long key in gt.station2stationRoutes)
            {
                int lo = (int)(key & 0xFFFFFFFFL);
                int hi = (int)(key >> 32);
                if (lo == gid || hi == gid) n++;
            }
            return n;
        }

        // ================= 星际航路（星球↔星球 / 行星系↔行星系，按物品） =================
        // key = min(aId,bId) | max<<22 | itemId<<44；aId 传 planetId = 星球航路，传 astroId = 行星系航路。

        /// <summary>
        /// 添加航路（直写字典，**不刷新**）。
        /// 键不存在 → 新建（enable=true）；键已存在但被禁用 → 重新启用；
        /// 键已存在且启用 → 返回 false（幂等）。
        /// </summary>
        public static bool AddAstroRouteRaw(int aId, int bId, int itemId)
        {
            var gt = GT;
            if (gt == null || aId <= 0 || bId <= 0 || aId == bId || itemId <= 0) return false;
            long key = gt.CalculateAstro2AstroKey(aId, bId, itemId);
            LogisticShipRoute exist;
            if (gt.astro2astroRoutes.TryGetValue(key, out exist))
            {
                if (exist.enable) return false;
                exist.enable = true; // 存在但被禁用 → 与「＋新增」预览语义一致：启用它
                return true;
            }
            var route = new LogisticShipRoute();
            route.Init(); // enable=true, comment=""
            gt.astro2astroRoutes.Add(key, route);
            return true;
        }

        /// <summary>移除航路（直写字典，**不刷新**）。</summary>
        public static bool RemoveAstroRouteRaw(int aId, int bId, int itemId)
        {
            var gt = GT;
            if (gt == null || aId <= 0 || bId <= 0 || aId == bId || itemId <= 0) return false;
            return gt.astro2astroRoutes.Remove(gt.CalculateAstro2AstroKey(aId, bId, itemId));
        }

        /// <summary>航路是否存在（且启用）。</summary>
        public static bool HasAstroRoute(int aId, int bId, int itemId)
        {
            var gt = GT;
            if (gt == null || itemId <= 0) return false;
            return gt.IsAstro2AstroRouteEnable(aId, bId, itemId);
        }

        /// <summary>航路总数。</summary>
        public static int RouteCount
        {
            get { var gt = GT; return gt != null ? gt.astro2astroRoutes.Count : 0; }
        }

        /// <summary>P2P 路由总数。</summary>
        public static int P2PRouteCount
        {
            get { var gt = GT; return gt != null ? gt.station2stationRoutes.Count : 0; }
        }

        private static void ClassifyEndpoint(int id, HashSet<int> planets, HashSet<int> astros)
        {
            if (id <= 0) return;
            if (id % 100 == 0) astros.Add(id); // 整百 = 行星系 id
            else planets.Add(id);              // 否则 = 星球 id
        }

        /// <summary>
        /// 按物品收集航路两端点：itemId → (星球端点集合, 行星系端点集合)。
        /// 键 = min(a,b) | max&lt;&lt;22 | itemId&lt;&lt;44；planetId%100∈{1..}、astroId%100==0 以此分类。
        /// 用于诊断「某塔某物品是否被航路覆盖」的精确判断。
        /// </summary>
        public static void CollectRouteEndpointsByItem(
            out Dictionary<int, HashSet<int>> planetsByItem,
            out Dictionary<int, HashSet<int>> astrosByItem)
        {
            planetsByItem = new Dictionary<int, HashSet<int>>();
            astrosByItem = new Dictionary<int, HashSet<int>>();
            var gt = GT;
            if (gt == null) return;
            try
            {
                foreach (long key in gt.astro2astroRoutes.Keys)
                {
                    int itemId = (int)(key >> 44);
                    if (itemId <= 0) continue;
                    HashSet<int> planets, astros;
                    if (!planetsByItem.TryGetValue(itemId, out planets))
                    {
                        planets = new HashSet<int>();
                        astros = new HashSet<int>();
                        planetsByItem[itemId] = planets;
                        astrosByItem[itemId] = astros;
                    }
                    ClassifyEndpoint((int)(key & 0x3FFFFF), planetsByItem[itemId], astrosByItem[itemId]);
                    ClassifyEndpoint((int)((key >> 22) & 0x3FFFFF), planetsByItem[itemId], astrosByItem[itemId]);
                }
            }
            catch { }
        }

        /// <summary>某星球的某物品是否被任一航路覆盖（按物品精确匹配）。</summary>
        public static bool IsPlanetCoveredByRouteForItem(int planetId, int itemId,
            Dictionary<int, HashSet<int>> planetsByItem, Dictionary<int, HashSet<int>> astrosByItem)
        {
            HashSet<int> planets, astros;
            if (planetsByItem != null && planetsByItem.TryGetValue(itemId, out planets) && planets.Contains(planetId)) return true;
            if (astrosByItem != null && astrosByItem.TryGetValue(itemId, out astros) && astros.Contains(AstroId(planetId))) return true;
            return false;
        }

        /// <summary>一次遍历统计每个 gid 关联的 P2P 路由数（供快照批量读取，避免逐塔全表扫描）。</summary>
        public static Dictionary<int, int> GetP2PRouteCounts()
        {
            var map = new Dictionary<int, int>();
            var gt = GT;
            if (gt == null) return map;
            foreach (long key in gt.station2stationRoutes)
            {
                int lo = (int)(key & 0xFFFFFFFFL);
                int hi = (int)(key >> 32);
                int v;
                map.TryGetValue(lo, out v);
                map[lo] = v + 1;
                map.TryGetValue(hi, out v);
                map[hi] = v + 1;
            }
            return map;
        }

        // ================= 行为 / 分组（实证：直接写字段） =================

        public static void SetBehavior(StationComponent st, ERemoteRoutePriority priority)
        {
            if (st == null) return;
            st.routePriority = priority;
        }

        /// <summary>切换分组位（bit 从 0 起，对应原生 UI 的第一个组按钮）。变更后需刷新配对。</summary>
        public static void ToggleGroup(StationComponent st, int bit)
        {
            if (st == null || bit < 0 || bit > 62) return;
            st.remoteGroupMask ^= 1L << bit;
            RefreshGalacticTraffic("ToggleGroup");
        }

        /// <summary>设置分组位（直写掩码差异，**不刷新**）。返回是否发生变化。</summary>
        public static bool SetGroupBitRaw(StationComponent st, int bit, bool on)
        {
            if (st == null || bit < 0 || bit > 62) return false;
            long mask = st.remoteGroupMask;
            long target = on ? (mask | (1L << bit)) : (mask & ~(1L << bit));
            if (target == mask) return false;
            st.remoteGroupMask = target;
            return true;
        }

        /// <summary>写槽位远程物流逻辑（直写字段，**不刷新**，实验性）。返回是否发生变化。</summary>
        public static bool SetSlotRemoteLogicRaw(int gid, int slotIndex, ELogisticStorage logic)
        {
            var st = GetStation(gid);
            if (st == null || st.storage == null) return false;
            if (slotIndex < 0 || slotIndex >= st.storage.Length) return false;
            if (st.storage[slotIndex].itemId <= 0) return false;
            if (st.storage[slotIndex].remoteLogic == logic) return false;
            st.storage[slotIndex].remoteLogic = logic;
            return true;
        }

        // ================= 刷新（带计时，验证项 C） =================

        /// <summary>触发星际配对全量重建（不传参 = 跳过在途船修复）。记录耗时。</summary>
        public static void RefreshGalacticTraffic(string reason)
        {
            var gt = GT;
            if (gt == null) return;
            var sw = Stopwatch.StartNew();
            gt.RefreshTraffic();
            sw.Stop();
            StationFlowPlugin.LogInfo($"RefreshTraffic({reason}) 耗时 {sw.ElapsedMilliseconds}ms");
        }

        // ================= 名称辅助 =================

        public static string PlanetName(int planetId)
        {
            try
            {
                var galaxy = GameMain.galaxy;
                if (galaxy == null) return planetId.ToString();
                var planet = galaxy.PlanetById(planetId);
                return planet != null ? planet.displayName : planetId.ToString();
            }
            catch { return planetId.ToString(); }
        }

        /// <summary>恒星系显示名（如"母星系"）。planetId/100 = 恒星 id。</summary>
        public static string GalaxyName(int planetId)
        {
            try
            {
                var galaxy = GameMain.galaxy;
                if (galaxy == null) return "";
                var star = galaxy.StarById(planetId / 100);
                return star != null ? star.displayName : "";
            }
            catch { return ""; }
        }

        /// <summary>短行星名（"母星系 - IV号星" → "IV号星"）。</summary>
        public static string ShortPlanetName(int planetId)
        {
            string full = PlanetName(planetId);
            if (string.IsNullOrEmpty(full)) return "";
            int i = full.LastIndexOf('-');
            return i >= 0 ? full.Substring(i + 1).Trim() : full;
        }

        /// <summary>行星系 id（官方公式：planetId / 100 * 100）。</summary>
        public static int AstroId(int planetId)
        {
            return planetId / 100 * 100;
        }

        /// <summary>物品本地化名（LDB 查询）。</summary>
        public static string ItemName(int itemId)
        {
            try
            {
                if (itemId <= 0) return "";
                var proto = LDB.items.Select(itemId);
                return proto != null ? proto.name : itemId.ToString();
            }
            catch { return itemId.ToString(); }
        }

        /// <summary>物品图标 sprite（官方小图标，用于表格显示）。</summary>
        public static UnityEngine.Sprite ItemIcon(int itemId)
        {
            try
            {
                if (itemId <= 0) return null;
                var proto = LDB.items.Select(itemId);
                return proto != null ? proto.iconSprite : null;
            }
            catch { return null; }
        }

        /// <summary>全部物品 (id, 本地化名)——用于名字解析索引。</summary>
        public static List<KeyValuePair<int, string>> AllItemNames()
        {
            var list = new List<KeyValuePair<int, string>>();
            try
            {
                var ldb = LDB.items;
                if (ldb == null || ldb.dataArray == null) return list;
                foreach (var proto in ldb.dataArray)
                {
                    if (proto == null || proto.ID <= 0 || string.IsNullOrEmpty(proto.name)) continue;
                    string n = proto.name.Trim();
                    if (n.Length == 0) continue;
                    list.Add(new KeyValuePair<int, string>(proto.ID, n));
                }
            }
            catch (Exception ex)
            {
                StationFlowPlugin.LogWarn($"AllItemNames 异常: {ex.Message}");
            }
            return list;
        }

        /// <summary>行星宇宙坐标（用于就近供应排序）。失败返回 false。</summary>
        public static bool TryGetPlanetPosition(int planetId, out double px, out double py, out double pz)
        {
            px = py = pz = 0;
            try
            {
                var galaxy = GameMain.galaxy;
                if (galaxy == null) return false;
                var planet = galaxy.PlanetById(planetId);
                if (planet == null) return false;
                px = planet.uPosition.x;
                py = planet.uPosition.y;
                pz = planet.uPosition.z;
                return true;
            }
            catch { return false; }
        }

        // ================= 物流流量统计（实证：TrafficStatistics.factoryTrafficPool[factoryIndex]） =================
        // TrafficStat.total 布局：input [0..5]各时间粒度+[6]总累计；output [7..12]+[13]；internal [14..19]+[20]
        // 时间粒度：0=10tick 1=1s 2=10s 3=1min 4=10min 5=1h

        /// <summary>
        /// 读取某星球某物品的物流流量。
        /// perMin 取 level3（约最近 1 分钟）；total 为历史总累计。
        /// </summary>
        public static void GetTraffic(int planetId, int itemId,
            out long inputPerMin, out long outputPerMin, out long inputTotal, out long outputTotal)
        {
            inputPerMin = outputPerMin = inputTotal = outputTotal = 0;
            try
            {
                var data = GameMain.data;
                if (data == null || data.statistics == null) return;
                var traffic = data.statistics.traffic;
                if (traffic == null || traffic.factoryTrafficPool == null) return;
                var galaxy = GameMain.galaxy;
                if (galaxy == null) return;
                var planet = galaxy.PlanetById(planetId);
                if (planet == null) return;
                int fi = planet.factoryIndex;
                if (fi < 0 || fi >= traffic.factoryTrafficPool.Length) return;
                var stat = traffic.factoryTrafficPool[fi];
                if (stat == null || stat.itemIndices == null || stat.trafficPool == null) return;
                if (itemId <= 0 || itemId >= stat.itemIndices.Length) return;
                int idx = stat.itemIndices[itemId];
                if (idx <= 0 || idx >= stat.trafficCursor) return;
                var ts = stat.trafficPool[idx];
                if (ts == null || ts.total == null || ts.total.Length < 21) return;
                inputPerMin = ts.total[3];    // input level3 ≈ 最近 1 分钟
                outputPerMin = ts.total[10];  // output level3
                inputTotal = ts.total[6];     // input 总累计
                outputTotal = ts.total[13];   // output 总累计
            }
            catch { }
        }

        /// <summary>塔的"主物品"：第一个非空槽位的物品 id（用于总览与命名生成自动填充）。</summary>
        public static int MainItemId(StationComponent st)
        {
            if (st == null || st.storage == null) return 0;
            for (int i = 0; i < st.storage.Length; i++)
            {
                if (st.storage[i].itemId > 0) return st.storage[i].itemId;
            }
            return 0;
        }

        /// <summary>当前正在打开物流塔窗口的塔（没有则 null）。</summary>
        public static StationComponent CurrentOpenedStation()
        {
            try
            {
                var uiGame = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
                var win = uiGame != null ? uiGame.stationWindow : null;
                if (win == null || !win.active) return null;
                int stationId = win.stationId;
                return stationId > 0 ? GetStation(stationId) : null;
            }
            catch { return null; }
        }
    }
}
