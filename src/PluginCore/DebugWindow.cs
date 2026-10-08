using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StationFlow.VanillaBridge;

namespace StationFlow.PluginCore
{
    /// <summary>
    /// 阶段 0 调试窗（F9）：塔清单、详情、改名/P2P/行为/分组验证操作、CSV 导出。
    /// 仅开发验证用，正式功能（阶段 1+）将使用自建 uGUI 面板。
    /// </summary>
    public class DebugWindow : MonoBehaviour
    {
        private Rect windowRect = new Rect(40, 40, 1080, 700);
        private Vector2 listScroll;
        private readonly List<StationComponent> stations = new List<StationComponent>();
        private float lastRebuildTime = -10f;
        private bool listDirty = true;

        private int selectedGid = 0;
        private int p2pA = 0;
        private int p2pB = 0;
        private string nameInput = "";
        private string groupBitInput = "0";
        private string statusMessage = "";

        private GUIStyle labelStyle;
        private GUIStyle rowStyle;

        // 透明输入阻挡层：IMGUI 窗口不参与游戏的 uGUI EventSystem，
        // 会导致点击穿透到游戏世界。用一个与窗口同位置的近透明 uGUI Image
        // 挡住指针，让游戏的世界点击检测（EventSystem 射线）认为指针在 UI 上。
        private GameObject blockerGo;
        private RectTransform blockerRt;

        private const float RowHeight = 20f;
        private const float ListViewHeight = 520f;
        private const float ListWidth = 430f;

        private void EnsureStyles()
        {
            if (labelStyle != null) return;
            // 加载系统中文字体，避免塔名乱码（开发机 Windows 自带微软雅黑）
            try
            {
                var osFont = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 13);
                labelStyle = new GUIStyle(GUI.skin.label) { font = osFont, wordWrap = false, clipping = TextClipping.Clip };
                rowStyle = new GUIStyle(GUI.skin.button) { font = osFont, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
            }
            catch
            {
                labelStyle = new GUIStyle(GUI.skin.label);
                rowStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
            }
        }

        private void RebuildList()
        {
            stations.Clear();
            if (StationOps.GameReady)
            {
                foreach (var st in StationOps.AllStations()) stations.Add(st);
            }
            listDirty = false;
            lastRebuildTime = Time.unscaledTime;
        }

        private void OnGUI()
        {
            if (!StationFlowPlugin.DebugWindowVisible)
            {
                if (blockerGo != null && blockerGo.activeSelf) blockerGo.SetActive(false);
                return;
            }
            EnsureStyles();
            EnsureBlocker();
            if (blockerGo != null && !blockerGo.activeSelf) blockerGo.SetActive(true);

            // 每秒自动重建一次列表（塔可能被拆建）
            if (listDirty || Time.unscaledTime - lastRebuildTime > 1f) RebuildList();

            windowRect = GUI.Window(991001, windowRect, DrawWindow, "StationFlow Debug (按 F9 或 Close 关闭)");
            SyncBlocker();
        }

        /// <summary>创建透明阻挡层（独立 Overlay Canvas，ConstantPixelSize 保证 1 单位=1 像素）。</summary>
        private void EnsureBlocker()
        {
            if (blockerGo != null) return;
            try
            {
                var canvasGo = new GameObject("StationFlow_InputBlocker");
                Object.DontDestroyOnLoad(canvasGo);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000; // 盖在游戏 UI 之上
                var scaler = canvasGo.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                canvasGo.AddComponent<GraphicRaycaster>();

                var imgGo = new GameObject("Blocker", typeof(RectTransform), typeof(Image));
                imgGo.transform.SetParent(canvasGo.transform, false);
                var img = imgGo.GetComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0.001f); // 近乎全透明但参与射线检测
                img.raycastTarget = true;
                blockerRt = imgGo.GetComponent<RectTransform>();
                blockerRt.anchorMin = new Vector2(0f, 1f); // 锚定屏幕左上角（与 IMGUI 坐标系一致）
                blockerRt.anchorMax = new Vector2(0f, 1f);
                blockerRt.pivot = new Vector2(0f, 1f);

                blockerGo = canvasGo;
                StationFlowPlugin.LogInfo("输入阻挡层已创建");
            }
            catch (System.Exception ex)
            {
                StationFlowPlugin.LogError($"创建输入阻挡层失败: {ex}");
            }
        }

        /// <summary>把阻挡层同步到当前窗口位置（每帧调用，跟随拖动）。</summary>
        private void SyncBlocker()
        {
            if (blockerRt == null) return;
            blockerRt.anchoredPosition = new Vector2(windowRect.x, -windowRect.y);
            blockerRt.sizeDelta = new Vector2(windowRect.width, windowRect.height);
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            // ===== 顶栏 =====
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Towers: {stations.Count}", labelStyle, GUILayout.Width(110));
            if (GUILayout.Button("Refresh", GUILayout.Width(80))) { listDirty = true; }
            if (GUILayout.Button("Dump CSV", GUILayout.Width(90)))
            {
                try
                {
                    string p = DumpTool.Dump();
                    statusMessage = p != null ? $"Dumped: {p}" : "Dump 失败：游戏未就绪";
                }
                catch (System.Exception ex) { statusMessage = "Dump 异常: " + ex.Message; }
            }
            if (GUILayout.Button("Close (F9)", GUILayout.Width(85)))
            {
                StationFlowPlugin.LogInfo("Close 按钮点击，关闭调试窗");
                StationFlowPlugin.DebugWindowVisible = false;
            }
            GUILayout.Label(statusMessage, labelStyle);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            // ===== 左：塔列表（虚拟滚动） =====
            GUILayout.BeginVertical(GUILayout.Width(ListWidth));
            Rect viewRect = GUILayoutUtility.GetRect(ListWidth, ListViewHeight, GUILayout.Width(ListWidth), GUILayout.Height(ListViewHeight));
            float contentHeight = Mathf.Max(ListViewHeight, stations.Count * RowHeight);
            listScroll = GUI.BeginScrollView(viewRect, listScroll, new Rect(0, 0, ListWidth - 18, contentHeight));

            int first = Mathf.Max(0, (int)(listScroll.y / RowHeight) - 1);
            int last = Mathf.Min(stations.Count - 1, (int)((listScroll.y + ListViewHeight) / RowHeight) + 1);
            for (int i = first; i <= last; i++)
            {
                var st = stations[i];
                if (st == null || st.id <= 0) continue;
                string txt = $"#{st.gid}  {Shorten(StationOps.GetName(st), 14)}  {Shorten(StationOps.PlanetName(st.planetId), 8)}  {st.routePriority}";
                var style = st.gid == selectedGid ? rowStyle : GUI.skin.button;
                if (GUI.Button(new Rect(0, i * RowHeight, ListWidth - 20, RowHeight - 1), txt, style))
                {
                    selectedGid = st.gid;
                    nameInput = StationOps.GetName(st);
                }
            }
            GUI.EndScrollView();
            GUILayout.EndVertical();

            // ===== 右：选中塔详情与操作 =====
            GUILayout.BeginVertical();
            var sel = StationOps.GetStation(selectedGid);
            if (sel == null)
            {
                GUILayout.Label("点击左侧列表选择一个物流塔", labelStyle);
            }
            else
            {
                GUILayout.Label($"gid={sel.gid}  entityId={sel.entityId}  behavior={sel.routePriority}", labelStyle);
                GUILayout.Label($"planet={sel.planetId} ({StationOps.PlanetName(sel.planetId)})  stellar={sel.isStellar}  collector={sel.isCollector}", labelStyle);
                GUILayout.Label($"groupMask={sel.remoteGroupMask}  P2P routes(of this tower)={StationOps.CountP2PRoutesOf(sel.gid)}", labelStyle);
                GUILayout.Label($"name(raw)=「{Shorten(StationOps.GetName(sel), 40)}」", labelStyle);

                // 桶计数
                var off = sel.remotePairOffsets;
                if (off != null && off.Length >= 7)
                {
                    GUILayout.Label($"pairs: total={off[6]}  normal={off[1] - off[0]}  P2P={off[2] - off[1]}", labelStyle);
                    GUILayout.Label($"  planet-route={off[3] - off[2]}  astro-route={off[4] - off[3]}  group={off[5] - off[4]}  fallback={off[6] - off[5]}", labelStyle);
                }

                GUILayout.Space(4);

                // ---- 改名 ----
                GUILayout.BeginHorizontal();
                GUILayout.Label("Name:", labelStyle, GUILayout.Width(45));
                nameInput = GUILayout.TextField(nameInput ?? "", GUILayout.Width(200));
                if (GUILayout.Button("Set", GUILayout.Width(45)))
                {
                    statusMessage = StationOps.SetName(sel, nameInput) ? $"已改名为「{nameInput.Trim()}」" : "改名失败";
                }
                if (GUILayout.Button("Clear", GUILayout.Width(55)))
                {
                    StationOps.SetName(sel, "");
                    nameInput = "";
                    statusMessage = "已清空名字";
                }
                GUILayout.EndHorizontal();

                // ---- 行为 ----
                GUILayout.BeginHorizontal();
                GUILayout.Label("Behavior:", labelStyle, GUILayout.Width(65));
                foreach (ERemoteRoutePriority p in new[] { ERemoteRoutePriority.Ignore, ERemoteRoutePriority.Prioritize, ERemoteRoutePriority.Only, ERemoteRoutePriority.Designated })
                {
                    bool isCur = sel.routePriority == p;
                    string label = (isCur ? "● " : "") + p.ToString();
                    if (GUILayout.Button(label, GUILayout.Width(110)) && !isCur)
                    {
                        StationOps.SetBehavior(sel, p);
                        StationOps.RefreshGalacticTraffic("调试窗-改行为"); // 行为影响配对桶读取范围，与官方 UI 一致重建
                        statusMessage = $"行为已设为 {p}（已重建配对表）";
                    }
                }
                GUILayout.EndHorizontal();

                // ---- 分组 ----
                GUILayout.BeginHorizontal();
                GUILayout.Label("Group bit:", labelStyle, GUILayout.Width(65));
                groupBitInput = GUILayout.TextField(groupBitInput, GUILayout.Width(40));
                if (GUILayout.Button("Toggle", GUILayout.Width(70)))
                {
                    if (int.TryParse(groupBitInput, out int bit) && bit >= 0 && bit <= 62)
                    {
                        StationOps.ToggleGroup(sel, bit);
                        statusMessage = $"已切换分组位 {bit} → mask={sel.remoteGroupMask}";
                    }
                    else statusMessage = "分组位需为 0~62 的整数";
                }
                GUILayout.Label($"mask={sel.remoteGroupMask}", labelStyle);
                GUILayout.EndHorizontal();

                GUILayout.Space(4);

                // ---- P2P 验证（两塔选择） ----
                GUILayout.Label($"P2P A = #{p2pA} (routes={StationOps.CountP2PRoutesOf(p2pA)})   B = #{p2pB} (routes={StationOps.CountP2PRoutesOf(p2pB)})", labelStyle);
                GUILayout.Label("提示：P2P 配对桶生效需两塔互补供需（同物品·一供一需·跨星球）", labelStyle);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"Set A = 当前选中 (#{sel.gid})", GUILayout.Width(180))) { p2pA = sel.gid; }
                if (GUILayout.Button($"Set B = 当前选中 (#{sel.gid})", GUILayout.Width(180))) { p2pB = sel.gid; }
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Add P2P (A-B)", GUILayout.Width(120)))
                {
                    bool ok = StationOps.AddP2P(p2pA, p2pB);
                    statusMessage = ok ? $"路由已建 #{p2pA} ↔ #{p2pB}；看两塔 pairP2P 桶计数与船只行为" : "添加失败（无效 gid / 已存在 / 同一塔）";
                }
                if (GUILayout.Button("Remove P2P (A-B)", GUILayout.Width(130)))
                {
                    bool ok = StationOps.RemoveP2P(p2pA, p2pB);
                    statusMessage = ok ? $"已移除 P2P #{p2pA} ↔ #{p2pB}" : "移除失败（不存在）";
                }
                if (GUILayout.Button("Check", GUILayout.Width(80)))
                {
                    statusMessage = $"route exists = {StationOps.HasP2P(p2pA, p2pB)}";
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                // ---- 槽位表 ----
                GUILayout.Label("Storage slots:", labelStyle);
                if (sel.storage != null)
                {
                    for (int i = 0; i < sel.storage.Length; i++)
                    {
                        var s = sel.storage[i];
                        if (s.itemId <= 0) continue;
                        GUILayout.Label($"  [{i}] item={s.itemId}  {s.count}/{s.max}  local={s.localLogic}  remote={s.remoteLogic}", labelStyle);
                    }
                }
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        private static string Shorten(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "＜未命名＞";
            s = s.Replace('\n', ' ');
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
