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
        [Header("Editable TOV toast")]
        [SerializeField] private Image cardBackground;
        [SerializeField] private Image accent;
        [SerializeField] private Image severityIcon;
        [SerializeField] private TextMeshProUGUI titleLabel;
        [SerializeField] private TextMeshProUGUI detailLabel;
        [SerializeField] private Sprite infoIcon;
        [SerializeField] private Sprite positiveIcon;
        [SerializeField] private Sprite warningIcon;
        [SerializeField] private Sprite negativeIcon;

        public enum Severity
        {
            Info,
            Positive,
            Warning,
            Negative
        }

        private readonly struct ToastMessage
        {
            public ToastMessage(string title, string detail, Severity severity)
            {
                Title = title;
                Detail = detail;
                Severity = severity;
            }

            public string Title { get; }
            public string Detail { get; }
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
            var split = message.IndexOf(':');
            var title = split > 0 ? message.Substring(0, split).Trim() : message.Trim();
            var detail = split > 0 ? message.Substring(split + 1).Trim() : string.Empty;
            Show(title, detail, severity);
        }

        public void Show(string title, string detail, Severity severity = Severity.Info)
        {
            if (string.IsNullOrWhiteSpace(title)) return;
            EnsureReferences();
            messages.Enqueue(new ToastMessage(title.Trim(), detail?.Trim() ?? string.Empty, severity));
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
                if (titleLabel != null) titleLabel.text = message.Title;
                if (detailLabel != null)
                {
                    detailLabel.text = message.Detail;
                    detailLabel.gameObject.SetActive(!string.IsNullOrWhiteSpace(message.Detail));
                }
                if (label != null && titleLabel == null) label.text = message.Title;
                ApplySeverity(message.Severity);

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
                Debug.LogError("[OntologyActorToast] ActorToastCanvas is missing from the hierarchy.", this);
                return;
            }

            canvas.renderMode = RenderMode.WorldSpace;
            canvasGroup = canvas.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                Debug.LogError("[OntologyActorToast] CanvasGroup is missing from ActorToastCanvas.", this);
                return;
            }

            rectTransform = canvas.GetComponent<RectTransform>();
            if (cardBackground == null) cardBackground = FindImage("ToastCard");
            background = cardBackground != null ? cardBackground : canvas.GetComponentInChildren<Image>(true);
            if (background == null)
            {
                Debug.LogError("[OntologyActorToast] Background Image is missing from the hierarchy.", this);
                return;
            }

            if (titleLabel == null) titleLabel = FindText("TitleLabel");
            if (detailLabel == null) detailLabel = FindText("DetailLabel");
            label = titleLabel != null ? titleLabel : canvas.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label == null)
            {
                Debug.LogError("[OntologyActorToast] Text label is missing from the hierarchy.", this);
            }
        }

        private void ApplyTheme()
        {
            if (background != null)
            {
                if (cardBackground == null) background.color = Theme.actorToastBackground;
            }

            if (label != null)
            {
                label.color = Theme.actorToastText;
                label.raycastTarget = false;
            }
            if (titleLabel != null) titleLabel.raycastTarget = false;
            if (detailLabel != null) detailLabel.raycastTarget = false;
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

        private void ApplySeverity(Severity severity)
        {
            var color = GetSeverityTextColor(severity);
            if (titleLabel != null) titleLabel.color = new Color32(29, 48, 58, 255);
            if (detailLabel != null) detailLabel.color = color;
            if (label != null && titleLabel == null) label.color = color;
            if (accent != null) accent.color = color;
            if (severityIcon != null)
            {
                severityIcon.sprite = severity switch
                {
                    Severity.Positive => positiveIcon,
                    Severity.Warning => warningIcon,
                    Severity.Negative => negativeIcon,
                    _ => infoIcon
                };
            }
        }

        private Image FindImage(string objectName)
        {
            foreach (var image in canvas.GetComponentsInChildren<Image>(true))
                if (image.name == objectName) return image;
            return null;
        }

        private TextMeshProUGUI FindText(string objectName)
        {
            foreach (var text in canvas.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (text.name == objectName) return text;
            return null;
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
