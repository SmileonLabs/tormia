using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Tormia.Ontology.Core
{
    public static class OntologyUIPointerUtility
    {
        private static readonly List<RaycastResult> RaycastResults = new();

        public static bool IsPointerOverUi()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && IsPointerOverUi(Mouse.current.position.ReadValue()))
            {
                return true;
            }

            if (Touchscreen.current != null)
            {
                var touch = Touchscreen.current.primaryTouch;
                if (touch.press.isPressed && IsPointerOverUi(touch.position.ReadValue()))
                {
                    return true;
                }
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return IsPointerOverUi(Input.mousePosition);
#else
            return false;
#endif
        }

        public static bool IsPointerOverUi(Vector2 screenPosition)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            RaycastResults.Clear();
            var eventData = new PointerEventData(EventSystem.current)
            {
                position = screenPosition
            };
            EventSystem.current.RaycastAll(eventData, RaycastResults);
            return RaycastResults.Count > 0;
        }
    }
}
