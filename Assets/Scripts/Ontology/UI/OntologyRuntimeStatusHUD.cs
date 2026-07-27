using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyRuntimeStatusHUD : OntologyUIPanelBase, IPointerClickHandler
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private Text uiText;
        [SerializeField] private Component textMeshProText;
        [SerializeField] private string actorId = "Player";
        [SerializeField] private OntologyAnimationAdapter animationAdapter;
        [Header("Editable TOV status card")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text movementLabel;
        [SerializeField] private TMP_Text movementValue;
        [SerializeField] private TMP_Text interactionLabel;
        [SerializeField] private TMP_Text interactionValue;
        [SerializeField] private TMP_Text permissionLabel;
        [SerializeField] private TMP_Text permissionValue;
        [SerializeField] private TMP_Text saveLabel;
        [SerializeField] private TMP_Text saveValue;
        [Header("Hierarchy-authored collapse")]
        [SerializeField] private RectTransform titleHitArea;
        [SerializeField] private GameObject collapsibleContent;
        [SerializeField] private RectTransform collapseShadow;
        [SerializeField, Min(1f)] private float expandedHeight = 320f;
        [SerializeField, Min(1f)] private float collapsedHeight = 76f;
        [SerializeField] private OntologyInputSystemPlayerInput playerInput;
        [SerializeField] private OntologyWorldAuthorityClient authorityClient;
        [SerializeField] private OntologyPersistentStateCoordinator persistentStateCoordinator;
        [SerializeField] private OntologyAvatarCheckpointController checkpointController;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.2f;

        private TMP_Text tmpText;
        private float nextRefreshAt;
        public bool IsCollapsed { get; private set; }

        private void Awake()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (animationAdapter == null)
            {
                animationAdapter = FindAnyObjectByType<OntologyAnimationAdapter>();
            }
            if (playerInput == null) playerInput = FindAnyObjectByType<OntologyInputSystemPlayerInput>();
            if (authorityClient == null) authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (persistentStateCoordinator == null) persistentStateCoordinator = FindAnyObjectByType<OntologyPersistentStateCoordinator>();
            if (checkpointController == null) checkpointController = FindAnyObjectByType<OntologyAvatarCheckpointController>();

            tmpText = textMeshProText as TMP_Text;
            var root = transform as RectTransform;
            if (root != null && root.sizeDelta.y > collapsedHeight)
                expandedHeight = root.sizeDelta.y;
            ApplyTheme();
        }

        private void OnEnable()
        {
            if (bootstrap == null)
            {
                bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            }

            if (bootstrap != null)
            {
                bootstrap.WorldChanged += Refresh;
            }
            if (authorityClient != null) authorityClient.StateChanged += Refresh;
            OntologyLanguagePackService.LanguageChanged += Refresh;

            Refresh();
        }

        private void OnDisable()
        {
            if (bootstrap != null)
            {
                bootstrap.WorldChanged -= Refresh;
            }
            if (authorityClient != null) authorityClient.StateChanged -= Refresh;
            OntologyLanguagePackService.LanguageChanged -= Refresh;
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefreshAt) return;
            nextRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
            Refresh();
        }

        private void Refresh()
        {
            if (playerInput == null) playerInput = FindAnyObjectByType<OntologyInputSystemPlayerInput>();
            if (authorityClient == null) authorityClient = FindAnyObjectByType<OntologyWorldAuthorityClient>();
            if (persistentStateCoordinator == null) persistentStateCoordinator = FindAnyObjectByType<OntologyPersistentStateCoordinator>();
            if (checkpointController == null) checkpointController = FindAnyObjectByType<OntologyAvatarCheckpointController>();

            if (titleText == null || movementValue == null)
            {
                SetLegacyText();
                return;
            }

            titleText.text = L("ui.runtime_status.title", "WORLD STATUS");
            movementLabel.text = L("ui.runtime_status.movement", "MOVEMENT");
            interactionLabel.text = L("ui.runtime_status.interaction", "INTERACTION");
            permissionLabel.text = L("ui.runtime_status.permission", "PERMISSION");
            saveLabel.text = L("ui.runtime_status.save", "SAVE");
            movementValue.text = ResolveMovement();
            interactionValue.text = ResolveInteraction();
            permissionValue.text = ResolvePermission();
            saveValue.text = ResolveSave();
        }

        private void ApplyTheme()
        {
            var background = GetComponent<Image>();
            if (background != null)
            {
                background.color = Theme.hudBackground;
                // The root receives the pointer and accepts it only when the
                // authored title rectangle contains the click.
                background.raycastTarget = true;
            }

            if (tmpText != null)
            {
                tmpText.color = Theme.hudText;
            }

            if (uiText != null)
            {
                uiText.color = Theme.hudText;
            }
        }

        private string ResolveMovement()
        {
            if (bootstrap != null && bootstrap.World != null)
            {
                if (HasFact(actorId, "movement_state", "Slowed"))
                    return L("ui.runtime_status.slowed", "Slowed");
                var mode = GetFactObject(actorId, "mobility_mode");
                if (!string.IsNullOrWhiteSpace(mode) && mode != Labels.noneText)
                    return mode;
            }
            return playerInput != null && playerInput.IsMovingIntent
                ? L("ui.runtime_status.moving", "Moving")
                : L("ui.runtime_status.explore", "Explore");
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (titleHitArea == null || eventData == null) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    titleHitArea,
                    eventData.position,
                    eventData.pressEventCamera))
            {
                return;
            }

            SetCollapsed(!IsCollapsed);
        }

        public void ToggleCollapsed() => SetCollapsed(!IsCollapsed);

        public void SetCollapsed(bool collapsed)
        {
            IsCollapsed = collapsed;
            if (collapsibleContent != null)
                collapsibleContent.SetActive(!collapsed);

            if (transform is RectTransform root)
            {
                var size = root.sizeDelta;
                size.y = collapsed ? collapsedHeight : expandedHeight;
                root.sizeDelta = size;
            }
            if (collapseShadow != null)
            {
                var shadowSize = collapseShadow.sizeDelta;
                shadowSize.y = collapsed ? collapsedHeight : expandedHeight;
                collapseShadow.sizeDelta = shadowSize;
            }
        }

        private string ResolveInteraction()
        {
            var entityId = playerInput == null ? string.Empty : playerInput.SelectedInteractionEntityId;
            return string.IsNullOrWhiteSpace(entityId)
                ? L("ui.runtime_status.none", "None")
                : OntologyLanguagePackService.DisplaySemanticObject(entityId);
        }

        private string ResolvePermission()
        {
            if (authorityClient == null || !authorityClient.IsAuthenticated)
                return L("ui.runtime_status.permission.local", "Local");
            switch ((authorityClient.CurrentWorldRole ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "owner": return L("ui.runtime_status.permission.owner", "Owner");
                case "editor": return L("ui.runtime_status.permission.builder", "Builder");
                default: return L("ui.runtime_status.permission.visitor", "Visitor");
            }
        }

        private string ResolveSave()
        {
            if (authorityClient == null || !authorityClient.IsAuthenticated)
                return L("ui.runtime_status.save.local", "Local only");
            if (checkpointController != null && checkpointController.SaveInProgress)
                return L("ui.runtime_status.save.saving", "Saving");
            if (persistentStateCoordinator != null)
            {
                switch (persistentStateCoordinator.Status)
                {
                    case OntologyPersistenceStatus.Dirty:
                    case OntologyPersistenceStatus.Saving:
                    case OntologyPersistenceStatus.Retrying:
                        return L("ui.runtime_status.save.saving", "Saving");
                    case OntologyPersistenceStatus.Conflict:
                    case OntologyPersistenceStatus.Failed:
                    case OntologyPersistenceStatus.Offline:
                        return L("ui.runtime_status.save.attention", "Attention");
                }
            }
            var status = authorityClient.LastStatus ?? string.Empty;
            if (status.IndexOf("fail", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                status.IndexOf("error", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                status.IndexOf("reject", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return L("ui.runtime_status.save.attention", "Attention");
            return authorityClient.IsWorldRuntimeReady
                ? L("ui.runtime_status.save.saved", "Saved")
                : L("ui.runtime_status.save.connecting", "Connecting");
        }

        private bool HasFact(string subject, string predicate, string obj)
        {
            return bootstrap.World.HasFact(subject, predicate, obj);
        }

        private string GetFactObject(string subject, string predicate)
        {
            foreach (var fact in bootstrap.World.Facts)
            {
                if (fact.Subject.ToString() == subject && fact.Predicate.ToString() == predicate)
                {
                    return OntologyLanguagePackService.DisplaySemanticObject(
                        fact.Object.ToString());
                }
            }

            return Labels.noneText;
        }

        private void SetLegacyText()
        {
            var value = L("ui.runtime_status.title", "WORLD STATUS") + "\n" +
                        L("ui.runtime_status.world_not_ready", "World is not ready.");
            if (uiText != null) uiText.text = value;
            else if (tmpText != null) tmpText.text = value;
        }

        private static string L(string key, string fallback) =>
            OntologyLanguagePackService.Text(key, fallback);
    }
}
