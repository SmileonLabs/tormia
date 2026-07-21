using System.Collections.Generic;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    public enum OntologyPhysicalEffectPresentation
    {
        WindDrift,
        WaveRocking
    }

    /// <summary>
    /// Presentation data for one optional physical effect. The effect becomes active only
    /// after an ontology rule infers active_physical_effect for the owning object.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewPhysicalEffect",
        menuName = "Tormia/Ontology/Physical Effect")]
    public sealed class OntologyPhysicalEffectProfile : ScriptableObject
    {
        [Tooltip("Stable identifier stored in ontology facts and save data.")]
        public string effectId;
        [Tooltip("Database rule enabled by the runtime editor when this effect is selected.")]
        public string activationRuleId;
        [Tooltip("Physical profiles that can meaningfully use this effect.")]
        public List<string> compatibleProfileIds = new();
        [Tooltip("Unity-side expression used only after ontology inference activates the effect.")]
        public OntologyPhysicalEffectPresentation presentation;
        [Min(0f)] public float strength = 0.5f;
        [Min(0f)] public float frequency = 0.5f;
        public Vector3 direction = Vector3.right;
    }
}
