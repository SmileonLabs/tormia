using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologySemanticAdapterSynchronizerTests
    {
        [UnityTest]
        public IEnumerator LocalCharacterPhysicalMeaningOwnsAndRemovesMotionLease()
        {
            var bootstrapObject = new GameObject("Bootstrap");
            var avatar = new GameObject("LocalCharacterAvatar");
            var profile =
                ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            var database =
                ScriptableObject.CreateInstance<
                    OntologyPhysicalProfileDatabase>();
            try
            {
                profile.profileId =
                    OntologyObjects.LocalCharacterController;
                profile.mobilityMode =
                    OntologyPhysicalMobilityMode.AuthorityKinematic;
                profile.motionDriver =
                    OntologyMotionDriver.LocalCharacterController;
                profile.collisionRole =
                    OntologyCollisionRole.ActorBody;
                profile.maximumStepHeight = 0.42f;
                database.Replace(new[] { profile });
                database.ReplaceCollisionLayers(
                    CreateCollisionLayerBindings());

                var bootstrap =
                    bootstrapObject.AddComponent<OntologyWorldBootstrap>();
                typeof(OntologyWorldBootstrap)
                    .GetField(
                        "physicalProfileDatabase",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(bootstrap, database);
                avatar.AddComponent<CharacterController>();
                var identity =
                    avatar.AddComponent<OntologyAuthorityEntityIdentity>();
                var input =
                    avatar.AddComponent<OntologyInputSystemPlayerInput>();
                input.enabled = false;
                var ontology = avatar.AddComponent<OntologyObject>();
                ontology.ConfigureOntologyData(
                    "LocalCharacterAvatar",
                    new[] { OntologyConcepts.Actor },
                    new[]
                    {
                        new OntologyFactEntry
                        {
                            predicate =
                                OntologyPredicates.PhysicalProfile,
                            obj =
                                OntologyObjects
                                    .LocalCharacterController
                        }
                    });

                OntologySemanticAdapterSynchronizer
                    .SynchronizePhysical(avatar, bootstrap);
                yield return null;

                var driver =
                    avatar.GetComponent<OntologyMotionDriverAdapter>();
                var coordinator =
                    avatar.GetComponent<
                        OntologyCharacterMotionCoordinator>();
                Assert.That(driver, Is.Not.Null);
                Assert.That(
                    driver.Allows(
                        OntologyMotionDriver.LocalCharacterController),
                    Is.True);
                Assert.That(coordinator, Is.Not.Null);
                Assert.That(coordinator.enabled, Is.True);
                Assert.That(
                    avatar.layer,
                    Is.EqualTo(LayerMask.NameToLayer("ActorBody")));
                Assert.That(
                    avatar.GetComponent<OntologyCollisionRoleAdapter>()
                        .CollisionLayerApplied,
                    Is.True);
                Assert.That(
                    coordinator.MaximumStepHeight,
                    Is.EqualTo(0.42f).Within(0.0001f),
                    "The coordinator must consume character-step tuning from " +
                    "the active Physical Meaning profile.");
                Assert.That(
                    avatar.GetComponent<Rigidbody>(),
                    Is.Null,
                    "Local CharacterController presentation must not receive " +
                    "a competing Rigidbody motion owner.");
                Assert.That(
                    avatar.GetComponent<
                        OntologyAuthorityKinematicActorAdapter>(),
                    Is.Null);

                ontology.ConfigureOntologyData(
                    "LocalCharacterAvatar",
                    new[] { OntologyConcepts.Actor },
                    System.Array.Empty<OntologyFactEntry>());
                OntologySemanticAdapterSynchronizer
                    .SynchronizePhysical(avatar, bootstrap);
                yield return null;

                Assert.That(driver.enabled, Is.False);
                Assert.That(
                    avatar.layer,
                    Is.EqualTo(0),
                    "Removing Physical Meaning must restore the authored " +
                    "Unity presentation layer.");
                Assert.That(
                    coordinator.enabled,
                    Is.False,
                    "Removing Physical Meaning must remove the matching local " +
                    "motion lease instead of leaving a hidden fallback.");
                Assert.That(
                    coordinator.MaximumStepHeight,
                    Is.Zero,
                    "Removing Physical Meaning must clear its derived physical tuning.");
                Assert.That(
                    OntologyTransformOwnershipResolver.Resolve(
                        identity,
                        null),
                    Is.EqualTo(
                        OntologyTransformOwner.LocalCharacterController),
                    "Removing locomotion permission must stop movement without " +
                    "letting a stale durable projection reclaim the visible " +
                    "local avatar pose.");
            }
            finally
            {
                Object.Destroy(avatar);
                Object.Destroy(bootstrapObject);
                Object.Destroy(profile);
                Object.Destroy(database);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator DamageableConceptOwnsCombatPresenterLifecycle()
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = "OrdinaryEditedObject";
            try
            {
                var ontology = target.AddComponent<OntologyObject>();
                ontology.ConfigureOntologyData(
                    "OrdinaryEditedObject",
                    new[] { OntologyConcepts.Damageable },
                    new[]
                    {
                        new OntologyFactEntry
                        {
                            predicate =
                                OntologyPredicates.HitAnimationIntent,
                            obj = OntologyAnimationIntentIds.HitReaction
                        }
                    });
                var identity =
                    target.AddComponent<OntologyAuthorityEntityIdentity>();
                identity.SetGuid(System.Guid.NewGuid());

                OntologySemanticAdapterSynchronizer
                    .SynchronizeCombatPresentation(target);
                yield return null;

                var presenter =
                    target.GetComponent<OntologyCombatTargetPresenter>();
                Assert.That(presenter, Is.Not.Null);
                Assert.That(presenter.enabled, Is.True);
                Assert.That(
                    presenter.AuthorityIdentity,
                    Is.SameAs(identity),
                    "Combat presentation must be created from semantic data, " +
                    "not from a monster prefab.");

                ontology.ConfigureOntologyData(
                    "OrdinaryEditedObject",
                    System.Array.Empty<string>(),
                    System.Array.Empty<OntologyFactEntry>());
                OntologySemanticAdapterSynchronizer
                    .SynchronizeCombatPresentation(target);
                yield return null;

                Assert.That(
                    presenter.enabled,
                    Is.False,
                    "Removing Damageable meaning must remove the adapter route.");
            }
            finally
            {
                Object.Destroy(target);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator EntryGroundingKeepsFirstMoveOnSupportSurface()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var player = new GameObject("GroundedAvatar");
            try
            {
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(10f, 1f, 10f);
                player.transform.position = new Vector3(0f, 0.8f, 0f);
                var controller = player.AddComponent<CharacterController>();
                controller.center = new Vector3(0f, 1f, 0f);
                controller.height = 2f;
                controller.radius = 0.5f;
                var input =
                    player.AddComponent<OntologyInputSystemPlayerInput>();
                input.enabled = false;
                var grounding =
                    player.AddComponent<OntologyWorldEntryGroundingAdapter>();

                Physics.SyncTransforms();
                Assert.That(grounding.TrySettle(), Is.True);
                var settledY = player.transform.position.y;

                controller.Move(
                    Vector3.forward * 0.1f +
                    Vector3.down * 0.05f);
                yield return null;

                Assert.That(
                    player.transform.position.y,
                    Is.EqualTo(settledY).Within(0.02f),
                    "The first controller move must not sink below the " +
                    "support surface after checkpoint grounding.");
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(ground);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator WorldEntryPresentationPreparesBeforeSessionActivation()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var sessionObject = new GameObject("EntrySession");
            var player = new GameObject("PreparedEntryAvatar");
            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var hiddenVisual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(10f, 1f, 10f);
                player.transform.position = new Vector3(0f, 0.8f, 0f);
                visual.transform.SetParent(player.transform, false);
                var visualRenderer = visual.GetComponent<Renderer>();
                hiddenVisual.transform.SetParent(player.transform, false);
                var hiddenRenderer = hiddenVisual.GetComponent<Renderer>();
                hiddenRenderer.enabled = false;

                var session =
                    sessionObject.AddComponent<OntologyGameSessionCoordinator>();
                session.BeginWorldEntry();
                var identity =
                    player.AddComponent<OntologyAuthorityEntityIdentity>();
                identity.SetGuid(System.Guid.NewGuid());
                var controller = player.AddComponent<CharacterController>();
                controller.center = new Vector3(0f, 1f, 0f);
                controller.height = 2f;
                controller.radius = 0.5f;
                var input =
                    player.AddComponent<OntologyInputSystemPlayerInput>();
                player.AddComponent<Animator>();
                var animation =
                    player.AddComponent<OntologyAnimationAdapter>();
                player.AddComponent<OntologyWorldEntryGroundingAdapter>();
                var coordinator =
                    player.AddComponent<
                        OntologyWorldEntryPresentationCoordinator>();
                coordinator.Configure(session, identity);

                typeof(OntologyInputSystemPlayerInput)
                    .GetField(
                        "verticalVelocity",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(input, -12f);
                typeof(OntologyAnimationAdapter)
                    .GetField(
                        "transientPresentationActive",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(animation, true);

                Assert.That(coordinator.BeginPreparation(), Is.True);
                Assert.That(input.enabled, Is.False);
                Assert.That(animation.enabled, Is.False);
                Assert.That(visualRenderer.enabled, Is.False);
                Assert.That(session.State,
                    Is.EqualTo(OntologyGameSessionState.EnteringWorld));

                var prepared = false;
                yield return coordinator.PrepareRoutine(
                    value => prepared = value);

                Assert.That(prepared, Is.True);
                Assert.That(coordinator.IsPrepared, Is.True);
                Assert.That(controller.isGrounded, Is.True);
                Assert.That(input.VerticalVelocity, Is.Zero);
                Assert.That(input.IsMovingIntent, Is.False);
                Assert.That(
                    coordinator.CommitBeforeSessionActivation(),
                    Is.True);
                Assert.That(animation.enabled, Is.True);
                Assert.That(
                    (bool)typeof(OntologyAnimationAdapter)
                        .GetField(
                            "transientPresentationActive",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic)
                        .GetValue(animation),
                    Is.False);
                Assert.That(session.State,
                    Is.EqualTo(OntologyGameSessionState.EnteringWorld),
                    "Presentation must be prepared before the session gate " +
                    "opens runtime input and renderers.");

                session.WorldEntryCompleted(true);
                Assert.That(session.IsInWorld, Is.True);
                Assert.That(
                    input.enabled,
                    Is.True,
                    "Local input must open only from the completed InWorld " +
                    "session transition.");
                Assert.That(
                    visualRenderer.enabled,
                    Is.True,
                    "The local avatar must become visible from the same " +
                    "completed session transition.");
                Assert.That(
                    hiddenRenderer.enabled,
                    Is.False,
                    "Opening the entry gate must preserve per-part renderer " +
                    "visibility instead of enabling every character part.");
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(sessionObject);
                Object.Destroy(ground);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PendingGroundingRetriesWhenControllerBecomesReady()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var player = new GameObject("LateControllerGroundingAvatar");
            try
            {
                ground.transform.position = new Vector3(0f, -0.5f, 0f);
                ground.transform.localScale = new Vector3(10f, 1f, 10f);
                player.transform.position = new Vector3(0f, 0.8f, 0f);
                var controller = player.AddComponent<CharacterController>();
                controller.center = new Vector3(0f, 1f, 0f);
                controller.height = 2f;
                controller.radius = 0.5f;
                controller.enabled = false;
                var input =
                    player.AddComponent<OntologyInputSystemPlayerInput>();
                input.enabled = false;
                var grounding =
                    player.AddComponent<OntologyWorldEntryGroundingAdapter>();

                Assert.That(grounding.RequestSettle(), Is.False);
                Assert.That(grounding.SettlePending, Is.True);

                controller.enabled = true;
                yield return new WaitForFixedUpdate();
                yield return null;

                Assert.That(
                    grounding.SettlePending,
                    Is.False,
                    "Grounding must complete after the controller becomes " +
                    "eligible, without waiting for the first input move. " +
                    "controllerEnabled=" + controller.enabled +
                    ", controllerGrounded=" + controller.isGrounded +
                    ", coordinator=" +
                    (player.GetComponent<
                        OntologyCharacterMotionCoordinator>() != null) +
                    ", supportProbe=" +
                    (player.GetComponent<
                        OntologyCharacterSupportProbe>() != null) +
                    ", playerY=" + player.transform.position.y);
            }
            finally
            {
                Object.Destroy(player);
                Object.Destroy(ground);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator LocalPlayerRoleWinsWhenSeveralActorsExist()
        {
            var bootstrapObject = new GameObject("Bootstrap");
            var player = new GameObject("LocalAvatar");
            var npc = new GameObject("NpcActor");
            try
            {
                var bootstrap =
                    bootstrapObject.AddComponent<OntologyWorldBootstrap>();
                var playerOntology = player.AddComponent<OntologyObject>();
                playerOntology.ConfigureOntologyData(
                    "PlayerEntity",
                    new[] { OntologyConcepts.Actor },
                    new OntologyFactEntry[0]);
                var input =
                    player.AddComponent<OntologyInputSystemPlayerInput>();
                input.enabled = false;
                var npcOntology = npc.AddComponent<OntologyObject>();
                npcOntology.ConfigureOntologyData(
                    "NpcEntity",
                    new[] { OntologyConcepts.Actor },
                    new OntologyFactEntry[0]);

                bootstrap.ResetWorld(logReport: false);
                yield return null;

                Assert.That(
                    OntologySemanticAdapterSynchronizer
                        .ResolvePresentationActor(bootstrap),
                    Is.SameAs(playerOntology),
                    "NPC Actor entities must not make local equipment " +
                    "proximity lose the account avatar.");
            }
            finally
            {
                Object.Destroy(npc);
                Object.Destroy(player);
                Object.Destroy(bootstrapObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator LightBuoyantProfileKeepsMeshPrefabAboveSolidGround()
        {
            var profile = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            profile.profileId = "LightBuoyant";
            profile.supportsBuoyancy = true;
            profile.dynamicColliderMode =
                OntologyDynamicColliderMode.BoundsBox;
            profile.dynamicColliderSizeMultiplier =
                new Vector3(0.95f, 0.95f, 0.95f);
            var database =
                ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            database.Replace(new[] { profile });

            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap =
                bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalProfileDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, database);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(8f, 1f, 8f);

            var stone = new GameObject("MeshStone");
            stone.transform.position = new Vector3(0f, 2f, 0f);
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 0f, 0.5f)
                },
                triangles = new[]
                {
                    0, 2, 1,
                    0, 3, 2,
                    1, 2, 3,
                    0, 1, 3
                }
            };
            mesh.RecalculateBounds();
            var meshFilter = stone.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;
            stone.AddComponent<MeshRenderer>();
            var originalCollider = stone.AddComponent<MeshCollider>();
            originalCollider.sharedMesh = mesh;
            originalCollider.convex = false;
            var ontology = stone.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                stone.name,
                new[] { OntologyConcepts.FloatableObject },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = profile.profileId
                    }
                });

            OntologySemanticAdapterSynchronizer.SynchronizePhysical(
                stone,
                bootstrap);
            Assert.That(originalCollider.enabled, Is.False);
            Assert.That(stone.GetComponent<BoxCollider>(), Is.Not.Null);

            for (var index = 0; index < 90; index++)
                yield return new WaitForFixedUpdate();

            Assert.That(
                stone.transform.position.y,
                Is.GreaterThan(-0.15f),
                "The Rigidbody-safe bounds collider must stop the object at the ground.");

            Object.Destroy(stone);
            Object.Destroy(ground);
            Object.Destroy(mesh);
            Object.Destroy(bootstrapObject);
            Object.Destroy(database);
            Object.Destroy(profile);
            yield return null;
        }

        [UnityTest]
        public IEnumerator OptionalPhysicalEffectAdapterFollowsOntologyFact()
        {
            var physicalProfile =
                ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            physicalProfile.profileId = "HeavySinking";
            var physicalDatabase =
                ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            physicalDatabase.Replace(new[] { physicalProfile });
            var effect =
                ScriptableObject.CreateInstance<OntologyPhysicalEffectProfile>();
            effect.effectId = "WindDrift";
            effect.compatibleProfileIds = new List<string> { "HeavySinking" };
            var effectDatabase =
                ScriptableObject.CreateInstance<OntologyPhysicalEffectDatabase>();
            effectDatabase.Replace(new[] { effect });

            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap =
                bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalProfileDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, physicalDatabase);
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalEffectDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, effectDatabase);

            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var ontology = target.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                "Target",
                new string[0],
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = physicalProfile.profileId
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.HasPhysicalEffect,
                        obj = effect.effectId
                    }
                });

            OntologySemanticAdapterSynchronizer.SynchronizeAll(
                target,
                bootstrap);
            var adapter =
                target.GetComponent<OntologyPhysicalEffectAdapter>();
            Assert.That(adapter, Is.Not.Null);
            Assert.That(adapter.enabled, Is.True);

            ontology.ConfigureOntologyData(
                "Target",
                new string[0],
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = physicalProfile.profileId
                    }
                });
            OntologySemanticAdapterSynchronizer.SynchronizeAll(
                target,
                bootstrap);
            Assert.That(adapter.enabled, Is.False);

            Object.Destroy(target);
            Object.Destroy(bootstrapObject);
            Object.Destroy(effectDatabase);
            Object.Destroy(effect);
            Object.Destroy(physicalDatabase);
            Object.Destroy(physicalProfile);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhysicalProfileFactAddsAndRemovesBuoyancyAdapterConfiguration()
        {
            var profile = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            profile.profileId = "HeavyBuoyant";
            profile.supportsBuoyancy = true;
            profile.dynamicColliderMode = OntologyDynamicColliderMode.BoundsBox;
            var database = ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            database.Replace(new[] { profile });

            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalProfileDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, database);

            var stone = new GameObject("Stone");
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 0f, 0.5f)
                },
                triangles = new[]
                {
                    0, 2, 1,
                    0, 3, 2,
                    1, 2, 3,
                    0, 1, 3
                }
            };
            mesh.RecalculateBounds();
            var originalCollider = stone.AddComponent<MeshCollider>();
            originalCollider.sharedMesh = mesh;
            originalCollider.convex = false;
            var ontology = stone.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                stone.name,
                new[] { OntologyConcepts.FloatableObject },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = profile.profileId
                    }
                });

            OntologySemanticAdapterSynchronizer.SynchronizePhysical(stone, bootstrap);
            var adapter = stone.GetComponent<OntologyBuoyancyAdapter>();
            Assert.That(adapter, Is.Not.Null);
            Assert.That(adapter.PhysicalProfile, Is.SameAs(profile));
            Assert.That(stone.GetComponent<OntologyWaterOccupancySensor>(), Is.Not.Null);
            Assert.That(originalCollider.enabled, Is.False);
            Assert.That(stone.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(stone.GetComponent<Rigidbody>(), Is.Not.Null);

            ontology.ConfigureOntologyData(
                stone.name,
                new[] { OntologyConcepts.FloatableObject },
                new OntologyFactEntry[0]);
            OntologySemanticAdapterSynchronizer.SynchronizePhysical(stone, bootstrap);
            Assert.That(adapter.PhysicalProfile, Is.Null);
            Assert.That(originalCollider.enabled, Is.True);
            Assert.That(originalCollider.convex, Is.False);

            yield return null;
            Assert.That(stone.GetComponent<BoxCollider>(), Is.Null);
            Assert.That(stone.GetComponent<Rigidbody>(), Is.Null);

            Object.Destroy(stone);
            Object.Destroy(mesh);
            Object.Destroy(bootstrapObject);
            Object.Destroy(database);
            Object.Destroy(profile);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AttachmentProfileFactResolvesCarryPresentationWithoutObjectNameRules()
        {
            var profile = ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            profile.profileId = "BackCarry";
            profile.kind = OntologyAttachmentKind.Carryable;
            profile.slotId = "Back";
            profile.relationPredicate = OntologyPredicates.CarriedBy;
            profile.relationDirection =
                OntologyAttachmentRelationDirection.ItemToActor;
            profile.actorAnchorPath = "BackAnchor";
            var database =
                ScriptableObject.CreateInstance<OntologyAttachmentProfileDatabase>();
            database.Replace(new[] { profile });

            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "attachmentProfileDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, database);

            var actor = new GameObject("AnyActor");
            var actorOntology = actor.AddComponent<OntologyObject>();
            actorOntology.ConfigureOntologyData(
                "Actor_01",
                new[] { OntologyConcepts.Actor },
                new OntologyFactEntry[0]);
            var anchor = new GameObject("BackAnchor");
            anchor.transform.SetParent(actor.transform, false);

            var carried = GameObject.CreatePrimitive(PrimitiveType.Cube);
            carried.name = "ObjectWithArbitraryName";
            carried.AddComponent<Rigidbody>();
            var carriedOntology = carried.AddComponent<OntologyObject>();
            carriedOntology.ConfigureOntologyData(
                "Entity_42",
                new[] { OntologyConcepts.Carryable },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.AttachmentProfile,
                        obj = profile.profileId
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.HasSlot,
                        obj = profile.slotId
                    }
                });

            bootstrap.ResetWorld(logReport: false);
            OntologySemanticAdapterSynchronizer.SynchronizeAttachment(
                carried,
                bootstrap,
                actorOntology);
            // Let the proximity observation settle first. It legitimately runs a
            // simulation and rebuilds derived relations.
            yield return null;
            bootstrap.World.AddFact(
                carriedOntology.EntityId,
                OntologyPredicates.CarriedBy,
                actorOntology.EntityId);
            var adapter = carried.GetComponent<OntologyAttachmentAdapter>();
            Assert.That(
                bootstrap.World.HasFact(
                    carriedOntology.EntityId,
                    OntologyPredicates.AttachmentProfile,
                    profile.profileId),
                Is.True);
            Assert.That(
                bootstrap.World.HasFact(
                    carriedOntology.EntityId,
                    OntologyPredicates.CarriedBy,
                    actorOntology.EntityId),
                Is.True);
            adapter.SynchronizePresentation();
            yield return null;

            Assert.That(adapter, Is.Not.Null);
            Assert.That(adapter.AttachmentProfile, Is.SameAs(profile));
            Assert.That(adapter.IsAttached, Is.True);
            Assert.That(carried.transform.parent, Is.EqualTo(anchor.transform));

            carriedOntology.ConfigureOntologyData(
                carriedOntology.EntityId,
                new[] { OntologyConcepts.Carryable },
                new OntologyFactEntry[0]);
            OntologySemanticAdapterSynchronizer.SynchronizeAttachment(
                carried,
                bootstrap,
                actorOntology);
            Assert.That(adapter.IsAttached, Is.False);
            Assert.That(carried.transform.parent, Is.Null);

            Object.Destroy(carried);
            Object.Destroy(actor);
            Object.Destroy(bootstrapObject);
            Object.Destroy(database);
            Object.Destroy(profile);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AnchoredProfileUsesPhysicalBodyWithoutBuoyancyComponents()
        {
            var profile = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            profile.profileId = "StaticAnchored";
            profile.mobilityMode = OntologyPhysicalMobilityMode.Anchored;
            profile.supportsBuoyancy = false;
            var database = ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            database.Replace(new[] { profile });

            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalProfileDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, database);

            var landmark = GameObject.CreatePrimitive(PrimitiveType.Cube);
            landmark.name = "Landmark";
            var ontology = landmark.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                "Landmark",
                new[] { "Landmark" },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = profile.profileId
                    }
                });

            OntologySemanticAdapterSynchronizer.SynchronizePhysical(
                landmark,
                bootstrap);

            var bodyAdapter = landmark.GetComponent<OntologyPhysicalBodyAdapter>();
            Assert.That(bodyAdapter, Is.Not.Null);
            Assert.That(bodyAdapter.PhysicalProfile, Is.SameAs(profile));
            Assert.That(bodyAdapter.TargetBody.isKinematic, Is.True);
            Assert.That(bodyAdapter.TargetBody.useGravity, Is.False);
            Assert.That(
                landmark.GetComponent<OntologyBuoyancyAdapter>(),
                Is.Null);
            Assert.That(
                landmark.GetComponent<OntologyWaterOccupancySensor>(),
                Is.Null);

            Object.Destroy(landmark);
            Object.Destroy(bootstrapObject);
            Object.Destroy(database);
            Object.Destroy(profile);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RestoredObjectResolvesPhysicalProfileFromSavedOntologyFact()
        {
            var profile = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            profile.profileId = "HeavyBuoyant";
            profile.supportsBuoyancy = true;
            var database = ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            database.Replace(new[] { profile });

            var prefab = new GameObject("StonePrefab");
            var catalog = ScriptableObject.CreateInstance<OntologyPlaceableCatalog>();
            catalog.ReplaceDefinitions(new[]
            {
                new OntologyPlaceableDefinition
                {
                    definitionId = "Stone",
                    prefab = prefab
                }
            });
            var root = new GameObject("PlacedObjects").transform;
            var host = new GameObject("PlacementHost");
            var bootstrap = host.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalProfileDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, database);
            var placement = host.AddComponent<OntologyRuntimeObjectPlacementController>();
            placement.Configure(catalog, host.transform, null, bootstrap, root);

            var record = new OntologyPlacedObjectRecord
            {
                instanceName = "Stone_001",
                definitionId = "Stone",
                concepts = new List<string> { OntologyConcepts.FloatableObject },
                facts = new List<OntologyFactRecord>
                {
                    new OntologyFactRecord
                    {
                        subject = "Stone_001",
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = profile.profileId
                    }
                }
            };

            Assert.That(
                placement.RestorePlacedObjects(new[] { record }),
                Is.EqualTo(1));
            var restored = root.GetChild(0).gameObject;
            Assert.That(
                restored.GetComponent<OntologyBuoyancyAdapter>()?.PhysicalProfile,
                Is.SameAs(profile));
            Assert.That(
                restored.GetComponent<OntologyObject>().Facts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value.predicate == OntologyPredicates.PhysicalProfile &&
                    value.obj == profile.profileId));

            Object.Destroy(root.gameObject);
            Object.Destroy(host);
            Object.Destroy(prefab);
            Object.Destroy(catalog);
            Object.Destroy(database);
            Object.Destroy(profile);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BoundsColliderProfilePreventsDynamicStoneFromFallingThroughGround()
        {
            var profile = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            profile.profileId = "HeavyBuoyant";
            profile.supportsBuoyancy = true;
            profile.dynamicColliderMode = OntologyDynamicColliderMode.BoundsBox;
            var database = ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            database.Replace(new[] { profile });

            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalProfileDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, database);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.localScale = new Vector3(10f, 1f, 10f);
            var stone = new GameObject("DynamicStone");
            stone.transform.position = new Vector3(0f, 3f, 0f);
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(0.5f, 1f, 0.5f),
                    new Vector3(-0.5f, 1f, 0.5f)
                },
                triangles = new[]
                {
                    0, 2, 1,
                    0, 3, 2
                }
            };
            mesh.RecalculateBounds();
            var originalCollider = stone.AddComponent<MeshCollider>();
            originalCollider.sharedMesh = mesh;
            originalCollider.convex = false;
            var ontology = stone.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                stone.name,
                new[] { OntologyConcepts.FloatableObject },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = profile.profileId
                    }
                });

            OntologySemanticAdapterSynchronizer.SynchronizePhysical(stone, bootstrap);
            Physics.SyncTransforms();
            for (var index = 0; index < 60; index++)
                yield return new WaitForFixedUpdate();

            Assert.That(stone.transform.position.y, Is.GreaterThan(0.45f));
            Assert.That(stone.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(originalCollider.enabled, Is.False);

            Object.Destroy(stone);
            Object.Destroy(mesh);
            Object.Destroy(floor);
            Object.Destroy(bootstrapObject);
            Object.Destroy(database);
            Object.Destroy(profile);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BuoyancyCompensatesGravityAndSettlesAtConfiguredWaterline()
        {
            var profile = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            profile.profileId = "WaterlineTest";
            profile.supportsBuoyancy = true;
            profile.dynamicColliderMode = OntologyDynamicColliderMode.KeepExisting;
            profile.buoyancyStrength = 10f;
            profile.submergedDamping = 4f;
            profile.submergedFraction = 0.5f;
            profile.surfaceOffset = -0.25f;
            var database = ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            database.Replace(new[] { profile });

            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalProfileDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, database);

            var water = new GameObject("WaterRegion");
            var waterOntology = water.AddComponent<OntologyObject>();
            waterOntology.ConfigureOntologyData(
                "WaterRegion",
                new[] { OntologyConcepts.WaterRegion },
                new OntologyFactEntry[0]);
            var waterCollider = water.AddComponent<BoxCollider>();
            waterCollider.isTrigger = true;
            waterCollider.center = new Vector3(0f, -1f, 0f);
            waterCollider.size = new Vector3(10f, 2f, 10f);
            var waterVolume = water.AddComponent<OntologyWaterRegionVolume>();

            var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.name = "FloatingStone";
            stone.transform.position = new Vector3(0f, -2.25f, 0f);
            stone.GetComponent<BoxCollider>().center =
                new Vector3(0f, 1f, 0f);
            var stoneOntology = stone.AddComponent<OntologyObject>();
            stoneOntology.ConfigureOntologyData(
                stone.name,
                new[] { OntologyConcepts.FloatableObject },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = profile.profileId
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalState,
                        obj = OntologyObjects.Floating
                    }
                });

            bootstrap.ResetWorld(logReport: false);
            OntologySemanticAdapterSynchronizer.SynchronizePhysical(stone, bootstrap);
            stone.GetComponent<OntologyWaterOccupancySensor>().EnterVolume(waterVolume);
            Physics.SyncTransforms();

            for (var index = 0; index < 150; index++)
                yield return new WaitForFixedUpdate();

            Assert.That(
                stone.transform.position.y,
                Is.EqualTo(-1.25f).Within(0.15f),
                "Waterline must be calculated from physical bounds, not prefab pivot.");
            Assert.That(
                stone.GetComponent<OntologyBuoyancyAdapter>().IsBuoyancyActive,
                Is.True);

            Object.Destroy(stone);
            Object.Destroy(water);
            Object.Destroy(bootstrapObject);
            Object.Destroy(database);
            Object.Destroy(profile);
            yield return null;
        }

        private static IEnumerable<OntologyCollisionLayerBinding>
            CreateCollisionLayerBindings()
        {
            return new[]
            {
                new OntologyCollisionLayerBinding
                {
                    role = OntologyCollisionRole.DynamicProp,
                    layerName = "DynamicProp",
                    collidesWith = new List<OntologyCollisionRole>
                    {
                        OntologyCollisionRole.DynamicProp,
                        OntologyCollisionRole.WalkableSupport,
                        OntologyCollisionRole.ActorBody,
                        OntologyCollisionRole.WaterVolume
                    }
                },
                new OntologyCollisionLayerBinding
                {
                    role = OntologyCollisionRole.WalkableSupport,
                    layerName = "WorldStatic",
                    collidesWith = new List<OntologyCollisionRole>
                    {
                        OntologyCollisionRole.DynamicProp,
                        OntologyCollisionRole.ActorBody
                    }
                },
                new OntologyCollisionLayerBinding
                {
                    role = OntologyCollisionRole.ActorBody,
                    layerName = "ActorBody",
                    collidesWith = new List<OntologyCollisionRole>
                    {
                        OntologyCollisionRole.DynamicProp,
                        OntologyCollisionRole.WalkableSupport,
                        OntologyCollisionRole.InteractionTrigger,
                        OntologyCollisionRole.WaterVolume
                    }
                },
                new OntologyCollisionLayerBinding
                {
                    role = OntologyCollisionRole.InteractionTrigger,
                    layerName = "InteractionTrigger",
                    collidesWith = new List<OntologyCollisionRole>
                    {
                        OntologyCollisionRole.ActorBody
                    }
                },
                new OntologyCollisionLayerBinding
                {
                    role = OntologyCollisionRole.WaterVolume,
                    layerName = "WaterVolume",
                    collidesWith = new List<OntologyCollisionRole>
                    {
                        OntologyCollisionRole.DynamicProp,
                        OntologyCollisionRole.ActorBody
                    }
                }
            };
        }
    }
}
