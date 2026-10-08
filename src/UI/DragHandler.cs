using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace StationFlow.UI
{
    /// <summary>uGUI 面板拖动（挂在标题栏上，拖动目标面板）。</summary>
    public class DragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform Target;
        public Action OnEndMove;

        private Vector2 startPos;
        private Vector2 startMouse;

        public void OnBeginDrag(PointerEventData e)
        {
            if (Target == null) return;
            startPos = Target.anchoredPosition;
            startMouse = e.position;
        }

        public void OnDrag(PointerEventData e)
        {
            if (Target == null) return;
            Target.anchoredPosition = startPos + (e.position - startMouse);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (OnEndMove != null) OnEndMove();
        }
    }
}
