using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyActorProfileFactSynchronizerTests
    {
        [UnityTest]
        public IEnumerator ProfileRepertoireRepublishesAfterWorldRebuildAndRetractsOnRemoval()
        {
            var bootstrapObject = new GameObject("ProfileSyncBootstrap");
            var actor = new GameObject("ProfileSyncActor");
            var profile =
                ScriptableObject.CreateInstance<
                    Tormia.Ontology.Core.OntologyActorProfile>();
            try
            {
                var bootstrap =
                    bootstrapObject.AddComponent<
                        Tormia.Ontology.Core.OntologyWorldBootstrap>();
                var ontology =
                    actor.AddComponent<
                        Tormia.Ontology.Core.OntologyObject>();
                ontology.ConfigureOntologyData(
                    "ProfileSyncActor",
                    new[] { Tormia.Ontology.Core.OntologyConcepts.Actor },
                    new Tormia.Ontology.Core.OntologyFactEntry[0]);
                profile.actorType = "Player";
                profile.rigType = "Humanoid";
                profile.animationIds = new[] { "Anim_Profile_First" };

                var synchronizer =
                    actor.AddComponent<
                        Tormia.Ontology.Core
                            .OntologyActorProfileFactSynchronizer>();
                synchronizer.Configure(
                    bootstrap,
                    profile,
                    ontology,
                    simulateAfterSync: false);

                Assert.That(
                    bootstrap.World.HasFact(
                        ontology.EntityId,
                        Tormia.Ontology.Core.OntologyPredicates.HasAnimation,
                        "Anim_Profile_First"),
                    Is.True);

                profile.animationIds = new[] { "Anim_Profile_Replacement" };
                bootstrap.ResetWorld(logReport: false);

                Assert.That(
                    bootstrap.World.HasFact(
                        ontology.EntityId,
                        Tormia.Ontology.Core.OntologyPredicates.HasAnimation,
                        "Anim_Profile_First"),
                    Is.False,
                    "A rebuilt runtime world may not retain an obsolete " +
                    "profile repertoire contribution.");
                Assert.That(
                    bootstrap.World.HasFact(
                        ontology.EntityId,
                        Tormia.Ontology.Core.OntologyPredicates.HasAnimation,
                        "Anim_Profile_Replacement"),
                    Is.True,
                    "The current profile repertoire must be republished after " +
                    "the runtime world instance is replaced.");

                profile.animationIds = new string[0];
                synchronizer.Sync();
                Assert.That(
                    bootstrap.World.HasFact(
                        ontology.EntityId,
                        Tormia.Ontology.Core.OntologyPredicates.HasAnimation,
                        "Anim_Profile_Replacement"),
                    Is.False,
                    "Removing a profile animation must remove its runtime " +
                    "repertoire fact without a presentation fallback.");
            }
            finally
            {
                Object.Destroy(actor);
                Object.Destroy(bootstrapObject);
                Object.Destroy(profile);
            }

            yield return null;
        }
    }
}
