using System.Collections.Generic;
using StationFlow.Rules;

namespace StationFlow.Exec
{
    /// <summary>
    /// 配对结果 → 执行计划的公共构建器（RuleTab 手动生成 与 AutoPilot 自动巡检 共用）。
    /// 规则：仅新增（Exists=false）；按方式生成 P2P 或航路键；限流截断。
    /// </summary>
    public static class PlanBuilder
    {
        /// <summary>
        /// 从配对结果构建执行计划。
        /// </summary>
        /// <param name="result">配对结果</param>
        /// <param name="source">数据源（用于标题）</param>
        /// <param name="method">配对方式（P2P / 星球航路 / 行星系航路）</param>
        /// <param name="maxActions">最大操作数（限流；&lt;=0 表示不限）</param>
        /// <param name="titlePrefix">标题前缀</param>
        /// <param name="routes">航路方式时输出聚合结果（可为 null）</param>
        public static ExecutionPlan FromPairs(PairingResult result, PairSource source, PairMethod method,
            int maxActions, string titlePrefix, out List<RoutePlanItem> routes)
        {
            routes = null;
            string srcTag = source == PairSource.ByName ? "名字" : "槽位";
            var plan = new ExecutionPlan();

            if (method == PairMethod.P2P)
            {
                plan.Title = $"{titlePrefix}P2P({srcTag})";
                if (result != null)
                {
                    foreach (var p in result.Pairs)
                    {
                        if (p.Exists) continue;
                        plan.Actions.Add(new PlanAction { Type = PlanActionType.AddP2P, Gid = p.GidA, GidB = p.GidB });
                        if (maxActions > 0 && plan.Actions.Count >= maxActions) break;
                    }
                }
            }
            else
            {
                bool astroLevel = method == PairMethod.AstroRoute;
                plan.Title = $"{titlePrefix}{(astroLevel ? "行星系" : "星球")}航路({srcTag})";
                routes = result != null ? PairingEngine.AggregateRoutes(result.Pairs, astroLevel) : new List<RoutePlanItem>();
                foreach (var r in routes)
                {
                    for (int i = 0; i < r.ItemIds.Count; i++)
                    {
                        plan.Actions.Add(new PlanAction { Type = PlanActionType.AddRoute, Gid = r.AId, GidB = r.BId, IntValue = r.ItemIds[i] });
                        if (maxActions > 0 && plan.Actions.Count >= maxActions) break;
                    }
                    if (maxActions > 0 && plan.Actions.Count >= maxActions) break;
                }
            }
            return plan;
        }
    }
}
