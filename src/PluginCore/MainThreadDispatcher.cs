using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace StationFlow.PluginCore
{
    /// <summary>
    /// 主线程调度器：所有游戏对象（StationComponent/PlanetFactory 等）的操作
    /// 必须经由此调度器排队到 Unity 主线程执行，避免与游戏的多线程工厂模拟竞态。
    /// </summary>
    public class MainThreadDispatcher : MonoBehaviour
    {
        private static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
        private static MainThreadDispatcher instance;
        private static int mainThreadId;

        /// <summary>在插件启动时调用一次。</summary>
        public static void Ensure()
        {
            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            if (instance != null) return;
            var go = new GameObject("StationFlow_MainThreadDispatcher");
            UnityEngine.Object.DontDestroyOnLoad(go);
            instance = go.AddComponent<MainThreadDispatcher>();
        }

        /// <summary>当前是否在主线程。</summary>
        public static bool IsMainThread
        {
            get { return System.Threading.Thread.CurrentThread.ManagedThreadId == mainThreadId; }
        }

        /// <summary>排入主线程队列（异步）。</summary>
        public static void Enqueue(Action action)
        {
            queue.Enqueue(action);
        }

        /// <summary>在主线程同步执行（若已在主线程则立即执行，否则排队等下一帧）。</summary>
        public static void Run(Action action)
        {
            if (IsMainThread) action();
            else queue.Enqueue(action);
        }

        private void Update()
        {
            while (queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    StationFlowPlugin.LogError($"主线程任务执行异常: {ex}");
                }
            }
        }
    }
}
