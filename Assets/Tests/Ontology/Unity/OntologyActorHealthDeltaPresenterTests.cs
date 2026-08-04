using System;
using System.Collections.Generic;
using NUnit.Framework;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyActorHealthDeltaPresenterTests
    {
        [Test]
        public void BaselineIsSilentThenReportsDamageAndHealing()
        {
            var entityId = Guid.NewGuid();
            var tracker = new OntologyActorHealthDeltaPresenter.HealthBaselineTracker();

            Assert.That(tracker.Apply(Health(entityId, 100d)), Is.Empty);
            var damage = tracker.Apply(Health(entityId, 85d));
            Assert.That(damage, Has.Count.EqualTo(1));
            Assert.That(damage[0].EntityId, Is.EqualTo(entityId));
            Assert.That(damage[0].Delta, Is.EqualTo(-15d));

            var healing = tracker.Apply(Health(entityId, 91d));
            Assert.That(healing, Has.Count.EqualTo(1));
            Assert.That(healing[0].Delta, Is.EqualTo(6d));
        }

        [Test]
        public void MissingEntityEvictsBaselineAndDoesNotCreateStaleDelta()
        {
            var entityId = Guid.NewGuid();
            var tracker = new OntologyActorHealthDeltaPresenter.HealthBaselineTracker();

            tracker.Apply(Health(entityId, 100d));
            Assert.That(tracker.Apply(new Dictionary<Guid, double>()), Is.Empty);
            Assert.That(tracker.Apply(Health(entityId, 25d)), Is.Empty);
        }

        [Test]
        public void DuplicateProjectedHealthFailsClosed()
        {
            var entityId = Guid.NewGuid();
            var facts = new[]
            {
                Fact(entityId, "75"),
                Fact(entityId, "50")
            };

            Assert.That(
                OntologyActorHealthDeltaPresenter.ProjectCurrentHealth(facts),
                Does.Not.ContainKey(entityId));
        }

        private static Dictionary<Guid, double> Health(Guid entityId, double value) =>
            new() { [entityId] = value };

        private static OntologyAuthorityFactProjection Fact(Guid entityId, string value) =>
            new()
            {
                subjectEntityId = entityId.ToString("D"),
                predicateId = OntologyPredicates.CurrentHealth,
                objectValueJson = value
            };
    }
}
