using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyRuntimeWorldEditorSelectionTests
    {
        [UnityTest]
        public IEnumerator TabSelectionCyclesNearbyObjectsInDistanceOrder()
        {
            var host = new GameObject("WorldEditorHost");
            var controller =
                host.AddComponent<OntologyRuntimeWorldEditorController>();
            var origin = new GameObject("SelectionOrigin");
            SetPrivateField(controller, "nearbySelectionOrigin", origin.transform);
            SetPrivateField(controller, "nearbySelectionRadius", 10f);

            var first = CreatePlaceable("First", new Vector3(1f, 0f, 0f));
            var second = CreatePlaceable("Second", new Vector3(2f, 0f, 0f));
            var third = CreatePlaceable("Third", new Vector3(3f, 0f, 0f));
            yield return null;

            controller.SelectNextNearbyPlaceable();
            Assert.That(controller.Selected, Is.SameAs(first));

            controller.SelectNextNearbyPlaceable();
            Assert.That(controller.Selected, Is.SameAs(second));

            controller.SelectNextNearbyPlaceable();
            Assert.That(controller.Selected, Is.SameAs(third));

            controller.SelectNextNearbyPlaceable();
            Assert.That(controller.Selected, Is.SameAs(first));

            second.gameObject.SetActive(false);
            controller.SelectNextNearbyPlaceable();
            Assert.That(
                controller.Selected,
                Is.SameAs(third),
                "Disabled candidates must be removed when the list is rebuilt.");

            Object.Destroy(third.gameObject);
            yield return null;
            controller.SelectNextNearbyPlaceable();
            Assert.That(
                controller.Selected,
                Is.SameAs(first),
                "Destroyed candidates must not leave a stale cycle entry.");

            Object.Destroy(first.gameObject);
            Object.Destroy(second.gameObject);
            Object.Destroy(origin);
            Object.Destroy(host);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ObjectCandidatesComeFromRegisteredProfileData()
        {
            var back = ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            back.profileId = "BackCarry";
            back.kind = OntologyAttachmentKind.Carryable;
            back.slotId = "Back";
            var seat = ScriptableObject.CreateInstance<OntologyAttachmentProfile>();
            seat.profileId = "DriverSeatMount";
            seat.kind = OntologyAttachmentKind.Mountable;
            seat.slotId = "DriverSeat";
            var database =
                ScriptableObject.CreateInstance<OntologyAttachmentProfileDatabase>();
            database.Replace(new[] { back, seat });

            var host = new GameObject("CandidateHost");
            var bootstrap = host.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "attachmentProfileDatabase",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(bootstrap, database);
            var controller =
                host.AddComponent<OntologyRuntimeWorldEditorController>();
            controller.Configure(null, null, bootstrap);

            Assert.That(
                controller.GetObjectCandidatesForRelation(
                    OntologyPredicates.AttachmentProfile),
                Is.EquivalentTo(new[] { "BackCarry", "DriverSeatMount" }));
            Assert.That(
                controller.GetObjectCandidatesForRelation(
                    OntologyPredicates.HasSlot),
                Is.EquivalentTo(new[] { "Back", "DriverSeat" }));
            Assert.That(
                controller.GetObjectCandidatesForRelation(
                    OntologyPredicates.PickupBehavior),
                Is.EquivalentTo(new[]
                {
                    OntologyObjects.SelectThenCarry,
                    OntologyObjects.SelectThenMount
                }));

            Object.Destroy(host);
            Object.Destroy(database);
            Object.Destroy(back);
            Object.Destroy(seat);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PhysicalMeaningSelectionSynchronizesDependentOntology()
        {
            var sinking =
                ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            sinking.profileId = "TestSinking";
            sinking.supportsBuoyancy = false;
            var buoyant =
                ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            buoyant.profileId = "TestBuoyant";
            buoyant.supportsBuoyancy = true;

            var physicalDatabase =
                ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            physicalDatabase.Replace(new[] { sinking, buoyant });
            var ruleDatabase =
                ScriptableObject.CreateInstance<OntologyRuleDatabase>();
            var buoyancyRule =
                ruleDatabase.CreateDefinition("BuoyantWhenInWater");
            buoyancyRule.effects.Add(OntologyEffect.AddFact(
                "?object",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating));
            buoyant.buoyancyRuleId = buoyancyRule.id;

            var host = new GameObject("PhysicalMeaningEditorHost");
            var bootstrap = host.AddComponent<OntologyWorldBootstrap>();
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "physicalProfileDatabase",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(bootstrap, physicalDatabase);
            typeof(OntologyWorldBootstrap)
                .GetField(
                    "ruleDatabase",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(bootstrap, ruleDatabase);
            var controller =
                host.AddComponent<OntologyRuntimeWorldEditorController>();
            controller.Configure(null, null, bootstrap);

            var target = CreatePlaceable("MeaningTarget", Vector3.zero);
            var ontology = target.gameObject.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                target.name,
                new[] { "Rock", OntologyConcepts.FloatableObject },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = sinking.profileId
                    },
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalState,
                        obj = OntologyObjects.Floating
                    }
                });
            var assignment =
                target.gameObject.AddComponent<OntologyRuleBlockAssignment>();
            assignment.Add(buoyancyRule.id, "?object");
            SetPrivateField(controller, "selected", target);
            yield return null;

            Assert.That(
                controller.SetSelectedPhysicalBehavior(buoyant.profileId),
                Is.True);
            Assert.That(
                ontology.Concepts,
                Does.Contain(OntologyConcepts.FloatableObject));
            Assert.That(
                ontology.Facts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value.predicate == OntologyPredicates.PhysicalProfile &&
                    value.obj == buoyant.profileId));
            Assert.That(
                ontology.Facts,
                Has.None.Matches<OntologyFactEntry>(value =>
                    value.predicate == OntologyPredicates.PhysicalState));
            Assert.That(
                assignment.Bindings,
                Has.Some.Matches<OntologyRuleBlockBinding>(value =>
                    value.ruleId == buoyancyRule.id));

            Assert.That(
                controller.SetSelectedPhysicalBehavior(sinking.profileId),
                Is.True);
            Assert.That(
                ontology.Concepts,
                Does.Not.Contain(OntologyConcepts.FloatableObject));
            Assert.That(
                ontology.Facts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value.predicate == OntologyPredicates.PhysicalProfile &&
                    value.obj == sinking.profileId));
            Assert.That(
                ontology.Facts,
                Has.None.Matches<OntologyFactEntry>(value =>
                    value.predicate == OntologyPredicates.PhysicalState));
            Assert.That(
                assignment.Bindings,
                Has.None.Matches<OntologyRuleBlockBinding>(value =>
                    value.ruleId == buoyancyRule.id));

            Object.Destroy(target.gameObject);
            Object.Destroy(host);
            Object.Destroy(ruleDatabase);
            Object.Destroy(physicalDatabase);
            Object.Destroy(sinking);
            Object.Destroy(buoyant);
            yield return null;
        }

        private static OntologyPlaceableInstance CreatePlaceable(
            string name,
            Vector3 position)
        {
            var value = GameObject.CreatePrimitive(PrimitiveType.Cube);
            value.name = name;
            value.transform.position = position;
            return value.AddComponent<OntologyPlaceableInstance>();
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            typeof(OntologyRuntimeWorldEditorController)
                .GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }
    }
}
