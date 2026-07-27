#if UNITY_EDITOR
using System;
using Tormia.Ontology.Core;
using Tormia.Ontology.Unity.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tormia.Ontology.Editor
{
    public static class OntologyCreatorWorkspaceAuthoring
    {
        private const string DatabasePath = "Assets/Data/Ontology/CharacterPartDatabase.asset";
        private const string ServiceCatalogPath =
            "Assets/Data/Ontology/Creator/CreatorServiceCatalog.asset";
        private const string CharacterVisualPrefabPath =
            "Assets/Prefabs/Ontology/Creator/CreatorNpcVisualTemplate.prefab";
        private const string SystemName = "CreatorWorkspaceSystem";
        private const string RootName = "CreatorWorkspaceRoot";

        [MenuItem("Tormia/Creator Workspace/Rebuild Temporary Specialist NPCs")]
        public static void RebuildTemporarySpecialistNpcs()
        {
            var player = GameObject.Find("OntologyPlayer");
            var sourceVisual = player != null
                ? player.transform.Find("Visual_Base_Mesh")
                : null;
            var database =
                AssetDatabase.LoadAssetAtPath<OntologyCharacterPartDatabase>(DatabasePath);
            var catalog =
                AssetDatabase.LoadAssetAtPath<OntologyCreatorServiceCatalog>(ServiceCatalogPath);
            if (player == null || sourceVisual == null || database == null ||
                catalog == null)
            {
                throw new InvalidOperationException(
                    "Creator workspace requires the player visual, part database, and service catalog.");
            }

            var characterVisualPrefab = RebuildCharacterVisualTemplate(sourceVisual);
            var existing = GameObject.Find(SystemName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing);
            }

            var system = new GameObject(SystemName);
            Undo.RegisterCreatedObjectUndo(system, "Create creator workspace");

            var root = new GameObject(RootName);
            root.transform.SetParent(system.transform, false);

            foreach (var service in catalog.Services)
            {
                if (service == null || !service.IsValid)
                {
                    continue;
                }

                CreateNpc(
                    root.transform,
                    player.transform,
                    sourceVisual,
                    characterVisualPrefab,
                    database,
                    service);
            }

            var controller = system.AddComponent<OntologyCreatorWorkspaceController>();
            controller.Configure(
                root,
                UnityEngine.Object.FindAnyObjectByType<OntologyGameSessionCoordinator>(),
                UnityEngine.Object.FindAnyObjectByType<OntologyWorldAuthorityClient>(),
                catalog);

            // Keep the generated hierarchy visible for layout editing. At runtime
            // the controller applies the owner/session/creator-mode visibility gate.
            root.SetActive(true);
            EditorUtility.SetDirty(system);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = root;
        }

        private static GameObject RebuildCharacterVisualTemplate(Transform sourceVisual)
        {
            EnsureAssetFolder("Assets/Prefabs");
            EnsureAssetFolder("Assets/Prefabs/Ontology");
            EnsureAssetFolder("Assets/Prefabs/Ontology/Creator");

            var templateObject = UnityEngine.Object.Instantiate(sourceVisual.gameObject);
            templateObject.name = "CreatorNpcVisualTemplate";
            templateObject.transform.SetParent(null, false);
            templateObject.transform.localPosition = Vector3.zero;
            templateObject.transform.localRotation = Quaternion.identity;
            templateObject.transform.localScale = sourceVisual.localScale;

            var prefab =
                PrefabUtility.SaveAsPrefabAsset(templateObject, CharacterVisualPrefabPath);
            UnityEngine.Object.DestroyImmediate(templateObject);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "Unable to create the creator NPC visual template.");
            }

            return prefab;
        }

        private static void EnsureAssetFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(
                path.Substring(0, slash),
                path.Substring(slash + 1));
        }

        private static void CreateNpc(
            Transform parent,
            Transform player,
            Transform sourceVisual,
            GameObject characterVisualPrefab,
            OntologyCharacterPartDatabase database,
            OntologyCreatorServiceDefinition service)
        {
            var npc = new GameObject(service.ObjectName);
            npc.transform.SetParent(parent, false);
            npc.transform.position = FindGroundPosition(
                player.position + service.workspaceOffset,
                player.position.y);
            npc.transform.rotation = Quaternion.LookRotation(
                Flatten(player.position - npc.transform.position),
                Vector3.up);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(
                characterVisualPrefab,
                npc.transform);
            visual.name = "CharacterVisual";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = sourceVisual.localScale;

            var appearance = npc.AddComponent<OntologyCreatorNpcAppearance>();
            appearance.Configure(
                service.serviceId,
                service.fallbackDisplayName,
                database,
                visual.transform,
                service.defaultPartIds);
        }

        private static Vector3 FindGroundPosition(Vector3 position, float fallbackY)
        {
            var rayOrigin = new Vector3(position.x, fallbackY + 100f, position.z);
            if (Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out var hit,
                    250f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point;
            }

            position.y = fallbackY;
            return position;
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f
                ? direction.normalized
                : Vector3.forward;
        }
    }
}
#endif
