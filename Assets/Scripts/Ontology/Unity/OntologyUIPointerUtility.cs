using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Tormia.Ontology.Core
{
    public interface IOntologyGameplayInputSurface
    {
    }

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
            return IsPointerOverUi(screenPosition, false);
        }

        public static bool IsPointerOverBlockingUi()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null &&
                IsPointerOverUi(Mouse.current.position.ReadValue(), true))
                return true;

            if (Touchscreen.current != null)
            {
                var touch = Touchscreen.current.primaryTouch;
                if (touch.press.isPressed &&
                    IsPointerOverUi(touch.position.ReadValue(), true))
                    return true;
            }
#endif
            return false;
        }

        private static bool IsPointerOverUi(
            Vector2 screenPosition,
            bool ignoreGameplayInputSurfaces)
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
            if (!ignoreGameplayInputSurfaces)
                return RaycastResults.Count > 0;

            foreach (var result in RaycastResults)
            {
                var behaviours = result.gameObject == null
                    ? null
                    : result.gameObject.GetComponentsInParent<
                        MonoBehaviour>(true);
                var gameplaySurface = behaviours != null &&
                                      System.Array.Exists(
                                          behaviours,
                                          value => value is
                                              IOntologyGameplayInputSurface);
                if (!gameplaySurface)
                    return true;
            }

            return false;
        }
    }
}
