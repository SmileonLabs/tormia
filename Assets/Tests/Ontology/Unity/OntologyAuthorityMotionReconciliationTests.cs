using System;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
public sealed class OntologyAuthorityMotionReconciliationTests
{
        [Test]
        public void ResolvedPoseIsPublishedOnlyForAcceptedLocomotionLease()
        {
            Assert.That(
                OntologyWorldAuthorityPlayerMotionReconciler
                    .ShouldPublishResolvedPose(true, true),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityPlayerMotionReconciler
                    .ShouldPublishResolvedPose(true, false),
                Is.False,
                "An expired lease must not receive pose samples.");
            Assert.That(
                OntologyWorldAuthorityPlayerMotionReconciler
                    .ShouldPublishResolvedPose(false, true),
                Is.False);
        }

        [Test]
        public void SnapshotExtrapolationProducesBoundedPlanarCorrection()
        {
            var state = new OntologyAuthorityPlayerMotionState
            {
                avatarEntityId = Guid.NewGuid().ToString("D"),
                zoneKey = "world_main",
                positionX = 1d,
                positionY = 100d,
                positionZ = 0d,
                velocityX = 2d,
                velocityZ = 0d,
                serverTick = 12,
                updatedAtUnixMilliseconds =
                    DateTimeOffset.UtcNow
                        .AddMilliseconds(-100)
                        .ToUnixTimeMilliseconds()
            };

            Assert.That(
                OntologyWorldAuthorityPlayerMotionReconciler
                    .TryResolvePlanarCorrection(
                        state,
                        Vector3.zero,
                        0.15f,
                        1f,
                        4f,
                        out var correction),
                Is.True);
            Assert.That(correction.x, Is.InRange(1.05f, 1.4f));
            Assert.That(correction.y, Is.EqualTo(0f));
        }

        [Test]
        public void StaleSnapshotIsNotUsedForCorrection()
        {
            var state = new OntologyAuthorityPlayerMotionState
            {
                positionX = 10d,
                positionZ = 10d,
                serverTick = 1,
                updatedAtUnixMilliseconds =
                    DateTimeOffset.UtcNow
                        .AddSeconds(-5)
                        .ToUnixTimeMilliseconds()
            };

            Assert.That(
                OntologyWorldAuthorityPlayerMotionReconciler
                    .TryResolvePlanarCorrection(
                        state,
                        Vector3.zero,
                        0.15f,
                        1f,
                        4f,
                        out _),
                Is.False);
        }

        [Test]
        public void AcceptedStopSequenceClearsMovingCorrectionResidual()
        {
            var coordinator = CreateCoordinator(out var avatar);
            try
            {
                const long movingSequence = 20;
                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.right,
                        serverTick: 100,
                        processedIntentSequence: movingSequence,
                        correctionSpeed: 2f,
                        deadZone: 0.01f),
                    Is.True);
                Assert.That(
                    coordinator.PendingAuthorityPlanarCorrection,
                    Is.EqualTo(Vector3.right));

                coordinator.AdvanceAuthorityIntentFence(
                    movingSequence + 1);

                Assert.That(
                    coordinator.PendingAuthorityPlanarCorrection,
                    Is.EqualTo(Vector3.zero),
                    "An accepted stop must clear correction calculated from " +
                    "the preceding moving input.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void NewerServerTickCannotRestoreOlderProcessedSequence()
        {
            var coordinator = CreateCoordinator(out var avatar);
            try
            {
                const long movingSequence = 30;
                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.right, 200, movingSequence, 2f, 0.01f),
                    Is.True);
                coordinator.AdvanceAuthorityIntentFence(movingSequence + 1);

                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.left,
                        serverTick: 201,
                        processedIntentSequence: movingSequence,
                        correctionSpeed: 2f,
                        deadZone: 0.01f),
                    Is.False,
                    "A newer server tick cannot resurrect a correction for " +
                    "an input sequence superseded by the accepted stop.");
                Assert.That(
                    coordinator.PendingAuthorityPlanarCorrection,
                    Is.EqualTo(Vector3.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void SnapshotAtAcceptedStopSequenceIsAccepted()
        {
            var coordinator = CreateCoordinator(out var avatar);
            try
            {
                const long stopSequence = 41;
                coordinator.AdvanceAuthorityIntentFence(stopSequence);

                Assert.That(
                    OntologyWorldAuthorityPlayerMotionReconciler
                        .IsSnapshotCaughtUp(
                            stopSequence - 1,
                            stopSequence),
                    Is.False);
                Assert.That(
                    OntologyWorldAuthorityPlayerMotionReconciler
                        .IsSnapshotCaughtUp(
                            stopSequence,
                            stopSequence),
                    Is.True);
                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.left,
                        serverTick: 300,
                        processedIntentSequence: stopSequence,
                        correctionSpeed: 2f,
                        deadZone: 0.01f),
                    Is.True);
                Assert.That(
                    coordinator.LastAuthorityProcessedIntentSequence,
                    Is.EqualTo(stopSequence));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void StationaryTargetRequeuesUntilPresentationConverges()
        {
            var state = new OntologyAuthorityPlayerMotionState
            {
                positionX = 12d,
                positionZ = 8d,
                velocityX = 0d,
                velocityZ = 0d
            };
            Assert.That(
                OntologyWorldAuthorityPlayerMotionReconciler
                    .ShouldQueueAuthorityCorrection(
                        state, new Vector3(1f, 0f, 0f), 0.08f),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityPlayerMotionReconciler
                    .ShouldQueueAuthorityCorrection(
                        state, new Vector3(0.04f, 0f, 0f), 0.08f),
                Is.False,
                "A stationary Authority heartbeat stops only after the actual " +
                "presentation residual enters the dead zone.");

            Assert.That(
                OntologyWorldAuthorityPlayerMotionReconciler
                    .ShouldQueueAuthorityCorrection(
                        state,
                        new Vector3(0.2f, 0f, 0f),
                        0.08f),
                Is.True,
                "A repeated stationary target remains authoritative while " +
                "presentation is still outside the dead zone.");
        }

        [Test]
        public void BlockedControllerMovePreservesAuthorityCorrectionResidual()
        {
            var pending = new Vector3(2f, 0f, 0f);
            var requested = new Vector3(0.2f, 0f, 0f);
            Assert.That(
                OntologyCharacterMotionCoordinator
                    .ResolveRemainingAuthorityCorrection(
                        pending,
                        requested,
                        Vector3.zero),
                Is.EqualTo(pending),
                "A collision-blocked correction must not be consumed as if " +
                "the CharacterController had moved.");
            var partiallyResolved =
                OntologyCharacterMotionCoordinator
                    .ResolveRemainingAuthorityCorrection(
                        pending,
                        requested,
                        new Vector3(0.1f, 0f, 0f));
            Assert.That(
                Vector3.Distance(
                    partiallyResolved,
                    new Vector3(1.9f, 0f, 0f)),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void AcceptedRemovementRejectsPriorStopSequence()
        {
            var coordinator = CreateCoordinator(out var avatar);
            try
            {
                const long stopSequence = 51;
                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.left, 400, stopSequence, 2f, 0.01f),
                    Is.True);

                var movementSequence = stopSequence + 1;
                coordinator.AdvanceAuthorityIntentFence(movementSequence);

                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.left,
                        serverTick: 401,
                        processedIntentSequence: stopSequence,
                        correctionSpeed: 2f,
                        deadZone: 0.01f),
                    Is.False);
                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.right,
                        serverTick: 402,
                        processedIntentSequence: movementSequence,
                        correctionSpeed: 2f,
                        deadZone: 0.01f),
                    Is.True);
                Assert.That(
                    coordinator.PendingAuthorityPlanarCorrection,
                    Is.EqualTo(Vector3.right));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        [Test]
        public void CheckpointReseedResetsTickAndIntentFences()
        {
            var coordinator = CreateCoordinator(out var avatar);
            try
            {
                var reconciler = avatar.AddComponent<
                    OntologyWorldAuthorityPlayerMotionReconciler>();
                var coordinatorField = typeof(
                        OntologyWorldAuthorityPlayerMotionReconciler)
                    .GetField(
                        "motionCoordinator",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(coordinatorField, Is.Not.Null);
                coordinatorField.SetValue(reconciler, coordinator);
                coordinator.AdvanceAuthorityIntentFence(60);
                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.right, 600, 60, 2f, 0.01f),
                    Is.True);

                reconciler.BeginCheckpointReseed();

                Assert.That(reconciler.CheckpointReseedPending, Is.True);
                Assert.That(coordinator.LastAuthorityServerTick, Is.Zero);
                Assert.That(
                    coordinator.LastAuthorityProcessedIntentSequence,
                    Is.Zero);
                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.left, 1, 1, 2f, 0.01f),
                    Is.True,
                    "Beginning a checkpoint reseed must clear the old " +
                    "accepted-input fence.");

                reconciler.CompleteCheckpointReseed(
                    accepted: true,
                    confirmedPosition: new Vector3(4f, 2f, 6f));

                Assert.That(reconciler.CheckpointReseedPending, Is.False);
                Assert.That(coordinator.LastAuthorityServerTick, Is.Zero);
                Assert.That(
                    coordinator.LastAuthorityProcessedIntentSequence,
                    Is.Zero);
                Assert.That(
                    coordinator.QueueAuthorityPlanarCorrection(
                        Vector3.forward, 1, 1, 2f, 0.01f),
                    Is.True,
                    "Completing the reseed must start a fresh runtime " +
                    "snapshot lineage.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }

        private static OntologyCharacterMotionCoordinator CreateCoordinator(
            out GameObject avatar)
        {
            avatar = new GameObject("SequenceAwareMotionCoordinator");
            avatar.AddComponent<CharacterController>();
            avatar.AddComponent<OntologyCharacterSupportProbe>();
            return avatar.AddComponent<OntologyCharacterMotionCoordinator>();
        }
    }
}
