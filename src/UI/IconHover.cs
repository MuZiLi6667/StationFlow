using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StationFlow.UI
{
    /// <summary>图标悬停提示：鼠标进入时把提示文本发给面板状态栏（轻量 tooltip）。</summary>
    public class IconHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public static Action<string> OnHoverText;

        public string Tip;

        public void OnPointerEnter(PointerEventData e)
        {
            if (!string.IsNullOrEmpty(Tip) && OnHoverText != null) OnHoverText(Tip);
        }

        public void OnPointerExit(PointerEventData e)
        {
            if (OnHoverText != null) OnHoverText("");
        }
    }
}
