using System.Collections;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class OntologyInferenceSchedulingTests
    {
        [UnityTest]
        public IEnumerator ObservationRequestsAreCoalescedIntoOneInferencePass()
        {
            var testScene = SceneManager.CreateScene(
                "OntologyInferenceSchedulingTest");
            SceneManager.SetActiveScene(testScene);

            var bootstrapObject = new GameObject("TestBootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();
            yield return null;

            var worldChangedCount = 0;
            bootstrap.WorldChanged += () => worldChangedCount++;

            bootstrap.RequestSimulation();
            bootstrap.RequestSimulation();

            Assert.That(
                worldChangedCount,
                Is.Zero,
                "Observation requests must not run inference before the frame's remaining sensors publish their facts.");

            yield return null;

            Assert.That(
                worldChangedCount,
                Is.EqualTo(1),
                "Multiple observation changes in one frame must produce one atomic inference pass.");

            Object.Destroy(bootstrapObject);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }

        [UnityTest]
        public IEnumerator RetractedDrowningImmediatelyReleasesMovementPresentation()
        {
            var testScene = SceneManager.CreateScene(
                "OntologyDrowningRetractionTest");
            SceneManager.SetActiveScene(testScene);

            var bootstrapObject = new GameObject("TestBootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();

            var player = new GameObject("TestPlayer");
            var ontologyObject = player.AddComponent<OntologyObject>();
            ontologyObject.ConfigureOntologyData(
                "Player",
                new[] { OntologyConcepts.Actor },
                new OntologyFactEntry[0]);
            var controller = player.AddComponent<CharacterController>();
            player.AddComponent<OntologyWaterPresenceSensor>();
            var recovery = player.AddComponent<OntologyDrowningRecoveryAdapter>();

            yield return null;
            bootstrap.ResetWorld(logReport: false);
            bootstrap.World.AddFact("Player", "movement_mode", "Drowning");

            Assert.That(recovery.TryRecover(Time.deltaTime), Is.True);
            Assert.That(controller.enabled, Is.False);

            bootstrap.World.RemoveFact("Player", "movement_mode", "Drowning");

            Assert.That(
                recovery.TryRecover(Time.deltaTime),
                Is.False,
                "A removed Drowning relation must immediately stop owning actor movement.");
            Assert.That(
                controller.enabled,
                Is.True,
                "The current ontology state must restore the CharacterController without completing stale recovery.");

            Object.Destroy(player);
            Object.Destroy(bootstrapObject);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }
    }
}
