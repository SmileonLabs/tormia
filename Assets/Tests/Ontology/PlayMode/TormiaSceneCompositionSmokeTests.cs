using System.Collections;
using System.Linq;
using NUnit.Framework;
using Tormia.Ontology.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tormia.Ontology.Tests
{
    public sealed class TormiaSceneCompositionSmokeTests
    {
        [UnityTest]
        public IEnumerator BootstrapLoadsWorldAndUiAsSeparateScenes()
        {
            var operation = SceneManager.LoadSceneAsync(
                "Assets/Scenes/TormiaBootstrap.unity",
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            yield return operation;
            yield return null;
            yield return null;

            Assert.That(SceneManager.GetSceneByName("TormiaBootstrap").isLoaded, Is.True);
            Assert.That(SceneManager.GetSceneByName("TormiaWorld").isLoaded, Is.True);
            Assert.That(SceneManager.GetSceneByName("TormiaUI").isLoaded, Is.True);

            var loader = Object.FindAnyObjectByType<OntologySceneCompositionLoader>();
            var bootstrap = Object.FindAnyObjectByType<OntologyWorldBootstrap>();
            var player = Object.FindAnyObjectByType<OntologyPlayerPositionTracker>(
                FindObjectsInactive.Include);
            var canvas = Object.FindAnyObjectByType<OntologyAccountFlowNavigator>(
                FindObjectsInactive.Include);

            Assert.That(loader, Is.Not.Null);
            Assert.That(loader.IsCompositionReady, Is.True);
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(player, Is.Not.Null);
            Assert.That(canvas, Is.Not.Null);
            Assert.That(bootstrap.gameObject.scene.name, Is.EqualTo("TormiaBootstrap"));
            Assert.That(player.gameObject.scene.name, Is.EqualTo("TormiaWorld"));
            Assert.That(canvas.gameObject.scene.name, Is.EqualTo("TormiaUI"));
            Assert.That(bootstrap.World, Is.Not.Null);
            Assert.That(bootstrap.World.Facts.Count(), Is.GreaterThan(0));

            Assert.That(
                Object.FindObjectsByType<EventSystem>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length,
                Is.EqualTo(1));
            Assert.That(
                Object.FindObjectsByType<AudioListener>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None).Length,
                Is.EqualTo(1));

            var statusHud = Object.FindAnyObjectByType<OntologyRuntimeStatusHUD>(
                FindObjectsInactive.Include);
            Assert.That(statusHud, Is.Not.Null);
            Assert.That(statusHud.gameObject.activeInHierarchy, Is.False);

            Assert.That(
                Object.FindAnyObjectByType<OntologyCombatController>(
                    FindObjectsInactive.Include),
                Is.Not.Null,
                "The world player must expose the Authority combat input adapter.");
            Assert.That(
                Object.FindAnyObjectByType<OntologyCombatVfxAdapter>(
                    FindObjectsInactive.Include),
                Is.Not.Null,
                "The world scene must contain the pooled combat VFX presenter.");
            var entryPresentationCoordinator =
                Object.FindAnyObjectByType<
                    OntologyWorldEntryPresentationCoordinator>(
                    FindObjectsInactive.Include);
            Assert.That(
                entryPresentationCoordinator,
                Is.Not.Null,
                "The authored world player must own the transactional " +
                "entry-presentation coordinator.");
            Assert.That(
                entryPresentationCoordinator.gameObject,
                Is.EqualTo(player.gameObject),
                "Entry preparation must belong to the local player hierarchy, " +
                "not a runtime AddComponent fallback.");
        }

        [UnityTest]
        public IEnumerator UnloadingUiReleasesLanguagePackSubscribers()
        {
            var operation = SceneManager.LoadSceneAsync(
                "Assets/Scenes/TormiaBootstrap.unity",
                LoadSceneMode.Single);
            Assert.That(operation, Is.Not.Null);
            yield return operation;
            yield return null;
            yield return null;

            var uiScene = SceneManager.GetSceneByName("TormiaUI");
            Assert.That(uiScene.isLoaded, Is.True);
            yield return SceneManager.UnloadSceneAsync(uiScene);

            Assert.DoesNotThrow(OntologyLanguagePackService.Reload);
        }
    }
}
