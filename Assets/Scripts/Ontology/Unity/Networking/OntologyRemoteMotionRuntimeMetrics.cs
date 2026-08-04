using System;
using UnityEngine;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Presentation-only, in-process telemetry for the Authority remote-motion
    /// transport. It records transport and interpolation health without
    /// participating in input, rule evaluation, Authority decisions, or world
    /// persistence. Attach it beside the realtime client to inspect the current
    /// session while playing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OntologyRemoteMotionRuntimeMetrics : MonoBehaviour
    {
        [Header("Realtime connection")]
        [SerializeField] private long connectionAttempts;
        [SerializeField] private long reconnectAttempts;
        [SerializeField] private long connectionSuccesses;
        [SerializeField] private long connectionFailures;
        [SerializeField] private int consecutiveConnectionFailures;
        [SerializeField] private float lastReconnectDelaySeconds;
        [SerializeField, TextArea] private string lastReconnectReason;

        [Header("Motion frame transport")]
        [SerializeField] private long motionFramesReceived;
        [SerializeField] private long motionStatesReceived;
        [SerializeField] private long lastMotionFrameServerTick;
        [SerializeField] private long lastMotionFrameObservedAtUnixMilliseconds;

        [Header("Remote snapshot timeline")]
        [SerializeField] private long snapshotsAccepted;
        [SerializeField] private long staleSnapshotsRejected;
        [SerializeField] private long reorderedSnapshotsRejected;
        [SerializeField] private long timelineBaselines;
        [SerializeField] private long timelineResets;
        [SerializeField] private long bufferUnderrunEpisodes;
        [SerializeField] private long extrapolationEpisodes;
        [SerializeField] private float lastSnapshotAgeMilliseconds = -1f;
        [SerializeField] private float averageSnapshotAgeMilliseconds = -1f;
        [SerializeField] private long snapshotAgeSampleCount;
        [SerializeField] private long lastAcceptedServerTick;
        [SerializeField] private long lastRejectedServerTick;

        /// <summary>
        /// A point-in-time value snapshot for tests, diagnostics overlays, and
        /// support reports. It has no gameplay authority.
        /// </summary>
        public OntologyRemoteMotionRuntimeMetricsSnapshot Snapshot =>
            new OntologyRemoteMotionRuntimeMetricsSnapshot(
                connectionAttempts,
                reconnectAttempts,
                connectionSuccesses,
                connectionFailures,
                consecutiveConnectionFailures,
                lastReconnectDelaySeconds,
                lastReconnectReason,
                motionFramesReceived,
                motionStatesReceived,
                lastMotionFrameServerTick,
                lastMotionFrameObservedAtUnixMilliseconds,
                snapshotsAccepted,
                staleSnapshotsRejected,
                reorderedSnapshotsRejected,
                timelineBaselines,
                timelineResets,
                bufferUnderrunEpisodes,
                extrapolationEpisodes,
                lastSnapshotAgeMilliseconds,
                averageSnapshotAgeMilliseconds,
                lastAcceptedServerTick,
                lastRejectedServerTick);

        public void RecordConnectionAttempt(bool reconnect)
        {
            connectionAttempts++;
            if (reconnect)
            {
                reconnectAttempts++;
            }
        }

        public void RecordConnectionFailure(
            string reason,
            float retryDelaySeconds,
            int consecutiveFailures)
        {
            connectionFailures++;
            consecutiveConnectionFailures = Mathf.Max(0, consecutiveFailures);
            lastReconnectDelaySeconds = Mathf.Max(0f, retryDelaySeconds);
            lastReconnectReason = reason ?? string.Empty;
        }

        public void RecordConnectionSucceeded()
        {
            connectionSuccesses++;
            consecutiveConnectionFailures = 0;
            lastReconnectReason = string.Empty;
            lastReconnectDelaySeconds = 0f;
        }

        public void RecordMotionFrameReceived(
            long serverTick,
            long observedAtUnixMilliseconds,
            int stateCount)
        {
            motionFramesReceived++;
            motionStatesReceived += Mathf.Max(0, stateCount);
            lastMotionFrameServerTick = serverTick;
            lastMotionFrameObservedAtUnixMilliseconds =
                observedAtUnixMilliseconds;
        }

        public void RecordSnapshotAccepted(
            long serverTick,
            long updatedAtUnixMilliseconds)
        {
            snapshotsAccepted++;
            lastAcceptedServerTick = serverTick;
            RecordSnapshotAge(updatedAtUnixMilliseconds);
        }

        public void RecordSnapshotRejected(
            long serverTick,
            bool outOfOrder)
        {
            lastRejectedServerTick = serverTick;
            if (outOfOrder)
            {
                reorderedSnapshotsRejected++;
            }
            else
            {
                staleSnapshotsRejected++;
            }
        }

        public void RecordTimelineBaseline(bool reset)
        {
            timelineBaselines++;
            if (reset)
            {
                timelineResets++;
            }
        }

        public void RecordBufferUnderrunEpisode()
        {
            bufferUnderrunEpisodes++;
        }

        public void RecordExtrapolationEpisode()
        {
            extrapolationEpisodes++;
        }

        private void RecordSnapshotAge(long updatedAtUnixMilliseconds)
        {
            if (updatedAtUnixMilliseconds <= 0L)
            {
                return;
            }

            // The timestamp is Authority time. This is intentionally labeled an
            // estimate because clock skew is not yet calibrated by a transport
            // ping/echo exchange.
            var age = Mathf.Max(
                0f,
                (float)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() -
                        updatedAtUnixMilliseconds));
            lastSnapshotAgeMilliseconds = age;
            snapshotAgeSampleCount++;
            averageSnapshotAgeMilliseconds = snapshotAgeSampleCount == 1L
                ? age
                : averageSnapshotAgeMilliseconds +
                  (age - averageSnapshotAgeMilliseconds) /
                  snapshotAgeSampleCount;
        }
    }

    /// <summary>
    /// Immutable read-only projection of <see cref="OntologyRemoteMotionRuntimeMetrics"/>.
    /// Values are session-local diagnostic observations, not durable world Facts.
    /// </summary>
    public readonly struct OntologyRemoteMotionRuntimeMetricsSnapshot
    {
        public OntologyRemoteMotionRuntimeMetricsSnapshot(
            long connectionAttempts,
            long reconnectAttempts,
            long connectionSuccesses,
            long connectionFailures,
            int consecutiveConnectionFailures,
            float lastReconnectDelaySeconds,
            string lastReconnectReason,
            long motionFramesReceived,
            long motionStatesReceived,
            long lastMotionFrameServerTick,
            long lastMotionFrameObservedAtUnixMilliseconds,
            long snapshotsAccepted,
            long staleSnapshotsRejected,
            long reorderedSnapshotsRejected,
            long timelineBaselines,
            long timelineResets,
            long bufferUnderrunEpisodes,
            long extrapolationEpisodes,
            float lastSnapshotAgeMilliseconds,
            float averageSnapshotAgeMilliseconds,
            long lastAcceptedServerTick,
            long lastRejectedServerTick)
        {
            ConnectionAttempts = connectionAttempts;
            ReconnectAttempts = reconnectAttempts;
            ConnectionSuccesses = connectionSuccesses;
            ConnectionFailures = connectionFailures;
            ConsecutiveConnectionFailures = consecutiveConnectionFailures;
            LastReconnectDelaySeconds = lastReconnectDelaySeconds;
            LastReconnectReason = lastReconnectReason ?? string.Empty;
            MotionFramesReceived = motionFramesReceived;
            MotionStatesReceived = motionStatesReceived;
            LastMotionFrameServerTick = lastMotionFrameServerTick;
            LastMotionFrameObservedAtUnixMilliseconds =
                lastMotionFrameObservedAtUnixMilliseconds;
            SnapshotsAccepted = snapshotsAccepted;
            StaleSnapshotsRejected = staleSnapshotsRejected;
            ReorderedSnapshotsRejected = reorderedSnapshotsRejected;
            TimelineBaselines = timelineBaselines;
            TimelineResets = timelineResets;
            BufferUnderrunEpisodes = bufferUnderrunEpisodes;
            ExtrapolationEpisodes = extrapolationEpisodes;
            LastSnapshotAgeMilliseconds = lastSnapshotAgeMilliseconds;
            AverageSnapshotAgeMilliseconds = averageSnapshotAgeMilliseconds;
            LastAcceptedServerTick = lastAcceptedServerTick;
            LastRejectedServerTick = lastRejectedServerTick;
        }

        public long ConnectionAttempts { get; }
        public long ReconnectAttempts { get; }
        public long ConnectionSuccesses { get; }
        public long ConnectionFailures { get; }
        public int ConsecutiveConnectionFailures { get; }
        public float LastReconnectDelaySeconds { get; }
        public string LastReconnectReason { get; }
        public long MotionFramesReceived { get; }
        public long MotionStatesReceived { get; }
        public long LastMotionFrameServerTick { get; }
        public long LastMotionFrameObservedAtUnixMilliseconds { get; }
        public long SnapshotsAccepted { get; }
        public long StaleSnapshotsRejected { get; }
        public long ReorderedSnapshotsRejected { get; }
        public long TimelineBaselines { get; }
        public long TimelineResets { get; }
        public long BufferUnderrunEpisodes { get; }
        public long ExtrapolationEpisodes { get; }
        public float LastSnapshotAgeMilliseconds { get; }
        public float AverageSnapshotAgeMilliseconds { get; }
        public long LastAcceptedServerTick { get; }
        public long LastRejectedServerTick { get; }
    }
}
