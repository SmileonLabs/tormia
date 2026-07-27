using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyAttachmentAdapterTests
    {
        [UnityTest]
        public IEnumerator ProximityMeasurementSupportsColliderSurfaceAndInteractionAnchor()
        {
            var actor = new GameObject("DistanceActor");
            actor.transform.position = new Vector3(5f, 0f, 0f);
            var actorOntology = actor.AddComponent<OntologyObject>();
            actorOntology.ConfigureOntologyData(
                "DistanceActor",
                new[] { OntologyConcepts.Actor },
                new OntologyFactEntry[0]);

            var observed = new GameObject("LargeObservedObject");
            var observedOntology = observed.AddComponent<OntologyObject>();
            observedOntology.ConfigureOntologyData(
                "LargeObservedObject",
                new string[0],
                new OntologyFactEntry[0]);
            var collider = observed.AddComponent<BoxCollider>();
            collider.size = new Vector3(10f, 1f, 1f);
            var anchor = new GameObject("InteractionAnchor");
            anchor.transform.SetParent(observed.transform, false);
            anchor.transform.localPosition = new Vector3(4f, 0f, 0f);

            var sensor = observed.AddComponent<OntologyProximityObservationSensor>();
            sensor.Configure(
                null,
                actorOntology,
                1f,
                0.1f,
                OntologyProximityMeasurementMode.ColliderSurface);
            Physics.SyncTransforms();
            Assert.That(sensor.MeasureDistance(), Is.EqualTo(0f).Within(0.001f));

            sensor.Configure(
                null,
                actorOntology,
                1f,
                0.1f,
                OntologyProximityMeasurementMode.InteractionAnchor,
                "InteractionAnchor");
            Assert.That(sensor.MeasureDistance(), Is.EqualTo(1f).Within(0.001f));

            Object.Destroy(observed);
            Object.Destroy(actor);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NearObservationAndEquippedRelationControlWearableAttachment()
        {
            var testScene = SceneManager.CreateScene("OntologyAttachmentAdapterTest");
            SceneManager.SetActiveScene(testScene);

            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            var actionCandidates =
                ScriptableObject.CreateInstance<OntologyActionCandidateDatabase>();
            var actionEffects =
                ScriptableObject.CreateInstance<OntologyActionEffectDatabase>();
            SetPrivateField(
                actionCandidates,
                "definitions",
                new List<OntologyActionCandidateDefinition>
                {
                    new()
                    {
                        actionVerb = OntologyActions.UnequipWearable,
                        targetPattern = "?target",
                        conditions = new List<OntologyCondition>
                        {
                            OntologyCondition.Fact(
                                "?target",
                                OntologyPredicates.EquippedBy,
                                "?actor")
                        }
                    }
                });
            SetPrivateField(
                actionEffects,
                "definitions",
                new List<OntologyActionEffectDefinition>
                {
                    new()
                    {
                        actionVerb = OntologyActions.UnequipWearable,
                        subjectPattern = "?actor",
                        objectPattern = "?target",
                        conditions = new List<OntologyCondition>
                        {
                            OntologyCondition.Fact(
                                "?target",
                                OntologyPredicates.EquippedBy,
                                "?actor"),
                            OntologyCondition.Fact(
                                "?slot",
                                OntologyPredicates.SlotOwner,
                                "?actor"),
                            OntologyCondition.Fact(
                                "?slot",
                                OntologyPredicates.EquippedItem,
                                "?target")
                        },
                        effects = new List<OntologyEffect>
                        {
                            OntologyEffect.RemoveFact(
                                "?slot",
                                OntologyPredicates.EquippedItem,
                                "?target"),
                            OntologyEffect.RemoveFact(
                                "?target",
                                OntologyPredicates.EquippedBy,
                                "?actor"),
                            OntologyEffect.RemoveFact(
                                "?actor",
                                OntologyPredicates.InteractionIntent,
                                "?target")
                        }
                    }
                });
            SetPrivateField(
                bootstrap,
                "actionCandidateDatabase",
                actionCandidates);
            SetPrivateField(
                bootstrap,
                "actionEffectDatabase",
                actionEffects);

            var actor = new GameObject("TestActor");
            var actorOntology = actor.AddComponent<OntologyObject>();
            actorOntology.ConfigureOntologyData(
                "Player",
                new[] { "Actor" },
                new OntologyFactEntry[0]);
            var waistAnchor = new GameObject("WaistAnchor");
            waistAnchor.transform.SetParent(actor.transform, false);

            var item = new GameObject("TestWearable");
            item.transform.position = new Vector3(0.5f, 0f, 0f);
            var itemCollider = item.AddComponent<BoxCollider>();
            var itemBody = item.AddComponent<Rigidbody>();
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "OffsetVisual";
            visual.transform.SetParent(item.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            var itemOntology = item.AddComponent<OntologyObject>();
            itemOntology.ConfigureOntologyData(
                "InflatableRing",
                new[] { OntologyConcepts.Wearable },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.AttachmentProfile,
                        obj = "TestWaist"
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PickupBehavior,
                        obj = OntologyObjects.SelectThenEquip
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.HasSlot,
                        obj = "Waist"
                    }
                });

            var profile = ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            profile.profileId = "TestWaist";
            profile.kind = OntologyAttachmentKind.Wearable;
            profile.actorAnchorPath = "WaistAnchor";
            profile.autoEquipDistance = 1f;
            profile.proximityExitPadding = 0.1f;
            profile.disableWorldPhysicsWhileAttached = true;
            profile.disableWorldCollidersWhileAttached = true;
            profile.detachInteractionLocalCenter = new Vector3(0f, 0.75f, 0f);
            profile.detachInteractionLocalSize = Vector3.one;

            var proximity = item.AddComponent<OntologyProximityObservationSensor>();
            proximity.Configure(bootstrap, actorOntology, 1f, 0.1f);
            var attachment = item.AddComponent<OntologyAttachmentAdapter>();
            attachment.Configure(profile, bootstrap, actorOntology);

            bootstrap.ResetWorld(logReport: false);
            yield return null;

            Assert.That(
                bootstrap.World.HasFact(
                    "Player",
                    OntologyPredicates.Near,
                    "InflatableRing"),
                Is.True);

            bootstrap.ResetWorld(logReport: false);
            yield return null;
            Assert.That(
                bootstrap.World.HasFact(
                    "Player",
                    OntologyPredicates.Near,
                    "InflatableRing"),
                Is.True,
                "The sensor must restore its observation after the ontology world is rebuilt.");

            bootstrap.World.AddFact(
                "InflatableRing",
                OntologyPredicates.EquippedBy,
                "Player");
            bootstrap.World.AddFact(
                "Player",
                OntologyPredicates.InteractionIntent,
                "InflatableRing");
            var equipmentSlotId =
                OntologyAttachmentAdapter.BuildEquipmentSlotEntityId(
                    "Player",
                    "Waist");
            bootstrap.World.AddFact(
                equipmentSlotId,
                OntologyPredicates.SlotOwner,
                "Player");
            bootstrap.World.AddFact(
                equipmentSlotId,
                OntologyPredicates.SlotId,
                "Waist");
            bootstrap.World.AddFact(
                equipmentSlotId,
                OntologyPredicates.EquippedItem,
                "InflatableRing");
            yield return null;

            Assert.That(attachment.IsAttached, Is.True);
            Assert.That(item.transform.parent, Is.EqualTo(waistAnchor.transform));
            Assert.That(itemBody.isKinematic, Is.True);
            Assert.That(itemCollider.enabled, Is.False);
            var expectedCenter = visual.transform.TransformPoint(
                visual.GetComponent<MeshFilter>().sharedMesh.bounds.center);
            var clickRay = new Ray(
                expectedCenter + Vector3.back * 5f,
                Vector3.forward);
            Assert.That(
                attachment.RaycastPresentation(clickRay, 10f, out _),
                Is.True,
                "An attached visual must be selectable without adding a runtime collider.");
            Assert.That(
                item.GetComponents<BoxCollider>().Length,
                Is.EqualTo(1),
                "Attachment must not add a temporary click collider.");

            var placedObject = new GameObject("PlacedLandmark");
            var placedOntology = placedObject.AddComponent<OntologyObject>();
            placedOntology.ConfigureOntologyData(
                "PlacedLandmark",
                new[] { "Landmark" },
                new OntologyFactEntry[0]);
            bootstrap.RegisterSceneObject(
                placedOntology,
                runSimulation: true);
            yield return null;

            Assert.That(
                bootstrap.World.HasFact(
                    equipmentSlotId,
                    OntologyPredicates.EquippedItem,
                    "InflatableRing"),
                Is.True,
                "Placing another object must not reset persistent equipment state.");
            Assert.That(attachment.IsAttached, Is.True);

            placedOntology.ConfigureOntologyData(
                "PlacedLandmark",
                new[] { "EditedLandmark" },
                new OntologyFactEntry[0]);
            bootstrap.SynchronizeSceneObjects(runSimulation: true);
            yield return null;
            Assert.That(
                bootstrap.World.HasFact(
                    equipmentSlotId,
                    OntologyPredicates.EquippedItem,
                    "InflatableRing"),
                Is.True,
                "Editing authored triples must not reset persistent equipment state.");
            Assert.That(
                bootstrap.World.HasFact(
                    "PlacedLandmark",
                    OntologyPredicates.HasConcept,
                    "Landmark"),
                Is.False);
            Assert.That(
                bootstrap.World.HasFact(
                    "PlacedLandmark",
                    OntologyPredicates.HasConcept,
                    "EditedLandmark"),
                Is.True);

            Assert.That(
                attachment.TryUnequip(),
                Is.True);
            Assert.That(
                bootstrap.World.HasFact(
                    equipmentSlotId,
                    OntologyPredicates.EquippedItem,
                    "InflatableRing"),
                Is.False);
            Assert.That(
                bootstrap.World.HasFact(
                "InflatableRing",
                OntologyPredicates.EquippedBy,
                "Player"),
                Is.False);
            Assert.That(
                bootstrap.World.HasFact(
                    "Player",
                    OntologyPredicates.InteractionIntent,
                    "InflatableRing"),
                Is.False);
            yield return null;

            Assert.That(attachment.IsAttached, Is.False);
            Assert.That(itemBody.isKinematic, Is.False);
            Assert.That(itemCollider.enabled, Is.True);
            var planarDetach = item.transform.position - actor.transform.position;
            planarDetach.y = 0f;
            Assert.That(
                planarDetach.magnitude,
                Is.EqualTo(profile.detachForwardDistance).Within(0.01f));

            item.transform.position = new Vector3(10f, 0f, 0f);
            Physics.SyncTransforms();
            yield return null;

            Assert.That(
                bootstrap.World.HasFact(
                    "Player",
                    OntologyPredicates.Near,
                    "InflatableRing"),
                Is.False);

            Object.Destroy(profile);
            Object.Destroy(actionEffects);
            Object.Destroy(actionCandidates);
            Object.Destroy(placedObject);
            Object.Destroy(item);
            Object.Destroy(actor);
            Object.Destroy(bootstrapObject);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            var field = target
                .GetType()
                .GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
