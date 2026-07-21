using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Binds a hierarchy-authored contextual edit handle to the selected world object.
    /// The prefab owns all visual layout, sprites, and button hierarchy; this component
    /// only supplies behavior and screen positioning.
    /// </summary>
    public sealed class OntologyRuntimeWorldEditHandle : MonoBehaviour
    {
        [SerializeField] private OntologyRuntimeWorldEditorController controller;
        [Header("Hierarchy Bindings (assign in Inspector)")]
        [SerializeField] private RectTransform panel;
        [SerializeField] private Button moveButton;
        [SerializeField] private Button rotateLeftButton;
        [SerializeField] private Button rotateRightButton;
        [SerializeField] private Button scaleDownButton;
        [SerializeField] private Button scaleUpButton;
        [SerializeField] private Button duplicateButton;
        [SerializeField] private Button deleteButton;
        [SerializeField] private Button ontologyButton;

        private Transform target;
        private bool bound;
        private RectTransform[] controlRects = System.Array.Empty<RectTransform>();
        private readonly Vector3[] worldCorners = new Vector3[4];

        public void Configure(OntologyRuntimeWorldEditorController owner)
        {
            controller = owner;
            bound = false;
            Bind();
        }

        private void Awake()
        {
            Bind();
            SetTarget(null);
        }

        /// <summary>
        /// One-time editor helper for a prefab authored from the documented hierarchy.
        /// It is never used to create UI at runtime.
        /// </summary>
        [ContextMenu("Migrate Context Handle Bindings")]
        public void MigrateLegacyHierarchyBindings()
        {
#if UNITY_EDITOR
            UnityEditor.Undo.RecordObject(this, "Migrate world edit handle bindings");
            panel = transform.Find("HandlePanel") as RectTransform;
            moveButton = panel?.Find("MoveButton")?.GetComponent<Button>();
            rotateLeftButton = panel?.Find("RotateLeftButton")?.GetComponent<Button>();
            rotateRightButton = panel?.Find("RotateRightButton")?.GetComponent<Button>();
            scaleDownButton = panel?.Find("ScaleDownButton")?.GetComponent<Button>();
            scaleUpButton = panel?.Find("ScaleUpButton")?.GetComponent<Button>();
            duplicateButton = panel?.Find("DuplicateButton")?.GetComponent<Button>();
            deleteButton = panel?.Find("DeleteButton")?.GetComponent<Button>();
            ontologyButton = panel?.Find("OntologyButton")?.GetComponent<Button>();
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }

        public void SetTarget(Transform value)
        {
            target = value;
            // Selection can be established after the controller's Awake order.
            // Rebind here so the authored buttons always have exactly one
            // runtime callback, without creating another UI handle.
            bound = false;
            Bind();
            if (panel != null)
                panel.gameObject.SetActive(value != null);
        }

        private void Bind()
        {
            if (bound) return;
            bound = panel != null;
            if (!bound) return;

            BindButton(moveButton, () => controller?.BeginMove());
            BindButton(rotateLeftButton, () => controller?.RotateSelection(-15f));
            BindButton(rotateRightButton, () => controller?.RotateSelection(15f));
            BindButton(scaleDownButton, () => controller?.ScaleSelection(-.1f));
            BindButton(scaleUpButton, () => controller?.ScaleSelection(.1f));
            BindButton(duplicateButton, () => controller?.DuplicateSelection());
            BindButton(deleteButton, () => controller?.DeleteSelection());
            BindButton(ontologyButton, () => controller?.OpenOntologyEditorFor(target));

            controlRects = new[]
            {
                moveButton?.transform as RectTransform,
                rotateLeftButton?.transform as RectTransform,
                rotateRightButton?.transform as RectTransform,
                scaleDownButton?.transform as RectTransform,
                scaleUpButton?.transform as RectTransform,
                duplicateButton?.transform as RectTransform,
                deleteButton?.transform as RectTransform,
                ontologyButton?.transform as RectTransform
            };
        }

        private static void BindButton(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null) return;

            // The icon is the authored visual/click target.  The Button root is
            // intentionally transparent, so moving the Icon child in the
            // hierarchy also moves the effective hit area instead of leaving a
            // stale 100x100 invisible click rectangle behind.
            var rootGraphic = button.GetComponent<Graphic>();
            if (rootGraphic != null)
                rootGraphic.raycastTarget = false;

            Graphic visualGraphic = null;
            foreach (var graphic in button.GetComponentsInChildren<Graphic>(true))
            {
                if (graphic == null || graphic == rootGraphic || !graphic.gameObject.activeSelf)
                    continue;
                graphic.raycastTarget = true;
                visualGraphic ??= graphic;
            }

            if (visualGraphic != null)
                button.targetGraphic = visualGraphic;

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
        }

        private void LateUpdate()
        {
            if (target == null || panel == null || controller == null || !controller.IsEditing)
            {
                if (panel != null) panel.gameObject.SetActive(false);
                return;
            }

            var camera = controller.EditCamera;
            if (camera == null) return;
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);

            var screen = camera.WorldToScreenPoint(bounds.center);
            panel.gameObject.SetActive(screen.z > 0f);
            if (screen.z <= 0f) return;

            // The authored HandlePanel can be stretched to the canvas while its
            // buttons are laid out as a compact group. Move by the difference
            // between the group's visible screen center and the object center;
            // positioning the stretched panel rect itself would produce an
            // apparently offset toolbar.
            if (!TryGetControlGroupScreenCenter(out var groupCenter)) return;
            var parent = panel.parent as RectTransform;
            if (parent == null) return;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parent, screen, null, out var targetWorld)) return;
            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parent, groupCenter, null, out var groupWorld)) return;
            panel.position += targetWorld - groupWorld;
        }

        private bool TryGetControlGroupScreenCenter(out Vector2 center)
        {
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            var found = false;
            for (var index = 0; index < controlRects.Length; index++)
            {
                var control = controlRects[index];
                if (control == null || !control.gameObject.activeInHierarchy) continue;
                control.GetWorldCorners(worldCorners);
                for (var cornerIndex = 0; cornerIndex < worldCorners.Length; cornerIndex++)
                {
                    var point = RectTransformUtility.WorldToScreenPoint(null, worldCorners[cornerIndex]);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                    found = true;
                }
            }
            center = found ? (min + max) * .5f : Vector2.zero;
            return found;
        }
    }
}
