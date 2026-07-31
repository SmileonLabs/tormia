using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldAuthorityRemoteAvatarPresenterTests
    {
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
    }
}
