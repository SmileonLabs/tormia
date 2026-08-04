using NUnit.Framework;
using System.Linq;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyRuntimeDataAssetTests
    {
        [Test]
        public void WorldAuthorityEndpointCanSwitchEditorBetweenRemoteAndLocal()
        {
            var settings = Load<OntologyWorldAuthoritySettings>(
                "Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset");

            Assert.That(
                settings.ResolveBaseUrl(RuntimePlatform.WindowsEditor),
                Is.EqualTo("https://tov-api.punkarena.app"));
            Assert.That(
                settings.ResolveBaseUrl(RuntimePlatform.Android),
                Is.EqualTo("https://tov-api.punkarena.app"));

            var localSettings = ScriptableObject.CreateInstance<
                OntologyWorldAuthoritySettings>();
            try
            {
                localSettings.baseUrl = "http://127.0.0.1:5272/";
                localSettings.androidBaseUrl =
                    "https://tov-api.punkarena.app/";
                localSettings.useRemoteEndpointInEditor = false;

                Assert.That(
                    localSettings.ResolveBaseUrl(RuntimePlatform.WindowsEditor),
                    Is.EqualTo("http://127.0.0.1:5272"));
                Assert.That(
                    localSettings.ResolveBaseUrl(RuntimePlatform.Android),
                    Is.EqualTo("https://tov-api.punkarena.app"));
            }
            finally
            {
                Object.DestroyImmediate(localSettings);
            }
        }

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
            var authoritySettings =
                Load<OntologyWorldAuthoritySettings>(
                    "Assets/Data/Ontology/Networking/" +
                    "WorldAuthoritySettings.asset");
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
                ring.prefab.GetComponentsInChildren<MeshCollider>(true),
                Is.Empty,
                "The project-owned inflatable-ring presentation must not " +
                "cook the source asset's high-polygon convex MeshCollider. " +
                "Its authored LightBuoyant profile owns the generated " +
                "Rigidbody-safe collision proxy.");
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
                authoritySettings.developmentRuleDatabase,
                Is.SameAs(rules));
            foreach (var binding in ring.defaultRuleBlocks)
            {
                Assert.That(
                    authoritySettings.developmentRules.Any(value =>
                        value != null &&
                        value.ruleId == binding.ruleId),
                    Is.True,
                    "Every default tube Rule Block must be published before " +
                    "Authority accepts its binding.");
            }
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
        public void WeaponCatalogUsesTripleRuleBlockAndPhysicalMeaningContract()
        {
            var catalog = Load<OntologyPlaceableCatalog>(
                "Assets/Data/Ontology/Placeables/PolyStylePlaceables.asset");
            var profiles = Load<OntologyPhysicalProfileDatabase>(
                "Assets/Data/Ontology/Profiles/PhysicalProfileDatabase.asset");
            var settings = Load<OntologyWorldAuthoritySettings>(
                "Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset");
            var weaponProfile = profiles.Find("HandheldWeapon");

            Assert.That(weaponProfile, Is.Not.Null);
            Assert.That(
                weaponProfile.mobilityMode,
                Is.EqualTo(OntologyPhysicalMobilityMode.Dynamic));
            var equipmentRule = settings.developmentRules.Single(value =>
                value != null &&
                value.ruleId == "EquipItemOnInteractionIntent");
            var equipmentDefinition = Load<OntologyRuleDatabase>(
                    "Assets/Data/Ontology/RuleDatabase.asset")
                .Definitions
                .Single(value =>
                    value != null &&
                    value.id == "EquipItemOnInteractionIntent");
            Assert.That(
                equipmentRule.definitionVersion,
                Is.EqualTo(equipmentDefinition.catalogVersion),
                "The published Rule Block version must match the immutable " +
                "catalog version used by world bindings.");
            foreach (var ruleId in new[]
                     {
                         "BuoyantWhenInWater",
                         "AutoEquipNearbyWearable",
                         "EquippedItemGrantsTemporarySkill",
                         "AutoCarryNearbyCarryable",
                         "EquipItemOnInteractionIntent"
                     })
            {
                var publishedRule = settings.developmentRules.Single(value =>
                    value != null && value.ruleId == ruleId);
                var catalogRule = settings.developmentRuleDatabase.Definitions
                    .Single(value => value != null && value.id == ruleId);
                Assert.That(
                    publishedRule.definitionVersion,
                    Is.EqualTo(catalogRule.catalogVersion),
                    ruleId + " publication and binding versions must match.");
            }

            foreach (var weaponId in new[]
                     {
                         "Sword01Bronze",
                         "Sword08Corrupted",
                         "Sword15FrostVisual"
                     })
            {
                var weapon = catalog.Find(weaponId);
                Assert.That(weapon, Is.Not.Null, weaponId);
                Assert.That(
                    weapon.physicalProfile,
                    Is.SameAs(weaponProfile),
                    weaponId + " must use data-defined physical meaning.");
                Assert.That(weapon.attachmentProfile, Is.Not.Null, weaponId);
                Assert.That(
                    weapon.semanticContractVersion,
                    Is.EqualTo(OntologySemanticContracts.WeaponVersion),
                    weaponId + " must advance existing placed entities through " +
                    "the catalog-owned semantic contract migration.");
                Assert.That(
                    weapon.ruleBlockMigrations.Any(value =>
                        value != null &&
                        value.targetContractVersion == 2 &&
                        value.fromRuleId == "AutoCarryNearbyCarryable" &&
                        value.fromBindingVariable == "?object" &&
                        value.replacement != null &&
                        value.replacement.ruleId ==
                        "EquipItemOnInteractionIntent" &&
                        value.replacement.bindingVariable == "?target"),
                    Is.True,
                    weaponId + " must migrate the former automatic carry block " +
                    "without a prefab- or object-name exception.");
                Assert.That(
                    weapon.ruleBlockMigrations.Any(value =>
                        value != null &&
                        value.targetContractVersion == 7 &&
                        value.fromRuleId ==
                        "MeleeAttackOnPrimaryIntent" &&
                        value.fromBindingVariable == "?tool" &&
                        value.replacement != null &&
                        value.replacement.ruleId ==
                        "MeleeAttackOnPrimaryIntent" &&
                        value.replacement.bindingVariable == "?tool"),
                    Is.True,
                    weaponId + " must rebind the immutable v3 attack Rule " +
                    "definition during the v7 semantic migration.");
                Assert.That(
                    weapon.retiredRuleBlocks.Any(value =>
                        value != null &&
                        value.contractVersion == 3 &&
                        value.ruleId == "AutoCarryNearbyCarryable" &&
                        value.bindingVariable == "?object"),
                    Is.True,
                    weaponId + " must retire the obsolete automatic carry " +
                    "binding once without restoring later user removals.");
                Assert.That(
                    weapon.defaultRuleBlocks.Any(value =>
                        value != null &&
                        value.ruleId == "EquipItemOnInteractionIntent" &&
                        value.bindingVariable == "?target"),
                    Is.True,
                    weaponId + " must not equip without its Rule Block.");
                Assert.That(
                    weapon.ontologyTemplate.facts.Any(value =>
                        value != null &&
                        value.predicate == OntologyPredicates.PhysicalProfile &&
                        value.obj == "HandheldWeapon"),
                    Is.True,
                    weaponId + " must author the physical_profile triple.");
                Assert.That(
                    OntologyPlaceableSemanticValidator.Validate(weapon),
                    Is.Empty);
            }

            var equipAction = settings.developmentActions.First(value =>
                value != null && value.actionId == "equip_weapon");
            var equipDefinition =
                UnityEngine.JsonUtility.FromJson<OntologyActionEffectDefinition>(
                    equipAction.structuredDefinitionJson);
            Assert.That(
                equipDefinition.ruleInvocation,
                Is.Not.Null,
                "The Authority equip action must transport intent to a Rule Block.");
            Assert.That(
                equipDefinition.ruleInvocation.ruleId,
                Is.EqualTo("EquipItemOnInteractionIntent"),
                "Authority must resolve the exact assigned reusable Rule Block.");
            Assert.That(
                equipmentDefinition.conditions.Any(value =>
                    value != null &&
                    value.kind == OntologyConditionKind.EquipmentSlotAvailable),
                Is.True,
                "The Rule Block must reject a second item in the same authored slot.");
            Assert.That(
                equipDefinition.effects.Any(value =>
                    value != null &&
                    value.kind == OntologyEffectKind.SetFact &&
                    value.subject == "?target" &&
                    value.predicate == OntologyPredicates.EquippedBy &&
                    value.obj == "?actor"),
                Is.False,
                "An input/transport action must not own the final equipment " +
                "relation. The assigned reusable Rule Block must produce it.");

            var wearableAction = settings.developmentActions.First(value =>
                value != null && value.actionId == "equip_wearable");
            var wearableDefinition =
                UnityEngine.JsonUtility.FromJson<OntologyActionEffectDefinition>(
                    wearableAction.structuredDefinitionJson);
            Assert.That(
                wearableDefinition.conditions.Any(value =>
                    value != null &&
                    value.kind == OntologyConditionKind.Fact &&
                    value.predicate == OntologyPredicates.HasRuleBlock &&
                    value.obj == "AutoEquipNearbyWearable"),
                Is.True,
                "The immutable compatibility action must remain Rule-Block " +
                "gated even though normal wearable interaction uses proximity.");
        }

        [Test]
        public void ReusableEquipmentLogicMustBeOwnedByRuleBlock()
        {
            const string ruleId = "EquipItemOnInteractionIntent";
            var settings = Load<OntologyWorldAuthoritySettings>(
                "Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset");
            var rules = Load<OntologyRuleDatabase>(
                "Assets/Data/Ontology/RuleDatabase.asset");
            var equipAction = settings.developmentActions.Single(value =>
                value != null && value.actionId == "equip_weapon");
            var transportDefinition =
                UnityEngine.JsonUtility.FromJson<OntologyActionEffectDefinition>(
                    equipAction.structuredDefinitionJson);

            Assert.That(
                transportDefinition.effects.Any(effect =>
                    effect != null &&
                    effect.predicate == OntologyPredicates.EquippedBy),
                Is.False,
                "F/click/collision transport may emit intent, but must not " +
                "directly manufacture equipped_by.");
            Assert.That(
                transportDefinition.ruleInvocation?.ruleId,
                Is.EqualTo(ruleId),
                "The transport must invoke the exact Rule Block assigned to " +
                "the compatible target.");

            var rule = rules.Definitions.Single(value =>
                value != null && value.id == ruleId);
            Assert.That(
                rule.conditions.Any(condition =>
                    condition != null &&
                    condition.kind == OntologyConditionKind.Fact &&
                    condition.predicate == OntologyPredicates.InteractionIntent),
                Is.True,
                "The reusable Rule Block must consume canonical interaction intent.");
            Assert.That(
                rule.effects.Any(effect =>
                    effect != null &&
                    effect.predicate == OntologyPredicates.EquippedBy),
                Is.True,
                "The Rule Block, not the input action, must own the equipment result.");
            Assert.That(
                settings.developmentRules.Any(value =>
                    value != null && value.ruleId == ruleId),
                Is.True,
                "The reusable equipment Rule Block must be published before " +
                "an input path can invoke it.");
        }

        [Test]
        public void CanonicalGameplayPipelineStagesHaveExplicitOwners()
        {
            const string ruleId = "EquipItemOnInteractionIntent";
            var catalog = Load<OntologyPlaceableCatalog>(
                "Assets/Data/Ontology/Placeables/PolyStylePlaceables.asset");
            var rules = Load<OntologyRuleDatabase>(
                "Assets/Data/Ontology/RuleDatabase.asset");
            var settings = Load<OntologyWorldAuthoritySettings>(
                "Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset");
            var weapon = catalog.Find("Sword01Bronze");

            Assert.That(weapon, Is.Not.Null, "Triple owner is missing.");
            Assert.That(weapon.ontologyTemplate, Is.Not.Null);
            foreach (var requiredPredicate in new[]
                     {
                         OntologyPredicates.PickupBehavior,
                         OntologyPredicates.CanEquip,
                         OntologyPredicates.HasSlot,
                         OntologyPredicates.AttachmentProfile,
                         OntologyPredicates.PhysicalProfile
                     })
            {
                Assert.That(
                    weapon.ontologyTemplate.facts.Any(fact =>
                        fact != null &&
                        fact.predicate == requiredPredicate),
                    Is.True,
                    "The authored Triple stage is missing " +
                    requiredPredicate + ".");
            }

            Assert.That(
                weapon.defaultRuleBlocks.Any(binding =>
                    binding != null && binding.ruleId == ruleId),
                Is.True,
                "The compatible object must opt into the reusable Rule Block.");

            var rule = rules.Definitions.SingleOrDefault(value =>
                value != null && value.id == ruleId);
            Assert.That(
                rule,
                Is.Not.Null,
                "The Rule Definition and evaluation stage must exist before input integration.");
            Assert.That(
                rule.conditions.Any(condition =>
                    condition != null &&
                    condition.predicate == OntologyPredicates.InteractionIntent),
                Is.True,
                "The evaluator must consume canonical intent.");
            Assert.That(
                rule.effects.Any(effect =>
                    effect != null &&
                    effect.predicate == OntologyPredicates.EquippedBy),
                Is.True,
                "The evaluated Rule Block must own the result relation.");

            var transport = settings.developmentActions.Single(value =>
                value != null && value.actionId == "equip_weapon");
            var transportDefinition =
                UnityEngine.JsonUtility.FromJson<OntologyActionEffectDefinition>(
                    transport.structuredDefinitionJson);
            Assert.That(
                transportDefinition.effects.Any(effect =>
                    effect != null &&
                    effect.predicate == OntologyPredicates.EquippedBy),
                Is.False,
                "The Authority/input boundary must transport intent rather " +
                "than bypass evaluation with the final result.");
            Assert.That(
                transportDefinition.ruleInvocation?.intentPredicate,
                Is.EqualTo(OntologyPredicates.InteractionIntent),
                "The command boundary must inject only canonical ephemeral intent.");
            Assert.That(
                settings.developmentRules.Any(value =>
                    value != null && value.ruleId == ruleId),
                Is.True,
                "Shared-world evaluation must publish the exact Rule Block.");

            Assert.That(weapon.physicalProfile, Is.Not.Null);
            Assert.That(
                weapon.physicalProfile.profileId,
                Is.EqualTo("HandheldWeapon"),
                "Physical Meaning must be explicitly authored for this branch.");
            Assert.That(weapon.attachmentProfile, Is.Not.Null);
            Assert.That(
                weapon.attachmentProfile.relationPredicate,
                Is.EqualTo(OntologyPredicates.EquippedBy),
                "The adapter profile must consume the evaluated result relation.");
            Assert.That(
                weapon.attachmentProfile.kind,
                Is.EqualTo(OntologyAttachmentKind.Carryable),
                "The player-facing attachment is presentation, not a hidden rule.");
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
            var actor = profiles.Find("AuthorityKinematic");
            Assert.That(actor, Is.Not.Null);
            Assert.That(
                actor.mobilityMode,
                Is.EqualTo(OntologyPhysicalMobilityMode.AuthorityKinematic));
            Assert.That(
                profileWorld.HasFact(
                    actor.profileId,
                    OntologyPredicates.MobilityMode,
                    OntologyObjects.AuthorityKinematic),
                Is.True);
            var localCharacter =
                profiles.Find(
                    OntologyObjects.LocalCharacterController);
            Assert.That(localCharacter, Is.Not.Null);
            Assert.That(
                localCharacter.motionDriver,
                Is.EqualTo(
                    OntologyMotionDriver.LocalCharacterController));
            Assert.That(
                localCharacter.collisionRole,
                Is.EqualTo(OntologyCollisionRole.ActorBody));
            Assert.That(
                localCharacter.maximumStepHeight,
                Is.EqualTo(0.3f).Within(0.0001f),
                "Character step tuning must be authored by Physical Meaning.");
            Assert.That(
                profiles.ValidateCollisionLayers(out var collisionLayerError),
                Is.True,
                collisionLayerError);
            Assert.That(
                profiles.CollisionLayers,
                Has.Count.EqualTo(
                    System.Enum.GetValues(
                        typeof(OntologyCollisionRole)).Length));
            Assert.That(
                profiles.TryResolveCollisionLayer(
                    OntologyCollisionRole.ActorBody,
                    out var actorLayer),
                Is.True);
            Assert.That(
                actorLayer,
                Is.EqualTo(LayerMask.NameToLayer("ActorBody")));
            Assert.That(
                profiles.BuildCollisionMask(
                    OntologyCollisionRole.WalkableSupport),
                Is.EqualTo(1 << LayerMask.NameToLayer("WorldStatic")));
            Assert.That(
                catalog.DefaultPhysicalProfile,
                Is.Null,
                "A missing physical profile must not silently create Dynamic physics.");
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

            foreach (var definitionId in new[]
                     {
                         "PolyStyle_Grass_1",
                         "PolyStyle_Grass_2",
                         "PolyStyle_Environment_1"
                     })
            {
                var definition = catalog.Find(definitionId);
                Assert.That(
                    catalog.ResolvePhysicalProfile(definition),
                    Is.SameAs(anchored),
                    definitionId + " must explicitly select StaticAnchored.");
            }

            Assert.That(
                catalog.ResolvePhysicalProfile(catalog.Find("BeholderBasic")),
                Is.SameAs(actor));
            Assert.That(
                catalog.ResolvePhysicalProfile(new OntologyPlaceableDefinition()),
                Is.Null,
                "No catalog default may manufacture physics for incomplete data.");
            Assert.That(
                OntologyWorldAuthorityBridge
                    .ShouldMigrateLegacyImplicitPhysicalProfile(
                        "HeavySinking",
                        "StaticAnchored",
                        markerPresent: false),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityBridge
                    .ShouldMigrateLegacyImplicitPhysicalProfile(
                        "HeavySinking",
                        "StaticAnchored",
                        markerPresent: true),
                Is.False,
                "After migration, a later authored profile choice must remain authoritative.");
        }

        [Test]
        public void InteractionEquipPresetPublishesReusableEquipAndUnequipPackage()
        {
            var presets = Load<OntologyRuleBlockPresetDatabase>(
                "Assets/Data/Ontology/RuleBlockPresetDatabase.asset");
            var preset = presets.Find("interaction_equip");

            Assert.That(preset, Is.Not.Null);
            Assert.That(
                preset.primaryRuleId,
                Is.EqualTo(OntologyRuleBlocks.EquipItemOnInteractionIntent));
            Assert.That(
                preset.additionalRuleBlocks.Any(value =>
                    value != null &&
                    value.ruleId ==
                    OntologyRuleBlocks.UnequipItemOnInteractionIntent),
                Is.True);
            Assert.That(preset.requiredConcepts, Does.Contain("Item"));
            Assert.That(
                preset.requiredExistingConcepts,
                Does.Contain(OntologyConcepts.Carryable));
            Assert.That(
                preset.requiredFacts.Any(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.CanEquip &&
                    value.obj == bool.TrueString),
                Is.True);
            Assert.That(
                preset.requiredFacts.Any(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.EquipAction &&
                    value.obj == "equip_weapon"),
                Is.True);
            Assert.That(
                preset.requiredFacts.Any(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.UnequipAction &&
                    value.obj == "unequip_equipment"),
                Is.True);
            Assert.That(
                preset.requiredFacts.Any(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.InteractionRange &&
                    value.obj == "3"),
                Is.True);
        }

        [Test]
        public void MeleeWeaponPresetPublishesCompletePortableWeaponContract()
        {
            var presets = Load<OntologyRuleBlockPresetDatabase>(
                "Assets/Data/Ontology/RuleBlockPresetDatabase.asset");
            var preset = presets.Find("melee_weapon");

            Assert.That(preset, Is.Not.Null);
            Assert.That(
                preset.primaryRuleId,
                Is.EqualTo(OntologyRuleBlocks.MeleeAttackOnPrimaryIntent));
            Assert.That(
                preset.additionalRuleBlocks.Select(value => value.ruleId),
                Is.SupersetOf(new[]
                {
                    OntologyRuleBlocks.EquipItemOnInteractionIntent,
                    OntologyRuleBlocks.UnequipItemOnInteractionIntent,
                    OntologyRuleBlocks.SwingWeaponOnPrimaryIntent
                }));
            Assert.That(preset.requiredConcepts, Does.Contain("Weapon"));
            Assert.That(preset.physicalProfileId, Is.EqualTo("HandheldWeapon"));
            Assert.That(
                preset.requiredFacts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.AttackContactMode &&
                    value.obj == OntologyObjects.WeaponContactWindow));
            Assert.That(
                preset.requiredFacts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.AttackAction &&
                    value.obj == "attack"));
            Assert.That(
                preset.requiredFacts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.SwingAction &&
                    value.obj == "swing_weapon"));
            Assert.That(
                preset.requiredExistingPredicates,
                Does.Contain(OntologyPredicates.AttachmentProfile));
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, $"Missing runtime database: {path}");
            return asset;
        }
    }
}
