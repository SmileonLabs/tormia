using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only world prompt for an already evaluated interaction
    /// opportunity. It never decides eligibility or publishes gameplay intent.
    /// </summary>
    public sealed class OntologyWorldInteractionPrompt : MonoBehaviour
    {
        private Transform target;
        private Camera worldCamera;
        private RectTransform canvasRect;

        public static OntologyWorldInteractionPrompt Create(string label)
        {
            var root = new GameObject("WorldInteractionPrompt");
            var presenter = root.AddComponent<OntologyWorldInteractionPrompt>();
            presenter.Build(label);
            root.SetActive(false);
            return presenter;
        }

        public void SetTarget(Transform value)
        {
            target = value;
            gameObject.SetActive(value != null);
            if (value != null)
                UpdatePose();
        }

        private void Build(string label)
        {
            var canvasObject = new GameObject(
                "PromptCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(48f, 48f);
            canvasRect.localScale = Vector3.one * 0.006f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 120;

            var badge = new GameObject(
                "Badge",
                typeof(RectTransform),
                typeof(Image),
                typeof(Outline));
            badge.transform.SetParent(canvasRect, false);
            var badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = Vector2.zero;
            badgeRect.anchorMax = Vector2.one;
            badgeRect.offsetMin = Vector2.zero;
            badgeRect.offsetMax = Vector2.zero;
            var image = badge.GetComponent<Image>();
            image.color = new Color32(28, 63, 111, 245);
            image.raycastTarget = false;
            var outline = badge.GetComponent<Outline>();
            outline.effectColor = new Color32(245, 181, 47, 255);
            outline.effectDistance = new Vector2(3f, -3f);

            var textObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(badgeRect, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = label;
            text.fontSize = 29f;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                gameObject.SetActive(false);
                return;
            }
            UpdatePose();
        }

        private void UpdatePose()
        {
            worldCamera ??= Camera.main;
            var bounds = ResolveBounds(target);
            transform.position = new Vector3(
                bounds.center.x,
                bounds.max.y + 0.3f,
                bounds.center.z);
            if (worldCamera != null)
            {
                transform.rotation = Quaternion.LookRotation(
                    transform.position - worldCamera.transform.position,
                    Vector3.up);
            }
        }

        private static Bounds ResolveBounds(Transform value)
        {
            var renderers = value.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(value.position, Vector3.one);
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }
    }
}
