using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tormia.Ontology.Core
{
    /// <summary>
    /// Loads the authored world and editable UI scenes around the persistent
    /// Bootstrap scene. World rules remain in Authority/bootstrap services;
    /// the UI scene owns presentation only.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class OntologySceneCompositionLoader : MonoBehaviour
    {
        public const string DefaultBootstrapSceneName = "TormiaBootstrap";
        public const string DefaultWorldSceneName = "TormiaWorld";
        public const string DefaultUiSceneName = "TormiaUI";

        [SerializeField] private string worldSceneName = DefaultWorldSceneName;
        [SerializeField] private string uiSceneName = DefaultUiSceneName;
        [SerializeField] private bool loadSynchronouslyOnBoot = true;
        [SerializeField] private bool setWorldSceneActive = true;

        public string WorldSceneName => worldSceneName;
        public string UiSceneName => uiSceneName;
        public bool IsCompositionReady { get; private set; }
        public event Action CompositionReady;

        private void Awake()
        {
            StartCoroutine(LoadCompositionRoutine());
        }

        private IEnumerator Start()
        {
            if (!IsCompositionReady)
            {
                yield return new WaitUntil(() => IsCompositionReady);
            }

            // Bootstrap.Start can run after the additive objects are available,
            // but a final reset guarantees scene-authored ontology objects from
            // the World scene are the source of the local runtime projection.
            yield return null;
            RefreshCompositionBindings();
        }

        public static bool IsSceneLoaded(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName)) return false;
            var scene = SceneManager.GetSceneByName(sceneName);
            return scene.IsValid() && scene.isLoaded;
        }

        private IEnumerator LoadCompositionRoutine()
        {
            // Unity restores the remaining scenes of an Editor multi-scene
            // setup after the Bootstrap Awake calls. One frame lets those
            // scenes register before deciding whether an additive load is
            // required, preventing duplicate World/UI scenes in Play Mode.
            yield return null;

            if (loadSynchronouslyOnBoot)
            {
                LoadRequiredScene(worldSceneName);
                LoadRequiredScene(uiSceneName);
                yield return new WaitUntil(() =>
                    IsSceneLoaded(worldSceneName) &&
                    IsSceneLoaded(uiSceneName));
                CompleteComposition();
                yield break;
            }

            yield return LoadRequiredSceneRoutine(worldSceneName);
            yield return LoadRequiredSceneRoutine(uiSceneName);
            CompleteComposition();
        }

        private static void LoadRequiredScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || IsSceneLoaded(sceneName)) return;
            SceneManager.LoadScene(sceneName, LoadSceneMode.Additive);
        }

        private static IEnumerator LoadRequiredSceneRoutine(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || IsSceneLoaded(sceneName)) yield break;
            var operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
            if (operation != null) yield return operation;
        }

        private void CompleteComposition()
        {
            if (setWorldSceneActive)
            {
                var worldScene = SceneManager.GetSceneByName(worldSceneName);
                if (worldScene.IsValid() && worldScene.isLoaded)
                {
                    SceneManager.SetActiveScene(worldScene);
                }
            }

            IsCompositionReady =
                IsSceneLoaded(worldSceneName) &&
                IsSceneLoaded(uiSceneName);

            if (!IsCompositionReady)
            {
                Debug.LogError(
                    $"Scene composition is incomplete. World='{worldSceneName}', UI='{uiSceneName}'.",
                    this);
                return;
            }

            CompositionReady?.Invoke();
        }

        private static void RefreshCompositionBindings()
        {
            var authorityBridge = FindAnyObjectByType<OntologyWorldAuthorityBridge>();
            authorityBridge?.RefreshSceneBindings();

            var bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            bootstrap?.ResetWorld(logReport: false);

            foreach (var gate in FindObjectsByType<OntologyWorldRuntimeActivationGate>(
                         FindObjectsInactive.Include))
            {
                gate?.RefreshTargets();
            }

            var editorController = FindAnyObjectByType<OntologyRuntimeWorldEditorController>(
                FindObjectsInactive.Include);
            foreach (var panel in FindObjectsByType<OntologyRuntimeWorldEditorPanel>(
                         FindObjectsInactive.Include))
            {
                panel?.Configure(editorController);
            }
            foreach (var panel in FindObjectsByType<OntologyRuntimeWorldFactEditorPanel>(
                         FindObjectsInactive.Include))
            {
                panel?.Configure(editorController);
            }

            var placementController = FindAnyObjectByType<OntologyRuntimeObjectPlacementController>(
                FindObjectsInactive.Include);
            foreach (var panel in FindObjectsByType<OntologyObjectPlacementPanel>(
                         FindObjectsInactive.Include))
            {
                panel?.Configure(
                    placementController,
                    placementController == null ? null : placementController.Catalog);
            }
        }
    }
}
