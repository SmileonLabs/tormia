using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Core.Tests
{
    public sealed class OntologyAccountProfileRelationProjectorTests
    {
        [UnityTest]
        public IEnumerator AccountRelationsProjectToTheLocalActorAndAreRemovedAgain()
        {
            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            bootstrap.ResetWorld(logReport: false);

            var actor = new GameObject("Actor");
            var ontology = actor.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData("Avatar_01", new[] { OntologyConcepts.Actor }, new OntologyFactEntry[0]);
            var projector = actor.AddComponent<OntologyAccountProfileRelationProjector>();
            yield return null;

            projector.ApplyProfileRelations(new[]
            {
                new OntologyAuthorityProfileRelation
                {
                    subjectId = "Self", predicateId = "has_skill", objectId = "Talk"
                }
            });

            Assert.That(bootstrap.World.HasFact("Avatar_01", "has_skill", "Talk"), Is.True);
            projector.ClearProjection();
            Assert.That(bootstrap.World.HasFact("Avatar_01", "has_skill", "Talk"), Is.False);

            Object.Destroy(actor);
            Object.Destroy(bootstrapObject);
        }

        [UnityTest]
        public IEnumerator ClearingProfileProjectionPreservesAnExistingWorldFact()
        {
            var bootstrapObject = new GameObject("Bootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            bootstrap.ResetWorld(logReport: false);

            var actor = new GameObject("Actor");
            var ontology = actor.AddComponent<OntologyObject>();
            ontology.ConfigureOntologyData(
                "Avatar_01",
                new[] { OntologyConcepts.Actor },
                new OntologyFactEntry[0]);
            var projector =
                actor.AddComponent<OntologyAccountProfileRelationProjector>();
            yield return null;
            bootstrap.World.AddFact("Avatar_01", "has_skill", "Talk");

            projector.ApplyProfileRelations(new[]
            {
                new OntologyAuthorityProfileRelation
                {
                    subjectId = "Self",
                    predicateId = "has_skill",
                    objectId = "Talk"
                }
            });
            projector.ClearProjection();

            Assert.That(
                bootstrap.World.HasFact("Avatar_01", "has_skill", "Talk"),
                Is.True);

            Object.Destroy(actor);
            Object.Destroy(bootstrapObject);
        }
    }
}
