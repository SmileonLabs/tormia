using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    [CreateAssetMenu(
        fileName = "AttachmentProfileDatabase",
        menuName = "Tormia/Ontology/Attachment Profile Database")]
    public sealed class OntologyAttachmentProfileDatabase : ScriptableObject
    {
        [SerializeField] private List<OntologyAttachmentProfile> profiles = new();

        public IReadOnlyList<OntologyAttachmentProfile> Profiles => profiles;

        public OntologyAttachmentProfile Find(string profileId)
        {
            profileId = profileId?.Trim();
            return string.IsNullOrWhiteSpace(profileId)
                ? null
                : profiles.FirstOrDefault(value =>
                    value != null && value.profileId == profileId);
        }

        public void Replace(IEnumerable<OntologyAttachmentProfile> values)
        {
            profiles = values == null
                ? new List<OntologyAttachmentProfile>()
                : values.Where(value => value != null).Distinct().ToList();
        }

        public void ApplyTo(OntologyWorldState world)
        {
            if (world == null) return;
            foreach (var profile in profiles.Where(value =>
                         value != null &&
                         !string.IsNullOrWhiteSpace(value.profileId)))
            {
                world.AddConcept(
                    profile.profileId,
                    OntologyConcepts.AttachmentProfile);
                world.AddFact(
                    profile.profileId,
                    OntologyPredicates.AttachmentKind,
                    KindId(profile.kind));
                if (!string.IsNullOrWhiteSpace(profile.slotId))
                {
                    world.AddFact(
                        profile.profileId,
                        OntologyPredicates.SlotId,
                        profile.slotId);
                }
            }
        }

        public static string KindId(OntologyAttachmentKind kind)
        {
            return kind switch
            {
                OntologyAttachmentKind.Carryable => OntologyObjects.Carryable,
                OntologyAttachmentKind.Mountable => OntologyObjects.Mountable,
                _ => OntologyObjects.Wearable
            };
        }
    }
}
