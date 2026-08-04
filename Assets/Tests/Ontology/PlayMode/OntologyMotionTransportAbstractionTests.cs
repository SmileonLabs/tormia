using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyMotionTransportAbstractionTests
    {
        [Test]
        public void HttpAdapterImplementsBothCurrentReliableMotionContracts()
        {
            var host = new GameObject("MotionTransport");
            try
            {
                var adapter = host.AddComponent<
                    OntologyWorldAuthorityHttpMotionTransport>();

                Assert.That(adapter, Is.InstanceOf<IPlayerMotionIntentTransport>());
                Assert.That(adapter,
                    Is.InstanceOf<IResolvedPoseObservationTransport>());
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void UdpAdapterIsTransportOnlyAndDefaultsToReliableWriterFallback()
        {
            var host = new GameObject("UdpMotionTransport");
            try
            {
                var adapter = host.AddComponent<
                    OntologyWorldAuthorityUdpMotionTransport>();
                Assert.That(adapter, Is.InstanceOf<IPlayerMotionIntentTransport>());
                Assert.That(adapter.IsUdpFeatureEnabled, Is.False);
                Assert.That(adapter.IsUdpReady, Is.False);
                Assert.That(host.GetComponent<
                    OntologyWorldAuthorityHttpMotionTransport>(), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void UdpAdapterZeroesAdmissionSecretWhenStopped()
        {
            var host = new GameObject("UdpMotionTransport");
            try
            {
                var adapter = host.AddComponent<
                    OntologyWorldAuthorityUdpMotionTransport>();
                var secret = new byte[32];
                for (var index = 0; index < secret.Length; index++)
                    secret[index] = (byte)(index + 1);
                SetPrivateField(adapter, "datagramAuthenticationKey", secret);

                InvokePrivate(adapter, "StopUdpTransport", "test cleanup", true);

                CollectionAssert.AreEqual(new byte[32], secret);
                Assert.That(GetPrivateField<byte[]>(adapter,
                    "datagramAuthenticationKey"), Is.Empty);
                Assert.That(GetPrivateField<ulong>(adapter,
                    "admissionGeneration"), Is.GreaterThan(0));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void UdpAdapterCannotSendWhenFeatureFlagIsDisabled()
        {
            var host = new GameObject("UdpMotionTransport");
            try
            {
                var adapter = host.AddComponent<
                    OntologyWorldAuthorityUdpMotionTransport>();
                SetPrivateField(adapter, "connected", true);
                SetPrivateField(adapter, "datagramAuthenticationKey",
                    new byte[32]);

                var sent = adapter.TrySendAuthenticatedMotionIntent(
                    Guid.NewGuid(), "default", 1L, Vector2.up, 1f,
                    false, Vector2.zero, 0f);

                Assert.That(sent, Is.False);
                Assert.That(adapter.IsUdpFeatureEnabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void PlayerIntentSenderResolvesTransportWithoutOwningHttpCall()
        {
            var host = new GameObject("PlayerIntentSender");
            try
            {
                var sender = host.AddComponent<
                    OntologyWorldAuthorityPlayerIntentSender>();
                var resolver = typeof(OntologyWorldAuthorityPlayerIntentSender)
                    .GetMethod(
                        "ResolveIntentTransport",
                        BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(resolver, Is.Not.Null);
                var transport = resolver.Invoke(sender, null);
                Assert.That(transport,
                    Is.InstanceOf<IPlayerMotionIntentTransport>());
                Assert.That(host.GetComponent<
                        OntologyWorldAuthorityHttpMotionTransport>(),
                    Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SnapshotFeedImplementsAuthoritySnapshotContract()
        {
            var host = new GameObject("MotionSnapshotFeed");
            try
            {
                var feed = host.AddComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>();
                Assert.That(feed, Is.InstanceOf<IAuthorityMotionSnapshotFeed>());
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SnapshotFeedRelaysAuthorityFrameWithoutRewritingIt()
        {
            var host = new GameObject("MotionSnapshotFeed");
            try
            {
                var feed = host.AddComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>();
                var method = typeof(OntologyWorldAuthorityMotionSnapshotFeed)
                    .GetMethod(
                        "HandleZoneMotionFrameReceived",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                var worldId = Guid.NewGuid().ToString("D");
                var frame = new OntologyAuthorityZoneMotionFrame
                {
                    worldId = worldId,
                    zoneKey = "zone-a",
                    frameOccurrenceId = Guid.NewGuid().ToString("D"),
                    serverTick = 42,
                    observedAtUnixMilliseconds = 1_000L,
                    items = new[]
                    {
                        new OntologyAuthorityPlayerMotionState
                        {
                            worldId = worldId,
                            zoneKey = "zone-a",
                            avatarEntityId = Guid.NewGuid().ToString("D"),
                            runtimeSessionId = Guid.NewGuid().ToString("D"),
                            serverTick = 42,
                            updatedAtUnixMilliseconds = 1_000L
                        }
                    }
                };
                OntologyAuthorityZoneMotionFrame received = null;
                feed.ZoneMotionFrameReceived += value => received = value;

                Assert.That(method, Is.Not.Null);
                method.Invoke(feed, new object[] { frame });

                Assert.That(received, Is.SameAs(frame));
                Assert.That(received.serverTick, Is.EqualTo(42));
                Assert.That(received.observedAtUnixMilliseconds,
                    Is.EqualTo(1_000L));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void DisabledFeedConfiguresAndSubscribesExactlyOnceWhenEnabled()
        {
            var host = new GameObject("MotionFeedHost");
            host.SetActive(false);
            try
            {
                var realtime = host.AddComponent<
                    OntologyWorldAuthorityRealtimeClient>();
                var feed = host.AddComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>();
                feed.Configure(null, realtime);

                host.SetActive(true);
                Assert.That(GetPrivateField<bool>(
                    feed,
                    "realtimeSubscriptionActive"), Is.True);
                Assert.That(GetEventSubscriberCount(
                    realtime,
                    "ZoneMotionFrameReceived"), Is.EqualTo(1));

                feed.Configure(null, realtime);
                Assert.That(GetEventSubscriberCount(
                    realtime,
                    "ZoneMotionFrameReceived"), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void InvalidExplicitIntentTransportFailsClosedWithoutFallback()
        {
            var host = new GameObject("PlayerIntentSender");
            host.SetActive(false);
            try
            {
                var sender = host.AddComponent<
                    OntologyWorldAuthorityPlayerIntentSender>();
                var invalidSource = host.AddComponent<
                    OntologyRemoteMotionRuntimeMetrics>();
                SetPrivateField(sender, "intentTransportSource", invalidSource);
                host.SetActive(true);

                var transport = InvokePrivate(
                    sender,
                    "ResolveIntentTransport");
                Assert.That(transport, Is.Null);
                Assert.That(host.GetComponent<
                    OntologyWorldAuthorityHttpMotionTransport>(), Is.Null);
                Assert.That(GetPrivateField<MonoBehaviour>(
                    sender,
                    "intentTransportSource"), Is.SameAs(invalidSource));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SenderCreatesDefaultSnapshotFeedOnAuthorityHostOnly()
        {
            var authorityHost = new GameObject("AuthorityHost");
            var senderHost = new GameObject("SenderHost");
            senderHost.SetActive(false);
            try
            {
                var authority = authorityHost.AddComponent<
                    OntologyWorldAuthorityClient>();
                var sender = senderHost.AddComponent<
                    OntologyWorldAuthorityPlayerIntentSender>();
                SetPrivateField(sender, "authorityClient", authority);
                senderHost.SetActive(true);

                var feed = InvokePrivate(
                    sender,
                    "ResolveMotionSnapshotFeed") as
                    IAuthorityMotionSnapshotFeed;
                Assert.That(feed, Is.Not.Null);
                Assert.That(authorityHost.GetComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>(),
                    Is.Not.Null);
                Assert.That(senderHost.GetComponent<
                    OntologyWorldAuthorityMotionSnapshotFeed>(),
                    Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(senderHost);
                Object.DestroyImmediate(authorityHost);
            }
        }

        [Test]
        public void IntentCallbackFenceRejectsSupersededTransportGeneration()
        {
            var host = new GameObject("PlayerIntentSender");
            try
            {
                host.AddComponent<OntologyWorldAuthorityClient>();
                var sender = host.AddComponent<
                    OntologyWorldAuthorityPlayerIntentSender>();
                SetPrivateField(sender, "intentTransportGeneration", 4L);

                Assert.That((bool)InvokePrivate(
                    sender,
                    "IsCurrentIntentTransportCallback",
                    4L,
                    string.Empty), Is.True);
                Assert.That((bool)InvokePrivate(
                    sender,
                    "IsCurrentIntentTransportCallback",
                    3L,
                    string.Empty), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            return (T)target.GetType().GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(target);
        }

        private static void SetPrivateField(
            object target,
            string name,
            object value)
        {
            target.GetType().GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private static object InvokePrivate(
            object target,
            string name,
            params object[] arguments)
        {
            return target.GetType().GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, arguments);
        }

        private static int GetEventSubscriberCount(
            object target,
            string eventName)
        {
            var callback = target.GetType().GetField(
                    eventName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(target) as System.Delegate;
            return callback?.GetInvocationList().Length ?? 0;
        }
    }
}
