using System.Collections;
using System.Reflection;
using Delegate = System.Delegate;
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

        [UnityTest]
        public IEnumerator AuthorityMeaningPackageRequestIsCompleteAndDoesNotMutateLocally()
        {
            var buoyant =
                ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
            buoyant.profileId = "TestAuthorityBuoyant";
            buoyant.supportsBuoyancy = true;
            buoyant.buoyancyRuleId = "BuoyantWhenInWater";
            var physicalDatabase =
                ScriptableObject.CreateInstance<OntologyPhysicalProfileDatabase>();
            physicalDatabase.Replace(new[] { buoyant });
            var ruleDatabase =
                ScriptableObject.CreateInstance<OntologyRuleDatabase>();
            var rule = ruleDatabase.CreateDefinition("BuoyantWhenInWater");
            rule.catalogVersion = 3;
            rule.effects.Add(OntologyEffect.AddFact(
                "?object",
                OntologyPredicates.PhysicalState,
                OntologyObjects.Floating));

            var host = new GameObject("AuthorityMeaningPackageHost");
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

            var target = CreatePlaceable("ArbitraryVisualObject", Vector3.zero);
            var ontology = target.gameObject.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                "ArbitraryVisualObject",
                new[] { "Rock" },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = "HeavySinking"
                    }
                });
            SetPrivateField(controller, "selected", target);

            OntologyMeaningPackageChange requested = null;
            controller.MeaningPackageChangeRequested +=
                (_, change) => requested = change;
            yield return null;

            Assert.That(
                controller.SetSelectedPhysicalBehavior(buoyant.profileId),
                Is.True);
            Assert.That(requested, Is.Not.Null);
            Assert.That(requested.slotId, Is.EqualTo("primary_physical_meaning"));
            Assert.That(
                requested.replacePredicateIds,
                Does.Contain(OntologyPredicates.PhysicalProfile));
            Assert.That(
                requested.requiredConceptIds,
                Does.Contain(OntologyConcepts.FloatableObject));
            Assert.That(
                requested.authoredFacts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value.predicate == OntologyPredicates.PhysicalProfile &&
                    value.obj == buoyant.profileId));
            Assert.That(
                requested.ruleBlocks,
                Has.Some.Matches<OntologyMeaningPackageRuleBlock>(value =>
                    value.ruleId == rule.id &&
                    value.ruleVersion == rule.catalogVersion));

            // Command-first authoring never leaves an optimistic local semantic
            // state that a later Authority projection can overwrite.
            Assert.That(ontology.Concepts, Does.Not.Contain(
                OntologyConcepts.FloatableObject));
            Assert.That(
                ontology.Facts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value.predicate == OntologyPredicates.PhysicalProfile &&
                    value.obj == "HeavySinking"));

            Object.Destroy(target.gameObject);
            Object.Destroy(host);
            Object.Destroy(ruleDatabase);
            Object.Destroy(physicalDatabase);
            Object.Destroy(buoyant);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RemovingPreconfiguredRulesCompletesOwnedBaselineOnlyAtLastRule()
        {
            var host = new GameObject("BaselineRemovalEditorHost");
            var controller =
                host.AddComponent<OntologyRuntimeWorldEditorController>();
            var target = CreatePlaceable(
                "PreconfiguredMeaningTarget",
                Vector3.zero);
            var ontology = target.gameObject.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                target.name,
                new[] { "Weapon", "IndependentDecoration" },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = "HeldToolDynamic"
                    },
                    new OntologyFactEntry
                    {
                        predicate = "color",
                        obj = "blue"
                    }
                });
            var assignment =
                target.gameObject.AddComponent<OntologyRuleBlockAssignment>();
            assignment.Add("EquipItemOnInteractionIntent", "?target");
            assignment.Add("UnequipItemOnInteractionIntent", "?target");
            var ledger =
                target.gameObject.AddComponent<
                    OntologySemanticContributionLedger>();
            ledger.AddContribution(
                "template_semantic_baseline:TestWeapon:v2",
                new[] { "Weapon" },
                new[]
                {
                    new OntologyFactEntry
                    {
                        predicate = OntologyPredicates.PhysicalProfile,
                        obj = "HeldToolDynamic"
                    }
                },
                assignment.Bindings);
            SetPrivateField(controller, "selected", target);
            yield return null;

            Assert.That(
                controller.RemoveSelectedRuleBlock(
                    "EquipItemOnInteractionIntent",
                    "?target"),
                Is.True);
            Assert.That(
                assignment.Bindings,
                Has.None.Matches<OntologyRuleBlockBinding>(value =>
                    value.ruleId == "EquipItemOnInteractionIntent"),
                "The selected Rule Block must be removed independently.");
            Assert.That(
                assignment.Bindings,
                Has.Some.Matches<OntologyRuleBlockBinding>(value =>
                    value.ruleId == "UnequipItemOnInteractionIntent"),
                "Sibling Rule Blocks must remain active.");
            Assert.That(ontology.Concepts, Does.Contain("Weapon"));
            Assert.That(
                ontology.Facts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value.predicate == OntologyPredicates.PhysicalProfile),
                "Shared package meaning stays while a sibling Rule Block remains.");

            Assert.That(
                controller.RemoveSelectedRuleBlock(
                    "UnequipItemOnInteractionIntent",
                    "?target"),
                Is.True);
            Assert.That(assignment.Bindings, Is.Empty);
            Assert.That(ontology.Concepts, Does.Not.Contain("Weapon"));
            Assert.That(
                ontology.Facts,
                Has.None.Matches<OntologyFactEntry>(value =>
                    value.predicate == OntologyPredicates.PhysicalProfile),
                "Removing the final Rule Block completes package cleanup.");
            Assert.That(
                ontology.Concepts,
                Does.Contain("IndependentDecoration"),
                "Independent authored meaning must survive package removal.");
            Assert.That(
                ontology.Facts,
                Has.Some.Matches<OntologyFactEntry>(value =>
                    value.predicate == "color" && value.obj == "blue"));

            Object.Destroy(target.gameObject);
            Object.Destroy(host);
            yield return null;
        }

        [UnityTest]
        public IEnumerator AuthorityRuleRemovalKeepsProjectionUntilConfirmed()
        {
            var host = new GameObject("AuthorityRuleRemovalHost");
            var controller =
                host.AddComponent<OntologyRuntimeWorldEditorController>();
            var target = CreatePlaceable("AuthorityRuleTarget", Vector3.zero);
            var assignment =
                target.gameObject.AddComponent<OntologyRuleBlockAssignment>();
            var binding = new OntologyRuleBlockBinding
            {
                bindingId = System.Guid.NewGuid().ToString("D"),
                ruleId = "EquipItemOnInteractionIntent",
                bindingVariable = "?target",
                applicationId = System.Guid.NewGuid().ToString("D"),
                packageId = "weapon_equipment"
            };
            assignment.Replace(new[] { binding });
            SetPrivateField(controller, "selected", target);
            OntologyRuleBlockBinding requested = null;
            controller.RuleBlockRemovalRequested += (_, value) => requested = value;
            yield return null;

            Assert.That(controller.RequestSelectedRuleBlockRemoval(binding), Is.True);
            Assert.That(requested, Is.SameAs(binding));
            Assert.That(assignment.Bindings, Has.Count.EqualTo(1),
                "The local projection must remain unchanged until Authority confirms.");
            Assert.That(controller.IsRuleBlockRemovalPending(binding.bindingId), Is.True);

            controller.CompleteRuleBlockRemoval(binding.bindingId);
            Assert.That(controller.IsRuleBlockRemovalPending(binding.bindingId), Is.False);
            Object.Destroy(target.gameObject);
            Object.Destroy(host);
            yield return null;
        }

        [Test]
        public void RemoveByBindingIdRemovesOnlyExactDuplicateSemanticBinding()
        {
            var host = new GameObject("ExactBindingRemoval");
            var assignment = host.AddComponent<OntologyRuleBlockAssignment>();
            var first = System.Guid.NewGuid().ToString("D");
            var second = System.Guid.NewGuid().ToString("D");
            assignment.Replace(new[]
            {
                new OntologyRuleBlockBinding { bindingId = first, ruleId = "Rule", bindingVariable = "?target" },
                new OntologyRuleBlockBinding { bindingId = second, ruleId = "Rule", bindingVariable = "?target" }
            });

            Assert.That(assignment.RemoveByBindingId(first), Is.True);
            Assert.That(assignment.Bindings, Has.Count.EqualTo(1));
            Assert.That(assignment.Bindings[0].bindingId, Is.EqualTo(second));
            Object.DestroyImmediate(host);
        }

        [UnityTest]
        public IEnumerator AuthorityBridgeSubscribesWhenWorldEditorAppearsAfterBridgeEnable()
        {
            var bridgeHost = new GameObject("LateBindingAuthorityBridge");
            bridgeHost.SetActive(false);
            var client = bridgeHost.AddComponent<OntologyWorldAuthorityClient>();
            client.enabled = false;
            var bridge = bridgeHost.AddComponent<OntologyWorldAuthorityBridge>();
            bridgeHost.SetActive(true);
            yield return null;

            var editorHost = new GameObject("LateWorldEditor");
            editorHost.SetActive(false);
            var editor =
                editorHost.AddComponent<OntologyRuntimeWorldEditorController>();

            InvokePrivate(bridge, "ResolveDependencies");
            Assert.That(
                GetSubscriberCount(editor, "RuleBlockChanged"),
                Is.EqualTo(1),
                "An inactive World editor loaded after the persistent bridge " +
                "must still publish Rule Block changes when the runtime gate opens.");
            Assert.That(
                GetSubscriberCount(editor, "MeaningPackageChangeRequested"),
                Is.EqualTo(1),
                "A late World editor must publish complete meaning-package changes.");
            Assert.That(
                GetSubscriberCount(editor, "RuleBlockRemovalRequested"),
                Is.EqualTo(1));

            InvokePrivate(bridge, "ResolveDependencies");
            Assert.That(
                GetSubscriberCount(editor, "RuleBlockChanged"),
                Is.EqualTo(1),
                "Repeated dependency resolution must not duplicate subscriptions.");
            Assert.That(
                GetSubscriberCount(editor, "MeaningPackageChangeRequested"),
                Is.EqualTo(1));

            bridge.enabled = false;
            Assert.That(GetSubscriberCount(editor, "RuleBlockChanged"), Is.Zero);
            Assert.That(
                GetSubscriberCount(editor, "MeaningPackageChangeRequested"),
                Is.Zero);
            Assert.That(
                GetSubscriberCount(editor, "RuleBlockRemovalRequested"),
                Is.Zero);

            Object.Destroy(editorHost);
            Object.Destroy(bridgeHost);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DeleteSelectionUsesAuthorityRetirementWhenClaimed()
        {
            var host = new GameObject("AuthorityRetirementEditor");
            var controller =
                host.AddComponent<OntologyRuntimeWorldEditorController>();
            var target =
                CreatePlaceable("AuthorityProjectedEntity", Vector3.zero);
            SetPrivateField(controller, "selected", target);
            OntologyPlaceableInstance requested = null;
            controller.EntityRetirementRequested += value =>
            {
                requested = value;
                return true;
            };

            controller.DeleteSelection();
            yield return null;

            Assert.That(requested, Is.SameAs(target));
            Assert.That(
                target,
                Is.Not.Null,
                "A claimed Authority retirement must wait for projection removal.");

            Object.Destroy(target.gameObject);
            Object.Destroy(host);
            yield return null;
        }

        [UnityTest]
        public IEnumerator DeleteSelectionFallsBackOnlyWhenNoAuthorityHandlerClaimsIt()
        {
            var host = new GameObject("LocalRetirementEditor");
            var controller =
                host.AddComponent<OntologyRuntimeWorldEditorController>();
            var target = CreatePlaceable("LocalPreviewEntity", Vector3.zero);
            SetPrivateField(controller, "selected", target);
            controller.EntityRetirementRequested += _ => false;

            controller.DeleteSelection();
            yield return null;

            Assert.That(
                target == null,
                Is.True,
                "A local preview remains locally deletable when Authority does not own it.");

            Object.Destroy(host);
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

        private static void InvokePrivate(object target, string methodName)
        {
            target.GetType()
                .GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(target, null);
        }

        private static int GetSubscriberCount(object target, string eventName)
        {
            var callback = target.GetType()
                .GetField(
                    eventName,
                    BindingFlags.Instance |
                    BindingFlags.NonPublic |
                    BindingFlags.Public)
                ?.GetValue(target) as Delegate;
            return callback?.GetInvocationList().Length ?? 0;
        }
    }
}
