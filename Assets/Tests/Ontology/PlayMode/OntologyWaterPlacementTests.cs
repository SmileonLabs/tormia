using System.Collections;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWaterPlacementTests
    {
        [UnityTest]
        public IEnumerator PlacementSurfaceIgnoresActorCollider()
        {
            var actor = new GameObject("PlacementActor");
            actor.transform.position = new Vector3(0f, 1f, 0f);
            var actorCollider = actor.AddComponent<BoxCollider>();
            actorCollider.size = new Vector3(1f, 2f, 1f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "PlacementGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(10f, 1f, 10f);

            var controllerObject = new GameObject("PlacementController");
            var controller =
                controllerObject.AddComponent<OntologyRuntimeObjectPlacementController>();
            controller.Configure(null, actor.transform, null, null, null);

            var movingObject = new GameObject("MovingPlaceable");
            var movingInstance =
                movingObject.AddComponent<OntologyPlaceableInstance>();
            movingInstance.Configure("MovingPlaceable");

            Physics.SyncTransforms();
            Assert.That(
                controller.TryResolveMoveSurface(
                    movingInstance,
                    new Ray(new Vector3(0f, 5f, 0f), Vector3.down),
                    out var point),
                Is.True);
            Assert.That(
                point.y,
                Is.EqualTo(0f).Within(0.001f),
                "The actor collider must not be treated as a world placement surface.");

            Object.Destroy(movingObject);
            Object.Destroy(controllerObject);
            Object.Destroy(ground);
            Object.Destroy(actor);
            yield return null;
        }

        [UnityTest]
        public IEnumerator UniqueEntityIdIncludesWearableMovedUnderActorHierarchy()
        {
            var actor = new GameObject("Actor");
            var wornTube = new GameObject("Inflatable_Ring_A_Simple_001");
            wornTube.transform.SetParent(actor.transform, false);
            var wornOntology = wornTube.AddComponent<OntologyObject>();
            wornOntology.ConfigureOntologyData(
                "Inflatable_Ring_A_Simple_001",
                new[] { OntologyConcepts.Wearable },
                new OntologyFactEntry[0]);

            var controllerObject = new GameObject("PlacementController");
            var controller =
                controllerObject.AddComponent<OntologyRuntimeObjectPlacementController>();

            Assert.That(
                controller.GenerateUniqueInstanceName(
                    "Inflatable_Ring_A_Simple"),
                Is.EqualTo("Inflatable_Ring_A_Simple_002"),
                "An equipped object remains a live ontology entity even after its hierarchy parent changes.");

            Object.Destroy(controllerObject);
            Object.Destroy(actor);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RegistrySeparatesFactContributorsFromPlacedIdentityOwners()
        {
            var presentation = new GameObject("PlayerPresentation");
            presentation.AddComponent<BoxCollider>();
            var presentationOntology = presentation.AddComponent<OntologyObject>();
            presentationOntology.ConfigureOntologyData(
                "SharedEntity",
                new[] { OntologyConcepts.Actor },
                new OntologyFactEntry[0]);

            var dataContributor = new GameObject("PlayerFacts");
            var dataOntology = dataContributor.AddComponent<OntologyObject>();
            dataOntology.ConfigureOntologyData(
                "SharedEntity",
                new[] { "Agent" },
                new OntologyFactEntry[0]);

            var registry = new OntologyEntityRegistry();
            registry.Rebuild(new[] { dataOntology, presentationOntology });
            Assert.That(registry.DuplicateEntityIds, Is.Empty);
            Assert.That(registry.TryGet("SharedEntity", out var resolved), Is.True);
            Assert.That(resolved, Is.SameAs(presentationOntology));

            var placedA = new GameObject("PlacedA");
            placedA.AddComponent<OntologyPlaceableInstance>();
            var placedOntologyA = placedA.AddComponent<OntologyObject>();
            placedOntologyA.ConfigureOntologyData(
                "PlacedEntity",
                new string[0],
                new OntologyFactEntry[0]);

            var placedB = new GameObject("PlacedB");
            placedB.AddComponent<OntologyPlaceableInstance>();
            var placedOntologyB = placedB.AddComponent<OntologyObject>();
            placedOntologyB.ConfigureOntologyData(
                "PlacedEntity",
                new string[0],
                new OntologyFactEntry[0]);

            registry.Rebuild(new[] { placedOntologyA, placedOntologyB });
            Assert.That(
                registry.DuplicateEntityIds,
                Does.Contain("PlacedEntity"));

            Object.Destroy(placedB);
            Object.Destroy(placedA);
            Object.Destroy(dataContributor);
            Object.Destroy(presentation);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BootstrapAggregatesAndRetractsMultipleFactContributors()
        {
            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();

            var presentation = new GameObject("SharedPresentation");
            var presentationOntology = presentation.AddComponent<OntologyObject>();
            presentationOntology.ConfigureOntologyData(
                "SharedEntity",
                new[] { "Actor" },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = "state",
                        obj = "Ready"
                    }
                });

            var data = new GameObject("SharedFacts");
            var dataOntology = data.AddComponent<OntologyObject>();
            dataOntology.ConfigureOntologyData(
                "SharedEntity",
                new[] { "Agent" },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = "mood",
                        obj = "Calm"
                    }
                });

            bootstrap.ResetWorld(false);
            Assert.That(
                bootstrap.World.HasFact("SharedEntity", "state", "Ready"),
                Is.True);
            Assert.That(
                bootstrap.World.HasFact("SharedEntity", "mood", "Calm"),
                Is.True);

            presentationOntology.ConfigureOntologyData(
                "SharedEntity",
                new[] { "Actor" },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = "state",
                        obj = "Busy"
                    }
                });
            bootstrap.SynchronizeSceneObjects(runSimulation: false);

            Assert.That(
                bootstrap.World.HasFact("SharedEntity", "state", "Ready"),
                Is.False,
                "Removed facts from any contributor must be retracted.");
            Assert.That(
                bootstrap.World.HasFact("SharedEntity", "state", "Busy"),
                Is.True);
            Assert.That(
                bootstrap.World.HasFact("SharedEntity", "mood", "Calm"),
                Is.True,
                "Facts from the other contributor must remain.");

            Object.Destroy(data);
            Object.Destroy(presentation);
            Object.Destroy(bootstrapObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WorldEditorSelectsOnlyUnattachedPlacedObjects()
        {
            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();

            var placedObject = new GameObject("EditableTube");
            var placed = placedObject.AddComponent<OntologyPlaceableInstance>();
            var ontology = placedObject.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                "EditableTube_001",
                new[] { OntologyConcepts.Wearable },
                new OntologyFactEntry[0]);

            var editorObject = new GameObject("WorldEditor");
            var editor =
                editorObject.AddComponent<OntologyRuntimeWorldEditorController>();
            editor.Configure(null, null, bootstrap);
            bootstrap.ResetWorld(false);

            Assert.That(
                editor.IsWorldEditableCandidate(placed),
                Is.True,
                "A placed object without equipped_by must remain world-editable.");

            bootstrap.World.AddFact(
                ontology.EntityId,
                OntologyPredicates.EquippedBy,
                "Player");
            Assert.That(
                editor.IsWorldEditableCandidate(placed),
                Is.False,
                "An equipped object belongs to its actor and must leave world editing.");

            bootstrap.World.RemoveFact(
                ontology.EntityId,
                OntologyPredicates.EquippedBy,
                "Player");
            Assert.That(editor.IsWorldEditableCandidate(placed), Is.True);

            Object.Destroy(editorObject);
            Object.Destroy(placedObject);
            Object.Destroy(bootstrapObject);
            yield return null;
        }

        [Test]
        public void WorldEditPhysicsOverrideRestoresRigidbodyBaseline()
        {
            var placeable = new GameObject("PhysicsPlaceable");
            try
            {
                var body = placeable.AddComponent<Rigidbody>();
                body.isKinematic = false;
                body.useGravity = true;
                body.detectCollisions = true;
                placeable.AddComponent<BoxCollider>();
                var coordinator =
                    placeable.AddComponent<OntologyPhysicsPresentationCoordinator>();
                coordinator.CaptureWorldBaseline();

                coordinator.SetWorldEditOverride(true);
                Assert.That(body.isKinematic, Is.True);
                Assert.That(body.useGravity, Is.False);
                Assert.That(body.detectCollisions, Is.True);

                coordinator.SetWorldEditOverride(false);
                Assert.That(body.isKinematic, Is.False);
                Assert.That(body.useGravity, Is.True);
                Assert.That(body.detectCollisions, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(placeable);
            }
        }

        [Test]
        public void GroundPreviewUsesRendererBoundsWhenPreviewColliderIsDisabled()
        {
            var placeable = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                placeable.transform.position = Vector3.zero;
                placeable.transform.localScale = new Vector3(2f, 0.5f, 2f);
                placeable.GetComponent<Collider>().enabled = false;
                Physics.SyncTransforms();

                var supported = OntologyRuntimeObjectPlacementController
                    .CalculateSurfaceSupportedPosition(
                        placeable,
                        new Vector3(4f, 3f, 8f),
                        Vector3.up);

                Assert.That(supported.x, Is.EqualTo(4f).Within(0.001f));
                Assert.That(supported.y, Is.EqualTo(3.25f).Within(0.001f));
                Assert.That(supported.z, Is.EqualTo(8f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(placeable);
            }
        }

        [UnityTest]
        public IEnumerator BuoyantOntologyDefinitionUsesWaterVolumeSurfaceForPlacement()
        {
            var testScene = SceneManager.CreateScene("OntologyWaterPlacementTest");
            SceneManager.SetActiveScene(testScene);

            var actor = new GameObject("Actor");
            actor.transform.position = Vector3.zero;
            actor.transform.forward = Vector3.forward;

            var water = new GameObject("WaterVolume");
            water.transform.position = new Vector3(0f, -1f, 2f);
            var waterCollider = water.AddComponent<BoxCollider>();
            waterCollider.isTrigger = true;
            waterCollider.size = new Vector3(10f, 2f, 10f);
            water.AddComponent<OntologyWaterRegionVolume>();

            var sourcePrefab = new GameObject("FloatablePrefab");
            var template = ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
            template.concepts = new[] { OntologyConcepts.FloatableObject };
            var physicalProfile = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            physicalProfile.profileId = "TestFloatable";
            physicalProfile.supportsBuoyancy = true;
            var profileDatabase = ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            profileDatabase.Replace(new[] { physicalProfile });
            var ruleDatabase = ScriptableObject.CreateInstance<OntologyRuleDatabase>();
            var placementRule = ruleDatabase.CreateDefinition("BuoyantObjectWaterPlacement");
            placementRule.conditions.Add(OntologyCondition.HasConcept(
                "?object",
                OntologyConcepts.FloatableObject));
            placementRule.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.PhysicalProfile,
                "?profile"));
            placementRule.conditions.Add(OntologyCondition.Fact(
                "?profile",
                OntologyPredicates.SupportsBehavior,
                OntologyObjects.Buoyancy));
            placementRule.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.HasRuleBlock,
                "BuoyantWhenInWater"));
            placementRule.effects.Add(OntologyEffect.AddFact(
                "?object",
                OntologyPredicates.CanBePlacedOn,
                OntologyObjects.Water));

            var definition = new OntologyPlaceableDefinition
            {
                definitionId = "TestFloatable",
                prefab = sourcePrefab,
                ontologyTemplate = template,
                physicalProfile = physicalProfile,
                placementPolicy = new OntologyPlacementPolicy
                {
                    requiredSurface = OntologyPlacementSurfaceKind.AnyCollider,
                    maximumDistanceFromActor = 5f
                }
            };
            template.facts = new[]
            {
                new OntologyFactEntry
                {
                    predicate = OntologyPredicates.PhysicalProfile,
                    obj = physicalProfile.profileId
                }
            };
            definition.defaultRuleBlocks.Add(new OntologyRuleBlockBinding
            {
                ruleId = "BuoyantWhenInWater",
                bindingVariable = "?object"
            });

            var controllerObject = new GameObject("PlacementController");
            var bootstrap = controllerObject.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalProfileDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, profileDatabase);
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "ruleDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, ruleDatabase);
            var controller = controllerObject.AddComponent<OntologyRuntimeObjectPlacementController>();
            controller.Configure(null, actor.transform, null, bootstrap, null);

            Physics.SyncTransforms();
            Assert.That(definition.physicalProfile.supportsBuoyancy, Is.True);
            Assert.That(controller.BeginPlacement(definition), Is.True);
            Assert.That(controller.HasValidPreview, Is.True, controller.Status);
            Assert.That(controller.PreviewPosition.y, Is.EqualTo(0f).Within(0.001f));
            Assert.That(controller.PreviewPosition.z, Is.EqualTo(2f).Within(0.001f));

            controller.CancelPlacement();
            var liveObject = new GameObject("LiveFloatable");
            var liveInstance = liveObject.AddComponent<OntologyPlaceableInstance>();
            liveInstance.Configure(definition.definitionId);
            var liveOntology = liveObject.AddComponent<OntologyObject>();
            liveOntology.ConfigureOntologyData(
                liveObject.name,
                new[] { OntologyConcepts.FloatableObject },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = physicalProfile.profileId
                    }
                });
            var liveRules = liveObject.AddComponent<OntologyRuleBlockAssignment>();
            liveRules.Add("BuoyantWhenInWater", "?object");

            Physics.SyncTransforms();
            Assert.That(
                controller.TryResolveMoveSurface(
                    liveInstance,
                    new Ray(new Vector3(0f, 5f, 2f), Vector3.down),
                    out var movePoint),
                Is.True);
            Assert.That(movePoint.y, Is.EqualTo(0f).Within(0.001f));

            Object.Destroy(controllerObject);
            Object.Destroy(liveObject);
            Object.Destroy(sourcePrefab);
            Object.Destroy(ruleDatabase);
            Object.Destroy(profileDatabase);
            Object.Destroy(physicalProfile);
            Object.Destroy(template);
            Object.Destroy(water);
            Object.Destroy(actor);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }

        [UnityTest]
        public IEnumerator NonBuoyantPhysicalObjectCanBePlacedOnWaterAndFalls()
        {
            var testScene = SceneManager.CreateScene(
                "OntologyNonBuoyantWaterPlacementTest");
            SceneManager.SetActiveScene(testScene);

            var actor = new GameObject("Actor");
            actor.transform.position = Vector3.zero;
            actor.transform.forward = Vector3.forward;

            var water = new GameObject("WaterVolume");
            water.transform.position = new Vector3(0f, -1f, 2f);
            var waterCollider = water.AddComponent<BoxCollider>();
            waterCollider.isTrigger = true;
            waterCollider.size = new Vector3(10f, 2f, 10f);
            water.AddComponent<OntologyWaterRegionVolume>();

            var sourcePrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            sourcePrefab.name = "HeavyPrefab";
            sourcePrefab.transform.position = new Vector3(100f, 100f, 100f);

            var template =
                ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
            template.concepts = new[] { "PhysicalObject" };
            template.facts = new OntologyFactEntry[0];

            var heavyProfile =
                ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            heavyProfile.profileId = "TestHeavy";
            heavyProfile.mass = 5f;
            heavyProfile.supportsBuoyancy = false;
            heavyProfile.dynamicColliderMode =
                OntologyDynamicColliderMode.BoundsBox;
            var profileDatabase =
                ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            profileDatabase.Replace(new[] { heavyProfile });

            var definition = new OntologyPlaceableDefinition
            {
                definitionId = "TestHeavy",
                prefab = sourcePrefab,
                ontologyTemplate = template,
                physicalProfile = heavyProfile,
                placementPolicy = new OntologyPlacementPolicy
                {
                    requiredSurface =
                        OntologyPlacementSurfaceKind.AnyCollider,
                    maximumDistanceFromActor = 5f
                }
            };
            var catalog =
                ScriptableObject.CreateInstance<OntologyPlaceableCatalog>();
            catalog.ReplaceDefinitions(new[] { definition });

            var controllerObject = new GameObject("PlacementController");
            var bootstrap =
                controllerObject.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalProfileDatabase",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bootstrap, profileDatabase);
            var controller =
                controllerObject.AddComponent<OntologyRuntimeObjectPlacementController>();
            controller.Configure(
                catalog,
                actor.transform,
                null,
                bootstrap,
                null);

            Physics.SyncTransforms();
            Assert.That(controller.BeginPlacement(definition), Is.True);
            Assert.That(controller.HasValidPreview, Is.True, controller.Status);
            Assert.That(
                controller.PreviewPosition.y,
                Is.EqualTo(0.5f).Within(0.01f),
                "The object must initially sit on top of the water surface.");

            typeof(OntologyRuntimeObjectPlacementController)
                .GetMethod(
                    "ConfirmPlacement",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(controller, null);

            var placed = GameObject.Find("TestHeavy_001");
            Assert.That(placed, Is.Not.Null);
            var ontology = placed.GetComponent<OntologyObject>();
            Assert.That(
                ontology.Facts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value != null &&
                    value.predicate == OntologyPredicates.PhysicalProfile &&
                    value.obj == heavyProfile.profileId));
            var body = placed.GetComponent<Rigidbody>();
            Assert.That(body, Is.Not.Null);
            Assert.That(body.useGravity, Is.True);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(
                placed.GetComponent<OntologyPhysicalBodyAdapter>()
                    .PhysicalProfile,
                Is.SameAs(heavyProfile));
            Assert.That(
                placed.GetComponent<OntologyBuoyancyAdapter>(),
                Is.Null,
                "A non-buoyant object must not receive a buoyancy presenter.");
            Assert.That(
                placed.GetComponent<OntologyWaterOccupancySensor>(),
                Is.Null,
                "A non-buoyant object does not need water occupancy sensing.");
            Assert.That(
                placed.GetComponent<OntologySupportObservationSensor>(),
                Is.Not.Null);

            var initialHeight = placed.transform.position.y;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(
                placed.transform.position.y,
                Is.LessThan(initialHeight),
                "Without buoyancy, gravity must make the object sink.");

            Object.Destroy(controllerObject);
            Object.Destroy(sourcePrefab);
            Object.Destroy(catalog);
            Object.Destroy(profileDatabase);
            Object.Destroy(heavyProfile);
            Object.Destroy(template);
            Object.Destroy(water);
            Object.Destroy(actor);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }
    }
}
