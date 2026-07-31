using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Tormia.Ontology.Core
{
    public static class OntologyAttachmentProfilePreviewAuthoring
    {
        public const string PreviewScenePath =
            OntologyAttachmentProfilePreviewRig.PreviewScenePath;
        private const string WorldScenePath = "Assets/Scenes/TormiaWorld.unity";
        private const string ProfilePath =
            "Assets/Data/Ontology/Profiles/RightHandCarryAttachmentProfile.asset";
        private const string DefaultWeaponPrefabPath =
            "Assets/Prefabs/Ontology/Combat/OntologySword08Corrupted.prefab";
        private const string WeaponPrefabFolder =
            "Assets/Prefabs/Ontology/Combat/";

        [MenuItem("Tools/Ontology/Attachment Preview/Open Right-Hand Weapon Preview")]
        public static void OpenOrCreate()
        {
            var existing = SceneManager.GetSceneByPath(PreviewScenePath);
            if (existing.IsValid() && existing.isLoaded)
            {
                FocusPreview(existing);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PreviewScenePath) != null)
            {
                FocusPreview(EditorSceneManager.OpenScene(
                    PreviewScenePath,
                    OpenSceneMode.Additive));
                return;
            }

            Rebuild();
        }

        [MenuItem("Tools/Ontology/Attachment Preview/Rebuild Right-Hand Weapon Preview")]
        public static void Rebuild()
        {
            EnsureFolder("Assets/Scenes/Editor");

            var worldScene = SceneManager.GetSceneByPath(WorldScenePath);
            var openedWorldForPreview = !worldScene.IsValid() || !worldScene.isLoaded;
            if (openedWorldForPreview)
            {
                worldScene = EditorSceneManager.OpenScene(
                    WorldScenePath,
                    OpenSceneMode.Additive);
            }

            var sourceVisual = FindByPath(
                worldScene,
                "TormiaWorldRoot/OntologyPlayer/Visual_Base_Mesh");
            if (sourceVisual == null)
            {
                if (openedWorldForPreview)
                    EditorSceneManager.CloseScene(worldScene, true);
                throw new System.InvalidOperationException(
                    "TormiaWorld player visual was not found.");
            }

            var loadedPreview = SceneManager.GetSceneByPath(PreviewScenePath);
            if (loadedPreview.IsValid())
            {
                if (loadedPreview.isDirty)
                {
                    if (openedWorldForPreview)
                        EditorSceneManager.CloseScene(worldScene, true);
                    throw new System.InvalidOperationException(
                        "Save or close the modified attachment preview before rebuilding it.");
                }

                // Unity can retain a clean, unloaded scene handle after an
                // additive preview was closed. Removing that stale handle is
                // required before SaveScene can reuse the same asset path.
                EditorSceneManager.CloseScene(loadedPreview, true);
            }

            var previewScene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            var root = new GameObject("RightHandAttachmentPreview");
            SceneManager.MoveGameObjectToScene(root, previewScene);

            var character = Object.Instantiate(sourceVisual, root.transform);
            character.name = "CharacterPreview";
            character.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            character.transform.localScale = Vector3.one;

            var animator = character.GetComponent<Animator>() ??
                           character.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman)
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.CloseScene(previewScene, true);
                if (openedWorldForPreview)
                    EditorSceneManager.CloseScene(worldScene, true);
                throw new System.InvalidOperationException(
                    "Preview character needs a Humanoid Animator.");
            }

            var sourceSocket =
                sourceVisual.transform.parent.GetComponentsInChildren<
                        OntologyAttachmentSocket>(true)
                    .FirstOrDefault(value =>
                        value != null &&
                        value.SocketId == "RightHand");
            if (sourceSocket == null)
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.CloseScene(previewScene, true);
                if (openedWorldForPreview)
                    EditorSceneManager.CloseScene(worldScene, true);
                throw new System.InvalidOperationException(
                    "The player needs an authored RightHand attachment socket.");
            }

            var socketContainer =
                new GameObject("AttachmentSockets").transform;
            socketContainer.SetParent(root.transform, false);
            var socketObject =
                new GameObject("RightHandWeaponSocket");
            socketObject.transform.SetParent(socketContainer, false);
            var previewSocket =
                socketObject.AddComponent<OntologyAttachmentSocket>();
            previewSocket.Configure(
                sourceSocket.SocketId,
                root.transform,
                sourceSocket.SourceBone,
                sourceSocket.SourceChildPath,
                sourceSocket.LocalPosition,
                sourceSocket.LocalEulerAngles);
            if (!previewSocket.ApplyAuthoredPose())
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.CloseScene(previewScene, true);
                if (openedWorldForPreview)
                    EditorSceneManager.CloseScene(worldScene, true);
                throw new System.InvalidOperationException(
                    "The preview attachment socket source could not be resolved.");
            }

            var weaponPrefab = ResolveWeaponPrefab();
            var profile =
                AssetDatabase.LoadAssetAtPath<OntologyAttachmentProfile>(ProfilePath);
            if (weaponPrefab == null || profile == null)
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.CloseScene(previewScene, true);
                if (openedWorldForPreview)
                    EditorSceneManager.CloseScene(worldScene, true);
                throw new System.InvalidOperationException(
                    "Preview weapon or attachment profile is missing.");
            }

            var weapon = (GameObject)PrefabUtility.InstantiatePrefab(
                weaponPrefab,
                previewScene);
            weapon.name = "EquippedWeaponPreview";
            PrefabUtility.UnpackPrefabInstance(
                weapon,
                PrefabUnpackMode.Completely,
                InteractionMode.AutomatedAction);
            StripRuntimeComponents(weapon);
            var rig = root.AddComponent<OntologyAttachmentProfilePreviewRig>();
            rig.Configure(
                profile,
                animator,
                weapon.transform,
                weaponPrefab);
            if (!rig.ApplyProfileToPreview())
            {
                Object.DestroyImmediate(root);
                EditorSceneManager.CloseScene(previewScene, true);
                if (openedWorldForPreview)
                    EditorSceneManager.CloseScene(worldScene, true);
                throw new System.InvalidOperationException(
                    "The preview actor's right-hand anchor was not found.");
            }
            EditorUtility.SetDirty(weapon.transform);
            EditorSceneManager.MarkSceneDirty(previewScene);

            CreateCamera(root.transform);
            CreateLight(root.transform);
            if (!EditorSceneManager.SaveScene(previewScene, PreviewScenePath))
            {
                throw new System.InvalidOperationException(
                    "The attachment preview scene could not be saved.");
            }

            if (openedWorldForPreview)
                EditorSceneManager.CloseScene(worldScene, true);

            FocusPreview(previewScene);
        }

        private static void FocusPreview(Scene scene)
        {
            SceneManager.SetActiveScene(scene);
            var rig = scene.GetRootGameObjects()
                .Select(value =>
                    value.GetComponentInChildren<OntologyAttachmentProfilePreviewRig>(
                        true))
                .FirstOrDefault(value => value != null);
            if (rig == null) return;

            Selection.activeTransform = rig.AttachmentPreview;
            SceneView.lastActiveSceneView?.FrameSelected();
        }

        private static GameObject FindByPath(Scene scene, string path)
        {
            var parts = path.Split('/');
            var current = scene.GetRootGameObjects()
                .FirstOrDefault(value => value.name == parts[0]);
            if (current == null) return null;

            for (var index = 1; index < parts.Length; index++)
            {
                var child = current.transform.Find(parts[index]);
                if (child == null) return null;
                current = child.gameObject;
            }

            return current;
        }

        private static GameObject ResolveWeaponPrefab()
        {
            if (Selection.activeObject is GameObject selected)
            {
                var selectedPath = AssetDatabase.GetAssetPath(selected);
                if (selectedPath.StartsWith(
                        WeaponPrefabFolder,
                        System.StringComparison.Ordinal) &&
                    selected.GetComponentInChildren<
                        OntologyAttachmentGripPoint>(true) != null)
                {
                    return selected;
                }
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(
                DefaultWeaponPrefabPath);
        }

        private static void CreateCamera(Transform parent)
        {
            var cameraObject = new GameObject("PreviewCamera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.position = new Vector3(0f, 1.25f, 6f);
            cameraObject.transform.rotation = Quaternion.LookRotation(
                new Vector3(0f, 1.05f, 0f) -
                cameraObject.transform.position);
            var camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 40f;
        }

        private static void CreateLight(Transform parent)
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
        }

        private static void StripRuntimeComponents(GameObject previewObject)
        {
            // The preview owns visuals only. Leaving an OntologyObject,
            // attachment adapter, collider, or rigidbody here lets an
            // accidentally loaded preview participate in Bootstrap runtime.
            var behaviours =
                previewObject.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (var behaviour in behaviours)
            {
                // Remove adapters first. OntologyAttachmentAdapter requires
                // OntologyObject and the physics coordinator, so deleting its
                // dependencies first makes Unity add them back automatically.
                if (behaviour is OntologyAttachmentGripPoint ||
                    behaviour is OntologyObject ||
                    behaviour is OntologyPhysicsPresentationCoordinator)
                {
                    continue;
                }
                Object.DestroyImmediate(behaviour);
            }
            foreach (var behaviour in
                     previewObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour is OntologyAttachmentGripPoint)
                    continue;
                Object.DestroyImmediate(behaviour);
            }
            foreach (var collider in
                     previewObject.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(collider);
            }
            foreach (var body in
                     previewObject.GetComponentsInChildren<Rigidbody>(true))
            {
                Object.DestroyImmediate(body);
            }
        }

        private static void EnsureFolder(string folder)
        {
            var parts = folder.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }
    }
}
