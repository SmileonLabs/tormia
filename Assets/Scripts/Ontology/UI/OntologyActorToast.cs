using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyActorToast : MonoBehaviour
    {
        [SerializeField] private OntologyUITheme theme;
        [SerializeField] private Transform anchor;
        [SerializeField] private Camera targetCamera;

        public enum Severity
        {
            Info,
            Positive,
            Warning,
            Negative
        }

        private readonly struct ToastMessage
        {
            public ToastMessage(string text, Severity severity)
            {
                Text = text;
                Severity = severity;
            }

            public string Text { get; }
            public Severity Severity { get; }
        }

        private readonly Queue<ToastMessage> messages = new();
        private Canvas canvas;
        private CanvasGroup canvasGroup;
        private RectTransform rectTransform;
        private Image background;
        private TextMeshProUGUI label;
        private Coroutine routine;
        private Vector3 animatedOffset;
        private static OntologyUITheme fallbackTheme;

        private void Awake()
        {
            EnsureReferences();
            ApplyTheme();
            HideImmediate();
        }

        private void LateUpdate()
        {
            if (rectTransform == null || anchor == null)
            {
                return;
            }

            var themeToUse = Theme;
            rectTransform.position = anchor.position + themeToUse.actorToastWorldOffset + animatedOffset;
            var cameraToUse = targetCamera != null ? targetCamera : Camera.main;
            if (cameraToUse != null)
            {
                rectTransform.rotation = Quaternion.LookRotation(rectTransform.position - cameraToUse.transform.position);
                var distance = Vector3.Distance(cameraToUse.transform.position, rectTransform.position);
                var scale = themeToUse.actorToastWorldScale * Mathf.Max(0.1f, distance / Mathf.Max(0.1f, themeToUse.actorToastReferenceDistance));
                scale = Mathf.Clamp(scale, themeToUse.actorToastMinScale, themeToUse.actorToastMaxScale);
                rectTransform.localScale = Vector3.one * scale;
            }
        }

        public void Show(string message)
        {
            Show(message, Severity.Info);
        }

        public void Show(string message, Severity severity)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            EnsureReferences();
            messages.Enqueue(new ToastMessage(message, severity));
            if (routine == null)
            {
                routine = StartCoroutine(ShowRoutine());
            }
        }

        public void Configure(Transform targetAnchor, OntologyUITheme targetTheme, Camera cameraToUse)
        {
            anchor = targetAnchor;
            theme = targetTheme;
            targetCamera = cameraToUse;
            EnsureReferences();
            ApplyTheme();
        }

        private IEnumerator ShowRoutine()
        {
            while (messages.Count > 0)
            {
                var message = messages.Dequeue();
                label.text = message.Text;
                label.color = GetSeverityTextColor(message.Severity);

                var visibleTime = Mathf.Max(0.05f, Theme.actorToastDuration);
                var fadeTime = Mathf.Max(0.01f, Theme.actorToastFadeDuration);
                var elapsed = 0f;
                animatedOffset = Vector3.zero;
                canvasGroup.alpha = 1f;

                while (elapsed < visibleTime)
                {
                    elapsed += Time.deltaTime;
                    var t = Mathf.Clamp01(elapsed / visibleTime);
                    animatedOffset = Vector3.Lerp(Vector3.zero, Theme.actorToastFloatOffset, t);
                    if (elapsed > visibleTime - fadeTime)
                    {
                        canvasGroup.alpha = Mathf.Clamp01((visibleTime - elapsed) / fadeTime);
                    }

                    yield return null;
                }

                HideImmediate();
                yield return new WaitForSeconds(Mathf.Max(0f, Theme.actorToastQueueGap));
            }

            routine = null;
        }

        private void EnsureReferences()
        {
            if (anchor == null)
            {
                anchor = FindHeadAnchor();
            }

            canvas = GetComponentInChildren<Canvas>(true);
            if (canvas == null)
            {
                var canvasObject = new GameObject("ActorToastCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
                canvasObject.transform.SetParent(transform, false);
                canvas = canvasObject.GetComponent<Canvas>();
            }

            canvas.renderMode = RenderMode.WorldSpace;
            canvasGroup = canvas.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = canvas.gameObject.AddComponent<CanvasGroup>();
            }

            rectTransform = canvas.GetComponent<RectTransform>();
            background = canvas.GetComponentInChildren<Image>(true);
            if (background == null)
            {
                var backgroundObject = new GameObject("Background", typeof(RectTransform), typeof(Image));
                backgroundObject.transform.SetParent(canvas.transform, false);
                background = backgroundObject.GetComponent<Image>();
            }

            label = canvas.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null)
            {
                var labelObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelObject.transform.SetParent(background.transform, false);
                label = labelObject.GetComponent<TextMeshProUGUI>();
            }
        }

        private void ApplyTheme()
        {
            if (rectTransform != null)
            {
                rectTransform.sizeDelta = Theme.actorToastSize;
                rectTransform.localScale = Vector3.one * Theme.actorToastWorldScale;
            }

            if (background != null)
            {
                background.color = Theme.actorToastBackground;
                var backgroundRect = background.GetComponent<RectTransform>();
                backgroundRect.anchorMin = Vector2.zero;
                backgroundRect.anchorMax = Vector2.one;
                backgroundRect.offsetMin = Vector2.zero;
                backgroundRect.offsetMax = Vector2.zero;
            }

            if (label != null)
            {
                label.fontSize = Theme.actorToastFontSize;
                label.color = Theme.actorToastText;
                label.alignment = TextAlignmentOptions.Center;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
                label.raycastTarget = false;
                var labelRect = label.GetComponent<RectTransform>();
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(10f, 0f);
                labelRect.offsetMax = new Vector2(-10f, 0f);
            }
        }

        private void HideImmediate()
        {
            animatedOffset = Vector3.zero;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
            }
        }

        private Color GetSeverityTextColor(Severity severity)
        {
            switch (severity)
            {
                case Severity.Positive:
                    return Theme.actorToastPositiveText;
                case Severity.Warning:
                    return Theme.actorToastWarningText;
                case Severity.Negative:
                    return Theme.actorToastNegativeText;
                default:
                    return Theme.actorToastInfoText;
            }
        }

        private Transform FindHeadAnchor()
        {
            foreach (var animator in GetComponentsInChildren<Animator>(true))
            {
                if (!animator.isHuman)
                {
                    continue;
                }

                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    return head;
                }
            }

            var namedHead = transform.Find("Head");
            return namedHead != null ? namedHead : transform;
        }

        private OntologyUITheme Theme
        {
            get
            {
                if (theme != null)
                {
                    return theme;
                }

                if (fallbackTheme == null)
                {
                    fallbackTheme = ScriptableObject.CreateInstance<OntologyUITheme>();
                }

                return fallbackTheme;
            }
        }
    }
}
