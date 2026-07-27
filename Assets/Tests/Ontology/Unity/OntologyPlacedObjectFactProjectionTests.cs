using System.Collections.Generic;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyPlacedObjectFactProjectionTests
    {
        [Test]
        public void StableEntityIdOwnsFactsAndMigratesLegacyDisplayNameSubject()
        {
            var record = new OntologyPlacedObjectRecord
            {
                entityId = "d32a6386-5029-453f-a663-a8dc405e69b7",
                instanceName = "Editable Tree Name",
                concepts = new List<string> { "Plant" },
                facts = new List<OntologyFactRecord>
                {
                    new()
                    {
                        predicate = "growth_stage",
                        obj = "Young"
                    }
                }
            };
            var facts = new List<OntologyFactRecord>
            {
                new()
                {
                    subject = "Editable Tree Name",
                    predicate = "authored_note",
                    obj = "Keep"
                }
            };

            OntologyPlacedObjectFactProjection.Synchronize(facts, record);

            Assert.That(
                facts,
                Has.All.Matches<OntologyFactRecord>(value =>
                    value.subject == record.entityId));
            Assert.That(
                facts,
                Has.Some.Matches<OntologyFactRecord>(value =>
                    value.predicate == OntologyPredicates.HasConcept &&
                    value.obj == "Plant"));
            Assert.That(
                facts,
                Has.Some.Matches<OntologyFactRecord>(value =>
                    value.predicate == "authored_note" &&
                    value.obj == "Keep"));
        }

        [Test]
        public void ChangingDisplayNameDoesNotChangeStableFactSubject()
        {
            var record = new OntologyPlacedObjectRecord
            {
                entityId = "9cb189de-75ea-4e2d-a141-dde7ee93b359",
                instanceName = "First Name",
                concepts = new List<string> { "Rock" }
            };
            var facts = new List<OntologyFactRecord>();

            OntologyPlacedObjectFactProjection.Synchronize(facts, record);
            record.instanceName = "Renamed In Inspector";
            OntologyPlacedObjectFactProjection.Synchronize(facts, record);

            Assert.That(facts, Has.Count.EqualTo(1));
            Assert.That(facts[0].subject, Is.EqualTo(record.entityId));
        }

        [Test]
        public void RetiredTemplateMeaningIsRemovedFromStableAndLegacySubjects()
        {
            var template = ScriptableObject.CreateInstance<OntologyMapObjectTemplate>();
            try
            {
                template.retiredConcepts = new[] { "OldConcept" };
                var record = new OntologyPlacedObjectRecord
                {
                    entityId = "8e8030c0-30d9-4568-85ae-071d586695be",
                    instanceName = "Legacy Name",
                    concepts = new List<string> { "OldConcept", "CurrentConcept" }
                };
                var facts = new List<OntologyFactRecord>
                {
                    Fact(record.entityId, OntologyPredicates.HasConcept, "OldConcept"),
                    Fact(record.instanceName, OntologyPredicates.HasConcept, "OldConcept"),
                    Fact(record.entityId, OntologyPredicates.HasConcept, "CurrentConcept")
                };

                Assert.That(
                    OntologyPlacedObjectFactProjection.RemoveRetiredTemplateData(
                        facts,
                        record,
                        template),
                    Is.True);
                Assert.That(record.concepts, Is.EqualTo(new[] { "CurrentConcept" }));
                Assert.That(
                    facts,
                    Has.None.Matches<OntologyFactRecord>(value =>
                        value.obj == "OldConcept"));
                Assert.That(
                    facts,
                    Has.Some.Matches<OntologyFactRecord>(value =>
                        value.subject == record.entityId &&
                        value.obj == "CurrentConcept"));
            }
            finally
            {
                Object.DestroyImmediate(template);
            }
        }

        private static OntologyFactRecord Fact(
            string subject,
            string predicate,
            string obj) =>
            new()
            {
                subject = subject,
                predicate = predicate,
                obj = obj
            };
    }
}
