using System.Linq;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyCombatVerticalSliceAssetTests
    {
        [Test]
        public void CombatCatalogUsesProjectOwnedWrappersAndSemanticIntents()
        {
            var catalog = Load<OntologyCombatCatalog>(
                "Assets/Data/Ontology/Combat/CombatCatalog.asset");

            Assert.That(catalog.Weapons.Count, Is.EqualTo(3));
            Assert.That(catalog.Monsters.Count, Is.EqualTo(1));
            Assert.That(catalog.Vfx.Count, Is.EqualTo(2));
            Assert.That(catalog.Weapons.All(value =>
                value != null &&
                value.visualPrefab != null &&
                value.visualPrefab.name.StartsWith("Ontology") &&
                value.attachmentProfile != null &&
                value.weaponFamily == "Sword" &&
                value.contactModeId == "WeaponContactWindow" &&
                value.actionDefinitionVersion == 12), Is.True);
            Assert.That(catalog.FindVfx("WeaponSwingSwordBasic")?.prefab, Is.Not.Null);
            Assert.That(catalog.FindVfx("HitPhysicalLight")?.prefab, Is.Not.Null);
        }

        [Test]
        public void CombatPlaceablesCarryRequiredOntologyData()
        {
            var catalog = Load<OntologyPlaceableCatalog>(
                "Assets/Data/Ontology/Placeables/PolyStylePlaceables.asset");
            var monster = catalog.Find("BeholderBasic");

            Assert.That(monster, Is.Not.Null);
            Assert.That(monster.ontologyTemplate.concepts, Does.Contain("Damageable"));
            Assert.That(monster.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                value => value != null &&
                         value.predicate == "current_health" &&
                         value.obj == "30"));
            Assert.That(monster.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                value => value != null &&
                         value.predicate == "is_alive" &&
                         value.obj == "True"));
            Assert.That(monster.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                value => value != null &&
                         value.predicate == "combat_disposition" &&
                         value.obj == "Hostile"));
            Assert.That(monster.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                value => value != null &&
                         value.predicate == "loot_item" &&
                         value.obj == "OntologyDataFragment"));

            foreach (var id in new[]
                     {
                         "Sword01Bronze",
                         "Sword08Corrupted",
                         "Sword15FrostVisual"
                     })
            {
                var weapon = catalog.Find(id);
                Assert.That(weapon, Is.Not.Null);
                Assert.That(weapon.ontologyTemplate.concepts, Does.Contain("Weapon"));
                Assert.That(weapon.ontologyTemplate.concepts, Does.Contain("Sword"));
                Assert.That(weapon.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                    value => value != null &&
                             value.predicate == "grants_capability" &&
                             value.obj == "MeleeAttack"));
                Assert.That(weapon.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                    value => value != null &&
                             value.predicate == "attack_damage" &&
                             value.obj == "10"));
                Assert.That(weapon.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                    value => value != null &&
                             value.predicate == "attack_action" &&
                             value.obj == "attack"));
                Assert.That(weapon.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                    value => value != null &&
                             value.predicate == "swing_action" &&
                             value.obj == "swing_weapon"));
                Assert.That(weapon.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                    value => value != null &&
                             value.predicate == "attack_range" &&
                             value.obj == "3"));
                Assert.That(weapon.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                    value => value != null &&
                             value.predicate == "attack_cooldown" &&
                             value.obj == "1"));
                Assert.That(weapon.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                    value => value != null &&
                             value.predicate == "attack_playback_speed" &&
                             value.obj == "2"));
                Assert.That(weapon.ontologyTemplate.facts, Has.Some.Matches<OntologyFactEntry>(
                    value => value != null &&
                             value.predicate == "attack_contact_mode" &&
                             value.obj == "WeaponContactWindow"));
                Assert.That(weapon.ontologyTemplate.facts,
                    Has.Some.Matches<OntologyFactEntry>(value =>
                        value != null &&
                        value.predicate == "idle_animation_intent" &&
                        value.obj == "WeaponIdle"));
                Assert.That(weapon.ontologyTemplate.facts,
                    Has.Some.Matches<OntologyFactEntry>(value =>
                        value != null &&
                        value.predicate == "move_animation_intent" &&
                        value.obj == "WeaponWalk"));
                Assert.That(
                    weapon.defaultRuleBlocks,
                    Has.Some.Matches<OntologyRuleBlockBinding>(value =>
                        value != null &&
                        value.ruleId == "MeleeAttackOnPrimaryIntent" &&
                        value.bindingVariable == "?tool"));
                Assert.That(
                    weapon.defaultRuleBlocks,
                    Has.Some.Matches<OntologyRuleBlockBinding>(value =>
                        value != null &&
                        value.ruleId == "SwingWeaponOnPrimaryIntent" &&
                        value.bindingVariable == "?tool"));
                Assert.That(
                    weapon.semanticContractVersion,
                    Is.EqualTo(OntologySemanticContracts.WeaponVersion),
                    "Weapon assets must use the canonical semantic contract " +
                    "version rather than a test-local number.");
                Assert.That(
                    weapon.introducedFacts,
                    Has.Some.Matches<OntologyFactIntroduction>(value =>
                        value != null &&
                        value.contractVersion == 4 &&
                        value.fact != null &&
                        value.fact.predicate == "attack_action"));
                Assert.That(
                    weapon.introducedFacts,
                    Has.Some.Matches<OntologyFactIntroduction>(value =>
                        value != null &&
                        value.contractVersion == 5 &&
                        value.fact != null &&
                        value.fact.predicate == "swing_action" &&
                        value.fact.obj == "swing_weapon"));
                Assert.That(
                    weapon.introducedFacts,
                    Has.Some.Matches<OntologyFactIntroduction>(value =>
                        value != null &&
                        value.contractVersion == 11 &&
                        value.fact != null &&
                        value.fact.predicate == "attack_contact_reach"));
                Assert.That(
                    weapon.introducedFacts,
                    Has.Some.Matches<OntologyFactIntroduction>(value =>
                        value != null &&
                        value.contractVersion == 11 &&
                        value.fact != null &&
                        value.fact.predicate ==
                            "attack_contact_open_seconds" &&
                        value.fact.obj == "0.4"));
                Assert.That(
                    weapon.introducedFacts,
                    Has.Some.Matches<OntologyFactIntroduction>(value =>
                        value != null &&
                        value.contractVersion == 11 &&
                        value.fact != null &&
                        value.fact.predicate ==
                            "attack_contact_window_seconds" &&
                        value.fact.obj == "2"));
                Assert.That(
                    weapon.introducedFacts,
                    Has.Some.Matches<OntologyFactIntroduction>(value =>
                        value != null &&
                        value.contractVersion ==
                            OntologySemanticContracts.WeaponVersion &&
                        value.fact != null &&
                        value.fact.predicate == "idle_animation_intent" &&
                        value.fact.obj == "WeaponIdle"));
                Assert.That(
                    weapon.introducedFacts,
                    Has.Some.Matches<OntologyFactIntroduction>(value =>
                        value != null &&
                        value.contractVersion ==
                            OntologySemanticContracts.WeaponVersion &&
                        value.fact != null &&
                        value.fact.predicate == "move_animation_intent" &&
                        value.fact.obj == "WeaponWalk"));
                Assert.That(
                    weapon.introducedFacts,
                    Has.Some.Matches<OntologyFactIntroduction>(value =>
                        value != null &&
                        value.contractVersion ==
                            OntologyCombatVerticalSliceAuthoring
                                .SwingSemanticContractVersion &&
                        value.fact != null &&
                        value.fact.predicate == "attack_playback_speed" &&
                        value.fact.obj == "2"));
                Assert.That(
                    weapon.introducedRuleBlocks,
                    Has.Some.Matches<OntologyRuleBlockIntroduction>(value =>
                        value != null &&
                        value.contractVersion == 4 &&
                        value.binding != null &&
                        value.binding.ruleId ==
                        "MeleeAttackOnPrimaryIntent" &&
                        value.binding.bindingVariable == "?tool"),
                    "The migration may introduce the attack rule once, but " +
                    "must not silently restore it after a user removes it.");
                Assert.That(
                    weapon.introducedRuleBlocks,
                    Has.Some.Matches<OntologyRuleBlockIntroduction>(value =>
                        value != null &&
                        value.contractVersion ==
                            OntologyCombatVerticalSliceAuthoring
                                .SwingSemanticContractVersion &&
                        value.binding != null &&
                        value.binding.ruleId ==
                            "SwingWeaponOnPrimaryIntent" &&
                        value.binding.ruleVersion == 2 &&
                        value.binding.bindingVariable == "?tool"),
                    "The swing Rule Block must be a one-time semantic upgrade.");
            }

            Assert.That(
                catalog.Find("Sword15FrostVisual").ontologyTemplate.facts.Any(value =>
                    value != null &&
                    (value.predicate.Contains("cold") ||
                     value.predicate.Contains("freeze"))),
                Is.False,
                "A visual asset name must not silently introduce a cold gameplay rule.");
        }

        [Test]
        public void MeleeDamageRequiresAuthoredContactModeAndAnimationWindow()
        {
            var rules = Load<OntologyRuleDatabase>(
                "Assets/Data/Ontology/RuleDatabase.asset");
            var attackRule = rules.Definitions.Single(value =>
                value.id == "MeleeAttackOnPrimaryIntent");
            Assert.That(attackRule.catalogVersion, Is.EqualTo(5));
            Assert.That(
                attackRule.conditions,
                Has.Some.Matches<OntologyCondition>(value =>
                    value != null &&
                    value.subject == "?tool" &&
                    value.predicate == "attack_contact_mode" &&
                    value.obj == "WeaponContactWindow"));

            var manifest = Load<OntologyAnimationContentManifest>(
                "Assets/Data/Ontology/AnimationContentManifest.asset");
            var attackEntry = manifest.Entries.Single(value =>
                value.animationId == "Anim_Sword_LightAttack");
            Assert.That(attackEntry.hasContactWindow, Is.True);
            Assert.That(
                OntologyAnimationAdapter.IsWithinContactWindow(
                    0.5f,
                    attackEntry.contactWindowStartNormalized,
                    attackEntry.contactWindowEndNormalized),
                Is.True);
            Assert.That(
                OntologyAnimationAdapter.IsWithinContactWindow(
                    0.05f,
                    attackEntry.contactWindowStartNormalized,
                    attackEntry.contactWindowEndNormalized),
                Is.False);
        }

        [Test]
        public void CombatStateTransitionsOutliveRulesButEquipmentDoesNot()
        {
            var rules = Load<OntologyRuleDatabase>(
                "Assets/Data/Ontology/RuleDatabase.asset");
            var persistentRuleIds = new[]
            {
                OntologyRuleBlocks.MeleeAttackOnPrimaryIntent,
                OntologyRuleBlocks.AutonomousMeleeCombat,
                OntologyRuleBlocks.RespawnPlayerOnDeath,
                OntologyRuleBlocks.CollectAvailableLoot
            };

            foreach (var ruleId in persistentRuleIds)
            {
                var rule = rules.Definitions.Single(value =>
                    value != null && value.id == ruleId);
                Assert.That(
                    rule.effects,
                    Is.Not.Empty,
                    ruleId + " must author a state transition.");
                Assert.That(
                    rule.effects.All(value =>
                        value != null &&
                        value.resultLifetime ==
                        OntologyRuleResultLifetime.DurableState),
                    Is.True,
                    ruleId +
                    " must not rewind accepted health, death, respawn, or loot history when removed.");
            }

            var equipmentRule = rules.Definitions.Single(value =>
                value != null &&
                value.id ==
                OntologyRuleBlocks.EquipItemOnInteractionIntent);
            Assert.That(
                equipmentRule.effects,
                Is.Not.Empty);
            Assert.That(
                equipmentRule.effects.All(value =>
                    value != null &&
                    value.resultLifetime ==
                    OntologyRuleResultLifetime.RuleBound),
                Is.True,
                "Removing the equipment Rule Block must remove the behavior-owned relation.");
        }

        [Test]
        public void DisabledWeaponColliderStillObservesOnlyApprovedContact()
        {
            var weaponObject = new GameObject("ContactTestWeapon");
            var approvedObject = new GameObject("ApprovedTarget");
            var otherObject = new GameObject("OtherTarget");
            try
            {
                var volume = weaponObject.AddComponent<BoxCollider>();
                volume.isTrigger = true;
                volume.size = Vector3.one;
                var weapon =
                    weaponObject.AddComponent<OntologyCombatWeaponPresenter>();
                weapon.Configure(
                    string.Empty,
                    weaponObject.transform,
                    volume,
                    OntologyObjects.WeaponContactWindow);
                volume.enabled = false;

                approvedObject.transform.position =
                    new Vector3(0.4f, 0f, 0f);
                approvedObject.AddComponent<SphereCollider>().isTrigger = true;
                var approved = approvedObject.AddComponent<
                    OntologyCombatTargetPresenter>();

                otherObject.transform.position =
                    new Vector3(-0.4f, 0f, 0f);
                otherObject.AddComponent<SphereCollider>().isTrigger = true;
                otherObject.AddComponent<OntologyCombatTargetPresenter>();
                Physics.SyncTransforms();

                Assert.That(
                    weapon.SupportsContactMode(
                        OntologyObjects.WeaponContactWindow),
                    Is.True);
                Assert.That(
                    weapon.TryObserveApprovedContact(
                        approved,
                        out var contactPoint),
                    Is.True);
                Assert.That(contactPoint.x, Is.GreaterThanOrEqualTo(0f));

                approvedObject.transform.position =
                    new Vector3(5f, 0f, 0f);
                Physics.SyncTransforms();
                Assert.That(
                    weapon.TryObserveApprovedContact(
                        approved,
                        out _),
                    Is.False,
                    "Another overlapping target must not satisfy the " +
                    "Authority-approved target contact.");
            }
            finally
            {
                Object.DestroyImmediate(otherObject);
                Object.DestroyImmediate(approvedObject);
                Object.DestroyImmediate(weaponObject);
            }
        }

        [Test]
        public void WeaponSweepOrientationFollowsAuthoredBladeAndMotionAxes()
        {
            Assert.That(
                OntologyCombatWeaponPresenter.TryCalculateSweepRotation(
                    Vector3.up,
                    Vector3.right,
                    0.001f,
                    out var rotation),
                Is.True);
            Assert.That(
                Vector3.Dot(
                    rotation * Vector3.up,
                    Vector3.up),
                Is.GreaterThan(0.999f));
            Assert.That(
                Vector3.Dot(
                    rotation * Vector3.right,
                    Vector3.right),
                Is.GreaterThan(0.999f));
            Assert.That(
                OntologyCombatWeaponPresenter.TryCalculateSweepRotation(
                    Vector3.up,
                    Vector3.zero,
                    0.001f,
                    out _),
                Is.False);
        }

        [Test]
        public void WeaponContactSweepSubstepsCoverLinearAndAngularMotion()
        {
            Assert.That(
                OntologyCombatWeaponPresenter.CalculateContactSweepSubsteps(
                    linearDistance: 0.24f,
                    angularDistance: 25f,
                    linearStep: 0.08f,
                    angularStep: 10f,
                    maximumSubsteps: 12),
                Is.EqualTo(3));
            Assert.That(
                OntologyCombatWeaponPresenter.CalculateContactSweepSubsteps(
                    linearDistance: 5f,
                    angularDistance: 180f,
                    linearStep: 0.08f,
                    angularStep: 10f,
                    maximumSubsteps: 12),
                Is.EqualTo(12));
        }

        [Test]
        public void WeaponContactSweepObservesTargetSkippedBetweenFrames()
        {
            var weaponObject = new GameObject("SweptContactWeapon");
            var targetObject = new GameObject("SweptContactTarget");
            try
            {
                var volume = weaponObject.AddComponent<BoxCollider>();
                volume.isTrigger = true;
                volume.size = Vector3.one * 0.2f;
                var weapon =
                    weaponObject.AddComponent<OntologyCombatWeaponPresenter>();
                weapon.Configure(
                    string.Empty,
                    weaponObject.transform,
                    volume,
                    OntologyObjects.WeaponContactWindow);
                targetObject.AddComponent<SphereCollider>().radius = 0.2f;
                var target =
                    targetObject.AddComponent<OntologyCombatTargetPresenter>();

                weaponObject.transform.position = Vector3.left;
                Physics.SyncTransforms();
                Assert.That(
                    weapon.TryObserveApprovedContact(target, out _),
                    Is.False);

                weaponObject.transform.position = Vector3.right;
                Physics.SyncTransforms();
                Assert.That(
                    weapon.TryObserveApprovedContact(
                        target,
                        out var contactPoint),
                    Is.True,
                    "Continuous sweep sampling must observe a target crossed " +
                    "between two non-overlapping frame positions.");
                Assert.That(
                    Mathf.Abs(contactPoint.x),
                    Is.LessThan(0.3f));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(weaponObject);
            }
        }

        [Test]
        public void ContactApproachUsesOrientedWeaponSurface()
        {
            var weaponObject = new GameObject("ApproachWeapon");
            var targetObject = new GameObject("ApproachTarget");
            try
            {
                weaponObject.transform.position =
                    new Vector3(0.5f, 1f, 0f);
                var volume = weaponObject.AddComponent<BoxCollider>();
                volume.isTrigger = true;
                volume.size = new Vector3(0.2f, 2f, 0.2f);
                var weapon =
                    weaponObject.AddComponent<OntologyCombatWeaponPresenter>();
                weapon.Configure(
                    string.Empty,
                    weaponObject.transform,
                    volume,
                    OntologyObjects.WeaponContactWindow);

                targetObject.transform.position =
                    new Vector3(2f, 1f, 0f);
                targetObject.AddComponent<SphereCollider>().radius = 0.5f;
                var target =
                    targetObject.AddComponent<OntologyCombatTargetPresenter>();
                Physics.SyncTransforms();

                Assert.That(
                    weapon.TryEstimateContactApproachDistance(
                        Vector3.zero,
                        target,
                        out var approachDistance),
                    Is.True);
                Assert.That(
                    approachDistance,
                    Is.EqualTo(1.1f).Within(0.02f),
                    "A vertical blade's long Y axis must not be counted as " +
                    "sideways reach toward the target.");
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(weaponObject);
            }
        }

        [Test]
        public void PrimedContactSampleSeedsFirstApprovedSweep()
        {
            var weaponObject = new GameObject("PrimedSweepWeapon");
            var targetObject = new GameObject("PrimedSweepTarget");
            try
            {
                var volume = weaponObject.AddComponent<BoxCollider>();
                volume.isTrigger = true;
                volume.size = Vector3.one * 0.2f;
                var weapon =
                    weaponObject.AddComponent<OntologyCombatWeaponPresenter>();
                weapon.Configure(
                    string.Empty,
                    weaponObject.transform,
                    volume,
                    OntologyObjects.WeaponContactWindow);
                targetObject.AddComponent<SphereCollider>().radius = 0.2f;
                var target =
                    targetObject.AddComponent<OntologyCombatTargetPresenter>();

                weaponObject.transform.position = Vector3.left;
                Physics.SyncTransforms();
                weapon.PrimeContactSample();

                weaponObject.transform.position = Vector3.right;
                Physics.SyncTransforms();
                Assert.That(
                    weapon.TryObserveApprovedContact(
                        target,
                        out var contactPoint),
                    Is.True,
                    "The last pose before the manifest contact window must " +
                    "seed its first approved continuous sweep.");
                Assert.That(
                    Mathf.Abs(contactPoint.x),
                    Is.LessThan(0.3f));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(weaponObject);
            }
        }

        [Test]
        public void CombatBodyClearanceUsesAuthoredColliderSurfaces()
        {
            var actorObject = new GameObject("ClearanceActor");
            var targetObject = new GameObject("ClearanceTarget");
            try
            {
                actorObject.transform.position = Vector3.zero;
                var actorController =
                    actorObject.AddComponent<CharacterController>();
                actorController.radius = 0.45f;
                actorController.height = 2f;
                actorController.center = Vector3.up;
                actorController.skinWidth = 0.03f;

                targetObject.transform.position =
                    new Vector3(3f, 0f, 0f);
                var targetCollider =
                    targetObject.AddComponent<SphereCollider>();
                targetCollider.radius = 0.75f;
                targetCollider.center = Vector3.up * 1.1f;
                targetCollider.isTrigger = true;
                Physics.SyncTransforms();

                Assert.That(
                    OntologyCombatController
                        .CalculatePlanarBodyClearanceDistance(
                            actorController.bounds,
                            actorObject.transform.position,
                            targetCollider.bounds,
                            targetObject.transform.position,
                            actorController.skinWidth),
                    Is.EqualTo(1.23f).Within(0.02f));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(actorObject);
            }
        }

        [Test]
        public void AttackInputPublishesAuthoredActionsWithoutLocalRuleEvaluation()
        {
            var toolId = System.Guid.NewGuid();
            var projection = new OntologyAuthorityWorldProjection
            {
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = toolId.ToString("D"),
                        predicateId = "attack_action",
                        objectKind = "canonical",
                        objectCanonicalId = "attack"
                    },
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = toolId.ToString("D"),
                        predicateId = "swing_action",
                        objectKind = "canonical",
                        objectCanonicalId = "swing_weapon"
                    }
                }
            };

            Assert.That(
                OntologyCombatController.TryResolveCanonicalFact(
                    projection,
                    toolId,
                    "attack_action",
                    out var actionId),
                Is.True);
            Assert.That(
                OntologyCombatController.TryResolveCanonicalFact(
                    projection,
                    toolId,
                    "swing_action",
                    out var swingActionId),
                Is.True);
            Assert.That(swingActionId, Is.EqualTo("swing_weapon"));
            Assert.That(actionId, Is.EqualTo("attack"));
            Assert.That(
                typeof(OntologyCombatController).GetMethod(
                    "TryBeginPrimaryPointerIntent"),
                Is.Not.Null);
            Assert.That(
                typeof(OntologyWorldAuthorityClient).GetMethod(
                    "PreviewActionRoutine"),
                Is.Not.Null,
                "Shared click routing must ask Authority to evaluate the " +
                "assigned Rule Block.");
            Assert.That(
                typeof(OntologyCombatController).GetMethod(
                    "ProjectionDeclaresLivingHostileTarget"),
                Is.Null,
                "Unity must not duplicate hostile/alive Rule Block conditions.");
            Assert.That(
                typeof(OntologyCombatController).GetMethod(
                    "TryResolvePositiveNumberFact"),
                Is.Null,
                "Unity must not duplicate the Authority attack-range evaluator.");
            Assert.That(
                typeof(OntologyCombatController).GetMethod(
                    "IsWithinAttackRange"),
                Is.Null,
                "Unity must not decide attack eligibility from local transforms.");
        }

        [Test]
        public void AttackFactIntroductionRunsOnceAndPreservesAuthoredValue()
        {
            Assert.That(
                OntologyWorldAuthorityBridge.ShouldIntroduceAuthoredFact(
                    3,
                    4,
                    4,
                    false),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityBridge.ShouldIntroduceAuthoredFact(
                    3,
                    4,
                    4,
                    true),
                Is.False,
                "An already authored attack value must not be overwritten.");
            Assert.That(
                OntologyWorldAuthorityBridge.ShouldIntroduceAuthoredFact(
                    4,
                    4,
                    4,
                    false),
                Is.False,
                "A user removal after migration must not be silently restored.");
            Assert.That(
                OntologyWorldAuthorityBridge.ShouldIntroduceAuthoredFact(
                    4,
                    5,
                    5,
                    false),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityBridge.ShouldIntroduceAuthoredFact(
                    5,
                    5,
                    5,
                    false),
                Is.False,
                "A removed swing_action value must not be silently restored.");
        }

        [Test]
        public void AuthorityPlacementPayloadCarriesTypedTemplateFactsAtomically()
        {
            var health = OntologyWorldAuthorityClient.CreateInitialFact(
                "current_health",
                "30");
            var alive = OntologyWorldAuthorityClient.CreateInitialFact(
                "is_alive",
                "True");
            var payload = OntologyWorldAuthorityClient.CreatePlaceEntityPayload(
                System.Guid.NewGuid(),
                "BeholderBasic",
                "Beholder",
                UnityEngine.Vector3.zero,
                UnityEngine.Vector3.zero,
                UnityEngine.Vector3.one,
                null,
                new[] { health, alive });

            Assert.That(health.objectKind, Is.EqualTo("number"));
            Assert.That(health.objectValueJson, Is.EqualTo("30"));
            Assert.That(alive.objectKind, Is.EqualTo("boolean"));
            Assert.That(alive.objectValueJson, Is.EqualTo("true"));
            Assert.That(payload, Does.Contain("\"initialFacts\":["));
            Assert.That(payload, Does.Contain("\"objectKind\":\"number\""));
            Assert.That(payload, Does.Contain("\"objectKind\":\"boolean\""));
        }

        [Test]
        public void AuthorityProjectionRestoresNumericAndBooleanOntologyValues()
        {
            var resolver = typeof(OntologyWorldAuthorityBridge).GetMethod(
                "ResolveRemoteFactObject",
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.NonPublic);

            Assert.That(resolver, Is.Not.Null);
            Assert.That(
                resolver.Invoke(null, new object[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        objectKind = "number",
                        objectValueJson = "30"
                    }
                }),
                Is.EqualTo("30"));
            Assert.That(
                resolver.Invoke(null, new object[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        objectKind = "boolean",
                        objectValueJson = "true"
                    }
                }),
                Is.EqualTo("True"));
        }

        [Test]
        public void EquipInputWaitsForAuthorityFirstPlacementWithinGraceWindow()
        {
            Assert.That(
                OntologyCombatController.ShouldRetryPendingEquip(
                    12f,
                    11f,
                    false,
                    false),
                Is.True);
            Assert.That(
                OntologyCombatController.ShouldRetryPendingEquip(
                    12f,
                    13f,
                    false,
                    false),
                Is.False,
                "An expired input must not equip a later unrelated weapon.");
            Assert.That(
                OntologyCombatController.ShouldRetryPendingEquip(
                    12f,
                    11f,
                    false,
                    true),
                Is.False,
                "An existing Authority equipment relation cancels the retry.");
        }

        [Test]
        public void EquipCandidateUsesCanonicalAuthorityConceptDuringPresentationRefresh()
        {
            var weaponId = System.Guid.NewGuid();
            var projection = new OntologyAuthorityWorldProjection
            {
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = weaponId.ToString("D"),
                        predicateId = OntologyPredicates.HasConcept,
                        objectKind = "canonical",
                        objectCanonicalId = OntologyConcepts.Weapon
                    }
                }
            };

            Assert.That(
                OntologyCombatController.ProjectionDeclaresConcept(
                    projection,
                    weaponId,
                    OntologyConcepts.Weapon),
                Is.True);
            Assert.That(
                OntologyCombatController.ProjectionDeclaresConcept(
                    projection,
                    weaponId,
                    OntologyConcepts.Damageable),
                Is.False);

            projection.facts =
                System.Array.Empty<OntologyAuthorityFactProjection>();
            Assert.That(
                OntologyCombatController.ProjectionDeclaresConcept(
                    projection,
                    weaponId,
                    OntologyConcepts.Weapon),
                Is.False);
        }

        [Test]
        public void EquipCommandRequiresAuthorityRuntimePosition()
        {
            Assert.That(
                OntologyCombatController.IsRuntimePositionReady(null),
                Is.False);
            Assert.That(
                OntologyCombatController.IsRuntimePositionReady(
                    new OntologyAuthorityPlayerMotionState
                    {
                        zoneKey = string.Empty
                    }),
                Is.False);
            Assert.That(
                OntologyCombatController.IsRuntimePositionReady(
                    new OntologyAuthorityPlayerMotionState
                    {
                        zoneKey = "world_main",
                        motionStatus = "idle"
                    }),
                Is.True);
            Assert.That(
                OntologyCombatController.ResolveEquipFailureStatusId(
                    "action_actor_runtime_position_unavailable"),
                Is.EqualTo("equip_actor_position_unavailable"));
        }

        [Test]
        public void PlayerCombatUsesAuthoredAttackInputAndPresentationAdapters()
        {
            const string scenePath = "Assets/Scenes/TormiaWorld.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var combat = scene.GetRootGameObjects()
                    .SelectMany(value =>
                        value.GetComponentsInChildren<
                            OntologyCombatController>(true))
                    .Single();
                var serialized = new SerializedObject(combat);
                var attackReference = serialized
                    .FindProperty("attackActionReference")
                    .objectReferenceValue as
                    UnityEngine.InputSystem.InputActionReference;
                var equipReference = serialized
                    .FindProperty("equipActionReference")
                    .objectReferenceValue as
                    UnityEngine.InputSystem.InputActionReference;
                Assert.That(attackReference, Is.Not.Null);
                Assert.That(equipReference, Is.Not.Null);
                Assert.That(equipReference.action.actionMap.name,
                    Is.EqualTo("Player"));
                Assert.That(equipReference.action.name, Is.EqualTo("Equip"));
                Assert.That(
                    equipReference.action.bindings.Any(value =>
                        value.effectivePath == "<Keyboard>/f"),
                    Is.True,
                    "Equip input must be authored in the shared Input Action " +
                    "asset instead of constructed by gameplay code.");
                Assert.That(
                    serialized.FindProperty("carryableEquipActionId"),
                    Is.Null,
                    "The equipped object's equip_action Fact must select the " +
                    "Authority action.");
                Assert.That(
                    serialized.FindProperty("unequipActionId"),
                    Is.Null,
                    "The equipped object's unequip_action Fact must select " +
                    "the Authority action.");
                Assert.That(
                    attackReference.action.actionMap.name,
                    Is.EqualTo("Player"));
                Assert.That(
                    attackReference.action.name,
                    Is.EqualTo("Attack"));
                Assert.That(
                    attackReference.action.bindings.Any(value =>
                        value.effectivePath == "<Mouse>/leftButton"),
                    Is.True);
                Assert.That(
                    attackReference.action.bindings.Any(value =>
                        value.effectivePath == "<Mouse>/rightButton"),
                    Is.False,
                    "Camera look-hold and combat attack must not share the " +
                    "same mouse binding.");
                Assert.That(
                    combat.GetComponent<OntologyAuthorityTargetingAdapter>(),
                    Is.Not.Null);
                Assert.That(
                    combat.GetComponent<OntologyWorldEntryGroundingAdapter>(),
                    Is.Not.Null);
                Assert.That(
                    typeof(OntologyCombatController).GetField(
                        "localTargetingDistance",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic),
                    Is.Null,
                    "Unity must not duplicate the Authority attack-range rule.");
                Assert.That(
                    typeof(OntologyCombatController).GetField(
                        "attackActionId",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic),
                    Is.Null,
                    "The equipped tool's attack_action Fact must select the " +
                    "Authority action; Unity must not own an action-id fallback.");
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TargetingAndGroundingPoliciesKeepPresentationBoundaries()
        {
            var actor = new UnityEngine.GameObject("Actor");
            var actorChild = new UnityEngine.GameObject("ActorChild");
            var unrelated = new UnityEngine.GameObject("Unrelated");
            try
            {
                actorChild.transform.SetParent(actor.transform, false);
                Assert.That(
                    OntologyAuthorityTargetingAdapter.IsPartOf(
                        actorChild.transform,
                        actor.transform),
                    Is.True);
                Assert.That(
                    OntologyAuthorityTargetingAdapter.IsPartOf(
                        unrelated.transform,
                        actor.transform),
                    Is.False);
                Assert.That(
                    OntologyWorldEntryGroundingAdapter.CanSettle(
                        true,
                        false,
                        false),
                    Is.True);
                Assert.That(
                    OntologyWorldEntryGroundingAdapter.CanSettle(
                        true,
                        true,
                        false),
                    Is.False);
                Assert.That(
                    OntologyWorldEntryGroundingAdapter.CanSettle(
                        true,
                        false,
                        true),
                    Is.False);
                Assert.That(
                    OntologyWorldEntryGroundingAdapter.CanSettle(
                        false,
                        false,
                        false),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actor);
                UnityEngine.Object.DestroyImmediate(unrelated);
            }
        }

        [Test]
        public void CombatPointerAssist_UsesOnlyAuthoredColliderFootprint()
        {
            var bounds = new UnityEngine.Bounds(
                new UnityEngine.Vector3(10f, 5f, 20f),
                new UnityEngine.Vector3(2f, 4f, 2f));
            Assert.That(
                OntologyAuthorityTargetingAdapter
                    .IsWithinPlanarBoundsFootprint(
                        bounds,
                        new UnityEngine.Vector3(10.9f, 0f, 20.9f),
                        0f),
                Is.True,
                "Ground directly beneath an authored combat collider must " +
                "remain a combat pointer observation.");
            Assert.That(
                OntologyAuthorityTargetingAdapter
                    .IsWithinPlanarBoundsFootprint(
                        bounds,
                        new UnityEngine.Vector3(11.1f, 0f, 20f),
                        0.15f),
                Is.True,
                "Small pointer imprecision may use the serialized assist " +
                "padding around the authored collider footprint.");
            Assert.That(
                OntologyAuthorityTargetingAdapter
                    .IsWithinPlanarBoundsFootprint(
                        bounds,
                        new UnityEngine.Vector3(11.3f, 0f, 20f),
                        0.15f),
                Is.False,
                "The assist must not turn unrelated terrain clicks into " +
                "combat intent.");
        }

        [Test]
        public void CombatPointerAssist_RecoversGroundHitBeneathProjectedTarget()
        {
            var actor = new UnityEngine.GameObject("PointerActor");
            var clientObject = new UnityEngine.GameObject("PointerAuthorityClient");
            var cameraObject = new UnityEngine.GameObject("PointerCamera");
            var ground = UnityEngine.GameObject.CreatePrimitive(
                UnityEngine.PrimitiveType.Cube);
            var target = new UnityEngine.GameObject("PointerTarget");
            try
            {
                var actorIdentity =
                    actor.AddComponent<OntologyAuthorityEntityIdentity>();
                actorIdentity.SetGuid(System.Guid.NewGuid());
                var adapter =
                    actor.AddComponent<OntologyAuthorityTargetingAdapter>();
                var client =
                    clientObject.AddComponent<OntologyWorldAuthorityClient>();
                var camera = cameraObject.AddComponent<UnityEngine.Camera>();
                camera.pixelRect = new UnityEngine.Rect(0f, 0f, 800f, 600f);
                cameraObject.transform.position =
                    new UnityEngine.Vector3(0f, 5f, -10f);
                cameraObject.transform.LookAt(UnityEngine.Vector3.zero);

                ground.transform.position =
                    new UnityEngine.Vector3(0f, -0.5f, 0f);
                ground.transform.localScale =
                    new UnityEngine.Vector3(20f, 1f, 20f);

                var targetIdentity =
                    target.AddComponent<OntologyAuthorityEntityIdentity>();
                var targetId = targetIdentity.EnsureGuid();
                var targetCollider =
                    target.AddComponent<UnityEngine.SphereCollider>();
                targetCollider.center =
                    new UnityEngine.Vector3(0f, 1f, 0f);
                targetCollider.radius = 0.75f;
                targetCollider.isTrigger = true;
                var presenter =
                    target.AddComponent<OntologyCombatTargetPresenter>();
                presenter.Configure(
                    targetIdentity,
                    target.transform,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    null,
                    targetCollider);

                var projection = new OntologyAuthorityWorldProjection
                {
                    entities = new[]
                    {
                        new OntologyAuthorityEntityProjection
                        {
                            entityId = targetId.ToString("D"),
                            templateId = "TestCombatTarget"
                        }
                    },
                    facts = new[]
                    {
                        new OntologyAuthorityFactProjection
                        {
                            subjectEntityId = targetId.ToString("D"),
                            predicateId = OntologyPredicates.IsAlive,
                            objectValueJson = "true"
                        },
                        new OntologyAuthorityFactProjection
                        {
                            subjectEntityId = targetId.ToString("D"),
                            predicateId = OntologyPredicates.CurrentHealth,
                            objectValueJson = "10"
                        }
                    }
                };
                var projectionField = typeof(OntologyWorldAuthorityClient)
                    .GetField(
                        "currentProjection",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);
                Assert.That(projectionField, Is.Not.Null);
                projectionField.SetValue(client, projection);
                Assert.That(client.CurrentProjection, Is.SameAs(projection));
                UnityEngine.Physics.SyncTransforms();

                var groundScreen =
                    camera.WorldToScreenPoint(UnityEngine.Vector3.zero);
                Assert.That(
                    client.ContainsProjectedEntity(targetId),
                    Is.True,
                    "The test target must exist in the Authority projection.");
                Assert.That(
                    UnityEngine.Object
                        .FindObjectsByType<OntologyCombatTargetPresenter>(
                            UnityEngine.FindObjectsInactive.Include)
                        .Contains(presenter),
                    Is.True,
                    "The enabled target presenter must be discoverable.");
                var pointerRay = camera.ScreenPointToRay(groundScreen);
                var pointerHits = UnityEngine.Physics.RaycastAll(
                    pointerRay,
                    500f,
                    ~0,
                    UnityEngine.QueryTriggerInteraction.Collide);
                Assert.That(
                    pointerHits,
                    Is.Not.Empty,
                    "The pointer ray must reach the authored ground surface.");
                Assert.That(
                    adapter.TryResolvePointerCandidate(
                        camera,
                        groundScreen,
                        actorIdentity,
                        null,
                        client,
                        out var resolved,
                        out _),
                    Is.True,
                    "Hits: " + string.Join(
                        ", ",
                        pointerHits.Select(value =>
                            value.collider.name + "@" +
                            value.point.ToString("F2"))));
                Assert.That(resolved, Is.EqualTo(targetIdentity));

                var unrelatedGroundScreen = camera.WorldToScreenPoint(
                    new UnityEngine.Vector3(2f, 0f, 0f));
                Assert.That(
                    adapter.TryResolvePointerCandidate(
                        camera,
                        unrelatedGroundScreen,
                        actorIdentity,
                        null,
                        client,
                        out _,
                        out _),
                    Is.False,
                    "Terrain outside the authored target footprint must remain " +
                    "available to click-to-move.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actor);
                UnityEngine.Object.DestroyImmediate(clientObject);
                UnityEngine.Object.DestroyImmediate(cameraObject);
                UnityEngine.Object.DestroyImmediate(ground);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ProjectOwnedWeaponsHaveOneCalibratedGripPoint()
        {
            var attachmentProfile = Load<OntologyAttachmentProfile>(
                "Assets/Data/Ontology/Profiles/" +
                "RightHandCarryAttachmentProfile.asset");
            Assert.That(
                attachmentProfile.requireItemGripPoint,
                Is.True,
                "Weapons must not silently return to a generic attachment " +
                "pose when prefab-owned grip metadata is removed.");

            foreach (var path in new[]
                     {
                         "Assets/Prefabs/Ontology/Combat/" +
                         "OntologySword01Bronze.prefab",
                         "Assets/Prefabs/Ontology/Combat/" +
                         "OntologySword08Corrupted.prefab",
                         "Assets/Prefabs/Ontology/Combat/" +
                         "OntologySword15FrostVisual.prefab"
                     })
            {
                var prefab = Load<UnityEngine.GameObject>(path);
                var grips = prefab.GetComponentsInChildren<
                    OntologyAttachmentGripPoint>(true);
                Assert.That(grips, Has.Length.EqualTo(1), path);
                Assert.That(grips[0].IsCalibrated, Is.True, path);
            }
        }

        [Test]
        public void WorldPlayerPublishesCollisionResolvedMotion()
        {
            const string scenePath = "Assets/Scenes/TormiaWorld.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var reconciler = scene.GetRootGameObjects()
                    .SelectMany(value =>
                        value.GetComponentsInChildren<
                            OntologyWorldAuthorityPlayerMotionReconciler>(true))
                    .Single();
                var serialized = new SerializedObject(reconciler);
                Assert.That(
                    serialized.FindProperty(
                            "publishCollisionResolvedPose")
                        .boolValue,
                    Is.True,
                    "The local avatar must publish its collision-resolved pose " +
                    "for shared runtime presentation.");
                Assert.That(
                    reconciler.GetComponent<OntologyInputSystemPlayerInput>(),
                    Is.Not.Null,
                    "The resolved-pose publisher must observe the local " +
                    "CharacterController owner.");
                Assert.That(
                    serialized.FindProperty(
                            "publicationsPerSecond")
                        .floatValue,
                    Is.GreaterThanOrEqualTo(1f));
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void ResolvedPosePublisherNeverOwnsLocalTransform()
        {
            var source = System.IO.File.ReadAllText(
                "Assets/Scripts/Ontology/Unity/Networking/" +
                "OntologyWorldAuthorityPlayerMotionReconciler.cs");
            StringAssert.DoesNotContain(
                "CharacterController.Move",
                source);
            StringAssert.DoesNotContain(
                "transform.position =",
                source);
            StringAssert.DoesNotContain(
                "GetHorizontalCorrection",
                source);
        }

        [Test]
        public void AcceptedAnimationIntentWaitsForPresentationReadinessButReplayDoesNot()
        {
            Assert.That(
                OntologyAnimationAdapter.CanCommandRequestAuthorityPresentation(
                    new OntologyWorldCommand
                    {
                        commandType =
                            OntologyWorldCommandKinds.ExecuteAction
                    }),
                Is.True);
            Assert.That(
                OntologyAnimationAdapter.CanCommandRequestAuthorityPresentation(
                    new OntologyWorldCommand
                    {
                        commandType =
                            OntologyWorldCommandKinds.SaveAvatarCheckpoint
                    }),
                Is.False,
                "Non-action Authority commands must not enter the animation " +
                "readiness queue.");

            var accepted = new OntologyAuthorityCommandResult
            {
                accepted = true
            };
            Assert.That(
                OntologyAnimationAdapter.ShouldQueueAuthorityPresentation(
                    accepted,
                    2f),
                Is.True);
            Assert.That(
                OntologyAnimationAdapter.ShouldRetryAuthorityPresentation(
                    12f,
                    11f),
                Is.True);
            Assert.That(
                OntologyAnimationAdapter.ShouldRetryAuthorityPresentation(
                    12f,
                    13f),
                Is.False);

            accepted.isReplay = true;
            Assert.That(
                OntologyAnimationAdapter.ShouldQueueAuthorityPresentation(
                    accepted,
                    2f),
                Is.False,
                "Idempotent command replay must not replay a visual animation.");
        }

        [Test]
        public void RightHandWeaponUsesGenericDataDefinedAttachmentContract()
        {
            var profile = Load<OntologyAttachmentProfile>(
                "Assets/Data/Ontology/Profiles/RightHandCarryAttachmentProfile.asset");

            Assert.That(
                profile.relationPredicate,
                Is.EqualTo(OntologyPredicates.EquippedBy));
            Assert.That(
                profile.relationDirection,
                Is.EqualTo(OntologyAttachmentRelationDirection.ItemToActor));
            Assert.That(
                profile.localPosition,
                Is.EqualTo(UnityEngine.Vector3.zero),
                "The shared profile must not hide a mesh-specific grip correction.");
            Assert.That(
                profile.localEulerAngles,
                Is.EqualTo(UnityEngine.Vector3.zero),
                "Actor sockets and prefab grip points own orientation, not the shared profile.");
            Assert.That(
                profile.localScale,
                Is.EqualTo(UnityEngine.Vector3.one),
                "The shared profile must preserve authored weapon scale.");
            Assert.That(
                profile.placeInWorldOnRelationRemoval,
                Is.True,
                "A pushed Authority relation removal must release the weapon " +
                "at the actor instead of restoring its old durable pose.");
            Assert.That(
                typeof(OntologyCombatWeaponPresenter).GetMethod("PresentEquipped"),
                Is.Null,
                "Combat presentation must not retain a hidden direct-parenting fallback.");
        }

        [Test]
        public void HumanoidFootGroundingAdapterIsPresentationOnly()
        {
            var source = System.IO.File.ReadAllText(
                "Assets/Scripts/Ontology/Unity/" +
                "OntologyCharacterFootGroundingAdapter.cs");

            Assert.That(source, Does.Contain("OnAnimatorIK"));
            Assert.That(source, Does.Contain("Physics.RaycastNonAlloc"));
            Assert.That(source, Does.Not.Contain("transform.position ="));
            Assert.That(source, Does.Not.Contain("CharacterController.Move"));
            Assert.That(source, Does.Not.Contain("Rigidbody.MovePosition"));
        }

        [Test]
        public void ArbitraryCarryableUsesExplicitRootAnchoredProfile()
        {
            var profile = Load<OntologyAttachmentProfile>(
                "Assets/Data/Ontology/Profiles/" +
                "RightHandObjectCarryAttachmentProfile.asset");
            var database = Load<OntologyAttachmentProfileDatabase>(
                "Assets/Data/Ontology/Profiles/AttachmentProfileDatabase.asset");

            Assert.That(profile.kind, Is.EqualTo(OntologyAttachmentKind.Carryable));
            Assert.That(profile.slotId, Is.EqualTo("RightHand"));
            Assert.That(profile.actorSocketId, Is.EqualTo("RightHand"));
            Assert.That(profile.relationPredicate,
                Is.EqualTo(OntologyPredicates.EquippedBy));
            Assert.That(profile.requireItemGripPoint, Is.False,
                "Arbitrary objects require an explicit root-anchored profile; " +
                "the attachment adapter must not silently bypass a calibrated " +
                "weapon grip contract.");
            Assert.That(profile.placeInWorldOnRelationRemoval, Is.True);
            Assert.That(database.Find(profile.profileId), Is.SameAs(profile));
        }

        [Test]
        public void EquippedNonWeaponRemainsAvailableToUnequipInput()
        {
            var actorObject = new UnityEngine.GameObject("Actor");
            try
            {
                var actorId = System.Guid.NewGuid();
                var itemId = System.Guid.NewGuid();
                var identity = actorObject.AddComponent<
                    OntologyAuthorityEntityIdentity>();
                identity.SetGuid(actorId);
                var controller = actorObject.AddComponent<
                    OntologyCombatController>();
                controller.Configure(null, identity, null, null);
                var projection = new OntologyAuthorityWorldProjection
                {
                    facts = new[]
                    {
                        new OntologyAuthorityFactProjection
                        {
                            subjectEntityId = itemId.ToString("D"),
                            predicateId = OntologyPredicates.EquippedBy,
                            objectKind = "entity",
                            objectEntityId = actorId.ToString("D")
                        },
                        new OntologyAuthorityFactProjection
                        {
                            subjectEntityId = itemId.ToString("D"),
                            predicateId = OntologyPredicates.UnequipAction,
                            objectKind = "canonical",
                            objectCanonicalId = "unequip_equipment"
                        }
                    }
                };
                typeof(OntologyCombatController).GetMethod(
                        "ApplyEquipmentProjection",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    .Invoke(controller, new object[] { projection });
                var equipped = (System.Guid[])typeof(OntologyCombatController)
                    .GetField(
                        "projectedEquippedItemIds",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    .GetValue(controller);

                Assert.That(equipped, Is.EquivalentTo(new[] { itemId }),
                    "F unequip must follow equipped_by for every Item; Weapon " +
                    "is only a combat-presentation specialization.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actorObject);
            }
        }

        [Test]
        public void RightHandSocketUsesTPosePropAnchorWithoutCorrection()
        {
            var socketObject = new UnityEngine.GameObject("SocketDefaults");
            try
            {
                var defaultSocket =
                    socketObject.AddComponent<OntologyAttachmentSocket>();
                Assert.That(defaultSocket.LocalPosition, Is.EqualTo(UnityEngine.Vector3.zero));
                Assert.That(
                    defaultSocket.LocalEulerAngles,
                    Is.EqualTo(UnityEngine.Vector3.zero),
                    "A newly-authored socket must not introduce a hidden weapon rotation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(socketObject);
            }

            const string scenePath = "Assets/Scenes/TormiaWorld.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var socket = scene.GetRootGameObjects()
                    .SelectMany(value =>
                        value.GetComponentsInChildren<
                            OntologyAttachmentSocket>(true))
                    .Single(value => value.SocketId == "RightHand");
                Assert.That(socket.LocalPosition, Is.EqualTo(UnityEngine.Vector3.zero));
                Assert.That(socket.SourceBone, Is.EqualTo(UnityEngine.HumanBodyBones.RightHand));
                Assert.That(socket.SourceChildPath, Is.EqualTo("RightHandProp"));
                Assert.That(
                    socket.LocalEulerAngles,
                    Is.EqualTo(new UnityEngine.Vector3(0f, 0f, 90f)),
                    "Sword length must follow the T-pose palm-width axis, not the forearm axis.");
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void AttachmentPreviewSceneIsEditorOnlyAndRuntimeNeutral()
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    OntologyAttachmentProfilePreviewRig.PreviewScenePath),
                Is.Not.Null);
            Assert.That(
                EditorBuildSettings.scenes.Any(value =>
                    value.path ==
                    OntologyAttachmentProfilePreviewRig.PreviewScenePath),
                Is.False,
                "The attachment authoring preview must not enter the player build.");

            var previewScene = SceneManager.GetSceneByPath(
                OntologyAttachmentProfilePreviewRig.PreviewScenePath);
            var openedForTest = !previewScene.IsValid() || !previewScene.isLoaded;
            if (openedForTest)
            {
                previewScene = EditorSceneManager.OpenScene(
                    OntologyAttachmentProfilePreviewRig.PreviewScenePath,
                    OpenSceneMode.Additive);
            }
            try
            {
                var rig = previewScene.GetRootGameObjects()
                    .Select(value =>
                        value.GetComponentInChildren<
                            OntologyAttachmentProfilePreviewRig>(true))
                    .FirstOrDefault(value => value != null);
                Assert.That(rig, Is.Not.Null);
                Assert.That(
                    rig.GetComponentsInChildren<OntologyObject>(true),
                    Is.Empty,
                    "Preview visuals must never join the runtime ontology scan.");
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(previewScene, true);
            }
        }

        [Test]
        public void EnabledActionSelectsNewestVersionWithinOnePackage()
        {
            var projection = new OntologyAuthorityWorldProjection
            {
                actions = new[]
                {
                    new OntologyAuthorityActionDefinitionProjection
                    {
                        packageId = "combat",
                        actionId = "attack",
                        definitionVersion = 10
                    },
                    new OntologyAuthorityActionDefinitionProjection
                    {
                        packageId = "combat",
                        actionId = "attack",
                        definitionVersion = 11
                    }
                }
            };

            Assert.That(
                OntologyWorldAuthorityClient.TryResolveEnabledAction(
                    projection,
                    "attack",
                    out var resolved),
                Is.True);
            Assert.That(resolved.definitionVersion, Is.EqualTo(11));
        }

        [Test]
        public void EnabledActionRejectsSameIdFromDifferentPackages()
        {
            var projection = new OntologyAuthorityWorldProjection
            {
                actions = new[]
                {
                    new OntologyAuthorityActionDefinitionProjection
                    {
                        packageId = "combat-a",
                        actionId = "attack",
                        definitionVersion = 11
                    },
                    new OntologyAuthorityActionDefinitionProjection
                    {
                        packageId = "combat-b",
                        actionId = "attack",
                        definitionVersion = 12
                    }
                }
            };

            Assert.That(
                OntologyWorldAuthorityClient.TryResolveEnabledAction(
                    projection,
                    "attack",
                    out _),
                Is.False);
        }

        [Test]
        public void PublishedDevelopmentActionsContainEquipAndGuardedDeath()
        {
            var settings = Load<OntologyWorldAuthoritySettings>(
                "Assets/Data/Ontology/Networking/WorldAuthoritySettings.asset");
            var manifest = Load<OntologyWeaponContentManifest>(
                "Assets/Data/Ontology/Combat/WeaponContentManifest.asset");
            var catalog = Load<OntologyCombatCatalog>(
                "Assets/Data/Ontology/Combat/CombatCatalog.asset");
            var attack = settings.developmentActions.Single(value =>
                value.actionId == "attack");
            var swing = settings.developmentActions.Single(value =>
                value.actionId == "swing_weapon");
            var equip = settings.developmentActions.Single(value =>
                value.actionId == "equip_weapon");
            var equipWearable = settings.developmentActions.Single(value =>
                value.actionId == "equip_wearable");

            var unequip = settings.developmentActions.Single(value =>
                value.actionId == "unequip_equipment");
            var ruleDatabase = Load<OntologyRuleDatabase>(
                "Assets/Data/Ontology/RuleDatabase.asset");
            var attackRule = ruleDatabase.Definitions.Single(value =>
                value.id == "MeleeAttackOnPrimaryIntent");
            var swingRule = ruleDatabase.Definitions.Single(value =>
                value.id == "SwingWeaponOnPrimaryIntent");

            Assert.That(settings.developmentPackageVersion, Is.EqualTo("4.0.0"));
            Assert.That(settings.developmentActions.All(value =>
                !string.IsNullOrWhiteSpace(value.actionId) &&
                value.definitionVersion >= 1), Is.True);
            Assert.That(attack.definitionVersion, Is.EqualTo(12));
            Assert.That(swing.definitionVersion, Is.EqualTo(2));
            Assert.That(
                manifest.Weapons.All(value =>
                    value != null &&
                    value.attackActionId == attack.actionId &&
                    value.actionDefinitionVersion ==
                    attack.definitionVersion),
                Is.True);
            Assert.That(
                catalog.Weapons.All(value =>
                    value != null &&
                    value.attackActionId == attack.actionId &&
                    value.actionDefinitionVersion ==
                    attack.definitionVersion),
                Is.True);
            Assert.That(equip.definitionVersion, Is.EqualTo(9));
            Assert.That(equipWearable.definitionVersion, Is.EqualTo(2));
            Assert.That(unequip.definitionVersion, Is.EqualTo(3));
            Assert.That(
                settings.developmentRules,
                Has.Some.Matches<OntologyAuthorityDevelopmentRule>(value =>
                    value != null &&
                    value.ruleId == "MeleeAttackOnPrimaryIntent" &&
                    value.definitionVersion == attackRule.catalogVersion));
            Assert.That(
                settings.developmentRules,
                Has.Some.Matches<OntologyAuthorityDevelopmentRule>(value =>
                    value != null &&
                    value.ruleId == "SwingWeaponOnPrimaryIntent" &&
                    value.definitionVersion == swingRule.catalogVersion));
            Assert.That(
                attackRule.conditions,
                Has.Some.Matches<OntologyCondition>(value =>
                    value != null &&
                    value.predicate == "primary_attack_intent"));
            Assert.That(
                attackRule.effects,
                Has.Some.Matches<OntologyEffect>(value =>
                    value != null &&
                    value.predicate == "current_health" &&
                    value.valueFrom != null &&
                    value.valueFrom.predicate == "attack_damage"));
            Assert.That(
                attack.structuredDefinitionJson,
                Does.Not.Contain("\"actorAnimationIntent\":\"AttackLight\""),
                "Damage transport must not duplicate swing presentation.");
            Assert.That(
                attack.structuredDefinitionJson,
                Does.Contain("\"ruleId\":\"MeleeAttackOnPrimaryIntent\""));
            Assert.That(
                attack.structuredDefinitionJson,
                Does.Contain("\"bindingEntityPattern\":\"?tool\""));
            Assert.That(
                attack.structuredDefinitionJson,
                Does.Contain("\"intentPredicate\":\"primary_attack_intent\""));
            Assert.That(
                attack.structuredDefinitionJson,
                Does.Contain("\"predicate\":\"attack_range\""));
            Assert.That(
                attack.structuredDefinitionJson,
                Does.Contain("\"predicate\":\"attack_cooldown\""));
            Assert.That(
                attack.structuredDefinitionJson,
                Does.Not.Contain("\"attack_damage\""));
            Assert.That(
                attack.structuredDefinitionJson,
                Does.Not.Contain("\"predicate\":\"loot_item\""));
            Assert.That(
                attack.structuredDefinitionJson,
                Does.Contain(
                    "\"ruleId\":\"LootBecomesAvailableOnDefeat\""));
            Assert.That(
                swing.structuredDefinitionJson,
                Does.Contain("\"ruleId\":\"SwingWeaponOnPrimaryIntent\""));
            Assert.That(
                swing.structuredDefinitionJson,
                Does.Contain("\"intentPredicate\":\"primary_swing_intent\""));
            Assert.That(
                swing.structuredDefinitionJson,
                Does.Contain("\"predicate\":\"attack_cooldown\""));
            Assert.That(
                swing.structuredDefinitionJson,
                Does.Not.Contain("\"actorAnimationIntent\":\"AttackLight\""),
                "The swing Rule Block, not its transport, owns presentation.");
            Assert.That(swingRule.effects, Is.Empty);
            Assert.That(
                swingRule.runtimePresentation.actorAnimationIntent,
                Is.EqualTo("AttackLight"));
            Assert.That(
                swingRule.runtimePresentation.playbackSpeedFrom.subject,
                Is.EqualTo("?tool"));
            Assert.That(
                swingRule.runtimePresentation.playbackSpeedFrom.predicate,
                Is.EqualTo("attack_playback_speed"));
            Assert.That(swingRule.catalogVersion, Is.EqualTo(2));
            Assert.That(
                OntologyRuleValidator.Validate(new[] { swingRule }),
                Is.Empty);
            Assert.That(equip.structuredDefinitionJson, Does.Not.Contain("\"equipped_item\""));
            Assert.That(
                equip.structuredDefinitionJson,
                Does.Not.Contain("\"predicate\":\"equipped_by\""),
                "The F transport must not duplicate the Rule Block's result.");
            Assert.That(
                equip.structuredDefinitionJson,
                Does.Not.Contain("\"has_slot\""),
                "Equipment invariants belong to the invoked Rule Block.");
            Assert.That(
                equip.structuredDefinitionJson,
                Does.Contain("\"ruleInvocation\""));
            Assert.That(
                equip.structuredDefinitionJson,
                Does.Contain("\"ruleId\":\"EquipItemOnInteractionIntent\""));
            Assert.That(
                equip.structuredDefinitionJson,
                Does.Contain("\"intentPredicate\":\"interaction_intent\""));
            Assert.That(
                equipWearable.structuredDefinitionJson,
                Does.Contain("\"AutoEquipNearbyWearable\""));
            Assert.That(
                equip.structuredDefinitionJson,
                Does.Contain("\"predicate\":\"interaction_range\""));
            Assert.That(
                equip.structuredDefinitionJson,
                Does.Not.Contain("\"predicate\":\"equips\""));
            Assert.That(
                equip.structuredDefinitionJson,
                Does.Contain(
                    "\"presentation\":{\"actorAnimationIntent\":\"WeaponEquip\""));
            Assert.That(
                unequip.structuredDefinitionJson,
                Does.Not.Contain(
                    "\"actorAnimationIntent\":\"WeaponUnequip\""),
                "The Rule Block, not the transport, owns unequip presentation.");
        }

        [Test]
        public void CombatFDoesNotRouteWearablesAwayFromProximityRule()
        {
            var controllerObject =
                new UnityEngine.GameObject("CombatController");
            var wearableObject =
                new UnityEngine.GameObject("Wearable");
            var weaponObject =
                new UnityEngine.GameObject("Weapon");
            try
            {
                var controller =
                    controllerObject.AddComponent<OntologyCombatController>();
                var wearableIdentity = CreateEquipmentIdentity(
                    wearableObject,
                    OntologyObjects.SelectThenEquip);
                var weaponIdentity = CreateEquipmentIdentity(
                    weaponObject,
                    OntologyObjects.SelectThenCarry);
                var method = typeof(OntologyCombatController).GetMethod(
                    "TryResolveEquipActionId",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);

                var wearableArguments =
                    new object[] { wearableIdentity, null };
                Assert.That(
                    method.Invoke(controller, wearableArguments),
                    Is.False,
                    "Wearables must continue through interaction_intent and " +
                    "AutoEquipNearbyWearable instead of the combat F input.");

                var weaponArguments =
                    new object[] { weaponIdentity, null };
                Assert.That(
                    method.Invoke(controller, weaponArguments),
                    Is.True);
                Assert.That(
                    weaponArguments[1],
                    Is.EqualTo("equip_weapon"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(controllerObject);
                UnityEngine.Object.DestroyImmediate(wearableObject);
                UnityEngine.Object.DestroyImmediate(weaponObject);
            }
        }

        [Test]
        public void WeaponCatalogIsBackedByEditableContentManifest()
        {
            var manifest = Load<OntologyWeaponContentManifest>(
                "Assets/Data/Ontology/Combat/WeaponContentManifest.asset");
            var catalog = Load<OntologyCombatCatalog>(
                "Assets/Data/Ontology/Combat/CombatCatalog.asset");

            Assert.That(manifest.Weapons, Is.Not.Empty);
            Assert.That(
                manifest.Weapons.All(value =>
                    value != null && value.IsValid),
                Is.True);
            Assert.That(
                catalog.Weapons.Select(value => value.weaponId),
                Is.EquivalentTo(
                    manifest.Weapons.Select(value => value.weaponId)));
        }

        [Test]
        public void AuthorityEquippedEntitySelectsProjectedAnimationIntent()
        {
            var actorId = System.Guid.NewGuid().ToString("D");
            var itemId = System.Guid.NewGuid().ToString("D");
            var catalog = UnityEngine.ScriptableObject.CreateInstance<
                OntologyCombatCatalog>();
            try
            {
                catalog.ReplaceDefinitions(
                    new[]
                    {
                        new OntologyWeaponPresentationDefinition
                        {
                            weaponId = "TestWeaponTemplate",
                            idleAnimationIntent = "TestWeaponIdle",
                            moveAnimationIntent = "TestWeaponWalk"
                        }
                    },
                    null,
                    null);
                var projection = new OntologyAuthorityWorldProjection
                {
                    entities = new[]
                    {
                        new OntologyAuthorityEntityProjection
                        {
                            entityId = itemId,
                            templateId = "TestWeaponTemplate"
                        }
                    },
                    facts = new[]
                    {
                        new OntologyAuthorityFactProjection
                        {
                            subjectEntityId = itemId,
                            predicateId = OntologyPredicates.EquippedBy,
                            objectEntityId = actorId
                        },
                        new OntologyAuthorityFactProjection
                        {
                            subjectEntityId = itemId,
                            predicateId = OntologyPredicates.IdleAnimationIntent,
                            objectCanonicalId = "TestWeaponIdle"
                        },
                        new OntologyAuthorityFactProjection
                        {
                            subjectEntityId = itemId,
                            predicateId = OntologyPredicates.MoveAnimationIntent,
                            objectCanonicalId = "TestWeaponWalk"
                        }
                    }
                };

                Assert.That(
                    OntologyEquipmentAnimationIntentResolver.TryResolveIdleIntent(
                        projection,
                        actorId,
                        catalog,
                        out var intent),
                    Is.True);
                Assert.That(intent, Is.EqualTo("TestWeaponIdle"));
                Assert.That(
                    OntologyEquipmentAnimationIntentResolver.TryResolveMoveIntent(
                        projection,
                        actorId,
                        catalog,
                        out var moveIntent),
                    Is.True);
                Assert.That(moveIntent, Is.EqualTo("TestWeaponWalk"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void RemovedProjectedAnimationMeaningRemovesWeaponIdleBehavior()
        {
            var actorId = System.Guid.NewGuid().ToString("D");
            var itemId = System.Guid.NewGuid().ToString("D");
            var projection = new OntologyAuthorityWorldProjection
            {
                entities = new[]
                {
                    new OntologyAuthorityEntityProjection
                    {
                        entityId = itemId,
                        templateId = "PresentationFreeTemplate"
                    }
                },
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = itemId,
                        predicateId = OntologyPredicates.EquippedBy,
                        objectEntityId = actorId
                    }
                }
            };
            var catalog = UnityEngine.ScriptableObject.CreateInstance<
                OntologyCombatCatalog>();
            try
            {
                catalog.ReplaceDefinitions(
                    new[]
                    {
                        new OntologyWeaponPresentationDefinition
                        {
                            weaponId = "PresentationFreeTemplate",
                            idleAnimationIntent = string.Empty,
                            moveAnimationIntent = string.Empty
                        }
                    },
                    null,
                    null);

                Assert.That(
                    OntologyEquipmentAnimationIntentResolver.TryResolveIdleIntent(
                        projection,
                        actorId,
                        catalog,
                        out _),
                    Is.False,
                    "Removing the catalog intent must reveal no hidden WeaponIdle fallback.");
                Assert.That(
                    OntologyEquipmentAnimationIntentResolver.TryResolveMoveIntent(
                        projection,
                        actorId,
                        catalog,
                        out _),
                    Is.False,
                    "Removing the catalog intent must reveal no hidden WeaponWalk fallback.");

                projection.facts = System.Array.Empty<
                    OntologyAuthorityFactProjection>();
                Assert.That(
                    OntologyEquipmentAnimationIntentResolver.TryResolveIdleIntent(
                        projection,
                        actorId,
                        catalog,
                        out _),
                    Is.False,
                    "Removing the equipped_by relation must restore controller Idle.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void SwordIdleAnimationIsConfiguredAsPersistentLoopablePose()
        {
            var database = Load<OntologyAnimationDatabase>(
                "Assets/Data/Ontology/AnimationDatabase.asset");
            var definition = database.Definitions.Single(value =>
                value.animationId == "Anim_Sword_Idle");

            Assert.That(definition.intents, Does.Contain("WeaponIdle"));
            Assert.That(definition.canBlend, Is.True);
        }

        [Test]
        public void SwordWalkAnimationIsCatalogSelectedAndLoopable()
        {
            var catalog = Load<OntologyCombatCatalog>(
                "Assets/Data/Ontology/Combat/CombatCatalog.asset");
            Assert.That(catalog.Weapons.All(value =>
                value.moveAnimationIntent == "WeaponWalk"), Is.True);

            var database = Load<OntologyAnimationDatabase>(
                "Assets/Data/Ontology/AnimationDatabase.asset");
            var definition = database.Definitions.Single(value =>
                value.animationId == "Anim_Sword_Walk");

            Assert.That(definition.clip, Is.Not.Null);
            Assert.That(definition.clip.isLooping, Is.True);
            Assert.That(definition.intents, Does.Contain("WeaponWalk"));
            Assert.That(definition.canBlend, Is.True);

            var profile = Load<OntologyActorProfile>(
                "Assets/Data/Ontology/Actors/PlayerProfile.asset");
            Assert.That(profile.animationIds, Does.Contain("Anim_Sword_Walk"));
        }

        [Test]
        public void EquipmentTransitionMetadataAllowsImmediateLocomotion()
        {
            var manifest = Load<OntologyAnimationContentManifest>(
                "Assets/Data/Ontology/AnimationContentManifest.asset");
            var database = Load<OntologyAnimationDatabase>(
                "Assets/Data/Ontology/AnimationDatabase.asset");

            foreach (var animationId in new[]
                     {
                         "Anim_Sword_Equip",
                         "Anim_Sword_Unequip"
                     })
            {
                Assert.That(
                    manifest.Entries.Single(value =>
                        value.animationId == animationId).interruptible,
                    Is.True,
                    animationId + " must yield to authored locomotion intent.");
                Assert.That(
                    database.Definitions.Single(value =>
                        value.animationId == animationId).interruptible,
                    Is.True,
                    animationId + " runtime definition must match the manifest.");
            }

            Assert.That(
                OntologyAnimationAdapter
                    .ShouldYieldTransientPresentationToLocomotion(
                        true,
                        true,
                        true),
                Is.True);
            Assert.That(
                OntologyAnimationAdapter
                    .ShouldYieldTransientPresentationToLocomotion(
                        true,
                        false,
                        true),
                Is.False,
                "Non-interruptible attacks and reactions must retain their " +
                "authored presentation lock.");
        }

        [Test]
        public void DefeatedStateKeepsItsPersistentDeathPresentation()
        {
            Assert.That(
                OntologyAnimationAdapter.ShouldReleaseTransientPresentation(
                    true,
                    true,
                    true,
                    true,
                    10f,
                    1f),
                Is.False,
                "A projected defeated state must not return to idle after " +
                "the death clip duration or because movement was observed.");
            Assert.That(
                OntologyAnimationAdapter.ShouldReleaseTransientPresentation(
                    true,
                    false,
                    false,
                    false,
                    10f,
                    1f),
                Is.True,
                "An ordinary completed transient still returns to its base intent.");
        }

        [Test]
        public void SwordLightAttackYieldsPresentationToApprovedLocomotion()
        {
            var manifest = Load<OntologyAnimationContentManifest>(
                "Assets/Data/Ontology/AnimationContentManifest.asset");
            var database = Load<OntologyAnimationDatabase>(
                "Assets/Data/Ontology/AnimationDatabase.asset");

            Assert.That(
                manifest.Entries.Single(value =>
                    value.animationId ==
                    "Anim_Sword_LightAttack").interruptible,
                Is.True,
                "The source manifest explicitly owns the current attack " +
                "presentation interruption policy.");
            Assert.That(
                database.Definitions.Single(value =>
                    value.animationId ==
                    "Anim_Sword_LightAttack").interruptible,
                Is.True,
                "The runtime projection must match the source manifest.");
            Assert.That(
                OntologyAnimationAdapter
                    .ShouldYieldTransientPresentationToLocomotion(
                        true,
                        true,
                        true),
                Is.True);
            Assert.That(
                OntologyAnimationAdapter
                    .ShouldYieldTransientPresentationToLocomotion(
                        true,
                        true,
                        false),
                Is.False,
                "The attack still plays when no locomotion intent exists.");
        }

        [Test]
        public void WorldPlayerAnimationAdapterReferencesEquipmentPresentationCatalog()
        {
            const string scenePath = "Assets/Scenes/TormiaWorld.unity";
            const string catalogPath =
                "Assets/Data/Ontology/Combat/CombatCatalog.asset";
            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var playerInput = scene.GetRootGameObjects()
                    .SelectMany(value =>
                        value.GetComponentsInChildren<
                            OntologyInputSystemPlayerInput>(true))
                    .FirstOrDefault(value =>
                        value.transform.root.gameObject.scene == scene);
                var adapter =
                    playerInput != null
                        ? playerInput.GetComponent<OntologyAnimationAdapter>()
                        : null;
                Assert.That(
                    adapter,
                    Is.Not.Null,
                    "The world player must have an ontology animation adapter.");

                var serializedAdapter = new SerializedObject(adapter);
                var catalogProperty = serializedAdapter.FindProperty(
                    "equipmentPresentationCatalog");
                Assert.That(catalogProperty, Is.Not.Null);
                Assert.That(
                    catalogProperty.objectReferenceValue,
                    Is.EqualTo(Load<OntologyCombatCatalog>(catalogPath)),
                    "Without the scene-owned catalog reference an equipped_by " +
                    "Fact cannot select its data-owned idle intent.");
            }
            finally
            {
                if (openedForTest)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void PlayerRepertoireContainsAuthoritySelectedSwordAttack()
        {
            var profile = Load<OntologyActorProfile>(
                "Assets/Data/Ontology/Actors/PlayerProfile.asset");

            Assert.That(profile.animationIds, Does.Contain("Anim_Sword_LightAttack"));
            Assert.That(
                typeof(OntologyCombatController).GetField(
                    "attackAnimationIntent",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic),
                Is.Null,
                "The input controller must not choose an animation intent.");
            Assert.That(
                typeof(OntologyWeaponPresentationDefinition).GetField(
                    "attackAnimationIntent"),
                Is.Null,
                "The weapon visual catalog must not duplicate the action-owned intent.");
            Assert.That(
                typeof(OntologyCombatTargetPresenter).GetField(
                    "fallbackHitState",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic),
                Is.Null,
                "Removing ontology animation data must not reveal a hidden Animator fallback.");
        }

        [Test]
        public void AcceptedVersionedActionResolvesItsDataOwnedActorIntent()
        {
            var actorId = System.Guid.NewGuid();
            var command = OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.ExecuteAction,
                OntologyWorldAuthorityClient.CreateExecuteActionPayload(
                    actorId,
                    System.Guid.NewGuid(),
                    System.Guid.NewGuid(),
                    "social_village",
                    "1.2.0",
                    "attack",
                    5));
            var result = new OntologyAuthorityCommandResult
            {
                accepted = true
            };
            var projection = new OntologyAuthorityWorldProjection
            {
                actions = new[]
                {
                    new OntologyAuthorityActionDefinitionProjection
                    {
                        packageId = "social_village",
                        packageVersion = "1.2.0",
                        actionId = "attack",
                        definitionVersion = 5,
                        actorAnimationIntent = "AttackLight"
                    }
                }
            };

            Assert.That(
                OntologyAuthorityActionAnimationIntentResolver.TryResolveActorIntent(
                    command,
                    result,
                    projection,
                    actorId.ToString("D"),
                    out var intent),
                Is.True);
            Assert.That(intent, Is.EqualTo("AttackLight"));
        }

        [Test]
        public void AuthorityAnimationQueueScopesExecuteActionToOwningActor()
        {
            var actorId = System.Guid.NewGuid();
            var otherActorId = System.Guid.NewGuid();
            var command = OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.ExecuteAction,
                OntologyWorldAuthorityClient.CreateExecuteActionPayload(
                    actorId,
                    System.Guid.NewGuid(),
                    null,
                    "combat_core",
                    "1.0.0",
                    "primary_attack",
                    1));

            Assert.That(
                OntologyAuthorityActionAnimationIntentResolver
                    .IsExecuteActionForActor(
                        command,
                        actorId.ToString("D")),
                Is.True);
            Assert.That(
                OntologyAuthorityActionAnimationIntentResolver
                    .IsExecuteActionForActor(
                        command,
                        otherActorId.ToString("D")),
                Is.False,
                "An accepted action must not enter another actor's animation " +
                "readiness queue.");
            Assert.That(
                OntologyAuthorityActionAnimationIntentResolver
                    .IsExecuteActionForActor(
                        new OntologyWorldCommand
                        {
                            commandType =
                                OntologyWorldCommandKinds.ExecuteAction,
                            payloadJson = "{}"
                        },
                        actorId.ToString("D")),
                Is.False,
                "Malformed or actor-free intents must fail closed.");
        }

        [Test]
        public void RejectedOrPresentationFreeActionDoesNotInventAnimation()
        {
            var actorId = System.Guid.NewGuid();
            var command = OntologyWorldAuthorityClient.CreateCommand(
                OntologyWorldCommandKinds.ExecuteAction,
                OntologyWorldAuthorityClient.CreateExecuteActionPayload(
                    actorId,
                    System.Guid.NewGuid(),
                    null,
                    "social_village",
                    "1.2.0",
                    "talk",
                    2));
            var projection = new OntologyAuthorityWorldProjection
            {
                actions = new[]
                {
                    new OntologyAuthorityActionDefinitionProjection
                    {
                        packageId = "social_village",
                        packageVersion = "1.2.0",
                        actionId = "talk",
                        definitionVersion = 2,
                        actorAnimationIntent = string.Empty
                    }
                }
            };

            Assert.That(
                OntologyAuthorityActionAnimationIntentResolver.TryResolveActorIntent(
                    command,
                    new OntologyAuthorityCommandResult { accepted = true },
                    projection,
                    actorId.ToString("D"),
                    out _),
                Is.False,
                "Removing presentation metadata must remove the animation.");
            Assert.That(
                OntologyAuthorityActionAnimationIntentResolver.TryResolveActorIntent(
                    command,
                    new OntologyAuthorityCommandResult { accepted = false },
                    projection,
                    actorId.ToString("D"),
                    out _),
                Is.False,
                "A rejected action must never produce action presentation.");
        }

        [Test]
        public void PlayerCombatClipsHaveHumanoidSourceMappings()
        {
            var paths = new[]
            {
                "Assets/Animations/Mixamo/Combat/Sword/Player/SwordEquip.fbx",
                "Assets/Animations/Mixamo/Combat/Sword/Player/SwordIdle.fbx",
                "Assets/Animations/Mixamo/Combat/Sword/Player/WeaponeWalk.fbx",
                "Assets/Animations/Mixamo/Combat/Sword/Player/SwordLightAttack.fbx",
                "Assets/Animations/Mixamo/Combat/Sword/Player/SwordHitRecovery.fbx",
                "Assets/Animations/Mixamo/Combat/Sword/Player/SwordUnequip.fbx"
            };

            foreach (var path in paths)
            {
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(
                    importer.animationType,
                    Is.EqualTo(ModelImporterAnimationType.Human),
                    path);
                Assert.That(
                    importer.avatarSetup,
                    Is.EqualTo(ModelImporterAvatarSetup.CreateFromThisModel),
                    path);
                Assert.That(importer.importAnimation, Is.True, path);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(path)
                    .OfType<UnityEngine.Avatar>()
                    .FirstOrDefault();
                Assert.That(avatar, Is.Not.Null, path);
                Assert.That(avatar.isValid, Is.True, path);
                Assert.That(avatar.isHuman, Is.True, path);
            }
        }

        [Test]
        public void ImportedCombatMaterialsBridgeFromStandardToUrpLit()
        {
            var targetShader = UnityEngine.Shader.Find(
                "Universal Render Pipeline/Lit");
            Assert.That(targetShader, Is.Not.Null);

            var paths = new[]
            {
                "Assets/Blink/Art/Weapons/LowPoly/FreeSwords/Sword1/Sword1_Material_Bronze.mat",
                "Assets/Blink/Art/Weapons/LowPoly/FreeSwords/Sword8/Sword8_Material_Corrupted.mat",
                "Assets/Blink/Art/Weapons/LowPoly/FreeSwords/Sword15/Sword15_Material_Frost.mat",
                "Assets/RPGMonsterPartnersPBRPolyart/Materials/PolyartDefault.mat"
            };

            foreach (var path in paths)
            {
                var source = Load<UnityEngine.Material>(path);
                var expectedTexture = source.GetTexture("_MainTex");
                var converted =
                    OntologyRenderPipelineMaterialAdapter.CreateCompatibleMaterial(
                        source,
                        targetShader);
                try
                {
                    Assert.That(converted, Is.Not.Null, path);
                    Assert.That(converted.shader, Is.EqualTo(targetShader), path);
                    Assert.That(
                        converted.GetTexture("_BaseMap"),
                        Is.EqualTo(expectedTexture),
                        path);
                    if (path.EndsWith(
                            "PolyartDefault.mat",
                            System.StringComparison.Ordinal))
                    {
                        Assert.That(
                            converted.IsKeywordEnabled("_EMISSION"),
                            Is.False,
                            "Disabled source emission must not wash the monster albedo white.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(converted);
                }
            }
        }

        [Test]
        public void TargetHitPresentationRequiresNewAuthorityHealthDecrease()
        {
            Assert.That(
                OntologyCombatTargetPresenter.ShouldPresentConfirmedDamage(
                    true, 100, 10, 85, 11, false),
                Is.True);
            Assert.That(
                OntologyCombatTargetPresenter.ShouldPresentConfirmedDamage(
                    true, 100, 10, 85, 10, false),
                Is.False,
                "Replaying the same projection must not replay the reaction.");
            Assert.That(
                OntologyCombatTargetPresenter.ShouldPresentConfirmedDamage(
                    false, 0, -1, 85, 11, false),
                Is.False,
                "The initial projection is baseline state, not a hit event.");
            Assert.That(
                OntologyCombatTargetPresenter.ShouldPresentConfirmedDamage(
                    true, 10, 10, 0, 11, true),
                Is.False,
                "Defeat owns the death intent instead of the hit reaction.");
        }

        [Test]
        public void TargetHitOccurrenceRequiresCommittedDamageForThisTarget()
        {
            var occurrence = new OntologyAuthorityRevisionOccurrence
            {
                revision = 42,
                eventId = "event-42",
                targetEntityId = "target-a",
                damageResult = true
            };

            Assert.That(
                OntologyCombatTargetPresenter
                    .ShouldPresentAuthorityDamageOccurrence(
                        occurrence,
                        "target-a",
                        string.Empty),
                Is.True);
            Assert.That(
                OntologyCombatTargetPresenter
                    .ShouldPresentAuthorityDamageOccurrence(
                        occurrence,
                        "target-b",
                        string.Empty),
                Is.False);
            Assert.That(
                OntologyCombatTargetPresenter
                    .ShouldPresentAuthorityDamageOccurrence(
                        occurrence,
                        "target-a",
                        "event-42"),
                Is.False,
                "One Authority occurrence must never replay twice.");
            occurrence.damageResult = false;
            Assert.That(
                OntologyCombatTargetPresenter
                    .ShouldPresentAuthorityDamageOccurrence(
                        occurrence,
                        "target-a",
                        string.Empty),
                Is.False,
                "A targeted non-damage action must not look like a hit.");
        }

        [Test]
        public void CombatTargetEligibilityRequiresProjectedLivingState()
        {
            var entityId = System.Guid.NewGuid();
            var projection = new OntologyAuthorityWorldProjection
            {
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = entityId.ToString("D"),
                        predicateId = OntologyPredicates.IsAlive,
                        objectValueJson = "true"
                    },
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = entityId.ToString("D"),
                        predicateId = OntologyPredicates.CurrentHealth,
                        objectValueJson = "10"
                    }
                }
            };

            Assert.That(
                OntologyCombatTargetPresenter.IsProjectedAlive(
                    projection,
                    entityId),
                Is.True);

            projection.facts[1].objectValueJson = "0";
            Assert.That(
                OntologyCombatTargetPresenter.IsProjectedAlive(
                    projection,
                    entityId),
                Is.False,
                "A defeated presentation must not intercept combat input.");
        }

        [Test]
        public void EquipmentInteractionRequiresAuthorityAndPresentationRange()
        {
            var actor = Vector3.zero;
            var authorityTarget = new Vector3(1f, 0f, 0f);

            Assert.That(
                OntologyCombatController.IsWithinInteractionRange(
                    actor,
                    authorityTarget,
                    actor,
                    new Vector3(1f, 0f, 0f),
                    3f),
                Is.True);
            Assert.That(
                OntologyCombatController.IsWithinInteractionRange(
                    actor,
                    authorityTarget,
                    actor,
                    new Vector3(1f, -100f, 0f),
                    3f),
                Is.False,
                "A stale durable position must not equip a Dynamic body " +
                "whose collision presentation moved out of range.");
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, "Missing combat asset: " + path);
            return asset;
        }

        private static OntologyAuthorityEntityIdentity
            CreateEquipmentIdentity(
                UnityEngine.GameObject target,
                string pickupBehavior)
        {
            var ontology = target.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                target.name,
                System.Array.Empty<string>(),
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PickupBehavior,
                        obj = pickupBehavior
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.EquipAction,
                        obj = string.Equals(
                            pickupBehavior,
                            OntologyObjects.SelectThenCarry,
                            System.StringComparison.Ordinal)
                            ? OntologyActions.EquipWeapon
                            : string.Empty
                    }
                });
            var identity =
                target.AddComponent<OntologyAuthorityEntityIdentity>();
            identity.SetGuid(System.Guid.NewGuid());
            return identity;
        }
    }
}
