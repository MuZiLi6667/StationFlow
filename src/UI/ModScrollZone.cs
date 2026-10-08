using System.Collections.Generic;
using UnityEngine;

namespace StationFlow.UI
{
    /// <summary>
    /// 把 mod 的滚动区域注册进游戏自带的 UIScrollZone 列表。
    /// 游戏 VFInput.OnUpdate 会检测鼠标是否处于任意注册区域内（UIScrollZone.inScroll），
    /// 命中时把滚轮输入归零——这样在 mod 面板里滚动鼠标不会缩放游戏镜头。
    /// 注意：通过 Krafs.Publicizer 访问 UIScrollZone 的 private static 列表（Mono 运行时允许）。
    /// </summary>
    public class ModScrollZone : MonoBehaviour
    {
        private UIScrollZone.Zone zone;
        private bool registered;

        private void OnEnable()
        {
            var rt = transform as RectTransform;
            if (rt == null) return;
            // Overlay Canvas：cam 传 null（RectTransformUtility 对 Overlay 的约定）
            zone = new UIScrollZone.Zone(rt, null);
            Register();
        }

        private void OnDisable()
        {
            Unregister();
        }

        private void Register()
        {
            if (registered) return;
            try
            {
                if (UIScrollZone.allScrollTrans == null)
                {
                    UIScrollZone.allScrollTrans = new List<UIScrollZone.Zone>(64);
                }
                UIScrollZone.allScrollTrans.Add(zone);
                registered = true;
            }
            catch (System.Exception ex)
            {
                StationFlowPlugin.LogWarn($"注册滚动区失败: {ex.Message}");
            }
        }

        private void Unregister()
        {
            if (!registered) return;
            try
            {
                var list = UIScrollZone.allScrollTrans;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].trans == zone.trans)
                        {
                            list.RemoveAt(i);
                            i--;
                        }
                    }
                }
            }
            catch { }
            registered = false;
        }
    }
}
