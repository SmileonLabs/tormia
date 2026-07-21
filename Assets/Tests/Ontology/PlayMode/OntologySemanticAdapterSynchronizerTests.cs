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
    }
}
