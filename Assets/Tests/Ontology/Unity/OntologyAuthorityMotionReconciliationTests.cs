using System;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyAuthorityMotionReconciliationTests
    {
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
    }
}
