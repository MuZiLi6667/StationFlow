using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using StationFlow.Exec;
using StationFlow.PluginCore;
using StationFlow.UI;

namespace StationFlow
{
    /// <summary>
    /// StationFlow 插件入口：星际物流塔自动化管理 mod。
    /// 阶段 1：F8 主面板（总览+批量命名）、F9 调试窗。
    /// 阶段 2：规则配对（模式一·命名规则驱动）+ 事务执行器。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class StationFlowPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "LILINGLONG.dsp.StationFlow";
        public const string PluginName = "StationFlow";
        public const string PluginVersion = "0.2.0";

        internal static BepInEx.Logging.ManualLogSource LogSource;
        internal static bool DebugWindowVisible = false;
        internal static ConfigEntry<float> CfgPanelX;
        internal static ConfigEntry<float> CfgPanelY;

        // 规则配对偏好（重启记忆）
        internal static ConfigEntry<int> CfgPairSource;    // 0=按名字 1=按槽位
        internal static ConfigEntry<int> CfgPairMethod;    // 0=点对点 1=星球航路 2=行星系航路
        internal static ConfigEntry<int> CfgTopK;
        internal static ConfigEntry<float> CfgMinRatio;
        internal static ConfigEntry<bool> CfgCrossAstro;

        // 自动巡检（持续自动模式）
        internal static ConfigEntry<bool> CfgAutoEnabled;
        internal static ConfigEntry<int> CfgAutoMaxActions;

        private DebugWindow debugWindow;

        private void Awake()
        {
            LogSource = Logger;
            LogInfo($"{PluginName} v{PluginVersion} 正在加载…");
            try
            {
                MainThreadDispatcher.Ensure();
                CfgPanelX = Config.Bind("UI", "PanelX", 60f, "管理面板初始 X 位置（像素，左上角原点）");
                CfgPanelY = Config.Bind("UI", "PanelY", 60f, "管理面板初始 Y 位置（像素，左上角原点）");
                CfgPairSource = Config.Bind("Rule", "PairSource", 0, "数据源：0=按名字 1=按槽位");
                CfgPairMethod = Config.Bind("Rule", "PairMethod", 0, "配对方式：0=点对点 1=星球航路 2=行星系航路");
                CfgTopK = Config.Bind("Rule", "TopK", 3, "每个需求塔最多配对几个供应塔（1~5）");
                CfgMinRatio = Config.Bind("Rule", "MinSupplyRatio", 0.2f, "最小供应能力占需求容量的比例（0.1~0.5）");
                CfgCrossAstro = Config.Bind("Rule", "AllowCrossAstro", true, "是否允许跨行星系配对");
                CfgAutoEnabled = Config.Bind("Auto", "Enabled", false, "自动巡检：发现新建塔时自动增量配对（默认关）");
                CfgAutoMaxActions = Config.Bind("Auto", "MaxActionsPerSweep", 30, "每次自动巡检最多执行的配对数（1~200）");
                debugWindow = gameObject.AddComponent<DebugWindow>();
                gameObject.AddComponent<Executor>();
                gameObject.AddComponent<AutoPilot>();
                gameObject.AddComponent<MainPanel>();
                LogInfo("初始化完成（F8 主面板[总览/规则配对/物流网/诊断/批量工具] / F9 调试窗）。");
            }
            catch (System.Exception ex)
            {
                // 启动失败绝不能影响游戏本体
                LogError($"初始化失败（mod 将被禁用）: {ex}");
            }
        }

        private void Update()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.F8))
                {
                    MainPanel.Instance?.Toggle();
                }
                // 调试窗（阶段 0 验证工具，保留）
                if (Input.GetKeyDown(KeyCode.F9))
                {
                    DebugWindowVisible = !DebugWindowVisible;
                    LogInfo($"调试窗 {(DebugWindowVisible ? "打开" : "关闭")}");
                }
            }
            catch (System.Exception ex)
            {
                LogError($"热键处理异常: {ex}");
            }
        }

        // ---- 日志辅助（统一 [StationFlow] 前缀，便于在 LogOutput.log 中过滤）----
        public static void LogInfo(string msg) { LogSource?.LogInfo($"[StationFlow] {msg}"); }
        public static void LogWarn(string msg) { LogSource?.LogWarning($"[StationFlow] {msg}"); }
        public static void LogError(string msg) { LogSource?.LogError($"[StationFlow] {msg}"); }
    }
}
