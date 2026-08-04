using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldAuthorityRemoteAvatarPresenterTests
    {
        [Test]
        public void NotificationBurstsCoalesceWithoutPostponingEarlierRefresh()
        {
            var first = OntologyWorldAuthorityRemoteAvatarPresenter
                .CoalesceRefreshDeadline(0f, 10f, 0.1f);
            var burst = OntologyWorldAuthorityRemoteAvatarPresenter
                .CoalesceRefreshDeadline(first, 10.02f, 0.1f);
            var alreadyScheduled = OntologyWorldAuthorityRemoteAvatarPresenter
                .CoalesceRefreshDeadline(10.05f, 10.02f, 0.1f);

            Assert.That(first, Is.EqualTo(10.1f).Within(0.0001f));
            Assert.That(burst, Is.EqualTo(first).Within(0.0001f));
            Assert.That(alreadyScheduled, Is.EqualTo(10.05f).Within(0.0001f));
        }

        [Test]
        public void OrderedSnapshotsRenderBehindLatestAuthorityTime()
        {
            var snapshots = new List<RemoteAvatarSnapshotSample>
            {
                Sample(100, 10000, new Vector3(0f, 0f, 0f), Vector3.right, "moving", 1f),
                Sample(102, 10100, new Vector3(1f, 0f, 0f), Vector3.right, "moving", 1.1f),
                Sample(104, 10200, new Vector3(2f, 0f, 0f), Vector3.right, "moving", 1.2f)
            };

            var resolved = OntologyWorldAuthorityRemoteAvatarPresenter
                .TryResolveBufferedRenderSample(
                    snapshots,
                    localNow: 1.25f,
                    interpolationDelay: 0.125f,
                    maximumExtrapolation: 0.15f,
                    out var rendered);

            Assert.That(resolved, Is.True);
            Assert.That(rendered.IsExtrapolated, Is.False);
            Assert.That(rendered.Position.x, Is.EqualTo(1.25f).Within(0.02f));
            Assert.That(rendered.MotionStatus, Is.EqualTo("moving"));
        }

        [Test]
        public void BufferedSnapshotsUseOnlyBoundedAuthorityVelocityExtrapolation()
        {
            var snapshots = new List<RemoteAvatarSnapshotSample>
            {
                Sample(100, 10000, Vector3.zero, new Vector3(4f, 0f, 0f), "moving", 1f),
                Sample(102, 10100, new Vector3(0.4f, 0f, 0f), new Vector3(4f, 0f, 0f), "moving", 1.1f)
            };

            Assert.That(
                OntologyWorldAuthorityRemoteAvatarPresenter
                    .TryResolveBufferedRenderSample(
                        snapshots, 1.35f, 0.125f, 0.15f, out var bounded),
                Is.True);
            Assert.That(bounded.IsExtrapolated, Is.True);
            Assert.That(bounded.Position.x, Is.EqualTo(0.9f).Within(0.02f));

            Assert.That(
                OntologyWorldAuthorityRemoteAvatarPresenter
                    .TryResolveBufferedRenderSample(
                        snapshots, 1.7f, 0.125f, 0.15f, out var stale),
                Is.True);
            Assert.That(stale.IsExtrapolated, Is.True);
            Assert.That(stale.Position.x, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(stale.MotionStatus, Is.EqualTo("idle"));
        }

        [Test]
        public void SnapshotOrderingRejectsOlderTicksButAcceptsARealTimelineRestart()
        {
            var previous = Sample(100, 10000, Vector3.zero, Vector3.zero, "idle", 1f);
            var outOfOrder = Sample(99, 10050, Vector3.zero, Vector3.zero, "idle", 1.1f);
            var restart = Sample(1, 12001, Vector3.zero, Vector3.zero, "idle", 1.2f);

            Assert.That(
                OntologyWorldAuthorityRemoteAvatarPresenter.IsSnapshotNewer(
                    previous, outOfOrder),
                Is.False);
            Assert.That(
                OntologyWorldAuthorityRemoteAvatarPresenter.ShouldResetSnapshotTimeline(
                    previous, outOfOrder),
                Is.False);
            Assert.That(
                OntologyWorldAuthorityRemoteAvatarPresenter.ShouldResetSnapshotTimeline(
                    previous, restart),
                Is.True);
        }

        [Test]
        public void SessionChangeAndLongGapRequireAVisualBaseline()
        {
            var previous = Sample(100, 10000, Vector3.zero, Vector3.zero, "idle", 1f, "session-a");
            var changedSession = Sample(1, 10050, Vector3.zero, Vector3.zero, "idle", 1.1f, "session-b");
            var longGap = Sample(102, 11000, Vector3.zero, Vector3.zero, "idle", 2f, "session-a");

            Assert.That(
                OntologyWorldAuthorityRemoteAvatarPresenter.HasSessionChanged(
                    previous.RuntimeSessionId, changedSession.RuntimeSessionId),
                Is.True);
            Assert.That(
                OntologyWorldAuthorityRemoteAvatarPresenter.HasTimelineGap(
                    previous, longGap, 0.75f),
                Is.True);
        }

        [UnityTest]
        public IEnumerator LocalAvatarResolutionIgnoresArbitraryWorldIdentity()
        {
            var decoy = new GameObject("ArbitraryCarryableIdentity");
            var decoyIdentity =
                decoy.AddComponent<OntologyAuthorityEntityIdentity>();
            decoyIdentity.SetGuid(System.Guid.NewGuid());

            var player = new GameObject("LocalInputActor");
            player.SetActive(false);
            var playerIdentity =
                player.AddComponent<OntologyAuthorityEntityIdentity>();
            playerIdentity.SetGuid(System.Guid.NewGuid());
            player.AddComponent<OntologyInputSystemPlayerInput>();

            var entryHost = new GameObject("AccountEntryHost");
            entryHost.SetActive(false);
            var entryFlow =
                entryHost.AddComponent<OntologyWorldAuthorityAccountEntryFlow>();
            entryHost.SetActive(true);
            yield return null;

            Assert.That(
                entryFlow.AvatarIdentity,
                Is.SameAs(playerIdentity),
                "Account entry must resolve the actor that owns local input, not " +
                "the first arbitrary Authority identity in scene order.");

            var checkpoint = entryHost.GetComponent<OntologyAvatarCheckpointController>();
            Assert.That(checkpoint, Is.Not.Null);
            Assert.That(
                GetPrivateField<OntologyAuthorityEntityIdentity>(
                    checkpoint,
                    "avatarIdentity"),
                Is.SameAs(playerIdentity),
                "Durable checkpoints must belong to the local-input avatar and " +
                "must never capture a monster or carryable identity.");

            var host = new GameObject("RemoteAvatarPresenterHost");
            host.SetActive(false);
            var presenter =
                host.AddComponent<OntologyWorldAuthorityRemoteAvatarPresenter>();
            SetPrivateField(presenter, "localAvatarIdentity", decoyIdentity);

            host.SetActive(true);
            yield return null;

            Assert.That(
                GetPrivateField<OntologyAuthorityEntityIdentity>(
                    presenter,
                    "localAvatarIdentity"),
                Is.SameAs(playerIdentity),
                "A carryable or other arbitrary Authority entity must never be " +
                "used as the local avatar exclusion identity.");

            Object.Destroy(host);
            Object.Destroy(entryHost);
            Object.Destroy(player);
            Object.Destroy(decoy);
            yield return null;
        }

        private static T GetPrivateField<T>(object target, string fieldName)
            where T : class
        {
            return target.GetType()
                .GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(target) as T;
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            target.GetType()
                .GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static RemoteAvatarSnapshotSample Sample(
            long tick,
            long time,
            Vector3 position,
            Vector3 velocity,
            string status,
            float receivedAt,
            string session = "session-a") =>
            new(
                tick,
                time,
                session,
                position,
                velocity,
                status,
                receivedAt);
    }
}
