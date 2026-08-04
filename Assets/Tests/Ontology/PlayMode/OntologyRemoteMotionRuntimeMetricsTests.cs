using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyRemoteMotionRuntimeMetricsTests
    {
        [Test]
        public void RuntimeMetricsExposeConnectionFrameAndTimelineEvidence()
        {
            var host = new GameObject("RemoteMotionMetrics");
            try
            {
                var metrics = host.AddComponent<OntologyRemoteMotionRuntimeMetrics>();

                metrics.RecordConnectionAttempt(false);
                metrics.RecordConnectionAttempt(true);
                metrics.RecordConnectionFailure("socket closed", 1.5f, 2);
                metrics.RecordConnectionSucceeded();
                metrics.RecordMotionFrameReceived(120L, 1_000L, 3);
                metrics.RecordSnapshotAccepted(119L, 1L);
                metrics.RecordSnapshotRejected(118L, true);
                metrics.RecordSnapshotRejected(119L, false);
                metrics.RecordTimelineBaseline(false);
                metrics.RecordTimelineBaseline(true);
                metrics.RecordBufferUnderrunEpisode();
                metrics.RecordExtrapolationEpisode();

                var snapshot = metrics.Snapshot;
                Assert.That(snapshot.ConnectionAttempts, Is.EqualTo(2));
                Assert.That(snapshot.ReconnectAttempts, Is.EqualTo(1));
                Assert.That(snapshot.ConnectionFailures, Is.EqualTo(1));
                Assert.That(snapshot.ConnectionSuccesses, Is.EqualTo(1));
                Assert.That(snapshot.ConsecutiveConnectionFailures, Is.EqualTo(0));
                Assert.That(snapshot.MotionFramesReceived, Is.EqualTo(1));
                Assert.That(snapshot.MotionStatesReceived, Is.EqualTo(3));
                Assert.That(snapshot.LastMotionFrameServerTick, Is.EqualTo(120));
                Assert.That(snapshot.SnapshotsAccepted, Is.EqualTo(1));
                Assert.That(snapshot.ReorderedSnapshotsRejected, Is.EqualTo(1));
                Assert.That(snapshot.StaleSnapshotsRejected, Is.EqualTo(1));
                Assert.That(snapshot.TimelineBaselines, Is.EqualTo(2));
                Assert.That(snapshot.TimelineResets, Is.EqualTo(1));
                Assert.That(snapshot.BufferUnderrunEpisodes, Is.EqualTo(1));
                Assert.That(snapshot.ExtrapolationEpisodes, Is.EqualTo(1));
                Assert.That(snapshot.LastSnapshotAgeMilliseconds, Is.GreaterThanOrEqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
