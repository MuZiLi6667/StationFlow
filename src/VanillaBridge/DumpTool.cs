using System;
using System.IO;
using System.Text;
using BepInEx;
using StationFlow.PluginCore;

namespace StationFlow.VanillaBridge
{
    /// <summary>
    /// 阶段 0 验证工具：导出全星区物流塔清单为 CSV，用于静态分析：
    /// 名字、星球、行为、分组、槽位供需、配对桶计数。
    /// 输出目录：BepInEx/StationFlow/
    /// </summary>
    public static class DumpTool
    {
        public static string DumpRootDir
        {
            get { return Path.Combine(Paths.BepInExRootPath, "StationFlow"); }
        }

        public static string Dump()
        {
            if (!StationOps.GameReady) return null;
            Directory.CreateDirectory(DumpRootDir);
            string path = Path.Combine(DumpRootDir, $"dump-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("gid,entityId,name,planetId,planetName,isStellar,isCollector,routePriority,groupMask," +
                          "pairTotal,pairNormal,pairP2P,pairPlanetRoute,pairAstroRoute,pairGroup,pairFallback," +
                          "slots(itemId:ll:rl:count:max)");

            foreach (var st in StationOps.AllStations())
            {
                string name = CsvEscape(StationOps.GetName(st));
                string planet = CsvEscape(StationOps.PlanetName(st.planetId));

                // 配对桶计数（段划分见 docs/phase0-findings.md）
                string pairs = "0,0,0,0,0,0,0";
                var off = st.remotePairOffsets;
                if (off != null && off.Length >= 7)
                {
                    pairs = string.Join(",", new[]
                    {
                        off[6].ToString(),
                        (off[1] - off[0]).ToString(), // 段0 普通
                        (off[2] - off[1]).ToString(), // 段1 P2P
                        (off[3] - off[2]).ToString(), // 段2 行星航路
                        (off[4] - off[3]).ToString(), // 段3 星系航路
                        (off[5] - off[4]).ToString(), // 段4 分组
                        (off[6] - off[5]).ToString()  // 段5 兜底普通
                    });
                }

                // 槽位摘要
                var slotSb = new StringBuilder();
                if (st.storage != null)
                {
                    for (int i = 0; i < st.storage.Length; i++)
                    {
                        var s = st.storage[i];
                        if (s.itemId <= 0) continue;
                        if (slotSb.Length > 0) slotSb.Append(';');
                        slotSb.Append($"{s.itemId}:{(int)s.localLogic}:{(int)s.remoteLogic}:{s.count}:{s.max}");
                    }
                }

                sb.AppendLine($"{st.gid},{st.entityId},{name},{st.planetId},{planet},{st.isStellar},{st.isCollector}," +
                              $"{(int)st.routePriority},{st.remoteGroupMask},{pairs},{CsvEscape(slotSb.ToString())}");
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            StationFlowPlugin.LogInfo($"Dump 完成：{path}（{StationOps.CountStations()} 个塔）");
            return path;
        }

        private static string CsvEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r', ';' }) >= 0)
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }
    }
}
