using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// A user-facing semantic shortcut. A preset does not contain executable game
    /// logic: it declares which existing rule blocks and authored ontology data are
    /// connected to one placed object when the user chooses a plain-language action.
    /// </summary>
    [Serializable]
    public sealed class OntologyRuleBlockPreset
    {
        [Tooltip("Stable identifier used by UI and save-safe authoring.")]
        public string presetId;
        [Tooltip("Localization key for the short action name.")]
        public string displayNameKey;
        [Tooltip("Localization key for the explanation shown in Details.")]
        public string descriptionKey;
        [Tooltip("The primary rule activated by this shortcut.")]
        public string primaryRuleId;
        [Tooltip("Variable in the primary rule that represents the selected object.")]
        public string bindingVariable = "?object";
        [Tooltip("Optional physical profile selected as part of this semantic action.")]
        public string physicalProfileId;
        [Tooltip("Stable meaning-package slot. Empty uses the legacy derived slot.")]
        public string packageSlotId;
        [Tooltip("Fail atomically if this package cannot own at least one Rule Block.")]
        public bool requiresOwnedBinding = true;
        [Tooltip("Canonical adapter/profile contracts that must be available before apply.")]
        public List<string> requiredAdapterIds = new();
        [Tooltip("Canonical animation intents that must exist in the manifest/profile.")]
        public List<string> requiredAnimationIntents = new();
        [Tooltip("Additional rule blocks that must be attached to the same object.")]
        public List<OntologyRuleBlockBinding> additionalRuleBlocks = new();
        [Tooltip("Authored concepts added to the placed object, never to its template.")]
        public List<string> requiredConcepts = new();
        [Tooltip("Authored facts added to the placed object, never to its template.")]
        public List<OntologyFactEntry> requiredFacts = new();
        [Tooltip("Concepts that must already be authored before this preset can activate.")]
        public List<string> requiredExistingConcepts = new();
        [Tooltip("Relations that must already be authored before this preset can activate.")]
        public List<string> requiredExistingPredicates = new();
    }

    [CreateAssetMenu(
        fileName = "RuleBlockPresetDatabase",
        menuName = "Tormia/Ontology/Rule Block Preset Database")]
    public sealed class OntologyRuleBlockPresetDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyRuleBlockPreset> presets = new();

        public IReadOnlyList<OntologyRuleBlockPreset> Presets => presets;

        public OntologyRuleBlockPreset Find(string presetId)
        {
            return string.IsNullOrWhiteSpace(presetId)
                ? null
                : presets.FirstOrDefault(value =>
                    value != null && value.presetId == presetId);
        }

        public void Upsert(OntologyRuleBlockPreset preset)
        {
            if (preset == null ||
                string.IsNullOrWhiteSpace(preset.presetId))
            {
                return;
            }

            presets.RemoveAll(value =>
                value != null &&
                string.Equals(
                    value.presetId,
                    preset.presetId,
                    StringComparison.Ordinal));
            presets.Add(preset);
        }
    }
}
