using System;
using System.Linq;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyGameSessionCoordinatorTests
    {
        [Test]
        public void RuntimeBecomesAvailableOnlyAfterWorldEntryCompletes()
        {
            var value = new GameObject("SessionCoordinatorTest");
            try
            {
                var client = value.AddComponent<OntologyWorldAuthorityClient>();
                var coordinator = value.AddComponent<OntologyGameSessionCoordinator>();
                coordinator.Bind(client);

                SetField(client, "currentUserId", Guid.NewGuid().ToString());
                coordinator.AuthenticationCompleted(true);
                Assert.That(coordinator.State, Is.EqualTo(OntologyGameSessionState.Authenticated));
                Assert.That(coordinator.IsInWorld, Is.False);

                SetField(client, "currentCharacterId", Guid.NewGuid().ToString());
                coordinator.CharacterSelected();
                SetField(client, "currentWorldId", Guid.NewGuid().ToString());
                coordinator.WorldSelected();
                Assert.That(coordinator.State, Is.EqualTo(OntologyGameSessionState.WorldSelected));
                Assert.That(coordinator.IsInWorld, Is.False);

                coordinator.BeginWorldEntry();
                Assert.That(coordinator.State, Is.EqualTo(OntologyGameSessionState.EnteringWorld));
                Assert.That(coordinator.IsInWorld, Is.False);

                SetField(client, "hasEnteredCurrentWorld", true);
                coordinator.WorldEntryCompleted(true);
                Assert.That(coordinator.State, Is.EqualTo(OntologyGameSessionState.InWorld));
                Assert.That(coordinator.IsInWorld, Is.True);

                coordinator.BeginLeavingWorld();
                Assert.That(coordinator.IsInWorld, Is.False);
                coordinator.SignedOut();
                Assert.That(coordinator.State, Is.EqualTo(OntologyGameSessionState.SignedOut));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        [Test]
        public void FailedEntryReturnsToWorldSelection()
        {
            var value = new GameObject("SessionEntryFailureTest");
            try
            {
                var coordinator = value.AddComponent<OntologyGameSessionCoordinator>();
                coordinator.BeginWorldEntry();
                coordinator.WorldEntryCompleted(false);
                Assert.That(coordinator.State, Is.EqualTo(OntologyGameSessionState.WorldSelected));
                Assert.That(coordinator.IsInWorld, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        [Test]
        public void WorldOwnedAvatarIdentityIsStableAndDifferentPerWorldAndUser()
        {
            var baseAvatarId = Guid.NewGuid();
            var worldA = Guid.NewGuid();
            var worldB = Guid.NewGuid();
            var userA = Guid.NewGuid();
            var userB = Guid.NewGuid();

            var first = OntologyWorldAuthorityAccountEntryFlow.CreateWorldScopedAvatarId(
                baseAvatarId,
                worldA,
                userA);
            var replay = OntologyWorldAuthorityAccountEntryFlow.CreateWorldScopedAvatarId(
                baseAvatarId,
                worldA,
                userA);
            var otherWorld = OntologyWorldAuthorityAccountEntryFlow.CreateWorldScopedAvatarId(
                baseAvatarId,
                worldB,
                userA);
            var otherUser = OntologyWorldAuthorityAccountEntryFlow.CreateWorldScopedAvatarId(
                baseAvatarId,
                worldA,
                userB);

            Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            Assert.That(first, Is.Not.EqualTo(baseAvatarId));
            Assert.That(replay, Is.EqualTo(first));
            Assert.That(otherWorld, Is.Not.EqualTo(first));
            Assert.That(otherUser, Is.Not.EqualTo(first));
        }

        [Test]
        public void RuntimeZoneSelectionPrefersContainingActiveZoneAndSkipsDormant()
        {
            var zones = new[]
            {
                new OntologyAuthorityWorldZoneProjection
                {
                    zoneKey = "sleeping",
                    minX = -10f,
                    minZ = -10f,
                    maxX = 10f,
                    maxZ = 10f,
                    simulationMode = "dormant"
                },
                new OntologyAuthorityWorldZoneProjection
                {
                    zoneKey = "world_main",
                    minX = -100f,
                    minZ = -100f,
                    maxX = 100f,
                    maxZ = 100f,
                    simulationMode = "active"
                }
            };

            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow.SelectRuntimeZone(
                    zones,
                    Vector3.zero),
                Is.EqualTo("world_main"));
        }

        [Test]
        public void RuntimeZoneResolutionPreservesCheckpointZoneAndPosition()
        {
            var zones = new[]
            {
                new OntologyAuthorityWorldZoneProjection
                {
                    zoneKey = "world_main",
                    minX = -100f,
                    minZ = -100f,
                    maxX = 100f,
                    maxZ = 100f,
                    simulationMode = "active"
                },
                new OntologyAuthorityWorldZoneProjection
                {
                    zoneKey = "village",
                    minX = 200f,
                    minZ = 200f,
                    maxX = 300f,
                    maxZ = 300f,
                    simulationMode = "reduced"
                }
            };
            var checkpoint = new OntologyAuthorityAvatarCheckpoint
            {
                zoneKey = "village",
                transform = new OntologyAuthorityTransform
                {
                    positionX = 250f,
                    positionY = 3f,
                    positionZ = 240f,
                    rotationY = 45f,
                    scaleX = 1f,
                    scaleY = 1f,
                    scaleZ = 1f
                }
            };

            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow.ResolveRuntimeZone(
                    "world_main",
                    checkpoint,
                    zones),
                Is.EqualTo("village"));

            var payload = OntologyWorldAuthorityClient.CreateAvatarCheckpointPayload(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "village",
                checkpoint.transform);
            StringAssert.Contains("\"positionX\":250", payload);
            StringAssert.Contains("\"positionY\":3", payload);
            StringAssert.Contains("\"positionZ\":240", payload);
        }

        [Test]
        public void RuntimeZoneResolutionRepairsMissingCheckpointZoneFromSavedPosition()
        {
            var zones = new[]
            {
                new OntologyAuthorityWorldZoneProjection
                {
                    zoneKey = "world_main",
                    minX = -100f,
                    minZ = -100f,
                    maxX = 100f,
                    maxZ = 100f,
                    simulationMode = "active"
                }
            };
            var checkpoint = new OntologyAuthorityAvatarCheckpoint
            {
                zoneKey = null,
                transform = new OntologyAuthorityTransform
                {
                    positionX = 12f,
                    positionZ = -8f
                }
            };

            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow.ResolveRuntimeZone(
                    string.Empty,
                    checkpoint,
                    zones),
                Is.EqualTo("world_main"));
        }

        [Test]
        public void RuntimeFoundationDetectionUsesCanonicalFactsOnly()
        {
            var avatarId = Guid.NewGuid();
            var projection = new OntologyAuthorityWorldProjection
            {
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId = OntologyPredicates.HasConcept,
                        objectKind = "canonical",
                        objectCanonicalId = OntologyConcepts.Actor
                    },
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId = OntologyPredicates.MovementSpeed,
                        objectKind = "number",
                        objectValueJson = "5"
                    },
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = avatarId.ToString("D"),
                        predicateId = "equips",
                        objectKind = "entity",
                        objectEntityId = Guid.NewGuid().ToString("D")
                    }
                }
            };

            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow.HasActiveFact(
                    projection,
                    avatarId,
                    OntologyPredicates.MovementSpeed),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow.HasActiveFactObject(
                    projection,
                    avatarId,
                    OntologyPredicates.HasConcept,
                    OntologyConcepts.Actor),
                Is.True,
                "The world avatar must retain its Actor triple so Rule Blocks " +
                "can evaluate it after Authority projection.");
            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow
                    .ContainsLegacyEquipmentRelation(projection),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow.HasActiveFact(
                    projection,
                    Guid.NewGuid(),
                    OntologyPredicates.MovementSpeed),
                Is.False);
        }

        [Test]
        public void NewAvatarPlacementAuthorsCanonicalActorTriple()
        {
            var facts =
                OntologyWorldAuthorityAccountEntryFlow
                    .CreateAvatarSemanticFacts(
                        AssetDatabase.LoadAssetAtPath<
                            OntologyActorProfile>(
                            "Assets/Data/Ontology/Actors/" +
                            "PlayerProfile.asset"),
                        AssetDatabase.LoadAssetAtPath<
                            OntologyWorldAuthoritySettings>(
                            "Assets/Data/Ontology/Networking/" +
                            "WorldAuthoritySettings.asset")
                            .defaultAvatarMovementSpeed);

            Assert.That(facts, Has.Length.EqualTo(34));
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId == OntologyPredicates.HasConcept &&
                        value.objectKind == "canonical" &&
                        value.objectCanonicalId == OntologyConcepts.Actor),
                Is.True);
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId == OntologyPredicates.HasConcept &&
                        value.objectCanonicalId ==
                        OntologyConcepts.PlayerControlled),
                Is.True);
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId == OntologyPredicates.HasConcept &&
                        value.objectCanonicalId ==
                        OntologyConcepts.Damageable),
                Is.True);
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId == OntologyPredicates.HasConcept &&
                        value.objectCanonicalId ==
                        OntologyConcepts.Combatant),
                Is.True);
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId ==
                        OntologyPredicates.BelongsToFaction &&
                        value.objectCanonicalId == "PlayerFaction"),
                Is.True);
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId == "current_health" &&
                        value.objectKind == "number" &&
                        value.objectValueJson == "100"),
                Is.True);
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId == "maximum_health" &&
                        value.objectKind == "number" &&
                        value.objectValueJson == "100"),
                Is.True);
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId == OntologyPredicates.IsAlive &&
                        value.objectKind == "boolean" &&
                        value.objectValueJson == "true"),
                Is.True);
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId ==
                        OntologyPredicates.RespawnAction &&
                        value.objectKind == "canonical" &&
                        value.objectCanonicalId ==
                        OntologyActions.RespawnAvatar),
                Is.True);
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId ==
                        OntologyPredicates.MaximumStepHeight &&
                        value.objectKind == "number" &&
                        value.objectValueJson == "0.3"),
                Is.True);
            Assert.That(
                Array.Exists(
                    facts,
                    value =>
                        value.predicateId ==
                        OntologyPredicates.GroundClearance &&
                        value.objectKind == "number" &&
                        value.objectValueJson == "0.03"),
                Is.True);
        }

        [Test]
        public void RuntimeWalkableSupportIdIsStableAndWorldScoped()
        {
            var worldId = Guid.NewGuid();
            var sameA =
                OntologyWorldAuthorityAccountEntryFlow
                    .CreateWorldScopedSupportId(worldId, "runtime");
            var sameB =
                OntologyWorldAuthorityAccountEntryFlow
                    .CreateWorldScopedSupportId(worldId, "runtime");
            var otherZone =
                OntologyWorldAuthorityAccountEntryFlow
                    .CreateWorldScopedSupportId(worldId, "other");
            var otherWorld =
                OntologyWorldAuthorityAccountEntryFlow
                    .CreateWorldScopedSupportId(
                        Guid.NewGuid(),
                        "runtime");

            Assert.That(sameA, Is.EqualTo(sameB));
            Assert.That(otherZone, Is.Not.EqualTo(sameA));
            Assert.That(otherWorld, Is.Not.EqualTo(sameA));
        }

        [Test]
        public void OrphanedAttackToolRelationRequiresEquipmentRepair()
        {
            var actorId = Guid.NewGuid();
            var toolId = Guid.NewGuid();
            var projection = new OntologyAuthorityWorldProjection
            {
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = actorId.ToString("D"),
                        predicateId = OntologyPredicates.AttacksWith,
                        objectKind = "entity",
                        objectEntityId = toolId.ToString("D")
                    }
                }
            };

            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow
                    .ContainsLegacyEquipmentRelation(projection),
                Is.True);

            projection.facts = projection.facts
                .Append(new OntologyAuthorityFactProjection
                {
                    subjectEntityId = toolId.ToString("D"),
                    predicateId = OntologyPredicates.EquippedBy,
                    objectKind = "entity",
                    objectEntityId = actorId.ToString("D")
                })
                .ToArray();

            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow
                    .ContainsLegacyEquipmentRelation(projection),
                Is.False);
        }

        [Test]
        public void LegacyUnzonedEntityIsMigratedOnlyForOneMatchingZone()
        {
            var entity = new OntologyAuthorityEntityProjection
            {
                entityId = Guid.NewGuid().ToString("D"),
                zoneKey = string.Empty,
                transform = new OntologyAuthorityTransform
                {
                    positionX = 10f,
                    positionZ = 20f
                }
            };
            var projection = new OntologyAuthorityWorldProjection
            {
                entities = new[] { entity }
            };
            var zones = new[]
            {
                new OntologyAuthorityWorldZoneProjection
                {
                    zoneKey = "world_main",
                    minX = 0f,
                    minZ = 0f,
                    maxX = 100f,
                    maxZ = 100f
                }
            };

            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow
                    .ContainsUniquelyZonableEntity(projection, zones),
                Is.True);

            zones = zones
                .Append(new OntologyAuthorityWorldZoneProjection
                {
                    zoneKey = "overlap",
                    minX = 5f,
                    minZ = 5f,
                    maxX = 25f,
                    maxZ = 25f
                })
                .ToArray();

            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow
                    .ContainsUniquelyZonableEntity(projection, zones),
                Is.False);
        }

        [Test]
        public void MeaningPackageUsesJsonNullForOptionalEntityGuid()
        {
            var change = new OntologyMeaningPackageChange
            {
                operation = "apply",
                applicationId = Guid.NewGuid().ToString("D"),
                slotId = "primary_physical_meaning",
                packageId = "rule_preset_water_buoyancy",
                adoptExistingContributions = true
            };
            change.authoredFacts.Add(new OntologyFactEntry
            {
                predicate = OntologyPredicates.PhysicalProfile,
                obj = "LightBuoyant"
            });

            var payload =
                OntologyWorldAuthorityClient.CreateMeaningPackagePayload(
                    Guid.NewGuid(),
                    change);

            Assert.That(
                payload,
                Does.Contain("\"objectEntityId\":null"));
            Assert.That(
                payload,
                Does.Not.Contain("\"objectEntityId\":\"\""));
            Assert.That(
                payload,
                Does.Contain("\"adoptExistingContributions\":true"));
        }

        [Test]
        public void MeaningPackagePreservesTypedAuthoredFacts()
        {
            var change = new OntologyMeaningPackageChange
            {
                operation = "apply",
                applicationId = Guid.NewGuid().ToString("D"),
                slotId = "template_semantic_baseline",
                packageId = "typed_semantic_baseline"
            };
            change.authorityFacts.Add(
                OntologyWorldAuthorityClient.CreateInitialFact(
                    "maximum_health",
                    "25"));
            change.authorityFacts.Add(
                OntologyWorldAuthorityClient.CreateInitialFact(
                    "is_alive",
                    "true"));

            var payload =
                OntologyWorldAuthorityClient.CreateMeaningPackagePayload(
                    Guid.NewGuid(),
                    change);

            Assert.That(payload, Does.Contain(
                "\"predicateId\":\"maximum_health\",\"objectKind\":\"number\""));
            Assert.That(payload, Does.Contain("\"objectValueJson\":\"25\""));
            Assert.That(payload, Does.Contain(
                "\"predicateId\":\"is_alive\",\"objectKind\":\"boolean\""));
            Assert.That(payload, Does.Contain("\"objectValueJson\":\"true\""));
        }

        [Test]
        public void LegacyMeaningPackageOutboxNormalizesEmptyOptionalGuid()
        {
            const string legacy =
                "{\"authoredFacts\":[{\"objectEntityId\":\"\"}]}";

            Assert.That(
                OntologyWorldAuthorityClient
                    .NormalizeMeaningPackagePayload(legacy),
                Is.EqualTo(
                    "{\"authoredFacts\":[{\"objectEntityId\":null}]}"));
        }

        [Test]
        public void MeaningPackageRemovalUsesAValidEmptyApplicationGuid()
        {
            var change = new OntologyMeaningPackageChange
            {
                operation = "remove",
                applicationId = string.Empty,
                slotId = "primary_physical_meaning",
                packageId = string.Empty
            };

            var payload =
                OntologyWorldAuthorityClient.CreateMeaningPackagePayload(
                    Guid.NewGuid(),
                    change);

            Assert.That(
                payload,
                Does.Contain(
                    "\"applicationId\":\"" +
                    Guid.Empty.ToString("D") + "\""));
            Assert.That(
                payload,
                Does.Not.Contain("\"applicationId\":\"\""));
        }

        [Test]
        public void RuleBindingIdentityIsDeterministicAndDataScoped()
        {
            var entityId = Guid.NewGuid();
            var first =
                OntologyWorldAuthorityBridge.CreateRuleBindingId(
                    entityId,
                    "AutoEquipNearbyWearable",
                    "?object");
            var replay =
                OntologyWorldAuthorityBridge.CreateRuleBindingId(
                    entityId,
                    "AutoEquipNearbyWearable",
                    "?object");
            var otherVariable =
                OntologyWorldAuthorityBridge.CreateRuleBindingId(
                    entityId,
                    "AutoEquipNearbyWearable",
                    "?item");

            Assert.That(replay, Is.EqualTo(first));
            Assert.That(otherVariable, Is.Not.EqualTo(first));
        }

        [Test]
        public void RuntimeGateHidesAssignedWorldPresentationUntilInWorld()
        {
            var coordinatorObject = new GameObject("RuntimeGateCoordinator");
            var gateObject = new GameObject("RuntimeGate");
            var runtimeRoot = new GameObject("RuntimeHud");
            try
            {
                var coordinator = coordinatorObject.AddComponent<OntologyGameSessionCoordinator>();
                var gate = gateObject.AddComponent<OntologyWorldRuntimeActivationGate>();
                SetField(gate, "sessionCoordinator", coordinator);
                SetField(gate, "runtimeRoots", new[] { runtimeRoot });
                SetField(gate, "discoverKnownRuntimeObjects", false);

                coordinator.SignedOut();
                gate.RefreshTargets();
                Assert.That(runtimeRoot.activeSelf, Is.False);
                Assert.That(gate.RuntimeVisible, Is.False);

                coordinator.BeginWorldEntry();
                Assert.That(runtimeRoot.activeSelf, Is.False);
                coordinator.WorldEntryCompleted(true);
                Assert.That(runtimeRoot.activeSelf, Is.True);
                Assert.That(gate.RuntimeVisible, Is.True);

                coordinator.BeginLeavingWorld();
                Assert.That(runtimeRoot.activeSelf, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runtimeRoot);
                UnityEngine.Object.DestroyImmediate(gateObject);
                UnityEngine.Object.DestroyImmediate(coordinatorObject);
            }
        }

        [Test]
        public void RuntimeGateActivatesCombatAndTargetingOnlyAfterWorldEntry()
        {
            var coordinatorObject =
                new GameObject("CombatActivationCoordinator");
            var combatObject = new GameObject("CombatActivationController");
            var targetingObject = new GameObject("CombatActivationTargeting");
            var gateObject = new GameObject("CombatActivationGate");
            try
            {
                var coordinator = coordinatorObject.AddComponent<
                    OntologyGameSessionCoordinator>();
                var combat = combatObject.AddComponent<
                    OntologyCombatController>();
                var targeting = targetingObject.AddComponent<
                    OntologyAuthorityTargetingAdapter>();
                var gate = gateObject.AddComponent<
                    OntologyWorldRuntimeActivationGate>();

                SetField(gate, "sessionCoordinator", coordinator);
                SetField(gate, "runtimeRoots", Array.Empty<GameObject>());
                SetField(gate, "runtimeBehaviours", Array.Empty<Behaviour>());
                SetField(gate, "discoverKnownRuntimeObjects", true);

                coordinator.SignedOut();
                gate.RefreshTargets();
                Assert.That(combat.enabled, Is.False);
                Assert.That(targeting.enabled, Is.False);

                coordinator.BeginWorldEntry();
                Assert.That(combat.enabled, Is.False);
                Assert.That(targeting.enabled, Is.False);

                coordinator.WorldEntryCompleted(true);
                Assert.That(combat.enabled, Is.True);
                Assert.That(targeting.enabled, Is.True);

                coordinator.BeginLeavingWorld();
                Assert.That(combat.enabled, Is.False);
                Assert.That(targeting.enabled, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(gateObject);
                UnityEngine.Object.DestroyImmediate(targetingObject);
                UnityEngine.Object.DestroyImmediate(combatObject);
                UnityEngine.Object.DestroyImmediate(coordinatorObject);
            }
        }

        [Test]
        public void RuntimeGateNeverDisablesLocalAvatarPresentationBoundary()
        {
            var coordinatorObject =
                new GameObject("AvatarBoundaryCoordinator");
            var gateObject = new GameObject("AvatarBoundaryGate");
            var avatarObject = new GameObject("LocalAvatar");
            try
            {
                var coordinator = coordinatorObject.AddComponent<
                    OntologyGameSessionCoordinator>();
                var gate = gateObject.AddComponent<
                    OntologyWorldRuntimeActivationGate>();
                avatarObject.AddComponent<
                    OntologyWorldEntryPresentationCoordinator>();

                SetField(gate, "sessionCoordinator", coordinator);
                SetField(gate, "discoverKnownRuntimeObjects", false);

                var addRoot = typeof(OntologyWorldRuntimeActivationGate)
                    .GetMethod(
                        "AddRoot",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(addRoot, Is.Not.Null);
                addRoot.Invoke(gate, new object[] { avatarObject.transform });

                coordinator.SignedOut();
                gate.RefreshTargets();

                Assert.That(avatarObject.activeSelf, Is.True);
                Assert.That(gate.RuntimeVisible, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatarObject);
                UnityEngine.Object.DestroyImmediate(gateObject);
                UnityEngine.Object.DestroyImmediate(coordinatorObject);
            }
        }

        [Test]
        public void RuntimeGate_DoesNotOpenOnDemandWorldPlacementPanelOnEntry()
        {
            var coordinatorObject = new GameObject("PlacementGateCoordinator");
            var gateObject = new GameObject("PlacementGate");
            var panelHost = new GameObject("PlacementPanelHost");
            var placementHud = new GameObject(
                "ObjectPlacementHUD",
                typeof(RectTransform));
            try
            {
                placementHud.transform.SetParent(panelHost.transform, false);
                panelHost.AddComponent<OntologyObjectPlacementPanel>();
                placementHud.SetActive(false);

                var coordinator =
                    coordinatorObject.AddComponent<OntologyGameSessionCoordinator>();
                var gate = gateObject.AddComponent<OntologyWorldRuntimeActivationGate>();
                SetField(gate, "sessionCoordinator", coordinator);
                SetField(gate, "runtimeRoots", Array.Empty<GameObject>());
                SetField(gate, "discoverKnownRuntimeObjects", true);
                gate.RefreshTargets();

                coordinator.BeginWorldEntry();
                coordinator.WorldEntryCompleted(true);

                Assert.That(panelHost.activeSelf, Is.True,
                    "The placement binder must become available in world.");
                Assert.That(placementHud.activeSelf, Is.False,
                    "The on-demand placement catalog must remain closed on entry.");

                placementHud.SetActive(true);
                coordinator.BeginLeavingWorld();
                Assert.That(placementHud.activeSelf, Is.False,
                    "Leaving the world must close an open placement catalog.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(placementHud);
                UnityEngine.Object.DestroyImmediate(panelHost);
                UnityEngine.Object.DestroyImmediate(gateObject);
                UnityEngine.Object.DestroyImmediate(coordinatorObject);
            }
        }

        [Test]
        public void LocalSnapshotPathIsIsolatedByAccountAndWorld()
        {
            var root = Path.Combine("C:", "TormiaTest");
            var accountAWorldA = OntologySaveController.BuildScopedSavePath(
                root, "account-a", "world-a", "ontology_save.json");
            var accountAWorldB = OntologySaveController.BuildScopedSavePath(
                root, "account-a", "world-b", "ontology_save.json");
            var accountBWorldA = OntologySaveController.BuildScopedSavePath(
                root, "account-b", "world-a", "ontology_save.json");

            Assert.That(accountAWorldA, Is.Not.EqualTo(accountAWorldB));
            Assert.That(accountAWorldA, Is.Not.EqualTo(accountBWorldA));
            Assert.That(accountAWorldA, Does.Contain("account-account-a"));
            Assert.That(accountAWorldA, Does.Contain("world-world-a"));
        }

        [Test]
        public void ConfiguredAuthorityNeverFallsBackToLocalSnapshotBeforeLogin()
        {
            Assert.That(
                OntologySaveController.ShouldUseLocalSnapshot(
                    authorityClientConfigured: true),
                Is.False);
            Assert.That(
                OntologySaveController.ShouldUseLocalSnapshot(
                    authorityClientConfigured: false),
                Is.True);
        }

        [Test]
        public void CheckpointRetriesOnlyARevisionConflict()
        {
            Assert.That(
                OntologyAvatarCheckpointController.ShouldReloadAndRetry(
                    OntologyAuthorityCommandResult.Rejected("stale_revision")),
                Is.True);
            Assert.That(
                OntologyAvatarCheckpointController.ShouldReloadAndRetry(
                    OntologyAuthorityCommandResult.Rejected("forbidden")),
                Is.False);
            Assert.That(
                OntologyAvatarCheckpointController.ShouldReloadAndRetry(
                    OntologyAuthorityCommandResult.TransportFailure(
                        "network_error")),
                Is.False);
        }

        [Test]
        public void CheckpointCaptureRejectsTransientUnsafePositions()
        {
            Assert.That(
                OntologyAvatarCheckpointController.CanCaptureCheckpoint(
                    recoveryActive: false,
                    hasWaterOverlap: false,
                    controllerEnabled: true,
                    controllerGrounded: true),
                Is.True);
            Assert.That(
                OntologyAvatarCheckpointController.CanCaptureCheckpoint(
                    recoveryActive: true,
                    hasWaterOverlap: false,
                    controllerEnabled: false,
                    controllerGrounded: false),
                Is.False,
                "Drowning presentation must not overwrite the confirmed respawn anchor.");
            Assert.That(
                OntologyAvatarCheckpointController.CanCaptureCheckpoint(
                    recoveryActive: false,
                    hasWaterOverlap: true,
                    controllerEnabled: true,
                    controllerGrounded: false),
                Is.False,
                "A water-overlap observation must not become a durable respawn checkpoint.");
            Assert.That(
                OntologyAvatarCheckpointController.CanCaptureCheckpoint(
                    recoveryActive: false,
                    hasWaterOverlap: false,
                    controllerEnabled: true,
                    controllerGrounded: false),
                Is.False,
                "An airborne presentation sample must not become a durable respawn checkpoint.");
        }

        [Test]
        public void CheckpointReseedSuspendsResolvedPosePublication()
        {
            var actor = new GameObject("CheckpointReseedActor");
            try
            {
                var reconciler =
                    actor.AddComponent<
                        OntologyWorldAuthorityPlayerMotionReconciler>();

                reconciler.BeginCheckpointReseed();

                Assert.That(reconciler.CheckpointReseedPending, Is.True);

                reconciler.CompleteCheckpointReseed(
                    accepted: true,
                    confirmedPosition: new Vector3(4f, 2f, 6f));

                Assert.That(reconciler.CheckpointReseedPending, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(actor);
            }
        }

        [Test]
        public void PackageConflictUsesPlayerFacingLocalizationKey()
        {
            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow
                    .ResolvePackageFailureLocalizationKey(
                        "Development action package publish failed: " +
                        "action_definition_version_conflict"),
                Is.EqualTo(
                    "ui.account.world_entry.package_version_conflict"));
            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow
                    .ResolvePackageFailureLocalizationKey(
                        "Development Rule Block package publish failed: " +
                        "rule_definition_version_conflict"),
                Is.EqualTo(
                    "ui.account.world_entry.package_version_conflict"));
            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow
                    .ResolvePackageFailureLocalizationKey("network_error"),
                Is.EqualTo(
                    "ui.account.world_entry.package_verification_failed"));
        }

        [Test]
        public void AuthorityProjectionMembershipRemovesOnlyConfirmedGhostPresentation()
        {
            var value = new GameObject("ProjectionMembership");
            try
            {
                var identity =
                    value.AddComponent<OntologyAuthorityEntityIdentity>();
                var entityId = Guid.NewGuid();
                identity.SetGuid(entityId);
                var emptyProjection = new OntologyAuthorityWorldProjection
                {
                    entities = Array.Empty<OntologyAuthorityEntityProjection>()
                };
                var matchingProjection = new OntologyAuthorityWorldProjection
                {
                    entities = new[]
                    {
                        new OntologyAuthorityEntityProjection
                        {
                            entityId = entityId.ToString("D")
                        }
                    }
                };

                Assert.That(
                    OntologyWorldAuthorityBridge
                        .ShouldRemoveUnprojectedPlaceablePresentation(
                            true,
                            false,
                            emptyProjection,
                            identity),
                    Is.True,
                    "A visible runtime placeable absent from Authority is a " +
                    "non-interactable ghost and must be removed.");
                Assert.That(
                    OntologyWorldAuthorityBridge
                        .ShouldRemoveUnprojectedPlaceablePresentation(
                            true,
                            false,
                            matchingProjection,
                            identity),
                    Is.False,
                    "A projected placeable remains visible.");
                Assert.That(
                    OntologyWorldAuthorityBridge
                        .ShouldRemoveUnprojectedPlaceablePresentation(
                            true,
                            true,
                            emptyProjection,
                            identity),
                    Is.False,
                    "An Authority-first placement in flight must not be removed.");
                Assert.That(
                    OntologyWorldAuthorityBridge
                        .ShouldRemoveUnprojectedPlaceablePresentation(
                            false,
                            false,
                            emptyProjection,
                            identity),
                    Is.False,
                    "Local edit-mode authoring is preserved before world entry.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(value);
            }
        }

        [Test]
        public void PlacedSemanticContractReadsTypedNumericProjectionFact()
        {
            var entityId = Guid.NewGuid();
            var projection = new OntologyAuthorityWorldProjection
            {
                facts = new[]
                {
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = entityId.ToString("D"),
                        predicateId =
                            OntologyPredicates.SemanticContractVersion,
                        objectKind = "number",
                        objectValueJson = "7"
                    },
                    new OntologyAuthorityFactProjection
                    {
                        subjectEntityId = entityId.ToString("D"),
                        predicateId =
                            OntologyPredicates.SemanticContractVersion,
                        objectKind = "canonical",
                        objectCanonicalId = "6"
                    }
                }
            };

            Assert.That(
                OntologyAuthorityProjectionSemantics
                    .ResolveSemanticContractVersion(projection, entityId),
                Is.EqualTo(7),
                "A typed numeric marker must prevent the catalog baseline " +
                "from being reapplied on every world load.");
            Assert.That(
                OntologyWorldAuthorityAccountEntryFlow
                    .ResolveSemanticContractVersion(projection, entityId),
                Is.EqualTo(7),
                "Player and placed-entity migration must share one parser.");
        }

        [Test]
        public void TemplateMeaningPackageApplicationIdentityIsStable()
        {
            var entityId = Guid.NewGuid();
            var first = OntologyWorldAuthorityBridge
                .CreateMeaningPackageApplicationId(
                    entityId,
                    "template_semantic_baseline",
                    "BeholderBasic_semantic_baseline_v7");
            var retry = OntologyWorldAuthorityBridge
                .CreateMeaningPackageApplicationId(
                    entityId,
                    "template_semantic_baseline",
                    "BeholderBasic_semantic_baseline_v7");
            var nextVersion = OntologyWorldAuthorityBridge
                .CreateMeaningPackageApplicationId(
                    entityId,
                    "template_semantic_baseline",
                    "BeholderBasic_semantic_baseline_v8");

            Assert.That(retry, Is.EqualTo(first));
            Assert.That(nextVersion, Is.Not.EqualTo(first));
        }

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
