using NUnit.Framework;
using System.Linq;
using Tormia.Ontology.Core;
using UnityEditor;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyRuntimeDataAssetTests
    {
        [Test]
        public void RuntimeDatabasesExistAndPassValidation()
        {
            var rules = Load<OntologyRuleDatabase>("Assets/Data/Ontology/RuleDatabase.asset");
            var quests = Load<OntologyQuestDatabase>("Assets/Data/Ontology/QuestDatabase.asset");
            var candidates = Load<OntologyActionCandidateDatabase>("Assets/Data/Ontology/ActionCandidateDatabase.asset");
            var effects = Load<OntologyActionEffectDatabase>("Assets/Data/Ontology/ActionEffectDatabase.asset");

            Assert.That(rules.Definitions, Is.Not.Empty);
            Assert.That(quests.Definitions, Is.Not.Empty);
            Assert.That(candidates.Definitions, Is.Not.Empty);
            Assert.That(effects.Definitions, Is.Not.Empty);
            Assert.That(OntologyRuleValidator.Validate(rules.Definitions), Is.Empty);
            Assert.That(OntologyQuestValidator.Validate(quests.Definitions), Is.Empty);
            Assert.That(OntologyActionValidator.ValidateCandidates(candidates.Definitions), Is.Empty);
            Assert.That(OntologyActionValidator.ValidateEffects(effects.Definitions), Is.Empty);

            var buoyancy = rules.Definitions.First(value =>
                value != null && value.id == "BuoyantWhenInWater");
            Assert.That(
                buoyancy.conditions.Any(value =>
                    value != null &&
                    value.kind == OntologyConditionKind.Fact &&
                    value.subject == "?profile" &&
                    value.predicate == OntologyPredicates.SupportsBehavior &&
                    value.obj == OntologyObjects.Buoyancy),
                Is.True,
                "Floating must be justified by a profile that supports Buoyancy.");

            Assert.That(
                rules.Definitions.Any(value =>
                    value != null &&
                    value.id == "SupportedObjectRestsOnSupport" &&
                    value.conditions.Any(condition =>
                        condition != null &&
                        condition.predicate == OntologyPredicates.SupportedBy) &&
                    value.effects.Any(effect =>
                        effect != null &&
                        effect.predicate == OntologyPredicates.RestingOn)),
                Is.True);
        }

        [Test]
        public void InflatableRingCatalogContainsCompleteTemporarySwimmingSkillPipeline()
        {
            var rules = Load<OntologyRuleDatabase>("Assets/Data/Ontology/RuleDatabase.asset");
            var catalog = Load<OntologyPlaceableCatalog>(
                "Assets/Data/Ontology/Placeables/PolyStylePlaceables.asset");
            var ring = catalog.Find("Inflatable_Ring_A_Simple");

            Assert.That(ring, Is.Not.Null);
            Assert.That(ring.physicalProfile, Is.Not.Null);
            Assert.That(ring.attachmentProfile, Is.Not.Null);
            Assert.That(ring.ontologyTemplate, Is.Not.Null);
            Assert.That(ring.physicalProfile.supportsBuoyancy, Is.True);
            Assert.That(
                ring.physicalProfile.profileId,
                Is.EqualTo("LightBuoyant"));
            Assert.That(
                ring.ontologyTemplate.facts.Any(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.GrantsSkill &&
                    value.obj == "Swimming"),
                Is.True);
            Assert.That(
                ring.defaultRuleBlocks.Any(value =>
                    value != null &&
                    value.ruleId == "EquippedItemGrantsTemporarySkill" &&
                    value.bindingVariable == "?item"),
                Is.True);
            Assert.That(
                rules.Definitions.Any(value =>
                    value != null && value.id == "TemporarySkillBecomesUsable"),
                Is.True);
            Assert.That(
                rules.Definitions.Any(value =>
                    value != null && value.id == "EquippedSlotItemAttaches"),
                Is.True);
            var autoEquip = rules.Definitions.First(value =>
                value != null && value.id == "AutoEquipNearbyWearable");
            Assert.That(
                autoEquip.conditions.Any(value =>
                    value != null &&
                    value.kind == OntologyConditionKind.NotFact &&
                    value.predicate == OntologyPredicates.EquippedItem),
                Is.True);
            Assert.That(
                autoEquip.conditions.Any(value =>
                    value != null &&
                    value.kind == OntologyConditionKind.Fact &&
                    value.predicate == OntologyPredicates.InteractionIntent),
                Is.True);
            Assert.That(
                rules.Definitions.Any(value =>
                    value != null && value.id == "UsableSwimmingSkillEnablesDeepWaterMovement"),
                Is.True);
            Assert.That(
                rules.Definitions.Any(value =>
                    value != null && value.id == "WaterFloatCapabilitySwimming"),
                Is.False);
            Assert.That(OntologyPlaceableSemanticValidator.Validate(ring), Is.Empty);
        }

        [Test]
        public void BuoyantProfilesDescribeBehaviorInsteadOfObjectType()
        {
            var profiles = Load<OntologyPhysicalProfileDatabase>(
                "Assets/Data/Ontology/Profiles/PhysicalProfileDatabase.asset");
            var light = profiles.Find("LightBuoyant");
            var heavy = profiles.Find("HeavyBuoyant");
            var sinking = profiles.Find("HeavySinking");

            Assert.That(light, Is.Not.Null);
            Assert.That(heavy, Is.Not.Null);
            Assert.That(sinking, Is.Not.Null);
            Assert.That(light.supportsBuoyancy, Is.True);
            Assert.That(heavy.supportsBuoyancy, Is.True);
            Assert.That(sinking.supportsBuoyancy, Is.False);
            Assert.That(
                light.dynamicColliderMode,
                Is.EqualTo(OntologyDynamicColliderMode.BoundsBox),
                "A buoyant profile must replace non-convex prefab mesh colliders " +
                "with a Rigidbody-safe collider before gravity is enabled.");
            Assert.That(
                light.buoyancyStrength,
                Is.LessThan(heavy.buoyancyStrength));
            Assert.That(
                light.submergedFraction,
                Is.LessThan(heavy.submergedFraction));
        }

        [Test]
        public void OptionalPhysicalEffectsUseCompatibilityFactsAndActivationRules()
        {
            var effects = Load<OntologyPhysicalEffectDatabase>(
                "Assets/Data/Ontology/Profiles/PhysicalEffectDatabase.asset");
            var rules = Load<OntologyRuleDatabase>(
                "Assets/Data/Ontology/RuleDatabase.asset");
            var wave = effects.Find("WaveRocking");
            var wind = effects.Find("WindDrift");

            Assert.That(wave, Is.Not.Null);
            Assert.That(wind, Is.Not.Null);
            Assert.That(
                effects.IsCompatible(wave.effectId, "LightBuoyant"),
                Is.True);
            Assert.That(
                effects.IsCompatible(wave.effectId, "HeavySinking"),
                Is.False);
            Assert.That(
                effects.IsCompatible(wind.effectId, "StaticAnchored"),
                Is.False);

            var world = new OntologyWorldState();
            effects.ApplyTo(world);
            Assert.That(
                world.HasFact(
                    wave.effectId,
                    OntologyPredicates.CompatibleWith,
                    "LightBuoyant"),
                Is.True);

            foreach (var effect in effects.Effects)
            {
                var rule = rules.Definitions.FirstOrDefault(value =>
                    value != null &&
                    value.id == effect.activationRuleId);
                Assert.That(
                    rule,
                    Is.Not.Null,
                    effect.effectId + " must reference an existing activation rule.");
                Assert.That(
                    rule.conditions.Any(value =>
                        value != null &&
                        value.predicate ==
                        OntologyPredicates.HasPhysicalEffect &&
                        value.obj == effect.effectId),
                    Is.True);
                Assert.That(
                    rule.conditions.Any(value =>
                        value != null &&
                        value.predicate ==
                        OntologyPredicates.CompatibleWith),
                    Is.True);
                Assert.That(
                    rule.effects.Any(value =>
                        value != null &&
                        value.kind == OntologyEffectKind.AddFact &&
                        value.predicate ==
                        OntologyPredicates.ActivePhysicalEffect &&
                        value.obj == effect.effectId),
                    Is.True);
            }
        }

        [Test]
        public void StoneCatalogUsesNonBuoyantDynamicPhysicalProfile()
        {
            var catalog = Load<OntologyPlaceableCatalog>(
                "Assets/Data/Ontology/Placeables/PolyStylePlaceables.asset");
            var profiles = Load<OntologyPhysicalProfileDatabase>(
                "Assets/Data/Ontology/Profiles/PhysicalProfileDatabase.asset");
            var heavy = profiles.Find("HeavySinking");

            Assert.That(heavy, Is.Not.Null);
            Assert.That(heavy.supportsBuoyancy, Is.False);
            Assert.That(
                heavy.mobilityMode,
                Is.EqualTo(OntologyPhysicalMobilityMode.Dynamic));
            var anchored = profiles.Find("StaticAnchored");
            Assert.That(anchored, Is.Not.Null);
            Assert.That(
                anchored.mobilityMode,
                Is.EqualTo(OntologyPhysicalMobilityMode.Anchored));
            var profileWorld = new OntologyWorldState();
            profiles.ApplyTo(profileWorld);
            Assert.That(
                profileWorld.HasFact(
                    heavy.profileId,
                    OntologyPredicates.MobilityMode,
                    OntologyObjects.Dynamic),
                Is.True);
            Assert.That(
                profileWorld.HasFact(
                    anchored.profileId,
                    OntologyPredicates.MobilityMode,
                    OntologyObjects.Anchored),
                Is.True);
            Assert.That(catalog.DefaultPhysicalProfile, Is.SameAs(heavy));
            Assert.That(
                heavy.constraints,
                Is.EqualTo(
                    UnityEngine.RigidbodyConstraints.FreezeRotationX |
                    UnityEngine.RigidbodyConstraints.FreezeRotationZ));
            foreach (var definitionId in new[]
                     {
                         "PolyStyle_Stone_1",
                         "PolyStyle_Stone_2",
                         "PolyStyle_Stone_3"
                     })
            {
                var stone = catalog.Find(definitionId);
                Assert.That(stone, Is.Not.Null);
                Assert.That(stone.physicalProfile, Is.SameAs(heavy));
                Assert.That(
                    stone.ontologyTemplate.facts,
                    Has.Some.Matches<OntologyFactEntry>(value =>
                        value != null &&
                        value.predicate == OntologyPredicates.HasMaterial &&
                        value.obj == OntologyObjects.Stone));
                Assert.That(
                    stone.ontologyTemplate.facts,
                    Has.Some.Matches<OntologyFactEntry>(value =>
                        value != null &&
                        value.predicate == OntologyPredicates.DensityClass &&
                        value.obj == OntologyObjects.Heavy));
            }

            foreach (var definition in catalog.Definitions.Where(value =>
                         value != null &&
                         value.definitionId != "Inflatable_Ring_A_Simple"))
            {
                Assert.That(
                    catalog.ResolvePhysicalProfile(definition),
                    Is.SameAs(heavy),
                    definition.definitionId +
                    " must inherit the non-buoyant default physical profile.");
            }
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, $"Missing runtime database: {path}");
            return asset;
        }
    }
}
