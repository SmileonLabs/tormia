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
        public IEnumerator InflatableRingVerticalSliceRequiresTripleRuleBlockAndPhysicalMeaning()
        {
            var scene = SceneManager.CreateScene(
                "InflatableRingVerticalSliceTest");
            SceneManager.SetActiveScene(scene);

            var bootstrapObject = new GameObject("Bootstrap");
            var actor = new GameObject("PlayerAvatar");
            var ring = new GameObject("RuntimeWearable");
            var ruleDatabase =
                ScriptableObject.CreateInstance<OntologyRuleDatabase>();
            var attachmentDatabase =
                ScriptableObject.CreateInstance<
                    OntologyAttachmentProfileDatabase>();
            var physicalDatabase =
                ScriptableObject.CreateInstance<
                    OntologyPhysicalProfileDatabase>();
            var attachmentProfile =
                ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            var physicalProfile =
                ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            try
            {
                CreateAutoEquipRule(ruleDatabase);
                CreateSlotAttachmentRule(ruleDatabase);
                CreateTemporarySkillRules(ruleDatabase);

                var ruleRegistry =
                    bootstrapObject.AddComponent<OntologyRuleBlockRegistry>();
                ruleRegistry.Replace(new[]
                {
                    "AutoEquipNearbyWearable",
                    "EquippedItemGrantsTemporarySkill"
                });
                var bootstrap =
                    bootstrapObject.AddComponent<OntologyWorldBootstrap>();
                SetPrivateField(bootstrap, "ruleDatabase", ruleDatabase);
                SetPrivateField(
                    bootstrap,
                    "ruleBlockRegistry",
                    ruleRegistry);
                SetPrivateField(
                    bootstrap,
                    "attachmentProfileDatabase",
                    attachmentDatabase);
                SetPrivateField(
                    bootstrap,
                    "physicalProfileDatabase",
                    physicalDatabase);

                var actorOntology = actor.AddComponent<OntologyObject>();
                actorOntology.ConfigureOntologyData(
                    "PlayerEntity",
                    new[] { OntologyConcepts.Actor },
                    new OntologyFactEntry[0]);
                var waistAnchor = new GameObject("WaistAnchor");
                waistAnchor.transform.SetParent(actor.transform, false);

                attachmentProfile.profileId = "WaistInflatableRing";
                attachmentProfile.kind = OntologyAttachmentKind.Wearable;
                attachmentProfile.slotId = "Waist";
                attachmentProfile.relationPredicate =
                    OntologyPredicates.EquippedBy;
                attachmentProfile.relationDirection =
                    OntologyAttachmentRelationDirection.ItemToActor;
                attachmentProfile.actorAnchorPath = "WaistAnchor";
                attachmentProfile.autoEquipDistance = 1.5f;
                attachmentProfile.proximityExitPadding = 0.1f;
                attachmentProfile.disableWorldPhysicsWhileAttached = true;
                attachmentProfile.disableWorldCollidersWhileAttached = true;
                attachmentDatabase.Replace(new[] { attachmentProfile });

                physicalProfile.profileId = "LightBuoyant";
                physicalProfile.mass = 1f;
                physicalProfile.supportsBuoyancy = true;
                physicalProfile.buoyancyRuleId = "BuoyantWhenInWater";
                physicalDatabase.Replace(new[] { physicalProfile });

                ring.transform.position = new Vector3(0.5f, 0f, 0f);
                var ringOntology = ring.AddComponent<OntologyObject>();
                ringOntology.ConfigureOntologyData(
                    "RingEntity",
                    new[]
                    {
                        OntologyConcepts.FloatableObject,
                        OntologyConcepts.Wearable
                    },
                    new[]
                    {
                        new OntologyFactEntry
                        {
                            predicate = OntologyPredicates.HasSlot,
                            obj = "Waist"
                        },
                        new OntologyFactEntry
                        {
                            predicate = OntologyPredicates.PickupBehavior,
                            obj = OntologyObjects.SelectThenEquip
                        },
                        new OntologyFactEntry
                        {
                            predicate =
                                OntologyPredicates.AttachmentProfile,
                            obj = attachmentProfile.profileId
                        },
                        new OntologyFactEntry
                        {
                            predicate = OntologyPredicates.PhysicalProfile,
                            obj = physicalProfile.profileId
                        },
                        new OntologyFactEntry
                        {
                            predicate = OntologyPredicates.GrantsSkill,
                            obj = "Swimming"
                        },
                        new OntologyFactEntry
                        {
                            predicate =
                                OntologyPredicates.SkillGrantRequiresRule,
                            obj = "BuoyantWhenInWater"
                        }
                    });
                var assignment =
                    ring.AddComponent<OntologyRuleBlockAssignment>();
                assignment.Add("AutoEquipNearbyWearable", "?object");
                assignment.Add(
                    "EquippedItemGrantsTemporarySkill",
                    "?item");
                assignment.Add("BuoyantWhenInWater", "?object");

                OntologySemanticAdapterSynchronizer.SynchronizeAll(
                    ring,
                    bootstrap,
                    actorOntology);
                bootstrap.ResetWorld(logReport: false);
                // Let the bootstrap's normal Start lifecycle complete before
                // publishing the ephemeral interaction observation.
                yield return null;
                bootstrap.World.AddFactContribution(
                    actorOntology.EntityId,
                    OntologyPredicates.InteractionIntent,
                    ringOntology.EntityId,
                    OntologyFactOrigin.RuntimeObservation);
                bootstrap.RequestSimulation();

                yield return null;
                yield return null;

                var slotId =
                    OntologyAttachmentAdapter.BuildEquipmentSlotEntityId(
                        actorOntology.EntityId,
                        "Waist");
                var attachment =
                    ring.GetComponent<OntologyAttachmentAdapter>();
                var facts = bootstrap.World.DumpFacts();
                Assert.That(
                    bootstrap.World.HasFact(
                        actorOntology.EntityId,
                        OntologyPredicates.Near,
                        ringOntology.EntityId),
                    Is.True,
                    "The proximity sensor must publish Near before the Rule Block can run.\n" +
                    facts);
                Assert.That(
                    bootstrap.World.HasFact(
                        ringOntology.EntityId,
                        OntologyPredicates.HasRuleBlock,
                        "AutoEquipNearbyWearable"),
                    Is.True,
                    "The assigned Rule Block must be projected as a fact.\n" + facts);
                Assert.That(
                    bootstrap.World.HasFact(
                        slotId,
                        OntologyPredicates.EquippedItem,
                        ringOntology.EntityId),
                    Is.True,
                    "The proximity + interaction Rule Block must populate the equipment slot.\n" +
                    facts);
                Assert.That(
                    bootstrap.World.HasFact(
                        ringOntology.EntityId,
                        OntologyPredicates.EquippedBy,
                        actorOntology.EntityId),
                    Is.True,
                    "The uncontrolled slot projection rule must derive equipped_by.");
                Assert.That(
                    attachment.IsAttached,
                    Is.True,
                    "The attachment adapter must present the derived equipped_by fact.");
                Assert.That(
                    ring.GetComponent<OntologyPhysicalBodyAdapter>(),
                    Is.Not.Null,
                    "physical_profile must create the generic physical adapter.");
                Assert.That(
                    ring.GetComponent<OntologyBuoyancyAdapter>(),
                    Is.Not.Null,
                    "The buoyancy profile must select its presentation adapter.");
                Assert.That(
                    bootstrap.World.HasFact(
                        actorOntology.EntityId,
                        OntologyPredicates.HasTemporarySkill,
                        "Swimming"),
                    Is.True,
                    "The rule-gated grant must derive temporary Swimming.");

                assignment.Remove(
                    "AutoEquipNearbyWearable",
                    "?object");
                // Evaluate the disabled case from clean authored state. The
                // former equipment slot is a persistent state transition, so
                // removing the rule does not retroactively rewrite history;
                // it must prevent the same interaction from equipping again.
                bootstrap.ResetWorld(logReport: false);
                yield return null;
                bootstrap.World.AddFactContribution(
                    actorOntology.EntityId,
                    OntologyPredicates.InteractionIntent,
                    ringOntology.EntityId,
                    OntologyFactOrigin.RuntimeObservation);
                bootstrap.RequestSimulation();
                yield return null;
                yield return null;
                attachment.SynchronizePresentation();
                yield return null;

                Assert.That(
                    bootstrap.World.HasFact(
                        ringOntology.EntityId,
                        OntologyPredicates.HasRuleBlock,
                        "AutoEquipNearbyWearable"),
                    Is.False,
                    "The disabled run must not project the removed Rule Block.");
                Assert.That(
                    bootstrap.World.HasFact(
                        slotId,
                        OntologyPredicates.EquippedItem,
                        ringOntology.EntityId),
                    Is.False,
                    "Without the Rule Block, the same interaction must not equip.");
                Assert.That(attachment.IsAttached, Is.False);
                Assert.That(
                    bootstrap.World.HasFact(
                        actorOntology.EntityId,
                        OntologyPredicates.HasTemporarySkill,
                        "Swimming"),
                    Is.False);
            }
            finally
            {
                Object.Destroy(physicalProfile);
                Object.Destroy(attachmentProfile);
                Object.Destroy(physicalDatabase);
                Object.Destroy(attachmentDatabase);
                Object.Destroy(ruleDatabase);
                Object.Destroy(ring);
                Object.Destroy(actor);
                Object.Destroy(bootstrapObject);
            }

            yield return null;
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        [Test]
        public void RequiredGripContractDisablesGenericAttachmentFallback()
        {
            var anchor = new GameObject("AuthoredSocket");
            var item = new GameObject("WeaponWithoutGrip");
            var profile =
                ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            try
            {
                profile.requireItemGripPoint = true;

                Assert.That(
                    OntologyAttachmentPoseUtility.Apply(
                        item.transform,
                        anchor.transform,
                        profile),
                    Is.False);
                Assert.That(item.transform.parent, Is.Null);

                var grip = new GameObject("GripPoint");
                grip.transform.SetParent(item.transform, false);
                var gripPoint =
                    grip.AddComponent<OntologyAttachmentGripPoint>();

                Assert.That(
                    OntologyAttachmentPoseUtility.Apply(
                        item.transform,
                        anchor.transform,
                        profile),
                    Is.False,
                    "An uncalibrated required grip must not become an " +
                    "implicit zero-offset attachment.");
                gripPoint.MarkCalibrated();

                Assert.That(
                    OntologyAttachmentPoseUtility.Apply(
                        item.transform,
                        anchor.transform,
                        profile),
                    Is.True);
                Assert.That(
                    item.transform.parent,
                    Is.EqualTo(anchor.transform));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(item);
                Object.DestroyImmediate(anchor);
            }
        }

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
            profile.relationPredicate = OntologyPredicates.EquippedBy;
            profile.relationDirection =
                OntologyAttachmentRelationDirection.ItemToActor;
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
            Assert.That(
                attachment.OwnsWorldTransform,
                Is.True,
                "The release lease must block an old durable projection until " +
                "Rigidbody ownership resumes.");
            Assert.That(
                itemBody.isKinematic,
                Is.True,
                "A released attachment must remain kinematic until Unity has " +
                "observed its restored collider at the safe world pose.");
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(itemBody.isKinematic, Is.False);
            Assert.That(attachment.OwnsWorldTransform, Is.False);
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

        [UnityTest]
        public IEnumerator ActorToItemRelationUsesGenericAttachmentAndPhysicsOwnership()
        {
            var bootstrapObject = new GameObject("AuthorityAttachmentBootstrap");
            var actor = new GameObject("AuthorityActor");
            var item = new GameObject("ArbitraryTool");
            var profile =
                ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            try
            {
                var bootstrap =
                    bootstrapObject.AddComponent<OntologyWorldBootstrap>();
                var actorOntology = actor.AddComponent<OntologyObject>();
                actorOntology.ConfigureOntologyData(
                    "Actor_01",
                    new[] { OntologyConcepts.Actor },
                    new OntologyFactEntry[0]);
                var hand = new GameObject("HandSocket");
                hand.transform.SetParent(actor.transform, false);
                var authoredSocket =
                    hand.AddComponent<OntologyAttachmentSocket>();
                authoredSocket.Configure(
                    "ActiveTool",
                    actor.transform,
                    HumanBodyBones.Hips,
                    string.Empty,
                    Vector3.zero,
                    Vector3.zero);

                var itemOntology = item.AddComponent<OntologyObject>();
                itemOntology.ConfigureOntologyData(
                    "Tool_01",
                    new[] { OntologyConcepts.Carryable },
                    new[]
                    {
                        new OntologyFactEntry
                        {
                            predicate = OntologyPredicates.AttachmentProfile,
                            obj = "ActorOwnedTool"
                        }
                    });
                var body = item.AddComponent<Rigidbody>();
                var collider = item.AddComponent<BoxCollider>();
                var gripObject = new GameObject("AuthoredGripPoint");
                gripObject.transform.SetParent(item.transform, false);
                gripObject.transform.localPosition =
                    new Vector3(0.2f, -0.15f, 0.05f);
                gripObject.transform.localRotation =
                    Quaternion.Euler(0f, 35f, -20f);
                var gripPoint =
                    gripObject.AddComponent<OntologyAttachmentGripPoint>();

                profile.profileId = "ActorOwnedTool";
                profile.kind = OntologyAttachmentKind.Carryable;
                profile.slotId = "ActiveTool";
                profile.relationPredicate = OntologyPredicates.EquippedItem;
                profile.relationDirection =
                    OntologyAttachmentRelationDirection.ActorToItem;
                profile.actorSocketId = "ActiveTool";
                profile.actorAnchorPath = "MissingFallbackAnchor";
                profile.localPosition =
                    new Vector3(0.05f, 0.1f, -0.02f);
                profile.localEulerAngles =
                    new Vector3(0f, 10f, 0f);
                profile.disableWorldPhysicsWhileAttached = true;
                profile.disableWorldCollidersWhileAttached = true;

                var attachment =
                    item.AddComponent<OntologyAttachmentAdapter>();
                attachment.Configure(profile, bootstrap, actorOntology);
                // Let the bootstrap's normal Start reset complete before adding
                // the simulated Authority relation under test.
                yield return null;
                bootstrap.ResetWorld(logReport: false);
                bootstrap.World.SetFact(
                    actorOntology.EntityId,
                    OntologyPredicates.EquippedItem,
                    itemOntology.EntityId,
                    out _);
                Assert.That(
                    bootstrap.World.HasFact(
                        itemOntology.EntityId,
                        OntologyPredicates.AttachmentProfile,
                        profile.profileId),
                    Is.True,
                    "The item must retain its authored attachment-profile fact.");
                Assert.That(
                    bootstrap.World.HasFact(
                        actorOntology.EntityId,
                        profile.relationPredicate,
                        itemOntology.EntityId),
                    Is.True,
                    "The world must contain the profile-authored actor-to-item relation.");
                Assert.That(
                    bootstrap.EntityRegistry.TryGet(
                        actorOntology.EntityId,
                        out var registeredActor),
                    Is.True,
                    "The relation subject must resolve through the entity registry.");
                Assert.That(registeredActor, Is.EqualTo(actorOntology));
                attachment.SynchronizePresentation();
                yield return null;

                Assert.That(
                    attachment.IsAttached,
                    Is.True,
                    "The actor-to-item relation must activate the generic attachment.");
                Assert.That(
                    attachment.OwnsWorldTransform,
                    Is.True,
                    "An attached presentation must own its Transform.");
                Assert.That(
                    item.transform.parent,
                    Is.EqualTo(hand.transform),
                    "The item must resolve the profile-authored actor anchor.");
                var expectedGripPosition =
                    hand.transform.TransformPoint(profile.localPosition);
                var expectedGripRotation =
                    hand.transform.rotation *
                    Quaternion.Euler(profile.localEulerAngles);
                Assert.That(
                    Vector3.Distance(
                        gripPoint.transform.position,
                        expectedGripPosition),
                    Is.LessThan(0.0001f),
                    "The item-authored grip point must meet the actor socket.");
                Assert.That(
                    Quaternion.Angle(
                        gripPoint.transform.rotation,
                        expectedGripRotation),
                    Is.LessThan(0.01f),
                    "The item-authored grip orientation must match the actor socket.");
                Assert.That(
                    body.isKinematic,
                    Is.True,
                    "Attachment physics ownership must suspend world simulation.");
                Assert.That(body.useGravity, Is.False);
                Assert.That(collider.enabled, Is.False);

                var rootPositionBeforeGripEdit =
                    item.transform.localPosition;
                gripPoint.transform.localPosition +=
                    new Vector3(0.1f, 0.05f, -0.02f);
                attachment.SynchronizePresentation();
                yield return null;

                Assert.That(
                    Vector3.Distance(
                        gripPoint.transform.position,
                        expectedGripPosition),
                    Is.LessThan(0.0001f),
                    "Editing the prefab-owned grip pose must immediately realign the attached item.");
                Assert.That(
                    Vector3.Distance(
                        item.transform.localPosition,
                        rootPositionBeforeGripEdit),
                    Is.GreaterThan(0.01f),
                    "The attached root must not remain at a stale forced pose.");

                bootstrap.World.RemoveFact(
                    actorOntology.EntityId,
                    OntologyPredicates.EquippedItem,
                    itemOntology.EntityId);
                attachment.SynchronizePresentation();
                yield return null;

                Assert.That(attachment.IsAttached, Is.False);
                Assert.That(attachment.OwnsWorldTransform, Is.True);
                yield return new WaitForFixedUpdate();
                yield return null;
                Assert.That(attachment.OwnsWorldTransform, Is.False);
                Assert.That(body.isKinematic, Is.False);
                Assert.That(body.useGravity, Is.True);
                Assert.That(collider.enabled, Is.True);

                Object.DestroyImmediate(gripObject);
                bootstrap.World.SetFact(
                    actorOntology.EntityId,
                    OntologyPredicates.EquippedItem,
                    itemOntology.EntityId,
                    out _);
                attachment.SynchronizePresentation();
                yield return null;

                Assert.That(attachment.IsAttached, Is.True);
                Assert.That(
                    Vector3.Distance(
                        item.transform.localPosition,
                        profile.localPosition),
                    Is.LessThan(0.0001f),
                    "Removing the item grip contract must restore the generic profile pose.");
                Assert.That(
                    Quaternion.Angle(
                        item.transform.localRotation,
                        Quaternion.Euler(profile.localEulerAngles)),
                    Is.LessThan(0.01f));

                bootstrap.World.RemoveFact(
                    actorOntology.EntityId,
                    OntologyPredicates.EquippedItem,
                    itemOntology.EntityId);
                attachment.SynchronizePresentation();
                yield return null;
            }
            finally
            {
                Object.Destroy(profile);
                Object.Destroy(item);
                Object.Destroy(actor);
                Object.Destroy(bootstrapObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator AmbiguousEquipmentOwnershipDisablesPresentationUntilRepaired()
        {
            var bootstrapObject = new GameObject("AmbiguousAttachmentBootstrap");
            var firstActor = new GameObject("FirstActor");
            var secondActor = new GameObject("SecondActor");
            var item = new GameObject("SharedTool");
            var profile =
                ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            try
            {
                var bootstrap =
                    bootstrapObject.AddComponent<OntologyWorldBootstrap>();
                var firstOntology = firstActor.AddComponent<OntologyObject>();
                firstOntology.ConfigureOntologyData(
                    "Actor_A",
                    new[] { OntologyConcepts.Actor },
                    new OntologyFactEntry[0]);
                var secondOntology = secondActor.AddComponent<OntologyObject>();
                secondOntology.ConfigureOntologyData(
                    "Actor_B",
                    new[] { OntologyConcepts.Actor },
                    new OntologyFactEntry[0]);
                var hand = new GameObject("HandSocket");
                hand.transform.SetParent(firstActor.transform, false);

                var itemOntology = item.AddComponent<OntologyObject>();
                itemOntology.ConfigureOntologyData(
                    "SharedTool_01",
                    new[] { OntologyConcepts.Carryable },
                    new[]
                    {
                        new OntologyFactEntry
                        {
                            predicate = OntologyPredicates.AttachmentProfile,
                            obj = "AmbiguousTool"
                        }
                    });
                item.AddComponent<Rigidbody>();
                item.AddComponent<BoxCollider>();

                profile.profileId = "AmbiguousTool";
                profile.kind = OntologyAttachmentKind.Carryable;
                profile.relationPredicate = OntologyPredicates.EquippedItem;
                profile.relationDirection =
                    OntologyAttachmentRelationDirection.ActorToItem;
                profile.actorAnchorPath = "HandSocket";

                var attachment =
                    item.AddComponent<OntologyAttachmentAdapter>();
                attachment.Configure(profile, bootstrap, firstOntology);
                yield return null;
                bootstrap.ResetWorld(logReport: false);
                bootstrap.World.SetFact(
                    firstOntology.EntityId,
                    OntologyPredicates.EquippedItem,
                    itemOntology.EntityId,
                    out _);
                bootstrap.World.SetFact(
                    secondOntology.EntityId,
                    OntologyPredicates.EquippedItem,
                    itemOntology.EntityId,
                    out _);

                LogAssert.Expect(
                    LogType.Error,
                    "[OntologyAttachment] Multiple actors own the same " +
                    "attachment relation. Presentation is disabled until " +
                    "Authority repairs the relation.");
                attachment.SynchronizePresentation();
                yield return null;
                Assert.That(attachment.IsAttached, Is.False);

                bootstrap.World.RemoveFact(
                    secondOntology.EntityId,
                    OntologyPredicates.EquippedItem,
                    itemOntology.EntityId);
                attachment.SynchronizePresentation();
                yield return null;

                Assert.That(attachment.IsAttached, Is.True);
                Assert.That(item.transform.parent, Is.EqualTo(hand.transform));

                bootstrap.World.RemoveFact(
                    firstOntology.EntityId,
                    OntologyPredicates.EquippedItem,
                    itemOntology.EntityId);
                attachment.SynchronizePresentation();
                yield return null;
            }
            finally
            {
                Object.Destroy(profile);
                Object.Destroy(item);
                Object.Destroy(secondActor);
                Object.Destroy(firstActor);
                Object.Destroy(bootstrapObject);
            }

            yield return null;
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

        private static void CreateAutoEquipRule(
            OntologyRuleDatabase database)
        {
            var rule =
                database.CreateDefinition("AutoEquipNearbyWearable");
            rule.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.Near,
                "?object"));
            rule.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.InteractionIntent,
                "?object"));
            rule.conditions.Add(
                OntologyCondition.HasConcept(
                    "?actor",
                    OntologyConcepts.Actor));
            rule.conditions.Add(
                OntologyCondition.HasConcept(
                    "?object",
                    OntologyConcepts.Wearable));
            rule.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.PickupBehavior,
                OntologyObjects.SelectThenEquip));
            rule.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.AttachmentProfile,
                "?profile"));
            rule.conditions.Add(OntologyCondition.Fact(
                "?profile",
                OntologyPredicates.AttachmentKind,
                OntologyObjects.Wearable));
            rule.conditions.Add(OntologyCondition.Fact(
                "?object",
                OntologyPredicates.HasSlot,
                "?slot"));
            rule.conditions.Add(OntologyCondition.NotFact(
                "EquipmentSlot_{actor}_{slot}",
                OntologyPredicates.EquippedItem,
                "?equipped"));
            rule.effects.Add(OntologyEffect.SetFact(
                "EquipmentSlot_{actor}_{slot}",
                OntologyPredicates.EquippedItem,
                "?object"));
            rule.effects.Add(OntologyEffect.SetFact(
                "EquipmentSlot_{actor}_{slot}",
                OntologyPredicates.SlotOwner,
                "?actor"));
            rule.effects.Add(OntologyEffect.SetFact(
                "EquipmentSlot_{actor}_{slot}",
                OntologyPredicates.SlotId,
                "?slot"));
        }

        private static void CreateSlotAttachmentRule(
            OntologyRuleDatabase database)
        {
            var rule =
                database.CreateDefinition("EquippedSlotItemAttaches");
            rule.conditions.Add(OntologyCondition.Fact(
                "?slotEntity",
                OntologyPredicates.SlotOwner,
                "?actor"));
            rule.conditions.Add(OntologyCondition.Fact(
                "?slotEntity",
                OntologyPredicates.SlotId,
                "?slot"));
            rule.conditions.Add(OntologyCondition.Fact(
                "?slotEntity",
                OntologyPredicates.EquippedItem,
                "?item"));
            rule.conditions.Add(OntologyCondition.Fact(
                "?item",
                OntologyPredicates.HasSlot,
                "?slot"));
            rule.conditions.Add(
                OntologyCondition.HasConcept(
                    "?item",
                    OntologyConcepts.Wearable));
            rule.effects.Add(OntologyEffect.AddFact(
                "?item",
                OntologyPredicates.EquippedBy,
                "?actor"));
        }

        private static void CreateTemporarySkillRules(
            OntologyRuleDatabase database)
        {
            var grant = database.CreateDefinition(
                "EquippedItemGrantsTemporarySkill");
            grant.conditions.Add(OntologyCondition.Fact(
                "?item",
                OntologyPredicates.EquippedBy,
                "?actor"));
            grant.conditions.Add(OntologyCondition.Fact(
                "?item",
                OntologyPredicates.GrantsSkill,
                "?skill"));
            grant.conditions.Add(OntologyCondition.Fact(
                "?item",
                OntologyPredicates.SkillGrantRequiresRule,
                "?requiredRule"));
            grant.conditions.Add(OntologyCondition.Fact(
                "?item",
                OntologyPredicates.HasRuleBlock,
                "?requiredRule"));
            grant.effects.Add(OntologyEffect.AddFact(
                "?actor",
                OntologyPredicates.HasTemporarySkill,
                "?skill"));

            var usable = database.CreateDefinition(
                "TemporarySkillBecomesUsable");
            usable.conditions.Add(OntologyCondition.Fact(
                "?actor",
                OntologyPredicates.HasTemporarySkill,
                "?skill"));
            usable.effects.Add(OntologyEffect.AddFact(
                "?actor",
                OntologyPredicates.CanUseSkill,
                "?skill"));
        }
    }
}
