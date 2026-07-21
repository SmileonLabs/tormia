using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// A genre-neutral bundle of ontology vocabulary and executable catalog IDs.
    /// A pack describes what can be enabled; it does not silently activate rules.
    /// Activation remains an explicit world/profile operation.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewOntologyFeaturePack",
        menuName = "Tormia/Ontology/Feature Pack")]
    public sealed class OntologyFeaturePack : ScriptableObject
    {
        [Tooltip("Stable English identifier used by account/world save data.")]
        public string packId;
        [Tooltip("Localization key for the player-facing pack name.")]
        public string displayNameKey;
        [TextArea(2, 6)] public string description;
        [Tooltip("Packs are opt-in. The catalog never activates a pack merely because it exists.")]
        public bool enabledByDefault;

        [Header("Ontology vocabulary")]
        public List<string> conceptIds = new();
        public List<string> authoredFactTemplates = new();

        [Header("Executable catalog references")]
        public List<string> ruleIds = new();
        public List<string> ruleBlockPresetIds = new();
        public List<string> actionVerbs = new();
        public List<string> actionEffectIds = new();
        public List<string> physicalProfileIds = new();
    }
}
