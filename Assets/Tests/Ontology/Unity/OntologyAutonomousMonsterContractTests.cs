using System.Linq;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEditor;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyAutonomousMonsterContractTests
    {
        [Test]
        public void MonsterPresetCanGiveAnySelectedEntityCompleteMeaning()
        {
            var presets = Load<OntologyRuleBlockPresetDatabase>(
                "Assets/Data/Ontology/RuleBlockPresetDatabase.asset");
            var preset = presets.Find("autonomous_melee_monster");

            Assert.That(preset, Is.Not.Null);
            Assert.That(
                preset.primaryRuleId,
                Is.EqualTo("AutonomousMeleeCombat"));
            Assert.That(
                preset.bindingVariable,
                Is.EqualTo("?actor"));
            Assert.That(
                preset.physicalProfileId,
                Is.EqualTo("AuthorityKinematic"));
            Assert.That(
                preset.requiredConcepts,
                Does.Contain("AutonomousAgent"));
            Assert.That(
                preset.requiredFacts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == "attack_action" &&
                    value.obj == "autonomous_melee_attack"));
            Assert.That(
                preset.requiredFacts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == "hostile_to_faction" &&
                    value.obj == "PlayerFaction"));
            Assert.That(
                preset.requiredFacts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == "attack_windup_seconds" &&
                    value.obj == "0.45"));
            Assert.That(
                preset.requiredFacts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == "attack_recovery_seconds" &&
                    value.obj == "0.55"));
            Assert.That(
                preset.requiredFacts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == "attack_contact_reach" &&
                    value.obj == "0.2"));
            Assert.That(
                preset.requiredFacts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == "collision_radius" &&
                    value.obj == "0.65"));
        }

        [Test]
        public void BeholderUsesSamePortableContractAsAnEditedObject()
        {
            var catalog = Load<OntologyPlaceableCatalog>(
                "Assets/Data/Ontology/Placeables/" +
                "PolyStylePlaceables.asset");
            var beholder = catalog.Find("BeholderBasic");

            Assert.That(beholder, Is.Not.Null);
            Assert.That(
                beholder.semanticContractVersion,
                Is.EqualTo(
                    OntologySemanticContracts.AutonomousActorVersion),
                "The starter monster must use the canonical autonomous " +
                "actor contract version rather than a test-local number.");
            Assert.That(
                beholder.defaultRuleBlocks,
                Has.Some.Matches<OntologyRuleBlockBinding>(value =>
                    value != null &&
                    value.ruleId == "AutonomousMeleeCombat" &&
                    value.bindingVariable == "?actor"));
            Assert.That(
                beholder.ontologyTemplate.concepts,
                Does.Contain("AutonomousAgent"));
            Assert.That(
                beholder.ontologyTemplate.facts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == "physical_profile" &&
                    value.obj == "AuthorityKinematic"));
            Assert.That(
                beholder.introducedRuleBlocks,
                Has.Some.Matches<OntologyRuleBlockIntroduction>(value =>
                    value != null &&
                    value.contractVersion <=
                        OntologySemanticContracts.AutonomousActorVersion &&
                    value.binding.ruleId == "AutonomousMeleeCombat"));
            Assert.That(
                beholder.introducedFacts,
                Has.Some.Matches<OntologyFactIntroduction>(value =>
                    value != null &&
                    value.contractVersion <=
                        OntologySemanticContracts.AutonomousActorVersion &&
                    value.fact.predicate == "attack_contact_reach" &&
                    value.fact.obj == "0.2"));
            Assert.That(
                beholder.ruleBlockMigrations,
                Has.Some.Matches<OntologyRuleBlockMigration>(value =>
                    value != null &&
                    value.targetContractVersion ==
                        OntologySemanticContracts.AutonomousActorVersion &&
                    value.fromRuleId == "AutonomousMeleeCombat" &&
                    value.replacement != null &&
                    value.replacement.ruleId ==
                    "AutonomousMeleeCombat"));
            Assert.That(
                beholder.introducedFacts,
                Has.None.Matches<OntologyFactIntroduction>(value =>
                    value != null &&
                    value.contractVersion ==
                        OntologySemanticContracts.AutonomousActorVersion &&
                    (value.fact.predicate == "current_health" ||
                     value.fact.predicate == "is_alive")),
                "A semantic contract migration must never heal or resurrect " +
                "an existing monster.");
            Assert.That(
                beholder.defaultRuleBlocks,
                Has.Some.Matches<OntologyRuleBlockBinding>(value =>
                    value != null &&
                    value.ruleId == "AcquireNearestHostileTarget"));
            Assert.That(
                beholder.defaultRuleBlocks,
                Has.Some.Matches<OntologyRuleBlockBinding>(value =>
                    value != null &&
                    value.ruleId == "ChaseTargetWithinLeash"));
            Assert.That(
                beholder.retiredFacts,
                Has.Some.Matches<OntologyFactRetirement>(value =>
                    value != null &&
                    value.contractVersion == 3 &&
                    value.fact.predicate == "current_health" &&
                    value.fact.obj == "30" &&
                    value.onlyWhenPredicateHasDifferentValue));
            Assert.That(
                beholder.retiredFacts,
                Has.Some.Matches<OntologyFactRetirement>(value =>
                    value != null &&
                    value.contractVersion == 3 &&
                    value.fact.predicate == "is_alive" &&
                    value.fact.obj == bool.TrueString &&
                    value.onlyWhenPredicateHasDifferentValue));
        }

        [Test]
        public void CatalogDoesNotInferMonsterMeaningFromOrdinaryObjectIdentity()
        {
            var catalog = Load<OntologyPlaceableCatalog>(
                "Assets/Data/Ontology/Placeables/" +
                "PolyStylePlaceables.asset");
            var stone = catalog.Find("PolyStyle_Stone_1");

            Assert.That(stone, Is.Not.Null);
            Assert.That(
                stone.defaultRuleBlocks,
                Has.None.Matches<OntologyRuleBlockBinding>(value =>
                    value != null &&
                    value.ruleId == "AutonomousMeleeCombat"));
            Assert.That(
                stone.introducedFacts,
                Has.None.Matches<OntologyFactIntroduction>(value =>
                    value != null &&
                    value.fact.predicate == "has_concept" &&
                    value.fact.obj == "Monster"));
        }

        [Test]
        public void ConceptRelationIsDeclaredAsSetValuedForContractMigration()
        {
            var relation = OntologyLanguagePackService.Registry.Find(
                OntologyPredicates.HasConcept);

            Assert.That(relation, Is.Not.Null);
            Assert.That(
                relation.Cardinality,
                Is.EqualTo(OntologyCardinalityKind.Set));
        }

        [Test]
        public void LegacyContractRepairRespectsCanonicalCardinality()
        {
            Assert.That(
                OntologyWorldAuthorityBridge
                    .ShouldPublishLegacyInitialFact(
                        OntologyCardinalityKind.Set,
                        predicateAlreadyExists: true,
                        exactFactAlreadyExists: false),
                Is.True,
                "Another concept must not suppress a missing set member.");
            Assert.That(
                OntologyWorldAuthorityBridge
                    .ShouldPublishLegacyInitialFact(
                        OntologyCardinalityKind.Single,
                        predicateAlreadyExists: true,
                        exactFactAlreadyExists: false),
                Is.False,
                "A legacy repair must preserve an existing single-valued " +
                "runtime state.");
            Assert.That(
                OntologyWorldAuthorityBridge
                    .ShouldPublishLegacyInitialFact(
                        OntologyCardinalityKind.Unknown,
                        predicateAlreadyExists: true,
                        exactFactAlreadyExists: false),
                Is.False,
                "Unspecified relations remain conservative and preserve an " +
                "existing value.");
        }

        [Test]
        public void FactRetirementRunsOnlyAcrossItsVersionAndOnConflict()
        {
            Assert.That(
                OntologyWorldAuthorityBridge.ShouldRetireAuthoredFact(
                    2,
                    3,
                    3,
                    onlyWhenPredicateHasDifferentValue: true,
                    predicateHasDifferentValue: true),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityBridge.ShouldRetireAuthoredFact(
                    2,
                    3,
                    3,
                    onlyWhenPredicateHasDifferentValue: true,
                    predicateHasDifferentValue: false),
                Is.False,
                "The only valid default must not be removed.");
            Assert.That(
                OntologyWorldAuthorityBridge.ShouldRetireAuthoredFact(
                    3,
                    3,
                    3,
                    onlyWhenPredicateHasDifferentValue: true,
                    predicateHasDifferentValue: true),
                Is.False,
                "A later user-authored value must survive after migration.");
        }

        [Test]
        public void AuthorityPackagePublishesMonsterRuleAndAction()
        {
            var settings = Load<OntologyWorldAuthoritySettings>(
                "Assets/Data/Ontology/Networking/" +
                "WorldAuthoritySettings.asset");

            Assert.That(
                settings.developmentPackageVersion,
                Is.EqualTo("4.0.0"));
            Assert.That(
                settings.developmentRules,
                Has.Some.Matches<OntologyAuthorityDevelopmentRule>(value =>
                    value != null &&
                    value.ruleId == "AutonomousMeleeCombat" &&
                    value.definitionVersion == 5));
            Assert.That(
                settings.developmentActions,
                Has.Some.Matches<OntologyAuthorityDevelopmentAction>(value =>
                    value != null &&
                    value.actionId == "autonomous_melee_attack" &&
                    value.definitionVersion == 3 &&
                    !value.requiresTool &&
                    value.structuredDefinitionJson.Contains(
                        "AutonomousMeleeCombat")));
            Assert.That(
                settings.developmentActions,
                Has.Some.Matches<OntologyAuthorityDevelopmentAction>(value =>
                    value != null &&
                    value.actionId == "acquire_autonomous_target" &&
                    value.definitionVersion == 2 &&
                    value.structuredDefinitionJson.Contains(
                        "AcquireNearestHostileTarget")));
            Assert.That(
                settings.developmentActions,
                Has.Some.Matches<OntologyAuthorityDevelopmentAction>(value =>
                    value != null &&
                    value.actionId == "chase_autonomous_target" &&
                    value.definitionVersion == 2 &&
                    value.structuredDefinitionJson.Contains(
                        "ChaseTargetWithinLeash")));
        }

        [Test]
        public void MonsterPresentationIntentsComeFromManifest()
        {
            var manifest = Load<OntologyAnimationContentManifest>(
                "Assets/Data/Ontology/AnimationContentManifest.asset");
            var profile = Load<OntologyActorProfile>(
                "Assets/Data/Ontology/Actors/MonsterProfile.asset");

            foreach (var expected in new[]
                     {
                         ("Anim_Beholder_Attack", "MonsterAttack"),
                         ("Anim_Beholder_Walk", "MonsterWalk"),
                         ("Anim_Beholder_Run", "MonsterRun")
                     })
            {
                var entry = manifest.Entries.Single(value =>
                    value.animationId == expected.Item1);
                Assert.That(entry.clip, Is.Not.Null);
                Assert.That(entry.intents, Does.Contain(expected.Item2));
                Assert.That(entry.profiles, Does.Contain(profile));
                Assert.That(
                    profile.animationIds,
                    Does.Contain(expected.Item1));
            }

            var attack = manifest.Entries.Single(value =>
                value.animationId == "Anim_Beholder_Attack");
            Assert.That(attack.hasContactWindow, Is.True);
            Assert.That(
                attack.contactWindowStartNormalized,
                Is.LessThan(attack.contactWindowEndNormalized));
        }

        [Test]
        public void AuthorityKinematicAdapterDoesNotContainMonsterNames()
        {
            var source = System.IO.File.ReadAllText(
                "Assets/Scripts/Ontology/Unity/Networking/" +
                "OntologyAuthorityKinematicActorAdapter.cs");
            Assert.That(source, Does.Not.Contain("Beholder"));
            Assert.That(source, Does.Not.Contain("prefab.name"));
            Assert.That(source, Does.Not.Contain("gameObject.name"));
        }

        [Test]
        public void AutonomousSchedulerResolvesRuleBindingsFromImmutableActions()
        {
            var source = System.IO.File.ReadAllText(
                "server/Tormia.WorldAuthority/Program.cs");
            var start = source.IndexOf(
                "GetAutonomousActorConfigurations",
                System.StringComparison.Ordinal);
            var end = source.IndexOf(
                "GetAutonomousTargetConfigurations",
                start,
                System.StringComparison.Ordinal);

            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.GreaterThan(start));
            var schedulerQuery = source.Substring(start, end - start);
            Assert.That(
                schedulerQuery,
                Does.Contain("definition.payload #>>"));
            Assert.That(
                schedulerQuery,
                Does.Contain("target_definition.payload #>>"));
            Assert.That(
                schedulerQuery,
                Does.Contain("chase_definition.payload #>>"));
            Assert.That(
                schedulerQuery,
                Does.Not.Contain("AcquireNearestHostileTarget"));
            Assert.That(
                schedulerQuery,
                Does.Not.Contain("ChaseTargetWithinLeash"));
            Assert.That(
                schedulerQuery,
                Does.Not.Contain("AutonomousMeleeCombat"));
            Assert.That(
                schedulerQuery,
                Does.Contain("alive.predicate_id = 'is_alive'"));
            Assert.That(
                schedulerQuery,
                Does.Contain("alive.object_value::text = 'true'"));
        }

        [Test]
        public void AutonomousTargetsRequireProjectedLivingState()
        {
            var source = System.IO.File.ReadAllText(
                "server/Tormia.WorldAuthority/Program.cs");
            var start = source.IndexOf(
                "GetAutonomousTargetConfigurations",
                System.StringComparison.Ordinal);
            var end = source.IndexOf(
                "ResetPlayerMotionFromDurableState",
                start,
                System.StringComparison.Ordinal);

            Assert.That(start, Is.GreaterThanOrEqualTo(0));
            Assert.That(end, Is.GreaterThan(start));
            var query = source.Substring(start, end - start);
            Assert.That(
                query,
                Does.Contain("alive.predicate_id = 'is_alive'"));
            Assert.That(
                query,
                Does.Contain("alive.object_value::text = 'true'"));
            Assert.That(
                query,
                Does.Contain("requires_runtime_position"));
        }

        private static T Load<T>(string path)
            where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path);
            return asset;
        }
    }
}
