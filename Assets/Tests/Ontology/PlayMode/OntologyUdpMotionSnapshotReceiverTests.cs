using System;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using Tormia.Ontology.Core;
using Tormia.Ontology.Realtime.Protocol;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyUdpMotionSnapshotReceiverTests
    {
        private static readonly Guid TestWorldId = Guid.Parse(
            "11111111-2222-3333-4444-555555555555");
        [Test]
        public void ReceiverPublishesOnlyCompleteAuthenticatedPageSet()
        {
            var fixture = new Fixture();
            try
            {
                fixture.EstablishReliableActor(1);
                var occurrence = Guid.NewGuid();
                var first = fixture.Datagram(10, 100, occurrence, 0, 2, 2,
                    fixture.Actor(fixture.ActorId, 2));
                var second = fixture.Datagram(11, 100, occurrence, 1, 2, 2,
                    fixture.Actor(fixture.ActorId, 2));

                Assert.That(fixture.Process(first), Is.True);
                Assert.That(fixture.Received, Is.Null);
                Assert.That(fixture.Process(second), Is.False,
                    "Duplicate actors across pages must fail closed.");
                Assert.That(fixture.Received, Is.Null);

                occurrence = Guid.NewGuid();
                first = fixture.Datagram(12, 101, occurrence, 0, 2, 2,
                    fixture.Actor(fixture.ActorId, 2));
                second = fixture.Datagram(13, 101, occurrence, 1, 2, 2,
                    fixture.Actor(fixture.SecondActorId, 2));
                fixture.EstablishReliableActor(1, fixture.SecondActorId,
                    fixture.SecondSessionId);

                Assert.That(fixture.Process(second), Is.True);
                Assert.That(fixture.Received, Is.Null);
                Assert.That(fixture.Process(first), Is.True);
                Assert.That(fixture.Received, Is.Not.Null);
                Assert.That(fixture.Received.items, Has.Length.EqualTo(2));
                Assert.That(fixture.Received.frameOccurrenceId,
                    Is.EqualTo(occurrence.ToString("D")));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void ReceiverRejectsTamperReplayAndWrongBinding()
        {
            var fixture = new Fixture();
            try
            {
                fixture.EstablishReliableActor(1);
                var valid = fixture.Datagram(70, 200, Guid.NewGuid(), 0, 1, 1,
                    fixture.Actor(fixture.ActorId, 2));
                var tampered = (byte[])valid.Clone();
                tampered[60] ^= 0x40;
                Assert.That(fixture.Process(tampered), Is.False);
                Assert.That(fixture.Process(valid), Is.True);
                Assert.That(fixture.Process(valid), Is.False);
                Assert.That(fixture.Counters.replayRejected, Is.EqualTo(1));

                var tooOld = fixture.Datagram(5, 199, Guid.NewGuid(),
                    0, 1, 1, fixture.Actor(fixture.ActorId, 2));
                Assert.That(fixture.Process(tooOld), Is.False);
                Assert.That(fixture.Counters.replayRejected, Is.EqualTo(2));

                var wrongWorld = fixture.Datagram(71, 201, Guid.NewGuid(),
                    0, 1, 1, fixture.Actor(fixture.ActorId, 3),
                    Guid.NewGuid());
                Assert.That(fixture.Process(wrongWorld), Is.False);
            }
            finally { fixture.Dispose(); }
        }

        [UnityTest]
        public IEnumerator MissingPageExpiresWithoutPublishingPartialFrame()
        {
            var fixture = new Fixture();
            try
            {
                fixture.EstablishReliableActor(1);
                var occurrence = Guid.NewGuid();
                Assert.That(fixture.Process(fixture.Datagram(10, 100,
                    occurrence, 0, 2, 2,
                    fixture.Actor(fixture.ActorId, 2))), Is.True);
                fixture.AdvanceClock(1f);
                yield return null;
                Assert.That(fixture.Received, Is.Null,
                    "An expired partial snapshot must never be published.");
                Assert.That(fixture.PendingAssemblyCount, Is.Zero);
                Assert.That(fixture.PendingAssemblyBytes, Is.Zero);
                Assert.That(fixture.Counters.assemblyExpired, Is.EqualTo(1));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void IncompleteSnapshotCapacityFailsClosedAndIsCounted()
        {
            var fixture = new Fixture();
            try
            {
                fixture.EstablishReliableActor(1);
                for (ulong sequence = 1; sequence <= 8; sequence++)
                    Assert.That(fixture.Process(fixture.Datagram(sequence, 100,
                        Guid.NewGuid(), 0, 2, 2,
                        fixture.Actor(fixture.ActorId, 2))), Is.True);

                Assert.That(fixture.Process(fixture.Datagram(9, 101,
                    Guid.NewGuid(), 0, 2, 2,
                    fixture.Actor(fixture.ActorId, 2))), Is.False);
                Assert.That(fixture.Counters.assemblyRejected,
                    Is.EqualTo(1));
            }
            finally { fixture.Dispose(); }
        }

        [Test]
        public void FeedRequiresReliableBarrierForRuntimeSessionSwapAndDedupesLane()
        {
            var host = new GameObject("MotionFeed");
            try
            {
                var feed = host.AddComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>();
                var actor = Guid.NewGuid();
                var firstSession = Guid.NewGuid();
                var secondSession = Guid.NewGuid();
                var occurrence = Guid.NewGuid().ToString("D");
                var received = 0;
                feed.ZoneMotionFrameReceived += _ => received++;

                PublishReliable(feed, Frame(actor, firstSession, 1,
                    Guid.NewGuid().ToString("D")));
                Assert.That(received, Is.EqualTo(1));
                Assert.That(feed.PublishAuthenticatedUdpFrame(
                    Frame(actor, secondSession, 2, occurrence)), Is.False);
                Assert.That(received, Is.EqualTo(1));

                PublishReliable(feed,
                    Frame(actor, secondSession, 2, occurrence));
                Assert.That(received, Is.EqualTo(2),
                    "Reliable delivery must establish a session UDP cannot.");

                PublishReliable(feed, Frame(actor, secondSession, 2,
                    Guid.NewGuid().ToString("D")));
                Assert.That(received, Is.EqualTo(2));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void ReliableDuplicateOccurrenceCompletesActorsSkippedByUdp()
        {
            var host = new GameObject("MotionFeed");
            try
            {
                var feed = host.AddComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>();
                var known = Guid.NewGuid();
                var knownSession = Guid.NewGuid();
                var unknown = Guid.NewGuid();
                var unknownSession = Guid.NewGuid();
                var occurrence = Guid.NewGuid().ToString("D");
                OntologyAuthorityZoneMotionFrame received = null;
                feed.ZoneMotionFrameReceived += value => received = value;
                PublishReliable(feed, Frame(known, knownSession, 1,
                    Guid.NewGuid().ToString("D")));

                var udp = Frame(known, knownSession, 2, occurrence);
                udp.items = new[]
                {
                    udp.items[0],
                    State(unknown, unknownSession, 2)
                };
                Assert.That(feed.PublishAuthenticatedUdpFrame(udp), Is.True);
                Assert.That(received.items, Has.Length.EqualTo(1));
                Assert.That(received.items[0].avatarEntityId,
                    Is.EqualTo(known.ToString("D")));

                var reliable = Frame(known, knownSession, 2, occurrence);
                reliable.items = new[]
                {
                    reliable.items[0],
                    State(unknown, unknownSession, 2)
                };
                PublishReliable(feed, reliable);
                Assert.That(received.items, Has.Length.EqualTo(1));
                Assert.That(received.items[0].avatarEntityId,
                    Is.EqualTo(unknown.ToString("D")));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void DisableClearsReliableScopeBarrier()
        {
            var host = new GameObject("MotionFeed");
            try
            {
                var feed = host.AddComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>();
                var actor = Guid.NewGuid();
                var session = Guid.NewGuid();
                PublishReliable(feed, Frame(actor, session, 1,
                    Guid.NewGuid().ToString("D")));
                feed.enabled = false;
                feed.enabled = true;

                Assert.That(feed.PublishAuthenticatedUdpFrame(
                    Frame(actor, session, 2,
                        Guid.NewGuid().ToString("D"))), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void HttpRecoveryEstablishesActorSessionAndTickBarrier()
        {
            var host = new GameObject("MotionFeed");
            try
            {
                var authority = host.AddComponent<
                    OntologyWorldAuthorityClient>();
                SetField(authority, "currentWorldId",
                    TestWorldId.ToString("D"));
                SetField(authority, "hasRuntimeProjectionZoneScope", true);
                SetField(authority, "runtimeProjectionZoneKey", "default");
                var feed = host.AddComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>();
                feed.Configure(authority, null);
                var actor = Guid.NewGuid();
                var session = Guid.NewGuid();
                var recovery = new[] { State(actor, session, 5) };
                InvokeRecovery(feed, recovery, false);

                Assert.That(feed.PublishAuthenticatedUdpFrame(
                    Frame(actor, session, 6,
                        Guid.NewGuid().ToString("D"))), Is.True);
                Assert.That(feed.PublishAuthenticatedUdpFrame(
                    Frame(actor, Guid.NewGuid(), 7,
                        Guid.NewGuid().ToString("D"))), Is.False);

                var staleActor = Guid.NewGuid();
                var staleSession = Guid.NewGuid();
                var stale = State(staleActor, staleSession, 1);
                stale.worldId = Guid.NewGuid().ToString("D");
                InvokeRecovery(feed, new[] { stale }, false);
                Assert.That(feed.PublishAuthenticatedUdpFrame(
                    Frame(staleActor, staleSession, 2,
                        Guid.NewGuid().ToString("D"))), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void AcceptedRealtimeFrameIsImmediatelyAvailableForUdpAck()
        {
            var host = CreateRecoveryFeed(out _, out var feed);
            try
            {
                var actor = Guid.NewGuid();
                var session = Guid.NewGuid();
                var reliable = Frame(actor, session, 1,
                    Guid.NewGuid().ToString("D"));
                reliable.items[0].updatedAtUnixMilliseconds =
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                PublishReliable(feed, reliable);

                Assert.That(feed.TryGetLatestPlayerMotion(
                    actor, 1f, out var state), Is.True);
                Assert.That(state.runtimeSessionId,
                    Is.EqualTo(session.ToString("D")));
                Assert.That(state.serverTick, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void CompleteZoneRecoveryRemovesMissingActorBarrier()
        {
            var host = CreateRecoveryFeed(out var authority, out var feed);
            try
            {
                var actor = Guid.NewGuid();
                var session = Guid.NewGuid();
                InvokeRecovery(feed, new[] { State(actor, session, 1) }, true);
                InvokeRecovery(feed,
                    Array.Empty<OntologyAuthorityPlayerMotionState>(), true);
                Assert.That(feed.PublishAuthenticatedUdpFrame(
                    Frame(actor, session, 2,
                        Guid.NewGuid().ToString("D"))), Is.False);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        [Test]
        public void LateHttpRecoveryCannotRewindNewerSignalRSession()
        {
            var host = CreateRecoveryFeed(out var authority, out var feed);
            try
            {
                var actor = Guid.NewGuid();
                var oldSession = Guid.NewGuid();
                var newSession = Guid.NewGuid();
                PublishReliable(feed, Frame(actor, oldSession, 1,
                    Guid.NewGuid().ToString("D")));
                var capture = CaptureRecovery(feed);
                var newer = Frame(actor, newSession, 1,
                    Guid.NewGuid().ToString("D"));
                newer.items[0].updatedAtUnixMilliseconds = 5_000;
                PublishReliable(feed, newer);
                var late = State(actor, oldSession, 2);
                late.updatedAtUnixMilliseconds = 2_000;
                InvokeRecovery(feed, new[] { late }, false, capture);

                Assert.That(feed.PublishAuthenticatedUdpFrame(
                    Frame(actor, oldSession, 3,
                        Guid.NewGuid().ToString("D"))), Is.False);
                var udpNew = Frame(actor, newSession, 2,
                    Guid.NewGuid().ToString("D"));
                udpNew.items[0].updatedAtUnixMilliseconds = 5_100;
                Assert.That(feed.PublishAuthenticatedUdpFrame(udpNew), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
        }

        private static OntologyAuthorityZoneMotionFrame Frame(Guid actor,
            Guid session, long tick, string occurrence) => new()
        {
            worldId = TestWorldId.ToString("D"), zoneKey = "default",
            frameOccurrenceId = occurrence, serverTick = tick,
            observedAtUnixMilliseconds = 1000 + tick,
            items = new[] { State(actor, session, tick) }
        };

        private static OntologyAuthorityPlayerMotionState State(Guid actor,
            Guid session, long tick) => new()
        {
            worldId = TestWorldId.ToString("D"),
            zoneKey = "default",
            avatarEntityId = actor.ToString("D"),
            runtimeSessionId = session.ToString("D"),
            serverTick = tick,
            updatedAtUnixMilliseconds = 1000 + tick
        };

        private static void PublishReliable(
            OntologyWorldAuthorityMotionSnapshotFeed feed,
            OntologyAuthorityZoneMotionFrame frame) =>
            typeof(OntologyWorldAuthorityMotionSnapshotFeed).GetMethod(
                "HandleZoneMotionFrameReceived",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(feed, new object[] { frame });

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name,
                BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private static GameObject CreateRecoveryFeed(
            out OntologyWorldAuthorityClient authority,
            out OntologyWorldAuthorityMotionSnapshotFeed feed)
        {
            var host = new GameObject("MotionFeed");
            authority = host.AddComponent<OntologyWorldAuthorityClient>();
            SetField(authority, "currentWorldId", TestWorldId.ToString("D"));
            SetField(authority, "hasRuntimeProjectionZoneScope", true);
            SetField(authority, "runtimeProjectionZoneKey", "default");
            feed = host.AddComponent<OntologyWorldAuthorityMotionSnapshotFeed>();
            feed.Configure(authority, null);
            return host;
        }

        private static object CaptureRecovery(
            OntologyWorldAuthorityMotionSnapshotFeed feed) =>
            typeof(OntologyWorldAuthorityMotionSnapshotFeed).GetMethod(
                "CaptureReliableRecoveryScope",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(feed, new object[] { null });

        private static void InvokeRecovery(
            OntologyWorldAuthorityMotionSnapshotFeed feed,
            OntologyAuthorityPlayerMotionState[] states,
            bool complete,
            object capture = null)
        {
            capture ??= CaptureRecovery(feed);
            typeof(OntologyWorldAuthorityMotionSnapshotFeed).GetMethod(
                "RegisterReliableRecovery",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(feed, new[] { (object)states, capture, complete });
        }

        private sealed class Fixture : IDisposable
        {
            private readonly GameObject host = new("UdpSnapshotReceiver");
            private readonly byte[] key = new byte[32];
            private readonly Guid worldId = Guid.NewGuid();
            private readonly Guid avatarId = Guid.NewGuid();
            private readonly Guid runtimeSessionId = Guid.NewGuid();
            private readonly Guid transportSessionId = Guid.NewGuid();
            private readonly OntologyWorldAuthorityUdpMotionTransport transport;
            private readonly OntologyWorldAuthorityMotionSnapshotFeed feed;
            private readonly FakeClock clock = new();

            internal Fixture()
            {
                RandomNumberGenerator.Fill(key);
                var authority = host.AddComponent<OntologyWorldAuthorityClient>();
                feed = host.AddComponent<OntologyWorldAuthorityMotionSnapshotFeed>();
                transport = host.AddComponent<
                    OntologyWorldAuthorityUdpMotionTransport>();
                Set(authority, "currentUserId", Guid.NewGuid().ToString("D"));
                Set(authority, "currentWorldId", worldId.ToString("D"));
                Set(authority, "hasRuntimeProjectionZoneScope", true);
                Set(authority, "runtimeProjectionZoneKey", "default");
                Set(authority, "activeRuntimeAvatarId", avatarId);
                Set(authority, "activeRuntimeAvatarZoneKey", "default");
                Set(authority, "activeRuntimeSessionId",
                    runtimeSessionId.ToString("D"));
                Set(transport, "enableUdpSnapshotReceiver", true);
                Set(transport, "connected", true);
                Set(transport, "datagramAuthenticationKey", key);
                Set(transport, "transportSessionId", transportSessionId);
                Set(transport, "transportGeneration", 5UL);
                Set(transport, "runtimeSessionId", runtimeSessionId);
                Set(transport, "admittedWorldId", worldId);
                Set(transport, "admittedAvatarEntityId", avatarId);
                Set(transport, "admittedZoneKey", "default");
                transport.ConfigureRuntimeSeams(clock, null, null);
                feed.ZoneMotionFrameReceived += value => Received = value;
            }

            internal Guid ActorId { get; } = Guid.NewGuid();
            internal Guid SecondActorId { get; } = Guid.NewGuid();
            internal Guid ActorSessionId { get; } = Guid.NewGuid();
            internal Guid SecondSessionId { get; } = Guid.NewGuid();
            internal OntologyAuthorityZoneMotionFrame Received { get; private set; }
            internal OntologyRealtimeTransportCounters Counters =>
                transport.Counters;
            internal int PendingAssemblyCount =>
                transport.PendingSnapshotAssemblyCount;
            internal int PendingAssemblyBytes =>
                transport.PendingSnapshotAssemblyBytes;

            internal void AdvanceClock(float seconds) =>
                clock.MonotonicSeconds += seconds;

            internal AuthorityMotionSnapshotItem Actor(Guid id, long tick) => new()
            {
                ActorEntityId = id,
                ActorRuntimeSessionId = id == SecondActorId
                    ? SecondSessionId : ActorSessionId,
                ActorServerTick = tick, RotationW = 1f,
                MotionStatus = "Idle"
            };

            internal void EstablishReliableActor(long tick,
                Guid? actor = null, Guid? session = null)
            {
                var frame = Frame(actor ?? ActorId,
                    session ?? ActorSessionId, tick,
                    Guid.NewGuid().ToString("D"));
                frame.worldId = worldId.ToString("D");
                foreach (var state in frame.items)
                    state.worldId = frame.worldId;
                PublishReliable(feed, frame);
                Received = null;
            }

            internal bool Process(byte[] datagram) => (bool)typeof(
                OntologyWorldAuthorityUdpMotionTransport).GetMethod(
                    "TryProcessAuthoritySnapshotDatagram",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(transport, new object[] { datagram });

            internal byte[] Datagram(ulong sequence, long tick,
                Guid occurrence, ushort page, ushort pages, ushort total,
                AuthorityMotionSnapshotItem item, Guid? world = null)
            {
                Assert.That(RealtimeWireCodec
                    .TryCreateAuthorityMotionSnapshotForAuthentication(
                        new RealtimePacketMetadata(transportSessionId, 5,
                            sequence, tick),
                        new AuthorityMotionSnapshotPayload
                        {
                            WorldId = world ?? worldId, ZoneKey = "default",
                            FrameOccurrenceId = occurrence, PageIndex = page,
                            PageCount = pages, TotalItemCount = total,
                            ObservedAtUnixMilliseconds = 10_000 + tick,
                            Items = new[] { item }
                        }, out var datagram, out var error), Is.True,
                    error.ToString());
                Assert.That((bool)typeof(
                    OntologyWorldAuthorityUdpMotionTransport).GetMethod(
                        "TryAuthenticateDatagram",
                        BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { datagram, key, null }), Is.True);
                return datagram;
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(host);
                CryptographicOperations.ZeroMemory(key);
            }

            private static void Set(object target, string name, object value) =>
                target.GetType().GetField(name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(target, value);
        }

        private sealed class FakeClock : IAuthorityRealtimeClock
        {
            public float MonotonicSeconds { get; set; }
            public long UtcMilliseconds =>
                (long)(MonotonicSeconds * 1000f);
        }
    }
}
