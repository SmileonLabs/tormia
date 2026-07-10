using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Tormia.Ontology.Core;

namespace Tormia.Ontology.Editor
{
    public sealed class OntologyActorAnimationSetupWizard : EditorWindow
    {
        private static readonly string[] ActorTypes = { "Player", "NPC", "Monster" };
        private static readonly string[] RigTypes = { "Humanoid", "Quadruped", "Flying" };
        private GameObject actorObject;
        private OntologyWorldBootstrap bootstrap;
        private OntologyAnimationDatabase animationDatabase;
        private OntologyActorProfile profile;
        private int actorTypeIndex;
        private int rigTypeIndex;
        private string actorId = "Player";
        private string intent = "Idle";
        private Vector2 scroll;

        [MenuItem("Tools/Ontology/Actor Animation Setup Wizard")]
        public static void Open() => GetWindow<OntologyActorAnimationSetupWizard>("Actor Animation Setup");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Actor 유형, 씬 오브젝트, 애니메이션 데이터를 한 번에 연결합니다.", MessageType.Info);
            actorObject = (GameObject)EditorGUILayout.ObjectField("Actor Object", actorObject, typeof(GameObject), true);
            if (GUILayout.Button("Use Selected Object")) actorObject = Selection.activeGameObject;
            actorTypeIndex = EditorGUILayout.Popup("Actor Type", actorTypeIndex, ActorTypes);
            rigTypeIndex = EditorGUILayout.Popup("Rig Type", rigTypeIndex, RigTypes);
            actorId = EditorGUILayout.TextField("Actor Id", actorId);
            bootstrap = (OntologyWorldBootstrap)EditorGUILayout.ObjectField("World Bootstrap (optional)", bootstrap, typeof(OntologyWorldBootstrap), true);
            animationDatabase = (OntologyAnimationDatabase)EditorGUILayout.ObjectField("Animation Database", animationDatabase, typeof(OntologyAnimationDatabase), false);
            profile = (OntologyActorProfile)EditorGUILayout.ObjectField("Profile (optional)", profile, typeof(OntologyActorProfile), false);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("Apply Setup", GUILayout.Height(30))) ApplySetup();
            if (GUILayout.Button("Inject Ontology Data")) InjectFacts();

            EditorGUILayout.Space(6);
            intent = EditorGUILayout.TextField("Preview Intent", intent);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var definition in MatchingDefinitions())
                EditorGUILayout.LabelField("[OK] " + definition.animationId, definition.clip != null ? definition.clip.name : "Missing Clip");
            EditorGUILayout.EndScrollView();
        }

        private IEnumerable<OntologyAnimationDefinition> MatchingDefinitions()
        {
            if (animationDatabase == null || animationDatabase.Definitions == null) yield break;
            var actorType = ActorTypes[Mathf.Clamp(actorTypeIndex, 0, ActorTypes.Length - 1)];
            var rigType = RigTypes[Mathf.Clamp(rigTypeIndex, 0, RigTypes.Length - 1)];
            foreach (var definition in animationDatabase.Definitions)
            {
                if (definition == null || definition.clip == null || !HasIntent(definition, intent)) continue;
                if (!Matches(definition.actorTypes, actorType) || !Matches(definition.rigTypes, rigType)) continue;
                yield return definition;
            }
        }

        private void ApplySetup()
        {
            if (actorObject == null) { Debug.LogError("Actor Animation Setup: Actor Object is required."); return; }
            if (bootstrap == null) bootstrap = FindAnyObjectByType<OntologyWorldBootstrap>();
            if (animationDatabase == null) animationDatabase = AssetDatabase.LoadAssetAtPath<OntologyAnimationDatabase>("Assets/Data/Ontology/AnimationDatabase.asset");
            if (bootstrap == null) { Debug.LogError("Actor Animation Setup: World Bootstrap is missing."); return; }
            if (animationDatabase == null) { Debug.LogError("Actor Animation Setup: Animation Database is missing."); return; }
            if (string.IsNullOrWhiteSpace(actorId)) actorId = actorObject.name;

            profile = EnsureProfile();
            var animator = FindAnimator(actorObject);
            var adapter = GetOrAdd<OntologyAnimationAdapter>(actorObject);
            var adapterSerialized = new SerializedObject(adapter);
            adapterSerialized.FindProperty("bootstrap").objectReferenceValue = bootstrap;
            adapterSerialized.FindProperty("animationDatabase").objectReferenceValue = animationDatabase;
            adapterSerialized.FindProperty("actorProfile").objectReferenceValue = profile;
            adapterSerialized.FindProperty("targetAnimator").objectReferenceValue = animator;
            adapterSerialized.FindProperty("actorId").stringValue = actorId;
            adapterSerialized.ApplyModifiedPropertiesWithoutUndo();

            var synchronizer = GetOrAdd<OntologyActorProfileFactSynchronizer>(actorObject);
            var syncSerialized = new SerializedObject(synchronizer);
            syncSerialized.FindProperty("bootstrap").objectReferenceValue = bootstrap;
            syncSerialized.FindProperty("profile").objectReferenceValue = profile;
            syncSerialized.FindProperty("actorId").stringValue = actorId;
            syncSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(actorObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(actorObject.scene);
            Selection.activeGameObject = actorObject;
            Debug.Log("Actor Animation Setup complete: " + actorId + " (" + ActorTypes[actorTypeIndex] + ").");
        }

        private void InjectFacts()
        {
            if (actorObject == null) { Debug.LogError("Select an actor object first."); return; }
            var sync = actorObject.GetComponent<OntologyActorProfileFactSynchronizer>();
            if (sync == null) { ApplySetup(); sync = actorObject.GetComponent<OntologyActorProfileFactSynchronizer>(); }
            if (sync != null) sync.Sync();
        }

        private OntologyActorProfile EnsureProfile()
        {
            if (profile == null)
            {
                const string folder = "Assets/Data/Ontology/Actors";
                if (!AssetDatabase.IsValidFolder("Assets/Data/Ontology")) AssetDatabase.CreateFolder("Assets/Data", "Ontology");
                if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Data/Ontology", "Actors");
                var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + actorId + "Profile.asset");
                profile = CreateInstance<OntologyActorProfile>();
                profile.actorType = ActorTypes[actorTypeIndex];
                profile.rigType = RigTypes[rigTypeIndex];
                profile.capabilities = DefaultCapabilities(profile.actorType);
                AssetDatabase.CreateAsset(profile, path);
                AssetDatabase.SaveAssets();
            }
            else
            {
                profile.actorType = ActorTypes[actorTypeIndex];
                profile.rigType = RigTypes[rigTypeIndex];
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
            return profile;
        }

        private static string[] DefaultCapabilities(string type)
        {
            if (type == "Monster") return new[] { "Locomotion", "Combat", "Aggro" };
            if (type == "NPC") return new[] { "Locomotion", "Talk", "Work" };
            return new[] { "Locomotion", "Jump", "Combat", "Dodge" };
        }

        private static Animator FindAnimator(GameObject root)
        {
            foreach (var candidate in root.GetComponentsInChildren<Animator>(true))
                if (candidate != null && candidate.avatar != null && candidate.isHuman) return candidate;
            return root.GetComponentInChildren<Animator>(true);
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            var value = target.GetComponent<T>();
            return value != null ? value : Undo.AddComponent<T>(target);
        }

        private static bool HasIntent(OntologyAnimationDefinition definition, string value)
        {
            if (definition.intents == null) return false;
            foreach (var item in definition.intents) if (item == value) return true;
            return false;
        }

        private static bool Matches(string[] values, string expected)
        {
            if (values == null || values.Length == 0) return true;
            foreach (var item in values) if (item == expected) return true;
            return false;
        }
    }
}
