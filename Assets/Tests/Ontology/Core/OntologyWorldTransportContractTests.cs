using System;
using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyWorldTransportContractTests
    {
        [Test]
        public void ValidCommandEnvelopeIsAcceptedBeforeSemanticValidation()
        {
            var command = new OntologyWorldCommand
            {
                commandId = Guid.NewGuid().ToString("D"),
                worldId = Guid.NewGuid().ToString("D"),
                actorUserId = Guid.NewGuid().ToString("D"),
                expectedRevision = 3,
                commandType = OntologyWorldCommandKinds.AddRuleBlock,
                payloadJson = "{\"ruleId\":\"BuoyantWhenInWater\"}"
            };

            Assert.That(command.TryValidateEnvelope(out var rejectionCode), Is.True);
            Assert.That(rejectionCode, Is.Empty);
        }

        [Test]
        public void CommandEnvelopeRejectsInvalidIdentityAndRevision()
        {
            var command = new OntologyWorldCommand
            {
                commandId = "not-a-guid",
                worldId = Guid.NewGuid().ToString("D"),
                actorUserId = Guid.NewGuid().ToString("D"),
                expectedRevision = -1,
                commandType = OntologyWorldCommandKinds.PlaceEntity,
                payloadJson = "{}"
            };

            Assert.That(command.TryValidateEnvelope(out var rejectionCode), Is.False);
            Assert.That(rejectionCode, Is.EqualTo("invalid_command_id"));
        }
    }
}
