using System;
using System.Reflection;
using System.IO;
using NUnit.Framework;
using Tormia.Ontology.Core;
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
        public void WorldOwnedAvatarIdentityIsStableAndDifferentPerWorld()
        {
            var baseAvatarId = Guid.NewGuid();
            var worldA = Guid.NewGuid();
            var worldB = Guid.NewGuid();

            var first = OntologyWorldAuthorityAccountEntryFlow.CreateWorldScopedAvatarId(
                baseAvatarId,
                worldA);
            var replay = OntologyWorldAuthorityAccountEntryFlow.CreateWorldScopedAvatarId(
                baseAvatarId,
                worldA);
            var otherWorld = OntologyWorldAuthorityAccountEntryFlow.CreateWorldScopedAvatarId(
                baseAvatarId,
                worldB);

            Assert.That(first, Is.Not.EqualTo(Guid.Empty));
            Assert.That(first, Is.Not.EqualTo(baseAvatarId));
            Assert.That(replay, Is.EqualTo(first));
            Assert.That(otherWorld, Is.Not.EqualTo(first));
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

        private static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }
    }
}
