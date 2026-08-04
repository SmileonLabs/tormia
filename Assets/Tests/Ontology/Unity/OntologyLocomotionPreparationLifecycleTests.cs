using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Tormia.Ontology.Core.Tests
{
    public sealed class OntologyLocomotionPreparationLifecycleTests
    {
        [Test]
        public void EntryHandshakeAcceptsCurrentCallbackWhilePresenterIsGated()
        {
            var host = new GameObject("LocomotionPreparationLifecycle");
            try
            {
                var client = host.AddComponent<OntologyWorldAuthorityClient>();
                var sender = host.AddComponent<
                    OntologyWorldAuthorityPlayerIntentSender>();
                var clientField = typeof(
                        OntologyWorldAuthorityPlayerIntentSender)
                    .GetField(
                        "authorityClient",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(clientField, Is.Not.Null);
                clientField.SetValue(sender, client);
                sender.SetAvatarRegistered(true);
                sender.enabled = false;

                var method = typeof(OntologyWorldAuthorityPlayerIntentSender)
                    .GetMethod(
                        "IsCurrentIntentTransportCallback",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                var generationField = typeof(
                        OntologyWorldAuthorityPlayerIntentSender)
                    .GetField(
                        "intentTransportGeneration",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                Assert.That(generationField, Is.Not.Null);
                var generation = (long)generationField.GetValue(sender);

                var entryHandshakeAccepted = (bool)method.Invoke(
                    sender,
                    new object[] { generation, string.Empty, false });
                var liveInputAccepted = (bool)method.Invoke(
                    sender,
                    new object[] { generation, string.Empty, true });

                Assert.That(entryHandshakeAccepted, Is.True,
                    "The coordinator-owned entry handshake must survive a " +
                    "presentation gate when generation and session remain current.");
                Assert.That(liveInputAccepted, Is.False,
                    "Ordinary runtime input must remain disabled with the sender.");
                Assert.That(client, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
