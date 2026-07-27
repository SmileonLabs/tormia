using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyStableEntityIdentityTests
    {
        [Test]
        public void EntityIdNeverFallsBackToEditableGameObjectName()
        {
            var target = new GameObject("Editable Display Name");
            try
            {
                var ontology = target.AddComponent<OntologyObject>();

                Assert.That(ontology.EntityId, Is.Empty);

                ontology.ConfigureOntologyData(
                    "9ad88cb6-5385-45eb-ac61-ed04991624c4",
                    new[] { "Object" },
                    new OntologyFactEntry[0]);
                target.name = "Renamed By Owner";

                Assert.That(
                    ontology.EntityId,
                    Is.EqualTo("9ad88cb6-5385-45eb-ac61-ed04991624c4"));
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
