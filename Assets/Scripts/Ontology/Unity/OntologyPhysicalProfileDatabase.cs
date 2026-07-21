using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(
        fileName = "PhysicalProfileDatabase",
        menuName = "Tormia/Ontology/Physical Profile Database")]
    public sealed class OntologyPhysicalProfileDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyPhysicalProfile> profiles = new();

        public IReadOnlyList<OntologyPhysicalProfile> Profiles => profiles;

        public OntologyPhysicalProfile Find(string profileId)
        {
            profileId = OntologyLanguagePackService.CanonicalTerm(profileId);
            return string.IsNullOrWhiteSpace(profileId)
                ? null
                : profiles.FirstOrDefault(value =>
                    value != null &&
                    value.profileId == profileId);
        }

        public void Replace(IEnumerable<OntologyPhysicalProfile> values)
        {
            profiles = values == null
                ? new List<OntologyPhysicalProfile>()
                : values.Where(value => value != null).Distinct().ToList();
        }

        public void ApplyTo(OntologyWorldState world)
        {
            if (world == null) return;
            foreach (var profile in profiles.Where(value =>
                         value != null &&
                         !string.IsNullOrWhiteSpace(value.profileId)))
            {
                world.AddConcept(profile.profileId, OntologyConcepts.PhysicalProfile);
                world.AddFact(
                    profile.profileId,
                    OntologyPredicates.MobilityMode,
                    profile.mobilityMode == OntologyPhysicalMobilityMode.Anchored
                        ? OntologyObjects.Anchored
                        : OntologyObjects.Dynamic);
                if (profile.supportsBuoyancy)
                {
                    world.AddFact(
                        profile.profileId,
                        OntologyPredicates.SupportsBehavior,
                        OntologyObjects.Buoyancy);
                }
            }
        }
    }
}
