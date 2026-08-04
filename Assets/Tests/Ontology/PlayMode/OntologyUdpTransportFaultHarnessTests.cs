using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using Tormia.Ontology.Realtime.Protocol;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyUdpTransportFaultHarnessTests
    {
        [UnityTest]
        public IEnumerator AdmissionAndInboundUseInjectedTransportBoundaries()
        {
            var fixture = new Fixture();
            try
            {
                var admitted = false;
                yield return fixture.Transport.PrepareUdpAdmissionRoutine(
                    fixture.AvatarId, value => admitted = value);

                Assert.That(admitted, Is.True,
                    fixture.Transport.LastStatus + "; transitions=" +
                    fixture.Control.TransitionRequests + "; ready=" +
                    fixture.Driver.IsReady);
                Assert.That(fixture.Driver.StartCount, Is.EqualTo(1));
                Assert.That(fixture.Control.TicketRequests, Is.EqualTo(1));
                Assert.That(fixture.Control.TransitionRequests, Is.EqualTo(1));
                Assert.That(fixture.Transport.IsUdpWriterActive, Is.True);

                Assert.That(fixture.Transport.TrySendAuthenticatedMotionIntent(
                    fixture.AvatarId, "default", 1, Vector2.up, 3f,
                    false, Vector2.zero, 0f), Is.True);
                Assert.That(fixture.Driver.Sent.Count, Is.EqualTo(1));
                Assert.That(fixture.Transport.Counters.udpSent, Is.EqualTo(1));

                fixture.Driver.BlockOutbound = true;
                Assert.That(fixture.Transport.TrySendAuthenticatedMotionIntent(
                    fixture.AvatarId, "default", 2, Vector2.up, 3f,
                    false, Vector2.zero, 0f), Is.False);
                Assert.That(fixture.Transport.Counters.udpDropped,
                    Is.EqualTo(1));
            }
            finally { fixture.Dispose(); }
        }

        [UnityTest]
        public IEnumerator LostFallbackResponseRetriesIdempotentlyAndRecovers()
        {
            var fixture = new Fixture();
            try
            {
                yield return fixture.Transport.PrepareUdpAdmissionRoutine(
                    fixture.AvatarId);
                fixture.Control.DropNextFallback = true;
                fixture.Driver.RaiseNetworkFailure("simulated_loss");
                yield return null;
                yield return null;
                Assert.That(fixture.Transport.IsUdpWriterActive, Is.False);

                fixture.Clock.Advance(1f);
                for (var frame = 0; frame < 6; frame++) yield return null;

                Assert.That(fixture.Transport.Counters.fallbackAttempts,
                    Is.GreaterThanOrEqualTo(2));
                Assert.That(fixture.Transport.Counters.fallbackSuccesses,
                    Is.EqualTo(1), "fallback success");
                Assert.That(fixture.Control.RecoveryRequests,
                    Is.EqualTo(1), "recovery request");

                yield return fixture.Transport.PrepareUdpAdmissionRoutine(
                    fixture.AvatarId);
                Assert.That(fixture.Driver.StartCount, Is.EqualTo(2));
                Assert.That(fixture.Control.TicketRequests, Is.EqualTo(2));
            }
            finally { fixture.Dispose(); }
        }

        [UnityTest]
        public IEnumerator DelayedAuthorityAckTimesOutAndFencesUdpWriter()
        {
            var fixture = new Fixture();
            try
            {
                yield return fixture.Transport.PrepareUdpAdmissionRoutine(
                    fixture.AvatarId);
                OntologyAuthorityRuntimeIntentResult result = null;
                fixture.Start(fixture.Transport.SendPlayerIntentRoutine(
                    fixture.AvatarId, "default", 1, Vector2.up, 3f,
                    false, Vector2.zero, 0f, null,
                    value => result = value));
                yield return null;
                fixture.Clock.Advance(2f);
                for (var frame = 0; frame < 3; frame++) yield return null;

                Assert.That(result, Is.Not.Null);
                Assert.That(result.accepted, Is.False);
                Assert.That(result.rejectionCode,
                    Is.EqualTo("authority_udp_acknowledgement_timeout"));
                Assert.That(fixture.Transport.Counters.ackTimeouts,
                    Is.EqualTo(1));
                Assert.That(fixture.Transport.IsUdpWriterActive, Is.False);
            }
            finally { fixture.Dispose(); }
        }

        [UnityTest]
        public IEnumerator ExpiredTicketCannotStartSocketAndReissueCanRecover()
        {
            var fixture = new Fixture();
            try
            {
                fixture.Control.ExpireNextTicket = true;
                var admitted = true;
                yield return fixture.Transport.PrepareUdpAdmissionRoutine(
                    fixture.AvatarId, value => admitted = value);
                Assert.That(admitted, Is.False);
                Assert.That(fixture.Driver.StartCount, Is.Zero);

                yield return fixture.Transport.PrepareUdpAdmissionRoutine(
                    fixture.AvatarId, value => admitted = value);
                Assert.That(admitted, Is.True, fixture.Transport.LastStatus);
                Assert.That(fixture.Driver.StartCount, Is.EqualTo(1));
                Assert.That(fixture.Control.TicketRequests, Is.EqualTo(2));
            }
            finally { fixture.Dispose(); }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject host = new("UdpFaultHarness");
            internal readonly Guid WorldId = Guid.NewGuid();
            internal readonly Guid AvatarId = Guid.NewGuid();
            internal readonly Guid RuntimeSessionId = Guid.NewGuid();
            internal readonly Guid TransportSessionId = Guid.NewGuid();
            internal readonly FakeClock Clock = new();
            internal readonly FakeUdpDriver Driver = new();
            internal readonly FakeControlPlane Control;
            internal readonly OntologyWorldAuthorityUdpMotionTransport Transport;

            internal Fixture()
            {
                var authority = host.AddComponent<OntologyWorldAuthorityClient>();
                Set(authority, "currentUserId", Guid.NewGuid().ToString("D"));
                Set(authority, "currentWorldId", WorldId.ToString("D"));
                Set(authority, "hasRuntimeProjectionZoneScope", true);
                Set(authority, "runtimeProjectionZoneKey", "default");
                Set(authority, "activeRuntimeAvatarId", AvatarId);
                Set(authority, "activeRuntimeAvatarZoneKey", "default");
                Set(authority, "activeRuntimeSessionId",
                    RuntimeSessionId.ToString("D"));
                Set(authority, "activeMotionWriterMode", "http");
                Set(authority, "activeMotionWriterEpoch", 1L);
                Set(authority, "activeMotionWriterWorldRevision", 10L);
                Control = new FakeControlPlane(this);
                Transport = host.AddComponent<
                    OntologyWorldAuthorityUdpMotionTransport>();
                Set(Transport, "enableUdpMotionWriter", true);
                Set(Transport, "enableUdpSnapshotReceiver", true);
                Set(Transport, "udpHost", "test.invalid");
                Set(Transport, "udpPort", 7777);
                Set(Transport, "connectTimeoutSeconds", 3f);
                Set(Transport, "authorityAckTimeoutSeconds", 1.5f);
                Transport.ConfigureRuntimeSeams(Clock, Driver, Control);
            }

            public void Dispose() => Object.DestroyImmediate(host);
            internal Coroutine Start(IEnumerator routine) =>
                host.GetComponent<OntologyWorldAuthorityUdpMotionTransport>()
                    .StartCoroutine(routine);

            private static void Set(object target, string name, object value) =>
                target.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(target, value);
        }

        private sealed class FakeClock : IAuthorityRealtimeClock
        {
            public float MonotonicSeconds { get; private set; }
            public long UtcMilliseconds =>
                10_000 + (long)(MonotonicSeconds * 1000f);
            internal void Advance(float seconds) => MonotonicSeconds += seconds;
        }

        private sealed class FakeUdpDriver : IAuthorityUdpClientDriver
        {
            public bool IsReady { get; private set; }
            public bool BlockOutbound { get; set; }
            public int StartCount { get; private set; }
            private bool pendingConnected;
            public List<byte[]> Sent { get; } = new();
            public event Action Connected;
            public event Action<string> Disconnected;
            public event Action<string> NetworkFailed;
            public event Action<byte[]> DatagramReceived;

            public bool Start(string host, int port, byte[] bootstrap)
            {
                StartCount++;
                IsReady = true;
                pendingConnected = true;
                return true;
            }

            public bool TrySend(byte[] datagram)
            {
                if (!IsReady || BlockOutbound) return false;
                Sent.Add((byte[])datagram.Clone());
                return true;
            }

            public void Poll()
            {
                if (!pendingConnected) return;
                pendingConnected = false;
                Connected?.Invoke();
            }
            public void Close() => IsReady = false;
            internal void RaiseDisconnected(string reason)
            {
                IsReady = false;
                Disconnected?.Invoke(reason);
            }
            internal void RaiseNetworkFailure(string reason)
            {
                IsReady = false;
                NetworkFailed?.Invoke(reason);
            }
        }

        private sealed class FakeControlPlane : IAuthorityMotionControlPlane
        {
            private readonly Fixture fixture;
            internal int TicketRequests { get; private set; }
            internal int TransitionRequests { get; private set; }
            internal int RecoveryRequests { get; private set; }
            internal bool ExpireNextTicket { get; set; }
            internal bool DropNextFallback { get; set; }
            private Guid issuedTransportSessionId;
            private ulong issuedTransportGeneration;

            internal FakeControlPlane(Fixture fixture) => this.fixture = fixture;

            public IEnumerator IssueTicket(Guid avatarEntityId,
                string runtimeSessionId, ushort protocolVersion,
                Action<OntologyAuthorityUdpTransportTicket> completed)
            {
                TicketRequests++;
                issuedTransportSessionId = Guid.NewGuid();
                issuedTransportGeneration = (ulong)(6 + TicketRequests);
                var expired = ExpireNextTicket;
                ExpireNextTicket = false;
                completed(new OntologyAuthorityUdpTransportTicket
                {
                    accepted = true,
                    ticket = Convert.ToBase64String(new byte[32]),
                    datagramAuthenticationKey =
                        Convert.ToBase64String(new byte[32]),
                    expiresAtUnixMilliseconds = expired ? 1 : 100_000,
                    authenticatedTransportSessionId =
                        issuedTransportSessionId.ToString("D"),
                    runtimeSessionId = fixture.RuntimeSessionId.ToString("D"),
                    transportGeneration = issuedTransportGeneration,
                    protocolVersion = protocolVersion,
                    writerMode = "http",
                    writerEpoch = 1,
                    worldRevision = 10
                });
                yield break;
            }

            public IEnumerator TransitionWriter(Guid avatarEntityId,
                OntologyAuthorityUdpTransportTicket ticket, bool promoteUdp,
                Action<OntologyAuthorityMotionTransportTransition> completed)
            {
                TransitionRequests++;
                if (!promoteUdp && DropNextFallback)
                {
                    DropNextFallback = false;
                    completed(null);
                    yield break;
                }
                completed(new OntologyAuthorityMotionTransportTransition
                {
                    accepted = true,
                    writerMode = promoteUdp ? "udp" : "http",
                    writerEpoch = ticket.writerEpoch + 1,
                    worldRevision = ticket.worldRevision,
                    runtimeSessionId = fixture.RuntimeSessionId.ToString("D"),
                    authenticatedTransportSessionId = promoteUdp
                        ? issuedTransportSessionId.ToString("D") : null,
                    transportGeneration = promoteUdp
                        ? issuedTransportGeneration : 0UL,
                    previousAuthenticatedTransportSessionId = promoteUdp
                        ? null : issuedTransportSessionId.ToString("D"),
                    previousTransportGeneration = promoteUdp
                        ? 0UL : issuedTransportGeneration
                });
                yield break;
            }

            public IEnumerator LoadCompleteZoneRecovery(string zoneKey,
                Action<IReadOnlyList<OntologyAuthorityPlayerMotionState>> completed)
            {
                RecoveryRequests++;
                completed(new[]
                {
                    new OntologyAuthorityPlayerMotionState
                    {
                        worldId = fixture.WorldId.ToString("D"),
                        avatarEntityId = fixture.AvatarId.ToString("D"),
                        runtimeSessionId = fixture.RuntimeSessionId.ToString("D"),
                        zoneKey = zoneKey,
                        lastProcessedIntentSequence = 0
                    }
                });
                yield break;
            }
        }
    }
}
