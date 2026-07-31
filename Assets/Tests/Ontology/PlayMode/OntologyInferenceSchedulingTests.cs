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

        [UnityTest]
        public IEnumerator DrowningRecoveryUsesConfirmedCheckpointAnchor()
        {
            var testScene = SceneManager.CreateScene(
                "OntologyDrowningCheckpointRecoveryTest");
            SceneManager.SetActiveScene(testScene);

            var bootstrapObject = new GameObject("TestBootstrap");
            var bootstrap = bootstrapObject.AddComponent<OntologyWorldBootstrap>();

            var player = new GameObject("TestPlayer");
            player.transform.position = new Vector3(12f, 4f, 18f);
            var ontologyObject = player.AddComponent<OntologyObject>();
            ontologyObject.ConfigureOntologyData(
                "Player",
                new[] { OntologyConcepts.Actor },
                new OntologyFactEntry[0]);
            player.AddComponent<CharacterController>();
            player.AddComponent<OntologyWaterPresenceSensor>();
            var recovery = player.AddComponent<OntologyDrowningRecoveryAdapter>();
            var respawnPosition = new Vector3(2f, 1f, 3f);
            recovery.SetConfirmedRespawnAnchor(
                respawnPosition,
                Quaternion.Euler(0f, 45f, 0f));

            yield return null;
            bootstrap.ResetWorld(logReport: false);
            bootstrap.World.AddFact("Player", "movement_mode", "Drowning");

            var recoveryDeadline = Time.time + 1.2f;
            while (Time.time < recoveryDeadline)
            {
                recovery.TryRecover(Time.deltaTime);
                yield return null;
            }

            Assert.That(
                Vector3.Distance(player.transform.position, respawnPosition),
                Is.LessThan(0.01f),
                "Drowning recovery must return to the confirmed checkpoint instead of a transient water position.");

            bootstrap.World.RemoveFact("Player", "movement_mode", "Drowning");
            Assert.That(
                recovery.TryRecover(Time.deltaTime),
                Is.False,
                "Removing Drowning must release recovery movement ownership.");
            Assert.That(
                player.GetComponent<CharacterController>().enabled,
                Is.True);

            Object.Destroy(player);
            Object.Destroy(bootstrapObject);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }

        [UnityTest]
        public IEnumerator PlayerInputAdvancesRecoveryAfterControllerIsDisabled()
        {
            var testScene = SceneManager.CreateScene(
                "OntologyDrowningInputOwnershipTest");
            SceneManager.SetActiveScene(testScene);

            var bootstrapObject = new GameObject("TestBootstrap");
            var bootstrap =
                bootstrapObject.AddComponent<OntologyWorldBootstrap>();

            var player = new GameObject("TestPlayer");
            player.transform.position = new Vector3(8f, 4f, 9f);
            var ontologyObject = player.AddComponent<OntologyObject>();
            ontologyObject.ConfigureOntologyData(
                "Player",
                new[] { OntologyConcepts.Actor },
                new OntologyFactEntry[0]);
            var controller = player.AddComponent<CharacterController>();
            player.AddComponent<OntologyWaterPresenceSensor>();
            var recovery =
                player.AddComponent<OntologyDrowningRecoveryAdapter>();
            var respawnPosition = new Vector3(1f, 2f, 3f);
            recovery.SetConfirmedRespawnAnchor(
                respawnPosition,
                Quaternion.identity);
            player.AddComponent<OntologyInputSystemPlayerInput>();

            yield return null;
            bootstrap.ResetWorld(logReport: false);
            bootstrap.World.AddFact(
                "Player",
                "movement_mode",
                "Drowning");

            yield return null;
            Assert.That(
                controller.enabled,
                Is.False,
                "Drowning presentation should temporarily own the Transform.");

            var recoveryDeadline = Time.time + 1.2f;
            while (Time.time < recoveryDeadline)
            {
                yield return null;
            }

            Assert.That(
                controller.enabled,
                Is.True,
                "Recovery must keep advancing while its own presentation has disabled the CharacterController.");
            Assert.That(
                Vector3.Distance(
                    player.transform.position,
                    respawnPosition),
                Is.LessThan(0.01f),
                "Input dispatch must not strand the player at the water entry position.");

            bootstrap.World.RemoveFact(
                "Player",
                "movement_mode",
                "Drowning");
            yield return null;
            Assert.That(recovery.RecoveryActive, Is.False);

            Object.Destroy(player);
            Object.Destroy(bootstrapObject);
            yield return null;
            yield return SceneManager.UnloadSceneAsync(testScene);
        }
    }
}
