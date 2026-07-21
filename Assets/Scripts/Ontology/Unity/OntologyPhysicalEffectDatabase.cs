using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(
        fileName = "PhysicalEffectDatabase",
        menuName = "Tormia/Ontology/Physical Effect Database")]
    public sealed class OntologyPhysicalEffectDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyPhysicalEffectProfile> effects = new();

        public IReadOnlyList<OntologyPhysicalEffectProfile> Effects => effects;

        public OntologyPhysicalEffectProfile Find(string effectId)
        {
            effectId = OntologyLanguagePackService.CanonicalTerm(effectId);
            return string.IsNullOrWhiteSpace(effectId)
                ? null
                : effects.FirstOrDefault(value =>
                    value != null &&
                    value.effectId == effectId);
        }

        public bool IsCompatible(string effectId, string physicalProfileId)
        {
            var effect = Find(effectId);
            physicalProfileId =
                OntologyLanguagePackService.CanonicalTerm(physicalProfileId);
            return effect != null &&
                   !string.IsNullOrWhiteSpace(physicalProfileId) &&
                   effect.compatibleProfileIds.Any(value =>
                       OntologyLanguagePackService.CanonicalTerm(value) ==
                       physicalProfileId);
        }

        public void Replace(IEnumerable<OntologyPhysicalEffectProfile> values)
        {
            effects = values == null
                ? new List<OntologyPhysicalEffectProfile>()
                : values.Where(value => value != null).Distinct().ToList();
        }

        public void ApplyTo(OntologyWorldState world)
        {
            if (world == null) return;
            foreach (var effect in effects.Where(value =>
                         value != null &&
                         !string.IsNullOrWhiteSpace(value.effectId)))
            {
                world.AddConcept(
                    effect.effectId,
                    OntologyConcepts.PhysicalEffect);
                foreach (var profileId in effect.compatibleProfileIds
                             .Where(value => !string.IsNullOrWhiteSpace(value))
                             .Distinct())
                {
                    world.AddFact(
                        effect.effectId,
                        OntologyPredicates.CompatibleWith,
                        profileId);
                }
            }
        }
    }
}
