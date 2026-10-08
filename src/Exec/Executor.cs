using System;
using System.Collections.Generic;
using System.Diagnostics;
using StationFlow.PluginCore;
using StationFlow.VanillaBridge;
using UnityEngine;

namespace StationFlow.Exec
{
    public enum PlanActionType
    {
        AddP2P,         // Gid + GidB
        RemoveP2P,      // Gid + GidB
        AddRoute,       // Gid=astroA + GidB=astroB + IntValue=itemId（星球/行星系航路）
        RemoveRoute,    // 同上
        SetBehavior,    // Gid + IntValue（ERemoteRoutePriority）
        SetGroupBit,    // Gid + IntValue(bit) + BoolValue(on/off)
        SetRemoteLogic, // Gid + IntValue(slotIndex) + IntValue2(ELogisticStorage)
        Rename,         // Gid + StrValue
    }

    /// <summary>一条计划操作（纯数据）。</summary>
    public class PlanAction
    {
        public PlanActionType Type;
        public int Gid;
        public int GidB;
        public int IntValue;
        public int IntValue2;
        public bool BoolValue;
        public string StrValue;
    }

    /// <summary>一次执行计划。</summary>
    public class ExecutionPlan
    {
        public string Title = "";
        public List<PlanAction> Actions = new List<PlanAction>();
    }

    /// <summary>执行报告。</summary>
    public class ExecutionReport
    {
        public int Ok;        // 实际生效
        public int Skipped;   // 已是目标状态（幂等跳过）
        public int Fail;      // 失败
        public long ElapsedMs;
        public bool Refreshed;
        public bool UndoAvailable;
        public List<string> Errors = new List<string>();

        public string Summary
        {
            get
            {
                return $"完成：生效 {Ok}，跳过 {Skipped}，失败 {Fail}，耗时 {ElapsedMs}ms" +
                       (Refreshed ? "（含配对表重建）" : "") +
                       (UndoAvailable ? " · 可撤销" : "");
            }
        }
    }

    /// <summary>
    /// 事务执行器（阶段 2）：分帧写入 → 归并单次刷新 → 报告 → 可撤销。
    /// Update 泵驱动、全部主线程；执行期间禁止并发。
    /// </summary>
    public class Executor : MonoBehaviour
    {
        public static Executor Instance;

        private const int WriteBudgetPerFrame = 60;

        private ExecutionPlan currentPlan;
        private int cursor;
        private bool refreshing;
        private bool needsRefresh;
        private bool recordUndo;

        private ExecutionPlan undoPlan;
        private readonly ExecutionReport report = new ExecutionReport();
        private readonly Stopwatch sw = new Stopwatch();

        /// <summary>执行完成事件（UI 刷新用；支持多个订阅者）。</summary>
        public event Action OnCompleted;

        public bool IsRunning { get { return currentPlan != null; } }
        public string Stage { get; private set; }
        public ExecutionReport LastReport { get { return report; } }
        public bool UndoAvailable { get { return undoPlan != null && undoPlan.Actions.Count > 0; } }

        public float Progress
        {
            get
            {
                if (currentPlan == null) return 1f;
                if (refreshing) return 1f;
                return currentPlan.Actions.Count == 0 ? 1f : (float)cursor / currentPlan.Actions.Count;
            }
        }

        private void Awake()
        {
            Instance = this;
            Stage = "";
        }

        /// <summary>提交一个执行计划。返回 false = 已有任务在跑或游戏未就绪。</summary>
        public bool Execute(ExecutionPlan plan)
        {
            if (IsRunning || plan == null || plan.Actions.Count == 0) return false;
            if (!StationOps.GameReady) return false;
            return Begin(plan, recordUndo: true);
        }

        /// <summary>撤销上次执行（恢复反向操作；撤销本身不产生新的 undo）。</summary>
        public bool Undo()
        {
            if (IsRunning || undoPlan == null || undoPlan.Actions.Count == 0) return false;
            var plan = undoPlan;
            undoPlan = null;
            return Begin(plan, recordUndo: false);
        }

        private bool Begin(ExecutionPlan plan, bool recordUndo)
        {
            currentPlan = plan;
            this.recordUndo = recordUndo;
            cursor = 0;
            refreshing = false;
            needsRefresh = false;
            report.Ok = 0;
            report.Skipped = 0;
            report.Fail = 0;
            report.Errors.Clear();
            report.Refreshed = false;
            report.UndoAvailable = false;
            if (recordUndo) undoPlan = new ExecutionPlan { Title = "撤销：" + plan.Title };
            sw.Restart();
            Stage = "写入中…";
            return true;
        }

        private void Update()
        {
            if (currentPlan == null) return;
            try
            {
                if (!refreshing)
                {
                    int budget = WriteBudgetPerFrame;
                    while (cursor < currentPlan.Actions.Count && budget-- > 0)
                    {
                        var undo = ApplyOne(currentPlan.Actions[cursor++]);
                        if (recordUndo && undo != null) undoPlan.Actions.Add(undo);
                    }
                    if (cursor >= currentPlan.Actions.Count)
                    {
                        refreshing = true;
                        Stage = needsRefresh ? "重建配对表…" : "收尾…";
                    }
                }
                else
                {
                    if (needsRefresh)
                    {
                        StationOps.RefreshGalacticTraffic($"执行:{currentPlan.Title}");
                        report.Refreshed = true;
                    }
                    report.ElapsedMs = sw.ElapsedMilliseconds;
                    if (!recordUndo) undoPlan = null;                                             // 撤销不产生新链
                    if (recordUndo && (undoPlan == null || undoPlan.Actions.Count == 0)) undoPlan = null; // 空记录不保留
                    report.UndoAvailable = undoPlan != null && undoPlan.Actions.Count > 0;
                    StationFlowPlugin.LogInfo($"执行完成：{report.Summary}｜{currentPlan.Title}");
                    currentPlan = null;
                    Stage = "";
                    var cb = OnCompleted;
                    if (cb != null) cb();
                }
            }
            catch (Exception ex)
            {
                StationFlowPlugin.LogError($"Executor 异常: {ex}");
                report.Errors.Add(ex.Message);
                currentPlan = null;
                Stage = "";
                var cb = OnCompleted;
                if (cb != null) cb();
            }
        }

        /// <summary>应用单条操作，返回反向操作（用于 Undo）。</summary>
        private PlanAction ApplyOne(PlanAction a)
        {
            switch (a.Type)
            {
                case PlanActionType.AddP2P:
                {
                    if (StationOps.AddP2PRaw(a.Gid, a.GidB))
                    {
                        report.Ok++;
                        needsRefresh = true;
                        return new PlanAction { Type = PlanActionType.RemoveP2P, Gid = a.Gid, GidB = a.GidB };
                    }
                    report.Skipped++; // 已存在（幂等）
                    return null;
                }

                case PlanActionType.RemoveP2P:
                {
                    if (StationOps.RemoveP2PRaw(a.Gid, a.GidB))
                    {
                        report.Ok++;
                        needsRefresh = true;
                        return new PlanAction { Type = PlanActionType.AddP2P, Gid = a.Gid, GidB = a.GidB };
                    }
                    report.Skipped++;
                    return null;
                }

                case PlanActionType.AddRoute:
                {
                    if (StationOps.AddAstroRouteRaw(a.Gid, a.GidB, a.IntValue))
                    {
                        report.Ok++;
                        needsRefresh = true;
                        return new PlanAction { Type = PlanActionType.RemoveRoute, Gid = a.Gid, GidB = a.GidB, IntValue = a.IntValue };
                    }
                    report.Skipped++;
                    return null;
                }

                case PlanActionType.RemoveRoute:
                {
                    if (StationOps.RemoveAstroRouteRaw(a.Gid, a.GidB, a.IntValue))
                    {
                        report.Ok++;
                        needsRefresh = true;
                        return new PlanAction { Type = PlanActionType.AddRoute, Gid = a.Gid, GidB = a.GidB, IntValue = a.IntValue };
                    }
                    report.Skipped++;
                    return null;
                }

                case PlanActionType.SetBehavior:
                {
                    var st = StationOps.GetStation(a.Gid);
                    if (st == null) { report.Fail++; AddErr($"SetBehavior: 塔 #{a.Gid} 不存在"); return null; }
                    int old = (int)st.routePriority;
                    if (old == a.IntValue) { report.Skipped++; return null; }
                    st.routePriority = (ERemoteRoutePriority)a.IntValue;
                    report.Ok++;
                    needsRefresh = true; // 行为影响配对桶读取范围（含 Designated 的普配屏蔽），官方 UI 同样触发全量重建
                    return new PlanAction { Type = PlanActionType.SetBehavior, Gid = a.Gid, IntValue = old };
                }

                case PlanActionType.SetGroupBit:
                {
                    var st = StationOps.GetStation(a.Gid);
                    if (st == null) { report.Fail++; AddErr($"SetGroupBit: 塔 #{a.Gid} 不存在"); return null; }
                    bool old = (st.remoteGroupMask & (1L << a.IntValue)) != 0;
                    if (!StationOps.SetGroupBitRaw(st, a.IntValue, a.BoolValue)) { report.Skipped++; return null; }
                    report.Ok++;
                    needsRefresh = true;
                    return new PlanAction { Type = PlanActionType.SetGroupBit, Gid = a.Gid, IntValue = a.IntValue, BoolValue = old };
                }

                case PlanActionType.SetRemoteLogic:
                {
                    var st = StationOps.GetStation(a.Gid);
                    if (st == null || st.storage == null || a.IntValue < 0 || a.IntValue >= st.storage.Length)
                    {
                        report.Fail++; AddErr($"SetRemoteLogic: 塔 #{a.Gid} 槽位 {a.IntValue} 无效"); return null;
                    }
                    var old = (int)st.storage[a.IntValue].remoteLogic;
                    if (old == a.IntValue2) { report.Skipped++; return null; }
                    if (StationOps.SetSlotRemoteLogicRaw(a.Gid, a.IntValue, (ELogisticStorage)a.IntValue2))
                    {
                        report.Ok++;
                        needsRefresh = true;
                        return new PlanAction { Type = PlanActionType.SetRemoteLogic, Gid = a.Gid, IntValue = a.IntValue, IntValue2 = old };
                    }
                    report.Fail++;
                    return null;
                }

                case PlanActionType.Rename:
                {
                    var st = StationOps.GetStation(a.Gid);
                    if (st == null) { report.Fail++; AddErr($"Rename: 塔 #{a.Gid} 不存在"); return null; }
                    string old = StationOps.GetName(st);
                    if (old == a.StrValue) { report.Skipped++; return null; }
                    if (StationOps.SetName(st, a.StrValue))
                    {
                        report.Ok++;
                        return new PlanAction { Type = PlanActionType.Rename, Gid = a.Gid, StrValue = old };
                    }
                    report.Fail++;
                    return null;
                }

                default:
                    report.Fail++;
                    return null;
            }
        }

        private void AddErr(string msg)
        {
            if (report.Errors.Count < 20) report.Errors.Add(msg);
        }
    }
}
