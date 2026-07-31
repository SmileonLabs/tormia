using System;
using System.Collections.Generic;
using System.Linq;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Validates authored semantic prerequisites without inspecting prefab names or visuals.
    /// Warnings explain why a rule may not activate; they do not redefine the ontology.
    /// </summary>
    public static class OntologyPlaceableSemanticValidator
    {
        public static List<string> Validate(OntologyPlaceableDefinition definition)
        {
            var warnings = new List<string>();
            if (definition == null)
            {
                warnings.Add("Placeable definition is missing.");
                return warnings;
            }

            var template = definition.ontologyTemplate;
            var concepts = new HashSet<string>(
                template?.concepts?
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim()) ?? Enumerable.Empty<string>(),
                StringComparer.Ordinal);
            var facts = template?.facts?
                .Where(value =>
                    value != null &&
                    !string.IsNullOrWhiteSpace(value.predicate) &&
                    !string.IsNullOrWhiteSpace(value.obj))
                .ToList() ?? new List<OntologyFactEntry>();

            ValidatePhysicalProfile(definition.physicalProfile, concepts, facts, warnings);
            ValidateAttachmentProfile(
                definition.attachmentProfile,
                definition.prefab,
                concepts,
                facts,
                warnings);
            ValidateSemanticFacts(concepts, facts, warnings);
            ValidateRuleBlocks(definition.defaultRuleBlocks, warnings);
            ValidateIntroducedFacts(
                definition.introducedFacts,
                warnings);
            ValidateIntroducedRuleBlocks(
                definition.introducedRuleBlocks,
                warnings);
            ValidateRetiredFacts(
                definition.retiredFacts,
                warnings);
            return warnings;
        }

        private static void ValidatePhysicalProfile(
            OntologyPhysicalProfile profile,
            ISet<string> concepts,
            IReadOnlyCollection<OntologyFactEntry> facts,
            ICollection<string> warnings)
        {
            if (profile == null) return;
            if (string.IsNullOrWhiteSpace(profile.profileId))
                warnings.Add("Physical Profile has an empty profile id.");
            else if (!HasFact(
                         facts,
                         OntologyPredicates.PhysicalProfile,
                         profile.profileId))
                warnings.Add(
                    "The ontology template must contain physical_profile " +
                    $"'{profile.profileId}' so runtime adapters can resolve the profile from data.");
            if (profile.mass <= 0f)
                warnings.Add("Physical Profile mass must be greater than zero.");
            if (profile.supportsBuoyancy)
            {
                if (!concepts.Contains(OntologyConcepts.FloatableObject))
                    warnings.Add("A buoyant Physical Profile should be described with the FloatableObject Concept.");
                if (string.IsNullOrWhiteSpace(profile.buoyancyRuleId))
                    warnings.Add("A buoyant Physical Profile requires an explicit buoyancy rule id.");
            }
        }

        private static void ValidateAttachmentProfile(
            OntologyAttachmentProfile profile,
            UnityEngine.GameObject prefab,
            ISet<string> concepts,
            IReadOnlyCollection<OntologyFactEntry> facts,
            ICollection<string> warnings)
        {
            if (profile == null) return;
            if (string.IsNullOrWhiteSpace(profile.profileId))
                warnings.Add("Attachment Profile has an empty profile id.");
            else if (!HasFact(
                         facts,
                         OntologyPredicates.AttachmentProfile,
                         profile.profileId))
                warnings.Add(
                    "The ontology template must contain attachment_profile " +
                    $"'{profile.profileId}' so runtime adapters can resolve the profile from data.");
            if (string.IsNullOrWhiteSpace(profile.slotId))
                warnings.Add("Attachment Profile has an empty slot id.");
            if (string.IsNullOrWhiteSpace(profile.relationPredicate))
                warnings.Add(
                    "Attachment Profile requires an explicit canonical relation predicate.");
            if (profile.localScale.x <= 0f || profile.localScale.y <= 0f || profile.localScale.z <= 0f)
                warnings.Add("Attachment Profile scale must be positive on every axis.");
            if (profile.autoEquipDistance <= 0f)
                warnings.Add("Attachment Profile proximity distance must be greater than zero.");
            var weaponGripPoints = prefab == null
                ? System.Array.Empty<OntologyAttachmentGripPoint>()
                : prefab.GetComponentsInChildren<
                    OntologyAttachmentGripPoint>(true);
            if (concepts.Contains(OntologyConcepts.Weapon) &&
                weaponGripPoints.Length != 1)
            {
                warnings.Add(
                    "A Weapon Attachment Profile requires exactly one " +
                    "OntologyAttachmentGripPoint on its project-owned prefab.");
            }
            if (concepts.Contains(OntologyConcepts.Weapon) &&
                !profile.requireItemGripPoint)
            {
                warnings.Add(
                    "A Weapon Attachment Profile must require its authored " +
                    "OntologyAttachmentGripPoint.");
            }
            else if (concepts.Contains(OntologyConcepts.Weapon) &&
                     weaponGripPoints.Length == 1 &&
                     !weaponGripPoints[0].IsCalibrated)
            {
                warnings.Add(
                    "A Weapon OntologyAttachmentGripPoint must be calibrated " +
                    "and saved through the attachment preview.");
            }

            switch (profile.kind)
            {
                case OntologyAttachmentKind.Wearable:
                    ValidateAttachmentKind(
                        OntologyConcepts.Wearable,
                        OntologyObjects.SelectThenEquip,
                        "Wearable",
                        profile,
                        concepts,
                        facts,
                        warnings);
                    break;
                case OntologyAttachmentKind.Carryable:
                    ValidateAttachmentKind(
                        OntologyConcepts.Carryable,
                        OntologyObjects.SelectThenCarry,
                        "Carryable",
                        profile,
                        concepts,
                        facts,
                        warnings);
                    break;
                case OntologyAttachmentKind.Mountable:
                    ValidateAttachmentKind(
                        OntologyConcepts.Mountable,
                        OntologyObjects.SelectThenMount,
                        "Mountable",
                        profile,
                        concepts,
                        facts,
                        warnings);
                    if (string.IsNullOrWhiteSpace(profile.mountPointPath))
                        warnings.Add("A Mountable Attachment Profile requires a mount point path.");
                    break;
            }
        }

        private static void ValidateAttachmentKind(
            string concept,
            string pickupBehavior,
            string label,
            OntologyAttachmentProfile profile,
            ISet<string> concepts,
            IReadOnlyCollection<OntologyFactEntry> facts,
            ICollection<string> warnings)
        {
            if (!concepts.Contains(concept))
                warnings.Add($"A {label} Attachment Profile requires the {concept} Concept.");
            if (!HasFact(facts, OntologyPredicates.HasSlot, profile.slotId))
                warnings.Add($"A {label} Attachment Profile requires a matching has_slot Fact.");
            if (!HasFact(facts, OntologyPredicates.PickupBehavior, pickupBehavior))
                warnings.Add(
                    $"A {label} Attachment Profile should use pickup_behavior '{pickupBehavior}'.");
        }

        private static void ValidateSemanticFacts(
            ISet<string> concepts,
            IReadOnlyCollection<OntologyFactEntry> facts,
            ICollection<string> warnings)
        {
            if (HasFact(
                    facts,
                    OntologyPredicates.PickupBehavior,
                    OntologyObjects.SelectThenEquip) &&
                !concepts.Contains(OntologyConcepts.Wearable))
            {
                warnings.Add("An equip interaction requires the Wearable Concept.");
            }

            if (HasFact(
                    facts,
                    OntologyPredicates.PickupBehavior,
                    OntologyObjects.SelectThenCarry) &&
                !concepts.Contains(OntologyConcepts.Carryable))
            {
                warnings.Add("A carry interaction requires the Carryable Concept.");
            }

            if (HasFact(
                    facts,
                    OntologyPredicates.PickupBehavior,
                    OntologyObjects.SelectThenMount) &&
                !concepts.Contains(OntologyConcepts.Mountable))
            {
                warnings.Add("A mount interaction requires the Mountable Concept.");
            }

            if (HasFact(facts, OntologyPredicates.GrantsCapability, OntologyObjects.WaterFloat) &&
                !concepts.Contains(OntologyConcepts.FlotationDevice))
            {
                warnings.Add("WaterFloat capability is normally provided by a FlotationDevice.");
            }
        }

        private static void ValidateRuleBlocks(
            IReadOnlyList<OntologyRuleBlockBinding> bindings,
            ICollection<string> warnings)
        {
            if (bindings == null) return;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < bindings.Count; index++)
            {
                var binding = bindings[index];
                if (binding == null ||
                    string.IsNullOrWhiteSpace(binding.ruleId) ||
                    string.IsNullOrWhiteSpace(binding.bindingVariable))
                {
                    warnings.Add($"Default Rule Block[{index}] has an empty rule id or binding variable.");
                    continue;
                }

                var key = binding.ruleId.Trim() + "\n" + binding.bindingVariable.Trim();
                if (!unique.Add(key))
                    warnings.Add($"Default Rule Block '{binding.ruleId}' is duplicated for '{binding.bindingVariable}'.");
            }
        }

        private static void ValidateIntroducedRuleBlocks(
            IReadOnlyList<OntologyRuleBlockIntroduction> introductions,
            ICollection<string> warnings)
        {
            if (introductions == null) return;
            for (var index = 0; index < introductions.Count; index++)
            {
                var introduction = introductions[index];
                if (introduction == null ||
                    introduction.contractVersion < 1 ||
                    introduction.binding == null ||
                    string.IsNullOrWhiteSpace(
                        introduction.binding.ruleId) ||
                    string.IsNullOrWhiteSpace(
                        introduction.binding.bindingVariable))
                {
                    warnings.Add(
                        $"Introduced Rule Block[{index}] is incomplete.");
                }
            }
        }

        private static void ValidateIntroducedFacts(
            IReadOnlyList<OntologyFactIntroduction> introductions,
            ICollection<string> warnings)
        {
            if (introductions == null) return;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < introductions.Count; index++)
            {
                var introduction = introductions[index];
                if (introduction == null ||
                    introduction.contractVersion < 1 ||
                    introduction.fact == null ||
                    string.IsNullOrWhiteSpace(
                        introduction.fact.predicate) ||
                    string.IsNullOrWhiteSpace(
                        introduction.fact.obj))
                {
                    warnings.Add(
                        $"Introduced Fact[{index}] is incomplete.");
                    continue;
                }

                var key = introduction.contractVersion + "\n" +
                          introduction.fact.predicate.Trim();
                if (!unique.Add(key))
                {
                    warnings.Add(
                        $"Introduced Fact '{introduction.fact.predicate}' " +
                        $"is duplicated for contract version " +
                        $"{introduction.contractVersion}.");
                }
            }
        }

        private static void ValidateRetiredFacts(
            IReadOnlyList<OntologyFactRetirement> retirements,
            ICollection<string> warnings)
        {
            if (retirements == null) return;
            var unique = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < retirements.Count; index++)
            {
                var retirement = retirements[index];
                if (retirement == null ||
                    retirement.contractVersion < 1 ||
                    retirement.fact == null ||
                    string.IsNullOrWhiteSpace(
                        retirement.fact.predicate) ||
                    string.IsNullOrWhiteSpace(
                        retirement.fact.obj))
                {
                    warnings.Add(
                        $"Retired Fact[{index}] is incomplete.");
                    continue;
                }

                var key = retirement.contractVersion + "\n" +
                          retirement.fact.predicate.Trim() + "\n" +
                          retirement.fact.obj.Trim();
                if (!unique.Add(key))
                {
                    warnings.Add(
                        $"Retired Fact '{retirement.fact.predicate}=" +
                        $"{retirement.fact.obj}' is duplicated for contract " +
                        $"version {retirement.contractVersion}.");
                }
            }
        }

        private static bool HasFact(
            IEnumerable<OntologyFactEntry> facts,
            string predicate,
            string obj)
        {
            return facts.Any(value =>
                value != null &&
                string.Equals(value.predicate?.Trim(), predicate, StringComparison.Ordinal) &&
                string.Equals(value.obj?.Trim(), obj, StringComparison.Ordinal));
        }
    }
}
