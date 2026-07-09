using UnityEngine;
using UnityEngine.EventSystems;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyUIDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        [SerializeField] private RectTransform targetRect;
        [SerializeField] private bool constrainToParent = true;

        private RectTransform parentRect;
        private Vector2 pointerOffset;
        private readonly Vector3[] targetCorners = new Vector3[4];

        private RectTransform TargetRect => targetRect != null ? targetRect : transform as RectTransform;

        public void OnBeginDrag(PointerEventData eventData)
        {
            var target = TargetRect;
            if (target == null)
            {
                return;
            }

            parentRect = target.parent as RectTransform;
            if (parentRect == null)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out var localPointer))
            {
                pointerOffset = target.anchoredPosition - localPointer;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            var target = TargetRect;
            if (target == null || parentRect == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, eventData.pressEventCamera, out var localPointer))
            {
                return;
            }

            var nextPosition = localPointer + pointerOffset;
            target.anchoredPosition = nextPosition;
            if (constrainToParent)
            {
                target.anchoredPosition += GetParentClampOffset(target);
            }
        }

        private Vector2 GetParentClampOffset(RectTransform target)
        {
            var parent = target.parent as RectTransform;
            if (parent == null)
            {
                return Vector2.zero;
            }

            var parentRect = parent.rect;
            target.GetWorldCorners(targetCorners);

            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var i = 0; i < targetCorners.Length; i++)
            {
                var localCorner = parent.InverseTransformPoint(targetCorners[i]);
                min = Vector2.Min(min, localCorner);
                max = Vector2.Max(max, localCorner);
            }

            var offset = Vector2.zero;
            if (min.x < parentRect.xMin)
            {
                offset.x += parentRect.xMin - min.x;
            }
            if (max.x > parentRect.xMax)
            {
                offset.x -= max.x - parentRect.xMax;
            }

            if (min.y < parentRect.yMin)
            {
                offset.y += parentRect.yMin - min.y;
            }
            if (max.y > parentRect.yMax)
            {
                offset.y -= max.y - parentRect.yMax;
            }

            return offset;
        }
    }
}
