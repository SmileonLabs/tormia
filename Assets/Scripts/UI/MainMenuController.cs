using Tormia.Characters;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.UI
{
    public class MainMenuController : MonoBehaviour
    {
        [SerializeField] private GameObject mainMenuCanvas;
        [SerializeField] private GameObject worldRoot;
        [SerializeField] private Button startButton;
        [SerializeField] private Button exitButton;
        [SerializeField] private CharacterAppearanceApplier worldCharacterAppearance;

        private void Awake()
        {
            AddClick(startButton, StartWorld);
            AddClick(exitButton, ExitGame);
            ShowMainMenu();
        }

        public void StartWorld()
        {
            if (worldCharacterAppearance != null)
            {
                worldCharacterAppearance.ApplySavedAppearance();
            }

            SetActive(mainMenuCanvas, false);
            SetActive(worldRoot, true);
        }

        public void ShowMainMenu()
        {
            SetActive(mainMenuCanvas, true);
            SetActive(worldRoot, false);
            ConfigureCanvas(mainMenuCanvas, 10);
        }

        private void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private static void AddClick(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null)
            {
                target.SetActive(active);
            }
        }

        private static void ConfigureCanvas(GameObject canvasObject, int sortingOrder)
        {
            if (canvasObject == null)
            {
                return;
            }

            var canvas = canvasObject.GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.enabled = true;
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = sortingOrder;
                canvas.targetDisplay = 0;
            }

            var raycaster = canvasObject.GetComponent<GraphicRaycaster>();
            if (raycaster != null)
            {
                raycaster.enabled = true;
            }

            var rectTransform = canvasObject.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.zero;
                rectTransform.pivot = new Vector2(0.5f, 0.5f);
                rectTransform.sizeDelta = new Vector2(1920f, 1080f);
                rectTransform.anchoredPosition = new Vector2(1280f, 720f);
                rectTransform.localScale = Vector3.one;
                rectTransform.localRotation = Quaternion.identity;
            }
        }

    }
}
