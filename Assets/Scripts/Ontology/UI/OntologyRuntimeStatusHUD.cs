using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Tormia.Ontology.Core
{
    public sealed class OntologyRuntimeStatusHUD : OntologyUIPanelBase
    {
        [SerializeField] private OntologyWorldBootstrap bootstrap;
        [SerializeField] private Text uiText;
        [SerializeField] private Component textMeshProText;
        [SerializeField] private string actorId = "Player";
        [SerializeField] private OntologyAnimationAdapter animationAdapter;

        private readonly StringBuilder builder = new StringBuilder(512);
        private TMP_Text tmpText;
        private string lastRenderedText;

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

            tmpText = textMeshProText as TMP_Text;
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

            Refresh();
        }

        private void OnDisable()
        {
            if (bootstrap != null)
            {
                bootstrap.WorldChanged -= Refresh;
            }
        }

        private void Refresh()
        {
            if (bootstrap == null || bootstrap.World == null)
            {
                builder.Length = 0;
                builder.AppendLine(Labels.hudTitle);
                builder.AppendLine(Labels.hudWorldNotReady);
                SetText(builder.ToString());
                return;
            }

            builder.Length = 0;
            builder.AppendLine(Labels.hudTitle);
            AppendLine(Labels.hudCurrentTick, GetFactObject("Simulation", "current_tick"));
            builder.AppendLine(Labels.hudDivider);
            AppendLine(Labels.hudStandingOn, GetFactObject(actorId, "standing_on"));
            AppendLine(Labels.hudCoreVitality, GetFactObject(actorId, "core_vitality"));
            AppendLine(Labels.hudStatusWet, FormatBool(HasFact(actorId, "status", "Wet")));
            AppendLine(Labels.hudStateSlowed, FormatBool(HasFact(actorId, "movement_state", "Slowed")));
            AppendLine(Labels.hudExposedCold, FormatBool(HasFact(actorId, "exposed_to", "ColdEnvironment")));
            builder.AppendLine(Labels.hudDivider);
            AppendLine(Labels.hudEquippedParts, GetFactObjects(actorId, OntologyPredicates.EquippedPart));
            AppendLine(Labels.hudPartCapabilities, GetFactObjects(actorId, OntologyPredicates.HasCapability));
            builder.AppendLine(Labels.hudDivider);
            AppendLine(Labels.hudAnimationIntent, GetFactObject(actorId, "animation_intent"));
            if (animationAdapter != null)
            {
                AppendLine(Labels.hudSelectedIntent, FormatOptional(animationAdapter.SelectedIntent));
                AppendLine(Labels.hudSelectedAnimation, FormatOptional(animationAdapter.SelectedAnimationId));
                AppendLine(Labels.hudSelectedClip, FormatOptional(animationAdapter.SelectedClipName));
            }
            SetText(builder.ToString());
        }

        private void AppendLine(string label, string value)
        {
            builder.Append(label).Append(": ").AppendLine(value);
        }

        private void ApplyTheme()
        {
            var background = GetComponent<Image>();
            if (background != null)
            {
                background.color = Theme.hudBackground;
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

        private string FormatBool(bool value)
        {
            return value ? Labels.trueText : Labels.falseText;
        }

        private string FormatOptional(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? Labels.noneText
                : OntologyLanguagePackService.DisplaySemanticObject(value);
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

        private string GetFactObjects(string subject, string predicate)
        {
            var found = false;
            var first = true;
            var result = new StringBuilder();
            foreach (var fact in bootstrap.World.Facts)
            {
                if (fact.Subject.ToString() != subject || fact.Predicate.ToString() != predicate)
                {
                    continue;
                }

                if (!first)
                {
                    result.Append(", ");
                }

                result.Append(
                    OntologyLanguagePackService.DisplaySemanticObject(
                        fact.Object.ToString()));
                first = false;
                found = true;
            }

            return found ? result.ToString() : Labels.noneText;
        }

        private void SetText(string value)
        {
            if (string.Equals(lastRenderedText, value, System.StringComparison.Ordinal))
            {
                return;
            }

            lastRenderedText = value;
            if (uiText != null)
            {
                uiText.text = value;
                return;
            }

            if (tmpText != null)
            {
                tmpText.text = value;
            }
        }
    }
}
