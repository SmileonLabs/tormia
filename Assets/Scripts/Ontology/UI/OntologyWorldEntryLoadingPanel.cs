using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only transition shown while the Authority enters the
    /// selected account character into the selected world. Progress is a
    /// reader-facing transition indicator; durable authority state remains
    /// owned by OntologyWorldAuthorityAccountEntryFlow.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyWorldEntryLoadingPanel : MonoBehaviour
    {
        [SerializeField] private OntologyWorldAuthorityAccountEntryFlow entryFlow;
        [SerializeField] private OntologyAccountFlowNavigator navigator;
        [SerializeField] private CanvasGroup panelGroup;
        [SerializeField] private TMP_Text titleLabel;
        [SerializeField] private TMP_Text worldNameLabel;
        [SerializeField] private TMP_Text characterNameLabel;
        [SerializeField] private TMP_Text preparationLabel;
        [SerializeField] private TMP_Text stageLabel;
        [SerializeField] private TMP_Text progressLabel;
        [SerializeField] private TMP_Text tipHeadingLabel;
        [SerializeField] private TMP_Text tipLabel;
        [SerializeField] private Image progressFill;
        [SerializeField] private RectTransform progressFillBounds;
        [SerializeField] private RectTransform progressReveal;
        [SerializeField] private RectTransform progressMarker;
        [SerializeField, Min(0f)] private float minimumVisibleSeconds = 1.25f;
        [SerializeField] private bool startsVisible;

        private float visibleTime;
        private float displayedProgress;
        private bool awaitingCompletion;

        private void Awake()
        {
            ResolveDependencies();
            SetVisible(startsVisible);
        }

        private void OnEnable()
        {
            ResolveDependencies();
            OntologyLanguagePackService.LanguageChanged += RefreshLabels;
            RefreshLabels();
        }

        private void OnDisable() => OntologyLanguagePackService.LanguageChanged -= RefreshLabels;

        private void Update()
        {
            if (panelGroup == null || panelGroup.alpha < 0.01f) return;
            visibleTime += Time.unscaledDeltaTime;
            if (awaitingCompletion)
            {
                var simulatedTarget = Mathf.Min(0.9f, 0.08f + visibleTime * 0.2f);
                displayedProgress = Mathf.MoveTowards(displayedProgress, simulatedTarget, Time.unscaledDeltaTime * 0.18f);
                ApplyProgress();
            }
        }

        public void BeginEntry()
        {
            ResolveDependencies();
            if (entryFlow == null) return;
            StopAllCoroutines();
            visibleTime = 0f;
            displayedProgress = 0.08f;
            awaitingCompletion = true;
            SetVisible(true);
            RefreshLabels();
            ApplyProgress();
            StartCoroutine(PrepareThenEnterRoutine());
        }

        private IEnumerator PrepareThenEnterRoutine()
        {
            // Existing owner worlds may predate the current immutable content
            // release and avatar semantic contract. Preparation is an explicit,
            // idempotent phase owned by this launch orchestrator; the admission
            // routine itself remains read-only and never repairs world data.
            if (entryFlow.CanEditSelectedWorld)
            {
                if (stageLabel != null)
                {
                    stageLabel.text = L(
                        "ui.account.world_entry_loading.preparing_contract",
                        "Preparing world contract");
                }

                var preparationCompleted = false;
                var prepared = false;
                entryFlow.PrepareSelectedDevelopmentWorld(
                    value =>
                    {
                        prepared = value;
                        preparationCompleted = true;
                    });
                while (!preparationCompleted)
                    yield return null;

                if (!prepared)
                {
                    OnEntryCompleted(false);
                    yield break;
                }
            }

            if (stageLabel != null)
            {
                stageLabel.text = L(
                    "ui.account.world_entry_loading.stage",
                    "Loading world data");
            }
            entryFlow.EnterSelectedCharacterInCurrentWorld(OnEntryCompleted);
        }

        public void Open()
        {
            ResolveDependencies();
            visibleTime = 0f;
            displayedProgress = 0.72f;
            awaitingCompletion = false;
            SetVisible(true);
            RefreshLabels();
            ApplyProgress();
        }

        public void Close()
        {
            awaitingCompletion = false;
            StopAllCoroutines();
            SetVisible(false);
        }

        private void OnEntryCompleted(bool success)
        {
            awaitingCompletion = false;
            if (success) displayedProgress = 1f;
            if (stageLabel != null)
            {
                stageLabel.text = success
                    ? L("ui.account.world_entry_loading.ready", "World ready")
                    : entryFlow?.LastStatus ?? L("ui.account.world_entry_loading.failed", "World entry failed");
            }
            ApplyProgress();
            StartCoroutine(FinishTransition(success));
        }

        private IEnumerator FinishTransition(bool success)
        {
            var remaining = Mathf.Max(0f, minimumVisibleSeconds - visibleTime);
            if (remaining > 0f) yield return new WaitForSecondsRealtime(remaining);
            SetVisible(false);
            if (success) navigator?.CloseAll();
            else navigator?.ShowProfileReview();
        }

        private void RefreshLabels()
        {
            if (titleLabel != null) titleLabel.text = L("ui.account.world_entry_loading.title", "ENTERING WORLD");
            if (preparationLabel != null) preparationLabel.text =
                L("ui.account.world_entry_loading.preparing", "Preparing your adventure...");
            if (stageLabel != null && awaitingCompletion) stageLabel.text =
                L("ui.account.world_entry_loading.stage", "Loading world data");
            if (tipHeadingLabel != null) tipHeadingLabel.text = L("ui.account.world_entry_loading.tip_heading", "TIP");
            if (tipLabel != null) tipLabel.text = L("ui.account.world_entry_loading.tip",
                "Your character profile travels with you. World facts belong to this world.");
            if (characterNameLabel != null) characterNameLabel.text = entryFlow?.CurrentCharacter?.displayName ?? string.Empty;
            if (worldNameLabel != null) worldNameLabel.text = ResolveSelectedWorldTitle();
        }

        private string ResolveSelectedWorldTitle()
        {
            if (entryFlow == null) return string.Empty;
            foreach (var world in entryFlow.Worlds)
            {
                if (world != null && world.worldId == entryFlow.SelectedWorldId)
                    return string.IsNullOrWhiteSpace(world.title) ? world.slug : world.title;
            }
            return entryFlow.SelectedWorldId ?? string.Empty;
        }

        private void ApplyProgress()
        {
            var value = Mathf.Clamp01(displayedProgress);
            if (progressFill != null && progressFillBounds != null && progressReveal != null)
            {
                var fullWidth = progressFillBounds.rect.width;
                progressReveal.anchorMin = new Vector2(0f, 0f);
                progressReveal.anchorMax = new Vector2(0f, 1f);
                progressReveal.pivot = new Vector2(0f, 0.5f);
                progressReveal.anchoredPosition = Vector2.zero;
                progressReveal.sizeDelta = new Vector2(fullWidth * value, 0f);

                var fillRect = progressFill.rectTransform;
                fillRect.anchorMin = new Vector2(0f, 0f);
                fillRect.anchorMax = new Vector2(0f, 1f);
                fillRect.pivot = new Vector2(0f, 0.5f);
                fillRect.anchoredPosition = Vector2.zero;
                fillRect.sizeDelta = new Vector2(fullWidth, 0f);
            }
            if (progressMarker != null)
            {
                var anchor = new Vector2(value, 0.5f);
                progressMarker.anchorMin = anchor;
                progressMarker.anchorMax = anchor;
                progressMarker.anchoredPosition = Vector2.zero;
            }
            if (progressLabel != null) progressLabel.text = Mathf.RoundToInt(value * 100f) + "%";
        }

        private void ResolveDependencies()
        {
            if (entryFlow == null)
                entryFlow = FindAnyObjectByType<OntologyWorldAuthorityAccountEntryFlow>(
                    FindObjectsInactive.Include);
            if (navigator == null)
                navigator = FindAnyObjectByType<OntologyAccountFlowNavigator>(
                    FindObjectsInactive.Include);
            if (panelGroup == null) panelGroup = GetComponent<CanvasGroup>();
        }

        private void SetVisible(bool visible)
        {
            if (panelGroup == null) return;
            panelGroup.alpha = visible ? 1f : 0f;
            panelGroup.interactable = visible;
            panelGroup.blocksRaycasts = visible;
        }

        private static string L(string key, string fallback) => OntologyLanguagePackService.Text(key, fallback);
    }
}
