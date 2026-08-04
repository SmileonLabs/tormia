using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presents changes to Authority-projected current_health above the matching
    /// scene actor. It never predicts or applies damage/healing locally.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyActorHealthDeltaPresenter : MonoBehaviour
    {
        [Header("Authority")]
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;

        [Header("Typography")]
        [SerializeField] private TMP_FontAsset font;
        [SerializeField, Min(1f)] private float fontSize = 28f;
        [SerializeField] private Color damageColor = new(0.96f, 0.22f, 0.18f, 1f);
        [SerializeField] private Color healingColor = new(0.25f, 0.78f, 0.28f, 1f);

        [Header("World Presentation")]
        [SerializeField] private Vector3 overheadOffset = new(0f, 0.35f, 0f);
        [SerializeField, Min(0.001f)] private float worldScale = 0.01f;
        [SerializeField, Min(0f)] private float riseDistance = 0.65f;
        [SerializeField, Min(0.05f)] private float lifetime = 1.05f;
        [SerializeField, Range(0f, 1f)] private float fadeStart = 0.5f;
        [SerializeField, Min(1)] private int poolCapacity = 12;
        [SerializeField, Min(0f)] private float repeatedHitSpread = 0.18f;

        private readonly HealthBaselineTracker baselines = new();
        private readonly List<Popup> popups = new();
        private OntologyWorldAuthorityClient subscribedClient;
        private string observedWorldId = string.Empty;
        private int spawnSequence;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureRuntimePresenter()
        {
            var client = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (client == null) return;
            var presenter = client.GetComponent<OntologyActorHealthDeltaPresenter>() ??
                            client.gameObject.AddComponent<OntologyActorHealthDeltaPresenter>();
            presenter.Configure(client);
        }

        public void Configure(OntologyWorldAuthorityClient client)
        {
            authorityClient = client;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Awake()
        {
            if (authorityClient == null)
                authorityClient = GetComponent<OntologyWorldAuthorityClient>() ??
                                  FindAnyObjectByType<OntologyWorldAuthorityClient>();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            if (subscribedClient != null)
                subscribedClient.ProjectionReceived -= HandleProjection;
            subscribedClient = null;
        }

        private void LateUpdate()
        {
            var cameraTransform = Camera.main != null ? Camera.main.transform : null;
            for (var index = popups.Count - 1; index >= 0; index--)
            {
                var popup = popups[index];
                if (!popup.root.activeSelf) continue;

                popup.elapsed += Time.unscaledDeltaTime;
                var progress = Mathf.Clamp01(popup.elapsed / Mathf.Max(0.05f, lifetime));
                if (popup.anchor == null || progress >= 1f)
                {
                    popup.root.SetActive(false);
                    continue;
                }

                popup.root.transform.position = ResolveOverheadPosition(popup.anchor) +
                                                Vector3.up * (riseDistance * progress) +
                                                popup.spread;
                if (cameraTransform != null)
                    popup.root.transform.rotation = cameraTransform.rotation;

                var fadeProgress = Mathf.InverseLerp(fadeStart, 1f, progress);
                popup.group.alpha = 1f - fadeProgress;
            }
        }

        internal void HandleProjection(OntologyAuthorityWorldProjection projection)
        {
            if (projection?.facts == null) return;

            var worldId = projection.worldId ?? string.Empty;
            if (!string.Equals(observedWorldId, worldId, StringComparison.OrdinalIgnoreCase))
            {
                observedWorldId = worldId;
                baselines.Clear();
                HideAll();
            }

            var values = ProjectCurrentHealth(projection.facts);
            foreach (var change in baselines.Apply(values))
            {
                var identity = FindIdentity(change.EntityId);
                if (identity != null) Show(identity.transform, change.Delta);
            }
        }

        public static Dictionary<Guid, double> ProjectCurrentHealth(
            IEnumerable<OntologyAuthorityFactProjection> facts)
        {
            var projected = new Dictionary<Guid, double>();
            var invalid = new HashSet<Guid>();
            foreach (var fact in facts ?? Array.Empty<OntologyAuthorityFactProjection>())
            {
                if (fact == null ||
                    fact.predicateId != OntologyPredicates.CurrentHealth ||
                    !Guid.TryParse(fact.subjectEntityId, out var entityId) ||
                    !double.TryParse(
                        fact.objectValueJson,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var value) ||
                    double.IsNaN(value) ||
                    double.IsInfinity(value))
                {
                    continue;
                }

                if (!projected.TryAdd(entityId, value)) invalid.Add(entityId);
            }

            foreach (var entityId in invalid) projected.Remove(entityId);
            return projected;
        }

        private void Subscribe()
        {
            if (authorityClient == null)
                authorityClient = GetComponent<OntologyWorldAuthorityClient>() ??
                                  FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (subscribedClient == authorityClient) return;
            if (subscribedClient != null)
                subscribedClient.ProjectionReceived -= HandleProjection;
            subscribedClient = authorityClient;
            if (subscribedClient != null)
            {
                subscribedClient.ProjectionReceived += HandleProjection;
                if (subscribedClient.CurrentProjection != null)
                    HandleProjection(subscribedClient.CurrentProjection);
            }
        }

        private void Show(Transform anchor, double delta)
        {
            if (Math.Abs(delta) < 0.0001d) return;
            var popup = AcquirePopup();
            popup.anchor = anchor;
            popup.elapsed = 0f;
            popup.spread = Vector3.right *
                (((spawnSequence++ % 3) - 1) * repeatedHitSpread);
            popup.text.text = delta > 0d
                ? "+" + FormatAmount(delta)
                : "-" + FormatAmount(Math.Abs(delta));
            popup.text.color = delta > 0d ? healingColor : damageColor;
            popup.text.fontSize = fontSize;
            if (font != null) popup.text.font = font;
            popup.group.alpha = 1f;
            popup.root.transform.localScale = Vector3.one * worldScale;
            popup.root.SetActive(true);
        }

        private Popup AcquirePopup()
        {
            var inactive = popups.FirstOrDefault(value => !value.root.activeSelf);
            if (inactive != null) return inactive;
            if (popups.Count >= Mathf.Max(1, poolCapacity))
                return popups.OrderByDescending(value => value.elapsed).First();

            var root = new GameObject(
                "AuthorityHealthDelta",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasGroup));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 50;
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(180f, 60f);

            var textObject = new GameObject("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(root.transform, false);
            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.outlineWidth = 0.16f;
            text.outlineColor = new Color32(35, 31, 25, 220);

            var popup = new Popup(root, root.GetComponent<CanvasGroup>(), text);
            popups.Add(popup);
            return popup;
        }

        private Vector3 ResolveOverheadPosition(Transform anchor)
        {
            var renderers = anchor.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var index = 1; index < renderers.Length; index++)
                    bounds.Encapsulate(renderers[index].bounds);
                return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) + overheadOffset;
            }

            var collider = anchor.GetComponentInChildren<Collider>();
            return (collider != null
                       ? new Vector3(collider.bounds.center.x, collider.bounds.max.y, collider.bounds.center.z)
                       : anchor.position) + overheadOffset;
        }

        private static OntologyAuthorityEntityIdentity FindIdentity(Guid entityId) =>
            FindObjectsByType<OntologyAuthorityEntityIdentity>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None)
                .FirstOrDefault(value => value != null &&
                                         value.TryGetGuid(out var candidate) &&
                                         candidate == entityId);

        private static string FormatAmount(double amount) =>
            Math.Abs(amount - Math.Round(amount)) < 0.001d
                ? Math.Round(amount).ToString(CultureInfo.InvariantCulture)
                : amount.ToString("0.#", CultureInfo.InvariantCulture);

        private void HideAll()
        {
            foreach (var popup in popups) popup.root.SetActive(false);
        }

        private sealed class Popup
        {
            public readonly GameObject root;
            public readonly CanvasGroup group;
            public readonly TextMeshProUGUI text;
            public Transform anchor;
            public float elapsed;
            public Vector3 spread;

            public Popup(GameObject root, CanvasGroup group, TextMeshProUGUI text)
            {
                this.root = root;
                this.group = group;
                this.text = text;
            }
        }

        public readonly struct HealthDelta
        {
            public Guid EntityId { get; }
            public double Delta { get; }
            public HealthDelta(Guid entityId, double delta)
            {
                EntityId = entityId;
                Delta = delta;
            }
        }

        public sealed class HealthBaselineTracker
        {
            private readonly Dictionary<Guid, double> values = new();

            public IReadOnlyList<HealthDelta> Apply(IReadOnlyDictionary<Guid, double> current)
            {
                var changes = new List<HealthDelta>();
                foreach (var pair in current)
                {
                    if (values.TryGetValue(pair.Key, out var previous))
                    {
                        var delta = pair.Value - previous;
                        if (Math.Abs(delta) >= 0.0001d)
                            changes.Add(new HealthDelta(pair.Key, delta));
                    }
                }

                values.Clear();
                foreach (var pair in current) values[pair.Key] = pair.Value;
                return changes;
            }

            public void Clear() => values.Clear();
        }
    }
}
