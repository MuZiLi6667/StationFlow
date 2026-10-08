using System;
using System.Collections.Generic;
using System.Diagnostics;
using StationFlow.Data;
using StationFlow.PluginCore;
using StationFlow.Rules;
using StationFlow.VanillaBridge;
using UnityEngine;

namespace StationFlow.Exec
{
    /// <summary>
    /// 自动巡检（持续自动模式，默认关）：
    /// 每 30 秒检查一次塔数量（轻量），发现新增塔 → 快照 + 按当前规则生成增量配对计划
    /// （限流）→ 事务执行 → 日志与状态栏留痕。出错自动关闭。
    /// 增量语义：已存在的配对/航路自动跳过（幂等），只写新增。
    /// </summary>
    public class AutoPilot : MonoBehaviour
    {
        public static AutoPilot Instance;

        private const float CheckIntervalSec = 30f;

        private float timer;
        private int lastStationCount = -1;

        /// <summary>开关（与 cfg 同步）。</summary>
        public bool Enabled;

        /// <summary>最近一次巡检结果描述（UI 显示用）。</summary>
        public string LastRunInfo = "";

        private void Awake()
        {
            Instance = this;
            Enabled = StationFlowPlugin.CfgAutoEnabled.Value;
        }

        private void Update()
        {
            try
            {
                if (!Enabled) return;
                if (!StationOps.GameReady)
                {
                    lastStationCount = -1; // 回主菜单 → 重置基线
                    return;
                }

                timer += Time.unscaledDeltaTime;
                if (timer < CheckIntervalSec) return;
                timer = 0f;

                int count = StationOps.CountStations();
                if (lastStationCount < 0)
                {
                    lastStationCount = count; // 首次记录基线
                    return;
                }
                if (count == lastStationCount) return; // 无变化：不做事

                // 仅在巡检确认处理后才推进基线——Executor 忙时基线保持旧值，
                // 下轮继续重试，避免"塔数变化被吞掉后永不触发"的漏检
                if (Sweep()) lastStationCount = count;
            }
            catch (Exception ex)
            {
                StationFlowPlugin.LogError($"[自动巡检] 异常，已自动关闭: {ex}");
                Enabled = false;
                try { StationFlowPlugin.CfgAutoEnabled.Value = false; } catch { }
            }
        }

        /// <summary>
        /// 执行一次巡检（塔数变化时触发）。
        /// 返回 true = 本轮已处理（含"无新增配对"），调用方推进基线；
        /// 返回 false = 本轮未处理（Executor 忙 / 启动失败），保留基线下轮重试。
        /// </summary>
        private bool Sweep()
        {
            var ex = Executor.Instance;
            if (ex == null || ex.IsRunning) return false;

            var sw = Stopwatch.StartNew();
            var rows = StationSnapshot.Build();
            var rule = new PairRule
            {
                Source = (PairSource)Mathf.Clamp(StationFlowPlugin.CfgPairSource.Value, 0, 1),
                TopK = Mathf.Clamp(StationFlowPlugin.CfgTopK.Value, 1, 5),
                MinSupplyRatio = Mathf.Clamp(StationFlowPlugin.CfgMinRatio.Value, 0.1f, 0.5f),
                AllowCrossAstro = StationFlowPlugin.CfgCrossAstro.Value,
            };
            var result = PairingEngine.Build(rows, rule);
            sw.Stop();

            int maxActions = Mathf.Clamp(StationFlowPlugin.CfgAutoMaxActions.Value, 1, 200);
            var method = (PairMethod)Mathf.Clamp(StationFlowPlugin.CfgPairMethod.Value, 0, 2);
            List<RoutePlanItem> routes;
            var plan = PlanBuilder.FromPairs(result, rule.Source, method, maxActions, "自动巡检", out routes);

            if (plan.Actions.Count == 0)
            {
                LastRunInfo = $"{DateTime.Now:HH:mm:ss} 巡检：无新增配对（{sw.ElapsedMilliseconds}ms）";
                StationFlowPlugin.LogInfo($"[自动巡检] {LastRunInfo}");
                return true;
            }

            ex.OnCompleted += OnDone;
            if (!ex.Execute(plan))
            {
                ex.OnCompleted -= OnDone;
                return false;
            }
            LastRunInfo = $"{DateTime.Now:HH:mm:ss} 巡检：执行 {plan.Actions.Count} 条新增配对（{sw.ElapsedMilliseconds}ms）…";
            StationFlowPlugin.LogInfo($"[自动巡检] 塔数变化触发；{LastRunInfo}");
            return true;
        }

        private void OnDone()
        {
            var ex = Executor.Instance;
            if (ex != null) ex.OnCompleted -= OnDone;
            var r = ex != null ? ex.LastReport : null;
            LastRunInfo = $"{DateTime.Now:HH:mm:ss} 自动配对：{(r != null ? r.Summary : "完成")}";
            StationFlowPlugin.LogInfo($"[自动巡检] {LastRunInfo}");

            // 面板开着时同步数据与状态栏
            var panel = UI.MainPanel.Instance;
            if (panel != null && panel.IsVisible) panel.NotifyAutoRun(LastRunInfo);
        }
    }
}
