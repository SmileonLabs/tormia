using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only lifecycle and safe-area owner for mobile gameplay
    /// controls. OnScreenControls emit project Input Actions; this component
    /// never moves an actor or creates a gameplay result.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyMobileGameplayControls : MonoBehaviour,
        IOntologyGameplayInputSurface
    {
        [SerializeField] private OntologyGameSessionCoordinator sessionCoordinator;
        [SerializeField] private RectTransform controlsRoot;
        [SerializeField] private bool showInEditor;
        [SerializeField] private bool hideDuringWorldEditing = true;

        private OntologyGameSessionCoordinator subscribedCoordinator;
        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;

        public bool ControlsVisible =>
            controlsRoot != null && controlsRoot.gameObject.activeSelf;

        private void Awake()
        {
            ResolveDependencies();
            Subscribe();
            ApplyState();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            Subscribe();
            ApplyState();
        }

        private void Update()
        {
            if (hideDuringWorldEditing)
                ApplyVisibility();
            ApplySafeArea();
        }

        private void OnDisable()
        {
            if (subscribedCoordinator != null)
                subscribedCoordinator.StateChanged -= OnSessionStateChanged;
            subscribedCoordinator = null;
        }

        private void ResolveDependencies()
        {
            if (controlsRoot == null && transform.childCount > 0)
                controlsRoot = transform.GetChild(0) as RectTransform;
            if (sessionCoordinator == null)
                sessionCoordinator = FindAnyObjectByType<
                    OntologyGameSessionCoordinator>();
        }

        private void Subscribe()
        {
            if (subscribedCoordinator == sessionCoordinator) return;
            if (subscribedCoordinator != null)
                subscribedCoordinator.StateChanged -= OnSessionStateChanged;
            subscribedCoordinator = sessionCoordinator;
            if (subscribedCoordinator != null)
                subscribedCoordinator.StateChanged += OnSessionStateChanged;
        }

        private void OnSessionStateChanged(
            OntologyGameSessionState previous,
            OntologyGameSessionState next)
        {
            ApplyVisibility();
        }

        private void ApplyState()
        {
            ApplyVisibility();
            ApplySafeArea(force: true);
        }

        private void ApplyVisibility()
        {
            if (controlsRoot == null) return;
            var editing = hideDuringWorldEditing &&
                          (OntologyRuntimeObjectPlacementController
                               .IsPlacementInputCaptured ||
                           OntologyRuntimeWorldEditorController
                               .IsEditInputCaptured);
            var visible = IsMobilePresentationEnabled(
                              Application.platform,
                              showInEditor) &&
                          sessionCoordinator != null &&
                          sessionCoordinator.IsInWorld &&
                          !editing;
            if (controlsRoot.gameObject.activeSelf != visible)
                controlsRoot.gameObject.SetActive(visible);
        }

        private void ApplySafeArea(bool force = false)
        {
            if (controlsRoot == null || Screen.width <= 0 || Screen.height <= 0)
                return;
            var safeArea = Screen.safeArea;
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            if (!force && safeArea == lastSafeArea && screenSize == lastScreenSize)
                return;

            lastSafeArea = safeArea;
            lastScreenSize = screenSize;
            CalculateSafeAreaAnchors(
                safeArea,
                screenSize,
                out var anchorMin,
                out var anchorMax);
            controlsRoot.anchorMin = anchorMin;
            controlsRoot.anchorMax = anchorMax;
            controlsRoot.offsetMin = Vector2.zero;
            controlsRoot.offsetMax = Vector2.zero;
        }

        public static bool IsMobilePresentationEnabled(
            RuntimePlatform platform,
            bool editorPreview)
        {
            return platform == RuntimePlatform.Android ||
                   platform == RuntimePlatform.IPhonePlayer ||
                   (editorPreview &&
                    (platform == RuntimePlatform.WindowsEditor ||
                     platform == RuntimePlatform.OSXEditor ||
                     platform == RuntimePlatform.LinuxEditor));
        }

        public static void CalculateSafeAreaAnchors(
            Rect safeArea,
            Vector2Int screenSize,
            out Vector2 anchorMin,
            out Vector2 anchorMax)
        {
            if (screenSize.x <= 0 || screenSize.y <= 0)
            {
                anchorMin = Vector2.zero;
                anchorMax = Vector2.one;
                return;
            }

            anchorMin = new Vector2(
                Mathf.Clamp01(safeArea.xMin / screenSize.x),
                Mathf.Clamp01(safeArea.yMin / screenSize.y));
            anchorMax = new Vector2(
                Mathf.Clamp01(safeArea.xMax / screenSize.x),
                Mathf.Clamp01(safeArea.yMax / screenSize.y));
        }
    }
}
