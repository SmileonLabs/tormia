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
    }
}
