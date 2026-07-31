using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEditor;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyPlayerInputPriorityTests
    {
        [Test]
        public void DirectMovement_CancelsPendingClickNavigation()
        {
            var player = new GameObject("InputPriorityPlayer");
            try
            {
                var input = player.AddComponent<OntologyInputSystemPlayerInput>();
                SetPendingClick(input, true);

                Assert.That(
                    input.CancelClickNavigationForDirectInput(Vector2.right),
                    Is.True);
                Assert.That(ReadPendingClick(input), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void NoDirectMovement_PreservesPendingClickNavigation()
        {
            var player = new GameObject("InputPriorityPlayer");
            try
            {
                var input = player.AddComponent<OntologyInputSystemPlayerInput>();
                SetPendingClick(input, true);

                Assert.That(
                    input.CancelClickNavigationForDirectInput(Vector2.zero),
                    Is.False);
                Assert.That(ReadPendingClick(input), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void DirectMovement_CancelsPendingCombatApproach()
        {
            var player = new GameObject("CombatApproachPlayer");
            var target = new GameObject("CombatApproachTarget");
            try
            {
                var input =
                    player.AddComponent<OntologyInputSystemPlayerInput>();
                var identity =
                    target.AddComponent<OntologyAuthorityEntityIdentity>();
                SetPrivateField(
                    input,
                    "selectedCombatTarget",
                    identity);
                SetPendingClick(input, true);

                Assert.That(
                    input.CancelClickNavigationForDirectInput(Vector2.right),
                    Is.True);
                Assert.That(ReadPendingClick(input), Is.False);
                Assert.That(
                    ReadPrivateField<OntologyAuthorityEntityIdentity>(
                        input,
                        "selectedCombatTarget"),
                    Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void StaleAttackCompletion_PreservesNewGroundNavigation()
        {
            var player = new GameObject("AttackGroundClickPlayer");
            var target = new GameObject("CompletedAttackTarget");
            try
            {
                var input =
                    player.AddComponent<OntologyInputSystemPlayerInput>();
                var identity =
                    target.AddComponent<OntologyAuthorityEntityIdentity>();

                SetPrivateField(input, "combatIntentRevision", 2u);
                SetPrivateField(
                    input,
                    "selectedCombatTarget",
                    null);
                SetPendingClick(input, true);

                InvokePrivate(
                    input,
                    "CompleteCombatTargetIntent",
                    1u,
                    identity,
                    true);

                Assert.That(
                    ReadPendingClick(input),
                    Is.True,
                    "A completed older attack must not erase the newer ground-click destination.");
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void CurrentAttackCompletion_ClearsConsumedCombatIntent()
        {
            var player = new GameObject("CompletedAttackPlayer");
            var target = new GameObject("CompletedAttackTarget");
            try
            {
                var input =
                    player.AddComponent<OntologyInputSystemPlayerInput>();
                var identity =
                    target.AddComponent<OntologyAuthorityEntityIdentity>();

                SetPrivateField(input, "combatIntentRevision", 3u);
                SetPrivateField(
                    input,
                    "selectedCombatTarget",
                    identity);
                SetPrivateField(
                    input,
                    "combatIntentRequestPending",
                    true);

                InvokePrivate(
                    input,
                    "CompleteCombatTargetIntent",
                    3u,
                    identity,
                    true);

                Assert.That(
                    ReadPrivateField<OntologyAuthorityEntityIdentity>(
                        input,
                        "selectedCombatTarget"),
                    Is.Null);
                Assert.That(
                    ReadPrivateField<bool>(
                        input,
                        "combatIntentRequestPending"),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void WorldEditor_StaleSelectionReleasesPlayerInputCapture()
        {
            var editorObject = new GameObject("StaleWorldEditor");
            try
            {
                var editor =
                    editorObject.AddComponent<
                        OntologyRuntimeWorldEditorController>();
                editor.SetEditing(true);

                Assert.That(
                    OntologyRuntimeWorldEditorController
                        .IsEditInputCaptured,
                    Is.True);
                Assert.That(
                    InvokePrivate(
                        editor,
                        "ReleaseStaleSelectionCapture"),
                    Is.EqualTo(true));
                Assert.That(editor.IsEditing, Is.False);
                Assert.That(
                    OntologyRuntimeWorldEditorController
                        .IsEditInputCaptured,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(editorObject);
            }
        }

        [Test]
        public void ObjectPlacement_StaleOperationReleasesPlayerInputCapture()
        {
            var placementObject = new GameObject("StaleObjectPlacement");
            try
            {
                var placement =
                    placementObject.AddComponent<
                        OntologyRuntimeObjectPlacementController>();
                SetPrivateField(
                    placement,
                    "activeDefinition",
                    new OntologyPlaceableDefinition());
                SetPrivateStaticField(
                    typeof(OntologyRuntimeObjectPlacementController),
                    "<IsPlacementInputCaptured>k__BackingField",
                    true);

                Assert.That(
                    InvokePrivate(
                        placement,
                        "ReleaseStalePlacementCapture"),
                    Is.EqualTo(true));
                Assert.That(placement.IsPlacing, Is.False);
                Assert.That(
                    OntologyRuntimeObjectPlacementController
                        .IsPlacementInputCaptured,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(placementObject);
            }
        }

        [Test]
        public void CombatApproach_UsesAuthoredRangeWithNavigationInset()
        {
            Assert.That(
                OntologyInputSystemPlayerInput
                    .ComputeCombatApproachStopDistance(
                        defaultStopDistance: 0.1f,
                        authoredAttackRange: 2f,
                        navigationBuffer: 0.15f),
                Is.EqualTo(1.85f).Within(0.0001f));
        }

        [Test]
        public void CombatApproach_UsesPhysicalContactWhenShorterThanAuthorityRange()
        {
            Assert.That(
                OntologyInputSystemPlayerInput
                    .SelectCombatApproachDistance(
                        authoredAttackRange: 3f,
                        physicalContactDistance: 1.4f),
                Is.EqualTo(1.4f).Within(0.0001f));
        }

        [Test]
        public void CombatApproach_NeverConsumesPhysicalBodyClearanceAsRangeInset()
        {
            Assert.That(
                OntologyInputSystemPlayerInput
                    .ComputeCombatApproachStopDistance(
                        defaultStopDistance: 0.1f,
                        authoredAttackRange: 0.7f,
                        navigationBuffer: 0.15f,
                        minimumBodyClearance: 1.23f),
                Is.EqualTo(1.23f).Within(0.0001f));
        }

        [Test]
        public void CombatFacing_RequiresTargetDirectionBeforeAttackPublication()
        {
            Assert.That(
                OntologyInputSystemPlayerInput.IsWithinCombatFacingAngle(
                    Vector3.forward,
                    Quaternion.Euler(0f, 2f, 0f) * Vector3.forward,
                    3f),
                Is.True);
            Assert.That(
                OntologyInputSystemPlayerInput.IsWithinCombatFacingAngle(
                    Vector3.forward,
                    Quaternion.Euler(0f, 11f, 0f) * Vector3.forward,
                    3f),
                Is.False);
        }

        [Test]
        public void CombatRejection_OutOfRangeApproachesButCooldownDoesNotMove()
        {
            Assert.That(
                OntologyCombatController.RequiresApproach(
                    "attack_preview_rejected:action_target_out_of_range"),
                Is.True);
            Assert.That(
                OntologyCombatController.RequiresApproach(
                    "attack_contact_approach_required"),
                Is.True);
            Assert.That(
                OntologyCombatController.ShouldRetryWithoutMovement(
                    "attack_preview_rejected:action_target_out_of_range"),
                Is.False);
            Assert.That(
                OntologyCombatController.ShouldRetryWithoutMovement(
                    "attack_route_busy"),
                Is.True);
            Assert.That(
                OntologyCombatController.ShouldRetryWithoutMovement(
                    "attack_preview_rejected:action_cooldown_active"),
                Is.True);
        }

        [Test]
        public void CombatRejection_RemovedRuleDoesNotFallBackToApproach()
        {
            const string missingRule =
                "attack_preview_rejected:action_rule_not_enabled";
            Assert.That(
                OntologyCombatController.RequiresApproach(missingRule),
                Is.False);
            Assert.That(
                OntologyCombatController.ShouldRetryWithoutMovement(
                    missingRule),
                Is.False);
        }

        [Test]
        public void CreatorNpcVisibleBounds_ConsumeClickWithoutSolidCollider()
        {
            var root = new GameObject("CreatorNpcClickTarget");
            root.transform.position = new Vector3(0f, 1000f, 0f);
            try
            {
                var appearance = root.AddComponent<OntologyCreatorNpcAppearance>();
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.name = "Visual";
                visual.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(visual.GetComponent<Collider>());

                var notified = false;
                appearance.Clicked += _ => notified = true;
                var ray = new Ray(
                    new Vector3(0f, 1000f, -5f),
                    Vector3.forward);

                Assert.That(
                    OntologyInputSystemPlayerInput.TryConsumeCreatorNpcClick(ray, 10f),
                    Is.True);
                Assert.That(notified, Is.True);
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CreatorNpcMiss_DoesNotConsumeClick()
        {
            var root = new GameObject("CreatorNpcClickTarget");
            root.transform.position = new Vector3(0f, 1000f, 0f);
            try
            {
                var appearance = root.AddComponent<OntologyCreatorNpcAppearance>();
                var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                visual.transform.SetParent(root.transform, false);
                Object.DestroyImmediate(visual.GetComponent<Collider>());

                var notified = false;
                appearance.Clicked += _ => notified = true;
                var ray = new Ray(
                    new Vector3(5f, 1000f, -5f),
                    Vector3.forward);

                Assert.That(
                    OntologyInputSystemPlayerInput.TryConsumeCreatorNpcClick(ray, 10f),
                    Is.False);
                Assert.That(notified, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CreatorNpcAppearance_DisablesReintroducedPhysicalCollision()
        {
            var root = new GameObject("CreatorNpcNonBlocking");
            try
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.transform.SetParent(root.transform, false);
                var body = visual.AddComponent<Rigidbody>();

                Assert.That(
                    OntologyCreatorNpcAppearance.EnsureNonBlockingPresentation(root.transform),
                    Is.EqualTo(1));
                Assert.That(visual.GetComponent<Collider>().enabled, Is.False);
                Assert.That(body.detectCollisions, Is.False);
                Assert.That(body.isKinematic, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ClickTravel_IsClampedBeforeCrossingStopRadius()
        {
            Assert.That(
                OntologyInputSystemPlayerInput.ClampClickTravelDistance(
                    requestedDistance: 0.5f,
                    planarDistanceToTarget: 0.25f,
                    stopDistance: 0.1f),
                Is.EqualTo(0.15f).Within(0.0001f));
            Assert.That(
                OntologyInputSystemPlayerInput.ClampClickTravelDistance(
                    requestedDistance: 0.5f,
                    planarDistanceToTarget: 0.08f,
                    stopDistance: 0.1f),
                Is.Zero);
        }

        [Test]
        public void ClickDestination_InsideArrivalToleranceStopsMovementIntent()
        {
            Assert.That(
                OntologyInputSystemPlayerInput.HasReachedClickDestination(
                    planarDistanceToTarget: 0.105f,
                    stopDistance: 0.1f,
                    arrivalTolerance: 0.01f),
                Is.True);
        }

        [Test]
        public void ClickDestination_OutsideArrivalToleranceKeepsMoving()
        {
            Assert.That(
                OntologyInputSystemPlayerInput.HasReachedClickDestination(
                    planarDistanceToTarget: 0.12f,
                    stopDistance: 0.1f,
                    arrivalTolerance: 0.01f),
                Is.False);
        }

        [Test]
        public void DurableProjection_DoesNotOwnLocalControlledAvatarTransform()
        {
            var localAvatar = new GameObject("LocalAvatar");
            try
            {
                var identity =
                    localAvatar.AddComponent<OntologyAuthorityEntityIdentity>();
                localAvatar.AddComponent<OntologyInputSystemPlayerInput>();

                Assert.That(
                    OntologyWorldAuthorityBridge.ShouldApplyProjectedTransform(
                        identity,
                        identity),
                    Is.False);
                Assert.That(
                    OntologyWorldAuthorityBridge.ShouldApplyProjectedTransform(
                        identity,
                        null),
                    Is.False,
                    "The local-input contract must remain safe while scene references are rebinding.");
            }
            finally
            {
                Object.DestroyImmediate(localAvatar);
            }
        }

        [Test]
        public void DurableProjection_StillOwnsPlacedObjectTransform()
        {
            var localAvatar = new GameObject("LocalAvatar");
            var placedObject = new GameObject("PlacedObject");
            try
            {
                var localIdentity =
                    localAvatar.AddComponent<OntologyAuthorityEntityIdentity>();
                var placedIdentity =
                    placedObject.AddComponent<OntologyAuthorityEntityIdentity>();

                Assert.That(
                    OntologyWorldAuthorityBridge.ShouldApplyProjectedTransform(
                        placedIdentity,
                        localIdentity),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(placedObject);
                Object.DestroyImmediate(localAvatar);
            }
        }

        [Test]
        public void MotionDriverLeaseFailsClosedWhenPhysicalMeaningIsMissing()
        {
            var actor = new GameObject("MotionLeaseActor");
            try
            {
                Assert.That(
                    OntologyMotionDriverAdapter.Allows(
                        actor.transform,
                        OntologyMotionDriver.LocalCharacterController),
                    Is.False,
                    "A missing Physical Meaning adapter is not permission to move.");

                var lease =
                    actor.AddComponent<OntologyMotionDriverAdapter>();
                lease.Configure(
                    OntologyMotionDriver.LocalCharacterController);
                Assert.That(
                    OntologyMotionDriverAdapter.Allows(
                        actor.transform,
                        OntologyMotionDriver.LocalCharacterController),
                    Is.True);

                lease.enabled = false;
                Assert.That(
                    OntologyMotionDriverAdapter.Allows(
                        actor.transform,
                        OntologyMotionDriver.LocalCharacterController),
                    Is.False,
                    "Removing or disabling the derived adapter removes movement.");
            }
            finally
            {
                Object.DestroyImmediate(actor);
            }
        }

        [Test]
        public void CharacterStepHeightIsDerivedFromPhysicalMeaning()
        {
            var actor = new GameObject("PhysicalStepActor");
            OntologyPhysicalProfile profile = null;
            try
            {
                profile =
                    ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
                profile.motionDriver =
                    OntologyMotionDriver.LocalCharacterController;
                profile.maximumStepHeight = 0.42f;
                var coordinator =
                    actor.AddComponent<OntologyCharacterMotionCoordinator>();

                coordinator.Configure(profile);
                Assert.That(
                    coordinator.MaximumStepHeight,
                    Is.EqualTo(0.42f).Within(0.0001f));

                coordinator.Configure(null);
                Assert.That(
                    coordinator.MaximumStepHeight,
                    Is.Zero,
                    "Removing Physical Meaning must clear derived collision tuning.");
            }
            finally
            {
                if (profile != null) Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actor);
            }
        }

        [Test]
        public void LocalMotionProfileDoesNotClaimAnUncontrolledRemoteTransform()
        {
            var localAvatar = new GameObject("LocalAvatar");
            var remoteAvatar = new GameObject("RemoteAvatar");
            try
            {
                var localIdentity =
                    localAvatar.AddComponent<OntologyAuthorityEntityIdentity>();
                var remoteIdentity =
                    remoteAvatar.AddComponent<
                        OntologyAuthorityEntityIdentity>();
                remoteAvatar
                    .AddComponent<OntologyMotionDriverAdapter>()
                    .Configure(
                        OntologyMotionDriver.LocalCharacterController);

                Assert.That(
                    OntologyWorldAuthorityBridge.ShouldApplyProjectedTransform(
                        remoteIdentity,
                        localIdentity),
                    Is.True,
                    "A profile alone cannot turn a remote entity into the " +
                    "locally controlled avatar.");
            }
            finally
            {
                Object.DestroyImmediate(remoteAvatar);
                Object.DestroyImmediate(localAvatar);
            }
        }

        [Test]
        public void DurableProjection_YieldsToOntologySelectedDynamicPhysics()
        {
            var localAvatar = new GameObject("LocalAvatar");
            var placedObject = new GameObject("DynamicPlacedObject");
            OntologyPhysicalProfile profile = null;
            try
            {
                var localIdentity =
                    localAvatar.AddComponent<OntologyAuthorityEntityIdentity>();
                var placedIdentity =
                    placedObject.AddComponent<OntologyAuthorityEntityIdentity>();
                placedObject.AddComponent<OntologyObject>();
                profile = ScriptableObject.CreateInstance<OntologyPhysicalProfile>();
                profile.profileId = "TestDynamic";
                profile.mobilityMode = OntologyPhysicalMobilityMode.Dynamic;
                profile.motionDriver = OntologyMotionDriver.Rigidbody;
                placedObject
                    .AddComponent<OntologyMotionDriverAdapter>()
                    .Configure(profile.motionDriver);
                var body =
                    placedObject.AddComponent<OntologyPhysicalBodyAdapter>();
                body.Configure(profile);

                Assert.That(
                    OntologyTransformOwnershipResolver.Resolve(
                        placedIdentity,
                        localIdentity),
                    Is.EqualTo(OntologyTransformOwner.RuntimePhysics));
                Assert.That(
                    OntologyWorldAuthorityBridge.ShouldApplyProjectedTransform(
                        placedIdentity,
                        localIdentity),
                    Is.False,
                    "An active Dynamic profile must not be snapped back by a projection refresh.");
                Assert.That(
                    OntologyTransformOwnershipResolver.ShouldSeedDynamicProjection(
                        OntologyTransformOwner.RuntimePhysics,
                        hasAlreadySeeded: false),
                    Is.True);
                Assert.That(
                    OntologyTransformOwnershipResolver.ShouldSeedDynamicProjection(
                        OntologyTransformOwner.RuntimePhysics,
                        hasAlreadySeeded: true),
                    Is.False);
            }
            finally
            {
                if (profile != null) Object.DestroyImmediate(profile);
                Object.DestroyImmediate(placedObject);
                Object.DestroyImmediate(localAvatar);
            }
        }

        [Test]
        public void DynamicCheckpoint_RequiresMeaningfulSettledTransformChange()
        {
            Assert.That(
                OntologyDynamicTransformCheckpointAdapter.HasMeaningfulChange(
                    Vector3.zero,
                    Quaternion.identity,
                    new Vector3(0.005f, 0f, 0f),
                    Quaternion.Euler(0f, 0.1f, 0f),
                    minimumPositionChange: 0.02f,
                    minimumRotationChange: 0.5f),
                Is.False);
            Assert.That(
                OntologyDynamicTransformCheckpointAdapter.HasMeaningfulChange(
                    new Vector3(0f, -0.1f, 0f),
                    Quaternion.identity,
                    Vector3.zero,
                    Quaternion.identity,
                    minimumPositionChange: 0.02f,
                    minimumRotationChange: 0.5f),
                Is.True);
        }

        [Test]
        public void EntryGrounding_ClearsOnlyEphemeralVerticalVelocity()
        {
            var player = new GameObject("GroundedPlayer");
            try
            {
                player.AddComponent<CharacterController>();
                var input =
                    player.AddComponent<OntologyInputSystemPlayerInput>();
                typeof(OntologyInputSystemPlayerInput)
                    .GetField(
                        "verticalVelocity",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(input, -12f);

                input.ResetVerticalMotionAfterGrounding();

                Assert.That(input.VerticalVelocity, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void DurableProjection_YieldsAndRestoresOwnershipForAttachmentPresentation()
        {
            var localAvatar = new GameObject("LocalAvatar");
            var placedObject = new GameObject("DataDefinedAttachment");
            try
            {
                var localIdentity =
                    localAvatar.AddComponent<OntologyAuthorityEntityIdentity>();
                var placedIdentity =
                    placedObject.AddComponent<OntologyAuthorityEntityIdentity>();

                Assert.That(
                    OntologyWorldAuthorityBridge.ShouldApplyProjectedTransform(
                        placedIdentity,
                        localIdentity,
                        presentationOwnsTransform: true),
                    Is.False,
                    "An active attachment relation owns the Unity Transform presentation.");
                Assert.That(
                    OntologyWorldAuthorityBridge.ShouldApplyProjectedTransform(
                        placedIdentity,
                        localIdentity,
                        presentationOwnsTransform: false),
                    Is.True,
                    "Removing the relation must return Transform ownership to Authority.");
            }
            finally
            {
                Object.DestroyImmediate(placedObject);
                Object.DestroyImmediate(localAvatar);
            }
        }

        private static void SetPendingClick(
            OntologyInputSystemPlayerInput input,
            bool value)
        {
            var serialized = new SerializedObject(input);
            serialized.FindProperty("hasClickTarget").boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool ReadPendingClick(
            OntologyInputSystemPlayerInput input)
        {
            var serialized = new SerializedObject(input);
            return serialized.FindProperty("hasClickTarget").boolValue;
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            target.GetType()
                .GetField(
                    fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static T ReadPrivateField<T>(
            object target,
            string fieldName)
        {
            return (T)target.GetType()
                .GetField(
                    fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.GetValue(target);
        }

        private static object InvokePrivate(
            object target,
            string methodName,
            params object[] arguments)
        {
            return target.GetType()
                .GetMethod(
                    methodName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(target, arguments);
        }

        private static void SetPrivateStaticField(
            System.Type type,
            string fieldName,
            object value)
        {
            type.GetField(
                    fieldName,
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(null, value);
        }
    }
}
